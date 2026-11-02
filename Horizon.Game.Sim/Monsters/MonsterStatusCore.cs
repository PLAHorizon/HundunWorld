using System;

namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// 控制状态的号表（D28 选 a：**代码枚举，零新表、零迁移**）。
/// <para>为什么号住在代码里而不是库里：<c>status_id</c> 那两格今天填的是"随便一个整数"，
/// 校验器只能 Warn"没人读"。要让它变成能被引用检查的键，先要有**一份**权威清单；
/// 建一张表意味着 实体+迁移+Designer+Snapshot+编辑器面板+快照 DTO 六件套，
/// 而这份清单的内容（"这一档封不封移动 / 封不封施法"）本来就是一段代码判据，不是数据。</para>
/// <para><b>与 AC 的偏离要说明白</b>：AC 里"这式技能附带什么状态"是**技能的属性**
/// （<c>Spell::DoSpellHitOnUnit → Aura::TryRefreshStackOrCreate → EffectApplyAura</c>，
/// <c>Spell.cpp:2925/:3080</c>、<c>SpellEffects.cpp:1308</c>），
/// 而本作的 <c>status_id</c> 挂在**怪物的技能挂载行**上——这是自家设计（见方案文档 §2.5）。
/// 号表与 <c>WuxingStatusEffects.StatusKind</c> 共用同一个**词表**（Stun/Root/Slow 三档同名；
/// <c>Silence</c> 是**单边**的——玩家技能那一族今天没有沉默这一档，所以沉默只能由怪物侧施加。
/// 这条单边由用例钉成显式清单，见 <c>控制状态的词表与五行技能那一族同名而编号各归各</c>），
/// 但编号是本枚举自己定的（那边是隐式顺序，改它的顺序就会悄悄换掉所有玩家技能的含义，
/// 所以这里显式赋值，两侧靠闸门用例钉住"同一档同名"，见 <c>MonsterEditorContractTests</c>）。</para>
/// </summary>
public enum MonsterStatusKind : byte
{
    /// <summary>0 = 没配状态。与"不填"同形，所以这一档不许有任何闸口。</summary>
    None = 0,

    /// <summary>眩晕：封移动 + 封施法 + 施加即打断在读的那条。</summary>
    Stun = 1,

    /// <summary>沉默：封施法，**不**封移动。</summary>
    Silence = 2,

    /// <summary>定身：封移动，**不**封施法。</summary>
    Root = 3,

    /// <summary>
    /// 减速：数值类，不是闸口类。
    /// <para>这一档今天**声明了但没有幅度来源**——挂载上只有
    /// <c>status_id</c> 与 <c>status_duration_seconds</c> 两格，没有"减多少"那一格，
    /// 所以随便取一个 50% 就是把猜测包装成配置。落地条件见方案文档 §11.4 与 D30a 的注记：
    /// 要么加一列（迁移，等点头），要么改读技能模板自带的幅度（D28c 那条路）。
    /// 在那之前它**不进任何闸口**，而校验器/audit 会把"配了减速"念出来。</para>
    /// </summary>
    Slow = 4,
}

/// <summary>一条挂在实体身上的状态实例（到期时刻按服务器 tick 存，与嘲讽/免疫窗同一把尺）。</summary>
public struct MonsterStatusEntry
{
    /// <summary>哪一档；<see cref="MonsterStatusKind.None"/> 表示这一格空着。</summary>
    public MonsterStatusKind Kind;

    /// <summary>
    /// 到期 tick（<c>ServerTick &gt;= ExpireAtTick</c> 即回收）。
    /// <para>AC 存的是"剩余时长"每帧减（<c>Aura::Update</c> <c>SpellAuras.cpp:740</c>
    /// 只做 <c>m_duration -= diff</c>），回收在 <c>Unit::_UpdateSpells</c>（<c>Unit.cpp:3885-3900</c>）。
    /// 本作整个 AI 都是 tick 驱动、且金轨迹要求"同 tick 序列 → 同结果"，
    /// 所以存**绝对到期时刻**而不是累减剩余量：累减会把浮点 dt 的舍入误差积进时长。</para>
    /// </summary>
    public long ExpireAtTick;

