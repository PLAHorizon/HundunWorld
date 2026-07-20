using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.ChapterTransition
{
    public class ChapterTransitionPage : Panel, IInkPage
    {
        private Float2 _screenSize;

        private Panel _bgLayer;
        private Panel _vignette;

        private Panel _watermark;
        private Label _watermarkIcon;
        private Label _watermarkText;

        private Panel _splash1;
        private Panel _splash2;

        private Panel _centerContainer;

        private Panel _actContainer;
        private Panel _actLineLeft;
        private Label _actText1;
        private Label _actNum;
        private Label _actText2;
        private Panel _actLineRight;

        private Label _chapterTitle;
        private InkPanel _seal;
        private Label _sealText;
        private Label _chapterTitleEn;

        private Panel _dividerDeco;
        private Panel _dividerLine1;
        private Panel _dividerDiamond;
        private Panel _dividerLine2;

        private Label _chapterDesc;

        private InkButton _enterButton;

        private Panel _bottomHint;
        private Label _hintIcon;
        private Label _hintText1;
        private Label _hintBlink;
        private Label _hintText2;

        public event Action EnterWorldRequested;

        public ChapterTransitionPage()
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
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[ChapterTransitionPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildLayout()
        {
            _bgLayer = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            _vignette = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = new Color(0, 0, 0, 0.55f),
                Parent = this
            };

            _watermark = new Panel
            {
                Width = 160f,
                Height = 36f,
                Location = new Float2(48f, 32f),
                Parent = this
            };

            _watermarkIcon = new Label
            {
                Text = "卷",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                TextColor = InkWashTheme.GoldPrimary,
                Width = 24f,
                Height = 36f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(0, 0),
                Parent = _watermark
            };

            _watermarkText = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                TextColor = InkWashTheme.GoldPrimary,
                Width = 120f,
                Height = 36f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(30f, 0),
                Parent = _watermark
            };

            _splash1 = new Panel
            {
                Width = 320f,
                Height = 320f,
                Location = new Float2(_screenSize.X * 0.1f, _screenSize.Y * 0.15f),
                BackgroundColor = Color.Transparent,
                Parent = this
            };

            _splash2 = new Panel
            {
                Width = 280f,
                Height = 280f,
                Location = new Float2(_screenSize.X * 0.72f, _screenSize.Y * 0.6f),
                BackgroundColor = Color.Transparent,
                Parent = this
            };

            _centerContainer = new Panel
            {
                Width = 600f,
                Height = 500f,
                Location = new Float2(_screenSize.X * 0.5f - 300f, _screenSize.Y * 0.5f - 250f),
                Parent = this
            };

            _actContainer = new Panel
            {
                Width = 240f,
                Height = 40f,
                Location = new Float2(_centerContainer.Width * 0.5f - 120f, 0),
                Parent = _centerContainer
            };

            _actLineLeft = new Panel
            {
                Width = 60f,
                Height = 1f,
                Location = new Float2(0, 19.5f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.5f),
                Parent = _actContainer
            };

            _actText1 = new Label
            {
                Text = "第",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextSecondary,
                Width = 24f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(76f, 10f),
                Parent = _actContainer
            };

            _actNum = new Label
            {
                Text = "壹",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 16f),
                TextColor = InkWashTheme.GoldBright,
                Width = 24f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(106f, 10f),
                Parent = _actContainer
            };

            _actText2 = new Label
            {
                Text = "章",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextSecondary,
                Width = 24f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(136f, 10f),
                Parent = _actContainer
            };

            _actLineRight = new Panel
            {
                Width = 60f,
                Height = 1f,
                Location = new Float2(180f, 19.5f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.5f),
                Parent = _actContainer
            };

            Panel titleWrap = new Panel
            {
                Width = 400f,
                Height = 90f,
                Location = new Float2(_centerContainer.Width * 0.5f - 200f, 52f),
                Parent = _centerContainer
            };

            _chapterTitle = new Label
            {
                Text = "初入江湖",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 72f),
                TextColor = InkWashTheme.PaperBright,
                Width = 340f,
                Height = 80f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(0, 0),
                Parent = titleWrap
            };

            _seal = new InkPanel
            {
                Width = 64f,
                Height = 64f,
                Location = new Float2(320f, -10f),
                BackgroundColor = new Color(InkWashTheme.VermilionPrimary.R, InkWashTheme.VermilionPrimary.G, InkWashTheme.VermilionPrimary.B, 0.08f),
                Parent = titleWrap
            };

            _sealText = new Label
            {
                Text = "混",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = InkWashTheme.VermilionPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _seal
            };

            _chapterTitleEn = new Label
            {
                Text = "CHAPTER I · INTO THE JIANGHU",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 300f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(_centerContainer.Width * 0.5f - 150f, 140f),
                Parent = _centerContainer
            };

            _dividerDeco = new Panel
            {
                Width = 180f,
                Height = 12f,
                Location = new Float2(_centerContainer.Width * 0.5f - 90f, 172f),
                Parent = _centerContainer
            };

            _dividerLine1 = new Panel
            {
                Width = 80f,
                Height = 1f,
                Location = new Float2(0, 5.5f),
                BackgroundColor = InkWashTheme.BorderGold,
                Parent = _dividerDeco
            };

            _dividerDiamond = new Panel
            {
                Width = 8f,
                Height = 8f,
                Location = new Float2(86f, 2f),
                BackgroundColor = InkWashTheme.GoldPrimary,
                Parent = _dividerDeco
            };

            _dividerLine2 = new Panel
            {
                Width = 80f,
                Height = 1f,
                Location = new Float2(98f, 5.5f),
                BackgroundColor = InkWashTheme.BorderGold,
                Parent = _dividerDeco
            };

            _chapterDesc = new Label
            {
                Text = "天地混沌初开，江湖风起云涌。\n少年仗剑而出，踏入这烽烟四起的乱世。\n前路未知，刀光剑影间，且看你如何书写自己的传奇。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextSecondary,
                Width = 520f,
                Height = 84f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(_centerContainer.Width * 0.5f - 260f, 200f),
                Parent = _centerContainer
            };

            _enterButton = new InkButton
            {
                Width = 200f,
                Height = 52f,
                Text = "进入世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                BackgroundColor = InkWashTheme.GoldPrimary,
                TextColor = InkWashTheme.TextOnBrand,
                Location = new Float2(_centerContainer.Width * 0.5f - 100f, 300f),
                Parent = _centerContainer
            };
            _enterButton.Clicked += () => EnterWorldRequested?.Invoke();

            _bottomHint = new Panel
            {
                Width = 300f,
                Height = 20f,
                Location = new Float2(_screenSize.X * 0.5f - 150f, _screenSize.Y - 32f),
                Parent = this
            };

            _hintIcon = new Label
            {
                Text = "✦",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 18f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(0, 0),
                Parent = _bottomHint
            };

            _hintText1 = new Label
            {
                Text = "点击进入世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 100f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(22f, 0),
                Parent = _bottomHint
            };

            _hintBlink = new Label
            {
                Text = "·",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 12f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(128f, 0),
                Parent = _bottomHint
            };

            _hintText2 = new Label
            {
                Text = "混沌初开 · 万象更新",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Width = 150f,
                Height = 20f,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(144f, 0),
                Parent = _bottomHint
            };
        }

        public void RefreshLayout()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }
            Size = _screenSize;

            _bgLayer.Size = _screenSize;
            _vignette.Size = _screenSize;

            _splash1.Location = new Float2(_screenSize.X * 0.1f, _screenSize.Y * 0.15f);
            _splash2.Location = new Float2(_screenSize.X * 0.72f, _screenSize.Y * 0.6f);

            _centerContainer.Location = new Float2(_screenSize.X * 0.5f - 300f, _screenSize.Y * 0.5f - 250f);

            _bottomHint.Location = new Float2(_screenSize.X * 0.5f - 150f, _screenSize.Y - 32f);
        }

        public void BuildUI() { }

        public void RefreshBoundData() { }
    }
}