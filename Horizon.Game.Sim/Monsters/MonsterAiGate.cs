using System;

namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// "这只怪该不该对某人起手 / 该不该因为距离回家"这三条口径的**唯一主人**
/// （依据 AzerothCore <c>Entities/Creature/Creature.cpp</c> 的 <c>CanCreatureAttack</c>）。
/// <para>
/// <b>为什么要有这个类</b>：这三条以前只写在 v2（<c>MonsterAiRuntime.Advance</c>）里，
/// 而宿主在能力位全关时根本不进 v2（<c>TryDecideMonsterV2</c> 一进门就 return null）——
/// 于是当前所有种子怪（<c>ai_profile=0</c>）**一条都没享受过**，规则实际只活在测试里。
/// v1 与 v2 现在都调这里的式子：同一条规则写两遍，迟早有一处漏改，而漏改的那一处
/// 玩家看得见（贴刷新点被刚冒出来的怪秒咬、坦克拉出去的老远怪半路回头）。</para>
/// </summary>
public static class MonsterAiGate
{
    /// <summary>复活后不许**主动**起手的秒数（AC 里写死的 5 秒：<c>(now - m_respawnedTime) &lt; 5</c>）。</summary>
    public const float RespawnGraceSeconds = 5f;

    /// <summary>
    /// 连续"够不到目标"多久就脱战（秒）。取自 AC <c>CONFIG_NPC_EVADE_IF_NOT_REACHABLE</c> 的缺省 5 秒
    /// （<c>EVADE_REASON_NO_PATH</c>：超过这个窗口仍够不到 ⇒ 脱）。
    /// <para>v1 与 v2 都问这一格：两条通路的"够不到"判法不同（v1 只看下一步那格、v2 看 A* 结果），
    /// 但**窗口长度必须是同一个数**——否则同一张行为表换一档 AI 就换了一套脱战节奏。</para>
    /// </summary>
    public const float NoPathEvadeSeconds = 5f;

    /// <summary>宽限窗的截止 tick（两条通路共用同一次换算；调用方自己先把 tick/秒 归一化）。</summary>
    public static long RespawnGraceUntil(long serverTick, int ticksPerSecond)
        => serverTick + (long)(RespawnGraceSeconds * ticksPerSecond);

    /// <summary>是否还在"刚刷出来/刚复活"的宽限窗内（窗内不许**主动**起手，先挨的那一手照样还手）。</summary>
    public static bool InRespawnGrace(long serverTick, long noInitiateUntilTick)
        => serverTick < noInitiateUntilTick;

    /// <summary>
    /// 这只怪正在"<b>回位整段路上</b>"（v1 的 <c>Leash</c> / v2 的 <c>Evade</c>）。
    /// <para>AzerothCore 把这件事做成<b>状态</b>而不是秒数：<c>CreatureAI::EnterEvadeMode</c>
    /// （<c>AI/CreatureAI.cpp:205-233</c>）进 evade 时 <c>AddUnitState(UNIT_STATE_EVADE)</c> 再
    /// <c>MoveTargetedHome</c>，直到 <c>SmartAI::MovementInform</c> 收到"到达 OOC 点位"才
    /// <c>ClearUnitState</c>（<c>AI/SmartScripts/SmartAI.cpp:653-657</c>）；整段路里它不吃仇恨
    /// （<c>Entities/Unit/Unit.cpp:1092</c>、<c>:14765</c>）。
    /// 本作的 <c>EvadeImmunitySeconds</c> 只盖"到家之后"那几秒，<b>路上这一整段以前没人挡</b>：
    /// 于是"怪正往家跑，队友喊一嗓子/旁边同族打起来了，它半路就掉头"——玩家看得见，而 AC 不会。</para>
    /// </summary>
    public static bool ReturningHome(Horizon.Game.Sim.MonsterAiState state)
        => state == Horizon.Game.Sim.MonsterAiState.Leash || state == Horizon.Game.Sim.MonsterAiState.Evade;

    /// <summary>
    /// 这一 tick 不许<b>接新敌</b>的三个来源：复活宽限、回位免疫窗、回位整段路。
    /// <para>只回答"能不能接新敌"，不管"有没有已锁定的目标"——那条例外留给
    /// <see cref="BlocksNewInitiation"/>，因为"先挨的那一手"是另一件事。</para>
    /// </summary>
    public static bool BlockedFromNewEnemy(long serverTick, long noInitiateUntilTick, long evadeImmuneUntilTick,
                                           bool returningHome)
        => returningHome || InRespawnGrace(serverTick, noInitiateUntilTick) || InEvadeImmunity(serverTick, evadeImmuneUntilTick);

