using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Monsters;

/// <summary>一次掉落掷骰的产出（一件物品 + 数量 + 它挂的条件）。</summary>
public readonly struct MonsterLootDrop
{
    public readonly int ItemId;
    public readonly int Count;

    /// <summary>0 = 无条件直接给；1 = 需要持有该任务（由业务层判，DS 不猜）。</summary>
    public readonly int ConditionKind;

    public readonly int QuestId;

    /// <summary>这件掉落来自哪个绑定的第几个条目（诊断与幂等键用）。</summary>
    public readonly int BindingIndex;

    public readonly int EntryIndex;

    public MonsterLootDrop(int itemId, int count, int conditionKind, int questId, int bindingIndex, int entryIndex)
    {
        ItemId = itemId;
        Count = count;
        ConditionKind = conditionKind;
        QuestId = questId;
        BindingIndex = bindingIndex;
        EntryIndex = entryIndex;
    }
}

/// <summary>掷骰结果的账（每一条出口都要有自己的计数——否则"什么都没掉"与"掉了但没发"同形）。</summary>
public sealed class MonsterLootRollResult
{
    /// <summary>可无条件入包的掉落。</summary>
    public readonly List<MonsterLootDrop> Drops = new List<MonsterLootDrop>();

    /// <summary>需要业务层按任务状态决定是否给的掉落。</summary>
    public readonly List<MonsterLootDrop> ConditionalDrops = new List<MonsterLootDrop>();

    /// <summary>金币（本次击杀的总数额；0 = 没配钱）。</summary>
    public int Money;

    public int BindingsRolled;
    public int BindingsMissedGate;
    public int EntriesRolled;
    public int EntriesMissedChance;

    /// <summary>条件类型本层判不了、已按"不给"处理的条数（组队/稀有度门槛）。</summary>
    public int EntriesDeferredUnknownCondition;

    /// <summary>命中了但物品 id 非法（&lt;=0）的条数——它既不进包也不该被当成"掉了"。</summary>
    public int EntriesSkippedNoItem;

    /// <summary>参与掷骰的组轮数（一个组被重掷几轮由所属表的"抽取件数区间"决定；每轮最多一件）。</summary>
    public int GroupsRolled;

    /// <summary>被组通路消费的条目数（这些条目不再逐件独立掷，改由组内累计减法定胜负）。</summary>
    public int GroupEntriesRolled;

    /// <summary>掷出了胜者的组轮数（同一轮里只可能有一件）。</summary>
    public int GroupsPicked;

    /// <summary>整组空手的组数（显式档全不中且没有等概率档成员）。</summary>
    public int GroupsRolledEmpty;

    /// <summary>胜者来自等概率档（组内概率填 0 的那些成员）的组轮数。</summary>
    public int GroupEqualChancedPicks;

    /// <summary>因为组内某件概率 &gt;=100% 而短路、连减法都没轮到的后排成员数（魔兽里这是配错，会打日志）。</summary>
    public int GroupShadowedByGuaranteed;

    /// <summary>表级"抽几件"多花出去的组轮数（= GroupsRolled - 组数）。</summary>
    public int ExtraGroupRoundsRolled;

    /// <summary>同一条目被重掷再次抽中而多给的件数（魔兽的任务件不乘，见 <see cref="GroupQuestItemStopsMultiplying"/>）。</summary>
    public int GroupRepeatWins;

    /// <summary>因为中签的是任务件而**停止**继续重掷的组数（AC：<c>if (nonRefIterationsLeft &gt; 1 &amp;&amp; !item-&gt;needs_quest)</c>）。</summary>
    public int GroupQuestItemStopsMultiplying;

    /// <summary>至少中签过一次的条目数（多轮重掷下，同一条目只计一次——对账要用它而不是件数）。</summary>
    public int EntriesGranted;

    /// <summary>组里一次都没中签的条目数（对账用：组成员数 - 组内中签过的条目数）。</summary>
    public int GroupEntriesLost => GroupEntriesRolled - GroupEntriesGranted;

    /// <summary>组内中签过的条目数。</summary>
    public int GroupEntriesGranted;

    /// <summary>因为尸体件数上限（<see cref="MaxNormalLootItems"/> / <see cref="MaxQuestLootItems"/>）被丢掉的**件数**。</summary>
    public int ItemsDroppedOverCapacity;

    /// <summary>
    /// 因超限而一次都没给、且不属于任何互斥组的条目数（并入 <see cref="Accounted"/> 恒等式）。
    /// <para>组条目不在这里重复计：它们没中签时**本来**就落在 <see cref="GroupEntriesLost"/> 里，
    /// 两边都记会让恒等式凑巧对上一个错数。</para>
    /// </summary>
    public int EntriesDroppedOverCapacity;

