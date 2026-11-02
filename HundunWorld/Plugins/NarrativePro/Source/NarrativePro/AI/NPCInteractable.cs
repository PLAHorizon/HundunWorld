using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Interaction;
using NarrativePro.Items;
using NarrativePro.Tales;

namespace NarrativePro.AI
{
    /// <summary>
    /// NPC 可交互组件。对应 UE5 UNPCInteractable。
    /// 专用于 NPC 角色，根据 NPC 存活状态和对话可用性决定交互行为：
    /// - 存活且有对话：触发对话
    /// - 已死亡：触发搜刮
    /// </summary>
    public class NPCInteractable : NarrativeInteractableComponent
    {
        /// <summary>当前可用的对话路径（从 NPC 定义资产获取）</summary>
        public string DialoguePath = "";

        /// <summary>当前 NPC 是否存活（由 NPC 控制器更新）</summary>
        public bool bIsAlive = true;

        /// <summary>
        /// 此 NPC 是否提供交易（由 <c>NPCDefinition.bIsVendor</c> 写入，
        /// 挂载点见 <c>NarrativeCharacterSubsystem.EnsureNpcInteractable</c>）。
        /// 存活且没有对话时，交互转入交易分支。
        /// </summary>
        public bool bOffersTrade = false;

        /// <summary>商店显示名（由 <c>NPCDefinition.ShopFriendlyName</c> 写入，供 UI 取标题）。</summary>
        public string TradeShopName = "";

        /// <summary>交易开始事件（interactor, 商人背包）。搜刮/交易的 UI 入口都靠背包的 OnBeginLooting，这里额外给一次交互侧广播。</summary>
        public event Action<Actor, NarrativeInventoryComponent> OnTradeBegin;

        public override bool CanInteract(Actor interactor, NarrativeInteractionComponent interactionComp, out string errorText)
        {
            errorText = "";
            if (!bIsAlive)
            {
                // 已死亡：允许搜刮
                return true;
            }
            // 存活：仅在拥有对话（主对话或标签对话集）或提供交易时允许交互
            if (string.IsNullOrEmpty(DialoguePath))
            {
                if (bOffersTrade) return true;
                var npc = ResolveNpcCharacter();
                if (npc != null && npc.HasTaggedDialogues) return true;
                errorText = "NPC 没有可用对话";
                return false;
            }
            return true;
        }

        public override bool Interact(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            if (!bIsAlive)
            {
                // 已死亡：触发搜刮
                return HandleLooting(interactor, interactionComp);
            }
            if (!string.IsNullOrEmpty(DialoguePath))
            {
                // 存活且有对话：触发对话
                return HandleDialogue(interactor, interactionComp);
            }
            if (bOffersTrade)
            {
                // 存活、无主对话但是商人：先试标签对话（问候），播出则不进店；
                // 冷却/条件不满足或无标签集时保持旧行为，触发交易（对应 UE5 UNPCInteractable 的商店分支）
                var npc = ResolveNpcCharacter();
                if (npc != null && npc.HasTaggedDialogues &&
                    npc.PlayTaggedDialogue(Core.NarrativeGameplayTags.TaggedDialogue_Greet, interactor))
                {
                    return true;
                }
                return HandleTrade(interactor, interactionComp);
            }
            // 与旧行为一致：无对话也无可交易色时仍走对话分支（它会给出警告并返回 false）
            return HandleDialogue(interactor, interactionComp);
        }

        /// <summary>宿主 NPC 角色（Actor 未挂 NarrativeNPCCharacter 脚本时为 null）。</summary>
        protected NarrativePro.UnrealFramework.NarrativeNPCCharacter ResolveNpcCharacter()
        {
            return Actor != null ? Actor.GetScript<NarrativePro.UnrealFramework.NarrativeNPCCharacter>() : null;
        }

        /// <summary>处理对话交互</summary>
        protected virtual bool HandleDialogue(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            // 标签对话优先：对应 UE5 ANarrativeNPCCharacter::PlayTaggedDialogue（NarrativeNPCCharacter.cpp:536-557），
            // 从 TaggedDialogueSet 里按标签+冷却+距离+Required/Blocked 标签选一条，经
            // ExecutePlayTaggedDialogue → TalesComponent.BeginDialogue 播出；不可播时回退主对话路径。
            var npc = ResolveNpcCharacter();
            if (npc != null && npc.HasTaggedDialogues &&
                npc.PlayTaggedDialogue(Core.NarrativeGameplayTags.TaggedDialogue_Greet, interactor))
            {
                return true;
            }

            if (string.IsNullOrEmpty(DialoguePath))
            {
                NarrativeLog.LogWarning($"NPC {Actor?.Name} 没有对话路径");
                return false;
            }
            NarrativeLog.Log($"NPC 对话交互：{Actor?.Name} ← {interactor?.Name}");
            // 通过 Tales 对话系统启动对话（优先使用交互者的 TalesComponent，回退到 NPC 自身）
            TalesComponent talesComp = interactor?.GetScript<TalesComponent>();
            if (talesComp == null)
            {
                talesComp = Actor?.GetScript<TalesComponent>();
            }
            if (talesComp == null)
            {
                NarrativeLog.LogWarning($"NPC 对话交互失败：未找到 TalesComponent（{Actor?.Name}）");
                return false;
            }
            return talesComp.BeginDialogue(DialoguePath);
        }

