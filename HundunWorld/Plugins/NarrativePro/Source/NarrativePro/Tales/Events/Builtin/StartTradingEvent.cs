using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 开始交易事件。把商人 NPC 的交易库存设为玩家背包的拾取/交易源。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/BPE_StartTrading.uasset 符号表）：
    /// CallFunc 仅 <c>GetInventoryComponentFromTarget</c> + <c>GetTradingInventoryComponent</c> +
    /// <c>SetLootSource</c>，K2 节点含 <c>DynamicCast_AsNarrative_NPCCharacter</c>，
    /// EventFilter=<c>EF_OnlyNPCs</c>——即把 target 转成 NPC、取其
    /// <c>UNarrativeNPCCharacter::TradingInventoryComponent</c>
    /// （NarrativeNPCCharacter.h:350），再 <c>SetLootSource</c> 到玩家背包。
    /// </para>
    /// <para>
    /// 端口差异：玩家侧背包定位在端口无 GetPlayerController(0) 直取路径，
    /// 依次尝试 controller 的本地玩家作用域链与 <c>ArsenalStatics.ResolveLocalPlayerHostActor</c>
    /// （宿主装配模式）。设置 LootSource 后 <c>OnBeginLooting</c> 广播即端口商店 UI 的既有入口
    /// （AI/NPCInteractable.cs 的 HandleTrade 同型写法）。
    /// </para>
    /// </summary>
    public class StartTradingEvent : NarrativeEvent
    {
        /// <summary>对应 UE5 资产 EventFilter 默认 EF_OnlyNPCs（事件跑在商人 NPC 身上）。</summary>
        public StartTradingEvent()
        {
            EventFilter = EEventFilter.OnlyNPCs;
        }

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            var vendorInventory = ResolveVendorInventory(target);
            if (vendorInventory == null)
            {
                NarrativeLog.LogWarning("StartTradingEvent: 目标 NPC 没有交易库存组件，交易未开始");
                return;
            }

            var playerInventory = ResolvePlayerInventory(controller);
            if (playerInventory == null)
            {
                NarrativeLog.LogWarning("StartTradingEvent: 找不到玩家侧背包组件，交易未开始");
                return;
            }

            // 对应 UE5 SetLootSource(TradingInventoryComponent)：买入=RequestLootItem、卖出=RequestStoreItem
            playerInventory.SetLootSource(vendorInventory);
            NarrativeLog.Log($"StartTradingEvent: 已开始交易（商人 {vendorInventory.InventoryFriendlyName}）");
        }

        private static NarrativeInventoryComponent ResolveVendorInventory(object target)
        {
            if (target is not Actor targetActor) return null;

            // UE5 口径：先走 NPC 角色的 TradingInventoryComponent 访问器
            var npc = targetActor.GetScript<NarrativeNPCCharacter>();
            var vendor = npc?.GetTradingInventoryComponent();
            if (vendor != null) return vendor;

            return InventoryFunctionLibrary.GetInventoryComponentFromTarget(targetActor);
        }

        private static NarrativeInventoryComponent ResolvePlayerInventory(object controller)
        {
            if (controller is Actor controllerActor)
            {
                // 本地玩家作用域解析链（角色模式/宿主装配模式皆可）
                var inventory = ArsenalStatics.GetNarrativeInventoryComponent(controllerActor);
                if (inventory != null) return inventory;
            }

            var host = ArsenalStatics.ResolveLocalPlayerHostActor(out _);
            return host != null ? ArsenalStatics.GetNarrativeInventoryComponent(host) : null;
        }

        public override string GetGraphDisplayText() => "Start Trading";
        public override string GetHintText() => "开始交易";
    }
}
