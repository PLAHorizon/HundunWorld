using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages
{
    public class MiniGameCompletePage : ContainerControl, IInkPage
    {
        private const float PanelWidth = 460f;
        private const float PanelHeight = 520f;

        private const float HeaderY = 20f;
        private const float TitleX = 20f;
        private const float TitleY = 30f;
        private const float TitleWidth = 420f;
        private const float TitleHeight = 40f;

        private const float GameNameY = 80f;
        private const float GameTagY = 82f;
        private const float GameTagX = 280f;

        private const float ScoreBlockY = 110f;
        private const float ScoreValueY = 130f;
        private const float ScoreValueSize = 48f;

        private const float GradeBlockY = 180f;
        private const float GradeLetterY = 200f;
        private const float GradeLetterSize = 44f;
        private const float StarsX = 200f;
        private const float StarsY = 210f;

        private const float RewardsBlockY = 260f;
        private const float RewardsTitleY = 265f;
        private const float RewardItemStartY = 295f;
        private const float RewardItemHeight = 50f;
        private const float RewardItemSpacing = 8f;

        private const float BestScoreY = 425f;

        private const float ActionsY = 455f;
        private const float ClaimButtonX = 60f;
        private const float ReplayButtonX = 240f;
        private const float ButtonWidth = 160f;
        private const float ButtonHeight = 44f;

        private InkPanelElevated _panel;
        private InkTextBlock _titleText;
        private InkButton _closeButton;
        private InkTextBlock _gameNameText;
        private InkTag _gameTag;
        private InkTextBlock _scoreLabel;
        private InkTextBlock _scoreValue;
        private InkTag _newRecordTag;
        private InkTextBlock _gradeLabel;
        private InkTextBlock _gradeLetter;
        private StarControl[] _stars;
        private InkTextBlock _rewardsTitle;
        private RewardItemControl[] _rewardItems;
        private InkTextBlock _bestScoreText;
        private InkButton _claimButton;
        private InkButton _replayButton;

        private Float2 _screenSize;

        public MiniGameCompletePage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                _panel = new InkPanelElevated
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelWidth, PanelHeight),
                };
                AddChild(_panel);

                BuildCornerDecorations();
                BuildAccentLine();
                BuildTitleArea();
                BuildDivider(1);
                BuildGameNameArea();
                BuildScoreBlock();
                BuildGradeBlock();
                BuildDivider(2);
                BuildRewardsBlock();
                BuildBestScore();
                BuildActions();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MiniGameCompletePage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildCornerDecorations()
        {
            InkCornerDeco deco = new InkCornerDeco
            {
                Size = _panel.Size,
                Location = Float2.Zero,
                Parent = _panel
            };
        }

        private void BuildAccentLine()
        {
            AccentLineControl line = new AccentLineControl
            {
                Size = new Float2(140f, 2f),
                Location = new Float2((PanelWidth - 140f) * 0.5f, 0f),
                Parent = _panel
            };
        }

        private void BuildTitleArea()
        {
            _titleText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "小游戏完成",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX, TitleY),
                Size = new Float2(TitleWidth, TitleHeight),
                HorizontalAlignment = TextAlignment.Center,
            };
            _panel.AddChild(_titleText);

            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "×",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelWidth - 36f, TitleY),
                Size = new Float2(32f, 32f),
            };
            _closeButton.ButtonClicked += OnCloseButtonClicked;
            _panel.AddChild(_closeButton);
        }

        private void BuildDivider(int idx)
        {
            float y = idx == 1 ? 75f : 250f;
            InkDividerControl divider = new InkDividerControl
            {
                Size = new Float2(PanelWidth - 64f, 1f),
                Location = new Float2(32f, y),
                Parent = _panel
            };
        }

        private void BuildGameNameArea()
        {
            _gameNameText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "飞花令",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX, GameNameY),
                Size = new Float2(200f, 28f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextDefault,
            };
            _panel.AddChild(_gameNameText);

            _gameTag = new InkTag
            {
                TagVariant = InkTagVariant.Brand,
                Text = "诗词",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(GameTagX, GameTagY),
                Size = new Float2(60f, 24f),
            };
            _panel.AddChild(_gameTag);
        }

        private void BuildScoreBlock()
        {
            _scoreLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "得分",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX, ScoreBlockY),
                Size = new Float2(TitleWidth, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextTertiary,
            };
            _panel.AddChild(_scoreLabel);

            _scoreValue = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "8,650",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX, ScoreValueY),
                Size = new Float2(TitleWidth, ScoreValueSize),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, ScoreValueSize),
            };
            _panel.AddChild(_scoreValue);

            _newRecordTag = new InkTag
            {
                // 设计方案：荣誉成就用鎏金(Brand)而非朱红(Vermilion)，朱红仅限战斗/危险/扣减场景
                TagVariant = InkTagVariant.Brand,
                Text = "新纪录",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX + 250f, ScoreValueY + 10f),
                Size = new Float2(80f, 24f),
            };
            _panel.AddChild(_newRecordTag);
        }

        private void BuildGradeBlock()
        {
            _gradeLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "评级",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX, GradeBlockY),
                Size = new Float2(TitleWidth, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextTertiary,
            };
            _panel.AddChild(_gradeLabel);

            _gradeLetter = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "甲",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX + 80f, GradeLetterY),
                Size = new Float2(GradeLetterSize, GradeLetterSize),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, GradeLetterSize),
            };
            _panel.AddChild(_gradeLetter);

            _stars = new StarControl[5];
            float starStartX = StarsX;
            float starSize = 18f;
            float starSpacing = 6f;
            for (int i = 0; i < 5; i++)
            {
                _stars[i] = new StarControl
                {
                    Size = new Float2(starSize, starSize),
                    Location = new Float2(starStartX + i * (starSize + starSpacing), StarsY),
                    Parent = _panel,
                    IsFilled = true,
                    Delay = 0.5f + i * 0.08f,
                };
            }
        }

        private void BuildRewardsBlock()
        {
            _rewardsTitle = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "获得奖励",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX, RewardsTitleY),
                Size = new Float2(TitleWidth, 24f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_rewardsTitle);

            _rewardItems = new RewardItemControl[3];

            _rewardItems[0] = new RewardItemControl
            {
                Size = new Float2(PanelWidth - 64f, RewardItemHeight),
                Location = new Float2(32f, RewardItemStartY),
                Parent = _panel,
                Label = "修为经验",
                Tier = "良",
                Quantity = "×3,000",
                Quality = InkWashTheme.InkQuality.Uncommon,
            };

            _rewardItems[1] = new RewardItemControl
            {
                Size = new Float2(PanelWidth - 64f, RewardItemHeight),
                Location = new Float2(32f, RewardItemStartY + RewardItemHeight + RewardItemSpacing),
                Parent = _panel,
                Label = "铜钱",
                Tier = "珍",
                Quantity = "×1,500",
                Quality = InkWashTheme.InkQuality.Rare,
            };

            _rewardItems[2] = new RewardItemControl
            {
                Size = new Float2(PanelWidth - 64f, RewardItemHeight),
                Location = new Float2(32f, RewardItemStartY + (RewardItemHeight + RewardItemSpacing) * 2f),
                Parent = _panel,
                Label = "诗词残卷",
                Tier = "传",
                Quantity = "×2",
                Quality = InkWashTheme.InkQuality.Legendary,
            };
        }

        private void BuildBestScore()
        {
            _bestScoreText = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "最高记录: 8,650",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX, BestScoreY),
                Size = new Float2(TitleWidth, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextTertiary,
            };
            _panel.AddChild(_bestScoreText);
        }

        private void BuildActions()
        {
            _claimButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "领取奖励",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ClaimButtonX, ActionsY),
                Size = new Float2(ButtonWidth, ButtonHeight),
            };
            _claimButton.ButtonClicked += OnClaimButtonClicked;
            _panel.AddChild(_claimButton);

            _replayButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Lg,
                Text = "再来一局",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ReplayButtonX, ActionsY),
                Size = new Float2(ButtonWidth, ButtonHeight),
            };
            _replayButton.ButtonClicked += OnReplayButtonClicked;
            _panel.AddChild(_replayButton);
        }

        public event Action Claimed;
        public event Action Replay;
        public event Action Closed;

        private void OnClaimButtonClicked(Button button)
        {
            Claimed?.Invoke();
        }

        private void OnReplayButtonClicked(Button button)
        {
            Replay?.Invoke();
        }

        private void OnCloseButtonClicked(Button button)
        {
            Closed?.Invoke();
        }

        public void SetGameInfo(string gameName, string gameTag)
        {
            if (_gameNameText != null)
                _gameNameText.Text = gameName ?? string.Empty;
            if (_gameTag != null)
                _gameTag.Text = gameTag ?? string.Empty;
        }

        public void SetScore(int score, bool isNewRecord)
        {
            if (_scoreValue != null)
                _scoreValue.Text = score.ToString("N0");
            if (_newRecordTag != null)
                _newRecordTag.Visible = isNewRecord;
            if (_bestScoreText != null)
                _bestScoreText.Text = $"最高记录: {score.ToString("N0")}";
        }

        public void SetGrade(char grade, int stars)
        {
            if (_gradeLetter != null)
                _gradeLetter.Text = grade.ToString();
            if (_stars != null)
            {
                for (int i = 0; i < _stars.Length; i++)
                {
                    if (_stars[i] != null)
                        _stars[i].IsFilled = i < stars;
                }
            }
        }

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - PanelHeight) * 0.5f);
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

        private class AccentLineControl : ContainerControl
        {
            public AccentLineControl()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (Width <= 0f || Height <= 0f)
                    return;

                float centerX = Width * 0.5f;
                float halfWidth = Width * 0.5f;

                Color startColor = Color.Transparent;
                Color midColor = InkWashTheme.GoldBright;
                Color endColor = Color.Transparent;

                Render2D.FillRectangle(new Rectangle(0, 0, centerX, Height),
                    new Color(startColor.R, startColor.G, startColor.B, startColor.A));
                Render2D.FillRectangle(new Rectangle(centerX - halfWidth * 0.5f, 0, halfWidth, Height),
                    new Color(midColor.R, midColor.G, midColor.B, midColor.A));
                Render2D.FillRectangle(new Rectangle(centerX + halfWidth * 0.5f, 0, centerX, Height),
                    new Color(endColor.R, endColor.G, endColor.B, endColor.A));

                float gradientStep = 1f / Width;
                for (int i = 0; i < (int)Width; i++)
                {
                    float t = (float)i / Width;
                    float alpha;
                    if (t < 0.5f)
                        alpha = (t / 0.5f) * 1f;
                    else
                        alpha = ((1f - t) / 0.5f) * 1f;

                    Color lineColor = new Color(
                        InkWashTheme.GoldBright.R,
                        InkWashTheme.GoldBright.G,
                        InkWashTheme.GoldBright.B,
                        alpha);

                    Render2D.FillRectangle(new Rectangle(i, 0, 1f, Height), lineColor);
                }
            }
        }

        private class StarControl : ContainerControl
        {
            private bool _isFilled;
            private float _delay;
            private float _time;
            private bool _animatedIn;

            public bool IsFilled
            {
                get => _isFilled;
                set => _isFilled = value;
            }

            public float Delay
            {
                get => _delay;
                set => _delay = value;
            }

            public StarControl()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);

                if (!_animatedIn)
                {
                    _time += deltaTime;
                    if (_time >= _delay)
                    {
                        _animatedIn = true;
                    }
                }
            }

            public override void Draw()
            {
                base.Draw();

                if (Width <= 0f || Height <= 0f)
                    return;

                float scale = _animatedIn ? 1f : 0f;
                if (!_animatedIn && _time >= _delay)
                {
                    float progress = Mathf.Min(1f, (_time - _delay) / 0.2f);
                    scale = progress;
                }

                var center = new Float2(Width * 0.5f, Height * 0.5f);
                float radius = Mathf.Min(Width, Height) * 0.35f * scale;

                Color color = _isFilled ? InkWashTheme.GoldBright : InkWashTheme.TextDisabled;

                int points = 5;
                for (int i = 0; i < points * 2; i++)
                {
                    float angle = (float)i / (points * 2) * Mathf.Pi * 2f - Mathf.Pi * 0.5f;
                    float r = i % 2 == 0 ? radius : radius * 0.5f;
                    Float2 p = center + new Float2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);

                    if (i == 0)
                        Render2D.DrawLine(center, p, color, 1.5f);
                    else
                        Render2D.DrawLine(
                            center + new Float2(
                                Mathf.Cos(((float)(i - 1) / (points * 2) * Mathf.Pi * 2f - Mathf.Pi * 0.5f)) * (i % 2 == 1 ? radius : radius * 0.5f),
                                Mathf.Sin(((float)(i - 1) / (points * 2) * Mathf.Pi * 2f - Mathf.Pi * 0.5f)) * (i % 2 == 1 ? radius : radius * 0.5f)),
                            p, color, 1.5f);
                }

                if (_isFilled)
                {
                    Float2[] starPoints = new Float2[points];
                    for (int i = 0; i < points; i++)
                    {
                        float angle = (float)i / points * Mathf.Pi * 2f - Mathf.Pi * 0.5f;
                        starPoints[i] = center + new Float2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                    }

                    Color fillColor = new Color(color.R, color.G, color.B, 0.3f);
                    for (int i = 0; i < points; i++)
                    {
                        int next = (i + 1) % points;
                        Render2D.FillTriangle(center, starPoints[i], starPoints[next], fillColor);
                    }
                }
            }
        }

        private class RewardItemControl : ContainerControl
        {
            private string _label;
            private string _tier;
            private string _quantity;
            private InkWashTheme.InkQuality _quality;

            public string Label
            {
                get => _label;
                set => _label = value;
            }

            public string Tier
            {
                get => _tier;
                set => _tier = value;
            }

            public string Quantity
            {
                get => _quantity;
                set => _quantity = value;
            }

            public InkWashTheme.InkQuality Quality
            {
                get => _quality;
                set => _quality = value;
            }

            public RewardItemControl()
            {
                BackgroundColor = new Color(0f, 0f, 0f, 0.35f);
                ClipChildren = false;
            }

            public override void Draw()
            {
                base.Draw();

                if (Width <= 0f || Height <= 0f)
                    return;

                Color borderColor = InkWashTheme.BorderNeutralL2;
                if (_quality == InkWashTheme.InkQuality.Legendary)
                {
                    borderColor = InkWashTheme.BorderVermilion;
                }

                Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), borderColor, 1f);

                float iconSize = 32f;
                float iconX = 12f;
                float iconY = (Height - iconSize) * 0.5f;

                Color qualityColor = InkWashTheme.QualityColor(_quality);
                Color qualityBg = new Color(qualityColor.R, qualityColor.G, qualityColor.B, 0.12f);

                Render2D.FillRectangle(new Rectangle(iconX, iconY, iconSize, iconSize), qualityBg);
                Render2D.DrawRectangle(new Rectangle(iconX, iconY, iconSize, iconSize), qualityColor, 1f);

                float labelX = iconX + iconSize + 12f;
                float labelY = (Height - 20f) * 0.5f;

                var labelFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (labelFont != null)
                {
                    Render2D.DrawText(labelFont, _label ?? string.Empty,
                        new Rectangle(labelX, labelY, 100f, 20f),
                        InkWashTheme.TextDefault, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }

                float tierX = labelX + 120f;
                float tierY = (Height - 16f) * 0.5f + 2f;

                float tierWidth = 30f;
                float tierHeight = 16f;
                Render2D.FillRectangle(new Rectangle(tierX, tierY, tierWidth, tierHeight), qualityBg);
                Render2D.DrawRectangle(new Rectangle(tierX, tierY, tierWidth, tierHeight), qualityColor, 1f);

                var tierFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 10f).GetFont();
                if (tierFont != null)
                {
                    Render2D.DrawText(tierFont, _tier ?? string.Empty,
                        new Rectangle(tierX, tierY, tierWidth, tierHeight),
                        qualityColor, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

                float quantityX = Width - 80f;
                float quantityY = (Height - 20f) * 0.5f;

                var quantityFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f).GetFont();
                if (quantityFont != null)
                {
                    Render2D.DrawText(quantityFont, _quantity ?? string.Empty,
                        new Rectangle(quantityX, quantityY, 80f, 20f),
                        InkWashTheme.TextBrand, TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }
            }
        }

        private class InkDividerControl : ContainerControl
        {
            public InkDividerControl()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            public override void Draw()
            {
                base.Draw();

                if (Width <= 0f || Height <= 0f)
                    return;

                for (int x = 0; x < (int)Width; x++)
                {
                    float t = (float)x / Width;
                    float alpha;
                    if (t < 0.2f)
                        alpha = t / 0.2f * 0.12f;
                    else if (t > 0.8f)
                        alpha = (1f - t) / 0.2f * 0.12f;
                    else
                        alpha = 0.12f;

                    Color color = new Color(
                        InkWashTheme.GoldPrimary.R,
                        InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B,
                        alpha);

                    Render2D.FillRectangle(new Rectangle(x, 0, 1f, Height), color);
                }
            }
        }
    }
}