    /// <summary>
    /// 掷过骰、进了任何一条出口的**条目**数（对账用）。
    /// <para>恒等式 <c>Accounted == EntriesRolled</c> 必须由测试坐实：少一条出口，
    /// "什么都没掉"和"掉了但没发"就长成同一个样子。
    /// 表级重掷让"件数"与"条目数"不再相等（同一条目可以掉两次），所以这里一律按**条目去重**计：
    /// 多出来的件数由 <see cref="GroupRepeatWins"/> 单独报，别把它混进这条恒等式。</para>
    /// </summary>
    public int Accounted => EntriesGranted + EntriesDroppedOverCapacity
        + EntriesDeferredUnknownCondition + EntriesMissedChance + EntriesSkippedNoItem + GroupEntriesLost;
}

/// <summary>
/// 怪物掉落掷骰（MMORPG 目标里的"掉落物品及掉落几率"那一半的**唯一算法**）。
/// <para>
/// <b>填的哪个洞</b>：怪物编辑器早就把"一怪 N 张表 + 逐件独立概率 + 数量区间 + 金币 + 条件"存进了库、
/// 也投影进了内容快照，但运行时一次都没读过——真正在掷的是另一张全局表。
/// 于是策划在编辑器里把某件物品从 5% 改成 50%，游戏里的数字一动也不动，
/// 而库里、快照里、编辑器里三处都显示 50%。
/// </para>
/// <para>
/// <b>确定性</b>：与 <c>CombatCore</c> 同一支 splitmix64（<see cref="CombatCore.Roll01"/>），
/// 输入是 (击杀者, 怪物模板, tick) 加"绑定序号 × 条目序号"的盐——
/// 同一 tick 重放必然得到同一堆掉落，且**同表内两件同物品 ID 的条目各自独立掷**（盐里有序号）。
/// </para>
/// <para>
/// <b>表级"抽几件"落在谁身上</b>：<c>min_count</c>/<c>max_count</c> 是<b>这张表的互斥组重掷几轮</b>
/// （每轮每组最多一件），形状抄自 AzerothCore 的 <c>LootGroup::Process(nonRefIterationsLeft)</c>
/// （LootMgr.cpp:1439-1470 与 1711-1719；那边的次数来自世界配置 <c>RATE_DROP_ITEM_GROUP_AMOUNT</c>，
/// 本作没有世界配置层，所以落在表上——形状相同、宿主不同）。它<b>不作用于组 0 的逐件独立掷</b>：
/// 那些件掉几件本来就由各件概率决定（决策 D4），拿限额去裁它们等于换了一套口径。
/// 抽中任务件时停止重掷（AC 的 <c>!item-&gt;needs_quest</c>）。缺省 1..1 = 单轮 = 加这两格之前逐字相同。
/// </para>
/// <para>
/// <b>两种归属</b>：<c>group_id = 0</c> 是逐件独立掷（WoW 观测到的普通口径：一件的出现不影响另一件，
/// 期望件数是各件概率之和），这条口径连同掷骰盐值一起<b>逐字保留</b>，老数据不会因为加了组而换骰子；
/// <c>group_id &gt; 0</c> 是<b>同组互斥</b>——整组一次掷骰、最多出一件。后者抄的是 AzerothCore 的
/// <c>LootGroup::Roll</c>（LootMgr.cpp:1292-1327）：一个 [0,100) 的骰子按行序累减组内概率，
/// 谁把余额减成负数谁中签，概率 &gt;=100% 的成员直接短路；全组落空时，只在"组内把概率填 0 的那些成员"
/// 里等概率抽一个（<c>EqualChanced</c>，LootMgr.cpp:1283-1289），连它们也没有就整组不掉。
/// 没有组这件事，"从这 10 件里必出 1 件"在旧模型里根本写不出来：
/// 10 条 10% 独立掷既会一次掉 3 件，又永远做不到"必出一件"。
/// </para>
/// </summary>
public static class MonsterLootCore
{
    /// <summary>概率基点满值。</summary>
    public const int BasisPointsMax = 10000;

    /// <summary>
    /// 一具尸体最多放几件**普通**掉落（AzerothCore `Loot::MAX_NR_LOOT_ITEMS` = 18；
    /// 2026-10-29 拍板：「一具尸体的掉件上限 = 18 件 + 32 任务件」）。
    /// <para>AC 在 `Loot::AddItem` 里就是"满了直接丢，只打一条 debug 日志"。这里同样按"丢"处理，
    /// 但把丢掉的件数记进 <see cref="MonsterLootRollResult.ItemsDroppedOverCapacity"/>——
    /// 只丢不记账的话，"这张表配了 30 条必掉"在运行期看起来与"配了 18 条"完全一样。</para>
    /// </summary>
    public const int MaxNormalLootItems = 18;

    /// <summary>一具尸体最多放几件**任务/条件**掉落（AzerothCore `Loot::MAX_NR_QUEST_ITEMS` = 32）。</summary>
    public const int MaxQuestLootItems = 32;

