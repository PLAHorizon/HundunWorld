using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Settings
{
    /// <summary>
    /// 路牌可见性设置（设置 → 路牌）。7 个开关真实写入 GameConfigurationService；
    /// 路牌系统本身尚未接入，页面明示“仅保存偏好”，不伪造路牌已生效。
    /// </summary>
    public class SettingsRoadSignPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private const float ScreenEdge = 16f;
        private const float HeaderHeight = 64f;
        private const float SidebarWidth = 196f;
        private const float SidebarGap = 16f;
        private const float ContentTop = 88f;
        private const float RowHeight = 68f;
        private const float RowGap = 10f;

        private sealed class RowSpec
        {
            public string Key;
            public string Title;
            public string Desc;
        }

        private static readonly RowSpec[] Rows =
        {
            new RowSpec { Key = "ShowSelf", Title = "显示自己放置的路牌", Desc = "关闭后自己的路牌也隐藏" },
            new RowSpec { Key = "ShowOthers", Title = "显示其他玩家放置的路牌", Desc = "关闭后世界内其他玩家路牌不可见" },
            new RowSpec { Key = "ShowJianghu", Title = "显示江湖人士路牌", Desc = "系统/江湖人士放置的提示路牌" },
            new RowSpec { Key = "AutoGuild", Title = "自动加入帮派路牌", Desc = "加入帮派后自动显示帮派路牌" },
            new RowSpec { Key = "RecordInjury", Title = "记录重伤信息", Desc = "在倒地/重伤位置留下路牌提示" },
            new RowSpec { Key = "Public", Title = "路牌公开显示", Desc = "关闭后仅自己可见" },
            new RowSpec { Key = "FriendsGuildOnly", Title = "仅显示好友/帮派路牌", Desc = "过滤陌生玩家的路牌" },
        };

        private InkBackButton _back;
        private InkTextBlock _title;
        private InkTextBlock _subtitle;
        private InkTextBlock _note;
        private InkSidebarList _sidebar;
        private InkPanel[] _rowPanels;
        private InkButton[] _rowButtons;
        private InkTextBlock[] _rowTitles;
        private InkTextBlock[] _rowDescs;
        private InkTextBlock _saveNote;
        private bool[] _values;

        public event Action<string> NavigationRequested;

        public SettingsRoadSignPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = InkWashTheme.BaseSecondary;
            ClipChildren = false;
            AutoFocus = false;

            _values = new bool[Rows.Length];
            for (var i = 0; i < Rows.Length; i++)
                _values[i] = GameConfigurationService.Instance.GetRoadSignSetting(Rows[i].Key);

            BuildHeader();
            BuildSidebar();
            BuildRows();
            ApplyLayout();
            FlaxEngine.Debug.Log("[UI-REFORGE] SettingsRoadSignPage built");
        }

        public void RefreshLayout() => ApplyLayout();

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            ApplyLayout();
        }

        private void BuildHeader()
        {
            _back = new InkBackButton { AnchorPreset = AnchorPresets.TopLeft };
            _back.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);
            AddChild(_back);

            _title = MakeText("路牌设置", InkTextStyle.Display, InkWashTheme.GoldPrimary, 26f);
            AddChild(_title);
            _subtitle = MakeText("可见性 · 互动 · 隐私", InkTextStyle.Caption, InkWashTheme.TextTertiary, 12f);
            AddChild(_subtitle);
            _note = MakeText("路牌系统尚未接入 · 以下开关仅保存偏好", InkTextStyle.Caption, InkWashTheme.TextFaint, 11f);
            _note.HorizontalAlignment = TextAlignment.Far;
            AddChild(_note);
        }

        private void BuildSidebar()
        {
            _sidebar = new InkSidebarList(InkPageDomIds.NavSettings);
            _sidebar.NavigationRequested += id => NavigationRequested?.Invoke(id);
            AddChild(_sidebar);
        }

        private void BuildRows()
        {
            _rowPanels = new InkPanel[Rows.Length];
            _rowButtons = new InkButton[Rows.Length];
            _rowTitles = new InkTextBlock[Rows.Length];
            _rowDescs = new InkTextBlock[Rows.Length];

            for (var i = 0; i < Rows.Length; i++)
            {
                var index = i;
                var panel = new InkPanel { Radius = 8f };
                _rowPanels[i] = panel;
                AddChild(panel);

                _rowTitles[i] = MakeText(Rows[i].Title, InkTextStyle.Body, InkWashTheme.TextDefault, 14f);
                panel.AddChild(_rowTitles[i]);
                _rowDescs[i] = MakeText(Rows[i].Desc, InkTextStyle.Caption, InkWashTheme.TextTertiary, 11f);
                panel.AddChild(_rowDescs[i]);

                var button = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                };
                _rowButtons[i] = button;
                button.Clicked += () => _ = ToggleAsync(index);
                panel.AddChild(button);
                RefreshButton(index);
            }

            _saveNote = MakeText("开关保存到本地配置；路牌系统接入后由服务端消费。", InkTextStyle.Caption, InkWashTheme.TextFaint, 11f);
            AddChild(_saveNote);
        }

        private async System.Threading.Tasks.Task ToggleAsync(int index)
        {
            if (index < 0 || index >= Rows.Length) return;
            var next = !_values[index];
            _values[index] = next;
            RefreshButton(index);
            _saveNote.Text = "保存中…";

            var ok = await GameConfigurationService.Instance.SetRoadSignSettingAsync(Rows[index].Key, next);
            if (!ok)
            {
                _values[index] = !next;
                RefreshButton(index);
                _saveNote.Text = "保存失败，已恢复原值";
                return;
            }
            _saveNote.Text = "已保存（路牌系统尚未接入，暂不生效）";
        }

        private void RefreshButton(int index)
        {
            var on = _values[index];
            var button = _rowButtons[index];
            if (button == null) return;
            button.Text = on ? "开" : "关";
            button.TextColor = on ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary;
            button.BackgroundColor = on
                ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.16f)
                : Color.Transparent;
        }

        private static InkTextBlock MakeText(string text, InkTextStyle style, Color color, float size)
        {
            var label = new InkTextBlock(style)
            {
                Text = text,
                TextColor = color,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
            };
            label.Font = InkRenderHelper.GetFontRef(
                style == InkTextStyle.Display ? InkWashTheme.FontRole.Display :
                style == InkTextStyle.Heading ? InkWashTheme.FontRole.Heading : InkWashTheme.FontRole.Body, size);
            return label;
        }

        private void ApplyLayout()
        {
            if (_back == null) return;

            float w = Width > 0f ? Width : 1920f;
            float h = Height > 0f ? Height : 1080f;

            _back.Location = new Float2(ScreenEdge, ScreenEdge);
            _title.Location = new Float2(64f, 8f);
            _title.Size = new Float2(240f, 32f);
            _subtitle.Location = new Float2(64f, 38f);
            _subtitle.Size = new Float2(420f, 18f);
            _note.Location = new Float2(w - 560f, 24f);
            _note.Size = new Float2(540f, 18f);

            _sidebar.Layout(ScreenEdge, ContentTop, SidebarWidth, h - ContentTop - ScreenEdge);

            float contentX = ScreenEdge + SidebarWidth + SidebarGap;
            float contentW = Mathf.Max(0f, w - contentX - ScreenEdge);
            float rowW = Mathf.Min(900f, contentW);
            float top = ContentTop;

            for (var i = 0; i < _rowPanels.Length; i++)
            {
                var panel = _rowPanels[i];
                panel.Location = new Float2(contentX, top);
                panel.Size = new Float2(rowW, RowHeight);

                _rowTitles[i].Location = new Float2(18f, 10f);
                _rowTitles[i].Size = new Float2(Mathf.Max(0f, rowW - 120f), 22f);
                _rowDescs[i].Location = new Float2(18f, 34f);
                _rowDescs[i].Size = new Float2(Mathf.Max(0f, rowW - 120f), 18f);

                _rowButtons[i].Location = new Float2(Mathf.Max(0f, rowW - 76f), 20f);
                _rowButtons[i].Size = new Float2(52f, 28f);

                top += RowHeight + RowGap;
            }

            _saveNote.Location = new Float2(contentX, top + 4f);
            _saveNote.Size = new Float2(rowW, 18f);
        }
    }
}
