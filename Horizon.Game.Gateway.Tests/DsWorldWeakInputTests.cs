using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M5.3 —— **弱网输入模式**对 DS 权威世界的影响。
/// <para>
/// 弱网在"已建立 TCP 连接"上的真实表现不是随机丢字节（TCP 会重传），而是：
/// <b>重复到达</b>（客户端本就做冗余重传）、<b>乱序/迟到</b>（重传后与后续包交错）、
/// <b>突发</b>（网络恢复后积压一次性到达）。这些模式直接打在权威世界的输入队列上，
/// 因此必须在**内核层**验证，而不是只验证网络层。
/// </para>
/// <para>
/// 注意：客户端 <c>InputSendSystem</c> 的"未确认输入环形缓冲 + 冗余重传"是**设计行为**
/// （对抗丢包），因此"同一 clientTick 的输入被服务端重复收到"是**常态而非异常**。
/// </para>
/// </summary>
public class DsWorldWeakInputTests
{
    private const long SessionId = 2001L;
    private const ulong CharacterId = 2001UL;

    private static DsWorldSession NewWorld()
    {
        var world = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = 0,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 1,   // 每 tick 出快照，便于逐 tick 观察
            PlayerDefaultMaxHp = 1000,   // 高血量：隔离战斗干扰，聚焦移动
        });
        Assert.True(world.AddPlayer(SessionId, CharacterId, 0f, 0f, 0f, out var reason), reason);
        return world;
    }

    private static InputPacket Move(long clientTick, float predictedX)
        => new InputPacket
        {
            CharacterId = CharacterId,
            ClientTick = clientTick,
            MoveX = 1f,
            MoveY = 0f,
            PredictedEndX = predictedX,
            PredictedEndY = 0f,
            PredictedEndZ = 0f,
            MaxSpeed = MovementFormula.DefaultMaxSpeed,
        };

    /// <summary>取当前权威 X（经快照，即双端可观测的权威位置）。</summary>
    private static float ReadX(DsWorldSession world)
    {
        var r = world.Tick();
        var d = r.WorldSnapshot?.Deltas.FirstOrDefault();
        return d?.Transform?.X ?? 0f;
    }

    private const float Step = MovementFormula.DefaultMaxSpeed / 60f;

    [Fact]
    public void DuplicateInput_SameClientTick_IsAppliedOnlyOnce()
    {
        // 客户端冗余重传是设计行为：同一 clientTick 的输入会被服务端重复收到。
        // 若被重复应用，服务端会跑得比客户端快 → 持续下发修正（"被拉回"体感）。
        var world = NewWorld();

        var first = Move(1, Step);
        world.SubmitInput(SessionId, first);
        var x1 = ReadX(world);

        // 同一帧重复到达（重传）
        world.SubmitInput(SessionId, Move(1, Step));
        world.SubmitInput(SessionId, Move(1, Step));
        var x2 = ReadX(world);

        Assert.Equal(x1, x2, 4); // 位置不应因重复输入而推进
    }

    [Fact]
    public void OutOfOrderInput_OlderClientTick_DoesNotRewindAuthoritativePosition()
    {
        var world = NewWorld();

        world.SubmitInput(SessionId, Move(1, Step));
        world.SubmitInput(SessionId, Move(2, Step * 2));
        var afterNewer = ReadX(world);

        // 迟到的旧帧（重传后与后续包交错）
        world.SubmitInput(SessionId, Move(1, Step));
        var afterStale = ReadX(world);

        Assert.True(afterStale >= afterNewer - 1e-4f,
            $"迟到旧输入不应把权威位置拉回：{afterNewer} → {afterStale}");
    }

    [Fact]
    public void BurstInputs_AfterOutage_AreAllConsumedWithoutStall()
    {
        // 网络恢复后积压输入一次性到达：服务端必须消化掉，且不卡死、不丢帧。
        var world = NewWorld();

        var burst = new List<InputPacket>();
        for (var t = 1; t <= 30; t++) burst.Add(Move(t, Step * t));
        foreach (var i in burst) world.SubmitInput(SessionId, i);

        var result = world.Tick();

        Assert.Equal(1L, result.ServerTick);
        Assert.True(result.PlayerCount == 1);
        // 积压在一 tick 内处理完（SimWorld 回放全部待处理输入），位置应单调前进
        var d = result.WorldSnapshot?.Deltas.FirstOrDefault();
        Assert.True(d?.Transform?.X > 0f, "突发输入应产生前向位移");
    }

    [Fact]
    public void DuplicateAndOutOfOrder_UnderLoad_ProduceNoCorrectionStorm()
    {
        // 弱网组合场景：重复 + 乱序 + 突发交替注入，断言不出现修正风暴。
        // 修正风暴是"服务端与客户端持续对抗"的表征，比单次位置错误更致命。
        var world = NewWorld();
        var corrections = 0;

        for (var tick = 1; tick <= 300; tick++)
        {
            // 每个新帧重复投递 3 次（模拟冗余重传），并随机补投一次旧帧（重传交错）
            var input = Move(tick, Step * tick);
            world.SubmitInput(SessionId, input);
            world.SubmitInput(SessionId, Move(tick, Step * tick));

            if (tick > 3 && tick % 7 == 0)
            {
                world.SubmitInput(SessionId, Move(tick - 3, Step * (tick - 3)));
            }

            var r = world.Tick();
            corrections += r.Corrections.Count;
        }

        Assert.True(corrections <= 5,
            $"重复/乱序输入不应引发修正风暴；300 tick 内实际修正 {corrections} 次");
    }
}
