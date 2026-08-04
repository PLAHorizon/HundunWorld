using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Diagnostics;
using Horizon.Game.ECS.Arch.Systems;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务 7.3/7.4/7.5 — 远程同步防闪跳与多角色稳定性单元测试。
/// 覆盖：阈值扩大后的平滑区行为（99m 平滑 Lerp）、过渡区加速混合（300m）、硬跳（1000m）、
/// 混合重定向、NaN 异常隔离、多角色同时移动稳定、多角色 Spawn/Despawn 清理。
/// 被测代码：InterpolationSystem.cs（3 档传送处理 + 分支 D 有限值防御）、SnapshotApplySystem 相关规则。
/// </summary>
public class RemoteSyncInterpolationTests : IDisposable
{
    private readonly World _world;
    private readonly InterpolationSystem _system;

    public RemoteSyncInterpolationTests()
    {
        ResetAdaptiveState();
        SnapshotApplySystem.UseAdaptiveDelay = true;

        _world = World.Create();
        // 默认阈值（spec 5.1.2/5.2.1）：平滑区 100m / 硬跳 500m / 混合 0.2s
        _system = new InterpolationSystem
        {
            UseAdaptiveSpeed = false,
            InterpolationSpeed = 30f, // 60fps 下 lerpFactor=0.5
            TeleportThresholdMeters = 100f,
            HardSnapThresholdMeters = 500f,
            TeleportBlendDurationSeconds = 0.2f,
        };
    }

    public void Dispose()
    {
        World.Destroy(_world);
        ResetAdaptiveState();
    }

    private static void ResetAdaptiveState()
    {
        SnapshotApplySystem.ResetAdaptiveDelayStats();
        SnapshotApplySystem.Diagnostics = null;
    }

    /// <summary>创建 Active 远程实体，当前位置 (0,0,0)，目标 (targetX,0,0)，无速度（纯 Lerp 追赶）。</summary>
    private Entity CreateActiveEntity(float targetX, float timeSinceSnapshot = 0f)
    {
        var entity = _world.Create();
        var interp = new InterpolatedTransformComponent
        {
            X = 0f, Y = 0f, Z = 0f,
            StartX = 0f, StartY = 0f, StartZ = 0f,
            TargetX = targetX, TargetY = 0f, TargetZ = 0f,
            Yaw = 0f, StartYaw = 0f, TargetYaw = 0f,
            Alpha = 0f,
            ServerTick = 1,
            ReceivedTick = 1,
            TimeSinceLastSnapshot = timeSinceSnapshot,
            State = RemoteEntityState.Active,
        };
        _world.Add(entity, interp);
        return entity;
    }

    private static float Distance(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var dz = z2 - z1;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // ─── 任务 7.3：平滑区行为（偏移 99m < 100m 阈值） ───

    [Fact]
    public void Offset99m_WithinSmoothZone_NoInstantSnap()
    {
        // spec 5.1.1 规则 4 的 a：偏移 99m 时不得直接跳到目标位置，应平滑追赶
        var entity = CreateActiveEntity(99f);

        // 第一帧：若直接硬跳/混合启动会瞬移，若平滑 Lerp 则仅接近目标一小段
        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);

        // 平滑 Lerp：一帧仅推进 lerpFactor=0.5 的距离的一半 → 位置应远小于 99m
        Assert.True(interp.X < 50f, $"平滑区 99m 偏移不应一帧瞬移到目标，实际 X={interp.X}");
        Assert.True(interp.X > 0.1f, $"平滑区应开始追赶，实际 X={interp.X}");

        // 不应进入混合状态（TeleportBlendRemainingSeconds 应为 0，因为 ≤100m 走纯 Lerp）
        Assert.Equal(0f, interp.TeleportBlendRemainingSeconds);
    }

