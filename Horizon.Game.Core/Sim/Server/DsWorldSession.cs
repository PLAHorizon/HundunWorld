using System;
using System.Collections.Generic;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Game.Sim;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>DS 世界会话配置。</summary>
public sealed class DsWorldSessionOptions
{
    /// <summary>本 DS 承载的 shard ID。</summary>
    public long ShardId { get; set; }

    /// <summary>模拟频率（Hz）。Orleans 权威与 UE5 DS 均为 60，必须一致。</summary>
    public int TicksPerSecond { get; set; } = 60;

    /// <summary>快照广播间隔（tick）。3 → 20Hz，与 Orleans <c>ZoneShardGrain</c> 的
    /// <c>SnapshotBroadcastIntervalTicks</c> 保持一致（MMORPG 工业标准；60Hz 全速广播会
    /// 让单 tick 超帧预算）。</summary>
    public int SnapshotIntervalTicks { get; set; } = 3;

    /// <summary>玩家默认最大水平速度（米/秒），与 <see cref="MovementFormula.DefaultMaxSpeed"/> 一致。</summary>
    public float PlayerMaxSpeed { get; set; } = MovementFormula.DefaultMaxSpeed;

    /// <summary>玩家默认最大生命值。</summary>
    public int PlayerDefaultMaxHp { get; set; } = 100;

    /// <summary>单机实体上限（防御性：防止恶意握手刷屏撑爆内存）。</summary>
    public int MaxEntities { get; set; } = 20000;

    /// <summary>
    /// 单 DS 软容量上限（会话数）。超过时 <see cref="DsWorldSession.AddPlayer"/> 拒绝新玩家加入，
    /// 返回 reason="shard_full"，客户端/网关可据此重路由到其他 DS。
    /// <para>
    /// 与 <see cref="MaxEntities"/>（硬上限，含怪物）区分：<c>MaxSessions</c> 专指玩家会话数，
    /// 默认 5000 足以覆盖单 shard 正常负载，超限说明路由分配异常或遭受握手洪峰。
    /// </para>
    /// </summary>
    public int MaxSessions { get; set; } = 5000;

    /// <summary>玩家攻击力（M4 战斗；生产应由 CharacterGrain 的属性快照下发）。</summary>
    public float PlayerAttackPower { get; set; } = 10f;

    /// <summary>玩家防御力。</summary>
    public float PlayerDefense { get; set; } = 0f;

    /// <summary>玩家暴击率 [0,1]。</summary>
    public float PlayerCritRate { get; set; } = 0.1f;

    /// <summary>玩家闪避率 [0,1]（玩家侧闪避，用于怪物攻击玩家时判定）。</summary>
    public float PlayerDodgeRate { get; set; } = 0.05f;

    /// <summary>玩家正面扇区余弦阈值（120° 扇区 → cos60° = 0.5）。</summary>
    public float FacingCosThreshold { get; set; } = 0.5f;

    /// <summary>普通攻击距离（米）。</summary>
    public float PlayerAttackRange { get; set; } = 3f;

    /// <summary>玩家死亡后的复活延迟（秒）。</summary>
    public float PlayerRespawnDelaySeconds { get; set; } = 8f;

    /// <summary>
    /// 是否启用 AOI（Area Of Interest）分桶快照裁剪。
    /// <para>
    /// <c>false</c>（默认）：走原有全量路径——所有在线会话共享同一份全世界快照，
    /// 现有测试与行为完全不受影响（向后兼容开关）。<br/>
    /// <c>true</c>：接入 <see cref="ZoneShardAoi"/>，按玩家可见 chunk 分组裁剪快照，
    /// 把每 tick 的下行实体量从 O(N²)（人人看全世界）降到 O(N×K)（每人只看视野内），
    /// 相同可见集合的会话合并为一组、每组只编码一次字节（保留「一份字节多受众」不变量）。
    /// </para>
    /// </summary>
    public bool EnableAoiFilter { get; set; } = false;

    /// <summary>AOI chunk 边长（米）。默认 16，与 <see cref="WorldCoord.MetresPerChunkCell"/>（Orleans <c>ZoneShardGrain</c>）一致。</summary>
    public int AoiChunkSize { get; set; } = 16;

    /// <summary>
    /// AOI 视野半径（chunk 数）。默认 28（≈448m），与 Orleans 路径 <c>SyncHandshakeHandler.InitialAoiRadiusChunks</c> 一致。
    /// <para>
    /// 本 DS 世界为 Z-up 平面世界（怪物仅在 X/Y 平面移动，实体 Z≈0），故订阅在**水平面**上按
    /// <c>(2r+1)²</c> 展开（r=28 → 57² = 3249 chunk），而非 3D 立方体的 <c>(2r+1)³</c>（185193 chunk）——
    /// 后者对 tick 线程的每帧维护成本过高，且平面世界的垂直轴只有单一 chunk 层，无收益。
    /// </para>
    /// </summary>
    public int AoiRadiusChunks { get; set; } = 28;
}

/// <summary>DS 侧玩家会话。</summary>
public sealed class DsPlayer
{
    /// <summary>网关会话 ID（快照/事件的下行受众标识）。</summary>
    public long SessionId { get; init; }

    /// <summary>角色 ID（= <see cref="EntityId"/>，与既有 Orleans 语义一致）。</summary>
    public ulong CharacterId { get; init; }

    /// <summary>世界实体 ID。</summary>
    public ulong EntityId { get; init; }

    /// <summary>最近一次处理到的客户端 tick。</summary>
    public long LastProcessedClientTick { get; set; }

    /// <summary>
    /// 已入队（交给 <see cref="SimWorld"/>）的最大 clientTick —— 用于**输入去重**。
    /// <para>
    /// 语义与 <see cref="LastProcessedClientTick"/> 不同：后者是"已回放完成"的水位（来自 step 结果），
    /// 本字段是"已接收"的水位（提交时立即更新）。去重必须用后者：
    /// 同一 tick 内连续到达的重复帧尚未被 Step 处理，用"已处理"水位无法拦住它们。
    /// </para>
    /// </summary>
    public long LastQueuedClientTick { get; set; }

    /// <summary>当前生命值。</summary>
    public int Hp { get; set; }

    /// <summary>最大生命值。</summary>
    public int MaxHp { get; set; }

    /// <summary>
    /// 角色昵称（网关在握手时从 CharacterGrain 取的权威值）。
    /// 空 = 档案未取到，spawn 时由客户端回退 <c>Player_&lt;id&gt;</c> 占位。
    /// </summary>
    public string Nickname { get; set; } = "";

    /// <summary>角色等级（权威；0 = 未知，不下发）。</summary>
    public int Level { get; set; }

    /// <summary>角色称号名（空 = 未佩戴）。</summary>
    public string Title { get; set; } = "";

    /// <summary>上次已随快照广播出去的昵称/称号（v13 变更检测基线；null = 从未广播）。</summary>
    private string? _sentNickname;
    private string? _sentTitle;

    /// <summary>
    /// 若档案与上次广播不一致则返回增量（v13）。未变化的字段留 null，客户端据此保留原值。
    /// 昵称/称号在 Identity 里是 spawn-only，运行中变更只能走这条通道，
    /// 否则要靠重发 Spawn 生效，而重复 Spawn 会让客户端销毁重建远程角色。
    /// </summary>
    /// <param name="spawnedThisFrame">本帧已发 Spawn 时 Identity 自带档案，只需对齐基线、不再重复下发</param>
    public ProfileAuthComponent? TakeProfileChange(bool spawnedThisFrame = false)
    {
        var nicknameChanged = Nickname != _sentNickname;
        var titleChanged = Title != _sentTitle;
        if (!nicknameChanged && !titleChanged) return null;

        _sentNickname = Nickname;
        _sentTitle = Title;
        if (spawnedThisFrame) return null;

        return new ProfileAuthComponent
        {
            Nickname = nicknameChanged ? Nickname : null,
            Title = titleChanged ? Title : null,
            Level = 0,
        };
    }

    /// <summary>是否已在**共享广播路径**下发过 Spawn（用于首次全量 vs 后续 Update）。</summary>
    /// <remarks>
    /// 实体级全局标志，不代表"每个会话都见过"——晚加入/重连会话由
    /// <see cref="DsWorldSession"/> 的会话级首包强制 Spawn 补发（<c>InitialSpawnSnapshots</c>）覆盖。
    /// </remarks>
    public bool SpawnedToClients { get; set; }

    /// <summary>出生点 X（复活锚点）。</summary>
    public float SpawnX { get; set; }

    /// <summary>出生点 Y。</summary>
    public float SpawnY { get; set; }

    /// <summary>出生点 Z。</summary>
    public float SpawnZ { get; set; }

    /// <summary>允许复活的服务器 tick（0 = 存活）。</summary>
    public long RespawnAtTick { get; set; }

    /// <summary>是否处于死亡状态。</summary>
    public bool IsDead => Hp <= 0;

    /// <summary>
    /// 最近一次收到该会话输入的 UTC 墙钟（会话存活判据，非模拟确定性输入）。
    /// 客户端 InputPacket 为定频上行（挂机也持续发送），长时间无输入 = 连接早已消失
    /// 而网关未能送达"会话结束"通知（如网关崩溃/强杀）→ 宿主按租约回收，防幽灵角色。
    /// </summary>
    public DateTime LastInputUtc;

    // ---- AOI 订阅状态（仅 <see cref="DsWorldSessionOptions.EnableAoiFilter"/> 为 true 时有效）----

    /// <summary>当前订阅中心 chunk 的 X 块坐标（用于跨越边界时退订旧 chunk 集合）。</summary>
    public int AoiChunkX { get; set; }

    /// <summary>当前订阅中心 chunk 的 Y 块坐标。</summary>
    public int AoiChunkY { get; set; }

    /// <summary>当前订阅中心 chunk 的 Z（垂直层）块坐标。</summary>
    public int AoiChunkZ { get; set; }

    /// <summary>是否已建立 AOI 订阅（用于自愈：漏订阅时在下一 tick 补订）。</summary>
    public bool AoiSubscribed { get; set; }
}

