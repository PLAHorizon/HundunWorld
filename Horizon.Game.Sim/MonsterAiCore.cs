using System;
using System.Runtime.CompilerServices;

namespace Horizon.Game.Sim;

/// <summary>
/// 怪物 AI 状态（MMORPG 开发计划 M3.3）。
/// <para>
/// 状态语义（确定性状态机，无隐式转移）：
/// <list type="bullet">
///   <item><see cref="Idle"/>：出生/待机；超过 <see cref="MonsterAiConfig.IdleToPatrolSeconds"/> 转 <see cref="Patrol"/>。</item>
///   <item><see cref="Patrol"/>：围绕出生点游荡；发现仇恨目标转 <see cref="Chase"/>。</item>
///   <item><see cref="Chase"/>：向目标直线追击；进入 <see cref="MonsterAiConfig.AttackRange"/> 转 <see cref="Attack"/>；
///         超出 <see cref="MonsterAiConfig.LeashRadius"/> 转 <see cref="Leash"/>。</item>
///   <item><see cref="Attack"/>：在射程内按冷却攻击；目标离开射程转 <see cref="Chase"/>。</item>
///   <item><see cref="Leash"/>：脱战回归出生点（不再仇恨）；回到出生点半径内转 <see cref="Idle"/>。</item>
///   <item><see cref="Dead"/>：等待重生；<see cref="MonsterAiSelf.RespawnAtTick"/> 到达后由宿主转 <see cref="Idle"/>。</item>
/// </list>
/// </para>
/// </summary>
public enum MonsterAiState : byte
{
    /// <summary>待机。</summary>
    Idle = 0,

    /// <summary>游荡巡逻。</summary>
    Patrol = 1,

    /// <summary>追击。</summary>
    Chase = 2,

    /// <summary>攻击。</summary>
    Attack = 3,

    /// <summary>脱战回归。</summary>
    Leash = 4,

    /// <summary>死亡等待重生。</summary>
    Dead = 5,
}

/// <summary>
/// 怪物 AI 静态配置（来自刷怪点/模板，可在运行期不变更）。
/// <para>所有距离单位为米，与 <c>MovementFormula</c> 的世界尺度一致。</para>
/// </summary>
public struct MonsterAiConfig
{
    /// <summary>巡逻/追击最大移动速度（米/秒）。</summary>
    public float MoveSpeed;

    /// <summary>巡逻游荡半径（距出生点）。</summary>
    public float WanderRadius;

    /// <summary>仇恨半径：目标进入即开始追击。</summary>
    public float AggroRadius;

    /// <summary>脱战半径：距出生点超过该距离立即回归。</summary>
    public float LeashRadius;

    /// <summary>攻击射程。</summary>
    public float AttackRange;

    /// <summary>攻击冷却（秒）。</summary>
    public float AttackCooldownSeconds;

    /// <summary>待机转巡逻的时间（秒）。</summary>
    public float IdleToPatrolSeconds;

    /// <summary>最大生命值。</summary>
    public int MaxHp;

    /// <summary>攻击力（每次命中的基础伤害；最终伤害由战斗结算层决定）。</summary>
    public int AttackPower;

    /// <summary>死亡后重生延迟（秒）。</summary>
    public float RespawnDelaySeconds;

    /// <summary>
    /// 生产可用的一组默认值（便于测试与快速接入）。
    /// 取值保守：4 米/秒移动、10 米仇恨、30 米脱战——与既有 AOI chunk(16m) 尺度相容。
    /// </summary>
    public static MonsterAiConfig Default => new MonsterAiConfig
    {
        MoveSpeed = 4f,
        WanderRadius = 6f,
        AggroRadius = 10f,
        LeashRadius = 30f,
        AttackRange = 2.5f,
        AttackCooldownSeconds = 1.5f,
        IdleToPatrolSeconds = 3f,
        MaxHp = 100,
        AttackPower = 10,
        RespawnDelaySeconds = 15f,
    };
}

/// <summary>
/// 怪物 AI 自身状态（**权威**，由宿主持久化并逐 tick 回传）。
/// <para>宿主（Orleans <c>ZoneShardGrain</c> / UE5 DS）持有本结构，AI 内核只做纯函数决策。</para>
/// </summary>
public struct MonsterAiSelf
{
    /// <summary>当前世界坐标 X（Z-up）。</summary>
    public float X;

    /// <summary>当前世界坐标 Y（Z-up）。</summary>
    public float Y;

    /// <summary>当前世界坐标 Z（Z-up）。</summary>
    public float Z;

    /// <summary>当前朝向（弧度，仅表现用；不参与位置数学）。</summary>
    public float Yaw;

