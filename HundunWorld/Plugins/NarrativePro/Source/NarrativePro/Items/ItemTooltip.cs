using FlaxEngine;

namespace NarrativePro.Items
{
    /// <summary>
    /// 物品提示框数据源。适配 UE5 UItemTooltip（继承自 UUserWidget）。
    /// Flax 的提示框控件由 UI 侧的控件树创建（Widgets/UI 目录），本类是物品 → UI 的视图模型：
    /// 由 <see cref="InventoryFunctionLibrary.CreateItemTooltip"/> 生成，UI 控件绑定其字段渲染。
    /// 对应 UE5 <c>UItemTooltip::SetItem</c> + <c>UNarrativeItem::GetParsedDescription</c>。
    /// </summary>
    public class ItemTooltip
    {
        /// <summary>此提示框当前显示的物品</summary>
        public NarrativeItem Item { get; private set; }

        /// <summary>标题（物品显示名）</summary>
        public string Title { get; private set; } = "";

        /// <summary>已完成 {Var} 变量替换的描述文本</summary>
        public string Description { get; private set; } = "";

        /// <summary>属性行（对应 UE5 提示框中逐条展示的 NarrativeItemStat / StatTooltip）</summary>
        public string StatLines { get; private set; } = "";

        /// <summary>重量行（单件重量 / 堆叠重量）</summary>
        public string WeightText { get; private set; } = "";

        /// <summary>数量行（当前数量 / 最大堆叠）</summary>
        public string QuantityText { get; private set; } = "";

        /// <summary>是否有可展示的内容</summary>
        public bool bIsValid { get; private set; } = false;

        /// <summary>用指定物品填充提示框数据。对应 UE5 <c>UItemTooltip::SetItem</c>。</summary>
        public void PopulateFrom(NarrativeItem item)
        {
            Item = item;
            if (item == null)
            {
                Title = Description = StatLines = WeightText = QuantityText = "";
                bIsValid = false;
                return;
            }

            Title = item.DisplayName;
            Description = item.GetParsedDescription();
            StatLines = BuildStatLines(item);
            WeightText = $"{item.Weight:F1} / {item.GetStackWeight():F1}";
            QuantityText = $"{item.Quantity} / {item.MaxStackSize}";
            bIsValid = true;
        }

        /// <summary>
        /// 逐条拼装物品属性行：显示名 + 由 StringVariable 解析出的数值 + 属性悬浮说明。
        /// 对应 UE5 UItemTooltip 遍历 NarrativeItem.Stats 渲染预览窗属性。
        /// </summary>
        private static string BuildStatLines(NarrativeItem item)
        {
            if (item.Stats == null || item.Stats.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var stat in item.Stats)
            {
                if (stat == null) continue;
                string value = string.IsNullOrEmpty(stat.StringVariable) ? "" : item.GetStringVariable(stat.StringVariable);
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(stat.StatDisplayName).Append(": ").Append(value);
                if (!string.IsNullOrEmpty(stat.StatTooltip)) sb.Append(" (").Append(stat.StatTooltip).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>拼装单行摘要文本（用于日志/提示条，非富文本 UI 场景）。</summary>
        public string GetSummaryText()
        {
            if (!bIsValid) return "";
            return string.IsNullOrEmpty(Description) ? Title : $"{Title} — {Description}";
        }
    }
}