/// <summary>DS 侧怪物实体（权威状态由 <see cref="MonsterAiCore"/> 驱动）。</summary>
public sealed class DsMonster
{
    /// <summary>世界实体 ID（由 <see cref="SpawnDirectorCore.ComputeEntityId"/> 确定性分配）。</summary>
    public ulong EntityId { get; init; }

    /// <summary>来源刷怪点。</summary>
    public int SpawnPointId { get; init; }

    /// <summary>槽位序号。</summary>
    public int SlotIndex { get; init; }

    /// <summary>怪物模板 ID。</summary>
    public int TemplateId { get; set; }

    /// <summary>AI 状态（权威）。</summary>
    public MonsterAiSelf Ai { get; set; }

    /// <summary>是否已在**共享广播路径**下发过 Spawn。</summary>
    /// <remarks>同 <see cref="DsPlayer.SpawnedToClients"/>：实体级全局标志，晚加入者由会话级补发覆盖。</remarks>
    public bool SpawnedToClients { get; set; }
}

/// <summary>单个 DS tick 的产出。</summary>
public sealed class DsTickResult
{
    /// <summary>本 tick 的服务器 tick 号。</summary>
    public long ServerTick { get; init; }

    /// <summary>本 tick 是否到达快照广播节拍。</summary>
    public bool SnapshotDue { get; init; }

    /// <summary>
    /// 世界快照（全量，<c>BaselineTick = 0</c>）。**所有在线会话共享同一份**——
    /// 由宿主编码一次、按受众列表下发，避免 O(会话) 重复编码。
    /// </summary>
    public SnapshotPacket? WorldSnapshot { get; init; }

    /// <summary>本次快照的受众会话 ID（快照为 null 时为空）。</summary>
    public long[] AudienceSessionIds { get; init; } = Array.Empty<long>();

    /// <summary>
    /// AOI 分桶快照分组（仅 <see cref="DsWorldSessionOptions.EnableAoiFilter"/> 为 true 且到达快照节拍时非空）。
    /// <para>
    /// 每组一份 <see cref="SnapshotPacket"/> + 该组受众列表；宿主对每组**只编码一次字节**、按受众下发，
    /// 保留「一份字节多受众」不变量。AOI 关闭时为 null，此时仍用 <see cref="WorldSnapshot"/>/<see cref="AudienceSessionIds"/>。
    /// </para>
    /// </summary>
    public IReadOnlyList<DsSnapshotGroup>? SnapshotGroups { get; init; }

    /// <summary>
    /// 会话级首包强制 Spawn 快照（晚加入/重连补发；每项**仅发给对应会话**）。
    /// <para>
    /// 所有实体的 delta 均为 <see cref="EntityDeltaKind.Spawn"/> 且携带 Identity；
    /// AOI 开启时按该会话可见 chunk 集合裁剪，关闭时为全世界实体。
    /// 与共享快照不同（会话级 Spawn 状态无法在多受众间共享同一份字节），逐会话构建；
    /// 会话加入/重连为低频事件，构建成本可接受。
    /// </para>
    /// </summary>
    public IReadOnlyList<DsInitialSpawnSnapshot>? InitialSpawnSnapshots { get; init; }

    /// <summary>
    /// 离线实体的 Despawn 全广播快照（<c>RemovePlayer</c> 后的首个 tick 产出；无受众时为 null）。
    /// <para>
    /// 修复"客户端离线后其他客户端仍能看到其角色"：客户端据此销毁远端角色 Actor。
    /// 全广播（当前全部在线会话）、不做 AOI 过滤——镜像 Orleans 侧 ZoneShardGrain 的
    /// 「Despawn 全广播」经验（离线玩家所在 chunk 可能已无订阅者，AOI 过滤会永久丢包）。
    /// </para>
    /// </summary>
    public SnapshotPacket? DespawnBroadcast { get; init; }

    /// <summary>Despawn 广播受众（当前全部在线会话；升序）。</summary>
    public long[] DespawnBroadcastAudience { get; init; } = Array.Empty<long>();

    /// <summary>需要定向下发的修正包（含目标会话）。</summary>
    public IReadOnlyList<(long SessionId, CorrectionPacket Packet)> Corrections { get; init; }
        = Array.Empty<(long, CorrectionPacket)>();

    /// <summary>需要定向下发的 InputAck（含目标会话）。</summary>
    public IReadOnlyList<(long SessionId, long LastProcessedClientTick)> InputAcks { get; init; }
        = Array.Empty<(long, long)>();

    /// <summary>本 tick 新生成的怪物实体 ID（诊断/事件用）。</summary>
    public IReadOnlyList<ulong> SpawnedMonsters { get; init; } = Array.Empty<ulong>();

    /// <summary>
    /// 本 tick 需要下发的**定向下行包**（伤害 / 死亡 / 掉落表现等），每项自带受众。
    /// <para>统一为 (受众, 包) 二元组，使 <c>DsDownlinkEncoder</c> 无需为每种事件各写一条分支。</para>
    /// </summary>
    public IReadOnlyList<(long[] Audience, SyncPacket Packet)> Events { get; init; }
        = Array.Empty<(long[], SyncPacket)>();

    /// <summary>
    /// 本 tick 产生的**跨层玩法事件**（击杀/掉落/玩家死亡），由宿主上报网关转 Orleans 业务层。
    /// </summary>
    public IReadOnlyList<DsGameplayEvent> GameplayEvents { get; init; } = Array.Empty<DsGameplayEvent>();

    /// <summary>在线玩家数。</summary>
    public int PlayerCount { get; init; }

    /// <summary>存活怪物数。</summary>
    public int MonsterCount { get; init; }
}

/// <summary>
/// AOI 分桶快照的一个分组：一份快照字节 + 共享该快照的受众会话列表。
/// <para>
/// 同一组内的所有会话拥有**完全相同的可见 chunk 集合**，因此看到相同的实体集合——
/// 只需编码一次 <see cref="Snapshot"/> 即可下发给 <see cref="AudienceSessionIds"/> 的全部会话。
/// </para>
/// </summary>
public sealed class DsSnapshotGroup
{
    /// <summary>本组的快照包（全量，<c>BaselineTick = 0</c>；实体按 EntityId 升序）。</summary>
    public SnapshotPacket Snapshot { get; init; } = null!;

    /// <summary>本组受众会话 ID（升序）。</summary>
    public long[] AudienceSessionIds { get; init; } = Array.Empty<long>();
}

/// <summary>
/// 会话级首包强制 Spawn 快照：为晚加入/重连的**单个会话**准备的全量快照
/// （其中所有实体的 delta 均为 <see cref="EntityDeltaKind.Spawn"/> 且携带 Identity），
/// 用于修复"实体级全局 Spawn 标志导致晚加入者收不到 Spawn、玩家间互相不可见"。
/// </summary>
public sealed class DsInitialSpawnSnapshot
{
    /// <summary>目标会话（= 角色 ID）。</summary>
    public long SessionId { get; init; }

    /// <summary>专属快照（全量，<c>BaselineTick = 0</c>；实体按 EntityId 升序）。</summary>
    public SnapshotPacket Snapshot { get; init; } = null!;
}

/// <summary>
/// DS 权威世界会话（MMORPG 开发计划 P3/M3）—— 无头 DS 宿主的**心脏**。
/// <para>
/// 职责：持有本 shard 的玩家与怪物权威状态，按 60Hz 推进模拟，按 20Hz 产出可下发快照，
/// 并把修正/InputAck 定向回各自的会话。
/// </para>
/// <para>
/// <b>为什么与 <c>ZoneShardGrain</c> 并存而不是替换</b>：延续项目「新增并行通道 → 对拍验证 →
/// 灰度切换 → 旧通道退役」纪律。本类与 <c>ZoneShardGrain</c> 共用同一份
/// <see cref="SimWorld"/> / <see cref="MonsterAiCore"/> / <see cref="SpawnDirectorCore"/>，
/// 因此两端行为天然一致，灰度期可以直接对拍；切换由网关的 <c>UseDS</c> 开关控制。
/// </para>
/// <para>
/// <b>确定性</b>：怪物生成与 AI 全部走 <see cref="SpawnDirectorCore"/> /
/// <see cref="MonsterAiCore"/> 的纯函数内核；遍历顺序一律按 EntityId 升序固定。
/// 玩家移动走 <see cref="SimWorld"/>（与客户端预测、Orleans 权威同源）。
/// </para>
/// <para>
/// <b>线程模型</b>：非线程安全。宿主必须在**单一 tick 线程**上调用
/// <see cref="AddPlayer"/> / <see cref="SubmitInput"/> / <see cref="Tick"/>；
/// 网络线程只应把消息投递到该线程的队列（与 Orleans grain 的单线程 turn 模型语义一致，
/// 避免把 Orleans 的并发正确性假设换成一套新的锁竞争）。
/// </para>
/// </summary>
public sealed class DsWorldSession
{
    /// <summary>实体类型：玩家。</summary>
    public const int EntityTypePlayer = 1;

    /// <summary>实体类型：怪物。</summary>
    public const int EntityTypeMonster = 2;

    private readonly DsWorldSessionOptions _options;
    private readonly SimWorld _simWorld = new();
    private readonly Dictionary<long, DsPlayer> _playersBySession = new();
    private readonly Dictionary<ulong, DsMonster> _monstersById = new();
    private readonly Dictionary<ulong, long> _sessionByEntityId = new();

    /// <summary>
    /// 待补发"首包强制 Spawn"的会话（会话加入 / 重复握手（重连）/ 重连恢复时登记；
    /// 在下一个快照节拍各自收到一份专属全量强制 Spawn 快照后清空）。
    /// <para>
    /// 背景（多客户端互相不可见根因修复）：首包 Spawn/Identity 决策基于实体级全局标志
    /// <see cref="DsPlayer.SpawnedToClients"/>——只对"首次收到该实体的那次广播的受众"有效；
    /// 晚加入/重连的会话只会收到后续 Update（无 Identity），客户端无法创建远端实体。
    /// </para>
    /// </summary>
    private readonly HashSet<long> _pendingInitialSpawnSessions = new();

