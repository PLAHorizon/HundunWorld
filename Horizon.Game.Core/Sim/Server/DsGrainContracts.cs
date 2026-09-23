using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 掉落表配置快照（内核侧结构，MMORPG 开发计划 M4.3 收尾）。
/// <para>
/// 刻意与 Orleans 的 <c>LootTableConfig</c> 分离：内核（<c>Horizon.Game.Core</c> 的实时层）
/// 不应直接消费 Orleans 序列化契约对象——否则 Orleans 侧字段调整会静默改变 DS 的掉落行为。
/// 转换发生在适配器里，是一处显式的、可审查的边界。
/// </para>
/// </summary>
public sealed class LootTableSnapshot
{
    /// <summary>掉落表 ID。</summary>
    public int TableId { get; set; }

    /// <summary>掉落条目。</summary>
    public LootEntrySnapshot[] Entries { get; set; } = Array.Empty<LootEntrySnapshot>();
}

/// <summary>掉落条目快照。</summary>
public struct LootEntrySnapshot
{
    /// <summary>物品 ID。</summary>
    public int ItemId;

    /// <summary>掉落概率 [0,1]。</summary>
    public float DropRate;

    /// <summary>最小数量。</summary>
    public int MinCount;

    /// <summary>最大数量。</summary>
    public int MaxCount;

    /// <summary>稀有度（1-5，5 最稀有）。</summary>
    public int Rarity;
}

/// <summary>
/// 掉落表配置来源（Orleans 实现走 <c>ILootTableGrain.GetConfigAsync</c>；测试用假实现）。
/// <para>
/// <b>为什么是"取配置"而不是"请服务器掷骰"</b>：既有 <c>ILootTableGrain.RollLootAsync</c>
/// 是在 grain 内掷骰的，若 DS 在每次击杀时同步调用它，会有两个致命后果：
/// <list type="number">
///   <item><b>破坏确定性</b>——掷骰发生在 grain 状态里，同一场战斗在 Orleans 权威与 DS 权威下结果不同，
///         金轨迹对拍与重放申诉全部失效；</item>
///   <item><b>吃掉 tick 预算</b>——`IDsLootProvider.Roll` 是**同步**方法、在 DS 的 tick 线程上被调用，
///         一次跨进程 RPC 就是毫秒级阻塞，群怪同时死亡时会直接造成 tick 堆积
///         （正是既有深度分析报告 §8.1 警告的"单 shard 模拟瓶颈"）。</item>
/// </list>
/// 因此本接口只负责把**静态配置**取到 DS 侧缓存，掷骰在 DS 内以确定性方式本地完成——
/// 这也是真实 MMORPG 的常规做法（掉落表是配置，不是每次击杀的 RPC）。
/// </para>
/// </summary>
public interface ILootTableConfigSource
{
    /// <summary>取得掉落表配置；不存在时返回 null。</summary>
    Task<LootTableSnapshot?> GetLootTableAsync(int lootTableId, CancellationToken cancellationToken = default);
}

/// <summary>怪物模板 → 掉落表 ID 的映射。</summary>
public interface IMonsterLootMapping
{
    /// <summary>解析怪物模板对应的掉落表 ID；返回 0 表示该模板无掉落表。</summary>
    int ResolveLootTableId(int monsterTemplateId);
}

/// <summary>
/// 默认映射：模板 ID 即掉落表 ID。
/// <para>这是"能跑通"的最小约定；生产应改为读配置表（模板 → 掉落表）以免两者耦合。</para>
/// </summary>
public sealed class IdentityMonsterLootMapping : IMonsterLootMapping
{
    /// <inheritdoc />
    public int ResolveLootTableId(int monsterTemplateId) => monsterTemplateId > 0 ? monsterTemplateId : 0;
}

/// <summary>
/// 背包写入端（Orleans 实现走 <c>IInventoryGrain.AddItemAsync</c>）。
/// <para>
/// 以 <see cref="Guid"/> 作玩家键，是为了对齐既有 <c>IInventoryGrain : IGrainWithGuidKey</c>；
/// ulong 角色 ID 到 Guid 的换算集中在 <see cref="GrainKeyMapper"/>，避免各处各写一套。
/// </para>
/// </summary>
public interface IInventoryWriter
{
    /// <summary>给玩家加物品；返回是否成功。</summary>
    Task<bool> AddItemAsync(Guid playerId, int templateId, int quantity, int quality, CancellationToken cancellationToken = default);
}

/// <summary>
/// 任务击杀进度写入端（Orleans 实现走玩家维度 <c>IQuestGrain</c>）。
/// </summary>
public interface IQuestProgressWriter
{
    /// <summary>
    /// 推进某角色针对某怪物模板的击杀类任务目标。
    /// </summary>
    /// <returns>实际被推进的目标数量（0 表示该玩家没有匹配的进行中任务）。</returns>
    Task<int> AdvanceKillObjectiveAsync(ulong characterId, int monsterTemplateId, int count, CancellationToken cancellationToken = default);
}

