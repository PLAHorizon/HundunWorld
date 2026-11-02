using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Horizon.Game.Core.Security
{
    /// <summary>
    /// 脚本化世界生成配置的**唯一绑定入口**（网关与 Silo 都用它；DS 走
    /// <see cref="ScriptedSpawnDiagnostics.FromEnvironment"/>）。
    /// <para>
    /// <b>为什么不能直接用 <c>GetSection(...).Get&lt;ScriptedSpawnOptions&gt;()</c></b>：
    /// 实测 <c>ScriptedSpawn__Enabled=1</c>（也就是文档里让运营设的那个字面量）会让配置绑定器
    /// 抛 <c>InvalidOperationException: Failed to convert configuration value '1' at
    /// 'ScriptedSpawn:Enabled' to type System.Boolean</c> —— 而这段代码位于
    /// <c>ConfigureServices</c> 里，抛出的后果是**网关与 Silo 启动失败**，不是"开关没生效"。
    /// 一个数字字面量能把两个进程打死，这个风险不能留给运气。
    /// </para>
    /// <para>
    /// 因此这里逐键手工绑定：开关用与 DS 同一套字面量口径（<c>1/true/on/yes/enabled</c>、
    /// <c>0/false/off/no/disabled</c>），数值键解析失败就保留默认值并原样交给启动自检出面，
    /// **任何配置笔误都不会让进程起不来**。
    /// </para>
    /// </summary>
    public static class ScriptedSpawnConfiguration
    {
        /// <summary>
        /// 绑定配置节；读不到或读不懂的键保持 <see cref="ScriptedSpawnOptions"/> 的类内默认值。
        /// </summary>
        /// <param name="configuration">宿主配置（须已 <c>AddEnvironmentVariables()</c>）。</param>
        /// <param name="rawEnabledValue">开关的原始字面量（<c>null</c> = 完全没配），供启动日志如实表述。</param>
        public static ScriptedSpawnOptions Bind(IConfiguration? configuration, out string? rawEnabledValue)
        {
            var options = new ScriptedSpawnOptions();
            var section = configuration?.GetSection(ScriptedSpawnOptions.SectionName);

            var fromSection = section?[nameof(ScriptedSpawnOptions.Enabled)];
            var fromEnv = FirstNonEmpty(
                Environment.GetEnvironmentVariable(ScriptedSpawnDiagnostics.EnvEnabled),
                Environment.GetEnvironmentVariable(ScriptedSpawnDiagnostics.LegacyEnvEnabled));
            rawEnabledValue = string.IsNullOrWhiteSpace(fromSection) ? fromEnv?.Trim() : fromSection?.Trim();

            // 没配就保持默认（关闭）——灰度安全；配了但不认识，也保持默认而不是崩。
            if (TryParseSwitch(rawEnabledValue, out var enabled)) options.Enabled = enabled;

            if (section == null) return options;

            options.SigningKeyBase64 = FirstNonEmpty(
                section[nameof(ScriptedSpawnOptions.SigningKeyBase64)],
                Environment.GetEnvironmentVariable(ScriptedSpawnDiagnostics.EnvSigningKey),
                Environment.GetEnvironmentVariable(ScriptedSpawnDiagnostics.LegacyEnvSigningKey))?.Trim()
                ?? string.Empty;

            options.TokenTtlSeconds = ReadInt(section, nameof(ScriptedSpawnOptions.TokenTtlSeconds), options.TokenTtlSeconds);
            options.CooldownSeconds = ReadInt(section, nameof(ScriptedSpawnOptions.CooldownSeconds), options.CooldownSeconds);
            options.QuotaPerMinute = ReadInt(section, nameof(ScriptedSpawnOptions.QuotaPerMinute), options.QuotaPerMinute);
            options.MaxCountPerRequest = ReadInt(section, nameof(ScriptedSpawnOptions.MaxCountPerRequest), options.MaxCountPerRequest);
            options.MaxDistanceMetres = ReadFloat(section, nameof(ScriptedSpawnOptions.MaxDistanceMetres), options.MaxDistanceMetres);
            options.MaxScriptedEntitiesPerShard = ReadInt(section, nameof(ScriptedSpawnOptions.MaxScriptedEntitiesPerShard), options.MaxScriptedEntitiesPerShard);
            options.MaxTriggerZonesPerShard = ReadInt(section, nameof(ScriptedSpawnOptions.MaxTriggerZonesPerShard), options.MaxTriggerZonesPerShard);
            options.MaxTriggerRadiusMetres = ReadFloat(section, nameof(ScriptedSpawnOptions.MaxTriggerRadiusMetres), options.MaxTriggerRadiusMetres);
            options.MaxTriggerTtlSeconds = ReadInt(section, nameof(ScriptedSpawnOptions.MaxTriggerTtlSeconds), options.MaxTriggerTtlSeconds);
            options.MaxSpawnTtlSeconds = ReadInt(section, nameof(ScriptedSpawnOptions.MaxSpawnTtlSeconds), options.MaxSpawnTtlSeconds);
            options.BootstrapAdminIds = ReadIdArray(section, nameof(ScriptedSpawnOptions.BootstrapAdminIds));

            return options;
        }

        /// <summary>与 <see cref="ScriptedSpawnDiagnostics.FromEnvironment"/> 共用的开关字面量口径。</summary>
        public static bool TryParseSwitch(string? raw, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            switch (raw.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "on":
                case "yes":
                case "enabled":
                    value = true;
                    return true;
                case "0":
                case "false":
                case "off":
                case "no":
                case "disabled":
                    value = false;
                    return true;
                default:
                    return false;
            }
        }

        private static int ReadInt(IConfigurationSection section, string key, int fallback)
            => int.TryParse(section[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        private static float ReadFloat(IConfigurationSection section, string key, float fallback)
            => float.TryParse(section[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        /// <summary>
        /// 引导管理员 ID 数组：同时支持 <c>ScriptedSpawn:BootstrapAdminIds__0</c> 这种
        /// 环境变量/扁平写法，以及 appsettings 里的 JSON 数组。解析不出来的元素直接跳过而不是抛。
        /// </summary>
        private static long[] ReadIdArray(IConfigurationSection section, string key)
        {
            var children = section.GetSection(key).GetChildren().ToList();
            var values = new List<long>(children.Count);
            foreach (var child in children)
            {
                if (long.TryParse(child.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                    values.Add(id);
            }

            return values.ToArray();
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return null;
        }
    }
}
