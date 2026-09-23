using System.Globalization;
using System.Text;
using Horizon.Game.Sim;

namespace Horizon.DsReplayHarness;

/// <summary>
/// UE5 DS SimDriver 对拍参照 harness（阶段 6.2 延伸 / 2.0.6 C0）。
/// <para>
/// 读取 v3 金轨迹 <c>deterministic_trace_600.txt</c>，经 <see cref="GoldenTraceReplay"/>
/// （Horizon.Game.Sim 共享实现，与 UE 客户端 <c>StartupModule()</c> C0 自证、Orleans 生产宿主
/// 同一份指纹字节布局）直接重放，输出跨语言可复算的 SHA-256 指纹，并内置黄金指纹自检——
/// DS 侧（C++ 直连复刻或引用同一份 Sim DLL）重放同一文件、按同一字节布局复算指纹即可与本
/// harness 逐字节对拍，作为金轨迹自证（UE5_DS 文档 §3.3）的可执行参照。
/// </para>
/// <para>
/// 用法：
/// <c>dotnet run --project Horizon.DsReplayHarness -- &lt;tracePath&gt; [--expect &lt;hex&gt;] [--dump &lt;outPath&gt;]</c>
/// </para>
/// <list type="bullet">
///   <item><c>tracePath</c>：轨迹文件路径（缺省回退到仓库内嵌入式轨迹资源路径）。</item>
///   <item><c>--expect</c>：期望指纹，缺省为 v3 黄金指纹；不一致即非零退出（CI 门禁语义）。</item>
///   <item><c>--dump</c>：逐 tick 权威输出行写入文件（DS 侧逐行 diff 定位首个分叉 tick）。</item>
/// </list>
/// <para>退出码：0 = 通过；1 = 指纹与期望不一致；2 = 轨迹文件缺失 / 头部版本不匹配 / 解析失败。</para>
/// </summary>
public static class Program
{
    /// <summary>仓库内轨迹的缺省路径（相对仓库根；若 harness 从仓库根运行）。</summary>
    private const string DefaultTracePath = "Horizon.Game.Gateway.Tests/Traces/deterministic_trace_600.txt";

    /// <summary>玩法金轨迹缺省路径（M5.1）。</summary>
    private const string DefaultGameplayTracePath = "Horizon.Game.Gateway.Tests/Traces/gameplay_trace_600.txt";

    public static int Main(string[] args)
    {
        // M5.1：玩法金轨迹模式（刷怪 / 怪物 AI / 战斗结算）。
        // 与移动轨迹共用同一套纪律（头部版本校验 + SHA-256 指纹 + PASS/FAIL 退出码），
        // 但重放的是 GameplayGoldenTrace —— 移动只覆盖 MovementFormula，玩法此前无对拍资产。
        if (Array.IndexOf(args, "--gameplay") >= 0)
        {
            return RunGameplay(args);
        }

        var tracePath = ResolveTracePath(args);
        var expectHex = ReadOption(args, "--expect") ?? GoldenTraceReplay.V3Fingerprint;
        var dumpPath = ReadOption(args, "--dump");

        if (!File.Exists(tracePath))
        {
            Console.Error.WriteLine($"!! 轨迹文件不存在: {tracePath}");
            return 2;
        }

        // 逐 tick dump 行收集（纯诊断，不影响指纹；onStep 回调由 GoldenTraceReplay 驱动）。
        var dumpLines = dumpPath is not null ? new List<string>() : null;
        var result = GoldenTraceReplay.Verify(
            File.ReadAllText(tracePath),
            expectHex,
            onStep: dumpLines is not null
                ? (tick, step) => dumpLines!.Add(FormatDumpLine(tick, step))
                : null);

        Console.WriteLine($"FormulaVersion      = {result.FormulaVersion}");
        Console.WriteLine($"InputLines          = {result.InputLines}");
        Console.WriteLine($"ServerTicks         = {result.ServerTicks}");
        Console.WriteLine($"LastProcessedTick   = {result.LastProcessedClientTick}");
        Console.WriteLine($"FINGERPRINT         = {result.Fingerprint}");
        Console.WriteLine($"Expected            = {result.ExpectedFingerprint}");
        Console.WriteLine($"TraceFile           = {Path.GetFullPath(tracePath)}");

        if (result.Error is not null)
        {
            // 头部 FormulaVersion 不匹配 / 解析失败：轨迹本身不可用。
            Console.WriteLine($"RESULT              = FAIL ({result.Error})");
            return 2;
        }

        Console.WriteLine(result.Pass
            ? "RESULT              = PASS (指纹与期望一致)"
            : "RESULT              = FAIL (指纹与期望不一致)");

        if (dumpPath is not null && dumpLines is not null)
        {
            File.WriteAllLines(dumpPath, dumpLines);
            Console.WriteLine($"DumpFile            = {Path.GetFullPath(dumpPath)} ({dumpLines.Count} 行)");
        }

        return result.Pass ? 0 : 1;
    }

