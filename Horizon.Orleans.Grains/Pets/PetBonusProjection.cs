using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Pets;

namespace Horizon.Orleans.Grains.Pets;

/// <summary>宠物栏要展示的一行归因量（面板通路专用，不进战斗结算）。</summary>
public struct PetBonusDisplay
{
    /// <summary>宠物贡献的攻击。</summary>
    public int Attack;

    /// <summary>宠物贡献的生命上限。</summary>
    public int Health;

    /// <summary>宠物贡献的防御。</summary>
    public int Defense;

    /// <summary>宠物贡献的命中。</summary>
    public int Hit;

    /// <summary>替主人承受会心的概率（bps）。</summary>
    public int CritTankChanceBps;

    /// <summary>参与反哺的只数。</summary>
    public int Contributors;

    /// <summary>名册总只数。</summary>
    public int RosterCount;

    /// <summary>问题描述；空 = 一切正常。</summary>
    public string Issues;
}

/// <summary>
/// 名册快照 → 宠物栏归因量的唯一投影。
/// <para>
/// 为什么单独一个静态类而不是留在 <c>CharacterGrain</c> 的私有方法里：那段折算含真实的口径决定
/// （"主人基础值传 0，只取 FromPets*"），埋在 Grain 里就只能靠起 Orleans 宿主才能验，
/// 而它是"面板数字对不对"的全部依据。抽出来之后单测直接喂快照断言数字。</para>
/// </summary>
public static class PetBonusProjection
{
    /// <summary>装配里没有读数器时的固定说法（面板据此显示"-"而不是 0）。</summary>
    public const string NotInjectedIssue = "本装配未注入宠物名册读数器（IPetBonusReader=null），宠物栏未取数";

    /// <summary>快照为 null = 没有取数通路；其余一律按快照里的原始加成量折算。</summary>
    public static PetBonusDisplay Project(PetRosterSnapshotDto? snapshot)
    {
        if (snapshot == null)
        {
            return new PetBonusDisplay
            {
                Issues = NotInjectedIssue,
            };
        }

        var bonus = new PetOwnerBonus
        {
            Strength = snapshot.Strength,
            Spirit = snapshot.Spirit,
            Vitality = snapshot.Vitality,
            Agility = snapshot.Agility,
            Steadiness = snapshot.Steadiness,
            HpFlat = snapshot.HpFlat,
            ManaRegen = snapshot.ManaRegen,
            CritTankChanceBps = snapshot.CritTankChanceBps,
            DamageLeechBps = snapshot.DamageLeechBps,
        };

        // 基础值全 0 是刻意的：只要 FromPets* 那四项归因量。
        // 换算系数（每点力量给多少攻击…）必须走内核那一份，在这里重抄一遍就会静默分叉。
        var ownerBase = new PetAssistCore.OwnerBaseStats();
        var final = PetAssistCore.ApplyToOwnerStats(bonus, ownerBase);

        return new PetBonusDisplay
        {
            Attack = final.FromPetsAttack,
            Health = final.FromPetsHp,
            Defense = final.FromPetsDefense,
            Hit = final.FromPetsHit,
            CritTankChanceBps = snapshot.CritTankChanceBps,
            Contributors = snapshot.Contributors,
            RosterCount = snapshot.RosterCount,
            Issues = snapshot.Issues ?? string.Empty,
        };
    }
}
