using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 增减货币事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/BPE_AddCurrency.uasset 符号表）：
    /// CallFunc 含 <c>GetInventoryComponentFromTarget</c>（InventoryFunctionLibrary）、
    /// <c>HasAuthority</c>、<c>Abs_Int</c>、<c>Less_IntInt</c>、<c>Conv_IntToString</c> +
    /// <c>SelectString</c>（拼「获得/失去货币」调试串），BP 变量 <c>Amount</c>/<c>Currency</c>，
    /// 终点是 <c>UNarrativeInventoryComponent::AddCurrency(Amount)</c>
    /// （InventoryComponent.h:429，负数即扣减、内部 Math::Max(0) 夹底）。
    /// </para>
    /// <para>
    /// 端口差异：①UE5 的 <c>Currency</c> 变量只是货币名（组件本身仍是单一 _currency 计数），
    /// 端口同样只有单一货币（Items/InventoryComponent.cs:475-481 的 AddCurrency），
    /// 故 <see cref="Currency"/> 仅作日志/显示用；②UE5 图先判 <c>HasAuthority</c> 再改数，
    /// 端口 <c>AddCurrency</c> 是本地组件方法、无权威分支，直接调用；
    /// ③一次性发放默认不随读档重放（<c>bRefireOnLoad=false</c>，基类注释里「给 500 经验」同理）。
    /// </para>
    /// </summary>
    public class AddCurrencyEvent : NarrativeEvent
    {
        /// <summary>货币数量，正加负减。对应 UE5 <c>Amount</c>。</summary>
        public int Amount { get; set; } = 0;

        /// <summary>货币名（仅显示用）。对应 UE5 <c>Currency</c>。</summary>
        public string Currency { get; set; } = "Currency";

        /// <summary>对应 UE5 资产默认：一次性发放不随读档重放。</summary>
        public AddCurrencyEvent()
        {
            bRefireOnLoad = false;
        }

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (Amount == 0)
            {
                NarrativeLog.LogWarning("AddCurrencyEvent: Amount 为 0，跳过");
                return;
            }

            var inventory = ResolveInventory(target, controller);
            if (inventory == null)
            {
                NarrativeLog.LogWarning($"AddCurrencyEvent: 目标没有背包组件，无法{(Amount > 0 ? "增加" : "扣除")}{Currency} {System.Math.Abs(Amount)}");
                return;
            }

            inventory.AddCurrency(Amount);
            NarrativeLog.Log($"AddCurrencyEvent: {Currency}{(Amount > 0 ? " +" : " -")}{System.Math.Abs(Amount)}（余额 {inventory.GetCurrency()}）");
        }

        private static NarrativeInventoryComponent ResolveInventory(object target, object controller)
        {
            // 与 GiveItemEvent 同一解析顺序：角色定义 → target 直挂 → controller 直挂，
            // 再走 ArsenalStatics 的本地玩家作用域链兜底（宿主装配模式）。
            var character = ArsenalStatics.ResolveCharacterFromContext(target, controller);
            var inventory = character?.GetInventoryComponent();
            if (inventory == null && target is Actor targetActor)
                inventory = InventoryFunctionLibrary.GetInventoryComponentFromTarget(targetActor);
            if (inventory == null && controller is Actor controllerActor)
                inventory = InventoryFunctionLibrary.GetInventoryComponentFromTarget(controllerActor);
            if (inventory == null && controller is Actor fallback)
                inventory = ArsenalStatics.GetNarrativeInventoryComponent(fallback);
            return inventory;
        }

        public override string GetGraphDisplayText() => $"{(Amount >= 0 ? "Add" : "Remove")} {System.Math.Abs(Amount)} {Currency}";
        public override string GetHintText() => Amount >= 0 ? "获得货币" : "失去货币";
    }
}
