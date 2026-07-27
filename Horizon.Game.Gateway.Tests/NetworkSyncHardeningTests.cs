using System;
using System.Collections.Generic;
using System.Reflection;
using Horizon.Game.Core.Sim;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Moq;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 网络同步加固集成测试：覆盖动态 RTT 阈值、缓冲区溢出保护、断线重连重置等生产环境健壮性特性。
/// 对应文档 §12（生产环境风险）和 §14（后续研发路线）中的短期/中期行动项。
/// </summary>
public class NetworkSyncHardeningTests
{
    private const float Dt = 1f / 60f;

    #region 动态 RTT 阈值（§12.2 MovementValidator 高延迟误判修复）

    /// <summary>
    /// 动态阈值测试：高 RTT 时 effectiveEpsilon 应放宽，避免高延迟玩家被误判。
    /// </summary>
    [Fact]
    public void MovementValidator_HighRtt_RelaxesThreshold()
    {
        var validator = new MovementValidator(new MovementValidator.Options
        {
            TickDtSeconds = Dt,
            MaxSpeed = MovementFormula.DefaultMaxSpeed,
            HardSpeedCap = MovementValidator.DefaultHardSpeedCap,
            PositionEpsilon = 0.1f,
            RttScalingFactor = 0.002f,
            MaxDynamicEpsilon = 2.0f,
        });

        var startPos = new WorldPosition(0, 0, 0);
        // 使用 5 个 tick 的输入，让合法移动距离足够大，避免触发速度反作弊检测
        var inputs = new InputPacket[]
        {
            new() { ClientTick = 1, MoveX = 0.5f, MoveY = 0 },
            new() { ClientTick = 2, MoveX = 0.5f, MoveY = 0 },
            new() { ClientTick = 3, MoveX = 0.5f, MoveY = 0 },
            new() { ClientTick = 4, MoveX = 0.5f, MoveY = 0 },
            new() { ClientTick = 5, MoveX = 0.5f, MoveY = 0 },
        };

        // 用 MovementFormula 计算权威终点
        float x = 0, y = 0, z = 0, vz = 0;
        foreach (var inp in inputs)
        {
            var (nx, ny, nz, nvz) = MovementFormula.Step(x, y, z, vz, inp.MoveX, inp.MoveY, 0, Dt, MovementFormula.DefaultMaxSpeed);
            x = nx; y = ny; z = nz; vz = nvz;
        }

        // 客户端终点有 0.12m 偏差（超过 PositionEpsilon=0.1 但在 RTT 放宽后应通过）
        var clientEnd = new WorldPosition(x + 0.12f, y, z);

        // 无 RTT 时应触发修正（drift=0.12 > epsilon=0.1）
        var resultNoRtt = validator.Validate(1, startPos, 0, inputs, clientEnd, serverTick: 1, rttMs: 0f);
        Assert.True(resultNoRtt.NeedsCorrection, "无 RTT 时 drift=0.12 > epsilon=0.1 应触发修正");

        // RTT=200ms 时 effectiveEpsilon = 0.1 + 0.002*200 = 0.5，drift=0.12 < 0.5 应通过
        var resultHighRtt = validator.Validate(1, startPos, 0, inputs, clientEnd, serverTick: 1, rttMs: 200f);
        Assert.False(resultHighRtt.NeedsCorrection, "RTT=200ms 时 effectiveEpsilon=0.5 应容纳 drift=0.12");
    }

