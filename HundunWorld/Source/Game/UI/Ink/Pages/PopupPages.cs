using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 获得物品弹窗。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 居中 <see cref="InkPanelElevated"/>（400x500），
    /// 内含标题区、旋转金环装饰、品质图标、物品名、品质/类型标签、数量、描述、确认/详情按钮。
    /// 通过 <see cref="Confirmed"/> 和 <see cref="ViewDetail"/> 事件通知外部。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class PopupItemAcquired : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        private const float PanelWidth = 400f;

        private const float TitleY = 24f;
        private const float IconSectionY = 80f;
        private const float IconSize = 160f;
        private const float IconRingOuterSize = 150f;
        private const float IconRingInnerSize = 134f;
        private const float IconContainerSize = 120f;

        private const float NameY = 260f;
        private const float BadgesY = 296f;
        private const float QuantityY = 328f;
        private const float DescriptionY = 356f;
        private const float ActionsY = 412f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        private InkPanelElevated _panel;
        private InkButton _closeButton;

        private InkTextBlock _titleText;
        private InkPanel _titleLineLeft;
        private InkPanel _titleLineRight;
        private InkPanel _titleDivider;

        private InkPanel _iconRingOuter;
        private InkPanel _iconRingInner;
        private InkPanel[] _ringDots;
        private InkPanel _iconContainer;
        private InkPanel _iconGlow;
        private InkTextBlock _iconCharacter;

        private InkTextBlock _nameText;

        private InkTag _qualityBadge;
        private InkTag _typeBadge;

        private InkTextBlock _quantityLabel;
        private InkTextBlock _quantityValue;

        private InkTextBlock _descriptionText;

        private InkPanel _actionDivider;
        private InkButton _confirmButton;
        private InkButton _detailButton;

        private InkTextBlock _footerHint;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        private Float2 _screenSize;

        // ===================================================================
        // 构造函数
        // =======================================================================

        public PopupItemAcquired()
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
                    Size = new Float2(PanelWidth, 480f),
                };
                AddChild(_panel);

                BuildCloseButton();
                BuildTitleSection();
                BuildIconSection();
                BuildNameSection();
                BuildBadgesSection();
                BuildQuantitySection();
                BuildDescriptionSection();
                BuildActions();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PopupItemAcquired] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 构造方法
        // =======================================================================

        private void BuildCloseButton()
        {
            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelWidth - 38f, -10f),
                Size = new Float2(28f, 28f),
            };
            _closeButton.ButtonClicked += OnCloseButtonClicked;
            _panel.AddChild(_closeButton);
        }

        private void BuildTitleSection()
        {
            _titleLineLeft = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, TitleY + 12f),
                Size = new Float2(60f, 1f),
                BackgroundColor = InkWashTheme.GoldDeep,
            };
            _panel.AddChild(_titleLineLeft);

            _titleText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "获得物品",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(100f, TitleY),
                Size = new Float2(200f, 24f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
            };
            _panel.AddChild(_titleText);

            _titleLineRight = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(308f, TitleY + 12f),
                Size = new Float2(60f, 1f),
                BackgroundColor = InkWashTheme.GoldDeep,
            };
            _panel.AddChild(_titleLineRight);

            _titleDivider = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 80f) * 0.5f, TitleY + 40f),
                Size = new Float2(80f, 1f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _panel.AddChild(_titleDivider);

            InkPanel dividerCenter = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 4f) * 0.5f, TitleY + 38f),
                Size = new Float2(4f, 4f),
                BackgroundColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(dividerCenter);
        }

        private void BuildIconSection()
        {
            float iconCenterX = (PanelWidth - IconSize) * 0.5f;

            _iconRingOuter = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(iconCenterX + (IconSize - IconRingOuterSize) * 0.5f, IconSectionY + (IconSize - IconRingOuterSize) * 0.5f),
                Size = new Float2(IconRingOuterSize, IconRingOuterSize),
                BackgroundColor = Color.Transparent,
            };
            _panel.AddChild(_iconRingOuter);

            _ringDots = new InkPanel[4];
            float dotSize = 6f;
            _ringDots[0] = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((IconRingOuterSize - dotSize) * 0.5f, -3f),
                Size = new Float2(dotSize, dotSize),
                BackgroundColor = InkWashTheme.GoldBright,
            };
            _iconRingOuter.AddChild(_ringDots[0]);

            _ringDots[1] = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((IconRingOuterSize - dotSize) * 0.5f, IconRingOuterSize),
                Size = new Float2(dotSize, dotSize),
                BackgroundColor = InkWashTheme.GoldBright,
            };
            _iconRingOuter.AddChild(_ringDots[1]);

            _ringDots[2] = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(-3f, (IconRingOuterSize - dotSize) * 0.5f),
                Size = new Float2(dotSize, dotSize),
                BackgroundColor = InkWashTheme.GoldBright,
            };
            _iconRingOuter.AddChild(_ringDots[2]);

            _ringDots[3] = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(IconRingOuterSize, (IconRingOuterSize - dotSize) * 0.5f),
                Size = new Float2(dotSize, dotSize),
                BackgroundColor = InkWashTheme.GoldBright,
            };
            _iconRingOuter.AddChild(_ringDots[3]);

            _iconRingInner = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(iconCenterX + (IconSize - IconRingInnerSize) * 0.5f, IconSectionY + (IconSize - IconRingInnerSize) * 0.5f),
                Size = new Float2(IconRingInnerSize, IconRingInnerSize),
                BackgroundColor = Color.Transparent,
            };
            _panel.AddChild(_iconRingInner);

            float containerX = iconCenterX + (IconSize - IconContainerSize) * 0.5f;
            float containerY = IconSectionY + (IconSize - IconContainerSize) * 0.5f;

            _iconContainer = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(containerX, containerY),
                Size = new Float2(IconContainerSize, IconContainerSize),
                BackgroundColor = InkWashTheme.GoldDeep,
            };
            _panel.AddChild(_iconContainer);

            _iconGlow = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((IconContainerSize - 80f) * 0.5f, (IconContainerSize - 80f) * 0.5f),
                Size = new Float2(80f, 80f),
                BackgroundColor = new Color(InkWashTheme.GoldBright.R, InkWashTheme.GoldBright.G, InkWashTheme.GoldBright.B, 0.25f),
            };
            _iconContainer.AddChild(_iconGlow);

            _iconCharacter = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "剑",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(IconContainerSize, IconContainerSize),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 48f),
            };
            _iconContainer.AddChild(_iconCharacter);
        }

        private void BuildNameSection()
        {
            _nameText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "青锋剑·寒光",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, NameY),
                Size = new Float2(PanelWidth - 64f, 32f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 22f),
            };
            _panel.AddChild(_nameText);
        }

        private void BuildBadgesSection()
        {
            _qualityBadge = new InkTag
            {
                Text = "传世",
                // 设计方案§品质色阶：Legendary=#C8A858（鎏金），用Brand变体而非Vermilion
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 140f) * 0.5f, BadgesY),
                Size = new Float2(60f, 24f),
            };
            _panel.AddChild(_qualityBadge);

            InkPanel separator = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 140f) * 0.5f + 68f, BadgesY + 5f),
                Size = new Float2(1f, 14f),
                BackgroundColor = InkWashTheme.TextTertiary,
            };
            _panel.AddChild(separator);

            _typeBadge = new InkTag
            {
                Text = "长剑",
                TagVariant = InkTagVariant.Default,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 140f) * 0.5f + 76f, BadgesY),
                Size = new Float2(60f, 24f),
            };
            _panel.AddChild(_typeBadge);
        }

        private void BuildQuantitySection()
        {
            _quantityLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "数量",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 100f) * 0.5f, QuantityY),
                Size = new Float2(40f, 20f),
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(_quantityLabel);

            _quantityValue = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "× 1",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 100f) * 0.5f + 44f, QuantityY),
                Size = new Float2(56f, 20f),
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
            };
            _panel.AddChild(_quantityValue);
        }

        private void BuildDescriptionSection()
        {
            _descriptionText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "寒铁铸就，剑身泛着幽幽寒光。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, DescriptionY),
                Size = new Float2(PanelWidth - 64f, 24f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperAged,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
            };
            _panel.AddChild(_descriptionText);
        }

        private void BuildActions()
        {
            _actionDivider = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 80f) * 0.5f, ActionsY - 24f),
                Size = new Float2(80f, 1f),
                BackgroundColor = InkWashTheme.TextTertiary,
            };
            _panel.AddChild(_actionDivider);

            _confirmButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "确认",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 240f) * 0.5f, ActionsY),
                Size = new Float2(110f, 36f),
            };
            _confirmButton.ButtonClicked += OnConfirmButtonClicked;
            _panel.AddChild(_confirmButton);

            _detailButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "查看详情",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 240f) * 0.5f + 120f, ActionsY),
                Size = new Float2(110f, 36f),
            };
            _detailButton.ButtonClicked += OnDetailButtonClicked;
            _panel.AddChild(_detailButton);

            _footerHint = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "按 ESC 关闭",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 100f) * 0.5f, ActionsY + 52f),
                Size = new Float2(100f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextTertiary,
            };
            _panel.AddChild(_footerHint);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        public event Action Confirmed;
        public event Action ViewDetail;
        public event Action Closed;

        private void OnCloseButtonClicked(Button button)
        {
            Closed?.Invoke();
        }

        private void OnConfirmButtonClicked(Button button)
        {
            Confirmed?.Invoke();
        }

        private void OnDetailButtonClicked(Button button)
        {
            ViewDetail?.Invoke();
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        public void SetItem(string name, int count, InkWashTheme.InkQuality quality, string type = "武器")
        {
            if (_nameText != null)
                _nameText.Text = name ?? string.Empty;

            if (_quantityValue != null)
                _quantityValue.Text = $"× {count}";

            if (_typeBadge != null)
                _typeBadge.Text = type;

            if (_qualityBadge != null)
            {
                switch (quality)
                {
                    case InkWashTheme.InkQuality.Common:
                        _qualityBadge.Text = "凡";
                        _qualityBadge.TagVariant = InkTagVariant.Default;
                        break;
                    case InkWashTheme.InkQuality.Uncommon:
                        _qualityBadge.Text = "良";
                        _qualityBadge.TagVariant = InkTagVariant.Default;
                        break;
                    case InkWashTheme.InkQuality.Rare:
                        _qualityBadge.Text = "珍";
                        _qualityBadge.TagVariant = InkTagVariant.Brand;
                        break;
                    case InkWashTheme.InkQuality.Epic:
                        _qualityBadge.Text = "史";
                        _qualityBadge.TagVariant = InkTagVariant.Brand;
                        break;
                    case InkWashTheme.InkQuality.Legendary:
                        _qualityBadge.Text = "传世";
                        // 设计方案 §4.1 Legendary=鎏金 #C8A858，用 Brand 金色标签（非朱红）
                        _qualityBadge.TagVariant = InkTagVariant.Brand;
                        break;
                }
            }
        }

        public void ShowItem(ulong itemId, int count)
        {
            string name = "未知物品";
            InkWashTheme.InkQuality quality = InkWashTheme.InkQuality.Common;
            string type = "武器";

            try
            {
                var data = EquipmentDatabase.GetEquipment((int)itemId);
                if (data != null)
                {
                    name = data.Name ?? "未知物品";
                    quality = (InkWashTheme.InkQuality)Mathf.Clamp(data.Quality, 0, 4);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[PopupItemAcquired] 查询装备 {itemId} 失败: {ex.Message}");
            }

            SetItem(name, count, quality, type);
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                float panelHeight = ActionsY + 72f;
                _panel.Size = new Float2(PanelWidth, panelHeight);
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - panelHeight) * 0.5f);
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
    }

    // ===================================================================
    // PopupMessage
    // =======================================================================

    /// <summary>
    /// 江湖来信留言弹窗。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 居中 <see cref="InkPaperPanel"/>（420x520，信笺样式），
    /// 内含发件人信息区、信笺标题、留言正文、朱红印章、元数据条、操作按钮（回复、收藏、关闭）。
    /// 通过 <see cref="Closed"/>、<see cref="Replied"/>、<see cref="Favorited"/> 事件通知外部。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class PopupMessage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        private const float PanelWidth = 420f;
        private const float SenderSectionHeight = 72f;
        private const float TitleSectionHeight = 56f;
        private const float ContentSectionHeight = 300f;
        private const float FooterHeight = 60f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        private InkPaperPanel _panel;
        private InkButton _closeButton;

        private InkPanel _senderAvatar;
        private InkTextBlock _senderName;
        private InkTextBlock _senderDate;
        private InkPanel _senderDivider;

        private InkPanel _titleLineLeft;
        private InkTextBlock _titleText;
        private InkPanel _titleLineRight;

        private InkTextBlock _contentText;

        private InkPanel _seal;
        private InkTextBlock _sealText;

        private InkPanel _metadataBar;
        private InkTextBlock _metadataSource;
        private InkTextBlock _metadataTime;

        private InkButton _replyButton;
        private InkButton _favoriteButton;

        private InkTextBlock _footerHint;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        private Float2 _screenSize;

        // ===================================================================
        // 构造函数
        // =======================================================================

        public PopupMessage()
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
                _panel = new InkPaperPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelWidth, 500f),
                };
                AddChild(_panel);

                BuildCloseButton();
                BuildSenderSection();
                BuildTitleSection();
                BuildContentSection();
                BuildSeal();
                BuildMetadataBar();
                BuildActions();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PopupMessage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 构造方法
        // =======================================================================

        private void BuildCloseButton()
        {
            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelWidth - 44f, -12f),
                Size = new Float2(32f, 32f),
            };
            _closeButton.ButtonClicked += OnCloseButtonClicked;
            _panel.AddChild(_closeButton);
        }

        private void BuildSenderSection()
        {
            _senderAvatar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 28f),
                Size = new Float2(48f, 48f),
                BackgroundColor = InkWashTheme.BaseDefault,
            };
            _panel.AddChild(_senderAvatar);

            InkTextBlock avatarText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "云",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(48f, 48f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
            };
            _senderAvatar.AddChild(avatarText);

            _senderName = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "云游客",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(88f, 28f),
                Size = new Float2(120f, 24f),
                TextColor = InkWashTheme.TextOnPaper,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
            };
            _panel.AddChild(_senderName);

            _senderDate = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "甲辰年·孟秋",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(88f, 56f),
                Size = new Float2(120f, 16f),
                TextColor = InkWashTheme.PaperDark,
            };
            _panel.AddChild(_senderDate);

            _senderDivider = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 200f) * 0.5f, 80f),
                Size = new Float2(200f, 1f),
                BackgroundColor = InkWashTheme.PaperDark,
            };
            _panel.AddChild(_senderDivider);
        }

        private void BuildTitleSection()
        {
            _titleLineLeft = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 100f),
                Size = new Float2(40f, 1f),
                BackgroundColor = InkWashTheme.GoldDeep,
            };
            _panel.AddChild(_titleLineLeft);

            _titleText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "江湖来信",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 90f),
                Size = new Float2(260f, 32f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 20f),
            };
            _panel.AddChild(_titleText);

            _titleLineRight = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(348f, 100f),
                Size = new Float2(40f, 1f),
                BackgroundColor = InkWashTheme.GoldDeep,
            };
            _panel.AddChild(_titleLineRight);
        }

        private void BuildContentSection()
        {
            _contentText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "少侠亲启：\n\n江湖路远，风波不定。闻君近日于洛阳城内除恶扬善，名声鹊起，实为可喜。\n\n某不才，愿赠君一言：行走江湖，以义为先，以仁为怀。遇不平则鸣，逢危难则援，方为侠者本色。\n\n他日有缘，当于江南烟雨之中，共品茗茶，再叙江湖。\n\n云游客 顿首",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 136f),
                Size = new Float2(PanelWidth - 64f, ContentSectionHeight - 40f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                Wrapping = TextWrapping.WrapWords,
                TextColor = InkWashTheme.TextOnPaper,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 15f),
            };
            _panel.AddChild(_contentText);
        }

        private void BuildSeal()
        {
            _seal = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelWidth - 80f, ContentSectionHeight + 88f),
                Size = new Float2(48f, 48f),
                BackgroundColor = InkWashTheme.VermilionPrimary,
            };
            _panel.AddChild(_seal);

            _sealText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "印",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(48f, 48f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
            };
            _seal.AddChild(_sealText);
        }

        private void BuildMetadataBar()
        {
            _metadataBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, ContentSectionHeight + 152f),
                Size = new Float2(PanelWidth, 32f),
                BackgroundColor = new Color(InkWashTheme.PaperAged.R, InkWashTheme.PaperAged.G, InkWashTheme.PaperAged.B, 0.6f),
            };
            _panel.AddChild(_metadataBar);

            _metadataSource = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "来源：江湖传闻",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, 8f),
                Size = new Float2(150f, 16f),
                TextColor = InkWashTheme.PaperDark,
            };
            _metadataBar.AddChild(_metadataSource);

            _metadataTime = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "辰时送达",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelWidth - 100f, 8f),
                Size = new Float2(68f, 16f),
                HorizontalAlignment = TextAlignment.Far,
                TextColor = InkWashTheme.PaperDark,
            };
            _metadataBar.AddChild(_metadataTime);
        }

        private void BuildActions()
        {
            float actionsY = ContentSectionHeight + 192f;

            _replyButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "回复",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 320f) * 0.5f, actionsY),
                Size = new Float2(100f, 36f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            _replyButton.ButtonClicked += OnReplyButtonClicked;
            _panel.AddChild(_replyButton);

            _favoriteButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "收藏",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 320f) * 0.5f + 110f, actionsY),
                Size = new Float2(100f, 36f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            _favoriteButton.ButtonClicked += OnFavoriteButtonClicked;
            _panel.AddChild(_favoriteButton);

            InkButton closeBtn = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "关闭",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 320f) * 0.5f + 220f, actionsY),
                Size = new Float2(100f, 36f),
            };
            closeBtn.ButtonClicked += OnCloseButtonClicked;
            _panel.AddChild(closeBtn);

            _footerHint = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "按 ESC 关闭",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 100f) * 0.5f, actionsY + 48f),
                Size = new Float2(100f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperDark,
            };
            _panel.AddChild(_footerHint);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        public event Action Closed;
        public event Action Replied;
        public event Action Favorited;

        private void OnCloseButtonClicked(Button button)
        {
            Closed?.Invoke();
        }

        private void OnReplyButtonClicked(Button button)
        {
            Replied?.Invoke();
        }

        private void OnFavoriteButtonClicked(Button button)
        {
            Favorited?.Invoke();
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        public void SetMessage(string title, string content, string senderName = "云游客", string senderDate = "甲辰年·孟秋")
        {
            if (_titleText != null)
                _titleText.Text = title ?? string.Empty;

            if (_contentText != null)
                _contentText.Text = content ?? string.Empty;

            if (_senderName != null)
                _senderName.Text = senderName ?? string.Empty;

            if (_senderDate != null)
                _senderDate.Text = senderDate ?? string.Empty;
        }

        public void ShowMessage(string title, string content, string senderName = "云游客", string senderDate = "甲辰年·孟秋")
        {
            SetMessage(title, content, senderName, senderDate);
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                float panelHeight = ContentSectionHeight + 260f;
                _panel.Size = new Float2(PanelWidth, panelHeight);
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - panelHeight) * 0.5f);
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
    }

    // ===================================================================
    // PopupVerification
    // =======================================================================

    public class PopupVerification : ContainerControl, IInkPage
    {
        private const float PanelWidth = 400f;

        private const float SealSize = 100f;
        private const float SealY = 30f;

        private const float TitleY = 145f;
        private const float TitleHeight = 32f;

        private const float SubtitleY = 185f;
        private const float SubtitleHeight = 20f;

        private const float SummaryY = 220f;
        private const float SummaryWidth = 340f;
        private const float SummaryHeight = 140f;

        private const float TimestampY = 380f;

        private const float ActionsY = 410f;

        private InkPanelElevated _panel;
        private InkPanel _sealPanel;
        private InkPanel _sealOuterRing;
        private InkTextBlock _sealCharTop;
        private InkTextBlock _sealCharBottom;
        private InkTextBlock _titleText;
        private InkTextBlock _subtitleText;
        private InkPaperPanel _summaryPanel;
        private InkTextBlock[] _summaryLabels;
        private InkTextBlock[] _summaryValues;
        private InkTextBlock _timestampText;
        private InkButton _confirmButton;
        private InkTextBlock _rewardLinkText;

        private Float2 _screenSize;

        public PopupVerification()
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
                    Size = new Float2(PanelWidth, 480f),
                };
                AddChild(_panel);

                BuildSealArea();
                BuildTitleSection();
                BuildSummarySection();
                BuildTimestamp();
                BuildActions();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PopupVerification] 初始化失败: {ex.Message}");
            }
        }

        private void BuildSealArea()
        {
            _sealPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - SealSize) * 0.5f, SealY),
                Size = new Float2(SealSize, SealSize),
                BackgroundColor = InkWashTheme.VermilionPrimary,
            };
            _panel.AddChild(_sealPanel);

            _sealOuterRing = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 8f),
                Size = new Float2(SealSize - 16f, SealSize - 16f),
                BackgroundColor = Color.Transparent,
            };
            _sealPanel.AddChild(_sealOuterRing);

            _sealCharTop = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "验",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((SealSize - 40f) * 0.5f, 10f),
                Size = new Float2(40f, 40f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
            };
            _sealPanel.AddChild(_sealCharTop);

            _sealCharBottom = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "讫",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((SealSize - 40f) * 0.5f, 50f),
                Size = new Float2(40f, 40f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
            };
            _sealPanel.AddChild(_sealCharBottom);
        }

        private void BuildTitleSection()
        {
            _titleText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "任务验证完成",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 200f) * 0.5f, TitleY),
                Size = new Float2(200f, TitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_titleText);

            _subtitleText = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "官方验讫 · 已备案存档",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 200f) * 0.5f, SubtitleY),
                Size = new Float2(200f, SubtitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(_subtitleText);
        }

        private void BuildSummarySection()
        {
            _summaryPanel = new InkPaperPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - SummaryWidth) * 0.5f, SummaryY),
                Size = new Float2(SummaryWidth, SummaryHeight),
            };
            _panel.AddChild(_summaryPanel);

            InkTextBlock summarySubtitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "验证内容",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 16f),
                Size = new Float2(100f, 24f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            _summaryPanel.AddChild(summarySubtitle);

            _summaryLabels = new InkTextBlock[4];
            _summaryValues = new InkTextBlock[4];

            string[] labels = { "任务名称", "完成时间", "验证编号", "验证状态" };
            string[] values = { "昆仑山异兽调查", "2026.07.11 戌时", "YY-2026-0711-0342", "已通过" };

            for (int i = 0; i < 4; i++)
            {
                float rowY = 50f + i * 22f;

                _summaryLabels[i] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = labels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(20f, rowY),
                    Size = new Float2(100f, 20f),
                    TextColor = InkWashTheme.TextOnPaper,
                };
                _summaryPanel.AddChild(_summaryLabels[i]);

                _summaryValues[i] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = values[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(150f, rowY),
                    Size = new Float2(170f, 20f),
                    HorizontalAlignment = TextAlignment.Far,
                    TextColor = InkWashTheme.TextOnPaper,
                };
                if (i == 3)
                    _summaryValues[i].TextColor = InkWashTheme.JadeBright;
                _summaryPanel.AddChild(_summaryValues[i]);
            }
        }

        private void BuildTimestamp()
        {
            _timestampText = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "验讫时间：2026年7月11日 戌时三刻",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 300f) * 0.5f, TimestampY),
                Size = new Float2(300f, 18f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(_timestampText);
        }

        private void BuildActions()
        {
            _confirmButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "确认",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 120f) * 0.5f, ActionsY),
                Size = new Float2(120f, 36f),
            };
            _confirmButton.ButtonClicked += OnConfirmButtonClicked;
            _panel.AddChild(_confirmButton);

            InkTextBlock noteText = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "此验证已记录入册",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 160f) * 0.5f, ActionsY + 44f),
                Size = new Float2(160f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(noteText);

            _rewardLinkText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "查看成就奖励 →",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 160f) * 0.5f, ActionsY + 70f),
                Size = new Float2(160f, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_rewardLinkText);
        }

        public event Action Confirmed;
        public event Action ViewAchievement;

        private void OnConfirmButtonClicked(Button button)
        {
            Confirmed?.Invoke();
        }

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                float panelHeight = ActionsY + 90f;
                _panel.Size = new Float2(PanelWidth, panelHeight);
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - panelHeight) * 0.5f);
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
    }

    // ===================================================================
    // PopupMartialArts
    // =======================================================================

    public class PopupMartialArts : ContainerControl, IInkPage
    {
        private const float PanelWidth = 520f;

        private const float HeaderY = 24f;

        private const float SkillIdentityY = 72f;
        private const float SkillIconSize = 80f;

        private const float KeywordY = 170f;

        private const float EffectY = 210f;

        private const float NumericPanelY = 290f;

        private const float UpgradeY = 400f;

        private const float ActionBarY = 630f;

        private InkPanelElevated _panel;
        private InkTextBlock _headerEyebrow;
        private InkTextBlock _headerSubline;
        private InkButton _closeButton;

        private InkPanel _skillIconFrame;
        private InkPanel _skillIconInner;
        private InkTextBlock _skillIconChar;
        private InkTextBlock _skillIconRank;
        private InkTextBlock _skillName;
        private InkTextBlock _skillTagline;

        private InkPanel[] _keywordChips;
        private InkTextBlock[] _keywordTexts;

        private InkTextBlock _effectTitle;
        private InkTextBlock _effectText;
        private InkTextBlock _effectLore;

        private InkPanel[] _numericCells;
        private InkTextBlock[] _numericLabels;
        private InkTextBlock[] _numericValues;

        private InkTextBlock _upgradeTitle;
        private InkTextBlock _levelCurrent;
        private InkTextBlock _levelNext;
        private InkPanel _progressBar;
        private InkPanel _progressBarFill;
        private InkTextBlock _progressCount;

        private InkTextBlock[] _previewKeys;
        private InkTextBlock[] _previewFrom;
        private InkTextBlock[] _previewTo;
        private InkTextBlock[] _previewDelta;

        private InkPanel[] _materialRows;
        private InkTextBlock[] _materialNames;
        private InkTextBlock[] _materialSubs;
        private InkTextBlock[] _materialCounts;
        private InkTextBlock[] _materialStatus;

        private InkButton _upgradeButton;
        private InkButton _equipButton;
        private InkButton _closeBtnBottom;

        private Float2 _screenSize;

        public PopupMartialArts()
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
                    Size = new Float2(PanelWidth, 720f),
                };
                AddChild(_panel);

                BuildHeader();
                BuildSkillIdentity();
                BuildKeywordRow();
                BuildEffectBlock();
                BuildNumericPanel();
                BuildUpgradeBlock();
                BuildActionBar();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PopupMartialArts] 初始化失败: {ex.Message}");
            }
        }

        private void BuildHeader()
        {
            _headerEyebrow = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "奇术详情",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, HeaderY),
                Size = new Float2(80f, 16f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(_headerEyebrow);

            _headerSubline = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "SPECIAL ARTS",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, HeaderY + 18f),
                Size = new Float2(100f, 14f),
                TextColor = new Color(InkWashTheme.PaperFaded.R, InkWashTheme.PaperFaded.G, InkWashTheme.PaperFaded.B, 0.5f),
            };
            _panel.AddChild(_headerSubline);

            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(PanelWidth - 44f, HeaderY),
                Size = new Float2(32f, 32f),
            };
            _closeButton.ButtonClicked += OnCloseButtonClicked;
            _panel.AddChild(_closeButton);
        }

        private void BuildSkillIdentity()
        {
            _skillIconFrame = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, SkillIdentityY),
                Size = new Float2(SkillIconSize, SkillIconSize),
                BackgroundColor = new Color(0.15f, 0.12f, 0.1f, 1f),
            };
            _panel.AddChild(_skillIconFrame);

            _skillIconInner = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(6f, 6f),
                Size = new Float2(SkillIconSize - 12f, SkillIconSize - 12f),
                BackgroundColor = InkWashTheme.GoldDeep,
            };
            _skillIconFrame.AddChild(_skillIconInner);

            _skillIconChar = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "遁",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(SkillIconSize - 12f, SkillIconSize - 12f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 36f),
            };
            _skillIconInner.AddChild(_skillIconChar);

            _skillIconRank = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "叁",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SkillIconSize - 20f, -4f),
                Size = new Float2(16f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f),
            };
            _skillIconFrame.AddChild(_skillIconRank);

            _skillName = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "遁地术",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(128f, SkillIdentityY),
                Size = new Float2(200f, 32f),
                TextColor = InkWashTheme.PaperBright,
            };
            _panel.AddChild(_skillName);

            _skillTagline = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "土遁·匿形刺",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(128f, SkillIdentityY + 36f),
                Size = new Float2(200f, 20f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(_skillTagline);

            InkTag tag1 = new InkTag
            {
                Text = "奇术 · 主动",
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(128f, SkillIdentityY + 64f),
                Size = new Float2(96f, 22f),
            };
            _panel.AddChild(tag1);

            InkTag tag2 = new InkTag
            {
                Text = "史诗",
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(232f, SkillIdentityY + 64f),
                Size = new Float2(60f, 22f),
            };
            _panel.AddChild(tag2);

            InkTag tag3 = new InkTag
            {
                Text = "位移",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(300f, SkillIdentityY + 64f),
                Size = new Float2(60f, 22f),
            };
            _panel.AddChild(tag3);
        }

        private void BuildKeywordRow()
        {
            string[] keywords = { "无法选中", "移速 +50%", "范围伤害", "脱战遁走" };
            _keywordChips = new InkPanel[keywords.Length];
            _keywordTexts = new InkTextBlock[keywords.Length];

            float startX = 32f;
            float chipWidth = 110f;
            float chipGap = 8f;

            for (int i = 0; i < keywords.Length; i++)
            {
                _keywordChips[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(startX + i * (chipWidth + chipGap), KeywordY),
                    Size = new Float2(chipWidth, 28f),
                    BackgroundColor = new Color(0.15f, 0.14f, 0.12f, 1f),
                };
                _panel.AddChild(_keywordChips[i]);

                _keywordTexts[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = keywords[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 6f),
                    Size = new Float2(chipWidth - 16f, 16f),
                    HorizontalAlignment = TextAlignment.Center,
                    TextColor = InkWashTheme.PaperAged,
                };
                _keywordChips[i].AddChild(_keywordTexts[i]);
            }
        }

        private void BuildEffectBlock()
        {
            _effectTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "效果",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, EffectY),
                Size = new Float2(80f, 24f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_effectTitle);

            _effectText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "瞬间遁入地下，3秒内无法被选中，移动速度提升50%。结束时从地下跃出，对周围敌人造成攻击力120%的伤害。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, EffectY + 32f),
                Size = new Float2(PanelWidth - 64f, 56f),
                TextColor = InkWashTheme.PaperBright,
            };
            _panel.AddChild(_effectText);

            _effectLore = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "——「土行遁去，形隐于渊；破土而出，一击裂石。」此术源自茅山遁甲残篇，修至大成者可遁于山石草木之间。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, EffectY + 96f),
                Size = new Float2(PanelWidth - 64f, 32f),
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(_effectLore);
        }

        private void BuildNumericPanel()
        {
            string[] labels = { "消耗内力", "冷却时间", "持续时间", "伤害倍率", "影响范围", "施法距离" };
            string[] values = { "200", "15秒", "3秒", "120%", "5米", "自身" };

            _numericCells = new InkPanel[labels.Length];
            _numericLabels = new InkTextBlock[labels.Length];
            _numericValues = new InkTextBlock[labels.Length];

            float cellWidth = (PanelWidth - 64f) / 2f;
            float cellHeight = 48f;
            float startX = 32f;
            float startY = NumericPanelY;

            for (int i = 0; i < labels.Length; i++)
            {
                int row = i / 2;
                int col = i % 2;

                _numericCells[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(startX + col * cellWidth, startY + row * (cellHeight + 8f)),
                    Size = new Float2(cellWidth - 4f, cellHeight),
                    BackgroundColor = new Color(0.15f, 0.14f, 0.12f, 1f),
                };
                _panel.AddChild(_numericCells[i]);

                _numericLabels[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = labels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 8f),
                    Size = new Float2(cellWidth - 24f, 16f),
                    TextColor = InkWashTheme.PaperFaded,
                };
                _numericCells[i].AddChild(_numericLabels[i]);

                _numericValues[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = values[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 28f),
                    Size = new Float2(cellWidth - 24f, 16f),
                    TextColor = InkWashTheme.GoldBright,
                };
                _numericCells[i].AddChild(_numericValues[i]);
            }
        }

        private void BuildUpgradeBlock()
        {
            _upgradeTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "修炼等级",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, UpgradeY),
                Size = new Float2(100f, 24f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_upgradeTitle);

            _levelCurrent = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "Lv. 3",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(380f, UpgradeY + 4f),
                Size = new Float2(56f, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_levelCurrent);

            InkTextBlock levelArrow = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "→",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(444f, UpgradeY + 4f),
                Size = new Float2(20f, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(levelArrow);

            _levelNext = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "Lv. 4",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(472f, UpgradeY + 4f),
                Size = new Float2(56f, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(_levelNext);

            float progressY = UpgradeY + 40f;

            _progressCount = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "当前修为 180 / 300",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, progressY),
                Size = new Float2(PanelWidth - 64f, 16f),
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(_progressCount);

            _progressBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, progressY + 24f),
                Size = new Float2(PanelWidth - 64f, 8f),
                BackgroundColor = new Color(0f, 0f, 0f, 0.4f),
            };
            _panel.AddChild(_progressBar);

            _progressBarFill = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2((PanelWidth - 64f) * 0.6f, 8f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _progressBar.AddChild(_progressBarFill);

            float previewY = progressY + 50f;

            InkTextBlock previewTitle = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "升级预览",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, previewY),
                Size = new Float2(80f, 16f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(previewTitle);

            string[] previewKeys = { "伤害倍率", "持续时间", "冷却缩减" };
            string[] previewFrom = { "120%", "3秒", "0%" };
            string[] previewTo = { "140%", "4秒", "10%" };
            string[] previewDelta = { "+20%", "+1秒", "+10%" };

            _previewKeys = new InkTextBlock[previewKeys.Length];
            _previewFrom = new InkTextBlock[previewKeys.Length];
            _previewTo = new InkTextBlock[previewKeys.Length];
            _previewDelta = new InkTextBlock[previewKeys.Length];

            float rowHeight = 28f;
            for (int i = 0; i < previewKeys.Length; i++)
            {
                float rowY = previewY + 28f + i * rowHeight;

                _previewKeys[i] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = previewKeys[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(32f, rowY),
                    Size = new Float2(80f, 20f),
                    TextColor = InkWashTheme.PaperAged,
                };
                _panel.AddChild(_previewKeys[i]);

                _previewFrom[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = previewFrom[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(200f, rowY),
                    Size = new Float2(60f, 20f),
                    HorizontalAlignment = TextAlignment.Far,
                    TextColor = InkWashTheme.PaperFaded,
                };
                _panel.AddChild(_previewFrom[i]);

                InkTextBlock arrow = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = "→",
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(272f, rowY),
                    Size = new Float2(20f, 20f),
                    HorizontalAlignment = TextAlignment.Center,
                    TextColor = InkWashTheme.GoldBright,
                };
                _panel.AddChild(arrow);

                _previewTo[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = previewTo[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(300f, rowY),
                    Size = new Float2(60f, 20f),
                    HorizontalAlignment = TextAlignment.Far,
                    TextColor = InkWashTheme.GoldBright,
                };
                _panel.AddChild(_previewTo[i]);

                _previewDelta[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = previewDelta[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(380f, rowY),
                    Size = new Float2(60f, 20f),
                    HorizontalAlignment = TextAlignment.Far,
                    TextColor = InkWashTheme.JadeBright,
                };
                _panel.AddChild(_previewDelta[i]);
            }

            float materialsY = previewY + 110f;

            InkTextBlock materialsTitle = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "升级材料",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, materialsY),
                Size = new Float2(80f, 16f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(materialsTitle);

            string[] materialNames = { "玄铁精魄", "遁甲残卷", "黄泉灵露" };
            string[] materialSubs = { "奇珍 · 史诗", "古籍 · 珍稀", "灵药 · 珍稀" };
            string[] materialCounts = { "8/5", "2/3", "0/1" };
            string[] materialStatus = { "充足", "不足", "不足" };
            Color[] statusColors = { InkWashTheme.JadeBright, InkWashTheme.VermilionBright, InkWashTheme.VermilionBright };

            _materialRows = new InkPanel[materialNames.Length];
            _materialNames = new InkTextBlock[materialNames.Length];
            _materialSubs = new InkTextBlock[materialNames.Length];
            _materialCounts = new InkTextBlock[materialNames.Length];
            _materialStatus = new InkTextBlock[materialNames.Length];

            float materialRowHeight = 40f;
            for (int i = 0; i < materialNames.Length; i++)
            {
                float rowY = materialsY + 28f + i * materialRowHeight;

                _materialRows[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(32f, rowY),
                    Size = new Float2(PanelWidth - 64f, materialRowHeight - 4f),
                    BackgroundColor = new Color(0.15f, 0.14f, 0.12f, 1f),
                };
                _panel.AddChild(_materialRows[i]);

                _materialNames[i] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = materialNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(48f, 8f),
                    Size = new Float2(120f, 20f),
                    TextColor = InkWashTheme.PaperBright,
                };
                _materialRows[i].AddChild(_materialNames[i]);

                _materialSubs[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = materialSubs[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(48f, 26f),
                    Size = new Float2(120f, 14f),
                    TextColor = InkWashTheme.PaperFaded,
                };
                _materialRows[i].AddChild(_materialSubs[i]);

                _materialCounts[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = materialCounts[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(PanelWidth - 140f, 14f),
                    Size = new Float2(80f, 16f),
                    HorizontalAlignment = TextAlignment.Far,
                    TextColor = InkWashTheme.PaperAged,
                };
                _materialRows[i].AddChild(_materialCounts[i]);

                _materialStatus[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = materialStatus[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(PanelWidth - 68f, 14f),
                    Size = new Float2(36f, 16f),
                    HorizontalAlignment = TextAlignment.Far,
                    TextColor = statusColors[i],
                };
                _materialRows[i].AddChild(_materialStatus[i]);
            }

            InkPanel sufficiencyBanner = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, materialsY + 160f),
                Size = new Float2(PanelWidth - 64f, 32f),
                BackgroundColor = new Color(InkWashTheme.VermilionPrimary.R, InkWashTheme.VermilionPrimary.G, InkWashTheme.VermilionPrimary.B, 0.1f),
            };
            _panel.AddChild(sufficiencyBanner);

            InkTextBlock bannerText = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "材料不足，暂无法修炼升级",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(PanelWidth - 100f, 16f),
                TextColor = InkWashTheme.VermilionBright,
            };
            sufficiencyBanner.AddChild(bannerText);
        }

        private void BuildActionBar()
        {
            _upgradeButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "修炼升级",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, ActionBarY),
                Size = new Float2(160f, 40f),
            };
            _upgradeButton.ButtonClicked += OnUpgradeButtonClicked;
            _panel.AddChild(_upgradeButton);

            _equipButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "装备",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(200f, ActionBarY),
                Size = new Float2(120f, 40f),
            };
            _equipButton.ButtonClicked += OnEquipButtonClicked;
            _panel.AddChild(_equipButton);

            _closeBtnBottom = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "关闭",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(336f, ActionBarY),
                Size = new Float2(120f, 40f),
            };
            _closeBtnBottom.ButtonClicked += OnCloseButtonClicked;
            _panel.AddChild(_closeBtnBottom);
        }

        public event Action Upgraded;
        public event Action Equipped;
        public event Action Closed;

        private void OnUpgradeButtonClicked(Button button)
        {
            Upgraded?.Invoke();
        }

        private void OnEquipButtonClicked(Button button)
        {
            Equipped?.Invoke();
        }

        private void OnCloseButtonClicked(Button button)
        {
            Closed?.Invoke();
        }

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                float panelHeight = ActionBarY + 60f;
                _panel.Size = new Float2(PanelWidth, panelHeight);
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - panelHeight) * 0.5f);
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
    }

    // ===================================================================
    // PopupSkillRealization
    // =======================================================================

    public class PopupSkillRealization : ContainerControl, IInkPage
    {
        private const float PanelWidth = 500f;

        private const float TitleY = 32f;

        private const float CeremonyY = 80f;
        private const float GlyphSize = 120f;

        private const float EffectY = 280f;

        private const float AttrY = 360f;

        private const float ConfirmY = 520f;

        private InkPanelElevated _panel;
        private InkTextBlock _titleEyebrow;
        private InkTextBlock _titleMain;
        private InkButton _closeButton;

        private InkPanel _glyphRing;
        private InkTextBlock _glyphCore;
        private InkTextBlock _skillNameBig;

        private InkTextBlock _effectDesc;

        private InkTextBlock[] _attrNames;
        private InkTextBlock[] _attrBefore;
        private InkTextBlock[] _attrAfter;
        private InkTextBlock[] _attrDelta;

        private InkButton _confirmButton;
        private InkTextBlock _equipHint;
        private InkTextBlock _verifyLink;

        private Float2 _screenSize;

        public PopupSkillRealization()
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
                    Size = new Float2(PanelWidth, 600f),
                };
                AddChild(_panel);

                BuildTitle();
                BuildCeremony();
                BuildEffect();
                BuildAttributes();
                BuildConfirm();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PopupSkillRealization] 初始化失败: {ex.Message}");
            }
        }

        private void BuildTitle()
        {
            _titleEyebrow = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "ENLIGHTENMENT",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 120f) * 0.5f, TitleY),
                Size = new Float2(120f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = new Color(InkWashTheme.GoldBright.R, InkWashTheme.GoldBright.G, InkWashTheme.GoldBright.B, 0.6f),
            };
            _panel.AddChild(_titleEyebrow);

            _titleMain = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "心法领悟",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 120f) * 0.5f, TitleY + 24f),
                Size = new Float2(120f, 32f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_titleMain);

            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(PanelWidth - 44f, TitleY),
                Size = new Float2(32f, 32f),
            };
            _closeButton.ButtonClicked += OnCloseButtonClicked;
            _panel.AddChild(_closeButton);
        }

        private void BuildCeremony()
        {
            float glyphX = (PanelWidth - GlyphSize) * 0.5f;

            _glyphRing = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(glyphX, CeremonyY),
                Size = new Float2(GlyphSize, GlyphSize),
                BackgroundColor = Color.Transparent,
            };
            _panel.AddChild(_glyphRing);

            InkPanel ringOuter = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(GlyphSize, GlyphSize),
                BackgroundColor = Color.Transparent,
            };
            _glyphRing.AddChild(ringOuter);

            InkPanel ringInner = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 8f),
                Size = new Float2(GlyphSize - 16f, GlyphSize - 16f),
                BackgroundColor = InkWashTheme.GoldDeep,
            };
            _glyphRing.AddChild(ringInner);

            _glyphCore = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "心",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(GlyphSize - 16f, GlyphSize - 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 56f),
            };
            ringInner.AddChild(_glyphCore);

            float nameY = CeremonyY + GlyphSize + 24f;

            _skillNameBig = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "紫霞神功",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 200f) * 0.5f, nameY),
                Size = new Float2(200f, 40f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
            };
            _panel.AddChild(_skillNameBig);

            InkTag tag1 = new InkTag
            {
                Text = "史诗",
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 140f) * 0.5f, nameY + 48f),
                Size = new Float2(60f, 22f),
            };
            _panel.AddChild(tag1);

            InkTag tag2 = new InkTag
            {
                Text = "内功心法",
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 140f) * 0.5f + 70f, nameY + 48f),
                Size = new Float2(70f, 22f),
            };
            _panel.AddChild(tag2);

            InkTextBlock levelText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "领悟等级 1",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 100f) * 0.5f, nameY + 80f),
                Size = new Float2(100f, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(levelText);
        }

        private void BuildEffect()
        {
            _effectDesc = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "紫气东来，霞光护体。修炼后永久提升内力上限500点，内力恢复速度提升20%。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, EffectY),
                Size = new Float2(PanelWidth - 64f, 48f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
            };
            _panel.AddChild(_effectDesc);
        }

        private void BuildAttributes()
        {
            InkTextBlock attrTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "属性变化",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, AttrY),
                Size = new Float2(100f, 24f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(attrTitle);

            float tableY = AttrY + 32f;
            float rowHeight = 40f;

            InkPanel headerRow = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, tableY),
                Size = new Float2(PanelWidth - 64f, rowHeight),
                BackgroundColor = new Color(0.15f, 0.14f, 0.12f, 1f),
            };
            _panel.AddChild(headerRow);

            InkTextBlock colName = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "属性",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 12f),
                Size = new Float2(100f, 16f),
                TextColor = InkWashTheme.PaperAged,
            };
            headerRow.AddChild(colName);

            InkTextBlock colBefore = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "领悟前",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(140f, 12f),
                Size = new Float2(80f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperAged,
            };
            headerRow.AddChild(colBefore);

            InkTextBlock colAfter = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "领悟后",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(280f, 12f),
                Size = new Float2(80f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperAged,
            };
            headerRow.AddChild(colAfter);

            InkTextBlock colDelta = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "变化",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(380f, 12f),
                Size = new Float2(80f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperAged,
            };
            headerRow.AddChild(colDelta);

            string[] attrNames = { "内力上限", "内力恢复", "内功防御", "内功暴击" };
            string[] attrBefore = { "2,000", "50/秒", "800", "10%" };
            string[] attrAfter = { "2,500", "60/秒", "950", "12%" };
            string[] attrDelta = { "+500", "+10", "+150", "+2%" };

            _attrNames = new InkTextBlock[attrNames.Length];
            _attrBefore = new InkTextBlock[attrNames.Length];
            _attrAfter = new InkTextBlock[attrNames.Length];
            _attrDelta = new InkTextBlock[attrNames.Length];

            for (int i = 0; i < attrNames.Length; i++)
            {
                float rowY = tableY + rowHeight + i * rowHeight;

                InkPanel dataRow = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(32f, rowY),
                    Size = new Float2(PanelWidth - 64f, rowHeight),
                    BackgroundColor = new Color(0.1f, 0.09f, 0.08f, 1f),
                };
                _panel.AddChild(dataRow);

                _attrNames[i] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = attrNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 12f),
                    Size = new Float2(100f, 16f),
                    TextColor = InkWashTheme.PaperBright,
                };
                dataRow.AddChild(_attrNames[i]);

                _attrBefore[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = attrBefore[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(140f, 12f),
                    Size = new Float2(80f, 16f),
                    HorizontalAlignment = TextAlignment.Center,
                    TextColor = InkWashTheme.PaperFaded,
                };
                dataRow.AddChild(_attrBefore[i]);

                InkTextBlock arrow = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = "→",
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(232f, 12f),
                    Size = new Float2(20f, 16f),
                    HorizontalAlignment = TextAlignment.Center,
                    TextColor = InkWashTheme.GoldBright,
                };
                dataRow.AddChild(arrow);

                _attrAfter[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = attrAfter[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(280f, 12f),
                    Size = new Float2(80f, 16f),
                    HorizontalAlignment = TextAlignment.Center,
                    TextColor = InkWashTheme.GoldBright,
                };
                dataRow.AddChild(_attrAfter[i]);

                _attrDelta[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = attrDelta[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(380f, 12f),
                    Size = new Float2(80f, 16f),
                    HorizontalAlignment = TextAlignment.Center,
                    TextColor = InkWashTheme.JadeBright,
                };
                dataRow.AddChild(_attrDelta[i]);
            }
        }

        private void BuildConfirm()
        {
            _confirmButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "确认",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 160f) * 0.5f, ConfirmY),
                Size = new Float2(160f, 44f),
            };
            _confirmButton.ButtonClicked += OnConfirmButtonClicked;
            _panel.AddChild(_confirmButton);

            _equipHint = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "已自动装备至心法槽位",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 160f) * 0.5f, ConfirmY + 56f),
                Size = new Float2(160f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(_equipHint);

            _verifyLink = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "心法验证 →",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 100f) * 0.5f, ConfirmY + 84f),
                Size = new Float2(100f, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_verifyLink);
        }

        public event Action Confirmed;
        public event Action Verified;
        public event Action Closed;

        private void OnConfirmButtonClicked(Button button)
        {
            Confirmed?.Invoke();
        }

        private void OnCloseButtonClicked(Button button)
        {
            Closed?.Invoke();
        }

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                float panelHeight = ConfirmY + 110f;
                _panel.Size = new Float2(PanelWidth, panelHeight);
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - panelHeight) * 0.5f);
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
    }

    // ===================================================================
    // PopupMartialDetail
    // =======================================================================

    public class PopupMartialDetail : ContainerControl, IInkPage
    {
        private const float PanelWidth = 560f;

        private const float HeaderY = 24f;
        private const float IconSize = 64f;

        private const float AttrY = 180f;

        private const float ComboY = 350f;

        private const float ActionY = 520f;

        private InkPanelElevated _panel;
        private InkButton _closeButton;

        private InkPanel _moveIcon;
        private InkTextBlock _moveIconChar;
        private InkTextBlock _moveName;
        private InkTextBlock _moveFlavor;

        private InkPanel[] _attrCells;
        private InkTextBlock[] _attrLabels;
        private InkTextBlock[] _attrValues;

        private InkPanel[] _comboNodes;
        private InkTextBlock[] _comboNodeIndices;
        private InkTextBlock[] _comboNodeNames;
        private InkTextBlock[] _comboNodeEffects;
        private InkTextBlock[] _comboNodeTags;

        private InkButton _practiceButton;
        private InkButton _closeBtnBottom;

        private Float2 _screenSize;

        public PopupMartialDetail()
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
                    Size = new Float2(PanelWidth, 600f),
                };
                AddChild(_panel);

                BuildHeader();
                BuildAttributes();
                BuildComboPath();
                BuildActions();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PopupMartialDetail] 初始化失败: {ex.Message}");
            }
        }

        private void BuildHeader()
        {
            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(PanelWidth - 44f, HeaderY),
                Size = new Float2(32f, 32f),
            };
            _closeButton.ButtonClicked += OnCloseButtonClicked;
            _panel.AddChild(_closeButton);

            _moveIcon = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, HeaderY),
                Size = new Float2(IconSize, IconSize),
                BackgroundColor = InkWashTheme.VermilionPrimary,
            };
            _panel.AddChild(_moveIcon);

            _moveIconChar = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "剑",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(IconSize, IconSize),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
            };
            _moveIcon.AddChild(_moveIconChar);

            _moveName = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "青莲剑歌·第三式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(112f, HeaderY),
                Size = new Float2(320f, 32f),
                TextColor = InkWashTheme.PaperBright,
            };
            _panel.AddChild(_moveName);

            InkTag tag1 = new InkTag
            {
                Text = "传世",
                // 设计方案§品质色阶：Legendary=#C8A858（鎏金），用Brand变体而非Vermilion
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(112f, HeaderY + 36f),
                Size = new Float2(60f, 22f),
            };
            _panel.AddChild(tag1);

            InkTag tag2 = new InkTag
            {
                Text = "剑法 · 主动",
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(180f, HeaderY + 36f),
                Size = new Float2(96f, 22f),
            };
            _panel.AddChild(tag2);

            InkTextBlock levelText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "修炼等级 Lv. 15",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(284f, HeaderY + 38f),
                Size = new Float2(100f, 20f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(levelText);

            _moveFlavor = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "青莲剑歌第三式，剑出如莲，层层绽裂。以剑意引动天地灵气，化三式连绵不绝之攻势，终式有青莲绽放之威。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, HeaderY + 72f),
                Size = new Float2(PanelWidth - 64f, 56f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(_moveFlavor);
        }

        private void BuildAttributes()
        {
            InkTextBlock attrTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "招式属性",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, AttrY),
                Size = new Float2(100f, 24f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(attrTitle);

            InkPanel attrPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, AttrY + 32f),
                Size = new Float2(PanelWidth - 64f, 120f),
                BackgroundColor = new Color(0.15f, 0.14f, 0.12f, 1f),
            };
            _panel.AddChild(attrPanel);

            string[] labels = { "伤害", "破防", "范围", "消耗内力", "冷却", "施法时间", "连击数", "暴击率加成" };
            string[] values = { "3,850", "1,200", "前方5米扇形", "150", "8秒", "0.5秒", "3段", "+15%" };
            Color[] valueColors = { InkWashTheme.VermilionBright, InkWashTheme.GoldBright, InkWashTheme.PaperBright, InkWashTheme.GoldBright, InkWashTheme.GoldBright, InkWashTheme.GoldBright, InkWashTheme.GoldBright, InkWashTheme.VermilionBright };

            _attrCells = new InkPanel[labels.Length];
            _attrLabels = new InkTextBlock[labels.Length];
            _attrValues = new InkTextBlock[labels.Length];

            float cellWidth = (PanelWidth - 64f) / 4f;
            float cellHeight = 56f;

            for (int i = 0; i < labels.Length; i++)
            {
                int row = i / 4;
                int col = i % 4;

                _attrCells[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(col * cellWidth, row * cellHeight + 4f),
                    Size = new Float2(cellWidth - 4f, cellHeight - 8f),
                    BackgroundColor = Color.Transparent,
                };
                attrPanel.AddChild(_attrCells[i]);

                _attrLabels[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = labels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 8f),
                    Size = new Float2(cellWidth - 16f, 16f),
                    TextColor = InkWashTheme.PaperFaded,
                };
                _attrCells[i].AddChild(_attrLabels[i]);

                _attrValues[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = values[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 28f),
                    Size = new Float2(cellWidth - 16f, 20f),
                    TextColor = valueColors[i],
                };
                _attrCells[i].AddChild(_attrValues[i]);
            }
        }

        private void BuildComboPath()
        {
            InkTextBlock comboTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "连招路径",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, ComboY),
                Size = new Float2(100f, 24f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(comboTitle);

            float nodeWidth = 140f;
            float nodeHeight = 72f;
            float nodeGap = 24f;
            float startX = (PanelWidth - (nodeWidth * 3 + nodeGap * 2)) * 0.5f;
            float startY = ComboY + 32f;

            string[] nodeNames = { "起式·破风", "中式·流光", "终式·青莲" };
            string[] nodeEffects = { "破敌方护体，降低防御15%", "剑光连绵，三段追击伤害", "青莲绽放，范围爆发终结" };
            string[] nodeTags = { "起手", "连击", "终结" };
            string[] nodeIndices = { "壹", "贰", "叁" };

            _comboNodes = new InkPanel[3];
            _comboNodeIndices = new InkTextBlock[3];
            _comboNodeNames = new InkTextBlock[3];
            _comboNodeEffects = new InkTextBlock[3];
            _comboNodeTags = new InkTextBlock[3];

            for (int i = 0; i < 3; i++)
            {
                float nodeX = startX + i * (nodeWidth + nodeGap);

                _comboNodes[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(nodeX, startY),
                    Size = new Float2(nodeWidth, nodeHeight),
                    BackgroundColor = new Color(0.15f, 0.14f, 0.12f, 1f),
                };
                _panel.AddChild(_comboNodes[i]);

                _comboNodeIndices[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = nodeIndices[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 8f),
                    Size = new Float2(24f, 16f),
                    TextColor = InkWashTheme.GoldBright,
                };
                _comboNodes[i].AddChild(_comboNodeIndices[i]);

                _comboNodeNames[i] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = nodeNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(36f, 8f),
                    Size = new Float2(nodeWidth - 44f, 20f),
                    TextColor = InkWashTheme.PaperBright,
                };
                _comboNodes[i].AddChild(_comboNodeNames[i]);

                _comboNodeEffects[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = nodeEffects[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 36f),
                    Size = new Float2(nodeWidth - 16f, 32f),
                    TextColor = InkWashTheme.PaperFaded,
                };
                _comboNodes[i].AddChild(_comboNodeEffects[i]);

                _comboNodeTags[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = nodeTags[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(nodeWidth - 36f, 48f),
                    Size = new Float2(28f, 16f),
                    HorizontalAlignment = TextAlignment.Center,
                    TextColor = InkWashTheme.JadeBright,
                };
                if (i == 2)
                    _comboNodeTags[i].TextColor = InkWashTheme.VermilionBright;
                else if (i == 1)
                    _comboNodeTags[i].TextColor = InkWashTheme.GoldBright;
                _comboNodes[i].AddChild(_comboNodeTags[i]);

                if (i < 2)
                {
                    InkTextBlock arrow = new InkTextBlock(InkTextStyle.Body)
                    {
                        Text = "→",
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(nodeX + nodeWidth + 4f, startY + 28f),
                        Size = new Float2(16f, 16f),
                        HorizontalAlignment = TextAlignment.Center,
                        TextColor = InkWashTheme.GoldBright,
                    };
                    _panel.AddChild(arrow);
                }
            }

            InkTextBlock comboHint = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "连招需在8秒内依次释放，中断后需重新起手。终式命中可触发\"青莲剑意\"增益。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, ComboY + 120f),
                Size = new Float2(PanelWidth - 64f, 32f),
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(comboHint);
        }

        private void BuildActions()
        {
            _practiceButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "演练",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, ActionY),
                Size = new Float2(160f, 44f),
            };
            _practiceButton.ButtonClicked += OnPracticeButtonClicked;
            _panel.AddChild(_practiceButton);

            _closeBtnBottom = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "关闭",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(200f, ActionY),
                Size = new Float2(120f, 44f),
            };
            _closeBtnBottom.ButtonClicked += OnCloseButtonClicked;
            _panel.AddChild(_closeBtnBottom);
        }

        public event Action Practiced;
        public event Action Closed;

        private void OnPracticeButtonClicked(Button button)
        {
            Practiced?.Invoke();
        }

        private void OnCloseButtonClicked(Button button)
        {
            Closed?.Invoke();
        }

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                float panelHeight = ActionY + 60f;
                _panel.Size = new Float2(PanelWidth, panelHeight);
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - panelHeight) * 0.5f);
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
    }

    // ===================================================================
    // PopupGuideSide
    // =======================================================================

    public class PopupGuideSide : ContainerControl, IInkPage
    {
        private const float PanelWidth = 420f;

        private const float HeaderY = 24f;

        private const float StepY = 80f;

        private const float RewardY = 380f;

        private InkPanelElevated _panel;
        private InkButton _closeButton;

        private InkTextBlock _guideTitle;
        private InkTextBlock _guideSubtitle;
        private InkTextBlock _stepNumber;

        private InkPanel[] _stepItems;
        private InkTextBlock[] _stepTitles;
        private InkTextBlock[] _stepDescriptions;

        private InkTextBlock _rewardTitle;
        private InkPanel[] _rewardItems;
        private InkTextBlock[] _rewardNames;
        private InkTextBlock[] _rewardCounts;

        private InkButton _startButton;
        private InkButton _skipButton;

        private Float2 _screenSize;

        public PopupGuideSide()
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
                    AnchorPreset = AnchorPresets.TopRight,
                    Size = new Float2(PanelWidth, _screenSize.Y),
                    Location = new Float2(_screenSize.X - PanelWidth, 0f),
                };
                AddChild(_panel);

                BuildHeader();
                BuildSteps();
                BuildRewards();
                BuildActions();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PopupGuideSide] 初始化失败: {ex.Message}");
            }
        }

        private void BuildHeader()
        {
            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(PanelWidth - 44f, HeaderY),
                Size = new Float2(32f, 32f),
            };
            _closeButton.ButtonClicked += OnCloseButtonClicked;
            _panel.AddChild(_closeButton);

            _guideTitle = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "新手引导",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, HeaderY),
                Size = new Float2(200f, 32f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_guideTitle);

            _guideSubtitle = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "NEWBIE GUIDE",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, HeaderY + 36f),
                Size = new Float2(100f, 16f),
                TextColor = new Color(InkWashTheme.GoldBright.R, InkWashTheme.GoldBright.G, InkWashTheme.GoldBright.B, 0.5f),
            };
            _panel.AddChild(_guideSubtitle);

            _stepNumber = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "01 / 05",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(PanelWidth - 100f, HeaderY + 38f),
                Size = new Float2(68f, 16f),
                HorizontalAlignment = TextAlignment.Far,
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(_stepNumber);
        }

        private void BuildSteps()
        {
            InkTextBlock stepTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "任务目标",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, StepY),
                Size = new Float2(100f, 24f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(stepTitle);

            InkTextBlock stepName = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "初识江湖",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, StepY + 32f),
                Size = new Float2(200f, 32f),
                TextColor = InkWashTheme.PaperBright,
            };
            _panel.AddChild(stepName);

            InkTextBlock stepDesc = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "在新手村找到村长，领取你的第一把武器，开启江湖之旅。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, StepY + 72f),
                Size = new Float2(PanelWidth - 64f, 48f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(stepDesc);

            string[] steps = { "与村长对话", "领取新手武器", "学习基础招式", "击败野猪试练" };
            _stepItems = new InkPanel[steps.Length];
            _stepTitles = new InkTextBlock[steps.Length];
            _stepDescriptions = new InkTextBlock[steps.Length];

            float stepItemY = StepY + 136f;
            float stepItemHeight = 48f;

            for (int i = 0; i < steps.Length; i++)
            {
                _stepItems[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(32f, stepItemY + i * stepItemHeight),
                    Size = new Float2(PanelWidth - 64f, stepItemHeight - 4f),
                    BackgroundColor = new Color(0.15f, 0.14f, 0.12f, 1f),
                };
                _panel.AddChild(_stepItems[i]);

                InkTextBlock check = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = i == 0 ? "◇" : "◆",
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 16f),
                    Size = new Float2(20f, 16f),
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.JadeBright,
                };
                _stepItems[i].AddChild(check);

                _stepTitles[i] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = steps[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(36f, 16f),
                    Size = new Float2(PanelWidth - 100f, 16f),
                    TextColor = InkWashTheme.PaperBright,
                };
                _stepItems[i].AddChild(_stepTitles[i]);
            }
        }

        private void BuildRewards()
        {
            _rewardTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "完成奖励",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, RewardY),
                Size = new Float2(100f, 24f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_rewardTitle);

            string[] rewardNames = { "新手长剑", "铜钱 x500", "经验 x1,000", "回城符 x3" };
            string[] rewardCounts = { "", "", "", "" };

            _rewardItems = new InkPanel[rewardNames.Length];
            _rewardNames = new InkTextBlock[rewardNames.Length];
            _rewardCounts = new InkTextBlock[rewardNames.Length];

            float rewardItemY = RewardY + 32f;
            float rewardItemHeight = 40f;

            for (int i = 0; i < rewardNames.Length; i++)
            {
                _rewardItems[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(32f, rewardItemY + i * rewardItemHeight),
                    Size = new Float2(PanelWidth - 64f, rewardItemHeight - 4f),
                    BackgroundColor = new Color(0.1f, 0.09f, 0.08f, 1f),
                };
                _panel.AddChild(_rewardItems[i]);

                InkPanel iconBox = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 6f),
                    Size = new Float2(28f, 28f),
                    BackgroundColor = InkWashTheme.GoldDeep,
                };
                _rewardItems[i].AddChild(iconBox);

                _rewardNames[i] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = rewardNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(44f, 10f),
                    Size = new Float2(PanelWidth - 120f, 20f),
                    TextColor = InkWashTheme.PaperBright,
                };
                _rewardItems[i].AddChild(_rewardNames[i]);
            }
        }

        private void BuildActions()
        {
            float actionY = RewardY + 200f;

            _startButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "开始任务",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, actionY),
                Size = new Float2(PanelWidth - 64f, 44f),
            };
            _startButton.ButtonClicked += OnStartButtonClicked;
            _panel.AddChild(_startButton);

            _skipButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "跳过引导",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 120f) * 0.5f, actionY + 56f),
                Size = new Float2(120f, 28f),
            };
            _skipButton.ButtonClicked += OnSkipButtonClicked;
            _panel.AddChild(_skipButton);
        }

        public event Action Started;
        public event Action Skipped;
        public event Action Closed;

        private void OnStartButtonClicked(Button button)
        {
            Started?.Invoke();
        }

        private void OnSkipButtonClicked(Button button)
        {
            Skipped?.Invoke();
        }

        private void OnCloseButtonClicked(Button button)
        {
            Closed?.Invoke();
        }

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                _panel.Size = new Float2(PanelWidth, _screenSize.Y);
                _panel.Location = new Float2(_screenSize.X - PanelWidth, 0f);
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
    }

    // ===================================================================
    // PopupBestiarySide
    // =======================================================================

    public class PopupBestiarySide : ContainerControl, IInkPage
    {
        private const float PanelWidth = 420f;
        private const float IllustrationHeight = 180f;

        private InkPanelElevated _panel;
        private InkButton _closeButton;
        private InkButton _msgButton;

        private InkPanel _illustrationArea;
        private InkTextBlock _illustrationChar;
        private InkTextBlock _illustrationSeal;

        private InkTextBlock _entryTitle;
        private InkTextBlock _entryPinyin;

        private InkTextBlock _descTitle;
        private InkTextBlock _descText;
        private InkTextBlock _descTextSecondary;

        private InkTextBlock[] _attrLabels;
        private InkTextBlock[] _attrValues;

        private InkPanel _difficultyBar;
        private InkPanel _difficultyBarFill;
        private InkTextBlock _difficultyValue;

        private InkTextBlock[] _acqIndices;
        private InkTextBlock[] _acqTitles;
        private InkTextBlock[] _acqDescs;

        private InkButton _huntButton;
        private InkButton _trackButton;

        private Float2 _screenSize;

        public PopupBestiarySide()
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
                    AnchorPreset = AnchorPresets.TopRight,
                    Size = new Float2(PanelWidth, _screenSize.Y),
                    Location = new Float2(_screenSize.X - PanelWidth, 0f),
                };
                AddChild(_panel);

                BuildIllustration();
                BuildEntryName();
                BuildDescription();
                BuildAttributes();
                BuildAcquisition();
                BuildActions();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PopupBestiarySide] 初始化失败: {ex.Message}");
            }
        }

        private void BuildIllustration()
        {
            _illustrationArea = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(PanelWidth, IllustrationHeight),
                BackgroundColor = InkWashTheme.GoldDeep,
            };
            _panel.AddChild(_illustrationArea);

            _illustrationChar = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "麟",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 100f) * 0.5f, 30f),
                Size = new Float2(100f, 80f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 72f),
            };
            _illustrationArea.AddChild(_illustrationChar);

            _illustrationSeal = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "異獸",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 60f) * 0.5f, 118f),
                Size = new Float2(60f, 24f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.VermilionBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f),
            };
            _illustrationArea.AddChild(_illustrationSeal);
        }

        private void BuildEntryName()
        {
            float y = IllustrationHeight + 24f;

            _entryTitle = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "墨麒麟",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, y),
                Size = new Float2(200f, 32f),
                TextColor = InkWashTheme.PaperBright,
            };
            _panel.AddChild(_entryTitle);

            _entryPinyin = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "Mo Qi Lin",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, y + 36f),
                Size = new Float2(100f, 16f),
                TextColor = new Color(InkWashTheme.PaperFaded.R, InkWashTheme.PaperFaded.G, InkWashTheme.PaperFaded.B, 0.5f),
            };
            _panel.AddChild(_entryPinyin);

            InkTag tag1 = new InkTag
            {
                Text = "异兽",
                TagVariant = InkTagVariant.Vermilion,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, y + 60f),
                Size = new Float2(60f, 22f),
            };
            _panel.AddChild(tag1);

            InkTag tag2 = new InkTag
            {
                Text = "传说",
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(96f, y + 60f),
                Size = new Float2(60f, 22f),
            };
            _panel.AddChild(tag2);

            InkTag tag3 = new InkTag
            {
                Text = "木德",
                TagVariant = InkTagVariant.Default,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(160f, y + 60f),
                Size = new Float2(60f, 22f),
            };
            _panel.AddChild(tag3);

            InkTextBlock meta1 = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "昆仑山脉",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, y + 92f),
                Size = new Float2(100f, 16f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(meta1);

            InkTextBlock meta2 = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "辰时出现",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(144f, y + 92f),
                Size = new Float2(100f, 16f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(meta2);

            InkTextBlock meta3 = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "卷叁·异兽志",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(256f, y + 92f),
                Size = new Float2(130f, 16f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(meta3);
        }

        private void BuildDescription()
        {
            float y = IllustrationHeight + 136f;

            _descTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "志异",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, y),
                Size = new Float2(100f, 24f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_descTitle);

            _descText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "墨麒麟，上古异兽之一，通体墨色，脚踏祥云。性温善，喜食灵草，其鳞可入药，其角可铸兵。相传出没于昆仑山巅，每逢春雷初动之时，可闻其低吟于云海之间，声若洪钟，震彻百里。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, y + 32f),
                Size = new Float2(PanelWidth - 64f, 72f),
                TextColor = InkWashTheme.PaperBright,
            };
            _panel.AddChild(_descText);

            _descTextSecondary = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "古籍有载：\"麟之趾，振振公子，于嗟麟兮。\"世人以见麒麟为祥瑞之兆，然墨麒麟行踪诡秘，非常人所能遇也。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, y + 112f),
                Size = new Float2(PanelWidth - 64f, 32f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(_descTextSecondary);
        }

        private void BuildAttributes()
        {
            float y = IllustrationHeight + 260f;

            InkTextBlock attrTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "属性",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, y),
                Size = new Float2(100f, 24f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(attrTitle);

            string[] labels = { "等级", "气血", "攻击", "防御", "弱点", "抗性" };
            string[] values = { "Lv. 50", "25,000", "3,800", "2,500", "火属性", "水属性" };
            Color[] valueColors = { InkWashTheme.GoldBright, InkWashTheme.PaperBright, InkWashTheme.VermilionBright, InkWashTheme.PaperBright, InkWashTheme.VermilionBright, InkWashTheme.JadeBright };

            _attrLabels = new InkTextBlock[labels.Length];
            _attrValues = new InkTextBlock[labels.Length];

            float cellWidth = (PanelWidth - 64f) / 2f;
            float cellHeight = 40f;
            float startY = y + 32f;

            for (int i = 0; i < labels.Length; i++)
            {
                int row = i / 2;
                int col = i % 2;

                InkPanel attrCell = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(32f + col * cellWidth, startY + row * cellHeight),
                    Size = new Float2(cellWidth - 4f, cellHeight - 4f),
                    BackgroundColor = new Color(0.15f, 0.14f, 0.12f, 1f),
                };
                _panel.AddChild(attrCell);

                _attrLabels[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = labels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 8f),
                    Size = new Float2(cellWidth - 24f, 14f),
                    TextColor = InkWashTheme.PaperFaded,
                };
                attrCell.AddChild(_attrLabels[i]);

                _attrValues[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = values[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 22f),
                    Size = new Float2(cellWidth - 24f, 16f),
                    TextColor = valueColors[i],
                };
                attrCell.AddChild(_attrValues[i]);
            }

            float difficultyY = startY + 128f;

            InkTextBlock difficultyLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "捕获难度",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, difficultyY),
                Size = new Float2(100f, 16f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(difficultyLabel);

            _difficultyValue = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "极高",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelWidth - 72f, difficultyY),
                Size = new Float2(40f, 16f),
                HorizontalAlignment = TextAlignment.Far,
                TextColor = InkWashTheme.VermilionBright,
            };
            _panel.AddChild(_difficultyValue);

            _difficultyBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, difficultyY + 24f),
                Size = new Float2(PanelWidth - 64f, 8f),
                BackgroundColor = new Color(0f, 0f, 0f, 0.4f),
            };
            _panel.AddChild(_difficultyBar);

            _difficultyBarFill = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2((PanelWidth - 64f) * 0.92f, 8f),
                BackgroundColor = InkWashTheme.VermilionPrimary,
            };
            _difficultyBar.AddChild(_difficultyBarFill);
        }

        private void BuildAcquisition()
        {
            float y = IllustrationHeight + 410f;

            InkTextBlock acqTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "获取方式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, y),
                Size = new Float2(100f, 24f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(acqTitle);

            string[] indices = { "壹", "贰" };
            string[] titles = { "昆仑山巅 · 限时出现", "参与「异兽狩猎」活动" };
            string[] descs = { "春雷时节，辰时方显踪迹", "每周五、六限时开放" };

            _acqIndices = new InkTextBlock[indices.Length];
            _acqTitles = new InkTextBlock[indices.Length];
            _acqDescs = new InkTextBlock[indices.Length];

            float rowHeight = 56f;
            float startY = y + 32f;

            for (int i = 0; i < indices.Length; i++)
            {
                InkPanel acqRow = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(32f, startY + i * rowHeight),
                    Size = new Float2(PanelWidth - 64f, rowHeight - 8f),
                    BackgroundColor = new Color(0.15f, 0.14f, 0.12f, 1f),
                };
                _panel.AddChild(acqRow);

                _acqIndices[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = indices[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 14f),
                    Size = new Float2(24f, 16f),
                    TextColor = InkWashTheme.GoldBright,
                };
                acqRow.AddChild(_acqIndices[i]);

                _acqTitles[i] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = titles[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(44f, 10f),
                    Size = new Float2(PanelWidth - 120f, 20f),
                    TextColor = InkWashTheme.PaperBright,
                };
                acqRow.AddChild(_acqTitles[i]);

                _acqDescs[i] = new InkTextBlock(InkTextStyle.Caption)
                {
                    Text = descs[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(44f, 30f),
                    Size = new Float2(PanelWidth - 120f, 16f),
                    TextColor = InkWashTheme.PaperFaded,
                };
                acqRow.AddChild(_acqDescs[i]);
            }
        }

        private void BuildActions()
        {
            float actionY = IllustrationHeight + 520f;

            _huntButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "前往狩猎",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(32f, actionY),
                Size = new Float2(PanelWidth - 64f, 44f),
            };
            _huntButton.ButtonClicked += OnHuntButtonClicked;
            _panel.AddChild(_huntButton);

            _trackButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "追踪位置",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 120f) * 0.5f, actionY + 56f),
                Size = new Float2(120f, 28f),
            };
            _trackButton.ButtonClicked += OnTrackButtonClicked;
            _panel.AddChild(_trackButton);
        }

        public event Action Hunt;
        public event Action Track;
        public event Action Closed;

        private void OnHuntButtonClicked(Button button)
        {
            Hunt?.Invoke();
        }

        private void OnTrackButtonClicked(Button button)
        {
            Track?.Invoke();
        }

        private void OnCloseButtonClicked(Button button)
        {
            Closed?.Invoke();
        }

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                _panel.Size = new Float2(PanelWidth, _screenSize.Y);
                _panel.Location = new Float2(_screenSize.X - PanelWidth, 0f);
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
    }
}
