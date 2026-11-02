using System;

namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// AI 智能档位（决策清单里"AI 智能程度"的实现口径）。
/// <para>
/// WoW 没有一个叫"智能评分"的字段，但玩家一眼能感到"这怪笨/这怪精"。逆向下来的结论是：
/// 那种观感是<b>五个独立能力位</b>的组合，档位只是它们的预设值（单项仍可覆盖）。
/// </para>
/// </summary>
public enum MonsterAiProfile : byte
{
    /// <summary>笨：只咬最近的、不会技能、直线走、不求援。</summary>
    Dumb = 0,

    /// <summary>战：有威胁表（可被 OT），仍不会技能。</summary>
    Combatant = 1,

    /// <summary>精：威胁表 + 技能 + 走图 + 求援（精英怪）。</summary>
    Skilled = 2,

    /// <summary>王：全开 + 战术（拉距离/找掩体/残血逃跑），世界 BOSS。</summary>
    Master = 3,
}

/// <summary>
/// 已解析的 AI 能力位（档位在装配期展开成这一份，运行期只读位、不再查档）。
/// </summary>
/// <remarks>
/// <b>为什么"档位 + 单项覆盖"要展开成一份再交给内核</b>：如果内核里到处写
/// <c>if (profile &gt;= Skilled)</c>，那么"我想让这只 Lv8 的小怪会求援但不会技能"就配不出来，
/// 策划只能改档位——把档位表改成另一个档位，顺带改掉五项里另外四项。
/// 展开后档位只是"预设按钮"，覆盖才是真开关。
/// </remarks>
public readonly struct MonsterAiFlags
{
    /// <summary>用威胁表选目标（false = 只咬第一个打它的人）。</summary>
    public readonly bool UseThreatTable;

    /// <summary>会放技能。</summary>
    public readonly bool UseSkills;

    /// <summary>追击/巡逻/回位走导航图（false = 直线，会被树挡住）。</summary>
    public readonly bool UseNavigation;

    /// <summary>会向同族求援。</summary>
    public readonly bool UseAssist;

    /// <summary>会讲战术（保持距离、残血逃跑）。</summary>
    public readonly bool UseTactics;

    public MonsterAiFlags(bool threat, bool skills, bool nav, bool assist, bool tactics)
    {
        UseThreatTable = threat;
        UseSkills = skills;
        UseNavigation = nav;
        UseAssist = assist;
        UseTactics = tactics;
    }

    /// <summary>全关（最笨）。</summary>
    public static MonsterAiFlags None => new MonsterAiFlags(false, false, false, false, false);

    /// <summary>全开（王级）。</summary>
    public static MonsterAiFlags All => new MonsterAiFlags(true, true, true, true, true);

    /// <summary>档位预设。</summary>
    public static MonsterAiFlags ForProfile(MonsterAiProfile profile) => profile switch
    {
        MonsterAiProfile.Dumb => new MonsterAiFlags(false, false, false, false, false),
        MonsterAiProfile.Combatant => new MonsterAiFlags(true, false, false, false, false),
        MonsterAiProfile.Skilled => new MonsterAiFlags(true, true, true, true, false),
        MonsterAiProfile.Master => new MonsterAiFlags(true, true, true, true, true),
        _ => new MonsterAiFlags(true, false, false, false, false),
    };

    /// <summary>
    /// 档位 + 单项覆盖展开。
    /// </summary>
    /// <param name="profile">预设档位。</param>
    /// <param name="threatOverride">NULL = 跟档位；0/1 = 强制关/开。</param>
    public static MonsterAiFlags Resolve(
        MonsterAiProfile profile,
        int? threatOverride,
        int? skillsOverride,
        int? navOverride,
        int? assistOverride,
        int? tacticsOverride)
    {
        var preset = ForProfile(profile);
        return new MonsterAiFlags(
            threatOverride.HasValue ? threatOverride.Value != 0 : preset.UseThreatTable,
            skillsOverride.HasValue ? skillsOverride.Value != 0 : preset.UseSkills,
            navOverride.HasValue ? navOverride.Value != 0 : preset.UseNavigation,
            assistOverride.HasValue ? assistOverride.Value != 0 : preset.UseAssist,
            tacticsOverride.HasValue ? tacticsOverride.Value != 0 : preset.UseTactics);
    }

    /// <summary>逐项回显（编辑器的"档位展开成了什么"面板要能看见结果，不能只给个档位号）。</summary>
    public readonly string Describe() =>
        $"threat={(UseThreatTable ? 1 : 0)} skills={(UseSkills ? 1 : 0)} nav={(UseNavigation ? 1 : 0)} " +
        $"assist={(UseAssist ? 1 : 0)} tactics={(UseTactics ? 1 : 0)}";
}

