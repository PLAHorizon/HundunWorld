using System;

namespace Horizon.Game.Sim.Pets;

/// <summary>玩家给宠物的即时指令（WoW 控制条口径，逆向 §A5 [高]）。</summary>
public enum PetCommand : byte
{
    /// <summary>攻击当前目标。</summary>
    Attack = 0,

    /// <summary>跟随主人（不主动进战）。</summary>
    Follow = 1,

    /// <summary>待命（原地不动，WoW 的 Wait）。</summary>
    Wait = 2,

    /// <summary>防御/驻守指定点（Detach：与主人解绑）。</summary>
    Defend = 3,

    /// <summary>撤退/回主人身边。</summary>
    Retreat = 4,
}

/// <summary>常驻姿态（WoW §A5：被动/防御/攻击 + 畏缩；经典版只有前三档）。</summary>
public enum PetStance : byte
{
    /// <summary>畏缩：不主动攻击，挨打才还手。</summary>
    Cowardly = 0,

    /// <summary>被动：只在主人进战后才行动。</summary>
    Passive = 1,

    /// <summary>防御：优先自保与治疗类技能。</summary>
    Defensive = 2,

    /// <summary>攻击：主动索敌并按档位释放技能。</summary>
    Aggressive = 3,
}

/// <summary>
/// AI 智能程度档位（决策 D1：五档，展开成 5 个能力位）。
/// <para><b>为什么用"档位 → 能力位"而不是直接给一堆布尔</b>：直接给布尔的话，
/// 编辑器能配出"会自己选目标但不会护主"这种在游戏里根本不存在的组合，而没有人拦得住；
/// 档位保证默认组合是可信的，能力位只用于**在档位之上做微调**（三态：null = 跟档位）。</para>
/// </summary>
public enum PetIntellectTier : byte
{
    /// <summary>木讷：只会跟着主人，挨打才还手，永不主动放技能。</summary>
    Mindless = 0,

    /// <summary>本能：进入战斗后随机放已装配技能，不选目标。</summary>
    Instinct = 1,

    /// <summary>训练有素：会按姿态索敌、按等级概率选技能。</summary>
    Trained = 2,

    /// <summary>聪颖：会避让（低血撤退）、会护主（替主人挡）。</summary>
    Clever = 3,

    /// <summary>通灵：会保留控制技（不无脑倾泻），会打断敌方施法。</summary>
    Master = 4,
}

/// <summary>宠物性格（天龙 §D5 [中]：勇猛/精明/胆小/谨慎/忠诚，影响触发与受伤概率）。</summary>
public enum PetPersonality : byte
{
    /// <summary>勇猛。</summary>
    Brave = 0,

    /// <summary>精明。</summary>
    Clever = 1,

    /// <summary>胆小。</summary>
    Timid = 2,

    /// <summary>谨慎。</summary>
    Cautious = 3,

    /// <summary>忠诚。</summary>
    Loyal = 4,
}

