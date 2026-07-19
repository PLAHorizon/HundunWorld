using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Sim;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface.World;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Moq;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 网络同步系统的端到端集成测试。
/// 覆盖握手、输入处理、移动校验、回卷同步、重连和 ZoneShardGrain TickAsync 等核心流程。
/// </summary>
public class NetworkSyncIntegrationTests
{
    private const float Dt = 1f / 60f;

    private static readonly MethodInfo? HandleReconnectAsyncMethod =
        typeof(SyncPacketHandler).GetMethod("HandleReconnectAsync", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// 创建 ZoneShardGrain 测试实例，注入 mock IGrainContext 使 GetPrimaryKeyLong() 可用。
    /// <para>
    /// ZoneShardGrain 直接 new 时 GrainContext 为 null，导致 LogNoObserverWarn 等日志路径中
    /// 调用 GetPrimaryKeyLong() 抛 NullReferenceException。通过反射注入 mock IGrainContext 修复。
    /// </para>
    /// </summary>
    private static ZoneShardGrain CreateGrain()
    {
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var grain = new ZoneShardGrain(mockLogger.Object);

        var grainId = GrainId.Create(GrainType.Create("ZoneShard"), "1");
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);

        var contextField = typeof(Grain).GetField("<GrainContext>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        contextField?.SetValue(grain, mockContext.Object);

        return grain;
    }

    /// <summary>
    /// 握手流程测试：创建 PlayerSessionState 并调用 ApplyHandshake，验证状态更新。
    /// </summary>
    [Fact]
    public void Handshake_ValidParams_ReturnsTrueAndUpdatesState()
    {
        var state = new PlayerSessionState();

        var result = state.ApplyHandshake(baselineVersion: 1, worldPatchVersion: 3, lastAppliedDiffSeq: 100);

        Assert.True(result);
        Assert.Equal(1, state.BaselineVersion);
        Assert.Equal(3, state.WorldPatchVersion);
        Assert.Equal(100, state.LastAppliedDiffSeq);
    }

    /// <summary>
    /// 握手流程测试：传入负数参数应返回 false。
    /// </summary>
    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Handshake_NegativeParams_ReturnsFalse(int baselineVersion, int worldPatchVersion, long lastAppliedDiffSeq)
    {
        var state = new PlayerSessionState();
        Assert.False(state.ApplyHandshake(baselineVersion, worldPatchVersion, lastAppliedDiffSeq));
    }

    /// <summary>
    /// 输入 → Tick → InputAck 流程测试：提交多个输入包，消费后验证 LastProcessedClientTick 推进。
    /// </summary>
    [Fact]
    public void InputTickInputAck_MultipleInputs_AdvancesLastProcessedClientTick()
    {
        var state = new PlayerSessionState();
        state.AdvanceServerTick(100);

        var input1 = new InputPacket { ClientTick = 1, MoveX = 0.1f, MoveY = 0 };
        var input2 = new InputPacket { ClientTick = 2, MoveX = 0.2f, MoveY = 0 };
        var input3 = new InputPacket { ClientTick = 3, MoveX = 0.3f, MoveY = 0 };

        Assert.Equal(InputAcceptResult.Accepted, state.AcceptInput(input1));
        Assert.Equal(InputAcceptResult.Accepted, state.AcceptInput(input2));
        Assert.Equal(InputAcceptResult.Accepted, state.AcceptInput(input3));

        Assert.Equal(3, state.BufferedInputCount);
        Assert.Equal(0, state.LastProcessedClientTick);

        var consumed = state.ConsumeInputs();

        Assert.Equal(3, consumed.Count);
        Assert.Equal(3, state.LastProcessedClientTick);
        Assert.Equal(0, state.BufferedInputCount);

        var ack = state.BuildInputAck();
        Assert.Equal(3, ack.LastProcessedClientTick);
        Assert.Equal(100, ack.ServerTick);
    }

    /// <summary>
    /// 输入 → Tick → InputAck 流程测试：重复输入应被拒绝。
    /// </summary>
    [Fact]
    public void InputTickInputAck_DuplicateInputs_Rejected()
    {
        var state = new PlayerSessionState();

        var input = new InputPacket { ClientTick = 1, MoveX = 0.1f };
        Assert.Equal(InputAcceptResult.Accepted, state.AcceptInput(input));
        Assert.Equal(InputAcceptResult.Duplicate, state.AcceptInput(input));
    }

    /// <summary>
    /// 正常移动验证测试：使用小移动值，校验不应触发修正。
    /// </summary>
    [Fact]
    public void MovementValidation_NormalMovement_PassesWithoutCorrection()
    {
        var validator = new MovementValidator();
        var startPos = new WorldPosition(0, 0, 0);

        var inputs = new InputPacket[]
        {
            new() { ClientTick = 1, MoveX = 0.1f, MoveY = 0 },
            new() { ClientTick = 2, MoveX = 0.1f, MoveY = 0 },
            new() { ClientTick = 3, MoveX = 0.1f, MoveY = 0 },
        };

        float x = startPos.X, y = startPos.Y, z = startPos.Z, vz = 0;
        foreach (var input in inputs)
        {
            var (nx, ny, nz, nvz) = MovementFormula.Step(x, y, z, vz, input.MoveX, input.MoveY, 0, Dt, MovementFormula.DefaultMaxSpeed);
            x = nx; y = ny; z = nz; vz = nvz;
        }
        var clientEnd = new WorldPosition(x, y, z);

        var result = validator.Validate(entityId: 1, startPos, 0, inputs, clientEnd, serverTick: 10);

        Assert.False(result.NeedsCorrection);
        Assert.Null(result.Correction);
        Assert.True(result.DriftMeters < MovementValidator.DefaultPositionEpsilon);
    }

    /// <summary>
    /// 瞬移检测测试：输入为零移动但终点远离起点，应触发修正。
    /// </summary>
    [Fact]
    public void MovementValidation_TeleportDetection_TriggersCorrection()
    {
        var validator = new MovementValidator();
        var startPos = new WorldPosition(0, 0, 0);

        var inputs = new InputPacket[]
        {
            new() { ClientTick = 1, MoveX = 0, MoveY = 0 },
            new() { ClientTick = 2, MoveX = 0, MoveY = 0 },
        };

        var clientEnd = new WorldPosition(100, 100, 0);

        var result = validator.Validate(entityId: 1, startPos, 0, inputs, clientEnd, serverTick: 10);

        Assert.True(result.NeedsCorrection);
        Assert.NotNull(result.Correction);
        Assert.True(result.DriftMeters > MovementValidator.DefaultPositionEpsilon);
        Assert.Equal(CorrectionReason.PredictionDrift, result.Correction!.Reason);

        Assert.Equal(0, result.Correction.CorrectedX, 1);
        Assert.Equal(0, result.Correction.CorrectedY, 1);
        Assert.Equal(0, result.Correction.CorrectedZ, 1);
    }

    /// <summary>
    /// 速度挂检测测试：客户端 InputPacket.MaxSpeed 超过硬性上限，应标记为 SpeedHackSuspected。
    /// v6 协议：MaxSpeed 是合法字段，主判定为 input.MaxSpeed > HardSpeedCap。
    /// </summary>
    [Fact]
    public void MovementValidation_SpeedHackDetection_FlagsSpeedHack()
    {
        var validator = new MovementValidator(new MovementValidator.Options
        {
            HardSpeedCap = MovementValidator.DefaultHardSpeedCap,
            PositionEpsilon = 1000f,
            TickDtSeconds = Dt,
            MaxSpeed = MovementFormula.DefaultMaxSpeed,
        });

        var startPos = new WorldPosition(0, 0, 0);
        // clientEnd 与 startPos 重合，避免触发 PredictionDrift；
        // 速度判定完全由 input.MaxSpeed > HardSpeedCap 主导。
        var clientEnd = new WorldPosition(0, 0, 0);

        var inputs = new InputPacket[]
        {
            // MaxSpeed 显式超过 HardSpeedCap（200 m/s），触发 SpeedHackSuspected 主判定
            new() { ClientTick = 1, MoveX = 0, MoveY = 0, MaxSpeed = MovementValidator.DefaultHardSpeedCap + 50f },
            new() { ClientTick = 2, MoveX = 0, MoveY = 0, MaxSpeed = MovementValidator.DefaultHardSpeedCap + 50f },
            new() { ClientTick = 3, MoveX = 0, MoveY = 0, MaxSpeed = MovementValidator.DefaultHardSpeedCap + 50f },
        };

        var result = validator.Validate(entityId: 1, startPos, 0, inputs, clientEnd, serverTick: 10);

        Assert.True(result.NeedsCorrection);
        Assert.NotNull(result.Correction);
        Assert.Equal(CorrectionReason.SpeedHackSuspected, result.Correction!.Reason);
    }

    /// <summary>
    /// 回卷同步流程测试：客户端发送输入，服务器处理后发 InputAck，客户端回卷并重放未确认输入。
    /// </summary>
    [Fact]
    public void ReconciliationFlow_ClientReplay_MatchesServerAuthoritativePosition()
    {
        var sessionState = new PlayerSessionState();
        sessionState.AdvanceServerTick(100);

        var serverX = 0f;
        var serverY = 0f;
        var serverZ = 0f;
        var serverVz = 0f;

        var clientX = 0f;
        var clientY = 0f;
        var clientZ = 0f;
        var clientVz = 0f;

        var sentInputs = new List<(long tick, float moveX, float moveY)>();

        for (long i = 1; i <= 5; i++)
        {
            var input = new InputPacket { ClientTick = i, MoveX = 0.5f, MoveY = 0.3f };
            sessionState.AcceptInput(input);
            sentInputs.Add((i, input.MoveX, input.MoveY));

            var (sx, sy, sz, svz) = MovementFormula.Step(serverX, serverY, serverZ, serverVz, input.MoveX, input.MoveY, 0, Dt, MovementFormula.DefaultMaxSpeed);
            serverX = sx; serverY = sy; serverZ = sz; serverVz = svz;

            var (cx, cy, cz, cvz) = MovementFormula.Step(clientX, clientY, clientZ, clientVz, input.MoveX, input.MoveY, 0, Dt, MovementFormula.DefaultMaxSpeed);
            clientX = cx; clientY = cy; clientZ = cz; clientVz = cvz;
        }

        var consumed = sessionState.ConsumeInputs();
        var ack = sessionState.BuildInputAck();

        Assert.Equal(5, ack.LastProcessedClientTick);

        var confirmedTick = ack.LastProcessedClientTick;

        // 客户端回卷：从同一起点重放已发送的输入，验证 MovementFormula 确定性
        // （服务端权威位置 == 客户端从同一起点重放的位置）。
        var replayX = 0f;
        var replayY = 0f;
        var replayZ = 0f;
        var replayVz = 0f;

        foreach (var (_, moveX, moveY) in sentInputs)
        {
            var (rx, ry, rz, rvz) = MovementFormula.Step(replayX, replayY, replayZ, replayVz, moveX, moveY, 0, Dt, MovementFormula.DefaultMaxSpeed);
            replayX = rx;
            replayY = ry;
            replayZ = rz;
            replayVz = rvz;
        }

        Assert.Equal(serverX, replayX, 3);
        Assert.Equal(serverY, replayY, 3);
        Assert.Equal(serverZ, replayZ, 3);
    }

    /// <summary>
    /// 重连流程测试：小 backlog 时应返回 ResumeIncremental。
    /// </summary>
    [Fact]
    public void ReconnectFlow_SmallBacklog_ReturnsResumeIncremental()
    {
        var state = new PlayerSessionState();
        state.ApplyHandshake(baselineVersion: 1, worldPatchVersion: 5, lastAppliedDiffSeq: 100);

        var resumePacket = new ReconnectResumePacket
        {
            LocalCharacterId = 1,
            LastAppliedSnapshotTick = 50,
            LastAppliedDiffSeq = 95,
            BaselineVersion = 1,
            WorldPatchVersion = 5,
        };

        var decision = state.ApplyReconnect(resumePacket, serverHeadDiffSeq: 110, serverWorldPatchVersion: 5);

        Assert.Equal(ResumeDecision.ResumeIncremental, decision);
        Assert.Equal(1, state.BaselineVersion);
        Assert.Equal(5, state.WorldPatchVersion);
        Assert.Equal(95, state.LastAppliedDiffSeq);
    }

    /// <summary>
    /// 重连流程测试：版本差距过大时应返回 RequireLauncherPatch。
    /// </summary>
    [Fact]
    public void ReconnectFlow_LargeVersionGap_ReturnsRequireLauncherPatch()
    {
        var state = new PlayerSessionState();

        var resumePacket = new ReconnectResumePacket
        {
            LocalCharacterId = 1,
            LastAppliedSnapshotTick = 50,
            LastAppliedDiffSeq = 100,
            BaselineVersion = 1,
            WorldPatchVersion = 2,
        };

        var decision = state.ApplyReconnect(resumePacket, serverHeadDiffSeq: 110, serverWorldPatchVersion: 5);

        Assert.Equal(ResumeDecision.RequireLauncherPatch, decision);
    }

    /// <summary>
    /// 重连流程测试：backlog 过大时应返回 ResendFullChunks。
    /// </summary>
    [Fact]
    public void ReconnectFlow_LargeBacklog_ReturnsResendFullChunks()
    {
        var options = new PlayerSessionOptions { InputBufferCapacity = 256 };
        var state = new PlayerSessionState(options);

        var resumePacket = new ReconnectResumePacket
        {
            LocalCharacterId = 1,
            LastAppliedSnapshotTick = 10,
            LastAppliedDiffSeq = 0,
            BaselineVersion = 1,
            WorldPatchVersion = 5,
        };

        var hugeHead = (long)options.InputBufferCapacity * 64 + 1;
        var decision = state.ApplyReconnect(resumePacket, serverHeadDiffSeq: hugeHead, serverWorldPatchVersion: 5);

        Assert.Equal(ResumeDecision.ResendFullChunks, decision);
    }

    /// <summary>
    /// 重连流程测试：客户端自报 diff 高于服务器 head 时应返回 ForceReLogin。
    /// </summary>
    [Fact]
    public void ReconnectFlow_ClientDiffExceedsHead_ReturnsForceReLogin()
    {
        var state = new PlayerSessionState();

        var resumePacket = new ReconnectResumePacket
        {
            LocalCharacterId = 1,
            LastAppliedSnapshotTick = 50,
            LastAppliedDiffSeq = 200,
            BaselineVersion = 1,
            WorldPatchVersion = 5,
        };

        var decision = state.ApplyReconnect(resumePacket, serverHeadDiffSeq: 100, serverWorldPatchVersion: 5);

        Assert.Equal(ResumeDecision.ForceReLogin, decision);
    }

    [Fact]
    public async Task ReconnectHandler_UsesAuthoritativeDiffLogHead()
    {
        var clusterClient = new Mock<IClusterClient>();
        var sessionGrain = new Mock<IPlayerSessionGrain>();
        var diffLog = new Mock<IWorldDiffLogGrain>();
        var resume = new ReconnectResumePacket
        {
            LocalCharacterId = 1,
            LastAppliedDiffSeq = 95,
            BaselineVersion = 1,
            WorldPatchVersion = 1,
        };

        clusterClient
            .Setup(client => client.GetGrain<IPlayerSessionGrain>(1, It.IsAny<string>()))
            .Returns(sessionGrain.Object);
        clusterClient
            .Setup(client => client.GetGrain<IWorldDiffLogGrain>("global", It.IsAny<string>()))
            .Returns(diffLog.Object);
        diffLog
            .Setup(log => log.GetStatsAsync())
            .ReturnsAsync(new WorldDiffLogStats(111, 1, 110));
        sessionGrain
            .Setup(session => session.ResumeAsync(resume, 110, 1))
            .ReturnsAsync(ResumeDecision.ResumeIncremental);

        var handler = new SyncPacketHandler(
            new Mock<ILogger<MessageHandlerBase>>().Object,
            clusterClient.Object,
            new HorizonMessageAdapter());

        var task = (Task<SyncPacket>)HandleReconnectAsyncMethod!.Invoke(handler, new object[] { resume })!;
        await task;

        sessionGrain.Verify(session => session.ResumeAsync(resume, 110, 1), Times.Once);
    }

    [Fact]
    public async Task EnterWorld_TwoSessionsReceiveEachOthersEcsSpawn()
    {
        var grain = CreateGrain();
        var observer = new CapturingFanoutObserver();
        var interest = WorldCoord.GetChunksInView(0f, 0f, 0f, radius: 1).ToArray();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        await grain.EnterWorldAsync(1001, 1001, 0f, 0f, 0f, interest);
        observer.ReceivedDiffs.Clear();

        await grain.EnterWorldAsync(2002, 2002, 0f, 0f, 0f, interest);

        Assert.Contains(observer.ReceivedDiffs, received =>
            received.SessionIds.Contains(1001L)
            && ContainsSpawnForEntity(received.Diff, 2002));
        Assert.Contains(observer.ReceivedDiffs, received =>
            received.SessionIds.Contains(2002L)
            && ContainsSpawnForEntity(received.Diff, 1001));
    }

    private static bool ContainsSpawnForEntity(WorldChunkDiffPacket diff, ulong entityId)
    {
        if (diff.PayloadType != WorldChunkDiffPayloadType.EntityDelta)
        {
            return false;
        }

        var deltas = MemoryPackSerializer.Deserialize<EntityDelta[]>(diff.Payload);
        return deltas?.Any(delta => delta.EntityId == entityId && delta.Kind == EntityDeltaKind.Spawn) == true;
    }

    private sealed class CapturingFanoutObserver : IZoneShardFanoutObserver
    {
        public List<(WorldChunkDiffPacket Diff, IReadOnlyCollection<long> SessionIds)> ReceivedDiffs { get; } = new();

        public Task OnChunkDiffAsync(WorldChunkDiffPacket diff, IReadOnlyCollection<long> sessionIds)
        {
            ReceivedDiffs.Add((diff, sessionIds));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// ZoneShardGrain TickAsync 集成测试：注册实体并提交输入后调用 TickAsync，验证处理实体数大于 0。
    /// </summary>
    [Fact]
    public async Task ZoneShardGrain_TickAsync_WithValidInputs_ProcessesEntities()
    {
        var grain = CreateGrain();

        const ulong entityId = 1001;
        await grain.RegisterEntityAsync(entityId, initialX: 0, initialY: 0, initialZ: 0);

        var startPos = new WorldPosition(0, 0, 0);
        float x = startPos.X, y = startPos.Y, z = startPos.Z, vz = 0;

        for (long i = 1; i <= 5; i++)
        {
            var input = new InputPacket { ClientTick = i, MoveX = 0.5f, MoveY = 0 };
            var (nx, ny, nz, nvz) = MovementFormula.Step(x, y, z, vz, input.MoveX, input.MoveY, 0, Dt, MovementFormula.DefaultMaxSpeed);
            x = nx; y = ny; z = nz; vz = nvz;

            await grain.SubmitInputAsync(entityId, input, reportedEndX: x, reportedEndY: y, reportedEndZ: z);
        }

        var processedCount = await grain.TickAsync(tickTime: 1.0);

        Assert.Equal(1, processedCount);

        var stats = await grain.GetStatsAsync();
        Assert.Equal(0, stats.SessionCount);
        Assert.Equal(0, stats.ChunkCount);
    }

    /// <summary>
    /// ZoneShardGrain TickAsync 集成测试：无输入时 TickAsync 应返回 0。
    /// </summary>
    [Fact]
    public async Task ZoneShardGrain_TickAsync_NoInputs_ReturnsZeroProcessed()
    {
        var grain = CreateGrain();

        await grain.RegisterEntityAsync(entityId: 1, 0, 0, 0);

        var processedCount = await grain.TickAsync(tickTime: 1.0);

        Assert.Equal(0, processedCount);
    }

    /// <summary>
    /// ZoneShardGrain TickAsync 集成测试：实体被注销后 TickAsync 不应处理。
    /// </summary>
    [Fact]
    public async Task ZoneShardGrain_TickAsync_AfterUnregister_ProcessesZeroEntities()
    {
        var grain = CreateGrain();

        const ulong entityId = 2001;
        await grain.RegisterEntityAsync(entityId, 0, 0, 0);

        var input = new InputPacket { ClientTick = 1, MoveX = 0.1f };
        await grain.SubmitInputAsync(entityId, input, reportedEndX: 0.1f, reportedEndY: 0, reportedEndZ: 0);

        await grain.UnregisterEntityAsync(entityId);

        var processedCount = await grain.TickAsync(tickTime: 1.0);

        Assert.Equal(0, processedCount);
    }

    /// <summary>
    /// 完整端到端流程：注册实体 → 提交多帧输入 → TickAsync 校验 → 验证位置更新正确。
    /// </summary>
    [Fact]
    public async Task EndToEnd_EntityRegistrationInputTick_PositionUpdatesCorrectly()
    {
        var grain = CreateGrain();

        const ulong entityId = 3001;
        const float initialX = 10, initialY = 20, initialZ = 0;
        await grain.RegisterEntityAsync(entityId, initialX, initialY, initialZ);

        var validator = new MovementValidator(new MovementValidator.Options
        {
            TickDtSeconds = Dt,
            MaxSpeed = MovementFormula.DefaultMaxSpeed,
            HardSpeedCap = MovementValidator.DefaultHardSpeedCap,
            PositionEpsilon = MovementValidator.DefaultPositionEpsilon,
        });

        var inputs = new InputPacket[10];
        float x = initialX, y = initialY, z = initialZ, vz = 0;

        for (int i = 0; i < inputs.Length; i++)
        {
            var tick = (long)i + 1;
            inputs[i] = new InputPacket { ClientTick = tick, MoveX = 0.5f, MoveY = 0.25f };
            var (nx, ny, nz, nvz) = MovementFormula.Step(x, y, z, vz, inputs[i].MoveX, inputs[i].MoveY, 0, Dt, MovementFormula.DefaultMaxSpeed);
            x = nx; y = ny; z = nz; vz = nvz;

            await grain.SubmitInputAsync(entityId, inputs[i], reportedEndX: x, reportedEndY: y, reportedEndZ: z);
        }

        var validationResult = validator.Validate(
            entityId,
            new WorldPosition(initialX, initialY, initialZ),
            0,
            inputs,
            new WorldPosition(x, y, z),
            serverTick: 1);

        Assert.False(validationResult.NeedsCorrection);

        await grain.TickAsync(tickTime: 1.0);

        await grain.RegisterEntityAsync(entityId, x, y, z);

        for (int i = 0; i < 3; i++)
        {
            var tick = (long)(inputs.Length + i + 1);
            var input = new InputPacket { ClientTick = tick, MoveX = 0.1f, MoveY = 0 };
            var (nx, ny, nz, nvz) = MovementFormula.Step(x, y, z, vz, input.MoveX, input.MoveY, 0, Dt, MovementFormula.DefaultMaxSpeed);
            x = nx; y = ny; z = nz; vz = nvz;
            await grain.SubmitInputAsync(entityId, input, reportedEndX: x, reportedEndY: y, reportedEndZ: z);
        }

        var processedCount = await grain.TickAsync(tickTime: 2.0);

        Assert.True(processedCount >= 1);
    }

    /// <summary>
    /// 多端一致性端到端测试：验证"客户端输入（行走+旋转+跳跃）→服务端权威 TickAsync→广播 EntityDelta"
    /// 链路产出的 delta 包含正确的位置、朝向、动作状态，作为多端同步的代码层证据。
    /// </summary>
    /// <remarks>
    /// 覆盖目标要求的"行走、旋转、跳跃等基础动作的多端一致性"：
    /// <list type="bullet">
    ///   <item>行走：MoveX=0.5 → EntityDelta.Transform.X 增加</item>
    ///   <item>旋转：LookYaw=π/2 → EntityDelta.Transform.Yaw 等于输入值</item>
    ///   <item>跳跃：InputBits bit0=1 → EntityDelta.Transform.Y(Z 轴)上升 + MovementMode=Fall + IsGrounded=false</item>
    /// </list>
    /// 坐标系：服务端 entity 使用 ECS Z-up（X=左右, Y=前后, Z=上下），
    /// EntityDelta.Transform 使用 Flax Y-up（X=左右, Y=上下, Z=前后），
    /// TickAsync 在构建 delta 时执行 ECS→Flax 转换：Transform.Y=entity.Z, Transform.Z=entity.Y。
    /// </remarks>
    [Fact]
    public async Task EndToEnd_InputWithRotationAndJump_BroadcastsDeltaWithCorrectTransformAndMovementState()
    {
        var grain = CreateGrain();
        var observer = new CapturingFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        // 订阅实体所在 chunk（实体位于原点 ECS(0,0,0)）
        var chunkKey0 = WorldCoord.ToChunkMortonKey(0, 0, 0);
        await grain.SubscribeSessionAsync(sessionId: 5001, mortonKeys: new[] { chunkKey0 });

        const ulong entityId = 4001;
        // RegisterEntityAsync 接受 Flax 坐标 (initialX, initialY, initialZ)，
        // 内部转换为 ECS（ecsX=initialX, ecsY=initialZ, ecsZ=initialY）。
        // 传入 (0,0,0) → ECS(0,0,0)。
        await grain.RegisterEntityAsync(entityId, initialX: 0, initialY: 0, initialZ: 0);

        // 清空注册阶段产生的 Spawn delta，只保留 TickAsync 产生的 Update delta
        observer.ReceivedDiffs.Clear();

        // 构造输入包：MoveX=0.5（行走） + LookYaw=π/2（旋转 90°） + InputBits bit0=1（跳跃，边沿触发后语义）
        const float lookYaw = MathF.PI / 2f;
        var input = new InputPacket
        {
            ClientTick = 1,
            MoveX = 0.5f,
            MoveY = 0f,
            LookYaw = lookYaw,
            InputBits = 0x1, // 跳跃位（客户端边沿触发后写入）
            CharacterId = entityId,
        };

        // 用 MovementFormula 计算客户端预测的终点（与服务端权威回放一致）
        // 跳跃：jumpImpulse = 5.5 m/s（基础单次跳跃）
        var (predictedX, predictedY, predictedZ, predictedVz) = MovementFormula.Step(
            x: 0f, y: 0f, z: 0f, vz: 0f,
            input.MoveX, input.MoveY, jumpImpulse: 5.5f,
            Dt, MovementFormula.DefaultMaxSpeed);

        await grain.SubmitInputAsync(
            entityId, input,
            reportedEndX: predictedX, reportedEndY: predictedY, reportedEndZ: predictedZ);

        // 触发服务端权威 TickAsync
        var processedCount = await grain.TickAsync(tickTime: 1.0);
        Assert.Equal(1, processedCount);

        // TickAsync 的 BroadcastSnapshotAsync 是 fire-and-forget，需等待广播完成
        await Task.Delay(300);

        // 从 observer 捕获的 diff 中找到对应 entityId 的 Update delta
        EntityDelta? targetDelta = null;
        foreach (var (diff, _) in observer.ReceivedDiffs)
        {
            if (diff.PayloadType != WorldChunkDiffPayloadType.EntityDelta) continue;
            var deltas = MemoryPackSerializer.Deserialize<EntityDelta[]>(diff.Payload);
            if (deltas is null) continue;
            var found = deltas.FirstOrDefault(d => d.EntityId == entityId && d.Kind == EntityDeltaKind.Update);
            if (found.EntityId == entityId)
            {
                targetDelta = found;
                break;
            }
        }

        // 验证 1：TickAsync 应广播包含该实体的 Update delta
        Assert.NotNull(targetDelta, "未捕获到 entityId={EntityId} 的 Update delta");
        Assert.NotNull(targetDelta!.Transform, "delta.Transform 不应为 null（每 tick 都应填充）");
        Assert.NotNull(targetDelta.MovementState, "delta.MovementState 不应为 null（每 tick 都应填充）");

        var transform = targetDelta.Transform!.Value;
        var movementState = targetDelta.MovementState!.Value;

        // 验证 2：旋转同步 — Transform.Yaw 等于输入的 LookYaw
        // 服务端 TickAsync: entity.Yaw = lastInput.LookYaw → EntityDelta.Transform.Yaw = entity.Yaw
        Assert.Equal(lookYaw, transform.Yaw, 5);

        // 验证 3：行走同步 — Transform.X 增加（ECS X = Flax X，无转换）
        // MoveX=0.5, maxSpeed=6, dt=1/60 → dx = 0.5*6*(1/60) = 0.05m
        Assert.True(transform.X > 0f,
            $"行走后 Transform.X 应增加，实际 X={transform.X}（预期约 0.05）");

        // 验证 4：跳跃同步 — Transform.Y（Flax 上下轴 = ECS Z）上升
        // 服务端 TickAsync 构建 delta: Transform.Y = entity.Z（ECS Z → Flax Y）
        // 跳跃后 entity.Z 上升约 0.0889m（jumpImpulse=5.5, gravity=9.81, dt=1/60）
        Assert.True(transform.Y > 0f,
            $"跳跃后 Transform.Y（Flax 上下轴）应上升，实际 Y={transform.Y}（预期约 0.089）");

        // 验证 5：动作状态同步 — MovementMode = Fall（跳跃后离地）
        // 服务端 TickAsync 推导：!entity.IsGrounded → MovementMode.Fall
        Assert.Equal(MovementMode.Fall, movementState.MovementMode);

        // 验证 6：落地状态同步 — IsGrounded = false（跳跃后空中）
        Assert.False(movementState.IsGrounded,
            $"跳跃后 IsGrounded 应为 false，实际 = {movementState.IsGrounded}");
    }
}