    /// <summary>
    /// 回位免疫窗的截止 tick：秒数 ≤0 时返回 0 = **不开窗**（不是"开到世界尽头"）。
    /// <para>这条以前只有 v2 那份状态机做；v1 的怪跑回家既不回血也没窗，
    /// 于是"远程风筝把怪磨到一丝血再耗死"对全部种子怪成立，而开了威胁表的怪会唰地满血。</para>
    /// </summary>
    public static long EvadeImmunityUntil(long serverTick, float immunitySeconds, int ticksPerSecond)
        => immunitySeconds > 0f ? serverTick + (long)(immunitySeconds * ticksPerSecond) : 0L;

    /// <summary>是否在"刚回位不接新敌"的窗内（窗内可以挨打，不主动接敌）。</summary>
    public static bool InEvadeImmunity(long serverTick, long evadeImmuneUntilTick)
        => serverTick < evadeImmuneUntilTick;

    /// <summary>
    /// 这一 tick 不许<b>主动起手</b>（宽限窗／回位免疫窗／回位整段路，且还没有已锁定的目标）。
    /// <para>"还没有已锁定的目标"这半边是关键：三处挡的都是"接新敌"，
    /// 先挨的那一手（宿主把攻击者写进单槽仇恨）不该被挡——否则表现会变成"打了它它也不还手"。</para>
    /// </summary>
    public static bool BlocksNewInitiation(long serverTick, long noInitiateUntilTick, long evadeImmuneUntilTick,
                                           bool returningHome, bool hasLockedTarget)
        => !hasLockedTarget && BlockedFromNewEnemy(serverTick, noInitiateUntilTick, evadeImmuneUntilTick, returningHome);

    /// <summary>
    /// 已经选中目标（威胁表里有它）的怪，这一 tick 要不要把目标<b>清零</b>：只有"回位"才清——
    /// 免疫窗内或整段回家的路上；<b>复活宽限不覆盖这一条</b>。
    /// <para>区别就在这里：宽限窗挡的是"接新敌"，而打进威胁表的那一手不是它自己接的
    /// （AC 的 <c>!IsEngagedBy(victim)</c> 例外，<code>Creature.cpp:2606</code>）；
    /// 把宽限也算进这里，表现就变成"刚复活的怪被我砍了两刀，它还是不打我"。</para>
    /// </summary>
    public static bool WipesSelectedTarget(long serverTick, long evadeImmuneUntilTick, bool returningHome)
        => returningHome || InEvadeImmunity(serverTick, evadeImmuneUntilTick);

    /// <summary>
    /// 外力（嘲讽）这一 tick 能不能把它拉回战斗：<b>回位整段路与回位免疫窗都不行</b>。
    /// <para>这里<b>没有</b>"已锁定目标就放行"的例外——外力不是"它先挨的那一手"。
    /// 也<b>不带</b>复活宽限：AC 那条判据管的是"怪自己不许起手攻击"，被嘲讽钉住是另一件事，
    /// 源码里读不到明确答案就不自己发明（见设计文档 §11.2 第 13 条）。</para>
    /// </summary>
    public static bool CanBeForcedIntoCombat(long serverTick, long evadeImmuneUntilTick, bool returningHome)
        => !returningHome && !InEvadeImmunity(serverTick, evadeImmuneUntilTick);

