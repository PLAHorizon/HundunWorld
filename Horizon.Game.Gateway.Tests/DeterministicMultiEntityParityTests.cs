using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Game.Sim;
using Horizon.Orleans.Grains.World;
using Microsoft.Extensions.Logging;
using Moq;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.2（多实体）— 多实体确定性对拍（AOI 可见性交叉 + 边界移动护航网）。
/// <para>
/// 目标：把单实体金轨迹扩展为<b>多实体 + AOI 层</b>——4 个实体在 2×4 Shard 网格
/// （<c>ChunksPerShardX/Z=2</c>、<c>ShardsPerRowX=4</c>、ShardCount=8，行主序 id = gz*4+gx，16m/chunk）
/// 上相向而行 / 跨边界摆动 / 垂直相位，每个 tick 重放后按位置做 AOI 订阅差量更新
/// （<see cref="ZoneShardAoi"/>）、解析覆盖 Shard 集合（<see cref="ShardRegionResolver"/>）、
/// 计算实体两两可见性（B 对 A 可见 ⟺ B 所在 chunk 的 MortonKey ∈ A 的订阅集合）。
/// 全部组件（<c>SimWorld</c>/<c>ZoneShardAoi</c>/<c>WorldCoord</c>/<c>ShardRegionResolver</c>）
/// 均为确定性纯逻辑，UE5 DS 引用同一份共享库即可跨语言对拍。
/// </para>
/// <para>
/// 轨迹资产：<c>Traces/multi_entity_trace_600.txt</c>（嵌入式资源）——4 实体 × 600 客户端帧的
/// 真实输入记录（含间隙丢帧与批量到达调度），由 <c>.freebuff/Verify --multi-trace</c> 生成。
/// 场景：E1(0,0,0)+X 0→60m（tick 519 前覆盖 [0,1]、后 [1,2]）；E2(96,0,0)-X 96→36m（与 E1 在
/// tick 319 起互相进入视口——可见性交叉）；E3(24,0,0) 摆动 24→54→24（tick 79/240 覆盖集合
/// [0,1,3]→[1,2,3]——边界移动）；E4(80,0,0) 水平静止 + 跳跃/轻功/轻功超限/自由落体（垂直多样性，
/// JumpCountExceeded 修正）。
/// </para>
/// <para>
/// <b>指纹格式（跨语言可复算）</b>：对每个服务器 tick（0..590）依次追加<b>移动记录</b>（实体按
/// EntityId 升序：X/Y/Z/Vz/VelocityX/VelocityY 各 4 字节 IEEE-754 位模式 LE + MovementMode 1 字节 +
/// IsGrounded 1 字节 + HadInput 1 字节 + LastProcessedClientTick 8 字节 LE + 修正存在标志 1 字节，
/// 存在时追加 Reason 1 字节 + CorrectedX/Y/Z/CorrectedVz/DriftMeters 各 4 字节位模式 +
/// LastProcessedClientTick 8 字节 LE），然后追加<b>AOI 记录</b>（实体按 EntityId 升序：覆盖 Shard
/// 集合 int 计数 LE + 各 Shard 8 字节 LE；订阅 chunk 集合 int 计数 LE + 各 MortonKey 8 字节 LE 升序；
/// 可见性矩阵对每个其他实体 1 字节）。全部字节取 SHA-256 → 小写 hex。
/// <see cref="MovementFingerprint"/> 只含移动记录（grain 对拍用——AOI 是宿主层，grain 不参与）。
/// </para>
/// <para>
/// 任何对移动公式/校验/回放逻辑或 AOI/网格逻辑的修改都会改变指纹 → 测试失败。重新生成：
/// <c>dotnet build .freebuff/Verify && dotnet run --project .freebuff/Verify --no-build -- --multi-trace &lt;out&gt;</c>
/// （勿加 <c>--nologo</c>），再同步本文件指纹与检查点。
/// </para>
/// </summary>
public class DeterministicMultiEntityParityTests
{
    /// <summary>黄金指纹：全轨迹逐 tick（移动 + AOI）权威输出的 SHA-256（生成：.freebuff/Verify --multi-trace）。</summary>
    private const string GoldenFingerprint = "ec7831cc2127c04e815af162a7326a1a3ef2898aa215bf8f0f9fae1f74c8b455";

    /// <summary>移动部分指纹（grain 对拍用，AOI 不参与）。</summary>
    private const string MovementFingerprint = "44e38293411f6c177dd8f5660552eb8c20a17edfc80cc1ae2c79a9b770dfaf13";