    /// <summary>出生点 X（脱战回归锚点）。</summary>
    public float SpawnX;

    /// <summary>出生点 Y。</summary>
    public float SpawnY;

    /// <summary>出生点 Z。</summary>
    public float SpawnZ;

    /// <summary>当前状态。</summary>
    public MonsterAiState State;

    /// <summary>当前生命值。</summary>
    public int Hp;

    /// <summary>当前攻击冷却剩余（秒；≤0 表示可攻击）。</summary>
    public float AttackCooldownRemaining;

    /// <summary>进入当前状态的累计时长（秒），用于 Idle→Patrol 等时长驱动转移。</summary>
    public float StateElapsedSeconds;

    /// <summary>待机/巡逻的方向符号（+1/-1），由宿主以确定性方式翻转，避免内核使用随机源。</summary>
    public float PatrolDirection;

    /// <summary>死亡后允许重生的服务器 tick；仅 <see cref="MonsterAiState.Dead"/> 有意义。</summary>
    public long RespawnAtTick;

    /// <summary>当前仇恨目标实体（0 = 无）。</summary>
    public ulong ThreatTargetId;
}

/// <summary>AI 视野内的候选目标（顺序由宿主给定，必须是**稳定顺序**以保证确定性）。</summary>
public struct MonsterAiTarget
{
    /// <summary>目标实体 ID。</summary>
    public ulong EntityId;

    /// <summary>目标世界坐标 X。</summary>
    public float X;

    /// <summary>目标世界坐标 Y。</summary>
    public float Y;

    /// <summary>目标世界坐标 Z。</summary>
    public float Z;

    /// <summary>目标是否存活（死亡目标不构成仇恨）。</summary>
    public bool IsAlive;

    /// <summary>目标是否可被攻击（同阵营/无敌/NPC 为 false）。</summary>
    public bool IsAttackable;
}

/// <summary>AI 单 tick 决策结果，由宿主应用。</summary>
public struct MonsterAiDecision
{
    /// <summary>决策后的状态（宿主写回 <see cref="MonsterAiSelf.State"/>）。</summary>
    public MonsterAiState NextState;

    /// <summary>本 tick 水平位移分量 X（已含速度与 dt，宿主直接加到坐标上）。</summary>
    public float DeltaX;

    /// <summary>本 tick 水平位移分量 Y。</summary>
    public float DeltaY;

    /// <summary>决策后的朝向（弧度，表现用）。</summary>
    public float NextYaw;

    /// <summary>攻击冷却剩余（宿主写回）。</summary>
    public float NextAttackCooldownRemaining;

    /// <summary>本 tick 是否发起一次攻击。</summary>
    public bool WantsAttack;

    /// <summary>本 tick 攻击的目标（<see cref="WantsAttack"/> 为 true 时有效）。</summary>
    public ulong AttackTargetId;

    /// <summary>仇恨目标（宿主写回 <see cref="MonsterAiSelf.ThreatTargetId"/>）。</summary>
    public ulong ThreatTargetId;

    /// <summary>本 tick 是否发生位移（用于宿主跳过无变化的快照 delta）。</summary>
    public bool Moved;
}

