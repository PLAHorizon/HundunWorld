using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 全局角色 ID 标签控件
    /// 封装 ID 显示逻辑，支持阴影效果
    /// </summary>
    public class CharacterIdLabel : ContainerControl
    {
        private Label _label;
        private Label _shadowLabel;
        private string _characterId = "0126998214";

        /// <summary>
        /// 当前角色 ID
        /// </summary>
        public string CharacterId
        {
            get => _characterId;
            set
            {
                if (_characterId != value)
                {
                    _characterId = value;
                    UpdateLabelText();
                }
            }
        }

        public CharacterIdLabel()
        {
            // 容器透明，仅用于组合阴影和主标签
            BackgroundColor = Color.Transparent;
            Size = new Float2(280, 24);

            CreateUI();
        }

        private void CreateUI()
        {
            // 阴影层（50% 透明黑色，偏移 1px）
            _shadowLabel = new Label
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                Text = $"ID: {_characterId}",
                TextColor = new Color(0f, 0f, 0f, 0.5f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Font = new FontReference { Size = 16 },
                Location = new Float2(1, 1)
            };

            // 主标签（金色 RGB(212,175,55)，16pt）
            _label = new Label
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                Text = $"ID: {_characterId}",
                TextColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Font = new FontReference { Size = 16 }
            };
        }

        private void UpdateLabelText()
        {
            if (_label != null)
                _label.Text = $"ID: {_characterId}";
            if (_shadowLabel != null)
                _shadowLabel.Text = $"ID: {_characterId}";
        }

        /// <summary>
        /// 设置标签位置
        /// </summary>
        public void SetPosition(Float2 position)
        {
            Location = position;
        }
    }
}
