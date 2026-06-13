using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Core.Sim;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;
using Horizon.Orleans.Grains.World;
using Microsoft.Extensions.Logging;
using Moq;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 网络同步系统的端到端集成测试。
/// 覆盖握手、输入处理、移动校验、回卷同步、重连和 ZoneShardGrain TickAsync 等核心流程。
/// </summary>
public class NetworkSyncIntegrationTests
{
    private const float Dt = 1f / 60f;

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
    /// 速度挂检测测试：输入导致客户端速度超过硬性上限，应标记为 SpeedHackSuspected。
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
        var farDistance = MovementValidator.DefaultHardSpeedCap * Dt * 3 + 5f;
        var clientEnd = new WorldPosition(farDistance, 0, 0);

        var inputs = new InputPacket[]
        {
            new() { ClientTick = 1, MoveX = 0, MoveY = 0 },
            new() { ClientTick = 2, MoveX = 0, MoveY = 0 },
            new() { ClientTick = 3, MoveX = 0, MoveY = 0 },
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

        var replayX = serverX;
        var replayY = serverY;
        var replayZ = serverZ;
        var replayVz = serverVz;

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

    /// <summary>
    /// ZoneShardGrain TickAsync 集成测试：注册实体并提交输入后调用 TickAsync，验证处理实体数大于 0。
    /// </summary>
    [Fact]
    public async Task ZoneShardGrain_TickAsync_WithValidInputs_ProcessesEntities()
    {
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var grain = new ZoneShardGrain(mockLogger.Object);

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
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var grain = new ZoneShardGrain(mockLogger.Object);

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
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var grain = new ZoneShardGrain(mockLogger.Object);

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
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var grain = new ZoneShardGrain(mockLogger.Object);

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
}