    /// <summary>
    /// 组号上界：魔兽的 <c>groupid</c> 只有 7 位（AzerothCore 的 <c>LootStoreItem::groupid : 7</c>，
    /// 并且 <c>IsValid</c> 把 &gt;=128 的行整条丢弃，LootMgr.cpp:335-339）。我们沿用同一个上限，
    /// 但<b>不照抄"丢弃"</b>——静默丢行会让策划看到"配了不掉"，改成校验器提示（见设计文档 §11.2）。
    /// </summary>
    public const int MaxGroupId = 127;

    /// <summary>条件：无条件。</summary>
    public const int ConditionNone = 0;

    /// <summary>条件：需要持有某个任务（本层不判，交给业务层，因为它才知道任务状态）。</summary>
    public const int ConditionQuestHolder = 1;

    /// <summary>掷骰盐值基（与闪避/暴击/施法那几支区分开）。</summary>
    private const uint LootSaltBase = 0x4C00u;

    /// <summary>组掷骰盐（与逐件、数量的骰子分开——同一个数被两处复用会让"组中签"和"数量"一起漂移）。</summary>
    private const uint GroupRollSalt = LootSaltBase ^ 0x47u;

    /// <summary>组内等概率档抽人的盐。</summary>
    private const uint GroupEqualRollSalt = LootSaltBase ^ 0x45u;

    /// <summary>表级"抽几件"的轮数盐。</summary>
    private const uint GroupRoundsSalt = LootSaltBase ^ 0x52u;

    /// <summary>
    /// 一张表最多让互斥组重掷几轮（表级 <c>min_count</c>/<c>max_count</c> 的上界）。
    /// <para>魔兽把这个次数放在世界配置 <c>RATE_DROP_ITEM_GROUP_AMOUNT</c> 上
    /// （AzerothCore LootMgr.cpp:1711-1719，且只对最外层生效）；本作没有世界配置层，
    /// 于是落在**表**上——形状相同、宿主不同，写清楚是为了不让后来人以为这是魔兽的原样。</para>
    /// </summary>
    public const int MaxTableRounds = 16;

    /// <summary>组掷骰下标里"轮次"这一维的步长：必须大于 <see cref="MaxTableRounds"/>，否则组号与轮次会串位撞同一颗骰子。</summary>
    private const int RoundStride = MaxTableRounds + 1;

    /// <summary>
    /// 这张表的互斥组要重掷几轮：在 <c>[min_count, max_count]</c> 区间内确定性取（钳到 1..<see cref="MaxTableRounds"/>）。
    /// <para>只作用在**互斥组**上：组 0 的逐件独立掷，件数本来就由各件概率决定（决策 D4），
    /// 拿它去裁独立掷会把"每件各掉各的"改成"整表限额"，那是另一套口径。</para>
    /// </summary>
    public static int GroupRoundsFor(in MonsterLootBindingSnapshot binding, ulong killerCharacterId,
                                     int monsterTemplateId, long serverTick, int bindingIndex)
    {
        var min = binding.MinCount < 1 ? 1 : binding.MinCount;
        var max = binding.MaxCount < min ? min : binding.MaxCount;
        if (max > MaxTableRounds) max = MaxTableRounds;
        if (min > MaxTableRounds) min = MaxTableRounds;
        if (max <= min) return min;
        var roll = Roll01(killerCharacterId, monsterTemplateId, serverTick, bindingIndex, 0, GroupRoundsSalt);
        return min + (int)(roll * (max - min + 1));
    }

    /// <summary>
    /// 组内某条目的"有效轮数期望"：轮数本身在 <c>[min,max]</c> 上是均匀的，
    /// 而某一轮抽中任务件时后面的轮次全部作废（AC 的 <c>!item->needs_quest</c>）。
    /// <para>预览与运行时共用这一条：<see cref="EntryExpectedItems"/> 靠它给出"这张表每次掉几件"的声明值。
    /// 少了它，试算会按单轮报数，而实际多轮重掷 ⇒ 又是"编辑器报一个服务端达不到的数"那一族。</para>
    /// </summary>
    /// <param name="stopChanceBps">单轮抽中任务件的概率（基点，组内求和）。</param>
    public static double ExpectedRoundsForGroup(in MonsterLootBindingSnapshot binding, double stopChanceBps)
    {
        var min = binding.MinCount < 1 ? 1 : binding.MinCount;
        var max = binding.MaxCount < min ? min : binding.MaxCount;
        if (max > MaxTableRounds) max = MaxTableRounds;
        if (min > max) min = max;
        var stop = stopChanceBps / BasisPointsMax;
        if (stop < 0.0) stop = 0.0;
        if (stop > 1.0) stop = 1.0;

        var sum = 0.0;
        var survive = 1.0;
        for (var r = 1; r <= max; r++)
        {
            var atLeast = r <= min ? 1.0 : (max - r + 1) / (double)(max - min + 1);
            sum += atLeast * survive;
            survive *= 1.0 - stop;
        }
        return sum;
    }