    /// <summary>
    /// 这一 tick 它<b>吃不吃打</b>（AC 挂在<b>受击方</b>那侧的 <code>Creature::IsEvadingAttacks()</code>，
    /// <code>Entities/Creature/Creature.h:139</code>）：回位整段路上谁都打不到它。
    /// <para>AC 把这一条铺在五处，一处都不留：<code>Unit::CanAttack</code>（<code>Unit.cpp:10496</code>，
    /// 攻击指令本身）、<code>RollMeleeOutcomeAgainst</code>（<code>:2846</code>，已经在空中的那一刀没有结果）、
    /// <code>DealSpellDamage</code>（<code>:1480</code>）与 <code>DealMeleeDamage</code>（<code>:1867</code>）
    /// （伤害落地那一步）、<code>SpellHitResult</code>（<code>:3529</code> 与 <code>:3602</code>，
    /// 返回 <code>SPELL_MISS_EVADE</code>，玩家屏幕上那行"闪避"就是它）。
    /// 五处叠在一起才是"打不到"这个观感；只做其中一处会出现"条子说没打到而血照掉"。</para>
    /// <para><b>这条刻意比"接新敌"窄</b>：复活宽限那 5 秒<b>不挡打</b>（AC 的宽限写在
    /// <code>CanCreatureAttack</code> 的<b>攻击方</b>判据里，受击方那侧没有它——所以"刚冒出来的怪"
    /// 是你打得它而它不还手，不是打不到）；到家之后的免疫窗也<b>不挡打</b>（那只管不接新敌，
    /// 血已经在到家那一 tick 刷满了）。把三样合成一条会同时造出两处与 AC 相反的表现，
    /// 所以这里<b>只认回位状态</b>这一个输入。AC 那半边 <code>|| CanNotReachTarget()</code>
    /// （卡在移动里也算"打不到"）本作没有对应概念——服务端没有移动卡死检测，不凭空发明。</para>
    /// </summary>
    public static bool BlocksIncomingDamage(bool returningHome) => returningHome;

    /// <summary>这个实体还在候选集合里、还活着、还打得着吗。</summary>
    public static bool IsEngageable(ulong entityId, ReadOnlySpan<MonsterAiTarget> targets)
    {
        for (var i = 0; i < targets.Length; i++)
        {
            if (targets[i].EntityId == entityId) return targets[i].IsAlive && targets[i].IsAttackable;
        }
        return false;
    }

    /// <summary>
    /// 嘲讽锁是否还成立：有对象、没过期、且**那个人还在场可打**。
    /// <para>三条出口必须一起判，少一条就露出"锁撤了、距离检查还被锁挡着"的组合：
    /// 怪既没目标又不脱战，站在原地当木桩。</para>
    /// </summary>
    public static bool TauntHolds(ulong tauntTargetId, long tauntUntilTick, long serverTick,
                                  ReadOnlySpan<MonsterAiTarget> targets)
        => tauntTargetId != 0UL && serverTick < tauntUntilTick && IsEngageable(tauntTargetId, targets);

    /// <summary>
    /// "离家过远就脱战回家"这一档对这只怪<b>成不成立</b>：世界 BOSS 不查（AC 写在 <c>!isWorldBoss()</c> 里），
    /// <b>站桩档也不查</b>——它根本走不回去。
    /// <para>后一半是必需的，不是锦上添花：切片 18 让"回位中打不到"真的生效之后，
    /// 一只不能移动的怪如果被判成"正在回位"，就再也出不去那个状态，
    /// 于是变成一只永远打不到的怪（玩家看得见的是"它站着不动而我砍不动它"）。
    /// AC 的 leash 是一段<em>移动</em>状态（<c>EnterEvadeMode</c> 里紧跟着 <c>MoveTargetedHome</c>），
    /// 不能动的怪没有这件事。</para>
    /// </summary>
    public static bool LeashApplies(MonsterRank rank, MonsterFollowType follow)
        => rank != MonsterRank.WorldBoss && MonsterPatrolCore.FollowProducesMovement(follow);

    /// <summary>
    /// 脱战距离判据（四条合起来）：世界 BOSS 不查、被嘲讽钉住的这段窗口不查、
    /// **刚挨过打的风筝宽限窗内不查**、其余按半径。
    /// <para>风筝宽限窗是 AC 原文里那一格（`Creature::CanCreatureAttack`）：
    /// <c>GetLastLeashExtensionTime() + GetLeashTimer() &gt; now || HasTauntAura()</c> 为真就整段跳过
    /// "距回家点"的检查，注释写着 "allow kiting away from spawnpoint"。本作以前只有半径和嘲讽旁路，
    /// 于是"边打边把怪拉出 30 米"会立刻判定离家过远——玩家看到的是一只会瞬移回家的怪，
    /// 而 WoW 里只要一直挂着伤害，它就会跟着走。</para>
    /// </summary>
    public static bool ExceededLeash(bool leashApplies, bool tauntHolds, bool kitingGraceActive,
                                     float distToSpawn, float leashRadius)
        => leashApplies && !tauntHolds && !kitingGraceActive && distToSpawn > leashRadius;

