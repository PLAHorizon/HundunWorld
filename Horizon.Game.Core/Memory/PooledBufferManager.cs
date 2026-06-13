using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Game.Core.Configuration;

namespace Horizon.Game.Core.Memory
{
    /// <summary>
    /// 基于对象池的高性能缓冲区管理器
    /// </summary>
    public class PooledBufferManager : IDisposable
    {
        private readonly ILogger<PooledBufferManager> _logger;
        private readonly NetworkConfiguration _config;
        private readonly ArrayPool<byte> _arrayPool;
        private readonly ConcurrentDictionary<string, PooledClientBuffer> _clientBuffers;
        private readonly Timer _cleanupTimer;
        private volatile bool _disposed = false;

        public PooledBufferManager(
            ILogger<PooledBufferManager> logger,
            IOptions<NetworkConfiguration> config)
        {
            _logger = logger;
            _config = config.Value;
            _arrayPool = ArrayPool<byte>.Shared;
            _clientBuffers = new ConcurrentDictionary<string, PooledClientBuffer>();

            // 每分钟清理一次超时的缓冲区
            _cleanupTimer = new Timer(CleanupExpiredBuffers, null,
                TimeSpan.FromMilliseconds(_config.ClientBufferCleanupIntervalMs),
                TimeSpan.FromMilliseconds(_config.ClientBufferCleanupIntervalMs));
        }

