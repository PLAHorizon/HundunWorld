using System;
using System.Threading.Tasks;
using Orleans;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// Zone 分片 grain 契约（P2-b）。<br/>
/// 一个 ZoneShard 负责若干 ChunkCell 的权威状态 + AOI 订阅 + 批量扇出。
/// Grain Primary Key = shardId（由上层根据 chunk 哈希分配）。
/// </summary>
/// <remarks>
/// 本契约只暴露 P2-b 所需的订阅/扇出/tick 操作。P3+ 会扩展：
/// <list type="bullet">
///   <item>持久化 chunk op log（到 SQL Server 的 <c>chunk_state</c> 表）。</item>
///   <item>与 <see cref="IWorldDiffLogGrain"/> 对接产生 <see cref="WorldChunkDiffPacket"/>。</item>
/// </list>
/// </remarks>
[global::Orleans.CodeGeneration.Version(1)]
public interface IZoneShardGrain : IGrainWithIntegerKey
{
    /// <summary>给 sessionId 订阅一组 chunk（Morton 键数组）。返回新增订阅条数。</summary>
    Task<int> SubscribeSessionAsync(long sessionId, ulong[] mortonKeys);

    /// <summary>给 sessionId 退订一组 chunk。返回移除条数。</summary>
    Task<int> UnsubscribeSessionAsync(long sessionId, ulong[] mortonKeys);

    /// <summary>会话整体离线清理。返回被移除的条数。</summary>
    Task<int> RemoveSessionAsync(long sessionId);

    /// <summary>
    /// 对一批 <see cref="WorldChunkDiffPacket"/> 做扇出，返回"每个目标 sessionId 收到哪些 diff 下标"的映射，
    /// 上层据此走 Gateway 本地扇出或 stream pub-sub。
    /// </summary>
    /// <param name="diffs">本批待广播的 diff；每个元素的 <see cref="WorldChunkDiffPacket.ChunkMortonKey"/> 决定目标。</param>
    Task<FanOutResult[]> BroadcastChunkDiffsAsync(WorldChunkDiffPacket[] diffs);

    /// <summary>返回订阅了给定 chunk 的 sessionId 数组（诊断 / 监控用）。</summary>
    Task<long[]> GetSubscribersAsync(ulong mortonKey);

    /// <summary>返回 (会话数, chunk 数) 快照。</summary>
    Task<(int SessionCount, int ChunkCount)> GetStatsAsync();

    /// <summary>
    /// 注册一个 <see cref="IZoneShardFanoutObserver"/> 到本分片（P6-b 运行时连线）。<br/>
    /// 每次 <see cref="BroadcastChunkDiffsAsync"/> 产生扇出时，grain 会遍历所有已注册观察者并调用
    /// <see cref="IZoneShardFanoutObserver.OnChunkDiffAsync"/>。一个 gateway 实例用一个固定
    /// <paramref name="subscriptionId"/> 多次注册等价于覆盖（幂等）。
    /// </summary>
    Task SubscribeFanoutAsync(Guid subscriptionId, IZoneShardFanoutObserver observer);

    /// <summary>按 <paramref name="subscriptionId"/> 退订本分片 fanout（通常在 gateway 下线/关闭时调用）。</summary>
    Task UnsubscribeFanoutAsync(Guid subscriptionId);

    /// <summary>
    /// 执行一次 tick 周期：回放所有已注册实体的输入序列，校验位置偏差并生成 correction。
    /// </summary>
    /// <param name="tickTime">本次 tick 的模拟时间戳（秒）。</param>
    /// <returns>本次 tick 处理的实体数量。</returns>
    Task<int> TickAsync(double tickTime);

    /// <summary>
    /// 注册一个模拟实体到本分片。
    /// </summary>
    /// <param name="entityId">实体 ID。</param>
    /// <param name="initialX">初始位置 X。</param>
    /// <param name="initialY">初始位置 Y。</param>
    /// <param name="initialZ">初始位置 Z。</param>
    /// <param name="maxSpeed">最大水平速度（米/秒）。</param>
    Task RegisterEntityAsync(ulong entityId, float initialX, float initialY, float initialZ, float maxSpeed = 6f);