/// <summary>
/// 确定性怪物 AI 内核（MMORPG 开发计划 M3.3 —— 消灭缺口 G10「无怪物移动模型」与 G9「玩法不可对拍」）。
/// <para>
/// <b>为什么放在 <c>Horizon.Game.Sim</c></b>：与 <see cref="MovementFormula"/> 同源——Orleans
/// 权威路径（<c>ZoneShardGrain.TickAsync</c>）与 UE5 DS 权威路径必须消费**同一份决策数学**，
/// 否则「双份 AI 实现」会像当年的移动公式一样产生跨端漂移，且无法做金轨迹对拍。
/// </para>
/// <para>
/// <b>确定性约束</b>（与 <see cref="MovementFormula"/> 同级）：
/// <list type="bullet">
///   <item>纯静态函数：无全局状态、无 <c>DateTime.Now</c>、无随机源（随机性由宿主以确定性种子注入，
///         内核只消费 <see cref="MonsterAiSelf.PatrolDirection"/> 这类已确定的值）。</item>
///   <item>不使用 SIMD/向量类型；全程 <c>float</c>，运算顺序固定。</item>
///   <item>目标集合的遍历顺序由宿主保证稳定（<c>MonsterAiTarget[]</c> 按 EntityId 升序传入）。</item>
///   <item><c>MathF.Atan2</c> 仅用于 <see cref="MonsterAiDecision.NextYaw"/>（表现层朝向），
///         **永不回灌到位移数学**，故不构成位级一致性风险。</item>
/// </list>
/// </para>
/// <para>
/// 任何改变实体位置/状态迁移的修改必须递增 <see cref="Version"/>，并由宿主在握手中协商，
/// 与 <see cref="MovementFormula.FormulaVersion"/> 同一纪律。
/// </para>
/// </summary>
public static class MonsterAiCore
{
    /// <summary>
    /// 怪物 AI 内核版本。任何影响状态迁移或位移的改动都必须递增（金轨迹对拍锚点）。
    /// <para>v1：首版——Idle/Patrol/Chase/Attack/Leash/Dead 六态机 + 直线追击 + 冷却攻击。</para>
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// 推进一 tick 的怪物 AI 决策。
    /// </summary>
    /// <param name="self">自身状态（引用只读传入，内核不改动调用方结构）。</param>
    /// <param name="targets">视野内候选目标（**必须按 EntityId 升序**，保证确定性）。</param>
    /// <param name="dt">时间步长（秒），典型 1/60。</param>
    /// <param name="config">静态配置。</param>
    /// <param name="serverTick">当前服务器 tick（仅用于计算 <see cref="MonsterAiSelf.RespawnAtTick"/>）。</param>
    /// <returns>本 tick 决策；宿主负责应用到权威状态与快照。</returns>
    public static MonsterAiDecision Decide(
        in MonsterAiSelf self,
        ReadOnlySpan<MonsterAiTarget> targets,
        float dt,
        in MonsterAiConfig config,
        long serverTick)
    {
        var decision = new MonsterAiDecision
        {
            NextState = self.State,
            NextYaw = self.Yaw,
            NextAttackCooldownRemaining = self.AttackCooldownRemaining,
            ThreatTargetId = self.ThreatTargetId,
            DeltaX = 0f,
            DeltaY = 0f,
            Moved = false,
            WantsAttack = false,
            AttackTargetId = 0UL,
        };

        // 非法 dt：不做任何推进（与 MovementFormula 的 dt<=0 早退语义一致）。
        // 注意：早退发生在冷却递减之前——保持"零时长不改变任何状态"的纯函数语义，
        // 避免宿主在暂停/追帧场景下因重复调用而累积冷却漂移。
        if (dt <= 0f) return decision;

        // 冷却递减（无论何种状态都推进，避免脱战期间冷却"冻结"）。
        if (decision.NextAttackCooldownRemaining > 0f)
        {
            decision.NextAttackCooldownRemaining -= dt;
            if (decision.NextAttackCooldownRemaining < 0f) decision.NextAttackCooldownRemaining = 0f;
        }

        // --- 死亡分支：只等待重生 ---
        if (self.State == MonsterAiState.Dead)
        {
            if (self.RespawnAtTick > 0 && serverTick >= self.RespawnAtTick)
            {
                decision.NextState = MonsterAiState.Idle;
                decision.ThreatTargetId = 0UL;
                decision.NextAttackCooldownRemaining = 0f;
            }
            return decision;
        }

        // 血量归零 → 进入死亡（宿主据此广播 Despawn/DeathPacket）。
        if (self.Hp <= 0)
        {
            decision.NextState = MonsterAiState.Dead;
            decision.ThreatTargetId = 0UL;
            decision.Moved = false;
            return decision;
        }

        // --- 目标选择 ---
        // 语义区分（关键）：
        //   * 仇恨半径（AggroRadius）只负责"**获取**新目标"——Idle/Patrol 状态下的索敌范围。
        //   * 一旦进入 Chase/Attack，已锁定的目标在"仍存在/存活/可攻击"期间**持续有效**，
        //     不受仇恨半径约束（否则怪物追出 10 米就会立刻丢失目标回 Idle，表现为"追两步就发呆"）。
        //     追逐的终止条件由脱战半径（LeashRadius）单独负责。
        var threatId = ResolveThreat(self, targets, config);
        decision.ThreatTargetId = threatId;

        var distToSpawn = Distance2D(self.X, self.Y, self.SpawnX, self.SpawnY);
        var exceededLeash = distToSpawn > config.LeashRadius;

        switch (self.State)
        {
            case MonsterAiState.Idle:
            {
                if (threatId != 0UL && !exceededLeash)
                {
                    decision.NextState = MonsterAiState.Chase;
                    break;
                }

                if (self.StateElapsedSeconds >= config.IdleToPatrolSeconds)
                {
                    decision.NextState = MonsterAiState.Patrol;
                    break;
                }
                break;
            }

            case MonsterAiState.Patrol:
            {
                if (threatId != 0UL && !exceededLeash)
                {
                    decision.NextState = MonsterAiState.Chase;
                    break;
                }

                // 围绕出生点往返巡逻：方向由宿主提供（确定性），到边界则反向。
                var dir = self.PatrolDirection >= 0f ? 1f : -1f;
                var nextX = self.X + dir * config.MoveSpeed * dt;
                var nextDist = Distance2D(nextX, self.Y, self.SpawnX, self.SpawnY);
                if (nextDist > config.WanderRadius)
                {
                    // 到达游荡边界：反向并停在边界上（不越界），本 tick 不移动。
                    decision.NextState = MonsterAiState.Patrol;
                    decision.NextYaw = YawTowards(self.SpawnX, self.SpawnY, self.X, self.Y, self.Yaw);
                    break;
                }

                decision.DeltaX = dir * config.MoveSpeed * dt;
                decision.DeltaY = 0f;
                decision.Moved = decision.DeltaX != 0f;
                decision.NextYaw = YawTowards(self.X, self.Y, nextX, self.Y, self.Yaw);
                break;
            }

            case MonsterAiState.Chase:
            case MonsterAiState.Attack:
            {
                if (threatId == 0UL)
                {
                    // 目标消失/死亡 → 回归（有出生点偏移则 Leash，否则直接 Idle）。
                    decision.NextState = exceededLeash ? MonsterAiState.Leash : MonsterAiState.Idle;
                    break;
                }

                if (exceededLeash)
                {
                    decision.NextState = MonsterAiState.Leash;
                    break;
                }

                var t = FindTarget(targets, threatId);
                var distToTarget = Distance2D(self.X, self.Y, t.X, t.Y);

                if (distToTarget <= config.AttackRange)
                {
                    decision.NextState = MonsterAiState.Attack;
                    decision.NextYaw = YawTowards(self.X, self.Y, t.X, t.Y, self.Yaw);
                    if (decision.NextAttackCooldownRemaining <= 0f)
                    {
                        decision.WantsAttack = true;
                        decision.AttackTargetId = threatId;
                        decision.NextAttackCooldownRemaining = config.AttackCooldownSeconds;
                    }
                    break;
                }

                decision.NextState = MonsterAiState.Chase;
                var (dx, dy) = StepTowards(self.X, self.Y, t.X, t.Y, config.MoveSpeed, dt);
                decision.DeltaX = dx;
                decision.DeltaY = dy;
                decision.Moved = dx != 0f || dy != 0f;
                decision.NextYaw = YawTowards(self.X, self.Y, self.X + dx, self.Y + dy, self.Yaw);
                break;
            }

            case MonsterAiState.Leash:
            {
                var distHome = Distance2D(self.X, self.Y, self.SpawnX, self.SpawnY);
                if (distHome <= 0.5f)
                {
                    decision.NextState = MonsterAiState.Idle;
                    decision.NextYaw = YawTowards(self.X, self.Y, self.SpawnX, self.SpawnY, self.Yaw);
                    break;
                }

                var (dx, dy) = StepTowards(self.X, self.Y, self.SpawnX, self.SpawnY, config.MoveSpeed, dt);
                decision.DeltaX = dx;
                decision.DeltaY = dy;
                decision.Moved = dx != 0f || dy != 0f;
                decision.NextYaw = YawTowards(self.X, self.Y, self.X + dx, self.Y + dy, self.Yaw);
                decision.ThreatTargetId = 0UL;
                break;
            }

            default:
            {
                decision.NextState = MonsterAiState.Idle;
                break;
            }
        }

        return decision;
    }

