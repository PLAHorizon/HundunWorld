using System;
using System.Collections.Generic;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Monsters;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>DS 世界会话配置。</summary>
public sealed class DsWorldSessionOptions
{
    /// <summary>本 DS 承载的 shard ID。</summary>
    public long ShardId { get; set; }

    /// <summary>模拟频率（Hz）。Orleans 权威与 UE5 DS 均为 60，必须一致。</summary>
    public int TicksPerSecond { get; set; } = 60;

    /// <summary>
    /// 把掉落扣在尸体上、由权威侧裁决拾取（2026-10-29 拍板 D23）。
    /// <para><b>默认 false</b>：网关/Grain/客户端拾取入口就绪前开它，掉落会拳在一个没人能取的地方。
    /// 开启后：击杀只登记（上行四帧中的 Register）、不再发入包事件；拾取走请求/转发/回执三段。</para>
    /// </summary>
    public bool DeferCorpseLootToAuthority { get; set; }

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
    /// 是否接受网关下发的脚本化生成指令（DS 侧总开关）。
    /// <para>关闭时指令一律以 <c>scripted-disabled</c> 拒绝并计数，便于灰度。</para>
    /// </summary>
    public bool EnableScriptedSpawn { get; set; }

    /// <summary>本 shard 同时存活的触发区域上限（DS 侧硬顶；网关侧另有预算护栏）。</summary>
    public int MaxScriptedTriggerZones { get; set; } = 32;

    /// <summary>触发区域未显式给 TTL 时的强制上限秒数（防"永久隐形刷怪机器"）。</summary>
    public int MaxScriptedTriggerZoneTtlSeconds { get; set; } = 600;

    /// <summary>
    /// 本 DS 向网关注册表声明承载的**全部** shard。留空表示只承载 <see cref="ShardId"/> 一片。
    /// <para>
    /// 令牌绑定的是"操作者被路由到的那片"，而一个 DS 进程可以声明多片（多片共用同一个世界实例，
    /// 玩家实体与快照本来就不按片隔离）。若只与 <see cref="ShardId"/>（即 <c>ShardIds[0]</c>）比，
    /// 声明 <c>1,2,3</c> 的 DS 会把所有落在 2/3 的合法指令判成 <c>wrong-shard</c> 而全量拒绝。
    /// 比集合才是与注册表同口径的做法，同时仍然拒绝"投给不承载该片的 DS"（跨片重放）。
    /// </para>
    /// </summary>
    public long[] HostedShardIds { get; set; } = Array.Empty<long>();

    /// <summary>
    /// 脚本令牌 HMAC 密钥（与网关/Grain 共享）。
    /// <para><b>缺失即全部拒绝</b>：DS 不能在没有密钥时"跳过验签"，否则任何能连上 DS 的人都可造怪。</para>
    /// </summary>
    public byte[]? ScriptedSigningKey { get; set; }

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

    /// <summary>
    /// 嘲讽的锁定秒数。取 3 是逆向口径（WoW 的嘲讽窗口就是 3 秒），
    /// 不是本作技能表的值——**等玩家技能表把嘲讽那一格接线后应由技能决定**（见设计文档 §8 D14）。
    /// </summary>
    public float MonsterTauntSeconds { get; set; } = 3f;

    /// <summary>
    /// 嘲讽的施法距离（米）。它**必须比普攻远得多**：嘲讽是一个远程施法动作，
    /// 不是贴身踢脚（拿 <see cref="PlayerAttackRange"/>=3 米当判据的话，坦克在怪被拉走后就再也拉不回来了）。
    /// 25 米是本作"一个远程技能"的量级占位；真实值同样等技能表接线（§8 D14）。0 = 退回用普攻距离。
    /// </summary>
    public float MonsterTauntRange { get; set; } = 25f;

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

    /// <summary>
    /// 角色**累计**修为经验（权威值由网关从 CharacterGrain 回合推下来；
    /// -1 = 未知，不下发）。0 是合法值（新角色），不能拿 0 当"未下发"。
    /// <para>为什么由网关推：DS 只知道自己发了多少击杀经验，不知道角色原本有多少；
    /// 用"本会话累计"冒充绝对值会让经验条从 0 开始重算。</para>
    /// </summary>
    public long Exp { get; set; } = -1L;

    /// <summary>
    /// 身上挂着的控制状态表（与怪物那份共用 <see cref="MonsterStatusCore"/>；null = 从没被控过）。
    /// <para>为什么玩家也住 DS：本作的战斗位置权威在 <c>SimWorld</c>（DS 侧），
    /// 而"被晕住时这一帧能不能走"只影响这一帧的输入放行——把它放到 grain 里会多一跳，
    /// 且会让"同 tick 序列 → 同结果"这条前提在跨进程上断掉（D29 选 a 的理由）。</para>
    /// </summary>
    public MonsterStatusEntry[]? Statuses { get; set; }

    /// <summary>状态表内有效条目数。</summary>
    public int StatusCount { get; set; }

    /// <summary>角色称号名（空 = 未佩戴）。</summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// 角色阵营（0 未入 / 1 正派 / 2 邪派）。来自网关权威档案，用于 PvP 敌对判定。
    /// <para>越界值由 <c>FactionCore</c> 判为 UnknownFaction 并拒绝参战，这里不做静默归零。</para>
    /// </summary>
    public int Faction { get; set; }

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

    /// <summary>
    /// 脚本生成时携带的类别（怪物 / NPC / 玩法型事件）；0 = 非脚本生成（静态刷怪点）。
    /// <para>只影响下发给客户端的 <c>EntityType</c> 与显示目录选择，不影响 AI/战斗/快照通路本身——
    /// 这样脚本实体天然继承既有全部不变量（AOI、晚加入补发、Despawn 全广播）。</para>
    /// </summary>
    public int ScriptedKind { get; set; }

    /// <summary>
    /// AI v2 的运行态（威胁表 / 施法 / 巡逻游标 / 路径 / 回位免疫窗）。
    /// <para>null = 这只怪仍走 v1（能力位全关或未装配内容包）。
    /// 刻意按需分配：<c>DsMonster</c> 是热路径结构，无脑 new 会让每只站桩小怪都背两套数组。</para>
    /// </summary>
    public MonsterRuntimeState? Runtime { get; set; }

    /// <summary>
    /// 贡献者账本（谁在死前摸过这只怪）。击杀经验的分配依据——
    /// 只有 killer 一个字段时，"我打了半天被别人收尾就没经验"这条 WoW 观感实现不出来。
    /// </summary>
    public MonsterContributors? Contributors { get; set; }

    /// <summary>是否已在**共享广播路径**下发过 Spawn。</summary>
    /// <remarks>同 <see cref="DsPlayer.SpawnedToClients"/>：实体级全局标志，晚加入者由会话级补发覆盖。</remarks>
    public bool SpawnedToClients { get; set; }

    /// <summary>
    /// 客户端头顶是否正挂着这只怪的施法条（服务端侧的记账位）。
    /// <para>
    /// 施法条是"起手发一条、结束发一条"的**边沿事件**，而实体 ID 会随重生复用同一个刷怪点槽位。
    /// 没有这个记账位，中途掉线/漏发的那一条就会让下一只同 ID 的怪开局挂着别人的条子；
    /// 有了它，宿主每 tick 只需比对"真的在读条吗"与"屏幕上有条子吗"，两个不一致就补发对应的那一条。
    /// </para>
    /// </summary>
    public bool CastBarVisible { get; set; }

    /// <summary>
    /// 本轮进战是否已经喊过话（与 <see cref="CastBarVisible"/> 同一形状：按状态对账，不逐出口写 if）。
    /// <para>进入战斗的出口有好几个（目力索敌、被嘲讽、被同族求援、先挨一手），逐出口挂一句
    /// "在这里喊一次"迟早漏一个——漏掉的那个不会报错，只会让某些怪永远不喊话。</para>
    /// </summary>
    public bool SayAnnouncedInCombat { get; set; }

    /// <summary>
    /// 这只 NPC 上一 tick 是否处于"残血逃跑"（<c>FleesAtLowHealth</c> 位触发的远离威胁）。
    /// 只用来把回执计数"按进入次数"而不是"按 tick 数"记；状态本身每 tick 由判据重新决定，
    /// 不把它当成需要持久化的 AI 状态。
    /// </summary>
    public bool NpcFleeing { get; set; }

    /// <summary>
    /// 这只怪的**生效等级**（生成那一刻算一次：<c>刷怪点覆盖 → 内容模板基准 → 代码目录模板等级</c>）。
    /// <para>
    /// <b>为什么必须在生成时定下来</b>：等级同时决定血量/攻击、击杀经验和头顶信息卡。
    /// 以前三条各读各的——刷怪配置读覆盖值、会话内的属性与经验读 <c>LevelBase</c>、
    /// 下发给客户端的等级读代码目录——"给刷怪点填 25 级"的结果是一只**顶着 12 级名字、
    /// 按 12 级掉血、只给 12 级经验**的怪。放在生成时算并存在实体上，
    /// 三处读同一个字段就不可能再分家；重算（每次查询扫一遍刷怪点）既慢又迟早被某处绕过。
    /// </para>
    /// </summary>
    public int EffectiveLevel { get; set; }

    /// <summary>
    /// 尸体消失 tick；0 = 这只怪还活着（或当场消失，压根没进尸体阶段）。
    /// <para>非零时实体**留在世界里**（快照照常下发，带 Dead 位），但不参与 AI、不可被打、
    /// 不在网关注册表的可交互名单里；到期才真正摘除并广播 Despawn。</para>
    /// </summary>
    public long CorpseExpireAtTick { get; set; }

    /// <summary>
    /// 尸体**可拾取窗口**的截止 tick（0 = 这具尸体取不了：窗口配成 0、或尸体已经消失）。
    /// <para>与 <see cref="CorpseExpireAtTick"/> 分开：尸体可以还在地上、但已经过了拾取窗口。
    /// 权威判定在 Orleans（2026-10-28 拍板），DS 这一格只负责"窗口多长、什么时候关"，
    /// 供拒绝迟到请求与客户端提示使用。</para>
    /// </summary>
    public long CorpseLootExpireAtTick { get; set; }

    /// <summary>这具尸体是否已向权威侧登记过拾取列表（决定收尸时要不要发 Gone）。</summary>
    public bool CorpseLootRegistered { get; set; }

    /// <summary>
    /// 这具尸体**登记出去的那份**击杀参与者名单（AC `DIDNT_KILL`）；null/空 = 不限制。
    /// <para>刻意存成"登记时定下的快照"而不是每次回头读 <see cref="Contributors"/>：
    /// 本地门与权威侧必须看同一份名单，两处各读各的迟早分家（一处内存活账本、一处网络快照）。
    /// </para>
    /// </summary>
    public ulong[]? CorpseLootContributors { get; set; }

    /// <summary>登记时这具尸体上有几个槽（与 <see cref="CorpseLootSlotsTaken"/> 一起算"还剩几件"）。</summary>
    public int CorpseLootSlotsTotal { get; set; }

    /// <summary>已经从这具尸体上取走的槽数（只数真的入包的那些）。</summary>
    public int CorpseLootSlotsTaken { get; set; }

    /// <summary>窗口到期"东西没拿完"的提示是否已经发过（同一具尸体只发一次）。</summary>
    public bool CorpseLootExpiredNotified { get; set; }

    /// <summary>拾取交互距离（米；缺省 AC 的 5.5，可由内容列覆盖）。</summary>
    public float PickupDistanceMeters { get; set; } = MonsterCorpseCore.DefaultPickupDistanceMeters;

    /// <summary>
    /// 这只怪走哪条巡逻线（生成那一刻定一次：<c>刷怪点 route_override_id → 行为表 patrol_route_id → 0=无路线</c>）。
    /// <para>与 <see cref="EffectiveLevel"/> 同一条理由：覆盖关系如果三处各读各的迟早分家。
    /// 0 表示"刷怪点没覆盖"，消费方退回行为表那一份（见 <c>TryDecideMonsterV2</c>），
    /// 所以脚本生成这类没走静态刷怪点的路径不会因为这里没填而丢掉原有路线。</para>
    /// </summary>
    public int PatrolRouteId { get; set; }

    /// <summary>
    /// 有人在上一拍请求打断这只怪的读条（一次性：被 AI 消费后即清）。
    /// <para>刻意做成"一拍的信号"而不是"一段状态"：内核里那条判据
    /// （<c>MonsterCastingCore</c> 的 interruptSignal 分支）本来就只在**正在读条**那一 tick 有效，
    /// 留成长期标志会让"打断了一条已经放完的条"变成一次迟到的取消。</para>
    /// </summary>
    public bool InterruptRequested { get; set; }

    /// <summary>
    /// 这次生成是**复活**（该槽位此前死过）而不是服务器/点位的首次生成。
    /// <para>只作用一件事：复活才装"刚出现 5 秒不主动接敌"的宽限期（逆向依据见
    /// <see cref="MonsterAiRuntime.RespawnGraceSeconds"/>）。首次生成不装，
    /// 否则服务器一开、全图的怪都先聋 5 秒。</para>
    /// </summary>
    public bool SpawnIsRespawn { get; set; }
}

/// <summary>
/// 一只怪的伤害/治疗贡献账本（定长，按角色 ID 去重；插入序即记录序）。
/// </summary>
/// <remarks>
/// <b>为什么定长</b>：世界 BOSS 会被几十个玩家同时打，无上限的账本会让一只怪的内存
/// 随围观人数增长，而增长发生在最不该出问题的热点上。超出容量时挤掉"最久没贡献"的人——
/// 与 WoW 的 tag 窗口语义一致（10 秒不摸就没了）。
/// </remarks>
public sealed class MonsterContributors
{
    /// <summary>容量（超出后按最后贡献时刻淘汰）。</summary>
    public const int Capacity = 24;

    public readonly ulong[] CharacterIds = new ulong[Capacity];
    public readonly int[] Levels = new int[Capacity];
    public readonly long[] LastTick = new long[Capacity];

    /// <summary>记录一次贡献（伤害或治疗）。同一角色只更新时刻，不重复占位。</summary>
    public void Note(ulong characterId, int level, long serverTick)
    {
        if (characterId == 0UL) return;
        for (var i = 0; i < Capacity; i++)
        {
            if (CharacterIds[i] != characterId) continue;
            LastTick[i] = serverTick;
            if (level > 0) Levels[i] = level;
            return;
        }

        var worst = -1;
        var worstTick = long.MaxValue;
        for (var i = 0; i < Capacity; i++)
        {
            if (CharacterIds[i] == 0UL)
            {
                CharacterIds[i] = characterId;
                Levels[i] = level > 0 ? level : 1;
                LastTick[i] = serverTick;
                return;
            }
            if (LastTick[i] < worstTick)
            {
                worstTick = LastTick[i];
                worst = i;
            }
        }
        if (worst >= 0)
        {
            CharacterIds[worst] = characterId;
            Levels[worst] = level > 0 ? level : 1;
            LastTick[worst] = serverTick;
        }
    }

    /// <summary>导出为内核参与者数组（已排序由 <see cref="Horizon.Game.Sim.Monsters.MonsterXpCore.SortParticipantsByPriority"/> 负责）。</summary>
    public void CopyTo(List<MonsterXpParticipant> output)
    {
        for (var i = 0; i < Capacity; i++)
        {
            if (CharacterIds[i] == 0UL) continue;
            output.Add(new MonsterXpParticipant
            {
                CharacterId = CharacterIds[i],
                Level = Levels[i] > 0 ? Levels[i] : 1,
                LastContributionTick = LastTick[i],
            });
        }
    }
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

    /// <summary>
    /// 本 tick 的**实体身份变更**（生成/移除），由宿主上送网关建注册表（见 <see cref="DsEntityRegistry"/>）。
    /// <para>稀疏事件：无变更时为空数组，不做每 tick 分配。</para>
    /// </summary>
    public IReadOnlyList<DsEntityDescriptor> EntityDescriptors { get; init; } = Array.Empty<DsEntityDescriptor>();

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
    /// <summary>
    /// 实体类型：玩家。
    /// <para>
    /// 必须与 <c>Horizon.Game.Message.Network.NetworkEntityType</c>（及 Flax 客户端的同号副本）一致：
    /// 原值 1 在共享枚举里是 <c>LocalPlayer</c>，客户端按类型分流 Actor 时会把所有远程玩家读成本地玩家。
    /// </para>
    /// </summary>
    public const int EntityTypePlayer = (int)Horizon.Game.Message.Network.NetworkEntityType.RemotePlayer;

    /// <summary>实体类型：怪物（原值 2 与共享枚举的 <c>RemotePlayer</c> 撞号，客户端曾据此把怪物建成玩家 Actor）。</summary>
    public const int EntityTypeMonster = (int)Horizon.Game.Message.Network.NetworkEntityType.Monster;

    /// <summary>NPC 的网络实体类型（客户端据此选择"不可选中战斗"的呈现分支）。</summary>
    public const int EntityTypeNpc = (int)Horizon.Game.Message.Network.NetworkEntityType.Npc;

    /// <summary>实体类型：地面掉落物 / 可拾取物品。</summary>
    public const int EntityTypeItem = (int)Horizon.Game.Message.Network.NetworkEntityType.Item;

    private readonly DsWorldSessionOptions _options;

    /// <summary>
    /// 玩法区世界底限（与实体 Z 同单位，本工程为 cm）。
    /// <para>
    /// DS 通道是玩家移动的权威方，但这条链路从来没有人给 <see cref="MovementValidator"/>
    /// 注入 <c>GroundHeightSampler</c>（服务端没有 Flax 物理），所以权威 Z 只受重力支配：
    /// 客户端脚下没有碰撞体时会一路下坠（实测相机到 -590m），期间客户端把自己的深渊值上报，
    /// 权威值就此固化；此后即使客户端已经贴回地面，服务端仍持续广播那个值把它拽下去——
    /// 客户端贴地与服务端下拉来回拉扯，就是"停留几秒→沉入地面→反复抖动"。
    /// 底限 0 与客户端 HundunWorldGame.TryLayRuntimeGround 铺的地面顶面同高。
    /// 改成按高度图采样这件事的前提**已经不在盘上了**（2026-10-25 实测：
    /// <c>Content/SceneData/World/Terrain/*_Heightmap.flax</c> 与 <c>*_Heightfield.flax</c> 都在），
    /// 缺的是"服务端读到它"的通路：服务端没有 Flax 物理，所以正解是离线把高度图烘焙成
    /// <c>MonsterNavGrid</c>（编辑器掩码 → CLI <c>bake-nav</c> → <c>--monster-nav</c>），
    /// 再让 <c>GroundHeightSampler</c> 与怪物寻路共用同一份网格，而不是在服务端解析 .flax 资产。
    /// </para>
    /// </summary>
    private const float WorldFloorZ = 0f;

    private readonly SimWorld _simWorld = new(new MovementValidator.Options
    {
        WorldFloorZ = WorldFloorZ,
    });
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

    /// <summary>
    /// 正在地上躺着的怪物尸体（<see cref="DsMonster.CorpseExpireAtTick"/> 非零的实体 ID）。
    /// <para>
    /// 单独记账而不是每次扫全表：一片区域里同时躺着的尸体是少数，
    /// 每 tick 遍历所有实体只为找尸体在几百只怪的 shard 上是纯浪费。
    /// 插入序=死亡序（确定性），到期收尸时再按 ID 升序排一次，让 Despawn 包的字节序稳定。
    /// </para>
    /// </summary>
    private readonly List<ulong> _corpses = new();

    /// <summary>
    /// 每会话"已下发过 Spawn 且尚未告知移除"的实体 ID 集合（仅 <see cref="DsWorldSessionOptions.EnableAoiFilter"/>
    /// 为 true 时参与差分）。
    /// <para>
    /// 为什么必须有它：AOI 裁剪只是把不在订阅 chunk 里的实体从该会话的 deltas 中**剔除**，
    /// 客户端从没有被告知"这个实体没了"，于是远处实体的 Actor 永久残留（鬼影），
    /// 并且"选中目标只在目标消失时才丢"这条语义也无从触发。可见性退出必须显式发 Despawn，
    /// 而 Despawn 需要"上一 tick 你看得见哪些"作为差分的左操作数——实体级
    /// <see cref="DsPlayer.SpawnedToClients"/> 那个全局标志给不出这个信息（它是跨会话的）。
    /// </para>
    /// </summary>
    private readonly Dictionary<long, HashSet<ulong>> _aoiKnownEntityIds = new();
    private readonly List<SpawnPointConfig> _spawnPoints = new();

    /// <summary>
    /// 服务端导航网格（AI v2 的走图数据）。默认 <c>IsValid == false</c> = 所有怪走直线，
    /// 与接入寻路之前的行为逐字相同。宿主在启动时用 <see cref="ConfigureNavigation"/> 装入。
    /// <para>为什么数据由宿主喂进来而不是本类自己造：<c>Horizon.Game.Sim</c> 不许有 IO，
    /// 而"从地形/NavMesh 造这张图"是宿主侧的装配工作（二期由烘焙产物导入）。</para>
    /// </summary>
    private MonsterNavGrid _navGrid;

    /// <summary>寻路工作缓冲（按网格尺寸分配一次，运行期零分配）。</summary>
    private MonsterNavScratch? _navScratch;
    private readonly Dictionary<int, SpawnSlotState[]> _spawnSlots = new();
    /// <summary>
    /// 友好 NPC 的游荡半径（米）。观测：WoW 的平民/商贩在摊位附近来回踱几步，
    /// 不会走出视野。取常量而不是内容字段，是因为 NPC 模板目前没有这一列；
    /// 等编辑器加上去之后只需把这里换成配置值。
    /// </summary>
    internal const float NpcIdleWanderRadiusMeters = 6f;

    /// <summary>
    /// 脚本生成点 → 巡逻路线 id。内容 NPC 的线来自 <c>NpcContent.Path</c>，
    /// 而 <c>ResolvePatrolRouteId</c> 只认怪物内容，所以生成时把路线 id 记在这里，解析时先查它。
    /// </summary>
    private readonly Dictionary<int, int> _scriptPointRouteId = new Dictionary<int, int>();

    /// <summary>脚本生成点 → 显式等级覆盖（刷点表的 <c>level_override</c>）。0/缺省 = 用模板等级。</summary>
    private readonly Dictionary<int, int> _scriptPointLevelOverride = new Dictionary<int, int>();

    private readonly IDsLootProvider _lootProvider;

    /// <summary>
    /// 尸体拾取列表的权威存储（2026-10-28 拍板：住在 Orleans）。
    /// <b>null = 本会话走旧行为</b>（击杀即发）——这是故意的安全门：
    /// 客户端拾取入口就绪前把掉落扣在尸体上，会让玩家永远拿不到。
    /// </summary>
    private readonly ICorpseLootStore? _corpseLootStore;

    private readonly List<(long[] Audience, SyncPacket Packet)> _events = new();
    private readonly List<DsGameplayEvent> _gameplayEvents = new();

    /// <summary>
    /// 待上送的实体身份变更（生成/移除）。与玩法事件同样"随 tick 批量送出后清空"。
    /// </summary>
    private readonly List<DsEntityDescriptor> _entityDescriptors = new();

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
    public DsWorldSession(DsWorldSessionOptions options, IDsLootProvider? lootProvider = null,
                          ICorpseLootStore? corpseLootStore = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _lootProvider = lootProvider ?? new DeterministicLootProvider();
        _corpseLootStore = corpseLootStore;
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

    /// <summary>
    /// 从已装配的 NPC 内容快照生成 NPC（常驻、无战斗）。
    /// <para>
    /// <b>敌对型也生成</b>：它走**怪物管线**（战斗/威胁/脱战/击杀经验全部复用），
    /// 名字与等级由 <c>ResolveMonsterTemplate</c> / <c>ResolveTemplateLevel</c> 退到 NPC 目录。
    /// <b>仍未接的是掉落</b>（NPC 掉落要 owner 扩展，见设计文档 §12 A11）——
    /// 所以敌对 NPC 目前"能打、给经验、不掉东西"，这条差异必须能从日志与文档里读出来。
    /// </para>
    /// <para>
    /// 用 <see cref="CreateScriptedPoint"/> 的同一入口，因此内容 NPC 与 GM 指令 NPC 共享
    /// 生成/快照/到期/身份下发语义（NPC 分支会把仇恨/游荡/速度清零—— NPC 不是"不会动的怪"）。
    /// </para>
    /// </summary>
    /// <returns>(已生成, 其中敌对型, 因分片不匹配跳过, 因模板缺失跳过)。<b>敌对型也生成</b>（走怪物管线）。</returns>
    public (int Spawned, int SkippedHostile, int SkippedShard, int SkippedUnknown) ApplyNpcContentSpawns()
    {
        var points = Horizon.Game.Sim.Npcs.NpcContent.SpawnPoints();
        if (points is null || points.Count == 0) return (0, 0, 0, 0);

        // 排序后再生成：CreateScriptedPoint 的 id 由调用顺序决定，顺序不稳会让实体 id 与快照顺序漂。
        var ordered = new System.Collections.Generic.List<Horizon.Game.Sim.Npcs.NpcSpawnSnapshot>(points.Count);
        for (var i = 0; i < points.Count; i++) ordered.Add(points[i]);
        ordered.Sort(static (a, b) => a.SpawnPointId.CompareTo(b.SpawnPointId));

        int spawned = 0, hostile = 0, wrongShard = 0, unknown = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var point = ordered[i];

            // shard 0 视为"任意分片"（种子脚本里的开发点就是 0），其余必须与本会话一致。
            if (point.ShardId != 0 && point.ShardId != _options.ShardId)
            {
                wrongShard++;
                continue;
            }

            if (!Horizon.Game.Sim.Npcs.NpcContent.TryGetConfig(point.NpcId, out var config) || config is null)
            {
                unknown++;
                continue;
            }

            if (config.Stance == Horizon.Game.Sim.Npcs.NpcStance.Hostile)
            {
                // 敌对型走**怪物管线**生成：战斗/威胁/脱战/击杀经验全部复用，
                // 名字/等级由 ResolveMonsterTemplate / ResolveTemplateLevel 退到 NPC 目录。
                // 掉落暂未绑定（NPC 掉落需要 owner 扩展，见设计文档 §11.4/§12 A11）。
                //
                // AI 能力位里真正影响战斗的两格在这里落地：
                //   * 不还手（DefendsSelf 关）→ 仇恨半径 0：被打了才反应，不主动开战；
                //   * 会追（Pursues 关）→ 同上（不追人就没有"主动索敌"的半径）；
                //   * 会追 → 脱战半径给到哨戒半径的 2.5 倍（下限 30 米，与怪物默认同量级）。
                // 不接的话，"AI 档位"在敌对 NPC 身上完全无表现：站桩山贼与精英山贼行为一模一样。
                var flags = config.Flags;
                var aggro = 0f;
                if (flags.Has(Horizon.Game.Sim.Npcs.NpcAiBits.DefendsSelf)
                    || flags.Has(Horizon.Game.Sim.Npcs.NpcAiBits.Pursues))
                {
                    aggro = config.AlertRadiusMeters > 0 ? config.AlertRadiusMeters : 10f;
                }

                var leash = 0f;
                if (flags.Has(Horizon.Game.Sim.Npcs.NpcAiBits.Pursues))
                {
                    leash = aggro * 2.5f;
                    if (leash < 30f) leash = 30f;
                }

                var hostileId = CreateScriptedPoint(
                    Horizon.Game.Message.Network.ScriptedKind.Monster,
                    point.NpcId,
                    point.X, point.Y, point.Z,
                    count: 1,
                    ttlSeconds: 0,
                    ownerCharacterId: 0,
                    wanderRadiusOverride: flags.Has(Horizon.Game.Sim.Npcs.NpcAiBits.Wanders)
                        ? NpcIdleWanderRadiusMeters
                        : 0f,
                    moveSpeedOverride: flags.Has(Horizon.Game.Sim.Npcs.NpcAiBits.Wanders)
                        ? config.SpeedWalk
                        : 0f,
                    runSpeedOverride: config.SpeedRun,
                    aggroRadiusOverride: aggro,
                    leashRadiusOverride: leash,
                    respawnSecondsOverride: point.RespawnSeconds,
                    yawOverride: ResolveNpcSpawnYaw(point),
                    levelOverride: point.LevelOverride > 0 || config.LevelVariance > 0 ? point.ResolveLevel(config) : 0);
                if (hostileId != 0)
                {
                    // Spawned 记"总生成数"，hostile 记"其中敌对型"——两个计数各答一个问题；
                    // 只加一个会让日志出现"已生成 1（其中敌对型 1）"这种自相矛盾。
                    spawned++;
                    hostile++;
                }

                continue;
            }

            // 会踱步的档位（ai_bits.Wanders）给一圈小范围随机走动；站桩档保持 0。
            // 半径取常量而不是内容字段：WoW 平民的游荡半径是"一个摊位"量级，
            // 这里先给 6 米并留成命名常量，等编辑器有这一列再换成数据。
            var wanderRadius = config.Flags.Has(Horizon.Game.Sim.Npcs.NpcAiBits.Wanders)
                ? NpcIdleWanderRadiusMeters
                : 0f;

            var id = CreateScriptedPoint(
                Horizon.Game.Message.Network.ScriptedKind.Npc,
                point.NpcId,
                point.X, point.Y, point.Z,
                count: 1,
                ttlSeconds: 0,          // 内容 NPC 常驻；到期由退场逻辑处理
                ownerCharacterId: 0,
                wanderRadiusOverride: wanderRadius,
                moveSpeedOverride: config.SpeedWalk,
                runSpeedOverride: config.SpeedRun,
                routeIdOverride: ResolveNpcRouteId(point),
                respawnSecondsOverride: point.RespawnSeconds,
                yawOverride: ResolveNpcSpawnYaw(point),
                levelOverride: point.LevelOverride > 0 || config.LevelVariance > 0 ? point.ResolveLevel(config) : 0);
            if (id != 0) spawned++;
        }

        return (spawned, hostile, wrongShard, unknown);
    }

    /// <summary>
    /// 内容 NPC 走哪条线：刷点上的 <c>RouteId</c>，且内容里那条线至少有两点。
    /// 少于两点时不返回 id（退回"绕出生点踱步"），因为一条一点的线走不动。
    /// </summary>
    private static int ResolveNpcRouteId(Horizon.Game.Sim.Npcs.NpcSpawnSnapshot point)
    {
        if (point.RouteId <= 0) return 0;
        var path = Horizon.Game.Sim.Npcs.NpcContent.Path(point.RouteId);
        return path is { } p && p.Points != null && p.Points.Count >= 2 ? point.RouteId : 0;
    }

    /// <summary>注册一个玩家会话（对应网关的 Handshake 上行）。</summary>
    /// <param name="sessionId">网关会话 ID。</param>
    /// <param name="characterId">角色 ID。</param>
    /// <param name="x">初始 X。</param>
    /// <param name="y">初始 Y。</param>
    /// <param name="z">初始 Z。</param>
    /// <param name="reason">失败原因。</param>
    public bool AddPlayer(long sessionId, ulong characterId, float x, float y, float z, out string reason,
        string nickname = "", int level = 0, string title = "", int faction = 0)
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
            Faction = faction,
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

