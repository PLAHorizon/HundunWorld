using System;
using Horizon.Game.Sim.Monsters;

namespace Horizon.Game.Sim.Pets;

/// <summary>跟随状态机的状态。</summary>
public enum PetFollowState : byte
{
    /// <summary>跟随主人（在 leash 内待机或走位）。</summary>
    Following = 0,

    /// <summary>被指令攻击/防御，暂时不跟。</summary>
    Engaged = 1,

    /// <summary>待命（WoW 的 Wait：原地不动，主人走远了也不追，§A5）。</summary>
    Waiting = 2,

    /// <summary>脱战归位中。</summary>
    Returning = 3,

    /// <summary>超距，已传送到主人身边（WoW 表现：宠物跑不过来就直接出现，§A12）。</summary>
    Teleported = 4,

    /// <summary>尸体滞留原地（血 0；不消失、不可操作，§A12）。</summary>
    Corpse = 5,

    /// <summary>复活施法中（§A12：4 秒施法 / 1.5 秒 GCD）。</summary>
    Reviving = 6,

    /// <summary>已收回（不在场景里）。</summary>
    Summoned_Out = 7,
}

/// <summary>跟随与寻路参数（物种模板可逐项覆盖）。</summary>
public readonly struct PetFollowConfig
{
    /// <summary>理想跟随距离（米）——宠物会停在主人周围这个半径内，而不是叠在主人身上。</summary>
    public readonly float FollowDistance;

    /// <summary>脱战 leash 半径（米）：超出即进入 <see cref="PetFollowState.Returning"/>。</summary>
    public readonly float LeashRadius;

    /// <summary>硬 leash：超出该半径直接传送（WoW 的"跑不过来就出现"，§A12）。</summary>
    public readonly float TeleportBeyondRadius;

    /// <summary>重算路径的最小间隔（tick）；防止每 tick 重寻路把 CPU 打满。</summary>
    public readonly int RepathIntervalTicks;

    /// <summary>移动速度（米/秒）。</summary>
    public readonly float Speed;

    /// <summary>到点判定距离（米）。</summary>
    public readonly float ArriveEpsilon;

    /// <summary>尸体滞留秒数；0 = 不留尸体（天龙口径"血 0 可重召"，§D12）。</summary>
    public readonly int CorpseSeconds;

    /// <summary>复活施法秒数（WoW 经典 4 秒，§A12）。</summary>
    public readonly int ReviveCastSeconds;

    /// <summary>召唤/收回冷却秒数（兽栏瞬召为 0，§A12）。</summary>
    public readonly int SummonCooldownSeconds;

    /// <summary>跟随起步延迟（毫秒）。四款游戏都没有"贴主人背后立刻出现"，短延迟才有"它在跟着你"的观感。</summary>
    public readonly int FollowStartDelayMs;

    public PetFollowConfig(float followDistance, float leashRadius, float teleportBeyondRadius,
        int repathIntervalTicks, float speed, float arriveEpsilon, int corpseSeconds,
        int reviveCastSeconds, int summonCooldownSeconds, int followStartDelayMs)
    {
        FollowDistance = followDistance <= 0f ? 3f : followDistance;
        LeashRadius = leashRadius < FollowDistance ? FollowDistance : leashRadius;
        TeleportBeyondRadius = teleportBeyondRadius < LeashRadius ? LeashRadius * 2f : teleportBeyondRadius;
        RepathIntervalTicks = repathIntervalTicks < 1 ? 1 : repathIntervalTicks;
        Speed = speed <= 0f ? 4f : speed;
        ArriveEpsilon = arriveEpsilon <= 0f ? 0.5f : arriveEpsilon;
        CorpseSeconds = corpseSeconds < 0 ? 0 : corpseSeconds;
        ReviveCastSeconds = reviveCastSeconds < 0 ? 0 : reviveCastSeconds;
        SummonCooldownSeconds = summonCooldownSeconds < 0 ? 0 : summonCooldownSeconds;
        FollowStartDelayMs = followStartDelayMs < 0 ? 0 : followStartDelayMs;
    }

    /// <summary>
    /// 默认值取 WoW 猎人宠的观感尺度（3 米跟随、12 米 leash、40 米硬传送、跑速略快于主人）。
    /// <para>国产三家是回合制、没有 leash 概念（§B12/§C12），所以这里的"形状"只能来自 WoW；
    /// 数值尺度按本作米制给出，属可调初值。</para>
    /// </summary>
    public static PetFollowConfig Default => new(
        followDistance: 3f, leashRadius: 12f, teleportBeyondRadius: 40f,
        repathIntervalTicks: 10, speed: 5.5f, arriveEpsilon: 0.6f,
        corpseSeconds: 0, reviveCastSeconds: 4, summonCooldownSeconds: 0, followStartDelayMs: 300);
}

