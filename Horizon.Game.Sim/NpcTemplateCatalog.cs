namespace Horizon.Game.Sim;

using System.Collections.Generic;
using Horizon.Game.Sim.Npcs;

/// <summary>
/// NPC 模板的展示信息（名称 / 头衔 / 模型键）。与 <see cref="MonsterTemplateInfo"/> 分列：
/// NPC 没有等级与战斗数值，把两者塞进同一张表会让"NPC 能不能被打"这类问题变得含糊。
/// </summary>
/// <param name="TemplateId">模板 ID，与脚本生成点登记的 <c>MonsterTemplateId</c> 共用一个整数空间但按类别解析。</param>
/// <param name="Name">显示名。空名会让客户端回退成占位符，故目录内禁止空名。</param>
/// <param name="Title">头顶头衔（"铁匠"/"守门人"），随 Spawn 的 Identity 下发。</param>
/// <param name="ModelKey">客户端模型键；当前客户端尚未接入 NPC 美术资源，仅作为稳定分流标识。</param>
/// <param name="WuxingDomain">该 NPC 所属的五行域（1金 2木 3水 4火 5土）。
/// <b>刻意不给默认值</b>：给 0 之后，新登记的 NPC 忘记写域也能编过，症状是"和这个 NPC 说话永远不涨好感"，
/// 且不报错、不显示——好感是 §14 玄阶门槛的唯一动态来源之一，静默为零等于悄悄砍掉一条可玩路径。
/// 未登记 NPC 走 <see cref="Get"/> 的兜底分支时域为 0，含义是"不记账"（fail closed）。</param>
public readonly record struct NpcTemplateInfo(int TemplateId, string Name, string Title, string ModelKey, int WuxingDomain);

/// <summary>
/// NPC 模板目录（脚本化生成的白名单来源之一）。
/// <para>
/// <b>为什么同样是纯静态查表</b>：与 <see cref="MonsterTemplateCatalog"/> 一致，保持
/// <c>Horizon.Game.Sim</c> 零存储/零框架依赖的纪律；生产环境由配置表下发表数据替换，
/// 只改实现不改调用点。
/// </para>
/// <para>
/// <b>白名单语义</b>：<see cref="TryGetRegistered"/> / <see cref="TryFindByName"/> 未登记时返回 false，
/// 授权侧据此拒绝——<see cref="Get"/> 的确定性兜底只服务显示，绝不用于放行。
/// </para>
/// </summary>
public static class NpcTemplateCatalog
{
    /// <summary>开发用默认模板。</summary>
    /// <remarks>
    /// 三人的五行域按职司取象：锻金之刃归金、守城厚土归土、行商流转如木。
    /// 水/火两域没有 NPC 来源是有意留白——它们的击杀、任务与信物三条口已足够撑起 favorNeed，
    /// 不要为"每域都要有个说话对象"这种对称美感硬塞两个空壳 NPC。
    /// </remarks>
    private static readonly NpcTemplateInfo[] Templates =
    {
        new(1, "铸剑师", "铁匠", "npc_smith", 1),
        new(2, "守门人", "城门", "npc_gatekeeper", 5),
        new(3, "游方商队", "行商", "npc_merchant", 2),
    };

    /// <summary>
    /// 取模板信息；未登记走确定性兜底名，保证客户端不会显示空名。
    /// <para><b>读法：先查已装配内容，再退回本文件那三行硬编码</b>。没装配时结果与今天逐字相同——
    /// 这条等价性由 <c>NpcEditorContractTests</c> 钉住，因为 1/2/3 号是存量任务的发布者，
    /// <b>不能因为装配了新内容就查不到了</b>。</para>
    /// </summary>
    public static NpcTemplateInfo Get(int templateId)
    {
        if (TryGetRegistered(templateId, out var registered)) return registered;

        return new NpcTemplateInfo(templateId, $"路人#{templateId}", string.Empty, $"npc_{templateId}", 0);
    }

    /// <summary>
    /// NPC 模板 → 五行域；未登记或域非法（越界/0）返回 0，调用方据此<b>不记账</b>。
    /// 与 <see cref="MonsterTemplateCatalog.DomainOf"/> 同形：查表不抛、非法值不写进账本。
    /// </summary>
    public static int DomainOf(int templateId)
    {
        if (!TryGetRegistered(templateId, out var info)) return 0;
        return info.WuxingDomain >= 1 && info.WuxingDomain <= 5 ? info.WuxingDomain : 0;
    }

    /// <summary>
    /// 取<b>已登记</b>模板；未登记返回 false（授权白名单用，不做兜底）。
    /// <para><b>这一位是授权</b>：脚本生成与任务发布校验都走这里，所以装配进来的 NPC 一旦登记
    /// 就等于拿到生成权——装配器必须只装 <c>publish_state=已发布</c> 的行。</para>
    /// </summary>
    public static bool TryGetRegistered(int templateId, out NpcTemplateInfo info)
    {
        // 先查装配内容（库里/编辑器里配好的 NPC）。
        if (NpcContent.TryGetConfig(templateId, out var config))
        {
            info = new NpcTemplateInfo(
                config.NpcId,
                string.IsNullOrEmpty(config.Name) ? $"路人#{config.NpcId}" : config.Name,
                config.Title,
                string.IsNullOrEmpty(config.ModelKey) ? $"npc_{config.NpcId}" : config.ModelKey,
                config.WuxingDomain);
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

    /// <summary>
    /// 取全部<b>已登记</b>模板（任务编辑器的 NPC 下拉数据源）。
    /// <para><b>顺序：按编号升序的并集</b>（硬编码 1/2/3 与装配内容去重后一起排）。
    /// 清单顺序会被下拉框与对账脚本比，顺序不稳定会制造"没人改却变了"的假象。</para>
    /// </summary>
    public static NpcTemplateInfo[] RegisteredTemplates()
    {
        var ids = new List<int>(Templates.Length + NpcContent.RegisteredIds().Count);
        for (var i = 0; i < Templates.Length; i++) ids.Add(Templates[i].TemplateId);

        foreach (var id in NpcContent.RegisteredIds())
        {
            // 装配内容里若重号（库里也登记了 1 号铸剑师），以装配值为准，不再重复列硬编码行。
            if (!ids.Contains(id)) ids.Add(id);
        }

        ids.Sort();

        var result = new List<NpcTemplateInfo>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            if (TryGetRegistered(ids[i], out var info)) result.Add(info);
        }

        return result.ToArray();
    }

    /// <summary>按显示名或模型键查已登记模板（大小写不敏感；顺序与 <see cref="RegisteredTemplates"/> 一致）。</summary>
    public static bool TryFindByName(string key, out NpcTemplateInfo info)
    {
        var all = RegisteredTemplates();
        for (var i = 0; i < all.Length; i++)
        {
            if (string.Equals(all[i].Name, key, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(all[i].ModelKey, key, System.StringComparison.OrdinalIgnoreCase))
            {
                info = all[i];
                return true;
            }
        }

        info = default;
        return false;
    }
}