        /// <summary>
        /// 获取或创建客户端缓冲区
        /// </summary>
        public PooledClientBuffer GetOrCreateBuffer(string clientId)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PooledBufferManager));

            return _clientBuffers.GetOrAdd(clientId, id =>
            {
                _logger.LogDebug("为客户端 {ClientId} 创建新的池化缓冲区", id);
                return new PooledClientBuffer(id, _arrayPool, _config.MaxMessageLength);
            });
        }

        /// <summary>
        /// 移除客户端缓冲区
        /// </summary>
        public void RemoveClientBuffer(string clientId)
        {
            if (_disposed || string.IsNullOrEmpty(clientId)) return;

            if (_clientBuffers.TryRemove(clientId, out var buffer))
            {
                buffer.Dispose();
                _logger.LogDebug("已移除客户端 {ClientId} 的缓冲区", clientId);
            }
        }

        /// <summary>
        /// 清理过期的缓冲区
        /// </summary>
        private void CleanupExpiredBuffers(object? state)
        {
            if (_disposed) return;

            var expiredClients = new List<string>();
            var cutoffTime = DateTime.UtcNow.AddMinutes(-5); // 5分钟超时

            foreach (var kvp in _clientBuffers.ToArray())
            {
                if (kvp.Value.LastAccessTime < cutoffTime)
                {
                    expiredClients.Add(kvp.Key);
                }
            }

            foreach (var clientId in expiredClients)
            {
                RemoveClientBuffer(clientId);
            }

            if (expiredClients.Count > 0)
            {
                _logger.LogInformation("已清理 {Count} 个过期客户端缓冲区", expiredClients.Count);
            }
        }

        /// <summary>
        /// 获取缓冲区使用统计
        /// </summary>
        public BufferManagerStats GetStats()
        {
            return new BufferManagerStats
            {
                ActiveBuffersCount = _clientBuffers.Count,
                TotalBytesAllocated = _clientBuffers.Values.Sum(b => b.AllocatedBytes)
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cleanupTimer?.Dispose();

            // 清理所有客户端缓冲区
            foreach (var kvp in _clientBuffers.ToArray())
            {
                if (_clientBuffers.TryRemove(kvp.Key, out var buffer))
                {
                    buffer.Dispose();
                }
            }

            _logger.LogInformation("池化缓冲区管理器已释放");
        }
    }

    /// <summary>
    /// 基于对象池的客户端缓冲区
    /// </summary>
    public class PooledClientBuffer : IDisposable
    {
        private readonly string _clientId;
        private readonly ArrayPool<byte> _arrayPool;
        private readonly int _maxBufferSize;
        private readonly object _lock = new();

        private byte[]? _buffer;
        private int _dataLength;
        private bool _disposed = false;

        public DateTime LastAccessTime { get; private set; }
        public int AllocatedBytes => _buffer?.Length ?? 0;

        public PooledClientBuffer(string clientId, ArrayPool<byte> arrayPool, int maxBufferSize)
        {
            _clientId = clientId;
            _arrayPool = arrayPool;
            _maxBufferSize = maxBufferSize;
            LastAccessTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 向缓冲区追加数据（使用零拷贝技术）
        /// </summary>
        public void AppendData(ReadOnlySpan<byte> data)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PooledClientBuffer));

            lock (_lock)
            {
                LastAccessTime = DateTime.UtcNow;

                var requiredSize = _dataLength + data.Length;
                if (requiredSize > _maxBufferSize)
                {
                    throw new InvalidOperationException($"Buffer size would exceed maximum ({_maxBufferSize} bytes)");
                }

                EnsureCapacity(requiredSize);

                data.CopyTo(_buffer.AsSpan(_dataLength));
                _dataLength += data.Length;
            }
        }

        /// <summary>
        /// 尝试读取完整的消息
        /// </summary>
        public bool TryReadMessage(out ReadOnlyMemory<byte> message)
        {
            message = default;
            if (_disposed) return false;

            lock (_lock)
            {
                LastAccessTime = DateTime.UtcNow;

                if (_dataLength < 4) return false;

                var messageLength = BitConverter.ToInt32(_buffer, 0);
                if (messageLength <= 0 || messageLength > _maxBufferSize - 4) return false;

                var totalMessageSize = 4 + messageLength;
                if (_dataLength < totalMessageSize) return false;

                // 创建消息的只读内存视图
                message = new ReadOnlyMemory<byte>(_buffer, 0, totalMessageSize);

                // 移除已读取的数据
                var remainingDataLength = _dataLength - totalMessageSize;
                if (remainingDataLength > 0)
                {
                    Array.Copy(_buffer, totalMessageSize, _buffer, 0, remainingDataLength);
                }
                _dataLength = remainingDataLength;

                return true;
            }
        }

        /// <summary>
        /// 尝试在缓冲区中查找下一个消息边界
        /// </summary>
        public bool TryFindNextMessageBoundary()
        {
            if (_disposed) return false;

            lock (_lock)
            {
                LastAccessTime = DateTime.UtcNow;

                for (int i = 1; i < _dataLength - 3; i++)
                {
                    var potentialLength = BitConverter.ToInt32(_buffer, i);
                    if (potentialLength > 0 && potentialLength <= _maxBufferSize - 4)
                    {
                        var totalMessageSize = 4 + potentialLength;
                        if (i + totalMessageSize <= _dataLength)
                        {
                            // 找到了可能的消息边界，移除前面的损坏数据
                            var remainingDataLength = _dataLength - i;
                            Array.Copy(_buffer, i, _buffer, 0, remainingDataLength);
                            _dataLength = remainingDataLength;
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 清空缓冲区
        /// </summary>
        public void Clear()
        {
            if (_disposed) return;

            lock (_lock)
            {
                LastAccessTime = DateTime.UtcNow;
                _dataLength = 0;
            }
        }

        /// <summary>
        /// 确保缓冲区容量
        /// </summary>
        private void EnsureCapacity(int requiredSize)
        {
            if (_buffer == null || _buffer.Length < requiredSize)
            {
                var newSize = Math.Max(requiredSize, Math.Min(_buffer?.Length * 2 ?? 1024, _maxBufferSize));
                var newBuffer = _arrayPool.Rent(newSize);

                if (_buffer != null && _dataLength > 0)
                {
                    Array.Copy(_buffer, newBuffer, _dataLength);
                    _arrayPool.Return(_buffer, clearArray: true);
                }

                _buffer = newBuffer;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                if (_buffer != null)
                {
                    _arrayPool.Return(_buffer, clearArray: true);
                    _buffer = null;
                }
            }
        }
    }

    /// <summary>
    /// 缓冲区管理器统计信息
    /// </summary>
    public class BufferManagerStats
    {
        public int ActiveBuffersCount { get; init; }
        public long TotalBytesAllocated { get; init; }
    }
}