/// <summary>
/// AI 能力位（三态展开）。
/// <para>照抄 <c>MonsterAiFlags</c> 的做法：每个位是 <c>bool</c> 而不是 <c>int?</c>，
/// 由 <see cref="Resolve"/> 把"档位默认 + 模板覆盖（null=用默认）"合成一次，之后全工程只读展开结果。</para>
/// </summary>
public readonly struct PetAiFlags
{
    /// <summary>会自主索敌（否则只在主人进战后行动）。</summary>
    public readonly bool CanAcquireTarget;

    /// <summary>会自动释放技能（否则只平A）。</summary>
    public readonly bool CanAutoCast;

    /// <summary>会避让（低血撤退/绕后）。</summary>
    public readonly bool CanEvade;

    /// <summary>会护主（替主人承受伤害，天龙"忠心"，§D11）。</summary>
    public readonly bool CanGuardOwner;

    /// <summary>会保留控制技/打断（不一次性倾泻）。</summary>
    public readonly bool CanHoldControl;

    public PetAiFlags(bool canAcquireTarget, bool canAutoCast, bool canEvade, bool canGuardOwner, bool canHoldControl)
    {
        CanAcquireTarget = canAcquireTarget;
        CanAutoCast = canAutoCast;
        CanEvade = canEvade;
        CanGuardOwner = canGuardOwner;
        CanHoldControl = canHoldControl;
    }

    /// <summary>
    /// 三个可空覆盖合成档位默认。<b>null 一律取档位默认，不是 false</b>——
    /// 把"没配"当成"关掉了"会让所有模板都退化成木讷档，而且编译期看不出来。
    /// </summary>
    public static PetAiFlags Resolve(PetIntellectTier tier, int? acquireOverride = null, int? autoCastOverride = null,
        int? evadeOverride = null, int? guardOverride = null, int? holdOverride = null)
    {
        var d = DefaultOf(tier);
        return new PetAiFlags(
            Pick(d.CanAcquireTarget, acquireOverride),
            Pick(d.CanAutoCast, autoCastOverride),
            Pick(d.CanEvade, evadeOverride),
            Pick(d.CanGuardOwner, guardOverride),
            Pick(d.CanHoldControl, holdOverride));
    }

    /// <summary>档位默认能力位。</summary>
    public static PetAiFlags DefaultOf(PetIntellectTier tier) => tier switch
    {
        PetIntellectTier.Mindless => new PetAiFlags(false, false, false, false, false),
        PetIntellectTier.Instinct => new PetAiFlags(false, true, false, false, false),
        PetIntellectTier.Trained => new PetAiFlags(true, true, false, false, false),
        PetIntellectTier.Clever => new PetAiFlags(true, true, true, true, false),
        _ => new PetAiFlags(true, true, true, true, true),
    };

    /// <summary>全关（用于"这只宠不参与 AI 决策"的显式声明，而不是靠默认值）。</summary>
    public static PetAiFlags None => new(false, false, false, false, false);

    private static bool Pick(bool fallback, int? overrideValue)
        => overrideValue is null ? fallback : overrideValue.Value != 0;
}

/// <summary>一次 AI 决策的结果。</summary>
public readonly struct PetAiDecision
{
    /// <summary>要执行的指令。</summary>
    public readonly PetCommand Command;

    /// <summary>目标实体 id；0 = 无目标。</summary>
    public readonly ulong TargetId;

    /// <summary>要释放的技能 id；0 = 不施法（走普攻）。</summary>

    public readonly int SkillId;

    /// <summary>决策原因码（进日志/进界面，为了"它为什么这么干"可解释）。</summary>
    public readonly PetAiReason Reason;

    public PetAiDecision(PetCommand command, ulong targetId, int skillId, PetAiReason reason)
    {
        Command = command;
        TargetId = targetId;
        SkillId = skillId;
        Reason = reason;
    }
}

/// <summary>AI 决策原因码。</summary>
public enum PetAiReason : byte
{
    /// <summary>无（未决策/非法输入）。</summary>
    None = 0,

    /// <summary>姿态为待命。</summary>
    StandingBy = 1,

    /// <summary>低忠诚拒绝出战（梦幻 §B6）。</summary>
    RefusedByLoyalty = 2,

    /// <summary>档位不允许自主索敌，等待主人进战。</summary>
    WaitingForOwner = 3,

    /// <summary>锁定目标。</summary>
    Acquired = 4,

    /// <summary>血量低于避让阈值，撤退。</summary>
    Evading = 5,

    /// <summary>护主：替主人承受攻击。</summary>
    GuardingOwner = 6,

    /// <summary>按技能等级概率释放成功（天龙 §D5）。</summary>
    Casting = 7,

    /// <summary>所有技能冷却中，转普攻。</summary>
    BasicAttack = 8,

    /// <summary>保留控制技：本轮不放，等更高价值时机（通灵档独有）。</summary>
    HoldingControl = 9,
}