    /// <summary>玩法金轨迹重放（<c>--gameplay [tracePath] [--expect &lt;hex&gt;]</c>）。</summary>
    private static int RunGameplay(string[] args)
    {
        var tracePath = ResolveGameplayTracePath(args);
        var expectHex = ReadOption(args, "--expect") ?? GameplayGoldenTrace.GoldenFingerprint;

        if (!File.Exists(tracePath))
        {
            Console.Error.WriteLine($"!! 玩法轨迹文件不存在: {tracePath}");
            return 2;
        }

        var result = GameplayGoldenTrace.Verify(File.ReadAllText(tracePath), expectHex);

        Console.WriteLine("=== 玩法金轨迹（GameplayGoldenTrace）===");
        Console.WriteLine($"AiVersion           = {result.AiVersion} (cur {MonsterAiCore.Version})");
        Console.WriteLine($"SpawnVersion        = {result.SpawnVersion} (cur {SpawnDirectorCore.Version})");
        Console.WriteLine($"CombatVersion       = {result.CombatVersion} (cur {CombatCore.Version})");
        Console.WriteLine($"Ticks               = {result.Ticks}");
        Console.WriteLine($"SpawnPoints         = {result.SpawnPoints}");
        Console.WriteLine($"Players             = {result.Players}");
        Console.WriteLine($"MonstersSpawned     = {result.MonstersSpawned}");
        Console.WriteLine($"MonsterDeaths       = {result.MonsterDeaths}");
        Console.WriteLine($"LootDrops           = {result.LootDrops}");
        Console.WriteLine($"FINGERPRINT         = {result.Fingerprint}");
        Console.WriteLine($"Expected            = {result.ExpectedFingerprint}");
        Console.WriteLine($"TraceFile           = {Path.GetFullPath(tracePath)}");

        if (result.Error is not null)
        {
            Console.WriteLine($"RESULT              = FAIL ({result.Error})");
            return 2;
        }

        Console.WriteLine(result.Pass
            ? "RESULT              = PASS (指纹与期望一致)"
            : "RESULT              = FAIL (指纹与期望不一致)");
        return result.Pass ? 0 : 1;
    }

    /// <summary>解析玩法轨迹路径（<c>--gameplay</c> 之后的首个非选项参数）。</summary>
    private static string ResolveGameplayTracePath(string[] args)
    {
        var seenGameplay = false;
        foreach (var a in args)
        {
            if (a == "--gameplay") { seenGameplay = true; continue; }
            if (!seenGameplay) continue;
            if (!a.StartsWith("--") && a != "--expect") return a;
        }
        return DefaultGameplayTracePath;
    }

    private static string ResolveTracePath(string[] args)
    {
        // 第一个非选项参数为轨迹路径。
        foreach (var a in args)
        {
            if (!a.StartsWith("--") && a != "--expect" && a != "--dump")
                return a;
        }
        // 缺省：仓库根（harness 运行目录向上找 Horizon.sln 所在目录）。
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln")))
            dir = dir.Parent;
        return dir is null ? DefaultTracePath : Path.Combine(dir.FullName, DefaultTracePath);
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    /// <summary>逐 tick 权威输出行（R 格式浮点 + 原始字段），供 DS 侧重放后逐行 diff 定位首个分叉 tick。</summary>
    private static string FormatDumpLine(long tick, SimWorld.StepResult step)
    {
        var sb = new StringBuilder();
        sb.Append(tick.ToString(CultureInfo.InvariantCulture)).Append('|');
        sb.Append(F(step.AuthoritativeEnd.X)).Append('|');
        sb.Append(F(step.AuthoritativeEnd.Y)).Append('|');
        sb.Append(F(step.AuthoritativeEnd.Z)).Append('|');
        sb.Append(F(step.AuthoritativeVz)).Append('|');
        sb.Append(F(step.VelocityX)).Append('|');
        sb.Append(F(step.VelocityY)).Append('|');
        sb.Append((byte)step.MovementMode).Append('|');
        sb.Append(step.IsGrounded ? 1 : 0).Append('|');
        sb.Append(step.HadInput ? 1 : 0).Append('|');
        sb.Append(step.LastProcessedClientTick.ToString(CultureInfo.InvariantCulture)).Append('|');
        if (step.Correction is null)
        {
            sb.Append('0');
        }
        else
        {
            sb.Append('1').Append('|');
            sb.Append((byte)step.Correction.Reason).Append('|');
            sb.Append(F(step.Correction.CorrectedX)).Append('|');
            sb.Append(F(step.Correction.CorrectedY)).Append('|');
            sb.Append(F(step.Correction.CorrectedZ)).Append('|');
            sb.Append(F(step.Correction.CorrectedVz)).Append('|');
            sb.Append(F(step.Correction.DriftMeters)).Append('|');
            sb.Append(step.Correction.LastProcessedClientTick.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
}
