using System;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.Combat.Skills;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.ActionBar
{
    /// <summary>
    /// 五行循环节指示器：技能栏上方一排五枚相生序节点（金水木火土），
    /// 亮起的是已叠层数，闪一下的是刚续上的那一环。
    /// <para>数据来自 <see cref="WuxingCycleTracker"/>（客户端预测），服务端权威节数下行后可直接替换数据源。</para>
    /// </summary>
    public class InkWuxingCycleIndicator : Control
    {
        private const float IndicatorW = 380f;
        private const float IndicatorH = 24f;
        private const float BottomMargin = 90f;   // 技能栏 62 + 栏底边距 20 + 间隙 8

        /// <summary>相生序的展示顺序：金生水、水生木、木生火、火生土、土生金。</summary>
        private static readonly WuxingElement[] GenerateOrder =
        {
            WuxingElement.Metal, WuxingElement.Water, WuxingElement.Wood, WuxingElement.Fire, WuxingElement.Earth,
        };

        private static readonly string[] ElementGlyphs = { "金", "水", "木", "火", "土" };

        private InkPageShell _shell;
        private float _pulse;
        private int _completionFlashAt = -1;

        /// <summary>Draw 时顺手记下的五枚节点矩形，OnMouseDown 直接按本地坐标命中（与技能栏同一口径）。</summary>
        private readonly Rectangle[] _chipBoxes = new Rectangle[GenerateOrder.Length];
        private float _guardStatusAge = 999f;

        public InkWuxingCycleIndicator()
        {
            AnchorPreset = AnchorPresets.TopLeft;
            BackgroundColor = Color.Transparent;
            AutoFocus = false;
            Size = new Float2(IndicatorW, IndicatorH);
            Visible = false;

            WuxingCycleTracker.Instance.CycleCompleted += OnCycleCompleted;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            WuxingCycleTracker.Instance.CycleCompleted -= OnCycleCompleted;
        }

        private void OnCycleCompleted()
        {
            _completionFlashAt = WuxingCycleTracker.Instance.CompletionCount;
            _pulse = 1f;
        }

        public void AttachTo(InkPageShell shell)
        {
            if (shell == null || Parent != null) return;
            _shell = shell;
            shell.AddPersistentControl(this);
            Relocate();
        }

        private void Relocate()
        {
            if (_shell == null) return;
            Float2 design = _shell.DesignSize;
            if (design.X <= 0f) return;
            _shell.PlaceInDesignSpace(this, new Float2(design.X * 0.5f - IndicatorW * 0.5f, design.Y - BottomMargin - IndicatorH));
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (_shell != null && (_shell.DesignScale != 0f) && Visible) Relocate();
            if (_pulse > 0f) _pulse = Math.Max(0f, _pulse - deltaTime * 1.6f);

            // 镇岳倒计时由这里走：指示器是常驻可见的，而 tracker 不是控件、自己没有 Update。
            WuxingCycleTracker.Instance.TickGuardCooldown(deltaTime);
            _guardStatusAge += deltaTime;
        }

        /// <summary>
        /// 点一枚节点 = 把护体钉在这一行；再点同一枚 = 放开（回到"防御最高行"自动口径）。
        /// 本地不改状态，只发请求——生效行由服务端回执决定，被拒时界面自己弹回。
        /// </summary>
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left) return base.OnMouseDown(location, button);

            var tracker = WuxingCycleTracker.Instance;
            for (int i = 0; i < _chipBoxes.Length; i++)
            {
                if (!_chipBoxes[i].Contains(location)) continue;

                if (tracker.GuardCooldownRemaining > 0f)
                {
                    _guardStatusAge = 0f;        // 把"还在冷却"顶到提示位，别让人以为没点到
                    return true;
                }

                var player = HundunWorldGame.Instance;
                if (player == null || player.PlayerId == 0UL) return true;

                int target = tracker.GuardElement == GenerateOrder[i] ? 0 : (int)GenerateOrder[i];
                tracker.MarkGuardPending(0.6f);  // 只挡连点，不代表生效
                _ = HundunWorld.Game.Network.MessageHandlers.GuardElementHandler.RequestAsync(player.PlayerId, target);
                return true;
            }

            return base.OnMouseDown(location, button);
        }

        public override void Draw()
        {
            if (!Visible || Width <= 0f) return;

            var tracker = WuxingCycleTracker.Instance;
            int stack = Math.Max(0, Math.Min(WuxingCycleTracker.MaxStack, tracker.Stack));
            float chipW = 26f;
            float gap = 6f;
            float startX = 0f;
            var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();

            for (int i = 0; i < GenerateOrder.Length; i++)
            {
                var box = new Rectangle(startX + i * (chipW + gap), 2f, chipW, chipW);
                _chipBoxes[i] = box;
                bool lit = i < stack;
                bool next = i == stack;                       // 下一环：该打这一行才能续上
                bool justNow = tracker.LastCastGenerated && i == stack - 1;

                var color = ColorFor(GenerateOrder[i]);
                var bg = lit
                    ? new Color(color.R, color.G, color.B, 0.55f)
                    : next ? new Color(color.R, color.G, color.B, 0.16f)
                    : new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.45f);
                InkRenderHelper.FillRoundedRectangle(box, InkWashTheme.RadiusSm, bg);

                var border = lit ? color
                    : next ? InkWashTheme.GoldBright
                    : InkWashTheme.BorderGoldSubtle;
                InkRenderHelper.DrawRoundedRectangle(box, InkWashTheme.RadiusSm,
                    new Color(border.R, border.G, border.B, justNow ? 1f : 0.8f), justNow ? 2f : 1f);

                // 镇岳行：整枚再套一圈粗金边并在底部压一道金线。刻意全画在框内——
                // 控件高 24，把"岳"字顶到框外会被上层裁掉或压到技能栏上。
                if (tracker.GuardElement == GenerateOrder[i])
                {
                    var guardBox = new Rectangle(box.X - 2f, box.Y - 2f, box.Width + 4f, box.Height + 4f);
                    InkRenderHelper.DrawRoundedRectangle(guardBox, InkWashTheme.RadiusSm + 2f, InkWashTheme.GoldBright, 2f);
                    Render2D.DrawLine(new Float2(box.X + 2f, box.Bottom - 1f), new Float2(box.Right - 2f, box.Bottom - 1f),
                        InkWashTheme.GoldBright, 2f);
                }

                if (font != null)
                {
                    Render2D.DrawText(font, ElementGlyphs[i], box,
                        lit ? InkWashTheme.PaperBright : InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

                // 相生序的箭头，让人看懂这是环不是并列条
                if (i < GenerateOrder.Length - 1)
                {
                    float ax = box.Right + 1f;
                    float ay = box.Y + chipW * 0.5f;
                    Render2D.DrawLine(new Float2(ax, ay), new Float2(ax + gap - 2f, ay),
                        InkWashTheme.BorderGoldSubtle, 1f);
                }
            }

            var labelFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();
            if (labelFont != null)
            {
                float x = GenerateOrder.Length * (chipW + gap) + 6f;
                string hint;
                var guard = tracker.GuardElement;
                if (_guardStatusAge < 4f && !string.IsNullOrEmpty(tracker.GuardStatusText))
                {
                    // 回执优先占位：被拒的原因（冷却中/该行没练过）只有这几秒窗口能看见，
                    // 错过就得让玩家自己去猜为什么点了没反应。
                    hint = tracker.GuardStatusText;
                }
                else if (guard != WuxingElement.None)
                {
                    int gi = System.Array.IndexOf(GenerateOrder, guard);
                    string glyph = gi >= 0 ? ElementGlyphs[gi] : "?";
                    hint = tracker.GuardCooldownRemaining > 0f
                        ? $"镇岳 {glyph}（{tracker.GuardCooldownRemaining:F0}s）"
                        : $"镇岳 {glyph} · 点同枚放开";
                }
                else if (stack == 0)
                {
                    hint = "起环：点一行可镇岳，任选一行起手";
                }
                else
                {
                    hint = $"节 {stack}/{WuxingCycleTracker.MaxStack} · 增伤 +{stack * 3}%";
                }
                var rect = new Rectangle(x, 4f, Width - x, IndicatorH - 8f);
                Render2D.DrawText(labelFont, hint, rect,
                    _pulse > 0f ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                string done = $"周天 {tracker.CompletionCount}";
                var doneRect = new Rectangle(Width - 62f, 4f, 62f, IndicatorH - 8f);
                Render2D.DrawText(labelFont, done, doneRect,
                    _completionFlashAt == tracker.CompletionCount && _pulse > 0f ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary,
                    TextAlignment.Far, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        private static Color ColorFor(WuxingElement element) => element switch
        {
            WuxingElement.Metal => InkWashTheme.ElementMetal,
            WuxingElement.Wood => InkWashTheme.ElementWood,
            WuxingElement.Water => InkWashTheme.ElementWater,
            WuxingElement.Fire => InkWashTheme.ElementFire,
            _ => InkWashTheme.ElementEarth,
        };
    }
}
