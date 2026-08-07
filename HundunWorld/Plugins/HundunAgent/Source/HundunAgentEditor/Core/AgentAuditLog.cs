using System;
using System.IO;
using System.Text.Json;

namespace HundunAgent.Core
{
    /// <summary>
    /// Agent 工具调用审计日志：全部调用写入 Logs/HundunAgent/tools-yyyyMMdd.jsonl。
    /// </summary>
    public static class AgentAuditLog
    {
        private static readonly object _lock = new object();
        private static string _currentDate;
        private static StreamWriter _writer;

        public static void Write(string tool, JsonElement args, bool success, string error, double elapsedMs)
        {
            try
            {
                var record = new
                {
                    time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    tool,
                    success,
                    error,
                    elapsedMs,
                    args = Truncate(args)
                };

                var line = JsonSerializer.Serialize(record);

                lock (_lock)
                {
                    EnsureWriter();
                    _writer.WriteLine(line);
                    _writer.Flush();
                }
            }
            catch
            {
                // 审计日志失败不影响工具执行
            }
        }

        private static string Truncate(JsonElement args)
        {
            string raw;
            try
            {
                raw = args.ValueKind == JsonValueKind.Undefined ? "{}" : args.GetRawText();
            }
            catch
            {
                raw = "{}";
            }

            return raw.Length > 2000 ? raw.Substring(0, 2000) + "...(truncated)" : raw;
        }

        private static void EnsureWriter()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            if (_writer != null && _currentDate == today)
                return;

            try { _writer?.Dispose(); } catch { }

            var dir = Path.Combine(FlaxEngine.Globals.ProjectFolder, "Logs", "HundunAgent");
            Directory.CreateDirectory(dir);

            _currentDate = today;
            _writer = new StreamWriter(
                new FileStream(Path.Combine(dir, "tools-" + today + ".jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = false
            };
        }
    }
}
