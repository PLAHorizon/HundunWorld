using System;
using System.Collections.Generic;
using Horizon.Game.Sim.Monsters;

namespace Horizon.MonsterCli;

/// <summary>引用对账口子（id 空间只有连了库才判得准，所以校验必须跑在能查库的这一侧）。</summary>
public interface IMonsterReferenceIndex
{
    /// <summary>库里存在的技能 id。</summary>
    HashSet<int> SkillIds();

    /// <summary>库里存在的物品模板 id。</summary>
    HashSet<int> ItemIds();

    /// <summary>库里存在的任务 id（条件掉落引用用）。</summary>
    HashSet<int> QuestIds();

    /// <summary>
    /// 库里任务的发布状态（任务 id → <c>publish_state</c>）。
    /// <para>
    /// 存在的任务不等于**接得到**的任务：<c>publish_state=0</c> 的模板被
    /// <c>QuestGrain</c> 直接拒绝、目录也不下发。条件件引用了它，表现是
    /// "配了 100% 却一次都没掉过"，而库/编辑器/快照三处数字全对。
    /// </para>
    /// </summary>
    IReadOnlyDictionary<int, int> QuestPublishStates();

    /// <summary>已存在的怪物编号集合（刷怪点引用完整性用）。</summary>
    HashSet<int> MonsterIds();
}

/// <summary>校验结果（错误阻断、警告不阻断但要人看一眼）。</summary>
public sealed class MonsterValidationResult
{
    public bool Ok => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>结构化错误路径（编辑器据此把输入框标红，而不是只给一句人话）。</summary>
    public List<string> ErrorPaths { get; } = new();

    public void Error(string path, string message)
    {
        Errors.Add(path + "：" + message);
        ErrorPaths.Add(path);
    }

    public void Warn(string message) => Warnings.Add(message);
}

/// <summary>
/// 怪物草稿校验器。
/// <para>
/// <b>为什么判据要写在 CLI 侧而不是只写在插件里</b>：插件那侧的自测用的是插件自己的镜像模型，
/// 两边同错时全是绿。判据放服务端一份、插件调 <c>validate</c> 拿回执，才能出现"插件写错了会被拒"。
/// </para>
/// <para>
/// 每条判据都对应一个"漏配之后的坏表现"，注释里写清楚——否则后人只会把它当样板代码删掉。
/// </para>
/// </summary>
public static class MonsterValidator
{
    /// <summary>玩家等级上限（与 <c>ProgressionCurve.MaxLevel</c> 同值；由对账测试钉住）。</summary>
    public const int MaxLevel = 300;

    /// <summary>单怪技能挂载上限（配 12 个已经超出可玩性，超限多半是把序列粘进了同一行）。</summary>
    public const int MaxSkillMounts = 16;

    /// <summary>伤害系别位掩码上限（六个位：物理 + 金木水火土；与服务端 <c>MonsterSchoolBits.AllMask</c> 同值）。</summary>
    public const int MaxSchoolMask = 63;

    /// <summary>
    /// 检测位里**已被内核读取**的位集合：bit0 视觉 / bit1 听觉 / bit2 隐蔽探测（<c>MonsterBehaviorCore.CanDetectStealth</c> 读 0x4）。
    /// <para>这不是"列的取值范围"（列是 int，什么位都存得下），而是"有消费者的位"。
    /// 按列宽放行会让策划以为多出来的位生效了，实际表现与没配一模一样——静默失效。</para>
    /// </summary>
    public const int MaxDetectMaskBits = 7;

    /// <summary>单怪掉落绑定上限。</summary>
    public const int MaxLootBindings = 8;

    /// <summary>每绑定逐件条目上限。</summary>
    public const int MaxDropEntries = 16;

    /// <summary>巡逻点数上限（路线是手打的，超过 32 点说明把整张地图粘进来了）。</summary>
    public const int MaxRoutePoints = 32;

    /// <summary>一个刷怪点的同时存活上限。</summary>
    public const int MaxAliveLimit = 20;

    /// <summary>
    /// 任务表里"已上线"的 <c>publish_state</c> 值（= <c>QuestPublishState.Online</c>）。
    /// 校验器不引用任务侧程序集，所以这里留一个与 <c>QuestGrain</c> 同值的常量。
    /// </summary>
    public const int QuestOnlineState = 1;

