namespace Horizon.Game.Sim.Npcs;

/// <summary>
/// NPC 的"残血逃跑"判据（<see cref="NpcAiBits.FleesAtLowHealth"/>）。
/// <para>
/// <b>阈值是一个有名字的常量，不是表列</b>：能力位本身只有"逃 / 不逃"两态；阈值若藏在
/// 编辑器里没露出的列上，运营会以为勾了位就该逃、而数字在别处。宿主每 tick 只问这一个函数，
/// 边界值（满血 / 一滴血 / 尸体 / 上限 0）就都能在一处钉住。
/// </para>
/// <para>判据只看"血够不够低"：是否在战斗、往哪跑由宿主决定——内核不知道 <c>MonsterAiState</c>。</para>
/// </summary>
public static class NpcFleeCore
{
    /// <summary>
    /// 逃跑阈值（百分比）。与 AzerothCore 的 <c>Creature::FleeToGetAssistance</c> 同量级：
    /// 低于四分之一血才开始逃，玩家先看到"快打死了"再看到"它跑了"。
    /// </summary>
    public const int HpPercentThreshold = 25;

    /// <summary>残血判据：活着、上限已知、且当前血量 ≤ 阈值百分比。</summary>
    public static bool IsLowHealth(int hp, int maxHp)
    {
        // 用 long 乘：两个 int 直接相乘会溢出成负数，进而把满血判成残血。
        return hp > 0 && maxHp > 0 && (long)hp * 100L <= (long)maxHp * HpPercentThreshold;
    }

    /// <summary>能力位 + 残血两条同时成立才逃。</summary>
    public static bool ShouldFlee(bool fleesAtLowHealth, int hp, int maxHp)
        => fleesAtLowHealth && IsLowHealth(hp, maxHp);

    /// <summary>诊断/回执用的一行摘要。</summary>
    public static string Describe(int hp, int maxHp)
    {
        var percent = maxHp > 0 ? (int)((long)hp * 100L / maxHp) : 0;
        return $"hp={hp}/{maxHp} ({percent}%) flee_below={HpPercentThreshold}%";
    }
}