    /// <summary>
    /// 风筝宽限窗的秒数（AC `Creature::GetLeashTimer`：<c>11 + minlevel/10 - 2</c>，下限 11）。
    /// <para>AC 那一段是 <c>uint8</c> 运算，等级 &lt; 20 时 `minlevel/10 - 2` 会下溢绕回来，
    /// 绕回后的结果再被 <c>max(11, …)</c> 压回 11——所以**等级 &lt; 20 一律 11 秒**，
    /// 20 级起每 10 级 +1（70 级 16 秒）。这里按量出来的形状写，不照抄下溢本身。</para>
    /// </summary>
    public static int LeashExtensionSeconds(int monsterLevel)
        => Math.Max(11, 9 + (monsterLevel > 0 ? monsterLevel / 10 : 0));

    /// <summary>
    /// 宽限窗此刻还开着吗。
    /// <para><b>0 = 从未挨过打，不是"很久以前挨过"</b>：结构体默认值 0 与"tick 0 挨打"同形，
    /// 若把 0 当起点，那么每只怪在开局 11 秒内都免检距离——回位、嘲讽压 leash、金轨迹全被这扇门吃掉。
    /// 判据因此是合取：有起点 + 起点不晚于现在 + 差小于窗长。</para>
    /// </summary>
    public static bool KitingGraceActive(long nowTick, long lastLeashExtensionTick, int windowTicks)
        => lastLeashExtensionTick > 0
           && windowTicks > 0
           && nowTick >= lastLeashExtensionTick
           && nowTick - lastLeashExtensionTick < windowTicks;

    /// <summary>
    /// 目标在它的<b>索敌扇形</b>里吗（内容表 <c>aggro_cone_degrees</c> 那一格，编辑器"索敌角度"）。
    /// <para>口径三条：<b>&lt;=0 或 &gt;=360 都算全向</b>（这一格没配不等于"看不见人"）；
    /// <b>贴在脚下</b>（距离≈0）时方向没有定义，按"看得见"处理——让玩家成功贴到怪脚上却让它
    /// 无视自己，是更坏的那种"看不见"；其余按"朝向与连线的夹角不超过半扇"判。</para>
    /// <para>以前这条式子只写在 v2 的索敌里（<c>MonsterAiRuntime.SelectByAggroRangeAndCone</c>），
    /// v1 只比半径 ⇒ 默认档（生产全部种子怪）的怪那一格是装饰：画了窄扇形，背后走来的人照样被咬。</para>
    /// <para><b>这一格不是 AzerothCore 的口径</b>，别去 AC 里找它：AC 的索敌是一整个球
    /// （<code>Creature::GetAggroRadius</code>，<code>Entities/Creature/Creature.cpp:3225-3270</code>——
    /// 基准 <code>GetDetectionRange()</code>、按等级差每级约 1 码、等级差截到 -25、保底 5 码、再叠两类光环），
    /// 读不到"扇形"这一说。接进 v1 是为了"配了要生效"，不是"照抄 AC"；
    /// AC 那条<b>等级差半径</b>才是还没落地的口径（见设计文档 §11.2 第 15 条）。</para>
    /// </summary>
    public static bool InAggroCone(float coneDegrees, float yaw, float dx, float dy)
    {
        var cone = coneDegrees <= 0f ? 360f : coneDegrees;
        if (cone >= 360f) return true;

        var sq = dx * dx + dy * dy;
        if (sq <= 1e-6f) return true;

        var inv = 1f / MathF.Sqrt(sq);
        var cosDelta = dx * inv * MathF.Cos(yaw) + dy * inv * MathF.Sin(yaw);
        return cosDelta >= MathF.Cos(cone * 0.5f * (MathF.PI / 180f));
    }

