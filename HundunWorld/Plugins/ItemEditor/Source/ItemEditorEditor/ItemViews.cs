using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace ItemEditor
{
    /// <summary>物品编辑器的配色（与怪物/宠物/任务编辑器同一族深色）。</summary>
    internal static class ItemColors
    {
        public static readonly Color Canvas = new Color(0.09f, 0.095f, 0.105f);
        public static readonly Color Card = new Color(0.15f, 0.16f, 0.175f);
        public static readonly Color Border = new Color(0.24f, 0.25f, 0.27f);
        public static readonly Color Text = new Color(0.90f, 0.91f, 0.93f);
        public static readonly Color TextDim = new Color(0.62f, 0.64f, 0.67f);
        public static readonly Color Accent = new Color(0.36f, 0.68f, 0.92f);
        public static readonly Color Ok = new Color(0.36f, 0.72f, 0.48f);
        public static readonly Color Warn = new Color(0.95f, 0.75f, 0.30f);
        public static readonly Color Danger = new Color(0.86f, 0.32f, 0.28f);
    }

    /// <summary>
    /// 物品 tooltip 预览：按行画出"玩家在游戏里会看到的样子"。
    /// <para>数据来源优先是 CLI <c>preview-tooltip</c>（内核生成）；CLI 不可用时退回
    /// <see cref="ItemPreviewMath.BuildTooltip"/> 的本地副本。</para>
    /// </summary>
    public sealed class ItemTooltipView : Control
    {
        private readonly List<ItemTooltipPreviewLine> _lines = new List<ItemTooltipPreviewLine>();
        private string _note = string.Empty;
        private int _quality;

        public ItemTooltipView()
        {
            AnchorPreset = AnchorPresets.TopLeft;
        }

        public void SetData(IEnumerable<ItemTooltipPreviewLine> lines, int quality, string note)
        {
            _lines.Clear();
            if (lines != null) _lines.AddRange(lines);
            _quality = quality;
            _note = note ?? string.Empty;
        }

        public override void Draw()
        {
            var bounds = Bounds;
            Render2D.FillRectangle(bounds, ItemColors.Canvas);
            Render2D.DrawRectangle(bounds, ItemColors.Border);

            Render2D.DrawText(Style.Current.FontMedium, "Tooltip 预览",
                new Rectangle(8f, 4f, Math.Max(0f, Width - 16f), 18f), ItemColors.TextDim);

            var y = 26f;
            var rowHeight = 16f;
            foreach (var line in _lines)
            {
                if (y > Height - 22f) break;
                var color = ColorFor(line.Kind);
                Render2D.DrawText(Style.Current.FontSmall, line.Text,
                    new Rectangle(10f, y, Math.Max(0f, Width - 20f), rowHeight), color);
                y += rowHeight;
            }

            if (!string.IsNullOrEmpty(_note))
            {
                Render2D.DrawText(Style.Current.FontSmall, _note,
                    new Rectangle(10f, Math.Max(0f, Height - 20f), Math.Max(0f, Width - 20f), 16f),
                    ItemColors.TextDim);
            }
        }

        private Color ColorFor(string kind)
        {
            switch (kind)
            {
                case "title": return ParseColor(ItemPreviewMath.QualityColorHex(_quality));
                case "quality": return ItemColors.Text;
                case "stat": return ItemColors.Ok;
                case "damage": return ItemColors.Warn;
                case "armor": return ItemColors.Warn;
                case "price": return ItemColors.Warn;
                case "req": return ItemColors.Danger;
                case "set-active": return ItemColors.Ok;
                case "set-inactive": return ItemColors.TextDim;
                case "desc": return new Color(0.95f, 0.84f, 0.42f);
                default: return ItemColors.Text;
            }
        }

        /// <summary>把 "#RRGGBB" 解析成 Color；坏值回白色而不是抛异常（预览不该阻断编辑）。</summary>
        public static Color ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return Color.White;
            try
            {
                var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255f;
                var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255f;
                var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255f;
                return new Color(r, g, b);
            }
            catch
            {
                return Color.White;
            }
        }
    }

    /// <summary>
    /// 属性预算曲线：把"同品质下 ilvl → 预算"画成折线，并在当前点上画标记。
    /// <para>这条曲线由本地镜像公式画（<see cref="ItemPreviewMath.StatBudget"/>），
    /// 逐点与内核 <c>preview-budget --sweep</c> 对账由窗口的"试算预算"按钮负责。</para>
    /// </summary>
    public sealed class ItemBudgetCurveView : Control
    {
        private int _itemLevel = 1;
        private int _quality = 1;
        private int _budget;

        public ItemBudgetCurveView()
        {
            AnchorPreset = AnchorPresets.TopLeft;
        }

        public void SetData(int itemLevel, int quality, int budget)
        {
            _itemLevel = itemLevel < 1 ? 1 : itemLevel;
            _quality = quality;
            _budget = budget;
        }

        public override void Draw()
        {
            var bounds = Bounds;
            Render2D.FillRectangle(bounds, ItemColors.Canvas);
            Render2D.DrawRectangle(bounds, ItemColors.Border);

            Render2D.DrawText(Style.Current.FontMedium,
                string.Format("属性预算曲线（品质 {0}）", ItemPreviewMath.QualityName(_quality)),
                new Rectangle(8f, 4f, Math.Max(0f, Width - 16f), 18f), ItemColors.TextDim);

            var area = new Rectangle(34f, 26f, Math.Max(10f, Width - 46f), Math.Max(20f, Height - 52f));
            var maxLevel = Math.Min(ItemPreviewMath.MaxItemLevel, Math.Max(30, _itemLevel * 2));
            var maxBudget = Math.Max(1, ItemPreviewMath.StatBudget(maxLevel, _quality));

            for (var i = 0; i <= 4; i++)
            {
                var y = area.Y + area.Height * i / 4f;
                Render2D.FillRectangle(new Rectangle(area.X, y, area.Width, 1f), new Color(0.18f, 0.19f, 0.21f));
            }

            var previous = Float2.Zero;
            for (var level = 1; level <= maxLevel; level++)
            {
                var budget = ItemPreviewMath.StatBudget(level, _quality);
                var x = area.X + area.Width * (level - 1) / (float)Math.Max(1, maxLevel - 1);
                var y = area.Bottom - area.Height * (budget / (float)maxBudget);
                var point = new Float2(x, y);
                if (level > 1) DrawSegment(previous, point, ItemColors.Accent);
                previous = point;
            }

            var markerX = area.X + area.Width * (_itemLevel - 1) / (float)Math.Max(1, maxLevel - 1);
            var markerY = area.Bottom - area.Height * (Math.Min(_budget, maxBudget) / (float)maxBudget);
            Render2D.FillRectangle(new Rectangle(markerX - 3f, markerY - 3f, 6f, 6f), ItemColors.Ok);

            Render2D.DrawText(Style.Current.FontSmall,
                string.Format("ilvl {0} → 预算 {1}（画面画到 ilvl {2}）", _itemLevel, _budget, maxLevel),
                new Rectangle(34f, Math.Max(0f, Height - 20f), Math.Max(0f, Width - 46f), 16f), ItemColors.Text);
        }

        private static void DrawSegment(Float2 a, Float2 b, Color color)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = (float)Math.Sqrt(dx * dx + dy * dy);
            var steps = Math.Min(256, Math.Max(2, (int)len));
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                Render2D.FillRectangle(new Rectangle(a.X + dx * t, a.Y + dy * t, 1.5f, 1.5f), color);
            }
        }
    }

    /// <summary>套装档位图：每档画一根横条，已达成件数之下的档高亮。</summary>
    public sealed class ItemSetTierView : Control
    {
        private readonly List<int> _pieces = new List<int>();
        private readonly List<string> _labels = new List<string>();
        private int _equipped;

        public ItemSetTierView()
        {
            AnchorPreset = AnchorPresets.TopLeft;
        }

        public void SetData(IEnumerable<int> pieces, IEnumerable<string> labels, int equippedPieces)
        {
            _pieces.Clear();
            _labels.Clear();
            if (pieces != null) _pieces.AddRange(pieces);
            if (labels != null) _labels.AddRange(labels);
            _equipped = equippedPieces;
        }

        public override void Draw()
        {
            var bounds = Bounds;
            Render2D.FillRectangle(bounds, ItemColors.Canvas);
            Render2D.DrawRectangle(bounds, ItemColors.Border);

            Render2D.DrawText(Style.Current.FontMedium,
                string.Format("套装档位（已穿 {0} 件）", _equipped),
                new Rectangle(8f, 4f, Math.Max(0f, Width - 16f), 18f), ItemColors.TextDim);

            var y = 28f;
            for (var i = 0; i < _pieces.Count && y < Height - 20f; i++)
            {
                var active = _equipped >= _pieces[i];
                var color = active ? ItemColors.Ok : ItemColors.TextDim;
                Render2D.FillRectangle(new Rectangle(10f, y, 46f, 16f),
                    active ? new Color(0.18f, 0.34f, 0.24f) : new Color(0.14f, 0.15f, 0.16f));
                Render2D.DrawRectangle(new Rectangle(10f, y, 46f, 16f), color);
                Render2D.DrawText(Style.Current.FontSmall, _pieces[i] + " 件",
                    new Rectangle(12f, y + 1f, 42f, 14f), color);
                var label = i < _labels.Count ? _labels[i] : string.Empty;
                Render2D.DrawText(Style.Current.FontSmall, label,
                    new Rectangle(62f, y + 1f, Math.Max(0f, Width - 74f), 14f),
                    active ? ItemColors.Text : ItemColors.TextDim);
                y += 20f;
            }

            if (_pieces.Count == 0)
            {
                Render2D.DrawText(Style.Current.FontSmall, "未配套装档（setId>0 且 tiers 非空时出现）",
                    new Rectangle(10f, 30f, Math.Max(0f, Width - 20f), 16f), ItemColors.TextDim);
            }
        }
    }
}
