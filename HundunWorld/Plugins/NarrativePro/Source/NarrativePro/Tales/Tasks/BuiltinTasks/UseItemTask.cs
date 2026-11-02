using System;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「使用指定物品若干次」的任务条目。对应 UE5 蓝图任务 <c>BPT_UseItem</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_UseItem.uasset）。
    /// <para>
    /// 二进制证据：<c>CallFunc_GetInventoryComponentFromTarget_ReturnValue</c> 取背包后
    /// <c>K2Node_AssignDelegate</c> 绑 <c>OnItemUsed</c>
    /// （<c>OnItemUsed__DelegateSignature</c> + <c>K2Node_CustomEvent_Item</c>，用
    /// <c>CallFunc_EqualEqual_ClassClass_ReturnValue</c> 比对使用掉的物品类），
    /// 变量 <c>ItemToUse</c>，未命中类时靠 <c>IsValidClass</c> 走「任意物品」分支；
    /// 达标走 <c>Add_IntInt</c> + <c>Less_IntInt</c> 循环。
    /// </para>
    /// </summary>
    [Serializable]
    public class UseItemTask : NarrativeTask
    {
        /// <summary>要使用的物品类 ID；留空表示使用任意物品都算。对应 UE5 <c>ItemToUse</c>。</summary>
        public string ItemClassId { get; set; } = "";

        /// <summary>物品显示名（仅用于任务描述）。</summary>
        public string ItemDisplayName { get; set; } = "";

        /// <summary>开始时是否代为使用一次（UE5 图只等玩家点，端口保留脚本触发能力）。</summary>
        public bool bUseOnBegin { get; set; } = false;

        private NarrativeInventoryComponent _inventory;

        protected override void OnBeginTask()
        {
            _inventory = BuiltinTaskSupport.GetInventory(this);
            if (_inventory == null)
            {
                NarrativeLog.LogWarning("UseItemTask: 角色没有背包组件，无法监听物品使用");
                return;
            }

            // 使用事件没有可回溯的历史（UE5 也一样），但读档后可能已经通过进度还原满足。
            if (CurrentProgress >= RequiredQuantity) return;

            _inventory.OnItemUsed += HandleItemUsed;

            if (bUseOnBegin)
            {
                if (!_inventory.UseItemOfClass(ItemClassId))
                    NarrativeLog.LogWarning($"UseItemTask: 代为使用 '{ItemClassId}' 失败（背包里没有或物品拒绝）");
            }
        }

        private void HandleItemUsed(NarrativeInventoryComponent source, NarrativeItem item)
        {
            if (!bIsActive || source != _inventory) return;
            if (item == null) return;

            if (!string.IsNullOrEmpty(ItemClassId) &&
                !string.Equals(item.ItemClassId, ItemClassId, StringComparison.Ordinal)) return;

            AddProgress(1);
        }

        protected override void OnEndTask()
        {
            if (_inventory != null)
            {
                _inventory.OnItemUsed -= HandleItemUsed;
                _inventory = null;
            }
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            string name = !string.IsNullOrEmpty(ItemDisplayName) ? ItemDisplayName
                          : (!string.IsNullOrEmpty(ItemClassId) ? ItemClassId : "任意物品");
            return $"使用 {name}";
        }
    }
}
