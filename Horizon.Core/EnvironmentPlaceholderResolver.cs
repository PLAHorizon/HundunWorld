using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Horizon.Core
{
    /// <summary>
    /// 环境变量占位符解析器
    /// 加载 .env 文件到环境变量（不覆盖系统环境变量），并把配置中的 ${VAR} 占位符解析为实际值，
    /// 兑现 .env.example 的"${VAR} 由此注入"约定。
    /// </summary>
    public static class EnvironmentPlaceholderResolver
    {
        private static readonly Regex PlaceholderRegex = new Regex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

        /// <summary>
        /// 从指定目录加载 .env 文件到环境变量。
        /// 若变量已存在于系统环境变量中，则不覆盖（保留系统变量优先级）。
        /// 支持 # 注释、空行，以及 "KEY=VALUE" 与 "KEY=..." 两种形式。
        /// </summary>
        /// <param name="basePath">查找 .env 文件的目录</param>
        public static void LoadDotEnvFile(string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath)) return;

            var envPath = Path.Combine(basePath, ".env");
            if (!File.Exists(envPath)) return;

            foreach (var rawLine in File.ReadAllLines(envPath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                var eqIndex = line.IndexOf('=');
                if (eqIndex <= 0) continue;

                var key = line.Substring(0, eqIndex).Trim();
                var value = line.Substring(eqIndex + 1).Trim();

                // 去除可能存在的引号（保留引号内内容，便于密码含特殊字符）
                if (value.Length >= 2)
                {
                    if ((value[0] == '"' && value[value.Length - 1] == '"')
                        || (value[0] == '\'' && value[value.Length - 1] == '\''))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                }

                if (string.IsNullOrWhiteSpace(key)) continue;

                // 不覆盖已存在的系统环境变量
                if (Environment.GetEnvironmentVariable(key) != null) continue;

                Environment.SetEnvironmentVariable(key, value);
            }
        }

        /// <summary>
        /// 遍历配置中的所有键值，将值中的 ${VAR} 占位符替换为环境变量中的实际值。
        /// 返回的集合可直接用于 AddInMemoryCollection（优先级最高）。
        /// </summary>
        /// <param name="configuration">已完成构建的配置对象</param>
        /// <returns>解析后的键值覆盖集合；未解析成功的占位符保留原样</returns>
        public static IEnumerable<KeyValuePair<string, string?>> BuildResolvedOverrides(IConfiguration configuration)
        {
            var result = new List<KeyValuePair<string, string?>>();
            if (configuration == null) return result;

            foreach (var section in configuration.AsEnumerable())
            {
                var value = section.Value;
                if (string.IsNullOrEmpty(value) || !value.Contains("${")) continue;

                result.Add(new KeyValuePair<string, string?>(section.Key, ResolvePlaceholders(value)));
            }

            return result;
        }

        /// <summary>
        /// 把单个字符串中的 ${VAR} 占位符解析为环境变量值；未定义的变量保留原占位符。
        /// </summary>
        public static string ResolvePlaceholders(string input)
        {
            if (string.IsNullOrEmpty(input) || !input.Contains("${")) return input;

            return PlaceholderRegex.Replace(input, match =>
            {
                var envName = match.Groups[1].Value;
                var envValue = Environment.GetEnvironmentVariable(envName);
                return string.IsNullOrEmpty(envValue) ? match.Value : envValue;
            });
        }
    }
}
