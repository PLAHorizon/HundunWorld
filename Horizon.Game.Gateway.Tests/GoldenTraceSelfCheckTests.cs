using Horizon.Game.Sim;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 2.0.6 C0 自证共享实现的正式测试。
/// <para>
/// <see cref="GoldenTraceReplay"/>（Horizon.Game.Sim）是 UE 客户端 <c>StartupModule()</c> C0 自证、
/// <c>Horizon.DsReplayHarness</c>、以及未来 UE5 DS <c>SimDriver</c> 共用的重放 + 指纹实现。本测试
/// 在无 UE 宿主下验证该精确代码路径：黄金指纹一致（与 <c>DeterministicReplayParityTests</c> 同源）、
/// FormulaVersion 不匹配拒绝、轨迹被篡改时自证失败——即 C0 门禁确实能检出确定性漂移。
/// </para>
/// </summary>
public class GoldenTraceSelfCheckTests
{
    private const string Resource = "Horizon.Game.Gateway.Tests.Traces.deterministic_trace_600.txt";

    private static string LoadTrace()
    {
        using var stream = typeof(GoldenTraceSelfCheckTests).Assembly
            .GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"嵌入式轨迹资源缺失：{Resource}");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 共享实现重放嵌入式 v3 金轨迹必须产出黄金指纹（与 DeterministicReplayParityTests.GoldenFingerprint 同源）。
    /// 这是 C0 自证在无 UE 宿主下的等价验证——StartupModule() 调用的正是同一 Verify 实现。
    /// </summary>
    [Fact]
    public void SharedReplay_ProducesV3GoldenFingerprint()
    {
        var result = GoldenTraceReplay.Verify(LoadTrace());

        Assert.Null(result.Error);
        Assert.True(result.Pass, $"指纹 {result.Fingerprint} ≠ 黄金指纹 {result.ExpectedFingerprint}");
        Assert.Equal(GoldenTraceReplay.V3Fingerprint, result.Fingerprint);
        Assert.Equal(3, result.FormulaVersion);
        Assert.True(result.InputLines > 500, $"InputLines={result.InputLines}");
        Assert.True(result.ServerTicks > 500, $"ServerTicks={result.ServerTicks}");
        Assert.True(result.LastProcessedClientTick > 0, $"LastProcessedClientTick={result.LastProcessedClientTick}");
    }

    /// <summary>
    /// 轨迹头部 FormulaVersion 与当前公式版本不一致时，自证必须失败（防误用旧版轨迹对拍）。
    /// </summary>
    [Fact]
    public void SharedReplay_DetectsFormulaVersionMismatch()
    {
        var trace = LoadTrace().Replace("# FormulaVersion = 3", "# FormulaVersion = 2");

        var result = GoldenTraceReplay.Verify(trace);

        Assert.False(result.Pass);
        Assert.NotNull(result.Error);
        Assert.Contains("FormulaVersion", result.Error);
        Assert.Equal(2, result.FormulaVersion);
    }

    /// <summary>
    /// 篡改输入行（第三数据行 moveX 1→-1）必须改变指纹并导致自证失败——C0 门禁能检出确定性漂移。
    /// 注意：前两个数据行（serverTick 0/1）因 SimWorld 首输入分支（LastSyncTick==0 时位置对齐 + 移动分量
    /// 置零）不参与真实回放，篡改它们不影响指纹——必须篡改 serverTick ≥ 2 的行（第三数据行起）。
    /// </summary>
    [Fact]
    public void SharedReplay_DetectsTamperedTrace()
    {
        var trace = LoadTrace();
        var lines = trace.Split('\n');
        var dataIdx = Array.FindIndex(lines, l => l.Length > 0 && l[0] != '#');
        Assert.True(dataIdx >= 0 && dataIdx + 2 < lines.Length, "轨迹应有至少三条数据行");
        lines[dataIdx + 2] = lines[dataIdx + 2].Replace("|1|0|0|6|", "|-1|0|0|6|"); // moveX 1→-1（serverTick=2）

        var result = GoldenTraceReplay.Verify(string.Join('\n', lines));

        Assert.False(result.Pass);
        Assert.NotEqual(GoldenTraceReplay.V3Fingerprint, result.Fingerprint);
    }

    /// <summary>
    /// 轨迹行字段数错误（丢一列）时自证失败并给出解析错误——防静默忽略损坏轨迹。
    /// </summary>
    [Fact]
    public void SharedReplay_DetectsMalformedTraceLine()
    {
        var trace = LoadTrace();
        var lines = trace.Split('\n');
        var dataIdx = Array.FindIndex(lines, l => l.Length > 0 && l[0] != '#');
        lines[dataIdx] = lines[dataIdx].Substring(0, lines[dataIdx].LastIndexOf('|')); // 截断最后一列

        var result = GoldenTraceReplay.Verify(string.Join('\n', lines));

        Assert.False(result.Pass);
        Assert.NotNull(result.Error);
        Assert.Contains("字段数", result.Error);
    }
}
