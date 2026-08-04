namespace Horizon.Game.Core.Configuration;

/// <summary>
/// 兴趣分级档位：按"实体与订阅玩家"距离对快照下发频率与字段完整性分级。
/// </summary>
/// <remarks>
/// 近档保持高频全量，保证近距离实体平滑度；中/远档降频并裁剪低频字段。
/// </remarks>
public enum InterestGrade : byte
{
    /// <summary>近档（≤ NearDistanceMeters）：高频全量字段。</summary>
    Near = 0,

    /// <summary>中档（≤ MidDistanceMeters）：降频 + 裁剪低频字段。</summary>
    Mid = 1,

    /// <summary>远档（&gt; MidDistanceMeters）：最低频或事件驱动。</summary>
    Far = 2,
}