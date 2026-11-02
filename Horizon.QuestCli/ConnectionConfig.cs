using System;
using System.Collections.Generic;
using System.IO;

namespace Horizon.QuestCli;

/// <summary>
/// 连接串解析：环境变量优先，其次就近找服务自己的 <c>.env</c>。
/// <para>
/// 仓库根没有 <c>.env</c>，真实凭据只存在于各服务的 <c>bin/Debug/net10.0/.env</c> 里
/// （Silo 按 BaseDirectory 加载它）。EF 设计时路径不展开 <c>${VAR}</c> 占位符，
/// 所以这里必须自己把值取出来，而不是复用 <c>repository.json</c>。
/// </para>
/// </summary>
public static class ConnectionConfig
{
    /// <summary>环境变量名（与 Silo/Gateway 用同一份约定）。</summary>
    public const string EnvVarName = "DB_GAME_CONNECTION_STRING";

    /// <summary>取 Game 库连接串；取不到时返回 null 并给出可操作的提示。</summary>
    public static string? Resolve(out string hint)
    {
        hint = string.Empty;

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;

        var root = FindRepositoryRoot();
        if (root == null)
        {
            hint = "没找到仓库根（上级目录里没有 Horizon.sln），也没有 " + EnvVarName + " 环境变量。";
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(root, ".env"),
            Path.Combine(root, "Horizon.Orleans.Silo", "bin", "Debug", "net10.0", ".env"),
            Path.Combine(root, "Horizon.Game.Gateway", "bin", "Debug", "net10.0", ".env"),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            var value = ReadEnvValue(path, EnvVarName);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        hint = "以下位置都没有 " + EnvVarName + "：" + string.Join(" | ", candidates) + "。先跑过一次 Silo 或显式设置该环境变量。";
        return null;
    }

    /// <summary>
    /// Redis 连接串的**候选清单**（按优先级），供草稿层逐个尝试。
    /// <para>
    /// 为什么要一串而不是只认一个环境变量：编辑器把 CLI 当子进程拉起，子进程的环境里
    /// **没有**任何 <c>HORIZON_*</c> 变量（现场就是"未设置 HORIZON_QUEST_REDIS / HORIZON_ORLEANS_REDIS，
    /// 草稿层不可用"）。而凭据其实一直躺在各服务的 <c>.env</c> 里（DB 连接串走的就是这条路），
    /// 所以这里把 <c>.env</c> 里的 <c>GATEWAY_REDIS_CONNECTION_STRING</c> 也接进来，
    /// 最后再补一个本机 dev 兜底（只在本机能连上时才成立，见调用方的 PING 判定）。
    /// </para>
    /// </summary>
    public static IEnumerable<(string ConnectionString, string Origin)> RedisCandidates()
    {
        var environmentKeys = new[]
        {
            "HORIZON_QUEST_REDIS",
            "HORIZON_ORLEANS_REDIS",
            "GATEWAY_REDIS_CONNECTION_STRING",
            "SILO_REDIS_CONNECTION_STRING",
            "REDIS_CONNECTION_STRING",
        };

        foreach (var key in environmentKeys)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value)) yield return (value.Trim(), "环境变量 " + key);
        }

        var root = FindRepositoryRoot();
        if (root != null)
        {
            var files = new[]
            {
                Path.Combine(root, ".env"),
                Path.Combine(root, "Horizon.Orleans.Silo", "bin", "Debug", "net10.0", ".env"),
                Path.Combine(root, "Horizon.Game.Gateway", "bin", "Debug", "net10.0", ".env"),
            };

            foreach (var path in files)
            {
                if (!File.Exists(path)) continue;
                // 标签用相对仓库根的路径：只显示 net10.0 的话，"这串是从哪个服务那儿借来的"看不出来。
                var label = path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    ? path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, '/')
                    : path;
                foreach (var key in environmentKeys)
                {
                    var value = ReadEnvValue(path, key);
                    if (!string.IsNullOrWhiteSpace(value))
                        yield return (value.Trim(), label + " 的 " + key);
                }
            }
        }

        // 本机 dev 兜底：默认端口 9379（与 .env 里的 REDIS_HOST 一致）。
        yield return ("127.0.0.1:9379,abortConnect=false", "本机 dev 兜底");
    }

    /// <summary>从当前目录逐级上溯找仓库根。</summary>
    public static string? FindRepositoryRoot()    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory != null && depth < 12; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Horizon.sln"))) return directory.FullName;
        }

        // 从 exe 位置找不到时（例如被复制到 Temp 跑），退回当前工作目录。
        directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var depth = 0; directory != null && depth < 12; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Horizon.sln"))) return directory.FullName;
        }

        return null;
    }

    private static string? ReadEnvValue(string path, string key)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            if (!string.Equals(line.Substring(0, equals).Trim(), key, StringComparison.OrdinalIgnoreCase)) continue;

            var value = line.Substring(equals + 1).Trim();
            if (value.Length >= 2 && ((value.StartsWith("\"") && value.EndsWith("\"")) || (value.StartsWith("'") && value.EndsWith("'"))))
                value = value.Substring(1, value.Length - 2);
            return value;
        }

        return null;
    }
}
