using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.CharacterCreation
{
    public class CharacterNamingPage : Panel, IInkPage
    {
        private Float2 _screenSize;

        private Panel _bgLayer;
        private Panel _vignette;

        private InkButton _backButton;

        private Panel _stepIndicator;
        private InkPanel[] _stepNumbers;
        private Label[] _stepLabels;
        private Panel[] _stepDividers;

        private InkPaperPanel _namingPanel;
        private InkPanel _titleColumn;
        private Label _verticalTitle;
        private Label _titleSub;
        private InkPanel _seal;
        private Label _sealText;

        private Panel _formColumn;
        private InkPanel _paperSeal;
        private Label _paperSealText;

        private Label _nameLabel;
        private InkPanel _nameInputWrap;
        private TextBox _nameInput;
        private Label _nameCount;
        private Label _nameHint;

        private Label _genderLabel;
        private Panel _genderOptions;
        private InkButton[] _genderOpts;
        private Label[] _genderIcons;
        private Label[] _genderLabels;
        private int _selectedGender = 0;

        private Label _factionLabel;
        private Panel _factionGrid;
        private InkButton[] _factionOpts;
        private Label[] _factionNameLabels;
        private Label[] _factionDescLabels;
        private int _selectedFaction = 0;

        private InkPanel _actions;
        private InkButton _randomButton;
        private Label _randomIcon;
        private Label _randomText;
        private InkButton _nextButton;

        private Label _bottomHint;

        private readonly string[] _factionNameStrings = { "江湖散人", "少林俗家", "武当记名" };
        private readonly string[] _factionDescStrings = { "无门无派", "金刚怒目", "太极归元" };
        private readonly string[] _surnames = { "李", "王", "张", "赵", "萧", "楚", "林", "陈", "叶", "苏" };
        private readonly string[] _givenNames = { "忘生", "长风", "无极", "青衫", "惊鸿", "孤云", "听雨", "凌霄", "落尘", "问剑" };

        public string CharacterName { get; private set; } = "李忘生";
        public int Gender => _selectedGender;
        public int Faction => _selectedFaction;

        public event Action BackRequested;
        public event Action NextRequested;
        public event Action<string> NameChanged;

        public CharacterNamingPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildBackground();
                BuildBackButton();
                BuildStepIndicator();
                BuildNamingPanel();
                BuildBottomHint();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterNamingPage] 初始化失败: {ex.Message}");
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

            _bgLayer.Size = _screenSize;
            _vignette.Size = _screenSize;

            _backButton.Location = new Float2(20, 20);

            float stepCenterX = _screenSize.X * 0.5f;
            _stepIndicator.Location = new Float2(stepCenterX - 160, 28);

            float panelWidth = Math.Min(720, _screenSize.X * 0.92f);
            float panelHeight = 420;
            _namingPanel.Location = new Float2(_screenSize.X * 0.5f - panelWidth * 0.5f, _screenSize.Y * 0.5f - panelHeight * 0.5f);
            _namingPanel.Size = new Float2(panelWidth, panelHeight);

            float titleColWidth = 100;
            _titleColumn.Size = new Float2(titleColWidth, panelHeight);

            float formColWidth = panelWidth - titleColWidth;
            _formColumn.Size = new Float2(formColWidth, panelHeight);
            _formColumn.Location = new Float2(titleColWidth, 0);

            _bottomHint.Location = new Float2(_screenSize.X * 0.5f - 200, _screenSize.Y - 24);
        }

        public void BuildUI() { }

        private void BuildBackground()
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
        }

        private void BuildBackButton()
        {
            _backButton = new InkButton
            {
                Size = new Float2(40, 40),
                Location = new Float2(20, 20),
                Text = "",
                BackgroundColor = InkWashTheme.Panel,
                Parent = this
            };
            _backButton.Clicked += () => BackRequested?.Invoke();

            Label icon = new Label
            {
                Text = "<",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 20),
                TextColor = InkWashTheme.GoldPrimary,
                AnchorPreset = AnchorPresets.StretchAll,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _backButton
            };
        }

        private void BuildStepIndicator()
        {
            _stepIndicator = new Panel
            {
                Size = new Float2(320, 32),
                Location = new Float2(_screenSize.X * 0.5f - 160, 28),
                Parent = this
            };

            _stepNumbers = new InkPanel[3];
            _stepLabels = new Label[3];
            _stepDividers = new Panel[2];

            string[] steps = { "命名", "捏脸", "入门" };
            float offsetX = 0;

            for (int i = 0; i < 3; i++)
            {
                bool isActive = i == 0;

                Panel stepWrap = new Panel
                {
                    Size = new Float2(80, 32),
                    Location = new Float2(offsetX, 0),
                    Parent = _stepIndicator
                };

                _stepNumbers[i] = new InkPanel
                {
                    Size = isActive ? new Float2(24, 24) : new Float2(22, 22),
                    Location = isActive ? new Float2(28, 0) : new Float2(29, 1),
                    BackgroundColor = isActive ? InkWashTheme.GoldPrimary : Color.Transparent,
                    Parent = stepWrap
                };

                Label numLabel = new Label
                {
                    Text = (i + 1).ToString(),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12),
                    TextColor = isActive ? InkWashTheme.TextOnBrand : InkWashTheme.TextTertiary,
                    AnchorPreset = AnchorPresets.StretchAll,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = _stepNumbers[i]
                };

                _stepLabels[i] = new Label
                {
                    Text = steps[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13),
                    TextColor = isActive ? InkWashTheme.GoldPrimary : InkWashTheme.TextTertiary,
                    Location = new Float2(40, 26),
                    Width = 40,
                    Height = 14,
                    Parent = stepWrap
                };

                offsetX += 80;

                if (i < 2)
                {
                    _stepDividers[i] = new Panel
                    {
                        Size = new Float2(32, 1),
                        Location = new Float2(offsetX, 15),
                        BackgroundColor = InkWashTheme.BorderGold,
                        Parent = _stepIndicator
                    };
                    offsetX += 32;
                }
            }
        }

        private void BuildNamingPanel()
        {
            _namingPanel = new InkPaperPanel
            {
                Size = new Float2(720, 420),
                Location = new Float2(_screenSize.X * 0.5f - 360, _screenSize.Y * 0.5f - 210),
                Parent = this
            };

            AddCornerDecorations();
            BuildTitleColumn();
            BuildFormColumn();
        }

        private void AddCornerDecorations()
        {
            InkCornerDeco deco = new InkCornerDeco
            {
                Size = _namingPanel.Size,
                Location = Float2.Zero,
                Parent = _namingPanel
            };
        }

        private void BuildTitleColumn()
        {
            _titleColumn = new InkPanel
            {
                Size = new Float2(100, 420),
                Location = new Float2(0, 0),
                BackgroundColor = new Color(InkWashTheme.PaperDark.R, InkWashTheme.PaperDark.G, InkWashTheme.PaperDark.B, 0.08f),
                Parent = _namingPanel
            };

            _verticalTitle = new Label
            {
                Text = "命名",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 36),
                TextColor = InkWashTheme.GoldDeep,
                AnchorPreset = AnchorPresets.StretchAll,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _titleColumn
            };

            _titleSub = new Label
            {
                Text = "混沌初开",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12),
                TextColor = InkWashTheme.PaperDark,
                Location = new Float2(50, 140),
                Width = 50,
                Height = 14,
                Parent = _titleColumn
            };

            _seal = new InkPanel
            {
                Size = new Float2(44, 44),
                Location = new Float2(28, 200),
                BackgroundColor = new Color(192f / 255f, 57f / 255f, 43f / 255f, 0.06f),
                Parent = _titleColumn
            };

            _sealText = new Label
            {
                Text = "混",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20),
                TextColor = InkWashTheme.VermilionPrimary,
                AnchorPreset = AnchorPresets.StretchAll,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _seal
            };
        }

        private void BuildFormColumn()
        {
            _formColumn = new Panel
            {
                Size = new Float2(620, 420),
                Location = new Float2(100, 0),
                Parent = _namingPanel
            };

            _paperSeal = new InkPanel
            {
                Size = new Float2(52, 52),
                Location = new Float2(_formColumn.Size.X - 72, 16),
                BackgroundColor = Color.Transparent,
                Parent = _formColumn
            };

            _paperSealText = new Label
            {
                Text = "燕云",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22),
                TextColor = new Color(InkWashTheme.VermilionDeep.R, InkWashTheme.VermilionDeep.G, InkWashTheme.VermilionDeep.B, 0.5f),
                AnchorPreset = AnchorPresets.StretchAll,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _paperSeal
            };

            float y = 32;

            _nameLabel = new Label
            {
                Text = "姓 名",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13),
                TextColor = InkWashTheme.PaperDark,
                Location = new Float2(36, y),
                Width = 60,
                Height = 16,
                Parent = _formColumn
            };

            y += 24;

            _nameInputWrap = new InkPanel
            {
                Size = new Float2(_formColumn.Size.X - 72, 40),
                Location = new Float2(36, y),
                BackgroundColor = new Color(1, 1, 1, 0.5f),
                Parent = _formColumn
            };

            _nameInput = new TextBox
            {
                Size = new Float2(_nameInputWrap.Size.X - 80, 40),
                Location = new Float2(14, 0),
                Text = "李忘生",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18),
                TextColor = InkWashTheme.TextOnPaper,
                BackgroundColor = Color.Transparent,
                MaxLength = 6,
                Parent = _nameInputWrap
            };
            _nameInput.TextChanged += () =>
            {
                CharacterName = _nameInput.Text;
                _nameCount.Text = $"{_nameInput.Text.Length}/6";
                NameChanged?.Invoke(CharacterName);
            };

            _nameCount = new Label
            {
                Text = "3/6",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12),
                TextColor = InkWashTheme.PaperDark,
                Location = new Float2(_nameInputWrap.Size.X - 60, 14),
                Width = 50,
                Height = 14,
                Parent = _nameInputWrap
            };

            y += 48;

            _nameHint = new Label
            {
                Text = "汉字二至六字，可为侠客取一个响彻江湖之名",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11),
                TextColor = InkWashTheme.PaperDark,
                Location = new Float2(36, y),
                Width = 300,
                Height = 14,
                Parent = _formColumn
            };

            y += 32;

            _genderLabel = new Label
            {
                Text = "性 别",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13),
                TextColor = InkWashTheme.PaperDark,
                Location = new Float2(36, y),
                Width = 60,
                Height = 16,
                Parent = _formColumn
            };

            y += 24;

            _genderOptions = new Panel
            {
                Size = new Float2(_formColumn.Size.X - 72, 60),
                Location = new Float2(36, y),
                Parent = _formColumn
            };

            _genderOpts = new InkButton[2];
            _genderIcons = new Label[2];
            _genderLabels = new Label[2];
            string[] genders = { "男", "女" };

            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                _genderOpts[i] = new InkButton
                {
                    Size = new Float2((_genderOptions.Size.X - 10) / 2, 60),
                    Location = new Float2(i * ((_genderOptions.Size.X - 10) / 2 + 10), 0),
                    Text = "",
                    BackgroundColor = new Color(1, 1, 1, 0.4f),
                    Parent = _genderOptions
                };
                _genderOpts[i].Clicked += () => SelectGender(idx);

                _genderIcons[i] = new Label
                {
                    Text = i == 0 ? "♂" : "♀",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 24),
                    TextColor = InkWashTheme.PaperDark,
                    Location = new Float2(_genderOpts[i].Size.X * 0.5f - 12, 8),
                    Width = 24,
                    Height = 28,
                    Parent = _genderOpts[i]
                };

                _genderLabels[i] = new Label
                {
                    Text = genders[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13),
                    TextColor = InkWashTheme.TextOnPaper,
                    Location = new Float2(_genderOpts[i].Size.X * 0.5f - 8, 40),
                    Width = 16,
                    Height = 16,
                    Parent = _genderOpts[i]
                };
            }

            SelectGender(0);

            y += 84;

            _factionLabel = new Label
            {
                Text = "出 身",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13),
                TextColor = InkWashTheme.PaperDark,
                Location = new Float2(36, y),
                Width = 60,
                Height = 16,
                Parent = _formColumn
            };

            y += 24;

            _factionGrid = new Panel
            {
                Size = new Float2(_formColumn.Size.X - 72, 56),
                Location = new Float2(36, y),
                Parent = _formColumn
            };

            _factionOpts = new InkButton[3];
            _factionNameLabels = new Label[3];
            _factionDescLabels = new Label[3];

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                _factionOpts[i] = new InkButton
                {
                    Size = new Float2((_factionGrid.Size.X - 16) / 3, 56),
                    Location = new Float2(i * ((_factionGrid.Size.X - 16) / 3 + 8), 0),
                    Text = "",
                    BackgroundColor = new Color(1, 1, 1, 0.4f),
                    Parent = _factionGrid
                };
                _factionOpts[i].Clicked += () => SelectFaction(idx);

                _factionNameLabels[i] = new Label
                {
                    Text = _factionNameStrings[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13),
                    TextColor = InkWashTheme.TextOnPaper,
                    Location = new Float2(_factionOpts[i].Size.X * 0.5f - 20, 8),
                    Width = 40,
                    Height = 16,
                    Parent = _factionOpts[i]
                };

                _factionDescLabels[i] = new Label
                {
                    Text = _factionDescStrings[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10),
                    TextColor = InkWashTheme.PaperDark,
                    Location = new Float2(_factionOpts[i].Size.X * 0.5f - 15, 30),
                    Width = 30,
                    Height = 14,
                    Parent = _factionOpts[i]
                };
            }

            SelectFaction(0);

            y += 80;

            _actions = new InkPanel
            {
                Size = new Float2(_formColumn.Size.X - 72, 44),
                Location = new Float2(36, y),
                BackgroundColor = Color.Transparent,
                Parent = _formColumn
            };

            _randomButton = new InkButton
            {
                Size = new Float2(100, 32),
                Location = new Float2(0, 6),
                Text = "",
                BackgroundColor = Color.Transparent,
                Parent = _actions
            };
            _randomButton.Clicked += GenerateRandomName;

            _randomIcon = new Label
            {
                Text = "⚅",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 16),
                TextColor = InkWashTheme.GoldDeep,
                Location = new Float2(0, 8),
                Width = 20,
                Height = 20,
                Parent = _randomButton
            };

            _randomText = new Label
            {
                Text = "天机赐名",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12),
                TextColor = InkWashTheme.GoldDeep,
                Location = new Float2(24, 8),
                Width = 80,
                Height = 16,
                Parent = _randomButton
            };

            _nextButton = new InkButton
            {
                Size = new Float2(140, 44),
                Location = new Float2(_actions.Size.X - 140, 0),
                Text = "下一步",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15),
                BackgroundColor = InkWashTheme.GoldPrimary,
                TextColor = InkWashTheme.TextOnBrand,
                Parent = _actions
            };
            _nextButton.Clicked += () => NextRequested?.Invoke();
        }

        private void BuildBottomHint()
        {
            _bottomHint = new Label
            {
                Text = "姓名将伴随你的整个江湖旅程，请慎重落笔",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11),
                TextColor = InkWashTheme.TextTertiary,
                Location = new Float2(_screenSize.X * 0.5f - 200, _screenSize.Y - 24),
                Width = 400,
                Height = 14,
                Parent = this
            };
        }

        private void SelectGender(int index)
        {
            _selectedGender = index;
            for (int i = 0; i < 2; i++)
            {
                if (i == index)
                {
                    _genderOpts[i].BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f);
                    _genderOpts[i].BorderColor = InkWashTheme.GoldPrimary;
                    _genderOpts[i].BorderThickness = 1f;
                    _genderIcons[i].TextColor = InkWashTheme.GoldDeep;
                }
                else
                {
                    _genderOpts[i].BackgroundColor = new Color(1, 1, 1, 0.4f);
                    _genderOpts[i].BorderColor = Color.Transparent;
                    _genderOpts[i].BorderThickness = 0f;
                    _genderIcons[i].TextColor = InkWashTheme.PaperDark;
                }
            }
        }

        private void SelectFaction(int index)
        {
            _selectedFaction = index;
            for (int i = 0; i < 3; i++)
            {
                if (i == index)
                {
                    _factionOpts[i].BackgroundColor = new Color(InkWashTheme.VermilionPrimary.R, InkWashTheme.VermilionPrimary.G, InkWashTheme.VermilionPrimary.B, 0.08f);
                    _factionOpts[i].BorderColor = InkWashTheme.VermilionPrimary;
                    _factionOpts[i].BorderThickness = 1f;
                    _factionNameLabels[i].TextColor = InkWashTheme.VermilionDeep;
                }
                else
                {
                    _factionOpts[i].BackgroundColor = new Color(1, 1, 1, 0.4f);
                    _factionOpts[i].BorderColor = Color.Transparent;
                    _factionOpts[i].BorderThickness = 0f;
                    _factionNameLabels[i].TextColor = InkWashTheme.TextOnPaper;
                }
            }
        }

        private void GenerateRandomName()
        {
            Random random = new Random();
            string surname = _surnames[random.Next(_surnames.Length)];
            string givenName = _givenNames[random.Next(_givenNames.Length)];
            _nameInput.Text = surname + givenName;
        }

        public void RefreshBoundData() { }
    }
}