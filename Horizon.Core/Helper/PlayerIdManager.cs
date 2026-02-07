using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// 玩家ID生成器管理类（单例模式）
    /// </summary>
    public sealed class PlayerIdManager
    {
        #region 单例实现
        
        private static readonly Lazy<PlayerIdManager> _instance = 
            new Lazy<PlayerIdManager>(() => new PlayerIdManager());
        
        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static PlayerIdManager Instance => _instance.Value;
        
        #endregion
        
        #region 字段
        
        /// <summary>
        /// ID生成器
        /// </summary>
        private readonly PlayerIdGenerator _generator;
        
        /// <summary>
        /// 机器ID
        /// </summary>
        public long WorkerId { get; }
        
        /// <summary>
        /// 数据中心ID
        /// </summary>
        public long DataCenterId { get; }
        
        /// <summary>
        /// ID缓存（用于避免重复）
        /// </summary>
        private readonly ConcurrentDictionary<long, DateTime> _idCache = 
            new ConcurrentDictionary<long, DateTime>();
        
        #endregion
        
        #region 构造函数
        
        /// <summary>
        /// 私有构造函数
        /// </summary>
        private PlayerIdManager()
        {
            // 自动获取机器ID和数据中心ID
            WorkerId = GetWorkerId();
            DataCenterId = GetDataCenterId();
            
            _generator = new PlayerIdGenerator(WorkerId, DataCenterId);
            
            // 启动清理过期缓存的定时器
            StartCacheCleanup();
        }
        
        #endregion
        
        #region 公共方法
        
        /// <summary>
        /// 生成唯一玩家ID
        /// </summary>
        /// <returns>64位唯一ID</returns>
        public long GeneratePlayerId()
        {
            var id = _generator.NextId();
            
            // 将ID添加到缓存中，用于去重检查
            _idCache.TryAdd(id, DateTime.UtcNow);
            
            return id;
        }
        
        /// <summary>
        /// 生成带前缀的玩家ID字符串
        /// </summary>
        /// <param name="prefix">前缀</param>
        /// <returns>带前缀的玩家ID</returns>
        public string GeneratePlayerIdString(string prefix = "PLY")
        {
            return _generator.GeneratePlayerId(prefix);
        }
        
        /// <summary>
        /// 批量生成玩家ID
        /// </summary>
        /// <param name="count">生成数量</param>
        /// <returns>ID数组</returns>
        public long[] GenerateBatchPlayerIds(int count)
        {
            var ids = _generator.GenerateBatch(count);
            
            // 将批量ID添加到缓存
            var now = DateTime.UtcNow;
            foreach (var id in ids)
            {
                _idCache.TryAdd(id, now);
            }
            
            return ids;
        }
        
        /// <summary>
        /// 验证ID是否是有效的玩家ID
        /// </summary>
        /// <param name="id">要验证的ID</param>
        /// <returns>是否有效</returns>
        public bool IsValidPlayerId(long id)
        {
            try
            {
                // 检查时间戳是否合理
                var timestamp = _generator.ParseTimestamp(id);
                var now = DateTime.UtcNow;
                
                // ID的时间戳不应该超过当前时间，也不应该太久远
                return timestamp <= now && timestamp >= new DateTime(2024, 1, 1);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 检查ID是否已经被使用过（基于缓存）
        /// </summary>
        /// <param name="id">要检查的ID</param>
        /// <returns>是否已使用</returns>
        public bool IsIdUsed(long id)
        {
            return _idCache.ContainsKey(id);
        }
        
        /// <summary>
        /// 获取ID的详细信息
        /// </summary>
        /// <param name="id">要解析的ID</param>
        /// <returns>ID信息</returns>
        public IdInfo GetIdInfo(long id)
        {
            return new IdInfo
            {
                Id = id,
                Timestamp = _generator.ParseTimestamp(id),
                WorkerId = _generator.ParseWorkerId(id),
                DataCenterId = _generator.ParseDataCenterId(id),
                Sequence = _generator.ParseSequence(id),
                IsValid = IsValidPlayerId(id),
                IsUsed = IsIdUsed(id)
            };
        }
        
        /// <summary>
        /// 获取生成器状态信息
        /// </summary>
        /// <returns>状态信息</returns>
        public GeneratorStatus GetStatus()
        {
            return new GeneratorStatus
            {
                WorkerId = WorkerId,
                DataCenterId = DataCenterId,
                CachedIdCount = _idCache.Count,
                LastGeneratedTime = DateTime.UtcNow
            };
        }
        
        /// <summary>
        /// 清理过期的ID缓存
        /// </summary>
        /// <param name="expireHours">过期小时数，默认24小时</param>
        public void CleanExpiredCache(int expireHours = 24)
        {
            var expireTime = DateTime.UtcNow.AddHours(-expireHours);
            var expiredKeys = new List<long>();
            
            foreach (var kvp in _idCache)
            {
                if (kvp.Value < expireTime)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }
            
            foreach (var key in expiredKeys)
            {
                _idCache.TryRemove(key, out _);
            }
        }
        
        #endregion
        
        #region 私有方法
        
        /// <summary>
        /// 获取机器ID（基于MAC地址）
        /// </summary>
        /// <returns>机器ID</returns>
        private static long GetWorkerId()
        {
            try
            {
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in networkInterfaces)
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        var mac = ni.GetPhysicalAddress().ToString();
                        if (!string.IsNullOrEmpty(mac))
                        {
                            // 使用MAC地址的后5位作为WorkerId
                            return Math.Abs(mac.GetHashCode()) % 32;
                        }
                    }
                }
            }
            catch
            {
                // 如果获取失败，使用随机数
            }
            
            return new Random().Next(0, 32);
        }
        
        /// <summary>
        /// 获取数据中心ID（基于IP地址）
        /// </summary>
        /// <returns>数据中心ID</returns>
        private static long GetDataCenterId()
        {
            try
            {
                var hostName = Dns.GetHostName();
                var hostEntry = Dns.GetHostEntry(hostName);
                
                foreach (var ip in hostEntry.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        // 使用IP地址的最后一个字节作为DataCenterId
                        var bytes = ip.GetAddressBytes();
                        return bytes[bytes.Length - 1] % 32;
                    }
                }
            }
            catch
            {
                // 如果获取失败，使用随机数
            }
            
            return new Random().Next(0, 32);
        }
        
        /// <summary>
        /// 启动缓存清理定时器
        /// </summary>
        private void StartCacheCleanup()
        {
            var timer = new System.Threading.Timer(
                callback: _ => CleanExpiredCache(),
                state: null,
                dueTime: TimeSpan.FromHours(1),  // 1小时后开始
                period: TimeSpan.FromHours(6)   // 每6小时执行一次
            );
        }
        
        #endregion
    }
    
    /// <summary>
    /// ID信息
    /// </summary>
    public class IdInfo
    {
        /// <summary>
        /// ID值
        /// </summary>
        public long Id { get; set; }
        
        /// <summary>
        /// 生成时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// 机器ID
        /// </summary>
        public long WorkerId { get; set; }
        
        /// <summary>
        /// 数据中心ID
        /// </summary>
        public long DataCenterId { get; set; }
        
        /// <summary>
        /// 序列号
        /// </summary>
        public long Sequence { get; set; }
        
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; }
        
        /// <summary>
        /// 是否已使用
        /// </summary>
        public bool IsUsed { get; set; }
        
        public override string ToString()
        {
            return $"ID: {Id}, 时间: {Timestamp:yyyy-MM-dd HH:mm:ss}, " +
                   $"机器: {WorkerId}, 数据中心: {DataCenterId}, 序列: {Sequence}, " +
                   $"有效: {IsValid}, 已使用: {IsUsed}";
        }
    }
    
    /// <summary>
    /// 生成器状态
    /// </summary>
    public class GeneratorStatus
    {
        /// <summary>
        /// 机器ID
        /// </summary>
        public long WorkerId { get; set; }
        
        /// <summary>
        /// 数据中心ID
        /// </summary>
        public long DataCenterId { get; set; }
        
        /// <summary>
        /// 缓存的ID数量
        /// </summary>
        public int CachedIdCount { get; set; }
        
        /// <summary>
        /// 最后生成时间
        /// </summary>
        public DateTime LastGeneratedTime { get; set; }
        
        public override string ToString()
        {
            return $"机器ID: {WorkerId}, 数据中心ID: {DataCenterId}, " +
                   $"缓存数量: {CachedIdCount}, 最后生成: {LastGeneratedTime:yyyy-MM-dd HH:mm:ss}";
        }
    }
}