    /// <summary>
    /// 这只怪会不会<b>自己先看上人</b>（内容表 <code>is_aggressive</code> 那一格的反面）。
    /// <para>被动怪的定义是"不打你先手"，<b>不是</b>"打不动它它也不还手"：先挨的那一手由宿主
    /// 写进单槽仇恨/威胁表，那条通路不经过这里（与复活宽限同一族例外）。</para>
    /// <para>以前这一格只在 v2 那一段被读（<code>MonsterAiRuntime</code> 的两处 inline），
    /// v1 的 <code>ResolveThreat</code> 根本没有这个输入 ⇒ 默认档（生产全部种子怪）配成被动也照样先咬人。
    /// 输入取 <c>isPassive</c> 而不是 <c>isAggressive</c> 是因为 v1 那份结构体的字段只能这么缺省
    /// （bool 的零值必须是"和今天一样"，见 <c>MonsterAiConfig.IsPassive</c> 的注释）。</para>
    /// </summary>
    public static bool MayInitiateOnSight(bool isPassive) => !isPassive;

    /// <summary>
    /// 索敌半径这一格的<b>唯一主人</b>：半径 ≤0 ⇒ 这一轮不成立（不去挑新目标）。
    /// <para>口径（2026-10-27 拍板）：<b>0 是非法值</b>，作者侧由校验器当场拒收
    /// （<code>MonsterValidator</code> 的 <code>behavior.aggroRadius</code>），装载侧由快照那份
    /// 报成坏输入；所以运行期这条只是"漏网之鱼不许自己发明一个距离"的兜底，
    /// 不是一种可配的玩法。</para>
    /// <para>以前这里是<b>两份清单</b>：v1 把 0 当"永不索敌"，v2 把 0 当"没配 ⇒ 退回宿主那份
    /// （缺省 10 米）"，理由是"别让编辑器没露出这一项变成永不索敌"。那句话现在由两道闸门去保证，
    /// 而不是由运行期偷偷补一个距离——因为"补出来的 10 米"会静默把一只配成 0 的怪变成能索敌的怪，
    /// 而两档 AI 通路的可见差别正是最难查的那类（同族见 <c>MayInitiateOnSight</c> 的注释）。</para>
    /// </summary>
    public static bool AggroRadiusUsable(float aggroRadius) => aggroRadius > 0f;

    /// <summary>
    /// "这只怪现在在战斗里吗"——**判据只此一份**（追击与攻击两态算在战斗，巡逻/待机/回位/脱战不算）。
    /// <para>为什么要收进来：宿主里原先有三处各自写 <c>state == Chase || state == Attack</c>
    /// （求援门槛、施法时机、盟友判定），而"进战喊话"是第四个消费者。
    /// 同一概念写四份，改一漏三的表现是"有的地方认为它在战斗、有的地方不认为"，
    /// 那比全都判错更难查（这条工程里已经付过几次学费）。</para>
    /// </summary>
    public static bool InCombat(MonsterAiState state) =>
        state == MonsterAiState.Chase || state == MonsterAiState.Attack;

    /// <summary>检测位里"隐蔽探测"那一位（`detect_mask` 的 bit2）。</summary>
    public const int DetectMaskStealth = 0x4;

    /// <summary>
    /// 这只怪看不看得见那个候选：**藏着的人**只有检测位带 bit2 才看得见，没藏着的一律看得见。
    /// <para>口径说明：AC 那边是一条连续值公式（<c>30 + 5×(level-1) + detect − stealth</c>，
    /// <c>Object.cpp:1981</c> <c>CanDetectStealthOf</c>），本作**明写不抄**——本作没有连续的侦测/隐匿数值，
    /// 抄一个只有半边输入的公式等于凭空发明（D32a 拍板：`detect_mask` 是本作独立口径，只接这一道闸）。
    /// 判据唯一主人，v1 与 v2 都问它。</para>
    /// </summary>
    public static bool CanDetectTarget(int detectMask, bool targetHidden) =>
        !targetHidden || (detectMask & DetectMaskStealth) != 0;

    /// <summary>AC 的"每级约 1 码"（<c>Creature.cpp:3249</c> 注释 <c>roughly 1 yard/level</c>）换算到本作单位：1 码 = 0.9144 米。</summary>
    public const float AggroMetersPerLevel = 0.9144f;

    /// <summary>AC <c>Unit.h:45</c> 的 <c>#define MAX_AGGRO_RADIUS 45.0f // yards</c> ⇒ 41.143 米。
    /// 这条回答了过去记成"上限归谁配"的那个问题：**上限是引擎里的一个硬常量，不是配置项**。</summary>
    public const float MaxAggroRadiusMeters = 45f * AggroMetersPerLevel;

