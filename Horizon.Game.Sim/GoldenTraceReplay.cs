using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.Sim;

/// <summary>
/// 金轨迹确定性重放（2.0.6 C0 自证共享实现）。
/// <para>
/// 读取 v3 金轨迹 <c>deterministic_trace_600.txt</c>（与 <c>Horizon.Game.Gateway.Tests/Traces</c>、
/// <c>Horizon.DsReplayHarness</c>、<c>.freebuff/Verify</c> 同源），经 <see cref="SimWorld"/> 直接重放，
/// 输出跨语言可复算的 SHA-256 指纹。UE5 DS <c>SimDriver</c>、Orleans 生产宿主、UE 客户端
/// <c>StartupModule()</c> C0 自证与 <c>Horizon.DsReplayHarness</c> 共用本实现——指纹字节布局
/// 单点定义，杜绝多端漂移；重放路径与 <c>DeterministicReplayParityTests.ReplayDirect</c> 逐字一致。
/// </para>
/// </summary>
public static class GoldenTraceReplay
{
    /// <summary>重放实体（与 DsReplayHarness / DeterministicReplayParityTests 一致）。</summary>
    public const ulong EntityId = 1;

    /// <summary>
    /// v3 黄金指纹（与 DeterministicReplayParityTests.GoldenFingerprint 同源）。
    /// 公式版本变更必须重新生成轨迹（.freebuff/Verify --trace）并同步本常量，否则 CI 自证失败。
    /// </summary>
    public const string V3Fingerprint =
        "1474bd2332c0fead92694db8df5020b585eab649b66ac13db657ca3784718836";

    /// <summary>自证结果。</summary>
    public sealed record Result(
        int FormulaVersion,
        int InputLines,
        long ServerTicks,
        long LastProcessedClientTick,
        string Fingerprint,
        string ExpectedFingerprint,
        bool Pass,
        string? Error);

    /// <summary>构造失败结果（资源缺失 / 重放异常等调用方侧错误）。</summary>
    public static Result Failure(string error)
        => new(0, 0, 0, 0, string.Empty, string.Empty, false, error);

    /// <summary>
    /// 解析并重放金轨迹文本，输出 SHA-256 指纹并与期望指纹比对。
    /// </summary>
    /// <param name="traceText">轨迹文件全文（UTF-8 文本，与 <c>deterministic_trace_600.txt</c> 同格式）。</param>
    /// <param name="expectedFingerprint">期望指纹；缺省 v3 黄金指纹。</param>
    /// <param name="onStep">可选：每 tick 回调（供 harness <c>--dump</c> 逐行收集，不影响指纹）。</param>
    public static Result Verify(
        string traceText,
        string expectedFingerprint = V3Fingerprint,
        Action<long, SimWorld.StepResult>? onStep = null)
    {
        ArgumentNullException.ThrowIfNull(traceText);

        // 1) 头部 FormulaVersion 校验——v3 轨迹必须与当前公式版本一致（防误用旧版轨迹自证）。
        var version = ReadFormulaVersion(traceText);
        if (version != MovementFormula.FormulaVersion)
        {
            return new Result(version, 0, 0, 0, string.Empty, expectedFingerprint, false,
                $"FormulaVersion={version} ≠ MovementFormula.FormulaVersion={MovementFormula.FormulaVersion}"
                + "——轨迹已过期，须重新生成（.freebuff/Verify --trace）。");
        }

        // 2) 解析轨迹（与 DeterministicReplayParityTests.ParseEmbeddedTrace 同格式）。
        List<InputLine> inputs;
        try
        {
            inputs = ParseTrace(traceText);
        }
        catch (FormatException ex)
        {
            return Failure(ex.Message);
        }

        var serverTicks = inputs[^1].ServerTick + 1;
        var (fingerprint, lastTick) = ReplayAndFingerprint(inputs, serverTicks, onStep);
        var pass = string.Equals(fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase);
        return new Result(version, inputs.Count, serverTicks, lastTick, fingerprint, expectedFingerprint, pass, null);
    }

    private sealed class InputLine
    {
        public int ServerTick;
        public InputPacket Input = new();
        public float ReportedEndX, ReportedEndY, ReportedEndZ;
    }