/// <summary>怪物移动方式（决定寻路怎么用）。与 <c>MonsterAiFlags.UseNavigation</c> 的分工：后者是"这只怪会不会走图"，前者是"它用什么方式移动"。</summary>
public enum MonsterFollowType : byte
{
    /// <summary>步行：受地面与导航图约束（默认）。</summary>
    GroundWalk = 0,

    /// <summary>直线：完全不查图——按直线位移，会穿墙/贴树（某些元素怪就是这么设计的）。</summary>
    Straight = 1,

    /// <summary>
    /// 飞行：仍然走图（所以不会穿墙），但**忽略地面代价**——难走格（陡坡/灌木）对它折回基准代价，
    /// 于是它直接穿过灌木丛而不像地面单位那样绕开。
    /// <para>以前它与 <see cref="Straight"/> 在移动上完全同形（两个都不查图），
    /// 编辑器里那个下拉是个装饰；现在这一档真的不一样了（内核 v6）。</para>
    /// </summary>
    Fly = 2,

    /// <summary>不移动：站桩怪（炮台/法阵）。</summary>
    Static = 3,
}

/// <summary>巡逻路线的行进方式。</summary>
public enum MonsterPatrolMode : byte
{
    /// <summary>往返（到终点反向）。</summary>
    PingPong = 0,

    /// <summary>循环（终点接回第一点）。</summary>
    Loop = 1,

    /// <summary>随机下一点（由宿主提供的确定性随机选择，内核不持随机源）。</summary>
    RandomNext = 2,
}

/// <summary>
/// 怪物行为静态配置（编辑器"行为与 AI"面板的产物，装配后运行期不变）。
/// </summary>
/// <remarks>单位：米 / 秒。坐标口径与服务端一致（Z-up，水平面 = XY）。</remarks>
public struct MonsterBehaviorConfig
{
    /// <summary>AI 能力位（已展开）。</summary>
    public MonsterAiFlags Flags;

    /// <summary>主动怪（靠近即索敌）；false = 被动（必须先打它才还手）。WoW 里玩家一眼能分辨。</summary>
    public bool IsAggressive;

    /// <summary>索敌半径（米）。</summary>
    public float AggroRadius;

    /// <summary>索敌朝向锥（度）；360 = 全向。逆向规格 §3.1："背后贴着它不一定会醒"。</summary>
    public float AggroConeDegrees;

    /// <summary>脱战半径（米）：距出生点超过即 Evade。</summary>
    public float LeashRadius;

    /// <summary>Evade 回位后的免疫秒（期间不接受新仇恨，但仍可受伤害）。</summary>
    public float EvadeImmunitySeconds;

    /// <summary>
    /// 上面那一格的缺省秒数——**只有这一个主人**。
    /// <para>v1（<c>MonsterAiConfig.Default</c>）与行为表缺省都取它；内容快照 DTO、模板实体、
    /// 草稿模型那三处字面量由 <c>MonsterEditorContractTests</c> 按值对账。
    /// 一个概念四个数字各自写一遍，改一处就会让"编辑器里写的 3 秒"和"跑起来的 3 秒"不是同一个数。</para>
    /// </summary>
    public const float EvadeImmunityDefaultSeconds = 3f;
    /// <summary>求援半径（米；TrinityCore 默认 10）。<see cref="MonsterAiFlags.UseAssist"/> 为 false 时忽略。</summary>
    public float AssistRadius;

    /// <summary>检测类型位（bit0 视觉 / bit1 听觉 / bit2 隐蔽探测 …）。0 = 只按距离，不看隐蔽。</summary>
    public int DetectMask;

