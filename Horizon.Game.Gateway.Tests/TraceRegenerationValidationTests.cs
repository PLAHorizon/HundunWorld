using System;
using System.IO;
using System.Text;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 轨迹再生成 CI 校验（防静默漂移门禁）。
/// <para>
/// 金轨迹（<c>Traces/deterministic_trace_600.txt</c> / <c>multi_entity_trace_600.txt</c>）是
/// 生成器（<c>.freebuff/Verify --trace / --multi-trace</c>）的产物，与确定性对拍指纹互为凭证：
/// 轨迹必须始终代表<b>当前公式</b>的客户端预测。本测试在每次 <c>dotnet test</c>（CI 或本地）时：
/// </para>
/// <list type="number">
///   <item><b>再生成逐字节比对</b>：用生成器 <see cref="DeterministicTraceGen.BuildFileContent"/> /
///   <see cref="MultiEntityTraceGen.BuildFileContent"/> 重新生成完整轨迹内容，与嵌入式入库文件
///   <b>逐字节相等</b>——任何公式/生成器变更而未重新生成轨迹都会失败（轨迹与代码漂移）；</item>
///   <item><b>FormulaVersion 联动</b>：轨迹头部 <c># FormulaVersion = N</c> 必须等于当前
///   <see cref="MovementFormula.FormulaVersion"/>——公式版本 bump 而未显式重新生成轨迹同样失败。</item>
/// </list>
/// <para>
/// <b>公式变更流程（必须按序执行）</b>：
/// </para>
/// <list type="number">
///   <item>bump <see cref="MovementFormula.FormulaVersion"/>（并同步握手协议校验，见 FormulaVersionPolicy）；</item>
///   <item>重新生成两条轨迹：<c>dotnet run --project .freebuff/Verify --no-build -- --trace Traces/…txt</c>
///   与 <c>-- --multi-trace Traces/…txt</c>（勿加 <c>--nologo</c>，本环境会把它透传给程序参数）；</item>
///   <item>把生成器 stderr 输出的 <c>GoldenFingerprint</c>/<c>MovementFingerprint</c> 与检查点同步到
///   <c>DeterministicReplayParityTests</c> / <c>DeterministicMultiEntityParityTests</c> 的常量；</item>
///   <item>提交轨迹文件 + 测试常量（本测试与对拍测试全绿 = 轨迹、生成器、指纹、版本四者同步）。</item>
/// </list>
/// </summary>
public class TraceRegenerationValidationTests
{
    private const string SingleEntityResource = "Horizon.Game.Gateway.Tests.Traces.deterministic_trace_600.txt";
    private const string MultiEntityResource = "Horizon.Game.Gateway.Tests.Traces.multi_entity_trace_600.txt";

    private static string ReadEmbedded(string resource)
    {
        using var stream = typeof(TraceRegenerationValidationTests).Assembly
            .GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"嵌入式轨迹资源缺失：{resource}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static int ParseHeaderFormulaVersion(string content)
    {
        const string prefix = "# FormulaVersion = ";
        foreach (var line in content.Split('\n'))
        {
            if (!line.StartsWith(prefix)) continue;
            var value = line[prefix.Length..];
            int end = 0;
            while (end < value.Length && char.IsDigit(value[end])) end++;
            return int.Parse(value[..end]);
        }
        throw new InvalidOperationException("轨迹头部缺 FormulaVersion 标记——请用 .freebuff/Verify 重新生成轨迹。");
    }

    // ── 再生成逐字节比对（轨迹文件与生成器/公式漂移即失败）─────────────────

    [Fact]
    public void SingleEntityTrace_Regenerates_ByteIdentical_ToEmbedded()
    {
        var (lines, _) = DeterministicTraceGen.BuildTrace();
        var regenerated = DeterministicTraceGen.BuildFileContent(lines);
        var embedded = ReadEmbedded(SingleEntityResource);

        Assert.True(regenerated == embedded,
            "单实体轨迹与生成器漂移：公式/生成器变更后必须重新生成轨迹（.freebuff/Verify --trace）并提交，否则对拍指纹不代表当前公式。");
    }

    [Fact]
    public void MultiEntityTrace_Regenerates_ByteIdentical_ToEmbedded()
    {
        var (lines, _) = MultiEntityTraceGen.BuildTrace();
        var regenerated = MultiEntityTraceGen.BuildFileContent(lines);
        var embedded = ReadEmbedded(MultiEntityResource);

        Assert.True(regenerated == embedded,
            "多实体轨迹与生成器漂移：公式/生成器变更后必须重新生成轨迹（.freebuff/Verify --multi-trace）并提交。");
    }

    // ── FormulaVersion 联动（公式版本 bump 而未显式重生成轨迹即失败）──────

    [Fact]
    public void SingleEntityTrace_HeaderFormulaVersion_MatchesCurrent()
    {
        Assert.Equal(MovementFormula.FormulaVersion, ParseHeaderFormulaVersion(ReadEmbedded(SingleEntityResource)));
    }

    [Fact]
    public void MultiEntityTrace_HeaderFormulaVersion_MatchesCurrent()
    {
        Assert.Equal(MovementFormula.FormulaVersion, ParseHeaderFormulaVersion(ReadEmbedded(MultiEntityResource)));
    }
}
