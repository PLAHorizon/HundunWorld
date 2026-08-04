namespace Horizon.Game.Core.Configuration;

/// <summary>
/// 服务端 per-session 带宽预算配置（超大规模容量治理，spec 5.5.1.1）。
/// </summary>
/// <remarks>
/// <para>
/// 带宽红线：任意会话平均下行 ≤ <see cref="BudgetKbps"/>（默认 1000kbps）；
/// 超大规模档位目标 ≤ <see cref="UltraScaleBudgetKbps"/>（默认 500kbps）。
/// </para>
/// <para>
/// 三级降级顺序（spec 5.5.1.1 b）：先降频（<see cref="NormalSnapshotHz"/> → <see cref="ThrottledSnapshotHz"/> → <see cref="DegradedSnapshotHz"/>），
/// 再裁剪低频字段，最后按距离裁剪实体。恢复时按连续 <see cref="RecoverySeconds"/> 秒低于预算逐级回升。
/// </para>
/// <para>
/// 合法区间：预算 &gt; 0；NormalHz &gt; ThrottledHz &gt; DegradedHz ≥ 1；RecoverySeconds ≥ 1；
/// <see cref="WindowSeconds"/> &gt; 0。非法值由 <see cref="BandwidthBudgetValidator"/> 兜底回退默认。
/// </para>
/// </remarks>
public sealed class BandwidthBudgetOptions
{
    /// <summary>带宽红线预算（kbps），默认 1000（spec 5.5.1.1 a 验收）。</summary>
    public double BudgetKbps { get; set; } = 1000.0;

    /// <summary>超大规模档位目标预算（kbps），默认 500（spec 5.5.1.1 目标）。</summary>
    public double UltraScaleBudgetKbps { get; set; } = 500.0;

    /// <summary>正常快照频率（Hz），默认 20。</summary>
    public int NormalSnapshotHz { get; set; } = 20;

    /// <summary>限流快照频率（Hz，第一级降频），默认 10。</summary>
    public int ThrottledSnapshotHz { get; set; } = 10;

    /// <summary>深度降级快照频率（Hz，第二级降频），默认 5。</summary>
    public int DegradedSnapshotHz { get; set; } = 5;

    /// <summary>带宽恢复判定秒数（连续 N 秒低于预算后逐级回升），默认 3。</summary>
    public int RecoverySeconds { get; set; } = 3;

    /// <summary>带宽统计滚动窗口长度（秒），默认 1.0。</summary>
    public double WindowSeconds { get; set; } = 1.0;
}