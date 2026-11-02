using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Character;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 控制状态小方块（眩晕/沉默/定身…）：状态字 + 剩余秒数，颜色按状态区分。
    /// 传统 HUD 与 V2 HUD 共用，数据来源唯一：<see cref="ControlStateManager"/>。
    /// </summary>
    public sealed class ControlStateChipView : Control
    {
        /// <summary>图标区高度（剩余秒数画在其下 10px）。</summary>
        public const float IconHeight = 24f;

        private ControlState _state;
        private float _remaining;

        public ControlStateChipView()
        {
            AutoFocus = false;
        }

        public void SetState(ControlState state, float remaining)
        {
            _state = state;
            _remaining = remaining;
            TooltipText = LabelOf(state);
        }

        public override void Draw()
        {
            if (!Visible) return;

            var color = ColorOf(_state);
            var rect = new Rectangle(Float2.Zero, new Float2(Width, IconHeight));

            InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusSm,
                new Color(color.R, color.G, color.B, 0.18f));
            InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusSm, color, 1f);

            var glyphFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f).GetFont();
            if (glyphFont != null)
            {
                Render2D.DrawText(glyphFont, GlyphOf(_state), rect, color,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            var timeFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 9f).GetFont();
            if (timeFont != null)
            {
                Render2D.DrawText(timeFont, Mathf.Max(0f, _remaining).ToString("0.0"),
                    new Rectangle(0f, IconHeight, Width, 10f), InkWashTheme.TextTertiary,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        public static string GlyphOf(ControlState state) => state switch
        {
            ControlState.Stunned => "眩",
            ControlState.Silenced => "默",
            ControlState.Rooted => "定",
            ControlState.Feared => "惧",
            ControlState.Confused => "乱",
            ControlState.Disarmed => "缴",
            ControlState.Blinded => "盲",
            ControlState.Taunted => "嘲",
            ControlState.Sleep => "眠",
            ControlState.Frozen => "冰",
            _ => "?",
        };

        public static Color ColorOf(ControlState state) => state switch
        {
            ControlState.Stunned => InkWashTheme.BloodBright,
            ControlState.Silenced => InkWashTheme.GoldBright,
            ControlState.Rooted => InkWashTheme.JadeBright,
            ControlState.Feared => InkWashTheme.BloodBright,
            ControlState.Confused => InkWashTheme.QualityEpic,
            ControlState.Taunted => InkWashTheme.GoldPrimary,
            ControlState.Sleep => InkWashTheme.JadeDeep,
            ControlState.Frozen => InkWashTheme.ElementWater,
            _ => InkWashTheme.TextSecondary,
        };

        public static string LabelOf(ControlState state) => state switch
        {
            ControlState.Stunned => "眩晕",
            ControlState.Silenced => "沉默",
            ControlState.Rooted => "定身",
            ControlState.Feared => "恐惧",
            ControlState.Confused => "混乱",
            ControlState.Disarmed => "缴械",
            ControlState.Blinded => "致盲",
            ControlState.Taunted => "嘲讽",
            ControlState.Sleep => "睡眠",
            ControlState.Frozen => "冰冻",
            _ => "控制",
        };
    }
}
