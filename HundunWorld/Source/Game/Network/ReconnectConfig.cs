using Horizon.Game.Message.Enums;
using System;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 重连配置类，用于管理重连参数
    /// </summary>
    public class ReconnectConfig
    {
        /// <summary>
        /// 最大重连尝试次数
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = -1; // -1表示无限次重连

        /// <summary>
        /// 基础重连间隔(毫秒)
        /// </summary>
        public int ReconnectDelayMs { get; set; } = 1000;

        /// <summary>
        /// 最大重连持续时间
        /// </summary>
        public TimeSpan MaxReconnectDuration { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 重连策略
        /// </summary>
        public ReconnectStrategy Strategy { get; set; } = ReconnectStrategy.Adaptive;

        /// <summary>
        /// 指数退避的最大延迟(毫秒)
        /// </summary>
        public int MaxExponentialDelayMs { get; set; } = 60000; // 最大1分钟

        /// <summary>
        /// 线性增长的步长(毫秒)
        /// </summary>
        public int LinearStepMs { get; set; } = 1000;

        /// <summary>
        /// 创建默认配置
        /// </summary>
        /// <returns></returns>
        public static ReconnectConfig CreateDefault()
        {
            return new ReconnectConfig();
        }

        /// <summary>
        /// 根据策略计算下一次重连延迟
        /// </summary>
        /// <param name="attempt">重连尝试次数</param>
        /// <param name="elapsedSeconds">已用时间（秒）</param>
        /// <returns>延迟毫秒数</returns>
        public int CalculateNextDelay(int attempt, int elapsedSeconds = 0)
        {
            // 实现降级重连策略：
            // 第1分钟：每秒重连一次
            // 第2分钟：每5秒重连一次
            // 第3分钟：每20秒重连一次
            // 第4分钟：每2秒重连一次
            // 第5分钟：每5秒重连一次
            
            if (Strategy == ReconnectStrategy.Adaptive)
            {
                // 根据已用时间调整重连策略
                if (elapsedSeconds <= 60)
                {
                    // 第1分钟：每秒重连
                    return 1000;
                }
                else if (elapsedSeconds <= 120)
                {
                    // 第2分钟：每5秒重连
                    return 5000;
                }
                else if (elapsedSeconds <= 180)
                {
                    // 第3分钟：每20秒重连
                    return 20000;
                }
                else if (elapsedSeconds <= 240)
                {
                    // 第4分钟：每2秒重连
                    return 2000;
                }
                else
                {
                    // 第5分钟及以后：每5秒重连
                    return 5000;
                }
            }
            
            return Strategy switch
            {
                ReconnectStrategy.FixedInterval => ReconnectDelayMs,
                ReconnectStrategy.ExponentialBackoff => CalculateExponentialDelay(attempt),
                ReconnectStrategy.LinearBackoff => CalculateLinearDelay(attempt),
                _ => ReconnectDelayMs
            };
        }

        /// <summary>
        /// 计算指数退避延迟
        /// </summary>
        /// <param name="attempt">重连尝试次数</param>
        /// <returns>延迟毫秒数</returns>
        private int CalculateExponentialDelay(int attempt)
        {
            // 重连间隔 = Min(基础间隔 * (2 ^ (尝试次数-1)), 最大间隔)
            var delay = ReconnectDelayMs * Math.Pow(2, attempt - 1);
            return Math.Min((int)delay, MaxExponentialDelayMs);
        }

        /// <summary>
        /// 计算线性增长延迟
        /// </summary>
        /// <param name="attempt">重连尝试次数</param>
        /// <returns>延迟毫秒数</returns>
        private int CalculateLinearDelay(int attempt)
        {
            // 重连间隔 = 基础间隔 + (尝试次数-1) * 步长
            return ReconnectDelayMs + (attempt - 1) * LinearStepMs;
        }
    }
}