/// <summary>
/// 任务目标匹配器：判断某个任务目标是否应当被"击杀某怪物"推进。
/// <para>
/// <b>为什么需要它</b>：既有 <c>QuestObjectiveData</c> 只有字符串 <c>ObjectiveType</c> 与
/// <c>Description</c>，**没有数值型目标 ID**，因此无法可靠地判断"这个目标是杀这只怪"。
/// 本接口把这一判断显式外化，使：
/// <list type="bullet">
///   <item>匹配策略可替换、可测试；</item>
///   <item>"数据模型缺数值目标 ID"这件事在代码里可见，而不是被硬编码的字符串匹配掩盖。</item>
/// </list>
/// 后续应由任务系统在 <c>QuestObjectiveData</c> 上补一个数值 <c>TargetId</c>，
/// 届时换上精确匹配实现即可，本接口与调用方无需改动。
/// </para>
/// </summary>
public interface IQuestKillObjectiveMatcher
{
    /// <summary>该目标是否应被击杀事件推进。</summary>
    /// <param name="objectiveType">目标类型字符串（既有模型）。</param>
    /// <param name="description">目标描述（既有模型）。</param>
    /// <param name="monsterTemplateId">被击杀的怪物模板 ID。</param>
    bool ShouldAdvance(string objectiveType, string description, int monsterTemplateId);
}

/// <summary>
/// 默认匹配器：按目标类型字符串识别"击杀类"目标。
/// <para>
/// <b>已知局限</b>：它只能判断"这是个击杀目标"，**无法区分杀的是哪种怪**——
/// 因为既有数据模型没有承载该信息。因此当前实现会把任意击杀都推进所有击杀类目标。
/// 这在单一怪种的任务里是正确的，在"杀 5 只狼 + 杀 3 只熊"这种任务里会串。
/// 已在 <see cref="IQuestKillObjectiveMatcher"/> 的注释中登记为待补数据模型项。
/// </para>
/// </summary>
public sealed class KillObjectiveTypeMatcher : IQuestKillObjectiveMatcher
{
    private static readonly string[] KillTokens = { "kill", "击杀", "杀", "hunt", "狩猎", "讨伐" };

    /// <inheritdoc />
    public bool ShouldAdvance(string objectiveType, string description, int monsterTemplateId)
    {
        if (monsterTemplateId <= 0) return false;

        var type = objectiveType ?? string.Empty;
        for (var i = 0; i < KillTokens.Length; i++)
        {
            if (type.Contains(KillTokens[i], StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}

/// <summary>
/// 角色 ID ↔ Orleans Grain 键 的换算（单一来源，避免各处各写一套）。
/// </summary>
public static class GrainKeyMapper
{
    /// <summary>
    /// 把 <c>ulong</c> 角色 ID 映射为 <c>IInventoryGrain</c> 所需的 <see cref="Guid"/>。
    /// <para>
    /// 方案：Guid 的高 8 字节为 0，低 8 字节按**大端**写入角色 ID。
    /// 选择该方案的三个理由：① 保持原数值的字典序（便于范围扫描/排障时肉眼比对）；
    /// ② 不引入哈希，无碰撞；③ 双向可逆（见 <see cref="ToCharacterId"/>）。
    /// </para>
    /// <para>
    /// <b>部署约束</b>：该映射假设角色 ID 在集群内唯一。若将来引入"分服自增 ID"，
    /// 必须改为显式的全局映射表（或在 ID 高位编入 shard），否则不同服的同号角色会共用一个背包。
    /// </para>
    /// </summary>
    public static Guid ToInventoryKey(ulong characterId)
    {
        Span<byte> bytes = stackalloc byte[16];
        // 高 8 字节保持 0（bytes[0..7] 已为零）
        bytes[8] = (byte)(characterId >> 56);
        bytes[9] = (byte)(characterId >> 48);
        bytes[10] = (byte)(characterId >> 40);
        bytes[11] = (byte)(characterId >> 32);
        bytes[12] = (byte)(characterId >> 24);
        bytes[13] = (byte)(characterId >> 16);
        bytes[14] = (byte)(characterId >> 8);
        bytes[15] = (byte)characterId;
        return new Guid(bytes);
    }

    /// <summary>把 <see cref="ToInventoryKey"/> 产出（或任意 Guid）还原为角色 ID。</summary>
    public static ulong ToCharacterId(Guid key)
    {
        Span<byte> bytes = stackalloc byte[16];
        key.TryWriteBytes(bytes);
        ulong value = 0;
        for (var i = 8; i < 16; i++)
        {
            value = (value << 8) | bytes[i];
        }
        return value;
    }
}
