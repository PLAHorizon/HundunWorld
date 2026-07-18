using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Strategy.Storage.Redis
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using Horizon.Core.Abstract;
    using StackExchange.Redis;

    public sealed class RedisConnection : IDisposable
    {
        #region 连接池配置
        private const int DefaultPoolSize = 10;
        private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultSyncTimeout = TimeSpan.FromSeconds(5);
        #endregion

        #region 单例模式
        private static readonly Lazy<RedisConnection> _instance =
            new Lazy<RedisConnection>(() => new RedisConnection());
        public static RedisConnection Instance => _instance.Value;
        #endregion

        #region 内部成员
        private readonly string _connectionString;
        private readonly ConcurrentBag<ConnectionMultiplexer> _connectionPool = new();
        private readonly object _connectionGate = new();
        private readonly SemaphoreSlim _poolSemaphore;
        private bool _disposed;
        #endregion

        #region 构造函数
        /// <summary>
        /// 私有构造函数（单例模式）
        /// </summary>
        private RedisConnection()
        {
            _connectionString = GetDefaultConnectionString();
            _poolSemaphore = new SemaphoreSlim(DefaultPoolSize, DefaultPoolSize);
        }

        /// <summary>
        /// 自定义配置构造函数
        /// </summary>
        public RedisConnection(string connectionString, int poolSize = DefaultPoolSize)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _poolSemaphore = new SemaphoreSlim(poolSize, poolSize);
        }
        #endregion

        #region 连接管理
        /// <summary>
        /// 创建新连接
        /// </summary>
        private ConnectionMultiplexer CreateNewConnection()
        {
            var config = BuildConfigurationOptions(_connectionString);

            var connection = ConnectionMultiplexer.Connect(config);
            connection.ConnectionFailed += (sender, args) =>
                HandleConnectionFailure(sender as ConnectionMultiplexer);

            return connection;
        }

        /// <summary>
        /// 统一解析连接字符串，兼容两种格式：
        /// 1. 旧格式：<c>password=xxx@host:port,...</c>（项目历史遗留）
        /// 2. StackExchange.Redis 标准格式：<c>host:port,password=xxx,...</c>
        /// </summary>
        private static ConfigurationOptions BuildConfigurationOptions(string connectionString)
        {
            var normalized = NormalizeConnectionString(connectionString);
            var config = StackExchange.Redis.ConfigurationOptions.Parse(normalized);

            // 确保基础默认配置
            if (config.AbortOnConnectFail)
            {
                config.AbortOnConnectFail = false;
            }

            if (config.ConnectTimeout == 0)
            {
                config.ConnectTimeout = (int)DefaultConnectTimeout.TotalMilliseconds;
            }

            if (config.SyncTimeout == 0)
            {
                config.SyncTimeout = (int)DefaultSyncTimeout.TotalMilliseconds;
            }

            return config;
        }

        /// <summary>
        /// 将旧格式 <c>password=xxx@host:port</c> 转换为 StackExchange.Redis 标准格式。
        /// </summary>
        private static string NormalizeConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));

            // 旧格式特征：包含 '@' 且 '@' 前是 password=xxx
            var atIndex = connectionString.IndexOf('@');
            if (atIndex > 0)
            {
                var beforeAt = connectionString.Substring(0, atIndex);
                var afterAt = connectionString.Substring(atIndex + 1);

                if (beforeAt.StartsWith("password=", StringComparison.OrdinalIgnoreCase))
                {
                    var password = beforeAt.Substring(9);
                    if (string.IsNullOrWhiteSpace(password))
                        return afterAt;

                    return $"{afterAt},password={password}";
                }
            }

            return connectionString;
        }

        /// <summary>
        /// 处理连接失败事件
        /// </summary>
        private void HandleConnectionFailure(ConnectionMultiplexer connection)
        {
            if (connection != null && connection.IsConnected)
            {
                ReconnectAsync(connection).ConfigureAwait(false);
            }
        }

        private ConnectionMultiplexer GetOrCreateConnection()
        {
            lock (_connectionGate)
            {
                while (_connectionPool.TryTake(out var existing))
                {
                    if (existing != null && existing.IsConnected)
                    {
                        _connectionPool.Add(existing);
                        return existing;
                    }

                    existing?.Dispose();
                }

                var connection = CreateNewConnection();
                _connectionPool.Add(connection);
                return connection;
            }
        }
        #endregion

        #region 核心操作方法
        /// <summary>
        /// 获取数据库实例（异步）
        /// </summary>
        public async Task<IDatabase> GetDatabaseAsync(int db = -1)
        {
            return await Task.FromResult(GetOrCreateConnection().GetDatabase(db)).ConfigureAwait(false);
        }

        /// <summary>
        /// 执行Redis命令（同步）
        /// </summary>
        public T Execute<T>(Func<IDatabase, T> func, int db = -1)
        {
            return func(GetOrCreateConnection().GetDatabase(db));
        }

        /// <summary>
        /// 执行Redis命令（异步）
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<IDatabase, Task<T>> func, int db = -1)
        {
            return await func(GetOrCreateConnection().GetDatabase(db)).ConfigureAwait(false);
        }
        #endregion

        #region 重连机制
        /// <summary>
        /// 异步重连
        /// </summary>
        private async Task ReconnectAsync(ConnectionMultiplexer oldConnection)
        {
            const int maxRetries = 3;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                try
                {
                    attempt++;
                    await oldConnection.CloseAsync().ConfigureAwait(false);
                    var newConnection = CreateNewConnection();

                    // 验证新连接
                    if (newConnection.IsConnected)
                    {
                        _connectionPool.Add(newConnection);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (attempt >= maxRetries)
                    {
                        Log.Error($"Redis reconnection failed after {maxRetries} attempts", ex);
                        throw;
                    }

                    await Task.Delay(1000 * attempt).ConfigureAwait(false);
                }
            }
        }
        #endregion

        #region 辅助方法
        private static string GetDefaultConnectionString()
        {
            return "localhost:6379"; // 默认使用无密码的本地连接
        }
        #endregion

        #region 服务器管理
        /// <summary>
        /// 获取所有Redis终结点
        /// </summary>
        public IEnumerable<System.Net.EndPoint> GetEndPoints()
        {
            return GetOrCreateConnection().GetEndPoints();
        }

        /// <summary>
        /// 获取指定终结点的Redis服务器实例
        /// </summary>
        public IServer GetServer(System.Net.EndPoint endpoint)
        {
            return GetOrCreateConnection().GetServer(endpoint);
        }
        #endregion

        #region 释放资源
        public void Dispose()
        {
            if (_disposed) return;

            foreach (var connection in _connectionPool)
            {
                connection?.Dispose();
            }
            _poolSemaphore.Dispose();
            _disposed = true;
        }
        #endregion
    }
}