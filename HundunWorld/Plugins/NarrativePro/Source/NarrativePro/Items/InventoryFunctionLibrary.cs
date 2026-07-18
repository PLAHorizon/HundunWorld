using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 背包相关静态工具函数库。适配 UE5 UInventoryFunctionLibrary。
    /// 对应 UE5 UBlueprintFunctionLibrary，Flax 中以 static class 实现。
    /// </summary>
    public static class InventoryFunctionLibrary
    {
        /// <summary>
        /// 从目标对象查找背包组件。
        /// 若给定的是 Pawn/Controller，会进一步检查 Pawn 的 PlayerState 和 Controller。
        /// </summary>
        /// <param name="target">目标 Actor</param>
        /// <returns>找到的背包组件；未找到返回 null</returns>
        public static NarrativeInventoryComponent GetInventoryComponentFromTarget(Actor target)
        {
            if (target == null) return null;

            // 直接在目标 Actor 上查找
            var comp = target.GetScript<NarrativeInventoryComponent>();
            if (comp != null) return comp;

            // 在子级中查找（如 PlayerState 挂在 Controller 上）
            foreach (var child in target.Children)
            {
                comp = child.GetScript<NarrativeInventoryComponent>();
                if (comp != null) return comp;
            }

            // 若为 Pawn，尝试从 PlayerState 查找
            // Flax 中没有直接的 PlayerState 概念，此处保持与 Actor 同样的查找逻辑
            return null;
        }

        /// <summary>
        /// 按显示名 A-Z 排序物品数组。
        /// </summary>
        /// <param name="inItems">待排序物品列表</param>
        /// <param name="bReverse">是否倒序（Z-A）</param>
        /// <returns>排序后的新列表</returns>
        public static List<NarrativeItem> SortItemArrayAlphabetical(List<NarrativeItem> inItems, bool bReverse)
        {
            if (inItems == null) return new List<NarrativeItem>();
            var result = new List<NarrativeItem>(inItems);
            if (bReverse)
            {
                result.Sort((a, b) =>
                {
                    if (a == null && b == null) return 0;
                    if (a == null) return 1;
                    if (b == null) return -1;
                    return string.Compare(b.DisplayName, a.DisplayName, System.StringComparison.Ordinal);
                });
            }
            else
            {
                result.Sort((a, b) =>
                {
                    if (a == null && b == null) return 0;
                    if (a == null) return 1;
                    if (b == null) return -1;
                    return string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.Ordinal);
                });
            }
            return result;
        }

        /// <summary>
        /// 按堆叠重量排序物品数组。
        /// </summary>
        /// <param name="inItems">待排序物品列表</param>
        /// <param name="bReverse">是否倒序（重→轻）</param>
        /// <returns>排序后的新列表</returns>
        public static List<NarrativeItem> SortItemArrayWeight(List<NarrativeItem> inItems, bool bReverse)
        {
            if (inItems == null) return new List<NarrativeItem>();
            var result = new List<NarrativeItem>(inItems);
            if (bReverse)
            {
                // 重→轻
                result.Sort((a, b) =>
                {
                    float wa = a?.GetStackWeight() ?? 0f;
                    float wb = b?.GetStackWeight() ?? 0f;
                    return wb.CompareTo(wa);
                });
            }
            else
            {
                // 轻→重
                result.Sort((a, b) =>
                {
                    float wa = a?.GetStackWeight() ?? 0f;
                    float wb = b?.GetStackWeight() ?? 0f;
                    return wa.CompareTo(wb);
                });
            }
            return result;
        }
    }
}
