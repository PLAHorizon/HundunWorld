using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO.Compression;
using Horizon.Game.Message.Enums;
using System.IO;

namespace Game.Performance
{
    /// <summary>
    /// 缃戠粶鎬ц兘浼樺寲鍣?    /// 鎻愪緵缃戠粶杩炴帴浼樺寲銆佸欢杩熺洃鎺у拰鎬ц兘璋冧紭鍔熻兘
    /// </summary>
    public class NetworkOptimizer : IDisposable
    {
        private readonly Queue<long> _latencyHistory = new Queue<long>();
        private readonly Queue<DateTime> _connectionHistory = new Queue<DateTime>();
        // 娣诲姞娑堟伅鎵瑰鐞嗙浉鍏冲瓧娈?
        private readonly Queue<MessageBatchEntry> _messageBatch = new Queue<MessageBatchEntry>();
        private readonly object _batchLock = new object();
        private DateTime _lastBatchSendTime = DateTime.UtcNow;
        private TimeSpan _batchTimeThreshold = TimeSpan.FromMilliseconds(50);
        private int _batchSizeThreshold = 10;
        
        private const int MaxHistorySize = 100;
        private const int ConnectionWindowMinutes = 10;
        private bool _disposed = false;
        
        // 鎬ц兘鎸囨爣
        public long AverageLatency { get; private set; }
        public long MinLatency { get; private set; } = long.MaxValue;
        public long MaxLatency { get; private set; }
        public double LatencyVariance { get; private set; }
        public int ConnectionsPerMinute { get; private set; }
        
        /// <summary>
        /// 鏋勯€犲嚱鏁?        /// </summary>
        public NetworkOptimizer()
        {
        }
        
        /// <summary>
        /// 甯﹀弬鏁扮殑鏋勯€犲嚱鏁?        /// </summary>
        public NetworkOptimizer(int batchSizeThreshold, TimeSpan batchTimeThreshold)
        {
            _batchSizeThreshold = batchSizeThreshold;
            _batchTimeThreshold = batchTimeThreshold;
        }
        
        /// <summary>
        /// 璁板綍寤惰繜鏁版嵁
        /// </summary>
        public void RecordLatency(long latency)
        {
            if (latency <= 0) return;
            
            _latencyHistory.Enqueue(latency);
            if (_latencyHistory.Count > MaxHistorySize)
            {
                _latencyHistory.Dequeue();
            }
            
            UpdateLatencyMetrics();
        }
        
        /// <summary>
        /// 璁板綍杩炴帴浜嬩欢
        /// </summary>
        public void RecordConnection()
        {
            var now = DateTime.UtcNow;
            _connectionHistory.Enqueue(now);
            
            // 娓呯悊杩囨湡鐨勮繛鎺ヨ褰?
            var cutoff = now.AddMinutes(-ConnectionWindowMinutes);
            while (_connectionHistory.Count > 0 && _connectionHistory.Peek() < cutoff)
            {
                _connectionHistory.Dequeue();
            }
            
            UpdateConnectionMetrics();
        }
        
        /// <summary>
        /// 鏇存柊寤惰繜鎸囨爣
        /// </summary>
        private void UpdateLatencyMetrics()
        {
            if (_latencyHistory.Count == 0) return;
            
            var latencies = _latencyHistory.ToArray();
            
            AverageLatency = (long)latencies.Average();
            MinLatency = latencies.Min();
            MaxLatency = latencies.Max();
            
            // 璁＄畻鏂瑰樊
            var variance = latencies.Select(x => Math.Pow(x - AverageLatency, 2)).Average();
            LatencyVariance = Math.Sqrt(variance);
        }
        
        /// <summary>
        /// 鏇存柊杩炴帴鎸囨爣
        /// </summary>
        private void UpdateConnectionMetrics()
        {
            ConnectionsPerMinute = (int)Math.Round(_connectionHistory.Count / (double)ConnectionWindowMinutes);
        }
        
        /// <summary>
        /// 璇勪及缃戠粶璐ㄩ噺
        /// </summary>
        public NetworkQuality EvaluateNetworkQuality()
        {
            if (_latencyHistory.Count < 5)
                return NetworkQuality.Unknown;
                
            // 鍩轰簬寤惰繜璇勪及缃戠粶璐ㄩ噺
            if (AverageLatency <= 50 && LatencyVariance <= 10)
                return NetworkQuality.Excellent;
            else if (AverageLatency <= 100 && LatencyVariance <= 20)
                return NetworkQuality.Good;
            else if (AverageLatency <= 200 && LatencyVariance <= 50)
                return NetworkQuality.Fair;
            else if (AverageLatency <= 500)
                return NetworkQuality.Poor;
            else
                return NetworkQuality.VeryPoor;
        }
        