/// <summary>一次跟随推进的结果。</summary>
public readonly struct PetFollowStep
{
    /// <summary>新状态。</summary>
    public readonly PetFollowState State;

    /// <summary>新坐标（X/Y 平面，与服务端 <see cref="MonsterNavGrid"/> 同域）。</summary>
    public readonly float NewX;
    public readonly float NewY;

    /// <summary>与主人距离（米），留在结果里是为了让"为什么传过来了"可解释。</summary>
    public readonly float DistanceToOwner;

    /// <summary>本 tick 是否走了导航路径（false = 直线跟随或没动）。</summary>
    public readonly bool UsedNavigation;

    /// <summary>是否发生传送。</summary>
    public readonly bool Teleported;

    public PetFollowStep(PetFollowState state, float newX, float newY, float distanceToOwner,
        bool usedNavigation, bool teleported)
    {
        State = state;
        NewX = newX;
        NewY = newY;
        DistanceToOwner = distanceToOwner;
        UsedNavigation = usedNavigation;
        Teleported = teleported;
    }
}

/// <summary>
/// 宠物跟随与自动寻路的确定性纯函数（逆向规格 §A12/§D12/§E2）。
/// <para>
/// <b>复用 <see cref="MonsterPathCore"/> 而不是再写一份 A*</b>：跟随和巡逻用的图、代价、
/// 启发式是同一张导航网格，两份实现必然漂。这里只加"跟随目标一直在动"这件事特有的策略
/// （重算节流、超距传送、尸体/复活时间线）。
/// </para>
/// <para>
/// <b>坐标系纪律</b>：本模块在**服务端 2D 平面（X/Y）**上工作。写回 Flax 客户端时要按
/// 既有规范做 Y/Z 交换（记忆 <c>ecs-flax-conversion</c>、<c>ds-transform-3d</c>）；
/// 客户端一侧的对应实现走 <c>NarrativePro.Navigation.NavigatorStatics</c>（见 §3.11 设计文档）。
/// </para>
/// </summary>
public static class PetFollowCore
{
    /// <summary>跟随内核版本。</summary>
    public const int Version = 1;