    /// <summary>组内"抽中任务件"的单轮概率（基点）：只有任务件会打断重掷（AC 那条 <c>!needs_quest</c>）。</summary>
    public static double GroupStopChanceBps(in MonsterLootBindingSnapshot binding, int groupId)
    {
        var entries = binding.Entries;
        if (entries == null || groupId == 0) return 0.0;
        double stop = 0.0;
        for (var i = 0; i < entries.Length; i++)
        {
            if (GroupIdOf(entries[i]) != groupId) continue;
            if (ResolveConditionKind(in entries[i], in binding) != ConditionQuestHolder) continue;
            stop += EntryWinChanceBps(in binding, i);
        }
        return stop;
    }

    /// <summary>
    /// 按绑定掷一轮掉落。
    /// </summary>
    /// <param name="bindings">这只怪的绑定（顺序必须是稳定的：按绑定 id 升序，装配时排好）。</param>
    /// <param name="killerCharacterId">击杀者角色（掷骰输入；0 = 没有归属，整轮不发）。</param>
    /// <param name="monsterTemplateId">怪物模板（掷骰输入，保证不同怪不同结果）。</param>
    /// <param name="serverTick">服务器 tick（同 tick 重放一致）。</param>
    public static MonsterLootRollResult Roll(
        IReadOnlyList<MonsterLootBindingSnapshot>? bindings,
        ulong killerCharacterId,
        int monsterTemplateId,
        long serverTick)
    {
        var result = new MonsterLootRollResult();
        if (bindings == null || bindings.Count == 0) return result;
        if (killerCharacterId == 0UL || monsterTemplateId <= 0) return result;

        for (var b = 0; b < bindings.Count; b++)
        {
            var binding = bindings[b];
            result.BindingsRolled++;

            // 总闸：这张表这次要不要触发（10000 或 0=未配置 = 必触发）。
            var gateBps = GateChanceBps(binding.RollChanceBps);
            var gate = Roll01(killerCharacterId, monsterTemplateId, serverTick, b, 0, LootSaltBase);
            if (gateBps < BasisPointsMax && gate * BasisPointsMax >= gateBps)
            {
                result.BindingsMissedGate++;
                continue;
            }

            var entries = binding.Entries;
            if (entries == null) continue;
            var grantedOnce = new bool[entries.Length];

            // 第一遍：组 0 的逐件独立掷。挂了的条目连同它在数组里的下标一起留在这里，
            // 顺序、盐值与加组之前逐字相同。
            for (var e = 0; e < entries.Length; e++)
            {
                var entry = entries[e];
                if (GroupIdOf(entry) != 0) continue;
                result.EntriesRolled++;
                var roll = Roll01(killerCharacterId, monsterTemplateId, serverTick, b, e + 1, LootSaltBase);
                var chance = ClampBps(entry.ChanceBps);
                if (chance < BasisPointsMax && roll * BasisPointsMax >= chance)
                {
                    result.EntriesMissedChance++;
                    continue;
                }

                EmitEntry(result, binding, entries[e], grantedOnce, b, e, killerCharacterId, monsterTemplateId, serverTick);
            }

            // 第二遍：互斥组。组号升序（不是数组序）——同一份数据在任何查询顺序下都必须掷出同一件，
            // 而组内成员的相对次序仍取数组序，那就是 seq 决定的"掷骰顺序"。
            // 这张表的组要重掷几轮由表级"抽取件数区间"决定（魔兽把这件事放在世界倍率上，
            // 本作没有世界配置层，于是落在表上：见 MaxTableRounds 的注释）。
            var groupIds = CollectGroupIds(entries);
            if (groupIds.Count > 0)
            {
                var rounds = GroupRoundsFor(binding, killerCharacterId, monsterTemplateId, serverTick, b);
                result.ExtraGroupRoundsRolled += (rounds - 1) * groupIds.Count;
                for (var g = 0; g < groupIds.Count; g++)
                {
                    RollGroup(result, binding, entries, grantedOnce, b, groupIds[g], rounds,
                              killerCharacterId, monsterTemplateId, serverTick);
                }
            }

            // 金币：一张表一份，触发即给（数量在区间内确定性取）。
            if (binding.MoneyMax > 0)
            {
                var moneyRoll = Roll01(killerCharacterId, monsterTemplateId, serverTick, b, 0, LootSaltBase ^ 0x90u);
                var min = binding.MoneyMin < 0 ? 0 : binding.MoneyMin;
                var span = binding.MoneyMax > min ? binding.MoneyMax - min + 1 : 1;
                result.Money += min + (int)(moneyRoll * span);
            }
        }

        return result;
    }

