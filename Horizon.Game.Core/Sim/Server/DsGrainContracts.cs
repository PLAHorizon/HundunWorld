using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 掉落表配置快照（内核侧结构，MMORPG 开发计划 M4.3 收尾）。
/// <para>
/// 刻意与 Orleans 的 <c>LootTableConfig</c> 分离：内核（<c>Horizon.Game.Core</c> 的实时层）
/// 不应直接消费 Orleans 序列化契约对象——否则 Orleans 侧字段调整会静默改变 DS 的掉落行为。
/// 转换发生在适配器里，是一处显式的、可审查的边界。
/// </para>
/// </summary>
public sealed class LootTableSnapshot
{
    /// <summary>掉落表 ID。</summary>
    public int TableId { get; set; }

    /// <summary>掉落条目。</summary>
    public LootEntrySnapshot[] Entries { get; set; } = Array.Empty<LootEntrySnapshot>();
}

/// <summary>掉落条目快照。</summary>
public struct LootEntrySnapshot
{
    /// <summary>物品 ID。</summary>
    public int ItemId;

    /// <summary>掉落概率 [0,1]。</summary>
    public float DropRate;

    /// <summary>最小数量。</summary>
    public int MinCount;

    /// <summary>最大数量。</summary>
    public int MaxCount;

    /// <summary>稀有度（1-5，5 最稀有）。</summary>
    public int Rarity;
}

/// <summary>
/// 掉落表配置来源（Orleans 实现走 <c>ILootTableGrain.GetConfigAsync</c>；测试用假实现）。
/// <para>
/// <b>为什么是"取配置"而不是"请服务器掷骰"</b>：既有 <c>ILootTableGrain.RollLootAsync</c>
/// 是在 grain 内掷骰的，若 DS 在每次击杀时同步调用它，会有两个致命后果：
/// <list type="number">
///   <item><b>破坏确定性</b>——掷骰发生在 grain 状态里，同一场战斗在 Orleans 权威与 DS 权威下结果不同，
///         金轨迹对拍与重放申诉全部失效；</item>
///   <item><b>吃掉 tick 预算</b>——`IDsLootProvider.Roll` 是**同步**方法、在 DS 的 tick 线程上被调用，
///         一次跨进程 RPC 就是毫秒级阻塞，群怪同时死亡时会直接造成 tick 堆积
///         （正是既有深度分析报告 §8.1 警告的"单 shard 模拟瓶颈"）。</item>
/// </list>
/// 因此本接口只负责把**静态配置**取到 DS 侧缓存，掷骰在 DS 内以确定性方式本地完成——
/// 这也是真实 MMORPG 的常规做法（掉落表是配置，不是每次击杀的 RPC）。
/// </para>
/// </summary>
public interface ILootTableConfigSource
{
    /// <summary>取得掉落表配置；不存在时返回 null。</summary>
    Task<LootTableSnapshot?> GetLootTableAsync(int lootTableId, CancellationToken cancellationToken = default);
}

/// <summary>怪物模板 → 掉落表 ID 的映射。</summary>
public interface IMonsterLootMapping
{
    /// <summary>解析怪物模板对应的掉落表 ID；返回 0 表示该模板无掉落表。</summary>
    int ResolveLootTableId(int monsterTemplateId);
}

/// <summary>
/// 默认映射：模板 ID 即掉落表 ID。
/// <para>这是"能跑通"的最小约定；生产应改为读配置表（模板 → 掉落表）以免两者耦合。</para>
/// </summary>
public sealed class IdentityMonsterLootMapping : IMonsterLootMapping
{
    /// <inheritdoc />
    public int ResolveLootTableId(int monsterTemplateId) => monsterTemplateId > 0 ? monsterTemplateId : 0;
}

