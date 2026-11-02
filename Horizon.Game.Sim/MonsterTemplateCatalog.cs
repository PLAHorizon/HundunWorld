namespace Horizon.Game.Sim;

/// <summary>
/// 怪物模板的展示与数值信息（名称 / 等级 / 模型键）。
/// </summary>
/// <param name="TemplateId">模板 ID，与 <see cref="SpawnPointConfig.MonsterTemplateId"/> 对应。</param>
/// <param name="Name">显示名，随 Spawn 的 Identity 下发给客户端头顶信息卡。不得为空——空名会让客户端回退成 "Monster_&lt;id&gt;"。</param>
/// <param name="Level">显示等级。与 <see cref="SpawnPointConfig.MaxHp"/> 相互独立（等级只用于展示与后续战力压制计算）。</param>
/// <param name="ModelKey">客户端模型键；当前客户端尚未接入怪物美术资源，仅作为后续分流的稳定标识。</param>
/// <param name="WuxingDomain">
/// 该生物所属的五行域（1金 2木 3水 4火 5土），0 = 未定域。
/// <para>
/// 这是"怪 → 域"的唯一事实源，同时被三处消费：刷怪掉落的表 ID、击杀累积的分域好感、
/// 以及任务击杀目标的判定。<b>未定域不是"随便给个域"</b>——它意味着这只怪什么都不掉、
/// 也不加任何域的好感，因为"金域信物从火域怪身上掉"在日志里没有任何痕迹，只能靠漏配显形。
/// </para>
/// </param>
/// <param name="IsWorldBoss">
/// 世界 BOSS 标记：掉落走**另一张表**（表 ID = <c>100 + 域</c>，见 <c>WuxingLootTables</c>），
/// 里面装的是该域天阶三式。
/// <para>
/// 为什么必须是独立一档而不是"把天阶书也塞进域表"：设计上天阶只从世界 BOSS 出（一张就是学问，
/// 不该被小怪刷出来）；而道阶演化又要求天阶三境练满——所以这条标记是"超越天阶"那条路唯一的上游。
/// 小怪表里少了天阶是规则，BOSS 表接不上则是整条顶端断链，且断得很安静（BOSS 什么都不掉）。
/// </para>
/// </param>
public readonly record struct MonsterTemplateInfo(
    int TemplateId, string Name, int Level, string ModelKey, int WuxingDomain, bool IsWorldBoss);