    /// <summary>
    /// 强度（基点，10000 = 100%）。今天只有 <see cref="MonsterStatusKind.Slow"/> 用它：
    /// 5000 = 移动速度打五折。<b>0 = 没有强度来源 ⇒ 这一档按"不减"处理</b>——
    /// 随手写一个 50% 就是把猜测包装成配置，所以"没填"必须与"填了 0"同形且都不生效。
    /// <para>AC 那边减速是每一式技能自带的比例（`SPELL_AURA_MOD_DECREASE_SPEED` 的 effect 值），
    /// 本作把它放在技能挂载那一行（用户 2026-10-28 拍板"加一列"）。</para>
    /// </summary>
    public int MagnitudeBps;
}

/// <summary>
/// 控制状态内核：施加 / 到期回收 / 两条独立闸口 / 施加即打断。
/// <para><b>纯函数</b>：不读内容包、不碰世界、不分配堆（表由调用方带进来），
/// 与 <c>MonsterThreatCore</c>/<c>MonsterPatrolCore</c> 同一族，金轨迹可以直接重放。</para>
/// </summary>
public static class MonsterStatusCore
{
    /// <summary>
    /// 一个实体最多同时挂几条状态。
    /// <para>这一格的**唯一约束**是"等于可挂的档位数"（一期是 Stun/Silence/Root/Slow 四档）：
    /// 同档只刷新不叠条目（见 <see cref="Apply"/>），所以表永远装不满到需要挑选丢弃的程度。
    /// 有一条用例专门钉住这两个数相等（<c>状态表填满之后同档只刷新而条目数不变</c>）——
    /// 将来加第五档而忘了同步这一格，那条会红，而不是让"满表"这一支变成没人知道的死路。</para>
    /// </summary>
    public const int MaxEntries = 4;

    /// <summary>版本锚点：这一族判据变了要涨它（与 <c>MonsterAiRuntime.Version</c> 同一条纪律）。</summary>
    public const int Version = 1;

    /// <summary>
    /// 这一档封不封**移动**。
    /// <para>逆向依据：AC 的移动判据是 <c>Unit::IsImmobilizedState() = HasUnitState(UNIT_STATE_ROOT|UNIT_STATE_STUNNED)</c>
    /// （<c>Unit.h:775</c>），而 <c>SetStunned</c> 内部会 <c>SetRooted(true,true)</c>
    /// （<c>Unit.cpp:18418 → :18464-18497</c>）⇒ **眩晕必然带着定身**，两条 flag 走同一道闸口。</para>
    /// </summary>
    public static bool BlocksMovement(MonsterStatusKind kind) =>
        kind == MonsterStatusKind.Stun || kind == MonsterStatusKind.Root;

    /// <summary>
    /// 这一档封不封**施法**（不许起手）。
    /// <para>逆向依据：<c>Spell::CheckCasterAuras</c>（<c>Spell.cpp:6820</c>）见
    /// <c>UNIT_FLAG_STUNNED</c> → <c>SPELL_FAILED_STUNNED</c>（:6863-6891）、
    /// 见 <c>UNIT_FLAG_SILENCED</c> → <c>SPELL_FAILED_SILENCED</c>；
    /// 两者是**两个独立 flag**，都不封"平A"。</para>
    /// <para><b>AC 里眩晕与沉默都不阻止自动近战</b>（<c>Unit::AttackerStateUpdate</c>
    /// <c>Unit.cpp:2601-2606</c> 只看 <c>UNIT_FLAG_PACIFIED</c>），所以这一族**没有**"被封出手"这一档——
    /// 别把眩晕做成"什么都不做"，那不是 WoW 的表现。</para>
    /// </summary>
    public static bool BlocksCasting(MonsterStatusKind kind) =>
        kind == MonsterStatusKind.Stun || kind == MonsterStatusKind.Silence;

