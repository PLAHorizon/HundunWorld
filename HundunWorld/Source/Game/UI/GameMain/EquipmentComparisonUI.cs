using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.Equipment;
using EquipmentDataModel = HundunWorld.Game.Equipment.EquipmentData;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 装备对比面板
    /// 显示当前装备与新装备的属性对比，用绿色/红色箭头标示提升/下降
    /// </summary>
    public class EquipmentComparisonUI
    {
        /// <summary>
        /// 装备属性项
        /// </summary>
        public class EquipmentStat
        {
            public string Name { get; set; } = "";
            public float CurrentValue { get; set; }
            public float NewValue { get; set; }
            public float Diff => NewValue - CurrentValue;
        }

        /// <summary>
        /// 装备信息
        /// </summary>
        public class EquipmentData
        {
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public int Quality { get; set; } // 0=白,1=绿,2=蓝,3=紫,4=橙,5=红
            public int Level { get; set; }
            public int RequiredLevel { get; set; }
            public string SlotName { get; set; } = "";
            public List<EquipmentStat> Stats { get; set; } = new();
            public Dictionary<string, float> BaseStats { get; set; } = new();
            public Dictionary<WuxingElement, int> WuxingBonus { get; set; } = new();
        }

        private Panel _panel;

        /// <summary>
        /// 创建装备对比面板内容
        /// </summary>
        public void PopulatePanel(Panel panel, float startY, float width, float height)
        {
            _panel = panel;
            float y = startY;

            // 标题分隔线
            var divider = new Panel
            {
                Bounds = new Rectangle(10, y, width - 20, 2),
                BackgroundColor = new Color(0.4f, 0.4f, 0.4f, 0.5f)
            };
            panel.AddChild(divider);
            y += 10;

            // 两列布局：左=当前装备，右=新装备
            float colWidth = (width - 30) / 2;

            // 当前装备标题
            var currentTitle = new Label
            {
                Text = "── 当前装备 ──",
                TextColor = new Color(0.8f, 0.8f, 0.8f),
                Bounds = new Rectangle(10, y, colWidth, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(currentTitle);

            // 新装备标题
            var newTitle = new Label
            {
                Text = "── 新装备 ──",
                TextColor = new Color(1.0f, 0.84f, 0.0f),
                Bounds = new Rectangle(colWidth + 20, y, colWidth, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(newTitle);
            y += 35;

            // 示例数据展示
            var sampleStats = new[]
            {
                new EquipmentStat { Name = "攻击力", CurrentValue = 350, NewValue = 420 },
                new EquipmentStat { Name = "防御力", CurrentValue = 200, NewValue = 185 },
                new EquipmentStat { Name = "暴击率", CurrentValue = 12, NewValue = 18 },
                new EquipmentStat { Name = "生命值", CurrentValue = 1500, NewValue = 1600 },
                new EquipmentStat { Name = "五行攻击", CurrentValue = 80, NewValue = 95 }
            };

            foreach (var stat in sampleStats)
            {
                AddStatRow(panel, stat, y, colWidth);
                y += 28;
            }

            y += 10;

            // 总评分对比
            var scoreLabel = new Label
            {
                Text = "装备评分: 2142 → 2318  ▲ +176",
                TextColor = new Color(0.3f, 1.0f, 0.3f),
                Bounds = new Rectangle(10, y, width - 20, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(scoreLabel);
            y += 35;

            // 替换按钮
            var equipButton = new Button
            {
                Text = "替换装备",
                Bounds = new Rectangle(width / 2 - 60, y, 120, 35),
                BackgroundColor = new Color(0.2f, 0.6f, 0.3f, 0.9f)
            };
            panel.AddChild(equipButton);
        }

        /// <summary>
        /// 在内嵌面板中填充装备预览/对比内容。
        /// 根据 current 与 selected 的关系自动切换单装备展示或双列对比模式，并显示穿戴/卸下按钮。
        /// </summary>
        /// <param name="container">目标容器</param>
        /// <param name="current">当前已穿戴装备，可为 null</param>
        /// <param name="selected">当前选中的装备，可为 null</param>
        /// <param name="onEquip">点击“穿戴”按钮时的回调</param>
        /// <param name="onUnequip">点击“卸下”按钮时的回调</param>
        public void PopulateEmbeddedPreview(Panel container, EquipmentDataModel current, EquipmentDataModel selected, Action onEquip, Action onUnequip)
        {
            if (container == null) return;

            // 清空容器现有子控件
            container.DisposeChildren();

            float width = container.Width;
            float padding = 12f;
            float y = padding;

            // 确定要展示的主体装备
            EquipmentDataModel displayData = selected ?? current;

            // 空状态提示
            if (displayData == null)
            {
                var emptyLabel = new Label
                {
                    Text = "未选择装备",
                    TextColor = new Color(0.7f, 0.7f, 0.7f),
                    Bounds = new Rectangle(padding, y, width - padding * 2, 30),
                    HorizontalAlignment = TextAlignment.Center,
                    Font = UIHelper.SetFont(size: 14)
                };
                container.AddChild(emptyLabel);
                return;
            }

            // 标题：装备名称
            var titleLabel = new Label
            {
                Text = displayData.Name,
                TextColor = GetQualityColor(0),
                Bounds = new Rectangle(padding, y, width - padding * 2, 28),
                HorizontalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 18)
            };
            container.AddChild(titleLabel);
            y += 34;

            // 装备描述
            if (!string.IsNullOrEmpty(displayData.Description))
            {
                var descLabel = new Label
                {
                    Text = displayData.Description,
                    TextColor = new Color(0.75f, 0.75f, 0.75f),
                    Bounds = new Rectangle(padding, y, width - padding * 2, 22),
                    HorizontalAlignment = TextAlignment.Near,
                    Font = UIHelper.SetFont(size: 12)
                };
                container.AddChild(descLabel);
                y += 28;
            }

            // 需求等级
            var levelLabel = new Label
            {
                Text = $"需求等级：{displayData.RequiredLevel}",
                TextColor = ChineseClassicalTheme.SecondaryColor,
                Bounds = new Rectangle(padding, y, width - padding * 2, 22),
                HorizontalAlignment = TextAlignment.Near,
                Font = UIHelper.SetFont(size: 12)
            };
            container.AddChild(levelLabel);
            y += 32;

            // 分隔线
            y = AddDivider(container, y, width, padding);

            // 判断是否为对比模式
            bool isComparison = selected != null && current != null && selected != current;

            if (isComparison)
            {
                // 两列对比：当前装备 vs 选中装备
                float colWidth = (width - padding * 3) / 2f;

                var currentTitle = new Label
                {
                    Text = "当前装备",
                    TextColor = new Color(0.8f, 0.8f, 0.8f),
                    Bounds = new Rectangle(padding, y, colWidth, 24),
                    HorizontalAlignment = TextAlignment.Center,
                    Font = UIHelper.SetFont(size: 13)
                };
                container.AddChild(currentTitle);

                var selectedTitle = new Label
                {
                    Text = "选中装备",
                    TextColor = ChineseClassicalTheme.SecondaryColor,
                    Bounds = new Rectangle(padding + colWidth + padding, y, colWidth, 24),
                    HorizontalAlignment = TextAlignment.Center,
                    Font = UIHelper.SetFont(size: 13)
                };
                container.AddChild(selectedTitle);
                y += 30;

                y = AddComparisonStatRows(container, current, selected, y, padding, colWidth);
            }
            else
            {
                // 单装备属性展示
                y = AddSingleStatRows(container, displayData, y, padding, width - padding * 2);
            }

            y += 10;

            // 根据状态决定按钮类型
            bool showUnequip = current != null && (selected == current || selected == null);
            bool showEquip = selected != null && (current == null || selected != current);

            if (showUnequip)
            {
                var unequipButton = UIHelper.CreateDangerButton("卸下");
                unequipButton.Bounds = new Rectangle(width / 2f - 60, y, 120, 36);
                unequipButton.Clicked += () => onUnequip?.Invoke();
                container.AddChild(unequipButton);
                y += 46;
            }
            else if (showEquip)
            {
                var wearButton = UIHelper.CreatePrimaryButton("穿戴");
                wearButton.Bounds = new Rectangle(width / 2f - 60, y, 120, 36);
                wearButton.Clicked += () => onEquip?.Invoke();
                container.AddChild(wearButton);
                y += 46;
            }
        }

        /// <summary>
        /// 添加分隔线
        /// </summary>
        private float AddDivider(Panel container, float y, float width, float padding)
        {
            var divider = new Panel
            {
                Bounds = new Rectangle(padding, y, width - padding * 2, 2),
                BackgroundColor = new Color(0.4f, 0.4f, 0.4f, 0.5f)
            };
            container.AddChild(divider);
            return y + 12;
        }

        /// <summary>
        /// 添加单装备属性行列表
        /// </summary>
        private float AddSingleStatRows(Panel container, EquipmentDataModel data, float y, float padding, float rowWidth)
        {
            var statKeys = GetSortedStatKeys(data.BaseStats);
            foreach (var key in statKeys)
            {
                float value = data.BaseStats.TryGetValue(key, out float v) ? v : 0f;
                AddSingleStatRow(container, GetStatDisplayName(key), value, y, padding, rowWidth);
                y += 26;
            }

            foreach (var element in GetSortedWuxingElements(data.WuxingBonus))
            {
                int value = data.WuxingBonus.TryGetValue(element, out int v) ? v : 0;
                AddSingleStatRow(container, GetWuxingDisplayName(element), value, y, padding, rowWidth, ChineseClassicalTheme.SecondaryColor);
                y += 26;
            }

            return y;
        }

        /// <summary>
        /// 添加单属性行
        /// </summary>
        private void AddSingleStatRow(Panel container, string name, float value, float y, float padding, float rowWidth, Color? valueColor = null)
        {
            var nameLabel = new Label
            {
                Text = name,
                TextColor = new Color(0.7f, 0.7f, 0.7f),
                Bounds = new Rectangle(padding, y, rowWidth * 0.45f, 22),
                HorizontalAlignment = TextAlignment.Near,
                Font = UIHelper.SetFont(size: 12)
            };
            container.AddChild(nameLabel);

            var valueLabel = new Label
            {
                Text = value.ToString("F0"),
                TextColor = valueColor ?? Color.White,
                Bounds = new Rectangle(padding + rowWidth * 0.5f, y, rowWidth * 0.5f, 22),
                HorizontalAlignment = TextAlignment.Near,
                Font = UIHelper.SetFont(size: 12)
            };
            container.AddChild(valueLabel);
        }

        /// <summary>
        /// 添加对比属性行列表
        /// </summary>
        private float AddComparisonStatRows(Panel container, EquipmentDataModel current, EquipmentDataModel selected, float y, float padding, float colWidth)
        {
            var allKeys = new HashSet<string>();
            if (current?.BaseStats != null) allKeys.UnionWith(current.BaseStats.Keys);
            if (selected?.BaseStats != null) allKeys.UnionWith(selected.BaseStats.Keys);

            var statKeys = GetSortedStatKeys(allKeys.ToDictionary(k => k, _ => 0f));
            foreach (var key in statKeys)
            {
                float currentValue = 0f;
                float selectedValue = 0f;
                if (current?.BaseStats != null)
                    current.BaseStats.TryGetValue(key, out currentValue);
                if (selected?.BaseStats != null)
                    selected.BaseStats.TryGetValue(key, out selectedValue);
                AddComparisonStatRow(container, GetStatDisplayName(key), currentValue, selectedValue, y, padding, colWidth);
                y += 26;
            }

            var allElements = new HashSet<WuxingElement>();
            if (current?.WuxingBonus != null) allElements.UnionWith(current.WuxingBonus.Keys);
            if (selected?.WuxingBonus != null) allElements.UnionWith(selected.WuxingBonus.Keys);

            foreach (var element in GetSortedWuxingElements(allElements.ToDictionary(e => e, _ => 0)))
            {
                int currentValue = 0;
                int selectedValue = 0;
                if (current?.WuxingBonus != null)
                    current.WuxingBonus.TryGetValue(element, out currentValue);
                if (selected?.WuxingBonus != null)
                    selected.WuxingBonus.TryGetValue(element, out selectedValue);
                AddComparisonStatRow(container, GetWuxingDisplayName(element), currentValue, selectedValue, y, padding, colWidth);
                y += 26;
            }

            return y;
        }

        /// <summary>
        /// 添加对比属性行
        /// </summary>
        private void AddComparisonStatRow(Panel container, string name, float currentValue, float newValue, float y, float padding, float colWidth)
        {
            float diff = newValue - currentValue;
            Color diffColor = diff > 0
                ? ChineseClassicalTheme.SuccessColor
                : diff < 0
                    ? ChineseClassicalTheme.AccentColor
                    : new Color(0.7f, 0.7f, 0.7f);

            string arrow = diff > 0 ? "▲" : diff < 0 ? "▼" : "─";
            string diffText = diff != 0 ? $" {diff:+0;-0}" : "";

            // 属性名
            var nameLabel = new Label
            {
                Text = name,
                TextColor = new Color(0.7f, 0.7f, 0.7f),
                Bounds = new Rectangle(padding, y, 70, 22),
                HorizontalAlignment = TextAlignment.Near,
                Font = UIHelper.SetFont(size: 12)
            };
            container.AddChild(nameLabel);

            // 当前值
            var currentLabel = new Label
            {
                Text = currentValue.ToString("F0"),
                TextColor = Color.White,
                Bounds = new Rectangle(padding + 75, y, colWidth - 75, 22),
                HorizontalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 12)
            };
            container.AddChild(currentLabel);

            // 选中值（含差异提示）
            var newLabel = new Label
            {
                Text = $"{newValue:F0}  {arrow}{diffText}",
                TextColor = diffColor,
                Bounds = new Rectangle(padding + colWidth + padding, y, colWidth, 22),
                HorizontalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 12)
            };
            container.AddChild(newLabel);
        }

        /// <summary>
        /// 获取已排序的属性键列表，优先显示攻击力和防御力
        /// </summary>
        private List<string> GetSortedStatKeys(Dictionary<string, float> stats)
        {
            if (stats == null || stats.Count == 0)
                return new List<string>();

            var keys = stats.Keys.ToList();
            var orderedKeys = new List<string>();

            if (keys.Contains("Attack")) orderedKeys.Add("Attack");
            if (keys.Contains("Defense")) orderedKeys.Add("Defense");

            foreach (var key in keys.OrderBy(k => k))
            {
                if (key != "Attack" && key != "Defense")
                    orderedKeys.Add(key);
            }

            return orderedKeys;
        }

        /// <summary>
        /// 获取属性的中文显示名称
        /// </summary>
        private string GetStatDisplayName(string key)
        {
            return key switch
            {
                "Attack" => "攻击力",
                "Defense" => "防御力",
                "HP" => "生命值",
                "MP" => "内力值",
                "CritRate" => "暴击率",
                "CritDamage" => "暴击伤害",
                _ => key
            };
        }

        /// <summary>
        /// 获取五行元素的中文显示名称
        /// </summary>
        private string GetWuxingDisplayName(WuxingElement element)
        {
            return element switch
            {
                WuxingElement.Metal => "金",
                WuxingElement.Wood => "木",
                WuxingElement.Water => "水",
                WuxingElement.Fire => "火",
                WuxingElement.Earth => "土",
                _ => "无"
            };
        }

        /// <summary>
        /// 获取已排序的五行元素列表
        /// </summary>
        private List<WuxingElement> GetSortedWuxingElements<T>(Dictionary<WuxingElement, T> bonus)
        {
            if (bonus == null || bonus.Count == 0)
                return new List<WuxingElement>();

            return bonus.Keys
                .Where(e => e != WuxingElement.None)
                .OrderBy(e => (int)e)
                .ToList();
        }

        /// <summary>
        /// 添加属性对比行
        /// </summary>
        private void AddStatRow(Panel panel, EquipmentStat stat, float y, float colWidth)
        {
            // 属性名
            var nameLabel = new Label
            {
                Text = stat.Name,
                TextColor = new Color(0.7f, 0.7f, 0.7f),
                Bounds = new Rectangle(10, y, 80, 22),
                HorizontalAlignment = TextAlignment.Near
            };
            panel.AddChild(nameLabel);

            // 当前值
            var currentLabel = new Label
            {
                Text = stat.CurrentValue.ToString("F0"),
                TextColor = Color.White,
                Bounds = new Rectangle(90, y, colWidth - 80, 22),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(currentLabel);

            // 差异指示
            Color diffColor = stat.Diff > 0
                ? new Color(0.3f, 1.0f, 0.3f)   // 绿色=提升
                : stat.Diff < 0
                    ? new Color(1.0f, 0.3f, 0.3f) // 红色=下降
                    : new Color(0.7f, 0.7f, 0.7f); // 灰色=不变

            string arrow = stat.Diff > 0 ? "▲" : stat.Diff < 0 ? "▼" : "─";
            string diffText = stat.Diff != 0 ? $" {stat.Diff:+0;-0}" : "";

            // 新值
            var newLabel = new Label
            {
                Text = $"{stat.NewValue:F0}  {arrow}{diffText}",
                TextColor = diffColor,
                Bounds = new Rectangle(colWidth + 20, y, colWidth, 22),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(newLabel);
        }

        /// <summary>
        /// 获取品质颜色
        /// </summary>
        public static Color GetQualityColor(int quality)
        {
            return quality switch
            {
                0 => Color.White,                          // 白色
                1 => new Color(0.3f, 0.9f, 0.3f),        // 绿色
                2 => new Color(0.3f, 0.5f, 1.0f),        // 蓝色
                3 => new Color(0.7f, 0.3f, 0.9f),        // 紫色
                4 => new Color(1.0f, 0.6f, 0.1f),        // 橙色
                5 => new Color(1.0f, 0.2f, 0.2f),        // 红色
                _ => Color.Gray
            };
        }
    }
}
