using Horizon.Game.Core.Security;
using Horizon.Game.Message.Security;
using System;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 脚本化世界生成的**上线自检**测试。
/// <para>
/// 这条链路要三个进程同开关、同密钥才工作，最常见的故障不是逻辑错，而是"某个进程少配一项"。
/// 本组测试锁住两件事：故障必须被启动日志说中（而不是表现为莫名的 signature-mismatch），
/// 以及环境变量的权威名与旧名回退顺序确定。
/// </para>
/// </summary>
[Collection(ScriptedSpawnEnvironmentCollection.Name)]
public class ScriptedSpawnDiagnosticsTests
{
    private static readonly string KeyA = Convert.ToBase64String(new byte[32]
    {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
    });

    private static readonly string KeyB = Convert.ToBase64String(new byte[32]
    {
        32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17,
        16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1,
    });

    [Fact]
    public void 密钥指纹稳定且能区分不同密钥()
    {
        var a1 = ScriptGrantTokenSigner.KeyFingerprint(KeyA);
        var a2 = ScriptGrantTokenSigner.KeyFingerprint(KeyA);
        var b1 = ScriptGrantTokenSigner.KeyFingerprint(KeyB);

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b1);
        // 4 字节十六进制 = 8 字符，够肉眼比对，又不会把密钥信息带进日志。
        Assert.Equal(8, a1.Length);
        Assert.Matches("^[0-9A-F]{8}$", a1);
    }

    [Fact]
    public void 密钥指纹与外部工具口径一致()
    {
        // scripts/scripted-spawn-preflight.ps1（PowerShell SHA256）与 openssl dgst -sha256 对同一把
        // 密钥都给出 D7A8A7DB。把第三方实现的结果钉进断言，才能保证"日志里的指纹"和
        // "自检脚本里的指纹"是同一个数——否则三进程比对这件事本身就是错的。
        Assert.Equal("D7A8A7DB", ScriptGrantTokenSigner.KeyFingerprint("Wi5eQTMy"));
    }

    [Fact]
    public void 缺失与非法密钥各自给出可区分的字面()
    {
        Assert.Equal("(missing)", ScriptGrantTokenSigner.KeyFingerprint(null));
        Assert.Equal("(missing)", ScriptGrantTokenSigner.KeyFingerprint(""));
        Assert.Equal("(missing)", ScriptGrantTokenSigner.KeyFingerprint("   "));
        Assert.Equal("(not-base64)", ScriptGrantTokenSigner.KeyFingerprint("这不是base64!"));
    }

    [Fact]
    public void 关闭态判为告警并点名该配的环境变量()
    {
        var (severity, message) = ScriptedSpawnDiagnostics.BuildStartupReport(
            "Grain(签发)", new ScriptedSpawnOptions { Enabled = false, SigningKeyBase64 = KeyA });

        Assert.Equal(ScriptedSpawnDiagnostics.StartupSeverity.Warn, severity);
        Assert.Contains("未开启", message);
        Assert.Contains(ScriptedSpawnDiagnostics.EnvEnabled, message);
        Assert.Contains(ScriptedSpawnDiagnostics.EnvSigningKey, message);
    }

    [Fact]
    public void 开启但无密钥判为错误且明确是fail_closed()
    {
        var (severity, message) = ScriptedSpawnDiagnostics.BuildStartupReport(
            "DS(验签)", new ScriptedSpawnOptions { Enabled = true, SigningKeyBase64 = "" });

        Assert.Equal(ScriptedSpawnDiagnostics.StartupSeverity.Error, severity);
        Assert.Contains("(missing)", message);
        Assert.Contains("fail-closed", message);
    }

    [Fact]
    public void 就绪态判为信息并要求三进程比对指纹()
    {
        var (severity, message) = ScriptedSpawnDiagnostics.BuildStartupReport(
            "网关(令牌自检)", new ScriptedSpawnOptions { Enabled = true, SigningKeyBase64 = KeyA });

        Assert.Equal(ScriptedSpawnDiagnostics.StartupSeverity.Info, severity);
        Assert.Contains(ScriptGrantTokenSigner.KeyFingerprint(KeyA), message);
        Assert.Contains("signature-mismatch", message);
    }

    [Fact]
    public void 启动自检不会把密钥本体写进日志()
    {
        var (_, message) = ScriptedSpawnDiagnostics.BuildStartupReport("Grain(签发)",
            new ScriptedSpawnOptions { Enabled = true, SigningKeyBase64 = KeyA });

        Assert.DoesNotContain(KeyA, message, StringComparison.Ordinal);
    }

    [Fact]
    public void 环境变量优先且旧名仅作回退()
    {
        var (canonicalEnabled, legacyEnabled, canonicalKey, legacyKey) = (
            ScriptedSpawnDiagnostics.EnvEnabled,
            ScriptedSpawnDiagnostics.LegacyEnvEnabled,
            ScriptedSpawnDiagnostics.EnvSigningKey,
            ScriptedSpawnDiagnostics.LegacyEnvSigningKey);

        var snapshot = CaptureEnv(canonicalEnabled, legacyEnabled, canonicalKey, legacyKey);
        try
        {
            // 只有旧名时仍可用（既有启动脚本不失效）。
            ClearEnv(canonicalEnabled, canonicalKey);
            Environment.SetEnvironmentVariable(legacyEnabled, "1");
            Environment.SetEnvironmentVariable(legacyKey, KeyB);
            var fromLegacy = ScriptedSpawnDiagnostics.FromEnvironment();
            Assert.True(fromLegacy.Enabled);
            Assert.Equal(KeyB, fromLegacy.SigningKeyBase64);

            // 两名同时存在时，权威名赢。
            Environment.SetEnvironmentVariable(canonicalEnabled, "true");
            Environment.SetEnvironmentVariable(canonicalKey, KeyA);
            var canonicalWins = ScriptedSpawnDiagnostics.FromEnvironment();
            Assert.True(canonicalWins.Enabled);
            Assert.Equal(KeyA, canonicalWins.SigningKeyBase64);

            // 权威名显式关闭时不再受旧名"1"影响（开关必须只有一个真值源）。
            Environment.SetEnvironmentVariable(canonicalEnabled, "0");
            Assert.False(ScriptedSpawnDiagnostics.FromEnvironment().Enabled);

            // 一个都不设 = 关闭态（灰度安全）。
            ClearEnv(canonicalEnabled, legacyEnabled);
            Assert.False(ScriptedSpawnDiagnostics.FromEnvironment().Enabled);
        }
        finally
        {
            RestoreEnv(snapshot);
        }
    }

    private static (string? CanonicalEnabled, string? LegacyEnabled, string? CanonicalKey, string? LegacyKey)
        CaptureEnv(params string[] names)
    {
        var values = new string?[names.Length];
        for (var i = 0; i < names.Length; i++) values[i] = Environment.GetEnvironmentVariable(names[i]);
        return (values[0], values[1], values[2], values[3]);
    }

    private static void ClearEnv(params string[] names)
    {
        foreach (var name in names) Environment.SetEnvironmentVariable(name, null);
    }

    private static void RestoreEnv(
        (string? CanonicalEnabled, string? LegacyEnabled, string? CanonicalKey, string? LegacyKey) snapshot)
    {
        Environment.SetEnvironmentVariable(ScriptedSpawnDiagnostics.EnvEnabled, snapshot.CanonicalEnabled);
        Environment.SetEnvironmentVariable(ScriptedSpawnDiagnostics.LegacyEnvEnabled, snapshot.LegacyEnabled);
        Environment.SetEnvironmentVariable(ScriptedSpawnDiagnostics.EnvSigningKey, snapshot.CanonicalKey);
        Environment.SetEnvironmentVariable(ScriptedSpawnDiagnostics.LegacyEnvSigningKey, snapshot.LegacyKey);
    }
}
