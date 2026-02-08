using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Horizon.Orleans.Silo.Monitoring
{
    /// <summary>
    /// Seq日志配置选项
    /// 用于开发环境的日志聚合与查询
    /// </summary>
    public class SeqLoggingOptions
    {
        /// <summary>
        /// Seq服务器URL（例如: http://localhost:5341）
        /// </summary>
        public string ServerUrl { get; set; } = "http://localhost:5341";

        /// <summary>
        /// Seq API密钥（可选）
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// 是否启用Seq日志
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 批量发送间隔（秒）
        /// </summary>
        public int BatchIntervalSeconds { get; set; } = 2;

        /// <summary>
        /// 每批最大事件数
        /// </summary>
        public int MaxBatchSize { get; set; } = 100;
    }

    /// <summary>
    /// Seq日志提供程序
    /// 基于CLEF (Compact Log Event Format) 将日志事件批量发送到Seq社区版
    /// </summary>
    public sealed class SeqLoggerProvider : ILoggerProvider
    {
        private readonly SeqLoggingOptions _options;
        private readonly ConcurrentDictionary<string, SeqLogger> _loggers = new();
        private readonly ConcurrentQueue<string> _eventQueue = new();
        private readonly HttpClient _httpClient;
        private readonly Timer _flushTimer;
        private readonly string _serviceName;
        private bool _disposed;

        public SeqLoggerProvider(SeqLoggingOptions options, string serviceName = "HundunWorld.Silo")
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _serviceName = serviceName;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(5)
            };

            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-Seq-ApiKey", options.ApiKey);
            }

            _flushTimer = new Timer(
                _ => _ = FlushAsync(),
                null,
                TimeSpan.FromSeconds(options.BatchIntervalSeconds),
                TimeSpan.FromSeconds(options.BatchIntervalSeconds));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new SeqLogger(name, _eventQueue, _serviceName));
        }

        private async Task FlushAsync()
        {
            if (_eventQueue.IsEmpty) return;

            var batch = new StringBuilder();
            var count = 0;

            while (count < _options.MaxBatchSize && _eventQueue.TryDequeue(out var eventJson))
            {
                batch.AppendLine(eventJson);
                count++;
            }

            if (count == 0) return;

            try
            {
                var content = new StringContent(batch.ToString(), Encoding.UTF8, "application/vnd.serilog.clef");
                var response = await _httpClient.PostAsync("api/events/raw?clef", content);
                // Silently ignore failures to avoid recursive logging
            }
            catch
            {
                // Silently ignore - we don't want logging failures to crash the app
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _flushTimer.Dispose();
            _ = FlushAsync(); // Final flush
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// Seq日志记录器实例
    /// 将日志事件序列化为CLEF格式并排入发送队列
    /// </summary>
    internal sealed class SeqLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly ConcurrentQueue<string> _eventQueue;
        private readonly string _serviceName;

        public SeqLogger(string categoryName, ConcurrentQueue<string> eventQueue, string serviceName)
        {
            _categoryName = categoryName;
            _eventQueue = eventQueue;
            _serviceName = serviceName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var clefEvent = new Dictionary<string, object?>
            {
                ["@t"] = DateTime.UtcNow.ToString("O"),
                ["@l"] = MapLogLevel(logLevel),
                ["@mt"] = formatter(state, null),
                ["SourceContext"] = _categoryName,
                ["Service"] = _serviceName
            };

            if (exception != null)
            {
                clefEvent["@x"] = exception.ToString();
            }

            if (eventId.Id != 0)
            {
                clefEvent["EventId"] = eventId.Id;
                if (!string.IsNullOrEmpty(eventId.Name))
                {
                    clefEvent["EventName"] = eventId.Name;
                }
            }

            try
            {
                var json = JsonSerializer.Serialize(clefEvent);
                _eventQueue.Enqueue(json);
            }
            catch
            {
                // Silently ignore serialization failures
            }
        }

        private static string MapLogLevel(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace => "Verbose",
            LogLevel.Debug => "Debug",
            LogLevel.Information => "Information",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Fatal",
            _ => "Information"
        };
    }
}
