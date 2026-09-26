using Game.Character.Attributes;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.Services;
using System.Collections.Generic;
using ClientSlot = HundunWorld.Game.Equipment.EquipmentSlot;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 装备 → 角色属性的聚合桥。
    /// <para>
    /// 唯一数据源是 <see cref="PlayerInventoryService"/> 的服务端权威穿戴快照（槽位→TemplateId），
    /// 数值来自客户端装备定义表 <see cref="EquipmentData.BaseStats"/>/<see cref="WuxingBonus"/>。
    /// 每次穿戴/卸下快照变化后重算并整体写入 <see cref="CharacterAttributesComponent"/>，
    /// 组件按"基值+加成"合成分层，不会被每帧服务端基值同步冲掉。
    /// </para>
    /// <para>
    /// 已知边界：服务端战斗（DS）仍按全局配置结算伤害，装备加成当前只影响客户端属性展示；
    /// 权威化需要服务端聚合后经 `CharacterAttributeRefresh` 通道下发。
    /// </summary>
    public static class EquipmentAttributeBridge
    {
        private static readonly ClientSlot[] AllSlots =
            (ClientSlot[])System.Enum.GetValues(typeof(ClientSlot));

        /// <summary>按当前权威穿戴快照重算加成并写入本地玩家属性组件。</summary>
        public static void RefreshFromEquipped()
        {
            var game = HundunWorldGame.Instance;
            var actor = game?.LocalPlayerActor;
            var component = actor != null ? actor.GetScript<CharacterAttributesComponent>() : null;
            if (component == null) return;

            component.SetEquipmentBonus(Summarize(PlayerInventoryService.Instance));
        }

        /// <summary>把权威穿戴快照求和为属性加成（纯函数，便于单测/复用）。</summary>
        public static CharacterAttributesComponent.EquipmentStatBonus Summarize(PlayerInventoryService inventory)
        {
            var bonus = new CharacterAttributesComponent.EquipmentStatBonus();
            if (inventory == null) return bonus;

            foreach (var slot in AllSlots)
            {
                if (!inventory.TryGetEquipped(slot, out var entry) || entry.TemplateId <= 0)
                    continue;

                var equipment = EquipmentDatabase.GetEquipment(entry.TemplateId);
                if (equipment == null) continue;

                AccumulateBaseStats(ref bonus, equipment);
                AccumulateWuxing(ref bonus, equipment);
                AccumulateSocketedGems(ref bonus, entry.Gems);
            }
            return bonus;
        }

        /// <summary>已镶宝石的五行加成计入角色五行（宝石属性与五行属性关联的主通道）。</summary>
        private static void AccumulateSocketedGems(ref CharacterAttributesComponent.EquipmentStatBonus bonus, System.Collections.Generic.List<int> gems)
        {
            if (gems == null) return;
            foreach (var gemTemplateId in gems)
            {
                if (gemTemplateId <= 0) continue;
                var gem = HundunWorld.Game.Inventory.ItemCatalog.Resolve(gemTemplateId);
                switch (gem.GemElement)
                {
                    case WuxingElement.Metal: bonus.MetalAffinity += gem.GemAffinityBonus; break;
                    case WuxingElement.Wood: bonus.WoodAffinity += gem.GemAffinityBonus; break;
                    case WuxingElement.Water: bonus.WaterAffinity += gem.GemAffinityBonus; break;
                    case WuxingElement.Fire: bonus.FireAffinity += gem.GemAffinityBonus; break;
                    case WuxingElement.Earth: bonus.EarthAffinity += gem.GemAffinityBonus; break;
                }
            }
        }

        private static void AccumulateBaseStats(ref CharacterAttributesComponent.EquipmentStatBonus bonus, EquipmentData equipment)
        {
            var stats = equipment.BaseStats;
            if (stats == null) return;

            foreach (var kv in stats)
            {
                switch (kv.Key)
                {
                    case "Attack": bonus.PhysicalAttack += kv.Value; break;
                    case "Defense": bonus.PhysicalDefense += kv.Value; break;
                    case "HP": bonus.MaxHealth += kv.Value; break;
                    case "MP": bonus.MaxEnergy += kv.Value; break;
                    // CritRate 等键暂无对应角色属性字段，跳过而不是错误摊进攻防。
                }
            }
        }

        private static void AccumulateWuxing(ref CharacterAttributesComponent.EquipmentStatBonus bonus, EquipmentData equipment)
        {
            var wuxing = equipment.WuxingBonus;
            if (wuxing == null) return;

            foreach (var kv in wuxing)
            {
                switch (kv.Key)
                {
                    case WuxingElement.Metal: bonus.MetalAffinity += kv.Value; break;
                    case WuxingElement.Wood: bonus.WoodAffinity += kv.Value; break;
                    case WuxingElement.Water: bonus.WaterAffinity += kv.Value; break;
                    case WuxingElement.Fire: bonus.FireAffinity += kv.Value; break;
                    case WuxingElement.Earth: bonus.EarthAffinity += kv.Value; break;
                }
            }
        }
    }
}
