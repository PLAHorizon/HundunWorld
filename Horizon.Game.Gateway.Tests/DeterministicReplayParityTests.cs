using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
/// 阶段 6.2 — 确定性对拍（确定性护航网）。
/// <para>
/// 目标：证明"同一套模拟换宿主"无漂移——Orleans 生产权威回放（<see cref="ZoneShardGrain"/>
/// 经 <see cref="SimWorld"/> 驱动）与直接 <see cref="SimWorld"/> 重放（未来 UE5 DS 的
/// <c>SimDriver</c> 将调用同一驱动）在<b>同一输入序列</b>下产出<b>逐字节一致</b>的权威输出，
/// 并固化为黄金指纹，使灰度切换 / SimWorld 移植期间任何公式、校验或回放逻辑漂移都会破坏本测试。
/// </para>
/// <para>
/// 轨迹资产：<c>Traces/deterministic_trace_600.txt</c>（嵌入式资源）——600 客户端帧的"真实输入
/// 记录"（客户端用 <see cref="MovementFormula"/> 连续预测，含间隙丢帧与批量打包的到达调度），
/// 由 <c>.freebuff/Verify --trace</c> 生成。UE5 DS 侧重放同一文件并复算下方指纹即可与本仓库对拍。
/// </para>
/// <para>
/// <b>指纹格式（跨语言可复算）</b>：对每个服务器 tick（0..590）依次追加：
/// X/Y/Z/Vz/VelocityX/VelocityY 各 4 字节（IEEE-754 位模式，little-endian）、
/// MovementMode 1 字节、IsGrounded 1 字节、HadInput 1 字节、LastProcessedClientTick 8 字节（LE）、
/// 修正存在标志 1 字节（0/1）；存在时追加 Reason 1 字节 + CorrectedX/Y/Z/CorrectedVz/DriftMeters
/// 各 4 字节（位模式）+ LastProcessedClientTick 8 字节（LE）。全部字节取 SHA-256 → 小写 hex。
/// </para>
/// <para>
/// 任何对 <see cref="MovementFormula"/> / <see cref="MovementValidator"/> / <see cref="SimWorld"/>
/// 回放逻辑的修改都必须 bump <see cref="MovementFormula.FormulaVersion"/> 并重新生成轨迹
/// （<c>dotnet build .freebuff/Verify && dotnet run --project .freebuff/Verify --no-build -- --trace &lt;out&gt;</c>，
/// 注意勿加 <c>--nologo</c>——本环境会把它透传给程序参数），再同步本文件的
/// <see cref="GoldenFingerprint"/> 与检查点。
/// </para>
/// </summary>
public class DeterministicReplayParityTests
{
    /// <summary>黄金指纹：全轨迹逐 tick 权威输出的 SHA-256（生成：.freebuff/Verify --trace）。</summary>
    private const string GoldenFingerprint = "1474bd2332c0fead92694db8df5020b585eab649b66ac13db657ca3784718836";

    /// <summary>轨迹覆盖的服务器 tick 总数（0..592；含无输入 tick）。</summary>
    private const int ServerTicks = 593;

    private const ulong EntityId = 1;

    // ── 轨迹解析 ──────────────────────────────────────────────────────────

    private sealed class InputLine
    {
        public int ServerTick;
        public InputPacket Input = new();
        public float ReportedEndX, ReportedEndY, ReportedEndZ;
    }

