using System;
using System.Runtime.CompilerServices;

namespace Horizon.Game.Sim;

/// <summary>
/// 刷怪点配置（MMORPG 开发计划 M3.2 —— 消灭缺口 G6「开放世界无刷怪点」）。
/// <para>
/// 一个刷怪点 = 一个"怪槽位池"：最多同时维持 <see cref="MaxAlive"/> 只怪，
/// 每只怪以确定性方式分配实体 ID 与出生偏移，死亡后按 <see cref="RespawnDelaySeconds"/> 重生。
/// </para>
/// <para>
/// 配置来源可以是 <c>GameConfigGrain</c>（既有）或 shard 初始化参数；本结构只描述数据，
/// 不关心加载方式——保证 <c>Horizon.Game.Sim</c> 不引入任何存储/框架依赖。
/// </para>
/// </summary>
public struct SpawnPointConfig
{
    /// <summary>刷怪点 ID（唯一；参与实体 ID 分配）。</summary>
    public int SpawnPointId;

    /// <summary>所属 shard。</summary>
    public long ShardId;

    /// <summary>刷怪中心 X（Z-up 世界坐标）。</summary>
    public float X;

    /// <summary>刷怪中心 Y。</summary>
    public float Y;

    /// <summary>刷怪中心 Z。</summary>
    public float Z;

    /// <summary>怪物初始朝向（弧度）。</summary>
    public float Yaw;

    /// <summary>怪物模板 ID（决定外观/数值/掉落表）。</summary>
    public int MonsterTemplateId;

    /// <summary>同时存活上限（= 槽位数）。</summary>
    public int MaxAlive;

    /// <summary>死亡后重生延迟（秒）。</summary>
    public float RespawnDelaySeconds;

    /// <summary>同点怪物的出生横向间距（米）；<c>slot</c> 沿 X 轴对称展开，避免重叠。</summary>
    public float SlotSpacing;

    /// <summary>巡逻游荡半径（米），透传给 <see cref="MonsterAiConfig.WanderRadius"/>。</summary>
    public float WanderRadius;

    /// <summary>仇恨半径（米）。</summary>
    public float AggroRadius;

    /// <summary>脱战半径（米）。</summary>
    public float LeashRadius;

    /// <summary>最大生命值。</summary>
    public int MaxHp;

    /// <summary>攻击力。</summary>
    public int AttackPower;

    /// <summary>移动速度（米/秒）。</summary>
    public float MoveSpeed;

    /// <summary>
    /// 生产可用的默认配置：3 只怪、8 米间距、15 秒重生、10 米仇恨。
    /// </summary>
    public static SpawnPointConfig Default => new SpawnPointConfig
    {
        SpawnPointId = 1,
        ShardId = 0,
        X = 0f,
        Y = 0f,
        Z = 0f,
        Yaw = 0f,
        MonsterTemplateId = 1,
        MaxAlive = 3,
        RespawnDelaySeconds = 15f,
        SlotSpacing = 8f,
        WanderRadius = 6f,
        AggroRadius = 10f,
        LeashRadius = 30f,
        MaxHp = 100,
        AttackPower = 10,
        MoveSpeed = 4f,
    };

    /// <summary>把刷怪点配置投影为本点怪物的 AI 静态配置。</summary>
    public MonsterAiConfig ToAiConfig()
    {
        var baseCfg = MonsterAiConfig.Default;
        baseCfg.MoveSpeed = MoveSpeed;
        baseCfg.WanderRadius = WanderRadius;
        baseCfg.AggroRadius = AggroRadius;
        baseCfg.LeashRadius = LeashRadius;
        baseCfg.MaxHp = MaxHp;
        baseCfg.AttackPower = AttackPower;
        baseCfg.RespawnDelaySeconds = RespawnDelaySeconds;
        return baseCfg;
    }
}

/// <summary>刷怪点内单个槽位的状态（宿主持久化）。</summary>
public struct SpawnSlotState
{
    /// <summary>该槽位当前怪物实体 ID；0 = 从未生成或已回收。</summary>
    public ulong EntityId;

    /// <summary>是否存活（true 时 <see cref="EntityId"/> 有效且在世界实体表中）。</summary>
    public bool Alive;

