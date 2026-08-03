using System.ComponentModel.DataAnnotations;

namespace Horizon.Game.Gateway.Configuration
{
    /// <summary>
    /// 客户端预测修正风暴检测配置选项。
    /// </summary>
    /// <remarks>
    /// 对应 <c>ReconciliationSystem</c> 的风暴检测参数：
    /// <list type="bullet">
    ///   <item><see cref="StormThreshold"/>：修正风暴检测窗口内允许的最大修正次数。</item>
    ///   <item><see cref="StormWindowSeconds"/>：修正风暴检测窗口长度（秒）。</item>
    ///   <item><see cref="StormCooldownSeconds"/>：修正风暴冷却时间（秒）。</item>
    /// </list>
    /// 启动时通过配置文件调整，不硬编码。例如高 RTT 玩家可放宽 <see cref="StormThreshold"/>=10。
    /// </remarks>
    public class SyncReconciliationOptions
    {
        /// <summary>
        /// 修正风暴检测窗口内允许的最大修正次数。
        /// 超过此次数进入冷却期，跳过后续修正避免角色反复抽搐。
        /// 默认 5；高 RTT 玩家可放宽到 10。
        /// </summary>
        [Range(1, 100)]
        public int StormThreshold { get; set; } = 5;

        /// <summary>
        /// 修正风暴检测窗口（秒）。
        /// 在此时间窗口内修正次数超过 <see cref="StormThreshold"/> 即触发风暴冷却。
        /// 默认 2.0 秒。
        /// </summary>
        [Range(0.5f, 30f)]
        public float StormWindowSeconds { get; set; } = 2.0f;

        /// <summary>
        /// 修正风暴冷却时间（秒）。
        /// 进入风暴模式后跳过修正的时长，避免角色反复抽搐的死循环。
        /// 默认 1.0 秒。
        /// </summary>
        [Range(0.1f, 10f)]
        public float StormCooldownSeconds { get; set; } = 1.0f;
    }
}