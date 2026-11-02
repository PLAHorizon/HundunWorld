using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// 运行期使用的怪物内容（装配后的不可变快照）。
/// </summary>
/// <remarks>
/// <b>为什么内核要拿这一份而不是直接查表</b>：<c>Horizon.Game.Sim</c> 不许有存储依赖，
/// 而权威端每秒要问几百次。装配器（Silo / DsHost）在启动或 publish 时把六张表压成这一份，
/// 内核只读。<b>装配必须是原子的</b>（整份替换引用）：半份新配置进 tick 会让金轨迹不可复现。
/// </remarks>
public sealed class MonsterContentBundle
{
    /// <summary>内容号（每次整替 +1；诊断与握手用）。</summary>
    public int ContentRevision;

    /// <summary>模板 + 行为（按编号索引）。</summary>
    public readonly Dictionary<int, MonsterRuntimeConfig> ByTemplateId = new Dictionary<int, MonsterRuntimeConfig>();

    /// <summary>刷怪点。</summary>
    public readonly List<MonsterSpawnSnapshot> SpawnPoints = new List<MonsterSpawnSnapshot>();

    /// <summary>巡逻路线（按路线 id 索引）。</summary>
    public readonly Dictionary<int, MonsterPatrolRoute> Routes = new Dictionary<int, MonsterPatrolRoute>();

    /// <summary>掉落绑定（按怪物编号分组，内部按绑定 id 稳定序）。</summary>
    public readonly Dictionary<int, List<MonsterLootBindingSnapshot>> LootBindings = new Dictionary<int, List<MonsterLootBindingSnapshot>>();

    /// <summary>
    /// 喊话/起手文本（按怪物编号分组，内部按 <c>(Group, Seq)</c> 稳定序）。
    /// <para>为什么在内容包里而不是库里：文本是**展示物**，权威在插件草稿 + 内容快照文件
    /// （与外观表同一口径：<c>client_visuals.json</c> 那条链）——库里只存"这只怪进战要念第几号"。</para>
    /// </summary>
    public readonly Dictionary<int, List<MonsterTextSnapshot>> Texts = new Dictionary<int, List<MonsterTextSnapshot>>();

    public int TemplateCount => ByTemplateId.Count;
    public int SpawnPointCount => SpawnPoints.Count;

    /// <summary>
    /// 这只怪有没有配这一号文本。**判据只有这一处**：
    /// 运行期（要不要念）与装配期（指针落不落空）问的是同一个问题，写成两份迟早分家。
    /// </summary>
    public bool HasText(int templateId, int sayId)
    {
        if (sayId <= 0) return false;
        if (!Texts.TryGetValue(templateId, out var list)) return false;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].SayId == sayId) return true;
        }
        return false;
    }
}

/// <summary>
/// 一只怪的一条文本（AzerothCore 的 <c>CreatureTextEntry</c> 形状：
/// <c>(entry, group, index, text, emote)</c>，语言档与音效本作刻意没抄）。
/// <para><see cref="SayId"/> 是**行为表那一格指向它的号**（<c>aggro_say_id</c>）：
/// 线上只发这个号，文案随客户端包发布——把文案当身份用，翻译一次就会作废全部旧引用
/// （这条工程里已经栽过一次：分组 id 曾是分类名的哈希）。</para>
/// </summary>
public sealed class MonsterTextSnapshot
{
    /// <summary>文本号（一只怪内部唯一；行为表 <c>aggro_say_id</c> 指的就是它）。</summary>
    public int SayId;

    /// <summary>组（AC 的 <c>group</c>：同一组内按权重选一条；本作先按"一组一条"用）。</summary>
    public int Group;

    /// <summary>组内序号（稳定序用，不参与选择）。</summary>
    public int Seq;

    /// <summary>文案本体。**只在服务端校验"这一号存不存在"时用**，下发只发号。</summary>
    public string Text = string.Empty;

    /// <summary>起手动作（AC 的 <c>emote</c>；0 = 不做动作）。</summary>
    public int Emote;
}

/// <summary>一只怪的运行期配置（模板数值公式 + 行为 + 技能挂载）。</summary>
public sealed class MonsterRuntimeConfig
{
    public int TemplateId;
    public string Name = string.Empty;
    public MonsterRank Rank;
    public int Species;
    public int WuxingDomain;
    public string ModelKey = string.Empty;
    public string? PrefabGuid;
    public float ModelScale = 1f;
    public int StandState;
    public int LevelBase;
    public int LevelMin = 1;
    public int LevelMax = 1;
    public int AggroSayId;
    public float CorpseSeconds = 60f;