    /// <summary>
    /// 记一次真的产出：条目第一次中签进 <see cref="MonsterLootRollResult.EntriesGranted"/>
    /// （对账按条目去重就靠它），之后每次被重掷再抽进 <see cref="MonsterLootRollResult.GroupRepeatWins"/>
    /// （只有互斥组会有第二次——逐件独立掷一条只掷一次）。
    /// </summary>
    private static void CountGranted(MonsterLootRollResult result, in MonsterDropSnapshot entry, bool firstTime)
    {
        if (!firstTime)
        {
            result.GroupRepeatWins++;
            return;
        }
        result.EntriesGranted++;
        if (GroupIdOf(entry) != 0) result.GroupEntriesGranted++;
    }

    /// <summary>
    /// 总闸概率：`<= 0` 视为**未配置 = 必触发**。
    /// <para>这一条口径必须与编辑器的掉落预览一致：<c>roll_chance_bps</c> 在草稿与库里的默认值都是 0，
    /// 如果 0 解释成"永不触发"，那么所有没显式配概率的绑定都会安静地什么都不掉；
    /// 解释成"必触发"才符合"没配 = 用默认行为"。真要关掉一张表，做法是删掉这条绑定。</para>
    /// </summary>
    public static int GateChanceBps(int rollChanceBps)
        => rollChanceBps <= 0 ? BasisPointsMax : ClampBps(rollChanceBps);

    /// <summary>
    /// 一次击杀的期望掉落件数：逐条 <see cref="EntryExpectedItems"/> 求和，**再按 18 件普通件上限截断**。
    /// <para>为什么要截断：运行期 <c>EmitEntry</c> 入桶前会拦（AC `Loot::AddItem` 满了就丢），
    /// 期望却报"每次 30 件"的话，编辑器试算与服务端产出会各说一套——
    /// 而这一层存在的意义恰恰是"预览不许自己编一个数"。</para>
    /// <para>条件件本就计 0（业务层还没判，见 <see cref="EntryExpectedItems"/>），所以这里只可能被普通件上限压住。</para>
    /// </summary>
    public static double ExpectedItemsPerKill(IReadOnlyList<MonsterLootBindingSnapshot>? bindings)
    {
        var uncapped = ExpectedNormalItemsUncapped(bindings);
        return uncapped > MaxNormalLootItems ? MaxNormalLootItems : uncapped;
    }

    /// <summary>
    /// 普通件期望的**原始值**（没吃 18 件上限）。给预览做交叉核对用：
    /// "逐条求和"与"整表一次算"必须同数，而这两个数都该是未经截断的那个量。
    /// </summary>
    public static double ExpectedNormalItemsUncapped(IReadOnlyList<MonsterLootBindingSnapshot>? bindings)
    {
        if (bindings == null) return 0.0;
        double total = 0.0;
        for (var b = 0; b < bindings.Count; b++)
        {
            var binding = bindings[b];
            var entries = binding.Entries;
            if (entries == null) continue;
            for (var e = 0; e < entries.Length; e++) total += EntryExpectedItems(in binding, e);
        }
        return total;
    }

    /// <summary>
    /// 某一条目对"每次击杀期望件数"的贡献：总闸 × 中签概率 × 平均数量。
    /// <para>条件件计 0（业务层还没判），与旧公式一致。<b>预览与运行时必须走这里</b>——
    /// 命令行曾经自己抄了一遍"概率相加"，加了组以后那个数会系统性偏高，
    /// 于是编辑器里显示"每次击杀 2.4 件"而服务端最多给 1 件。</para>
    /// </summary>
    public static double EntryExpectedItems(in MonsterLootBindingSnapshot binding, int entryIndex)
    {
        var entries = binding.Entries;
        if (entries == null || entryIndex < 0 || entryIndex >= entries.Length) return 0.0;
        var entry = entries[entryIndex];
        if (entry.ItemId <= 0) return 0.0;
        // 条件件按**这一件**解析后跳过：业务层还没判，计进期望等于向策划报一个偏高的数。
        if (ResolveConditionKind(in entry, in binding) != ConditionNone) return 0.0;
        var gate = GateChanceBps(binding.RollChanceBps) / (double)BasisPointsMax;
        var min = entry.CountMin < 1 ? 1 : entry.CountMin;
        var max = entry.CountMax < min ? min : entry.CountMax;
        var rounds = GroupIdOf(entry) == 0
            ? 1.0
            : ExpectedRoundsForGroup(in binding, GroupStopChanceBps(in binding, GroupIdOf(entry)));
        return gate * (EntryWinChanceBps(in binding, entryIndex) / (double)BasisPointsMax) * ((min + max) / 2.0) * rounds;
    }