/// <summary>
/// 五行域映射：掉落表 ID = 怪物模板所属五行域（1金 2木 3水 4火 5土）。
/// <para>
/// <b>为什么换掉恒等映射</b>：五行技能树把掉落表按域装配（<c>WuxingLootTables.Build</c> 用域号作表 ID），
/// 而怪物模板 ID 是个远大于 5 的独立整数空间。用恒等映射时 <c>ResolveLootTableId(101)</c> 查到的是
/// 表 101——不存在，于是击杀恒不掉落，而这条链在日志里**没有任何痕迹**（装配好的 75 条产出永远读不到）。
/// </para>
/// <para>
/// <b>单一事实源</b>：域号只住在 <see cref="Horizon.Game.Sim.MonsterTemplateCatalog"/> 的
/// <c>WuxingDomain</c> 一列，好感入账读同一列，所以"这只怪掉哪个域的书"与"杀它涨哪个域的好感"
/// 不可能各说一套。未登记或该列留 0 的模板返回 0 = 无掉落表（宁可什么都不掉，也不掉错域）。
/// </para>
/// </summary>
public sealed class WuxingDomainLootMapping : IMonsterLootMapping
{
    /// <inheritdoc />
    public int ResolveLootTableId(int monsterTemplateId)
        => monsterTemplateId > 0 ? Horizon.Game.Sim.MonsterTemplateCatalog.LootTableIdOf(monsterTemplateId) : 0;
}