/// <summary>
/// 怪物模板目录（MMORPG 开发计划 —— 为可选中实体提供"名字 + 等级"的事实源）。
/// <para>
/// <b>为什么是纯静态查表</b>：<c>SpawnPointConfig</c> 只携带数值（MaxHp/AttackPower/MoveSpeed），
/// 没有任何展示信息，导致下发给客户端的怪物 Identity 无名称、State 无等级，头顶信息卡无从显示。
/// 本目录补齐这一层，且保持 <c>Horizon.Game.Sim</c> 零存储/零框架依赖的既有约束
/// （同 <see cref="MonsterAiCore"/> / <see cref="SpawnDirectorCore"/> 的确定性纯函数纪律）。
/// </para>
/// <para>
/// <b>过渡性质</b>：生产环境应由 <c>GameConfigGrain</c> 下发表数据替换，届时只改
/// <see cref="Get"/> 的实现，调用点不动。未登记的 ID 返回等级 1、名称 <c>异兽#id</c>，
/// 保证新增刷怪点不会因为漏配模板而在客户端显示成空名。
/// </para>
/// </summary>
public static class MonsterTemplateCatalog
{
    /// <summary>
    /// 开发用默认模板：与 <c>Horizon.DsHost</c> 的 <c>MonsterTemplateId = 1 + (i % 5)</c> 对应。
    /// <para>
    /// 前三行是既有模板（id/名称/等级都不动——脚本生成白名单与头顶信息卡的显示断言都按它们写过），
    /// 只补上域号；后两行是为凑齐五行域新增的低等级兽。等级刻意错开：金域那一只最低（Lv8），
    /// 这样"新手刷两只就够读玄·初"落在最没有战斗压力的怪身上。
    /// </para>
    /// </summary>
    private static readonly MonsterTemplateInfo[] Templates =
    {
        new(1, "墨麒麟", 12, "mo_qilin", 3, false),
        new(2, "石甲狰", 18, "shi_jia_zheng", 5, false),
        new(3, "冥羽鸩", 26, "ming_yu_zhen", 4, false),
        new(4, "银爪狸", 8, "yin_zhua_li", 1, false),
        new(5, "青藤蟒", 21, "qing_teng_mang", 2, false),

        // 五行域各一只世界 BOSS，负责该域天阶三式的产出（等级贴着 §2.1 天阶 Lv201 往上）。
        // 它们不在 DsHost 的开发轮换循环里（1+(i%5) 只出普通怪）：BOSS 要靠脚本化生成/世界事件出现，
        // 也不该被默认刷怪点当普通怪刷出来。
        new(6, "锐金夜叉", 210, "boss_jin_ye_cha", 1, true),
        new(7, "青梧蛟", 215, "boss_qing_wu_jiao", 2, true),
        new(8, "玄溟螭", 220, "boss_xuan_ming_chi", 3, true),
        new(9, "赤炎鹏", 225, "boss_chi_yan_peng", 4, true),
        new(10, "厚岩魁", 230, "boss_hou_yan_kui", 5, true),

        // 无域的世界 BOSS：掉混沌终式（表 100 是"无域"那一档的总表）。
        // 没有这一只，90001 的产出就只写在设计里——它不在任何域表，也没有任何模板映射到表 100。
        new(11, "混沌遗蜕", 300, "boss_hundun_yitui", 0, true),
    };

    /// <summary>世界 BOSS 掉落表的 ID 起点：表 ID = <c>WorldBossTableBase + 五行域</c>（101..105）。</summary>
    /// <remarks>
    /// 与 <c>WuxingLootTables</c> 的装配口径必须一致，两处各持这个数会静默断链
    /// （BOSS 掉不出天阶，而道阶演化又练不满）；所以 Silo 侧直接引用本常量，
    /// 并由 <c>WuxingBossLootTests</c> 断言"表 ID 与目录口径同源"。
    /// </remarks>
    public const int WorldBossTableBase = 100;

    /// <summary>
    /// 该模板的掉落表 ID：<b>先看编辑器配的绑定</b>，没有才回退推导
    /// （普通怪 = 域号 1..5，世界 BOSS = <c>100 + 域号</c>，未定域 = 0 无掉落）。
    /// </summary>
    /// <remarks>
    /// <b>推导必须留在兜底位</b>：WoW 的观测是"一只怪可挂 N 张表"，所以绑定才是配置；
    /// 但"世界 BOSS → 100+域"这条顶端链今天靠推导成立（<see cref="WorldBossTableBase"/> 的注释警告过：
    /// 接不上时 BOSS 什么都不掉，且断得很安静）。改成"只认绑定"会让所有未配绑定的存量 BOSS 当场断链。
    /// </remarks>
    public static int LootTableIdOf(int templateId)
    {
        // 1) 配置优先：取第一个有效的绑定表号（顺序即绑定 id 升序，由装配器保证）。
        var bindings = Horizon.Game.Sim.Monsters.MonsterContent.LootBindings(templateId);
        for (var i = 0; i < bindings.Count; i++)
        {
            if (bindings[i].LootTableId > 0) return bindings[i].LootTableId;
        }

        // 2) 回退推导。
        return DeriveLootTableId(templateId);
    }