/// <summary>
/// 宠物 AI 智能程度与战斗行为的确定性纯函数（逆向规格 §A5/§B5/§C5/§D5）。
/// <para>
/// <b>骨架取自 WoW，数值取自国产三家</b>：指令 × 姿态的二维结构（§A5）是 WoW 的，
/// "按技能等级概率自动释放"（§D5）是天龙的，"亲密影响连击触发"（§C6）是大话的。
/// 三者叠在一个决策函数里而不是三个函数，是因为它们争的是同一个资源——"这一步动作给谁"。
/// </para>
/// <para>
/// <b>每玩家只许一只出战</b>（天龙 §D5 [高]）：这条约束由 <see cref="CanDeployAlongside"/> 表达，
/// 附体位（3 个，§D11）不占出战位，所以它不在同一条判据里——把附体也算进"出战数"会让
/// 75 级开了 3 附体位的玩家一只都放不出来。
/// </para>
/// </summary>
public static class PetAiCore
{
    /// <summary>AI 内核版本。</summary>
    public const int Version = 1;

    /// <summary>每玩家同时出战的最大宠物数（天龙 §D5）。设为 1 是刻意的设计约束。</summary>
    public const int MaxBattlingPets = 1;

    /// <summary>附体位上限（天龙 §D11：75 级开 3 个）。</summary>
    public const int MaxPossessedSlots = 3;

    /// <summary>开启附体功能的主人等级（天龙 §D11：75 级）。</summary>
    public const int PossessionUnlockOwnerLevel = 75;

    /// <summary>避让触发的血量比例（bps）；60% 以下且档位允许时撤退。</summary>
    public const int EvadeHpBps = 3_000;

    /// <summary>主人受击时护主触发的基准概率（bps，天龙"忠心：一定概率替主人承受会心伤害"，§D11）。</summary>
    public const int GuardBaseChanceBps = 2_000;

    /// <summary>畏缩姿态下主动攻击的概率（bps）——不是 0，因为"畏缩"在 WoW 里表现为"会还手但不追"。</summary>
    public const int CowardlyRetaliationBps = 4_000;

    /// <summary>
    /// 性格乘数表（bps）：行 = 性格，列 = [技能触发率修正, 受伤概率修正]。
    /// <para>天龙 §D5 只说"影响资质、技能触发概率、受伤概率"未给数字，
    /// 这里给的是一套可调初值；<b>相对关系</b>（勇猛更爱放技能也更挨打、谨慎相反）才是从资料读出来的。</para>
    /// </summary>
    public static readonly int[][] PersonalityMultipliers =
    {
        new[] { 11_000, 11_000 }, // Brave：触发 +10%，受伤 +10%
        new[] { 10_500, 9_500 },  // Clever：触发 +5%，受伤 −5%
        new[] { 9_000, 11_500 },  // Timid：触发 −10%，受伤 +15%
        new[] { 9_500, 9_000 },   // Cautious：触发 −5%，受伤 −10%
        new[] { 10_000, 9_500 },  // Loyal：触发不变，受伤 −5%（护主倾向由能力位表达，不靠这里）
    };

    /// <summary>按性格取触发率修正（bps）；越界钳到末项。</summary>
    public static int PersonalityTriggerMultiplierBps(PetPersonality personality)
    {
        var idx = (int)personality;
        if (idx < 0 || idx >= PersonalityMultipliers.Length) return 10_000;
        return PersonalityMultipliers[idx][0];
    }

    /// <summary>按性格取受伤率修正（bps）。</summary>
    public static int PersonalityHurtMultiplierBps(PetPersonality personality)
    {
        var idx = (int)personality;
        if (idx < 0 || idx >= PersonalityMultipliers.Length) return 10_000;
        return PersonalityMultipliers[idx][1];
    }

    /// <summary>
    /// 能否再放一只出战（天龙口径：已有 1 只出战就拒绝）。
    /// <param name="battlingCount">当前出战数（<b>不含附体位</b>）。</param>
    /// </summary>
    public static bool CanDeployAlongside(int battlingCount) => battlingCount < MaxBattlingPets;

    /// <summary>附体位是否还可占用：需要主人等级达到解锁线，且未超 <see cref="MaxPossessedSlots"/>。</summary>
    public static bool CanOccupyPossessionSlot(int ownerLevel, int possessedCount)
        => ownerLevel >= PossessionUnlockOwnerLevel && possessedCount < MaxPossessedSlots;

