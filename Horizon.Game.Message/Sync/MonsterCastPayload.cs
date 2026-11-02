using MemoryPack;
using Orleans;
using System;

namespace Horizon.Game.Message.Sync;

/// <summary>
/// 怪物吟唱结束的出口（<see cref="SyncEventKind.MonsterCastStop"/> 的 <c>IntValue</c>）。
/// <para>
/// <b>为什么值要写在这里而不是两侧各写数字</b>：客户端要按原因决定"条子是走完、还是被打断后闪一下"，
/// 服务端要按原因发事件。两处各写一份字面量的话，新增一个出口（比如"怪被放逐"）只会有一侧知道，
/// 而漏掉的那侧不会报错——它只是把新原因当成 0（完成）来表现。
/// </para>
/// <para>
/// <b>为什么这里没有"施法者死亡/消失"那一档</b>：那条路走的是 <c>Despawn</c> **全广播**，
/// 客户端连同实体一起销毁，头顶条子是它的子控件、跟着没了；给"已经看不见这只怪"的人补发 Stop
/// 既没必要，也会把原因表撑出一个没人写的死值。
/// </para>
/// </summary>
public static class MonsterCastEndReason
{
    /// <summary>吟唱到点，技能已结算。</summary>
    public const int Completed = 0;

    /// <summary>被打断（客户端应当让条子"回退并闪断"，与完成区分开）。</summary>
    public const int Interrupted = 1;

    /// <summary>目标失效/施法条件不成立而取消（例如玩家跑出射程、脱战回位）。</summary>
    public const int Cancelled = 2;
}

/// <summary>
/// 伤害系别位（<c>Game_HunduShijie_MonsterSkillMount.school_mask</c> 与
/// <see cref="MonsterCastPayload.SchoolMask"/> 的**唯一口径**）。
/// <para>
/// <b>为什么要在这里把位定义写死</b>：这个字段从编辑器到库、从库到施法事件一路是裸 int，
/// 而"分色"是玩家分辨"这怪要喷火还是要放冰"的唯一依据。位序散落在注释里（"金木水火土 + 物理"）
/// 迟早会让写侧和读侧各猜一套——猜错的后果是颜色整体错位，而**没有任何一层会报错**。
/// 现在两侧都从这里取，五行位号还顺带与全工程的域号（1金 2木 3水 4火 5土）对齐：
/// 位号 == 域号，物理单独占 bit0。
/// </para>
/// </summary>
public static class MonsterSchoolBits
{
    /// <summary>物理（不走五行相生相克）。</summary>
    public const int Physical = 1 << 0;

    /// <summary>金（域 1）。</summary>
    public const int Metal = 1 << 1;

    /// <summary>木（域 2）。</summary>
    public const int Wood = 1 << 2;

    /// <summary>水（域 3）。</summary>
    public const int Water = 1 << 3;

    /// <summary>火（域 4）。</summary>
    public const int Fire = 1 << 4;

    /// <summary>土（域 5）。</summary>
    public const int Earth = 1 << 5;

    /// <summary>合法掩码上限（六个位全开）。</summary>
    public const int AllMask = Physical | Metal | Wood | Water | Fire | Earth;

    /// <summary>
    /// 取该掩码里**最低**的那个五行位对应的域号（1金..5土）；没有五行位（纯物理或 0）返回 0。
    /// <para>客户端一根条子只有一个颜色，多系技能取最低位是刻意的稳定规则——
    /// 用"随机挑一个"或"最后写入的那个"都会让同一只怪的条子颜色跳。</para>
    /// </summary>
    public static int FirstDomainOf(int schoolMask)
    {
        for (var domain = 1; domain <= 5; domain++)
        {
            if ((schoolMask & (1 << domain)) != 0) return domain;
        }
        return 0;
    }
}

/// <summary>
/// 怪物起手载荷（<see cref="SyncEventKind.MonsterCastStart"/> 的二次序列化内容）。
/// <para>
/// 总秒数与目标刻意放在 <see cref="SyncEvent"/> 的现成字段里（<c>FloatValue</c>/<c>TargetEntityId</c>），
/// 这里只带"条子长什么样"的那几个位：系别决定颜色、可打断决定提示、可否移动决定条子期间施法者会不会挪窝。
/// 这三项都来自怪物编辑器的技能挂载行，是**策划配的东西**，客户端不该自己猜。
/// </para>
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial class MonsterCastPayload
{
    /// <summary>伤害系别位——位序见 <see cref="MonsterSchoolBits"/>（客户端据此给施法条分色）。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public int SchoolMask { get; set; }

    /// <summary>可打断：客户端据此显示"可打断"标记（没有打断技能时它仍然只是提示）。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public bool Interruptible { get; set; } = true;

    /// <summary>移动施法：false 时客户端可以把"施法者站定"当成读条的视觉确认。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public bool AllowMovement { get; set; }

    /// <summary>最大目标数（&gt;1 时客户端知道这是群体技能，条子上标 AOE）。</summary>
    [MemoryPackOrder(3)]
    [Id(3)]
    public int TargetCount { get; set; } = 1;

    /// <summary>AOE 半径（米；0 = 非范围技能）。客户端画地面圈是后续的事，先把数据带到。</summary>
    [MemoryPackOrder(4)]
    [Id(4)]
    public float AoeRadius { get; set; }
}
