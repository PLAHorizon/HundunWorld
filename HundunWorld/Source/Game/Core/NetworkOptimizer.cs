using FlaxEngine;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Core
{
    /// <summary>
    /// 网络性能统计
    /// </summary>
    public class NetworkStats
    {
        public float RTT { get; set; }              // 往返延迟(ms)
        public float Jitter { get; set; }           // 抖动(ms)
        public float PacketLoss { get; set; }       // 丢包率(0-1)
        public float BandwidthIn { get; set; }      // 入站带宽(bytes/s)
        public float BandwidthOut { get; set; }     // 出站带宽(bytes/s)
        public int PendingMessages { get; set; }    // 待发送消息数
        public float InterpolationDelay { get; set; } // 插值延迟(ms)
    }

    /// <summary>
    /// 网络优化管理器 - 产品级网络体验。
    /// 特性：
    /// - 延迟补偿（Lag Compensation）
    /// - 带宽自适应（动态调整同步频率）
    /// - 消息优先级队列
    /// - 实体兴趣管理（AOI优化）
    /// - 快照压缩
    /// </summary>
    public class NetworkOptimizer
    {
        private static NetworkOptimizer _instance;
        public static NetworkOptimizer Instance => _instance ??= new NetworkOptimizer();

        // ===== 延迟补偿 =====
        private float _estimatedRTT = 50f;
        private float _rttVariance = 10f;
        private List<float> _rttSamples = new List<float>(32);
        private const int MaxRTTSamples = 20;

        // ===== 带宽自适应 =====
        private float _baseSyncRate = 20f;      // 基础同步频率(Hz)
        private float _currentSyncRate = 20f;   // 当前同步频率
        private float _minSyncRate = 5f;
        private float _maxSyncRate = 30f;

        // ===== 消息优先级 =====
        private PriorityQueue _messageQueue = new PriorityQueue();

        // ===== 统计 =====
        public NetworkStats Stats { get; } = new NetworkStats();

        /// <summary>当前估算RTT(ms)</summary>
        public float EstimatedRTT => _estimatedRTT;

        /// <summary>当前同步频率(Hz)</summary>
        public float CurrentSyncRate => _currentSyncRate;

        /// <summary>同步间隔(秒)</summary>
        public float SyncInterval => 1f / _currentSyncRate;

        // ===== 延迟补偿 =====

        /// <summary>
        /// 记录RTT样本（收到服务器ACK时调用）
        /// </summary>
        public void RecordRTT(float rttMs)
        {
            _rttSamples.Add(rttMs);
            if (_rttSamples.Count > MaxRTTSamples)
                _rttSamples.RemoveAt(0);

            // 计算加权平均RTT
            float sum = 0, sumSq = 0;
            foreach (var sample in _rttSamples)
            {
                sum += sample;
                sumSq += sample * sample;
            }
            float mean = sum / _rttSamples.Count;
            float variance = (sumSq / _rttSamples.Count) - (mean * mean);

            _estimatedRTT = mean;
            _rttVariance = Mathf.Sqrt(Mathf.Max(0, variance));

            Stats.RTT = _estimatedRTT;
            Stats.Jitter = _rttVariance;
        }

        /// <summary>
        /// 获取延迟补偿时间（用于命中判定回退）
        /// </summary>
        public float GetLagCompensationTime()
        {
            // 回退半个RTT + 1个插值帧
            return (_estimatedRTT * 0.5f + Stats.InterpolationDelay) / 1000f;
        }

        /// <summary>
        /// 获取插值延迟（用于远程实体平滑）
        /// </summary>
        public float GetInterpolationDelay()
        {
            // 插值延迟 = RTT + 2*Jitter（确保有足够缓冲）
            float delay = _estimatedRTT + 2f * _rttVariance;
            return Mathf.Clamp(delay, 50f, 300f); // 50-300ms
        }

        // ===== 带宽自适应 =====

        /// <summary>
        /// 每帧更新（自适应调整同步频率）
        /// </summary>
        public void Update(float deltaTime)
        {
            AdaptSyncRate();
            Stats.InterpolationDelay = GetInterpolationDelay();
        }

        private void AdaptSyncRate()
        {
            // 根据RTT和丢包率动态调整同步频率
            float targetRate = _baseSyncRate;

            // RTT越高，降低频率
            if (_estimatedRTT > 100f)
                targetRate *= 0.7f;
            else if (_estimatedRTT > 200f)
                targetRate *= 0.5f;

            // 丢包率高，降低频率
            if (Stats.PacketLoss > 0.05f)
                targetRate *= 0.6f;
            else if (Stats.PacketLoss > 0.1f)
                targetRate *= 0.4f;

            _currentSyncRate = Mathf.Clamp(targetRate, _minSyncRate, _maxSyncRate);
        }

        /// <summary>
        /// 是否应该在本帧发送同步数据
        /// </summary>
        private float _syncAccumulator = 0f;
        public bool ShouldSyncThisFrame(float deltaTime)
        {
            _syncAccumulator += deltaTime;
            if (_syncAccumulator >= SyncInterval)
            {
                _syncAccumulator -= SyncInterval;
                return true;
            }
            return false;
        }

        // ===== 消息优先级 =====

        /// <summary>
        /// 消息优先级
        /// </summary>
        public enum MessagePriority
        {
            /// <summary>关键（战斗/移动，立即发送）</summary>
            Critical = 0,
            /// <summary>高（技能/交互）</summary>
            High = 1,
            /// <summary>普通（聊天/状态）</summary>
            Normal = 2,
            /// <summary>低（统计/日志）</summary>
            Low = 3
        }

        /// <summary>
        /// 将消息加入优先级队列
        /// </summary>
        public void EnqueueMessage(byte[] data, MessagePriority priority)
        {
            _messageQueue.Enqueue(data, (int)priority);
            Stats.PendingMessages = _messageQueue.Count;
        }

        /// <summary>
        /// 获取下一批待发送消息（按优先级）
        /// </summary>
        public List<byte[]> DequeueBatch(int maxCount = 10)
        {
            var batch = new List<byte[]>();
            for (int i = 0; i < maxCount && _messageQueue.Count > 0; i++)
            {
                batch.Add(_messageQueue.Dequeue());
            }
            Stats.PendingMessages = _messageQueue.Count;
            return batch;
        }

        // ===== 快照压缩 =====

        /// <summary>
        /// 计算实体是否需要同步（基于距离和重要性）
        /// </summary>
        public bool ShouldSyncEntity(float distance, bool isImportant)
        {
            if (isImportant) return true;

            // 距离越远，同步频率越低
            if (distance < 20f) return true;           // 近距离：每帧同步
            if (distance < 50f) return _syncAccumulator < SyncInterval * 2;  // 中距离：2倍间隔
            if (distance < 100f) return _syncAccumulator < SyncInterval * 4; // 远距离：4倍间隔
            return false; // 超远距离：不同步
        }

        /// <summary>
        /// 获取位置压缩精度（基于距离）
        /// </summary>
        public float GetPositionPrecision(float distance)
        {
            if (distance < 20f) return 0.01f;   // 高精度
            if (distance < 50f) return 0.05f;   // 中精度
            return 0.1f;                         // 低精度
        }
    }

    /// <summary>
    /// 简单优先级队列
    /// </summary>
    internal class PriorityQueue
    {
        private List<(byte[] data, int priority)> _items = new List<(byte[], int)>();

        public int Count => _items.Count;

        public void Enqueue(byte[] data, int priority)
        {
            _items.Add((data, priority));
            // 按优先级排序（数字越小优先级越高）
            _items.Sort((a, b) => a.priority.CompareTo(b.priority));
        }

        public byte[] Dequeue()
        {
            if (_items.Count == 0) return null;
            var item = _items[0];
            _items.RemoveAt(0);
            return item.data;
        }
    }
}
