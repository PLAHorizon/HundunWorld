using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 物品提示框 UI 基类。适配 UE5 UItemTooltip（继承自 UUserWidget）。
    /// 在 Flax 中以 Script 形式存在，作为物品提示框的视图模型/数据源基类，
    /// 子类（实际 UI 控件）可绑定到 Item 属性以渲染物品信息。
    /// </summary>
    public class ItemTooltip : Script
    {
        /// <summary>此提示框当前显示的物品</summary>
        public NarrativeItem Item { get; set; }
    }
}
