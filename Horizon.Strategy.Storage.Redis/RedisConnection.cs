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
            InitializeConnectionPool();
        }

        /// <summary>
        /// 自定义配置构造函数
        /// </summary>
        public RedisConnection(string connectionString, int poolSize = DefaultPoolSize)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _poolSemaphore = new SemaphoreSlim(poolSize, poolSize);
            InitializeConnectionPool();
        }
        #endregion

        #region 连接管理
        /// <summary>
        /// 初始化连接池
        /// </summary>
        private void InitializeConnectionPool()
        {
            for (int i = 0; i < _poolSemaphore.CurrentCount; i++)
            {
                var connection = CreateNewConnection();
                _connectionPool.Add(connection);
            }
        }

        /// <summary>
        /// 创建新连接
        /// </summary>
        private ConnectionMultiplexer CreateNewConnection()
        {
            var config = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                ConnectTimeout = (int)DefaultConnectTimeout.TotalMilliseconds,
                SyncTimeout = (int)DefaultSyncTimeout.TotalMilliseconds,
                Password = ParsePasswordFromString(_connectionString),
                EndPoints = { ParseEndPoint(_connectionString) }
            };

            var connection = ConnectionMultiplexer.Connect(config);
            connection.ConnectionFailed += (sender, args) =>
                HandleConnectionFailure(sender as ConnectionMultiplexer);

            return connection;
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
        #endregion

        #region 核心操作方法
        /// <summary>
        /// 获取数据库实例（异步）
        /// </summary>
        public async Task<IDatabase> GetDatabaseAsync(int db = -1)
        {
            await _poolSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_connectionPool.TryTake(out var connection))
                {
                    connection = CreateNewConnection();
                }

                return connection.GetDatabase(db);
            }
            finally
            {
                _poolSemaphore.Release();
            }
        }

        /// <summary>
        /// 执行Redis命令（同步）
        /// </summary>
        public T Execute<T>(Func<IDatabase, T> func, int db = -1)
        {
            _poolSemaphore.Wait();
            try
            {
                if (!_connectionPool.TryTake(out var connection))
                {
                    connection = CreateNewConnection();
                }

                return func(connection.GetDatabase(db));
            }
            finally
            {
                _poolSemaphore.Release();
            }
        }

        /// <summary>
        /// 执行Redis命令（异步）
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<IDatabase, Task<T>> func, int db = -1)
        {
            await _poolSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_connectionPool.TryTake(out var connection))
                {
                    connection = CreateNewConnection();
                }

                return await func(connection.GetDatabase(db)).ConfigureAwait(false);
            }
            finally
            {
                _poolSemaphore.Release();
            }
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
        private static string ParsePasswordFromString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be empty");

            try
            {
                // 支持多种连接字符串格式
                // 1. password=your_password@host:port
                // 2. host:port (无密码)
                if (connectionString.Contains("@"))
                {
                    var parts = connectionString.Split('@');
                    if (parts.Length != 2)
                        throw new FormatException("Invalid connection string format");

                    var passwordPart = parts[0];
                    if (passwordPart.StartsWith("password="))
                    {
                        var password = passwordPart.Substring(9); // Skip "password="
                        if (string.IsNullOrWhiteSpace(password))
                            return null; // No password

                        return password;
                    }
                }
                
                // 如果没有@符号，说明没有密码
                return null;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Failed to parse password from connection string", ex);
            }
        }

        private static string ParseEndPoint(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be empty");

            try
            {
                // 支持多种连接字符串格式
                // 1. password=your_password@host:port
                // 2. host:port (无密码)
                if (connectionString.Contains("@"))
                {
                    var parts = connectionString.Split('@');
                    if (parts.Length != 2)
                        throw new FormatException("Invalid connection string format");

                    var endpoint = parts[1];
                    if (string.IsNullOrWhiteSpace(endpoint))
                        throw new ArgumentException("Endpoint cannot be empty");

                    return endpoint;
                }
                else
                {
                    // 直接返回连接字符串作为端点
                    return connectionString;
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Failed to parse endpoint from connection string", ex);
            }
        }

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
            if (_connectionPool.TryTake(out var connection))
            {
                try
                {
                    return connection.GetEndPoints();
                }
                finally
                {
                    _connectionPool.Add(connection);
                }
            }
            return Enumerable.Empty<System.Net.EndPoint>();
        }

        /// <summary>
        /// 获取指定终结点的Redis服务器实例
        /// </summary>
        public IServer GetServer(System.Net.EndPoint endpoint)
        {
            if (_connectionPool.TryTake(out var connection))
            {
                try
                {
                    return connection.GetServer(endpoint);
                }
                finally
                {
                    _connectionPool.Add(connection);
                }
            }
            return null;
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