    /// <summary>
    /// 原子进入世界：先建立角色的初始 AOI 订阅，再注册权威实体并下发 ECS 生命周期基线。
    /// </summary>
    /// <param name="sessionId">接收同步的会话 ID（当前为角色 ID）。</param>
    /// <param name="entityId">要注册的权威实体 ID。</param>
    /// <param name="initialX">初始 X 坐标。</param>
    /// <param name="initialY">初始 Y 坐标。</param>
    /// <param name="initialZ">初始 Z 坐标。</param>
    /// <param name="initialInterestChunks">初始 AOI 兴趣集。</param>
    /// <param name="maxSpeed">最大水平速度（米/秒）。</param>
    Task EnterWorldAsync(
        long sessionId,
        ulong entityId,
        float initialX,
        float initialY,
        float initialZ,
        ulong[] initialInterestChunks,
        float maxSpeed = 6f);

    /// <summary>
    /// 从本分片注销一个模拟实体。
    /// </summary>
    /// <param name="entityId">实体 ID。</param>
    Task UnregisterEntityAsync(ulong entityId);

    /// <summary>
    /// 批量续约实体租约。<br/>
    /// 网关每 20 秒调用一次，为所有在线角色的实体续约。<br/>
    /// 超过租约期（默认 90 秒）未续约的实体将被视为孤儿实体（网关崩溃/断线未清理），
    /// 由 <see cref="ZoneShardGrain.TickAsync"/> 自动注销并广播 Despawn。
    /// </summary>
    /// <param name="entityIds">需要续约的实体 ID 列表。</param>
    /// <returns>实际续约的实体数量（不存在的实体会被跳过）。</returns>
    Task<int> RenewLeaseAsync(ulong[] entityIds);

    /// <summary>
    /// 向实体追加输入包（由上层调用，通常在收到客户端 input 时）。
    /// </summary>
    /// <param name="entityId">目标实体 ID。</param>
    /// <param name="input">输入包。</param>
    /// <param name="reportedEndX">客户端报告的终点 X。</param>
    /// <param name="reportedEndY">客户端报告的终点 Y。</param>
    /// <param name="reportedEndZ">客户端报告的终点 Z。</param>
    Task SubmitInputAsync(ulong entityId, InputPacket input, float reportedEndX, float reportedEndY, float reportedEndZ);

    Task SubmitSkillCastAsync(ulong entityId, int skillId, ulong targetId);

    Task CompleteSkillCastAsync(ulong entityId, float damage, ulong targetId, bool isCritical);

    /// <summary>
    /// 生成并广播一条交互槽状态同步包（阶段 5）。
    /// 构造 <see cref="InteractionSyncPacket"/> 并通过现有 fanout 机制推送到 AOI 兴趣集内的玩家，
    /// 参照 <see cref="EventPacket"/> 的 fanout 模式。
    /// </summary>
    /// <param name="slotIdx">交互槽索引（同一 InteractableId 下可有多个槽位）。</param>
    /// <param name="interactableId">可交互对象的 NetworkId。</param>
    /// <param name="interactorId">交互者（玩家）的 NetworkId。</param>
    /// <param name="stateBits">交互状态位标志（占用/进行中/结束/被抢占等）。</param>
    /// <param name="serverTick">本包对应的服务器 tick；传 0 时由 grain 使用当前 tick。</param>
    Task GenerateInteractionSync(int slotIdx, long interactableId, long interactorId, byte stateBits, long serverTick);

    // ===== Task B.5：角色同步扩展 =====

    /// <summary>
    /// Task B.5.4：触发动画 Montage 事件（开始/结束），写入待下发队列，由下次 TickAsync 下发。
    /// </summary>
    /// <param name="entityId">目标实体 ID。</param>
    /// <param name="montageId">Montage 资源 ID（0 表示停止当前 Montage）。</param>
    /// <param name="animInstanceId">动画实例 ID。</param>
    /// <param name="playRate">播放速率。</param>
    /// <param name="isLooping">是否循环。</param>
    Task TriggerMontageAsync(ulong entityId, uint montageId, uint animInstanceId, float playRate = 1f, bool isLooping = false);