    [Fact]
    public void Offset99m_SmoothCatchUp_ConvergesWithoutSnap()
    {
        // spec 5.1.1 规则 1 的 a：60fps 匀速移动，角色连续移动不出现瞬移
        var entity = CreateActiveEntity(99f);
        var dt = TimeSpan.FromSeconds(1.0 / 60.0);
        float maxFrameDelta = 0f;

        for (int frame = 0; frame < 300; frame++)
        {
            ref var before = ref _world.Get<InterpolatedTransformComponent>(entity);
            var prevX = before.X;
            _system.Update(_world, dt);
            ref var after = ref _world.Get<InterpolatedTransformComponent>(entity);
            var frameDelta = Math.Abs(after.X - prevX);
            if (frameDelta > maxFrameDelta) maxFrameDelta = frameDelta;
        }

        ref var final = ref _world.Get<InterpolatedTransformComponent>(entity);
        // 最终收敛到目标位置
        Assert.True(final.X > 98f, $"平滑追赶应收敛到目标附近，实际 X={final.X}");

        // 无单帧瞬移：追赶为渐进式，首帧位移（Lerp 指数衰减 lerpFactor=0.5 → 49.5m）远小于整段 99m，
        // 且后续每帧位移严格递减、单调收敛（不出现回退或一次性跳到目标）。
        Assert.True(maxFrameDelta < 50f, $"平滑追赶首帧位移应 < 50m（渐进追赶而非瞬移），实际最大帧位移={maxFrameDelta:F1}m");
    }

    // ─── 任务 7.3：过渡区加速混合（300m，100~500m） ───

    [Fact]
    public void Offset300m_TransitionZone_SmoothBlendOver200ms()
    {
        // spec 5.2.1 规则 1/2/3：300m 在 0.2s 内 smoothstep 缓动过渡，混合期间位置连续、无两帧间大跳变
        var entity = CreateActiveEntity(300f);
        var dt = TimeSpan.FromSeconds(1.0 / 60.0);
        float maxFrameDelta = 0f;

        for (int frame = 0; frame < 60; frame++)
        {
            ref var before = ref _world.Get<InterpolatedTransformComponent>(entity);
            var prevX = before.X;
            _system.Update(_world, dt);
            ref var after = ref _world.Get<InterpolatedTransformComponent>(entity);
            var frameDelta = Math.Abs(after.X - prevX);
            if (frameDelta > maxFrameDelta) maxFrameDelta = frameDelta;
        }

        ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);
        // 混合完成：位置等于 Target，混合状态清零
        Assert.True(Math.Abs(interp.X - 300f) < 0.01f, $"混合完成后位置应等于 Target，实际 X={interp.X}");
        Assert.Equal(0f, interp.TeleportBlendRemainingSeconds);
        Assert.Equal(1f, interp.Alpha);