    /// <summary>
    /// 写入玩家的权威等级与累计经验（网关在击杀经验落进 CharacterGrain 之后回合推下来）。
    /// <para>不写它的后果：DS 的玩家快照里 <c>Exp</c> 永远是 0，而客户端经验条读的就是这一格
    /// ——"怪杀了、grain 里经验涨了，玩家屏幕上的经验条纹丝不动"。</para>
    /// </summary>
    /// <returns>true = 找到该玩家会话并已写入。</returns>
    public bool ApplyPlayerProgress(long characterId, int level, long exp)
    {
        if (characterId <= 0 || !_sessionByEntityId.TryGetValue((ulong)characterId, out var sessionId)
            || !_playersBySession.TryGetValue(sessionId, out var player))
            return false;

        if (level > 0) player.Level = level;
        if (exp >= 0) player.Exp = exp;
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

        // 已知实体集合随会话一并丢弃：留着它既泄漏内存，也会在被复用 sessionId 时误发 Despawn。
        _aoiKnownEntityIds.Remove(sessionId);

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

        // 控制状态：被封腿时把**交给 sim 的那一份**换掉（不动调用方的对象——那是数据面刚反序列化的包）。
        // 客户端不知道自己被晕了，仍会照常发"往前跑"；我们这里把它降成"原地不动"，
        // 并把预测终点钉在他**当前**位置上，否则反作弊校验会因为"声明跑了 5 米却只走了 0 米"当场纠偏。
        var submittedInput = input;
        var slowScale = 1f;
        if (player.Statuses != null && player.StatusCount > 0)
            slowScale = MonsterStatusCore.SpeedScale(player.Statuses, player.StatusCount);
        if (PlayerMovementBlocked(player))
        {
            var here = _simWorld.GetEntity(player.EntityId);
            submittedInput = new InputPacket
            {
                CharacterId = input.CharacterId,
                ClientTick = input.ClientTick,
                InputBits = input.InputBits & ~JumpOrLeapBits,
                LookYaw = input.LookYaw,
                LookPitch = input.LookPitch,
                MoveX = 0f,
                MoveY = 0f,
                PredictedEndX = here?.X ?? input.PredictedEndX,
                PredictedEndY = here?.Y ?? input.PredictedEndY,
                PredictedEndZ = here?.Z ?? input.PredictedEndZ,
                MaxSpeed = input.MaxSpeed,
            };
            InputBlockedByStatus++;
        }
        else if (slowScale < 1f)
        {
            // 减速：**按比例缩小移动输入**（AC 的 `SPELL_AURA_MOD_DECREASE_SPEED` 也是改速度，
            // 不是禁行动）。预测终点同样按比例缩——只缩 Move 而不缩 PredictedEnd，
            // 反作弊会把"声明跑满了却只走了半步"当成漂移去纠偏。
            var here = _simWorld.GetEntity(player.EntityId);
            var curX = here?.X ?? input.PredictedEndX;
            var curY = here?.Y ?? input.PredictedEndY;
            submittedInput = new InputPacket
            {
                CharacterId = input.CharacterId,
                ClientTick = input.ClientTick,
                InputBits = input.InputBits & ~JumpOrLeapBits,
                LookYaw = input.LookYaw,
                LookPitch = input.LookPitch,
                MoveX = input.MoveX * slowScale,
                MoveY = input.MoveY * slowScale,
                PredictedEndX = curX + (input.PredictedEndX - curX) * slowScale,
                PredictedEndY = curY + (input.PredictedEndY - curY) * slowScale,
                PredictedEndZ = input.PredictedEndZ,
                // MaxSpeed **不缩**：它是角色属性（上限），不是这一帧的实际位移；
                // 缩放它会让移动校验看到"上限突然掉一半又涨回去"，那是另一件事（详见那条实测注释）。
                MaxSpeed = input.MaxSpeed,
            };
            InputSlowedByStatus++;
        }

        _simWorld.SubmitInput(
            player.EntityId,
            submittedInput,
            submittedInput.PredictedEndX,
            submittedInput.PredictedEndY,
            submittedInput.PredictedEndZ);
        return true;
    }

    /// <summary>
    /// 处理客户端翻越请求：在 DS 的权威位置上做 <see cref="SimWorld.TryBeginTraversal"/> 裁决，
    /// 并把结果排进该会话的下行事件（客户端据此开始/取消本地翻越预测）。
    /// <para>
    /// 与尸体拾取同一纪律：DS 才知道权威坐标，裁决当场回执，不让客户端干等超时。
    /// </para>
    /// </summary>
    public TraversalRejectReason HandleTraversalRequestFromClient(long sessionId, Horizon.Game.Message.Sync.TraversalRequestPacket request)
    {
        var result = new Horizon.Game.Message.Sync.TraversalResultPacket
        {
            CharacterId = request.CharacterId,
            ClientTick = request.ClientTick,
            ServerTick = _tick,
        };

        if (!_playersBySession.TryGetValue(sessionId, out var player))
        {
            // 没握手的会话：没有权威实体，谈不上翻越。回 AuthorityUnavailable 让客户端当场回正。
            result.Accepted = false;
            result.Status = (byte)TraversalRejectReason.AuthorityUnavailable;
            TraversalRequestsRejected++;
            _events.Add((new[] { sessionId }, result));
            return TraversalRejectReason.AuthorityUnavailable;
        }

        var plan = new TraversalPlan
        {
            StartX = request.StartX,
            StartY = request.StartY,
            StartZ = request.StartZ,
            EndX = request.EndX,
            EndY = request.EndY,
            EndZ = request.EndZ,
            Duration = request.Duration,
            ArcHeight = request.ArcHeight,
            EndYaw = request.EndYaw,
        };

        var reason = request.CurveVersion != TraversalMath.Version
            ? TraversalRejectReason.InvalidNumbers
            : _simWorld.TryBeginTraversal(player.EntityId, plan, _tick);

        var entity = _simWorld.GetEntity(player.EntityId);
        result.Accepted = reason == TraversalRejectReason.None;
        result.Status = (byte)reason;
        result.EndX = entity?.X ?? request.EndX;
        result.EndY = entity?.Y ?? request.EndY;
        result.EndZ = entity?.Z ?? request.EndZ;

        if (result.Accepted)
            TraversalRequestsAccepted++;
        else
            TraversalRequestsRejected++;

        _events.Add((new[] { sessionId }, result));
        TraversalResultPacketsQueued++;
        return reason;
    }

    /// <summary>受理的翻越请求数。</summary>
    public int TraversalRequestsAccepted { get; private set; }
    /// <summary>拒绝的翻越请求数（与受理成对记账；只记一边会把"全拒"读成"没请求"）。</summary>
    public int TraversalRequestsRejected { get; private set; }

    /// <summary>已排入下行队列的翻越结果包数。</summary>
    public int TraversalResultPacketsQueued { get; private set; }