    /// <summary>
    /// Task B.5.3：更新角色扩展属性（Mana/Level/Exp/Stamina 等），变化将在下次 TickAsync 通过 EntityState 下发。
    /// </summary>
    /// <param name="entityId">目标实体 ID。</param>
    /// <param name="mana">当前法力值（-1 表示不更新）。</param>
    /// <param name="maxMana">最大法力值（-1 表示不更新）。</param>
    /// <param name="level">等级（-1 表示不更新）。</param>
    /// <param name="exp">经验值（-1 表示不更新）。</param>
    /// <param name="stamina">当前体力值（-1 表示不更新）。</param>
    /// <param name="maxStamina">最大体力值（-1 表示不更新）。</param>
    /// <param name="hp">当前生命值（-1 表示不更新）。</param>
    /// <param name="maxHp">最大生命值（-1 表示不更新）。</param>
    /// <param name="stateBits">状态位掩码（uint.MaxValue 表示不更新）。</param>
    Task UpdateCharacterAttributesAsync(
        ulong entityId,
        int mana = -1, int maxMana = -1,
        int level = -1, long exp = -1,
        int stamina = -1, int maxStamina = -1,
        int hp = -1, int maxHp = -1,
        uint stateBits = uint.MaxValue);

    /// <summary>
    /// Task B.5.2：更新角色移动状态（移动模式/水平速度）。
    /// </summary>
    /// <param name="entityId">目标实体 ID。</param>
    /// <param name="mode">移动模式。</param>
    /// <param name="velX">水平速度 X。</param>
    /// <param name="velY">水平速度 Y。</param>
    Task UpdateMovementStateAsync(ulong entityId, MovementMode mode, float velX, float velY);

    // ===== Task C.4：场景对象状态管理 =====

    /// <summary>
    /// Task C.4.2：处理客户端场景对象交互意图。
    /// 校验冷却/归属/状态合法性后更新 StateBits/OwnerCharacterId/CooldownEndTick，
    /// 生成 <see cref="SceneObjectSyncPacket"/> 并通过 <see cref="BroadcastSceneObjectSyncAsync"/> 下发。
    /// </summary>
    /// <param name="interactorId">交互者（玩家）角色 ID。</param>
    /// <param name="objectId">场景对象 ID。</param>
    /// <param name="intentBits">交互意图状态位（低 4 位有效，参考 <see cref="SceneObjectStateBits"/>）。</param>
    /// <returns>true 表示交互成功并已下发；false 表示校验失败。</returns>
    Task<bool> HandleSceneObjectInteract(ulong interactorId, ulong objectId, uint intentBits);

    /// <summary>
    /// Task C.4：注册场景对象到本分片（初始化时由上层调用，填充内部状态表）。
    /// </summary>
    /// <param name="objectId">场景对象 ID。</param>
    /// <param name="objectType">对象类型。</param>
    /// <param name="initialStateBits">初始状态位。</param>
    /// <param name="transformX">初始位置 X（0 表示无 Transform）。</param>
    /// <param name="transformY">初始位置 Y。</param>
    /// <param name="transformZ">初始位置 Z。</param>
    Task RegisterSceneObjectAsync(ulong objectId, SceneObjectType objectType, uint initialStateBits,
        float transformX = 0f, float transformY = 0f, float transformZ = 0f);

    /// <summary>
    /// Task 19：返回当前 ZoneShard 的负载指标快照（实体数/会话数/chunk 数/上次 tick 耗时等）。
    /// 用于未来 sharding 路由决策和监控。
    /// </summary>
    Task<ZoneShardLoadMetrics> GetLoadMetricsAsync();
}

/// <summary>扇出结果：一个 session 收到哪些 diff（按原 <c>diffs</c> 数组下标表示）。</summary>
[GenerateSerializer]
public readonly record struct FanOutResult(
    [property: Id(0)] long SessionId,
    [property: Id(1)] int[] DiffIndices);