    /// <summary>尸体可拾取窗口秒（AC 缺省 60；内容列可覆盖）。</summary>
    public float LootWindowSeconds = 60f;

    /// <summary>拾取交互距离米（AC INTERACTION_DISTANCE 缺省 5.5）。</summary>
    public float PickupDistanceMeters = 5.5f;

    public MonsterStatsConfig Stats;
    public MonsterXpConfig Xp;

    /// <summary>刷新秒公式（截距 + 每级 + rank 倍率由内核算）。</summary>
    public float RespawnBaseSeconds = 15f;
    public float RespawnPerLevelSeconds;

    public MonsterBehaviorConfig Behavior = MonsterBehaviorConfig.Default;

    /// <summary>技能挂载（必须已按 SlotSeq 升序排好——顺序是数据，不是插入历史）。</summary>
    public MonsterSkillMount[] Skills = Array.Empty<MonsterSkillMount>();
}

/// <summary>刷怪点快照（数值一律由模板派生，这里只有位置与规模）。</summary>
public readonly struct MonsterSpawnSnapshot
{
    public readonly int SpawnPointId;
    public readonly int ZoneId;
    public readonly long ShardId;
    public readonly float X;
    public readonly float Y;
    public readonly float Z;
    public readonly float Yaw;
    public readonly int MonsterTemplateId;
    public readonly int? LevelOverride;
    public readonly int MaxAlive;
    public readonly float SlotSpacing;
    public readonly float RespawnOverrideSeconds;
    public readonly int RouteOverrideId;

    public MonsterSpawnSnapshot(
        int spawnPointId, int zoneId, long shardId,
        float x, float y, float z, float yaw,
        int monsterTemplateId, int? levelOverride,
        int maxAlive, float slotSpacing, float respawnOverrideSeconds, int routeOverrideId)
    {
        SpawnPointId = spawnPointId;
        ZoneId = zoneId;
        ShardId = shardId;
        X = x;
        Y = y;
        Z = z;
        Yaw = yaw;
        MonsterTemplateId = monsterTemplateId;
        LevelOverride = levelOverride;
        MaxAlive = maxAlive;
        SlotSpacing = slotSpacing;
        RespawnOverrideSeconds = respawnOverrideSeconds;
        RouteOverrideId = routeOverrideId;
    }

    /// <summary>实际生效等级：覆盖优先，否则模板基准。</summary>
    public int ResolveLevel(in MonsterRuntimeConfig config)
        => LevelOverride.HasValue && LevelOverride.Value > 0 ? LevelOverride.Value : config.LevelBase;
}

/// <summary>掉落绑定快照（一怪 N 表）。</summary>
public readonly struct MonsterLootBindingSnapshot
{
    public readonly int LootTableId;
    public readonly int RollChanceBps;
    public readonly int MinCount;
    public readonly int MaxCount;
    public readonly int ConditionKind;
    public readonly int QuestId;
    public readonly int MoneyMin;
    public readonly int MoneyMax;
    public readonly MonsterDropSnapshot[] Entries;

    public MonsterLootBindingSnapshot(
        int lootTableId, int rollChanceBps, int minCount, int maxCount,
        int conditionKind, int questId, int moneyMin, int moneyMax, MonsterDropSnapshot[] entries)
    {
        LootTableId = lootTableId;
        RollChanceBps = rollChanceBps;
        MinCount = minCount;
        MaxCount = maxCount;
        ConditionKind = conditionKind;
        QuestId = questId;
        MoneyMin = moneyMin;
        MoneyMax = moneyMax;
        Entries = entries ?? Array.Empty<MonsterDropSnapshot>();
    }
}

