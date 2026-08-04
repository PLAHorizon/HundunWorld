namespace Horizon.Game.ECS.Arch.Configuration;

/// <summary>
/// 远程角色同步阈值与多角色性能配置（受控配置通道下发，非法值由 <see cref="RemoteSyncThresholdValidator"/> 兜底）。
/// </summary>
/// <remarks>
/// 集中承载平滑区/硬跳/混合时长/多角色性能参数，替代 <c>InterpolationSystem</c> 与
/// <c>FlaxActorSyncSystem</c> 中编译期硬编码的阈值（DFX 4.4.2 阈值可配置）。
/// 默认值与需求规格完全一致：平滑区 100m / 硬跳 500m / 混合时长 0.2s / Near 30m / Mid 80m /
/// 性能降档 10 个 / 数量硬上限 20 个。
/// </remarks>
public sealed class RemoteSyncThresholdOptions
{
    /// <summary>平滑区阈值（米），默认 100，合法区间 (0, <see cref="HardSnapThresholdMeters"/>]。</summary>
    public float SmoothThresholdMeters { get; set; } = 100f;

    /// <summary>硬跳阈值（米），默认 500，合法区间 (<see cref="SmoothThresholdMeters"/>, +∞)。</summary>
    public float HardSnapThresholdMeters { get; set; } = 500f;

    /// <summary>加速混合时长（秒），默认 0.2，合法区间 (0, +∞)，建议 [0.1, 0.3]。</summary>
    public float BlendDurationSeconds { get; set; } = 0.2f;

    /// <summary>分级距离：Near 上限（米），默认 30，合法区间 (0, +∞)。</summary>
    public float NearDistanceMeters { get; set; } = 30f;

    /// <summary>分级距离：Mid 上限（米），默认 80，合法区间 (<see cref="NearDistanceMeters"/>, +∞)。</summary>
    public float MidDistanceMeters { get; set; } = 80f;

    /// <summary>远程角色性能降档阈值（个），超此数量整体降档，默认 10，合法区间 (0, +∞)。</summary>
    public int PerformanceDegradeEntityCount { get; set; } = 10;

    /// <summary>远程角色数量硬上限（个），超限暂停最远角色插值推进，默认 20，合法区间 [<see cref="PerformanceDegradeEntityCount"/>, +∞)。</summary>
    public int MaxRemoteEntityCount { get; set; } = 20;

    /// <summary>
    /// 客户端规模档位阈值（实体数），默认 { 20, 100, 1000, 5000 }（spec 5.7.1 档位 20/100/1000/5000）。
    /// 必须严格递增且全部 &gt; 0；非法时回退默认。
    /// </summary>
    public int[] TierThresholds { get; set; } = { 20, 100, 1000, 5000 };

    /// <summary>超规模实体数上限（个），默认 5000；同屏实体数超过该值触发 <c>OverLimit</c> 档位最远优先降级。</summary>
    public int UltraScaleEntityCap { get; set; } = 5000;
}