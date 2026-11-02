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
    /// 三行的合并口径与服务端一致：攻击求和、防御/破防"已高者计"（见 <c>CombatProfile.Aggregate</c>）。
    /// 服务端另有一份独立聚合（<c>CombatEquipmentSource</c> 读物品模板的 base_attributes），
    /// 两端靠同一套词表（MetalAttack / EarthDefense / WoodPenetration…）对齐；
    /// 权威数值回传客户端仍缺 <c>CharacterAttributeRefresh</c> 通道。
    /// </para>
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
            if (component == null)
            {
                // 快照比 Actor 早到（进场景瞬间）时就是这条路：不写日志的话整条链路一步都不留痕。
                FlaxEngine.Debug.Log("[EquipmentBridge] 本地玩家属性组件未就绪，本次装备加成留待下次快照重算。");
                return;
            }

            var bonus = Summarize(PlayerInventoryService.Instance);
            component.SetEquipmentBonus(bonus);

            // 逐项计数而不是只写"成功"：三行全 0 时能一眼看出断点在"快照没内容"还是"装备表没这行"。
            FlaxEngine.Debug.Log(
                $"[EquipmentBridge] 槽位={CountEquipped(PlayerInventoryService.Instance)} " +
                $"攻={Rows(bonus.ElementAttack)} 防={Rows(bonus.ElementDefense)} 破防={Rows(bonus.ElementPenetration)}");
        }

        private static int CountEquipped(PlayerInventoryService inventory)
        {
            int n = 0;
            foreach (var slot in AllSlots)
                if (inventory != null && inventory.TryGetEquipped(slot, out var entry) && entry.TemplateId > 0) n++;
            return n;
        }

        private static string Rows(in CharacterAttributesComponent.WuxingRows rows)
            => $"{rows.Metal}/{rows.Wood}/{rows.Water}/{rows.Fire}/{rows.Earth}";

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
                AccumulateWuxingRows(ref bonus.ElementAttack, equipment.WuxingAttack);
                AccumulateWuxingRows(ref bonus.ElementDefense, equipment.WuxingDefense);
                AccumulateWuxingRows(ref bonus.ElementPenetration, equipment.WuxingPenetration);
                AccumulateSocketedGems(ref bonus, entry.Gems);
            }
            return bonus;
        }

        /// <summary>
        /// 累加装备的某一行五行战斗属性（攻/防/破防共用一条路径，避免三份近似代码各自漂移）。
        /// </summary>
        private static void AccumulateWuxingRows(
            ref CharacterAttributesComponent.WuxingRows rows,
            System.Collections.Generic.Dictionary<WuxingElement, int> bonus)
        {
            if (bonus == null) return;
            foreach (var kv in bonus)
            {
                if (kv.Value != 0) rows.Add(kv.Key, kv.Value);
            }
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
                    // 生命/内力上限**不在这里加**：服务端血池（CharacterInfo.MaxHealth）现在已把
                    // 装备生命算进去并下行，客户端再加一次就是双倍（"+5000 变 +10000"）。
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
