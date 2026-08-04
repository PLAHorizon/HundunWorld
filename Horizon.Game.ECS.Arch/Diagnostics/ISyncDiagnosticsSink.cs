namespace Horizon.Game.ECS.Arch.Diagnostics;

/// <summary>
/// 同步管线诊断事件汇接口。
/// ECS 同步系统（InterpolationSystem/ReconciliationSystem/SnapshotApplySystem）通过依赖注入可选持有该 Sink，
/// 在关键事件发生时调用对应方法输出结构化诊断信息。当 Sink 为 null 时不输出，保证零开销。
/// </summary>
/// <remarks>
/// <b>异常映射契约</b>：实现方应吞掉所有异常，避免日志/监控失败影响同步主逻辑。
/// 调用方无需 try-catch，但实现方内部必须 try-catch 包裹全部逻辑。
/// </remarks>
public interface ISyncDiagnosticsSink
{
    /// <summary>远程角色位置发生传送跳变（距离超过传送阈值，直接跳到 Target）。</summary>
    /// <param name="entityId">发生跳变的远程角色实体 ID。</param>
    /// <param name="distance">跳变距离（米）。</param>
    /// <param name="serverTick">触发跳变的服务器 tick。</param>
    void OnTeleportJump(ulong entityId, float distance, long serverTick);

    /// <summary>客户端预测修正风暴触发（窗口内修正次数超阈值，进入冷却期跳过修正）。</summary>
    /// <param name="entityId">触发风暴的实体 ID。</param>
    /// <param name="recentCount">窗口内近期修正次数。</param>
    /// <param name="windowSeconds">统计窗口长度（秒）。</param>
    void OnCorrectionStormTriggered(ulong entityId, int recentCount, float windowSeconds);

    /// <summary>过期修正被跳过（修正对应的客户端 tick 已被后续 ACK 超越）。</summary>
    /// <param name="entityId">实体 ID。</param>
    /// <param name="lastProcessedTick">该修正对应的客户端 tick。</param>
    /// <param name="lastAckedTick">当前已 ACK 的最新客户端 tick。</param>
    void OnStaleCorrectionSkipped(ulong entityId, long lastProcessedTick, long lastAckedTick);

    /// <summary>自适应插值延迟窗口发生显著调整。</summary>
    /// <param name="oldDelaySeconds">调整前窗口（秒）。</param>
    /// <param name="newDelaySeconds">调整后窗口（秒）。</param>
    /// <param name="rttSeconds">当前 RTT（秒）。</param>
    /// <param name="jitterSeconds">当前抖动（秒）。</param>
    void OnAdaptiveWindowAdjusted(float oldDelaySeconds, float newDelaySeconds, float rttSeconds, float jitterSeconds);

    /// <summary>delta 解码时 baseline 不匹配，客户端请求服务端重传全量快照。</summary>
    /// <param name="expectedBaselineTick">delta 期望的 baseline tick。</param>
    /// <param name="receivedBaselineTick">客户端实际持有的 baseline tick（无则为 0）。</param>
    void OnBaselineResyncRequested(long expectedBaselineTick, long receivedBaselineTick);

    /// <summary>配置非法并回退默认值，或混合时长配置可能仍表现为闪跳。</summary>
    /// <param name="fieldName">非法字段名（如 "SmoothThresholdMeters"）。</param>
    /// <param name="configuredValue">配置值。</param>
    /// <param name="fallbackValue">回退后的默认值。</param>
    /// <param name="isWarningOnly">true=仅警告不回退（如混合时长过短）。</param>
    void OnConfigInvalid(string fieldName, float configuredValue, float fallbackValue, bool isWarningOnly);

    /// <summary>快照位置数据非有限值（NaN/Infinity），已跳过该实体本次更新。</summary>
    /// <param name="entityId">异常实体标识。</param>
    /// <param name="serverTick">对应服务器 tick。</param>
    void OnInvalidSnapshotSkipped(ulong entityId, long serverTick);

    /// <summary>多角色数量超限触发降档或插值暂停。</summary>
    /// <param name="remoteEntityCount">当前远程角色数量。</param>
    /// <param name="reason">触发原因（PerformanceDegrade / MaxEntityCap）。</param>
    void OnMultiEntityDegraded(int remoteEntityCount, string reason);

    /// <summary>会话带宽超预算触发限流降频。</summary>
    /// <remarks>
    /// 触发条件：会话 1s 滚动窗口平均带宽超预算（如 100kbps 红线）→ 快照频率降档。
    /// 实现方契约：吞异常、可限频输出，避免日志影响同步主逻辑。
    /// </remarks>
    /// <param name="sessionId">被限流的会话标识。</param>
    /// <param name="kbps">触发时的当前带宽（kbps）。</param>
    /// <param name="fromHz">降频前快照频率。</param>
    /// <param name="toHz">降频后快照频率。</param>
    void OnBandwidthThrottled(long sessionId, double kbps, int fromHz, int toHz);

    /// <summary>会话带宽连续低于预算，恢复更高快照频率。</summary>
    /// <remarks>
    /// 触发条件：连续 RecoverySeconds 秒低于预算 → 快照频率回升。
    /// 实现方契约：吞异常、可限频输出。
    /// </remarks>
    /// <param name="sessionId">恢复的会话标识。</param>
    /// <param name="kbps">恢复时的当前带宽（kbps）。</param>
    /// <param name="fromHz">恢复前快照频率。</param>
    /// <param name="toHz">恢复后快照频率。</param>
    void OnBandwidthRecovered(long sessionId, double kbps, int fromHz, int toHz);

    /// <summary>客户端同屏实体数跨档位阈值，触发规模档位切换。</summary>
    /// <remarks>
    /// 触发条件：客户端同屏远程实体数跨档位阈值 20/100/1000/5000。
    /// 实现方契约：吞异常、持续过程限频为一条启动事件。
    /// </remarks>
    /// <param name="entityCount">切换时的同屏实体数。</param>
    /// <param name="from">切换前档位。</param>
    /// <param name="to">切换后档位。</param>
    void OnScaleTierChanged(int entityCount, SyncScaleTier from, SyncScaleTier to);

    /// <summary>超档位时对最远实体进行降级（暂停平滑推进或降低更新优先级）。</summary>
    /// <remarks>
    /// 触发条件：客户端同屏实体数超当前档位上限，按距离选取最远实体降级。
    /// 实现方契约：吞异常、持续过程限频为一条启动事件。
    /// </remarks>
    /// <param name="entityId">被降级的实体 ID。</param>
    /// <param name="distanceMeters">实体与本地玩家距离（米）。</param>
    /// <param name="reason">降级原因（如 "ScaleOverLimit"）。</param>
    void OnScaleDegrade(ulong entityId, float distanceMeters, string reason);
}