    /// <summary>
    /// 条目的中签概率（基点，不含总闸）。
    /// <para>组 0 = 自己填的概率（旧口径）。组 &gt;0 是组内累计减法的解析解：
    /// 显式档拿"排在它前面的同组显式档之和"之后剩下的预算里属于它的那一段，
    /// 被一件 &ge;100% 的成员挡在身后就是 0；等概率档（组内填 0 的那些）只在显式档全落空时轮到，
    /// 然后把那部分余额在它们之间平分。</para>
    /// </summary>
    public static double EntryWinChanceBps(in MonsterLootBindingSnapshot binding, int entryIndex)
    {
        var entries = binding.Entries;
        if (entries == null || entryIndex < 0 || entryIndex >= entries.Length) return 0.0;
        var self = entries[entryIndex];
        var own = ClampBps(self.ChanceBps);
        var groupId = GroupIdOf(self);
        if (groupId == 0) return own;

        double sumBefore = 0.0;
        double sumExplicit = 0.0;
        var equalCount = 0;
        for (var e = 0; e < entries.Length; e++)
        {
            if (GroupIdOf(entries[e]) != groupId) continue;
            var chance = ClampBps(entries[e].ChanceBps);
            if (chance <= 0)
            {
                equalCount++;
                continue;
            }
            if (e < entryIndex) sumBefore += chance;
            sumExplicit += chance;
        }

        if (own > 0) return Max0(Math.Min(own, BasisPointsMax - sumBefore));
        return equalCount > 0 ? Max0(BasisPointsMax - sumExplicit) / equalCount : 0.0;
    }

    private static double Max0(double value) => value < 0.0 ? 0.0 : value;

    /// <summary>条件：逐件没填，用所属绑定那一份（库里、草稿里、旧快照文件里的"未设置"都是这个值）。</summary>
    public const int ConditionInherit = -1;

    /// <summary>
    /// 解析这一件实际适用的条件：<c>-1</c>（未设置）就用所属绑定的，其余照用。
    /// <para>魔兽把 <c>QuestRequired</c> 挂在 <c>LootStoreItem</c> **每一行**上（AzerothCore
    /// <c>Loot/LootMgr.h:127-149</c>），所以"一张表里既有普通货又有一件任务物品"是常态配法；
    /// 本作以前只有绑定级，整张表只能同进同退。</para>
    /// <para>这条继承规则只有这一个主人：内核掷骰、校验器判"这件配了任务却没任务 id"、
    /// 命令行试算都调它，各写一遍 ternary 迟早会分叉。</para>
    /// </summary>
    public static int ResolveConditionKind(int entryConditionKind, int bindingConditionKind)
        => entryConditionKind == ConditionInherit ? bindingConditionKind : entryConditionKind;

    /// <summary>条目形态的同名入口（见 <see cref="ResolveConditionKind(int,int)"/>）。</summary>
    public static int ResolveConditionKind(in MonsterDropSnapshot entry, in MonsterLootBindingSnapshot binding)
        => ResolveConditionKind(entry.ConditionKind, binding.ConditionKind);

    /// <summary>解析任务 id：条目自己带就用自己的，否则用绑定的（条件不是"任务持有者"时没人会读它）。</summary>
    public static int ResolveQuestId(int entryQuestId, int bindingQuestId) => entryQuestId > 0 ? entryQuestId : bindingQuestId;

    /// <summary>条目形态的同名入口。</summary>
    public static int ResolveQuestId(in MonsterDropSnapshot entry, in MonsterLootBindingSnapshot binding)
        => ResolveQuestId(entry.QuestId, binding.QuestId);

    /// <summary>组号读取：负数按"独立掷"处理，免得一个手滑的 -1 把条目变成谁也归不进去的孤儿。</summary>
    public static int GroupIdOf(in MonsterDropSnapshot entry) => entry.GroupId < 0 ? 0 : entry.GroupId;

    /// <summary>
    /// 把逐件条目排成内核实际使用的次序：按 <c>(seq, itemId)</c> 升序。
    /// <para>这条规则只有一个主人。内核把数组下标当掷骰盐值的一部分，"谁用哪颗骰子"必须由数据里的
    /// seq 决定，而不是由 SQL 返回行序决定；装配层和命令行预览都走这里，
    /// 否则一处改了排序，另一处的数字就自成一个世界。</para>
    /// <para>排序键刻意<b>不含组号</b>：给某一行填上组号会让它从数组中部挪到前面，
    /// 从而把整表所有条目的骰子重新分配一遍——掉率整体漂移，而编辑器、库、快照三处的数字全都没变。</para>
    /// </summary>
    public static void SortEntriesForRoll(MonsterDropSnapshot[] entries)
    {
        if (entries == null || entries.Length < 2) return;
        System.Array.Sort(entries, (a, b) =>
        {
            var bySeq = a.Seq.CompareTo(b.Seq);
            return bySeq != 0 ? bySeq : a.ItemId.CompareTo(b.ItemId);
        });
    }

    /// <summary>该绑定里出现的组号（升序、去重、跳过 0）。</summary>
    private static List<int> CollectGroupIds(MonsterDropSnapshot[] entries)
    {
        var ids = new List<int>();
        for (var e = 0; e < entries.Length; e++)
        {
            var gid = GroupIdOf(entries[e]);
            if (gid == 0 || ids.Contains(gid)) continue;
            ids.Add(gid);
        }
        // 组的处理次序取组号数值升序，而不是"在数组里出现的次序"：后者会随查询计划变，
        // 而它是掷骰输入的一部分。
        ids.Sort();
        return ids;
    }