/// <summary>
/// 分域好感写入端（Orleans 实现走 <c>IFavorabilityGrain.AddAsync</c>）。
/// <para>
/// <c>targetKey</c> 由调用方给成形如 <c>monster:3</c> 的来源标识：同一来源累计到上限后不再增加，
/// 这是 Grain 侧既有的防刷口径（逮着一只怪刷满一域）。DS 这里只负责"这次击杀属于哪个域、来源是谁"。
/// </para>
/// </summary>
public interface IFavorabilityWriter
{
    /// <summary>为某角色的某五行域累积好感；域越界或写入失败都不抛异常，返回 false 交由调用方计数。</summary>
    Task<bool> AddAsync(Guid playerId, int domain, int delta, string targetKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// 击杀经验写入端（<see cref="DsGameplayEventKind.KillXp"/> 的消费方）。
/// <para>做成接口而不是直接调 Grain：事件泵的单测要在没有集群的情况下断言
/// "每个参与者各收到一份自己等级算出的经验"，而 Grain 依赖会把这条断言吞成
/// catch 里的"什么都没发生"（本工程有过跨 Grain 编排被 catch 吞掉的教训）。</para>
/// </summary>
public interface IExperienceWriter
{
    /// <summary>给某角色加经验；写不进（不在线/已归档）返回 false，由调用方计数而不是静默丢弃。</summary>
    Task<bool> AddExpAsync(ulong characterId, long amount, string reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// 加经验后的**权威累计值**（旧接口只有 bool，拿不到"现在总共多少"）。
/// <para>客户端经验条读的是 <c>CharacterAttributesComponent.Exp</c>，而它由 DS 的玩家快照下发；
/// DS 不知道自己发了多少以外的东西，所以必须由真正落库的这侧把绝对值交出来。</para>
/// </summary>
public readonly struct ExpGrant
{
    public bool Success { get; init; }
    public int Level { get; init; }
    public long Experience { get; init; }

    public static ExpGrant Failed => new() { Success = false, Level = 0, Experience = -1 };
}

/// <summary>
/// 派生能力：既写经验、又把写入后的累计值交回来。
/// <para>按 [[extend-interface-via-derived-capability]]：不直接给 <see cref="IExperienceWriter"/>
/// 加方法，旧实现（单测里的假写入端）仍能原样编译，消费方用 <c>is</c> 检测。</para>
/// </summary>
public interface IExperienceWriterWithResult : IExperienceWriter
{
    /// <summary>加经验并返回写入后的等级/累计经验；失败返回 <see cref="ExpGrant.Failed"/>。</summary>
    Task<ExpGrant> AddExpWithResultAsync(ulong characterId, long amount, string reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// 玩家权威进度推送端（击杀经验落库后，把新的等级/累计经验送回 DS）。
/// <para>不接它：经验真的进了 CharacterGrain，但玩家屏幕上的经验条永远停在登录值
/// （DS 的玩家快照里 Exp 恒为 0）。</para>
/// </summary>
public interface IPlayerProgressNotifier
{
    /// <summary>把某角色的权威进度推给其所处 DS；找不到会话/DS 时静默失败（不阻断经验入账）。</summary>
    void Notify(long characterId, int level, long exp);
}

/// <summary>
/// 金币入账写入端（<see cref="DsGameplayEventKind.KillMoney"/> 的消费方）。
/// <para>与 <see cref="IExperienceWriter"/> 同一条理由做成接口：事件泵的单测要在不起集群的情况下
/// 断言"掷出的金额真的被记到了拿钱那个人头上、且只记一次"，而直接依赖 Grain 会把这条断言
/// 吞成 catch 里的"什么都没发生"（本工程有过跨 Grain 编排被 catch 吞掉的教训）。</para>
/// <para>返回值语义也是同一条纪律：**只有回执里"余额真的加了这么多"才算成功**，
/// 不抛异常不等于到账（钱包那侧越界时一分不入，见 <c>WalletMutation.Reject</c>）。</para>
/// </summary>
public interface ICurrencyWriter
{
    /// <summary>给某角色入账金币；金额非正、钱包拒绝（越界/未登记）或被吞异常都返回 false。</summary>
    Task<bool> AddGoldAsync(ulong characterId, long amount, string source, CancellationToken cancellationToken = default);
}

/// <summary>
/// 任务持有状态（条件掉落的判据）。
/// <para><see cref="Unknown"/> 与 <see cref="NotHeld"/> 必须分开：前者是"任务系统没答上来"，
/// 后者是"玩家确实没接这个任务"。把两者混成一个 false，装配缺失（reader 没接、grain 超时）
/// 就会伪装成"玩家不满足条件"，而这两种情况的处置完全相反。</para>
/// </summary>
public enum QuestHoldState
{
    /// <summary>问不出来（任务系统不可用/超时）。</summary>
    Unknown = 0,

    /// <summary>玩家没有持有该任务。</summary>
    NotHeld = 1,

    /// <summary>玩家持有该任务（进行中）。</summary>
    Held = 2,
}

/// <summary>
/// 任务状态读取端（条件掉落用它判"这个玩家配不配拿这件东西"）。
/// <para>为什么判在业务层而不是 DS：DS 手里没有玩家的任务状态，而"DS 猜一个条件"的结果是
/// 条件掉落变成人人有份。</para>
/// </summary>
public interface IQuestStateReader
{
    /// <summary>查某角色是否持有某任务。</summary>
    Task<QuestHoldState> QueryHoldAsync(ulong characterId, int questId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取某角色**进行中**的全部任务 id（条件件在尸体上要一次判多个槽，逐槽 RPC 不划算）。
    /// <para>缺省实现返回 null = "这一端不提供"：调用方按"判不了→不满足"处理，
    /// 表现为条件槽取不了（而不是发给不该拿的人）。已有实现方不必改。</para>
    /// </summary>
    Task<IReadOnlyCollection<int>?> ActiveQuestIdsAsync(ulong characterId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<int>?>(null);
}

/// <summary>
/// 怪物模板 → 五行域 的解析口（掉落与好感共用）。
/// <para>刻意做成接口而不是静态调用：这样击杀事件里"用的是哪一列"能在测试里被断言，
/// 而不是依赖 GrainFactory/静态目录的存在与否（后者在脚手架里会被 catch 吞成"什么都没发生"）。</para>
/// </summary>
public interface IMonsterDomainResolver
{
    /// <summary>取模板的五行域；未定域返回 0。</summary>
    int ResolveDomain(int monsterTemplateId);
}

/// <summary>默认解析：读 <see cref="Horizon.Game.Sim.MonsterTemplateCatalog"/> 的 WuxingDomain 列。</summary>
public sealed class MonsterTemplateDomainResolver : IMonsterDomainResolver
{
    /// <inheritdoc />
    public int ResolveDomain(int monsterTemplateId)
        => monsterTemplateId > 0 ? Horizon.Game.Sim.MonsterTemplateCatalog.DomainOf(monsterTemplateId) : 0;
}

/// <summary>
/// 背包写入端（Orleans 实现走 <c>IInventoryGrain.AddItemAsync</c>）。
/// <para>
/// 以 <see cref="Guid"/> 作玩家键，是为了对齐既有 <c>IInventoryGrain : IGrainWithGuidKey</c>；
/// ulong 角色 ID 到 Guid 的换算集中在 <see cref="GrainKeyMapper"/>，避免各处各写一套。
/// </para>
/// </summary>
public interface IInventoryWriter
{
    /// <summary>给玩家加物品；返回是否成功。</summary>
    Task<bool> AddItemAsync(Guid playerId, int templateId, int quantity, int quality, CancellationToken cancellationToken = default);
}

/// <summary>
/// 任务击杀进度写入端（Orleans 实现走玩家维度 <c>IQuestGrain</c>）。
/// </summary>
public interface IQuestProgressWriter
{
    /// <summary>
    /// 推进某角色针对某怪物模板的击杀类任务目标。
    /// </summary>
    /// <returns>实际被推进的目标数量（0 表示该玩家没有匹配的进行中任务）。</returns>
    Task<int> AdvanceKillObjectiveAsync(ulong characterId, int monsterTemplateId, int count, CancellationToken cancellationToken = default);
}

/// <summary>
/// 任务目标匹配器：判断某个任务目标是否应当被"击杀某怪物"推进。
/// <para>
/// <b>为什么需要它</b>：既有 <c>QuestObjectiveData</c> 只有字符串 <c>ObjectiveType</c> 与
/// <c>Description</c>，**没有数值型目标 ID**，因此无法可靠地判断"这个目标是杀这只怪"。
/// 本接口把这一判断显式外化，使：
/// <list type="bullet">
///   <item>匹配策略可替换、可测试；</item>
///   <item>"数据模型缺数值目标 ID"这件事在代码里可见，而不是被硬编码的字符串匹配掩盖。</item>
/// </list>
/// 后续应由任务系统在 <c>QuestObjectiveData</c> 上补一个数值 <c>TargetId</c>，
/// 届时换上精确匹配实现即可，本接口与调用方无需改动。
/// </para>
/// </summary>
public interface IQuestKillObjectiveMatcher
{
    /// <summary>该目标是否应被击杀事件推进。</summary>
    /// <param name="objectiveType">目标类型字符串（既有模型）。</param>
    /// <param name="description">目标描述（既有模型）。</param>
    /// <param name="monsterTemplateId">被击杀的怪物模板 ID。</param>
    bool ShouldAdvance(string objectiveType, string description, int monsterTemplateId);
}

/// <summary>
/// 默认匹配器：按目标类型字符串识别"击杀类"目标，并支持**按五行域限定**。
/// <para>
/// 目标类型串两种写法都认：
/// <list type="bullet">
///   <item><c>kill</c> / <c>击杀</c> / <c>hunt</c> …：任意生物都推进（遗留语义）。
///         在"杀 5 只狼 + 杀 3 只熊"这种任务里会串，这是既有数据模型的局限，不在本次改动范围内扩大它。</item>
///   <item><c>kill_domain:3</c>：只有<b>被杀怪所属五行域</b>等于 3 才推进。域号来自
///         <see cref="MonsterTemplateCatalog"/> 的 <c>WuxingDomain</c> 一列——与刷怪掉落读的是同一列，
///         所以"任务要杀的水域生物"和"掉水域书的怪"不可能是两套东西。</item>
/// </list>
/// </para>
/// <para>
/// <b>域号解析失败一律不推进</b>（不是"当成任意击杀"）：写成 <c>kill_domain:</c> 或 <c>kill_domain:9</c>
/// 的目标本来就没法满足，把它降级成"任意击杀"会让一个配错的任务悄悄能交，
/// 而玩家看到的是"随便杀点就过了"——这比任务卡住更难被发现。配错应在接任务那侧就被拒（见 <c>QuestGrain</c>）。
/// </para>
/// </summary>
public sealed class KillObjectiveTypeMatcher : IQuestKillObjectiveMatcher
{
    private static readonly string[] KillTokens = { "kill", "击杀", "杀", "hunt", "狩猎", "讨伐" };

    /// <summary>域限定目标的前缀（类型串形如 <c>kill_domain:3</c>）。</summary>
    public const string DomainPrefixToken = "kill_domain";

    /// <summary>
    /// 模板限定目标的前缀（类型串形如 <c>kill_monster:101</c>）。
    /// <para>任务编辑器的"击杀某只怪"走这一条：关键词兜底只服务历史数据（裸 <c>kill</c>），
    /// 新草稿一律带对象 id，否则"杀 5 只金域怪"和"杀 5 只任意怪"在现场看不出区别。</para>
    /// </summary>
    public const string MonsterPrefixToken = "kill_monster";

    private readonly IMonsterDomainResolver _domains;

    /// <summary>创建匹配器。</summary>
    /// <param name="domains">怪物模板 → 五行域 解析；null 用读怪物模板目录的默认实现。</param>
    public KillObjectiveTypeMatcher(IMonsterDomainResolver? domains = null)
        => _domains = domains ?? new MonsterTemplateDomainResolver();

    /// <inheritdoc />
    public bool ShouldAdvance(string objectiveType, string description, int monsterTemplateId)
    {
        if (monsterTemplateId <= 0) return false;

        var type = (objectiveType ?? string.Empty).Trim();

        if (type.StartsWith(DomainPrefixToken, StringComparison.OrdinalIgnoreCase))
        {
            var colon = type.IndexOf(':');
            if (colon < 0 || colon + 1 >= type.Length) return false;
            if (!int.TryParse(type.Substring(colon + 1).Trim(), out var wantDomain)) return false;
            if (wantDomain < 1 || wantDomain > 5) return false;

            return _domains.ResolveDomain(monsterTemplateId) == wantDomain;
        }

        for (var i = 0; i < KillTokens.Length; i++)
        {
            if (type.Contains(KillTokens[i], StringComparison.OrdinalIgnoreCase))
            {
                // 带模板限定串的目标（kill_monster:<id>）在这里就要按 id 收敛：
                // 关键词匹配是历史兜底，不能让它把"指定怪"降级成"任意怪"。
                if (type.StartsWith(MonsterPrefixToken, StringComparison.OrdinalIgnoreCase))
                {
                    var monsterColon = type.IndexOf(':');
                    if (monsterColon < 0 || monsterColon + 1 >= type.Length) return false;
                    if (!int.TryParse(type.Substring(monsterColon + 1).Trim(), out var wantTemplate)) return false;
                    return wantTemplate > 0 && monsterTemplateId == wantTemplate;
                }

                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// 角色 ID ↔ Orleans Grain 键 的换算（单一来源，避免各处各写一套）。
/// </summary>
public static class GrainKeyMapper
{
    /// <summary>
    /// 把 <c>ulong</c> 角色 ID 映射为 <c>IInventoryGrain</c> 所需的 <see cref="Guid"/>。
    /// <para>
    /// 方案：Guid 的高 8 字节为 0，低 8 字节按**大端**写入角色 ID。
    /// 选择该方案的三个理由：① 保持原数值的字典序（便于范围扫描/排障时肉眼比对）；
    /// ② 不引入哈希，无碰撞；③ 双向可逆（见 <see cref="ToCharacterId"/>）。
    /// </para>
    /// <para>
    /// <b>部署约束</b>：该映射假设角色 ID 在集群内唯一。若将来引入"分服自增 ID"，
    /// 必须改为显式的全局映射表（或在 ID 高位编入 shard），否则不同服的同号角色会共用一个背包。
    /// </para>
    /// </summary>
    public static Guid ToInventoryKey(ulong characterId)
    {
        Span<byte> bytes = stackalloc byte[16];
        // 高 8 字节保持 0（bytes[0..7] 已为零）
        bytes[8] = (byte)(characterId >> 56);
        bytes[9] = (byte)(characterId >> 48);
        bytes[10] = (byte)(characterId >> 40);
        bytes[11] = (byte)(characterId >> 32);
        bytes[12] = (byte)(characterId >> 24);
        bytes[13] = (byte)(characterId >> 16);
        bytes[14] = (byte)(characterId >> 8);
        bytes[15] = (byte)characterId;
        return new Guid(bytes);
    }

    /// <summary>把 <see cref="ToInventoryKey"/> 产出（或任意 Guid）还原为角色 ID。</summary>
    public static ulong ToCharacterId(Guid key)
    {
        Span<byte> bytes = stackalloc byte[16];
        key.TryWriteBytes(bytes);
        ulong value = 0;
        for (var i = 8; i < 16; i++)
        {
            value = (value << 8) | bytes[i];
        }
        return value;
    }
}