    /// <summary>计算死亡时应设置的 <see cref="MonsterAiSelf.RespawnAtTick"/>。</summary>
    /// <param name="serverTick">当前服务器 tick。</param>
    /// <param name="ticksPerSecond">宿主 tick 频率（Orleans/DS 均为 60）。</param>
    /// <param name="respawnDelaySeconds">重生延迟（秒）。</param>
    public static long ComputeRespawnTick(long serverTick, int ticksPerSecond, float respawnDelaySeconds)
    {
        if (ticksPerSecond <= 0) ticksPerSecond = 60;
        var delaySeconds = respawnDelaySeconds > 0f ? respawnDelaySeconds : 0f;
        var ticks = (long)(delaySeconds * ticksPerSecond);
        return serverTick + ticks;
    }

    /// <summary>按 EntityId 升序排序目标（宿主在传入前调用，保证内核确定性）。</summary>
    /// <remarks>
    /// 使用插入排序而非 <c>Array.Sort</c>/LINQ：小数组下更快，且**消除不稳定排序**带来的
    /// 遍历顺序不确定性（同距离目标的相对顺序会直接影响威胁选择结果）。
    /// </remarks>
    public static void SortTargetsByEntityId(Span<MonsterAiTarget> targets)
    {
        for (var i = 1; i < targets.Length; i++)
        {
            var key = targets[i];
            var j = i - 1;
            while (j >= 0 && targets[j].EntityId > key.EntityId)
            {
                targets[j + 1] = targets[j];
                j--;
            }
            targets[j + 1] = key;
        }
    }

