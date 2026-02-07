using System;
using System.Threading;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// 分布式系统玩家角色唯一ID生成器
    /// 基于雪花算法(Snowflake Algorithm)实现
    /// </summary>
    public class PlayerIdGenerator
    {
        #region 常量定义
        
        /// <summary>
        /// 开始时间戳 (2024-01-01 00:00:00 UTC)
        /// </summary>
        private const long StartTimeStamp = 1704067200000L;
        
        /// <summary>
        /// 机器ID位数
        /// </summary>
        private const int WorkerIdBits = 5;
        
        /// <summary>
        /// 数据中心ID位数
        /// </summary>
        private const int DataCenterIdBits = 5;
        
        /// <summary>
        /// 序列号位数
        /// </summary>
        private const int SequenceBits = 12;
        
        /// <summary>
        /// 最大机器ID
        /// </summary>
        private const long MaxWorkerId = -1L ^ (-1L << WorkerIdBits);
        
        /// <summary>
        /// 最大数据中心ID
        /// </summary>
        private const long MaxDataCenterId = -1L ^ (-1L << DataCenterIdBits);
        
        /// <summary>
        /// 序列号掩码
        /// </summary>
        private const long SequenceMask = -1L ^ (-1L << SequenceBits);
        
        /// <summary>
        /// 机器ID左移位数
        /// </summary>
        private const int WorkerIdShift = SequenceBits;
        
        /// <summary>
        /// 数据中心ID左移位数
        /// </summary>
        private const int DataCenterIdShift = SequenceBits + WorkerIdBits;
        
        /// <summary>
        /// 时间戳左移位数
        /// </summary>
        private const int TimestampShift = SequenceBits + WorkerIdBits + DataCenterIdBits;
        
        #endregion
        
        #region 字段
        
        /// <summary>
        /// 机器ID
        /// </summary>
        private readonly long _workerId;
        
        /// <summary>
        /// 数据中心ID
        /// </summary>
        private readonly long _dataCenterId;
        
        /// <summary>
        /// 序列号
        /// </summary>
        private long _sequence = 0L;
        
        /// <summary>
        /// 上次时间戳
        /// </summary>
        private long _lastTimestamp = -1L;
        
        /// <summary>
        /// 锁对象
        /// </summary>
        private readonly object _lock = new object();
        
        #endregion
        
        #region 构造函数
        
        /// <summary>
        /// 初始化玩家ID生成器
        /// </summary>
        /// <param name="workerId">机器ID (0-31)</param>
        /// <param name="dataCenterId">数据中心ID (0-31)</param>
        public PlayerIdGenerator(long workerId, long dataCenterId)
        {
            if (workerId > MaxWorkerId || workerId < 0)
            {
                throw new ArgumentException($"机器ID必须在0到{MaxWorkerId}之间", nameof(workerId));
            }
            
            if (dataCenterId > MaxDataCenterId || dataCenterId < 0)
            {
                throw new ArgumentException($"数据中心ID必须在0到{MaxDataCenterId}之间", nameof(dataCenterId));
            }
            
            _workerId = workerId;
            _dataCenterId = dataCenterId;
        }
        
        #endregion
        
        #region 公共方法
        
        /// <summary>
        /// 生成下一个唯一ID
        /// </summary>
        /// <returns>64位唯一ID</returns>
        public long NextId()
        {
            lock (_lock)
            {
                var timestamp = GetCurrentTimestamp();
                
                // 如果当前时间小于上一次ID生成的时间戳，说明系统时钟回退过，抛出异常
                if (timestamp < _lastTimestamp)
                {
                    throw new InvalidOperationException($"时钟回退，拒绝生成ID，回退时间：{_lastTimestamp - timestamp}毫秒");
                }
                
                // 如果是同一时间生成的，则进行毫秒内序列
                if (_lastTimestamp == timestamp)
                {
                    _sequence = (_sequence + 1) & SequenceMask;
                    
                    // 毫秒内序列溢出
                    if (_sequence == 0)
                    {
                        // 阻塞到下一个毫秒，获得新的时间戳
                        timestamp = TilNextMillis(_lastTimestamp);
                    }
                }
                else
                {
                    // 时间戳改变，毫秒内序列重置
                    _sequence = 0L;
                }
                
                // 上次生成ID的时间截
                _lastTimestamp = timestamp;
                
                // 移位并通过或运算拼到一起组成64位的ID
                return ((timestamp - StartTimeStamp) << TimestampShift)
                       | (_dataCenterId << DataCenterIdShift)
                       | (_workerId << WorkerIdShift)
                       | _sequence;
            }
        }
        
        /// <summary>
        /// 生成玩家角色ID（带前缀）
        /// </summary>
        /// <param name="prefix">前缀，默认为"PLY"</param>
        /// <returns>带前缀的玩家ID字符串</returns>
        public string GeneratePlayerId(string prefix = "PLY")
        {
            return $"{prefix}{NextId()}";
        }
        
        /// <summary>
        /// 批量生成玩家ID
        /// </summary>
        /// <param name="count">生成数量</param>
        /// <returns>ID数组</returns>
        public long[] GenerateBatch(int count)
        {
            if (count <= 0)
                throw new ArgumentException("生成数量必须大于0", nameof(count));
                
            var ids = new long[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = NextId();
            }
            return ids;
        }
        
        /// <summary>
        /// 解析ID中的时间戳
        /// </summary>
        /// <param name="id">要解析的ID</param>
        /// <returns>时间戳对应的DateTime</returns>
        public DateTime ParseTimestamp(long id)
        {
            var timestamp = (id >> TimestampShift) + StartTimeStamp;
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
        }
        
        /// <summary>
        /// 解析ID中的机器ID
        /// </summary>
        /// <param name="id">要解析的ID</param>
        /// <returns>机器ID</returns>
        public long ParseWorkerId(long id)
        {
            return (id >> WorkerIdShift) & MaxWorkerId;
        }
        
        /// <summary>
        /// 解析ID中的数据中心ID
        /// </summary>
        /// <param name="id">要解析的ID</param>
        /// <returns>数据中心ID</returns>
        public long ParseDataCenterId(long id)
        {
            return (id >> DataCenterIdShift) & MaxDataCenterId;
        }
        
        /// <summary>
        /// 解析ID中的序列号
        /// </summary>
        /// <param name="id">要解析的ID</param>
        /// <returns>序列号</returns>
        public long ParseSequence(long id)
        {
            return id & SequenceMask;
        }
        
        #endregion
        
        #region 私有方法
        
        /// <summary>
        /// 阻塞到下一个毫秒，直到获得新的时间戳
        /// </summary>
        /// <param name="lastTimestamp">上次生成ID的时间截</param>
        /// <returns>当前时间戳</returns>
        private long TilNextMillis(long lastTimestamp)
        {
            var timestamp = GetCurrentTimestamp();
            while (timestamp <= lastTimestamp)
            {
                timestamp = GetCurrentTimestamp();
            }
            return timestamp;
        }
        
        /// <summary>
        /// 获取当前时间戳
        /// </summary>
        /// <returns>当前时间戳（毫秒）</returns>
        private static long GetCurrentTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        
        #endregion
    }
}