    /// <summary>
    /// 一个互斥组：按表级"抽取件数区间"抽来的轮数重掷，每轮最多一件
    /// （AzerothCore <c>LootGroup::Roll</c> + <c>LootGroup::Process</c> 的 <c>nonRefIterationsLeft</c>，
    /// LootMgr.cpp:1292-1327 与 1439-1470）。
    /// <para>组内填 0 的成员不是"永不掉"，而是"平分显式档没吃到的那部分"——同一个 0 在组外是永不掉，
    /// 这一条差异是校验器必须讲清楚的，否则策划按组外口径理解就会配出一张掉率完全不同的表。</para>
    /// <para>成员普查只做一次：条目数与"被组通路消费的条目数"是按条目计的量，
    /// 重掷几轮都不该把它们乘上去（乘了就等于把同一行数两遍）。</para>
    /// </summary>
    private static void RollGroup(
        MonsterLootRollResult result, in MonsterLootBindingSnapshot binding, MonsterDropSnapshot[] entries, bool[] grantedOnce,
        int bindingIndex, int groupId, int rounds, ulong killerCharacterId, int monsterTemplateId, long serverTick)
    {
        var explicitCount = 0;
        var equalCount = 0;
        for (var e = 0; e < entries.Length; e++)
        {
            if (GroupIdOf(entries[e]) != groupId) continue;
            result.EntriesRolled++;
            result.GroupEntriesRolled++;
            if (ClampBps(entries[e].ChanceBps) > 0) explicitCount++;
            else equalCount++;
        }

        var anyRoundWon = false;
        for (var round = 0; round < rounds; round++)
        {
            result.GroupsRolled++;
            // 盐值里带上轮次：不同轮必须是不同的骰子（同一颗骰子重掷 = 每次抽到同一件，
            // 那"这张表抽 2 件"就变成了"这一件给 2 份"）。
            var diceIndex = groupId * RoundStride + round;

            var winner = -1;
            var reached = 0;
            var guaranteedHit = false;
            var remaining = Roll01(killerCharacterId, monsterTemplateId, serverTick, bindingIndex, diceIndex, GroupRollSalt) * BasisPointsMax;
            for (var e = 0; e < entries.Length; e++)
            {
                if (GroupIdOf(entries[e]) != groupId) continue;
                var chance = ClampBps(entries[e].ChanceBps);
                if (chance <= 0) continue;   // 等概率档要等显式档全落空才轮到
                reached++;
                if (chance >= BasisPointsMax)
                {
                    winner = e;
                    guaranteedHit = true;
                    break;
                }
                remaining -= chance;
                if (remaining < 0)
                {
                    winner = e;
                    break;
                }
            }

            if (guaranteedHit)
            {
                // 短路之后，显式档没轮到的、以及整档等概率成员都连减法都没参与过——
                // 魔兽把这当成配置错误打日志（LootMgr.cpp:1497-1509），这里把它数出来。
                result.GroupShadowedByGuaranteed += (explicitCount - reached) + equalCount;
            }

            if (winner < 0 && equalCount > 0)
            {
                var pick = (int)(Roll01(killerCharacterId, monsterTemplateId, serverTick, bindingIndex, diceIndex, GroupEqualRollSalt) * equalCount);
                if (pick >= equalCount) pick = equalCount - 1;
                var seen = 0;
                for (var e = 0; e < entries.Length; e++)
                {
                    if (GroupIdOf(entries[e]) != groupId || ClampBps(entries[e].ChanceBps) > 0) continue;
                    if (seen == pick)
                    {
                        winner = e;
                        result.GroupEqualChancedPicks++;
                        break;
                    }
                    seen++;
                }
            }

            if (winner < 0) continue;        // 这一轮空手：还剩的轮次照掷
            anyRoundWon = true;

            result.GroupsPicked++;
            EmitEntry(result, in binding, entries[winner], grantedOnce, bindingIndex, winner,
                      killerCharacterId, monsterTemplateId, serverTick);

            // 魔兽的重掷上限只乘"非任务件"（LootMgr.cpp:1466：
            // <c>if (nonRefIterationsLeft > 1 && !item->needs_quest)</c>）——任务物品是"给做了任务的人一份"，
            // 不是"给做了任务的人三份"。
            if (round + 1 < rounds && ResolveConditionKind(in entries[winner], in binding) == ConditionQuestHolder)
            {
                result.GroupQuestItemStopsMultiplying++;
                break;
            }
        }

        if (!anyRoundWon) result.GroupsRolledEmpty++;
    }

