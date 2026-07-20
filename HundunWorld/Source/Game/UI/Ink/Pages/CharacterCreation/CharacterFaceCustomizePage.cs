using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.CharacterCreation
{
    public class CharacterFaceCustomizePage : Panel, IInkPage
    {
        private Float2 _screenSize;

        private Panel _previewArea;
        private Label _previewLabel;
        private Label _previewHint;

        private InkPanel _characterSilhouette;

        private Panel _rotateControls;

        private InkPanel _customizePanel;

        private Label _panelTitle;

        private Panel _tabs;
        private InkButton[] _tabItems;
        private Label[] _tabLabels;
        private int _selectedTab = 0;

        private Panel _tabBody;

        private InkButton[] _presetItems;
        private Label[] _presetLabels;
        private int _selectedPreset = 0;

        private Panel[] _sliderRows;
        private InkBar[] _sliders;
        private Label[] _sliderLabels;
        private Label[] _sliderValues;

        private InkButton[] _colorSwatches;

        private InkButton _randomButton;
        private InkButton _enterButton;

        private readonly string[] _tabNames = { "脸型", "发型", "眉眼", "妆容" };
        private readonly string[] _sliderNames = { "脸宽", "下巴", "颧骨", "额头", "眉骨" };
        private readonly float[] _sliderValuesDefault = { 55f, 40f, 68f, 50f, 35f };
        private readonly Color[] _skinColors =
        {
            new Color(245f/255f, 240f/255f, 232f/255f, 1f),
            new Color(235f/255f, 229f/255f, 216f/255f, 1f),
            new Color(212f/255f, 201f/255f, 184f/255f, 1f),
            new Color(200f/255f, 187f/255f, 168f/255f, 1f),
            new Color(168f/255f, 158f/255f, 138f/255f, 1f),
            new Color(122f/255f, 74f/255f, 32f/255f, 1f)
        };

        public event Action RandomRequested;
        public event Action EnterGameRequested;

        public CharacterFaceCustomizePage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.BaseDefault;
            ClipChildren = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildLayout();
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterFaceCustomizePage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildLayout()
        {
            var layout = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            _previewArea = new Panel
            {
                Width = _screenSize.X * 0.5f,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = layout
            };

            _previewLabel = new Label
            {
                Text = "角色预览",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Near,
                Location = new Float2(_previewArea.Width * 0.5f - 60, 40f),
                Parent = _previewArea
            };

            _previewHint = new Label
            {
                Text = "拖动旋转查看角色",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Near,
                Location = new Float2(_previewArea.Width * 0.5f - 60, 80f),
                Parent = _previewArea
            };

            _characterSilhouette = new InkPanel
            {
                Width = 300f,
                Height = 500f,
                Location = new Float2(_previewArea.Width * 0.5f - 150, _previewArea.Height * 0.5f - 250),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f),
                Parent = _previewArea
            };

            _rotateControls = new Panel
            {
                Width = 120f,
                Height = 36f,
                Location = new Float2(_previewArea.Width * 0.5f - 60, _previewArea.Height - 80f),
                Parent = _previewArea
            };

            Label rotateLeft = new Label
            {
                Text = "<",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 24f),
                TextColor = InkWashTheme.GoldPrimary,
                Width = 36f,
                Height = 36f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(0, 0),
                Parent = _rotateControls
            };

            Label rotateRight = new Label
            {
                Text = ">",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 24f),
                TextColor = InkWashTheme.GoldPrimary,
                Width = 36f,
                Height = 36f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(84f, 0),
                Parent = _rotateControls
            };

            _customizePanel = new InkPanel
            {
                Width = _screenSize.X * 0.5f,
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = InkWashTheme.Panel,
                Parent = layout
            };

            _panelTitle = new Label
            {
                Text = "捏脸",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Near,
                Location = new Float2(_customizePanel.Width * 0.5f - 30, 20f),
                Parent = _customizePanel
            };

            _tabs = new Panel
            {
                Width = _customizePanel.Width,
                Height = 40f,
                Location = new Float2(0, 60f),
                Parent = _customizePanel
            };

            _tabItems = new InkButton[4];
            _tabLabels = new Label[4];

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _tabItems[i] = new InkButton
                {
                    Width = _tabs.Width / 4,
                    Height = 40f,
                    Location = new Float2(i * (_tabs.Width / 4), 0),
                    Text = "",
                    BackgroundColor = i == 0 ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f) : Color.Transparent,
                    Parent = _tabs
                };
                _tabItems[i].Clicked += () => SelectTab(idx);

                _tabLabels[i] = new Label
                {
                    Text = _tabNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _tabItems[i]
                };
            }

            _tabBody = new Panel
            {
                Width = _customizePanel.Width,
                Height = _customizePanel.Height - 160f,
                Location = new Float2(0, 100f),
                Parent = _customizePanel
            };

            var presetTitle = new Label
            {
                Text = "预设",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(20f, 20f),
                Parent = _tabBody
            };

            _presetItems = new InkButton[4];
            _presetLabels = new Label[4];
            string[] presetNames = { "俊秀", "英武", "清雅", "豪迈" };

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _presetItems[i] = new InkButton
                {
                    Width = 80f,
                    Height = 80f,
                    Location = new Float2(20f + i * 90f, 50f),
                    Text = "",
                    BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.05f),
                    BorderColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.BorderGold,
                    BorderThickness = i == 0 ? 2f : 1f,
                    Parent = _tabBody
                };
                _presetItems[i].Clicked += () => SelectPreset(idx);

                _presetLabels[i] = new Label
                {
                    Text = presetNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Far,
                    Location = new Float2(0, 60f),
                    Parent = _presetItems[i]
                };
            }

            var sliderTitle = new Label
            {
                Text = "调整",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(20f, 170f),
                Parent = _tabBody
            };

            _sliderRows = new Panel[5];
            _sliders = new InkBar[5];
            _sliderLabels = new Label[5];
            _sliderValues = new Label[5];

            for (int i = 0; i < 5; i++)
            {
                _sliderRows[i] = new Panel
                {
                    Width = _tabBody.Width - 40f,
                    Height = 36f,
                    Location = new Float2(20f, 200f + i * 36f),
                    Parent = _tabBody
                };

                _sliderLabels[i] = new Label
                {
                    Text = _sliderNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Width = 56f,
                    Location = new Float2(0f, 0f),
                    Parent = _sliderRows[i]
                };

                _sliders[i] = new InkBar
                {
                    Width = 220f,
                    Height = 6f,
                    FillVariant = InkBarFillVariant.Gold,
                    Value = _sliderValuesDefault[i] / 100f,
                    Location = new Float2(66f, 15f),
                    Parent = _sliderRows[i]
                };

                _sliderValues[i] = new Label
                {
                    Text = $"{(int)_sliderValuesDefault[i]}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = InkWashTheme.TextBrand,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    Width = 36f,
                    Location = new Float2(300f, 0f),
                    Parent = _sliderRows[i]
                };
            }

            var colorSection = new Panel
            {
                Width = _tabBody.Width,
                Height = 80f,
                Location = new Float2(0, 400f),
                Parent = _tabBody
            };

            var colorTitle = new Label
            {
                Text = "肤色",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(20f, 0f),
                Parent = colorSection
            };

            var colorLabel = new Label
            {
                Text = "底色",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Width = 56f,
                Location = new Float2(20f, 30f),
                Parent = colorSection
            };

            _colorSwatches = new InkButton[6];

            for (int i = 0; i < 6; i++)
            {
                _colorSwatches[i] = new InkButton
                {
                    Width = 22f,
                    Height = 22f,
                    Text = "",
                    BackgroundColor = _skinColors[i],
                    BorderColor = InkWashTheme.BorderGold,
                    BorderThickness = i == 0 ? 2f : 1f,
                    Location = new Float2(86f + i * 28f, 30f),
                    Parent = colorSection
                };
            }

            var footer = new Panel
            {
                Width = _customizePanel.Width,
                Height = 56f,
                Location = new Float2(0, _customizePanel.Height - 56f),
                BackgroundColor = new Color(0f, 0f, 0f, 0.2f),
                Parent = _customizePanel
            };

            _randomButton = new InkButton
            {
                Text = "随机面相",
                Width = 170f,
                Height = 36f,
                Location = new Float2(20f, 10f),
                BackgroundColor = Color.Transparent,
                BorderColor = InkWashTheme.BorderGold,
                TextColor = InkWashTheme.TextSecondary,
                Parent = footer
            };
            _randomButton.Clicked += () => RandomRequested?.Invoke();

            _enterButton = new InkButton
            {
                Text = "进入江湖",
                Width = 170f,
                Height = 36f,
                Location = new Float2(_customizePanel.Width - 190f, 10f),
                BackgroundColor = InkWashTheme.GoldPrimary,
                BorderColor = InkWashTheme.GoldPrimary,
                TextColor = InkWashTheme.TextOnBrand,
                Parent = footer
            };
            _enterButton.Clicked += () => EnterGameRequested?.Invoke();
        }

        private void SelectTab(int index)
        {
            _selectedTab = index;
            for (int i = 0; i < 4; i++)
            {
                if (i == index)
                {
                    _tabItems[i].BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f);
                    _tabLabels[i].TextColor = InkWashTheme.GoldBright;
                }
                else
                {
                    _tabItems[i].BackgroundColor = Color.Transparent;
                    _tabLabels[i].TextColor = InkWashTheme.TextSecondary;
                }
            }
        }

        private void SelectPreset(int index)
        {
            _selectedPreset = index;
            for (int i = 0; i < 4; i++)
            {
                if (i == index)
                {
                    _presetItems[i].BorderColor = InkWashTheme.GoldBright;
                    _presetItems[i].BorderThickness = 2f;
                }
                else
                {
                    _presetItems[i].BorderColor = InkWashTheme.BorderGold;
                    _presetItems[i].BorderThickness = 1f;
                }
            }
        }

        public void ApplyLayout()
        {
            _customizePanel.Location = new Float2(_screenSize.X * 0.5f, 0);
            _customizePanel.Size = new Float2(_screenSize.X * 0.5f, _screenSize.Y);

            _previewArea.Size = new Float2(_screenSize.X * 0.5f, _screenSize.Y);
        }

        public void RefreshLayout()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }
            Size = _screenSize;
            ApplyLayout();
        }

        public void BuildUI() { }

        public void RefreshBoundData() { }
    }
}