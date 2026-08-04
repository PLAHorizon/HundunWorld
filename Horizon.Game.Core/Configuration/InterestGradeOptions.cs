namespace Horizon.Game.Core.Configuration;

/// <summary>
/// 服务端兴趣区分级降频配置（超大规模容量治理，spec 5.5.1.2）。
/// </summary>
/// <remarks>
/// <para>
/// 分级语义：近距离实体保持高频全量字段，保证其平滑度不受远距离实体拖累。
/// </para>
/// <list type="bullet">
///   <item>近档（≤ <see cref="NearDistanceMeters"/>）：高频（<see cref="NearSnapshotHz"/>）全量字段。</item>
///   <item>中档（≤ <see cref="MidDistanceMeters"/>）：降频（<see cref="MidSnapshotHz"/>）+ 裁剪低频字段。</item>
///   <item>远档（&gt; <see cref="MidDistanceMeters"/>）：最低频（<see cref="FarSnapshotHz"/>）或事件驱动。</item>
/// </list>
/// <para>
/// <see cref="HysteresisMeters"/> 为分级切换滞回（防边界抖动，spec 5.5.3.2）。
/// 合法区间：0 &lt; Near &lt; Mid &lt; 视野范围；NearHz ≥ MidHz ≥ FarHz ≥ 1；Hysteresis &gt; 0。
/// 非法值由 <see cref="InterestGradeValidator"/> 兜底回退默认。
/// </para>
/// </remarks>
public sealed class InterestGradeOptions
{
    /// <summary>分级距离：Near 上限（米），默认 30。</summary>
    public float NearDistanceMeters { get; set; } = 30f;

    /// <summary>分级距离：Mid 上限（米），默认 80。</summary>
    public float MidDistanceMeters { get; set; } = 80f;

    /// <summary>近档下发频率（Hz），默认 20。</summary>
    public int NearSnapshotHz { get; set; } = 20;

    /// <summary>中档下发频率（Hz），默认 10。</summary>
    public int MidSnapshotHz { get; set; } = 10;

    /// <summary>远档下发频率（Hz），默认 5。</summary>
    public int FarSnapshotHz { get; set; } = 5;

    /// <summary>分级切换滞回距离（米），默认 5，防边界抖动。</summary>
    public float HysteresisMeters { get; set; } = 5f;
}