    /// <summary>
    /// 只看域与档位的<b>纯推导</b>表号（普通怪 = 域号 1..5，世界 BOSS = <c>100 + 域号</c>，
    /// 无域 BOSS = <c>100</c> 总表，未定域的普通怪 = 0 不掉）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="LootTableIdOf"/> 分开是有原因的：那个数**先看绑定**，所以拿它去问
    /// "这只怪的域该是哪张表"会读到刚刚写进去的那个号——问一次就把自己钉死了。
    /// 落库脚本（<c>Tools/author-mob-loot.py</c>）要的正是前者：把表号写成与域对齐的那个数，
    /// 而不是在 Python 里再抄一遍公式（抄一份就会与 <see cref="WorldBossTableBase"/> 静默分叉）。
    /// </remarks>
    public static int DeriveLootTableId(int templateId)
    {
        var row = Get(templateId);
        return DeriveLootTableId(row.WuxingDomain, row.IsWorldBoss);
    }

    /// <summary>
    /// 纯推导的另一个入口：<b>输入是域与档位，不是模板号</b>。
    /// <para>给"手上只有草稿、库里这只怪还没进代码目录也没装配内容"的调用方用的
    /// （CLI 的 preview-drop 就是：它读库里的 domain/rank，不读代码目录）。
    /// 没有这个入口，那些调用方只能自己抄一遍公式与"什么算 BOSS"——一抄就分叉。</para>
    /// </summary>
    public static int DeriveLootTableId(int wuxingDomain, bool isWorldBoss)
    {
        var domainInRange = wuxingDomain >= 1 && wuxingDomain <= 5;
        if (isWorldBoss)
        {
            // 世界 BOSS：有域走本域天阶表（100+域），无域走总表（100，混沌终式那一档）。
            return domainInRange ? WorldBossTableBase + wuxingDomain : WorldBossTableBase;
        }

        return domainInRange ? wuxingDomain : 0;
    }

    /// <summary>档位算不算"走 100+域那张表的首领"：<c>rank >= </c><see cref="MonsterRank.Boss"/>。</summary>
    /// <remarks>副本首领（3）与世界 BOSS（4）同属"另一张表"，所以是 `>= Boss` 而不是 `== WorldBoss`；
    /// 这个口径住在 <see cref="ToInfo"/>，落库脚本与 CLI 都从这里问，别各写一遍。</remarks>
    public static bool IsBossTableRank(int rank)
        => (Horizon.Game.Sim.Monsters.MonsterRank)rank >= Horizon.Game.Sim.Monsters.MonsterRank.Boss;

    /// <summary>取模板信息；未登记的 ID 走确定性兜底，不抛异常（刷怪点配置错误不应中断模拟）。</summary>
    /// <remarks>
    /// <b>先查装配内容，再退代码目录</b>：本目录是白名单授权、任务击杀目标、分域好感、掉落表号
    /// 四条通路的共同事实源。若只有 DS 读库、这里仍读硬编码，就会出现
    /// "编辑器发布了一只怪、游戏里能刷出来，但任务选不到它 / 脚本生成被白名单拒 / 它不掉任何东西"。
    /// 未装配（开发期、库里还没东西、单测）时行为与改动前<b>逐字相同</b>。
    /// </remarks>
    public static MonsterTemplateInfo Get(int templateId)
    {
        if (Horizon.Game.Sim.Monsters.MonsterContent.TryGetConfig(templateId, out var config) && config != null)
            return ToInfo(config);

        for (var i = 0; i < Templates.Length; i++)
        {
            if (Templates[i].TemplateId == templateId) return Templates[i];
        }

        // 兜底行的 WuxingDomain 留 0：漏配模板只该表现为"不掉不涨好感"，不该被猜成某个域。
        return new MonsterTemplateInfo(templateId, $"异兽#{templateId}", 1, $"monster_{templateId}", 0, false);
    }

    /// <summary>内容配置 → 本目录的展示结构（rank≥3 视同世界 BOSS：它们走另一张掉落表）。</summary>
    private static MonsterTemplateInfo ToInfo(Horizon.Game.Sim.Monsters.MonsterRuntimeConfig config)
        => new MonsterTemplateInfo(
            config.TemplateId,
            string.IsNullOrEmpty(config.Name) ? $"异兽#{config.TemplateId}" : config.Name,
            config.LevelBase > 0 ? config.LevelBase : 1,
            string.IsNullOrEmpty(config.ModelKey) ? $"monster_{config.TemplateId}" : config.ModelKey,
            config.WuxingDomain,
            IsBossTableRank((int)config.Rank));

