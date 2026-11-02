using System;
using System.Collections.Generic;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>一件掉落物。</summary>
public struct DsLootDrop
{
    /// <summary>物品 ID。</summary>
    public int ItemId;

    /// <summary>数量。</summary>
    public int Count;
}

/// <summary>
/// 掉落掷骰来源（MMORPG 开发计划 M4.3 —— 首次接线既有 <c>LootTableGrain</c>）。
/// <para>
/// 抽象的理由：DS 权威世界位于实时层，不应直接依赖 Orleans 业务层实现；
/// 生产环境注入"经事件流/LootTableGrain 掷骰"的适配器，测试注入确定性实现。
/// 这样"击杀→掉落"的**决策时序**（在 DS 的哪个 tick、用哪个实体 ID 作为掷骰输入）
/// 可以在没有 Orleans 的情况下被完整验证。
/// </para>
/// </summary>
public interface IDsLootProvider
{
    /// <summary>为一次击杀掷骰掉落。</summary>
    /// <param name="monsterTemplateId">怪物模板 ID。</param>
    /// <param name="killerCharacterId">击杀者角色 ID。</param>
    /// <param name="serverTick">击杀发生的服务器 tick（确定性掷骰的输入之一）。</param>
    IReadOnlyList<DsLootDrop> Roll(int monsterTemplateId, ulong killerCharacterId, long serverTick);
}

/// <summary>
/// 确定性掉落掷骰（默认实现）。
/// <para>
/// 掉落必须可复现：若使用 <c>Random.Shared</c>，同一场战斗在 Orleans 权威与 UE5 DS 权威下
/// 会给出不同掉落，灰度对拍与"重放验证玩家申诉"都会失效。因此这里复用
/// <see cref="Horizon.Game.Sim.CombatCore.Roll01"/> 的确定性掷骰，把
/// (模板, 击杀者, tick) 作为随机源本身——**无状态、可重放**。
/// </para>
/// <para>
/// 概率表刻意做得极简（必掉 1 件 + 30% 追加 1 件），生产应改为读
/// <c>LootTableGrain</c> 下发的配置；此处只保证"闭环跑得通、结果可复现"。
/// </para>
/// </summary>
public sealed class DeterministicLootProvider : IDsLootProvider
{
    /// <inheritdoc />
    public IReadOnlyList<DsLootDrop> Roll(int monsterTemplateId, ulong killerCharacterId, long serverTick)
    {
        if (monsterTemplateId <= 0) return Array.Empty<DsLootDrop>();
        if (killerCharacterId == 0UL) return Array.Empty<DsLootDrop>();

        var drops = new List<DsLootDrop>(2);

        // 必掉：物品 ID 由模板派生（模板 100 → 物品 100001），保证不同怪掉不同东西。
        drops.Add(new DsLootDrop { ItemId = monsterTemplateId * 1000 + 1, Count = 1 });

        // 追加掉落：30% 概率，掷骰输入含 tick —— 同一 tick 重放结果一致。
        var roll = Horizon.Game.Sim.CombatCore.Roll01(killerCharacterId, (ulong)monsterTemplateId, serverTick, salt: 0x1001u);
        if (roll < 0.3f)
        {
            drops.Add(new DsLootDrop { ItemId = monsterTemplateId * 1000 + 2, Count = 1 });
        }

        return drops;
    }
}

/// <summary>跨层玩法事件类型（DS → 网关 → Orleans 业务层）。</summary>
public enum DsGameplayEventKind : byte
{
    /// <summary>未知/保留。</summary>
    Unknown = 0,

    /// <summary>击杀（用于任务进度 / 成就 / 统计）。</summary>
    Kill = 1,

    /// <summary>掉落归属（用于背包入包 / 邮件兜底）。</summary>
    Loot = 2,

    /// <summary>玩家死亡（用于死亡惩罚 / 统计）。</summary>
    PlayerDeath = 3,

    /// <summary>
    /// 击杀经验发放（<c>ItemCount</c> 复用为经验值；<c>ActorCharacterId</c> = 参与者）。
    /// <para>与 <see cref="Kill"/> 分开是**必须的**：Kill 驱动任务进度与分域好感，
    /// 若把"每个摸过怪的人都算击杀"塞进同一种事件，任务会在围观时被重复推进。
    /// 经验按 WoW 的让分曲线由 <c>MonsterXpCore</c> 算好，业务层只负责加给对应角色。</para>
    /// </summary>
    KillXp = 4,

