namespace Horizon.Game.ECS.Arch.Diagnostics;

/// <summary>
/// 网络质量等级：用于驱动同步策略滞回切换，避免边界反复抖动。
/// </summary>
/// <remarks>
/// 滞回切换阈值（基于 EWMA 平滑后的 RTT）：
/// <list type="bullet">
///   <item>Strong → Medium：RTT &gt; 50ms</item>
///   <item>Medium → Strong：RTT &lt; 30ms</item>
///   <item>Medium → Weak：RTT &gt; 200ms</item>
///   <item>Weak → Medium：RTT &lt; 150ms</item>
/// </list>
/// </remarks>
public enum NetworkQualityLevel : byte
{
    /// <summary>强网络（低延迟，可缩短插值窗口、禁用 Dead Reckoning）。</summary>
    Strong = 0,

    /// <summary>中等网络（默认策略）。</summary>
    Medium = 1,

    /// <summary>弱网络（高延迟，强制启用 Dead Reckoning、放宽插值窗口）。</summary>
    Weak = 2,
}