    /// <summary>
    /// 件数上限闸门（AC `Loot::AddItem` 的 18 / 32）：桶满了就丢这一件并记账。
    /// <para>为什么按**桶**分而不是按总数：任务件与普通件在客户端是两个页签，AC 也是两条独立上限；
    /// 合成一条总数会让"18 件普通货 + 32 件任务件"里的 32 永远用不满。</para>
    /// </summary>
    /// <returns>true = 这一件可以进桶；false = 已超限（调用方必须跳过它，别再计数成"中签"）。</returns>
    private static bool TryAdmit(MonsterLootRollResult result, List<MonsterLootDrop> bucket, int capacity,
                                 in MonsterDropSnapshot entry, bool firstTime)
    {
        if (bucket.Count < capacity) return true;
        result.ItemsDroppedOverCapacity++;
        if (firstTime && GroupIdOf(entry) == 0) result.EntriesDroppedOverCapacity++;
        return false;
    }

    /// <summary>
    /// 把一条中签的条目落到出口（数量 + 条件路由）。
    /// <para>独立掷与组掷共用这一份实现：两处各写一遍，迟早有一处漏掉某个出口而账对不上。</para>
    /// <para><paramref name="grantedOnce"/> 是"这条目本次击杀有没有中过签"：表级重掷下同一条目可以掉两次，
    /// 条目级的对账只记一次，多出来的件数记进 <see cref="MonsterLootRollResult.GroupRepeatWins"/>。</para>
    /// </summary>
    private static void EmitEntry(
        MonsterLootRollResult result, in MonsterLootBindingSnapshot binding, in MonsterDropSnapshot entry, bool[] grantedOnce,
        int bindingIndex, int entryIndex, ulong killerCharacterId, int monsterTemplateId, long serverTick)
    {
        var firstTime = !grantedOnce[entryIndex];
        if (firstTime) grantedOnce[entryIndex] = true;

        var count = RollCount(entry.CountMin, entry.CountMax, killerCharacterId, monsterTemplateId, serverTick, bindingIndex, entryIndex + 1);
        if (count <= 0) count = 1;

        // 条件按**这一件**解析（条目没填才退回绑定）：任务物品和普通货混在同一张表/同一个组里
        // 是魔兽的常态配法，整表同进同退会把"必掉且只给做了任务的人"变成"要么全都判要么都不判"。
        var conditionKind = ResolveConditionKind(in entry, in binding);
        var questId = ResolveQuestId(in entry, in binding);

        var drop = new MonsterLootDrop(entry.ItemId, count, conditionKind, questId, bindingIndex, entryIndex);
        switch (conditionKind)
        {
            case ConditionNone:
                if (entry.ItemId > 0)
                {
                    if (!TryAdmit(result, result.Drops, MaxNormalLootItems, in entry, firstTime)) break;
                    result.Drops.Add(drop);
                    CountGranted(result, in entry, firstTime);
                }
                else if (firstTime) result.EntriesSkippedNoItem++;
                break;
            case ConditionQuestHolder:
                // 本层没有任务状态，也不去猜：交给业务层判，判不过就是没给（有计数）。
                if (entry.ItemId > 0 && questId > 0)
                {
                    if (!TryAdmit(result, result.ConditionalDrops, MaxQuestLootItems, in entry, firstTime)) break;
                    result.ConditionalDrops.Add(drop);
                    CountGranted(result, in entry, firstTime);
                }
                else if (firstTime) result.EntriesDeferredUnknownCondition++;
                break;
            default:
                // 组队 / 稀有度门槛：这两条件本层同样判不了。宁可"不给"并计数，
                // 也不能默认给——默认给会让"只有队伍里才掉"的件变成人人有份。
                if (firstTime) result.EntriesDeferredUnknownCondition++;
                break;
        }
    }

    /// <summary>数量区间掷骰：上界小于下界时按下界给（装配层已兜过一次，这里再兜一次防止绕过）。</summary>
    public static int RollCount(int min, int max, ulong killerCharacterId, int monsterTemplateId, long serverTick, int bindingIndex, int entryIndex)
    {
        if (max < min) max = min;
        if (min <= 0 && max <= 0) return 1;
        if (min < 0) min = 0;
        if (max == min) return max;
        var roll = Roll01(killerCharacterId, monsterTemplateId, serverTick, bindingIndex, entryIndex, LootSaltBase ^ 0x5Au);
        return min + (int)(roll * (max - min + 1));
    }

    private static int ClampBps(int bps) => bps < 0 ? 0 : (bps > BasisPointsMax ? BasisPointsMax : bps);

    /// <summary>
    /// 把 (击杀者, 模板, tick, 绑定序号, 条目序号) 混进同一支确定性掷骰。
    /// <para>盐里必须带上两个序号：不带的话"同表两条同物品"会掷出同一个数（一条必中一条必不中），
    /// 而日志里看不出任何异常。</para>
    /// </summary>
    private static float Roll01(ulong killer, int templateId, long tick, int bindingIndex, int entryIndex, uint salt)
    {
        var mixed = (ulong)(templateId * 397 + bindingIndex * 31 + entryIndex);
        return CombatCore.Roll01(killer, mixed, tick, salt);
    }
}