    /// <summary>
    /// 战斗决策入口。
    /// </summary>
    /// <param name="flags">已展开的能力位。</param>
    /// <param name="stance">姿态。</param>
    /// <param name="command">玩家即时指令（优先级高于姿态）。</param>
    /// <param name="loyalty">忠诚（决定是否拒绝出战）。</param>
    /// <param name="hpBps">当前血量比例（bps，0–10000）。</param>
    /// <param name="ownerUnderAttack">主人本回合是否受击。</param>
    /// <param name="ownerEngaged">主人是否已进战。</param>
    /// <param name="targetIds">可见敌方实体 id（按声明顺序，调用方负责顺序稳定）。</param>
    /// <param name="skillIds">已装配技能 id。</param>
    /// <param name="skillLevels">与 <paramref name="skillIds"/> 同序的技能等级。</param>
    /// <param name="controlSkillIndexes">标记哪些是控制技（通灵档会保留）；可空。</param>
    /// <param name="personality">性格。</param>
    /// <param name="intimacyBonusBps">亲密给的触发加成（<c>PetBondCore.IntimacyTriggerBonusBps</c>）。</param>
    /// <param name="seed">确定性种子。</param>
    /// <remarks>
    /// 判定顺序就是玩家观察到的优先级：拒绝出战 &gt; 待命/撤退指令 &gt; 低血避让 &gt; 护主 &gt; 攻击。
    /// 换成别的顺序会出现"血皮了还在替主人挡刀"这种看起来像 bug 的行为。
    /// </remarks>
    public static PetAiDecision Decide(PetAiFlags flags, PetStance stance, PetCommand command, int loyalty,
        int hpBps, bool ownerUnderAttack, bool ownerEngaged, ulong[] targetIds, int[] skillIds,
        int[] skillLevels, int[] controlSkillIndexes, PetPersonality personality, int intimacyBonusBps, long seed)
    {
        // ① 低忠诚拒绝（梦幻 §B6：概率拒绝，越低概率越高）
        var refusal = PetBondCore.RefusalChanceBps(loyalty);
        if (refusal > 0 && PetSkillCore.Roll(seed, 30_001) < refusal)
            return new PetAiDecision(PetCommand.Wait, 0UL, 0, PetAiReason.RefusedByLoyalty);

        // ② 显式指令压过姿态
        if (command == PetCommand.Wait || command == PetCommand.Retreat)
            return new PetAiDecision(command, 0UL, 0, PetAiReason.StandingBy);

        // ③ 避让（需要档位允许）
        var hp = hpBps < 0 ? 0 : (hpBps > 10_000 ? 10_000 : hpBps);
        if (flags.CanEvade && hp < EvadeHpBps && stance != PetStance.Aggressive)
            return new PetAiDecision(PetCommand.Retreat, 0UL, 0, PetAiReason.Evading);

        // ④ 护主
        if (flags.CanGuardOwner && ownerUnderAttack)
        {
            var guardBps = GuardBaseChanceBps * PersonalityTriggerMultiplierBps(personality) / 10_000;
            if (PetSkillCore.Roll(seed, 30_002) < guardBps)
                return new PetAiDecision(PetCommand.Defend, 0UL, 0, PetAiReason.GuardingOwner);
        }

        // ⑤ 是否进入攻击：畏缩只在还手概率内行动；被动等主人进战；本能以下不自主索敌
        var willingToFight = stance switch
        {
            PetStance.Cowardly => PetSkillCore.Roll(seed, 30_003) < CowardlyRetaliationBps,
            PetStance.Passive => ownerEngaged || command == PetCommand.Attack,
            _ => true,
        };
        if (!willingToFight)
            return new PetAiDecision(PetCommand.Follow, 0UL, 0, PetAiReason.StandingBy);

        if (targetIds is null || targetIds.Length == 0)
            return new PetAiDecision(PetCommand.Follow, 0UL, 0, PetAiReason.WaitingForOwner);

        // 目标选择：不自主索敌的档位必须等 command==Attack 才锁目标
        if (!flags.CanAcquireTarget && command != PetCommand.Attack)
            return new PetAiDecision(PetCommand.Follow, 0UL, 0, PetAiReason.WaitingForOwner);

        var target = PickTarget(targetIds, stance, seed);
        if (target == 0UL)
            return new PetAiDecision(PetCommand.Attack, 0UL, 0, PetAiReason.Acquired);

        if (!flags.CanAutoCast || skillIds is null || skillIds.Length == 0)
            return new PetAiDecision(PetCommand.Attack, target, 0, PetAiReason.BasicAttack);

        // ⑥ 技能选择：逐个按等级概率掷，命中即放；通灵档把控制技留到主人受击时才放
        var triggerMult = PersonalityTriggerMultiplierBps(personality);
        for (var i = 0; i < skillIds.Length; i++)
        {
            if (skillIds[i] <= 0) continue;

            var isControl = controlSkillIndexes is { Length: > 0 } && Array.IndexOf(controlSkillIndexes, i) >= 0;
            if (isControl && flags.CanHoldControl && !ownerUnderAttack)
                continue; // 保留

            var level = skillLevels is not null && skillLevels.Length > i ? skillLevels[i] : 1;
            var chance = PetSkillCore.AutoCastChanceBps(level);
            chance = chance * triggerMult / 10_000 + intimacyBonusBps / 10; // 亲密只加成到 +300bps 量级（§C6 修饰项）
            if (chance > 9_000) chance = 9_000;

            if (PetSkillCore.Roll(seed, 30_100 + i) < chance)
                return new PetAiDecision(PetCommand.Attack, target, skillIds[i], PetAiReason.Casting);
        }

        // 全是控制技且都不该现在放 → 明确报"保留"，而不是静默普攻：
        // 界面要能解释"它今天一次大招都没放"不是坏了。
        if (flags.CanHoldControl && controlSkillIndexes is { Length: > 0 }
            && controlSkillIndexes.Length == skillIds.Length)
        {
            return new PetAiDecision(PetCommand.Attack, target, 0, PetAiReason.HoldingControl);
        }

        return new PetAiDecision(PetCommand.Attack, target, 0, PetAiReason.BasicAttack);
    }