    /// <summary>是否具备隐蔽探测（<see cref="DetectMask"/> 的 bit2 便捷读法）。</summary>
    public readonly bool CanDetectStealth => (DetectMask & 0x4) != 0;

    /// <summary>无路线时的踱步半径（米）。</summary>
    public float WanderRadius;

    /// <summary>待机转巡逻秒。</summary>
    public float IdleToPatrolSeconds;

    /// <summary>巡逻速度（米/秒）。WoW 观测：明显慢于追击。</summary>
    public float WalkSpeed;

    /// <summary>追击速度（米/秒）。</summary>
    public float MoveSpeed;

    /// <summary>移动方式。</summary>
    public MonsterFollowType FollowType;

    /// <summary>近战挥击间隔（秒）。与"技能冷却"分开：玩家能听出怪的攻击节奏。</summary>
    public float MeleeSwingSeconds;

    /// <summary>远程怪的保持距离区间（米）；<see cref="MonsterAiFlags.UseTactics"/> 为 false 时忽略。</summary>
    public float RangedMinDistance;

    /// <summary>远程怪的保持距离上限（米）。</summary>
    public float RangedMaxDistance;

    /// <summary>残血逃跑阈值百分比（0 = 从不逃）。</summary>
    public int FleeHpPercent;

    /// <summary>巡逻路线 id（0 = 无路线，走 <see cref="WanderRadius"/> 踱步）。</summary>
    public int PatrolRouteId;

    // 尸体停留秒不在这里：它是**模板级**属性（一只怪的尸体不随行为档位变），
    // 唯一的权威字段是 MonsterRuntimeConfig.CorpseSeconds，由库里的 corpse_seconds 直接喂。
    // 以前这里也有一份，且装配时被硬写成 60（既不来自库也没人读），
    // 于是同一个概念在运行时配置里有两个可以互相矛盾的住处。

    /// <summary>默认值：与既有 <c>MonsterAiConfig.Default</c> 保持同一组保守数字，避免接入即改变现有手感。</summary>
    public static MonsterBehaviorConfig Default => new MonsterBehaviorConfig
    {
        Flags = MonsterAiFlags.ForProfile(MonsterAiProfile.Dumb),
        IsAggressive = true,
        AggroRadius = 10f,
        AggroConeDegrees = 360f,
        LeashRadius = 30f,
        EvadeImmunitySeconds = EvadeImmunityDefaultSeconds,
        AssistRadius = 10f,
        DetectMask = 0x1,
        WanderRadius = 6f,
        IdleToPatrolSeconds = 3f,
        WalkSpeed = 2.5f,
        MoveSpeed = 4f,
        FollowType = MonsterFollowType.GroundWalk,
        MeleeSwingSeconds = 1.5f,
        RangedMinDistance = 0f,
        RangedMaxDistance = 0f,
        FleeHpPercent = 0,
        PatrolRouteId = 0,
    };
}

/// <summary>
/// 等级 → 战斗属性（决策 D2：<b>公式为主、覆盖可选</b>）。
/// </summary>
/// <remarks>
/// <b>为什么不是"每级一行数字"</b>：WoW 的怪在同一模板下有 1..N 级铺开的实例（同怪在不同区域等级不同），
/// 逐行数字会让"怪物等级范围"变成"怪物等级清单"，编辑器无法维护。
/// 公式给的是形状（线性 <c>base + perLevel × Lv</c> × rank 倍率），
/// BOSS 这类要手调的走 <see cref="MonsterStatOverride"/>。
/// </remarks>
public struct MonsterStatsConfig
{
    public int HpBase;
    public int HpPerLevel;
    public int AtkBase;
    public int AtkPerLevel;
    public int DefBase;
    public int DefPerLevel;

    /// <summary>血量覆盖基点（0 = 不覆盖）。BOSS 手调而不必改公式，见 MonsterStatsCore.Compute。</summary>
    public int HpOverrideBps;

    /// <summary>攻击覆盖基点（0 = 不覆盖）。</summary>
    public int AtkOverrideBps;

    /// <summary>rank 血量倍率（精英常见 2×，首领更高）；索引 = <see cref="MonsterRank"/>。</summary>
    public double[] HpScaleByRank;

