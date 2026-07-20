using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.ElementVision
{
    public class ElementVisionPage : InkPanel, IInkPage
    {
        private Float2 _screenSize;
        
        private Panel _visionOverlay;
        private Panel _visionVignette;
        
        private Panel _timerRing;
        private Label _timerLabel;
        private InkVerticalTitle _visionTitle;
        
        private InkPanel _statusBar;
        private Label _statusLabel;
        
        private InkButton _deathLink;
        
        private InkButton _exitButton;
        
        private readonly string[] _markerNames = { "残影刺客", "寒铁剑胚", "千年灵芝", "古碑残片" };
        private readonly string[] _markerQualities = { "传说", "史诗", "罕见", "稀有" };
        private readonly float[] _markerDistances = { 12.4f, 8.7f, 15.2f, 5.3f };
        private readonly Color[] _markerColors =
        {
            InkWashTheme.VermilionPrimary,
            InkWashTheme.GoldPrimary,
            InkWashTheme.JadeDeep,
            InkWashTheme.QualityRare
        };
        
        private Panel[] _visionMarkers;
        private InkPanel[] _markerGlows;
        private InkPanel[] _markerLabels;
        
        private InkPanel _visionListPanel;
        
        private float _remainingTime = 23f;
        private float _progress;
        
        public event Action ExitRequested;
        public event Action DeathScreenRequested;
        public event Action<int> MarkerSelected;
        
        public ElementVisionPage()
        {
            _screenSize = LoadingPageHelper.ResolveScreenSize();
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.BaseDefault;
            ClipChildren = false;
            Location = Float2.Zero;
            Size = _screenSize;
            
            BuildBackground();
            BuildOverlay();
            BuildTimer();
            BuildStatus();
            BuildMarkers();
            BuildVisionList();
            BuildExitButton();
            ApplyLayout();
        }
        
        private void BuildBackground()
        {
            var bgPanel = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this
            };
        }
        
        private void BuildOverlay()
        {
            _visionOverlay = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = new Color(45f/255f, 55f/255f, 65f/255f, 0.35f),
                Parent = this
            };
            
            _visionVignette = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = this
            };
        }
        
        private void BuildTimer()
        {
            var timerPanel = new Panel
            {
                Width = 100f,
                Height = 120f,
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = this
            };
            
            _timerRing = new Panel
            {
                Width = 80f,
                Height = 80f,
                AnchorPreset = AnchorPresets.TopCenter,
                Location = new Float2(-40f, 0f),
                Parent = timerPanel
            };
            
            var ringBg = new Panel
            {
                Width = 80f,
                Height = 80f,
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = Color.Transparent,
                Parent = _timerRing
            };
            
            _timerLabel = new Label
            {
                Text = "23",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 22f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _timerRing
            };
            
            _visionTitle = new InkVerticalTitle
            {
                Text = "元素视野",
                FontSize = 24f,
                AnchorPreset = AnchorPresets.TopCenter,
                Location = new Float2(-40f, 85f),
                Parent = timerPanel
            };
        }
        
        private void BuildStatus()
        {
            _statusBar = new InkPanel
            {
                Width = 180f,
                Height = 36f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.75f),
                Parent = this
            };
            
            _statusLabel = new Label
            {
                Text = "视野已激活 · 洞察",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _statusBar
            };
            
            _deathLink = new InkButton
            {
                Text = "阵亡",
                Width = 100f,
                Height = 30f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = new Color(InkWashTheme.VermilionPrimary.R, InkWashTheme.VermilionPrimary.G, InkWashTheme.VermilionPrimary.B, 0.15f),
                Parent = this
            };
            _deathLink.Clicked += () => DeathScreenRequested?.Invoke();
        }
        
        private void BuildMarkers()
        {
            _visionMarkers = new Panel[4];
            _markerGlows = new InkPanel[4];
            _markerLabels = new InkPanel[4];
            
            for (int i = 0; i < 4; i++)
            {
                _visionMarkers[i] = new Panel
                {
                    Width = 60f,
                    Height = 60f,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Parent = this
                };
                
                _markerGlows[i] = new InkPanel
                {
                    Width = 52f,
                    Height = 52f,
                    AnchorPreset = AnchorPresets.TopCenter,
                    Location = new Float2(-26f, 4f),
                    BackgroundColor = new Color(_markerColors[i].R, _markerColors[i].G, _markerColors[i].B, 0.55f),
                    Parent = _visionMarkers[i]
                };
                
                var core = new InkPanel
                {
                    Width = 18f,
                    Height = 18f,
                    AnchorPreset = AnchorPresets.StretchAll,
                    BackgroundColor = new Color(InkWashTheme.PaperBright.R, InkWashTheme.PaperBright.G, InkWashTheme.PaperBright.B, 0.08f),
                    Parent = _visionMarkers[i]
                };
                
                _markerLabels[i] = new InkPanel
                {
                    Width = 140f,
                    Height = 30f,
                    AnchorPreset = AnchorPresets.TopLeft,
                    BackgroundColor = new Color(InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.88f),
                    Parent = this
                };
                
                var nameLabel = new Label
                {
                    Text = _markerNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(8f, 0f),
                    Width = 70f,
                    Height = 30f,
                    Parent = _markerLabels[i]
                };
                
                var qualityTag = new InkPanel
                {
                    Width = 60f,
                    Height = 20f,
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(-70f, 5f),
                    BackgroundColor = new Color(_markerColors[i].R, _markerColors[i].G, _markerColors[i].B, 0.12f),
                    Parent = _markerLabels[i]
                };
                
                var qualityLabel = new Label
                {
                    Text = _markerQualities[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = _markerColors[i],
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = qualityTag
                };
            }
        }
        
        private void BuildVisionList()
        {
            _visionListPanel = new InkPanel
            {
                Width = 280f,
                Height = 200f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = new Color(InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.88f),
                Parent = this
            };
            
            var header = new Panel
            {
                Height = 36f,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Parent = _visionListPanel
            };
            
            var headerLabel = new Label
            {
                Text = "感知到 4 处异象",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = header
            };
            
            var divider = new Panel
            {
                Height = 1f,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                BackgroundColor = InkWashTheme.Divider,
                Location = new Float2(0f, 36f),
                Parent = _visionListPanel
            };
            
            for (int i = 0; i < 4; i++)
            {
                var listItem = new Panel
                {
                    Height = 40f,
                    AnchorPreset = AnchorPresets.HorizontalStretchTop,
                    BackgroundColor = Color.Transparent,
                    Location = new Float2(0f, 40f + i * 42f),
                    Parent = _visionListPanel
                };
                
                var nameLabel = new Label
                {
                    Text = _markerNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(16f, 0f),
                    Width = 150f,
                    Height = 40f,
                    Parent = listItem
                };
                
                var distLabel = new Label
                {
                    Text = $"{_markerDistances[i]}m",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                    TextColor = InkWashTheme.PaperDark,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.HorizontalStretchTop,
                    Parent = listItem
                };
            }
        }
        
        private void BuildExitButton()
        {
            _exitButton = new InkButton
            {
                Text = "V · 退出视野",
                Width = 150f,
                Height = 40f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.85f),
                Parent = this
            };
            _exitButton.Clicked += () => ExitRequested?.Invoke();
        }
        
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;
            
            if (_statusBar != null)
            {
                _statusBar.Location = new Float2(32f, 32f);
            }
            
            if (_deathLink != null)
            {
                _deathLink.Location = new Float2(32f, 76f);
            }
            
            if (_visionMarkers[0] != null)
            {
                _visionMarkers[0].Location = new Float2(sw * 0.31f - 30f, sh * 0.41f - 30f);
                _markerLabels[0].Location = new Float2(sw * 0.31f + 35f, sh * 0.41f - 15f);
            }
            
            if (_visionMarkers[1] != null)
            {
                _visionMarkers[1].Location = new Float2(sw * 0.63f - 30f, sh * 0.35f - 30f);
                _markerLabels[1].Location = new Float2(sw * 0.63f + 35f, sh * 0.35f - 15f);
            }
            
            if (_visionMarkers[2] != null)
            {
                _visionMarkers[2].Location = new Float2(sw * 0.42f - 30f, sh * 0.63f - 30f);
                _markerLabels[2].Location = new Float2(sw * 0.42f - 175f, sh * 0.63f - 15f);
            }
            
            if (_visionMarkers[3] != null)
            {
                _visionMarkers[3].Location = new Float2(sw * 0.73f - 30f, sh * 0.27f - 30f);
                _markerLabels[3].Location = new Float2(sw * 0.73f - 70f, sh * 0.27f + 35f);
            }
            
            if (_visionListPanel != null)
            {
                _visionListPanel.Location = new Float2(sw - 300f, sh - 220f);
            }
            
            if (_exitButton != null)
            {
                _exitButton.Location = new Float2(32f, sh - 60f);
            }
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
            ApplyLayout();
        }
        
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            
            _remainingTime -= deltaTime;
            if (_remainingTime < 0f)
                _remainingTime = 0f;
            
            _timerLabel.Text = $"{(int)Math.Ceiling(_remainingTime)}";
            _progress = _remainingTime / 23f;
        }

        public void BuildUI() { }

        public void RefreshBoundData() { }
    }
}