    /// <summary>允许重生的服务器 tick（<see cref="Alive"/> 为 false 时有意义）。</summary>
    public long RespawnAtTick;
}

/// <summary>单槽位本 tick 的刷怪决策。</summary>
public struct SpawnSlotDecision
{
    /// <summary>槽位序号。</summary>
    public int SlotIndex;

    /// <summary>是否应当在本 tick 生成/重生一只怪。</summary>
    public bool ShouldSpawn;

    /// <summary>生成的实体 ID（<see cref="ShouldSpawn"/> 为 true 时有效）。</summary>
    public ulong EntityId;

    /// <summary>出生坐标 X。</summary>
    public float X;

    /// <summary>出生坐标 Y。</summary>
    public float Y;

    /// <summary>出生坐标 Z。</summary>
    public float Z;

    /// <summary>初始朝向（弧度）。</summary>
    public float Yaw;
}

/// <summary>刷怪点本 tick 的整体决策结果。</summary>
public struct SpawnPointDecision
{
    /// <summary>本 tick 需要生成的槽位（<see cref="ShouldSpawn"/> 为 true 的项）。</summary>
    public SpawnSlotDecision[] Spawns;

    /// <summary>本 tick 新进入"死亡待重生"的槽位（宿主据此写回 <see cref="SpawnSlotState.RespawnAtTick"/>）。</summary>
    public int[] NewlyDeadSlots;
}

/// <summary>
/// 确定性刷怪调度内核（MMORPG 开发计划 M3.2）。
/// <para>
/// <b>为什么放在 <c>Horizon.Game.Sim</c></b>：与 <see cref="MonsterAiCore"/> 同理——Orleans 权威路径
/// 与 UE5 DS 权威路径必须消费同一份刷怪决策，否则"同一 shard 在两套宿主上怪的数量/位置不同"，
/// 灰度对比与金轨迹对拍全部失效。
/// </para>
/// <para>
/// <b>确定性约束</b>：
/// <list type="bullet">
///   <item>实体 ID = <c>(SpawnPointId &lt;&lt; 32) | (slotIndex + 1)</c>——纯整数运算，
///         同一槽位跨重生保持同一 ID 段，且不同刷怪点天然不冲突（消除随机 ID 分配）。</item>
///   <item>出生偏移只沿 X 轴按 <see cref="SpawnPointConfig.SlotSpacing"/> 对称展开（仅乘加），
///         不引入三角函数/随机源。</item>
///   <item>不依赖 <c>DateTime.Now</c>；时间一律用 <c>serverTick</c> 表达。</item>
/// </list>
/// </para>
/// </summary>
public static class SpawnDirectorCore
{
    /// <summary>
    /// 刷怪内核版本。任何影响生成时机/数量/位置的改动都必须递增（金轨迹对拍锚点）。
    /// <para>v1：首版——槽位池 + 重生延迟 + 对称出生偏移。</para>
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// 计算槽位对应的确定性实体 ID。
    /// <para>高 32 位 = 刷怪点 ID，低 32 位 = 槽位序号 + 1（0 保留给"无实体"）。</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ComputeEntityId(int spawnPointId, int slotIndex)
    {
        var hi = (ulong)(uint)spawnPointId;
        var lo = (ulong)(uint)(slotIndex + 1);
        return (hi << 32) | lo;
    }

    /// <summary>
    /// 计算槽位的确定性出生坐标（沿 X 轴对称展开）。
    /// </summary>
    public static (float X, float Y, float Z) ComputeSpawnPosition(in SpawnPointConfig config, int slotIndex)
    {
        var count = config.MaxAlive > 0 ? config.MaxAlive : 1;
        // 以中心对称展开：offset = (slot - (count-1)/2) * spacing
        // 用整数 half 避免 count-1 的浮点除法带来的精度歧义。
        var center = (count - 1) * 0.5f;
        var offset = (slotIndex - center) * config.SlotSpacing;
        return (config.X + offset, config.Y, config.Z);
    }