    /// <summary>两点距离（米）。</summary>
    public static float Distance(float ax, float ay, float bx, float by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 该不该走导航：<b>近距离一律直线</b>。
    /// <para>理由不是省 CPU：跟随场景里目标每 tick 都在动，走 A* 会让宠物在主人脚边
    /// 反复重算出一条新折线，观感是"抖"。四款游戏里宠物在主人身边都是直接走位的。</para>
    /// </summary>
    public static bool ShouldUseNavigation(in PetFollowConfig config, float distanceToOwner)
        => distanceToOwner > config.FollowDistance * 2f;

    /// <summary>
    /// 推进一个 tick。
    /// </summary>
    /// <param name="state">当前状态。</param>
    /// <param name="config">跟随参数。</param>
    /// <param name="selfX">宠物 X（服务端平面）。</param>
    /// <param name="selfY">宠物 Y。</param>
    /// <param name="ownerX">主人 X。</param>
    /// <param name="ownerY">主人 Y。</param>
    /// <param name="dt">步长（秒）。</param>
    /// <param name="command">玩家即时指令（<see cref="PetCommand.Wait"/> 会冻结跟随）。</param>
    /// <param name="grid">导航网格；<c>default</c> 表示没有网格，只走直线。</param>
    /// <param name="scratch">A* 工作缓冲（宿主复用）；可 null。</param>
    /// <param name="pathCells">路径输出缓冲（宿主复用）；可 null。</param>
    /// <param name="pathCursor">路点游标（引用，宿主持有）。</param>
    /// <param name="pathLength">本次路径的有效长度（引用，宿主持有）。<b>必须是寻路返回的长度而不是缓冲长度</b>——
    /// 用缓冲长度会让角色朝缓冲区里的垃圾格走。</param>
    /// <param name="ticksSinceRepath">距上次重算的 tick 数（引用，本函数负责前进）。</param>
    /// <remarks>
    /// 判据顺序：<b>传送门 &gt; leash 归位 &gt; 待机 &gt; 正常跟随</b>。
    /// 传送判在最前是为了"主人已经跑出半个地图"时不再演一段追不上的戏——WoW 就是这么处理的（§A12）。
    /// </remarks>
    public static PetFollowStep Advance(PetFollowState state, in PetFollowConfig config, float selfX, float selfY,
        float ownerX, float ownerY, float dt, PetCommand command, in MonsterNavGrid grid,
        MonsterNavScratch scratch, Span<int> pathCells, ref int pathCursor, ref int pathLength,
        ref int ticksSinceRepath)
    {
        var dist = Distance(selfX, selfY, ownerX, ownerY);

        // 尸体/复活时间线不参与跟随决策：状态本身把它们挡在外面。
        if (state is PetFollowState.Corpse or PetFollowState.Reviving or PetFollowState.Summoned_Out)
            return new PetFollowStep(state, selfX, selfY, dist, false, false);

        // ① 超距传送
        if (dist > config.TeleportBeyondRadius)
        {
            pathCursor = 0;
            pathLength = 0;
            ticksSinceRepath = 0;
            var tx = ownerX;
            var ty = ownerY;
            OffsetToFollowDistance(ref tx, ref ty, selfX, selfY, config.FollowDistance);
            return new PetFollowStep(PetFollowState.Teleported, tx, ty, dist, false, true);
        }

        // ② 待命指令：原地不动，也不进 Returning
        if (command == PetCommand.Wait || state == PetFollowState.Waiting)
            return new PetFollowStep(PetFollowState.Waiting, selfX, selfY, dist, false, false);

        // ③ 交战中不跟（攻击/防御指令下宠物有自己的目标）
        if (command is PetCommand.Attack or PetCommand.Defend || state == PetFollowState.Engaged)
            return new PetFollowStep(PetFollowState.Engaged, selfX, selfY, dist, false, false);

        // ④ leash 内且已在跟随距离附近：不动，避免"绕着主人转圈"
        if (dist <= config.FollowDistance + config.ArriveEpsilon && state != PetFollowState.Returning)
            return new PetFollowStep(PetFollowState.Following, selfX, selfY, dist, false, false);

        // ⑤ 归位或跟随
        var nextX = selfX;
        var nextY = selfY;
        var usedNav = false;

        var beyond = dist > config.LeashRadius;
        var targetState = beyond ? PetFollowState.Returning : PetFollowState.Following;

        if (grid.IsValid && scratch is not null && pathCells.Length > 0 && ShouldUseNavigation(config, dist))
        {
            ticksSinceRepath++;
            if (pathCursor <= 0 || pathCursor >= pathLength || ticksSinceRepath >= config.RepathIntervalTicks)
            {
                // 目标是"主人周围的跟随点"，不是主人本体：寻到本体只会让宠物反复撞主人身上。
                var goalX = ownerX;
                var goalY = ownerY;
                OffsetToFollowDistance(ref goalX, ref goalY, selfX, selfY, config.FollowDistance);

                var result = MonsterPathCore.FindPath(in grid, scratch, nextX, nextY, goalX, goalY,
                    pathCells, out var found);
                if ((result == MonsterPathResult.Found || result == MonsterPathResult.Partial
                     || result == MonsterPathResult.Direct) && found > 0)
                {
                    pathLength = found;
                    pathCursor = 0;
                    ticksSinceRepath = 0;
                }
                else
                {
                    pathLength = 0;
                    pathCursor = -1; // 明确标记"这次没路"，交给直线兜底
                }
            }

            if (pathCursor >= 0 && pathLength > 0)
            {
                var moved = MonsterPathCore.AdvanceAlongPath(in grid, pathCells.Slice(0, pathLength),
                    pathLength, ref pathCursor, ref nextX, ref nextY, config.Speed, dt, config.ArriveEpsilon);
                usedNav = moved;
            }
        }

        if (!usedNav)
        {
            // 直线兜底：没有网格、寻路失败、或距离短到不值得寻路。
            var goalX = ownerX;
            var goalY = ownerY;
            OffsetToFollowDistance(ref goalX, ref goalY, selfX, selfY, config.FollowDistance);
            MoveToward(config.Speed * dt, ref nextX, ref nextY, goalX, goalY);
        }

        return new PetFollowStep(targetState, nextX, nextY, dist, usedNav, false);
    }

    /// <summary>
    /// 把目标点定在<b>主人周围</b>距主人 <paramref name="followDistance"/> 的那一点（朝宠物方向回退）。
    /// <para>宠物与主人重合时给一个固定偏移，避免除零并保证结果确定（同输入同输出）。
    /// <b>回退方向必须是"主人→宠物"</b>：早前版本按"宠物→主人"走了 <paramref name="followDistance"/> 米，
    /// 于是跟随点被算成了"离主人 dist−3 米"，超距传送还会把宠物放到离主人 97 米的地方——
    /// 那正是"传送了但还在画面外"的表现。</para>
    /// </summary>
    public static void OffsetToFollowDistance(ref float goalX, ref float goalY, float selfX, float selfY,
        float followDistance)
    {
        // 从主人出发、朝宠物方向回退；宠物已在回退距离之内则目标就是宠物自己（等价于不移动）。
        var dx = selfX - goalX;
        var dy = selfY - goalY;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= 1e-8f)
        {
            goalX = goalX + followDistance;
            goalY = goalY;
            return;
        }
        var len = (float)Math.Sqrt(lenSq);
        var back = len < followDistance ? len : followDistance;
        goalX = goalX + dx / len * back;
        goalY = goalY + dy / len * back;
    }