    /// <summary>rank 伤害倍率。</summary>
    public double[] DamageScaleByRank;

    /// <summary>保守默认：与 <c>MonsterAiConfig.Default</c> 的 Lv 中等价（Hp=100/Atk=10 在 Lv 十几时成立）。</summary>
    public static MonsterStatsConfig Default => new MonsterStatsConfig
    {
        HpBase = 20,
        HpPerLevel = 6,
        AtkBase = 2,
        AtkPerLevel = 1,
        DefBase = 0,
        DefPerLevel = 0,
        HpScaleByRank = new double[] { 1.0, 2.0, 2.0, 4.0, 6.0 },
        DamageScaleByRank = new double[] { 1.0, 1.5, 1.5, 2.5, 3.0 },
    };
}

/// <summary>计算出的战斗属性。</summary>
public readonly struct MonsterStats
{
    public readonly int MaxHp;
    public readonly int AttackPower;
    public readonly int Defense;

    public MonsterStats(int maxHp, int attackPower, int defense)
    {
        MaxHp = maxHp;
        AttackPower = attackPower;
        Defense = defense;
    }
}

/// <summary>
/// 属性公式内核（纯整数，避免浮点跨平台差异；rank 倍率用基点整数表达）。
/// </summary>
public static class MonsterStatsCore
{
    /// <summary>版本（进金轨迹锚点）。</summary>
    public const int Version = 1;

    /// <summary>
    /// 按等级与 rank 算属性。
    /// </summary>
    /// <param name="cfg">公式系数。</param>
    /// <param name="level">怪物实际等级（刷怪点可覆盖模板基准等级）。</param>
    /// <param name="rank">怪物 rank。</param>
    /// <param name="hpOverrideBps">血量覆盖（基点，0 = 不覆盖）。让 BOSS 能手调而不必改公式。</param>
    /// <param name="atkOverrideBps">攻击覆盖（基点，0 = 不覆盖）。</param>
    public static MonsterStats Compute(
        in MonsterStatsConfig cfg,
        int level,
        MonsterRank rank,
        int hpOverrideBps = 0,
        int atkOverrideBps = 0)
    {
        if (level < 1) level = 1;
        var hpScale = ScaleFor(cfg.HpScaleByRank, rank);
        var dmgScale = ScaleFor(cfg.DamageScaleByRank, rank);

        // 定点：倍率表是 double，但落到属性上要变成整数才跨端一致。这里在"算完整数再加倍率"后四舍五入一次。
        var hpRaw = (long)(cfg.HpBase + (long)cfg.HpPerLevel * level) * hpScale;
        var atkRaw = (long)(cfg.AtkBase + (long)cfg.AtkPerLevel * level) * dmgScale;
        var def = cfg.DefBase + cfg.DefPerLevel * level;

        var hp = (int)Math.Round(hpRaw, MidpointRounding.AwayFromZero);
        var atk = (int)Math.Round(atkRaw, MidpointRounding.AwayFromZero);

        if (hpOverrideBps > 0) hp = (int)Math.Round((double)hp * hpOverrideBps / 10000.0, MidpointRounding.AwayFromZero);
        if (atkOverrideBps > 0) atk = (int)Math.Round((double)atk * atkOverrideBps / 10000.0, MidpointRounding.AwayFromZero);

        if (hp < 1) hp = 1;
        if (atk < 0) atk = 0;
        if (def < 0) def = 0;
        return new MonsterStats(hp, atk, def);
    }

    private static double ScaleFor(double[]? table, MonsterRank rank)
    {
        var i = (int)rank;
        if (table == null || i < 0 || i >= table.Length) return 1.0;
        return table[i];
    }