    /// <summary>
    /// 待全广播的离线实体 ID（<see cref="RemovePlayer"/> 登记，下一 tick 产出 Despawn 广播）。
    /// <para>
    /// 镜像 Orleans 侧 <c>ZoneShardGrain</c>「离线角色永久残留」修复经验：Despawn 必须
    /// **全广播、不做 AOI 过滤**——离线玩家所在 chunk 的订阅关系可能已随其移除而清空，
    /// 若按 AOI 过滤，附近观察者将永远收不到 Despawn，离线角色在其他客户端永久残留。
    /// </para>
    /// </summary>
    private readonly List<ulong> _pendingDespawnEntityIds = new();
    private readonly List<SpawnPointConfig> _spawnPoints = new();
    private readonly Dictionary<int, SpawnSlotState[]> _spawnSlots = new();
    private readonly IDsLootProvider _lootProvider;

    private readonly List<(long[] Audience, SyncPacket Packet)> _events = new();
    private readonly List<DsGameplayEvent> _gameplayEvents = new();

    // ── Sprint 4：tick 线程复用的快照构建缓冲（减少每 tick 的 List 分配 GC 压力）──
    private readonly List<EntityDelta> _snapshotDeltaBuffer = new();
    private readonly List<ulong> _snapshotIdBuffer = new();

    /// <summary>
    /// AOI 订阅表（复用 Orleans 路径已生产验证的 <see cref="ZoneShardAoi"/>）。
    /// <para>非线程安全：与本会话一致，只在 tick 线程上访问。</para>
    /// </summary>
    private readonly ZoneShardAoi _aoi = new();

    /// <summary>解析后的 AOI chunk 边长（米，&gt;0）。</summary>
    private readonly int _aoiChunkSize;

    /// <summary>解析后的 AOI 视野半径（chunk 数，&ge;0）。</summary>
    private readonly int _aoiRadiusChunks;

    private long _tick;

    /// <summary>创建世界会话。</summary>
    /// <param name="options">配置。</param>
    /// <param name="lootProvider">掉落掷骰来源；null 使用确定性默认实现。</param>
    public DsWorldSession(DsWorldSessionOptions options, IDsLootProvider? lootProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _lootProvider = lootProvider ?? new DeterministicLootProvider();
        if (_options.TicksPerSecond <= 0) _options.TicksPerSecond = 60;
        if (_options.SnapshotIntervalTicks <= 0) _options.SnapshotIntervalTicks = 1;
        _aoiChunkSize = _options.AoiChunkSize > 0 ? _options.AoiChunkSize : 16;
        _aoiRadiusChunks = _options.AoiRadiusChunks >= 0 ? _options.AoiRadiusChunks : 0;
    }

    /// <summary>本 DS 承载的 shard。</summary>
    public long ShardId => _options.ShardId;

    /// <summary>当前服务器 tick。</summary>
    public long ServerTick => _tick;

    /// <summary>在线玩家数。</summary>
    public int PlayerCount => _playersBySession.Count;

    /// <summary>当前怪物数。</summary>
    public int MonsterCount => _monstersById.Count;

    /// <summary>累计拒绝的握手数（容量溢出）。</summary>
    public long RejectedHandshakeCount { get; private set; }