    /// <summary>
    /// 施加这一档时，要不要**当场打断在读的那条施法**。
    /// <para>眩晕的依据是字面的：<c>Unit::SetStunned</c> 在 <c>Unit.cpp:18435</c> 直接
    /// <c>CastStop()</c>（"读条被晕就是被打断"）。</para>
    /// <para><b>沉默这一半是刻意退化</b>：AC 只打断
    /// <c>PreventionType == SPELL_PREVENTION_TYPE_SILENCE</c> 的那一条（闸口 <c>Spell.cpp:6900</c>），
    /// 而本作没有任何一格记"这条施法的预防类型"——技能模板里没这列，怪物挂载里也没这列。
    /// 两条出路：① 按 AC 的字面形状做"沉默不断条"，那是**比 AC 更弱**的控制（沉默变成纯禁止起手，
    /// 玩家会看到怪被沉默了却把条读完）；② 加一列（迁移，等点头）。
    /// 这里先取"也打断"（对玩家更直观、也与本作的"踢一脚打断"同形），
    /// 并在方案文档 §11.4 留一条**已知与 AC 不同形**的登记，不静默。</para>
    /// </summary>
    public static bool InterruptsCastOnApply(MonsterStatusKind kind) =>
        kind == MonsterStatusKind.Stun || kind == MonsterStatusKind.Silence;

    /// <summary>这一档是不是"有内容的档"（0 = 没配，闸口一律不许因为 0 而生效）。</summary>
    public static bool IsUsable(MonsterStatusKind kind) =>
        kind != MonsterStatusKind.None;

    /// <summary>
    /// 把内容里那一格整数号翻成枚举（唯一翻译口）。
    /// <para>解析要**严格**：不认识的号必须回 false 而不是落成 <c>None</c>——
    /// "配了 7 而 7 不存在"被静默当成"没配"，就是这一族最贵的坏形状
    /// （校验器念不出来、运行期也不报，玩家只觉得'这怪明明配了控却从不控人'）。
    /// 越界与 0 都算不认识。</para>
    /// </summary>
    public static bool TryFromId(int statusId, out MonsterStatusKind kind)
    {
        kind = MonsterStatusKind.None;
        if (statusId <= 0 || statusId > byte.MaxValue) return false;
        var candidate = (MonsterStatusKind)(byte)statusId;
        if (!Enum.IsDefined(typeof(MonsterStatusKind), candidate)) return false;
        kind = candidate;
        return IsUsable(kind);
    }

    /// <summary>
    /// 施加一条状态，并回答"要不要当场打断在读的那条施法"。
    /// <para>同档重复施加 = **刷新到期时刻**（取较晚的那个），不新增条目——
    /// AC 走 <c>Aura::TryRefreshStackOrCreate</c>（<c>Spell.cpp:3080</c>）也是"刷新或创建"，
    /// 而我们没有叠层（<c>MaxAffected</c>/<c>stackAmount</c>，D31a 明写不做）。
    /// 表按档位数定长，所以"满表还要再加一种新档"这件事结构上不会发生
    /// （见 <see cref="MaxEntries"/> 与那条钉"两个数相等"的用例）。</para>
    /// </summary>
    /// <param name="entries">调用方带来的定长表（长度必须 = <see cref="MaxEntries"/>）。</param>
    /// <param name="count">表内有效条目数（就地改）。</param>
    /// <param name="kind">哪一档；<see cref="MonsterStatusKind.None"/> 直接拒绝——"没配"不许变成一条永久的锁。</param>
    /// <param name="expireAtTick">到期 tick。</param>
    /// <param name="nowTick">当前 tick（用来拒绝"施加时已经过期"这种输入）。</param>
    /// <returns>真被打断才回 true；被拒绝（None / 已过期）回 false。</returns>
    public static bool Apply(
        MonsterStatusEntry[] entries, ref int count,
        MonsterStatusKind kind, long expireAtTick, long nowTick, int magnitudeBps = 0)
    {
        if (entries == null || !IsUsable(kind) || expireAtTick <= nowTick) return false;

        for (var i = 0; i < count; i++)
        {
            if (entries[i].Kind != kind) continue;
            // 刷新：只把到期时刻往后推，不许缩短（AC 的 refresh 同样不惩罚"先被减速再被减速"）。
            if (expireAtTick > entries[i].ExpireAtTick) entries[i].ExpireAtTick = expireAtTick;
            // 强度同样只取更强的那一份：第二次施加更弱的一条不该把玩家"救"回来。
            if (magnitudeBps > entries[i].MagnitudeBps) entries[i].MagnitudeBps = magnitudeBps;
            return InterruptsCastOnApply(kind);
        }

        if (count >= entries.Length) return false;   // 见 <see cref="MaxEntries"/>：这条只在"加档位忘了同步"时才会走到
        entries[count] = new MonsterStatusEntry
        {
            Kind = kind,
            ExpireAtTick = expireAtTick,
            MagnitudeBps = magnitudeBps < 0 ? 0 : magnitudeBps,
        };
        count++;
        return InterruptsCastOnApply(kind);
    }