    /// <summary>
    /// 刷新秒数随等级增长（逆向规格 §8："低级的怪几秒，精英几十秒，世界 BOSS 数小时"）。
    /// </summary>
    /// <param name="baseSeconds">截距。</param>
    /// <param name="perLevelSeconds">每级增量。</param>
    /// <param name="level">等级。</param>
    /// <param name="rank">rank（用倍率而不是加法：BOSS 要的是"按小时"这个量级）。</param>
    /// <param name="overrideSeconds">刷怪点显式覆盖（&gt;0 时优先）。</param>
    public static float RespawnSeconds(
        float baseSeconds,
        float perLevelSeconds,
        int level,
        MonsterRank rank,
        float overrideSeconds)
    {
        if (overrideSeconds > 0f) return overrideSeconds;
        if (level < 1) level = 1;
        var raw = baseSeconds + perLevelSeconds * level;
        var scale = rank switch
        {
            MonsterRank.Normal => 1f,
            MonsterRank.Elite => 2f,
            MonsterRank.RareElite => 3f,
            MonsterRank.Boss => 6f,
            _ => 12f,
        };
        raw *= scale;
        if (raw < 0f) raw = 0f;
        if (raw > 24f * 3600f) raw = 24f * 3600f; // 一天封顶：配错成 1e9 不该让这只怪永不再来
        return raw;
    }
}

/// <summary>
/// 巡逻路线（waypoint 折线）。逆向规格 §5.1：路线给"要去哪"，导航图给"怎么走"（决策 D6：两件事分开）。
/// </summary>
public struct MonsterPatrolRoute
{
    /// <summary>路线 id。</summary>
    public int RouteId;

    /// <summary>行进方式。</summary>
    public MonsterPatrolMode Mode;

    /// <summary>每点默认停留秒（点级可覆盖）。</summary>
    public float PauseAtPointSeconds;

    /// <summary>是否用导航图连段（false = 直线连，配 <see cref="MonsterFollowType.Straight"/> 的怪用）。</summary>
    public bool UseNavMesh;

    /// <summary>点列（宿主从表装配，顺序即 <c>seq</c>）。</summary>
    public MonsterPatrolPoint[] Points;

    /// <summary>点数（Points 为 null 时 0）。</summary>
    public readonly int PointCount => Points?.Length ?? 0;

    /// <summary>路线是否可用（少于 2 点谈不上巡逻）。</summary>
    public readonly bool IsValid => PointCount >= 2;
}

/// <summary>巡逻点（服务端 Z-up 世界坐标，水平面 = XY）。</summary>
public readonly struct MonsterPatrolPoint
{
    public readonly float X;
    public readonly float Y;
    public readonly float Z;

    /// <summary>该点停留秒；&lt;0 = 用路线默认值。</summary>
    public readonly float PauseSeconds;

    public MonsterPatrolPoint(float x, float y, float z, float pauseSeconds = -1f)
    {
        X = x;
        Y = y;
        Z = z;
        PauseSeconds = pauseSeconds;
    }
}

/// <summary>巡逻运行态（宿主持有）。</summary>
public struct MonsterPatrolSelf
{
    /// <summary>当前目标点下标。</summary>
    public int PointIndex;

    /// <summary>行进方向（PingPong 用：+1 前进 / -1 后退）。</summary>
    public int Direction;

    /// <summary>在当前点已停留的秒数（&gt;0 表示已到点正在停）。</summary>
    public float PausedSeconds;

    /// <summary>是否已停在点上等待。</summary>
    public bool Arrived;
}

/// <summary>巡逻推进的产出。</summary>
public struct MonsterPatrolStep
{
    /// <summary>本段的目的地坐标（宿主把它交给寻路或直接直线走）。</summary>
    public float TargetX;
    public float TargetY;
    public float TargetZ;

    /// <summary>本 tick 是否正在停留（停留 = 不移动）。</summary>
    public bool Waiting;

    /// <summary>本 tick 是否换了目标点（宿主据此重置路径游标——不重置会让怪拿旧路径走新目的地）。</summary>
    public bool SwitchedPoint;
}

/// <summary>
/// 巡逻路线推进内核（把"沿折线走 + 每点停留"变成确定性纯函数）。
/// </summary>
public static class MonsterPatrolCore
{
    /// <summary>版本。</summary>
    public const int Version = 1;

    /// <summary>判定"到点"的距离（米）。取 0.5：小于一格，不至于在点附近来回抖。</summary>
    public const float ArriveDistance = 0.5f;