    /// <summary>
    /// 条件掉落（"要持有某任务才给"）：<c>ItemId/ItemCount</c> 是 DS 掷出来的结果，
    /// <c>QuestId</c> 是要判的条件。
    /// <para>为什么判在业务层：DS 手里没有玩家的任务状态，而"DS 去猜一个条件"的后果是
    /// 条件掉落变成人人有份——那比不给更糟。掷归模拟层（要确定性），判归业务层（要有状态）。</para>
    /// </summary>
    LootConditional = 5,

    /// <summary>
    /// 击杀金币入账（<c>Money</c> 是 DS 掷出来的金额，<c>ActorCharacterId</c> = 拿钱的人）。
    /// <para>为什么要单独一种事件而不是挂在 <see cref="Loot"/> 上：<c>Loot</c> 的消费端第一句就是
    /// <c>if (ItemId &gt; 0 &amp;&amp; ItemCount &gt; 0)</c>，金币事件的 ItemId 天然是 0 ——
    /// 挂上去会被那条判据安静吞掉（"掉金币从没到账"最好的伪装形态）。</para>
    /// <para>金额归模拟层掷（要确定性、要与掉落表同一套盐值与顺序），到账归业务层（要有钱包）；
    /// 与 <see cref="KillXp"/>、<see cref="LootConditional"/> 是同一条分工原则。</para>
    /// </summary>
    KillMoney = 6,
}

/// <summary>
/// 跨层玩法事件接收端（MMORPG 开发计划 M4.4）。
/// <para>
/// 网关实现它并把事件转投 Orleans 业务层（<c>InventoryGrain</c>/<c>QuestGrain</c>/…）。
/// 抽象成接口是为了让网关的控制面逻辑**不直接依赖 Orleans 客户端**——
/// 这样"事件是否被正确解析、幂等键是否正确、灰度关闭时是否被丢弃"全部可以在单测里断言。
/// </para>
/// </summary>
/// <summary>
/// 会话结束通知端（网关 → DS）。
/// <para>
/// DS 是权威世界，玩家实体常驻其中；客户端断线时网关必须**主动告知** DS，
/// 否则 DS 会永久保留该玩家并持续广播快照给死会话（实测：网关日志 <c>Packet dropped: session offline</c> 刷屏）。
/// </para>
/// <para>抽象成接口是为了让"何时通知、通知谁"可在单测里断言，而不必起真实 socket。</para>
/// </summary>
public interface IDsSessionNotifier
{
    /// <summary>
    /// 通知承载该 shard 的 DS：这些会话已结束。<br/>
    /// 找不到可用 DS 时静默跳过（调用方不应因此失败——断线清理不能被 DS 可用性阻塞）。
    /// </summary>
    /// <param name="shardId">会话所属 shard。</param>
    /// <param name="sessionIds">已结束的会话 ID（本架构中 sessionId == characterId）。</param>
    /// <returns>是否成功送达（false = 无可用 DS 或发送失败）。</returns>
    bool NotifySessionEnded(long shardId, System.Collections.Generic.IReadOnlyList<long> sessionIds);
}

/// <summary>
/// 跨层玩法事件接收端（MMORPG 开发计划 M4.4）。
/// <para>
/// 网关实现它并把事件转投 Orleans 业务层（<c>InventoryGrain</c>/<c>QuestGrain</c>/…）。
/// 抽象成接口是为了让网关的控制面逻辑**不直接依赖 Orleans 客户端**——
/// 这样"事件是否被正确解析、幂等键是否正确、灰度关闭时是否被丢弃"全部可以在单测里断言。
/// </para>
/// </summary>
public interface IDsGameplayEventSink
{
    /// <summary>投递一个玩法事件。实现方负责幂等去重（见 <see cref="DsGameplayEvent.BuildIdempotencyKey"/>）。</summary>
    void Publish(in DsGameplayEvent evt);
}