    /// <summary>
    /// 刷新一次刷怪点。纯函数：不修改传入的 <paramref name="slots"/>，由宿主应用结果。
    /// </summary>
    /// <param name="config">刷怪点配置。</param>
    /// <param name="slots">槽位状态数组（长度应等于 <see cref="SpawnPointConfig.MaxAlive"/>；不足时按缺省"待生成"处理）。</param>
    /// <param name="serverTick">当前服务器 tick。</param>
    /// <param name="ticksPerSecond">宿主 tick 频率（Orleans/DS 均为 60）。</param>
    /// <returns>本 tick 的生成决策。</returns>
    public static SpawnPointDecision Tick(
        in SpawnPointConfig config,
        ReadOnlySpan<SpawnSlotState> slots,
        long serverTick,
        int ticksPerSecond)
    {
        var count = config.MaxAlive;
        if (count <= 0)
        {
            return new SpawnPointDecision
            {
                Spawns = Array.Empty<SpawnSlotDecision>(),
                NewlyDeadSlots = Array.Empty<int>(),
            };
        }

        var spawns = new System.Collections.Generic.List<SpawnSlotDecision>(count);
        var newlyDead = new System.Collections.Generic.List<int>();

        for (var slot = 0; slot < count; slot++)
        {
            // 槽位缺失（宿主尚未扩容数组）→ 视为待生成。
            var state = slot < slots.Length
                ? slots[slot]
                : default;

            if (state.Alive)
            {
                continue;
            }

            // 死后重生：仅在到达重生 tick 时生成。
            // RespawnAtTick == 0 表示"从未生成过"——首次生成不受延迟约束。
            if (state.RespawnAtTick > 0 && serverTick < state.RespawnAtTick)
            {
                continue;
            }

            var (x, y, z) = ComputeSpawnPosition(config, slot);
            spawns.Add(new SpawnSlotDecision
            {
                SlotIndex = slot,
                ShouldSpawn = true,
                EntityId = ComputeEntityId(config.SpawnPointId, slot),
                X = x,
                Y = y,
                Z = z,
                Yaw = config.Yaw,
            });
        }

        return new SpawnPointDecision
        {
            Spawns = spawns.ToArray(),
            NewlyDeadSlots = newlyDead.ToArray(),
        };
    }

    /// <summary>
    /// 标记槽位死亡：计算重生 tick 并写回槽位状态（宿主在实体死亡时调用）。
    /// </summary>
    /// <param name="config">刷怪点配置。</param>
    /// <param name="slots">槽位状态数组（**会被就地修改**）。</param>
    /// <param name="slotIndex">槽位序号。</param>
    /// <param name="serverTick">当前服务器 tick。</param>
    /// <param name="ticksPerSecond">宿主 tick 频率。</param>
    /// <returns>重生 tick；槽位越界时返回 -1。</returns>
    public static long MarkSlotDead(Span<SpawnSlotState> slots, int slotIndex, long serverTick, int ticksPerSecond, float respawnDelaySeconds)
    {
        if (slotIndex < 0 || slotIndex >= slots.Length) return -1L;

        var respawnAt = MonsterAiCore.ComputeRespawnTick(serverTick, ticksPerSecond, respawnDelaySeconds);
        var s = slots[slotIndex];
        s.Alive = false;
        s.RespawnAtTick = respawnAt;
        slots[slotIndex] = s;
        return respawnAt;
    }

    /// <summary>
    /// 标记槽位已生成：写入实体 ID 并置为存活（宿主在生成成功后调用）。
    /// </summary>
    public static bool MarkSlotAlive(Span<SpawnSlotState> slots, int slotIndex, ulong entityId)
    {
        if (slotIndex < 0 || slotIndex >= slots.Length) return false;

        var s = slots[slotIndex];
        s.EntityId = entityId;
        s.Alive = true;
        s.RespawnAtTick = 0;
        slots[slotIndex] = s;
        return true;
    }

    /// <summary>
    /// 初始化槽位数组（宿主在 shard 激活时调用）。
    /// </summary>
    public static SpawnSlotState[] CreateSlots(int count)
    {
        if (count <= 0) return Array.Empty<SpawnSlotState>();
        return new SpawnSlotState[count];
    }

    /// <summary>当前存活数量（供 <c>MaxAlive</c> 监控与压测断言）。</summary>
    public static int CountAlive(ReadOnlySpan<SpawnSlotState> slots)
    {
        var n = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].Alive) n++;
        }
        return n;
    }
}