    /// <summary>AC 的下限是"战斗距离 5 码"（<c>Creature.cpp:3262-3265</c>；那份对宠物取 10 码，本作没有宠物索敌通路，取 5）。</summary>
    public const float MinAggroRadiusMeters = 5f * AggroMetersPerLevel;

    /// <summary>AC 只往一个方向截等级差（<c>Creature.cpp:3238-3240</c>：<c>levelDiff &lt; -25 ⇒ -25</c>，
    /// 注释写着"最高 45 码 = 25 级差"）。等级差为正（怪比人高）那一侧 AC **不截**，靠下面的 5 码下限兜。</summary>
    public const int MaxAggroLevelDiffDown = -25;

    /// <summary>
    /// 对某个具体目标算这只怪的索敌半径——<b>这一格唯一的主人</b>，v1 与 v2 都问它，且都是<b>逐候选</b>问
    /// （AC 的 <c>GetAggroRange(Unit const* target)</c> 本来就是按目标算的，所以"同一只怪对贴身的高等级玩家
    /// 看得见、对远处的低等级玩家看不见"是同一份判据的两半，不是两份规则）。
    /// <para>口径逐条对齐 AC <c>Creature::GetAggroRange</c>（<c>Creature.cpp:3224-3266</c>）：
    /// ① <c>levelDiff = 怪等级 − 目标等级</c>；② 只截负方向到 −25；
    /// ③ <c>半径 −= levelDiff × 每码</c> ⇒ <b>玩家等级越高越容易被先看见</b>（怪比人高则视野收窄）；
    /// ④ 上下限各截一次（45 码 / 5 码）。本作单位是米，所以换算系数写在 <see cref="AggroMetersPerLevel"/> 上，
    /// 而不是把 AC 的码数当米抄。</para>
    /// <para><b>两处刻意没抄</b>：① AC 那两个光环修正
    /// （<c>SPELL_AURA_MOD_DETECT_RANGE</c> / <c>SPELL_AURA_MOD_DETECTED_RANGE</c>）——本作没有状态层（§8 D14），
    /// 抄了就是"配了没人读"的第三种形态；② AC 的世界速率 <c>Rate.Creature.Aggro</c>（默认 1，且为 0 时整条关闭）——
    /// 本作没有世界配置层（与掉落组重掷那一轮同一个理由），所以这一格今天<b>恒等于 1</b>。</para>
    /// <para>等级为 0 表示"这一路的输入没带等级"（旧内容、旧夹具、金轨迹都属这一档）⇒ <b>不调</b>。
    /// 这不是兜底玩法，而是让"接入等级差"这件事对既有内容零变化：0 与"未知"必须同形，
    /// 否则一只没配等级的怪会因为对面写了个 0 而视野凭空缩到 5 码。</para>
    /// </summary>
    public static float EffectiveAggroRadius(float authoredRadius, int creatureLevel, int targetLevel)
    {
        // 非法值不许被等级差"救活"成 5 码——0 是非法值这件事由作者侧与装载侧定，运行期只负责不给它翻身的机会。
        if (!AggroRadiusUsable(authoredRadius)) return authoredRadius;
        if (creatureLevel <= 0 || targetLevel <= 0) return authoredRadius;

        var levelDiff = creatureLevel - targetLevel;
        if (levelDiff < MaxAggroLevelDiffDown) levelDiff = MaxAggroLevelDiffDown;

        var radius = authoredRadius - levelDiff * AggroMetersPerLevel;
        if (radius > MaxAggroRadiusMeters) radius = MaxAggroRadiusMeters;
        if (radius < MinAggroRadiusMeters) radius = MinAggroRadiusMeters;
        return radius;
    }

    /// <summary>
    /// 被动怪要不要把<b>已经选中的</b>威胁清零：<b>不能</b>——"被动"管的是"不主动起手"，
    /// 而先挨的那一手与同族求援（<code>assist_radius</code> 把旁边不会索敌的同类叫起来）都不是"主动"。
    /// <para>求援那一半是这条判据存在的理由：不加豁免，求援对所有"需要被叫"的怪都是空转
    /// （内核确实写进了威胁表，却在收尾这一行被清零）。免疫窗不给这个例外——那条在更前面就挡掉了。</para>
    /// </summary>
    public static bool WipesThreatForPassive(bool isPassive, bool hasLockedTarget, bool calledByAlly)
        => isPassive && !hasLockedTarget && !calledByAlly;
}
