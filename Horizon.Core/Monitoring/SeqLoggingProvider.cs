using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Horizon.Core.Monitoring
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

        /// <summary>
        /// 事件队列最大容量（防止内存溢出）
        /// </summary>
        public int MaxQueueSize { get; set; } = 10000;
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

        public SeqLoggerProvider(SeqLoggingOptions options, string serviceName)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _serviceName = serviceName;

            // 验证URL方案（仅允许http/https，防止SSRF）
            var uri = new Uri(options.ServerUrl.TrimEnd('/') + "/");
            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                throw new ArgumentException(
                    $"Seq ServerUrl必须使用http或https方案，不允许: {uri.Scheme}",
                    nameof(options));
            }

            _httpClient = new HttpClient
            {
                BaseAddress = uri,
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
            return _loggers.GetOrAdd(categoryName, name => new SeqLogger(name, _eventQueue, _serviceName, _options.MaxQueueSize));
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
        private readonly int _maxQueueSize;

        public SeqLogger(string categoryName, ConcurrentQueue<string> eventQueue, string serviceName, int maxQueueSize = 10000)
        {
            _categoryName = categoryName;
            _eventQueue = eventQueue;
            _serviceName = serviceName;
            _maxQueueSize = maxQueueSize;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // 防止队列无限增长导致内存溢出
            if (_eventQueue.Count >= _maxQueueSize) return;

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

        internal static string MapLogLevel(LogLevel logLevel) => logLevel switch
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

    /// <summary>
    /// Seq日志集成扩展方法
    /// 在开发环境中启用Seq社区版日志聚合
    /// </summary>
    public static class SeqLoggingExtensions
    {
        /// <summary>
        /// 添加Seq日志提供程序（如果配置启用）
        /// </summary>
        /// <param name="logging">日志构建器</param>
        /// <param name="configuration">应用配置</param>
        /// <param name="serviceName">服务名称</param>
        /// <returns>日志构建器</returns>
        public static ILoggingBuilder AddSeqIfEnabled(
            this ILoggingBuilder logging,
            IConfiguration configuration,
            string serviceName)
        {
            var options = new SeqLoggingOptions();
            configuration.GetSection("Seq").Bind(options);

            if (options.Enabled && !string.IsNullOrEmpty(options.ServerUrl))
            {
                logging.AddProvider(new SeqLoggerProvider(options, serviceName));
            }

            return logging;
        }

        /// <summary>
        /// 添加Seq日志提供程序（通过显式选项）
        /// </summary>
        /// <param name="logging">日志构建器</param>
        /// <param name="options">Seq配置选项</param>
        /// <param name="serviceName">服务名称</param>
        /// <returns>日志构建器</returns>
        public static ILoggingBuilder AddSeq(
            this ILoggingBuilder logging,
            SeqLoggingOptions options,
            string serviceName)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            logging.AddProvider(new SeqLoggerProvider(options, serviceName));
            return logging;
        }
    }
}
