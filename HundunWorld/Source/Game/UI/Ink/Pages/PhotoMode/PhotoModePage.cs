using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System.Threading.Tasks;

namespace HundunWorld.Game.UI.Ink.Pages.PhotoMode
{
    public class PhotoModePage : Panel, IInkPage
    {
        private Float2 _screenSize;

        private InkButton _exitButton;
        private Label _titleLabel;
        private Label _subtitleLabel;

        private Panel _viewfinderFrame;
        private InkBar _fovSlider;
        private InkBar _exposureSlider;
        private InkBar _focalSlider;

        private Label _fovValue;
        private Label _exposureValue;
        private Label _focalValue;

        private InkButton _angleButton;
        private InkButton _shutterButton;
        private InkButton _albumButton;
        private Label _albumCount;

        private Panel _flashLayer;

        private InkPanel _filterPanel;
        private InkPanel _cameraPanel;
        private InkPanel _bottomBar;

        private readonly string[] _filterNames = { "原图", "水墨", "复古", "明快", "暗调" };
        private InkPanel[] _filterItems;
        private Label[] _filterLabels;
        private int _selectedFilter = 0;

        public event Action ExitRequested;
        public event Action ShutterClicked;
        public event Action AngleClicked;
        public event Action AlbumClicked;

        public PhotoModePage()
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
                BuildTopBar();
                BuildViewfinder();
                BuildFilterPanel();
                BuildCameraPanel();
                BuildBottomBar();
                BuildFlashLayer();
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PhotoModePage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildTopBar()
        {
            var topBar = new Panel
            {
                Width = _screenSize.X,
                Height = 60f,
                Location = new Float2(0, 0),
                BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.8f),
                Parent = this
            };

            _exitButton = new InkButton
            {
                Text = "退出拍照",
                Width = 100f,
                Height = 36f,
                Location = new Float2(32f, 12f),
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.TextSecondary,
                Parent = topBar
            };
            _exitButton.Clicked += () => ExitRequested?.Invoke();

            _titleLabel = new Label
            {
                Text = "拍照模式",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Width = 120f,
                Height = 24f,
                Location = new Float2(_screenSize.X * 0.5f - 60f, 10f),
                Parent = topBar
            };

            _subtitleLabel = new Label
            {
                Text = "PHOTO MODE",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Center,
                Width = 80f,
                Height = 16f,
                Location = new Float2(_screenSize.X * 0.5f - 40f, 34f),
                Parent = topBar
            };

            var settingsButton = new InkButton
            {
                Text = "",
                Width = 36f,
                Height = 36f,
                Location = new Float2(_screenSize.X - 68f, 12f),
                BackgroundColor = Color.Transparent,
                Parent = topBar
            };
        }

        private void BuildViewfinder()
        {
            _viewfinderFrame = new Panel
            {
                Width = 800f,
                Height = 500f,
                Location = new Float2(_screenSize.X * 0.5f - 400f, _screenSize.Y * 0.5f - 250f),
                Parent = this
            };

            var cornerTL = new InkPanel
            {
                Width = 28f,
                Height = 28f,
                Location = new Float2(0, 0),
                BackgroundColor = Color.Transparent,
                Parent = _viewfinderFrame
            };

            var cornerTR = new InkPanel
            {
                Width = 28f,
                Height = 28f,
                Location = new Float2(_viewfinderFrame.Width - 28f, 0),
                BackgroundColor = Color.Transparent,
                Parent = _viewfinderFrame
            };

            var cornerBL = new InkPanel
            {
                Width = 28f,
                Height = 28f,
                Location = new Float2(0, _viewfinderFrame.Height - 28f),
                BackgroundColor = Color.Transparent,
                Parent = _viewfinderFrame
            };

            var cornerBR = new InkPanel
            {
                Width = 28f,
                Height = 28f,
                Location = new Float2(_viewfinderFrame.Width - 28f, _viewfinderFrame.Height - 28f),
                BackgroundColor = Color.Transparent,
                Parent = _viewfinderFrame
            };

            var infoBar = new Panel
            {
                Width = _viewfinderFrame.Width,
                Height = 28f,
                Location = new Float2(0, _viewfinderFrame.Height - 28f),
                BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.6f),
                Parent = _viewfinderFrame
            };

            var apertureLabel = new Label
            {
                Text = "f/2.8",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Width = 60f,
                Height = 28f,
                Location = new Float2(16f, 0f),
                Parent = infoBar
            };

