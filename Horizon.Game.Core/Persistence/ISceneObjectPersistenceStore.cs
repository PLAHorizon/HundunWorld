using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.Core.Persistence;

/// <summary>
/// Task C.5.1：场景对象状态持久化存储接口。
/// <para>
/// 由 <c>SqlServerSceneObjectPersistenceStore</c> 实现，基于 EF Core / 原始 ADO.NET 访问 SqlServer。
/// 在 <c>ZoneShardGrain</c> 激活时调用 <see cref="LoadWorldStateAsync"/> 填充内存状态表；
/// 定时（30s）调用 <see cref="SaveWorldStateAsync"/> 批量落盘；
/// 关键事件（宝箱开启/任务门激活）调用 <see cref="SaveSingleAsync"/> 即时落盘。
/// </para>
/// </summary>
public interface ISceneObjectPersistenceStore
{
    /// <summary>
    /// 加载指定 shard 下所有场景对象状态。
    /// </summary>
    /// <param name="shardKey">分片键（与 <c>IZoneShardGrain</c> 的 PrimaryKey 对齐）。</param>
    /// <returns>ObjectId → 状态数据 的字典；无数据时返回空字典。</returns>
    Task<Dictionary<ulong, SceneObjectStateData>> LoadWorldStateAsync(long shardKey);

    /// <summary>
    /// 批量保存指定 shard 下所有场景对象状态（upsert 语义）。
    /// </summary>
    /// <param name="shardKey">分片键。</param>
    /// <param name="states">待保存的状态集合。</param>
    Task SaveWorldStateAsync(long shardKey, IEnumerable<SceneObjectStateData> states);

    /// <summary>
    /// 保存单个场景对象状态（upsert 语义），用于关键事件即时落盘。
    /// </summary>
    /// <param name="shardKey">分片键。</param>
    /// <param name="state">待保存的状态。</param>
    Task SaveSingleAsync(long shardKey, SceneObjectStateData state);
}

/// <summary>
/// Task C.5.2：场景对象状态数据类（持久化用）。
/// 与 <see cref="SceneObjectStateAuthComponent"/> 字段对齐，但增加 ShardKey/Transform/UpdatedAt 用于持久化。
/// </summary>
public sealed class SceneObjectStateData
{
    /// <summary>场景对象的全局唯一 ID。</summary>
    public ulong ObjectId { get; set; }

    /// <summary>所属分片键。</summary>
    public long ShardKey { get; set; }

    /// <summary>场景对象类型。</summary>
    public SceneObjectType ObjectType { get; set; }

    /// <summary>状态位掩码（Opened/Activated/Locked/Reset）。</summary>
    public uint StateBits { get; set; }

    /// <summary>冷却结束的服务器 tick（0 表示无冷却）。</summary>
    public long CooldownEndTick { get; set; }

    /// <summary>当前归属角色 ID（0 表示无归属）。</summary>
    public ulong OwnerCharacterId { get; set; }

    /// <summary>Transform - X 坐标（可移动场景对象使用，0 表示静态）。</summary>
    public float TransformX { get; set; }

    /// <summary>Transform - Y 坐标。</summary>
    public float TransformY { get; set; }

    /// <summary>Transform - Z 坐标。</summary>
    public float TransformZ { get; set; }

    /// <summary>Transform - Pitch（弧度）。</summary>
    public float TransformPitch { get; set; }

    /// <summary>Transform - Yaw（弧度）。</summary>
    public float TransformYaw { get; set; }

    /// <summary>Transform - Roll（弧度）。</summary>
    public float TransformRoll { get; set; }

    /// <summary>最后更新时间（UTC）。</summary>
    public System.DateTime UpdatedAt { get; set; }
}
