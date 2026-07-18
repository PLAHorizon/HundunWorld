// #region debug-point debug-reporter
// 临时调试上报器（TRAE-debugger 会话：despawn-not-reaching-late-joiner）
// 会话结束后删除本文件。
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// 临时调试日志上报器：把关键诊断日志通过 HTTP POST 上报到 TRAE-debugger Debug Server。
/// 会话结束后删除本类及其调用点。
/// </summary>
public static class DebugReporter
{
    private const string DefaultUrl = "http://127.0.0.1:7777/event";
    private const string DefaultSessionId = "despawn-not-reaching-late-joiner";
    private const string EnvFilePath = ".dbg/despawn-not-reaching-late-joiner.env";

    private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    private static string? _resolvedUrl;
    private static string? _resolvedSessionId;

    private static string Url => _resolvedUrl ??= ResolveFromEnv("DEBUG_SERVER_URL") ?? DefaultUrl;
    private static string SessionId => _resolvedSessionId ??= ResolveFromEnv("DEBUG_SESSION_ID") ?? DefaultSessionId;

    private static string? ResolveFromEnv(string key)
    {
        try
        {
            if (!File.Exists(EnvFilePath)) return null;
            var lines = File.ReadAllLines(EnvFilePath);
            foreach (var line in lines)
            {
                if (line.StartsWith(key + "=", StringComparison.Ordinal))
                {
                    return line.Substring(key.Length + 1).Trim();
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 上报一条调试日志到 Debug Server（fire-and-forget，不抛异常）。
    /// </summary>
    /// <param name="hypothesisId">假设 ID（A/B/C/D/E）</param>
    /// <param name="location">代码位置（file:line）</param>
    /// <param name="msg">消息（带 [DEBUG] 前缀）</param>
    /// <param name="data">结构化数据</param>
    public static async void Report(string hypothesisId, string location, string msg, object? data = null)
    {
        try
        {
            var payload = new
            {
                sessionId = SessionId,
                runId = "pre",
                hypothesisId,
                location,
                msg,
                data = data ?? new { },
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _client.PostAsync(Url, content).ConfigureAwait(false);
        }
        catch
        {
            // 静默吞掉上报异常，避免影响业务逻辑
        }
    }
}
// #endregion