        /// <summary>处理搜刮交互</summary>
        protected virtual bool HandleLooting(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            NarrativeLog.Log($"NPC 搜刮交互：{Actor?.Name} ← {interactor?.Name}");
            // 接入物品系统：将 NPC 背包设为玩家搜刮源，触发 OnBeginLooting 事件由 UI 监听打开搜刮界面
            var playerInventory = interactor?.GetScript<NarrativeInventoryComponent>();
            var npcInventory = Actor?.GetScript<NarrativeInventoryComponent>();
            if (playerInventory == null || npcInventory == null)
            {
                NarrativeLog.LogWarning($"NPC 搜刮交互失败：缺少 NarrativeInventoryComponent（{Actor?.Name}）");
                return false;
            }
            playerInventory.SetLootSource(npcInventory);
            return true;
        }

        /// <summary>处理交易交互。对应 UE5 UNPCInteractable 对商人 NPC 的商店分支。</summary>
        protected virtual bool HandleTrade(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            var vendorInventory = Actor?.GetScript<NarrativeInventoryComponent>();
            if (vendorInventory == null)
            {
                NarrativeLog.LogWarning($"NPC 交易失败：{Actor?.Name} 没有背包组件");
                return false;
            }
            if (!vendorInventory.bIsVendor)
            {
                NarrativeLog.LogWarning($"NPC 交易失败：{Actor?.Name} 的背包未标记为商人");
                return false;
            }

            var playerInventory = interactor?.GetScript<NarrativeInventoryComponent>();
            if (playerInventory == null)
            {
                NarrativeLog.LogWarning($"NPC 交易失败：交互者 {interactor?.Name} 没有背包组件");
                return false;
            }

            // 商人背包设为玩家的交易源：之后 RequestLootItem=买入、RequestStoreItem=卖出，
            // 两条都会走 AllowLootItem/AllowStoreItem 的商人校验与货币结算分支。
            // OnBeginLooting 由背包广播，搜刮/商店 UI 监听同一事件即可复用现有列表控件。
            playerInventory.SetLootSource(vendorInventory);
            NarrativeLog.Log($"NPC 交易交互：{Actor?.Name}（{TradeShopName}）← {interactor?.Name}");
            OnTradeBegin?.Invoke(interactor, vendorInventory);
            return true;
        }

        /// <summary>
        /// 由外部（UI / 技能 / 任务脚本）直接发起交易的公共入口。
        /// 与按交互键的路径等价：把商人背包挂到 <c>buyer</c> 的 LootSource 上。
        /// </summary>
        public bool BeginTrade(Actor buyer) => HandleTrade(buyer, null);

        /// <summary>
        /// 单件买卖结算（交易 UI 或服务端确认用的最小 API）。
        /// <paramref name="bBuyFromVendor"/> 为真表示从商人买入（走 RequestLootItem），
        /// 为假表示卖给商人（走 RequestStoreItem），两者都会经商人校验扣/加货币。
        /// </summary>
        public bool TradeItem(Actor counterparty, string itemClassId, int quantity, bool bBuyFromVendor, out string errorText)
        {
            errorText = "";
            var vendor = Actor?.GetScript<NarrativeInventoryComponent>();
            var other = counterparty?.GetScript<NarrativeInventoryComponent>();
            if (vendor == null || other == null)
            {
                errorText = "缺少背包组件";
                return false;
            }
            if (!vendor.bIsVendor)
            {
                errorText = "该 NPC 不是商人";
                return false;
            }
            if (quantity <= 0)
            {
                errorText = "数量必须大于 0";
                return false;
            }

            // 交易窗口未打开时补挂一次，保证 Request* 系列的 LootSource 前置条件成立
            if (other.LootSource != vendor)
            {
                other.SetLootSource(vendor);
            }

            var buyer = bBuyFromVendor ? other : vendor;
            var item = bBuyFromVendor
                ? vendor.FindItemOfClass(itemClassId)
                : other.FindItemOfClass(itemClassId);
            if (item == null)
            {
                errorText = "库存不足";
                return false;
            }

            return bBuyFromVendor
                ? buyer.RequestLootItem(item, out errorText, quantity)
                : buyer.RequestStoreItem(item, out errorText, quantity);
        }
    }
}