    /// <summary>
    /// 解析本 tick 的仇恨目标：优先沿用已锁定目标（追逐/攻击中），否则按仇恨半径获取新目标。
    /// </summary>
    private static ulong ResolveThreat(in MonsterAiSelf self, ReadOnlySpan<MonsterAiTarget> targets, in MonsterAiConfig config)
    {
        // 已锁定目标：仅校验"仍存在且可打"，不做距离约束（距离约束归 LeashRadius 管）。
        if (self.ThreatTargetId != 0UL && IsEngageable(self.ThreatTargetId, targets))
        {
            return self.ThreatTargetId;
        }

        // 仅在未接战时才受仇恨半径限制。
        if (self.State == MonsterAiState.Idle || self.State == MonsterAiState.Patrol)
        {
            return SelectThreat(self, targets, config);
        }

        // Chase/Attack/Leash 状态下锁定目标失效：允许立即按仇恨半径重新索敌，
        // 使"目标死亡后旁边还有别的敌人"能无缝转火，而不是先回到 Idle。
        return SelectThreat(self, targets, config);
    }

    /// <summary>目标是否仍可交战（存在 + 存活 + 可攻击）。</summary>
    private static bool IsEngageable(ulong entityId, ReadOnlySpan<MonsterAiTarget> targets)
    {
        for (var i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            if (t.EntityId == entityId)
            {
                return t.IsAlive && t.IsAttackable;
            }
        }
        return false;
    }

    /// <summary>
    /// 选择仇恨目标：仇恨半径内、存活且可攻击的**最近**目标；
    /// 距离相等时取 EntityId 较小者（消除顺序相关的歧义，保证确定性）。
    /// </summary>
    private static ulong SelectThreat(in MonsterAiSelf self, ReadOnlySpan<MonsterAiTarget> targets, in MonsterAiConfig config)
    {
        if (config.AggroRadius <= 0f) return 0UL;

        var aggroSq = config.AggroRadius * config.AggroRadius;
        var bestId = 0UL;
        var bestSq = float.MaxValue;

        for (var i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            if (!t.IsAlive || !t.IsAttackable || t.EntityId == 0UL) continue;

            var dx = t.X - self.X;
            var dy = t.Y - self.Y;
            var sq = dx * dx + dy * dy;
            if (sq > aggroSq) continue;

            // 严格小于：同距离保留先遇到者；因 targets 已按 EntityId 升序，等价于取较小 ID。
            if (sq < bestSq)
            {
                bestSq = sq;
                bestId = t.EntityId;
            }
        }

        return bestId;
    }

    private static MonsterAiTarget FindTarget(ReadOnlySpan<MonsterAiTarget> targets, ulong entityId)
    {
        for (var i = 0; i < targets.Length; i++)
        {
            if (targets[i].EntityId == entityId) return targets[i];
        }
        return default;
    }

    /// <summary>朝 (tx, ty) 直线移动一步；返回本 tick 的位移增量（不修改坐标）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (float DeltaX, float DeltaY) StepTowards(float x, float y, float tx, float ty, float speed, float dt)
    {
        var dx = tx - x;
        var dy = ty - y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= 1e-8f)
        {
            return (0f, 0f);
        }

        var len = MathF.Sqrt(lenSq);
        var step = speed * dt;
        // 不越过目标：按剩余距离截断，避免在目标点附近抖动（determinism 无关，纯粹手感/稳定性）。
        if (step >= len)
        {
            return (dx, dy);
        }

        var inv = 1f / len;
        return (dx * inv * step, dy * inv * step);
    }

    /// <summary>XZ 平面距离（Z-up 世界，水平面为 XY）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Distance2D(float ax, float ay, float bx, float by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 计算朝向（弧度，atan2(dy, dx) 约定）。
    /// <para>仅用于表现层；调用方必须保证该值不回灌到位移数学。</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float YawTowards(float fromX, float fromY, float toX, float toY, float fallbackYaw)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;
        if (dx == 0f && dy == 0f) return fallbackYaw;
        return MathF.Atan2(dy, dx);
    }
}
