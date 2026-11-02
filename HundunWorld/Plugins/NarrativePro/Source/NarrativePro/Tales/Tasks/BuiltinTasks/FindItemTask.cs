using System;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「找到若干数量的指定物品」的任务条目。对应 UE5 蓝图任务 <c>BPT_FindItem</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_FindItem.uasset）。
    /// <para>
    /// 二进制证据：<c>CallFunc_GetInventoryComponentFromTarget_ReturnValue</c> +
    /// <c>K2Node_CreateDelegate</c> 绑 <c>OnItemAdded</c>
    /// （<c>OnItemAdded__DelegateSignature</c> + <c>K2Node_CustomEvent_AddResult</c>，即
    /// <c>FItemAddResult</c>），用 <c>CallFunc_Conv_ClassToSoftClassReference_ReturnValue</c> +
    /// <c>EqualEqual_SoftClassReference</c> + <c>IsValidSoftClassReference</c> 比对物品类；
    /// 回溯判定走 <c>CallFunc_GetInventoryComponentFromTarget</c> →
    /// <c>CallFunc_FindItemByClassExact_ReturnValue</c> /
    /// <c>CallFunc_GetQuantity_ReturnValue</c>（先查背包里已有的量），
    /// 达标走 <c>Add_IntInt</c>；<c>bCheckVisibility</c> 决定「背包 UI 可见」是否计入。
    /// </para>
    /// <para>
    /// 端口差异：UE5 的物品类型是 <c>TSoftClassPtr&lt;UNarrativeItem&gt;</c>，端口是字符串
    /// <c>ItemClassId</c>（<see cref="NarrativeItem.ItemClassId"/>），且
    /// <see cref="NarrativeInventoryComponent.GetTotalQuantityOfItemExact"/> 目前与
    /// <c>GetTotalQuantityOfItem</c> 同实现（无子类继承判断），故「Exact」语义与 UE5 略有出入。
    /// </para>
    /// </summary>
    [Serializable]
    public class FindItemTask : NarrativeTask
    {
        /// <summary>要找的物品类 ID。对应 UE5 <c>ItemToFind</c>。</summary>
        public string ItemClassId { get; set; } = "";

        public string ItemDisplayName { get; set; } = "";

        /// <summary>是否只统计背包 UI 可见的堆叠。对应 UE5 <c>bCheckVisibility</c>。</summary>
        public bool bCheckVisibility { get; set; } = false;

        private NarrativeInventoryComponent _inventory;

        protected override void OnBeginTask()
        {
            if (string.IsNullOrEmpty(ItemClassId))
            {
                NarrativeLog.LogWarning("FindItemTask: ItemClassId 为空，无收集目标");
                return;
            }

            _inventory = BuiltinTaskSupport.GetInventory(this);
            if (_inventory == null)
            {
                NarrativeLog.LogWarning("FindItemTask: 角色没有背包组件，无法监听物品获得");
                return;
            }

            RequiredQuantity = RequiredQuantity > 0 ? RequiredQuantity : 1;

            // 回溯：进分支前背包里已有的量直接计入（UE5 的 FindItemByClassExact + GetQuantity 分支）。
            RefreshProgressFromInventory();

            if (CurrentProgress >= RequiredQuantity) return;

            _inventory.OnItemAdded += HandleItemAdded;
        }

        private void HandleItemAdded(NarrativeInventoryComponent source, ItemAddResult result)
        {
            if (!bIsActive || source != _inventory) return;
            if (result != null && !string.Equals(result.ItemClassId, ItemClassId, StringComparison.Ordinal)) return;

            // 以背包实际量为真相，而不是累加广播里的 AmountGiven：
            // 堆叠合并/容量截断会让两者不等，累加会把进度推过头。
            RefreshProgressFromInventory();
        }

        private void RefreshProgressFromInventory()
        {
            if (_inventory == null) return;
            int current = _inventory.GetTotalQuantityOfItemExact(ItemClassId, bCheckVisibility);
            if (current != CurrentProgress)
                SetProgress(current);
        }

        protected override void OnEndTask()
        {
            if (_inventory != null)
            {
                _inventory.OnItemAdded -= HandleItemAdded;
                _inventory = null;
            }
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            string name = !string.IsNullOrEmpty(ItemDisplayName) ? ItemDisplayName : ItemClassId;
            return $"找到 {name}";
        }

        public override string GetTaskProgressText()
        {
            return $"({CurrentProgress}/{RequiredQuantity})";
        }
    }
}
