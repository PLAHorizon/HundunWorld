namespace Horizon.Game.ECS.Arch.Diagnostics;

/// <summary>
/// 客户端规模档位：按同屏远程实体数量分级，驱动呈现降级策略。
/// </summary>
/// <remarks>
/// 档位阈值（实体数）：
/// <list type="bullet">
///   <item>Tier0：≤ 20</item>
///   <item>Tier1：≤ 100</item>
///   <item>Tier2：≤ 1000</item>
///   <item>Tier3：≤ 5000</item>
///   <item>OverLimit：&gt; 5000</item>
/// </list>
/// 超档位时对最远实体"暂停平滑推进或降低更新优先级"，但不得从订阅集无声丢失。
/// </remarks>
public enum SyncScaleTier : byte
{
    /// <summary>基础档位：同屏实体数 ≤ 20，全帧平滑呈现。</summary>
    Tier0 = 0,

    /// <summary>小型场景档位：同屏实体数 ≤ 100。</summary>
    Tier1 = 1,

    /// <summary>中型场景档位：同屏实体数 ≤ 1000。</summary>
    Tier2 = 2,

    /// <summary>超大规模档位：同屏实体数 ≤ 5000。</summary>
    Tier3 = 3,

    /// <summary>超出规模上限：同屏实体数 &gt; 5000，需按规则降级。</summary>
    OverLimit = 4,
}