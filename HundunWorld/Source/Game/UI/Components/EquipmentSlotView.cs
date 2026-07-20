using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 装备插槽视图控件
    /// 魔兽世界风格：金属边框 + 石质凹底 + 品质色发光
    /// </summary>
    public class EquipmentSlotView : ContainerControl
    {
        public EquipmentSlot Slot { get; }
        public EquipmentData CurrentEquipment { get; private set; }
        public string EmptySlotName { get; set; }
        public event Action<EquipmentSlotView> Clicked;

        private Image _iconImage;
        private Panel _iconPlaceholder;
        private Label _iconPlaceholderLabel;
        private Label _nameLabel;
        private Label _itemLevelLabel;
        private Panel _itemLevelBg;
        private Panel _borderOuter;
        private Panel _borderInner;
        private Panel _glowPanel;
        private Panel _backgroundPanel;
        private Panel _iconWell;
        private bool _isHover;

        private const float BorderPadding = 2f;
        private const float NameLabelHeight = 16f;
        private const float ItemLevelHeight = 14f;

        public EquipmentSlotView(EquipmentSlot slot)
            : this(slot, new Float2(64f, 80f))
        {
        }

        public EquipmentSlotView(EquipmentSlot slot, Float2 size)
        {
            Slot = slot;
            Size = size;
            EmptySlotName = GetDefaultSlotName(slot);
            BackgroundColor = Color.Transparent;
            ClipChildren = false;

            BuildVisualHierarchy();
        }

        private void BuildVisualHierarchy()
        {
            float w = Width;
            float h = Height;

            // === 1. 品质色外发光层 ===
            _glowPanel = new Panel
            {
                Bounds = new Rectangle(-2f, -2f, w + 4f, h + 4f),
                BackgroundColor = Color.Transparent
            };
            AddChild(_glowPanel);

            // === 2. 外层金属边框（暗铜色） ===
            _borderOuter = new Panel
            {
                Bounds = new Rectangle(0, 0, w, h),
                BackgroundColor = ChineseClassicalTheme.MetalBorderColor
            };
            AddChild(_borderOuter);

            // === 3. 内层金色描边 ===
            _borderInner = new Panel
            {
                Bounds = new Rectangle(BorderPadding, BorderPadding, w - BorderPadding * 2f, h - BorderPadding * 2f),
                BackgroundColor = ChineseClassicalTheme.MetalBorderHighlightColor
            };
            AddChild(_borderInner);

            // === 4. 石质凹陷背景 ===
            _backgroundPanel = new Panel
            {
                Bounds = new Rectangle(BorderPadding + 1f, BorderPadding + 1f, w - (BorderPadding + 1f) * 2f, h - (BorderPadding + 1f) * 2f),
                BackgroundColor = ChineseClassicalTheme.DarkStoneInsetColor
            };
            AddChild(_backgroundPanel);

            // 凹陷内描边（深色）
            DrawInnerBorder(_backgroundPanel, _backgroundPanel.Width, _backgroundPanel.Height, ChineseClassicalTheme.WowInnerBorderColor);

            // 顶部金线装饰
            _backgroundPanel.AddChild(new Panel
            {
                Bounds = new Rectangle(0, 0, _backgroundPanel.Width, 1f),
                BackgroundColor = ChineseClassicalTheme.MetalBorderSoftHighlightColor
            });

            // === 5. 图标区域容器（凹陷 + 细边） ===
            float iconAreaTop = BorderPadding + 4f;
            float iconAreaHeight = h - NameLabelHeight - BorderPadding * 2f - 8f;
            float iconSize = Mathf.Min(w - BorderPadding * 2f - 8f, iconAreaHeight);
            float iconX = (w - iconSize) * 0.5f;
            float iconY = iconAreaTop + (iconAreaHeight - iconSize) * 0.5f;

            _iconWell = new Panel
            {
                Bounds = new Rectangle(iconX, iconY, iconSize, iconSize),
                BackgroundColor = ChineseClassicalTheme.DarkStoneBackgroundColor
            };
            AddChild(_iconWell);
            DrawInnerBorder(_iconWell, iconSize, iconSize, ChineseClassicalTheme.WowInnerBorderColor);

            // 图标内部占位
            _iconPlaceholder = new Panel
            {
                Bounds = new Rectangle(2f, 2f, iconSize - 4f, iconSize - 4f),
                BackgroundColor = Color.Transparent,
                Parent = _iconWell
            };

            _iconPlaceholderLabel = new Label
            {
                Bounds = new Rectangle(0, 0, iconSize - 4f, iconSize - 4f),
                Text = GetSlotShortName(Slot),
                Font = UIHelper.SetFont(size: Mathf.Max(10f, iconSize * 0.38f)),
                TextColor = ChineseClassicalTheme.WowSubTextColor,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _iconPlaceholder
            };

            // 图标图像
            _iconImage = new Image
            {
                Bounds = new Rectangle(2f, 2f, iconSize - 4f, iconSize - 4f),
                KeepAspectRatio = true,
                Color = Color.White,
                Visible = false,
                Parent = _iconWell
            };

            // === 6. 物品等级标签（右上角小盒子：金属边 + 深色底 + 金色数字） ===
            float ilvlBoxW = 26f;
            float ilvlBoxH = ItemLevelHeight;
            _itemLevelBg = new Panel
            {
                Bounds = new Rectangle(w - ilvlBoxW - BorderPadding - 1f, BorderPadding + 1f, ilvlBoxW, ilvlBoxH),
                BackgroundColor = ChineseClassicalTheme.DarkStoneBackgroundColor,
                Visible = false
            };
            AddChild(_itemLevelBg);
            // 金属边框
            DrawInnerBorder(_itemLevelBg, ilvlBoxW, ilvlBoxH, ChineseClassicalTheme.MetalBorderHighlightColor);

            _itemLevelLabel = new Label
            {
                Bounds = new Rectangle(0, 0, ilvlBoxW, ilvlBoxH),
                Text = string.Empty,
                TextColor = ChineseClassicalTheme.WowNumberTextColor,
                Font = UIHelper.SetFont(size: 10),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _itemLevelBg
            };

            // === 7. 底部装备/空槽名称 ===
            _nameLabel = new Label
            {
                Bounds = new Rectangle(BorderPadding + 2f, h - NameLabelHeight - BorderPadding, w - BorderPadding * 2f - 4f, NameLabelHeight),
                Text = EmptySlotName,
                TextColor = ChineseClassicalTheme.WowHintTextColor,
                Font = UIHelper.SetFont(size: 10),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            AddChild(_nameLabel);
        }

        private void DrawInnerBorder(Panel parent, float pw, float ph, Color color)
        {
            parent.AddChild(new Panel { Bounds = new Rectangle(0, 0, pw, 1f), BackgroundColor = color });
            parent.AddChild(new Panel { Bounds = new Rectangle(0, ph - 1f, pw, 1f), BackgroundColor = color });
            parent.AddChild(new Panel { Bounds = new Rectangle(0, 0, 1f, ph), BackgroundColor = color });
            parent.AddChild(new Panel { Bounds = new Rectangle(pw - 1f, 0, 1f, ph), BackgroundColor = color });
        }

        public void Refresh(EquipmentData equipment)
        {
            CurrentEquipment = equipment;
            bool hasEquipment = equipment != null;

            // 名称
            _nameLabel.Text = hasEquipment ? equipment.Name : EmptySlotName;
            _nameLabel.TextColor = hasEquipment
                ? ChineseClassicalTheme.GetQualityTextColor(equipment.Quality)
                : ChineseClassicalTheme.WowHintTextColor;

            // 图标占位
            _iconPlaceholderLabel.Text = hasEquipment && !string.IsNullOrEmpty(equipment.Name)
                ? equipment.Name[0].ToString()
                : GetSlotShortName(Slot);
            _iconPlaceholderLabel.TextColor = hasEquipment
                ? ChineseClassicalTheme.GetQualityTextColor(equipment.Quality)
                : ChineseClassicalTheme.WowSubTextColor;

            // 物品等级
            if (hasEquipment && equipment.ItemLevel > 0)
            {
                _itemLevelLabel.Text = equipment.ItemLevel.ToString();
                _itemLevelBg.Visible = true;
            }
            else
            {
                _itemLevelBg.Visible = false;
            }

            // 图标图像
            bool iconLoaded = false;
            if (hasEquipment && !string.IsNullOrEmpty(equipment.IconPath))
            {
                try
                {
                    var texture = Content.Load<Texture>(equipment.IconPath);
                    if (texture != null)
                    {
                        _iconImage.Brush = new TextureBrush(texture);
                        iconLoaded = true;
                    }
                }
                catch (Exception)
                {
                    // 图标加载失败回退到占位符
                }
            }

            _iconImage.Visible = iconLoaded;
            _iconPlaceholder.Visible = !iconLoaded;

            // 背景
            _backgroundPanel.BackgroundColor = hasEquipment
                ? ChineseClassicalTheme.DarkStonePanelColor
                : ChineseClassicalTheme.DarkStoneInsetColor;

            // 边框品质色
            Color borderColor = hasEquipment
                ? ChineseClassicalTheme.GetQualityColor(equipment.Quality)
                : ChineseClassicalTheme.MetalBorderColor;
            Color glowColor = hasEquipment
                ? ChineseClassicalTheme.GetQualityGlowColor(equipment.Quality)
                : Color.Transparent;

            _borderOuter.BackgroundColor = borderColor;
            _glowPanel.BackgroundColor = glowColor;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            bool hover = IsMouseOver;
            if (hover != _isHover)
            {
                _isHover = hover;
                UpdateHoverVisual();
            }
        }

        private void UpdateHoverVisual()
        {
            bool hasEquipment = CurrentEquipment != null;

            if (_isHover)
            {
                // 悬停时：背景变亮 + 金色边框高光 + 品质发光增强
                _backgroundPanel.BackgroundColor = ChineseClassicalTheme.DarkStonePanelHighlight;
                _borderOuter.BackgroundColor = ChineseClassicalTheme.MetalBorderSoftHighlightColor;
                _glowPanel.BackgroundColor = hasEquipment
                    ? ChineseClassicalTheme.GetQualityGlowColor(CurrentEquipment.Quality)
                    : new Color(ChineseClassicalTheme.MetalBorderHighlightColor.R, ChineseClassicalTheme.MetalBorderHighlightColor.G, ChineseClassicalTheme.MetalBorderHighlightColor.B, 0.3f);
            }
            else
            {
                _backgroundPanel.BackgroundColor = hasEquipment
                    ? ChineseClassicalTheme.DarkStonePanelColor
                    : ChineseClassicalTheme.DarkStoneInsetColor;

                Color borderColor = hasEquipment
                    ? ChineseClassicalTheme.GetQualityColor(CurrentEquipment.Quality)
                    : ChineseClassicalTheme.MetalBorderColor;
                _borderOuter.BackgroundColor = borderColor;

                _glowPanel.BackgroundColor = hasEquipment
                    ? ChineseClassicalTheme.GetQualityGlowColor(CurrentEquipment.Quality)
                    : Color.Transparent;
            }
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                Clicked?.Invoke(this);
                return true;
            }
            return base.OnMouseDown(location, button);
        }

        private static string GetDefaultSlotName(EquipmentSlot slot)
        {
            return slot switch
            {
                EquipmentSlot.Body => "衣服",
                EquipmentSlot.Head => "头盔",
                EquipmentSlot.Back => "披风",
                EquipmentSlot.RightHand => "右手",
                EquipmentSlot.LeftHand => "左手",
                EquipmentSlot.Waist => "腰带",
                EquipmentSlot.Face => "面具",
                EquipmentSlot.Neck => "项链",
                _ => "装备"
            };
        }

        private static string GetSlotShortName(EquipmentSlot slot)
        {
            return slot switch
            {
                EquipmentSlot.Body => "衣",
                EquipmentSlot.Head => "头",
                EquipmentSlot.Back => "披",
                EquipmentSlot.RightHand => "右",
                EquipmentSlot.LeftHand => "左",
                EquipmentSlot.Waist => "腰",
                EquipmentSlot.Face => "面",
                EquipmentSlot.Neck => "颈",
                _ => "空"
            };
        }
    }
}