    /// <summary>
    /// 按会话取权威实体的只读视图（诊断/验收：确认翻越曲线真的在推进权威位置）。
    /// 未知会话返回 null。
    /// </summary>
    public SimWorldEntity? GetAuthoritativeEntity(long sessionId)
        => _playersBySession.TryGetValue(sessionId, out var player) ? _simWorld.GetEntity(player.EntityId) : null;

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
        // 先消费脚本指令再跑刷怪调度：本 tick 登记的脚本生成点应在**同一个 tick** 出怪，
        // 操作者才不会看到"指令已受理，但世界过几秒才变"。
        ConsumeScriptCommands();
        TickTriggerZones();
        // 收尸必须排在刷怪之前：尸体窗口与重生共用同一个实体 ID，
        // 先收尸才能让"Despawn 老尸体"和"Spawn 新怪"稳定落在不同 tick。
        TickCorpses();
        ExpirePendingCorpseLoot();
        var spawnedMonsters = TickSpawnDirector();
        TickScriptReceipts();
        TickScriptedExpiry();
        TickMonsters(dt);
        // 玩家的控制状态回收排在怪物后面、快照前面：快照要看到"这一拍还控着谁"，
        // 而回收晚了会让"到期那一拍还多晕一帧"（与怪物侧同一个单点纪律）。
        TickPlayerStatuses();
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
                despawnBroadcast = BuildDespawnPacket(_pendingDespawnEntityIds);
            }

            // 这些实体已彻底离开世界（不是"暂时看不见"），必须同步从各会话的已知集合里摘掉，
            // 否则下一 tick 的 AOI 差分又会把它们当作"可见性退出"再发一次 Despawn。
            if (_options.EnableAoiFilter && _aoiKnownEntityIds.Count > 0)
            {
                foreach (var knownSet in _aoiKnownEntityIds.Values)
                {
                    for (var i = 0; i < _pendingDespawnEntityIds.Count; i++)
                        knownSet.Remove(_pendingDespawnEntityIds[i]);
                }
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
            EntityDescriptors = _entityDescriptors.Count > 0 ? _entityDescriptors.ToArray() : Array.Empty<DsEntityDescriptor>(),
            PlayerCount = _playersBySession.Count,
            MonsterCount = _monstersById.Count,
        };

        // 事件已复制进结果，此处才可安全清空（缓冲复用，避免每 tick 分配 List）。
        _events.Clear();
        _gameplayEvents.Clear();
        _entityDescriptors.Clear();

        return result;
    }

    /// <summary>
    /// 请求打断一只怪的读条（WoW 的"脚踢"那一类：不造成伤害，只掐断那条正在走的条）。
    /// <para>判据全部在这里，且每条出口都单独计数——"踢不动"有三种完全不同的原因
    /// （没在读条 / 那条本来不可打断 / 够不着），混成一个 Invalid 就等于把排查成本推给玩家。</para>
    /// <para>真正的中断发生在**下一拍**的 AI 里（见 <see cref="DsMonster.InterruptRequested"/>）：
    /// 施法推进只有在那一拍才知道自己正在读的是哪一条。</para>
    /// </summary>
    private CombatResolution RequestMonsterInterrupt(DsPlayer player, DsMonster monster)
    {
        if (player.Hp <= 0)
        {
            return new CombatResolution { Outcome = CombatOutcome.AttackerDead };
        }

        var casting = monster.Runtime?.Casting ?? default;
        if (!casting.IsCasting)
        {
            // 没在读条（包括：已经放完了、根本没进战斗、这只怪走的是 v1 没有施法态）
            MonsterInterruptRejectedNoCastCount++;
            return new CombatResolution { Outcome = CombatOutcome.Invalid };
        }

        // "可打断"记在**挂载表**上，不在运行态里（内核那条分支读的也是 `mount.Interruptible`）：
        // 所以这里必须按 ActiveMountIndex 去内容包里取同一条挂载，判据才与内核逐字一致。
        // 查不到挂载 = 内核那一支也不会打断，这里同样拒（而不是放行让它空转一拍）。
        MonsterContent.TryGetConfig(monster.TemplateId, out var templateConfig);
        var mounts = templateConfig?.Skills;
        var mountIndex = casting.ActiveMountIndex;
        var interruptible = mounts != null && mountIndex >= 0 && mountIndex < mounts.Length
                            && mounts[mountIndex].Interruptible;
        if (!interruptible)
        {
            // 编辑器里把"可打断"勾掉的那一条：客户端描的是白边（不是金边），服务端也必须真的踢不动
            MonsterInterruptRejectedNotInterruptibleCount++;
            return new CombatResolution { Outcome = CombatOutcome.Invalid };
        }

        var attackerPos = _simWorld.GetEntity(player.EntityId);
        var ax = attackerPos?.X ?? player.SpawnX;
        var ay = attackerPos?.Y ?? player.SpawnY;
        var range = _options.PlayerAttackRange;
        var dx = monster.Ai.X - ax;
        var dy = monster.Ai.Y - ay;
        if (range > 0f && dx * dx + dy * dy > range * range)
        {
            MonsterInterruptRejectedOutOfRangeCount++;
            return new CombatResolution { Outcome = CombatOutcome.OutOfRange };
        }

        monster.InterruptRequested = true;
        MonsterInterruptAcceptedCount++;
        return new CombatResolution
        {
            // 打断不结算伤害：Outcome 用 Hit 只表示"这个动作被受理了"，Damage 恒为 0。
            // 客户端要确认"踢断了"看的是那条 MonsterCastStop(Interrupted) 事件，不是这里的数字。
            Outcome = CombatOutcome.Hit,
            Damage = 0,
            TargetRemainingHp = monster.Ai.Hp,
        };
    }

    /// <summary>
    /// 请求嘲讽一只怪（WoW 的"坦克把人拉走"）：不造成伤害，只换**强制目标**。
    /// <para>两条通路都要受理，不能只给开了威胁表的那一路开口：
    /// ① v2 → 走内核的锁（<see cref="MonsterRuntimeState.TauntTargetId"/> + 截止时间），
    /// 窗口内不因滞回换回去、窗口一到自动交回威胁表；
    /// ② v1（没有 <c>Runtime</c>；当前 11 只种子怪 <c>ai_profile=0</c>，生产数据下这是**多数**）
    /// → 直接改写单槽目标。只做①的话，"嘲讽"会对绝大多数怪静默失效——那正是本轮一直在清扫的那一类形状。</para>
    /// <para>每条出口单独计数：拉不动的原因（够不着 / 正在回位免疫窗 / 已经是一具尸体）
    /// 混成一个 Invalid 就等于把排查成本推给玩家。</para>
    /// </summary>
    /// <summary>
    /// 回位免疫窗的截止 tick —— 两条通路把窗口装在**各自的状态对象**上：
    /// v2 在 <c>Runtime.EvadeImmuneUntilTick</c>，v1（能力位全关、根本没有 Runtime）在
    /// <c>Ai.EvadeImmuneUntilTick</c>。判据必须两个家都问一遍，否则"免疫窗里拉不住"
    /// 这条对生产数据里全部的种子怪都不成立（表现：坦克在怪回家的路上照样能把它拽走）。
    /// </summary>
    private static long EvadeImmuneUntilOf(DsMonster monster)
        => monster.Runtime != null ? monster.Runtime.EvadeImmuneUntilTick : monster.Ai.EvadeImmuneUntilTick;

    /// <summary>
    /// 这只怪对客户端可见的状态位：<b>掩码只有这一个主人</b>。
    /// <para>以前两处快照各写一份 <c>Hp &lt;= 0 ? Dead : 0</c>，而"回位中打不到"这件事要第三个人知道
    /// （否则玩家按下去什么都不发生，而没人能说出为什么）。写位的地方一旦有两份，
    /// 加一位就得记得改两处——漏掉的那一处表现为"另一台下行通路上这只怪又能打了"。</para>
    /// <para><c>Evading</c> 这一位与拒伤用的是同一条判据（<see cref="MonsterAiGate.BlocksIncomingDamage"/>），
    /// 所以"客户端说打不到"与"服务端真的拒了"不可能各说各话。</para>
    /// </summary>
    private uint MonsterStateBitsOf(DsMonster monster, long tick)
    {
        uint bits = 0u;
        if (monster.Ai.Hp <= 0) bits |= EntityStateBits.Dead;
        if (MonsterAiGate.BlocksIncomingDamage(MonsterAiGate.ReturningHome(monster.Ai.State)))
            bits |= EntityStateBits.Evading;
        // 尸体可拾取位必须进**快照**：客户端只看得见快照，看不见网关那份实体描述符。
        // 窗口一关就收位——尸体可能还在地上（尸体停留 ≥ 拾取窗口），那时它已经取不了了。
        if (monster.CorpseLootRegistered && monster.CorpseLootExpireAtTick > tick)
            bits |= EntityStateBits.CorpseInteractable;
        return bits;
    }

    private CombatResolution RequestMonsterTaunt(DsPlayer player, DsMonster monster)
    {
        if (player.Hp <= 0)
        {
            return new CombatResolution { Outcome = CombatOutcome.AttackerDead };
        }

        // 尸体不能被拉（它既不会动也不会打）。这条判据与"求援盟友要滤掉尸体"是同一条口径。
        if (monster.Ai.Hp <= 0 || monster.CorpseExpireAtTick != 0)
        {
            MonsterTauntRejectedCorpseCount++;
            return new CombatResolution { Outcome = CombatOutcome.Invalid };
        }

        var attackerPos = _simWorld.GetEntity(player.EntityId);
        var ax = attackerPos?.X ?? player.SpawnX;
        var ay = attackerPos?.Y ?? player.SpawnY;
        var range = _options.MonsterTauntRange > 0f ? _options.MonsterTauntRange : _options.PlayerAttackRange;
        var dx = monster.Ai.X - ax;
        var dy = monster.Ai.Y - ay;
        if (range > 0f && dx * dx + dy * dy > range * range)
        {
            MonsterTauntRejectedOutOfRangeCount++;
            return new CombatResolution { Outcome = CombatOutcome.OutOfRange };
        }

        var runtime = monster.Runtime;
        // 回位免疫窗 + 回位整段路都拉不住（AC 的 UNIT_STATE_EVADE 覆盖 MoveTargetedHome 全程）。
        if (!MonsterAiGate.CanBeForcedIntoCombat(_tick, EvadeImmuneUntilOf(monster),
                                                MonsterAiGate.ReturningHome(monster.Ai.State)))
        {
            // 回位免疫窗里拉不住（与"evade 途中不该回头再咬"同一条口径）。
            // 必须**当场拒**而不是"先答应下来、窗过再生效"：后者会让坦克以为拉到了而让怪自己走回去。
            MonsterTauntRejectedImmuneCount++;
            return new CombatResolution { Outcome = CombatOutcome.Invalid };
        }

        if (runtime != null)
        {
            MonsterAiRuntime.NoteTaunt(runtime, player.EntityId, _tick,
                _options.TicksPerSecond, _options.MonsterTauntSeconds);
            MonsterTauntAcceptedCount++;
        }
        else
        {
            // v1 的怪没有 Runtime，锁就装在它自己的 AI 状态上（同一套三条出口仍由 MonsterAiGate 判）：
            // 只钉目标不给窗口 = "永久粘住"，给了窗口不给对象 = "那人不在场就撤锁"这条没法执行。
            // 窗内它不查脱战距离——否则坦克把怪拉开，怪跑一半还是 evade 回去。
            var ai = monster.Ai;
            ai.ThreatTargetId = player.EntityId;
            ai.TauntTargetId = player.EntityId;
            ai.TauntUntilTick = _tick + (long)(_options.MonsterTauntSeconds * _options.TicksPerSecond);
            monster.Ai = ai;
            MonsterTauntV1RetargetCount++;
        }

        return new CombatResolution
        {
            // 与打断同形：受理与否看这个 Outcome，伤害恒为 0（嘲讽不结算伤害）。
            Outcome = CombatOutcome.Hit,
            Damage = 0,
            TargetRemainingHp = monster.Ai.Hp,
        };
    }

    /// <summary>
    /// 最近一次普通/技能攻击的**权威几何**（攻方位置、目标位置、射程）。
    /// <para>为什么要有它：客户端觉得"贴脸"而服务端判 OutOfRange 时，只数"超射程 N 次"
    /// 分不清是玩家坐标没同步上来还是怪在别处——这三对数字能当场分辨。</para>
    /// </summary>
    public float LastCombatAttackerX { get; private set; }
    public float LastCombatAttackerY { get; private set; }
    public float LastCombatTargetX { get; private set; }
    public float LastCombatTargetY { get; private set; }
    public float LastCombatRange { get; private set; }

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

        // 控制状态：**技能类动作**在眩晕/沉默期间不许发出。
        // 依据：AC 的 `Spell::CheckCasterAuras`（`Spell.cpp:6820`）见 `UNIT_FLAG_STUNNED/SILENCED`
        // 直接回 `SPELL_FAILED_STUNNED/SILENCED`（`:6863-6891`）；而**普通近战不受影响**——
        // `Unit::AttackerStateUpdate`（`Unit.cpp:2601-2606`）只看 `UNIT_FLAG_PACIFIED`。
        // 所以这里只拦"不是普通攻击"的那几类（嘲讽/打断），门放在解析目标与任何结算**之前**：
        // 走到这行之前什么都没改过，拒了不用回滚。
        if (action.ActionKind != CombatActionKind.NormalAttack && PlayerCastingBlocked(player))
        {
            CombatActionRejectedByStatus++;
            return new CombatResolution { Outcome = CombatOutcome.Invalid };
        }

        // 目标是另一个玩家时的判定：合法性只由阵营矩阵给（客户端自报的阵营不采信，
        // 它来自网关填的权威档案）。敌对阵营真结算（伤害/死亡/经验），其余一律 Invalid。
        if (!_monstersById.TryGetValue(targetEntityId, out var monster))
        {
            if (_sessionByEntityId.TryGetValue(targetEntityId, out var targetSessionId)
                && _playersBySession.TryGetValue(targetSessionId, out var targetPlayer))
            {
                return ResolvePlayerVsPlayer(sessionId, player, targetSessionId, targetPlayer, action);
            }

            return new CombatResolution { Outcome = CombatOutcome.Invalid };
        }

        // 打断是**另一类动作**：不造成伤害，只掐掉一条正在读的条。
        // 内核那条分支（MonsterCastingCore 的 interruptSignal）从写下起就没有任何调用方能触发它——
        // 表现是"怪的条子只能等它自己放完"，与 WoW 的"一脚踢断"完全相反。
        if (action.ActionKind == CombatActionKind.Interrupt)
        {
            return RequestMonsterInterrupt(player, monster);
        }

        // 嘲讽是**第三种"不造成伤害的动作"**：换强制目标，不进 CombatCore 的命中/伤害结算。
        if (action.ActionKind == CombatActionKind.Taunt)
        {
            return RequestMonsterTaunt(player, monster);
        }

        // 回位中的怪**打不到**（AC 挂在受击方那侧的 `Creature::IsEvadingAttacks()`，`Creature.h:139`：
        // `Unit::CanAttack:10496` 把攻击指令本身挡掉，`:2846/:1480/:1867/:3529` 再把已经在飞的那一手
        // 逐层挡掉，最后 `SpellHitResult` 回 `SPELL_MISS_EVADE`——玩家屏幕上那行"闪避"就是它）。
        // 这道门必须挡在 `CombatCore.Resolve` **之前**：Resolve 之后再拒，伤害、贡献记账、
        // v1 的"先挨那一手钉住攻击者"、v2 的威胁表写入都已经落在结算尾部了，丢不干净。
        // 注意这条比"不许接新敌"窄：复活宽限与到家后的免疫窗都不挡打（判据只有主人那一份）。
        if (MonsterAiGate.BlocksIncomingDamage(MonsterAiGate.ReturningHome(monster.Ai.State)))
        {
            MonsterDamageRejectedEvadingCount++;
            return new CombatResolution
            {
                Outcome = CombatOutcome.Evading,
                Damage = 0,
                TargetRemainingHp = monster.Ai.Hp,
                TargetMaxHp = BuildMonsterStats(monster).MaxHp,
            };
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

        LastCombatAttackerX = request.AttackerX;
        LastCombatAttackerY = request.AttackerY;
        LastCombatTargetX = request.TargetX;
        LastCombatTargetY = request.TargetY;
        LastCombatRange = request.AttackRange;

        if (resolution.DealtDamage)
        {
            ApplyDamageToMonster(monster, resolution, attackerEntityId);

            // 玩家技能把控制状态挂到**怪物**身上（v16 给动作包追加了那两格）。
            // 落点与伤害同一条件（打空了不挂），与怪物打玩家那条完全同形——AC 那边两边都是
            // "技能 effect 落在目标单位上"（`Spell.cpp:2925/:3080`），差别只在配置写在哪。
            ApplyStatusToMonster(monster, action.StatusId, action.StatusDurationSeconds, action.StatusMagnitudeBps);

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

    /// <summary>
    /// 玩家互殴（PvP）结算：<b>合法性只由 <see cref="Horizon.Game.Sim.Factions.FactionCore"/> 判</b>，
    /// 伤害走与打怪同一个 <see cref="CombatCore"/>，死亡复用玩家复活排定。
    /// <para>
    /// 为什么必须与打怪同一条结算内核：另写一套 PvP 伤害会让"同一把刀砍怪"和"砍人"出现两条公式，
    /// 而玩家只会拿两边的数字互相对照。阵营只决定"能不能打"，不决定"打多少"。
    /// </para>
    /// <para>
    /// 击杀经验按 <c>NpcKillCore</c> 的 <c>PvpPlayer</c> 来源算（与打怪同一条让分曲线，
    /// 所以"大号杀小号不给经验"与"打灰名怪"是同一条判据）。
    /// </para>
    /// </summary>
    private CombatResolution ResolvePlayerVsPlayer(
        long attackerSessionId,
        DsPlayer attacker,
        long targetSessionId,
        DsPlayer target,
        CombatActionPacket action)
    {
        // 阵营矩阵：非敌对（同阵营/未入阵营/脏数据）直接拒，与"打了个空气"共用 Invalid 是有意的——
        // 玩家这边的表现本来就该一样：打不了。
        var faction = Horizon.Game.Sim.Factions.FactionCore.Evaluate(attacker.Faction, target.Faction);
        if (!faction.Hostile)
        {
            PvpRejectedFactionCount++;
            return new CombatResolution { Outcome = CombatOutcome.Invalid };
        }

        if (attacker.Hp <= 0) return new CombatResolution { Outcome = CombatOutcome.AttackerDead };
        if (target.Hp <= 0) return new CombatResolution { Outcome = CombatOutcome.TargetDead };

        var attackerPos = _simWorld.GetEntity(attacker.EntityId);
        var targetPos = _simWorld.GetEntity(target.EntityId);

        var request = new CombatRequest
        {
            AttackerId = attacker.EntityId,
            TargetId = target.EntityId,
            SkillId = action.SkillId,
            SkillRawDamage = action.ActionKind == CombatActionKind.SkillCast ? _options.PlayerAttackPower : 0f,
            ServerTick = _tick,
            AttackerX = attackerPos?.X ?? attacker.SpawnX,
            AttackerY = attackerPos?.Y ?? attacker.SpawnY,
            AttackerYaw = action.AttackerYaw,
            TargetX = targetPos?.X ?? target.SpawnX,
            TargetY = targetPos?.Y ?? target.SpawnY,
            AttackRange = _options.PlayerAttackRange,
            FacingCosThreshold = _options.FacingCosThreshold,
        };

        var resolution = CombatCore.Resolve(request, BuildPlayerStats(attacker), BuildPlayerStats(target));
        if (!resolution.DealtDamage) return resolution;

        target.Hp = resolution.TargetRemainingHp;
        PvpDamageDealtCount++;

        // 伤害数字双方都要看到：攻击者看自己的战果，受击者看自己掉了多少。
        // 怪物打人那条只发给受击者，是因为打怪者已经拿到了伤害回执；PvP 两边都是真人。
        var audience = attackerSessionId == targetSessionId
            ? new[] { targetSessionId }
            : new[] { targetSessionId, attackerSessionId };
        _events.Add((audience, new DamagePacket
        {
            AttackerId = attacker.EntityId,
            TargetId = target.EntityId,
            DamageAmount = resolution.Damage,
            DamageType = 1, // 1 = 物理（既有 DamagePacket.DamageType 约定）
            IsCritical = resolution.IsCritical,
            RemainingHp = resolution.TargetRemainingHp,
            MaxHp = resolution.TargetMaxHp,
            SkillId = action.SkillId,
            ServerTick = _tick,
        }));

        if (!resolution.TargetDied) return resolution;

        HandlePlayerDeathByPlayer(attackerSessionId, attacker, targetSessionId, target);
        return resolution;
    }

    /// <summary>PvP 死亡尾部：复活排定 + 死亡表现 + 死亡/击杀经验事件（与"怪物打死玩家"共用复活语义）。</summary>
    private void HandlePlayerDeathByPlayer(long attackerSessionId, DsPlayer attacker, long targetSessionId, DsPlayer target)
    {
        target.RespawnAtTick = _tick + (long)(_options.PlayerRespawnDelaySeconds * _options.TicksPerSecond);

        var audience = attackerSessionId == targetSessionId
            ? new[] { targetSessionId }
            : new[] { targetSessionId, attackerSessionId };
        _events.Add((audience, new DeathPacket
        {
            EntityId = target.EntityId,
            KillerId = attacker.EntityId,
            RespawnDelaySeconds = _options.PlayerRespawnDelaySeconds,
            ServerTick = _tick,
        }));

        // 死亡惩罚/统计走业务层（与怪物击杀玩家同一种事件）。
        _gameplayEvents.Add(new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.PlayerDeath,
            ShardId = _options.ShardId,
            ServerTick = _tick,
            ActorCharacterId = target.CharacterId,
            MonsterEntityId = attacker.EntityId,
        });

        // 荣誉经验：PvpPlayer 来源走同一条让分曲线；灰名/等级不可用时 Xp=0，事件就不发（与怪物侧一致）。
        var verdict = Horizon.Game.Sim.Npcs.NpcKillCore.Evaluate(
            Horizon.Game.Sim.Npcs.NpcKillSource.PvpPlayer,
            Horizon.Game.Sim.Monsters.MonsterXpConfig.Default,
            target.Level > 0 ? target.Level : 1,
            attacker.Level > 0 ? attacker.Level : 1);

        if (verdict.Xp <= 0) return;

        _gameplayEvents.Add(new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.KillXp,
            ShardId = _options.ShardId,
            ServerTick = _tick,
            ActorCharacterId = attacker.CharacterId,
            MonsterEntityId = target.EntityId,
            ItemCount = (int)Math.Min(int.MaxValue, verdict.Xp),
        });
        PvpKillXpGranted += verdict.Xp;
    }

    /// <summary>把玩家数值投影为战斗内核入参。</summary>
    private CombatantStats BuildPlayerStats(DsPlayer player) => new CombatantStats    {
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
        // 上限与攻击力的权威只有一个：装配了内容时按**这只怪自己的生效等级**从同一个投影出口算
        // （等级区间现在逐只掷，见 MonsterLevelCore.Roll）。
        // 不这么做的话，"等级 7 的那只"会拿着装配期按基准等级算的血量——两套账，玩家看得见的是
        // "同一营地的两只同级怪血差一大截"或反过来"等级牌变了而血没变"。
        if (MonsterContent.TryGetConfig(monster.TemplateId, out var contentCfg) && contentCfg != null)
            cfg = BuildAiConfigFromContent(contentCfg, cfg, MonsterLevelOf(monster));

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
        // 风筝宽限窗的起点：AC 在伤害结算里 `UpdateLeashExtensionTime()`（Unit.cpp:1088 等）。
        // 这是**唯一**写方；内核只读它来跳过"离家过远"（MonsterAiGate.KitingGraceActive）。
        if (resolution.DealtDamage)
        {
            self.LastLeashExtensionTick = _tick;
            MonsterLeashExtensions++;
        }
        if (resolution.TargetDied)
        {
            self.State = MonsterAiState.Dead;
            self.ThreatTargetId = attackerEntityId;
        }
        else if (resolution.DealtDamage && monster.Runtime == null && self.ThreatTargetId == 0UL)
        {
            // v1 的怪没有威胁表，"先挨的那一手"必须当场钉住攻击者，否则"刚复活不许主动起手"
            // 会变成"打了它也不还手"——AzerothCore 那条判据带的正是 `!IsEngagedBy(victim)` 这个例外。
            // 已经锁着别人的不换（v1 单槽语义：锁定后不轻易换目标）。
            self.ThreatTargetId = attackerEntityId;
        }
        monster.Ai = self;
        // AC 的"共享 leash 计时器"（`Creature.cpp:231`）：打中组内任何一只，整组的宽限窗一起刷新，
        // 表现是"拉一只，同伴一起回家"。只刷新**同组**的（0 = 自己一组，不扩散）。
        if (resolution.DealtDamage) RefreshLeashTimerGroup(monster.EntityId, self.LeashGroupId);

        // 贡献者记账：打中就记，**与最后一击无关**。WoW 的观感是"摸过就有经验"；
        // 只把经验挂在 killer 上，组队刷怪就会退化成"抢最后一下"。
        if (resolution.DealtDamage) NoteContribution(monster, attackerEntityId, _tick);

        // 威胁表记账（只在 v2 已挂载时）。治疗入口留在 MonsterAiRuntime.NoteHealing，
        // 会话里目前没有治疗通路，接上时不必再改内核。
        if (monster.Runtime != null && resolution.DealtDamage)
            MonsterAiRuntime.NoteDamage(monster.Runtime, attackerEntityId, resolution.Damage, _tick);

        // NPC 求援（CallsReinforcement）：被玩家打中时叫醒身边的同族敌对 NPC。
        // 只在这一处接：ApplyDamageToMonster 是所有对怪伤害的唯一写点。
        if (resolution.DealtDamage && !resolution.TargetDied)
            TryWakeNpcReinforcements(monster, attackerEntityId);
    }

    /// <summary>
    /// 这一刻能不能从这具尸体上取东西（距离 + 窗口）。
    /// <para>权威列表在 Orleans（2026-10-28 拍板）；这一处只做"请求到达 DS 时的门槛"：
    /// 实体不在 / 已经不在尸体阶段 / 窗口过期 / 站太远，都要能当场说清是哪一种，
    /// 而不是统一回一句"拾取失败"。<see cref="CanLootCorpse"/> 的判据与客户端提示**同源**。</para>
    /// </summary>
    public bool CanLootCorpse(ulong entityId, float distanceMeters, out MonsterCorpseCore.PickupDenial denial)
    {
        if (!_monstersById.TryGetValue(entityId, out var monster) || monster == null || monster.CorpseExpireAtTick == 0)
        {
            denial = MonsterCorpseCore.PickupDenial.Expired;   // 不在世界里 / 不是尸体：对外就是"取不了"
            return false;
        }
        denial = MonsterCorpseCore.CanPickUp(_tick, monster.CorpseLootExpireAtTick, distanceMeters, monster.PickupDistanceMeters);
        return denial == MonsterCorpseCore.PickupDenial.None;
    }

    /// <summary>
    /// 把"被叫来的盟友"并进首领那一组 leash 计时器（AC `Creature.cpp:231` 的 `SetLastLeashExtensionTimePtr`）。
    /// <para>首领若已是某组的一员，就沿用那一组（不另开新组）；否则组号取首领自己的实体 id。
    /// 只有非 0 组号才会在伤害落点扩散（<see cref="RefreshLeashTimerGroup"/>），0 表示"自己一组"。</para>
    /// </summary>
    private void LinkLeashTimerGroup(DsMonster assistant, ulong callerId)
    {
        if (callerId == 0UL) return;
        if (!_monstersById.TryGetValue(callerId, out var caller) || caller == null) return;
        if (ReferenceEquals(caller, assistant)) return;

        var group = caller.Ai.LeashGroupId != 0UL ? caller.Ai.LeashGroupId : callerId;
        var leader = caller.Ai;
        leader.LeashGroupId = group;
        caller.Ai = leader;
        var helper = assistant.Ai;
        helper.LeashGroupId = group;
        assistant.Ai = helper;
        MonsterLeashGroupsLinked++;
    }

    /// <summary>
    /// 伤害落点刷新整组的宽限窗起点（AC：打中组内任何一只，<c>*lastLeashExtensionTime = now</c> 对所有共享者生效）。
    /// <para>按实体 id 升序不必要（只写标量、结果与顺序无关），但仍走同一个字典以便"同 tick 同结果"。</para>
    /// </summary>
    private void RefreshLeashTimerGroup(ulong exceptId, ulong groupId)
    {
        if (groupId == 0UL) return;
        foreach (var kv in _monstersById)
        {
            var other = kv.Value;
            if (other == null || other.EntityId == exceptId) continue;
            if (other.Ai.LeashGroupId != groupId) continue;
            var ai = other.Ai;
            if (ai.LastLeashExtensionTick == _tick) continue;   // 同一拍已经刷过：计数只报"真的多刷了几只"
            ai.LastLeashExtensionTick = _tick;
            other.Ai = ai;
            MonsterLeashSharedRefreshes++;
        }
    }

    /// <summary>
    /// NPC 求援：把警戒半径内**同模板、活着、还没目标**的敌对 NPC 的目标钉到攻击者身上。
    /// <para>
    /// <b>为什么在宿主里做而不进内核</b>：v1 的 AI 是单槽、没有"社交仇恨"入口；这里只做一次跨实体唤醒
    /// （写对方的 <c>ThreatTargetId</c>），之后它走的就是与"先挨了一下"完全相同的既有通路——
    /// 不新增任何状态位，也不改内核判据。
    /// </para>
    /// <para>复活宽限 / 回位免疫窗内的怪不叫醒：那两窗本来就只挡"接新敌"。</para>
    /// </summary>
    private void TryWakeNpcReinforcements(DsMonster victim, ulong attackerEntityId)
    {
        if (attackerEntityId == 0UL) return;
        if (!Horizon.Game.Sim.Npcs.NpcContent.TryGetConfig(victim.TemplateId, out var cfg) || cfg == null) return;
        if (cfg.Stance != Horizon.Game.Sim.Npcs.NpcStance.Hostile) return;
        if (!cfg.Flags.Has(Horizon.Game.Sim.Npcs.NpcAiBits.CallsReinforcement)) return;

        var radius = cfg.AlertRadiusMeters;
        if (radius <= 0f) return;   // 装载期已要求勾了位就必须配半径；这里仍按"没配就不叫"处理
        var radiusSq = radius * radius;

        // 遍历顺序不影响结果：每只独立判定、写的是同一份"目标 id"（没有先后依赖）。
        foreach (var kv in _monstersById)
        {
            var other = kv.Value;
            if (ReferenceEquals(other, victim)) continue;
            if (other.TemplateId != victim.TemplateId) continue;
            if (other.Ai.Hp <= 0 || other.CorpseExpireAtTick != 0) continue;
            if (other.Ai.ThreatTargetId != 0UL) continue;
            if (other.Ai.NoInitiateUntilTick > _tick || other.Ai.EvadeImmuneUntilTick > _tick) continue;

            var dx = other.Ai.X - victim.Ai.X;
            var dy = other.Ai.Y - victim.Ai.Y;
            if (dx * dx + dy * dy > radiusSq) continue;

            var self = other.Ai;
            self.ThreatTargetId = attackerEntityId;
            other.Ai = self;
            NpcReinforcementWokenCount++;
        }
    }

    /// <summary>记录一次对这只怪的贡献（角色与等级由攻击者实体反查会话表得到）。</summary>
    private void NoteContribution(DsMonster monster, ulong attackerEntityId, long serverTick)
    {
        if (!_sessionByEntityId.TryGetValue(attackerEntityId, out var sessionId)) return;
        if (!_playersBySession.TryGetValue(sessionId, out var player)) return;
        if (player.CharacterId == 0UL) return;

        // 账本懒分配：站桩小怪被路过的人摸一下也要开始记，否则那个人拿不到经验。
        monster.Contributors ??= new MonsterContributors();
        monster.Contributors.Note(player.CharacterId, player.Level > 0 ? player.Level : 1, serverTick);
    }

    /// <summary>
    /// 把一次击杀的掷骰结果登记给权威侧（Orleans）。
    /// <para>槽序固定：普通件 → 条件件 → 金币，与 <c>MonsterCorpseLootState.FromRoll</c> 同一顺序。
    /// 它会被写进持久层，以后加东西只能往尾部追加——否则旧存档的槽号会整体平移。</para>
    /// </summary>
    private void RegisterCorpseLoot(DsMonster monster, MonsterLootRollResult roll, ulong killerCharacterId)
    {
        // 注意：这里**不能**因为"没有本地存储"就提前返回——上行那一份登记与本地存储无关，
        // 权威侧本来就住在 Orleans。早返回会让"只走上行"的配置连登记都不发（本轮实测踩到）。
        var registration = new CorpseLootRegistration
        {
            CorpseEntityId = monster.EntityId,
            ExpireTick = monster.CorpseLootExpireAtTick,
            PickupDistanceMeters = monster.PickupDistanceMeters,
        };
        for (var i = 0; i < roll.Drops.Count; i++)
        {
            var d = roll.Drops[i];
            registration.Slots.Add(new CorpseLootSlotDto
            {
                ItemId = d.ItemId, Count = d.Count, ConditionKind = d.ConditionKind, QuestId = d.QuestId,
            });
        }
        for (var i = 0; i < roll.ConditionalDrops.Count; i++)
        {
            var d = roll.ConditionalDrops[i];
            registration.Slots.Add(new CorpseLootSlotDto
            {
                ItemId = d.ItemId, Count = d.Count, ConditionKind = d.ConditionKind, QuestId = d.QuestId,
            });
        }
        if (roll.Money > 0)
            registration.Slots.Add(new CorpseLootSlotDto { ItemId = 0, Count = 1, Money = roll.Money });

        // 击杀参与者（AC DIDNT_KILL）：在登记这一刻定下来，本地门与权威侧此后都只看这一份。
        // 账本为 null（环境击杀/自爆，没有任何玩家碰过它）时留空 = 不限制。
        // 放在"没东西就不登记"之后：那具尸体根本没有拾取列表，留一份名单只会让人以为它登记过。
        if (registration.Slots.Count == 0)
        {
            // 没东西就不登记：空列表会让拾取回一个 Empty，而玩家看到的是一个看得见、点不开的尸体。
            return;
        }
        if (monster.Contributors != null)
        {
            var known = new List<MonsterXpParticipant>();
            monster.Contributors.CopyTo(known);
            for (var i = 0; i < known.Count; i++)
            {
                var id = known[i].CharacterId;
                if (id == 0UL || registration.Contributors.Contains(id)) continue;
                registration.Contributors.Add(id);
            }
        }
        monster.CorpseLootContributors = registration.Contributors.Count > 0
            ? registration.Contributors.ToArray()
            : null;

        // 权威侧那份走上行四帧里的 Register（宾客两个地方：本地存储只是测试脚手架）。
        _pendingCorpseLootRegistrations.Add(new DsCorpseLootRegister
        {
            CorpseEntityId = registration.CorpseEntityId,
            ExpireTick = registration.ExpireTick,
            PickupDistanceMeters = registration.PickupDistanceMeters,
            Slots = BuildCorpseLootWireSlots(registration),
            Contributors = monster.CorpseLootContributors ?? Array.Empty<ulong>(),
        });
        monster.CorpseLootRegistered = true;
        monster.CorpseLootSlotsTotal = registration.Slots.Count;
        monster.CorpseLootSlotsTaken = 0;
        monster.CorpseLootExpiredNotified = false;
        if (_corpseLootStore == null) return;
        var task = _corpseLootStore.RegisterAsync(registration);
        if (task.IsCompletedSuccessfully) MonsterCorpseLootRegistered++;
        else task.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) Interlocked.Increment(ref _corpseLootRegistered);
            else Interlocked.Increment(ref _corpseLootRegisterFailures);
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// 拾取一个槽：DS 先过自己那道门（距离/窗口），再由权威侧裁决。
    /// <para><paramref name="distanceMeters"/> 由调用方给出（DS 不持玩家坐标）；取走成功时才发入包事件。</para>
    /// <para>取走这一步在 DS 的 tick 线程上同步等待权威回执：今天只有内存实现被测试驱动，接真 Grain 时要换成异步。</para>
    /// </summary>
    public CorpseLootTakeStatus TryLootCorpse(ulong corpseEntityId, long sessionId, int slotIndex, float distanceMeters)
    {
        if (_corpseLootStore == null) return CorpseLootTakeStatus.UnknownCorpse;
        if (!_playersBySession.TryGetValue(sessionId, out var player) || player == null || player.IsDead)
            return CorpseLootTakeStatus.UnknownCorpse;
        if (_monstersById.TryGetValue(corpseEntityId, out var corpse) && corpse != null
            && !MonsterCorpseCore.IsAllowedLooter(corpse.CorpseLootContributors, player.CharacterId))
        {
            CorpseLootRejectedNotYours++;
            return CorpseLootTakeStatus.NotYours;
        }
        if (!CanLootCorpse(corpseEntityId, distanceMeters, out var denial))
        {
            return denial == MonsterCorpseCore.PickupDenial.TooFar
                ? CorpseLootTakeStatus.TooFar
                : CorpseLootTakeStatus.Expired;
        }
        var result = _corpseLootStore.TakeAsync(corpseEntityId, player.CharacterId, slotIndex, _tick, distanceMeters)
            .GetAwaiter().GetResult();
        if (result.Status == CorpseLootTakeStatus.ConditionNotMet) CorpseLootRejectedCondition++;
        if (result.Status != CorpseLootTakeStatus.Taken || result.Slot == null) return result.Status;

        var monster = _monstersById.TryGetValue(corpseEntityId, out var m) ? m : null;
        EmitCorpseLootGrant(player.CharacterId, corpseEntityId, monster != null ? monster.TemplateId : 0, result.Slot);
        return CorpseLootTakeStatus.Taken;
    }

    /// <summary>
    /// 把"这一槽真的被取走了"落成一条入包事件（金币 / 无条件 / 条件件三档）。
    /// <para>存储路径与上行协议路径**共用这一处**：两条各写一遍的话，其中一条迟早漏掉一个档，
    /// 而症状只是"某种东西取了不掉"——不报错、不计数。</para>
    /// </summary>
    private void EmitCorpseLootGrant(ulong takerCharacterId, ulong corpseEntityId, int templateId, in CorpseLootSlotDto slot)
    {
        MonsterCorpseLootTaken++;
        // "还剩几件"只在 DS 这一侧记：权威侧的位图跨进程拿不到，而提示要在窗口到期那一拍决定发不发。
        if (_monstersById.TryGetValue(corpseEntityId, out var corpseMonster) && corpseMonster != null)
            corpseMonster.CorpseLootSlotsTaken++;
        if (slot.Money > 0)
        {
            _gameplayEvents.Add(new DsGameplayEvent
            {
                Kind = DsGameplayEventKind.KillMoney,
                ShardId = _options.ShardId,
                ServerTick = _tick,
                ActorCharacterId = takerCharacterId,
                MonsterEntityId = corpseEntityId,
                MonsterTemplateId = templateId,
                Money = slot.Money,
            });
            return;
        }
        _gameplayEvents.Add(new DsGameplayEvent
        {
            Kind = slot.ConditionKind == 0 ? DsGameplayEventKind.Loot : DsGameplayEventKind.LootConditional,
            ShardId = _options.ShardId,
            ServerTick = _tick,
            ActorCharacterId = takerCharacterId,
            MonsterEntityId = corpseEntityId,
            MonsterTemplateId = templateId,
            ItemId = slot.ItemId,
            ItemCount = slot.Count,
            QuestId = slot.QuestId,
        });
    }

    private readonly List<DsCorpseLootRegister> _pendingCorpseLootRegistrations = new();
    private readonly List<DsCorpseLootGone> _pendingCorpseLootGone = new();

    /// <summary>取走自上次调用以来待上送的"登记"帧（宿主负责真正发送）。</summary>
    public List<DsCorpseLootRegister> DrainCorpseLootRegistrations()
    {
        var copy = new List<DsCorpseLootRegister>(_pendingCorpseLootRegistrations);
        _pendingCorpseLootRegistrations.Clear();
        return copy;
    }

    /// <summary>取走自上次调用以来待上送的"作废"帧。</summary>
    public List<DsCorpseLootGone> DrainCorpseLootGone()
    {
        var copy = new List<DsCorpseLootGone>(_pendingCorpseLootGone);
        _pendingCorpseLootGone.Clear();
        return copy;
    }

    private static CorpseLootSlotWire[] BuildCorpseLootWireSlots(in CorpseLootRegistration registration)
    {
        var slots = new CorpseLootSlotWire[registration.Slots.Count];
        for (var i = 0; i < slots.Length; i++)
        {
            var s = registration.Slots[i];
            slots[i] = new CorpseLootSlotWire
            {
                ItemId = s.ItemId, Count = s.Count, ConditionKind = s.ConditionKind, QuestId = s.QuestId, Money = s.Money,
            };
        }
        return slots;
    }

    private long _nextCorpseLootRequestId;

    /// <summary>
    /// 由客户端请求包进来的拾取：**距离在这里量**（DS 才知道权威坐标），然后走与上行请求完全相同的两道门。
    /// <para>拒绝时直接返回带回执；通过时把 forward 放进待发队列（宿主经上行转给网关）。</para>
    /// </summary>
    public CorpseLootTakeStatus HandleCorpseLootTakeFromClient(long sessionId, ulong corpseEntityId, int slotIndex,
                                                              out DsCorpseLootResult? denial)
    {
        denial = null;
        if (!_playersBySession.TryGetValue(sessionId, out var player) || player == null || player.IsDead)
        {
            denial = new DsCorpseLootResult { SessionId = sessionId, Status = (byte)CorpseLootTakeStatus.UnknownCorpse };
            return CorpseLootTakeStatus.UnknownCorpse;
        }
        float distance = float.MaxValue;
        if (_monstersById.TryGetValue(corpseEntityId, out var monster) && monster != null)
        {
            var pos = _simWorld.GetEntity(player.EntityId);
            var px = pos?.X ?? player.SpawnX;
            var py = pos?.Y ?? player.SpawnY;
            var dx = monster.Ai.X - px;
            var dy = monster.Ai.Y - py;
            distance = MathF.Sqrt(dx * dx + dy * dy);
        }
        var request = new DsCorpseLootRequest
        {
            SessionId = sessionId,
            CorpseEntityId = corpseEntityId,
            SlotIndex = slotIndex,
            DistanceMeters = distance,
            RequestId = ++_nextCorpseLootRequestId,
        };
        if (HandleCorpseLootRequest(request, out var forward, out var gateDenial))
        {
            if (forward != null) _pendingCorpseLootForwards.Add(forward);
            return CorpseLootTakeStatus.Taken;   // 已受理（等权威回执）
        }
        denial = gateDenial;
        if (denial != null)
        {
            denial.SessionId = sessionId;
            // 当场拒也直接下发：玩家要立刻看到"太远/已过期"，而不是等一个不会来的回执。
            _events.Add((new[] { sessionId }, new Horizon.Game.Message.Sync.CorpseLootTakeResultPacket
            {
                Kind = Horizon.Game.Message.Sync.SyncPacketKind.CorpseLootTakeResult,
                CorpseEntityId = corpseEntityId,
                SlotIndex = slotIndex,
                Status = denial.Status,
            }));
            CorpseLootResultPacketsQueued++;
        }
        return (CorpseLootTakeStatus)(denial?.Status ?? (byte)CorpseLootTakeStatus.UnknownCorpse);
    }

    private readonly List<DsCorpseLootForward> _pendingCorpseLootForwards = new();

    /// <summary>取走自上次调用以来待发送的转发帧。</summary>
    public List<DsCorpseLootForward> DrainCorpseLootForwards()
    {
        var copy = new List<DsCorpseLootForward>(_pendingCorpseLootForwards);
        _pendingCorpseLootForwards.Clear();
        return copy;
    }

    /// <summary>等待权威侧回执的拾取请求（RequestId → 发起人/尸体/模板/槽）。</summary>
    private readonly Dictionary<long, PendingCorpseLoot> _pendingCorpseLoot = new();

    private struct PendingCorpseLoot
    {
        public ulong CharacterId;
        public ulong CorpseEntityId;
        public int TemplateId;
        public int SlotIndex;
        public long RequestedTick;
    }

    /// <summary>
    /// 上行协议路径的第一段：本地两道门（玩家活着 + 窗口/距离）过了才转发给权威侧；
    /// 没过就当场给一条同状态的回执（让客户端拿到"太远"而不是干等）。
    /// </summary>
    /// <returns>true = 请求被受理（<paramref name="forward"/> 非空）；false = 已拒（<paramref name="denial"/> 非空）。</returns>
    public bool HandleCorpseLootRequest(in DsCorpseLootRequest request, out DsCorpseLootForward? forward, out DsCorpseLootResult? denial)
    {
        forward = null;
        denial = null;
        if (!_playersBySession.TryGetValue(request.SessionId, out var player) || player == null || player.IsDead)
        {
            denial = new DsCorpseLootResult { RequestId = request.RequestId, Status = (byte)CorpseLootTakeStatus.UnknownCorpse };
            return false;
        }
        var monster = _monstersById.TryGetValue(request.CorpseEntityId, out var m) ? m : null;

        // 资格先于窗口/距离：对没有资格的人回答"太远了"，等于告诉他走近一点就能拿。
        if (monster != null && !MonsterCorpseCore.IsAllowedLooter(monster.CorpseLootContributors, player.CharacterId))
        {
            CorpseLootRejectedNotYours++;
            denial = new DsCorpseLootResult
            {
                RequestId = request.RequestId,
                Status = (byte)CorpseLootTakeStatus.NotYours,
            };
            return false;
        }

        if (!CanLootCorpse(request.CorpseEntityId, request.DistanceMeters, out var gate))
        {
            denial = new DsCorpseLootResult
            {
                RequestId = request.RequestId,
                Status = (byte)(gate == MonsterCorpseCore.PickupDenial.TooFar
                    ? CorpseLootTakeStatus.TooFar
                    : CorpseLootTakeStatus.Expired),
            };
            return false;
        }
        _pendingCorpseLoot[request.RequestId] = new PendingCorpseLoot
        {
            CharacterId = player.CharacterId,
            CorpseEntityId = request.CorpseEntityId,
            TemplateId = monster != null ? monster.TemplateId : 0,
            SlotIndex = request.SlotIndex,
            RequestedTick = _tick,
        };
        forward = new DsCorpseLootForward
        {
            SessionId = request.SessionId,
            CorpseEntityId = request.CorpseEntityId,
            SlotIndex = request.SlotIndex,
            RequestId = request.RequestId,
            ServerTick = _tick,
            TakerCharacterId = player.CharacterId,
        };
        return true;
    }

    /// <summary>因"不是这次击杀的参与者"被本地门拒掉的次数（回执念出来，免得与"太远/过期"混成一堆）。</summary>
    public int CorpseLootRejectedNotYours { get; private set; }

    /// <summary>因"没持有该任务"被拒的条件槽次数（AC 的 `needs_quest`；槽不消耗，队友仍可来取）。</summary>
    public int CorpseLootRejectedCondition { get; private set; }

    /// <summary>
    /// 上行协议路径的第二段：权威侧回执。**只有 Taken 才发入包事件**，且同一条回执只认一次
    /// （回执重复到达时计数，不重复发东西）。
    /// </summary>
    public bool HandleCorpseLootResult(in DsCorpseLootResult result)
    {
        if (!_pendingCorpseLoot.TryGetValue(result.RequestId, out var pending))
        {
            CorpseLootResultUnmatched++;
            return false;
        }
        _pendingCorpseLoot.Remove(result.RequestId);
        var status = (CorpseLootTakeStatus)result.Status;
        if (status != CorpseLootTakeStatus.Taken)
        {
            // 权威侧拒绝也必须回一条结果包：**同槽两人同时点**时，第二个人拿到的 AlreadyTaken
            // 只有权威侧知道（DS 本地门放行了它），不转给客户端就表现成"点了没反应"——
            // 玩家会以为是自己没点中，继续反复点。入包事件仍然不发（没取到东西）。
            CorpseLootResultsRejected++;
            // 条件件被拒（needs_quest）单独计数：它与"太远/过期/已取/不是你的"是不同的事，
            // 混在"权威拒"里会让"这件任务件为什么没人拿到"查不出来。
            if (status == CorpseLootTakeStatus.ConditionNotMet) CorpseLootRejectedCondition++;
            var rejectedIndex = result.SlotIndex >= 0 ? result.SlotIndex : pending.SlotIndex;
            QueueCorpseLootResultPacket(pending.CharacterId, pending.CorpseEntityId, rejectedIndex, result,
                                        new CorpseLootSlotDto());
            return false;
        }
        var slot = new CorpseLootSlotDto
        {
            ItemId = result.ItemId,
            Count = result.Count,
            ConditionKind = result.ConditionKind,
            QuestId = result.QuestId,
            Money = result.Money,
        };
        EmitCorpseLootGrant(pending.CharacterId, pending.CorpseEntityId, pending.TemplateId, slot);
        // 结果直接按会话下行：回执走网关再绕一圈只会多一处可能丢的地方。
        // 请求可能是「取下一件」（槽号 -1）：回执里的槽号才是给客户端看的那个。
        var takenIndex = result.SlotIndex >= 0 ? result.SlotIndex : pending.SlotIndex;
        QueueCorpseLootResultPacket(pending.CharacterId, pending.CorpseEntityId, takenIndex, result, slot);
        return true;
    }

    /// <summary>把一条拾取结果排进该会话的下行事件（客户端据此弹"获得 X"或"取不了"）。</summary>
    private void QueueCorpseLootResultPacket(ulong characterId, ulong corpseEntityId, int slotIndex,
                                             in DsCorpseLootResult result, in CorpseLootSlotDto slot)
    {
        var sessionId = characterId != 0UL ? unchecked((long)characterId) : 0L;
        if (sessionId == 0L) return;
        _events.Add((new[] { sessionId }, new Horizon.Game.Message.Sync.CorpseLootTakeResultPacket
        {
            Kind = Horizon.Game.Message.Sync.SyncPacketKind.CorpseLootTakeResult,
            CorpseEntityId = corpseEntityId,
            SlotIndex = slotIndex,
            Status = result.Status,
            ItemId = slot.ItemId,
            Count = slot.Count,
            Money = slot.Money,
        }));
        CorpseLootResultPacketsQueued++;
    }

    /// <summary>已排给客户端的拾取结果包数（"取到了但客户端没反应"时先看这一格）。</summary>
    public int CorpseLootResultPacketsQueued { get; private set; }

    /// <summary>回执找不到对应请求的次数（超时清理后迟到 / 网关重启）。</summary>
    public int CorpseLootResultUnmatched { get; private set; }

    /// <summary>权威侧裁定"没取到"（已取走/过期/太远/越界/没资格）并已把结果转给客户端的次数。</summary>
    public int CorpseLootResultsRejected { get; private set; }

    /// <summary>超时未收到回执而被丢弃的请求数（避免字典无限增长）。</summary>
    public int CorpseLootPendingExpired { get; private set; }

    /// <summary>清理超时的待回执请求（约 10 秒；由 tick 调用）。</summary>
    private void ExpirePendingCorpseLoot()
    {
        if (_pendingCorpseLoot.Count == 0) return;
        var limit = _tick - (long)(10f * _options.TicksPerSecond);
        if (limit <= 0) return;
        List<long>? stale = null;
        foreach (var kv in _pendingCorpseLoot)
            if (kv.Value.RequestedTick < limit) (stale ??= new List<long>()).Add(kv.Key);
        if (stale == null) return;
        for (var i = 0; i < stale.Count; i++) _pendingCorpseLoot.Remove(stale[i]);
        CorpseLootPendingExpired += stale.Count;
    }

    /// <summary>
    /// 死亡那一拍的两个期限（尸体停留 / 可拾取窗口）与拾取距离。
    /// <para>只此一份式子：拾取窗口不得活得比尸体更久，而"是否把掉落扣在尸体上"的判断也要知道这两个数——
    /// 两处各写一遍的后果是"判断说有窗口、而真正落库时取了另一个数"。</para>
    /// </summary>
    private (long CorpseExpiry, long LootExpiry, float PickupDistance) ComputeCorpseExpiries(
        int templateId, long serverTick, long respawnTick, bool allowCorpseLinger)
    {
        var config = allowCorpseLinger && MonsterContent.TryGetConfig(templateId, out var found) ? found : null;
        var corpseSeconds = config != null ? config.CorpseSeconds : 0f;
        var corpseExpiry = MonsterCorpseCore.PlanExpiryTick(serverTick, _options.TicksPerSecond, corpseSeconds, respawnTick);
        var lootSeconds = config != null ? config.LootWindowSeconds : 0f;
        var lootGranted = MonsterCorpseCore.PlanExpiryTick(serverTick, _options.TicksPerSecond, lootSeconds, respawnTick);
        var lootExpiry = Math.Min(lootGranted, corpseExpiry);
        var distance = config != null && config.PickupDistanceMeters > 0f
            ? config.PickupDistanceMeters
            : MonsterCorpseCore.DefaultPickupDistanceMeters;
        return (corpseExpiry, lootExpiry, distance);
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

        // 1b) 临终台词：文本里落在 `MonsterContent.DeathSayGroup` 那一组的句子，按 tick 盐挑一句发给看见它的人。
        //     与进战喊话共用一条事件通路（只发号，客户端按 (modelKey, sayId) 查文案）。
        //     没有那一组时**什么都不发**——大多数怪死时不出声，"没配"与"配了没发"必须分得开。
        EmitMonsterDeathSay(monster);

        // 2) 掉落掷骰（确定性）。
        //    优先用**这只怪自己的绑定**（编辑器里配的"一怪 N 表 + 逐件独立概率 + 数量区间 + 金币"）；
        //    一只都没配才退回全局掉落提供者（开发期那张"模板派生物品"的假表）。
        //    这一层以前只有后者——于是编辑器里把 5% 改成 50%，游戏里的数字一动不动。
        var bindings = MonsterContent.LootBindings(monster.TemplateId);
        if (bindings.Count == 0)
        {
            // 敌对 NPC 的掉落由 **NPC 内容**自持（同一套绑定结构，按 npc_id 索引）；
            // 怪物内容里查不到就退到这边，避免"能打能死却不掉东西"。
            bindings = Horizon.Game.Sim.Npcs.NpcContent.LootBindings(monster.TemplateId);
        }
        var useContentLoot = bindings != null && bindings.Count > 0;
        MonsterLootRollResult? contentRoll = null;
        IReadOnlyList<DsLootDrop> drops;
        if (useContentLoot)
        {
            contentRoll = MonsterLootCore.Roll(bindings!, killerCharacterId, monster.TemplateId, _tick);
            var list = new List<DsLootDrop>(contentRoll.Drops.Count);
            for (var i = 0; i < contentRoll.Drops.Count; i++)
            {
                list.Add(new DsLootDrop { ItemId = contentRoll.Drops[i].ItemId, Count = contentRoll.Drops[i].Count });
            }
            drops = list;
            MonsterLootBindingsRolled += contentRoll.BindingsRolled;
            MonsterLootItemsRolled += contentRoll.Drops.Count;
            MonsterLootConditionalRolled += contentRoll.ConditionalDrops.Count;
            MonsterLootDeferredByCondition += contentRoll.EntriesDeferredUnknownCondition;
            if (contentRoll.Money > 0) MonsterLootMoneyRolled += contentRoll.Money;
        }
        else
        {
            drops = _lootProvider.Roll(monster.TemplateId, killerCharacterId, _tick);
        }

        // 2026-10-29 拍板（D23）：接入了权威存储、且这只怪会留下可拾取的尸体时，
        // 掉落改成**尸体拾取**：击杀只掷骰、不再直接入包，取走才入包（与 AC 同形）。
        // 存储没注入（客户端拾取入口就绪前的生产现状）时照旧即时发放——否则掉落会抣在一个没人能取的地方。
        var respawnAtForLoot = MonsterAiCore.ComputeRespawnTick(_tick, _options.TicksPerSecond, cfg.RespawnDelaySeconds);
        var plannedCorpse = ComputeCorpseExpiries(monster.TemplateId, _tick, respawnAtForLoot, allowCorpseLinger: true);
        var deferLootToCorpse = (_options.DeferCorpseLootToAuthority || _corpseLootStore != null)
                                 && contentRoll != null && plannedCorpse.LootExpiry > _tick;

        // 2.5) 击杀经验：按贡献者逐个算（灰名断供、随玩家等级反比例衰减都发生在内核里）。
        // 每人一条独立事件；业务层只对**击杀者**推进任务与好感，避免"谁摸过谁算击杀任务"。
        EmitKillXpEvents(monster);

        // 3) 跨层事件：击杀 + 逐件掉落（业务层据此推进任务/入包）。
        //
        // 击杀归属转移（模板的 kill_credit_npc_id，对应 WoW 的 KillCredit1/2）：
        // **只改击杀事件的身份**——任务"击杀 N 只 X"与五行域好感都读这一个字段；
        // 掉落/金币属于尸体本身，仍记在被杀模板上（转给别人会让尸体掏出别人的货）。
        // 旧实现里这一列全仓没有读者：编辑器能填、任务与好感却永远记在被杀者头上。
        var creditTemplateId = monster.TemplateId;
        if (Horizon.Game.Sim.Npcs.NpcContent.TryGetConfig(monster.TemplateId, out var creditConfig)
            && creditConfig != null
            && creditConfig.KillCreditNpcId > 0
            && creditConfig.KillCreditNpcId != monster.TemplateId)
        {
            creditTemplateId = creditConfig.KillCreditNpcId;
        }

        _gameplayEvents.Add(new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.Kill,
            ShardId = _options.ShardId,
            ServerTick = _tick,
            ActorCharacterId = killerCharacterId,
            MonsterEntityId = monster.EntityId,
            MonsterTemplateId = creditTemplateId,
        });

        if (!deferLootToCorpse)
        {
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

        // 3.2) 金币：掷出来了就要单独发一条事件，否则它永远只是 DS 里的一个计数
        //      （`Loot` 事件那侧的消费端第一句要求 ItemId/ItemCount 为正，挂在上面会被安静吞掉）。
        //      给**击杀者**——与今天的物品同一口径（掉落事件本来就只发给击杀者）。
        //      AC 那边既不是"击杀者独得"也不是"每人各掷一份"，而是**尸体上只有一份掉落**、
        //      由右键拾取者按 round-robin / need-greed 分配（`Unit.cpp:18050-18059`，
        //      钱在 `:18059` 一次性掷自 `creature_template.mingold/maxgold`）。
        //      要对齐那个形状需要的是"尸体上那一份 + 拾取窗口"这一期，物品与钱要一起改，
        //      不是把发钱对象换成参与者列表（详见设计文档 §11.2-2/-18）。
        if (contentRoll != null && contentRoll.Money > 0)
        {
            _gameplayEvents.Add(new DsGameplayEvent
            {
                Kind = DsGameplayEventKind.KillMoney,
                ShardId = _options.ShardId,
                ServerTick = _tick,
                ActorCharacterId = killerCharacterId,
                MonsterEntityId = monster.EntityId,
                MonsterTemplateId = monster.TemplateId,
                Money = contentRoll.Money,
            });
        }

        // 3.5) 条件掉落（"要持有某任务才给"）：DS 只负责**掷**（确定性归模拟层），
        //      判任务状态归业务层（只有 Grain 知道玩家接了什么）——所以发的是 LootConditional。
        if (contentRoll != null)
        {
            for (var i = 0; i < contentRoll.ConditionalDrops.Count; i++)
            {
                var cd = contentRoll.ConditionalDrops[i];
                _gameplayEvents.Add(new DsGameplayEvent
                {
                    Kind = DsGameplayEventKind.LootConditional,
                    ShardId = _options.ShardId,
                    ServerTick = _tick,
                    ActorCharacterId = killerCharacterId,
                    MonsterEntityId = monster.EntityId,
                    MonsterTemplateId = monster.TemplateId,
                    ItemId = cd.ItemId,
                    ItemCount = cd.Count,
                    QuestId = cd.QuestId,
                });
            }
        }

        }

        // 4) 摘除实体 + 排定重生（与 MarkMonsterDead 走同一条确定性路径）。
        monster.Runtime?.ResetForRespawn();
        MarkMonsterDead(monster.EntityId, _tick);
        if (deferLootToCorpse) RegisterCorpseLoot(monster, contentRoll!, killerCharacterId);
    }

    /// <summary>发贡献者经验事件（参与上限与优先级排序都在 <see cref="MonsterXpCore"/> 里）。</summary>
    private void EmitKillXpEvents(DsMonster monster)
    {
        if (monster.Contributors == null) return;
        var collected = new List<MonsterXpParticipant>(MonsterContributors.Capacity);
        monster.Contributors.CopyTo(collected);
        if (collected.Count == 0) return;
        // 经验池只分给**还活着、还站在奖励距离内、还在线**的人（AzerothCore
        // `Player::IsAtGroupRewardDistance` + `_RewardXP` 里那句 `player->IsAlive()`）。
        // 这一步必须在进分母之前做：把死掉/跑远的人算进等级和，会稀释在场活着的人那一份，
        // 而"队友躺尸我经验变少"是玩家立刻能察觉的错。三类跳过各自计数。
        var mobPos = _simWorld.GetEntity(monster.EntityId);
        var mobX = mobPos?.X ?? 0f;
        var mobY = mobPos?.Y ?? 0f;
        var limit = MonsterXpCore.GroupRewardDistance;
        var limitSq = limit * limit;
        var present = new List<MonsterXpParticipant>(collected.Count);
        for (var i = 0; i < collected.Count; i++)
        {
            var p = collected[i];
            if (!_sessionByEntityId.TryGetValue(p.CharacterId, out var sid)
                || !_playersBySession.TryGetValue(sid, out var contributor) || contributor == null)
            {
                KillXpSkippedOffline++;
                continue;
            }
            if (contributor.Hp <= 0)
            {
                KillXpSkippedDead++;
                continue;
            }
            var pos = _simWorld.GetEntity(contributor.EntityId);
            var dx = (pos?.X ?? contributor.SpawnX) - mobX;
            var dy = (pos?.Y ?? contributor.SpawnY) - mobY;
            if (dx * dx + dy * dy > limitSq)
            {
                KillXpSkippedOutOfRange++;
                continue;
            }
            present.Add(p);
        }
        if (present.Count == 0) return;
        // 内核的排序/截断按 Span 收（确定性要求就地、无堆分配），这里做一次 List→数组的落地。
        var participants = present.ToArray();
        MonsterXpCore.SortParticipantsByPriority(participants);

        var monsterLevel = 1;
        var rank = MonsterRank.Normal;
        var xpConfig = MonsterXpConfig.Default;
        if (MonsterContent.TryGetConfig(monster.TemplateId, out var config) && config != null)
        {
            // 等级读**这只怪生成时定下的生效等级**，不再读模板基准：
            // 给刷怪点填 25 级却按 12 级发经验，是"经验条对不上怪"的那类投诉。
            monsterLevel = MonsterLevelOf(monster);
            rank = config.Rank;
            xpConfig = config.Xp;
        }
        else
        {
            // 未装配内容包（开发期/库里还没东西）时退回代码目录：等级与"是否世界 BOSS"都有确定值，
            // 经验不会算成 0，也不会因为漏配就被放大。
            var template = MonsterTemplateCatalog.Get(monster.TemplateId);
            monsterLevel = template.Level > 0 ? template.Level : 1;
            rank = template.IsWorldBoss ? MonsterRank.WorldBoss : MonsterRank.Normal;
        }

        var xpBuffer = new long[MonsterXpCore.MaxParticipants];
        // 怪物击杀是**共享经验池**的那条通路（NPC 击杀那边每人各算全额，所以它继续用
        // ComputeForParticipants）：整组拿 pool × 人数加成，再按等级比例分。
        var granted = MonsterXpCore.ComputeGroupShares(xpConfig, monsterLevel, rank, participants, xpBuffer, out var groupReceipt);
        KillXpLastGroupRate = groupReceipt.Rate;
        KillXpLastPoolLevel = groupReceipt.PoolLevel;
        if (groupReceipt.Halved) KillXpGroupHalvedCount++;
        for (var i = 0; i < granted; i++)
        {
            // 灰名怪算出 0：不发事件。发一条"0 经验"只会让业务层多一个要不要加 0 的分支。
            if (xpBuffer[i] <= 0) continue;
            _gameplayEvents.Add(new DsGameplayEvent
            {
                Kind = DsGameplayEventKind.KillXp,
                ShardId = _options.ShardId,
                ServerTick = _tick,
                ActorCharacterId = participants[i].CharacterId,
                MonsterEntityId = monster.EntityId,
                MonsterTemplateId = monster.TemplateId,
                ItemCount = (int)Math.Min(int.MaxValue, xpBuffer[i]),
            });
        }
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
                // 等级先定一次（模板区间掷，见 ResolveEffectiveLevel），随后**初始血、战斗层上限、AI 投影**
                // 都按这一个等级算：以前血量拿点位那份（装配期按基准等级算的），
                // 一旦区间真的掷出不同等级，就会出现"等级 7 的怪拿着等级 5 的血"两套账。
                var spawnLevel = ResolveEffectiveLevel(cfg.SpawnPointId, cfg.MonsterTemplateId, spawn.EntityId, _tick);
                MonsterContent.TryGetConfig(cfg.MonsterTemplateId, out var spawnContentCfg);
                var spawnStatsCfg = spawnContentCfg != null
                    ? BuildAiConfigFromContent(spawnContentCfg, aiCfg, spawnLevel)
                    : aiCfg;
                // 尸体窗口与重生共用实体 ID：正常情况下收尸排在刷怪之前、窗口也已被
                // MonsterCorpseCore 压到重生前一 tick，这里不会再撞上尸体；
                // 但清单里的残留项必须跟着覆盖动作一起清掉，否则它会一直谎报"地上还躺着一具"。
                _corpses.Remove(spawn.EntityId);
                // 这个槽位**曾经死过** ⇒ 这次是复活而不是首次生成。AzerothCore 的 5 秒"刚出现不主动咬人"
                // 宽限只挂在复活上（`if (m_respawnedTime && ...)`：从没死过的怪那个字段是 0，判据根本不生效），
                // 所以这里必须把两种生成区分开——否则服务器一开全图的怪都先聋 5 秒。
                var spawnIsRespawn = spawn.SlotIndex >= 0 && spawn.SlotIndex < slots.Length
                                     && slots[spawn.SlotIndex].RespawnAtTick > 0;
                _monstersById[spawn.EntityId] = new DsMonster
                {
                    EntityId = spawn.EntityId,
                    SpawnPointId = cfg.SpawnPointId,
                    SlotIndex = spawn.SlotIndex,
                    SpawnIsRespawn = spawnIsRespawn,
                    TemplateId = cfg.MonsterTemplateId,
                    EffectiveLevel = spawnLevel,
                    PatrolRouteId = ResolvePatrolRouteId(cfg.SpawnPointId, cfg.MonsterTemplateId),
                    ScriptedKind = _scriptPointKind.TryGetValue(cfg.SpawnPointId, out var scriptedKind) ? scriptedKind : 0,
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
                        Hp = spawnStatsCfg.MaxHp,
                        AttackCooldownRemaining = 0f,
                        StateElapsedSeconds = 0f,
                        // 巡逻方向由槽位奇偶决定（纯确定性，无随机源）。
                        PatrolDirection = (spawn.SlotIndex % 2 == 0) ? 1f : -1f,
                        // v1（能力位全关，也就是当前全部种子怪）的复活宽限装在**自身状态**上：
                        // 这一路的怪根本不进 v2，所以 Runtime 那份宽限永远轮不到它们。
                        // 首次生成不装（AC 那条判据本身就写着 `if (m_respawnedTime && ...)`），
                        // 装了会让服务器一开全图的怪先聋 5 秒。
                        NoInitiateUntilTick = spawnIsRespawn
                            ? MonsterAiGate.RespawnGraceUntil(_tick, _options.TicksPerSecond)
                            : 0L,
                    },
                };

                SpawnDirectorCore.MarkSlotAlive(slots, spawn.SlotIndex, spawn.EntityId);
                spawned.Add(spawn.EntityId);

                // 实体身份随生成上报：网关注册表从此知道"这个 entityId 是什么、在哪个坐标"。
                QueueEntityDescriptor(spawn.EntityId, cfg.SpawnPointId, cfg.MonsterTemplateId,
                    _monstersById[spawn.EntityId].ScriptedKind, spawn.X, spawn.Y, spawn.Z, removed: false);
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
                // 等级差改视野是 AC 的口径（<c>MonsterAiGate.EffectiveAggroRadius</c>），而它是<b>按目标</b>算的，
                // 所以候选身上必须带等级；没带（0）时那条判据整条不调，等于今天的旧行为。
                // Level = p.Level, 这行是被反证脚本改坏后没复原留下的形状（不是"可选的一格"）：
                // 注释掉它，等级差就只剩怪那一侧有值、差值恒为 0，判据整条静默不生效。
                Level = p.Level,
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
            // 尸体不进 AI：内核的死亡分支在重生 tick 会把状态翻回 Idle，而 HP 是由重生时的
            // 新实体带回来的——要是收尸因为任何时序意外晚了一 tick，这里就会养出一只
            // "站着不动、又打不死、头顶还挂着名牌"的鬼怪。判据放在遍历入口，不依赖两头的顺序。
            if (monster.CorpseExpireAtTick != 0) continue;
            var pointCfg = FindConfigForSpawnPoint(monster.SpawnPointId);

            // 等级档与行为表那一份只有内容包这一个来源，查一次给两条通路用：
            // 这条循环每 tick 每只怪都跑，同一次查找别做两遍。
            MonsterContent.TryGetConfig(monster.TemplateId, out var contentCfg);
            // **投影只在这里做一次**，v1 与 v2 拿同一份。
            // 以前这里是三行零散覆盖（Rank／免疫秒／移动方式），其余行为表数值留在点位那一份，
            // 于是同一张行为表换一档 AI 就是两套数（§11.2 第 14 条）。
            var cfg = contentCfg != null
                ? BuildAiConfigFromContent(contentCfg, pointCfg, MonsterLevelOf(monster))
                : pointCfg;

            // AI v2：只在**装配了内容包且能力位开着**时接管，否则原样走 v1。
            // 这条分界让"接入数据表"与"改变手感"不会在同一次提交里一起发生
            // （当前 11 只种子怪 ai_profile=0，全部走 v1，既有金轨迹与断言逐字不变）。
            // 到点回执走 decision 带回来的那一格（**不是**比游标下标）：v1 的游标在 decision.Patrol，
            // v2 的游标在 MonsterRuntimeState.Patrol，只比下标会系统性漏掉整条 v2 通路。
            var v2 = TryDecideMonsterV2(monster, targets, dt, cfg, monsterIds, contentCfg);
            if (v2.HasValue && v2.Value.SeededByAssist)
            {
                MonsterAssistSeeds++;
                // 被求援叫起来的那一拍照例喊一句（组 `AssistSayGroup`）。没配那一组就什么都不发。
                EmitMonsterAssistSay(monster);
                // AC 在那一刻把首领的 leash 计时器指针交给盟友（`Creature.cpp:231`）；
                // 本作把它表达成"并进同一组"：之后打其中任何一只都会刷新整组的宽限窗。
                LinkLeashTimerGroup(monster, v2.Value.AssistCallerId);
            }
            // 巡逻线现在两条通路都读得到（以前只有 v2 那一段解析过它 ⇒ 默认档的怪画了线也不走）。
            // 目的地按"刷怪点覆盖 → 行为表"，与 v2 那边同一个主人（ResolvePatrolRoute 那一条式子）。
            var patrolRoute = ResolveRouteObject(ResolvePatrolRoute(monster, contentCfg));
            var decision = v2.HasValue ? v2.Value.Decision
                       : MonsterAiCore.Decide(monster.Ai, targets, dt, cfg, _tick, _options.TicksPerSecond, patrolRoute);
            // 追不到路脱战（AC EVADE_REASON_NO_PATH）：宿主必须自己念得出这一类脱战，
            // 否则"怪贴着墙站了五秒然后回家"在日志里与"它自己脱战"同形。
            if (decision.EvadedByNoPath) MonsterNoPathEvades++;

            var self = monster.Ai;

            // NPC 内容配置只查一次：残血逃跑与朝向两条通路都用它（这条循环每 tick 每只怪都跑）。
            // **不按 ScriptedKind 筛**：敌对 NPC 走的是怪物管线（kind=Monster），按种类筛会把唯一会逃的漏掉；
            // 撞号风险由"立场必须是敌对"挡掉（怪物模板号与旧 NPC 号 1/2/3 重叠时查到的是友善配置）。
            Horizon.Game.Sim.Npcs.NpcContent.TryGetConfig(monster.TemplateId, out var npcCfg);

            // 残血逃跑（FleesAtLowHealth 位）：远离当前威胁跑，期间不出手。
            // 目标找不到/速度为 0 时按原样打——没有方向可逃时，"逃"只会变成站着挨打。
            var npcFleeing = false;
            if (npcCfg != null
                && npcCfg.Stance == Horizon.Game.Sim.Npcs.NpcStance.Hostile
                && MonsterAiGate.InCombat(decision.NextState)
                && Horizon.Game.Sim.Npcs.NpcFleeCore.ShouldFlee(
                    npcCfg.Flags.Has(Horizon.Game.Sim.Npcs.NpcAiBits.FleesAtLowHealth), self.Hp, cfg.MaxHp))
            {
                // 逃跑用**奔跑速度**（WoW 的生物脱战时跑得比走得快）；没配才退回生成时的移动速度。
                // `SpeedWalk` 已在生成时写进 cfg.MoveSpeed，这里再读 SpeedRun 才让编辑器里那一格有读者。
                var fleeSpeed = npcCfg.SpeedRun > 0f
                    ? npcCfg.SpeedRun
                    : (cfg.MoveSpeed > 0f ? cfg.MoveSpeed : cfg.WalkSpeed);
                var threatId = decision.ThreatTargetId != 0UL ? decision.ThreatTargetId : self.ThreatTargetId;
                if (fleeSpeed > 0f && TryGetTargetPosition(targets, threatId, out var threatX, out var threatY))
                {
                    var awayX = self.X - threatX;
                    var awayY = self.Y - threatY;
                    var len = MathF.Sqrt(awayX * awayX + awayY * awayY);
                    if (len < 0.01f)
                    {
                        // 与目标重合：固定朝 +X（不拿随机数破平局，同输入必须同结果）。
                        awayX = 1f;
                        awayY = 0f;
                        len = 1f;
                    }

                    var step = fleeSpeed * dt;
                    decision.DeltaX = awayX / len * step;
                    decision.DeltaY = awayY / len * step;
                    decision.NextYaw = MathF.Atan2(awayY, awayX);
                    decision.Moved = true;
                    decision.WantsAttack = false;
                    decision.AttackTargetId = 0UL;
                    npcFleeing = true;
                    if (!monster.NpcFleeing) NpcFleeStartedCount++;
                }
            }
            monster.NpcFleeing = npcFleeing;

            // ── NPC 施法（Casts 位 + 技能挂载）：与 v2 共用同一个施法内核与同一条伤害结算口 ──
            // v2 是"内容包驱动"，NPC 走不到那里；这里只在 **v1 且勾了 Casts 且有挂载** 时补一段推进。
            // 没有挂载（024 未执行/作者没配）时整段不执行，行为与之前逐字相同。
            if (v2 == null
                && !npcFleeing
                && npcCfg != null
                && npcCfg.Flags.Has(Horizon.Game.Sim.Npcs.NpcAiBits.Casts))
            {
                var npcMounts = Horizon.Game.Sim.Npcs.NpcContent.SkillMounts(monster.TemplateId);
                if (npcMounts.Length > 0)
                {
                    monster.Runtime ??= new MonsterRuntimeState();
                    if (monster.Runtime.ReadyAtTick.Length != npcMounts.Length)
                        monster.Runtime.ReadyAtTick = new long[npcMounts.Length];

                    var castThreat = decision.ThreatTargetId != 0UL ? decision.ThreatTargetId : self.ThreatTargetId;
                    float castX = 0f, castY = 0f, castZ = 0f;
                    var hasTargetPos = false;
                    for (var ti = 0; ti < targets.Length; ti++)
                    {
                        if (targets[ti].EntityId != castThreat) continue;
                        castX = targets[ti].X;
                        castY = targets[ti].Y;
                        castZ = targets[ti].Z;
                        hasTargetPos = true;
                        break;
                    }

                    var distToCastTarget = hasTargetPos
                        ? MathF.Sqrt((castX - self.X) * (castX - self.X) + (castY - self.Y) * (castY - self.Y))
                        : float.MaxValue;
                    var hpPercent = cfg.MaxHp > 0
                        ? (int)Math.Clamp(self.Hp * 100L / Math.Max(1, cfg.MaxHp), 0L, 100L)
                        : 100;
                    var castInCombat = MonsterAiGate.InCombat(decision.NextState);

                    var castDecision = MonsterCastingCore.Advance(
                        ref monster.Runtime.Casting, npcMounts, monster.Runtime.ReadyAtTick,
                        monster.EntityId, castThreat, castX, castY, castZ, distToCastTarget, hpPercent,
                        castInCombat, monster.InterruptRequested, _tick, _options.TicksPerSecond, dt);
                    monster.InterruptRequested = false;

                    if (castDecision.ShouldHalt)
                    {
                        decision.DeltaX = 0f;
                        decision.DeltaY = 0f;
                        decision.Moved = false;
                    }

                    if (castDecision.FinishesCast) ResolveMonsterCast(monster, castDecision);
                }
            }

            self.X += decision.DeltaX;
            self.Y += decision.DeltaY;
            self.Yaw = decision.NextYaw;
            // 巡逻游标无条件写回：内核每条出口（含早退与死亡）都带"入口那一份"，
            // 所以这里不需要判"这一拍有没有走线"——那种条件写回会漏掉某拍、把游标冻在旧值上。
            // 到点回执读内核那一位旗标（两条通路都填），见 MonsterPatrolPointsReached 的注释。
            self.Patrol = decision.Patrol;
            if (decision.PatrolPointSwitched) MonsterPatrolPointsReached++;
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
            // 内核只在"Dead 翻回 Idle"那一 tick 改这一格，其余路径原样带回，所以这里无条件写回。
            self.NoInitiateUntilTick = decision.NoInitiateUntilTick;
            self.EvadeImmuneUntilTick = decision.EvadeImmuneUntilTick;
            // 够不到的计时起点（v1 那一侧；v2 走 state.NoPathStartTick，不读这一格但会原样带回 0）
            self.NoPathStartTick = decision.NextNoPathStartTick;
            // 回位刷血：内核只在"到家那一 tick"报 HealedToFull，其余路径都是 false，
            // 所以这里就是唯一的写点（v2 那边由 result.HealedToFull 走同一件事）。
            if (decision.HealedToFull && decision.HpAfterHeal > 0) self.Hp = decision.HpAfterHeal;
            monster.Ai = self;

            // 进战喊话：v1 与 v2 都从这一行走（两条通路的战斗态在这里已经落定），
            // 所以这一格不再需要"只有开了能力位的怪才喊"那种作用域裂缝。
            SyncMonsterSay(monster);

            // NPC 的"转身面向玩家"（FacePlayer 位）：只改朝向，不动位置与状态。
            // 只处理 NPC 分支：敌对型的朝向由 AI 决策负责，这里再盖一次会与追击方向打架。
            // 逃跑途中不转：一边盯着玩家一边往反方向跑，看起来像倒着走。
            if (!npcFleeing
                && monster.ScriptedKind == (int)Horizon.Game.Message.Network.ScriptedKind.Npc
                && npcCfg != null
                && npcCfg.Flags.Has(Horizon.Game.Sim.Npcs.NpcAiBits.FacePlayer))
            {
                ApplyNpcFacing(monster, npcCfg, targets);
            }

            // AI 决定出手 → 走与玩家攻击同一条权威结算路径（CombatCore）。
            if (decision.WantsAttack && decision.AttackTargetId != 0UL)
            {
                ResolveMonsterAttack(monster, decision.AttackTargetId);
            }

            if (v2.HasValue)
            {
                // 施法条的边沿事件必须先于结算：同 tick 内"条子收掉"排在伤害包之前，
                // 客户端就不会出现"还在读条就已经掉血"的错觉。
                SyncMonsterCastBar(monster, v2.Value.Cast);

                // 施法完成 → 走与普攻同一条 CombatCore 结算（复用 DamagePacket，不新增线上契约）。
                if (v2.Value.Cast.FinishesCast) ResolveMonsterCast(monster, v2.Value.Cast);

                // 回位刷血（WoW 观感：脱战跑回去血条"唰"地满；不清这条，玩家可以无伤磨残血怪）。
                if (v2.Value.HealedToFull && v2.Value.HpAfterHeal > 0)
                {
                    var healed = monster.Ai;
                    healed.Hp = v2.Value.HpAfterHeal;
                    monster.Ai = healed;
                }
            }
        }
    }

    /// <summary>NPC 面向玩家的距离上限（米）：再远就不该"隔着半条街盯着你"。</summary>
    private const float NpcFacePlayerMaxMeters = 20f;

    /// <summary>
    /// 刷点初始朝向：0 固定 <c>Yaw</c>、1 确定性随机、2 面向最近玩家（规格 §9.1 档 1）。
    /// <para>
    /// "随机"不走 <c>Random</c>：同一刷点每次重启都要朝同一个方向，否则截图对账与重放没有基线。
    /// 面向玩家只在生成那一刻算一次——玩家走开之后该朝哪，是 <c>FacePlayer</c> 能力位的事，不在这里。
    /// </para>
    /// </summary>
    private float ResolveNpcSpawnYaw(in Horizon.Game.Sim.Npcs.NpcSpawnSnapshot point)
    {
        switch (point.FacingMode)
        {
            case 1:
                var hash = unchecked((uint)point.SpawnPointId * 2654435761u) ^ unchecked((uint)_options.ShardId);
                return (hash % 3600u) * (MathF.PI / 1800f); // 0..2π，步长 0.1°
            case 2:
                if (TryFindNearestPlayerYaw(point.X, point.Y, out var yaw)) return yaw;
                return point.Yaw;
            default:
                return point.Yaw;
        }
    }

    /// <summary>最近存活玩家相对 (x,y) 的朝向；没有玩家返回 false。同距离按实体 id 升序（可重放）。</summary>
    private bool TryFindNearestPlayerYaw(float x, float y, out float yaw)
    {
        yaw = 0f;
        if (_playersBySession.Count == 0) return false;

        var ids = new List<ulong>(_playersBySession.Count);
        foreach (var kv in _playersBySession)
        {
            if (kv.Value.Hp > 0) ids.Add(kv.Value.EntityId);
        }
        if (ids.Count == 0) return false;
        ids.Sort();

        var found = false;
        var bestSq = float.MaxValue;
        var bestDx = 0f;
        var bestDy = 0f;

        for (var i = 0; i < ids.Count; i++)
        {
            var pos = _simWorld.GetEntity(ids[i]);
            var dx = pos.X - x;
            var dy = pos.Y - y;
            var sq = dx * dx + dy * dy;
            if (found && sq >= bestSq) continue;
            found = true;
            bestSq = sq;
            bestDx = dx;
            bestDy = dy;
        }

        if (!found || bestSq <= 1e-4f) return false;
        yaw = MathF.Atan2(bestDy, bestDx);
        return true;
    }


    /// <summary>NPC 没配哨戒半径时的默认面向距离（米）。</summary>
    private const float NpcFacePlayerDefaultMeters = 8f;

    /// <summary>
    /// 按实体 id 找目标位置（<paramref name="targets"/> 已按 id 升序）。
    /// <para>找不到回 false——逃跑没有方向时不该原地乱走；这不是错误分支，是"目标已经不在场"。</para>
    /// </summary>
    private static bool TryGetTargetPosition(MonsterAiTarget[] targets, ulong targetId, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        if (targetId == 0UL || targets == null) return false;

        for (var i = 0; i < targets.Length; i++)
        {
            if (targets[i].EntityId != targetId) continue;
            x = targets[i].X;
            y = targets[i].Y;
            return true;
        }

        return false;
    }

    /// <summary>
    /// NPC 转向最近的玩家（<see cref="Horizon.Game.Sim.Npcs.NpcAiBits.FacePlayer"/>）。
    /// <para>
    /// 只写 <c>Yaw</c>：位置、状态、威胁一律不碰——"转身看着你"与"进入战斗"是两件事，
    /// 合成一件会让友善 NPC 一靠近就把玩家当敌人。
    /// </para>
    /// <para>同距离按玩家实体 id 升序取第一个：两个玩家并排时的朝向必须可重放。</para>
    /// </summary>
    private static void ApplyNpcFacing(
        DsMonster monster,
        Horizon.Game.Sim.Npcs.NpcRuntimeConfig cfg,
        MonsterAiTarget[] targets)
    {
        if (targets == null || targets.Length == 0) return;

        var radius = cfg.AlertRadiusMeters > 0
            ? MathF.Min(cfg.AlertRadiusMeters, NpcFacePlayerMaxMeters)
            : NpcFacePlayerDefaultMeters;
        var radiusSq = radius * radius;

        var self = monster.Ai;
        var found = false;
        var bestSq = float.MaxValue;
        var bestId = ulong.MaxValue;
        var bestDx = 0f;
        var bestDy = 0f;

        for (var i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            if (!target.IsAlive) continue;

            var dx = target.X - self.X;
            var dy = target.Y - self.Y;
            var sq = dx * dx + dy * dy;
            if (sq > radiusSq) continue;

            // 全序：距离优先，同距离按实体 id 升序（浮点相等才用 id 破平局）。
            if (found && (sq > bestSq || (sq == bestSq && target.EntityId > bestId))) continue;

            found = true;
            bestSq = sq;
            bestId = target.EntityId;
            bestDx = dx;
            bestDy = dy;
        }

        if (!found || bestSq <= 1e-4f) return; // 同一个点没有朝向可言

        self.Yaw = MathF.Atan2(bestDy, bestDx);
        monster.Ai = self;
    }

    /// <summary>
    /// 尝试用 AI v2 推进一只怪；返回 null 表示"这只怪该继续走 v1"。
    /// <para>判据只看两件事：内容装配了吗、能力位开着吗。<b>不看</b>怪物类型或刷怪点——
    /// 否则"哪只怪走新逻辑"会变成散在各处的隐式规则，而隐式规则没法测。</para>
    /// </summary>
    private MonsterRuntimeResult? TryDecideMonsterV2(
        DsMonster monster,
        MonsterAiTarget[] targets,
        float dt,
        MonsterAiConfig aiConfig,
        List<ulong> orderedMonsterIds,
        MonsterRuntimeConfig? contentCfg)
    {
        // 内容包由调用方查一次后带过来（v1 也要读等级档，同一份查找不做两遍）。
        var config = contentCfg;
        if (config is null) return null;
        var flags = config.Behavior.Flags;
        if (MonsterAiRuntime.AllCapabilitiesOff(in flags)) return null;

        if (monster.Runtime == null)
        {
            // 两个写方都要装"刚出现不主动接敌"的宽限期，且**只在复活时装**：
            // ① 刷怪点重建实体这条路（这里，靠 <see cref="DsMonster.SpawnIsRespawn"/> 判定）；
            // ② 内核里同一实体从 Dead 翻回 Idle 那条路（`ResetForRespawn` 后紧跟 `NoteRespawn`）。
            // 漏掉任何一条，表现就是"有些刷出来的怪会立刻咬人"这种逐点不一致。
            monster.Runtime = new MonsterRuntimeState();
            if (monster.SpawnIsRespawn)
            {
                MonsterAiRuntime.NoteRespawn(monster.Runtime, _tick, _options.TicksPerSecond);
            }
        }
        var level = MonsterLevelOf(monster);
        // 这份就是调用方投影过的那一份（**投影只有一处**，见 TickMonsters 里那三行的替换）：
        // v1 与 v2 因此吃到同一套数值，"换一档 AI 换成两套数"这件事从结构上没了。
        var legacy = aiConfig;
        // 巡逻线按"刷怪点覆盖 → 行为表"取——那条式子现在只有 <see cref="ResolvePatrolRoute"/> 一份，
        // v1 与 v2 都调它（以前它只写在这一段里，于是线只有 v2 读得到）。
        var routeId = ResolvePatrolRoute(monster, config);
        var route = ResolveRouteObject(routeId);
        // 求援要问"同场别的怪在打谁"。只在**这只怪真的会用得上**时才构造清单：
        // 每怪每 tick 都 new 一份数组，在几百只怪的 shard 上就是每分钟几万次分配。
        var allies = config.Behavior.AssistRadius > 0f && flags.UseAssist && config.Species > 0
                     && monster.Ai.ThreatTargetId == 0UL
            ? BuildAssistAllies(monster, orderedMonsterIds, config.Behavior.AssistRadius)
            : null;
        // 打断信号是"上一拍有人踢了"那一拍的一次性输入（见 DsMonster.InterruptRequested）：
        // 取出来立刻清零，保证一次踢只作用一次，不会在后面的拍子里反复取消同一条施法。
        var interruptSignal = monster.InterruptRequested;
        monster.InterruptRequested = false;
        var inputs = new MonsterRuntimeInputs(
            config, level, _navGrid, _navScratch, interruptSignal, _tick, _options.TicksPerSecond, route, allies);
        return MonsterAiRuntime.Advance(monster.Ai, monster.Runtime, targets, dt, legacy, inputs);
    }

    /// <summary>
    /// 构造同场盟友清单（求援判据的输入）。
    /// <para>遍历必须按 EntityId 升序（用调用方排好的那份 id 列表，绝不按 <c>Dictionary</c>
    /// 的枚举顺序走）：清单里同名次的相对顺序在截断后才进入产物，把它钉成一个确定的全序，
    /// 才是"同 tick 序列 → 同结果"的金轨迹前提。</para>
    /// <para>自己必须被剔除：<c>MonsterAiSelf</c> 里没有实体 id，内核剔不到，只能由宿主做。</para>
    /// <para>半径外的盟友在宿主这一侧就先滤掉：内核也会再滤一次，但清单上限只有
    /// <see cref="MaxAssistAllies"/> 格，不滤的话"这一片有 200 只怪、其中 3 只离得最近"
    /// 就会被远怪挤满名额。</para>
    /// </summary>
    private Horizon.Game.Sim.Monsters.MonsterAiAlly[] BuildAssistAllies(
        DsMonster self, List<ulong> orderedMonsterIds, float assistRadius)
    {
        // 清单要**先按半径筛、再按距离截**，不能按实体 id 截：
        // id 是"刷怪点<<32 | 槽位+1"，槽位又沿 X 轴排开——按 id 截到 64 条就等于
        // "保留这一片里 X 最小的 64 只"，于是**离自己最近的那只同族反而最先被挤出去**，
        // 而内核只会在清单里挑最近的：清单本身错了，挑得再对也没用。
        var radiusSq = assistRadius * assistRadius;
        var picked = new List<(float Sq, Horizon.Game.Sim.Monsters.MonsterAiAlly Ally)>(MaxAssistAllies * 2);
        for (var i = 0; i < orderedMonsterIds.Count; i++)
        {
            var id = orderedMonsterIds[i];
            if (id == self.EntityId) continue;
            if (!_monstersById.TryGetValue(id, out var other) || other == null) continue;
            // 尸体不算盟友：它还留在世界里（为了让玩家看得见），但既不会帮忙也不该占求援清单的名额。
            if (other.Ai.Hp <= 0) continue;

            var dx = other.Ai.X - self.Ai.X;
            var dy = other.Ai.Y - self.Ai.Y;
            var sq = dx * dx + dy * dy;
            // 与内核 SelectAssistTarget 同一条半径判据（判据两份必须同形，否则一边放行一边拒绝
            // 会变成"明明在半径内却没人来"那种查不出来的组合）。
            if (sq > radiusSq) continue;

            var species = 0;
            if (MonsterContent.TryGetConfig(other.TemplateId, out var allyConfig) && allyConfig != null)
            {
                species = allyConfig.Species;
            }
            var inCombat = MonsterAiGate.InCombat(other.Ai.State);
            picked.Add((sq, new Horizon.Game.Sim.Monsters.MonsterAiAlly(
                other.EntityId, other.Ai.X, other.Ai.Y, species, inCombat,
                inCombat ? other.Ai.ThreatTargetId : 0UL)));
        }

        var list = picked;
        if (picked.Count > MaxAssistAllies)
        {
            // 按"距离近 → 实体 id 小"排序后截：与内核的择优判据同一个**全序**，
            // 所以截断不改变"谁赢"，List.Sort 不稳定也无所谓（比较键里没有并列）。
            picked.Sort((a, b) => a.Sq != b.Sq ? a.Sq.CompareTo(b.Sq) : a.Ally.EntityId.CompareTo(b.Ally.EntityId));
            // 截断必须可见：这一片怪密到清单装不下时，"远处同族不来帮忙"是截断的结果，
            // 而不是配置错了求援半径。
            MonsterAssistAlliesTruncated++;
            list = picked.GetRange(0, MaxAssistAllies);
        }

        var array = new Horizon.Game.Sim.Monsters.MonsterAiAlly[list.Count];
        for (var i = 0; i < list.Count; i++) array[i] = list[i].Ally;
        return array;
    }

    /// <summary>求援清单上限：超过说明这一片的怪密到需要空间索引，先按上限截断并计数（不静默丢）。</summary>
    public const int MaxAssistAllies = 64;

    /// <summary>因超出 <see cref="MaxAssistAllies"/> 而被截断的次数（远处的同族没来帮忙时的归因）。</summary>
    public int MonsterAssistAlliesTruncated { get; private set; }

    /// <summary>被同族求援拉进战斗的次数（与 <c>MonsterAssistAlliesTruncated</c> 一起构成求援这一路的账）。</summary>
    public int MonsterAssistSeeds { get; private set; }

    /// <summary>被求援并进同一组 leash 计时器的次数（AC `Creature.cpp:231` 那次 `SetLastLeashExtensionTimePtr`）。</summary>
    public int MonsterLeashGroupsLinked { get; private set; }

    /// <summary>
    /// 因"组内另一只挨打"而被刷新宽限窗的**其它**怪物次数。
    /// <para>没有这一格时"共享没生效"和"根本没有组"在日志里同形：两只都没进组时这个数天然是 0。</para>
    /// </summary>
    public int MonsterLeashSharedRefreshes { get; private set; }

    /// <summary>
    /// 宽限窗被**打开或续上**的次数（伤害落点 + 对怪施加 Stun/Root 两处，AC 的 `UpdateLeashExtensionTime`）。
    /// <para>与"共享刷新"分开：这一格是"窗被打开了"，那一格是"打开时顺带刷到了别的怪"。
    /// 两个都为 0 时，"宽限窗没生效"与"这场战斗里根本没有怪挨过打/被控"才分得开。</para>
    /// </summary>
    public int MonsterLeashExtensions { get; private set; }

    /// <summary>尸体拾取列表登记成功的次数（对账用）。</summary>
    public int MonsterCorpseLootRegistered
    {
        get => Volatile.Read(ref _corpseLootRegistered);
        private set => Volatile.Write(ref _corpseLootRegistered, value);
    }

    private int _corpseLootRegistered;

    /// <summary>登记失败（权威侧不可用）次数。</summary>
    public int MonsterCorpseLootRegisterFailures => Volatile.Read(ref _corpseLootRegisterFailures);

    private int _corpseLootRegisterFailures;

    /// <summary>从尸体上真正取走了几件（含金币槽）。</summary>
    public int MonsterCorpseLootTaken { get; private set; }

    /// <summary>现在躺在地上的尸体数（<c>MonsterCount - MonsterCorpseCount</c> 才是活怪数）。</summary>
    public int MonsterCorpseCount => _corpses.Count;

    /// <summary>被打断受理的次数（WoW 的"踢断"；这条通路在接入本功能前**没有任何调用方能触发**）。</summary>
    public int MonsterInterruptAcceptedCount { get; private set; }

    /// <summary>踢在"没在读条的怪"上的次数（含走 v1 的怪、已经放完的、尸体）。</summary>
    public int MonsterInterruptRejectedNoCastCount { get; private set; }

    /// <summary>踢在"那条本来不可打断"的读条上的次数（编辑器里勾掉可打断的那一类）。</summary>
    public int MonsterInterruptRejectedNotInterruptibleCount { get; private set; }

    /// <summary>够不着（超出攻击距离）而被拒的打断次数。</summary>
    public int MonsterInterruptRejectedOutOfRangeCount { get; private set; }

    /// <summary>
    /// 累计"死亡后进入尸体窗口"的次数。配了 <c>corpse_seconds</c> 却一直是 0，
    /// 就说明这个值没从库走到运行时——比"看起来没尸体"更早暴露问题。
    /// </summary>
    public int MonsterCorpsesLingering { get; private set; }

    /// <summary>
    /// 嘲讽受理次数（走威胁表锁的那一路）。与 <see cref="MonsterTauntV1RetargetCount"/> 加起来
    /// 才是"坦克一共拉走了几只"——分开记是因为**当前生产数据里的种子怪全是 v1**（ai_profile=0），
    /// 只记前者会把"绝大多数怪根本走的是另一条改写通路"这件事藏起来。
    /// </summary>
    public int MonsterTauntAcceptedCount { get; private set; }

    /// <summary>走 v1 通路（直接钉单槽目标）被受理的嘲讽次数。</summary>
    public int MonsterTauntV1RetargetCount { get; private set; }

    /// <summary>因超出嘲讽施法距离被拒的次数（<see cref="DsWorldSessionOptions.MonsterTauntRange"/>）。</summary>
    public int MonsterTauntRejectedOutOfRangeCount { get; private set; }

    /// <summary>在回位免疫窗里被拒的次数（"刚 evade 的怪拉不住"，当场拒而不是窗后生效）。</summary>
    public int MonsterTauntRejectedImmuneCount { get; private set; }

    /// <summary>对着尸体嘲讽被拒的次数（坦克在怪倒地下了一手时该看见这条，而不是什么都没发生）。</summary>
    public int MonsterTauntRejectedCorpseCount { get; private set; }

    /// <summary>
    /// 打在"正往家跑"的怪身上而被整手拒掉的次数（AC 的 <code>IsEvadingAttacks</code> 受击方那一道门）。
    /// <para>这是那条规则的<b>机制层</b>读数：效果层只能观察到"血没掉"，而"血没掉"与
    /// "这一手根本没打到（OutOfRange）"、"打到了但被躲开（Dodged）"长得一样。分开计数才分得清。</para>
    /// </summary>
    public int MonsterDamageRejectedEvadingCount { get; private set; }

    /// <summary>摸过这只怪但**已经下线**、因此不进经验分母的人数（三类跳过各记各的，混成一个"没人拿"就查不动）。</summary>
    public int KillXpSkippedOffline { get; private set; }

    /// <summary>摸过这只怪但**倒下时没爬起来**、因此不进经验分母的人数。</summary>
    public int KillXpSkippedDead { get; private set; }

    /// <summary>摸过这只怪但**站在奖励距离外**（<see cref="MonsterXpCore.GroupRewardDistance"/> 米）的人数。</summary>
    public int KillXpSkippedOutOfRange { get; private set; }

    /// <summary>因为队里有人已经灰名而被整体减半的击杀次数（WoW 的观感："队里带了个低级号，我们全组经验打折"）。</summary>
    public int KillXpGroupHalvedCount { get; private set; }

    /// <summary>最后一次击杀结算用到的人数加成率（排查"我这队怎么才拿这么点"时的读数）。</summary>
    public double KillXpLastGroupRate { get; private set; }

    /// <summary>最后一次击杀结算的**经验池等级**（AC 的 <c>_maxNotGrayMember</c>：最高的那个还没灰名的队员）。</summary>
    public int KillXpLastPoolLevel { get; private set; }


    /// <summary>
    /// 这一拍该走哪条巡逻线：<b>刷怪点覆盖（生成时已定一次，存 <see cref="DsMonster.PatrolRouteId"/>）
    /// → 行为表那一份</b>。判据只有这一处，v1 与 v2 两条通路都调它。
    /// <para>为什么必须留着"退回行为表"那一半：脚本生成这类没走静态刷怪点的路径
    /// <c>PatrolRouteId=0</c>，只读实体那一格会把它们原本还有的线丢掉（这条注释以前挂在
    /// v2 那一段里，而 v1 那一段根本没有线可读——同一个理由现在两边都用得上）。</para>
    /// </summary>
    /// <summary>
    /// NPC 身份增量：姿态（随时段变）+ 服务位图。非内容 NPC 返回 (−1, −1) = "未下发"。
    /// <para>
    /// 为什么必须是每快照下发而不是 spawn-only：<c>NetworkIdentityAuthComponent</c> 只在 Spawn 发一次，
    /// 而作息会随一天时段换姿态（铁匠白天打铁、夜里站着收摊）。只在出生那一刻发，
    /// 客户端会永远停在"第一次看见他时的姿势"，而且没有任何报错。
    /// </para>
    /// <para>
    /// 姿态口径与 <c>NpcScheduleCore.SlotAt</c> 同源；该时段没命中任何档 = 站立（内核同一条兜底），
    /// 不是"未下发"——NPC 明确地"此刻应站立"和"服务端没说"在客户端是两种画法。
    /// </para>
    /// </summary>
    private (int StandState, int ServiceFlags) ResolveNpcIdentityDelta(DsMonster monster)
    {
        if (!Horizon.Game.Sim.Npcs.NpcContent.TryGetConfig(monster.TemplateId, out var cfg) || cfg is null)
            return (-1, -1);

        var serviceFlags = unchecked((int)cfg.ServiceFlags);

        var slots = Horizon.Game.Sim.Npcs.NpcContent.ScheduleSlots(monster.TemplateId);
        if (slots.Count == 0) return ((int)Horizon.Game.Sim.Npcs.NpcPosture.Stand, serviceFlags);

        var seconds = _options.TicksPerSecond > 0 ? _tick / _options.TicksPerSecond : 0L;
        var verdict = Horizon.Game.Sim.Npcs.NpcScheduleCore.SlotAt(
            slots, seconds, Horizon.Game.Sim.Npcs.NpcScheduleCore.DefaultDayCycleSeconds);

        return ((int)(verdict.Matched ? verdict.Posture : Horizon.Game.Sim.Npcs.NpcPosture.Stand), serviceFlags);
    }

    private int ResolvePatrolRoute(DsMonster monster, MonsterRuntimeConfig? contentCfg)
    {
        var fallback = monster.PatrolRouteId != 0
            ? monster.PatrolRouteId
            : contentCfg?.Behavior.PatrolRouteId ?? 0;

        // 内容 NPC：作息档按当前时段换路线（没配作息 / 没命中 / 档里没给路线 → 落回上面那条）。
        // 非 NPC 走 TryGetConfig 失败分支，行为与以前逐字相同。
        if (Horizon.Game.Sim.Npcs.NpcContent.TryGetConfig(monster.TemplateId, out _))
        {
            var slots = Horizon.Game.Sim.Npcs.NpcContent.ScheduleSlots(monster.TemplateId);
            if (slots.Count > 0)
            {
                var seconds = _options.TicksPerSecond > 0 ? _tick / _options.TicksPerSecond : 0L;
                return Horizon.Game.Sim.Npcs.NpcScheduleCore.RouteAt(
                    slots, seconds, Horizon.Game.Sim.Npcs.NpcScheduleCore.DefaultDayCycleSeconds, fallback);
            }
        }

        return fallback;
    }

    /// <summary>
    /// 把内容包投影成 v1 形状的那份数值配置（<b>v1 与 v2 都只从这里拿</b>）。
    /// <para>以前这里只盖 v2 用得到的那几格，而 v1 的 cfg 走的是另一条零散覆盖
    /// （只盖 Rank／免疫秒／移动方式），于是同一张行为表换一档 AI 就是两套数：
    /// 把"挥击间隔"从 1.5 秒改成 3 秒，开了威胁表的怪变慢、默认档的怪一动不动。
    /// 现在两条通路调<b>同一个函数</b>，行为表里露出的每一格要么两边都生效、要么两边都不生效。</para>
    /// <para><b>没有对应格的是攻击射程</b>：行为表里根本没有这一列（只有远程风筝的
    /// ranged_min/max，而 v1 没有远程攻击），所以它继续拿点位那一份——这不是漏搬，是没源。</para>
    /// </summary>
    private static MonsterAiConfig BuildAiConfigFromContent(MonsterRuntimeConfig config, MonsterAiConfig pointConfig, int level)
    {
        var behavior = config.Behavior;
        var stats = MonsterStatsCore.Compute(
            config.Stats, level, config.Rank, config.Stats.HpOverrideBps, config.Stats.AtkOverrideBps);

        // 以刷怪点那份为**底座**，只覆盖内容包里明确配了的项。
        // 新建一份空配置去填会静默丢掉 AttackRange 等没在编辑器里露出的项（半填结构体压默认值的坑）。
        var cfg = pointConfig;
        if (behavior.MoveSpeed > 0f) cfg.MoveSpeed = behavior.MoveSpeed;
        cfg.WanderRadius = behavior.WanderRadius;
        cfg.AggroRadius = behavior.AggroRadius;
        // 生效等级也要带：AC 的索敌半径是<b>按目标</b>算的（等级差每级约 1 码），
        // 少了这一格那条判据永远只能拿到"未知等级 ⇒ 不调"，等于配了个不会生效的公式。
        // 等级权威是切片 27 那一个出口（传进来的 level），这里只是把它递给 AI。
        cfg.Level = level;
        cfg.LeashRadius = behavior.LeashRadius;
        // 索敌扇形：以前只有 v2 的索敌查它（v1 只比半径 ⇒ 默认档的怪画了扇形也从背后咬人）。
        cfg.AggroConeDegrees = behavior.AggroConeDegrees;
        // 检测位：藏着的人只有带 bit2 的怪才看得见（判据主人 MonsterAiGate.CanDetectTarget）。
        // 投影只在这一处——漏带它这一格就会"内容里配了、v1 的怪却看不见"。
        cfg.DetectMask = behavior.DetectMask;
        // 被动怪那一格（内容表 is_aggressive 的反面）：以前只有 v2 读，默认档配成"不打你先手"也照样先咬人。
        // 用反向旗标是为了让 bool 的零值等于既有行为（缺省 false＝主动，旧夹具与旧内容逐字不变）。
        cfg.IsPassive = !behavior.IsAggressive;
        // 巡逻速度那一格（walk_speed）：以前只有 v2 的无路线游荡读它 ⇒ 默认档的怪散步用"追你"的速度，
        // 种子内容里 walk=2.5 / move=4，表现就是"怪溜达比冲刺快"，而编辑器那一格是装饰。
        cfg.WalkSpeed = behavior.WalkSpeed;
        // 移动方式两边都要带：v2 从 behavior 自己拿，而这份 legacy 是给 v1 形态的出口用的。
        // 少带这一格，"站桩"就只在一条通路上成立（同一管线的两份清单，漏一处就静默）。
        cfg.FollowType = behavior.FollowType;
        // 等级档唯一的用途是"世界 BOSS 整段不查距出生点"，判据住在 MonsterAiGate.LeashApplies。
        cfg.Rank = config.Rank;
        // 回位免疫窗的秒数：行为表那一份是权威，v1 也要吃得到（缺省数字两边同一个主人）。
        cfg.EvadeImmunitySeconds = behavior.EvadeImmunitySeconds;
        // 下面两格是"行为表数值到不了 v1"那两条（§11.2 第 14 条）：待机转巡逻、近战挥击间隔。
        cfg.IdleToPatrolSeconds = behavior.IdleToPatrolSeconds;
        if (behavior.MeleeSwingSeconds > 0f) cfg.AttackCooldownSeconds = behavior.MeleeSwingSeconds;
        cfg.MaxHp = stats.MaxHp;
        cfg.AttackPower = stats.AttackPower;
        // **不重算重生秒**：点位那一份才是它的权威（装配期 `MonsterSpawnPointBuilder` 已经把
        // "模板公式 + 刷怪点显式覆盖"算进去，脚本生成点还在那里写"限时怪不复活"的 float.MaxValue）。
        // 这一格两条 AI 通路都不读（读的是宿主死亡/槽位那条独立取点位的路），
        // 在这里按公式重算只会把覆盖值与 TTL 哨兵抹掉——以前正是这么丢的。
        return cfg;
    }

    /// <summary>内容包里的基准等级（非法值收成 1，避免"等级 0 的怪"把让分公式推到灰名之外）。</summary>
    private static int ResolveMonsterLevel(MonsterRuntimeConfig config)
        => config.LevelBase > 0 ? config.LevelBase : 1;

    /// <summary>
    /// 生成那一刻定下这只怪的**生效等级**：<c>刷怪点覆盖 → 内容模板基准 → 代码目录模板等级</c>。
    /// <para>只算一次并存在实体上（见 <see cref="DsMonster.EffectiveLevel"/>）：等级同时决定血量/攻击、
    /// 击杀经验和头顶信息卡，三处各自推导迟早分家。</para>
    /// </summary>
    /// <summary>
    /// 模板等级：怪物目录优先；它没登记这个号时看 NPC 内容（敌对型 NPC 的等级就存在那里）。
    /// <b>两处等级解析必须调这一个函数</b>，否则"名字对了、等级还是 1"。
    /// </summary>
    internal static int ResolveTemplateLevel(int templateId)
    {
        if (MonsterTemplateCatalog.TryGetRegistered(templateId, out var monsterTemplate))
            return monsterTemplate.Level;

        if (Horizon.Game.Sim.Npcs.NpcContent.TryGetConfig(templateId, out var npc) && npc != null && npc.LevelBase > 0)
            return npc.LevelBase;

        return MonsterTemplateCatalog.Get(templateId).Level;
    }

    /// <summary>
    /// 生成那一刻定下这只怪的**生效等级**：<c>刷怪点显式点名 → 模板等级区间掷 → 代码目录模板等级</c>。
    /// <para>中间那一档是 <see cref="MonsterLevelCore.Roll"/>（AzerothCore <c>Creature::SelectLevel</c> 同形：
    /// min/max 归一化、闭区间、<c>min == max</c> 不掷）。以前这里只用 <c>LevelBase</c>，
    /// 于是 <c>level_min</c>/<c>level_max</c> 那两格配了也没人读——而血量、攻击、击杀经验三处都跟着等级走。</para>
    /// <para>只算一次并存在实体上（见 <see cref="DsMonster.EffectiveLevel"/>）：等级同时决定那三件事与头顶信息卡，
    /// 三处各自推导迟早分家。掷骰键用**实体 id + 生成那一拍的 tick**，所以同 seed 同 tick 可重放，
    /// 而每次重生会重掷（AC 也是每次 <c>Respawn()</c> 重跑 <c>SelectLevel</c>）。</para>
    /// </summary>
    private int ResolveEffectiveLevel(int spawnPointId, int templateId, ulong entityKey, long serverTick)
    {
        // 脚本生成点（内容 NPC / GM 指令）的显式命名等级：一点定终身，不掷区间。
        if (_scriptPointLevelOverride.TryGetValue(spawnPointId, out var scriptedLevel) && scriptedLevel > 0)
            return scriptedLevel;

        var templateLevel = ResolveTemplateLevel(templateId);
        if (!MonsterContent.TryGetConfig(templateId, out var config) || config == null)
            return templateLevel > 0 ? templateLevel : 1;

        var points = MonsterContent.SpawnPoints();
        if (points != null)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i].SpawnPointId != spawnPointId) continue;
                // 刷怪点显式点名 ⇒ 一点定终身（策划要的就是"这一点是 7 级"），不掷区间。
                if (points[i].LevelOverride.HasValue && points[i].LevelOverride.Value > 0)
                    return points[i].LevelOverride.Value;
                break;
            }
        }
        // 没点名：按模板的等级区间掷；基准留着当"区间没配"那一档的退路。
        return MonsterLevelCore.Roll(config.LevelMin, config.LevelMax, ResolveMonsterLevel(config), entityKey, serverTick);
    }

    /// <summary>
    /// 这只怪走哪条巡逻线：<c>刷怪点 route_override_id → 行为表 patrol_route_id → 0=没有路线（按游荡半径踱步）</c>。
    /// <para>库里那一列的注释就写着"0=用行为表"，所以覆盖关系是配置侧的既有承诺；
    /// 而在本轮之前会话只用行为表那一份，于是策划在编辑器/库里给某个刷怪点换路线，游戏里一动不动。</para>
    /// </summary>
    /// <summary>
    /// 按 id 取巡逻线：怪物内容优先；没登记时看 NPC 内容（内容 NPC 的线存在那边，结构同款）。
    /// <b>两处调用都走这一个函数</b>，否则 v1/v2 会各自看到不同的"有没有线"。
    /// </summary>
    private static MonsterPatrolRoute? ResolveRouteObject(int routeId)
    {
        if (routeId <= 0) return null;

        if (MonsterContent.Route(routeId) is { } route) return route;

        var npcPath = Horizon.Game.Sim.Npcs.NpcContent.Path(routeId);
        if (npcPath == null) return null;

        var points = npcPath.Value.Points;
        if (points == null || points.Count < 2) return null;

        var array = new Horizon.Game.Sim.Monsters.MonsterPatrolPoint[points.Count];
        for (var i = 0; i < array.Length; i++) array[i] = points[i];

        return new Horizon.Game.Sim.Monsters.MonsterPatrolRoute
        {
            RouteId = npcPath.Value.PathId,
            Mode = (Horizon.Game.Sim.Monsters.MonsterPatrolMode)npcPath.Value.Mode,
            PauseAtPointSeconds = npcPath.Value.PauseAtPointSeconds,
            UseNavMesh = npcPath.Value.UseNavMesh,
            Points = array,
        };
    }

    private int ResolvePatrolRouteId(int spawnPointId, int templateId)
    {
        if (_scriptPointRouteId.TryGetValue(spawnPointId, out var scriptedRoute) && scriptedRoute > 0)
            return scriptedRoute;

        var templateRoute = 0;
        if (MonsterContent.TryGetConfig(templateId, out var config) && config != null)
            templateRoute = config.Behavior.PatrolRouteId;

        var points = MonsterContent.SpawnPoints();
        if (points != null)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i].SpawnPointId != spawnPointId) continue;
                return points[i].RouteOverrideId > 0 ? points[i].RouteOverrideId : templateRoute;
            }
        }
        return templateRoute;
    }

    /// <summary>读怪物的生效等级；脚本生成等没走刷怪点的路径（EffectiveLevel=0）退回模板基准。</summary>
    private static int MonsterLevelOf(DsMonster monster)
    {
        if (monster.EffectiveLevel > 0) return monster.EffectiveLevel;
        if (MonsterContent.TryGetConfig(monster.TemplateId, out var config) && config != null)
            return ResolveMonsterLevel(config);
        var templateLevel = ResolveTemplateLevel(monster.TemplateId);
        return templateLevel > 0 ? templateLevel : 1;
    }

    /// <summary>
    /// 装入服务端导航网格（宿主在启动/换图时调用；传无效网格 = 全部退回直线移动）。
    /// </summary>
    public void ConfigureNavigation(MonsterNavGrid grid)
    {
        _navGrid = grid;
        if (!grid.IsValid)
        {
            _navScratch = null;
            return;
        }
        _navScratch ??= new MonsterNavScratch();
        _navScratch.EnsureCapacity(grid.Width * grid.Height);
    }

    /// <summary>当前是否已装入可用的导航网格（诊断与自检用）。</summary>
    public bool HasNavigation => _navGrid.IsValid;

    /// <summary>
    /// 内容要求走图、而本 DS 没装网格的怪有几只（**仪表**，随装图/生成变化，不是累计计数）。
    /// <para>
    /// 为什么需要它：<c>flagUseNavMesh</c> 是内容侧的开关，网格是宿主侧的装配。两者对不上时
    /// 内核安静地退回直线（<c>inputs.Grid.IsValid</c> 那道门），表现是"编辑器里勾了走图、怪照样穿墙"——
    /// 没有任何一行日志说过原因。摘要里常驻这一格，"勾了没生效"就不再靠猜。
    /// </para>
    /// </summary>
    public int MonstersWantingNavWithoutGrid
    {
        get
        {
            if (_navGrid.IsValid) return 0;
            var count = 0;
            foreach (var kv in _monstersById)
            {
                var monster = kv.Value;
                if (monster == null) continue;
                if (!MonsterContent.TryGetConfig(monster.TemplateId, out var content) || content == null) continue;
                // 运行时配置里的能力位**已经展开过**（装配层按档位+逐位覆盖算好），这里不再自己展开一份。
                if (content.Behavior.Flags.UseNavigation) count++;
            }
            return count;
        }
    }

    /// <summary>
    /// 这只实体能用的技能挂载：怪物内容优先，NPC 内容回退（敌对 NPC 走怪物管线，没有 MonsterTemplate 行）。
    /// <para>结算与"晚进场补发 Spawn"必须问同一处，否则 NPC 起手时补发出去的是一根没有技能身份的空条。</para>
    /// </summary>
    private static Horizon.Game.Sim.Monsters.MonsterSkillMount[] ResolveCastMounts(int templateId)
    {
        Horizon.Game.Sim.Monsters.MonsterSkillMount[] mounts = null;
        if (MonsterContent.TryGetConfig(templateId, out var config) && config != null) mounts = config.Skills;
        if ((mounts == null || mounts.Length == 0)
            && Horizon.Game.Sim.Npcs.NpcContent.TryGetConfig(templateId, out var npcCfg)
            && npcCfg != null)
        {
            mounts = Horizon.Game.Sim.Npcs.NpcContent.SkillMounts(templateId);
        }

        return mounts;
    }

    /// <summary>
    /// 怪物技能结算：伤害走既有 <see cref="CombatCore"/>，线上仍只发 <see cref="DamagePacket"/>
    /// （施法条另有 <c>MonsterCastStart/Stop</c> 事件，不靠这个包）。
    /// <para>
    /// <b>结算前必须重算最终目标集合</b>：<c>target_select / target_count / aoe_radius</c> 是编辑器里配的东西，
    /// 起手时锁定的 <c>cast.TargetEntityId</c> 只是"首要目标/AOE 圆心"。
    /// 以前这里只打那一个人，于是"AOE 3 目标 8 米"在游戏里表现为只有一人掉血。
    /// </para>
    /// </summary>
    private void ResolveMonsterCast(DsMonster monster, MonsterCastDecision cast)
    {
        if (cast.TargetEntityId == 0UL) return;

        // 挂载来源：怪物内容优先；NPC 内容作为回退（敌对 NPC 走的是怪物管线，没有 MonsterTemplate 行）。
        // 两边的挂载结构是同一个 MonsterSkillMount，结算口只有这里一个。
        var mounts = ResolveCastMounts(monster.TemplateId);

        if (mounts == null || cast.MountIndex < 0 || cast.MountIndex >= mounts.Length) return;
        var mount = mounts[cast.MountIndex];

        var wanted = mount.TargetCount > 0 ? mount.TargetCount : 1;
        var cap = wanted > MaxCastTargets ? MaxCastTargets : wanted;
        var output = new ulong[cap];
        var candidates = BuildCastCandidates(monster);
        if (!MonsterCastingCore.SelectTargets(
                mount, cast.TargetEntityId, monster.Ai.X, monster.Ai.Y, candidates,
                monster.EntityId, _tick, output, out var selectedCount, out var requestedCount))
        {
            MonsterCastNoTarget++;
            return;
        }

        var baseStats = BuildMonsterStats(monster);
        var scale = mount.DamageScale > 0f ? mount.DamageScale : 1f;
        var attackerStats = new CombatantStats
        {
            Hp = baseStats.Hp,
            MaxHp = baseStats.MaxHp,
            AttackPower = baseStats.AttackPower * scale,
            Defense = baseStats.Defense,
            Element = baseStats.Element,
            DodgeRate = baseStats.DodgeRate,
            CritRate = baseStats.CritRate,
            CritMultiplier = baseStats.CritMultiplier,
        };
        var range = mount.MaxRange > 0f ? mount.MaxRange : 8f;

        MonsterCastTargetsSelected += selectedCount;
        if (selectedCount < requestedCount) MonsterCastTargetShortfalls++;

        for (var n = 0; n < selectedCount; n++)
        {
            var targetEntityId = output[n];
            if (!_sessionByEntityId.TryGetValue(targetEntityId, out var sessionId)) continue;
            if (!_playersBySession.TryGetValue(sessionId, out var player)) continue;
            if (player.IsDead) continue;

            var targetPos = _simWorld.GetEntity(targetEntityId);
            var request = new CombatRequest
            {
                AttackerId = monster.EntityId,
                TargetId = targetEntityId,
                SkillId = cast.SkillId,
                // 技能伤害由 SkillRawDamage 承载（与玩家施法同一条口径），普攻那条走 0。
                SkillRawDamage = baseStats.AttackPower * scale,
                ServerTick = _tick,
                AttackerX = monster.Ai.X,
                AttackerY = monster.Ai.Y,
                AttackerYaw = monster.Ai.Yaw,
                TargetX = targetPos?.X ?? player.SpawnX,
                TargetY = targetPos?.Y ?? player.SpawnY,
                AttackRange = range,
                FacingCosThreshold = -2f,
            };

            var resolution = CombatCore.Resolve(request, attackerStats, BuildPlayerStats(player));
            if (!resolution.DealtDamage) continue;

            // 与普攻共用同一段"扣血 + 广播 + 死亡处理"，两条路不会在死亡惩罚上分叉。
            ApplyMonsterHitToPlayer(sessionId, player, monster, targetEntityId, resolution,
                cast.SkillId, mount.SchoolMask > 0 ? 2 : 1);

            // 挂载上那两格（status_id / status_duration_seconds）在这里落地：**命中即施加**。
            // AC 的主路径同样是"技能 effect 落在目标单位上"（方案文档 §2.5），只是那边配在技能上。
            // 落点与伤害同一条件：`resolution.DealtDamage == false`（未命中/被闪避）时上面已经 continue，
            // 所以"打空了不挂控制"——这一条要与"被 0 伤害也要挂"（免伤/护盾）分开的话，
            // 得先有"命中但零伤"的信号，今天没有，不在这里假装区分。
            if (mount.StatusId > 0 && mount.StatusDurationSeconds > 0f)
                ApplyPlayerStatus(player, mount.StatusId, mount.StatusDurationSeconds, mount.StatusMagnitudeBps);
        }
    }

    /// <summary>一次技能最多结算几个人（同屏被 AOE 覆盖的人数上限；超出就是配置写错或战场失控）。</summary>
    private const int MaxCastTargets = 8;

    /// <summary>技能选出的目标总数（"AOE 到底打了几个"的证据，0 就说明这一层没生效）。</summary>
    public int MonsterCastTargetsSelected { get; private set; }

    /// <summary>想打的多、打到的少（被半径/人数/上限截断）的次数。</summary>
    public int MonsterCastTargetShortfalls { get; private set; }

    /// <summary>施法完成却一个目标都没选出来的次数（不该发生；发生即候选集合与威胁表脱节）。</summary>
    public int MonsterCastNoTarget { get; private set; }

    // ==================== 控制状态（期 1a：玩家侧一半；怪物侧住在 MonsterRuntimeState） ====================

    /// <summary>给玩家挂状态成功的次数（含同档刷新）。</summary>
    public int PlayerStatusAppliedCount { get; private set; }

    /// <summary>到期回收掉的状态条数（单点回收的证据）。</summary>
    public int PlayerStatusExpiredCount { get; private set; }

    /// <summary>被拒的次数：号不认识 / 时长非法。<b>必须可读</b>——否则"配了而从不生效"没人知道。</summary>
    public int PlayerStatusRejectedCount { get; private set; }

    /// <summary>这一拍身上还有状态的玩家数（摘要用）。</summary>
    public int PlayerStatusActiveCount { get; private set; }

    /// <summary>因为被封腿而抹掉移动输入的次数。</summary>
    public long InputBlockedByStatus { get; private set; }

    /// <summary>因为减速而按比例缩小移动输入的次数（数值类状态的效果层读数）。</summary>
    public long InputSlowedByStatus { get; private set; }

    /// <summary>起跳/轻功那两位：定身与眩晕期间连跳也不许。</summary>
    private const uint JumpOrLeapBits = 0x1u | (1u << 3);

    /// <summary>
    /// 玩家身上这一拍的状态位（**唯一写口**，快照那两处都调它）。
    /// <para><see cref="EntityStateBits.Hidden"/> / <see cref="EntityStateBits.Frozen"/> 今天仍然**没有写方**
    /// （隐身与冰冻的来源都还没做）——这一句就是它们唯一的登记处：将来接上来源时在这里加一位，
    /// 而不是去快照那两处各写一遍（那样迟早一处分叉）。</para>
    /// </summary>
    private static uint PlayerStateBitsOf(DsPlayer player)
    {
        if (player.IsDead) return EntityStateBits.Dead;
        var bits = 0u;
        if (player.Statuses == null || player.StatusCount == 0) return bits;
        // 眩晕必然连带定身（AC：`SetStunned` 内部 `SetRooted(true,true)`，Unit.cpp:18418/18464），
        // 所以两条都点亮 Rooted；"能不能施法"交给 Stunned 那一位自己表达。
        if (MonsterStatusCore.Has(player.Statuses, player.StatusCount, MonsterStatusKind.Stun))
            bits |= EntityStateBits.Stunned | EntityStateBits.Rooted;
        else if (MonsterStatusCore.Has(player.Statuses, player.StatusCount, MonsterStatusKind.Root))
            bits |= EntityStateBits.Rooted;
        // 沉默单独一位：它只封"放技能"（AC 的 SPELL_FAILED_SILENCED），不封走也不封平A。
        // 眩晕已经用 Stunned 表达"连施法也不行"，不必再叠一位（客户端读 Stunned 时本来就同时拦两样）。
        if (MonsterStatusCore.Has(player.Statuses, player.StatusCount, MonsterStatusKind.Silence))
            bits |= EntityStateBits.Silenced;
        return bits;
    }

    /// <summary>这一帧能不能走（定身/眩晕都拦；沉默不拦）。</summary>
    private static bool PlayerMovementBlocked(DsPlayer player) =>
        player.Statuses != null && player.StatusCount > 0
        && MonsterStatusCore.BlocksMovement(player.Statuses, player.StatusCount);

    /// <summary>这一帧能不能放技能（眩晕/沉默都拦；定身不拦）。</summary>
    private static bool PlayerCastingBlocked(DsPlayer player) =>
        player.Statuses != null && player.StatusCount > 0
        && MonsterStatusCore.BlocksCasting(player.Statuses, player.StatusCount);

    /// <summary>因为身上有控制状态而被拒掉的技能类动作数（嘲讽/打断）。</summary>
    public int CombatActionRejectedByStatus { get; private set; }

    /// <summary>
    /// 每 tick 单点回收玩家的控制状态（AC 的 `Unit::_UpdateSpells` 那一拍：扫 IsExpired → remove）。
    /// 怪物那份回收住在 `MonsterAiRuntime.Advance` 的拍首；玩家没有内核 tick，宿主这一处就是它的"拍首"。
    /// </summary>
    private void TickPlayerStatuses()
    {
        PlayerStatusActiveCount = 0;
        foreach (var kv in _playersBySession)
        {
            var player = kv.Value;
            if (player.Statuses == null || player.StatusCount == 0) continue;
            var count = player.StatusCount;
            var removed = MonsterStatusCore.Expire(player.Statuses, ref count, _tick);
            player.StatusCount = count;
            if (removed > 0) PlayerStatusExpiredCount += removed;
            if (count > 0) PlayerStatusActiveCount++;
        }
    }

    /// <summary>
    /// 把一次技能附带的状态挂到玩家身上（AC 的 aura 主路径：技能的 effect 直接落在目标单位上）。
    /// <para>号来自挂载的 <c>status_id</c>（本作的自家设计，AC 那侧是技能属性，见方案文档 §2.5）；
    /// 号不认识或时长非法 ⇒ **拒绝并计数**，不许静默当成"没配"。</para>
    /// <para>本作玩家上的"打断在读的条"不由 DS 做：玩家施法的权威在技能/战斗那两条链（grain 层，
    /// 属并行会话在改的文件），DS 这一侧今天只能拦移动。这条边界写进方案文档 §11.4。</para>
    /// </summary>
    private void ApplyPlayerStatus(DsPlayer player, int statusId, float durationSeconds, int magnitudeBps = 0)
    {
        if (player == null || player.IsDead) return;
        if (!MonsterStatusCore.TryFromId(statusId, out var kind) || durationSeconds <= 0f)
        {
            PlayerStatusRejectedCount++;
            return;
        }
        var ticks = (long)(durationSeconds * _options.TicksPerSecond);
        if (ticks <= 0)
        {
            PlayerStatusRejectedCount++;
            return;
        }
        player.Statuses ??= new MonsterStatusEntry[MonsterStatusCore.MaxEntries];
        var count = player.StatusCount;
        var interrupt = MonsterStatusCore.Apply(
            player.Statuses, ref count, kind, _tick + ticks, _tick, magnitudeBps);
        player.StatusCount = count;
        PlayerStatusAppliedCount++;
        if (interrupt) PlayerStatusInterruptRequested++;
    }

    /// <summary>
    /// "要打断在读那条"被请求了几次。
    /// <para>DS 今天**没有**玩家的施法权威（见 <see cref="ApplyPlayerStatus"/> 的注释），所以这一格只能是
    /// "请求了但没人执行"的证据。留在摘要里而不是丢掉：等技能链接上时它就是现成的入口读数，
    /// 而"0 次"也让"玩家侧眩晕/沉默目前不打断读条"这件事在运行期问得出来。</para>
    /// </summary>
    public int PlayerStatusInterruptRequested { get; private set; }

    /// <summary>玩家技能给**怪物**上状态成功的次数（v16 的 <c>CombatActionPacket.StatusId</c> 那条路）。</summary>
    public int MonsterStatusAppliedCount { get; private set; }

    /// <summary>给怪上状态被拒的次数：号不认识 / 时长非法。</summary>
    public int MonsterStatusRejectedCount { get; private set; }

    /// <summary>
    /// 怪身上**没有运行态**而装不了状态的次数（v1 的怪 `Runtime == null`）。
    /// <para>必须可读：生产那 11 只种子怪今天全是 `ai_profile=0`（走 v1），
    /// 这一格涨就说明"玩家给怪配了控制而它照旧追人"是这一层还没覆盖 v1，不是配错。</para>
    /// </summary>
    public int MonsterStatusUnsupportedCount { get; private set; }

    /// <summary>
    /// 把动作包里附带的状态挂到被命中的怪物身上（与 <see cref="ApplyPlayerStatus"/> 完全同形）。
    /// <para>AC 里两边都是"技能 effect 落在目标单位上"（`Spell.cpp:2925/:3080`）——
    /// 怪物打玩家用挂载行，玩家打怪用动作包，两条都进同一个 `MonsterStatusCore`。</para>
    /// </summary>
    private void ApplyStatusToMonster(DsMonster monster, int statusId, float durationSeconds, int magnitudeBps = 0)
    {
        if (statusId <= 0 && durationSeconds <= 0f) return;   // 动作没带状态：最常见的一条路，不计数也不拒
        if (!MonsterStatusCore.TryFromId(statusId, out var kind) || durationSeconds <= 0f)
        {
            MonsterStatusRejectedCount++;
            return;
        }
        var runtime = monster.Runtime;
        if (runtime == null)
        {
            MonsterStatusUnsupportedCount++;
            return;
        }
        var ticks = (long)(durationSeconds * _options.TicksPerSecond);
        if (ticks <= 0)
        {
            MonsterStatusRejectedCount++;
            return;
        }
        runtime.ApplyStatus(kind, _tick + ticks, _tick, magnitudeBps);
        // AC 在**丢失控制**期间也刷新 leash 计时器（Creature.cpp:799 的 UpdateLeashExtensionTime）：
        // 被按住/被定身的怪根本跑不出半径，若照常按距离判脱战，玩家看到的是"刚控住它就瞬移回家"。
        // 只认 Stun/Root（AC 的 LOST_CONTROL），减速不算——被减速仍然能走。
        if (kind == MonsterStatusKind.Stun || kind == MonsterStatusKind.Root)
        {
            var ai = monster.Ai;
            ai.LastLeashExtensionTick = _tick;
            monster.Ai = ai;
            MonsterLeashExtensions++;
        }
        MonsterStatusAppliedCount++;
    }

    /// <summary>按内容绑定掷过闸的掉落表张数（0 = 这只怪没配绑定，走了全局兜底表）。</summary>
    public int MonsterLootBindingsRolled { get; private set; }

    /// <summary>无条件掉落掷出的件数（每件都是一次独立掷骰的产物）。</summary>
    public int MonsterLootItemsRolled { get; private set; }

    /// <summary>条件掉落（要持有任务）掷出的件数：交给业务层判，判不过就是没给。</summary>
    public int MonsterLootConditionalRolled { get; private set; }

    /// <summary>条件类型本层判不了、按"不给"处理的条数（组队/稀有度门槛）。</summary>
    public int MonsterLootDeferredByCondition { get; private set; }

    /// <summary>
    /// 掷出的金币总额。**只有计数，DS 侧还没有入账那一段 wiring**：
    /// 钱包那侧的加钱接口已经存在（<c>ICurrencyGrain.AddAsync</c>，商店/训练师/制作在读它），
    /// 缺的是"击杀事件带上金额 → <c>OrleansGameplayWiring</c> 调一次 AddAsync"这一跳
    /// （见任务清单：事件结构体要多带 Money，且 <c>BuildIdempotencyKey</c> 必须把它一起拼进键，
    /// 否则两次不同金额的入账会互相被去重吞掉）。所以这条仍是"已经算出但还没有落点"的显形，不是"已经给了玩家"。
    /// </summary>
    public long MonsterLootMoneyRolled { get; private set; }

    /// <summary>
    /// 组施法候选集合：在线玩家的位置/血量 + 是否在这只怪的威胁表里，**按 EntityId 升序**。
    /// <para>顺序是内核的确定性前置契约（与 <c>TickMonsters</c> 喂给 AI 的那份同源）；
    /// 单独再拼一次是因为要多带 HP 与威胁表成员关系两样东西。</para>
    /// </summary>
    private MonsterCastingCore.MonsterCastCandidate[] BuildCastCandidates(DsMonster monster)
    {
        var runtime = monster.Runtime;
        var list = new List<MonsterCastingCore.MonsterCastCandidate>(_playersBySession.Count);
        foreach (var kv in _playersBySession)
        {
            var p = kv.Value;
            var entity = _simWorld.GetEntity(p.EntityId);
            var inThreat = false;
            if (runtime != null)
            {
                for (var i = 0; i < runtime.ThreatCount; i++)
                {
                    if (runtime.ThreatTable[i].EntityId != p.EntityId) continue;
                    inThreat = true;
                    break;
                }
            }
            list.Add(new MonsterCastingCore.MonsterCastCandidate(
                p.EntityId,
                entity?.X ?? p.SpawnX,
                entity?.Y ?? p.SpawnY,
                p.Hp,
                p.Hp > 0 && !p.IsDead,
                inThreat));
        }
        list.Sort(static (a, b) => a.EntityId.CompareTo(b.EntityId));
        return list.ToArray();
    }

    /// <summary>本会话累计下发的施法条事件数（起手/结束各一；诊断用，"配了技能但一条都没发"要靠它显形）。</summary>
    public int MonsterCastStartEvents { get; private set; }
    /// <summary>本会话累计下发的施法条结束事件数。</summary>
    public int MonsterCastStopEvents { get; private set; }

    /// <summary>
    /// 本会话累计"巡逻游标前进了一格"的次数（巡逻线上的**到点回执**）。
    /// <para>为什么单独计数：巡逻是"配了线 ⇒ 世界里的怪绕着点走"这种玩家能看见的行为，
    /// 而它以前**零回执**——DS 摘要里有实体数、有事件数、有施法条数，就是没有任何一行能回答
    /// "那条线真的有人在走吗"。没有这一格，"库里画了 13 个点"与"这条线被装配后没人走"
    /// 在日志里长得一模一样（与 <see cref="MonsterCastStartEvents"/> 同族：配了没人读，必须自己念出来）。</para>
    /// <para>计数口径取 <c>MonsterAiDecision.PatrolPointSwitched</c>（内核那一份"这一拍换了目的地"的旗标），
    /// 而不是比游标下标：v1 的游标在 <c>Ai.Patrol</c>、v2 的在 <c>MonsterRuntimeState.Patrol</c>，
    /// 比下标会系统性漏掉整条 v2 通路（这条判据第一次跑就是这样红的）。
    /// 也不拿位移当证据：停下时也会被拖到目标点（<c>MonsterRuntime.ApplyIfStopped</c>），
    /// 被战斗拖去的目标点同样有位移。</para>
    /// <para><b>这一格含 NPC 的路线</b>：<see cref="ResolveRouteObject"/> 先查怪物内容、查不到才退
    /// <c>NpcContent.Path</c>，所以生产包"路线=0"时这里仍会涨（涨的是 NPC 作息那条线）。
    /// 摘要标签因此写"巡逻到点(含NPC线)"——与"在位实体(含NPC)"同一句理由：会说谎的标签比没有标签更糟。</para>
    /// </summary>
    public int MonsterPatrolPointsReached { get; private set; }

    /// <summary>PvP 被阵营矩阵拒掉的次数（非敌对阵营/脏数据；诊断"打不了"是被谁拒的）。</summary>
    public int PvpRejectedFactionCount { get; private set; }

    /// <summary>PvP 实际造成伤害的次数。</summary>
    public int PvpDamageDealtCount { get; private set; }

    /// <summary>PvP 击杀累计发放的经验（&gt;0 = 荣誉经验确实发出去了，不只是事件在队列里）。</summary>
    public long PvpKillXpGranted { get; private set; }

    /// <summary>
    /// NPC 因残血逃跑而进入"远离威胁"的次数（<c>FleesAtLowHealth</c> 位，按"进入"计一次，不是每 tick）。
    /// 配了这一位却一直是 0，就说明内容没装进运行期或判据没被调用——比"看起来没跑"更早暴露问题。
    /// </summary>
    public int NpcFleeStartedCount { get; private set; }

    /// <summary>
    /// NPC 求援（<c>CallsReinforcement</c>）叫醒的同族数量。勾了位却一直是 0，
    /// 说明这个位仍只是装载期校验——比"看起来没人来帮忙"更早暴露问题。
    /// </summary>
    public int NpcReinforcementWokenCount { get; private set; }

    /// <summary>吟唱挂载在内容包里查不到的次数（运行态与内容包不一致的信号，正常应为 0）。</summary>
    public int MonsterCastMountLookupMisses { get; private set; }

    /// <summary>
    /// 头顶施法条的边沿同步：把"内核认为在读条吗"与"屏幕上挂着条子吗"对齐。
    /// <para>
    /// <b>为什么不照 <see cref="MonsterCastDecision.BeginsCast"/> 直接发</b>：起手有真事件，
    /// 但**结束**有四个出口（完成 / 打断 / 目标失效取消 / 施法者死亡消失），
    /// 其中"取消"那条在决策里没有独立标记。逐出口写 <c>if</c> 迟早漏一个，
    /// 而漏掉的那个出口不会报错——它只让一根条子永远停在头顶，谎报"这只怪还在读条"。
    /// 改成按状态对账后，任何让 <c>IsCasting</c> 变 false 的路径都会自动补出那一条 Stop。
    /// </para>
    /// </summary>
    private void SyncMonsterCastBar(DsMonster monster, in MonsterCastDecision cast)
    {
        var runtime = monster.Runtime;
        var casting = runtime != null && runtime.Casting.IsCasting;
        if (casting == monster.CastBarVisible) return;   // 稳态不进事件通路：每 tick 一条的话带宽先炸。

        if (casting)
        {
            EmitMonsterCastStart(monster, runtime!.Casting);
            monster.CastBarVisible = true;
            return;
        }

        // 条子要收：按本 tick 的决策判原因，判不出来就是"取消"（目标失效/脱离战斗）。
        var reason = cast.FinishesCast ? Horizon.Game.Message.Sync.MonsterCastEndReason.Completed
                   : cast.InterruptedCast ? Horizon.Game.Message.Sync.MonsterCastEndReason.Interrupted
                   : Horizon.Game.Message.Sync.MonsterCastEndReason.Cancelled;
        EmitMonsterCastStop(monster, reason);
        monster.CastBarVisible = false;
    }

    /// <summary>
    /// 进战喊话的边沿同步：从"不在战斗"翻到"在战斗"那一拍发一条 <c>MonsterSay</c>，脱战即复位（下一轮进战再喊）。
    /// <para>三件事决定了它长这样：</para>
    /// <list type="number">
    /// <item><b>按状态对账，不在各起手出口挂句子</b>——进入战斗有目力/嘲讽/求援/挨打多个出口，
    /// 逐出口写迟早漏一个（施法条那条已经付过学费，见 <see cref="SyncMonsterCastBar"/>）。</item>
    /// <item><b>只发文本号</b>：文案在客户端内容表里（与外观表同一口径）。
    /// 服务端连文案都不查，所以这里只问 <see cref="MonsterContent.HasText"/> 一句"这一号有没有东西"。</item>
    /// <item><b>指针落空要计数</b>：<c>aggro_say_id</c> 配了而内容包里没有那一号，
    /// 装配期已经会念一条 problem（<c>ToBundle</c>），运行期这条计数用来抓"包换了而号没换"。</item>
    /// </list>
    /// </summary>
    private void SyncMonsterSay(DsMonster monster)
    {
        MonsterContent.TryGetConfig(monster.TemplateId, out var config);
        var sayId = config?.AggroSayId ?? 0;
        var inCombat = MonsterAiGate.InCombat(monster.Ai.State);

        if (!inCombat)
        {
            monster.SayAnnouncedInCombat = false;
            return;
        }
        if (monster.SayAnnouncedInCombat) return;      // 本轮已经喊过：稳态不进事件通路
        monster.SayAnnouncedInCombat = true;           // 无论发不发都记账，避免每拍重试刷屏
        if (sayId <= 0) return;                        // 没配这一格：与"配了但没内容"分开计

        // 组内挑一句（AC `creature_text` 的 group 口径：一个事件组可以写多句，一次触发只念一句）。
        // 盐 = 实体号 × 黄金比例常数 ^ 当前拍：同一只怪两次进战可能换一句（魔兽就这样），
        // 而同一次回放（金轨迹）逐位相同——随机源必须来自 tick，不能来自进程内随机数。
        var pickedSayId = MonsterContent.ResolveSayId(
            monster.TemplateId, sayId, monster.EntityId * 0x9E3779B97F4A7C15UL ^ (ulong)_tick);
        if (pickedSayId <= 0)
        {
            MonsterSayPointerMisses++;
            return;
        }

        var audience = AudienceSeeingMonster(monster);
        if (audience.Length == 0)
        {
            // 没人在看：这条不补发（与施法条同一口径——看不见的人不需要收回他看不见的东西）。
            // 但**不算喊过**：等有人进视野时那一拍还在战斗里，会由下一条边沿补上吗？不会——
            // 所以这里把标记留在 true 是刻意的：一句"进战时没人看见就不喊"比"晚三秒凭空冒出一句"更贴近 WoW。
            return;
        }

        _events.Add((audience, new Horizon.Game.Message.Sync.EventPacket
        {
            ServerTick = _tick,
            Events = new[]
            {
                new Horizon.Game.Message.Sync.SyncEvent
                {
                    Kind = Horizon.Game.Message.Sync.SyncEventKind.MonsterSay,
                    SourceEntityId = monster.EntityId,
                    IntValue = sayId,
                },
            },
        }));
        MonsterSayEvents++;
    }

    /// <summary>累计真正下发的进战喊话条数（摘要里那一格）。</summary>
    public int MonsterSayEvents { get; private set; }

    /// <summary>累计真正下发的临终台词条数（没配那一组时恒为 0，与"配了没发"分开数）。</summary>
    public int MonsterDeathSayEvents { get; private set; }

    /// <summary>累计真正下发的应援台词条数（被同族叫起来的那一拍；没配组 3 时恒为 0）。</summary>
    public int MonsterAssistSayEvents { get; private set; }

    /// <summary>因"够不到目标超过 5 秒"而脱战的次数（AC <c>EVADE_REASON_NO_PATH</c>）。</summary>
    public int MonsterNoPathEvades { get; private set; }

    /// <summary>
    /// 应援台词：被求援叫起来时发一句（组 <c>MonsterContent.AssistSayGroup</c>）。
    /// <para>与进战/临终同一条事件通路（只发号）。**AC 那句是呼喊者喊的**，而内核只回传"我被叫起来了"，
    /// 所以这里落在被叫者身上——名字如实写成"应援"，不假装是呼喊者。</para>
    /// </summary>
    private void EmitMonsterAssistSay(DsMonster monster)
    {
        var sayId = MonsterContent.ResolveAssistSayId(
            monster.TemplateId, monster.EntityId * 0x9E3779B97F4A7C15UL ^ (ulong)_tick);
        if (sayId <= 0) return;

        var audience = AudienceSeeingMonster(monster);
        if (audience.Length == 0) return;

        _events.Add((audience, new Horizon.Game.Message.Sync.EventPacket
        {
            ServerTick = _tick,
            Events = new[]
            {
                new Horizon.Game.Message.Sync.SyncEvent
                {
                    Kind = Horizon.Game.Message.Sync.SyncEventKind.MonsterSay,
                    SourceEntityId = monster.EntityId,
                    IntValue = sayId,
                },
            },
        }));
        MonsterAssistSayEvents++;
    }

    /// <summary>
    /// 临终台词：与进战喊话同一条事件通路（只发号），只挑落在 <see cref="MonsterContent.DeathSayGroup"/> 那一组的句子。
    /// <para>盐取 `实体号 × 黄金比例常数 ^ tick`——同一只怪两次死亡可能换一句，同一次回放逐位相同
    /// （与 <see cref="SyncMonsterSay"/> 同一口径；随机源必须来自 tick）。</para>
    /// </summary>
    private void EmitMonsterDeathSay(DsMonster monster)
    {
        var sayId = MonsterContent.ResolveDeathSayId(
            monster.TemplateId, monster.EntityId * 0x9E3779B97F4A7C15UL ^ (ulong)_tick);
        if (sayId <= 0) return;

        var audience = AudienceSeeingMonster(monster);
        if (audience.Length == 0) return;

        _events.Add((audience, new Horizon.Game.Message.Sync.EventPacket
        {
            ServerTick = _tick,
            Events = new[]
            {
                new Horizon.Game.Message.Sync.SyncEvent
                {
                    Kind = Horizon.Game.Message.Sync.SyncEventKind.MonsterSay,
                    SourceEntityId = monster.EntityId,
                    IntValue = sayId,
                },
            },
        }));
        MonsterDeathSayEvents++;
    }

    /// <summary>配了 <c>aggro_say_id</c> 而内容包里没有那一号的次数（正常应为 0；装配期 problem 抓不到的漂移走这里）。</summary>
    public int MonsterSayPointerMisses { get; private set; }

    /// <summary>
    /// 起手事件：只带"条子长什么样"，进度由客户端按本地时钟插值（见 <c>MonsterCastStart</c> 的理由）。
    /// <para>瞬发（castMs=0）**不发**：一根 0 秒的条子只会闪一下噪声，而 WoW 的瞬发本来就没有条。</para>
    /// </summary>
    private void EmitMonsterCastStart(DsMonster monster, Horizon.Game.Sim.Monsters.MonsterCastingSelf casting)
    {
        var seconds = casting.CastTotalSeconds;
        if (seconds <= 0f) return;

        var audience = AudienceSeeingMonster(monster);
        if (audience.Length == 0) return;

        var payload = new Horizon.Game.Message.Sync.MonsterCastPayload();
        var skillId = 0;
        MonsterContent.TryGetConfig(monster.TemplateId, out var config);
        var mounts = config?.Skills;
        var mountIndex = casting.ActiveMountIndex;
        if (mounts != null && mountIndex >= 0 && mountIndex < mounts.Length)
        {
            var mount = mounts[mountIndex];
            skillId = mount.SkillId;
            payload.SchoolMask = mount.SchoolMask;
            payload.Interruptible = mount.Interruptible;
            payload.AllowMovement = mount.AllowMovement;
            payload.TargetCount = mount.TargetCount;
            payload.AoeRadius = mount.AoeRadius;
        }
        else
        {
            // 内容里查不到这条挂载：条子照样要画（时长与目标都在运行态里），但系别/可打断只能给默认值。
            // 这条分支不该常见，出现即说明运行态与内容包对不上（挂载表被换短了），故计数显形。
            MonsterCastMountLookupMisses++;
        }

        var bytes = MemoryPack.MemoryPackSerializer.Serialize(payload);
        _events.Add((audience, new Horizon.Game.Message.Sync.EventPacket
        {
            ServerTick = _tick,
            Events = new[]
            {
                new Horizon.Game.Message.Sync.SyncEvent
                {
                    Kind = Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStart,
                    SourceEntityId = monster.EntityId,
                    TargetEntityId = casting.CastTargetId,
                    IntValue = skillId,
                    FloatValue = seconds,
                    Payload = bytes,
                },
            },
        }));

        MonsterCastStartEvents++;
    }

    /// <summary>结束事件：受众仍按"现在看得见这只怪"算（跑远了看不见的人不需要收回他看不见的条子）。</summary>
    private void EmitMonsterCastStop(DsMonster monster, int reason)
    {
        var audience = AudienceSeeingMonster(monster);
        if (audience.Length == 0) return;

        _events.Add((audience, new Horizon.Game.Message.Sync.EventPacket
        {
            ServerTick = _tick,
            Events = new[]
            {
                new Horizon.Game.Message.Sync.SyncEvent
                {
                    Kind = Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStop,
                    SourceEntityId = monster.EntityId,
                    TargetEntityId = 0UL,
                    IntValue = reason,
                    FloatValue = 0f,
                    Payload = null,
                },
            },
        }));

        MonsterCastStopEvents++;
    }

    /// <summary>
    /// 当前看得见这只怪的会话（施法条这类"人人该看到"的表现用它）。
    /// <para>与快照分组同源：同一个 <see cref="ChunkKey"/> 口径，所以不会出现
    /// "快照里看得见它、施法条却发给看不见它的人"这种两套可见性。AOI 关闭时=全部会话。</para>
    /// </summary>
    private long[] AudienceSeeingMonster(DsMonster monster)
    {
        var list = new List<long>(_playersBySession.Count);
        if (!_options.EnableAoiFilter)
        {
            foreach (var sid in _playersBySession.Keys) list.Add(sid);
            list.Sort();
            return list.ToArray();
        }

        var key = ChunkKey(monster.Ai.X, monster.Ai.Y, monster.Ai.Z);
        foreach (var kv in _playersBySession)
        {
            var subs = _aoi.GetSubscriptions(kv.Key) as ISet<ulong>;
            if (subs != null && subs.Contains(key)) list.Add(kv.Key);
        }
        list.Sort();
        return list.ToArray();
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

        ApplyMonsterHitToPlayer(sessionId, player, monster, targetEntityId, resolution, 0, 1);
    }

    /// <summary>
    /// 怪物命中玩家的唯一尾部：扣血 → 广播伤害 → 死亡处理。
    /// <para>普攻与技能都调它。<b>这条收敛是刻意的</b>：两条路各自写一遍的话，
    /// 死亡惩罚/复活排定/跨层事件迟早会只在其中一条上更新（本项目有过"同一管线的两个容差"同类教训）。</para>
    /// </summary>
    private void ApplyMonsterHitToPlayer(
        long sessionId,
        DsPlayer player,
        DsMonster monster,
        ulong targetEntityId,
        in CombatResolution resolution,
        int skillId,
        int damageType)
    {
        player.Hp = resolution.TargetRemainingHp;

        _events.Add((new[] { sessionId }, new DamagePacket
        {
            AttackerId = monster.EntityId,
            TargetId = targetEntityId,
            DamageAmount = resolution.Damage,
            DamageType = (byte)damageType,
            IsCritical = resolution.IsCritical,
            RemainingHp = resolution.TargetRemainingHp,
            MaxHp = resolution.TargetMaxHp,
            SkillId = skillId,
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
        MonsterAiConfig cfg = MonsterAiConfig.Default;
        for (var i = 0; i < _spawnPoints.Count; i++)
        {
            if (_spawnPoints[i].SpawnPointId != spawnPointId) continue;
            cfg = _spawnPoints[i].ToAiConfig();
            break;
        }
        // 视线门那一格（v1 的索敌问 `config.NavGrid`）：没装图时保持 invalid ⇒ 行为零变化。
        cfg.NavGrid = _navGrid;
        return cfg;
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
                    // 累计经验：网关在 KillXp 落库后回合推下来的权威值（-1 = 未知，保持 0 不下发）。
                    Exp = player.Exp > 0 ? player.Exp : 0,
                    // 死亡位由 DS 权威血量决定：远程客户端据此立刻看到倒下/复活（v13）。
                    // 控制状态位与死亡位同一个写口（`PlayerStateBitsOf`）：分成两处写迟早分叉。
                    StateBits = PlayerStateBitsOf(player),
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
                ? MonsterAiConfig.ChaseSpeed(in cfg)
                : (monster.Ai.State == MonsterAiState.Patrol ? cfg.MoveSpeed : 0f);

            var template = ResolveMonsterTemplate(monster);
            var npcIdentity = ResolveNpcIdentityDelta(monster);

            deltas.Add(new EntityDelta
            {
                EntityId = monster.EntityId,
                Kind = isSpawn ? EntityDeltaKind.Spawn : EntityDeltaKind.Update,
                Identity = isSpawn ? BuildMonsterIdentity(monster, template) : null,
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
                    // 尸体的"已经死了"必须自己带在状态位上：以前怪一死就直接 Despawn，
                    // 客户端只需要"Actor 没了"这一种信息；现在尸体会继续出现在快照里，
                    // 只给 Health=0 的话客户端分不清"残血"和"尸体"，名牌、动画、可选中都全错。
                    StateBits = MonsterStateBitsOf(monster, _tick),
                    // 头顶信息卡与血条读同一个生效等级：以前这里读代码目录的模板等级，
                    // 于是"刷怪点覆盖 25 级"的怪会**顶着 12 级名字**挨 25 级的打。
                    Level = MonsterLevelOf(monster),
                    // 身份增量：姿态随作息时段变、服务位图供客户端画"能干什么"，两者都每快照下发。
                    // 非内容 NPC 给 −1（未下发），客户端保留自己的旧口径。
                    NpcStandState = npcIdentity.StandState,
                    NpcServiceFlags = npcIdentity.ServiceFlags,
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

        // 可见性退出补发的会话级 Despawn 组（每项受众只有 1 个会话）。
        // 复用 DsSnapshotGroup 通道：编码器本就按组各发一份字节、各带自己的受众，
        // 所以"只发给看过它的那几个会话"这种定向下发不需要动 DsDownlinkEncoder 与宿主。
        var exitDespawns = new List<DsSnapshotGroup>();
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

            // 6) 可见性退出差分：组内会话看到的实体集合相同，所以按"上次已知 − 本次可见"逐会话补 Despawn。
            //    不做这一步，实体只是从 deltas 里消失，客户端永远不知道它走了。
            if (_options.EnableAoiFilter)
            {
                var visibleIds = new HashSet<ulong>(deltas.Count);
                for (var d = 0; d < deltas.Count; d++) visibleIds.Add(deltas[d].EntityId);

                for (var a = 0; a < audience.Length; a++)
                {
                    var sid = audience[a];
                    if (!_aoiKnownEntityIds.TryGetValue(sid, out var known))
                    {
                        _aoiKnownEntityIds[sid] = new HashSet<ulong>(visibleIds);
                        continue;
                    }

                    // 自己的实体永不 Despawn：订阅中心就是本会话所在 chunk，理论上不会不可见，
                    // 但 AOI 抖动/垂直层切换一旦漏掉自己，客户端会把自己的 Actor 拆掉（人消失）。
                    var selfEntityId = _playersBySession[audience[a]].EntityId;
                    List<ulong>? leaving = null;
                    foreach (var entityId in known)
                    {
                        if (entityId == selfEntityId || visibleIds.Contains(entityId)) continue;
                        leaving ??= new List<ulong>();
                        leaving.Add(entityId);
                    }

                    known.Clear();
                    foreach (var entityId in visibleIds) known.Add(entityId);

                    if (leaving is null) continue;

                    leaving.Sort();
                    exitDespawns.Add(new DsSnapshotGroup
                    {
                        Snapshot = BuildDespawnPacket(leaving),
                        AudienceSessionIds = new[] { sid },
                    });
                }
            }
        }

        // 定向 Despawn 并入同一份有序输出（按最小 sessionId 升序，与共享组同一条确定性规则）。
        result.AddRange(exitDespawns);

        // 5) 按最小 sessionId 升序输出（确定性）。
        result.Sort((a, b) =>
        {
            var am = a.AudienceSessionIds.Length > 0 ? a.AudienceSessionIds[0] : long.MaxValue;
            var bm = b.AudienceSessionIds.Length > 0 ? b.AudienceSessionIds[0] : long.MaxValue;
            return am.CompareTo(bm);
        });
        return result;
    }

    /// <summary>构造只含 Despawn delta 的生命周期包（<c>BaselineTick = 0</c>，客户端逐 delta 应用）。</summary>
    private SnapshotPacket BuildDespawnPacket(IReadOnlyCollection<ulong> entityIds)
    {
        var deltas = new EntityDelta[entityIds.Count];
        var index = 0;
        foreach (var entityId in entityIds)
        {
            deltas[index++] = new EntityDelta
            {
                EntityId = entityId,
                Kind = EntityDeltaKind.Despawn,
            };
        }

        return new SnapshotPacket
        {
            ProtocolVersion = SyncProtocolVersion.Current,
            ServerTick = _tick,
            BaselineTick = 0,
            Deltas = deltas,
        };
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

            if (_options.EnableAoiFilter)
            {
                // 首包就是"该会话此刻的可见全集"，用它初始化已知集合：
                // 不初始化的话，本会话第一次参与分组差分时会被当成"无历史已知"而漏掉
                // 随后真正离开视野的那批实体。
                var knownIds = new HashSet<ulong>(deltas.Count);
                for (var d = 0; d < deltas.Count; d++) knownIds.Add(deltas[d].EntityId);
                _aoiKnownEntityIds[sid] = knownIds;
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
                Exp = player.Exp > 0 ? player.Exp : 0,
                // 死亡位由 DS 权威血量决定：远程客户端据此立刻看到倒下/复活（v13）。
                StateBits = PlayerStateBitsOf(player),
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
    /// <summary>
    /// 脚本实体下发给客户端的 <c>EntityType</c>：NPC 给 Npc，其余（怪物/玩法型事件）给 Monster。
    /// </summary>
    private static int ScriptedEntityTypeOf(DsMonster monster) =>
        monster.ScriptedKind == (int)Horizon.Game.Message.Network.ScriptedKind.Npc
            ? EntityTypeNpc
            : EntityTypeMonster;

    /// <summary>
    /// 取实体显示信息：怪物/玩法型事件查怪物目录，NPC 查 NPC 目录。
    /// <para>NPC 模板不含战斗数值，等级固定给 1——客户端头顶卡按"名字 + 等级"渲染，
    /// 给 0 会显示成"等级 0"这种明显不对的观感。</para>
    /// </summary>
    internal static MonsterTemplateInfo ResolveMonsterTemplate(DsMonster monster)
    {
        if (monster.ScriptedKind == (int)Horizon.Game.Message.Network.ScriptedKind.Npc)
        {
            return new MonsterTemplateInfo(monster.TemplateId, NpcTemplateCatalog.Get(monster.TemplateId).Name, 1, "npc", 0, false);
        }

        // 敌对型 NPC 以 npc_id 当模板 id 生成，怪物目录里没有这个号：
        // 不退回 NPC 目录的话，玩家会看到 "Monster_<id> / Lv.1"，而它的真实名字与等级都在 NPC 内容里。
        if (!MonsterTemplateCatalog.TryGetRegistered(monster.TemplateId, out _)
            && Horizon.Game.Sim.Npcs.NpcContent.TryGetConfig(monster.TemplateId, out var npc) && npc != null)
        {
            return new MonsterTemplateInfo(npc.NpcId, npc.Name, npc.LevelBase > 0 ? npc.LevelBase : 1,
                npc.ModelKey, npc.WuxingDomain, npc.Rank >= (byte)MonsterRank.WorldBoss);
        }

        return MonsterTemplateCatalog.Get(monster.TemplateId);
    }

    /// <summary>
    /// 怪物 Identity（spawn-only）的唯一构造点：显示名 + 外观四件套。
    /// <para>快照增量与晚加入补发两处必须调同一个函数——外观字段一旦只在一处填，
    /// 表现就是"先看到的怪有模型、后加入的人看到的全是占位人形"，而两条通路各自都有测试。</para>
    /// <para>外观取自装配内容（编辑器发布的那份）；没装配时退回代码目录的 model_key，
    /// 缩放 1.0、姿态 -1（= 未下发，客户端保留自己的默认）。</para>
    /// </summary>
    private static NetworkIdentityAuthComponent BuildMonsterIdentity(DsMonster monster, MonsterTemplateInfo template)
    {
        var identity = new NetworkIdentityAuthComponent
        {
            NetworkId = monster.EntityId,
            EntityType = ScriptedEntityTypeOf(monster),
            OwnerId = 0UL, // NPC/怪物：无归属玩家
            Nickname = template.Name,
            TemplateId = template.TemplateId,
            ModelKey = template.ModelKey ?? string.Empty,
            ModelScale = 1f,
            // 姿态与缩放必须能表达"未下发"：0 是合法姿态（站立），拿 0 当哨兵会逼所有存量怪显式配一遍
            StandState = -1,
            Rank = 0,
        };

        if (Horizon.Game.Sim.Monsters.MonsterContent.TryGetConfig(monster.TemplateId, out var config) && config != null)
        {
            identity.ModelKey = string.IsNullOrEmpty(config.ModelKey) ? (template.ModelKey ?? string.Empty) : config.ModelKey;
            identity.ModelScale = config.ModelScale > 0f ? config.ModelScale : 1f;
            identity.StandState = config.StandState;
            identity.Rank = (int)config.Rank;
        }
        else if (Horizon.Game.Sim.Npcs.NpcContent.TryGetConfig(monster.TemplateId, out var npcConfig) && npcConfig != null)
        {
            // 内容 NPC：外观/档位/头衔都来自**NPC 目录**。
            // 少了这一支，NPC 会以"无头衔、scale=1、rank=0（普通）"出场——
            // 编辑器里配的精英档与体型缩放全部静默失效，而头顶名字却是对的（只错一半最难发现）。
            identity.ModelKey = string.IsNullOrEmpty(npcConfig.ModelKey) ? (template.ModelKey ?? string.Empty) : npcConfig.ModelKey;
            identity.ModelScale = npcConfig.Scale > 0f ? npcConfig.Scale : 1f;
            identity.Rank = npcConfig.Rank;
            identity.Title = npcConfig.Title ?? string.Empty;
        }
        else if (template.IsWorldBoss)
        {
            identity.Rank = (int)Horizon.Game.Sim.Monsters.MonsterRank.WorldBoss;
        }

        // 晚进场的人只有这一份补发 Spawn 可读：起手是瞬时事件，只发给当时看得见这只怪的人。
        // 少了这一段，新人眼里正在施法的怪就是一根空条，直到这只怪下一次起手才"恢复正常"。
        var runtime = monster.Runtime;
        var casting = runtime?.Casting ?? default;
        if (runtime != null && casting.IsCasting && casting.CastTotalSeconds > 0f)
        {
            identity.CastTotalSeconds = casting.CastTotalSeconds;
            identity.CastElapsedSeconds = casting.CastElapsedSeconds;
            // 挂载解析必须与结算口同一个出口：少了 NPC 回退，正在起手的 NPC 给新人的是一根
            // "有条无技能"的空条（时长/进度对、技能 id 恒 0），客户端描边与打断提示全丢。
            var mounts = ResolveCastMounts(monster.TemplateId);
            var mountIndex = casting.ActiveMountIndex;
            if (mounts != null && mountIndex >= 0 && mountIndex < mounts.Length)
            {
                var mount = mounts[mountIndex];
                identity.CastSkillId = mount.SkillId;
                identity.CastSchoolMask = mount.SchoolMask;
                identity.CastInterruptible = mount.Interruptible ? 1 : 0;
            }
        }
        return identity;
    }

    private EntityDelta BuildMonsterDelta(DsMonster monster, bool forceSpawn = false)
    {
        var cfg = FindConfigForSpawnPoint(monster.SpawnPointId);
        var isSpawn = forceSpawn || !monster.SpawnedToClients;

        var speed = monster.Ai.State == MonsterAiState.Chase || monster.Ai.State == MonsterAiState.Leash
            ? MonsterAiConfig.ChaseSpeed(in cfg)
            : (monster.Ai.State == MonsterAiState.Patrol ? cfg.MoveSpeed : 0f);

        var template = ResolveMonsterTemplate(monster);

        var delta = new EntityDelta
        {
            EntityId = monster.EntityId,
            Kind = isSpawn ? EntityDeltaKind.Spawn : EntityDeltaKind.Update,
            Identity = isSpawn ? BuildMonsterIdentity(monster, template) : null,
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
                // 与共享路径同一个状态位：本方法是 **AOI 分组路径**和**晚加入者补发**唯一的写方，
                // 漏在这里等于开着 AOI 的玩家永远看不到"这是一具尸体"。
                StateBits = MonsterStateBitsOf(monster, _tick),
                // 等级口径必须和共享路径一致，否则"刷怪点覆盖等级"只在关掉 AOI 时才生效：
                // 这里以前读代码目录的 template.Level，而 2177 那份已改成生效等级——
                // 同一个产物的两个写方漏改一个，是这类"改了没反应"最省钱的查法。
                Level = MonsterLevelOf(monster),
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

    // ==================== 脚本化世界生成（设计方案 §3/§6） ====================
    //
    // 纪律：**DS 不做授权判定**。令牌由 Orleans Grain 裁决后 HMAC 签发，DS 只自证四件事——
    // 签名有效、未过期、Nonce 未消费过、目标 shard 确实是本会话。任何一项不过一律丢弃并计数。
    // 这样 60Hz tick 内不需要任何跨进程调用，也不会出现"DS 自己觉得可以就可以"。

    /// <summary>脚本生成点序号（单调递增；取负后进入与静态刷怪点不相交的 ID 域）。</summary>
    private long _scriptSerial;

    /// <summary>已验签、等待本 tick 消费的指令（仅在 tick 线程读写）。</summary>
    private readonly List<Horizon.Game.Message.Network.ScriptGrantToken> _pendingScripts = new();

    /// <summary>排序用的临时表（避免对 _pendingScripts 边遍历边改）。</summary>
    private readonly List<Horizon.Game.Message.Network.ScriptGrantToken> _scriptScratch = new();

    /// <summary>已消费 Nonce 的有界窗口，防重放。</summary>
    private readonly HashSet<long> _consumedNonces = new();

    /// <summary>Nonce 消费顺序（用于把去重窗口限制在有限长度内）。</summary>
    private readonly Queue<long> _consumedNonceOrder = new();

    /// <summary>去重窗口上限。令牌 TTL 只有数秒，4096 条足够覆盖突发窗口且内存可忽略。</summary>
    private const int MaxConsumedNonces = 4096;

    /// <summary>脚本生成点 → 到期 tick；<see cref="long.MaxValue"/> 表示永久。</summary>
    private readonly Dictionary<int, long> _scriptPointExpiryTick = new();

    /// <summary>脚本生成点 → 触发者角色 ID（供 <c>/despawn mine</c> 与审计）。</summary>
    private readonly Dictionary<int, long> _scriptPointOwner = new();

    /// <summary>脚本生成点 → 生成类别（<see cref="Horizon.Game.Message.Network.ScriptedKind"/> 的整数值）。</summary>
    private readonly Dictionary<int, int> _scriptPointKind = new();

    /// <summary>待回执的脚本指令（实体 ID 要等刷怪调度真正生成后才知道）。</summary>
    private struct ScriptReceipt
    {
        public long OperatorId;
        public long RequestId;
        public long Nonce;

        /// <summary>本条指令对应的脚本生成点；0 = 不适用（如触发区域回执）。</summary>
        public int SpawnPointId;

        /// <summary>已确定的实体 ID（清除类指令与区域回执直接填这里）。</summary>
        public List<ulong>? Ids;

        /// <summary>true = 清除类回执。</summary>
        public bool Removed;
    }

    private readonly List<ScriptReceipt> _scriptReceipts = new();
    private readonly List<ScriptReceipt> _receiptScratch = new();

    /// <summary>累计回执条数（诊断）。</summary>
    public long ScriptReceiptCount { get; private set; }

    /// <summary>累计被 DS 接受的脚本指令数。</summary>
    public long ScriptedAcceptedCount { get; private set; }

    /// <summary>累计被 DS 拒绝的脚本指令数（验签失败/过期/重放/跨 shard/未开启）。</summary>
    public long ScriptedRejectedCount { get; private set; }

    /// <summary>当前存活的脚本生成点数。</summary>
    public int ScriptedSpawnPointCount => _scriptPointOwner.Count;

    /// <summary>最近一次拒绝原因（诊断用，只保留最后一条）。</summary>
    public string LastScriptRejectReason { get; private set; } = string.Empty;

    /// <summary>
    /// 本 DS 是否承载该 shard（口径与向网关注册的 <see cref="DsWorldSessionOptions.HostedShardIds"/> 一致）。
    /// 未声明集合时回退为"只认 ShardId 一片"，缺省即最小权限。
    /// </summary>
    private bool HostsShard(long shardId)
    {
        var hosted = _options.HostedShardIds;
        if (hosted is null || hosted.Length == 0) return shardId == _options.ShardId;
        for (var i = 0; i < hosted.Length; i++)
        {
            if (hosted[i] == shardId) return true;
        }

        return false;
    }

    /// <summary>
    /// 接纳一条网关下发的脚本指令。<b>必须在 tick 线程调用</b>（世界非线程安全）：
    /// 宿主应把 <c>DsUplinkClient.ScriptedSpawnReceived</c> 的投递放到队列里，再于 tick 前调用本方法。
    /// </summary>
    /// <param name="token">待执行令牌。</param>
    /// <returns>null 表示已入队；否则返回拒绝原因字符串。</returns>
    public string? EnqueueScriptedCommand(Horizon.Game.Message.Network.ScriptGrantToken? token)
    {
        if (!_options.EnableScriptedSpawn)
            return Reject("scripted-disabled");

        if (token is null)
            return Reject("null-token");

        if (!HostsShard(token.TargetShardId))
            return Reject($"wrong-shard:{token.TargetShardId}");

        var key = _options.ScriptedSigningKey;
        if (key is null || key.Length == 0)
            return Reject("missing-signing-key");

        if (!Horizon.Game.Message.Security.ScriptGrantTokenSigner.Validate(
                token, key, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), out var validateReason))
            return Reject(validateReason);

        if (!_consumedNonces.Add(token.Nonce))
            return Reject("replayed");

        _consumedNonceOrder.Enqueue(token.Nonce);
        while (_consumedNonceOrder.Count > MaxConsumedNonces)
            _consumedNonces.Remove(_consumedNonceOrder.Dequeue());

        _pendingScripts.Add(token);
        ScriptedAcceptedCount++;
        return null;

        string Reject(string reason)
        {
            ScriptedRejectedCount++;
            LastScriptRejectReason = reason;
            return reason;
        }
    }

    /// <summary>
    /// 在模拟之前消费脚本指令。<b>按 Nonce 升序</b>处理：到达顺序取决于网络，
    /// 而黄金值回放要求逐 tick 结果只由确定性输入决定（与刷怪点按 SpawnPointId 排序同一条纪律）。
    /// </summary>
    private void ConsumeScriptCommands()
    {
        if (_pendingScripts.Count == 0) return;

        _scriptScratch.Clear();
        for (var i = 0; i < _pendingScripts.Count; i++) _scriptScratch.Add(_pendingScripts[i]);
        _pendingScripts.Clear();
        _scriptScratch.Sort((a, b) => a.Nonce.CompareTo(b.Nonce));

        for (var i = 0; i < _scriptScratch.Count; i++)
            ApplyScriptCommand(_scriptScratch[i]);
    }

    private void ApplyScriptCommand(Horizon.Game.Message.Network.ScriptGrantToken token)
    {
        switch (token.Op)
        {
            case Horizon.Game.Message.Network.ScriptOp.Spawn
                when token.Kind == Horizon.Game.Message.Network.ScriptedKind.TriggerZone:
                RegisterTriggerZone(token);
                break;

            case Horizon.Game.Message.Network.ScriptOp.Spawn
                when token.Kind == Horizon.Game.Message.Network.ScriptedKind.Event:
                FireWorldEvent(token);
                break;

            case Horizon.Game.Message.Network.ScriptOp.Spawn:
                // 怪物 / NPC 共用一条生成通路，类别只影响下发类型与显示目录。
                RegisterScriptedPoint(token);
                break;

            case Horizon.Game.Message.Network.ScriptOp.Despawn:
                EmitRemovalReceipt(token, RemoveScriptedEntity(token.TargetEntityId));
                break;

            case Horizon.Game.Message.Network.ScriptOp.DespawnMine:
                var mine = RemoveScriptedPointsOwnedBy(token.OperatorCharacterId);
                mine.AddRange(RemoveTriggerZonesOwnedBy(token.OperatorCharacterId));
                EmitRemovalReceipt(token, mine);
                break;
        }
    }

    /// <summary>
    /// 把一个脚本生成请求登记为**运行期刷怪点**（负 <c>SpawnPointId</c> 域）。
    /// <para>
    /// 复用既有 <see cref="SpawnDirectorCore"/> 槽位池而非另造一条生成通路：AI、重生、快照、
    /// AOI、晚加入补发全部自动生效，且实体 ID 由 <c>(spawnPointId &lt;&lt; 32) | (slot+1)</c>
    /// 确定性给出，负数高位与所有正值静态点天然不相交。
    /// </para>
    /// </summary>
    private void RegisterScriptedPoint(Horizon.Game.Message.Network.ScriptGrantToken token)
    {
        var pointId = CreateScriptedPoint(token.Kind, token.TemplateId, token.X, token.Y, token.Z,
            token.Count, token.TtlSeconds, token.OperatorCharacterId);
        if (pointId == 0) return;

        // 实体此刻还不存在：刷怪调度在本 tick 稍后才跑，ID 要在那之后收集（见 TickScriptReceipts）。
        _scriptReceipts.Add(new ScriptReceipt
        {
            OperatorId = token.OperatorCharacterId,
            RequestId = token.RequestId,
            Nonce = token.Nonce,
            SpawnPointId = pointId,
        });
    }

    /// <summary>
    /// 建立一个脚本生成点并返回其 ID（0 = 未建立）。触发区域召唤怪群走同一入口，
    /// 因此"指令直接生成"与"踩进去才生成"共享完全相同的生成/AI/快照/到期语义。
    /// </summary>
    private int CreateScriptedPoint(
        Horizon.Game.Message.Network.ScriptedKind kind,
        int templateId,
        float x, float y, float z,
        int count,
        int ttlSeconds,
        long ownerCharacterId,
        float wanderRadiusOverride = -1f,
        float moveSpeedOverride = -1f,
        float runSpeedOverride = -1f,
        int routeIdOverride = 0,
        float respawnSecondsOverride = -1f,
        float aggroRadiusOverride = -1f,
        float leashRadiusOverride = -1f,
        float yawOverride = 0f,
        int levelOverride = 0)
    {
        var serial = ++_scriptSerial;
        if (serial > int.MaxValue) return 0; // 理论上到不了；到了也不再分配，避免 ID 回绕串体

        var spawnPointId = -(int)serial;
        var maxAlive = Math.Clamp(count, 1, 10);

        if (routeIdOverride > 0) _scriptPointRouteId[spawnPointId] = routeIdOverride;

        var cfg = SpawnPointConfig.Default;
        cfg.SpawnPointId = spawnPointId;
        cfg.ShardId = _options.ShardId;
        cfg.X = x;
        cfg.Y = y;
        cfg.Z = z;
        // 初始朝向：刷点表的 yaw 由调用方算好（含"随机/面向玩家"两档）后从这里进来。
        cfg.Yaw = yawOverride;
        if (levelOverride > 0) _scriptPointLevelOverride[spawnPointId] = levelOverride;
        cfg.MonsterTemplateId = templateId;
        cfg.MaxAlive = maxAlive;
        cfg.SlotSpacing = 3f;
        // 限时生成物不该"死后原地满血复活"：那会让一条 TTL 指令变成永久刷怪机。
        // 永久生成点（TTL=0）则沿用默认重生节奏，与静态刷怪点行为一致。
        cfg.RespawnDelaySeconds = ttlSeconds > 0 ? float.MaxValue : SpawnPointConfig.Default.RespawnDelaySeconds;

        // 内容 NPC 的刷新时间由编辑器配（<c>NpcSpawnPoint.respawn_seconds</c>）：
        // 不接进来的话，运营把"30 秒刷新"改成 5 分钟，游戏里依旧是默认节奏，而两边都不报错。
        // 小于 0 才是"没配"（0 在表里表示"用全局默认曲线"，与 CreateScriptedPoint 的默认同义）。
        if (respawnSecondsOverride >= 0f && ttlSeconds <= 0)
            cfg.RespawnDelaySeconds = respawnSecondsOverride > 0f
                ? respawnSecondsOverride
                : SpawnPointConfig.Default.RespawnDelaySeconds;

        // 索敌/脱战半径的覆盖（NPC 的 AI 能力位 → 敌对型的战斗表现）。负数 = 不覆盖。
        if (aggroRadiusOverride >= 0f) cfg.AggroRadius = aggroRadiusOverride;
        if (leashRadiusOverride >= 0f) cfg.LeashRadius = leashRadiusOverride;

        // 脚本生成点**不需要**在这里补盖内容血量：生成时的初始血与战斗层的上限都走
        // `BuildAiConfigFromContent`（按这只怪自己的生效等级算），那已经是唯一一处公式所在地。
        // 以前这里盖过一次（切片 21），是因为那两个消费者还在各读点位那份；
        // 切片 27 的闸门数"宿主里按公式算数值的地方"只许出现一次，正是为了不让这种"顺手再算一遍"长回来。

        // NPC 不是"不会动的怪"，而是语义上不同的东西：不给仇恨/脱战/游荡，站定即可。
        // 战斗与 AI 通路本身照旧复用（见 ScriptedEntityTypeOf 的注释）。
        if (kind == Horizon.Game.Message.Network.ScriptedKind.Npc)
        {
            cfg.AggroRadius = 0f;
            cfg.LeashRadius = 0f;
            cfg.WanderRadius = 0f;
            cfg.MoveSpeed = 0f;
            cfg.MaxAlive = 1;      // 一条指令一个 NPC
            cfg.AttackPower = 0;
            maxAlive = 1;
        }

        // 内容驱动的移动覆盖（**对敌对型同样生效**）：会踱步的档给一圈小范围随机走动与步行速度；
        // 不给（负数）时沿用怪物默认——旧 GM 调用点逐字不变。
        // 敌对型不接这一条的话，站桩档的山贼也会按怪物默认 6 米乱走，"档位"就只改了名字。
        if (wanderRadiusOverride >= 0f) cfg.WanderRadius = wanderRadiusOverride;
        if (moveSpeedOverride >= 0f) cfg.MoveSpeed = moveSpeedOverride;
        // 追击/回位速度（NPC 的 speed_run）。是否真的用它还要过 MonsterAiCore.ChaseSpeed 那道"可动性"判据。
        if (runSpeedOverride >= 0f) cfg.RunSpeed = runSpeedOverride;

        _spawnPoints.Add(cfg);
        // 保持"按 SpawnPointId 升序"不变量：它决定生成顺序 → 决定实体 ID 与快照顺序。
        _spawnPoints.Sort((a, b) => a.SpawnPointId.CompareTo(b.SpawnPointId));
        _spawnSlots[spawnPointId] = SpawnDirectorCore.CreateSlots(maxAlive);

        _scriptPointOwner[spawnPointId] = ownerCharacterId;
        _scriptPointKind[spawnPointId] = (int)kind;
        _scriptPointExpiryTick[spawnPointId] = ttlSeconds > 0
            ? _tick + (long)ttlSeconds * Math.Max(1, _options.TicksPerSecond)
            : long.MaxValue;

        return spawnPointId;
    }

    /// <summary>
    /// 在刷怪调度之后发回执：此时脚本生成点里的实体 ID 已确定，回给操作者的才是真值而不是预估。
    /// <para>
    /// 受众<b>只有操作者自己</b>——指令回执不是世界状态，没必要广播给同屏其他人。
    /// sessionId 在本架构中等于 characterId，因此可直接定向。
    /// </para>
    /// </summary>
    private void TickScriptReceipts()
    {
        if (_scriptReceipts.Count == 0) return;

        _receiptScratch.Clear();
        for (var i = 0; i < _scriptReceipts.Count; i++) _receiptScratch.Add(_scriptReceipts[i]);
        _scriptReceipts.Clear();
        _receiptScratch.Sort((a, b) => a.Nonce.CompareTo(b.Nonce));

        for (var i = 0; i < _receiptScratch.Count; i++)
        {
            var receipt = _receiptScratch[i];
            var ids = receipt.Ids;
            if (ids == null)
            {
                // 生成类回执：从本 tick 实际生成的实体里取，容量裁剪掉的不会谎报。
                ids = new List<ulong>();
                foreach (var kv in _monstersById)
                {
                    // 只报本 tick 真的活着的新实体：尸体窗口里的上一代同 ID 尸体
                    // 若一起算进"这次生成了谁"，回执会把没生成的东西谎报成生成了。
                    if (kv.Value.Ai.Hp > 0 && kv.Value.SpawnPointId == receipt.SpawnPointId) ids.Add(kv.Key);
                }
                ids.Sort();
            }
            else
            {
                ids.Sort();
            }

            var payload = MemoryPack.MemoryPackSerializer.Serialize(
                new Horizon.Game.Message.Sync.ScriptedSpawnResultPayload
                {
                    RequestId = receipt.RequestId,
                    Nonce = receipt.Nonce,
                    EntityIds = ids.ToArray(),
                    Removed = receipt.Removed,
                });

            _events.Add((new[] { receipt.OperatorId }, new Horizon.Game.Message.Sync.EventPacket
            {
                ServerTick = _tick,
                Events = new[]
                {
                    new Horizon.Game.Message.Sync.SyncEvent
                    {
                        Kind = Horizon.Game.Message.Sync.SyncEventKind.ScriptedSpawnResult,
                        SourceEntityId = (ulong)receipt.OperatorId,
                        TargetEntityId = 0UL,
                        IntValue = ids.Count,
                        FloatValue = 0f,
                        Payload = payload,
                    },
                },
            }));

            ScriptReceiptCount++;
        }
    }

    /// <summary>到期清除：摘掉生成点并移除其存活实体（Despawn 走既有全广播路径）。</summary>
    private void TickScriptedExpiry()
    {
        if (_scriptPointExpiryTick.Count == 0) return;

        List<int>? due = null;
        foreach (var kv in _scriptPointExpiryTick)
        {
            if (kv.Value <= _tick) (due ??= new List<int>()).Add(kv.Key);
        }

        if (due == null) return;

        // 到期集合来自字典遍历，顺序不保证；而注销顺序决定 Despawn 全广播里 delta 的排列，
        // 进而决定回放指纹。与区域到期/进入沿同一纪律：显式排序。
        due.Sort();
        for (var i = 0; i < due.Count; i++) UnregisterScriptedPoint(due[i]);
    }

    private List<ulong> UnregisterScriptedPoint(int spawnPointId)
    {
        var removed = new List<ulong>();
        if (_spawnSlots.TryGetValue(spawnPointId, out var slots))
        {
            for (var s = 0; s < slots.Length; s++)
            {
                var entityId = slots[s].EntityId;
                if (slots[s].Alive && entityId != 0 && MarkMonsterDead(entityId, _tick, allowCorpseLinger: false)) removed.Add(entityId);
            }
            _spawnSlots.Remove(spawnPointId);
        }

        for (var i = _spawnPoints.Count - 1; i >= 0; i--)
        {
            if (_spawnPoints[i].SpawnPointId == spawnPointId) _spawnPoints.RemoveAt(i);
        }

        _scriptPointExpiryTick.Remove(spawnPointId);
        _scriptPointOwner.Remove(spawnPointId);
        _scriptPointKind.Remove(spawnPointId);
        return removed;
    }

    private List<ulong> RemoveScriptedEntity(ulong entityId)
    {
        var removed = new List<ulong>();
        if (!_monstersById.TryGetValue(entityId, out var monster)) return removed;

        // 只承认脚本域。静态刷怪点实体的清除需要 DespawnAny，而该判定已在 Grain 完成；
        // DS 无从复核权限，因此这里一律不动静态点——宁可"清不掉"，不可"越权清"。
        if (monster.SpawnPointId >= 0) return removed;

        if (MarkMonsterDead(entityId, _tick, allowCorpseLinger: false)) removed.Add(entityId);
        return removed;
    }

    /// <summary>把清除结果作为回执发给操作者（空列表也是有意义的回答：什么都没清掉）。</summary>
    private void EmitRemovalReceipt(
        Horizon.Game.Message.Network.ScriptGrantToken token, List<ulong> removed)
    {
        _scriptReceipts.Add(new ScriptReceipt
        {
            OperatorId = token.OperatorCharacterId,
            RequestId = token.RequestId,
            Nonce = token.Nonce,
            SpawnPointId = 0,
            Ids = removed,
            Removed = true,
        });
    }

    /// <summary>累计下发的表现型事件数（诊断）。</summary>
    public long PresentationEventCount { get; private set; }

    /// <summary>
    /// 派发特殊事件。<b>形态由服务端事件目录决定</b>：客户端只能给事件标识，
    /// "这次是刷一群怪还是放个天象"不能由指令参数指定，否则白名单与预算形同虚设。
    /// </summary>
    private void FireWorldEvent(Horizon.Game.Message.Network.ScriptGrantToken token)
    {
        if (!Horizon.Game.Sim.WorldEventCatalog.TryGetRegistered(token.TemplateId, out var ev))
        {
            LastScriptRejectReason = $"unknown-event:{token.TemplateId}";
            return;
        }

        if (ev.Mode != Horizon.Game.Sim.WorldEventMode.Presentation)
        {
            // 玩法型事件 = 按目录配置生成怪群；数量/模板来自目录，不来自令牌。
            // 回执口径与 /spawn 保持一致：它同属"生成类指令"，操作者需要拿到真实实体 ID。
            var pointId = CreateScriptedPoint(
                Horizon.Game.Message.Network.ScriptedKind.Monster,
                ev.MonsterTemplateId,
                token.X, token.Y, token.Z,
                ev.SpawnCount,
                token.TtlSeconds,
                token.OperatorCharacterId);

            if (pointId != 0)
            {
                _scriptReceipts.Add(new ScriptReceipt
                {
                    OperatorId = token.OperatorCharacterId,
                    RequestId = token.RequestId,
                    Nonce = token.Nonce,
                    SpawnPointId = pointId,
                });
            }
            return;
        }

        // 表现型事件不建实体，只走既有 EventPacket(Vfx) 通道下发给在线会话。
        var audience = GetSessionIds();
        if (audience.Count == 0)
        {
            // 没人在线时不发"对着空频道喊话"的字节：事件本身没有可投递对象。
            LastScriptRejectReason = "no-audience";
            return;
        }

        // 时长语义：TTL 对表现型事件就是"显示多久"，上限 30 秒防止一条指令刷屏不停。
        var durationSeconds = token.TtlSeconds > 0 ? Math.Min(token.TtlSeconds, 30) : 5f;
        var payload = MemoryPack.MemoryPackSerializer.Serialize(new Horizon.Game.Message.Sync.ScriptedEffectPayload
        {
            EffectKey = ev.Name,
            Text = ev.Name,
            X = token.X,
            Y = token.Y,
            Z = token.Z,
            DurationSeconds = durationSeconds,
        });

        var packet = new Horizon.Game.Message.Sync.EventPacket
        {
            ServerTick = _tick,
            Events = new[]
            {
                new Horizon.Game.Message.Sync.SyncEvent
                {
                    Kind = Horizon.Game.Message.Sync.SyncEventKind.Vfx,
                    SourceEntityId = 0UL,
                    TargetEntityId = 0UL,
                    IntValue = token.TemplateId,
                    FloatValue = durationSeconds,
                    Payload = payload,
                },
            },
        };

        _events.Add((audience.ToArray(), packet));
        PresentationEventCount++;
    }

    private List<ulong> RemoveScriptedPointsOwnedBy(long operatorCharacterId)
    {
        var removed = new List<ulong>();
        List<int>? owned = null;
        foreach (var kv in _scriptPointOwner)
        {
            if (kv.Value == operatorCharacterId) (owned ??= new List<int>()).Add(kv.Key);
        }

        if (owned == null) return removed;
        owned.Sort(); // 确定性移除顺序
        for (var i = 0; i < owned.Count; i++) removed.AddRange(UnregisterScriptedPoint(owned[i]));
        return removed;
    }

    /// <summary>把该操作者的触发区域一并摘除，返回被摘区域 ID。</summary>
    private List<ulong> RemoveTriggerZonesOwnedBy(long operatorCharacterId)
    {
        var removed = new List<ulong>();
        if (_triggerZones.Count == 0) return removed;

        List<ulong>? owned = null;
        foreach (var kv in _triggerZones)
        {
            if (kv.Value.OwnerCharacterId == operatorCharacterId) (owned ??= new List<ulong>()).Add(kv.Key);
        }

        if (owned == null) return removed;
        owned.Sort();
        for (var i = 0; i < owned.Count; i++)
        {
            removed.Add(owned[i]);
            UnregisterTriggerZone(owned[i]);
        }
        return removed;
    }

    // ==================== 触发区域（特殊事件第三态，设计方案 §6.5） ====================
    //
    // 区域本身**不下发给客户端**：工程里没有体积渲染，画出来要新增协议包与客户端分支。
    // 因此区域是纯服务端结构，只产出"谁进来了 → 召唤什么"，召唤出的怪群走脚本生成点通路，
    // 与直接指令生成完全同源（同一套 AI / 快照 / AOI / 晚加入补发 / Despawn 全广播不变量）。

    /// <summary>球形触发区域。</summary>
    private sealed class TriggerZone
    {
        /// <summary>区域 ID（取负数域的 serial，与生成点共用同一个单调计数器，保证互不重叠）。</summary>
        public ulong EntityId;

        /// <summary>放置者（供 <c>/despawn mine</c> 清理）。</summary>
        public long OwnerCharacterId;

        /// <summary>踏入时要召唤的怪物模板（Grain 侧已按白名单校验）。</summary>
        public int SpawnTemplateId;

        /// <summary>召唤数量。</summary>
        public int SpawnCount = 1;

        /// <summary>球心（服务端 Z-up，米）。</summary>
        public float X, Y, Z;

        /// <summary>半径（米）。判定用平方距离，不开方。</summary>
        public float Radius;

        /// <summary>覆盖的 chunk 键（注册时算一次，用于把候选玩家从"全体"收窄到"同 chunk"）。</summary>
        public ulong[] ChunkKeys = Array.Empty<ulong>();

        /// <summary>true = 可重复触发（受冷却限频）；false = 触发一次即失效。</summary>
        public bool Repeat;

        /// <summary>Repeat 模式的冷却秒数（Grain 已强制其不得为 0）。</summary>
        public int RepeatCooldownSeconds;

        /// <summary>下一次允许触发的 tick（Repeat 的限频闸门）。</summary>
        public long NextAllowedTick;

        /// <summary>到期 tick；到期只摘区域，已召唤出的怪群按自身规则存续。</summary>
        public long ExpiresAtTick;

        /// <summary>当前在区域内的会话 ID 集合（用于求"进入沿"）。</summary>
        public readonly HashSet<long> Inside = new();
    }

    /// <summary>区域 ID → 区域。</summary>
    private readonly Dictionary<ulong, TriggerZone> _triggerZones = new();

    /// <summary>chunk 键 → 覆盖该 chunk 的区域 ID 列表（倒排索引）。</summary>
    private readonly Dictionary<ulong, List<ulong>> _zonesByChunk = new();

    /// <summary>累计触发次数（诊断）。</summary>
    public long TriggerFiredCount { get; private set; }

    /// <summary>当前存活区域数。</summary>
    public int TriggerZoneCount => _triggerZones.Count;

    private void RegisterTriggerZone(Horizon.Game.Message.Network.ScriptGrantToken token)
    {
        if (_triggerZones.Count >= _options.MaxScriptedTriggerZones)
        {
            LastScriptRejectReason = "zone-budget";
            return;
        }

        var serial = ++_scriptSerial;
        if (serial > int.MaxValue) return;

        // 区域必须有有限寿命：Grain 已拒 TTL=0，这里再兜一层，防止绕过裁决直接构造的令牌常驻。
        var ttlSeconds = token.TtlSeconds > 0
            ? Math.Min(token.TtlSeconds, _options.MaxScriptedTriggerZoneTtlSeconds)
            : _options.MaxScriptedTriggerZoneTtlSeconds;

        var zone = new TriggerZone
        {
            EntityId = Horizon.Game.Sim.SpawnDirectorCore.ComputeEntityId(-(int)serial, 0),
            OwnerCharacterId = token.OperatorCharacterId,
            SpawnTemplateId = token.TemplateId,
            SpawnCount = Math.Clamp(token.Count, 1, 10),
            X = token.X,
            Y = token.Y,
            Z = token.Z,
            Radius = token.Radius,
            Repeat = token.TriggerMode == Horizon.Game.Message.Network.ScriptTriggerMode.Repeat,
            RepeatCooldownSeconds = Math.Max(1, token.CooldownSeconds),
            ExpiresAtTick = _tick + (long)ttlSeconds * Math.Max(1, _options.TicksPerSecond),
        };

        // chunk 半径向上取整：漏一个 chunk 就等于漏一批候选，宁可多算不可少算。
        var chunkSize = Math.Max(1, _aoiChunkSize);
        var radiusChunks = (int)Math.Ceiling(zone.Radius / chunkSize);
        var (cx, cy, cz) = ChunkCoords(zone.X, zone.Y, zone.Z);
        zone.ChunkKeys = ComputeAoiChunkKeys(cx, cy, cz, radiusChunks);

        for (var i = 0; i < zone.ChunkKeys.Length; i++)
        {
            if (!_zonesByChunk.TryGetValue(zone.ChunkKeys[i], out var list))
            {
                list = new List<ulong>();
                _zonesByChunk[zone.ChunkKeys[i]] = list;
            }

            list.Add(zone.EntityId);
        }

        _triggerZones[zone.EntityId] = zone;

        _scriptReceipts.Add(new ScriptReceipt
        {
            OperatorId = token.OperatorCharacterId,
            RequestId = token.RequestId,
            Nonce = token.Nonce,
            SpawnPointId = 0,
            Ids = new List<ulong> { zone.EntityId },
        });
    }

    private void UnregisterTriggerZone(ulong zoneId)
    {
        if (!_triggerZones.Remove(zoneId)) return;
        foreach (var list in _zonesByChunk.Values) list.Remove(zoneId);
    }

    /// <summary>
    /// 进出沿检测。<b>必须早于刷怪调度</b>：本 tick 踩中的区域要立刻召唤，
    /// 召唤出的怪群在同一个 tick 就进入快照，操作者不会看到"已受理但世界没反应"。
    /// </summary>
    private void TickTriggerZones()
    {
        if (_triggerZones.Count == 0) return;

        // 1) 到期清理（按区域 ID 升序，确定性）。
        List<ulong>? due = null;
        foreach (var kv in _triggerZones)
        {
            if (kv.Value.ExpiresAtTick <= _tick) (due ??= new List<ulong>()).Add(kv.Key);
        }

        if (due != null)
        {
            due.Sort();
            for (var i = 0; i < due.Count; i++) UnregisterTriggerZone(due[i]);
        }

        if (_triggerZones.Count == 0) return;

        // 2) 候选收窄：只把"所在 chunk 命中某区域索引"的玩家拿出来做精确球体判定。
        //    区域数已被 MaxScriptedTriggerZones 封顶，玩家查表 O(1) → 整体 O(在线玩家)，
        //    而不是 O(在线玩家 × 区域数)。
        var entering = new List<(TriggerZone Zone, long SessionId)>();

        foreach (var kv in _playersBySession)
        {
            var player = kv.Value;
            if (player.IsDead) continue;

            var e = _simWorld.GetEntity(player.EntityId);
            var px = e?.X ?? player.SpawnX;
            var py = e?.Y ?? player.SpawnY;
            var pz = e?.Z ?? player.SpawnZ;

            if (!_zonesByChunk.TryGetValue(ChunkKey(px, py, pz), out var zoneIds) || zoneIds.Count == 0)
                continue;

            for (var i = 0; i < zoneIds.Count; i++)
            {
                if (!_triggerZones.TryGetValue(zoneIds[i], out var zone)) continue;

                var dx = px - zone.X;
                var dy = py - zone.Y;
                var dz = pz - zone.Z;
                var inside = dx * dx + dy * dy + dz * dz <= zone.Radius * zone.Radius;

                if (inside)
                {
                    if (!zone.Inside.Contains(kv.Key)) entering.Add((zone, kv.Key));
                    zone.Inside.Add(kv.Key);
                }
                else
                {
                    zone.Inside.Remove(kv.Key);
                }
            }
        }

        if (entering.Count == 0) return;

        // 召唤顺序按"区域 ID → 进入会话"排序，杜绝依赖字典/列表的插入遍历顺序。
        entering.Sort((a, b) => a.Zone.EntityId != b.Zone.EntityId
            ? a.Zone.EntityId.CompareTo(b.Zone.EntityId)
            : a.SessionId.CompareTo(b.SessionId));

        for (var i = 0; i < entering.Count; i++)
        {
            var zone = entering[i].Zone;
            if (_tick < zone.NextAllowedTick) continue;

            CreateScriptedPoint(
                Horizon.Game.Message.Network.ScriptedKind.Monster,
                zone.SpawnTemplateId,
                zone.X, zone.Y, zone.Z,
                zone.SpawnCount,
                ttlSeconds: 0,
                ownerCharacterId: zone.OwnerCharacterId);

            TriggerFiredCount++;

            if (zone.Repeat)
            {
                // 冷却闸门防"边界横跳刷怪风暴"；冷却秒数由令牌带下来且 Grain 已保证 > 0。
                zone.NextAllowedTick = _tick
                    + (long)zone.RepeatCooldownSeconds * Math.Max(1, _options.TicksPerSecond);
            }
            else
            {
                UnregisterTriggerZone(zone.EntityId); // Once：触发即失效
            }
        }
    }

    /// <summary>把怪物重新置为可重生（M4 死亡流程的接入点；当前供测试与后续里程碑使用）。</summary>
    /// <param name="allowCorpseLinger">
    /// false = 立刻摘除，不给尸体窗口。显式 Despawn 与脚本 TTL 到期必须走这条：
    /// 操作者说"把它移走"之后还留一具尸体在地上，是把自己的指令打了折扣。
    /// </param>
    public bool MarkMonsterDead(ulong entityId, long serverTick, bool allowCorpseLinger = true)
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
        // 风筝宽限窗不跨世：尸体重生后必须从"从没挨过打"重新开始，
        // 否则新刷出来的那只带着上一世最后 11 秒的免检资格（表现是被打一下就拉多远都不回家）。
        self.LastLeashExtensionTick = 0;
        // 组也别跨世：复活出来的那只若还挂着上一世的组号，队友挨打会莫名其妙刷新它的宽限窗。
        self.LeashGroupId = 0UL;
        monster.Ai = self;
        monster.Runtime?.ResetForRespawn();

        // 尸体窗口：只在**装配了内容包**时才可能生效（代码目录模板没有 corpse_seconds 这一列，
        // 取 0 = 当场消失 = 本功能之前的逐字行为，既有金轨迹与断言不受影响）。
        var planned = ComputeCorpseExpiries(monster.TemplateId, serverTick, self.RespawnAtTick, allowCorpseLinger);
        var corpseExpiry = planned.CorpseExpiry;
        monster.CorpseLootExpireAtTick = planned.LootExpiry > serverTick ? planned.LootExpiry : 0;
        monster.PickupDistanceMeters = planned.PickupDistance;
        if (corpseExpiry > serverTick)
        {
            // 尸体留在 _monstersById：快照照常下发（带 Dead 位），玩家看得见它、打不到它
            // （CombatCore 对 Hp<=0 直接判 TargetDead），AI 侧由两个内核的 Dead 早退挡住。
            // 网关注册表此刻就要摘掉它：那份名单的语义是"玩家面前可交互的对象"，
            // 尸体不该继续算一个（等真正消失时再摘一次是幂等的，见 RemoveMonsterFromWorld）。
            monster.CastBarVisible = false;
            monster.InterruptRequested = false;   // 尸体不欠任何人一次打断
            monster.CorpseExpireAtTick = corpseExpiry;
            _corpses.Add(entityId);
            var corpseAi = monster.Ai;
            // 尸体拾取（灰度）：尸体必须**留在注册表里**客户端才点得到——但带一个"已死但可交互"的位，
            // 旧客户端按 kind 过滤，不会因此把尸体会当成活怪。未开灰度时保持原来的"当场摘除"。
            var corpseInteractable = _options.DeferCorpseLootToAuthority && monster.CorpseLootExpireAtTick > 0;
            QueueEntityDescriptor(entityId, monster.SpawnPointId, monster.TemplateId, monster.ScriptedKind,
                corpseAi.X, corpseAi.Y, corpseAi.Z, removed: !corpseInteractable, corpseInteractable: corpseInteractable);
            MonsterCorpsesLingering++;
            return true;
        }

        RemoveMonsterFromWorld(entityId, monster);
        return true;
    }

    /// <summary>
    /// 让实体**彻底离开世界**：登记 Despawn 全广播、摘出字典、通知网关注册表。
    /// <para>
    /// 为什么必须登记 Despawn（而不是"停止更新"就够了）：本方法把实体从 _monstersById 摘掉后，
    /// 该 ID 不再出现在任何后续快照里，客户端既收不到 Despawn 也收不到 Dead 位，
    /// 结果是一只冻在原地的鬼怪——并且"选中目标只在目标消失时才丢"这条语义失效
    /// （击杀后选中框、头顶名牌、脚下高亮环全部挂在一个已经不存在的目标上）。
    /// 走与玩家离线同一条全广播路径（不按 AOI 过滤，否则不在该 chunk 订阅表里的观察者永远收不到）；
    /// 重生时新 DsMonster 的 SpawnedToClients=false，会自然重新走一遍 Spawn + Identity。
    /// </para>
    /// </summary>
    private void RemoveMonsterFromWorld(ulong entityId, DsMonster monster)
    {
        _pendingDespawnEntityIds.Add(entityId);
        _monstersById.Remove(entityId);
        monster.CorpseExpireAtTick = 0;
        monster.CorpseLootExpireAtTick = 0;
        monster.CorpseLootContributors = null;
        // 尸体彻底离开世界（到期收尸 / 显式摘除 / 重生覆盖）时释放权威侧的拾取登记，
        // 否则那份列表会一直挂在内存/持久层里等一个永远不会来的拾取者。
        _corpseLootStore?.RemoveAsync(entityId);
        if (monster.CorpseLootRegistered)
        {
            monster.CorpseLootRegistered = false;
            _pendingCorpseLootGone.Add(new DsCorpseLootGone { CorpseEntityId = entityId });
        }

        // 与 Despawn 广播同一条纪律：实体"彻底离开世界"必须让网关注册表也知道，
        // 否则一个已死的 NPC 会永远留在"玩家面前可交互对象"的名单里。
        var deadAi = monster.Ai;
        QueueEntityDescriptor(entityId, monster.SpawnPointId, monster.TemplateId, monster.ScriptedKind,
            deadAi.X, deadAi.Y, deadAi.Z, removed: true);
    }

    /// <summary>
    /// 收尸：把到期的尸体真正摘除并广播 Despawn。
    /// <para>顺序按 EntityId 升序——Despawn 包里的 delta 顺序进字节流，
    /// 而"同 tick 序列 → 同结果"是金轨迹对拍的前提，不能按死亡先后（那是 List 的插入序）。</para>
    /// </summary>
    private void TickCorpses()
    {
        if (_corpses.Count == 0) return;

        var expiring = new List<ulong>(_corpses.Count);
        for (var i = 0; i < _corpses.Count; i++)
        {
            var id = _corpses[i];
            if (!_monstersById.TryGetValue(id, out var monster) || monster == null)
            {
                // 已经不在世界里（被重生路径覆盖或被外部摘除）：这条尸体登记是过期的，直接丢掉。
                _corpses.RemoveAt(i);
                i--;
                continue;
            }
            // 提示先于摘除：拾取窗口与尸体停留常常是**同一拍**到期（缺省都是 60 秒），
            // 放到 else 里就正好让最常见的那种配法永远收不到提示。
            MaybeNotifyCorpseLootExpired(monster);
            if (monster.CorpseExpireAtTick != 0 && _tick >= monster.CorpseExpireAtTick) expiring.Add(id);
        }

        if (expiring.Count == 0) return;
        expiring.Sort();
        for (var i = 0; i < expiring.Count; i++)
        {
            var id = expiring[i];
            _corpses.Remove(id);
            if (_monstersById.TryGetValue(id, out var monster) && monster != null)
                RemoveMonsterFromWorld(id, monster);
        }
    }

    /// <summary>
    /// 拾取窗口到期而东西没拿完 → 给还在线的击杀参与者各发一条提示（同一具尸体只发一次）。
    /// <para>2026-10-29 拍板要这条提示。判据三条都要成立才发：登记过（尸体上本来有东西）、
    /// 窗口真的关了、还有没取走的槽。<b>拿空了的尸体不发</b>——"你全拿走了"不是需要道歉的事。</para>
    /// <para>收件人只取"击杀参与者 ∩ 本 DS 在线会话"：离线的人没有会话，发出去只会进一个不存在的队列。</para>
    /// </summary>
    private void MaybeNotifyCorpseLootExpired(DsMonster monster)
    {
        if (!monster.CorpseLootRegistered || monster.CorpseLootExpiredNotified) return;
        if (monster.CorpseLootExpireAtTick <= 0 || _tick < monster.CorpseLootExpireAtTick) return;
        monster.CorpseLootExpiredNotified = true;

        var remaining = monster.CorpseLootSlotsTotal - monster.CorpseLootSlotsTaken;
        if (remaining <= 0) return;
        if (monster.CorpseLootContributors == null || monster.CorpseLootContributors.Length == 0) return;

        for (var i = 0; i < monster.CorpseLootContributors.Length; i++)
        {
            var sessionId = 0L;
            foreach (var kv in _playersBySession)
            {
                if (kv.Value.CharacterId != monster.CorpseLootContributors[i]) continue;
                sessionId = kv.Key;
                break;
            }
            if (sessionId == 0L) continue;   // 不在线：没有会话可发
            _events.Add((new[] { sessionId }, new Horizon.Game.Message.Sync.EventPacket
            {
                ServerTick = _tick,
                Events = new[]
                {
                    new Horizon.Game.Message.Sync.SyncEvent
                    {
                        Kind = Horizon.Game.Message.Sync.SyncEventKind.CorpseLootExpired,
                        SourceEntityId = monster.EntityId,
                        TargetEntityId = monster.CorpseLootContributors[i],
                        IntValue = remaining,
                    },
                },
            }));
            CorpseLootExpiredNotices++;
        }
    }

    /// <summary>已下发的"窗口到期还没拿完"提示条数（每具尸体·每人一条）。</summary>
    public int CorpseLootExpiredNotices { get; private set; }

    /// <summary>
    /// 登记一条实体身份变更，随下一次 tick 结果批量上送网关（见 <see cref="DsEntityRegistry"/>）。
    /// <para>
    /// 只登记不改世界状态：本方法不参与确定性模拟，因此不会影响实体 id 分配、快照顺序等
    /// 任何既有不变量。
    /// </para>
    /// </summary>
    private void QueueEntityDescriptor(ulong entityId, int spawnPointId, int templateId, int scriptedKind,
        float x, float y, float z, bool removed, bool corpseInteractable = false)
    {
        _entityDescriptors.Add(new DsEntityDescriptor
        {
            EntityId = entityId,
            SpawnPointId = spawnPointId,
            TemplateId = templateId,
            ScriptedKind = (byte)Math.Clamp(scriptedKind, 0, byte.MaxValue),
            X = x,
            Y = y,
            Z = z,
            Removed = removed,
            CorpseInteractable = corpseInteractable,
        });
    }

    /// <summary>
    /// 全量收集当前世界里所有实体的身份描述符。
    /// <para>
    /// 用于**补发**：网关重启（注册表是内存态）或 DS 重连后，注册表对着一个仍在运行的世界会是空的，
    /// 邻近判定就会一律落空。宿主在每次注册成功后调用本方法把世界现状重放一遍。
    /// </para>
    /// </summary>
    public IReadOnlyList<DsEntityDescriptor> CollectAllEntityDescriptors()
    {
        if (_monstersById.Count == 0) return Array.Empty<DsEntityDescriptor>();

        var ids = new List<ulong>(_monstersById.Keys);
        ids.Sort(); // 固定顺序：同一世界状态产出同一份列表

        var list = new List<DsEntityDescriptor>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            if (!_monstersById.TryGetValue(ids[i], out var m)) continue;
            var ai = m.Ai;
            list.Add(new DsEntityDescriptor
            {
                EntityId = m.EntityId,
                SpawnPointId = m.SpawnPointId,
                TemplateId = m.TemplateId,
                ScriptedKind = (byte)Math.Clamp(m.ScriptedKind, 0, byte.MaxValue),
                X = ai.X,
                Y = ai.Y,
                Z = ai.Z,
                Removed = false,
            });
        }

        return list;
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