/// <summary>
/// 跨层玩法事件发送端（DS 侧）。
/// <para>
/// 与 <see cref="IDsDownlinkSender"/> 分开定义：快照发送是"每个 tick 都可能发生"的热路径，
/// 玩法事件是"击杀时才发生"的稀疏路径。分开后宿主可以只接其中一条（例如纯移动压测不需要业务事件），
/// 也让测试可以独立注入与断言，不会因为共用接口而被迫实现无关方法。
/// </para>
/// </summary>
/// <summary>尸体拾取四帧的上送口（与 <see cref="IDsGameplayEventSender"/> 分开：这些是**请求/应答**，不是事件）。</summary>
public interface IDsCorpseLootSender
{
    Task<bool> SendCorpseLootRegisterAsync(DsCorpseLootRegister register, CancellationToken cancellationToken = default);
    Task<bool> SendCorpseLootGoneAsync(DsCorpseLootGone gone, CancellationToken cancellationToken = default);
    Task<bool> SendCorpseLootResultAsync(DsCorpseLootResult result, CancellationToken cancellationToken = default);
    Task<bool> SendCorpseLootForwardAsync(DsCorpseLootForward forward, CancellationToken cancellationToken = default);
}

public interface IDsGameplayEventSender
{
    /// <summary>上送一批玩法事件。返回 false 表示发送失败（调用方计数，不抛）。</summary>
    System.Threading.Tasks.Task<bool> SendGameplayEventsAsync(DsGameplayEventBatch batch, System.Threading.CancellationToken cancellationToken = default);
}

/// <summary>
/// 跨层玩法事件（MMORPG 开发计划 M4.3/M4.4）。
/// <para>
/// 实时层（DS）判定"发生了什么"，业务层（Orleans）决定"这意味着什么"：
/// 背包加物品、任务进度推进、成就解锁、经济监控。本结构是两者之间**单向、幂等可去重**的契约。
/// </para>
/// <para>
/// 幂等键 = <c>{ShardId}:{ServerTick}:{ActorEntityId}:{Kind}</c>（与既有
/// <c>IdempotentAttribute</c> 模式一致）——DS 重连或网关重试时业务层不会重复发奖。
/// </para>
/// </summary>
public struct DsGameplayEvent
{
    /// <summary>事件类型。</summary>
    public DsGameplayEventKind Kind;

    /// <summary>发生事件的 shard。</summary>
    public long ShardId;

    /// <summary>事件发生的服务器 tick。</summary>
    public long ServerTick;

    /// <summary>主角（击杀者/拾取者/死亡者）角色 ID。</summary>
    public ulong ActorCharacterId;

    /// <summary>相关怪物实体 ID（击杀/掉落）。</summary>
    public ulong MonsterEntityId;

    /// <summary>怪物模板 ID（任务"击杀 X 只 Y"的判据）。</summary>
    public int MonsterTemplateId;

    /// <summary>物品 ID（掉落事件）。</summary>
    public int ItemId;

    /// <summary>物品数量（掉落事件）。</summary>
    public int ItemCount;

    /// <summary>条件掉落要判的任务 id（<see cref="DsGameplayEventKind.LootConditional"/>；其余事件为 0）。</summary>
    public int QuestId;

    /// <summary>
    /// 金币金额（仅 <see cref="DsGameplayEventKind.KillMoney"/> 用；其余事件为 0）。
    /// <para>它**必须进幂等键**（见 <see cref="BuildIdempotencyKey"/>）：漏了就"同一 tick 同一个人
    /// 杀两只怪掉不同金额"会被去重吞掉一次——而这两笔在玩家那边是两个真实到账的铜板。</para>
    /// </summary>
    public long Money;