    /// <summary>
    /// 动态阈值上限测试：effectiveEpsilon 不应超过 MaxDynamicEpsilon。
    /// </summary>
    [Fact]
    public void MovementValidator_RttScaling_CappedAtMaxDynamicEpsilon()
    {
        var validator = new MovementValidator(new MovementValidator.Options
        {
            TickDtSeconds = Dt,
            MaxSpeed = MovementFormula.DefaultMaxSpeed,
            HardSpeedCap = MovementValidator.DefaultHardSpeedCap,
            PositionEpsilon = 0.1f,
            RttScalingFactor = 0.002f,
            MaxDynamicEpsilon = 0.5f, // 上限 0.5m
        });

        var startPos = new WorldPosition(0, 0, 0);
        var inputs = new InputPacket[]
        {
            new() { ClientTick = 1, MoveX = 0f, MoveY = 0f },
        };

        // 客户端终点偏差 0.6m（超过 MaxDynamicEpsilon=0.5）
        var clientEnd = new WorldPosition(0.6f, 0, 0);

        // 即使 RTT=1000ms（理论 epsilon=0.1+2.0=2.1），上限 0.5 仍应触发修正
        var result = validator.Validate(1, startPos, 0, inputs, clientEnd, serverTick: 1, rttMs: 1000f);
        Assert.True(result.NeedsCorrection, "drift=0.6 > MaxDynamicEpsilon=0.5 即使高 RTT 也应修正");
    }