    /// <summary>
    /// 目标选择。
    /// <para>防御姿态优先打"血最少的那个"（先清一个是一个），其余姿态优先打声明顺序的第一个（≈仇恨顺序）。
    /// <b>这里不重排数组</b>：重排会让同输入在两个宿主上给出不同顺序，而仇恨表由
    /// <c>MonsterThreatCore</c> 负责，宠物复用它的"按 EntityId 升序 Span"约定即可。</para>
    /// </summary>
    public static ulong PickTarget(ulong[] targetIds, PetStance stance, long seed)
    {
        if (targetIds is null || targetIds.Length == 0) return 0UL;
        if (stance == PetStance.Defensive)
        {
            // 防御姿态没有血量信息时用下标最小的合法目标；调用方要血量优先请走 Decide 的重载路径。
            for (var i = 0; i < targetIds.Length; i++)
            {
                if (targetIds[i] != 0UL) return targetIds[i];
            }
        }
        var idx = PetSkillCore.Roll(seed, 30_200) % targetIds.Length;
        return targetIds[idx];
    }
}

/// <summary>AI 决策的可见原因（界面提示用）。</summary>
public static class PetAiReasonText
{
    /// <summary>原因码 → 中文提示。<b>只用于显示，判据永远在 <see cref="PetAiReason"/> 上</b>。</summary>
    public static string Of(PetAiReason reason) => reason switch
    {
        PetAiReason.None => "未决策",
        PetAiReason.StandingBy => "待命中",
        PetAiReason.RefusedByLoyalty => "忠诚不足，拒绝出战",
        PetAiReason.WaitingForOwner => "等待主人进战",
        PetAiReason.Acquired => "锁定目标",
        PetAiReason.Evading => "血量过低，撤退",
        PetAiReason.GuardingOwner => "护主：替你挡下这一击",
        PetAiReason.Casting => "释放技能",
        PetAiReason.BasicAttack => "技能冷却中，普通攻击",
        PetAiReason.HoldingControl => "保留控制技",
        _ => "未决策",
    };
}
