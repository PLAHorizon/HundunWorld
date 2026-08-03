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
}