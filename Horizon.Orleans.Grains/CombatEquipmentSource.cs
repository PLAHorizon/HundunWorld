using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Core.Combat;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Orleans.Grains;

/// <summary>
/// 装备（含镶嵌宝石）→ 权威属性贡献的取数器。
/// <para>
/// 穿戴快照来自 <see cref="IInventoryGrain"/>（角色键的 Grain，服务端写的），
/// 数值来自 <c>Game_HunduShijie_ItemTemplate</c> 的属性 JSON，按
/// <see cref="ItemAttributeContract"/> 解析——客户端硬编码装备表不再是被相信的那一方。
/// </para>
/// <para>
/// 任何一步取不到（背包服务不可用、模板表未迁移、该模板没写属性）都只降级为"这件装备不加属性"，
/// 绝不抛异常打断结算：一次穿戴失败可以重穿，一次攻击结算崩掉会把整场对抗拖没。
/// </para>
/// </summary>
public static class CombatEquipmentSource
{
    /// <summary>
    /// 读取该角色当前穿戴（含宝石）的装备属性并合并。
    /// </summary>
    /// <param name="grainFactory">调用方 Grain 的 GrainFactory（InventoryGrain 是角色键 Guid）。</param>
    /// <param name="templateContext">物品模板上下文；未注入（单测/降级）时返回空结果。</param>
    /// <param name="characterId">角色 ID。</param>
    /// <param name="templateCache">调用方持有的模板缓存（内容表几乎不变，别每次受击都查库）。</param>
    public static async Task<ItemAttributeContract.ParsedItemAttributes> GatherAsync(
        IGrainFactory grainFactory,
        IDataContext<GameEntityContext, ItemTemplateEntity, int>? templateContext,
        ulong characterId,
        Dictionary<int, ItemTemplateEntity?>? templateCache,
        ILogger? logger = null)
    {
        if (grainFactory == null || templateContext == null || characterId == 0UL)
            return ItemAttributeContract.Combine(Array.Empty<ItemAttributeContract.ParsedItemAttributes>());

        List<ItemInfo>? equipped;
        try
        {
            var inventory = grainFactory.GetGrain<IInventoryGrain>(GrainKeyMapper.ToInventoryKey(characterId));
            equipped = await inventory.GetEquippedItemInfosAsync();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "读取穿戴快照失败，按无装备加成处理: CharacterId={CharacterId}", characterId);
            return ItemAttributeContract.Combine(Array.Empty<ItemAttributeContract.ParsedItemAttributes>());
        }

        if (equipped == null || equipped.Count == 0)
            return ItemAttributeContract.Combine(Array.Empty<ItemAttributeContract.ParsedItemAttributes>());

        var parts = new List<ItemAttributeContract.ParsedItemAttributes>(equipped.Count * 2);
        foreach (var item in equipped)
        {
            if (item == null) continue;

            var template = await ResolveAsync(templateContext, templateCache, item.TemplateId, logger);
            if (template != null)
            {
                // 固定属性与随机属性都要读：前者是定义，后者是这一件的身份差异所在。
                parts.Add(ItemAttributeContract.Parse(template.BaseAttributes));
                parts.Add(ItemAttributeContract.Parse(template.RandomAttributes));
            }

            if (item.Gems != null)
            {
                foreach (var gemTemplateId in item.Gems)
                {
                    if (gemTemplateId <= 0) continue;   // -1 = 空槽
                    var gem = await ResolveAsync(templateContext, templateCache, gemTemplateId, logger);
                    if (gem != null) parts.Add(ItemAttributeContract.Parse(gem.BaseAttributes));
                }
            }
        }

        return ItemAttributeContract.Combine(parts);
    }

    private static async Task<ItemTemplateEntity?> ResolveAsync(
        IDataContext<GameEntityContext, ItemTemplateEntity, int> context,
        Dictionary<int, ItemTemplateEntity?>? cache,
        int templateId,
        ILogger? logger)
    {
        if (templateId <= 0) return null;
        if (cache != null && cache.TryGetValue(templateId, out var cached)) return cached;

        ItemTemplateEntity? template = null;
        try
        {
            template = await context.QueryFirstOrDefaultAsync(t => t.Id == templateId);
        }
        catch (Exception ex)
        {
            // 模板表可能尚未迁移/该 id 不存在：当作"这件不加属性"，并把 null 缓存住避免反复打库。
            logger?.LogWarning(ex, "读取物品模板失败，按无属性处理: TemplateId={TemplateId}", templateId);
        }

        if (cache != null) cache[templateId] = template;
        return template;
    }
}
