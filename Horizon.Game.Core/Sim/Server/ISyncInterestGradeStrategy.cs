using Horizon.Game.Core.Configuration;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 服务端兴趣分级策略：按"实体与订阅玩家"距离对快照下发频率与字段完整性分级（spec 5.5.1.2）。
/// </summary>
/// <remarks>
/// <para>
/// 分级语义：近距离实体保持高频全量字段，保证其平滑度不受远距离实体拖累；
/// 中/远距离实体降频并裁剪低频字段，降低超大规模场景的总下发带宽。
/// </para>
/// <para>
/// 协作方式：快照生成端（ZoneShardGrain）在构建 EntityDelta 前调用 <see cref="Classify"/> 分级；
/// 分级结果决定 <see cref="ShouldSendFullFields"/>（字段裁剪）与 <see cref="GetSnapshotHz"/>（定频），
/// 且分级结果参与 <see cref="GatewaySyncDispatcher"/> 带宽计费（按裁剪后字段估算包大小）。
/// </para>
/// </remarks>
public interface ISyncInterestGradeStrategy
{
    /// <summary>按距离分级为近/中/远三档（含滞回保护，防边界抖动）。</summary>
    /// <param name="distanceMeters">实体与订阅玩家的距离（米）。</param>
    InterestGrade Classify(float distanceMeters);

    /// <summary>是否下发全量字段（近档 true；中/远档裁剪低频字段）。</summary>
    /// <param name="grade">分级档位。</param>
    bool ShouldSendFullFields(InterestGrade grade);

    /// <summary>获取指定档位的下发频率（Hz）。</summary>
    /// <param name="grade">分级档位。</param>
    int GetSnapshotHz(InterestGrade grade);
}