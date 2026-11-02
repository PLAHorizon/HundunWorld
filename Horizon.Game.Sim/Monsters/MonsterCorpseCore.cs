namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// 尸体停留窗口（WoW 观感：怪死后尸体会在地上躺一段时间，然后消失；重生是另一件事）。
/// <para>
/// 纯函数，不碰随机也不碰时钟：宿主把"死亡 tick / 每秒 tick / 配置秒数 / 重生 tick"喂进来，
/// 拿回"尸体消失的那个 tick"。定死成函数而不是散在 if 里，是因为这条边界一旦写错，
/// 症状是"同一实体 ID 在同一 tick 里既 Despawn 又 Spawn"——客户端表现成永久消失的怪，
/// 而这种时序在离线门里只看字节顺序，不看渲染。
/// </para>
/// </summary>
public static class MonsterCorpseCore
{
    /// <summary>尸体拾取窗口的缺省秒数（AzerothCore `CONFIG_CORPSE_DECAY_NORMAL` = 60）。</summary>
    /// <remarks>
    /// 2026-10-28 拍板：缺省用 AC 的数值，但窗口时长与拾取距离都**可逐怪配置**（内容列），
    /// 这两个常量只是"没配时"的底座，不是唯一真值。
    /// </remarks>
    public const float DefaultLootWindowSeconds = 60f;

    /// <summary>拾取交互距离的缺省值（AzerothCore `INTERACTION_DISTANCE` = 5.5 米）。</summary>
    public const float DefaultPickupDistanceMeters = 5.5f;

    /// <summary>能不能从这具尸体上取东西——三种结果各自可区分，别用 bool 把它压成一种。</summary>
    public enum PickupDenial
    {
        /// <summary>可以取。</summary>
        None = 0,
        /// <summary>窗口已过（含"正好到期"那一 tick）：尸体马上就要被收走了。</summary>
        Expired = 1,
        /// <summary>人在拾取距离之外。</summary>
        TooFar = 2,
    }

    /// <summary>
    /// 拾取判据：窗口未过 **且** 人在距离内。
    /// <para><b>边界口径</b>：<c>nowTick &gt;= expiryTick</c> 就算过期——收尸发生在到期那一拍，
    /// 让"到期那一拍还能取一次"与收尸竞争，症状是玩家偶尔取到一件本该消失的东西。</para>
    /// <para><b>距离口径</b>：传进来的 <paramref name="distanceMeters"/> 与阈值比较用 <c>&gt;</c>，
    /// 正好站在 5.5 米上算"够得着"（AC 也是 `IsWithinDistInMap` 的含边界写法）。</para>
    /// <para>这一步只回答"这一下能不能取"，**不回答"取完之后尸体怎么变"**（那属 D24–D27）。</para>
    /// </summary>
    public static PickupDenial CanPickUp(long nowTick, long expiryTick, float distanceMeters, float pickupDistanceMeters)
    {
        if (expiryTick <= 0 || nowTick >= expiryTick) return PickupDenial.Expired;
        var limit = pickupDistanceMeters > 0f ? pickupDistanceMeters : DefaultPickupDistanceMeters;
        return distanceMeters > limit ? PickupDenial.TooFar : PickupDenial.None;
    }

    /// <summary>
    /// 谁能从这具尸体上取东西（AzerothCore `Loot::CanLoot` + `AllowedForPlayer` 的 `DIDNT_KILL`）。
    /// <para>2026-10-29 拍板：**击杀参与者**才能取。名单为空表示"不限制"——这对应两种真实情况：
    /// ① 环境击杀/自爆的怪没有任何玩家贡献；② 接入本判据之前写下的历史登记。
    /// 把空名单当成"谁都取不了"会让第 ① 种情况下的尸体变成永远打不开的箱子。</para>
    /// <para><paramref name="takerCharacterId"/> 为 0（不知道是谁）**不**能通过非空名单：
    /// 认不出人时放行，等于把权限判据交给"调用方忘了传参"。</para>
    /// </summary>
    public static bool IsAllowedLooter(System.Collections.Generic.IReadOnlyList<ulong>? contributors, ulong takerCharacterId)
    {
        if (contributors == null || contributors.Count == 0) return true;
        if (takerCharacterId == 0UL) return false;
        for (var i = 0; i < contributors.Count; i++)
            if (contributors[i] == takerCharacterId) return true;
        return false;
    }

    /// <summary>
    /// 计算尸体消失 tick。返回值 &lt;= <paramref name="deathTick"/> 表示"当场消失"（接入本功能前的行为）。
    /// </summary>
    /// <param name="respawnTick">同一槽位下一次重生的 tick；&lt;= deathTick 表示没有排定重生。</param>
    public static long PlanExpiryTick(
        long deathTick,
        int ticksPerSecond,
        float corpseSeconds,
        long respawnTick)
    {
        if (ticksPerSecond <= 0) ticksPerSecond = 60;
        if (corpseSeconds <= 0f) return deathTick;

        var expiry = deathTick + (long)(corpseSeconds * ticksPerSecond);

        // 尸体绝不能活到（或超过）重生那一 tick：重生会用**同一个实体 ID** 重新 Spawn，
        // 而死亡实体的 Despawn 走全广播、Spawn 走快照，两条路在同一个 tick 里的先后由网关决定。
        // 客户端若先收到 Spawn 再收到 Despawn，这只怪就再也没有第二次 Spawn 机会
        // （SpawnedToClients 已被置真）——屏幕上是一块永久空位。
        // 所以最迟在重生前一 tick 收尸；corpse_seconds 大于刷新延迟时，玩家看到的就是"尸体被新怪取代"。
        if (respawnTick > deathTick && expiry >= respawnTick) expiry = respawnTick - 1;

        return expiry > deathTick ? expiry : deathTick;
    }
}
