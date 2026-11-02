using System;
using Horizon.Game.Message.Security;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core.Security
{
    /// <summary>
    /// 脚本化世界生成（DS 怪物 / NPC / 特殊事件）的**上线自检**。
    /// <para>
    /// 存在的理由：这条链路由三个进程共配才能工作——Grain 签发、网关自检、DS 验签，
    /// 三处读同一开关与同一 HMAC 密钥。任一进程少配，表现分别是"指令一律被拒"与
    /// "验签一律 signature-mismatch"，日志里都看不出根因。本类把有效配置在每个进程
    /// 启动时打成一行人可读的判定，并给出对应的修法。
    /// </para>
    /// </summary>
    public static class ScriptedSpawnDiagnostics
    {
        /// <summary>
        /// 开关的环境变量名（.NET 配置系统的分节符写法：<c>节名__键名</c>）。
        /// <para>
        /// 网关与 Silo 走 <c>Host.CreateDefaultBuilder</c> / <c>AddEnvironmentVariables</c>，
        /// 天然识别本名字；DS 宿主不加载 appsettings，故由本类显式读取，使**同一对变量能注入三个进程**。
        /// </para>
        /// </summary>
        public const string EnvEnabled = "ScriptedSpawn__Enabled";

        /// <summary>HMAC 密钥的环境变量名（Base64）。三进程必须同值，否则验签全失败。</summary>
        public const string EnvSigningKey = "ScriptedSpawn__SigningKeyBase64";

        /// <summary>旧名兼容：早期 DS 宿主只读这一对，保留以免既有启动脚本失效。</summary>
        public const string LegacyEnvEnabled = "HZN_SCRIPTED_SPAWN";

        /// <summary>旧名兼容，见 <see cref="LegacyEnvEnabled"/>。</summary>
        public const string LegacyEnvSigningKey = "HZN_SCRIPT_SIGNING_KEY_B64";

        /// <summary>
        /// 从环境构造配置（DS 宿主用）。appsettings 不参与：密钥不应落盘进仓库。
        /// <para>先读 <see cref="EnvEnabled"/>/<see cref="EnvSigningKey"/>，缺失再回退旧名。</para>
        /// </summary>
        public static ScriptedSpawnOptions FromEnvironment(ScriptedSpawnOptions? fallback = null)
        {
            var options = fallback ?? new ScriptedSpawnOptions();

            var enabledRaw = ReadFirstNonEmpty(EnvEnabled, LegacyEnvEnabled);
            if (!string.IsNullOrWhiteSpace(enabledRaw)
                && ScriptedSpawnConfiguration.TryParseSwitch(enabledRaw, out var enabled))
            {
                options.Enabled = enabled;
            }

            var keyRaw = ReadFirstNonEmpty(EnvSigningKey, LegacyEnvSigningKey);
            if (!string.IsNullOrWhiteSpace(keyRaw)) options.SigningKeyBase64 = keyRaw.Trim();

            return options;
        }

        private static string? ReadFirstNonEmpty(params string[] names)
        {
            foreach (var name in names)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return null;
        }

        /// <summary>
        /// 打印本进程的有效配置与下一步判定。<paramref name="role"/> 取 "Grain(签发)" / "网关(令牌自检)" / "DS(验签)"。
        /// </summary>
        public static void LogStartup(ILogger? logger, string role, ScriptedSpawnOptions options)
        {
            if (logger == null) return;
            var (severity, message) = BuildStartupReport(role, options);
            switch (severity)
            {
                case StartupSeverity.Error: logger.LogError("{Message}", message); break;
                case StartupSeverity.Warn: logger.LogWarning("{Message}", message); break;
                default: logger.LogInformation("{Message}", message); break;
            }
        }

        /// <summary>
        /// 无 <c>ILogger</c> 的宿主（DS 用行缓冲的控制台日志）用的同款自检。
        /// </summary>
        public static void LogStartup(Action<string>? sink, string role, ScriptedSpawnOptions options)
        {
            if (sink == null) return;
            var (_, message) = BuildStartupReport(role, options);
            sink(message);
        }

        /// <summary>启动自检的判定档位（决定日志级别）。</summary>
        public enum StartupSeverity
        {
            Info,
            Warn,
            Error,
        }

        /// <summary>组装启动自检文本；独立成方法是为了可被单测断言（三态各一条）。</summary>
        public static (StartupSeverity Severity, string Message) BuildStartupReport(string role, ScriptedSpawnOptions options)
        {
            options ??= new ScriptedSpawnOptions();
            var fingerprint = ScriptGrantTokenSigner.KeyFingerprint(options.SigningKeyBase64);
            var summary = $"[ScriptedSpawn] {role}：Enabled={options.Enabled} 密钥指纹={fingerprint} "
                + $"配额={options.QuotaPerMinute}/分钟 冷却={options.CooldownSeconds}s "
                + $"单次上限={options.MaxCountPerRequest} 距离上限={options.MaxDistanceMetres}m "
                + $"单shard脚本实体上限={options.MaxScriptedEntitiesPerShard} "
                + $"引导管理员数={options.BootstrapAdminIds?.Length ?? 0}";

            if (!options.Enabled)
            {
                return (StartupSeverity.Warn,
                    summary + $"。功能处于关闭态：全部指令会被 Grain 以『脚本化生成功能未开启』拒绝。"
                    + $"开启方式：在启动 {role} 的会话里设置 {EnvEnabled}=1 且 {EnvSigningKey}=<Base64 密钥>，"
                    + "三个进程（Grain/网关/DS）必须同值。");
            }

            if (fingerprint is "(missing)" or "(not-base64)")
            {
                return (StartupSeverity.Error,
                    summary + $"。开关已开但密钥不可用（{fingerprint}）：本进程一律 fail-closed。"
                    + $"请设置 {EnvSigningKey}=<Base64 密钥>；{role} 重启后才能生效。");
            }

            return (StartupSeverity.Info,
                summary + "。请核对 Grain(签发) / 网关(自检) / DS(验签) 三行日志的密钥指纹完全一致，"
                + "不一致的一侧会把所有令牌判为 signature-mismatch。");
        }
    }
}