    /// <summary>
    /// 推进一 tick：返回本段该往哪儿走，并就地维护 <paramref name="self"/>。
    /// </summary>
    /// <param name="self">巡逻运行态（引用，就地更新）。</param>
    /// <param name="route">路线。</param>
    /// <param name="selfX">怪当前 X。</param>
    /// <param name="selfY">怪当前 Y。</param>
    /// <param name="dt">步长秒。</param>
    /// <param name="randomNextPick">RandomNext 模式下的下一点选择（由宿主用确定性随机算好传入；-1 = 顺序下一个）。</param>
    public static MonsterPatrolStep Advance(
        ref MonsterPatrolSelf self,
        in MonsterPatrolRoute route,
        float selfX,
        float selfY,
        float dt,
        int randomNextPick = -1)
    {
        var step = new MonsterPatrolStep();
        var n = route.PointCount;
        if (n < 2 || route.Points == null)
        {
            step.Waiting = true;
            return step;
        }

        if (self.PointIndex < 0 || self.PointIndex >= n) self.PointIndex = 0;
        if (self.Direction == 0) self.Direction = 1;

        var point = route.Points[self.PointIndex];
        step.TargetX = point.X;
        step.TargetY = point.Y;
        step.TargetZ = point.Z;

        var dx = point.X - selfX;
        var dy = point.Y - selfY;
        var dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist <= ArriveDistance)
        {
            // 到点：进入停留。停留计时的推进在"已 Arrived"分支里做，避免同 tick 既到点又走人。
            if (!self.Arrived)
            {
                self.Arrived = true;
                self.PausedSeconds = 0f;
                step.SwitchedPoint = false;
            }
            var pause = point.PauseSeconds >= 0f ? point.PauseSeconds : route.PauseAtPointSeconds;
            if (pause < 0f) pause = 0f;
            self.PausedSeconds += dt > 0f ? dt : 0f;
            step.Waiting = self.PausedSeconds < pause;

            if (step.Waiting)
            {
                // 停留期间目的地就是当前点（宿主会算出"零位移"）。
                return step;
            }

            // 停留结束：换下一点。
            self.Arrived = false;
            self.PausedSeconds = 0f;
            var next = NextIndex(ref self, route.Mode, n, randomNextPick);
            self.PointIndex = next;
            step.SwitchedPoint = true;
            var np = route.Points[next];
            step.TargetX = np.X;
            step.TargetY = np.Y;
            step.TargetZ = np.Z;
            return step;
        }