    /// <summary>配置刷怪点（按 SpawnPointId 升序内部排序，保证确定性）。</summary>
    public void ConfigureSpawnPoints(IReadOnlyList<SpawnPointConfig> points)
    {
        _spawnPoints.Clear();
        _spawnSlots.Clear();
        if (points is null) return;

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.MaxAlive <= 0) continue;
            if (p.SpawnPointId <= 0) continue; // 0 与"无实体"哨兵冲突，拒绝
            _spawnPoints.Add(p);
        }

        // 按 SpawnPointId 升序：决定生成顺序，进而决定实体 ID 与快照顺序 → 必须稳定。
        _spawnPoints.Sort((a, b) => a.SpawnPointId.CompareTo(b.SpawnPointId));

        foreach (var p in _spawnPoints)
        {
            _spawnSlots[p.SpawnPointId] = SpawnDirectorCore.CreateSlots(p.MaxAlive);
        }
    }

    /// <summary>注册一个玩家会话（对应网关的 Handshake 上行）。</summary>
    /// <param name="sessionId">网关会话 ID。</param>
    /// <param name="characterId">角色 ID。</param>
    /// <param name="x">初始 X。</param>
    /// <param name="y">初始 Y。</param>
    /// <param name="z">初始 Z。</param>
    /// <param name="reason">失败原因。</param>
    public bool AddPlayer(long sessionId, ulong characterId, float x, float y, float z, out string reason,
        string nickname = "", int level = 0, string title = "")
    {
        reason = string.Empty;

        if (sessionId <= 0) { reason = "bad_session_id"; return false; }
        if (characterId == 0UL) { reason = "bad_character_id"; return false; }

        // 软容量上限：超过时拒绝新玩家加入，客户端/网关可根据 reason 重路由到其他 DS。
        if (_playersBySession.Count >= _options.MaxSessions)
        {
            reason = "shard_full";
            RejectedHandshakeCount++;
            return false;
        }

        // 硬上限防御（含怪物在内的实体总量不应超过此值）。
        if (_playersBySession.Count + _monstersById.Count >= _options.MaxEntities)
        {
            reason = "capacity";
            RejectedHandshakeCount++;
            return false;
        }

        // 玩家实体 ID 直接使用 characterId（与既有 Orleans 语义一致：一个角色 = 一个实体）。
        // 怪物实体 ID 高 32 位非 0，而角色 ID 实际为数据库自增（远小于 2^32），不会碰撞；
        // 这里仍做一次显式校验，避免将来 ID 空间演进时静默串体。
        if (characterId != 0UL && (characterId >> 32) != 0UL)
        {
            reason = "character_id_out_of_range";
            return false;
        }

        if (_monstersById.ContainsKey(characterId))
        {
            reason = "entity_id_collision";
            return false;
        }

        if (_playersBySession.TryGetValue(sessionId, out var existing))
        {
            if (existing.EntityId == characterId)
            {
                // 幂等重连：同一会话重复握手。客户端重连后已重建本地 ECS 世界（实体全清），
                // 必须登记首包强制 Spawn 补发，否则重连后只能收到 Update、远端实体不再出现。
                // 档案随本次握手重新下发（改名/换称号后重连也能拿到新值），空值不覆盖已有缓存。
                if (!string.IsNullOrEmpty(nickname)) existing.Nickname = nickname;
                if (level > 0) existing.Level = level;
                if (!string.IsNullOrEmpty(title)) existing.Title = title;
                _pendingInitialSpawnSessions.Add(sessionId);
                return true;
            }
            RemovePlayer(sessionId);
        }

        var player = new DsPlayer
        {
            SessionId = sessionId,
            CharacterId = characterId,
            EntityId = characterId,
            Hp = _options.PlayerDefaultMaxHp,
            MaxHp = _options.PlayerDefaultMaxHp,
            Nickname = nickname ?? "",
            Level = level,
            Title = title ?? "",
            // 去重水位初始为 -1：客户端首帧的 ClientTick 可能为 0，
            // 若从 0 起算会把**第一帧输入**误判为重复而丢弃（角色"起步不动"）。
            LastQueuedClientTick = -1L,
            SpawnX = x,
            SpawnY = y,
            SpawnZ = z,
            LastInputUtc = DateTime.UtcNow,
        };

        _playersBySession[sessionId] = player;
        _sessionByEntityId[characterId] = sessionId;
        _simWorld.AddOrUpdateEntity(characterId, x, y, z, _options.PlayerMaxSpeed);

        // AOI：根据初始位置订阅周围 chunk（仅开关开启时）。跨 chunk 边界的重订阅在 Tick 中维护。
        if (_options.EnableAoiFilter)
        {
            var (cx, cy, cz) = ChunkCoords(x, y, z);
            SubscribePlayerAoi(player, cx, cy, cz);
        }

        // 新会话：登记首包强制 Spawn（下一快照节拍补发全场/可见集 Spawn，修复晚加入不可见）。
        _pendingInitialSpawnSessions.Add(sessionId);
        return true;
    }

    /// <summary>移除玩家会话（断线/离线）。</summary>
    public bool RemovePlayer(long sessionId)
    {
        if (!_playersBySession.Remove(sessionId, out var player)) return false;
        _sessionByEntityId.Remove(player.EntityId);
        _simWorld.RemoveEntity(player.EntityId);

        // 离线实体必须广播 Despawn：登记后由下一 tick 全广播给其余在线会话，
        // 否则其他客户端收不到"该实体已消失"，离线角色永久残留（幽灵角色）。
        _pendingDespawnEntityIds.Add(player.EntityId);

        // AOI：整体退订该会话的所有 chunk（无订阅时为无操作，返回 0）。
        _aoi.RemoveSession(sessionId);

        // 待补发登记随会话一并清理（会话已离开，补发已无意义）。
        _pendingInitialSpawnSessions.Remove(sessionId);

        // 怪物把该玩家当目标时，下一 tick 的 IsEngageable 会返回 false 并自动脱战，
        // 无需在此逐个修正——保持“移除”这一操作 O(1)。
        return true;
    }

    /// <summary>
    /// 登记"下一次快照节拍为该会话补发一份专属强制 Spawn 全量快照"（重连恢复场景）。
    /// <para>重复登记幂等；会话未知（未握手/已移除）时无操作。</para>
    /// </summary>
    /// <returns>true = 已登记；false = 会话未知。</returns>
    public bool ScheduleInitialSpawn(long sessionId)
    {
        if (!_playersBySession.ContainsKey(sessionId)) return false;
        _pendingInitialSpawnSessions.Add(sessionId);
        return true;
    }

    /// <summary>当前全部玩家会话 ID（链路断开时宿主全量回收用）。</summary>
    public IReadOnlyList<long> GetSessionIds() => _playersBySession.Keys.ToList();

    /// <summary>
    /// 收集"输入租约过期"的会话：超过 <paramref name="lease"/> 未收到任何输入的玩家。
    /// 客户端输入为定频上行，长时间无输入即连接早已消失而网关未能通知（崩溃/强杀窗口），
    /// 由宿主投递回 tick 线程 RemovePlayer，防止幽灵角色永久驻留 DS 世界。
    /// </summary>
    public IReadOnlyList<long> CollectInputStarvedSessions(DateTime utcNow, TimeSpan lease)
    {
        if (_playersBySession.Count == 0) return Array.Empty<long>();
        List<long>? starved = null;
        foreach (var kv in _playersBySession)
        {
            if (utcNow - kv.Value.LastInputUtc > lease)
                (starved ??= new List<long>()).Add(kv.Key);
        }
        return starved ?? (IReadOnlyList<long>)Array.Empty<long>();
    }

    /// <summary>
    /// 提交一帧玩家输入（对应网关的 Input 上行）。
    /// <para>
    /// <b>去重是必须的，不是优化</b>：客户端 <c>InputSendSystem</c> 在服务端 Ack 落后时会
    /// **冗余重传全部未确认输入**（对抗丢包的设计行为），因此"同一 clientTick 被重复收到"是常态。
    /// 若重复帧被当作新帧入队，权威世界会按帧数重复推进——服务端跑得比客户端快，
    /// 于是**持续下发修正**，玩家表现为"被反复拉回"，即修正风暴。
    /// </para>
    /// <para>
    /// 判定规则：<c>clientTick</c> 不大于已入队水位即视为重复/迟到，直接丢弃。
    /// 这同时覆盖**重复到达**（重传）与**乱序迟到**（重传与后续包交错）两种弱网形态。
    /// </para>
    /// </summary>
    /// <returns>true = 已入队；false = 会话未知 / 空帧 / 重复或迟到被丢弃。</returns>
    public bool SubmitInput(long sessionId, InputPacket input)
    {
        if (input is null) return false;
        if (!_playersBySession.TryGetValue(sessionId, out var player)) return false;

        if (input.ClientTick <= player.LastQueuedClientTick)
        {
            DuplicateInputDroppedCount++;
            return false;
        }

        player.LastQueuedClientTick = input.ClientTick;
        player.LastInputUtc = DateTime.UtcNow;

        _simWorld.SubmitInput(
            player.EntityId,
            input,
            input.PredictedEndX,
            input.PredictedEndY,
            input.PredictedEndZ);
        return true;
    }

    /// <summary>累计被去重丢弃的重复/迟到输入帧数（监控：突增说明链路重传加剧）。</summary>
    public long DuplicateInputDroppedCount { get; private set; }

    /// <summary>推进一个 tick 并产出可下发的数据。</summary>
    public DsTickResult Tick()
    {
        _tick++;
        var dt = 1f / _options.TicksPerSecond;

        // 注意：**不要**在这里清空事件缓冲。
        // SubmitCombatAction 是在两次 Tick 之间被调用的，它产生的事件必须搭上"下一班车"
        // 被下发出去；若在 Tick 开头清空，这些事件会被静默吞掉（玩家看到怪掉血却没收到伤害包）。
        // 清空发生在构造完本 tick 结果之后（见本方法末尾）。
        var spawnedMonsters = TickSpawnDirector();
        TickMonsters(dt);
        TickPlayerRespawns();
        var stepResults = _simWorld.Step(_tick);

        var corrections = new List<(long, CorrectionPacket)>();
        var inputAcks = new List<(long, long)>();

        foreach (var step in stepResults)
        {
            if (!_sessionByEntityId.TryGetValue(step.EntityId, out var sessionId)) continue;
            var player = _playersBySession[sessionId];

            if (step.HadInput)
            {
                player.LastProcessedClientTick = step.LastProcessedClientTick;
                inputAcks.Add((sessionId, step.LastProcessedClientTick));
            }

            if (step.Correction is not null)
            {
                corrections.Add((sessionId, step.Correction));
            }
        }

        // AOI 维护：位置已由 Step 更新，检测玩家是否跨越 chunk 边界并重订阅。
        // 必须在快照构建之前完成，以保证 BuildSnapshotGroups 读到的是最新订阅。
        if (_options.EnableAoiFilter) UpdateAoiSubscriptions();

        var snapshotDue = (_tick % _options.SnapshotIntervalTicks) == 0;

        SnapshotPacket? snapshot = null;
        long[] audience = Array.Empty<long>();
        IReadOnlyList<DsSnapshotGroup>? snapshotGroups = null;
        IReadOnlyList<DsInitialSpawnSnapshot>? initialSpawns = null;
        if (snapshotDue && _playersBySession.Count > 0)
        {
            // 待补发首包 Spawn 的会话（加入/重连）：本 tick 走**专属**全量强制 Spawn 快照
            // （每会话一份字节），并从共享/分组路径的受众中剔除——
            // 共享路径按实体级全局标志只发"首次广播受众"，无法覆盖晚加入者（多客户端不可见根因）。
            List<long>? pendingInitialSpawn = _pendingInitialSpawnSessions.Count > 0
                ? new List<long>(_pendingInitialSpawnSessions)
                : null;

            if (_options.EnableAoiFilter)
            {
                // AOI 分桶路径：按可见 chunk 集合分组，每组一份快照（一份字节多受众）。
                snapshotGroups = BuildSnapshotGroups();
            }
            else
            {
                // 原有全量路径：所有会话共享同一份全世界快照（开关关闭时行为与修改前完全一致）。
                snapshot = BuildSnapshot();
                // 受众 = 全部会话 − 待补发会话（待补发会话改由专属快照服务）。
                var audienceList = new List<long>(_playersBySession.Count);
                foreach (var kv in _playersBySession)
                {
                    if (_pendingInitialSpawnSessions.Contains(kv.Key)) continue;
                    audienceList.Add(kv.Key);
                }
                audience = audienceList.ToArray();
                Array.Sort(audience);
            }

            initialSpawns = BuildInitialSpawnSnapshots(pendingInitialSpawn);

            // 已服务：清空待补发登记（期间已离开的会话由 RemovePlayer 清理；此处整体清空剩余项）。
            _pendingInitialSpawnSessions.Clear();
        }

        // 离线实体的 Despawn 全广播：不等快照节拍，紧随会话结束后的第一个 tick 立即下发，
        // 缩短"幽灵角色"在他人屏幕上的残留窗口（镜像 Orleans 侧做法）。
        SnapshotPacket? despawnBroadcast = null;
        var despawnAudience = Array.Empty<long>();
        if (_pendingDespawnEntityIds.Count > 0)
        {
            var audienceList = new List<long>(_playersBySession.Count);
            foreach (var kv in _playersBySession) audienceList.Add(kv.Key);
            audienceList.Sort();
            despawnAudience = audienceList.ToArray();

            // 无其他在线玩家时无需广播（实体已从世界移除，后续加入者不会收到其 Spawn）。
            if (despawnAudience.Length > 0)
            {
                var deltas = new EntityDelta[_pendingDespawnEntityIds.Count];
                for (var i = 0; i < deltas.Length; i++)
                {
                    deltas[i] = new EntityDelta
                    {
                        EntityId = _pendingDespawnEntityIds[i],
                        Kind = EntityDeltaKind.Despawn,
                    };
                }

                despawnBroadcast = new SnapshotPacket
                {
                    ProtocolVersion = SyncProtocolVersion.Current,
                    ServerTick = _tick,
                    BaselineTick = 0, // 0 = 全量（生命周期包，客户端逐 delta 应用）
                    Deltas = deltas,
                };
            }

            _pendingDespawnEntityIds.Clear();
        }

        var result = new DsTickResult
        {
            ServerTick = _tick,
            SnapshotDue = snapshotDue,
            WorldSnapshot = snapshot,
            AudienceSessionIds = audience,
            SnapshotGroups = snapshotGroups,
            InitialSpawnSnapshots = initialSpawns,
            DespawnBroadcast = despawnBroadcast,
            DespawnBroadcastAudience = despawnAudience,
            Corrections = corrections,
            InputAcks = inputAcks,
            SpawnedMonsters = spawnedMonsters,
            Events = _events.ToArray(),
            GameplayEvents = _gameplayEvents.ToArray(),
            PlayerCount = _playersBySession.Count,
            MonsterCount = _monstersById.Count,
        };

        // 事件已复制进结果，此处才可安全清空（缓冲复用，避免每 tick 分配 List）。
        _events.Clear();
        _gameplayEvents.Clear();

        return result;
    }

    /// <summary>
    /// 提交一次战斗动作（对应客户端 <c>CombatActionPacket</c> 上行）。
    /// <para>
    /// 服务端**立即权威结算**：命中/伤害/暴击/闪避全部由 <see cref="CombatCore"/> 判定，
    /// 客户端只负责表现。这是"战斗权威"的核心——客户端上报的伤害值一概不采信。
    /// </para>
    /// </summary>
    /// <param name="sessionId">发起者会话。</param>
    /// <param name="action">客户端动作包。</param>
    /// <returns>结算结果；未结算时 <see cref="CombatResolution.Outcome"/> 说明原因。</returns>
    public CombatResolution SubmitCombatAction(long sessionId, CombatActionPacket action)
    {
        if (action is null) return new CombatResolution { Outcome = CombatOutcome.Invalid };
        if (!_playersBySession.TryGetValue(sessionId, out var player))
        {
            return new CombatResolution { Outcome = CombatOutcome.Invalid };
        }

        var attackerEntityId = player.EntityId;
        var targetEntityId = action.TargetId;

        // 当前只支持"玩家打怪"；玩家互殴（PVP）需先有阵营/安全区规则，属后续里程碑。
        if (!_monstersById.TryGetValue(targetEntityId, out var monster))
        {
            return new CombatResolution { Outcome = CombatOutcome.Invalid };
        }

        var attackerStats = BuildPlayerStats(player);
        var targetStats = BuildMonsterStats(monster);

        var attackerPos = _simWorld.GetEntity(attackerEntityId);

        var request = new CombatRequest
        {
            AttackerId = attackerEntityId,
            TargetId = targetEntityId,
            SkillId = action.SkillId,
            SkillRawDamage = action.ActionKind == CombatActionKind.SkillCast ? _options.PlayerAttackPower : 0f,
            ServerTick = _tick,
            AttackerX = attackerPos?.X ?? player.SpawnX,
            AttackerY = attackerPos?.Y ?? player.SpawnY,
            AttackerYaw = action.AttackerYaw,
            TargetX = monster.Ai.X,
            TargetY = monster.Ai.Y,
            AttackRange = _options.PlayerAttackRange,
            FacingCosThreshold = _options.FacingCosThreshold,
        };

        var resolution = CombatCore.Resolve(request, attackerStats, targetStats);

        if (resolution.DealtDamage)
        {
            ApplyDamageToMonster(monster, resolution, attackerEntityId);

            _events.Add((new[] { sessionId }, new DamagePacket
            {
                AttackerId = attackerEntityId,
                TargetId = targetEntityId,
                DamageAmount = resolution.Damage,
                DamageType = 1, // 1 = 物理（既有 DamagePacket.DamageType 约定）
                IsCritical = resolution.IsCritical,
                RemainingHp = resolution.TargetRemainingHp,
                MaxHp = resolution.TargetMaxHp,
                SkillId = action.SkillId,
                ServerTick = _tick,
            }));

            if (resolution.TargetDied)
            {
                HandleMonsterDeath(monster, attackerEntityId, player.CharacterId);
            }
        }

        return resolution;
    }

    /// <summary>把玩家数值投影为战斗内核入参。</summary>
    private CombatantStats BuildPlayerStats(DsPlayer player) => new CombatantStats
    {
        Hp = player.Hp,
        MaxHp = player.MaxHp,
        AttackPower = _options.PlayerAttackPower,
        Defense = _options.PlayerDefense,
        Element = 0,
        DodgeRate = _options.PlayerDodgeRate,
        CritRate = _options.PlayerCritRate,
        CritMultiplier = CombatCore.DefaultCritMultiplier,
    };

    /// <summary>把怪物数值投影为战斗内核入参。</summary>
    private CombatantStats BuildMonsterStats(DsMonster monster)
    {
        var cfg = FindConfigForSpawnPoint(monster.SpawnPointId);
        return new CombatantStats
        {
            Hp = monster.Ai.Hp,
            MaxHp = cfg.MaxHp,
            AttackPower = cfg.AttackPower,
            Defense = 0f,
            Element = 0,
            DodgeRate = 0f,
            CritRate = 0f,
            CritMultiplier = CombatCore.DefaultCritMultiplier,
        };
    }

    /// <summary>扣血并处理死亡（怪物侧）。</summary>
    private void ApplyDamageToMonster(DsMonster monster, in CombatResolution resolution, ulong attackerEntityId)
    {
        var self = monster.Ai;
        self.Hp = resolution.TargetRemainingHp;
        if (resolution.TargetDied)
        {
            self.State = MonsterAiState.Dead;
            self.ThreatTargetId = attackerEntityId;
        }
        monster.Ai = self;
    }

    /// <summary>
    /// 怪物死亡：广播死亡事件 → 掉落掷骰 → 生成跨层玩法事件 → 摘除实体并排定重生。
    /// </summary>
    private void HandleMonsterDeath(DsMonster monster, ulong killerEntityId, ulong killerCharacterId)
    {
        var cfg = FindConfigForSpawnPoint(monster.SpawnPointId);
        var killerSessionId = _sessionByEntityId.TryGetValue(killerEntityId, out var sid) ? sid : 0L;

        // 1) 死亡表现事件（含重生延迟，客户端据此做倒地/复活表现）。
        var deathAudience = killerSessionId > 0 ? new[] { killerSessionId } : Array.Empty<long>();
        if (deathAudience.Length > 0)
        {
            _events.Add((deathAudience, new DeathPacket
            {
                EntityId = monster.EntityId,
                KillerId = killerEntityId,
                RespawnDelaySeconds = cfg.RespawnDelaySeconds,
                ServerTick = _tick,
            }));
        }

        // 2) 掉落掷骰（确定性；生产由 LootTableGrain 适配器接管）。
        var drops = _lootProvider.Roll(monster.TemplateId, killerCharacterId, _tick);

        // 3) 跨层事件：击杀 + 逐件掉落（业务层据此推进任务/入包）。
        _gameplayEvents.Add(new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.Kill,
            ShardId = _options.ShardId,
            ServerTick = _tick,
            ActorCharacterId = killerCharacterId,
            MonsterEntityId = monster.EntityId,
            MonsterTemplateId = monster.TemplateId,
        });

        for (var i = 0; i < drops.Count; i++)
        {
            _gameplayEvents.Add(new DsGameplayEvent
            {
                Kind = DsGameplayEventKind.Loot,
                ShardId = _options.ShardId,
                ServerTick = _tick,
                ActorCharacterId = killerCharacterId,
                MonsterEntityId = monster.EntityId,
                MonsterTemplateId = monster.TemplateId,
                ItemId = drops[i].ItemId,
                ItemCount = drops[i].Count,
            });
        }

        // 4) 摘除实体 + 排定重生（与 MarkMonsterDead 走同一条确定性路径）。
        MarkMonsterDead(monster.EntityId, _tick);
    }

    /// <summary>玩家复活：到达复活 tick 时回满血并回到出生点。</summary>
    private void TickPlayerRespawns()
    {
        if (_playersBySession.Count == 0) return;

        foreach (var kv in _playersBySession)
        {
            var player = kv.Value;
            if (!player.IsDead) continue;
            if (player.RespawnAtTick <= 0 || _tick < player.RespawnAtTick) continue;

            player.Hp = player.MaxHp;
            player.RespawnAtTick = 0;
            _simWorld.SetPosition(player.EntityId, player.SpawnX, player.SpawnY, player.SpawnZ);

            // 复活**不下发独立事件包**：客户端从下一帧快照即可观察到
            // "HP 回满 + 位置回到出生点"，这是既有全量快照契约已经覆盖的语义，
            // 不需要为此扩容 SyncEventKind（协议改动一律走 M4.6 的"尾部追加"纪律）。
        }
    }

    /// <summary>刷怪调度：按 SpawnPointId 升序处理，生成严格确定性。</summary>
    private List<ulong> TickSpawnDirector()
    {
        var spawned = new List<ulong>();
        if (_spawnPoints.Count == 0) return spawned;

        for (var i = 0; i < _spawnPoints.Count; i++)
        {
            var cfg = _spawnPoints[i];
            if (!_spawnSlots.TryGetValue(cfg.SpawnPointId, out var slots)) continue;

            var decision = SpawnDirectorCore.Tick(cfg, slots, _tick, _options.TicksPerSecond);
            for (var s = 0; s < decision.Spawns.Length; s++)
            {
                var spawn = decision.Spawns[s];

                // 玩家实体已占用该 ID 时跳过（理论不可达，但宁可少一只怪也不要串体）。
                if (_sessionByEntityId.ContainsKey(spawn.EntityId)) continue;

                var aiCfg = cfg.ToAiConfig();
                _monstersById[spawn.EntityId] = new DsMonster
                {
                    EntityId = spawn.EntityId,
                    SpawnPointId = cfg.SpawnPointId,
                    SlotIndex = spawn.SlotIndex,
                    TemplateId = cfg.MonsterTemplateId,
                    Ai = new MonsterAiSelf
                    {
                        X = spawn.X,
                        Y = spawn.Y,
                        Z = spawn.Z,
                        Yaw = spawn.Yaw,
                        SpawnX = spawn.X,
                        SpawnY = spawn.Y,
                        SpawnZ = spawn.Z,
                        State = MonsterAiState.Idle,
                        Hp = aiCfg.MaxHp,
                        AttackCooldownRemaining = 0f,
                        StateElapsedSeconds = 0f,
                        // 巡逻方向由槽位奇偶决定（纯确定性，无随机源）。
                        PatrolDirection = (spawn.SlotIndex % 2 == 0) ? 1f : -1f,
                    },
                };

                SpawnDirectorCore.MarkSlotAlive(slots, spawn.SlotIndex, spawn.EntityId);
                spawned.Add(spawn.EntityId);
            }
        }

        return spawned;
    }

    /// <summary>怪物 AI 推进：目标集合按 EntityId 升序传入（确定性契约）。</summary>
    private void TickMonsters(float dt)
    {
        if (_monstersById.Count == 0) return;

        // 目标集合 = 在线玩家，按 EntityId 升序（MonsterAiCore 的确定性前置契约）。
        var targets = new MonsterAiTarget[_playersBySession.Count];
        var t = 0;
        foreach (var kv in _playersBySession)
        {
            var p = kv.Value;
            var entity = _simWorld.GetEntity(p.EntityId);
            targets[t++] = new MonsterAiTarget
            {
                EntityId = p.EntityId,
                X = entity?.X ?? 0f,
                Y = entity?.Y ?? 0f,
                Z = entity?.Z ?? 0f,
                IsAlive = p.Hp > 0,
                IsAttackable = p.Hp > 0,
            };
        }
        MonsterAiCore.SortTargetsByEntityId(targets);

        // 怪物遍历顺序同样按 EntityId 升序：决策结果虽互不依赖，但击杀/掉落随顺序变化，
        // 固定顺序才能让"同 tick 序列 → 同结果"成立（M5 金轨迹对拍前提）。
        var monsterIds = new List<ulong>(_monstersById.Keys);
        monsterIds.Sort();

        for (var i = 0; i < monsterIds.Count; i++)
        {
            var id = monsterIds[i];
            var monster = _monstersById[id];
            var cfg = FindConfigForSpawnPoint(monster.SpawnPointId);

            var decision = MonsterAiCore.Decide(monster.Ai, targets, dt, cfg, _tick);

            var self = monster.Ai;
            self.X += decision.DeltaX;
            self.Y += decision.DeltaY;
            self.Yaw = decision.NextYaw;
            if (decision.NextState != self.State)
            {
                self.State = decision.NextState;
                self.StateElapsedSeconds = 0f;
            }
            else
            {
                self.StateElapsedSeconds += dt;
            }
            self.AttackCooldownRemaining = decision.NextAttackCooldownRemaining;
            self.ThreatTargetId = decision.ThreatTargetId;
            monster.Ai = self;

            // AI 决定出手 → 走与玩家攻击同一条权威结算路径（CombatCore）。
            if (decision.WantsAttack && decision.AttackTargetId != 0UL)
            {
                ResolveMonsterAttack(monster, decision.AttackTargetId);
            }
        }
    }

    /// <summary>
    /// 怪物攻击玩家：与玩家攻击怪物共用 <see cref="CombatCore"/>，因此两端的数值/闪避/暴击
    /// 语义完全对称，不存在"玩家打怪有闪避、怪打玩家必中"这类双标。
    /// </summary>
    private void ResolveMonsterAttack(DsMonster monster, ulong targetEntityId)
    {
        if (!_sessionByEntityId.TryGetValue(targetEntityId, out var sessionId)) return;
        if (!_playersBySession.TryGetValue(sessionId, out var player)) return;
        if (player.IsDead) return;

        var targetPos = _simWorld.GetEntity(targetEntityId);
        var cfg = FindConfigForSpawnPoint(monster.SpawnPointId);

        var request = new CombatRequest
        {
            AttackerId = monster.EntityId,
            TargetId = targetEntityId,
            // 怪物攻击不需要朝向判定（由 AI 的仇恨选择负责"打谁"），
            // 故把余弦阈值设为 -2 以跳过扇区检查。
            FacingCosThreshold = -2f,
            SkillId = 0,
            SkillRawDamage = 0f,
            ServerTick = _tick,
            AttackerX = monster.Ai.X,
            AttackerY = monster.Ai.Y,
            AttackerYaw = monster.Ai.Yaw,
            TargetX = targetPos?.X ?? player.SpawnX,
            TargetY = targetPos?.Y ?? player.SpawnY,
            AttackRange = cfg.AttackRange,
        };

        var resolution = CombatCore.Resolve(request, BuildMonsterStats(monster), BuildPlayerStats(player));
        if (!resolution.DealtDamage) return;

        player.Hp = resolution.TargetRemainingHp;

        _events.Add((new[] { sessionId }, new DamagePacket
        {
            AttackerId = monster.EntityId,
            TargetId = targetEntityId,
            DamageAmount = resolution.Damage,
            DamageType = 1,
            IsCritical = resolution.IsCritical,
            RemainingHp = resolution.TargetRemainingHp,
            MaxHp = resolution.TargetMaxHp,
            SkillId = 0,
            ServerTick = _tick,
        }));

        if (!resolution.TargetDied) return;

        // 玩家死亡：排定复活并上报跨层事件（死亡惩罚/统计由业务层决定）。
        player.RespawnAtTick = _tick + (long)(_options.PlayerRespawnDelaySeconds * _options.TicksPerSecond);

        _events.Add((new[] { sessionId }, new DeathPacket
        {
            EntityId = targetEntityId,
            KillerId = monster.EntityId,
            RespawnDelaySeconds = _options.PlayerRespawnDelaySeconds,
            ServerTick = _tick,
        }));

        _gameplayEvents.Add(new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.PlayerDeath,
            ShardId = _options.ShardId,
            ServerTick = _tick,
            ActorCharacterId = player.CharacterId,
            MonsterEntityId = monster.EntityId,
            MonsterTemplateId = monster.TemplateId,
        });
    }

    private MonsterAiConfig FindConfigForSpawnPoint(int spawnPointId)
    {
        for (var i = 0; i < _spawnPoints.Count; i++)
        {
            if (_spawnPoints[i].SpawnPointId == spawnPointId) return _spawnPoints[i].ToAiConfig();
        }
        return MonsterAiConfig.Default;
    }

    /// <summary>
    /// 构建全量世界快照。
    /// <para>
    /// <b>MVP 取舍（已在计划中登记为后续项）</b>：本实现向所有会话下发**同一份全量快照**，
    /// 未做 AOI 裁剪。这在小规模下正确且实现成本最低；规模上量后必须补 AOI 裁剪
    /// （复用既有 <c>ZoneShardAoi</c> / Morton 订阅）与增量 baseline 编码——
    /// 这正是既有深度分析报告 §8.1 指出的"每秒全量风暴"风险，此处不重复踩坑，
    /// 但也不假装已经解决。
    /// </para>
    /// </summary>
    private SnapshotPacket BuildSnapshot()
    {
        // Sprint 4：复用缓冲代替每 tick new List<>，减少 Gen0 分配。
        _snapshotDeltaBuffer.Clear();
        var deltas = _snapshotDeltaBuffer;

        // --- 玩家（按 EntityId 升序）---
        _snapshotIdBuffer.Clear();
        var playerIds = _snapshotIdBuffer;
        foreach (var kv in _playersBySession) playerIds.Add(kv.Value.EntityId);
        playerIds.Sort();

        for (var i = 0; i < playerIds.Count; i++)
        {
            var entityId = playerIds[i];
            if (!_sessionByEntityId.TryGetValue(entityId, out var sessionId)) continue;
            var player = _playersBySession[sessionId];
            var e = _simWorld.GetEntity(entityId);

            var isSpawn = !player.SpawnedToClients;
            // 档案变更增量（v13）：Spawn 帧由 Identity 自带，此处只对齐基线。
            var profileChange = player.TakeProfileChange(isSpawn);
            deltas.Add(new EntityDelta
            {
                EntityId = entityId,
                Kind = isSpawn ? EntityDeltaKind.Spawn : EntityDeltaKind.Update,
                Profile = profileChange,
                Identity = isSpawn
                    ? new NetworkIdentityAuthComponent
                    {
                        NetworkId = entityId,
                        EntityType = EntityTypePlayer,
                        OwnerId = player.CharacterId,
                        Nickname = player.Nickname,
                        Title = player.Title,
                    }
                    : null,
                Transform = new AuthTransformComponent
                {
                    // 坐标系约定（与 ZoneShardGrain 一致）：ECS Z-up → Flax Y-up 换轴，
                    // Y(上下) ← ECS.Z，Z(前后) ← ECS.Y。协议字段是 Flax Y-up，客户端直接作为
                    // AuthTransformComponent 使用——漏换轴会导致远端角色 3D 位置错乱。
                    X = e?.X ?? 0f,
                    Y = e?.Z ?? 0f,
                    Z = e?.Y ?? 0f,
                    Yaw = e?.Yaw ?? 0f,
                    ServerTick = _tick,
                },
                State = new EntityStateAuthComponent
                {
                    Health = player.Hp,
                    MaxHealth = player.MaxHp,
                    Level = player.Level,
                    // 死亡位由 DS 权威血量决定：远程客户端据此立刻看到倒下/复活（v13）。
                    StateBits = player.IsDead ? EntityStateBits.Dead : 0u,
                },
                MovementState = new MovementStateAuthComponent
                {
                    // 注意：MovementMode 枚举没有 Idle（0 = Walk）——"站立"在协议里由
                    // 水平速度≈0 + 客户端动画状态机表达，不占用独立枚举值。
                    MovementMode = e?.MovementMode ?? MovementMode.Walk,
                    VelocityXZ_X = e?.VelocityXZ_X ?? 0f,
                    VelocityXZ_Y = e?.VelocityXZ_Y ?? 0f,
                    IsGrounded = e?.IsGrounded ?? true,
                    ServerTick = _tick,
                },
            });

            player.SpawnedToClients = true;
        }

        // --- 怪物（按 EntityId 升序）---
        // 注意：此处不能复用 _snapshotIdBuffer（已被玩家 ID 占用），
        // 但怪物 ID 列表生命周期短且与玩家不重叠，单独分配可接受。
        var monsterIds = new List<ulong>(_monstersById.Keys);
        monsterIds.Sort();

        for (var i = 0; i < monsterIds.Count; i++)
        {
            var monster = _monstersById[monsterIds[i]];
            var cfg = FindConfigForSpawnPoint(monster.SpawnPointId);
            var isSpawn = !monster.SpawnedToClients;

            var speed = monster.Ai.State == MonsterAiState.Chase || monster.Ai.State == MonsterAiState.Leash
                ? cfg.MoveSpeed
                : (monster.Ai.State == MonsterAiState.Patrol ? cfg.MoveSpeed : 0f);

            deltas.Add(new EntityDelta
            {
                EntityId = monster.EntityId,
                Kind = isSpawn ? EntityDeltaKind.Spawn : EntityDeltaKind.Update,
                Identity = isSpawn
                    ? new NetworkIdentityAuthComponent
                    {
                        NetworkId = monster.EntityId,
                        EntityType = EntityTypeMonster,
                        OwnerId = 0UL, // NPC/怪物：无归属玩家
                    }
                    : null,
                Transform = new AuthTransformComponent
                {
                    // 坐标系约定：ECS Z-up → Flax Y-up 换轴（与玩家分支一致）。
                    X = monster.Ai.X,
                    Y = monster.Ai.Z,
                    Z = monster.Ai.Y,
                    Yaw = monster.Ai.Yaw,
                    ServerTick = _tick,
                },
                State = new EntityStateAuthComponent
                {
                    Health = monster.Ai.Hp,
                    MaxHealth = cfg.MaxHp,
                },
                MovementState = new MovementStateAuthComponent
                {
                    // 怪物一律 Walk（无奔跑/跳跃语义）；真正区分"是否在动"的是速度字段。
                    MovementMode = MovementMode.Walk,
                    // 怪物速度按状态给出：客户端据此驱动动画并参与前向预测，
                    // 给 0 会让高速移动的怪在客户端看起来"瞬移贴地"。
                    VelocityXZ_X = speed,
                    VelocityXZ_Y = 0f,
                    IsGrounded = true,
                    ServerTick = _tick,
                },
            });

            monster.SpawnedToClients = true;
        }

        return new SnapshotPacket
        {
            ProtocolVersion = SyncProtocolVersion.Current,
            ServerTick = _tick,
            BaselineTick = 0, // 0 = 全量（客户端不做增量重建）
            Deltas = deltas.ToArray(),
        };
    }

    // ========================================================================
    // AOI 分桶快照（仅 EnableAoiFilter = true 时走这条路径）
    // ========================================================================

    /// <summary>把世界坐标换算为 chunk 整数块坐标（Floor 语义，边长 = <see cref="_aoiChunkSize"/>）。</summary>
    private (int X, int Y, int Z) ChunkCoords(float x, float y, float z)
    {
        var cs = (float)_aoiChunkSize;
        return (
            (int)MathF.Floor(x / cs),
            (int)MathF.Floor(y / cs),
            (int)MathF.Floor(z / cs));
    }

    /// <summary>实体归桶用的 Morton chunk 键（与 <see cref="ChunkCoords"/> 同源，保证订阅/归桶一致）。</summary>
    private ulong ChunkKey(float x, float y, float z)
    {
        var (cx, cy, cz) = ChunkCoords(x, y, z);
        // 防御：超出 Morton 轴范围（≈±16km×1M，正常不可达）时降级为 0，避免抛异常中断快照。
        return MortonCodec.IsInRange(cx, cy, cz) ? MortonCodec.Encode3D(cx, cy, cz) : 0UL;
    }

    /// <summary>
    /// 计算以 (cx,cy,cz) 为中心、半径 radius 的**水平面** chunk 键集合（垂直轴固定在 cz 层）。
    /// <para>世界为 Z-up 平面世界，故展开 <c>(2r+1)²</c> 而非 <c>(2r+1)³</c>；与实体归桶的
    /// <see cref="ChunkKey"/> 一致（同层 cz），保证玩家能看见自己所在平面的实体。</para>
    /// </summary>
    private ulong[] ComputeAoiChunkKeys(int cx, int cy, int cz, int radius)
    {
        var r = radius < 0 ? 0 : radius;
        var side = 2 * r + 1;
        var buffer = new ulong[side * side];
        var n = 0;
        for (var dx = -r; dx <= r; dx++)
        {
            var nx = cx + dx;
            for (var dy = -r; dy <= r; dy++)
            {
                var ny = cy + dy;
                if (!MortonCodec.IsInRange(nx, ny, cz)) continue;
                buffer[n++] = MortonCodec.Encode3D(nx, ny, cz);
            }
        }
        if (n == buffer.Length) return buffer;
        var exact = new ulong[n];
        Array.Copy(buffer, exact, n);
        return exact;
    }

    /// <summary>为玩家建立（或重建）AOI 订阅，并记录中心 chunk 坐标。</summary>
    private void SubscribePlayerAoi(DsPlayer player, int cx, int cy, int cz)
    {
        var keys = ComputeAoiChunkKeys(cx, cy, cz, _aoiRadiusChunks);
        _aoi.Subscribe(player.SessionId, keys);
        player.AoiChunkX = cx;
        player.AoiChunkY = cy;
        player.AoiChunkZ = cz;
        player.AoiSubscribed = true;
    }

    /// <summary>每 tick（Step 之后）检测玩家是否跨越 chunk 边界，跨越则先退订旧平面再订阅新平面。</summary>
    private void UpdateAoiSubscriptions()
    {
        foreach (var kv in _playersBySession)
        {
            var p = kv.Value;
            var e = _simWorld.GetEntity(p.EntityId);
            var x = e?.X ?? p.SpawnX;
            var y = e?.Y ?? p.SpawnY;
            var z = e?.Z ?? p.SpawnZ;
            var (cx, cy, cz) = ChunkCoords(x, y, z);

            if (!p.AoiSubscribed)
            {
                // 自愈：漏订阅（如开关在会话中途打开）时在下一 tick 补订。
                SubscribePlayerAoi(p, cx, cy, cz);
                continue;
            }

            if (cx == p.AoiChunkX && cy == p.AoiChunkY && cz == p.AoiChunkZ) continue;

            // 跨越边界：先退订旧平面再订阅新平面（整体替换，语义简单且幂等）。
            var oldKeys = ComputeAoiChunkKeys(p.AoiChunkX, p.AoiChunkY, p.AoiChunkZ, _aoiRadiusChunks);
            _aoi.Unsubscribe(p.SessionId, oldKeys);
            SubscribePlayerAoi(p, cx, cy, cz);
        }
    }

    /// <summary>
    /// 构建 AOI 分桶快照分组：把快照带宽从 O(N²) 降到 O(N×K)。
    /// <para>步骤：（1）一次全量遍历实体按 Morton chunk 归桶；（2）按会话可见 chunk 集合
    /// （订阅 ∩ 承载实体的 chunk）分组；（3）每组合并可见 chunk 的 delta、按 EntityId 升序生成一份全量快照；
    /// （4）按最小 sessionId 升序输出（确定性）。相同可见集合的会话共享同一份字节，保留「一份字节多受众」不变量。</para>
    /// </summary>
    private List<DsSnapshotGroup> BuildSnapshotGroups()
    {
        // 1) 一次全量遍历所有实体（玩家 + 怪物），按 chunk 归类 delta。实体按 EntityId 升序遍历。
        // Sprint 4：复用 _snapshotDeltaBuffer / _snapshotIdBuffer 作为中间缓冲，减少每 tick 分配。
        var chunkToDeltas = new Dictionary<ulong, List<EntityDelta>>();

        _snapshotIdBuffer.Clear();
        var playerIds = _snapshotIdBuffer;
        foreach (var kv in _playersBySession) playerIds.Add(kv.Value.EntityId);
        playerIds.Sort();
        for (var i = 0; i < playerIds.Count; i++)
        {
            var entityId = playerIds[i];
            if (!_sessionByEntityId.TryGetValue(entityId, out var sessionId)) continue;
            var player = _playersBySession[sessionId];
            var e = _simWorld.GetEntity(entityId);
            var delta = BuildPlayerDelta(player);
            var key = ChunkKey(e?.X ?? player.SpawnX, e?.Y ?? player.SpawnY, e?.Z ?? player.SpawnZ);
            if (!chunkToDeltas.TryGetValue(key, out var list)) { list = new List<EntityDelta>(); chunkToDeltas[key] = list; }
            list.Add(delta);
        }

        var monsterIds = new List<ulong>(_monstersById.Keys);
        monsterIds.Sort();
        for (var i = 0; i < monsterIds.Count; i++)
        {
            var monster = _monstersById[monsterIds[i]];
            var delta = BuildMonsterDelta(monster);
            var key = ChunkKey(monster.Ai.X, monster.Ai.Y, monster.Ai.Z);
            if (!chunkToDeltas.TryGetValue(key, out var list)) { list = new List<EntityDelta>(); chunkToDeltas[key] = list; }
            list.Add(delta);
        }

        // 2) 承载实体的 chunk 键（升序，确定性）。
        var entityChunks = new List<ulong>(chunkToDeltas.Keys);
        entityChunks.Sort();

        // 3) 按“可见 chunk 集合”分组会话（按 sessionId 升序处理，保证确定性）。
        var sessionIds = new List<long>(_playersBySession.Keys);
        sessionIds.Sort();
        var groups = new Dictionary<ulong[], SnapshotGroupAccum>(ChunkSetComparer.Instance);

        for (var i = 0; i < sessionIds.Count; i++)
        {
            var sid = sessionIds[i];
            // 待补发首包 Spawn 的会话（加入/重连）本 tick 走专属快照，不参与分组共享。
            if (_pendingInitialSpawnSessions.Contains(sid)) continue;

            // ZoneShardAoi.GetSubscriptions 返回 HashSet<ulong>（未订阅时为空数组）；以 ISet 做 O(1) 命中判定。
            var subs = _aoi.GetSubscriptions(sid) as ISet<ulong>;

            ulong[] visible;
            if (subs is null)
            {
                visible = Array.Empty<ulong>();
            }
            else
            {
                // 可见集合 = 订阅 ∩ 承载实体的 chunk；只遍历 entityChunks（小），不遍历整个订阅立方（大）。
                var vis = new List<ulong>();
                for (var c = 0; c < entityChunks.Count; c++)
                {
                    if (subs.Contains(entityChunks[c])) vis.Add(entityChunks[c]);
                }
                visible = vis.ToArray();
            }

            if (!groups.TryGetValue(visible, out var accum))
            {
                accum = new SnapshotGroupAccum { VisibleChunks = visible };
                groups[visible] = accum;
            }
            accum.SessionIds.Add(sid);
        }

        // 4) 每组构建一份快照：合并可见 chunk 的 delta，按 EntityId 升序（确定性）。
        var result = new List<DsSnapshotGroup>(groups.Count);
        foreach (var accum in groups.Values)
        {
            var deltas = new List<EntityDelta>();
            for (var c = 0; c < accum.VisibleChunks.Length; c++)
            {
                if (chunkToDeltas.TryGetValue(accum.VisibleChunks[c], out var list)) deltas.AddRange(list);
            }
            deltas.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));

            var audience = accum.SessionIds.ToArray();
            Array.Sort(audience);

            result.Add(new DsSnapshotGroup
            {
                Snapshot = new SnapshotPacket
                {
                    ProtocolVersion = SyncProtocolVersion.Current,
                    ServerTick = _tick,
                    BaselineTick = 0, // 0 = 全量
                    Deltas = deltas.ToArray(),
                },
                AudienceSessionIds = audience,
            });
        }

        // 5) 按最小 sessionId 升序输出（确定性）。
        result.Sort((a, b) =>
        {
            var am = a.AudienceSessionIds.Length > 0 ? a.AudienceSessionIds[0] : long.MaxValue;
            var bm = b.AudienceSessionIds.Length > 0 ? b.AudienceSessionIds[0] : long.MaxValue;
            return am.CompareTo(bm);
        });
        return result;
    }

    /// <summary>
    /// 为待补发会话构建**专属**首包快照：所有（AOI 可见）实体强制 <see cref="EntityDeltaKind.Spawn"/> + Identity。
    /// <para>
    /// 每会话一份字节（会话级 Spawn 状态无法在多受众间共享）；实体遍历顺序与
    /// <see cref="BuildSnapshot"/>/<see cref="BuildSnapshotGroups"/> 一致（EntityId 升序），保证确定性。
    /// </para>
    /// </summary>
    /// <param name="sessionIds">待补发会话（升序处理；可能包含已离开会话，防御性跳过）。</param>
    private List<DsInitialSpawnSnapshot>? BuildInitialSpawnSnapshots(List<long>? sessionIds)
    {
        if (sessionIds is null || sessionIds.Count == 0) return null;
        sessionIds.Sort();

        var result = new List<DsInitialSpawnSnapshot>(sessionIds.Count);
        for (var i = 0; i < sessionIds.Count; i++)
        {
            var sid = sessionIds[i];
            if (!_playersBySession.ContainsKey(sid)) continue; // 会话已离开（防御）

            // AOI 开关开启时按该会话订阅集合裁剪；未订阅（理论上不会发生）= 可见集合为空（与分组路径语义一致）。
            var aoiFiltering = _options.EnableAoiFilter;
            var aoiSubs = aoiFiltering ? _aoi.GetSubscriptions(sid) as ISet<ulong> : null;

            var deltas = new List<EntityDelta>();

            // 玩家（EntityId 升序）。
            var playerIds = new List<ulong>(_playersBySession.Count);
            foreach (var kv in _playersBySession) playerIds.Add(kv.Value.EntityId);
            playerIds.Sort();
            for (var p = 0; p < playerIds.Count; p++)
            {
                if (!_sessionByEntityId.TryGetValue(playerIds[p], out var pSession)) continue;
                var player = _playersBySession[pSession];
                var e = _simWorld.GetEntity(player.EntityId);
                if (aoiFiltering && (aoiSubs is null ||
                    !aoiSubs.Contains(ChunkKey(e?.X ?? player.SpawnX, e?.Y ?? player.SpawnY, e?.Z ?? player.SpawnZ))))
                    continue;
                deltas.Add(BuildPlayerDelta(player, forceSpawn: true));
            }

            // 怪物（EntityId 升序）。
            var monsterIds = new List<ulong>(_monstersById.Keys);
            monsterIds.Sort();
            for (var m = 0; m < monsterIds.Count; m++)
            {
                var monster = _monstersById[monsterIds[m]];
                if (aoiFiltering && (aoiSubs is null ||
                    !aoiSubs.Contains(ChunkKey(monster.Ai.X, monster.Ai.Y, monster.Ai.Z))))
                    continue;
                deltas.Add(BuildMonsterDelta(monster, forceSpawn: true));
            }

            result.Add(new DsInitialSpawnSnapshot
            {
                SessionId = sid,
                Snapshot = new SnapshotPacket
                {
                    ProtocolVersion = SyncProtocolVersion.Current,
                    ServerTick = _tick,
                    BaselineTick = 0, // 0 = 全量
                    Deltas = deltas.ToArray(),
                },
            });
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>构建单个玩家的 EntityDelta（与 <see cref="BuildSnapshot"/> 中的玩家分支镜像；顺带置 SpawnedToClients）。</summary>
    /// <param name="forceSpawn">true = 本 delta 强制 Spawn（会话级首包补发专用；全局标志仍按共享路径语义置位）。</param>
    private EntityDelta BuildPlayerDelta(DsPlayer player, bool forceSpawn = false)
    {
        var entityId = player.EntityId;
        var e = _simWorld.GetEntity(entityId);
        var isSpawn = forceSpawn || !player.SpawnedToClients;
        var delta = new EntityDelta
        {
            EntityId = entityId,
            Kind = isSpawn ? EntityDeltaKind.Spawn : EntityDeltaKind.Update,
            // 档案变更增量（v13）：Spawn 帧由 Identity 自带，此处只对齐基线。
            Profile = player.TakeProfileChange(isSpawn),
            Identity = isSpawn
                ? new NetworkIdentityAuthComponent
                {
                    NetworkId = entityId,
                    EntityType = EntityTypePlayer,
                    OwnerId = player.CharacterId,
                    Nickname = player.Nickname,
                    Title = player.Title,
                }
                : null,
            Transform = new AuthTransformComponent
            {
                // 坐标系约定（与 ZoneShardGrain 一致）：ECS Z-up → Flax Y-up 换轴，
                // Y(上下) ← ECS.Z，Z(前后) ← ECS.Y。协议字段是 Flax Y-up，客户端直接作为
                // AuthTransformComponent 使用——漏换轴会导致远端角色 3D 位置错乱。
                X = e?.X ?? 0f,
                Y = e?.Z ?? 0f,
                Z = e?.Y ?? 0f,
                Yaw = e?.Yaw ?? 0f,
                ServerTick = _tick,
            },
            State = new EntityStateAuthComponent
            {
                Health = player.Hp,
                MaxHealth = player.MaxHp,
                Level = player.Level,
                // 死亡位由 DS 权威血量决定：远程客户端据此立刻看到倒下/复活（v13）。
                StateBits = player.IsDead ? EntityStateBits.Dead : 0u,
            },
            MovementState = new MovementStateAuthComponent
            {
                MovementMode = e?.MovementMode ?? MovementMode.Walk,
                VelocityXZ_X = e?.VelocityXZ_X ?? 0f,
                VelocityXZ_Y = e?.VelocityXZ_Y ?? 0f,
                IsGrounded = e?.IsGrounded ?? true,
                ServerTick = _tick,
            },
        };
        player.SpawnedToClients = true;
        return delta;
    }

    /// <summary>构建单个怪物的 EntityDelta（与 <see cref="BuildSnapshot"/> 中的怪物分支镜像；顺带置 SpawnedToClients）。</summary>
    /// <param name="forceSpawn">true = 本 delta 强制 Spawn（会话级首包补发专用；全局标志仍按共享路径语义置位）。</param>
    private EntityDelta BuildMonsterDelta(DsMonster monster, bool forceSpawn = false)
    {
        var cfg = FindConfigForSpawnPoint(monster.SpawnPointId);
        var isSpawn = forceSpawn || !monster.SpawnedToClients;

        var speed = monster.Ai.State == MonsterAiState.Chase || monster.Ai.State == MonsterAiState.Leash
            ? cfg.MoveSpeed
            : (monster.Ai.State == MonsterAiState.Patrol ? cfg.MoveSpeed : 0f);

        var delta = new EntityDelta
        {
            EntityId = monster.EntityId,
            Kind = isSpawn ? EntityDeltaKind.Spawn : EntityDeltaKind.Update,
            Identity = isSpawn
                ? new NetworkIdentityAuthComponent
                {
                    NetworkId = monster.EntityId,
                    EntityType = EntityTypeMonster,
                    OwnerId = 0UL, // NPC/怪物：无归属玩家
                }
                : null,
            Transform = new AuthTransformComponent
            {
                // 坐标系约定：ECS Z-up → Flax Y-up 换轴（与玩家分支一致）。
                X = monster.Ai.X,
                Y = monster.Ai.Z,
                Z = monster.Ai.Y,
                Yaw = monster.Ai.Yaw,
                ServerTick = _tick,
            },
            State = new EntityStateAuthComponent
            {
                Health = monster.Ai.Hp,
                MaxHealth = cfg.MaxHp,
            },
            MovementState = new MovementStateAuthComponent
            {
                MovementMode = MovementMode.Walk,
                VelocityXZ_X = speed,
                VelocityXZ_Y = 0f,
                IsGrounded = true,
                ServerTick = _tick,
            },
        };
        monster.SpawnedToClients = true;
        return delta;
    }

    /// <summary>分桶快照构建时的临时分组累加器。</summary>
    private sealed class SnapshotGroupAccum
    {
        public ulong[] VisibleChunks = Array.Empty<ulong>();
        public List<long> SessionIds = new();
    }

    /// <summary>ulong[] 的结构相等比较器（用于按“可见 chunk 集合”分组；精确相等，不因哈希碰撞误合）。</summary>
    private sealed class ChunkSetComparer : IEqualityComparer<ulong[]>
    {
        public static readonly ChunkSetComparer Instance = new();

        public bool Equals(ulong[]? x, ulong[]? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (x.Length != y.Length) return false;
            for (var i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }

        public int GetHashCode(ulong[] obj)
        {
            var h = new HashCode();
            for (var i = 0; i < obj.Length; i++) h.Add(obj[i]);
            return h.ToHashCode();
        }
    }

    /// <summary>把怪物重新置为可重生（M4 死亡流程的接入点；当前供测试与后续里程碑使用）。</summary>
    public bool MarkMonsterDead(ulong entityId, long serverTick)
    {
        if (!_monstersById.TryGetValue(entityId, out var monster)) return false;

        var cfg = FindConfigForSpawnPoint(monster.SpawnPointId);
        if (_spawnSlots.TryGetValue(monster.SpawnPointId, out var slots))
        {
            SpawnDirectorCore.MarkSlotDead(slots, monster.SlotIndex, serverTick, _options.TicksPerSecond, cfg.RespawnDelaySeconds);
        }

        var self = monster.Ai;
        self.Hp = 0;
        self.State = MonsterAiState.Dead;
        self.ThreatTargetId = 0UL;
        self.RespawnAtTick = MonsterAiCore.ComputeRespawnTick(serverTick, _options.TicksPerSecond, cfg.RespawnDelaySeconds);
        monster.Ai = self;

        // 死亡即从世界移除：重生由 SpawnDirectorCore 在到期 tick 以**同一实体 ID** 重新生成，
        // 这样客户端侧的实体身份在"死亡→重生"之间保持稳定，无需 Despawn/Spawn 往返。
        _monstersById.Remove(entityId);
        return true;
    }

    /// <summary>读取怪物权威状态（测试/诊断）。</summary>
    public bool TryGetMonster(ulong entityId, out DsMonster monster)
    {
        return _monstersById.TryGetValue(entityId, out monster!);
    }

    /// <summary>读取玩家权威状态（测试/诊断）。</summary>
    public bool TryGetPlayer(long sessionId, out DsPlayer player)
    {
        return _playersBySession.TryGetValue(sessionId, out player!);
    }
}