    /// <summary>扇出目标集合指纹（AOI FanOut 广播负载，独立 SHA-256；轨迹文件不变，随生成器/重放逻辑再生成）。</summary>
    private const string FanoutFingerprint = "84ce93e7ad645b5d98a98441e8f94a86f36f7c459c3b4ba8b18c2d3d322a6e09";

    /// <summary>轨迹覆盖的服务器 tick 总数（0..590；含无输入 tick）。</summary>
    private const int ServerTicks = 591;

    // ── 轨迹解析（复用生成器 InputLine 结构）──────────────────────────────

    private static IReadOnlyList<MultiEntityTraceGen.InputLine> ParseEmbeddedTrace()
    {
        const string resource = "Horizon.Game.Gateway.Tests.Traces.multi_entity_trace_600.txt";
        using var stream = typeof(DeterministicMultiEntityParityTests).Assembly
            .GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"嵌入式多实体轨迹资源缺失：{resource}");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var inputs = new List<MultiEntityTraceGen.InputLine>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split('|');
            if (p.Length != 14)
                throw new FormatException($"多实体轨迹行字段数错误（期望 14，实际 {p.Length}）：{line}");
            inputs.Add(new MultiEntityTraceGen.InputLine
            {
                ServerTick = int.Parse(p[0], CultureInfo.InvariantCulture),
                EntityId = ulong.Parse(p[1], CultureInfo.InvariantCulture),
                Input = new InputPacket
                {
                    ClientTick = long.Parse(p[2], CultureInfo.InvariantCulture),
                    MoveX = float.Parse(p[3], CultureInfo.InvariantCulture),
                    MoveY = float.Parse(p[4], CultureInfo.InvariantCulture),
                    InputBits = uint.Parse(p[5], CultureInfo.InvariantCulture),
                    MaxSpeed = float.Parse(p[6], CultureInfo.InvariantCulture),
                    PredictedEndX = float.Parse(p[7], CultureInfo.InvariantCulture),
                    PredictedEndY = float.Parse(p[8], CultureInfo.InvariantCulture),
                    PredictedEndZ = float.Parse(p[9], CultureInfo.InvariantCulture),
                    CharacterId = ulong.Parse(p[1], CultureInfo.InvariantCulture),
                },
                ReportedEndX = float.Parse(p[10], CultureInfo.InvariantCulture),
                ReportedEndY = float.Parse(p[11], CultureInfo.InvariantCulture),
                ReportedEndZ = float.Parse(p[12], CultureInfo.InvariantCulture),
            });
        }
        return inputs;
    }

    // ── grain 对拍（移动部分）─────────────────────────────────────────────

    private static async Task<string> ReplayMovementThroughGrainAsync(IReadOnlyList<MultiEntityTraceGen.InputLine> inputs)
    {
        var grain = CreateGrain();
        var simWorldField = typeof(ZoneShardGrain).GetField("_simWorld", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var correctionBufField = typeof(ZoneShardGrain).GetField("_correctionBuffer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var mirrorField = typeof(ZoneShardGrain).GetField("_simulatedEntities", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var simWorld = (SimWorld)simWorldField.GetValue(grain)!;
        var correctionBuf = (List<CorrectionPacket>)correctionBufField.GetValue(grain)!;
        var mirror = (Dictionary<ulong, ZoneShardGrain.SimulatedEntity>)mirrorField.GetValue(grain)!;

        foreach (var (id, x, y, z) in MultiEntityTraceGen.Entities)
            await grain.RegisterEntityAsync(id, x, y, z);

        var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int next = 0;
        for (int s = 0; s < ServerTicks; s++)
        {
            while (next < inputs.Count && inputs[next].ServerTick == s)
            {
                var l = inputs[next++];
                await grain.SubmitInputAsync(l.EntityId, l.Input, l.ReportedEndX, l.ReportedEndY, l.ReportedEndZ);
            }
            await grain.TickAsync(tickTime: 1.0);

            // 镜像（grain 实际广播/持久化的移动状态）+ 本 tick 修正缓冲（按实体匹配）。
            // HadInput 从 SimWorld 实体读（镜像的 HadInputThisTick 在 tick 末尾被复位）。
            foreach (var (id, _, _, _) in MultiEntityTraceGen.Entities.OrderBy(e => e.Id))
            {
                var m = mirror[id];
                var worldEntity = simWorld.GetEntity(id);
                var correction = correctionBuf.FirstOrDefault(c => c.EntityId == id);
                var pos = new WorldPosition(m.X, m.Y, m.Z);
                MultiEntityTraceGen.AppendMoveRecord(digest, new SimWorld.StepResult(
                    id,
                    startPosition: pos,
                    startYaw: 0f,
                    authoritativeEnd: pos,
                    authoritativeVz: m.Vz,
                    velocityX: m.VelocityXZ_X,
                    velocityY: m.VelocityXZ_Y,
                    movementMode: m.MovementMode,
                    yaw: 0f,
                    isGrounded: m.IsGrounded,
                    hadInput: worldEntity is { LastStepReplayedInputCount: > 0 },
                    lastProcessedClientTick: m.LastProcessedClientTick,
                    correction));
            }
        }
        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }

    private static ZoneShardGrain CreateGrain()
    {
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var mockState = new Mock<IPersistentState<ZoneShardState>>();
        mockState.SetupGet(s => s.State).Returns(new ZoneShardState());
        var grain = new ZoneShardGrain(mockLogger.Object, mockState.Object);

        GrainTestHarness.AttachGrainContext(grain);

        grain.SnapshotBroadcastIntervalTicks = 1;
        return grain;
    }

    private static void AssertBitEqual(float expected, float actual, string what)
    {
        Assert.True(
            BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual),
            $"{what}: 期望 {expected:R} 实际 {actual:R}");
    }

    private static SimWorld.StepResult StepOf(MultiEntityTraceGen.ReplaySnapshot snap, ulong id)
        => snap.Steps.First(st => st.EntityId == id);

    // ── 测试 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Trace_EmbeddedAsset_HasExpectedShape()
    {
        var inputs = ParseEmbeddedTrace();

        // 4 实体 × 597 输入行（600 客户端帧 − 3 个丢帧）；服务器 tick 0..590。
        Assert.Equal(597 * MultiEntityTraceGen.Entities.Length, inputs.Count);
        Assert.Equal(ServerTicks - 1, inputs[^1].ServerTick);
        Assert.Equal(600L, inputs[^1].Input.ClientTick);
        Assert.Equal(4UL, inputs[^1].EntityId);

        // 每实体 clientTick 严格递增（真实客户端计数器：丢帧后本地仍推进 tick）。
        foreach (var id in MultiEntityTraceGen.Entities.Select(e => e.Id))
        {
            var perEntity = inputs.Where(i => i.EntityId == id).ToArray();
            Assert.Equal(597, perEntity.Length);
            for (int i = 1; i < perEntity.Length; i++)
                Assert.True(perEntity[i].Input.ClientTick > perEntity[i - 1].Input.ClientTick,
                    $"实体 {id} clientTick 应严格递增：第 {i} 行 {perEntity[i].Input.ClientTick}");
        }

        // serverTick 单调不减（批量帧共享同一 tick）。
        for (int i = 1; i < inputs.Count; i++)
            Assert.True(inputs[i].ServerTick >= inputs[i - 1].ServerTick,
                $"serverTick 应单调不减：第 {i} 行 {inputs[i].ServerTick}");

        // 首帧锚定：E1 首输入 X=0.10000001（6 m/s × 1/60s），E4 首输入静止于 X=80。
        var firstE1 = inputs.First(i => i.EntityId == 1);
        Assert.Equal(0, firstE1.ServerTick);
        Assert.Equal(1L, firstE1.Input.ClientTick);
        AssertBitEqual(0.10000001f, firstE1.ReportedEndX, "E1 首帧 X");
        var firstE4 = inputs.First(i => i.EntityId == 4);
        AssertBitEqual(80f, firstE4.ReportedEndX, "E4 首帧 X");

        // 间隙丢帧：存在 serverTick 空洞（下一输入 clientTick 跳变）。
        bool hasGap = false;
        foreach (var id in MultiEntityTraceGen.Entities.Select(e => e.Id))
        {
            var perEntity = inputs.Where(i => i.EntityId == id).ToArray();
            for (int i = 1; i < perEntity.Length; i++)
                if (perEntity[i].ServerTick > perEntity[i - 1].ServerTick + 1) { hasGap = true; break; }
            if (hasGap) break;
        }
        Assert.True(hasGap, "轨迹应包含间隙丢帧（serverTick 空洞）");
    }

    [Fact]
    public void DirectReplay_MatchesGoldenFingerprint_AndCheckpoints()
    {
        Assert.Equal(3, MovementFormula.FormulaVersion);

        var inputs = ParseEmbeddedTrace();
        var (fingerprint, movementFingerprint, fanoutFingerprint, snapshots) =
            MultiEntityTraceGen.Replay(inputs, ServerTicks);

        // 黄金指纹：任何移动公式/校验/回放或 AOI/网格逻辑漂移都会改变哈希 → 测试失败。
        Assert.Equal(GoldenFingerprint, fingerprint);
        Assert.Equal(MovementFingerprint, movementFingerprint);
        Assert.Equal(FanoutFingerprint, fanoutFingerprint);

        // 末尾移动检查点（位级）：E1/E2/E3 各就各位、E4 轻功升空。
        var last = snapshots[ServerTicks - 1];
        AssertBitEqual(59.999657f, StepOf(last, 1).AuthoritativeEnd.X, "末尾 E1 X");
        AssertBitEqual(36.000916f, StepOf(last, 2).AuthoritativeEnd.X, "末尾 E2 X");
        AssertBitEqual(24f, StepOf(last, 3).AuthoritativeEnd.X, "末尾 E3 X（回到出生点）");
        AssertBitEqual(80f, StepOf(last, 4).AuthoritativeEnd.X, "末尾 E4 X（水平静止）");
        AssertBitEqual(158.9777f, StepOf(last, 4).AuthoritativeEnd.Z, "末尾 E4 Z");
        Assert.Null(StepOf(last, 1).Correction);

        // E4 轻功超限修正（clientTick 421..424 单批 4 按 → 第 4 按越界）确实发生。
        // 该批输入全部落在 serverTick 416（批量锚帧 ct=421 使 offset 抬升至 6..8，四帧共享同一 st）。
        Assert.Contains(snapshots, s =>
            StepOf(s, 4).Correction is { Reason: CorrectionReason.JumpCountExceeded });
    }

    [Fact]
    public void DirectReplay_AoiCrossingAndBoundaryMovement()
    {
        var inputs = ParseEmbeddedTrace();
        var (_, _, _, snapshots) = MultiEntityTraceGen.Replay(inputs, ServerTicks);

        // ── AOI 可见性交叉：E1 × E2 相向而行，tick 319 起互相进入视口（早期互不可见）。 ──
        Assert.DoesNotContain(snapshots[0].SeesByEntity[1], id => id == 2);   // tick 0：E1 不见 E2
        Assert.DoesNotContain(snapshots[0].SeesByEntity[2], id => id == 1);   // tick 0：E2 不见 E1
        Assert.Contains(snapshots[319].SeesByEntity[1], id => id == 2);       // tick 319：E1 见 E2
        Assert.Contains(snapshots[319].SeesByEntity[2], id => id == 1);       // tick 319：E2 见 E1
        Assert.Contains(snapshots[319].SeesByEntity[1], id => id == 3);       // tick 319：E1 同时见 E3
        Assert.Contains(snapshots[590].SeesByEntity[1], id => id == 2);       // 末尾仍互见

        // ── AOI 出视口：E3 在 tick 79 跨 chunk 边界瞬间离开 E1 视口。 ──
        Assert.Contains(snapshots[78].SeesByEntity[1], id => id == 3);        // tick 78：E1 见 E3
        Assert.DoesNotContain(snapshots[79].SeesByEntity[1], id => id == 3);  // tick 79：E1 不见 E3

        // ── 边界移动：覆盖 Shard 集合随视口跨网格边界变化。 ──
        Assert.Equal(new long[] { 0, 1 }, snapshots[0].ShardsByEntity[1]);     // E1 早期视口跨 gx 0-1
        Assert.Equal(new long[] { 1, 2 }, snapshots[519].ShardsByEntity[1]);   // E1 走到 chunk 3 后视口跨 gx 1-2
        Assert.Equal(new long[] { 0, 1, 3 }, snapshots[79].ShardsByEntity[3]);  // E3 在 chunk 2（X=32）
        Assert.Equal(new long[] { 1, 2, 3 }, snapshots[240].ShardsByEntity[3]); // E3 在 chunk 3（X=48.3）
        Assert.Equal(new long[] { 2, 3 }, snapshots[0].ShardsByEntity[2]);     // E2 早期（X=95.9 → chunk 5）
        Assert.Equal(new long[] { 0, 1, 2 }, snapshots[590].ShardsByEntity[2]); // E2 到 X=36（chunk 2）

        // ── 主 Shard 恒在（characterId % 8）：即使位置 Shard 变化，主 Shard 始终在覆盖集合内。 ──
        Assert.Contains(1L, snapshots[0].ShardsByEntity[1]);
        Assert.Contains(1L, snapshots[590].ShardsByEntity[1]);
        Assert.Contains(2L, snapshots[0].ShardsByEntity[2]);
        Assert.Contains(2L, snapshots[590].ShardsByEntity[2]);
        Assert.Contains(3L, snapshots[0].ShardsByEntity[3]);
        Assert.Contains(3L, snapshots[590].ShardsByEntity[3]);
        Assert.Contains(4L, snapshots[0].ShardsByEntity[4]);
        Assert.Contains(4L, snapshots[590].ShardsByEntity[4]);

        // ── AOI 订阅与可见性自洽：可见 ⟺ 目标实体所在 chunk 在本实体订阅集合内。 ──
        // （由 Replay 单一实现保证；此处抽查一个 tick 的对称性完整性：E2×E4 早期互见。）
        Assert.Contains(snapshots[0].SeesByEntity[2], id => id == 4);          // tick 0：E2 见 E4（同 chunk 5）
        Assert.Contains(snapshots[0].SeesByEntity[4], id => id == 2);          // tick 0：E4 见 E2
    }

    [Fact]
    public void DirectReplay_Fanout_ReflectsVisibility()
    {
        // 扇出目标集合 = 可见性矩阵的广播投影：实体按其所在 chunk 查订阅者，
        // payloadIndex = 实体升序索引（ids=[1,2,3,4] → E1=0, E2=1, E3=2, E4=3）。
        var inputs = ParseEmbeddedTrace();
        var (_, _, _, snapshots) = MultiEntityTraceGen.Replay(inputs, ServerTicks);

        // tick 0：E1 见 E3（同 Z 平面），E2 与 E4 同 chunk 5 互见。
        // E1 收自己(E1)+E3 → payload [0,2]；E2 收自己(E2)+E4 → [1,3]。
        Assert.Equal(new[] { 0, 2 }, snapshots[0].FanoutBySession[1]);
        Assert.Equal(new[] { 1, 3 }, snapshots[0].FanoutBySession[2]);
        Assert.Equal(new[] { 0, 2 }, snapshots[0].FanoutBySession[3]);
        Assert.Equal(new[] { 1, 3 }, snapshots[0].FanoutBySession[4]);

        // tick 319：E2 进入 E1 视口 → E2 的广播负载（payload 1）推给 E1；E1 同时收 E3（payload 2）。
        Assert.Contains(1, snapshots[319].FanoutBySession[1]);
        Assert.Contains(2, snapshots[319].FanoutBySession[1]);
        Assert.Contains(0, snapshots[319].FanoutBySession[2]);   // E2 收 E1
        Assert.Contains(3, snapshots[319].FanoutBySession[4]);   // E4 孤立高空：只收自己
        Assert.Equal(new[] { 3 }, snapshots[319].FanoutBySession[4]);

        // tick 78→79：E3 跨 chunk 边界瞬间出 E1 视口 → E1 的广播负载里 E3（payload 2）消失。
        Assert.Contains(2, snapshots[78].FanoutBySession[1]);
        Assert.DoesNotContain(2, snapshots[79].FanoutBySession[1]);
    }

    [Fact]
    public async Task GrainReplay_IsByteIdentical_ToDirectMovement()
    {
        // 同一输入序列：Orleans 生产宿主（ZoneShardGrain，多实体）vs 直接 SimWorld（UE5 DS 路径）
        // 的移动部分逐字节一致——AOI 是宿主层（grain 不参与），移动是核心，必须零漂移。
        var inputs = ParseEmbeddedTrace();
        var (_, movementFingerprint, _, _) = MultiEntityTraceGen.Replay(inputs, ServerTicks);
        var grainFingerprint = await ReplayMovementThroughGrainAsync(inputs);

        Assert.True(movementFingerprint == grainFingerprint,
            $"grain 移动指纹 {grainFingerprint} ≠ 直接移动指纹 {movementFingerprint}");
    }

    [Fact]
    public void DirectReplay_IsDeterministic_AcrossRuns()
    {
        var inputs = ParseEmbeddedTrace();
        var (fp1, _, _, _) = MultiEntityTraceGen.Replay(inputs, ServerTicks);
        var (fp2, _, _, _) = MultiEntityTraceGen.Replay(inputs, ServerTicks);
        Assert.Equal(fp1, fp2);
    }
}