    /// <summary>沿直线推进最多 <paramref name="budget"/> 米；不足则直接落到目标点（不越过）。</summary>
    public static void MoveToward(float budget, ref float x, ref float y, float targetX, float targetY)
    {
        if (budget <= 0f) return;
        var dx = targetX - x;
        var dy = targetY - y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= 1e-8f) return;
        var len = (float)Math.Sqrt(lenSq);
        if (len <= budget)
        {
            x = targetX;
            y = targetY;
            return;
        }
        x += dx / len * budget;
        y += dy / len * budget;
    }

    /// <summary>血 0 → 进入尸体态；返回滞留秒数（0 = 不留尸体，直接可重召，天龙 §D12）。</summary>
    public static (PetFollowState State, int CorpseSeconds) OnPetDied(in PetFollowConfig config)
        => (config.CorpseSeconds > 0 ? PetFollowState.Corpse : PetFollowState.Summoned_Out, config.CorpseSeconds);

    /// <summary>
    /// 复活推进：返回新状态与剩余施法秒。
    /// <para>WoW 经典是 4 秒施法 + 1.5 秒 GCD（§A12）；GCD 不单列参数，
    /// 因为"复活完成到能动作"的间隔在四款游戏里都没被单独观测过，把它并进施法时长是唯一有证据的做法。</para>
    /// </summary>
    public static (PetFollowState State, float RemainingCastSeconds) AdvanceRevive(
        float elapsedCastSeconds, in PetFollowConfig config)
    {
        var need = config.ReviveCastSeconds;
        if (need <= 0f) return (PetFollowState.Returning, 0f);
        var left = need - elapsedCastSeconds;
        if (left <= 0f) return (PetFollowState.Returning, 0f);
        return (PetFollowState.Reviving, left);
    }

    /// <summary>召唤/收回是否在冷却中（兽栏瞬召无 CD，§A12）。</summary>
    public static bool IsSummonOnCooldown(float elapsedSinceSummonSeconds, in PetFollowConfig config)
        => config.SummonCooldownSeconds > 0 && elapsedSinceSummonSeconds < config.SummonCooldownSeconds;
}