    private static int ReadFormulaVersion(string traceText)
    {
        using var reader = new StringReader(traceText);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!line.StartsWith("# FormulaVersion", StringComparison.Ordinal)) continue;
            var eq = line.IndexOf('=');
            // 数字后可能带注释（"= 3 — 公式版本变更…"），只取首个空白分隔 token。
            var token = eq > 0 ? line[(eq + 1)..].TrimStart().Split(' ', '\t')[0] : "";
            return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : -1;
        }
        return -1;
    }

    private static List<InputLine> ParseTrace(string traceText)
    {
        var inputs = new List<InputLine>();
        using var reader = new StringReader(traceText);
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
        if (inputs.Count == 0)
            throw new FormatException("轨迹文件无输入行");
        return inputs;
    }

    /// <summary>
    /// 直接 SimWorld 重放（UE5 DS SimDriver 的权威宿主路径）→ SHA-256 指纹。
    /// 字节布局与 DeterministicReplayParityTests.AppendRecord 逐字一致：
    /// 每 tick 追加 X/Y/Z/Vz/VelX/VelY 各 4 字节（IEEE-754 位模式 LE）、MovementMode/IsGrounded/HadInput
    /// 各 1 字节、LastProcessedClientTick 8 字节 LE；修正存在标志 1 字节，存在时追加 Reason 1 字节 +
    /// CorrectedX/Y/Z/CorrectedVz/DriftMeters 各 4 字节 + LastProcessedClientTick 8 字节 LE。
    /// </summary>
    private static (string Fingerprint, long LastProcessedClientTick) ReplayAndFingerprint(
        IReadOnlyList<InputLine> inputs,
        long serverTicks,
        Action<long, SimWorld.StepResult>? onStep)
    {
        var world = new SimWorld();
        world.AddOrUpdateEntity(EntityId, 0f, 0f, 0f, maxSpeed: 6f);

        var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long lastTick = 0;
        int next = 0;
        for (long s = 0; s < serverTicks; s++)
        {
            while (next < inputs.Count && inputs[next].ServerTick == s)
            {
                var l = inputs[next++];
                world.SubmitInput(EntityId, l.Input, l.ReportedEndX, l.ReportedEndY, l.ReportedEndZ);
            }
            var step = world.Step(s)[0];
            lastTick = step.LastProcessedClientTick;
            AppendStepToDigest(digest, step);
            onStep?.Invoke(s, step);
        }
        return (Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(), lastTick);
    }

    private static void AppendStepToDigest(IncrementalHash digest, SimWorld.StepResult step)
    {
        AppendFloat(digest, step.AuthoritativeEnd.X);
        AppendFloat(digest, step.AuthoritativeEnd.Y);
        AppendFloat(digest, step.AuthoritativeEnd.Z);
        AppendFloat(digest, step.AuthoritativeVz);
        AppendFloat(digest, step.VelocityX);
        AppendFloat(digest, step.VelocityY);
        digest.AppendData(new[] { (byte)step.MovementMode });
        digest.AppendData(new[] { (byte)(step.IsGrounded ? 1 : 0) });
        digest.AppendData(new[] { (byte)(step.HadInput ? 1 : 0) });
        AppendInt64(digest, step.LastProcessedClientTick);
        if (step.Correction is null)
        {
            digest.AppendData(new byte[] { 0 });
        }
        else
        {
            digest.AppendData(new byte[] { 1 });
            digest.AppendData(new[] { (byte)step.Correction.Reason });
            AppendFloat(digest, step.Correction.CorrectedX);
            AppendFloat(digest, step.Correction.CorrectedY);
            AppendFloat(digest, step.Correction.CorrectedZ);
            AppendFloat(digest, step.Correction.CorrectedVz);
            AppendFloat(digest, step.Correction.DriftMeters);
            AppendInt64(digest, step.Correction.LastProcessedClientTick);
        }
    }

    // 修复（确定性）：改用显式小端编码替代 BitConverter.GetBytes，避免在非小端目标上
    // 指纹字节序随平台变化，兑现"跨语言/跨运行时可复算"承诺（小端机器上指纹不变）。
    private static void AppendFloat(IncrementalHash digest, float v)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, BitConverter.SingleToInt32Bits(v));
        digest.AppendData(buf);
    }

    private static void AppendInt64(IncrementalHash digest, long v)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buf, v);
        digest.AppendData(buf);
    }
}