        self.Arrived = false;
        step.Waiting = false;
        return step;
    }

    /// <summary>路线首点（出生/回位时把巡逻游标复位到它）。</summary>
    public static void ResetToStart(ref MonsterPatrolSelf self, MonsterPatrolMode mode)
    {
        self.PointIndex = 0;
        self.Direction = 1;
        self.PausedSeconds = 0f;
        self.Arrived = false;
    }

    /// <summary>
    /// 该点是否值得走图（路线声明用图且怪的能力位开了导航）。
    /// <para>写成函数而不是散在各处的 <c>&amp;&amp;</c>：这条判据将来还要加"距离超过 N 米才走图"，
    /// 散着写会漏改一处。</para>
    /// <para>调用方：<c>MonsterAiRuntime.MoveToward</c> 在**正在走这条巡逻线**的状态里问这里
    /// （v7 起）。在这之前它没有调用方、内核另写了一份判据，于是 <c>use_nav_mesh</c> 那一列
    /// 存得进、读得出、却没人执行——正是"两处清单漏改一处"的形状。</para>
    /// 所以移动方式这一维必须走 <see cref="NavEligibleFollow"/>，两边共用同一张表——
    /// 否则"改了这张表没改那张"会变成又一个存得进、读得出、却没人执行的判据。</para>
    /// </summary>
    public static bool ShouldUseNavigation(in MonsterPatrolRoute route, in MonsterBehaviorConfig behavior)
        => route.UseNavMesh && behavior.Flags.UseNavigation && NavEligibleFollow(behavior.FollowType);

    /// <summary>
    /// 哪些移动方式有资格走图：<c>GroundWalk</c>（会绕开难走格）与 <c>Flying</c>（不绕难走格，但墙仍然不能穿）。
    /// <para><c>Straight</c> 永远直线、<c>Static</c> 不产生位移，两者都不查图。</para>
    /// </summary>
    public static bool NavEligibleFollow(MonsterFollowType follow)
        => follow == MonsterFollowType.GroundWalk || follow == MonsterFollowType.Fly;

    /// <summary>走图时是否把"难走但可走"的格子折回基准代价（只有飞行档是）。</summary>
    public static bool IgnoresHardCellCost(MonsterFollowType follow) => follow == MonsterFollowType.Fly;

    /// <summary>
    /// 这一档移动方式<b>会不会产生位移</b>——站桩档（炮台/法阵/绑在柱子上的祭司）永不移动。
    /// <para><b>为什么必须有主人</b>：这条判据以前只长在 v2，两处各写一遍 inline 比较
    /// （<c>MonsterAiRuntime.MoveToward</c> 的出口与"无路线踱步"那支），而 v1 的
    /// <c>MonsterAiConfig</c> 连 <c>FollowType</c> 这个输入都没有 ⇒ 三条写位移的分支一条都不问。
    /// 表现很具体：编辑器里把"移动方式"选成"站桩"，默认档（<c>ai_profile=0</c>，生产里全部种子怪）
    /// 的怪照样追人、照样跑回家，那一格是装饰。</para>
    /// <para>还有一条更糟的连带：切片 18 之后"回位中打不到"是生效的，而站桩的 v1 怪
    /// <b>走不回出生点</b> ⇒ 它会永远停在 Leash 里，变成一只<b>永远打不到</b>的怪。
    /// 所以站桩档同时不参与"离家过远"的脱战判定（<see cref="Horizon.Game.Sim.Monsters.MonsterAiGate.LeashApplies"/>
    /// 的第二个输入就是这个）：AC 的 leash 是一段<em>移动</em>状态（<c>MoveTargetedHome</c>），
    /// 不能动的怪没有"回家"这件事。</para>
    /// </summary>
    public static bool FollowProducesMovement(MonsterFollowType follow) => follow != MonsterFollowType.Static;

    /// <summary>
    /// 这只怪在**没有巡逻线的游荡**里该用哪个速度（米/秒）。
    /// <para>口径：<c>walkSpeed &gt; 0</c> 就用它（内容表"巡逻速度"那一格），否则退回 <c>moveSpeed</c>——
    /// 后者就是接进这一格之前 v1 的行为，所以**没带这一格的旧内容/旧夹具逐字不变**
    /// （v2 那份在装配期已被 <c>MonsterBehaviorSnapshot.ToConfig</c> 兜成 2.5，不会走到这个退路）。</para>
    /// <para>为什么要有这一条：v1 的游荡以前直接拿 <c>MoveSpeed</c>（那是"追你"的速度），
    /// 而 v2 拿 <c>WalkSpeed</c> ⇒ 种子内容 <c>walk=2.5 / move=4</c> 时，默认档的怪**散步比追你快**，
    /// 而编辑器里那一格对它根本不生效。位移速度的选择只许有这一处。</para>
    /// </summary>
    public static float WanderSpeed(float walkSpeed, float moveSpeed) => walkSpeed > 0f ? walkSpeed : moveSpeed;

    private static int NextIndex(ref MonsterPatrolSelf self, MonsterPatrolMode mode, int n, int randomNextPick)
    {
        switch (mode)
        {
            case MonsterPatrolMode.PingPong:
            {
                var dir = self.Direction >= 0 ? 1 : -1;
                var next = self.PointIndex + dir;
                // 到首点翻向"前进"，到末点翻向"后退"；端点不重复停留（否则同一点会停两次）。
                if (next <= 0)
                {
                    self.Direction = 1;
                    return 0;
                }
                if (next >= n - 1)
                {
                    self.Direction = -1;
                    return n - 1;
                }
                self.Direction = dir;
                return next;
            }
            case MonsterPatrolMode.RandomNext:
            {
                if (randomNextPick >= 0 && randomNextPick < n) return randomNextPick;
                var seq = self.PointIndex + 1;
                return seq >= n ? 0 : seq;
            }
            case MonsterPatrolMode.Loop:
            default:
            {
                var seq = self.PointIndex + 1;
                return seq >= n ? 0 : seq;
            }
        }
    }
}