    /// <summary>
    /// 计算幂等键。
    /// </summary>
    public string BuildIdempotencyKey()
    {
        // 用 stackalloc 手工拼接，避免 string.Format / 插值的堆分配——
        // 本方法在每次击杀与每件掉落上调用，属于热路径。
        //
        // 每一项都要进键，且**不能重复凑数**：以前这里把 ServerTick 拼了两遍而没有 ItemCount，
        // 于是"同一次击杀里两件同物品同数量"的掉落（两张表各掉一件、或一件掉两份）键相同，
        // 去重会安静地吞掉第二件——玩家看到的是"配了 2 件只进 1 件"，日志里没有任何异常。
        // 缓冲区按"最坏情况"给：8 个字段 × 每个最长 20 位 + 7 个分隔符 = 167，取 192。
        // 为什么不用 96：以前所有字段都是 int/短 long，加进 Money（long，且是玩家可见的钱）之后
        // 最坏情况会溢出 stackalloc 的切片——TryFormat 溢出时返回 false 并写 0 个字符，
        // 于是**键会静默变短**、两次不同的入账可能凑出同一个键（比报错更糟：它表现成"钱没到账"）。
        Span<char> buf = stackalloc char[192];
        var pos = 0;

        ShardId.TryFormat(buf.Slice(pos), out var written); pos += written;
        buf[pos++] = ':';
        ServerTick.TryFormat(buf.Slice(pos), out written); pos += written;
        buf[pos++] = ':';
        ActorCharacterId.TryFormat(buf.Slice(pos), out written); pos += written;
        buf[pos++] = ':';
        ((int)Kind).TryFormat(buf.Slice(pos), out written); pos += written;
        buf[pos++] = ':';
        ItemId.TryFormat(buf.Slice(pos), out written); pos += written;
        buf[pos++] = ':';
        ItemCount.TryFormat(buf.Slice(pos), out written); pos += written;
        buf[pos++] = ':';
        QuestId.TryFormat(buf.Slice(pos), out written); pos += written;
        buf[pos++] = ':';
        // 金币也必须进键（注释见字段声明）：两笔金额不同的入账如果被去重吞掉一笔，
        // 玩家看到的是"掉了一地钱而口袋里少一截"，日志里没有任何异常。
        Money.TryFormat(buf.Slice(pos), out written); pos += written;

        return new string(buf.Slice(0, pos));
    }
}

/// <summary>
/// 世界实体身份描述符（DS 内核结构，配套线上契约见 <see cref="DsEntityDescriptorDto"/>）。
/// <para>
/// DS 里"哪个实体是什么"只有 <c>DsWorldSession._monstersById</c> 知道，而下行同步包不带模板 id。
/// 本结构把这份身份搬出 DS：生成时一条、离开世界时一条（<see cref="Removed"/>=true）。
/// 坐标为**下行协议同一空间（Flax Y-up）**，DS 不做换轴解释。
/// </para>
/// </summary>
public struct DsEntityDescriptor
{
    /// <summary>运行时实体 ID。</summary>
    public ulong EntityId;

    /// <summary>生成类别原值（<c>ScriptedKind</c>；0 = 静态刷怪点怪物）。</summary>
    public byte ScriptedKind;

    /// <summary>模板 ID（按 <see cref="ScriptedKind"/> 决定语义）。</summary>
    public int TemplateId;

    /// <summary>所属刷怪点 ID（脚本生成点在负数域）。</summary>
    public int SpawnPointId;

    /// <summary>X（Flax 左右）。</summary>
    public float X;

    /// <summary>Y（Flax 上下）。</summary>
    public float Y;

    /// <summary>Z（Flax 前后）。</summary>
    public float Z;

    /// <summary>true = 实体已离开世界，接收方应删除该条目。</summary>
    public bool Removed;

    /// <summary>
    /// 这具尸体仍可被拾取（2026-10-29：尸体拾取需要一个"可交互但已死"的形态）。
    /// <para>只有开了 <c>CorpseLoot__Enabled</c> 的 DS 才会把它置真；旧网关/旧客户端忽略该尾部成员。</para>
    /// </summary>
    public bool CorpseInteractable;
}

/// <summary>
/// 实体身份描述符发送端（DS 侧）。
/// <para>
/// 与 <see cref="IDsGameplayEventSender"/> 分开的理由相同：那条是"发生了什么"（击杀/掉落），
/// 这条是"世界里有什么"。分开后测试可独立注入，宿主也可只接其一。
/// </para>
/// </summary>
public interface IDsEntityDescriptorSender
{
    /// <summary>上送一批实体身份变更。返回 false 表示发送失败（调用方计数，不抛）。</summary>
    System.Threading.Tasks.Task<bool> SendEntityDescriptorsAsync(
        DsEntityDescriptorBatch batch, System.Threading.CancellationToken cancellationToken = default);
}
