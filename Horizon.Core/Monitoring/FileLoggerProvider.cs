using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Horizon.Core.Monitoring
{
    /// <summary>
    /// 文件日志配置选项
    /// </summary>
    public class FileLoggerOptions
    {
        /// <summary>日志文件目录（相对路径基于 AppContext.BaseDirectory）。</summary>
        public string LogDirectory { get; set; } = "Logs";

        /// <summary>单个日志文件最大大小（MB），超过后按序号滚动。</summary>
        public int MaxFileSizeMB { get; set; } = 50;

        /// <summary>日志文件保留天数（0 表示不清理）。</summary>
        public int RetainDays { get; set; } = 30;

        /// <summary>服务名前缀（用于文件名标识多实例）。</summary>
        public string ServiceName { get; set; } = "App";

        /// <summary>是否在文件名中包含日期（按日切割）。</summary>
        public bool DailyRotation { get; set; } = true;

        /// <summary>批量写入间隔（秒）。</summary>
        public int FlushIntervalSeconds { get; set; } = 2;

        /// <summary>队列最大容量（防止内存溢出，0 表示不限）。</summary>
        public int MaxQueueSize { get; set; } = 20000;
    }

    /// <summary>
    /// 文件日志提供程序。<br/>
    /// 将日志事件以可读文本格式（非 JSON）批量写入按日切割的文件，<br/>
    /// 单文件超过阈值后按序号滚动，过期文件自动清理。<br/>
    /// 与 JsonConsole 不同，本提供程序只写入格式化后的 Message，<br/>
    /// 不暴露 State 中的 {OriginalFormat} 原始模板与占位符。
    /// </summary>
    public sealed class FileLoggerProvider : ILoggerProvider, IAsyncDisposable, IDisposable
    {
        private readonly FileLoggerOptions _options;
        private readonly string _logDirectory;
        private readonly string _filePrefix;
        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
        private readonly BlockingCollection<string> _queue;
        private readonly Task _writerTask;
        private readonly CancellationTokenSource _cts;
        private readonly Timer _cleanupTimer;
        private readonly long _maxFileSizeBytes;
        private bool _disposed;

        public FileLoggerProvider(FileLoggerOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            // 解析日志目录：绝对路径直接用，相对路径基于程序基目录
            _logDirectory = Path.IsPathRooted(_options.LogDirectory)
                ? _options.LogDirectory
                : Path.Combine(AppContext.BaseDirectory, _options.LogDirectory);
            Directory.CreateDirectory(_logDirectory);

            _filePrefix = string.IsNullOrWhiteSpace(_options.ServiceName) ? "App" : SanitizeFileName(_options.ServiceName);
            _maxFileSizeBytes = Math.Max(1, _options.MaxFileSizeMB) * 1024L * 1024L;

            _queue = new BlockingCollection<string>(
                _options.MaxQueueSize > 0 ? _options.MaxQueueSize : int.MaxValue);
            _cts = new CancellationTokenSource();
            _writerTask = Task.Run(() => WriterLoopAsync(_cts.Token));

            // 每天清理一次过期日志文件
            _cleanupTimer = new Timer(
                _ => CleanupExpiredFiles(),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromHours(24));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _queue));
        }

        /// <summary>
        /// 后台写入循环：从队列消费日志行，批量写入当前文件。<br/>
        /// 使用单一 StreamWriter 实例避免并发文件句柄争用。
        /// </summary>
        private async Task WriterLoopAsync(CancellationToken ct)
        {
            StreamWriter? writer = null;
            string? currentFilePath = null;
            DateTime currentDate = DateTime.MinValue;
            long currentLength = 0;

            try
            {
                // GetConsumingEnumerable 在 CompleteAdding 后会自然退出迭代
                foreach (var line in _queue.GetConsumingEnumerable(ct))
                {
                    try
                    {
                        // 按日切割：日期变化时切换新文件
                        var today = _options.DailyRotation ? DateTime.Today : DateTime.MinValue;
                        if (writer == null || today != currentDate)
                        {
                            writer?.Flush();
                            writer?.Dispose();
                            currentDate = today;
                            currentFilePath = ResolveFilePath(today);
                            currentLength = File.Exists(currentFilePath) ? new FileInfo(currentFilePath).Length : 0;
                            writer = new StreamWriter(currentFilePath, append: true, Encoding.UTF8)
                            {
                                AutoFlush = false
                            };
                        }

                        // 大小滚动：超过阈值切换到下一个序号文件
                        if (currentLength >= _maxFileSizeBytes)
                        {
                            writer.Flush();
                            writer.Dispose();
                            currentFilePath = ResolveFilePath(today, forceNextSequence: true);
                            currentLength = 0;
                            writer = new StreamWriter(currentFilePath, append: true, Encoding.UTF8)
                            {
                                AutoFlush = false
                            };
                        }

                        await writer.WriteLineAsync(line).ConfigureAwait(false);
                        currentLength += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;

                        // 批量刷新：队列空时 flush，避免每行一次 IO
                        if (_queue.Count == 0)
                        {
                            await writer.FlushAsync().ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        // 文件写入失败不应导致进程崩溃；丢弃该行
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 关闭信号
            }
            finally
            {
                try { writer?.Flush(); writer?.Dispose(); }
                catch { /* 忽略 */ }
            }
        }

        /// <summary>计算当前应写入的文件路径，支持大小滚动。</summary>
        private string ResolveFilePath(DateTime date, bool forceNextSequence = false)
        {
            string fileName;
            if (_options.DailyRotation)
            {
                var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                // 查找当天已有最大序号
                int seq = forceNextSequence ? (FindMaxSequence(dateStr) + 1) : FindMaxSequence(dateStr);
                fileName = seq == 0
                    ? $"{_filePrefix}-{dateStr}.log"
                    : $"{_filePrefix}-{dateStr}.{seq}.log";
            }
            else
            {
                fileName = $"{_filePrefix}.log";
            }
            return Path.Combine(_logDirectory, fileName);
        }

        private int FindMaxSequence(string dateStr)
        {
            try
            {
                var prefix = $"{_filePrefix}-{dateStr}";
                int maxSeq = 0;
                foreach (var file in Directory.EnumerateFiles(_logDirectory, $"{prefix}*.log"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    // 形如 App-2026-07-23 或 App-2026-07-23.1
                    var dotIdx = name.LastIndexOf('.');
                    if (dotIdx > prefix.Length && int.TryParse(name.AsSpan(dotIdx + 1), out int seq))
                    {
                        if (seq > maxSeq) maxSeq = seq;
                    }
                }
                return maxSeq;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>清理超过保留天数的日志文件。</summary>
        private void CleanupExpiredFiles()
        {
            if (_options.RetainDays <= 0) return;
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-_options.RetainDays);
                foreach (var file in Directory.EnumerateFiles(_logDirectory, "*.log"))
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < cutoff)
                    {
                        try { info.Delete(); } catch { /* 忽略单个文件删除失败 */ }
                    }
                }
            }
            catch
            {
                // 清理失败不应影响主流程
            }
        }

        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        private static string SanitizeFileName(string input)
        {
            var sb = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                sb.Append(Array.IndexOf(InvalidFileNameChars, ch) >= 0 ? '_' : ch);
            }
            return sb.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _queue.CompleteAdding();
            _cts.Cancel();
            _cleanupTimer.Dispose();

            try
            {
                await _writerTask.ConfigureAwait(false);
            }
            catch
            {
                // 关闭期间异常忽略
            }

            _queue.Dispose();
            _cts.Dispose();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// 文件日志记录器实例。<br/>
    /// 将日志事件格式化为可读文本行后入队，由后台写入线程统一落盘。<br/>
    /// 格式：<code>yyyy-MM-dd HH:mm:ss.fff [WRN] CategoryName 消息文本</code>
    /// </summary>
    internal sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly BlockingCollection<string> _queue;

        public FileLogger(string categoryName, BlockingCollection<string> queue)
        {
            _categoryName = categoryName;
            _queue = queue;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // 使用格式化后的 Message（不暴露 {OriginalFormat} 模板与 State 字典）
            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception == null) return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var levelTag = MapLogLevelTag(logLevel);
            var sb = new StringBuilder(256);
            sb.Append(timestamp).Append(" [").Append(levelTag).Append("] ").Append(_categoryName);
            if (eventId.Id != 0)
            {
                sb.Append(" (").Append(eventId.Id);
                if (!string.IsNullOrEmpty(eventId.Name)) sb.Append(":").Append(eventId.Name);
                sb.Append(")");
            }
            sb.Append(" ").Append(message);

            if (exception != null)
            {
                sb.AppendLine();
                sb.Append(exception.ToString());
            }

            var line = sb.ToString();

            // 队列满时直接丢弃，避免反压阻塞业务线程
            if (!_queue.IsAddingCompleted)
            {
                try { _queue.Add(line); } catch { /* 忽略 */ }
            }
        }

        private static string MapLogLevelTag(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
    }

    /// <summary>
    /// 文件日志集成扩展方法
    /// </summary>
    public static class FileLoggerExtensions
    {
        /// <summary>
        /// 添加文件日志提供程序（按 appsettings 中 "File" 节配置）。
        /// </summary>
        public static ILoggingBuilder AddFile(this ILoggingBuilder logging, IConfiguration configuration, string serviceName)
        {
            var options = new FileLoggerOptions();
            configuration.GetSection("Logging:File").Bind(options);
            if (string.IsNullOrWhiteSpace(options.ServiceName) || options.ServiceName == "App")
            {
                options.ServiceName = serviceName;
            }
            logging.AddProvider(new FileLoggerProvider(options));
            return logging;
        }

        /// <summary>
        /// 添加文件日志提供程序（通过显式选项）。
        /// </summary>
        public static ILoggingBuilder AddFile(this ILoggingBuilder logging, FileLoggerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            logging.AddProvider(new FileLoggerProvider(options));
            return logging;
        }
    }
}