/// <summary>逐件掉落。</summary>
public readonly struct MonsterDropSnapshot
{
    public readonly int ItemId;
    public readonly int ChanceBps;
    public readonly int CountMin;
    public readonly int CountMax;

    /// <summary>掷骰顺序（库里 seq 列）；装配层按它升序排，内核的下标盐值因此才是数据而不是查询顺序。</summary>
    public readonly int Seq;

    /// <summary>
    /// 掉落组号：<c>0</c> = 逐件独立掷（历史口径，逐字不变）；<c>&gt;0</c> = 同组互斥，整组最多出一件。
    /// <para>这是魔兽的口径：<c>creature_loot_template.GroupId</c> 把若干行捆成一个互斥桶，
    /// 一次掷骰只让一个成员中签（AzerothCore <c>LootGroup::Roll</c>，LootMgr.cpp:1292-1327）。
    /// 没有它，"从这 10 件里出 1 件"只能写成 10 条 10% 独立掷——
    /// 那既会一次掉出 3 件，又永远做不到"必出 1 件"，而编辑器里每件的概率看着都对。</para>
    /// </summary>
    public readonly int GroupId;

    /// <summary>
    /// 这一件自己的掉落条件：<c>-1</c> = 继承所属绑定（加这一列之前的行为），其余与
    /// <c>MonsterLootCore.Condition*</c> 同义。
    /// <para>默认必须是"继承"而不是 0：<see cref="MonsterLootCore.ConditionNone"/> 恰好就是 0，
    /// 旧快照文件里没有这个键、反序列化后落到初值，若初值是 0 就等于**把所有任务物品悄悄改成人人可拿**，
    /// 而库里、编辑器、快照三处的"条件"数字看着都没变。</para>
    /// </summary>
    public readonly int ConditionKind;

    /// <summary>条件=任务持有者时的小任务 id；<c>0</c> = 用绑定上那一份。</summary>
    public readonly int QuestId;

    public MonsterDropSnapshot(int itemId, int chanceBps, int countMin, int countMax, int seq = 0, int groupId = 0,
        int conditionKind = -1, int questId = 0)
    {
        ItemId = itemId;
        ChanceBps = chanceBps;
        CountMin = countMin;
        CountMax = countMax;
        Seq = seq;
        GroupId = groupId;
        ConditionKind = conditionKind;
        QuestId = questId;
    }
}

/// <summary>
/// 怪物模板数据的可替换来源。
/// <para>
/// 这是 <c>MonsterTemplateCatalog</c> 类注释里承诺过的那次替换："生产环境应由配置下发表数据替换，
/// 届时只改 <c>Get</c> 的实现，调用点不动"。<b>默认源仍是硬编码 11 行</b>：
/// 装配器不存在时（单元测试、精简宿主）行为与替换前逐字相同，不会"接了库就改了平衡"。
/// </para>
/// </summary>
public interface IMonsterTemplateSource
{
    /// <summary>取模板；未登记返回 false。</summary>
    bool TryGet(int templateId, out MonsterRuntimeConfig config);

    /// <summary>取全部已登记模板（对账/白名单用）。</summary>
    IReadOnlyCollection<int> RegisteredTemplateIds();
}

/// <summary>
/// 怪物模板与内容包的进程内安装点（宿主在启动或 publish 时调用）。
/// </summary>
/// <remarks>
/// <b>为什么用"安装 + 读回"而不是静态字典直接改</b>：目录类今天是静态查表，
/// 把它的存储换成可写字典会让"谁在什么时候改了内容"无从追溯。
/// 整份替换 <see cref="Installed"/> 引用 = 一次装配一个版本号，诊断能说出"现在跑的是 rev 几"。
/// </remarks>
public static class MonsterContent
{
    private static IMonsterTemplateSource? _installed;

    /// <summary>当前装配的来源；null = 用硬编码目录兜底。</summary>
    public static IMonsterTemplateSource? Installed => _installed;

    /// <summary>最近一次装配的内容号。</summary>
    public static int Revision { get; private set; }

    /// <summary>装配内容包（原子替换）。返回被替换掉的旧内容号。</summary>
    public static int Install(MonsterContentBundle bundle)
    {
        if (bundle == null) throw new ArgumentNullException(nameof(bundle));
        var previous = Revision;
        _installed = new BundleSource(bundle);
        Revision = bundle.ContentRevision;
        return previous;
    }

    /// <summary>退回硬编码目录（测试与"库里还没东西"的开发期用）。</summary>
    public static void ResetToHardcoded()
    {
        _installed = null;
        Revision = 0;
    }

    /// <summary>取模板（未装配时回退硬编码目录的展示信息，等级 1 兜底口径不变）。</summary>
    public static bool TryGetConfig(int templateId, out MonsterRuntimeConfig config)
    {
        var source = _installed;
        if (source != null && source.TryGet(templateId, out config!)) return true;
        config = null!;
        return false;
    }

    /// <summary>已登记编号（未装配时为空集，调用方据此决定是否回退到硬编码目录）。</summary>
    public static IReadOnlyCollection<int> RegisteredIds()
    {
        var source = _installed;
        if (source == null) return Array.Empty<int>();
        return source.RegisteredTemplateIds();
    }