    /// <summary>
    /// 该模板的五行域；未登记（或登记为未定域）返回 0。
    /// <para>与 <see cref="Get"/> 分开是因为掉落/好感要的是"确有其域"，而不是兜底出来的显示信息。</para>
    /// <para>同样先查装配内容：分域好感与掉落都以此为准，漏一步会让库里新配的怪"什么都不掉"。</para>
    /// </summary>
    public static int DomainOf(int templateId)
    {
        if (Horizon.Game.Sim.Monsters.MonsterContent.TryGetConfig(templateId, out var config) && config != null)
            return config.WuxingDomain;

        for (var i = 0; i < Templates.Length; i++)
        {
            if (Templates[i].TemplateId == templateId) return Templates[i].WuxingDomain;
        }

        return 0;
    }

    /// <summary>
    /// 已登记模板（内容对账与目录展示用）。
    /// <para>装配了内容包时以内容为准、并补上仍留在代码目录里的历史条目；
    /// 只返回硬编码会让"库里新增的怪"在任务选择器与白名单对账里凭空消失。</para>
    /// </summary>
    public static MonsterTemplateInfo[] RegisteredTemplates()
    {
        var content = Horizon.Game.Sim.Monsters.MonsterContent.RegisteredIds();
        if (content.Count == 0) return (MonsterTemplateInfo[])Templates.Clone();

        var merged = new System.Collections.Generic.List<MonsterTemplateInfo>(Templates.Length + content.Count);
        var seen = new System.Collections.Generic.HashSet<int>();
        foreach (var id in content)
        {
            if (seen.Add(id)) merged.Add(Get(id));
        }
        for (var i = 0; i < Templates.Length; i++)
        {
            if (seen.Add(Templates[i].TemplateId)) merged.Add(Templates[i]);
        }
        merged.Sort((a, b) => a.TemplateId.CompareTo(b.TemplateId));
        return merged.ToArray();
    }

    /// <summary>
    /// 取<b>已登记</b>模板；未登记返回 false。
    /// <para>
    /// 与 <see cref="Get"/> 的区别是本方法<b>不做兜底</b>：模拟层需要"漏配也能显示"，
    /// 而授权层需要"没登记就不允许生成"。两者混用会让白名单形同虚设，故分列。
    /// </para>
    /// <para>
    /// <b>这条也是脚本化生成的白名单闸门</b>（<c>ScriptAuthorityCore</c> 调它）。所以它必须查装配内容：
    /// 否则编辑器发布的新怪能被刷出来、却永远无法被脚本生成，而拒绝理由只有一句"未登记"。
    /// </para>
    /// </summary>
    public static bool TryGetRegistered(int templateId, out MonsterTemplateInfo info)
    {
        if (Horizon.Game.Sim.Monsters.MonsterContent.TryGetConfig(templateId, out var config) && config != null)
        {
            info = ToInfo(config);
            return true;
        }

        for (var i = 0; i < Templates.Length; i++)
        {
            if (Templates[i].TemplateId == templateId)
            {
                info = Templates[i];
                return true;
            }
        }

        info = default;
        return false;
    }

    /// <summary>按显示名或模型键查已登记模板（大小写不敏感）。装配内容优先。</summary>
    public static bool TryFindByName(string key, out MonsterTemplateInfo info)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            info = default;
            return false;
        }

        var rows = RegisteredTemplates();
        for (var i = 0; i < rows.Length; i++)
        {
            if (string.Equals(rows[i].Name, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rows[i].ModelKey, key, StringComparison.OrdinalIgnoreCase))
            {
                info = rows[i];
                return true;
            }
        }

        info = default;
        return false;
    }
}