            var timerLabel = new Label
            {
                Text = "1/125s",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Width = 60f,
                Height = 28f,
                Location = new Float2(100f, 0f),
                Parent = infoBar
            };

            var isoLabel = new Label
            {
                Text = "ISO 400",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Width = 60f,
                Height = 28f,
                Location = new Float2(180f, 0f),
                Parent = infoBar
            };

            var hudTag = new Panel
            {
                Width = 120f,
                Height = 24f,
                Location = new Float2(12f, 12f),
                BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.6f),
                Parent = _viewfinderFrame
            };

            var locationLabel = new Label
            {
                Text = "燕云州 · 落霞峰",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Width = 120f,
                Height = 24f,
                Parent = hudTag
            };
        }

        private void BuildFilterPanel()
        {
            _filterPanel = new InkPanel
            {
                Width = 180f,
                Height = 300f,
                Location = new Float2(20f, _screenSize.Y * 0.5f - 150f),
                Parent = this
            };

            var header = new Panel
            {
                Width = 180f,
                Height = 40f,
                Location = new Float2(0, 0),
                Parent = _filterPanel
            };

            var headerLabel = new Label
            {
                Text = "滤镜",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Width = 80f,
                Height = 40f,
                Location = new Float2(16f, 0f),
                Parent = header
            };

            _filterItems = new InkPanel[_filterNames.Length];
            _filterLabels = new Label[_filterNames.Length];

            for (int i = 0; i < _filterNames.Length; i++)
            {
                _filterItems[i] = new InkPanel
                {
                    Width = 180f,
                    Height = 44f,
                    Location = new Float2(0, 40f + i * 44f),
                    BackgroundColor = i == _selectedFilter ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f) : Color.Transparent,
                    Parent = _filterPanel
                };

                _filterLabels[i] = new Label
                {
                    Text = _filterNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = i == _selectedFilter ? InkWashTheme.TextBrand : InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Width = 160f,
                    Height = 44f,
                    Location = new Float2(16f, 0f),
                    Parent = _filterItems[i]
                };
            }

            var footer = new Panel
            {
                Width = 180f,
                Height = 40f,
                Location = new Float2(0, 260f),
                Parent = _filterPanel
            };
        }

        private void BuildCameraPanel()
        {
            _cameraPanel = new InkPanel
            {
                Width = 180f,
                Height = 320f,
                Location = new Float2(_screenSize.X - 200f, _screenSize.Y * 0.5f - 160f),
                Parent = this
            };

            var header = new Panel
            {
                Width = 180f,
                Height = 40f,
                Location = new Float2(0, 0),
                Parent = _cameraPanel
            };

            var headerLabel = new Label
            {
                Text = "相机参数",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Width = 160f,
                Height = 40f,
                Location = new Float2(16f, 0f),
                Parent = header
            };

            float y = 50f;

            var fovLabel = new Label
            {
                Text = "焦距",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperAged,
                Width = 60f,
                Height = 24f,
                Location = new Float2(16f, y),
                Parent = _cameraPanel
            };

            _fovSlider = new InkBar
            {
                Width = 140f,
                Height = 6f,
                FillVariant = InkBarFillVariant.Gold,
                Value = 0.5f,
                Location = new Float2(20f, y + 28f),
                Parent = _cameraPanel
            };

            _fovValue = new Label
            {
                Text = "50mm",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextBrand,
                Width = 60f,
                Height = 24f,
                Location = new Float2(120f, y),
                Parent = _cameraPanel
            };

            y += 56f;

            var exposureLabel = new Label
            {
                Text = "曝光",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperAged,
                Width = 60f,
                Height = 24f,
                Location = new Float2(16f, y),
                Parent = _cameraPanel
            };

            _exposureSlider = new InkBar
            {
                Width = 140f,
                Height = 6f,
                FillVariant = InkBarFillVariant.Gold,
                Value = 0.5f,
                Location = new Float2(20f, y + 28f),
                Parent = _cameraPanel
            };

            _exposureValue = new Label
            {
                Text = "0.0",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextBrand,
                Width = 60f,
                Height = 24f,
                Location = new Float2(120f, y),
                Parent = _cameraPanel
            };

            y += 56f;

            var focalLabel = new Label
            {
                Text = "对焦",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperAged,
                Width = 60f,
                Height = 24f,
                Location = new Float2(16f, y),
                Parent = _cameraPanel
            };

            _focalSlider = new InkBar
            {
                Width = 140f,
                Height = 6f,
                FillVariant = InkBarFillVariant.Gold,
                Value = 0.5f,
                Location = new Float2(20f, y + 28f),
                Parent = _cameraPanel
            };

            _focalValue = new Label
            {
                Text = "3.5m",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.TextBrand,
                Width = 60f,
                Height = 24f,
                Location = new Float2(120f, y),
                Parent = _cameraPanel
            };

            y += 70f;

            var timeLabel = new Label
            {
                Text = "时辰: 巳时",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                Width = 160f,
                Height = 20f,
                Location = new Float2(16f, y),
                Parent = _cameraPanel
            };

            var weatherLabel = new Label
            {
                Text = "天候: 晴",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                Width = 160f,
                Height = 20f,
                Location = new Float2(16f, y + 20f),
                Parent = _cameraPanel
            };

            var windLabel = new Label
            {
                Text = "风向: 东风",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                Width = 160f,
                Height = 20f,
                Location = new Float2(16f, y + 40f),
                Parent = _cameraPanel
            };
        }

        private void BuildBottomBar()
        {
            _bottomBar = new InkPanel
            {
                Width = 600f,
                Height = 80f,
                Location = new Float2(_screenSize.X * 0.5f - 300f, _screenSize.Y - 110f),
                BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.8f),
                Parent = this
            };

            _angleButton = new InkButton
            {
                Text = "角度",
                Width = 80f,
                Height = 40f,
                Location = new Float2(40f, 20f),
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.TextSecondary,
                Parent = _bottomBar
            };
            _angleButton.Clicked += () => AngleClicked?.Invoke();

            _shutterButton = new InkButton
            {
                Text = "",
                Width = 72f,
                Height = 72f,
                Location = new Float2(264f, 4f),
                BackgroundColor = InkWashTheme.PaperBright,
                BorderColor = InkWashTheme.GoldPrimary,
                BorderThickness = 4f,
                Parent = _bottomBar
            };
            _shutterButton.Clicked += OnShutterClicked;

            _albumButton = new InkButton
            {
                Text = "",
                Width = 48f,
                Height = 48f,
                Location = new Float2(508f, 16f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f),
                Parent = _bottomBar
            };
            _albumButton.Clicked += () => AlbumClicked?.Invoke();

            _albumCount = new Label
            {
                Text = "12",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                TextColor = InkWashTheme.GoldPrimary,
                Width = 20f,
                Height = 20f,
                Location = new Float2(540f, 12f),
                Parent = _bottomBar
            };
        }

        private void BuildFlashLayer()
        {
            _flashLayer = new Panel
            {
                Width = _screenSize.X,
                Height = _screenSize.Y,
                Location = new Float2(0, 0),
                BackgroundColor = new Color(1f, 1f, 1f, 0f),
                Visible = false,
                Parent = this
            };
        }

        private void OnShutterClicked()
        {
            ShutterClicked?.Invoke();

            _flashLayer.Visible = true;
            _flashLayer.BackgroundColor = new Color(1f, 1f, 1f, 1f);

            Task.Run(async () =>
            {
                await Task.Delay(100);
                _flashLayer.Visible = false;
                _flashLayer.BackgroundColor = new Color(1f, 1f, 1f, 0f);
            });
        }

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_titleLabel != null)
            {
                _titleLabel.Location = new Float2(sw * 0.5f - 60f, 10f);
                _titleLabel.Size = new Float2(120f, 24f);
            }

            if (_subtitleLabel != null)
            {
                _subtitleLabel.Location = new Float2(sw * 0.5f - 40f, 34f);
                _subtitleLabel.Size = new Float2(80f, 16f);
            }

            if (_viewfinderFrame != null)
            {
                _viewfinderFrame.Location = new Float2(sw * 0.5f - 400f, sh * 0.5f - 250f);
            }

            if (_filterPanel != null)
            {
                _filterPanel.Location = new Float2(20f, sh * 0.5f - 150f);
            }

            if (_cameraPanel != null)
            {
                _cameraPanel.Location = new Float2(sw - 200f, sh * 0.5f - 160f);
            }

            if (_bottomBar != null)
            {
                _bottomBar.Location = new Float2(sw * 0.5f - 300f, sh - 110f);
            }
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