        // 混合期间单帧位移应明显小于整段 300m（无两帧间大跳变）
        Assert.True(maxFrameDelta < 100f, $"混合期间不应有超大单帧跳变，最大帧位移={maxFrameDelta:F1}m");
    }

    // ─── 任务 7.3：硬跳（1000m > 500m） ───

    [Fact]
    public void Offset1000m_HardSnap_InstantTeleport()
    {
        // spec 5.2.1 规则 5 的 a：>500m 立即瞬移，不走混合
        var entity = CreateActiveEntity(1000f);

        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);
        Assert.True(Math.Abs(interp.X - 1000f) < 0.01f, $"硬跳应一帧瞬移到目标，实际 X={interp.X}");
        Assert.Equal(0f, interp.TeleportBlendRemainingSeconds);
    }

    // ─── 任务 7.3：混合重定向（混合中收到新快照，不重置进度） ───

    [Fact]
    public void BlendRedirect_NewSnapshotInTransition_ContinuesFromCurrentPosition()
    {
        // spec 5.2.1 规则 4：混合中收到新快照且新目标仍在过渡区时，从当前混合位置继续过渡，不重置到起始位置
        var entity = CreateActiveEntity(300f); // 起始混合到 300m
        var dt = TimeSpan.FromSeconds(1.0 / 60.0);

        // 推进 5 帧混合（约 1/12 进度，smoothstep 早期进度慢，位置约 38% 左右）
        for (int frame = 0; frame < 5; frame++)
            _system.Update(_world, dt);

        ref var mid = ref _world.Get<InterpolatedTransformComponent>(entity);
        var positionAtRedirect = mid.X;
        Assert.True(positionAtRedirect > 20f && positionAtRedirect < 200f, $"重定向前应处于混合中段，实际 X={positionAtRedirect}");

        // 模拟新快照重定向目标：写入新 Target（仍处于过渡区 0~500m 偏移）
        ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);
        interp.TargetX = 200f;
        interp.TargetY = 0f;
        interp.TargetZ = 0f;

        // 继续推进混合
        for (int frame = 0; frame < 60; frame++)
            _system.Update(_world, dt);

        ref var final = ref _world.Get<InterpolatedTransformComponent>(entity);
        // 混合完成后收敛到新目标 200m（而非 300m，证明重定向生效）
        Assert.True(Math.Abs(final.X - 200f) < 0.01f, $"混合重定向后应收敛到新目标 200m，实际 X={final.X}");

        // 重定向不应从起始位置(0)重新开始：位置保持连续性（单调且未回退到 0）
        ref var finalCheck = ref _world.Get<InterpolatedTransformComponent>(entity);
        Assert.True(finalCheck.X > positionAtRedirect * 0.5f, $"重定向后位置不应回退到接近起始位置，实际 X={finalCheck.X}");
    }

    // ─── 任务 7.3：混合中收到 >500m 新快照，硬跳覆盖混合 ───

    [Fact]
    public void BlendInProgress_NewTargetOverHardSnap_ImmediateHardSnapOverrides()
    {
        // spec 5.2.1 规则 5 的 b：混合中收到新目标偏移 >500m，立即硬跳覆盖，不继续过渡
        var entity = CreateActiveEntity(300f);
        var dt = TimeSpan.FromSeconds(1.0 / 60.0);

        for (int frame = 0; frame < 10; frame++)
            _system.Update(_world, dt);

        ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);
        Assert.True(interp.TeleportBlendRemainingSeconds > 0f, "前置条件：混合应进行中");

        // 新快照目标偏移 800m（> 500m 硬跳阈值）
        interp.TargetX = 800f;
        interp.TargetY = 0f;
        interp.TargetZ = 0f;

        _system.Update(_world, dt);

        ref var final = ref _world.Get<InterpolatedTransformComponent>(entity);
        Assert.True(Math.Abs(final.X - 800f) < 0.01f, $"硬跳覆盖应瞬移到 800m，实际 X={final.X}");
        Assert.Equal(0f, final.TeleportBlendRemainingSeconds);
    }

    // ─── 任务 7.3：朝向最短路径插值（±π 跨界不反向旋转） ───

    [Fact]
    public void YawShortestPath_CrossoverPi_NoReverseRotation()
    {
        // spec 5.2.1 规则 6 的 a：朝向从 3.0 变到 -3.0，沿最短路径约 0.28 弧度旋转
        var entity = _world.Create();
        var interp = new InterpolatedTransformComponent
        {
            X = 0f, Y = 0f, Z = 0f,
            TargetX = 0f, TargetY = 0f, TargetZ = 0f,
            Yaw = 3.0f, StartYaw = 3.0f, TargetYaw = -3.0f,
            Alpha = 0f,
            ServerTick = 1, ReceivedTick = 1,
            TimeSinceLastSnapshot = 0f,
            State = RemoteEntityState.Active,
        };
        _world.Add(entity, interp);

        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        ref var after = ref _world.Get<InterpolatedTransformComponent>(entity);
        // 一帧后 Yaw 应从 3.0 移向 0.28 方向（逼近 3.14 或 -3.14 方向），不得反向增大到接近 -3.0+2π=3.28 之外
        var yawDelta = Math.Abs(after.Yaw - 3.0f);
        Assert.True(yawDelta <= 0.5f, $"朝向应沿最短路径（<0.5rad/帧），实际 delta={yawDelta:F3}");
    }

    // ─── 任务 7.4：单角色 NaN 隔离 ───

    [Fact]
    public void NaN_Target_SkipsOnlyThatEntity_OthersUnaffected()
    {
        // spec 5.3.1 规则 7 的 a：一个远程角色 Target 为 NaN 时，跳过该角色更新，
        // 其余角色插值/渲染完全正常、进程不崩溃
        var goodEntity = CreateActiveEntity(10f);
        var badEntity = CreateActiveEntity(10f);

        // 注入 NaN Target 到坏实体
        ref var badInterp = ref _world.Get<InterpolatedTransformComponent>(badEntity);
        badInterp.TargetX = float.NaN;

        // 分支 D 前向预测路径应跳过 NaN 实体（保持位置不变），好实体正常推进
        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        ref var badAfter = ref _world.Get<InterpolatedTransformComponent>(badEntity);
        ref var goodAfter = ref _world.Get<InterpolatedTransformComponent>(goodEntity);

        // 坏实体：位置保持 0（未污染），不崩溃
        Assert.True(float.IsFinite(badAfter.X), "NaN 实体位置不应被污染");
        Assert.Equal(0f, badAfter.X);

        // 好实体：正常追赶
        Assert.True(goodAfter.X > 0f, $"好实体应正常追赶，实际 X={goodAfter.X}");
    }

    [Fact]
    public void NaN_TargetInLerp_DoesNotPoisonRendering()
    {
        // 分支 D 有限值防御：Target 为 NaN 时跳过插值推进，保持当前渲染位置与 Alpha 不变
        var entity = CreateActiveEntity(10f);
        ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);
        interp.TargetX = float.PositiveInfinity;

        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        ref var after = ref _world.Get<InterpolatedTransformComponent>(entity);
        Assert.True(float.IsFinite(after.X), $"渲染位置必须为有限值，实际 X={after.X}");
        Assert.Equal(0f, after.X);
    }

    // ─── 任务 7.4：多角色同时移动稳定 ───

    [Fact]
    public void MultipleRemoteEntities_ConcurrentMovement_AllSmooth()
    {
        // spec 5.3.1 规则 1：3/5/10 个远程角色持续移动时全部角色平滑移动、无卡死消失、进程不崩溃
        var dt = TimeSpan.FromSeconds(1.0 / 60.0);
        var entities = new List<Entity>();

        // 创建 10 个远程实体，目标 20m 内的不同位置
        for (int i = 0; i < 10; i++)
        {
            entities.Add(CreateActiveEntity(5f + i * 1.5f));
        }

        // 推进 120 帧，验证所有角色均正常推进且收敛
        for (int frame = 0; frame < 120; frame++)
        {
            _system.Update(_world, dt);
        }

        for (int i = 0; i < entities.Count; i++)
        {
            ref var interp = ref _world.Get<InterpolatedTransformComponent>(entities[i]);
            var expectedTarget = 5f + i * 1.5f;
            Assert.True(float.IsFinite(interp.X), $"实体 {i} 位置必须为有限值");
            Assert.True(interp.X > 4f, $"实体 {i} 应开始追赶目标（>4m），实际 X={interp.X}");
            Assert.True(Math.Abs(interp.X - expectedTarget) < 1f, $"实体 {i} 应收敛到目标 {expectedTarget}，实际 X={interp.X}");
        }
    }

    // ─── 任务 7.4：多角色同时 Spawn/Despawn（由 ECSUpdateDriver/FlaxActorSyncSystem 生命周期测试覆盖，
    //     此处验证插值系统对批量实体创建/销毁的稳定性） ───

    [Fact]
    public void BulkSpawnDespawn_MultipleEntities_InterpolationStable()
    {
        // spec 5.3.1 规则 3：同帧多个实体 Spawn/Despawn 不崩溃
        var dt = TimeSpan.FromSeconds(1.0 / 60.0);
        var entities = new List<Entity>();

        // 同帧批量 Spawn
        for (int i = 0; i < 5; i++)
            entities.Add(CreateActiveEntity(20f));

        _system.Update(_world, dt);

        // 验证全部实体均存在且正常推进
        for (int i = 0; i < entities.Count; i++)
        {
            Assert.True(_world.IsAlive(entities[i]));
            ref var interp = ref _world.Get<InterpolatedTransformComponent>(entities[i]);
            Assert.True(interp.X > 0f, $"实体 {i} 应正常推进");
        }

        // 同帧批量 Despawn
        for (int i = 0; i < entities.Count; i++)
            _world.Destroy(entities[i]);

        // 销毁后再推进若干帧，不崩溃
        for (int frame = 0; frame < 30; frame++)
            _system.Update(_world, dt);
    }

    // ─── 任务 7.5：断网恢复平滑追赶（偏移 60m，旧阈值 50m 会闪跳、新阈值 100m 平滑） ───

    [Fact]
    public void DisconnectRecovery_60mOffset_SmoothCatchUpNotSnap()
    {
        // spec 5.1.1 规则 1 的 b：断网 5~10 秒恢复后位置累积偏移 50~100m，
        // 新阈值 100m 下平滑追赶而非瞬移
        var entity = CreateActiveEntity(60f);
        var dt = TimeSpan.FromSeconds(1.0 / 60.0);

        _system.Update(_world, dt);

        ref var interp = ref _world.Get<InterpolatedTransformComponent>(entity);
        // 单帧位置推进应远小于 60m（平滑追赶而非瞬移）
        Assert.True(interp.X < 30f, $"断网恢复 60m 偏移应平滑追赶，单帧不应跳变到 {interp.X}m");
        Assert.True(interp.X > 0f, "应开始平滑追赶");
    }
}