using System;
using System.Collections.Generic;
using System.IO;

namespace Horizon.Entities;

/// <summary>
/// 设计时连接串解析：把 <c>repository.json</c> 里的 <c>${VAR}</c> 占位符展开成真实值。
/// <para>
/// 为什么需要它：迁移工具在设计时走 <c>GameEntityContextDes.OnConfiguring</c>，那里只读
/// <c>repository.json</c>；而仓库里那份文件写的是 <c>${DB_GAME_CONNECTION_STRING}</c>。
/// 不展开就会把占位符原样交给 SqlClient，报"初始化字符串的格式不符合规范"——
/// 表现是"迁移工具根本连不上库"，而不是配置少了一项。
/// </para>
/// <para>
/// 取值顺序：进程环境变量 → 就近的 <c>.env</c>（仓库根、Silo/Gateway 的 bin，按顺序找）。
/// 仍取不到时返回原串，让调用方看到带占位符的错误（不静默连到别的库）。
/// </para>
/// </summary>
public static class DesignTimeConnection
{
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.Ordinal);

    /// <summary>展开连接串里的 <c>${VAR}</c>（可多段）。</summary>
    public static string Expand(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;

        var result = raw;
        var start = result.IndexOf("${", StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = result.IndexOf('}', start + 2);
            if (end < 0) break;

            var name = result.Substring(start + 2, end - start - 2).Trim();
            var value = ResolveVariable(name);
            if (string.IsNullOrEmpty(value)) return raw;   // 展开失败：原样返回，让错误可见

            result = result[..start] + value + result[(end + 1)..];
            start = result.IndexOf("${", StringComparison.Ordinal);
        }

        return result;
    }

    private static string? ResolveVariable(string name)
    {
        var fromEnv = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

        if (Cache.TryGetValue(name, out var cached)) return cached;

        foreach (var path in EnvFileCandidates())
        {
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadAllLines(path))
            {
                var text = line.Trim();
                if (text.Length == 0 || text[0] == '#') continue;
                var eq = text.IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(text[..eq].Trim(), name, StringComparison.Ordinal)) continue;

                var value = text[(eq + 1)..].Trim().Trim('"');
                Cache[name] = value;
                return value;
            }
        }

        Cache[name] = null;
        return null;
    }

    private static IEnumerable<string> EnvFileCandidates()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        // 先就近（cwd 的 .env），再向上找仓库根（含 Horizon.sln），最后探两个宿主的 bin。
        var current = dir;
        for (var depth = 0; current != null && depth < 6; depth++, current = current.Parent)
        {
            yield return Path.Combine(current.FullName, ".env");
            if (File.Exists(Path.Combine(current.FullName, "Horizon.sln")))
            {
                var root = current.FullName;
                yield return Path.Combine(root, "Horizon.Orleans.Silo", "bin", "Debug", "net10.0", ".env");
                yield return Path.Combine(root, "Horizon.Game.Gateway", "bin", "Debug", "net10.0", ".env");
                yield break;
            }
        }
    }
}
