using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Guide
{
    public class GuideActionPage : Panel, IInkPage
    {
        private Float2 _screenSize;

        private InkPanel _modal;
        private InkButton _closeButton;
        private InkButton _confirmButton;

        private Label _guideTitle;
        private Label _operationTitle;
        private Label _keyLabelCluster;
        private Label _operationDesc;

        private InkPanel[] _keyButtons;
        private InkPanel[] _additionalKeyButtons;

        private InkButton _martialGuideLink;

        public event Action GuideClosed;
        public event Action MartialGuideRequested;

        public GuideActionPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildModal();
                BuildContent();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[GuideActionPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildModal()
        {
            _modal = new InkPanel
            {
                Width = 340f,
                Height = 520f,
                Location = new Float2((_screenSize.X - 340f) * 0.5f, (_screenSize.Y - 520f) * 0.5f),
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = this
            };

            InkCornerDeco cornerTL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _modal
            };

            InkCornerDeco cornerTR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopRight,
                Parent = _modal
            };

            InkCornerDeco cornerBL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Parent = _modal
            };

            InkCornerDeco cornerBR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomRight,
                Parent = _modal
            };
        }

        private void BuildContent()
        {
            float y = 0f;

            Panel headerPanel = new Panel
            {
                Width = 340f,
                Height = 48f,
                Location = new Float2(0, y),
                Parent = _modal
            };

            _guideTitle = new Label
            {
                Text = "操作引导",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(12f, 14f),
                Parent = headerPanel
            };

            InkPanel stepBadge = new InkPanel
            {
                Width = 56f,
                Height = 20f,
                Location = new Float2(100f, 14f),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.12f,
                Parent = headerPanel
            };

            Label stepLabel = new Label
            {
                Text = "第1步",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = stepBadge
            };

            _closeButton = new InkButton
            {
                Width = 28f,
                Height = 28f,
                Text = "×",
                Location = new Float2(300f, 10f),
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.PaperAged,
                Parent = headerPanel
            };
            _closeButton.Clicked += () => GuideClosed?.Invoke();

            y += 48f;

            InkDivider divider1 = new InkDivider
            {
                Width = 300f,
                Height = 1f,
                Location = new Float2(20f, y),
                Parent = _modal
            };
            y += 16f;

            Panel operationSection = new Panel
            {
                Width = 340f,
                Height = 60f,
                Location = new Float2(0f, y),
                Parent = _modal
            };

            InkPanel iconWrap = new InkPanel
            {
                Width = 24f,
                Height = 24f,
                Location = new Float2(12f, 12f),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.08f,
                Parent = operationSection
            };

            Label iconLabel = new Label
            {
                Text = "移",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 12f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = iconWrap
            };

            _operationTitle = new Label
            {
                Text = "基础移动",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.PaperBright,
                Location = new Float2(40f, 12f),
                Parent = operationSection
            };

            y += 60f;

            Panel clusterPanel = new Panel
            {
                Width = 340f,
                Height = 80f,
                Location = new Float2(0f, y),
                Parent = _modal
            };

            _keyButtons = new InkPanel[4];

            _keyButtons[0] = new InkPanel
            {
                Width = 36f,
                Height = 36f,
                Location = new Float2(152f, 0f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = clusterPanel
            };
            Label keyW = new Label
            {
                Text = "W",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _keyButtons[0]
            };

            _keyButtons[1] = new InkPanel
            {
                Width = 36f,
                Height = 36f,
                Location = new Float2(114f, 38f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = clusterPanel
            };
            Label keyA = new Label
            {
                Text = "A",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _keyButtons[1]
            };

            _keyButtons[2] = new InkPanel
            {
                Width = 36f,
                Height = 36f,
                Location = new Float2(152f, 38f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = clusterPanel
            };
            Label keyS = new Label
            {
                Text = "S",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _keyButtons[2]
            };

            _keyButtons[3] = new InkPanel
            {
                Width = 36f,
                Height = 36f,
                Location = new Float2(190f, 38f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = clusterPanel
            };
            Label keyD = new Label
            {
                Text = "D",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _keyButtons[3]
            };

            _keyLabelCluster = new Label
            {
                Text = "移动方向",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                Location = new Float2(100f, 76f),
                Parent = clusterPanel
            };

            y += 80f;

            Panel keysPanel = new Panel
            {
                Width = 340f,
                Height = 60f,
                Location = new Float2(0f, y),
                Parent = _modal
            };

            _additionalKeyButtons = new InkPanel[2];

            _additionalKeyButtons[0] = new InkPanel
            {
                Width = 50f,
                Height = 36f,
                Location = new Float2(80f, 0f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = keysPanel
            };
            Label shiftLabel = new Label
            {
                Text = "Shift",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _additionalKeyButtons[0]
            };

            Label shiftDesc = new Label
            {
                Text = "疾跑",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                Location = new Float2(80f, 38f),
                Parent = keysPanel
            };

            _additionalKeyButtons[1] = new InkPanel
            {
                Width = 80f,
                Height = 36f,
                Location = new Float2(180f, 0f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = keysPanel
            };
            Label spaceLabel = new Label
            {
                Text = "Space",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _additionalKeyButtons[1]
            };

            Label spaceDesc = new Label
            {
                Text = "跳跃",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                Location = new Float2(195f, 38f),
                Parent = keysPanel
            };

            y += 60f;

            Panel descPanel = new Panel
            {
                Width = 340f,
                Height = 50f,
                Location = new Float2(0f, y),
                Parent = _modal
            };

            Panel marker = new Panel
            {
                Width = 3f,
                Height = 40f,
                Location = new Float2(20f, 5f),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.6f,
                Parent = descPanel
            };

            _operationDesc = new Label
            {
                Text = "使用 WASD 键控制角色移动方向，按住 Shift 键可进入疾跑状态，按 Space 键跳跃。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                Location = new Float2(30f, 5f),
                Parent = descPanel
            };

            y += 50f;

            InkDivider divider2 = new InkDivider
            {
                Width = 300f,
                Height = 1f,
                Location = new Float2(20f, y),
                Parent = _modal
            };
            y += 16f;

            _martialGuideLink = new InkButton
            {
                Width = 340f,
                Height = 40f,
                Text = "前往武学引导",
                Location = new Float2(0f, y),
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _modal
            };
            _martialGuideLink.Clicked += () => MartialGuideRequested?.Invoke();

            y += 40f;

            _confirmButton = new InkButton
            {
                Width = 340f,
                Height = 38f,
                Text = "知道了",
                Location = new Float2(0f, y),
                Parent = _modal
            };
            _confirmButton.Clicked += () => GuideClosed?.Invoke();
        }

        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);

            if (_modal != null)
            {
                _modal.Location = new Float2((w - 340f) * 0.5f, (h - 520f) * 0.5f);
            }
        }

        public void RefreshAllData() { }

        public void OnPageEnter()
        {
            RefreshAllData();
        }

        public void OnPageLeave() { }

        public void OnPageUpdate() { }

        public void OnResolutionChanged()
        {
            _screenSize = FlaxEngine.Screen.Size;
            RefreshLayout();
        }

        public void BuildUI() { }

        public void RefreshBoundData() { }
    }
}