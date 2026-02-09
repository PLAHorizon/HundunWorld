using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Components;

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
            public int Quality { get; set; } // 0=白,1=绿,2=蓝,3=紫,4=橙,5=红
            public int Level { get; set; }
            public string SlotName { get; set; } = "";
            public List<EquipmentStat> Stats { get; set; } = new();
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