    /// <summary>
    /// 减速强度上限（9000 = 最多减到 10% 速度）。
    /// <para>为什么不让它到 10000：那等于把怪钉住，而"钉住"是定身那一档的语义——
    /// 两个不同的档给出同一个结果，玩家分不清自己是"被减速减到 0"还是"被定身"，
    /// 客户端那排状态图标也就没法说实话。真要完全不动，配定身。</para>
    /// </summary>
    public const int MaxSlowMagnitudeBps = 9000;

    /// <summary>
    /// 这一拍的移动速度缩放（1 = 不减）。
    /// <para>多条减速取**最强**那一条而不是相乘：AC 的移动速度是"在若干条减速里取最狠的那条"
    /// （`Unit::UpdateSpeed` 的取最小语义），相乘会让两条 50% 变成 25%——那是本作凭空发明的叠加规则。</para>
    /// <para>强度 0 = 没有幅度来源 ⇒ 不生效（见 <see cref="MonsterStatusEntry.MagnitudeBps"/>）。</para>
    /// </summary>
    public static float SpeedScale(MonsterStatusEntry[] entries, int count)
    {
        var worst = 0;
        for (var i = 0; i < count; i++)
        {
            if (entries[i].Kind != MonsterStatusKind.Slow) continue;
            var mag = entries[i].MagnitudeBps;
            if (mag > worst) worst = mag;
        }
        if (worst <= 0) return 1f;
        if (worst > MaxSlowMagnitudeBps) worst = MaxSlowMagnitudeBps;
        return 1f - worst / 10000f;
    }

    /// <summary>
    /// 到期回收（AC 的 <c>_UpdateSpells</c> 那一拍：扫 <c>IsExpired()</c> → remove）。
    /// <para>单点回收：只有这里删条目。任何"解除/驱散"的通路将来也必须走这一句，
    /// 否则会长出第二份"还剩几条"。</para>
    /// </summary>
    /// <returns>本拍回收掉几条（宿主要不要念给它自己决定；用例用这一格证明"回收真跑过"）。</returns>
    public static int Expire(MonsterStatusEntry[] entries, ref int count, long nowTick)
    {
        if (entries == null || count == 0) return 0;
        var removed = 0;
        var write = 0;
        for (var read = 0; read < count; read++)
        {
            if (entries[read].ExpireAtTick <= nowTick)
            {
                entries[read] = default;
                removed++;
                continue;
            }
            if (write != read) entries[write] = entries[read];
            write++;
        }
        // 压实之后尾部必须清空：留着旧副本会让"这一档还在不在"取决于读到哪一格。
        for (var i = write; i < count; i++) entries[i] = default;
        count = write;
        return removed;
    }

    /// <summary>这一拍身上有没有"封移动"的状态（表里逐条问同一个主人 <see cref="BlocksMovement"/>）。</summary>
    public static bool BlocksMovement(MonsterStatusEntry[] entries, int count)
    {
        for (var i = 0; i < count; i++)
            if (BlocksMovement(entries[i].Kind)) return true;
        return false;
    }

    /// <summary>这一拍身上有没有"封施法"的状态。</summary>
    public static bool BlocksCasting(MonsterStatusEntry[] entries, int count)
    {
        for (var i = 0; i < count; i++)
            if (BlocksCasting(entries[i].Kind)) return true;
        return false;
    }

    /// <summary>身上有没有这一档（用例与 audit 都用它，不另写一遍遍历）。</summary>
    public static bool Has(MonsterStatusEntry[] entries, int count, MonsterStatusKind kind)
    {
        for (var i = 0; i < count; i++)
            if (entries[i].Kind == kind) return true;
        return false;
    }

    /// <summary>还有多少条（到期回收之后剩下的）。</summary>
    public static int ActiveCount(MonsterStatusEntry[] entries, int count, long nowTick)
    {
        var alive = 0;
        for (var i = 0; i < count; i++)
            if (entries[i].Kind != MonsterStatusKind.None && entries[i].ExpireAtTick > nowTick) alive++;
        return alive;
    }
}
