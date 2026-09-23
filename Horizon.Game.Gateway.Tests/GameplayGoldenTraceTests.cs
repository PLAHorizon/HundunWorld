using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M5.1 —— 玩法确定性金轨迹测试（验收门 H5.1）。
/// <para>
/// 本套件是**玩法侧的"确定性四方锁"**：金轨迹资产 + 位级指纹 + 双端共用实现 + 内核版本握手。
/// 移动侧早已有此护栏（<c>DeterministicReplayParityTests</c>），玩法侧（刷怪 / 怪物 AI / 战斗结算）
/// 在本轮之前**完全没有对拍资产**——一旦漂移只能靠玩家投诉发现。
/// </para>
/// <para>
/// 门禁语义：任何改变刷怪时机/数量/位置、AI 状态迁移、命中/伤害/掉落**结果**的改动，
/// 都必须重新生成轨迹并同步 <see cref="GameplayGoldenTrace.GoldenFingerprint"/>，
/// 否则本套件与 CI 立刻红灯。这是设计意图，不是测试脆弱。
/// </para>
/// </summary>
public class GameplayGoldenTraceTests
{
    private const string ResourceName = "Horizon.Game.Gateway.Tests.Traces.gameplay_trace_600.txt";

    private static string LoadTrace()
    {
        var asm = typeof(GameplayGoldenTraceTests).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"未找到嵌入资源 {ResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ---------- 金轨迹对拍（核心门禁） ----------

    [Fact]
    public void GoldenTrace_FingerprintMatchesGoldenConstant()
    {
        var result = GameplayGoldenTrace.Verify(LoadTrace());

        Assert.Null(result.Error);
        Assert.True(result.Pass,
            $"玩法指纹漂移！实际={result.Fingerprint} 期望={result.ExpectedFingerprint}。" +
            "若这是有意的行为变更，请同步 GameplayGoldenTrace.GoldenFingerprint 与轨迹头部版本号。");
        Assert.Equal(GameplayGoldenTrace.GoldenFingerprint, result.Fingerprint);
    }

    [Fact]
    public void GoldenTrace_IsReproducibleAcrossRuns()
    {
        // 同进程内重复重放必须逐位一致（跨进程/跨运行时由 CI 矩阵保证）。
        var first = GameplayGoldenTrace.Verify(LoadTrace(), "x");
        for (var i = 0; i < 5; i++)
        {
            var again = GameplayGoldenTrace.Verify(LoadTrace(), "x");
            Assert.Equal(first.Fingerprint, again.Fingerprint);
        }
    }

    // ---------- 反假绿：轨迹必须真的把玩法跑起来了 ----------

    [Fact]
    public void GoldenTrace_ActuallyExercisesGameplay()
    {
        var r = GameplayGoldenTrace.Verify(LoadTrace());

        Assert.Equal(2, r.SpawnPoints);
        Assert.Equal(1, r.Players);
        Assert.Equal(600, r.Ticks);

        // 若刷怪没跑起来，后面的 AI/战斗断言全是空的。
        Assert.True(r.MonstersSpawned >= 3, $"应至少生成 3 只怪（2 个刷怪点 + 1 次重生），实际 {r.MonstersSpawned}");
        // 击杀必须真的发生（否则"死亡 → 掉落 → 重生"整条链没被覆盖）。
        Assert.True(r.MonsterDeaths >= 1, $"轨迹应至少击杀 1 只怪，实际 {r.MonsterDeaths}");
        // 掉落必须真的产生。
        Assert.True(r.LootDrops >= 1, $"击杀应产生掉落，实际 {r.LootDrops}");
        // 重生必须真的发生：生成数 > 初始存活数（3）。
        Assert.True(r.MonstersSpawned > 3, $"应发生槽位重生（生成数 > 初始 3），实际 {r.MonstersSpawned}");
    }

    [Fact]
    public void GoldenTrace_FingerprintIsSensitiveToTraceContent()
    {
        // 指纹必须对**输入**敏感，否则它只是在测"代码能跑"而不是"结果正确"。
        var trace = LoadTrace();
        var mutated = trace.Replace("TICKS|600", "TICKS|599", StringComparison.Ordinal);
        Assert.NotEqual(trace, mutated);

        var a = GameplayGoldenTrace.Verify(trace, "x");
        var b = GameplayGoldenTrace.Verify(mutated, "x");
        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void GoldenTrace_FingerprintIsSensitiveToSpawnLayout()
    {
        var trace = LoadTrace();
        var mutated = trace.Replace("SPAWN|1|0|0|2|100|30", "SPAWN|1|1|0|2|100|30", StringComparison.Ordinal);
        Assert.NotEqual(trace, mutated);

        Assert.NotEqual(
            GameplayGoldenTrace.Verify(trace, "x").Fingerprint,
            GameplayGoldenTrace.Verify(mutated, "x").Fingerprint);
    }

    // ---------- 内核版本握手（轨迹过期即失败） ----------

    [Fact]
    public void TraceHeader_VersionsMatchCurrentKernels()
    {
        // 轨迹头部声明的三个内核版本必须与当前实现一致——否则轨迹是"过去的证据"，
        // 用它做对拍会得到虚假的安全感。
        var trace = LoadTrace();
        Assert.Contains($"# AiVersion = {MonsterAiCore.Version}", trace, StringComparison.Ordinal);
        Assert.Contains($"# SpawnVersion = {SpawnDirectorCore.Version}", trace, StringComparison.Ordinal);
        Assert.Contains($"# CombatVersion = {CombatCore.Version}", trace, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleAiVersion_IsRejectedAsExpired()
    {
        var stale = LoadTrace().Replace(
            $"# AiVersion = {MonsterAiCore.Version}",
            $"# AiVersion = {MonsterAiCore.Version + 99}",
            StringComparison.Ordinal);

        var r = GameplayGoldenTrace.Verify(stale);

        Assert.False(r.Pass);
        Assert.NotNull(r.Error);
        Assert.Contains("AiVersion", r.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleCombatVersion_IsRejectedAsExpired()
    {
        var stale = LoadTrace().Replace(
            $"# CombatVersion = {CombatCore.Version}",
            $"# CombatVersion = {CombatCore.Version + 99}",
            StringComparison.Ordinal);

        var r = GameplayGoldenTrace.Verify(stale);

        Assert.False(r.Pass);
        Assert.Contains("CombatVersion", r.Error!, StringComparison.Ordinal);
    }

    // ---------- 解析健壮性 ----------

    /// <summary>构造带**正确**版本头的轨迹文本（使校验能推进到解析阶段）。</summary>
    private static string ValidHeader() =>
        $"# AiVersion = {MonsterAiCore.Version}\n" +
        $"# SpawnVersion = {SpawnDirectorCore.Version}\n" +
        $"# CombatVersion = {CombatCore.Version}\n";

    [Fact]
    public void MalformedTrace_IsRejectedWithClearError()
    {
        var r = GameplayGoldenTrace.Verify(ValidHeader() + "BAD|LINE\nTICKS|10\n");
        Assert.False(r.Pass);
        Assert.NotNull(r.Error);
        Assert.Contains("未知记录类型", r.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void TraceWithoutTicks_IsRejected()
    {
        var r = GameplayGoldenTrace.Verify(ValidHeader() + "PLAYER|1|0|0|100\n");
        Assert.False(r.Pass);
        Assert.Contains("TICKS", r.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void TraceWithoutPlayers_IsRejected()
    {
        var r = GameplayGoldenTrace.Verify(ValidHeader() + "TICKS|10\n");
        Assert.False(r.Pass);
        Assert.Contains("玩家", r.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void WrongFieldCount_IsRejectedWithLineNumber()
    {
        var r = GameplayGoldenTrace.Verify(ValidHeader() + "SPAWN|1|0\nTICKS|10\n");
        Assert.False(r.Pass);
        Assert.Contains("SPAWN", r.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingVersionHeaders_FailFastBeforeParsing()
    {
        // 版本握手先于解析：头缺失时立刻判"轨迹过期"，而不是先解析再报无关的语法错误。
        var r = GameplayGoldenTrace.Verify("PLAYER|1|0|0|100\nTICKS|10\n");
        Assert.False(r.Pass);
        Assert.Contains("Version", r.Error!, StringComparison.Ordinal);
    }
}