    /// <summary>取刷怪点；未装配返回空。</summary>
    public static IReadOnlyList<MonsterSpawnSnapshot> SpawnPoints()
    {
        var source = _installed as BundleSource;
        if (source == null) return Array.Empty<MonsterSpawnSnapshot>();
        return source.Bundle.SpawnPoints;
    }

    /// <summary>取路线；不存在返回 null。</summary>
    public static MonsterPatrolRoute? Route(int routeId)
    {
        var source = _installed as BundleSource;
        if (source == null || routeId <= 0) return null;
        MonsterPatrolRoute route;
        return source.Bundle.Routes.TryGetValue(routeId, out route) ? route : null;
    }

    /// <summary>取该怪的掉落绑定；未装配返回空。</summary>
    public static IReadOnlyList<MonsterLootBindingSnapshot> LootBindings(int templateId)
    {
        var source = _installed as BundleSource;
        if (source == null) return Array.Empty<MonsterLootBindingSnapshot>();
        List<MonsterLootBindingSnapshot> bindings;
        return source.Bundle.LootBindings.TryGetValue(templateId, out bindings)
            ? bindings
            : Array.Empty<MonsterLootBindingSnapshot>();
    }

    /// <summary>取该怪的文本；未装配返回空。</summary>
    public static IReadOnlyList<MonsterTextSnapshot> Texts(int templateId)
    {
        var source = _installed as BundleSource;
        if (source == null) return Array.Empty<MonsterTextSnapshot>();
        List<MonsterTextSnapshot> texts;
        return source.Bundle.Texts.TryGetValue(templateId, out texts)
            ? texts
            : Array.Empty<MonsterTextSnapshot>();
    }

    /// <summary>
    /// 这只怪有没有配这一号文本（运行期"要不要念"与校验期"指针落不落空"共用一条判据）。
    /// <para>刻意返回 bool 而不是文本：服务端**不需要文案**，把文案递到运行期只会诱使
    /// 有人把它塞进实时包（那条路一旦走了，翻译就作废全部旧引用）。
    /// 查表本身住在 <see cref="MonsterContentBundle.HasText"/>——装配期那份还没安装的包也要问同一个地方。</para>
    /// </summary>
    public static bool HasText(int templateId, int sayId)
    {
        var source = _installed as BundleSource;
        return source != null && source.Bundle.HasText(templateId, sayId);
    }

    /// <summary>
    /// 进战该念哪一号：`configuredSayId` 指到一条文本时，若它属于某个**组**（`Group &gt; 0`），
    /// 就在组内按 `(Seq, SayId)` 稳定序用 `salt` 挑一条；否则（组 0 / 组内只有一条）就是它自己。
    /// <para>为什么要有这一层：AC 的 `creature_text` 是 `(entry, group, id, text)`——同一个事件组里能写多句，
    /// 一次触发随机挑一句（"受死吧！" / "又一个送死的" / "血肉之躯……"）。本作此前只落了"一组一条"，
    /// 于是 `Group` 与 `Seq` 两列运行期**零读**：哪怕文件里写了三句，游戏里也永远只念行为表指向的那一句。</para>
    /// <para>返回 0 = 指的那一号根本不存在（调用方要单独计"指针落空"，不能和"没配"混成一格）。
    /// 返回值一定是**真实存在的那一号**：线上仍只发号，客户端按 (modelKey, sayId) 查文案的口径不变。</para>
    /// </summary>
    public static int ResolveSayId(int templateId, int configuredSayId, ulong salt)
    {
        if (configuredSayId <= 0) return 0;
        var texts = Texts(templateId);
        if (texts.Count == 0) return 0;

        var anchor = FindText(texts, configuredSayId);
        if (anchor == null) return 0;
        if (anchor.Group <= 0) return configuredSayId;

        var group = new List<MonsterTextSnapshot>(4);
        for (var i = 0; i < texts.Count; i++)
        {
            if (texts[i].Group == anchor.Group) group.Add(texts[i]);
        }
        if (group.Count <= 1) return configuredSayId;
        // 稳定序：内容包里的顺序是数据（按 (Group, Seq) 排好），但这里不依赖装配方，
        // 自己按 (Seq, SayId) 再排一遍——同一份内容无论怎么装，选中的都是同一号。
        group.Sort(static (a, b) =>
        {
            var bySeq = a.Seq.CompareTo(b.Seq);
            return bySeq != 0 ? bySeq : a.SayId.CompareTo(b.SayId);
        });
        return group[(int)(salt % (ulong)group.Count)].SayId;
    }

