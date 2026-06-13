using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 捏脸预设数据
    /// </summary>
    public class FacePresetData
    {
        public int Id;
        public string Name;
        public string ThumbnailPath;
        public Dictionary<string, float> FaceParameters;

        public FacePresetData()
        {
            FaceParameters = new Dictionary<string, float>();
        }

        /// <summary>
        /// 获取指定性别的默认预设列表
        /// </summary>
        /// <param name="gender">性别：male 或 female</param>
        /// <returns>8个预设的列表</returns>
        public static List<FacePresetData> GetDefaultPresets(string gender)
        {
            string[] names;
            if (gender.ToLower() == "male")
            {
                names = new[] { "清秀", "俊朗", "英气", "刚毅", "儒雅", "豪迈", "冷峻", "阳光" };
            }
            else
            {
                names = new[] { "温婉", "妩媚", "端庄", "灵动", "清丽", "冷艳", "甜美", "温柔" };
            }

            var presets = new List<FacePresetData>();
            for (int i = 0; i < names.Length; i++)
            {
                presets.Add(new FacePresetData
                {
                    Id = i,
                    Name = names[i],
                    ThumbnailPath = null,
                    FaceParameters = new Dictionary<string, float>()
                });
            }

            return presets;
        }
    }

    /// <summary>
    /// 捏脸预设卡片组件
    /// 用于角色创建界面中展示和选择预设脸型
    /// </summary>
    public class FacePresetCard : Panel
    {
        private Label _nameLabel;
        private Label _numberLabel;
        private bool _isSelected;
        private bool _isHover;
        private Float2 _baseSize;
        private Color _borderColor = Color.Transparent;
        private Float4 _borderThickness = Float4.Zero;
        private Color _shadowColor = ShadowColor;

        /// <summary>
        /// 标准金色 RGB(212,175,55) - 选中态边框/阴影金色
        /// </summary>
        public static readonly Color GoldColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1f);
        /// <summary>
        /// 阴影深色（半透明黑），选中时提升到 0.8f 透明度
        /// </summary>
        public static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.5f);
        public static readonly Color ShadowColorSelected = new Color(0f, 0f, 0f, 0.8f);

        /// <summary>
        /// 预设数据
        /// </summary>
        public FacePresetData PresetData { get; set; }

        /// <summary>
        /// 选中事件
        /// </summary>
        public event Action<FacePresetCard> OnSelected;

        /// <summary>
        /// 是否处于选中状态
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                UpdateVisual();
            }
        }

        /// <summary>
        /// 创建预设卡片
        /// </summary>
        /// <param name="presetData">预设数据</param>
        public FacePresetCard(FacePresetData presetData)
        {
            PresetData = presetData;
            // 更宽的卡片尺寸，匹配参考图
            _baseSize = new Float2(300, 110);
            Size = _baseSize;
            AnchorPreset = AnchorPresets.HorizontalStretchTop;
            BackgroundColor = new Color(15f / 255f, 15f / 255f, 20f / 255f, 0.85f);
            // 默认无边框，选中态切换为金色 3px 边框
            _borderColor = Color.Transparent;
            _borderThickness = new Float4(0);

            // 阴影在 DrawSelf 中绘制（在卡片背景之下）
            _shadowColor = ShadowColor;

            // 编号标签（左上角，小字体）
            _numberLabel = new Label
            {
                Text = (presetData.Id + 1).ToString(),
                Font = UIHelper.SetFont(size: 10),
                TextColor = new Color(ChineseClassicalTheme.TextColor.R, ChineseClassicalTheme.TextColor.G, ChineseClassicalTheme.TextColor.B, 0.5f),
                Location = new Float2(6, 4),
                Size = new Float2(20, 16),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            AddChild(_numberLabel);

            // 预设名称标签（底部居中）
            _nameLabel = new Label
            {
                Text = presetData.Name,
                Font = UIHelper.DefaultFont,
                TextColor = Color.White,
                Location = new Float2(0, 80),
                Size = new Float2(300, 26),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            AddChild(_nameLabel);

            // 初始化时调用一次以应用默认视觉
            UpdateVisual();
        }

        /// <summary>
        /// 检测鼠标悬停状态变化（OnMouseEnter/OnMouseLeave 在此 Flax 版本不可覆盖）
        /// </summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            bool isHover = IsMouseOver;
            if (isHover != _isHover)
            {
                _isHover = isHover;
                UpdateVisual();
            }
        }

        /// <summary>
        /// 更新选中状态视觉效果
        /// </summary>
        private void UpdateVisual()
        {
            // 选中时缩放 1.05x（Flax Engine Control 无 Scale 属性，通过修改 Size 实现）
            if (_isSelected)
            {
                Size = new Float2(_baseSize.X * 1.05f, _baseSize.Y * 1.05f);
            }
            else
            {
                Size = _baseSize;
            }

            // 选中态：金色 3px 四边边框 + 阴影提升到 0.8f 透明度
            if (_isSelected)
            {
                _borderColor = GoldColor;
                _borderThickness = new Float4(3);
                _shadowColor = ShadowColorSelected;
            }
            else
            {
                _borderColor = Color.Transparent;
                _borderThickness = new Float4(0);
                _shadowColor = ShadowColor;
            }

            // 根据 hover 与 selected 状态更新背景色与文字色
            if (_isSelected)
            {
                if (_isHover)
                {
                    BackgroundColor = new Color(25f / 255f, 25f / 255f, 35f / 255f, 0.85f);
                }
                else
                {
                    BackgroundColor = new Color(
                        ChineseClassicalTheme.SecondaryColor.R,
                        ChineseClassicalTheme.SecondaryColor.G,
                        ChineseClassicalTheme.SecondaryColor.B,
                        0.5f);
                }
                _nameLabel.TextColor = ChineseClassicalTheme.SecondaryColor;
            }
            else
            {
                if (_isHover)
                {
                    BackgroundColor = new Color(20f / 255f, 20f / 255f, 28f / 255f, 0.85f);
                }
                else
                {
                    BackgroundColor = new Color(15f / 255f, 15f / 255f, 20f / 255f, 0.85f);
                }
                _nameLabel.TextColor = Color.White;
            }
        }

        /// <summary>
        /// 手动绘制边框（Panel 不支持 BorderColor/BorderThickness）
        /// </summary>
        public override void DrawSelf()
        {
            // 先绘制阴影（在卡片背景之下）
            if (_shadowColor.A > 0f)
            {
                var shadowRect = new Rectangle(3, 3, Size.X, Size.Y);
                Render2D.FillRectangle(shadowRect, _shadowColor);
            }

            base.DrawSelf();

            // 再绘制边框（在卡片背景之上）
            float maxThickness = Mathf.Max(_borderThickness.X, _borderThickness.Y, _borderThickness.Z, _borderThickness.W);
            if (_borderColor.A > 0f && maxThickness > 0f)
            {
                Render2D.DrawRectangle(new Rectangle(Float2.Zero, Size), _borderColor, maxThickness);
            }
        }

        /// <summary>
        /// 鼠标按下事件处理
        /// </summary>
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                IsSelected = true;
                OnSelected?.Invoke(this);
                return true;
            }

            return base.OnMouseDown(location, button);
        }
    }
}