    private static IReadOnlyList<InputLine> ParseEmbeddedTrace()
    {
        const string resource = "Horizon.Game.Gateway.Tests.Traces.deterministic_trace_600.txt";
        using var stream = typeof(DeterministicReplayParityTests).Assembly
            .GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"嵌入式轨迹资源缺失：{resource}");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var inputs = new List<InputLine>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split('|');
            if (p.Length != 13)
                throw new FormatException($"轨迹行字段数错误（期望 13，实际 {p.Length}）：{line}");
            inputs.Add(new InputLine
            {
                ServerTick = int.Parse(p[0], CultureInfo.InvariantCulture),
                Input = new InputPacket
                {
                    ClientTick = long.Parse(p[1], CultureInfo.InvariantCulture),
                    MoveX = float.Parse(p[2], CultureInfo.InvariantCulture),
                    MoveY = float.Parse(p[3], CultureInfo.InvariantCulture),
                    InputBits = uint.Parse(p[4], CultureInfo.InvariantCulture),
                    MaxSpeed = float.Parse(p[5], CultureInfo.InvariantCulture),
                    PredictedEndX = float.Parse(p[6], CultureInfo.InvariantCulture),
                    PredictedEndY = float.Parse(p[7], CultureInfo.InvariantCulture),
                    PredictedEndZ = float.Parse(p[8], CultureInfo.InvariantCulture),
                    CharacterId = EntityId,
                },
                ReportedEndX = float.Parse(p[9], CultureInfo.InvariantCulture),
                ReportedEndY = float.Parse(p[10], CultureInfo.InvariantCulture),
                ReportedEndZ = float.Parse(p[11], CultureInfo.InvariantCulture),
            });
        }
        return inputs;
    }

    // ── 黄金指纹（与 .freebuff/Verify/DeterministicTraceGen 逐字一致）──────

    private static void AppendRecord(
        IncrementalHash digest,
        float x, float y, float z, float vz,
        float velX, float velY,
        MovementMode mode, bool grounded, bool hadInput, long lastAck,
        CorrectionPacket? correction)
    {
        AppendFloat(digest, x);
        AppendFloat(digest, y);
        AppendFloat(digest, z);
        AppendFloat(digest, vz);
        AppendFloat(digest, velX);
        AppendFloat(digest, velY);
        digest.AppendData(new[] { (byte)mode });
        digest.AppendData(new[] { (byte)(grounded ? 1 : 0) });
        digest.AppendData(new[] { (byte)(hadInput ? 1 : 0) });
        digest.AppendData(BitConverter.GetBytes(lastAck));
        if (correction is null)
        {
            digest.AppendData(new byte[] { 0 });
        }
        else
        {
            digest.AppendData(new byte[] { 1 });
            digest.AppendData(new[] { (byte)correction.Reason });
            AppendFloat(digest, correction.CorrectedX);
            AppendFloat(digest, correction.CorrectedY);
            AppendFloat(digest, correction.CorrectedZ);
            AppendFloat(digest, correction.CorrectedVz);
            AppendFloat(digest, correction.DriftMeters);
            digest.AppendData(BitConverter.GetBytes(correction.LastProcessedClientTick));
        }
    }

    private static void AppendFloat(IncrementalHash digest, float v)
        => digest.AppendData(BitConverter.GetBytes(BitConverter.SingleToInt32Bits(v)));

    private static string ToHex(IncrementalHash digest)
        => Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();

    /// <summary>直接 SimWorld 重放（未来 UE5 DS SimDriver 的调用路径）→ (指纹, 逐 tick 结果)。</summary>
    private static (string Fingerprint, List<SimWorld.StepResult> Results) ReplayDirect(IReadOnlyList<InputLine> inputs)
    {
        var world = new SimWorld();
        world.AddOrUpdateEntity(EntityId, 0f, 0f, 0f, maxSpeed: 6f);
        var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var results = new List<SimWorld.StepResult>(ServerTicks);
        int next = 0;
        for (int s = 0; s < ServerTicks; s++)
        {
            while (next < inputs.Count && inputs[next].ServerTick == s)
            {
                var l = inputs[next++];
                world.SubmitInput(EntityId, l.Input, l.ReportedEndX, l.ReportedEndY, l.ReportedEndZ);
            }
            var step = world.Step(s)[0];
            results.Add(step);
            AppendRecord(digest,
                step.AuthoritativeEnd.X, step.AuthoritativeEnd.Y, step.AuthoritativeEnd.Z, step.AuthoritativeVz,
                step.VelocityX, step.VelocityY,
                step.MovementMode, step.IsGrounded, step.HadInput, step.LastProcessedClientTick,
                step.Correction);
        }
        return (ToHex(digest), results);
    }

    /// <summary>经 ZoneShardGrain（Orleans 生产宿主）重放同一输入序列 → 同格式指纹。</summary>
    private static async Task<string> ReplayThroughGrainAsync(IReadOnlyList<InputLine> inputs)
    {
        var grain = CreateGrain();
        var simWorldField = typeof(ZoneShardGrain).GetField("_simWorld", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var correctionBufField = typeof(ZoneShardGrain).GetField("_correctionBuffer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var mirrorField = typeof(ZoneShardGrain).GetField("_simulatedEntities", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var simWorld = (SimWorld)simWorldField.GetValue(grain)!;
        var correctionBuf = (List<CorrectionPacket>)correctionBufField.GetValue(grain)!;
        var mirror = (Dictionary<ulong, ZoneShardGrain.SimulatedEntity>)mirrorField.GetValue(grain)!;

        await grain.RegisterEntityAsync(EntityId, 0f, 0f, 0f);
        var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int next = 0;
        for (int s = 0; s < ServerTicks; s++)
        {
            while (next < inputs.Count && inputs[next].ServerTick == s)
            {
                var l = inputs[next++];
                await grain.SubmitInputAsync(EntityId, l.Input, l.ReportedEndX, l.ReportedEndY, l.ReportedEndZ);
            }
            await grain.TickAsync(tickTime: 1.0);

            // 镜像（grain 实际广播/持久化的移动状态）+ 本 tick 修正缓冲。
            // HadInput 从 SimWorld 实体读（LastStepReplayedInputCount>0）：镜像的
            // HadInputThisTick 在 tick 末尾被复位为 false（仅广播决策期有效），不可事后读。
            var m = mirror[EntityId];
            var worldEntity = simWorld.GetEntity(EntityId);
            var correction = correctionBuf.Count > 0 ? correctionBuf[0] : null;
            AppendRecord(digest,
                m.X, m.Y, m.Z, m.Vz,
                m.VelocityXZ_X, m.VelocityXZ_Y,
                m.MovementMode, m.IsGrounded, worldEntity is { LastStepReplayedInputCount: > 0 },
                m.LastProcessedClientTick,
                correction);
        }
        return ToHex(digest);
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

    // ── 测试 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Trace_EmbeddedAsset_HasExpectedShape()
    {
        var inputs = ParseEmbeddedTrace();

        // 600 客户端帧 − 3 个丢帧 = 597 输入行；服务器 tick 0..590。
        Assert.Equal(597, inputs.Count);
        Assert.Equal(ServerTicks - 1, inputs[^1].ServerTick);
        Assert.Equal(600L, inputs[^1].Input.ClientTick);

        // 客户端 tick 严格递增（真实客户端计数器：丢帧后本地仍推进 tick，服务端据此做间隙吸附）。
        for (int i = 1; i < inputs.Count; i++)
            Assert.True(inputs[i].Input.ClientTick > inputs[i - 1].Input.ClientTick,
                $"clientTick 应严格递增：第 {i} 行 {inputs[i].Input.ClientTick}");

        // serverTick 单调不减（批量帧共享同一 tick）。
        for (int i = 1; i < inputs.Count; i++)
            Assert.True(inputs[i].ServerTick >= inputs[i - 1].ServerTick,
                $"serverTick 应单调不减：第 {i} 行 {inputs[i].ServerTick}");

        // 首个输入：客户端预测自洽（报告终点 = 包内预测终点 = 首帧位移后的位置，出生 (0,0,0)），
        // 服务端首输入对齐吸附到该终点（位移已含在终点内，不重复回放）。
        Assert.Equal(0, inputs[0].ServerTick);
        Assert.Equal(1L, inputs[0].Input.ClientTick);
        Assert.Equal(inputs[0].Input.PredictedEndX, inputs[0].ReportedEndX);
        Assert.Equal(inputs[0].Input.PredictedEndY, inputs[0].ReportedEndY);
        Assert.Equal(inputs[0].Input.PredictedEndZ, inputs[0].ReportedEndZ);
        AssertBitEqual(0.10000001f, inputs[0].ReportedEndX, "首帧 X（6 m/s × 1/60s）");
        Assert.Equal(0f, inputs[0].ReportedEndY);
        Assert.Equal(0f, inputs[0].ReportedEndZ);

        // 间隙丢帧：存在 serverTick 无输入（下一输入 clientTick 跳变）。
        bool hasGap = false;
        for (int i = 1; i < inputs.Count; i++)
            if (inputs[i].ServerTick > inputs[i - 1].ServerTick + 1) { hasGap = true; break; }
        Assert.True(hasGap, "轨迹应包含间隙丢帧（serverTick 空洞）");
    }

    [Fact]
    public void DirectReplay_MatchesGoldenFingerprint_AndCheckpoints()
    {
        Assert.Equal(3, MovementFormula.FormulaVersion);

        var (fingerprint, results) = ReplayDirect(ParseEmbeddedTrace());

        // 黄金指纹：任何公式/校验/回放漂移都会改变哈希 → 测试失败。
        Assert.Equal(GoldenFingerprint, fingerprint);

        // 关键检查点（位级）：末尾、两个加速度阶跃修正 tick、轻功超限修正 tick。
        var last = results[ServerTicks - 1];
        AssertBitEqual(19.242659f, last.AuthoritativeEnd.X, "末尾 X");
        AssertBitEqual(4.24264f, last.AuthoritativeEnd.Y, "末尾 Y");
        AssertBitEqual(51.18175f, last.AuthoritativeEnd.Z, "末尾 Z");
        AssertBitEqual(39.421993f, last.AuthoritativeVz, "末尾 Vz");
        Assert.Equal(MovementMode.Jump, last.MovementMode);
        Assert.False(last.IsGrounded);
        Assert.Null(last.Correction);

        Assert.Equal(CorrectionReason.AccelerationExceeded, results[118].Correction!.Reason);
        AssertBitEqual(10.442651f, results[118].AuthoritativeEnd.X, "tick118 X");

        Assert.Equal(CorrectionReason.AccelerationExceeded, results[178].Correction!.Reason);
        AssertBitEqual(22.14267f, results[178].AuthoritativeEnd.X, "tick178 X");

        Assert.Equal(CorrectionReason.JumpCountExceeded, results[418].Correction!.Reason);
        AssertBitEqual(16.242647f, results[418].AuthoritativeEnd.X, "tick418 X");
        AssertBitEqual(9.248674f, results[418].AuthoritativeEnd.Z, "tick418 Z");

        // 1.3.4 跨 tick 轻功护航：phase 6（tick 358/359/360）三按分属不同 server tick，
        // 权威侧跨批延续计数 5.5/4.5/3.5——合法 3 按无 correction（修复前每批从 0 起跳只给首段 → drift）。
        Assert.Null(results[358].Correction);
        Assert.Null(results[359].Correction);
        Assert.Null(results[360].Correction);
    }

    [Fact]
    public async Task GrainReplay_IsByteIdentical_ToDirectReplay()
    {
        // 同一输入序列：Orleans 生产宿主（ZoneShardGrain）vs 直接 SimWorld（UE5 DS 路径）——
        // 逐字节一致（SHA-256 相等），证明"同一套模拟换宿主"无漂移。
        var inputs = ParseEmbeddedTrace();
        var (directFingerprint, _) = ReplayDirect(inputs);
        var grainFingerprint = await ReplayThroughGrainAsync(inputs);

        Assert.True(directFingerprint == grainFingerprint,
            $"grain 指纹 {grainFingerprint} ≠ 直接指纹 {directFingerprint}");
    }

    [Fact]
    public void DirectReplay_IsDeterministic_AcrossRuns()
    {
        var inputs = ParseEmbeddedTrace();
        var (fp1, _) = ReplayDirect(inputs);
        var (fp2, _) = ReplayDirect(inputs);
        Assert.Equal(fp1, fp2);
    }

    /// <summary>
    /// 阶段 6.3 P0 ③ — 服务端 Correction 率指标：经生产宿主（ZoneShardGrain）重放金轨迹
    /// 至 tick 418（JumpCountExceeded 检查点），断言 <c>horizon.sync.corrections</c> 按 reason 维度上报
    /// （AccelerationExceeded 于 tick 118/178、JumpCountExceeded 于 tick 418——与直接重放检查点同源）。
    /// </summary>
    [Fact]
    public async Task GrainReplay_Corrections_ReportMetricByReason()
    {
        using var capture = new SyncMetricsCapture();
        var grain = CreateGrain();
        var inputs = ParseEmbeddedTrace();
        await grain.RegisterEntityAsync(EntityId, 0f, 0f, 0f);

        var next = 0;
        for (var s = 0; s <= 418; s++) // 覆盖 JumpCountExceeded 检查点 tick
        {
            while (next < inputs.Count && inputs[next].ServerTick == s)
            {
                var l = inputs[next++];
                await grain.SubmitInputAsync(EntityId, l.Input, l.ReportedEndX, l.ReportedEndY, l.ReportedEndZ);
            }
            await grain.TickAsync(tickTime: 1.0);
        }

        lock (capture.Counters)
        {
            Assert.Contains(capture.Counters,
                m => m.Name == "horizon.sync.corrections" && m.Reason == "AccelerationExceeded");
            Assert.Contains(capture.Counters,
                m => m.Name == "horizon.sync.corrections" && m.Reason == "JumpCountExceeded");
        }
    }
}