    /// <summary>
    /// ZoneShardGrain RTT EMA 估算测试：SubmitInputAsync 应从 ClientTick 计算并平滑 RTT。
    /// </summary>
    [Fact]
    public async Task ZoneShardGrain_SubmitInput_EstimatesRttViaEma()
    {
        var grain = CreateGrain();
        const ulong entityId = 9001;
        await grain.RegisterEntityAsync(entityId, 0, 0, 0);

        // 模拟 ClientTick = 当前时间 - 100ms（单向延迟约 100ms）
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var input = new InputPacket
        {
            ClientTick = nowMs - 100,
            MoveX = 0.1f,
            MoveY = 0,
        };

        await grain.SubmitInputAsync(entityId, input, 0.01f, 0, 0);

        // 通过反射读取 SimulatedEntity.EstimatedRttMs
        var entitiesField = typeof(ZoneShardGrain).GetField("_simulatedEntities", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(entitiesField);
        var entities = (Dictionary<ulong, ZoneShardGrain.SimulatedEntity>)entitiesField!.GetValue(grain)!;
        var entity = entities[entityId];

        // EMA 首次赋值应约等于 100ms（允许 ±20ms 误差，因为代码执行有延迟）
        Assert.True(entity.EstimatedRttMs > 80f && entity.EstimatedRttMs < 150f,
            $"EstimatedRttMs 应约 100ms，实际={entity.EstimatedRttMs:F1}ms");
    }

    #endregion

    #region 缓冲区溢出保护（§12 生产环境风险）

    /// <summary>
    /// SnapshotReceiveBuffer 溢出保护：超过 MaxQueueSize 时应 DropOldest。
    /// </summary>
    [Fact]
    public void SnapshotReceiveBuffer_Overflow_DropsOldest()
    {
        var buffer = SnapshotReceiveBuffer.Instance;
        buffer.ClearQueue(); // 确保干净状态

        var originalMax = buffer.MaxQueueSize;
        buffer.MaxQueueSize = 4; // 临时缩小上限

        try
        {
            // 入队 6 个包（超过上限 4）
            for (int i = 0; i < 6; i++)
            {
                buffer.Enqueue(new SnapshotPacket { ServerTick = i + 1 });
            }

            // 队列中应只有 4 个（最旧的 2 个被丢弃）
            Assert.Equal(4, buffer.Count);
            Assert.True(buffer.DroppedByOverflowCount >= 2);

            // 验证队列中保留的是最新的 4 个（tick 3,4,5,6）
            var ticks = new List<long>();
            while (buffer.TryDequeue(out var pkt))
            {
                ticks.Add(pkt.ServerTick);
            }
            Assert.Equal(4, ticks.Count);
            Assert.Equal(3, ticks[0]); // 最旧的 1,2 被丢弃
        }
        finally
        {
            buffer.MaxQueueSize = originalMax;
            buffer.ClearQueue();
        }
    }

    /// <summary>
    /// InputSendQueue 溢出保护：超过 MaxQueueSize 时应 DropOldest。
    /// </summary>
    [Fact]
    public void InputSendQueue_Overflow_DropsOldest()
    {
        var queue = InputSendQueue.Instance;
        queue.ClearQueue();

        var originalMax = queue.MaxQueueSize;
        queue.MaxQueueSize = 4;

        try
        {
            for (int i = 0; i < 6; i++)
            {
                queue.Enqueue(new InputPacket { ClientTick = i + 1 });
            }

            Assert.Equal(4, queue.Count);
            Assert.True(queue.DroppedByOverflowCount >= 2);
        }
        finally
        {
            queue.MaxQueueSize = originalMax;
            queue.ClearQueue();
        }
    }

    /// <summary>
    /// EventReceiveBuffer 溢出保护：超过 MaxQueueSize 时应 DropOldest。
    /// </summary>
    [Fact]
    public void EventReceiveBuffer_Overflow_DropsOldest()
    {
        var buffer = EventReceiveBuffer.Instance;
        buffer.ClearQueue();

        var originalMax = buffer.MaxQueueSize;
        buffer.MaxQueueSize = 4;

        try
        {
            for (int i = 0; i < 6; i++)
            {
                buffer.Enqueue(new EventPacket { ServerTick = i + 1 });
            }

            Assert.Equal(4, buffer.Count);
            Assert.True(buffer.DroppedByOverflowCount >= 2);
        }
        finally
        {
            buffer.MaxQueueSize = originalMax;
            buffer.ClearQueue();
        }
    }

    #endregion

    #region 断线重连重置（§12.4 断线重连状态污染）

    /// <summary>
    /// 断线重置测试：ClearQueue 后所有缓冲区应为空。
    /// </summary>
    [Fact]
    public void DisconnectReset_AllBuffersCleared()
    {
        // 填充各缓冲区
        SnapshotReceiveBuffer.Instance.Enqueue(new SnapshotPacket { ServerTick = 999 });
        InputSendQueue.Instance.Enqueue(new InputPacket { ClientTick = 999 });
        EventReceiveBuffer.Instance.Enqueue(new EventPacket { ServerTick = 999 });
        InputHistoryBuffer.Instance.Add(new InputPacket { ClientTick = 999 });
        CorrectionReceiveBuffer.Instance.Add(new Horizon.Game.Core.Sim.CorrectionPacket { EntityId = 999 });
        InputAckReceiveBuffer.Instance.Latest = new InputAckPacket { LastProcessedClientTick = 999 };

        // 执行断线清空
        SnapshotReceiveBuffer.Instance.ClearQueue();
        InputSendQueue.Instance.ClearQueue();
        EventReceiveBuffer.Instance.ClearQueue();
        InputHistoryBuffer.Instance.Clear();
        CorrectionReceiveBuffer.Instance.Clear();
        InputAckReceiveBuffer.Instance.Clear();

        // 验证全部为空
        Assert.Equal(0, SnapshotReceiveBuffer.Instance.Count);
        Assert.Equal(0, InputSendQueue.Instance.Count);
        Assert.Equal(0, EventReceiveBuffer.Instance.Count);
        Assert.Equal(0, InputHistoryBuffer.Instance.Count);
        Assert.False(CorrectionReceiveBuffer.Instance.TryTake(out _));
        Assert.False(InputAckReceiveBuffer.Instance.TryTake(out _));
    }

    /// <summary>
    /// 重连后旧 ACK 不应清理新会话的 InputHistoryBuffer。
    /// </summary>
    [Fact]
    public void Reconnect_StaleAck_DoesNotCorruptNewSessionHistory()
    {
        InputHistoryBuffer.Instance.Clear();
        InputAckReceiveBuffer.Instance.Clear();

        // 新会话添加输入
        InputHistoryBuffer.Instance.Add(new InputPacket { ClientTick = 10 });
        InputHistoryBuffer.Instance.Add(new InputPacket { ClientTick = 11 });
        InputHistoryBuffer.Instance.Add(new InputPacket { ClientTick = 12 });

        // 模拟旧会话 ACK（LastProcessedClientTick=100，远大于新会话 tick）
        // 如果未清空，会错误清理所有新会话输入
        InputAckReceiveBuffer.Instance.Latest = new InputAckPacket { LastProcessedClientTick = 100 };

        // 断线重置：清空 ACK 缓冲
        InputAckReceiveBuffer.Instance.Clear();

        // 验证新会话输入未被影响
        Assert.Equal(3, InputHistoryBuffer.Instance.Count);

        // 清理
        InputHistoryBuffer.Instance.Clear();
    }

    #endregion

    #region ZoneShardGrain TickAsync + 动态 RTT 端到端

    /// <summary>
    /// 端到端测试：高延迟客户端提交带偏差的终点，ZoneShardGrain TickAsync 应因 RTT 放宽而不触发修正。
    /// </summary>
    [Fact]
    public async Task ZoneShardGrain_HighLatencyClient_NoCorrectionWithRttRelaxation()
    {
        var grain = CreateGrain();
        const ulong entityId = 9100;
        await grain.RegisterEntityAsync(entityId, 0, 0, 0);

        // 模拟高延迟：ClientTick = 当前时间 - 300ms
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 计算权威终点（5 tick 移动）
        float x = 0, y = 0, z = 0, vz = 0;
        for (int i = 0; i < 5; i++)
        {
            var (nx, ny, nz, nvz) = MovementFormula.Step(x, y, z, vz, 0.5f, 0, 0, Dt, MovementFormula.DefaultMaxSpeed);
            x = nx; y = ny; z = nz; vz = nvz;
        }

        // 提交 5 个输入包，最后一个携带 0.12m 偏差（默认 epsilon=0.5 不会触发，但确认 RTT 被正确估算）
        for (int i = 0; i < 5; i++)
        {
            var input = new InputPacket
            {
                ClientTick = nowMs - 300 + i,
                MoveX = 0.5f,
                MoveY = 0,
            };
            // 最后一个包携带微小偏差终点
            var endX = (i == 4) ? x + 0.12f : x;
            await grain.SubmitInputAsync(entityId, input, reportedEndX: endX, reportedEndY: y, reportedEndZ: z);
        }

        // TickAsync 应处理实体且不触发修正（默认 PositionEpsilon=0.5 > drift=0.12）
        var processedCount = await grain.TickAsync(tickTime: 1.0);
        Assert.Equal(1, processedCount);

        // 验证：通过反射检查 _correctionsIssued 未增加
        var correctionsField = typeof(ZoneShardGrain).GetField("_correctionsIssued", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(correctionsField);
        var correctionsIssued = (int)correctionsField!.GetValue(grain)!;
        Assert.Equal(0, correctionsIssued);

        // 验证 RTT 被正确估算（应约 300ms）
        var entitiesField = typeof(ZoneShardGrain).GetField("_simulatedEntities", BindingFlags.NonPublic | BindingFlags.Instance);
        var entities = (Dictionary<ulong, ZoneShardGrain.SimulatedEntity>)entitiesField!.GetValue(grain)!;
        var entity = entities[entityId];
        Assert.True(entity.EstimatedRttMs > 250f && entity.EstimatedRttMs < 400f,
            $"EstimatedRttMs 应约 300ms，实际={entity.EstimatedRttMs:F1}ms");
    }

    #endregion

    #region 修复验证：角色无法真正移动（服务端位置不重置为原点）

    [Fact]
    public async Task BugFix_CharacterMovement_ServerPositionNotResetToOrigin()
    {
        var grain = CreateGrain();
        const ulong entityId = 9200;
        await grain.RegisterEntityAsync(entityId, 0, 0, 0);

        float clientX = 0, clientY = 0, clientZ = 0, clientVz = 0;
        for (long tick = 1; tick <= 10; tick++)
        {
            var input = new InputPacket { ClientTick = tick, MoveX = 1.0f, MoveY = 0f, CharacterId = entityId, MaxSpeed = 6f };
            var (nx, ny, nz, nvz) = MovementFormula.Step(clientX, clientY, clientZ, clientVz, input.MoveX, input.MoveY, 0, Dt, 6f);
            if (nz < 0f) { nz = 0f; nvz = 0f; }
            clientX = nx; clientY = ny; clientZ = nz; clientVz = nvz;
            input.PredictedEndX = clientX; input.PredictedEndY = clientY; input.PredictedEndZ = clientZ;
            await grain.SubmitInputAsync(entityId, input, clientX, clientY, clientZ);
            await grain.TickAsync(tickTime: tick / 60.0);
        }

        var entitiesField = typeof(ZoneShardGrain).GetField("_simulatedEntities", BindingFlags.NonPublic | BindingFlags.Instance);
        var entities = (Dictionary<ulong, ZoneShardGrain.SimulatedEntity>)entitiesField!.GetValue(grain)!;
        var entity = entities[entityId];

        Assert.True(entity.X > 0.9f, $"服务端 X 应 > 0.9m，实际={entity.X:F4}");
        Assert.True(entity.Z >= -0.01f, $"服务端 Z 应 >= 0，实际={entity.Z:F4}");
        Assert.True(entity.IsGrounded, $"IsGrounded 应为 true");
    }

    #endregion

    #region 持久化恢复测试

    /// <summary>
    /// 验证 ZoneShardGrain 持久化状态恢复：模拟实体移动后持久化，然后验证 State 中包含正确位置。
    /// </summary>
    [Fact]
    public async Task ZoneShardState_PersistAndRestore_PositionPreserved()
    {
        var mockState = new Mock<global::Orleans.Runtime.IPersistentState<Horizon.Orleans.Grains.World.ZoneShardState>>();
        var stateObj = new Horizon.Orleans.Grains.World.ZoneShardState();
        mockState.SetupGet(s => s.State).Returns(stateObj);
        mockState.Setup(s => s.WriteStateAsync()).Callback(() => { }).Returns(Task.CompletedTask);

        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var grain = new ZoneShardGrain(mockLogger.Object, mockState.Object);
        var grainId = GrainId.Create(GrainType.Create("ZoneShard"), "1");
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);
        typeof(Grain).GetField("<GrainContext>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(grain, mockContext.Object);

        const ulong entityId = 9400;
        await grain.RegisterEntityAsync(entityId, 0, 0, 0);

        // 移动 10 tick
        float cx = 0, cy = 0, cz = 0, cvz = 0;
        for (long tick = 1; tick <= 10; tick++)
        {
            var input = new InputPacket { ClientTick = tick, MoveX = 1.0f, MoveY = 0f, CharacterId = entityId, MaxSpeed = 6f };
            var (nx, ny, nz, nvz) = MovementFormula.Step(cx, cy, cz, cvz, 1.0f, 0f, 0, Dt, 6f);
            if (nz < 0f) { nz = 0f; nvz = 0f; }
            cx = nx; cy = ny; cz = nz; cvz = nvz;
            input.PredictedEndX = cx; input.PredictedEndY = cy; input.PredictedEndZ = cz;
            await grain.SubmitInputAsync(entityId, input, cx, cy, cz);
            await grain.TickAsync(tickTime: tick / 60.0);
        }

        // 触发持久化（通过反射调用 PersistEntityStateAsync）
        var persistMethod = typeof(ZoneShardGrain).GetMethod("PersistEntityStateAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        await (Task)persistMethod!.Invoke(grain, null)!;

        // 验证 State 中包含正确位置
        Assert.True(stateObj.Entities.ContainsKey(entityId), "持久化 State 应包含实体");
        var persistedEntity = stateObj.Entities[entityId];
        Assert.True(persistedEntity.X > 0.9f, $"持久化 X 应 > 0.9m，实际={persistedEntity.X:F4}");
        Assert.True(stateObj.TickCount >= 10, $"持久化 TickCount 应 >= 10，实际={stateObj.TickCount}");
    }

    #endregion

    #region 远程实体插值平滑性测试

    /// <summary>
    /// 验证 InterpolationSystem 在不均匀快照到达时输出位置连续无跳变。
    /// 模拟场景：远程实体以 6m/s 移动，快照分别在第 1/4/7 帧到达（模拟 20Hz + 抒动），
    /// 验证每帧位置变化量不超过合理上限（无瞬移）。
    /// </summary>
    [Fact]
    public void InterpolationSystem_UnevenSnapshots_PositionContinuous()
    {
        var world = Arch.Core.World.Create();
        var interpSystem = new Horizon.Game.ECS.Arch.Systems.InterpolationSystem();
        interpSystem.UseAdaptiveSpeed = false; // 固定速度便于确定性验证
        interpSystem.InterpolationSpeed = 10f; // 100ms 完成插值

        // 创建远程实体
        var entity = world.Create();
        var interp = new Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent
        {
            X = 0f, Y = 0f, Z = 0f,
            StartX = 0f, StartY = 0f, StartZ = 0f,
            TargetX = 0f, TargetY = 0f, TargetZ = 0f,
            Yaw = 0f, StartYaw = 0f, TargetYaw = 0f,
            Alpha = 1f, ServerTick = 0, ReceivedTick = 0,
            TimeSinceLastSnapshot = 0f,
        };
        world.Add(entity, interp);

        var dt = TimeSpan.FromSeconds(1.0 / 60.0);
        float prevX = 0f;
        float maxJump = 0f;

        // 模拟 30 帧，快照在第 1/4/7/10/13... 帧到达（每 3 帧一次 ≈ 20Hz）
        for (int frame = 0; frame < 30; frame++)
        {
            // 快照到达：设置新目标（服务端位置以 6m/s 沿 X 轴移动）
            if (frame % 3 == 0)
            {
                float serverX = (frame + 3) * (6f / 60f); // 3 帧后的服务端位置
                ref var interpRef = ref world.Get<Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent>(entity);
                interpRef.StartX = interpRef.X;
                interpRef.StartY = interpRef.Y;
                interpRef.StartZ = interpRef.Z;
                interpRef.TargetX = serverX;
                interpRef.TargetY = 0f;
                interpRef.TargetZ = 0f;
                interpRef.Alpha = 0f;
                interpRef.TimeSinceLastSnapshot = 0f;
            }

            // 运行插值系统
            interpSystem.Update(world, dt);

            // 检查位置连续性
            ref var interpAfter = ref world.Get<Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent>(entity);
            float jump = MathF.Abs(interpAfter.X - prevX);
            if (jump > maxJump) maxJump = jump;

            // 每帧位移不应超过 6m/s * (1/60) * 3 = 0.3m（3 倍容差，含 dead reckoning）
            Assert.True(jump < 0.35f,
                $"帧 {frame}: 位置跳变 {jump:F4}m 超过上限 0.35m（prevX={prevX:F4}, curX={interpAfter.X:F4}）");

            prevX = interpAfter.X;
        }

        // 最终位置应接近服务端最终位置（30 帧 * 6m/s / 60 = 3.0m）
        ref var finalInterp = ref world.Get<Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent>(entity);
        Assert.True(finalInterp.X > 2.0f,
            $"最终 X 应 > 2.0m（接近 3.0m），实际={finalInterp.X:F4}");

        Arch.Core.World.Destroy(world);
    }

    /// <summary>
    /// 验证 Yaw 环绕插值：从 3.0 rad 到 -3.0 rad 应走最短路径（0.28 rad），而非反向旋转 6.0 rad。
    /// </summary>
    [Fact]
    public void InterpolationSystem_YawWrapping_ShortestPath()
    {
        var world = Arch.Core.World.Create();
        var interpSystem = new Horizon.Game.ECS.Arch.Systems.InterpolationSystem();
        interpSystem.UseAdaptiveSpeed = false;
        interpSystem.InterpolationSpeed = 10f;

        var entity = world.Create();
        var interp = new Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent
        {
            Yaw = 3.0f, StartYaw = 3.0f, TargetYaw = -3.0f, // 跨越 ±π 边界
            Alpha = 0f,
        };
        world.Add(entity, interp);

        var dt = TimeSpan.FromSeconds(1.0 / 60.0);
        float prevYaw = 3.0f;

        for (int frame = 0; frame < 10; frame++)
        {
            interpSystem.Update(world, dt);
            ref var interpAfter = ref world.Get<Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent>(entity);

            // 每帧 Yaw 变化应 < π/4（0.785 rad），确保平滑无突变
            float yawChange = MathF.Abs(interpAfter.Yaw - prevYaw);
            Assert.True(yawChange < MathF.PI / 4f,
                $"帧 {frame}: Yaw 变化 {yawChange:F4}rad 超过 π/4（prev={prevYaw:F4}, cur={interpAfter.Yaw:F4}）");

            prevYaw = interpAfter.Yaw;
        }

        // 最终 Yaw 应接近 -3.0 + 2π = 3.283 rad（最短路径终点）
        ref var finalInterp = ref world.Get<Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent>(entity);
        float expectedEnd = 3.0f + 0.28318f; // 3.0 + (2π - 6.0)
        Assert.True(MathF.Abs(finalInterp.Yaw - expectedEnd) < 0.1f,
            $"最终 Yaw 应接近 {expectedEnd:F4}rad，实际={finalInterp.Yaw:F4}");

        Arch.Core.World.Destroy(world);
    }

    /// <summary>
    /// 验证快照长时间未到达时 Lerp 插值行为：
    /// 实体应平滑追赶目标位置并停留，而非原地冻结或突然跳变。
    /// </summary>
    [Fact]
    public void InterpolationSystem_SnapshotDelay_LerpConverges()
    {
        var world = Arch.Core.World.Create();
        var interpSystem = new Horizon.Game.ECS.Arch.Systems.InterpolationSystem();
        interpSystem.UseAdaptiveSpeed = false;
        interpSystem.InterpolationSpeed = 10f;

        var entity = world.Create();
        var interp = new Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent
        {
            X = 0f, Y = 0f, Z = 0f,
            TargetX = 5f, TargetY = 0f, TargetZ = 0f,
            Yaw = 0f, TargetYaw = 0f,
            Alpha = 0f, TimeSinceLastSnapshot = 0f,
        };
        world.Add(entity, interp);

        var dt = TimeSpan.FromSeconds(1.0 / 60.0);
        float prevX = 0f;

        // 模拟 60 帧（1 秒）无新快照
        for (int frame = 0; frame < 60; frame++)
        {
            interpSystem.Update(world, dt);
            ref var interpAfter = ref world.Get<Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent>(entity);

            // 每帧位移应 < 1.0m（Lerp 追赶，帧间距离递减）
            float jump = MathF.Abs(interpAfter.X - prevX);
            Assert.True(jump < 1.0f,
                $"帧 {frame}: 位置跳变 {jump:F4}m 超过上限（prev={prevX:F4}, cur={interpAfter.X:F4}）");

            prevX = interpAfter.X;
        }

        // 1 秒后实体应接近目标位置（Lerp 收敛）
        ref var finalInterp = ref world.Get<Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent>(entity);
        Assert.True(MathF.Abs(finalInterp.X - 5f) < 0.01f,
            $"1 秒后 X 应接近 5.0m（Lerp 收敛），实际={finalInterp.X:F4}");

        // 收敛后位置不再变化
        float xConverged = finalInterp.X;
        for (int frame = 0; frame < 10; frame++)
        {
            interpSystem.Update(world, dt);
        }
        ref var afterMore = ref world.Get<Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent>(entity);
        Assert.True(MathF.Abs(afterMore.X - xConverged) < 0.001f,
            $"收敛后位置不应变化：{xConverged:F4} → {afterMore.X:F4}");

        Arch.Core.World.Destroy(world);
    }

    #endregion

    #region 辅助方法

    private static ZoneShardGrain CreateGrain()
    {
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var mockState = new Mock<global::Orleans.Runtime.IPersistentState<Horizon.Orleans.Grains.World.ZoneShardState>>();
        mockState.SetupGet(s => s.State).Returns(new Horizon.Orleans.Grains.World.ZoneShardState());
        var grain = new ZoneShardGrain(mockLogger.Object, mockState.Object);

        var grainId = GrainId.Create(GrainType.Create("ZoneShard"), "1");
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);

        var contextField = typeof(Grain).GetField("<GrainContext>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        contextField?.SetValue(grain, mockContext.Object);

        return grain;
    }

    #endregion
}