        /// <summary>
        /// 获取网络状态摘要
        /// </summary>
        public string GetNetworkStatusSummary()
        {
            var quality = EvaluateNetworkQuality();
            return $"平均延迟: {AverageLatency:F0}ms\n" +
                   $"延迟方差: {LatencyVariance:F2}\n" +
                   $"连接频率: {ConnectionsPerMinute}次/分钟\n" +
                   $"网络质量: {quality}\n" +
                   $"批处理大小阈值: {_batchSizeThreshold}\n" +
                   $"批处理时间阈值: {_batchTimeThreshold.TotalMilliseconds}ms";
        }
        
        /// <summary>
        /// 获取网络建议
        /// </summary>
        public List<string> GetNetworkRecommendations()
        {
            var recommendations = new List<string>();
            var quality = EvaluateNetworkQuality();
            
            switch (quality)
            {
                case NetworkQuality.Excellent:
                    recommendations.Add("网络状况优秀，保持当前配置");
                    break;
                    
                case NetworkQuality.Good:
                    recommendations.Add("网络状况良好");
                    if (LatencyVariance > 15)
                        recommendations.Add("延迟波动较大，建议检查网络稳定性");
                    break;
                    
                case NetworkQuality.Fair:
                    recommendations.Add("网络状况一般，建议优化网络连接");
                    if (AverageLatency > 150)
                        recommendations.Add("平均延迟较高，考虑选择更近的服务器");
                    if (LatencyVariance > 30)
                        recommendations.Add("延迟不稳定，检查网络环境");
                    break;
                    
                case NetworkQuality.Poor:
                    recommendations.Add("网络状况较差，强烈建议优化");
                    recommendations.Add("考虑切换到更稳定的网络环境");
                    recommendations.Add("检查是否有其他程序占用带宽");
                    break;
                    
                case NetworkQuality.VeryPoor:
                    recommendations.Add("网络状况很差，游戏体验可能受到严重影响");
                    recommendations.Add("建议立即检查网络连接");
                    recommendations.Add("考虑联系网络服务提供商");
                    break;
                    
                default:
                    recommendations.Add("数据不足，继续监控网络状态");
                    break;
            }
            
            // 连接频率建议
            if (ConnectionsPerMinute > 10)
            {
                recommendations.Add("连接频率过高，可能存在频繁断线重连问题");
            }
            
            return recommendations;
        }
        
        /// <summary>
        /// 添加消息到批处理
        /// </summary>
        public void AddMessageToBatch(Horizon.Game.Message.MessageUnion message, MessageType messageType)
        {
            lock (_batchLock)
            {
                _messageBatch.Enqueue(new MessageBatchEntry
                {
                    Message = message,
                    MessageType = messageType,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        
        /// <summary>
        /// 压缩数据
        /// </summary>
        public byte[] CompressData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return data;
                
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress))
                {
                    gzip.Write(data, 0, data.Length);
                }
                return output.ToArray();
            }
        }
        
        /// <summary>
        /// 解压数据
        /// </summary>
        public byte[] DecompressData(byte[] compressedData)
        {
            if (compressedData == null || compressedData.Length == 0)
                return compressedData;
                
            using (var input = new MemoryStream(compressedData))
            {
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                    {
                        gzip.CopyTo(output);
                    }
                    return output.ToArray();
                }
            }
        }
        
        /// <summary>
        /// 获取推荐的超时时间
        /// </summary>
        public int GetRecommendedTimeout()
        {
            var quality = EvaluateNetworkQuality();
            
            return quality switch
            {
                NetworkQuality.Excellent => 3000,
                NetworkQuality.Good => 5000,
                NetworkQuality.Fair => 8000,
                NetworkQuality.Poor => 12000,
                NetworkQuality.VeryPoor => 15000,
                _ => 10000
            };
        }
        
        /// <summary>
        /// 获取推荐的重连间隔
        /// </summary>
        public int GetRecommendedReconnectInterval(int attemptCount)
        {
            var quality = EvaluateNetworkQuality();
            var baseInterval = quality switch
            {
                NetworkQuality.Excellent => 1000,
                NetworkQuality.Good => 2000,
                NetworkQuality.Fair => 3000,
                NetworkQuality.Poor => 5000,
                NetworkQuality.VeryPoor => 8000,
                _ => 3000
            };
            
            // 指数退避，但有上限
            var interval = baseInterval * Math.Pow(1.5, Math.Min(attemptCount, 5));
            return Math.Min((int)interval, 30000); // 最大30秒
        }
        
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// 释放资源
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                }
                
                // 释放非托管资源
                
                _disposed = true;
            }
        }
        
        /// <summary>
        /// 析构函数
        /// </summary>
        ~NetworkOptimizer()
        {
            Dispose(false);
        }
    }
    
    /// <summary>
    /// 网络质量枚举
    /// </summary>
    
    
    /// <summary>
    /// 消息批处理条目
    /// </summary>
    public class MessageBatchEntry
    {
        public Horizon.Game.Message.MessageUnion Message { get; set; }
        public Horizon.Game.Message.Enums.MessageType MessageType { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