    private static MonsterTextSnapshot? FindText(IReadOnlyList<MonsterTextSnapshot> texts, int sayId)
    {
        for (var i = 0; i < texts.Count; i++)
        {
            if (texts[i].SayId == sayId) return texts[i];
        }
        return null;
    }

    /// <summary>
    /// 死亡台词所在的组号（AC 的 <c>creature_text.group</c> 里每一组对应一次事件；本作只约定这一个数）。
    /// <para>为什么用"组"而不是再加一列：死亡喊话和进战喊话是**同一种数据**（一句展示文本），
    /// 差别只在"哪一拍念"。行为表那格 <c>aggro_say_id</c> 指的是进战那一句；死亡这一句不需要指针——
    /// 只要它落在这一组里就会在死亡时被挑。加一列要动迁移与五层接线，为一句台词不值得。</para>
    /// <para><b>0 与 1 留给既有内容</b>（0 = 不成组，现有台词全在这一档；1 未占用），所以死亡用 2。</para>
    /// </summary>
    public const int DeathSayGroup = 2;

    /// <summary>
    /// 这只怪临死该念哪一号：取 <see cref="DeathSayGroup"/> 那一组，按 <c>(Seq, SayId)</c> 稳定序用 <c>salt</c> 挑一句。
    /// 没有这一组的行时返回 0（调用方**什么都不发**——与"没配"同形，这是刻意的：绝大多数怪死时不出声）。
    /// </summary>
    public static int ResolveDeathSayId(int templateId, ulong salt)
        => ResolveSayIdInGroup(templateId, DeathSayGroup, salt);

    /// <summary>
    /// 被求援叫起来时念的那一组（"来援了"这类回应）。
    /// <para><b>与本作求援机制的对应关系写清楚</b>：AC 那句是**呼喊者**喊的，而我们的内核只回传
    /// "我（被叫的一方）这次是被人叫起来的"（<c>MonsterRuntimeResult.SeededByAssist</c>），
    /// 呼喊者那一侧没有 id 回执。所以先落在**被叫者**身上，并如实命名成"应援台词"；
    /// 要让呼喊者开口，得让内核多回一格 callerId（那是另一件事）。</para>
    /// </summary>
    public const int AssistSayGroup = 3;

    /// <summary>被求援叫起来时该念哪一号：<see cref="AssistSayGroup"/> 那一组里按 (Seq, SayId) 挑一句；没配回 0。</summary>
    public static int ResolveAssistSayId(int templateId, ulong salt)
        => ResolveSayIdInGroup(templateId, AssistSayGroup, salt);

    /// <summary>
    /// 从某一组里挑一句（组内按 <c>(Seq, SayId)</c> 稳定序，用 <c>salt</c> 取模）。
    /// <para>死亡与应援共用这一个：两份挑法各写一遍，早晚在"只有一句"或"序号相等"上分家。</para>
    /// </summary>
    public static int ResolveSayIdInGroup(int templateId, int group, ulong salt)
    {
        if (group <= 0) return 0;
        var texts = Texts(templateId);
        if (texts.Count == 0) return 0;
        var members = new List<MonsterTextSnapshot>(2);
        for (var i = 0; i < texts.Count; i++)
        {
            if (texts[i].Group == group) members.Add(texts[i]);
        }
        if (members.Count == 0) return 0;
        if (members.Count == 1) return members[0].SayId;
        members.Sort(static (a, b) =>
        {
            var bySeq = a.Seq.CompareTo(b.Seq);
            return bySeq != 0 ? bySeq : a.SayId.CompareTo(b.SayId);
        });
        return members[(int)(salt % (ulong)members.Count)].SayId;
    }

    /// <summary>把内容包包装成来源接口（保持 <see cref="IMonsterTemplateSource"/> 只有一个实现，避免两套查表代码）。</summary>
    private sealed class BundleSource : IMonsterTemplateSource
    {
        public readonly MonsterContentBundle Bundle;

        public BundleSource(MonsterContentBundle bundle) => Bundle = bundle;

        public bool TryGet(int templateId, out MonsterRuntimeConfig config)
        {
            MonsterRuntimeConfig found;
            if (Bundle.ByTemplateId.TryGetValue(templateId, out found))
            {
                config = found;
                return true;
            }
            config = null!;
            return false;
        }

        public IReadOnlyCollection<int> RegisteredTemplateIds() => Bundle.ByTemplateId.Keys;
    }
}
