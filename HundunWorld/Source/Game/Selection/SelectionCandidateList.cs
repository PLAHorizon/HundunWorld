using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.GameMain;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.Selection
{
    /// <summary>
    /// 候选目标列表：同一位置有多个可选中目标时，在光标就近处弹出纵向列表供玩家挑一个。
    /// <para>
    /// 挂在 <see cref="NameplateHost"/> 下而不是 HUD 页面里：名牌宿主位于 Canvas 根、
    /// <b>不参与 1920×1080 设计空间缩放</b>，所以这里可以直接用投影得到的屏幕像素坐标定位；
    /// 若塞进 HUD 页内容层，每个坐标都要除以 <c>UILayout.Scale</c>，两套换算混用是位置错乱的常见来源。
    /// 宿主本身按当前页面统一显隐，非战斗 HUD 页时列表自然跟着消失。
    /// </para>
    /// </summary>
    public class SelectionCandidateList
    {
        private const float RowHeight = 26f;
        private const float RowWidth = 220f;
        private const float Gap = 2f;
        private const int MaxRows = 6;

        /// <summary>无后续操作时列表自动收起的时间（秒）。</summary>
        private const float AutoHideSeconds = 4f;

        private Panel _root;
        private readonly List<Button> _rows = new();
        private float _shownAtTime = -1f;

        public bool IsVisible => _root != null && _root.Visible;

        /// <summary>在光标附近弹出候选列表；<paramref name="candidates"/> 数量 ≤ 1 时收起。</summary>
        public void Show(IReadOnlyList<SelectableEntityTag> candidates, Vector2 cursorScreenPos)
        {
            if (candidates == null || candidates.Count <= 1)
            {
                Hide();
                return;
            }

            EnsureRoot();

            // 默认在光标右下；贴边时整体翻到另一侧，避免列表被裁出屏幕。
            float x = cursorScreenPos.X + 12f;
            float y = cursorScreenPos.Y + 12f;
            float height = candidates.Count * (RowHeight + Gap);
            if (x + RowWidth > _root.Parent.Width)
                x = Mathf.Max(0f, cursorScreenPos.X - RowWidth - 12f);
            if (y + height > _root.Parent.Height)
                y = Mathf.Max(0f, _root.Parent.Height - height);

            _root.Location = new Float2(x, y);
            _root.Size = new Float2(RowWidth, height);
            _root.Visible = true;
            _shownAtTime = Time.GameTime;

            RebuildRows(candidates);
        }

        public void Hide()
        {
            if (_root == null)
                return;
            _root.Visible = false;
            _shownAtTime = -1f;
        }

        /// <summary>每帧由选中服务调用：超时自动收起 + 目标已消失时同步刷新。</summary>
        public void Tick()
        {
            if (!IsVisible || _shownAtTime < 0f)
                return;

            if (Time.GameTime - _shownAtTime > AutoHideSeconds)
                Hide();
        }

        public void Dispose()
        {
            if (_root == null)
                return;
            _root.Parent?.RemoveChild(_root);
            _root = null;
            _rows.Clear();
        }

        private void EnsureRoot()
        {
            if (_root != null && _root.Parent != null)
                return;

            var host = NameplateHost.Current;
            if (host == null)
                return;

            _root = new Panel
            {
                Parent = host,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = new Color(0.06f, 0.05f, 0.04f, 0.86f),
                ClipChildren = false,
            };
        }

        private void RebuildRows(IReadOnlyList<SelectableEntityTag> candidates)
        {
            foreach (var row in _rows)
            {
                if (row != null)
                    row.Dispose();
            }
            _rows.Clear();

            int count = Math.Min(candidates.Count, MaxRows);
            for (var i = 0; i < count; i++)
            {
                int index = i;
                var tag = candidates[i];

                var row = new Button
                {
                    Parent = _root,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, i * (RowHeight + Gap)),
                    Size = new Float2(RowWidth, RowHeight),
                    Text = Describe(tag),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.TextDefault,
                    BackgroundColor = Color.Transparent,
                };
                row.Clicked += () => WorldSelectionService.Instance?.SelectFromStack(index);
                _rows.Add(row);
            }

            if (candidates.Count > MaxRows)
            {
                // 说明行：不订阅 Clicked，纯提示。
                var more = new Button
                {
                    Parent = _root,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, count * (RowHeight + Gap)),
                    Size = new Float2(RowWidth, RowHeight),
                    Text = $"…另有 {candidates.Count - MaxRows} 个，用 Shift+Tab 继续切换",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextTertiary,
                    BackgroundColor = Color.Transparent,
                };
                _rows.Add(more);
            }
        }

        /// <summary>行文案：名称 + 种类/等级。</summary>
        private static string Describe(SelectableEntityTag tag)
        {
            string name = !string.IsNullOrEmpty(tag.DisplayName) ? tag.DisplayName : (tag.Actor?.Name ?? "目标");

            string detail = tag.Kind switch
            {
                SelectableKind.Monster => tag.Level > 0 ? $"妖兽 Lv.{tag.Level}" : "妖兽",
                SelectableKind.Player => tag.Level > 0 ? $"修士 Lv.{tag.Level}" : "修士",
                SelectableKind.Item => "物品",
                SelectableKind.GatherNode => "可采集",
                SelectableKind.Npc => "NPC",
                _ => string.Empty,
            };

            return string.IsNullOrEmpty(detail) ? name : $"{name}　{detail}";
        }
    }
}
