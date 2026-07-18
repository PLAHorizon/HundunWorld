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

        public override bool CanInteract(Actor interactor, NarrativeInteractionComponent interactionComp, out string errorText)
        {
            errorText = "";
            if (!bIsAlive)
            {
                // 已死亡：允许搜刮
                return true;
            }
            // 存活：仅在拥有对话时允许交互
            if (string.IsNullOrEmpty(DialoguePath))
            {
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
            // 存活：触发对话
            return HandleDialogue(interactor, interactionComp);
        }

        /// <summary>处理对话交互</summary>
        protected virtual bool HandleDialogue(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
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
    }
}
