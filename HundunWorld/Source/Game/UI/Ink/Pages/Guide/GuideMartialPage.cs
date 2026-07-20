using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Guide
{
    public class GuideMartialPage : Panel, IInkPage
    {
        private Float2 _screenSize;

        private InkPanel _modal;
        private InkButton _closeButton;

        private Label _guideTitle;
        private InkPanel _skillIcon;
        private Label _skillName;

        private InkPanel[] _comboSteps;
        private InkButton _practiceButton;
        private InkButton _multiplayerButton;

        private Label _timingNote;

        public event Action GuideClosed;
        public event Action PracticeRequested;
        public event Action MultiplayerRequested;

        private string[] _stepNumbers = { "壹", "贰", "叁" };
        private string[] _stepTitles = { "起手", "潜行", "突袭" };
        private string[] _stepKeys = { "Q", "长按 Q", "松开 Q" };
        private string[] _stepDescs = {
            "按 Q 键释放遁地术，潜入地下。",
            "长按 Q 键保持潜行状态，可躲避敌人视线。",
            "松开 Q 键跃出地面，对周围敌人造成突袭伤害。"
        };

        public GuideMartialPage()
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
                FlaxEngine.Debug.LogError($"[GuideMartialPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildModal()
        {
            _modal = new InkPanel
            {
                Width = 380f,
                Height = 580f,
                Location = new Float2((_screenSize.X - 380f) * 0.5f, (_screenSize.Y - 580f) * 0.5f),
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
                Width = 380f,
                Height = 48f,
                Location = new Float2(0, y),
                Parent = _modal
            };

            Panel decoLine = new Panel
            {
                Width = 3f,
                Height = 18f,
                BackgroundColor = InkWashTheme.GoldBright,
                Location = new Float2(12f, 15f),
                Parent = headerPanel
            };

            _guideTitle = new Label
            {
                Text = "奇术引导",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(20f, 14f),
                Parent = headerPanel
            };

            _closeButton = new InkButton
            {
                Width = 28f,
                Height = 28f,
                Text = "×",
                Location = new Float2(340f, 10f),
                BackgroundColor = Color.Transparent,
                TextColor = InkWashTheme.TextTertiary,
                Parent = headerPanel
            };
            _closeButton.Clicked += () => GuideClosed?.Invoke();

            y += 48f;

            InkDivider divider1 = new InkDivider
            {
                Width = 340f,
                Height = 1f,
                Location = new Float2(20f, y),
                Parent = _modal
            };
            y += 16f;

            Panel infoPanel = new Panel
            {
                Width = 380f,
                Height = 64f,
                Location = new Float2(0f, y),
                Parent = _modal
            };

            _skillIcon = new InkPanel
            {
                Width = 48f,
                Height = 48f,
                Location = new Float2(20f, 8f),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.18f,
                Parent = infoPanel
            };

            Label skillChar = new Label
            {
                Text = "遁",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 26f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _skillIcon
            };

            Panel metaPanel = new Panel
            {
                Width = 300f,
                Height = 64f,
                Location = new Float2(76f, 0f),
                Parent = infoPanel
            };

            _skillName = new Label
            {
                Text = "遁地术",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                Location = new Float2(0f, 8f),
                Parent = metaPanel
            };

            InkPanel skillTag = new InkPanel
            {
                Width = 80f,
                Height = 20f,
                Location = new Float2(0f, 32f),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.12f,
                Parent = metaPanel
            };

            Label tagLabel = new Label
            {
                Text = "奇术 · 主动",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = skillTag
            };

            y += 64f;

            InkDivider divider2 = new InkDivider
            {
                Width = 340f,
                Height = 1f,
                Location = new Float2(20f, y),
                Parent = _modal
            };
            y += 16f;

            Panel sectionPanel = new Panel
            {
                Width = 380f,
                Height = 280f,
                Location = new Float2(0f, y),
                Parent = _modal
            };

            Label subtitle = new Label
            {
                Text = "连招步骤",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                Location = new Float2(20f, 0f),
                Parent = sectionPanel
            };

            _comboSteps = new InkPanel[3];

            float stepY = 30f;
            for (int i = 0; i < 3; i++)
            {
                _comboSteps[i] = new InkPanel
                {
                    Width = 340f,
                    Height = 72f,
                    Location = new Float2(20f, stepY),
                    BackgroundColor = InkWashTheme.BaseSecondary,
                    Parent = sectionPanel
                };

                Panel leftLine = new Panel
                {
                    Width = 3f,
                    Height = 72f,
                    BackgroundColor = i == 2 ? InkWashTheme.VermilionPrimary : InkWashTheme.GoldBright * 0.6f,
                    Location = new Float2(0, 0),
                    Parent = _comboSteps[i]
                };

                InkPanel stepNum = new InkPanel
                {
                    Width = 26f,
                    Height = 26f,
                    Location = new Float2(8f, 10f),
                    BackgroundColor = i == 2 ? InkWashTheme.VermilionPrimary * 0.2f : InkWashTheme.GoldPrimary * 0.18f,
                    Parent = _comboSteps[i]
                };

                Label numLabel = new Label
                {
                    Text = _stepNumbers[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = i == 2 ? InkWashTheme.VermilionBright : InkWashTheme.GoldBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = stepNum
                };

                Label stepTitle = new Label
                {
                    Text = _stepTitles[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    Location = new Float2(40f, 12f),
                    Parent = _comboSteps[i]
                };

                InkPanel keyHint = new InkPanel
                {
                    Width = 60f,
                    Height = 24f,
                    Location = new Float2(270f, 10f),
                    BackgroundColor = i == 2 ? InkWashTheme.VermilionPrimary * 0.18f : InkWashTheme.BaseElevated,
                    Parent = _comboSteps[i]
                };

                Label keyLabel = new Label
                {
                    Text = _stepKeys[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = i == 2 ? InkWashTheme.VermilionBright : InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = keyHint
                };

                Label stepDesc = new Label
                {
                    Text = _stepDescs[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Near,
                    Location = new Float2(40f, 38f),
                    Parent = _comboSteps[i]
                };

                stepY += 72f;

                if (i < 2)
                {
                    Panel arrowPanel = new Panel
                    {
                        Width = 340f,
                        Height = 20f,
                        Location = new Float2(0f, stepY),
                        Parent = sectionPanel
                    };

                    Panel arrowLine = new Panel
                    {
                        Width = 1f,
                        Height = 12f,
                        BackgroundColor = InkWashTheme.GoldDeep,
                        Location = new Float2(170f, 4f),
                        Parent = arrowPanel
                    };
                    stepY += 20f;
                }
            }

            y += 280f;

            InkDivider divider3 = new InkDivider
            {
                Width = 340f,
                Height = 1f,
                Location = new Float2(20f, y),
                Parent = _modal
            };
            y += 16f;

            Panel timingSection = new Panel
            {
                Width = 380f,
                Height = 60f,
                Location = new Float2(0f, y),
                Parent = _modal
            };

            Label timingSubtitle = new Label
            {
                Text = "释放时机",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                Location = new Float2(20f, 0f),
                Parent = timingSection
            };

            _timingNote = new Label
            {
                Text = "在敌人攻击时释放遁地术，可避开伤害并反击。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Near,
                Location = new Float2(20f, 24f),
                Parent = timingSection
            };

            y += 60f;

            InkDivider divider4 = new InkDivider
            {
                Width = 340f,
                Height = 1f,
                Location = new Float2(20f, y),
                Parent = _modal
            };
            y += 16f;

            Panel actionPanel = new Panel
            {
                Width = 380f,
                Height = 44f,
                Location = new Float2(0f, y),
                Parent = _modal
            };

            _practiceButton = new InkButton
            {
                Width = 180f,
                Height = 40f,
                Text = "开始练习",
                Location = new Float2(20f, 2f),
                Parent = actionPanel
            };
            _practiceButton.Clicked += () => PracticeRequested?.Invoke();

            _multiplayerButton = new InkButton
            {
                Width = 180f,
                Height = 40f,
                Text = "联机切磋",
                Location = new Float2(180f, 2f),
                BackgroundColor = Color.Transparent,
                Parent = actionPanel
            };
            _multiplayerButton.Clicked += () => MultiplayerRequested?.Invoke();
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
                _modal.Location = new Float2((w - 380f) * 0.5f, (h - 580f) * 0.5f);
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