    /// <summary>
    /// 校验一份草稿。
    /// <para><paramref name="refs"/> 为 null 时跳过 id 对账（离线自检场景），
    /// 并在 warnings 里**显式声明跳过了哪几条** —— 否则"校验通过"与"没校引用"同形。</para>
    /// </summary>
    public static MonsterValidationResult Validate(MonsterDraft? draft, IMonsterReferenceIndex? refs)
    {
        var r = new MonsterValidationResult();
        if (draft == null)
        {
            r.Error("draft", "草稿为空（JSON 反序列化失败或字段名不匹配）");
            return r;
        }

        if (refs == null)
        {
            r.Warn("未连库：技能/物品/任务/怪物 id 与任务上线状态的对账已跳过（5 条检查未执行）");
        }

        // --- 身份 ---
        if (draft.MonsterId <= 0) r.Error("monsterId", "编号必须为正（号段规则见设计文档 §1.3）");
        if (string.IsNullOrWhiteSpace(draft.Name)) r.Error("name", "显示名不得为空——空名会让客户端回退成 Monster_<id>");
        if (draft.Rank < 0 || draft.Rank > 4) r.Error("rank", "rank 必须在 0..4（0普通 1精英 2稀有精英 3首领 4世界BOSS）");
        if (draft.WuxingDomain < 0 || draft.WuxingDomain > 5) r.Error("wuxingDomain", "五行域必须在 0..5（0=未定域：不掉不涨好感）");
        // 物种（求援"同族"的判据）：写库时是 `Math.Clamp(draft.Species, 0, 7)`——**越界会被静默改写**。
        // 配 9 想表达"龙族"却落成 7（昆虫），求援就找错同类，而库里、编辑器、快照三处显示的都是 7。
        // 所以这里按枚举表拒掉，让录入当场失败而不是让 clamp 替策划改主意。
        if (draft.Species < 0 || draft.Species > 7)
            r.Error("species", "种族必须在 0..7（0人形 1野兽 2元素 3龙类 4巨人 5亡灵 6机械 7昆虫）；越界会被写库时 clamp 静默改写");
        if (string.IsNullOrWhiteSpace(draft.ModelKey)) r.Warn("modelKey 为空：客户端只能落回占位 prefab");
        // 外观的"意图"与"进包"是两件事：GUID 只是指认了哪个资产，客户端真正加载靠的是内容路径。
        // 只填 GUID 不填路径 = 编辑器里看着配好了、进游戏还是占位人形，所以必须显形。
        if (!string.IsNullOrWhiteSpace(draft.PrefabGuid) && string.IsNullOrWhiteSpace(draft.PrefabPath))
            r.Warn("填了 prefabGuid 但没填 prefabPath：外观表按路径导出，这只怪进游戏仍会回退占位 Prefab");
        if (draft.PrefabPath != null && draft.PrefabPath.Trim().Length > 256)
            r.Error("prefabPath", "预制体路径超过 256 字符（列宽上限），请改用内容树里的短路径");
        if (draft.PrefabGuid != null && draft.PrefabGuid.Trim().Length > 64)
            r.Error("prefabGuid", "预制体 Guid 超过 64 字符（列宽上限）");
        if (draft.StandState < 0 || draft.StandState > 3) r.Error("standState", "待机姿态必须在 0..3");
        else if (draft.StandState != 0)
            // 数据会一路下发到客户端的标记上（identity.StandState → SelectableEntityTag.StandState），
            // 但**客户端没有任何渲染端**去读它：占位 prefab 没有坐/卧动作。填了不会改变玩家看到的姿态。
            // 今天 11 只种子怪都是 0，所以这句现在不响；只在该格真的被使用时提醒"配了等于没配"。
            r.Warn("standState=" + draft.StandState + "：这一格会下发到客户端标记，但目前**没有渲染端**" +
                   "（怪物占位 prefab 没有坐/卧动作）⇒ 玩家看到的仍是站立；要么接渲染端，要么保持 0");
        if (draft.ModelScale <= 0.0 || draft.ModelScale > 10.0) r.Error("modelScale", "体型缩放必须落在 (0,10]");

        // --- 等级 ---
        if (draft.LevelMin > draft.LevelMax) r.Error("levelMin", "等级下界不得大于上界");
        if (draft.LevelBase < draft.LevelMin || draft.LevelBase > draft.LevelMax)
            r.Error("levelBase", "基准等级必须落在 [levelMin, levelMax] 内（否则刷怪点的合法校验会放过一个模板自己都不接受的等级）");
        if (draft.LevelMax > MaxLevel) r.Error("levelMax", $"等级上界不得超过 {MaxLevel}（与 ProgressionCurve.MaxLevel 同源）");

        // --- 属性公式 ---
        var stats = draft.Stats;
        if (stats == null) r.Error("stats", "属性公式缺失");
        else
        {
            if (stats.HpBase + (long)stats.HpPerLevel * draft.LevelBase <= 0)
                r.Error("stats.hpBase", "该等级算出的血量为 0——怪一碰就死，且金轨迹会出现“出生即 Dead”分支");
            if (stats.AtkBase < 0 || stats.AtkPerLevel < 0 || stats.HpPerLevel < 0)
                r.Error("stats", "属性增量不得为负");
            if (stats.HpOverrideBps < 0 || stats.AtkOverrideBps < 0)
                r.Error("stats.*OverrideBps", "覆盖基点不得为负（0 = 不覆盖，正数 = 按倍率放大）");
            if (stats.HpOverrideBps > 100000 || stats.AtkOverrideBps > 100000)
                r.Error("stats.*OverrideBps", "覆盖基点超过 100000（100×）几乎必然是把百分比当成了倍率");
        }

        // --- 经验 ---
        var xp = draft.Xp;
        if (xp == null) r.Error("xp", "经验系数缺失");
        else
        {
            if (xp.XpBase < 0 || xp.XpPerLevel < 0) r.Error("xp", "经验系数不得为负");
            if (xp.XpBase == 0 && xp.XpPerLevel == 0)
                r.Warn("该怪击杀零经验（两项都是 0）——如果这是有意的请写进 remark");

            // 逐模板倍率（魔兽的 ModExperience / ModHealth）。这两格的坑不是"越界"，而是"看起来配了却没作用"：
            // 血薄削减那一条在魔兽里只认 0..7500，填 9000 与不填一模一样，填 15000 也不会放大经验。
            if (xp.XpModBps < 0) r.Error("xp.xpModBps", "经验倍率不得为负（要归零请填 0，负数会被当成没配）");
            if (xp.XpModBps == 0) r.Warn("xp.xpModBps：填 0 等于这只怪**一点经验都不给**（不是「没配」，没配是 10000）");
            if (xp.XpModBps > 100000) r.Warn("xp.xpModBps：倍率已超过 ×10，确认不是把百分比当成了基点");
            if (xp.HealthModBps < 0) r.Error("xp.healthModBps", "血薄削减基点不得为负");
            else if (xp.HealthModBps > MonsterXpCore.HealthModEffectiveCapBps && xp.HealthModBps != 10000)
                r.Warn($"xp.healthModBps：{xp.HealthModBps} 落在魔兽口径的「不生效」那一段——" +
                       $"只有 0..{MonsterXpCore.HealthModEffectiveCapBps} 才会削减经验，" +
                       "填 7501..10000 与不填一样，大于 10000 也不会放大");

            // 反比例衰减的形状由内核判据给；这里只保证"配了却拿不到"不是静默状态。
            var previewConfig = new MonsterXpConfig(xp.XpBase, xp.XpPerLevel,
                MonsterXpConfig.DefaultRankScale(), xp.XpModBps, xp.HealthModBps);
            var preview = MonsterXpCore.Decompose(
                previewConfig, draft.LevelBase, draft.LevelBase, (MonsterRank)Math.Clamp(draft.Rank, 0, 4));
            // 同级算出 0 有两种成因：曲线/基准被配反（错），或倍率就是归了零（有意的配置）。
            // 把后者也报成"配反了"会让人去改本来正确的 xp_base —— 判据要按成因分档。
            var unmodded = MonsterXpCore.Decompose(
                new MonsterXpConfig(xp.XpBase, xp.XpPerLevel, MonsterXpConfig.DefaultRankScale()),
                draft.LevelBase, draft.LevelBase, (MonsterRank)Math.Clamp(draft.Rank, 0, 4));
            if (preview.Final <= 0 && unmodded.Final > 0)
                r.Warn("xp：基准曲线本身给得出经验，是逐模板倍率把同级经验归零了——确认这是有意的");
            else if (preview.Final <= 0)
                r.Error("xp", "同级击杀经验算出为 0，说明基准或让分被配反了（灰名怪才是 0，同级不该是）");
        }

        // --- 刷新/尸体 ---
        if (draft.RespawnBaseSeconds < 0) r.Error("respawnBaseSeconds", "刷新截距不得为负");
        if (draft.RespawnPerLevelSeconds < 0) r.Error("respawnPerLevelSeconds", "每级增量不得为负");
        if (draft.CorpseSeconds < 0) r.Error("corpseSeconds", "尸体停留秒不得为负");
        // 这句文案以前许过一个做不到的承诺（"掉落窗口与它绑定"——掉落仍是击杀即发，不挂在尸体上）。
        // 现在这一列真的被消费了（DsWorldSession 的尸体阶段），所以按它**实际**改变得到的现象说：
        // 填 0 = 怪一死就从屏幕上消失，玩家看不到自己刚杀掉的东西。
        // 另外窗口比刷新秒数长不会出问题：装配时会被压到重生前一 tick（见 MonsterCorpseCore）。
        if (draft.CorpseSeconds == 0) r.Warn("corpseSeconds=0：怪一死立刻消失，玩家看不到尸体（掉落不受影响，仍是击杀即发）");
        // 拾取窗口两格（2026-10-28 拍板：缺省取 AC，但可逐怪配）。
        // 窗口 0 = 明确关掉拾取（不是"配错"），所以是 Warn 不是 Error——但要说清后果。
        if (draft.LootWindowSeconds < 0) r.Error("lootWindowSeconds", "可拾取窗口秒不得为负");
        if (draft.LootWindowSeconds == 0) r.Warn("lootWindowSeconds=0：尸体一出现就取不了（真要关拾取才填 0）");
        if (draft.LootWindowSeconds > draft.CorpseSeconds && draft.CorpseSeconds > 0)
            r.Warn("lootWindowSeconds 比 corpseSeconds 长：尸体先消失，超出的窗口永远用不到");
        // 距离 0 不是"关掉"——关掉请用窗口 0。与索敌半径 0 同一口径（半径 0 是非法值）。
        if (draft.PickupDistanceMeters <= 0) r.Error("pickupDistanceMeters", "拾取距离必须大于 0（关掉拾取请把窗口设 0，不要用距离 0）");
        // 索敌喊话：AC 侧那是一张 `creature_text` 表（id → 文案 + 语言 + 概率），本作**还没有这张表**，
        // `aggro_say_id` 以前是一句"配了也没人念"的 Warn（消费者普查实测运行期命中 0）。
        // 2026-10-28 文本这一层落了：文案住在旁挂源文件 `monster_texts.json`，
        // 进战时 DS 发文本号（`SyncEventKind.MonsterSay`），客户端按 (modelKey, sayId) 查文案。
        // 所以这里**不再每填必警**——那会把真问题埋成背景噪声；
        // 真正的坏形状是"号配了而源文件里没有那一号"，它由装配期那条引用对账抓
        // （`MonsterContentSnapshot.ToBundle` 的"行为表起手喊话指向文本 X 而包里没它"），
        // 那里看得见真实的包内容，而不是我在保存时猜。

        // --- 行为 ---
        var b = draft.Behavior;
        if (b == null) r.Error("behavior", "行为配置缺失");
        else
        {
            if (b.AiProfile < 0 || b.AiProfile > 3) r.Error("behavior.aiProfile", "AI 档位必须在 0..3");
            CheckFlag(r, "behavior.flagUseThreat", b.FlagUseThreat);
            CheckFlag(r, "behavior.flagUseSkills", b.FlagUseSkills);
            CheckFlag(r, "behavior.flagUseNavMesh", b.FlagUseNavMesh);
            CheckFlag(r, "behavior.flagUseAssist", b.FlagUseAssist);
            CheckFlag(r, "behavior.flagUseTactics", b.FlagUseTactics);

            if (b.AggroRadius <= 0)
                r.Error("behavior.aggroRadius",
                    "索敌半径必须是正数：0 与负数都是非法值（运行期两条通路此前各有一种兜底，所以这一格不许留空档）。" +
                    "不想让它先手请看 isAggressive——那一格才是开关，半径只表达距离");
            if (b.AggroConeDegrees <= 0 || b.AggroConeDegrees > 360) r.Error("behavior.aggroConeDegrees", "朝向锥必须落在 (0,360]");
            if (b.LeashRadius <= 0) r.Error("behavior.leashRadius", "脱战半径必须 > 0");
            // 这条判据是"表现坏但配置合法"的典型：leash 比 aggro 小时，怪刚索敌就已超距，
            // 结果它永远在 Idle 与 Evade 之间跳，看起来像"见了人就转身跑"。
            else if (b.LeashRadius < b.AggroRadius)
                r.Error("behavior.leashRadius", "脱战半径小于索敌半径会让怪刚索敌就已超距，表现为“见人就转身跑”");
            if (b.WalkSpeed <= 0 || b.MoveSpeed <= 0) r.Error("behavior.speed", "巡逻与追击速度都必须 > 0");
            if (b.MoveSpeed < b.WalkSpeed)
                r.Warn("追击速度低于巡逻速度：这只怪散步比追你快，玩家会觉得它“放不住”");
            if (b.FollowType < 0 || b.FollowType > 3) r.Error("behavior.followType", "移动方式必须在 0..3");
            if (b.MeleeSwingSeconds <= 0) r.Error("behavior.meleeSwingSeconds", "挥击间隔必须 > 0");
            if (b.FleeHpPercent < 0 || b.FleeHpPercent > 100) r.Error("behavior.fleeHpPercent", "逃跑阈值必须在 0..100");
            if (b.RangedMaxDistance < b.RangedMinDistance) r.Error("behavior.rangedMaxDistance", "保持距离上界不得小于下界");
            if (b.EvadeImmunitySeconds < 0) r.Error("behavior.evadeImmunitySeconds", "回位免疫秒不得为负");
            // 世界 BOSS 在运行时**根本不做**"距出生点超 leash 就脱战"这条检查
            // （逆向依据：AzerothCore `Creature::CanCreatureAttack` 把整段写在 `!isWorldBoss()` 里）。
            // 于是这一格对世界 BOSS 是死配置——策划改了它游戏里什么都不发生，必须当场念出来。
            if ((MonsterRank)draft.Rank == MonsterRank.WorldBoss && b.LeashRadius > 0)
                r.Warn("这只怪是世界BOSS：它不会因被拉远而脱战，leashRadius 对它不生效（脱战只能靠技能或死亡）");
            // 站桩档（移动方式=3）在运行时**不产生任何位移**（判据主人 `MonsterPatrolCore.FollowProducesMovement`，
            // v1 与 v2 现在都问它）。于是"脱战半径/游荡半径/巡逻线"这三格对它都是死配置：
            // 改了什么都不发生。以前这一格只对开了能力位的怪成立，默认档的站桩怪照样满场跑。
            if (b.FollowType == 3)
            {
                if (b.LeashRadius > 0 || b.WanderRadius > 0)
                    r.Warn("移动方式=站桩：这只怪不产生位移，leashRadius 与 wanderRadius 对它都不生效");
                if (b.PatrolRouteId > 0)
                    r.Warn("移动方式=站桩：它走不了巡逻线（patrolRouteId 是死配置）；要沿线上走就换成步行档");
            }
            if (b.AssistRadius < 0) r.Error("behavior.assistRadius", "求援半径不得为负");

            // 检测位的设计口径是 bit0 视觉 / bit1 听觉 / bit2 隐蔽探测，列是 int 所以 1<<20 也存得进去，
            // 超出已定义位一律拒（别留静默失效）。
            // bit2（隐蔽探测）今天**没有任何隐身来源**：这一位接上了判据，但场上一旦没人藏，
            // 它就不会改变任何行为。如实念出来，免得策划以为配了就能看破潜行。
            if ((b.DetectMask & MonsterAiGate.DetectMaskStealth) != 0)
                r.Warn("behavior.detectMask：隐蔽探测那一位今天没有作用对象（本作还没有隐身来源）"
                       + "——判据已接好，等隐身/潜行落地才看得出差别");
            if (b.DetectMask < 0 || (b.DetectMask & ~MaxDetectMaskBits) != 0)
                r.Error("behavior.detectMask",
                    $"检测位只能用已定义的 bit0..bit2（0..{MaxDetectMaskBits}）；实得 {b.DetectMask}——" +
                    "内核只读这三条位，更高的位存得进去但永远没人读");
            // 但要说实话：**今天这三条位也没有消费者**——`MonsterBehaviorConfig.CanDetectStealth` 全仓零调用方，
            // `DetectMask` 只被装配写进配置、运行期两处 AI 通路都不读它（本作还没有"隐蔽/潜行"这个状态层，挂在 D14）。
            // 于是"我给它开了真视"和"根本没配"在玩家那边是同一个游戏——这正是要念出来的那种静默失效。
            if (b.DetectMask != MonsterBehaviorConfig.Default.DetectMask)
                r.Warn("behavior.detectMask：本作的隐蔽/潜行状态层还没落地，这三条位今天**没有任何运行期消费者**" +
                       "（两条 AI 通路都不读它，`CanDetectStealth` 零调用方）——改它不会改变这只怪的任何表现；" +
                       "要真生效得先定状态层口径（设计文档 D14）");
            if (b.WanderRadius < 0) r.Error("behavior.wanderRadius", "无路线踱步半径不得为负（0 = 原地不动）");
            if (b.IdleToPatrolSeconds < 0) r.Error("behavior.idleToPatrolSeconds", "待机转巡逻秒不得为负");

            var flags = MonsterAiFlags.Resolve(
                (MonsterAiProfile)Math.Clamp(b.AiProfile, 0, 3),
                b.FlagUseThreat, b.FlagUseSkills, b.FlagUseNavMesh, b.FlagUseAssist, b.FlagUseTactics);
            if (flags.UseSkills && draft.Skills.Count == 0)
                r.Warn("开了技能能力位但没挂任何技能：这只怪会走施法判定然后什么都不放（表现为“进入战斗就站着”）");
            if (!flags.UseSkills && draft.Skills.Count > 0)
                r.Warn("挂了技能但档位把技能位关着：这些行不会生效（编辑器预览要显示成灰）");
            if (flags.UseNavigation && b.FollowType != (int)MonsterFollowType.GroundWalk)
                r.Warn("开导航但移动方式不是步行：走图判定会被 followType 短路");
            if (flags.UseAssist && !flags.UseThreatTable)
                r.Warn("开了求援位但档位把威胁表关着：求援整段写在威胁表分支里，这一位不会生效");
            // 求援这一路的"配了等于没配"有三处开关，缺一个都会**安静地什么都不做**——
            // 而安静失效的配置最贵：内核里没有日志，策划只能进游戏才知道没生效。
            // 三条判据逐字取自内核的进入条件（`threatId==0 && !immune && flags.UseAssist &&
            // behavior.AssistRadius>0`，整段又在 `flags.UseThreatTable` 里；物种 0 一律不许当同族，
            // 否则全图没配物种的怪会互相拉成片）。半径 0 与"没填"同义（都不求援），所以只念"开了位却没半径"。
            if (flags.UseAssist && b.AssistRadius <= 0f)
                r.Warn("开了求援位但求援半径是 0：求援要半径 > 0，这只怪既不会叫也不会被人叫");
            if (flags.UseAssist && b.AssistRadius > 0f && draft.Species == 0)
                r.Warn("开了求援半径但物种是 0：内核把物种 0 当“没配物种”，不允许 0==0 算同族，这条求援不会生效");
            // 风筝（保持距离）与残血逃跑这两组**只在战术位开着的那一支里被读**
            // （v2：`flags.UseTactics && behavior.FleeHpPercent > 0 …` / `flags.UseTactics && RangedMaxDistance > 0 …`，
            // v1 通路根本不实现它们）。档位 0/1/2 展开后 tactics=关 ⇒ 这三格配了没人问，
            // 表现就是"远程怪照样贴脸、残血也不跑"，而策划会先去查射程和血量。
            // 判据必须是**展开后的那一位**（问 `MonsterAiFlags.Resolve`），不是档位号：
            // 档位 1 + 战术位强制开 = 同样生效，拿档位号判会把这种合法配法误报。
            if (!flags.UseTactics && (b.FleeHpPercent > 0 || b.RangedMaxDistance > 0f || b.RangedMinDistance > 0f))
                r.Warn("behavior.useTactics：配了保持距离/逃跑阈值，但展开后的战术位是关的（档位 " + b.AiProfile +
                       "）——这三格运行时不会被读（v1 通路不实现风筝与逃跑）；" +
                       "要生效请把战术位开到档位 3 或强制开");
        }

        // --- 技能挂载 ---
        if (draft.Skills.Count > MaxSkillMounts)
            r.Error("skills", $"挂载数 {draft.Skills.Count} 超过上限 {MaxSkillMounts}");
        var seqSet = new HashSet<int>();
        var skillIds = refs?.SkillIds();
        for (var i = 0; i < draft.Skills.Count; i++)
        {
            var s = draft.Skills[i];
            var path = $"skills[{i}]";
            if (s.SkillId <= 0) r.Error(path + ".skillId", "技能 id 必须为正");
            else if (skillIds != null && !skillIds.Contains(s.SkillId))
                r.Error(path + ".skillId", $"技能 {s.SkillId} 不在技能模板表里（运行时结算会落到“未解析”分支）");
            if (s.ChanceBps < 0 || s.ChanceBps > 10000) r.Error(path + ".chanceBps", "概率基点必须落在 0..10000");
            if (s.ChanceBps == 0) r.Warn(path + "：chanceBps=0 等于没挂");
            if (s.CastMs < 0) r.Error(path + ".castMs", "吟唱毫秒不得为负");
            if (s.CooldownMs < 0) r.Error(path + ".cooldownMs", "冷却毫秒不得为负");
            if (s.MaxRange <= 0) r.Error(path + ".maxRange", "最大射程必须 > 0，否则这只怪永远选不出它");
            if (s.MinRange > s.MaxRange) r.Error(path + ".minRange", "最小射程不得大于最大射程（不可满足区间 = 死行）");
            if (s.TargetSelect < 0 || s.TargetSelect > 4) r.Error(path + ".targetSelect", "选敌方式必须在 0..4");
            if (s.TargetCount <= 0) r.Error(path + ".targetCount", "目标数必须 > 0");
            // 只有"以目标为心的 AOE"这一档会打多个人（内核 MonsterCastingCore.SelectTargets 的口径）。
            // 不在这里拦的话，"单体技能配 5 目标"会安静地只打一个——比报错更难查。
            if (s.TargetCount > 1 && s.TargetSelect != 4)
                r.Error(path + ".targetCount", "targetCount>1 只在选敌方式=4（AOE 以目标为心）时生效；其余方式是单体");
            if (s.TargetSelect == 4 && s.AoeRadius <= 0)
                r.Warn(path + ".aoeRadius：AOE 技能没给半径，内核会按 5 米兜底（这不是策划要的那个数）");
            // 本作还没有"受控（减速/定身）"状态位，slot_type=3 判不出条件：内核会把它当战斗中处理。
            // 与其让它静默降级，不如在这里说清楚——否则"配了不放"会被当成冷却或概率问题查很久。
            if (s.SlotType == 3)
                r.Warn(path + "：时机=受控时 还没有对应状态位（内核按战斗中处理），现在配它等于配了个战斗技能");
            // 状态号这一族从切片 59 起**有消费者**了（怪物施法命中玩家 → DS 施加状态、封移动、写进快照状态位），
            // 所以这里不再是"配了没人读"的告警，而是**引用检查**：
            // 号必须在词表里——不认识的号运行期会被拒收并计数，但那时玩家只看到"这怪从不控人"。
            if (s.StatusId > 0 && !MonsterStatusCore.TryFromId(s.StatusId, out _))
                r.Error(path + ".statusId",
                        $"状态号 {s.StatusId} 不在词表里（{MonsterStatusKind.None}=没配；今天是 1=眩晕 2=沉默 3=定身 4=减速）"
                        + "——运行期会拒收这一条");
            if (s.StatusId > 0 && s.StatusDurationSeconds <= 0)
                r.Warn(path + ".statusId：配了状态号却没给时长：运行期按非法拒收，这条技能等于没有控制");
            // 强度那一格只对**减速**有意义：它是"减多少"，不是闸口。三条各自对称，
            // 免得出现"填了却没用"（那正是这一族最贵的坏形状）。
            if (s.StatusMagnitudeBps < 0 || s.StatusMagnitudeBps > 9000)
                r.Error(path + ".statusMagnitudeBps",
                        $"状态强度必须在 0..9000 基点（0=没填；9000=减到 10%）：实测 {s.StatusMagnitudeBps}。"
                        + "填 10000 等于把怪钉住，那是定身那一档的语义，请改用定身");
            if (s.StatusId == (int)MonsterStatusKind.Slow && s.StatusMagnitudeBps == 0)
                r.Warn(path + ".statusMagnitudeBps：配了减速却没给强度：运行期按「没填」处理，这只怪不会被减速");
            if (s.StatusId > 0 && s.StatusId != (int)MonsterStatusKind.Slow && s.StatusMagnitudeBps > 0)
                r.Warn(path + ".statusMagnitudeBps：强度只对减速生效；眩晕/沉默/定身不看这一格（配了不会报错，也不会有效果）");
            if (s.StartEmote > 0)
                r.Warn(path + ".startEmote：客户端起手动画表还没落地，这个 id 没有任何人会播");
            if (s.DamageScaleBps <= 0) r.Error(path + ".damageScaleBps", "伤害倍率基点必须 > 0");
            if (s.SlotType < 0 || s.SlotType > 3) r.Error(path + ".slotType", "时机必须在 0..3");
            // 系别位是"客户端施法条分色"的唯一依据，口径写死在 MonsterSchoolBits：**位号 == 域号**
            // （bit1 金 … bit5 土），物理单独占 bit0。越界的位（把倍率或十进制串粘进来）
            // 不会让任何一层报错，只会让条子画成中性色或错色系——所以在这里拦成一条明确错误。
            if (s.SchoolMask < 0 || s.SchoolMask > MaxSchoolMask)
                r.Error(path + ".schoolMask",
                    $"系别位必须落在 0..{MaxSchoolMask}（bit0=物理，bit1..5=金木水火土，位号即域号；如“水系技能”=8）");
            if (s.EmergencyHpPercent < 0 || s.EmergencyHpPercent > 100) r.Error(path + ".emergencyHpPercent", "残血阈值必须在 0..100");
            if (!seqSet.Add(s.SlotSeq)) r.Error(path + ".slotSeq", $"顺序值 {s.SlotSeq} 重复（遍历顺序是数据，不能靠插入历史）");
        }

        // --- 掉落 ---
        if (draft.LootBindings.Count > MaxLootBindings)
            r.Error("lootBindings", $"绑定数 {draft.LootBindings.Count} 超过上限 {MaxLootBindings}");
        var itemIds = refs?.ItemIds();
        var questIds = refs?.QuestIds();
        var questStates = refs?.QuestPublishStates();
        var totalEntries = 0;
        var normalEntries = 0;
        var conditionalEntries = 0;
        for (var i = 0; i < draft.LootBindings.Count; i++)
        {
            var lb = draft.LootBindings[i];
            var path = $"lootBindings[{i}]";
            if (lb.LootTableId < 0) r.Error(path + ".lootTableId", "掉落表 id 不得为负（0 = 无掉落）");
            else if (lb.LootTableId > 0)
            {
                // 号段约定：1..5（域）与 100..105（世界 BOSS，base=100）。
                // **这条今天不是硬错**：内容掉落走的是"绑定自带的条目"（DsWorldSession 用
                // MonsterContent.LootBindings(模板号) 拿到绑定后直接 MonsterLootCore.Roll），
                // 表号只是与 WuxingLootTables 域表对齐的**约定**，以及"这只怪一行绑定都没配"时
                // 退回开发期 provider 的那条路才用到的键（那条路走 ResolveLootTableId）。
                // 以前这句写的是"指到不存在的表 ⇒ 运行时掷出空集合"，那是**假话**：
                // 条目就在同一条绑定里，表号写 1001 照样掉得出来。留成警告、说清它管什么。
                if (!(lb.LootTableId >= 1 && lb.LootTableId <= 5) && !(lb.LootTableId >= 100 && lb.LootTableId <= 105))
                    r.Warn(path + ".lootTableId：" + lb.LootTableId + " 不在域表号段约定内（1..5 / 100..105）——" +
                           "掉落**照样会掉**（条目跟着绑定走），但这个号与五行域表对不上，" +
                           "将来把这只怪并进技能树掉落、或走『一行绑定都没配所以退回 provider』那条路时找不到它");
            }
            if (lb.MinCount < 0 || lb.MaxCount < lb.MinCount) r.Error(path + ".minCount", "抽取件数区间非法");
            if (lb.RollChanceBps < 0 || lb.RollChanceBps > 10000) r.Error(path + ".rollChanceBps", "触发概率基点必须落在 0..10000");
            if (lb.ConditionKind < 0 || lb.ConditionKind > 3) r.Error(path + ".conditionKind", "条件类型必须在 0..3");
            if (lb.ConditionKind == 1)
            {
                if (lb.QuestId <= 0) r.Error(path + ".questId", "条件=任务持有者时必须给任务 id");
                else if (questIds != null && !questIds.Contains(lb.QuestId))
                    r.Error(path + ".questId", $"任务 {lb.QuestId} 不存在 → 这件掉落对任何人都永不出现");
            }
            if (lb.MoneyMax < lb.MoneyMin) r.Error(path + ".moneyMax", "金币上界不得小于下界");
            // 表级"抽几件"现在真的有消费者了：它是这张表互斥组的重掷轮数（AC 的 nonRefIterationsLeft）。
            // 没有互斥组的表配了它仍然什么都不做——那一半必须当场念出来，否则又是一个"配了没作用"。
            if (lb.MinCount < 0 || lb.MaxCount < 0) r.Error(path + ".minCount", "抽取件数不得为负");
            else if (lb.MaxCount < lb.MinCount)
                r.Error(path + ".maxCount", "抽取件数上界不得小于下界（内核会把上界收拢成下界，但那是在替你猜）");
            else if (lb.MaxCount > MonsterLootCore.MaxTableRounds)
                r.Warn(path + ".maxCount：运行时钳到 " + MonsterLootCore.MaxTableRounds + " 轮，配更大的数没有意义");
            else if (lb.MaxCount > 1)
            {
                var hasGroup = false;
                for (var j = 0; j < lb.Entries.Count; j++)
                {
                    if (lb.Entries[j].GroupId > 0) { hasGroup = true; break; }
                }
                if (!hasGroup)
                    r.Warn(path + ".maxCount：这张表没有任何互斥组，\"抽几件\"只作用在互斥组的重掷上——" +
                           "组外的逐件独立掷掉几件由各件概率决定（决策 D4），这两个数字现在没有作用对象");
            }
            // 金币这一段以前是"掷得出、进不了口袋"：内核算得出数额、DS 也只累计了计票，全项目没有货币写入端。
            // 那一跳已经接上（KillMoney 事件 → 玩法事件消费方 → 钱包 AddAsync），所以这里不再对 moneyMax 报警：
            // 一条"每填必响"的警告会被当成背景噪声，反而把真问题埋掉（闸门只该拦有害的配法）。
            // 剩下的口径与物品一致——只给击杀者，组队分金是另一条待拍板的事，见设计文档 §11.2。
            if (lb.Entries.Count > MaxDropEntries)
                r.Error(path + ".entries", $"逐件条目 {lb.Entries.Count} 超过上限 {MaxDropEntries}");
            totalEntries += lb.Entries.Count;
            var dropSeqSet = new HashSet<int>();

            for (var j = 0; j < lb.Entries.Count; j++)
            {
                var e = lb.Entries[j];
                var ep = $"{path}.entries[{j}]";
                if (e.ItemId <= 0) r.Error(ep + ".itemId", "物品 id 必须为正");
                else if (itemIds != null && !itemIds.Contains(e.ItemId))
                    r.Error(ep + ".itemId", $"物品 {e.ItemId} 不在物品模板表里（掉出来是空气）");
                if (e.ChanceBps < 0 || e.ChanceBps > 10000) r.Error(ep + ".chanceBps", "独立概率基点必须落在 0..10000");
                if (e.CountMax < e.CountMin || e.CountMin < 1) r.Error(ep + ".countMin", "数量区间非法");
                // 掷骰顺序重复只提示不拒：草稿里 seq 默认全 0，"没显式排序"是常态而不是错配。
                // 装配层用 (seq, itemId) 稳定兜底，所以重复的后果是"骰子按物品 id 分配"而不是"每次随机漂"，
                // 但策划以为的"第 2 行用第 2 颗骰子"并不成立——这句话必须当场说清楚。
                if (e.Seq >= 0 && !dropSeqSet.Add(e.Seq) && lb.Entries.Count > 1)
                    r.Warn(ep + $".seq：顺序值 {e.Seq} 在本绑定内重复，同序条目改按物品 id 排——" +
                           "骰子分配仍然稳定，但不是你写的行序");

                // 组号：魔兽的 groupid 只有 7 位（AzerothCore 直接丢弃 >=128 的行，LootMgr.cpp:335-339）。
                // 我们不丢行——丢行等于"配了不掉"，是最难归因的一种表现——而是拒收，让策划当场改。
                if (e.GroupId < 0)
                    r.Error(ep + ".groupId", "组号不得为负：运行时按「逐件独立掷」处理，与把它们捆成组的意图相反");
                else if (e.GroupId > MonsterLootCore.MaxGroupId)
                    r.Error(ep + ".groupId", $"组号 {e.GroupId} 超过魔兽口径的上限 {MonsterLootCore.MaxGroupId}（groupid 只有 7 位）");
                else if (e.GroupId == 0 && e.ChanceBps == 0)
                    // 同一个 0 在组内外含义相反：组外=永不掉，组内=平分显式档没吃到的余额。
                    // 不写出来的话，"我给这件配了保底随机"与"这件一次都没掉过"会在同一份数字下并存。
                    r.Warn(ep + ".chanceBps：组外（groupId=0）填 0 等于这件永不掉；" +
                               "若想让它参与「保底随机抽一件」，请把 groupId 填成组号——组内填 0 的含义是平分显式档剩下的余额");

                // 逐件条件（魔兽的 QuestRequired 挂在每一行上，LootMgr.h:127-149）。
                // 解析规则问内核，不在这里再写一遍 ternary——两处各写迟早分叉。
                if (e.ConditionKind < MonsterLootCore.ConditionInherit || e.ConditionKind > 3)
                    r.Error(ep + ".conditionKind", "逐件条件必须落在 -1..3（-1=继承所属绑定，0=无条件，1=任务持有者，2=组队，3=稀有度）");
                if (e.QuestId < 0) r.Error(ep + ".questId", "逐件任务 id 不得为负");

                var resolvedKind = MonsterLootCore.ResolveConditionKind(e.ConditionKind, lb.ConditionKind);
                var resolvedQuest = MonsterLootCore.ResolveQuestId(e.QuestId, lb.QuestId);
                if (resolvedKind == MonsterLootCore.ConditionQuestHolder) conditionalEntries++;
                else if (resolvedKind == MonsterLootCore.ConditionNone) normalEntries++;
                if (resolvedKind == MonsterLootCore.ConditionQuestHolder)
                {
                    if (resolvedQuest <= 0)
                        // 内核这一层判不了任务状态，"没有任务 id"的条件件走的是「判不了→不给」那一档：
                        // 表现是这件配了 100% 却一次都没发出去，而三处数字看着都正常。
                        r.Error(ep + ".questId", "这件按「任务持有者」出账，但条目与绑定都没有任务 id ⇒ 运行时永远不发放");
                    else if (questIds != null && !questIds.Contains(resolvedQuest))
                        r.Error(ep + ".questId", $"任务 {resolvedQuest} 不存在 → 这件掉落对任何人都永不出现");
                    else if (questStates != null && questStates.TryGetValue(resolvedQuest, out var questState)
                             && questState != QuestOnlineState)
                        // 存在 ≠ 接得到：QuestGrain 只认 Online 模板。这一句就是本轮真踩到的坑
                        // （墨麒麟那件挂了离线任务 20001）：配了、库里有、快照有，玩家一次也拿不到。
                        r.Warn(ep + ".questId：任务 " + resolvedQuest + " 当前没有上线（publish_state=" + questState + "）⇒ " +
                               "这件掉落对任何人都永不出现；要么等任务上线，要么换一个在线任务");
                }
                else if (e.QuestId > 0)
                    r.Warn(ep + ".questId：这件的条件不是「任务持有者」，任务 id 不会被任何人读到（死配置）");
                else if (resolvedKind == 2 || resolvedKind == 3)
                    r.Warn(ep + ".conditionKind：组队/稀有度这两档内核当前判不了，一律按「不给」出账（有计数但没有发放）；" +
                               "要真生效等控制状态层，不是怪物编辑器的缺口");
            }

            // 组级体检：按 (seq, itemId) 排成内核实际使用的成员次序，再看余额怎么被吃掉。
            for (var gid = 1; gid <= MonsterLootCore.MaxGroupId; gid++)
            {
                var members = new List<MonsterDropDraft>();
                for (var j = 0; j < lb.Entries.Count; j++)
                {
                    if (lb.Entries[j].GroupId == gid) members.Add(lb.Entries[j]);
                }
                if (members.Count == 0) continue;
                members.Sort((a, b) =>
                {
                    var bySeq = a.Seq.CompareTo(b.Seq);
                    return bySeq != 0 ? bySeq : a.ItemId.CompareTo(b.ItemId);
                });

                var explicitSum = 0;
                var explicitCount = 0;
                var equalCount = 0;
                var guaranteedAt = -1;
                for (var j = 0; j < members.Count; j++)
                {
                    var chance = Math.Clamp(members[j].ChanceBps, 0, 10000);
                    if (chance <= 0) { equalCount++; continue; }
                    explicitCount++;
                    explicitSum += chance;
                    if (chance >= 10000 && guaranteedAt < 0) guaranteedAt = j;
                }
                var gp = $"{path}.groupId={gid}（{members.Count} 件互斥，最多出一件）";

                // 累计减法的后果：组内合计超过 100% 时，后面的成员只拿"余额"，
                // 而不是自己所填的数字。魔兽在装载时给这条打错误日志（LootMgr.cpp:1497-1505）但照做，
                // 所以这里也照做——只是必须让人在录入当场看见实际拿到的份额。
                if (explicitSum > 10000)
                    r.Warn(gp + $"：显式概率合计 {explicitSum}bps 已超过 10000——排在后面的成员只拿前面吃剩的份额，" +
                                "实际中签率远低于它自己填写的数字");
                if (explicitSum >= 10000 && equalCount > 0)
                    r.Warn(gp + $"：有 {equalCount} 件把概率填 0（等概率档），但显式档已经吃满 100%——它们一次也轮不到");
                if (guaranteedAt >= 0 && guaranteedAt < members.Count - 1)
                    r.Warn(gp + $"：第 {guaranteedAt + 1} 件填了 ≥100%，运行时它一被轮到就直接中奖，" +
                                $"排在它后面的 {members.Count - guaranteedAt - 1} 件（含等概率档）连减法都参与不了");
                if (explicitCount == 0 && equalCount == members.Count)
                    r.Warn(gp + $"：整组都是等概率档，运行时每次击杀必定从这 {equalCount} 件里抽 1 件——确认这是「保底抽一件」而不是忘了填概率");
            }
        }
        if (draft.LootBindings.Count == 0 && draft.WuxingDomain == 0 && draft.Rank < 3)
            r.Warn("未配掉落且未定域：这只怪什么都不掉（这是设计允许的，但请确认不是漏配）");

        // 件数上限（2026-10-29 拍板：一具尸体 18 件普通 + 32 件任务，AC Loot::MAX_NR_LOOT_ITEMS / MAX_NR_QUEST_ITEMS）。
        // 运行期由 MonsterLootCore 在入桶时硬拦（超出即丢、有计数）；这里管的是另一种情况——
        // 候选条目本来就装不下。"配了 30 条必掉、屏幕上只有 18 件"不是报错，但按配置数读结果会以为丢了 12 件东西。
        if (normalEntries > MonsterLootCore.MaxNormalLootItems)
            r.Warn($"掉落候选里 {normalEntries} 条普通件（全部条目 {totalEntries} 条）超过一具尸体的 {MonsterLootCore.MaxNormalLootItems} 件上限：" +
                   "运行期按 (绑定序, 组序, 条目序) 先到先得，超出的部分会被丢弃并入 ItemsDroppedOverCapacity");
        if (conditionalEntries > MonsterLootCore.MaxQuestLootItems)
            r.Warn($"掉落候选里 {conditionalEntries} 条任务件超过一具尸体的 {MonsterLootCore.MaxQuestLootItems} 件上限：超出的部分运行期丢弃");

        // --- 巡逻路线 ---
        var route = draft.PatrolRoute;

        // 路线引用：草稿里带了这条怪自己的路线（routeId），行为表与刷怪点却可以填任意 id。
        // 引用一个本草稿之外的路线**是合法的**（路线可以跨怪共用），所以对不上的只提示；
        // 但"填了 id 而这只怪根本没配路线"是另一种情形——运行时按 id 查不到就用不上，
        // 表现是"我给它画了巡逻线，它却在原地踱步"，这条必须当场念出来。
        if (draft.Behavior != null && draft.Behavior.PatrolRouteId > 0 && route == null)
            r.Warn("behavior.patrolRouteId：" + draft.Behavior.PatrolRouteId +
                   " 指向一条路线，但本草稿没带路线数据——若库里也没有这条 id，运行时查不到会退化成无路线踱步");
        foreach (var sp in draft.SpawnPoints)
        {
            if (sp.RouteOverrideId > 0 && route == null)
                r.Warn($"spawnPoints[{sp.SpawnPointId}].routeOverrideId：" + sp.RouteOverrideId +
                       " 覆盖了一条路线，但本草稿没带路线数据——覆盖不生效时表现为按行为表的路线巡逻");
            // 覆盖**现在真的会被运行时采用**（DsMonster.PatrolRouteId：刷怪点覆盖 → 行为表），
            // 所以多出一条更该当场说的：这只怪画的是 route.RouteId，点位却覆盖成了另一个 id。
            // 结果不是报错，而是"我画的这条根本不会走"——配置与表现分家里最难查的一种。
            // 引用别的路线本身合法（路线可跨怪共用），所以只提示、不拦。
            else if (sp.RouteOverrideId > 0 && route != null && sp.RouteOverrideId != route.RouteId)
                r.Warn($"spawnPoints[{sp.SpawnPointId}].routeOverrideId={sp.RouteOverrideId} 与本草稿画的路线 routeId={route.RouteId} 不同：" +
                       "运行时会走覆盖那条，你画的这条不会生效");
        }
        if (route != null)
        {
            if (route.Mode < 0 || route.Mode > 2) r.Error("patrolRoute.mode", "行进方式必须在 0..2");
            if (route.PauseAtPointSeconds < 0) r.Error("patrolRoute.pauseAtPointSeconds", "停留秒不得为负");
            if (route.Points.Count < 2)
                r.Warn("patrolRoute：点数少于 2，谈不上巡逻（运行时会退化成踱步）");
            if (route.Points.Count > MaxRoutePoints)
                r.Error("patrolRoute.points", $"点数 {route.Points.Count} 超过上限 {MaxRoutePoints}");
            var pointSeq = new HashSet<int>();
            for (var i = 0; i < route.Points.Count; i++)
            {
                var p = route.Points[i];
                if (!pointSeq.Add(p.Seq)) r.Error($"patrolRoute.points[{i}].seq", $"顺序 {p.Seq} 重复");
                if (p.PauseSeconds.HasValue && p.PauseSeconds.Value < 0)
                    r.Error($"patrolRoute.points[{i}].pauseSeconds", "点停留秒不得为负（要默认值请留空而不是写 -1）");
                if (float.IsNaN((float)p.X) || float.IsInfinity((float)p.X)) r.Error($"patrolRoute.points[{i}].x", "坐标非法");
            }
            if (route.UseNavMesh == 1 && draft.Behavior != null)
            {
                var flags = MonsterAiFlags.Resolve(
                    (MonsterAiProfile)Math.Clamp(draft.Behavior.AiProfile, 0, 3),
                    draft.Behavior.FlagUseThreat, draft.Behavior.FlagUseSkills,
                    draft.Behavior.FlagUseNavMesh, draft.Behavior.FlagUseAssist, draft.Behavior.FlagUseTactics);
                if (!flags.UseNavigation)
                    r.Warn("路线声明走图，但导航能力位被档位/覆盖关着：连段会退化成直线");
            }
        }

        // --- 刷怪点 ---
        var existingMonsters = refs?.MonsterIds();
        var spawnIds = new HashSet<int>();
        for (var i = 0; i < draft.SpawnPoints.Count; i++)
        {
            var sp = draft.SpawnPoints[i];
            var path = $"spawnPoints[{i}]";
            if (sp.SpawnPointId <= 0) r.Error(path + ".spawnPointId", "刷怪点 id 必须为正");
            else if (!spawnIds.Add(sp.SpawnPointId)) r.Error(path + ".spawnPointId", $"刷怪点 {sp.SpawnPointId} 在本次提交里重复");
            else if (existingMonsters != null && sp.SpawnPointId > 0 && draft.MonsterId > 0)
            {
                // 引用完整性在"保存这一怪"的时刻判：它自己的编号必须在库里（发布后成立）；
                // 别的怪引用它则由 publish 前的这条挡住——先建模板再建点。
            }
            if (sp.MaxAlive < 1 || sp.MaxAlive > MaxAliveLimit)
                r.Error(path + ".maxAlive", $"同时存活必须落在 1..{MaxAliveLimit}（槽位数参与实体 id 分配，超限会让一个点吃掉整片 id 段）");
            if (sp.SlotSpacing <= 0) r.Error(path + ".slotSpacing", "槽位间距必须 > 0，否则多只怪重叠在同一格");
            if (sp.LevelOverride.HasValue)
            {
                var lv = sp.LevelOverride.Value;
                if (lv < draft.LevelMin || lv > draft.LevelMax)
                    r.Error(path + ".levelOverride", $"等级覆盖 {lv} 超出模板区间 [{draft.LevelMin},{draft.LevelMax}]");
            }
            if (sp.RespawnOverrideSeconds < 0) r.Error(path + ".respawnOverrideSeconds", "刷新覆盖秒不得为负");
            if (sp.Enabled < 0 || sp.Enabled > 1) r.Error(path + ".enabled", "enabled 必须是 0/1");
        }

        if (draft.PublishState < 0 || draft.PublishState > 2) r.Error("publishState", "发布状态必须在 0..2");
        if (draft.ContentVersion < 0) r.Error("contentVersion", "内容版本不得为负");

        // --- 掉落期望的"截断上限要两边各算一遍" ---
        var neededEntries = 0;
        foreach (var lb in draft.LootBindings) neededEntries += lb.Entries.Count;
        if (neededEntries > MaxDropEntries * MaxLootBindings)
            r.Error("lootBindings", $"逐件条目总数 {neededEntries} 超过闸门容量 {MaxDropEntries * MaxLootBindings}（注释里的“约”不算证据，这里是实测）");

        return r;
    }

    private static void CheckFlag(MonsterValidationResult r, string path, int? value)
    {
        if (value.HasValue && value.Value != 0 && value.Value != 1)
            r.Error(path, "能力位覆盖只能是 null（跟档位）/ 0（强制关）/ 1（强制开）");
    }
}
