using FlaxEngine;
using FlaxEngine.GUI;
using System;
using System.Collections.Generic;
using Game.Character.Attributes;
using Horizon.Game.Message.Network; // 添加网络消息命名空间
using Horizon.Game.Message.Enums; // 添加枚举命名空间
using System.Linq;
using System.Threading.Tasks; // 添加Task支持
using HundunWorld.Game.Services;
using static HundunWorld.Game.UI.UIHelper;

public class RoundedPanel : Panel
{
    public float CornerRadius { get; set; } = 10.0f;
    
    public override void Draw()
    {
        if (!Visible) return;
        
        var rect = new Rectangle(Vector2.Zero, Size);
        DrawRoundedBox(rect, BackgroundColor, CornerRadius);
        
        // 绘制子控件
        DrawChildren();
    }
    
    private void DrawRoundedBox(Rectangle rect, Color color, float radius)
    {
        if (radius <= 0)
        {
            Render2D.FillRectangle(rect, color);
            return;
        }
        
        radius = Mathf.Min(radius, rect.Width / 2f, rect.Height / 2f);
        
        // 绘制中心区域
        Render2D.FillRectangle(new Rectangle(rect.X + radius, rect.Y, rect.Width - radius * 2, rect.Height), color);
        Render2D.FillRectangle(new Rectangle(rect.X, rect.Y + radius, radius, rect.Height - radius * 2), color);
        Render2D.FillRectangle(new Rectangle(rect.Right - radius, rect.Y + radius, radius, rect.Height - radius * 2), color);
        
        // 绘制圆角（简化版本）
        // 注意：Flax的Render2D可能没有FillCircle方法，使用矩形近似
        Render2D.FillRectangle(new Rectangle(rect.X, rect.Y, radius * 2, radius * 2), color);
        Render2D.FillRectangle(new Rectangle(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2), color);
        Render2D.FillRectangle(new Rectangle(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2), color);
        Render2D.FillRectangle(new Rectangle(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2), color);
    }
}

// 注意：UIAnimationManager、UIHelper、ToastType、CharacterService已在其他文件中定义
// 删除此处的重复定义以避免命名冲突

// ComboBox是Dropdown的别名，简化使用
public class ComboBox : Dropdown
{
    public ComboBox() : base()
    {
    }
}

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 角色创建面板
    /// 提供完整的角色创建和自定义功能
    /// </summary>
    public class CharacterCreationPanel : Panel
    {
        #region 事件定义
        public event Action<CharacterInfo> CharacterCreated;
        public event Action Cancelled;
        #endregion

        #region UI组件
        private Label _titleLabel;
        private Panel _contentPanel;
        
        // 基本信息区域
        private TextBox _nameInput;
        private ComboBox _genderCombo;
        private ComboBox _classCombo;
        private ComboBox _appearanceCombo;
        
        // 属性分配区域
        private AttributeAllocator _attributeAllocator;
        
        // 预览区域
        private Image _previewImage;
        private Label _previewName;
        
        // 按钮区域
        private Button _createButton;
        private Button _cancelButton;
        private Button _randomizeButton;
        #endregion

        #region 数据
        private List<string> _availableClasses;
        private List<string> _availableAppearances;
        private CharacterInfo _newCharacter;
        #endregion

        public CharacterCreationPanel()
        {
            InitializeData();
            CreateUI();
        }

        #region 初始化
        private void InitializeData()
        {
            _availableClasses = new List<string>
            {
                "剑客", "刀客", "枪客", "弓手", "法师", "道士", "刺客", "医师"
            };
            
            _availableAppearances = new List<string>
            {
                "默认", "战士型", "法师型", "敏捷型", "力量型"
            };
            
            _newCharacter = new CharacterInfo();
        }

        private void CreateUI()
        {
            // 标题
            _titleLabel = new Label
            {
                Text = "创建新角色",
                Bounds = new Rectangle(0, 20, Width, 40),
                TextColor = UIStyleTokens.TextPrimary,
                HorizontalAlignment = TextAlignment.Center,
                Font = new FontReference(FlaxEngine.Content.LoadAsyncInternal<FontAsset>(@"Fonts\Arial"), 24)
            };
            AddChild(_titleLabel);
            
            // 内容面板
            _contentPanel = new Panel
            {
                Bounds = new Rectangle(20, 80, Width - 40, Height - 140),
                BackgroundColor = UIStyleTokens.InkPanel(0.7f)
            };
            AddChild(_contentPanel);
            
            CreateBasicInfoSection();
            CreateAttributeSection();
            CreatePreviewSection();
            CreateButtonSection();
        }

        private void CreateBasicInfoSection()
        {
            float yPos = 20f;
            float labelWidth = 100f;
            float inputWidth = 200f;
            float spacing = 10f;
            
            // 角色名称
            var nameLabel = new Label
            {
                Text = "角色名称:",
                Bounds = new Rectangle(20, yPos, labelWidth, 25),
                TextColor = UIStyleTokens.TextPrimary
            };
            _contentPanel.AddChild(nameLabel);
            
            _nameInput = new TextBox
            {
                WatermarkText = "请输入角色名称",  // Flax中使用WatermarkText而不是PlaceholderText
                Bounds = new Rectangle(130, yPos, inputWidth, 30)
            };
            _nameInput.TextChanged += OnNameChanged;
            _contentPanel.AddChild(_nameInput);
            
            yPos += 40f;
            
            // 性别选择
            var genderLabel = new Label
            {
                Text = "性别:",
                Bounds = new Rectangle(20, yPos, labelWidth, 25),
                TextColor = UIStyleTokens.TextPrimary
            };
            _contentPanel.AddChild(genderLabel);
            
            _genderCombo = new ComboBox
            {
                Items = { "男", "女" },
                SelectedIndex = 0,
                Bounds = new Rectangle(130, yPos, 100, 30)
            };
            _contentPanel.AddChild(_genderCombo);
            
            yPos += 40f;
            
            // 职业选择
            var classLabel = new Label
            {
                Text = "职业:",
                Bounds = new Rectangle(20, yPos, labelWidth, 25),
                TextColor = UIStyleTokens.TextPrimary
            };
            _contentPanel.AddChild(classLabel);
            
            _classCombo = new ComboBox
            {
                Items = new List<LocalizedString>(_availableClasses.Select(s => new LocalizedString(s))),
                SelectedIndex = 0,
                Bounds = new Rectangle(130, yPos, 150, 30)
            };
            _classCombo.SelectedIndexChanged += OnClassChanged;
            _contentPanel.AddChild(_classCombo);
            
            yPos += 40f;
            
            // 外观选择
            var appearanceLabel = new Label
            {
                Text = "外观:",
                Bounds = new Rectangle(20, yPos, labelWidth, 25),
                TextColor = UIStyleTokens.TextPrimary
            };
            _contentPanel.AddChild(appearanceLabel);
            
            _appearanceCombo = new ComboBox
            {
                Items = new List<LocalizedString>(_availableAppearances.Select(s => new LocalizedString(s))),
                SelectedIndex = 0,
                Bounds = new Rectangle(130, yPos, 150, 30)
            };
            _appearanceCombo.SelectedIndexChanged += OnAppearanceChanged;
            _contentPanel.AddChild(_appearanceCombo);
        }

        private void CreateAttributeSection()
        {
            _attributeAllocator = new AttributeAllocator
            {
                Bounds = new Rectangle(350, 20, 250, 200)
            };
            _contentPanel.AddChild(_attributeAllocator);
        }

        private void CreatePreviewSection()
        {
            // 预览图像
            _previewImage = new Image
            {
                Bounds = new Rectangle(Width - 220, 20, 200, 300),
                Brush = new SpriteBrush(), // 移除null参数
                BackgroundColor = UIStyleTokens.WithAlpha(UIStyleTokens.BgElevated, 0.8f)
            };
            _contentPanel.AddChild(_previewImage);
            
            // 预览名称
            _previewName = new Label
            {
                Text = "预览角色",
                Bounds = new Rectangle(Width - 220, 330, 200, 25),
                TextColor = UIStyleTokens.TextPrimary,
                HorizontalAlignment = TextAlignment.Center
            };
            _contentPanel.AddChild(_previewName);
        }

        private void CreateButtonSection()
        {
            float buttonWidth = 100f;
            float buttonHeight = 35f;
            float spacing = 15f;
            
            // 随机化按钮
            _randomizeButton = new Button
            {
                Text = "随机生成",
                Bounds = new Rectangle(20, Height - 55, buttonWidth, buttonHeight),
                BackgroundColor = UIStyleTokens.StatusAlert * 0.7f // 提醒色（--status-alert-default）
            };
            _randomizeButton.ButtonClicked += OnRandomizeClicked;  // 使用ButtonClicked而不是Clicked
            AddChild(_randomizeButton);
            
            // 取消按钮
            _cancelButton = new Button
            {
                Text = "取消",
                Bounds = new Rectangle(Width - buttonWidth * 2 - spacing, Height - 55, buttonWidth, buttonHeight),
                BackgroundColor = UIStyleTokens.TextMuted * 0.7f // 次操作灰（--ink-text-muted）
            };
            _cancelButton.ButtonClicked += OnCancelClicked;  // 使用ButtonClicked而不是Clicked
            AddChild(_cancelButton);
            
            // 创建按钮
            _createButton = new Button
            {
                Text = "创建角色",
                Bounds = new Rectangle(Width - buttonWidth, Height - 55, buttonWidth, buttonHeight),
                BackgroundColor = UIStyleTokens.StatusSuccess * 0.7f // 成功色（--status-success-default）
            };
            _createButton.ButtonClicked += OnCreateClicked;  // 使用ButtonClicked而不是Clicked
            AddChild(_createButton);
        }
        #endregion

        #region 事件处理
        private void OnNameChanged()
        {
            _previewName.Text = string.IsNullOrEmpty(_nameInput.Text) ? "预览角色" : _nameInput.Text;
            UpdateCreateButtonState();
        }

        private void OnClassChanged(Dropdown dropdown)
        {
            UpdateCharacterPreview();
        }

        private void OnAppearanceChanged(Dropdown dropdown)
        {
            UpdateCharacterPreview();
        }

        private void OnRandomizeClicked(Button button)
        {
            RandomizeCharacter();
        }

        private void OnCancelClicked(Button button)
        {
            Cancelled?.Invoke();
        }

        private async void OnCreateClicked(Button button)
        {
            if (!ValidateInput()) return;
            
            try
            {
                // 创建角色数据
                var characterService = CharacterService.Instance;
                var appearance = new AppearanceInfo
                {
                    HairModel = _appearanceCombo.SelectedIndex,
                    HairStyle = 0,
                    HairColor = 0,
                    FaceModel = 0,
                    SkinColor = 0
                };
                var response = await characterService.CreateCharacterAsync(
                    _nameInput.Text,
                    (Profession)_classCombo.SelectedIndex,
                    _genderCombo.SelectedIndex,
                    appearance
                );
                var newCharacter = response?.Character;
                
                if (newCharacter != null)
                {
                    CharacterCreated?.Invoke(newCharacter);
                    ResetForm();
                }
                else
                {
                    UIHelper.ShowToast("角色创建失败", ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharCreation] 角色创建失败: {ex.Message}");
                UIHelper.ShowToast("角色创建失败: " + ex.Message, ToastType.Error);
            }
        }

        private bool ValidateInput()
        {
            // 检查名称
            if (string.IsNullOrWhiteSpace(_nameInput.Text))
            {
                UIHelper.ShowToast("请输入角色名称", ToastType.Warning);
                return false;
            }
            
            if (_nameInput.Text.Length < 2 || _nameInput.Text.Length > 12)
            {
                UIHelper.ShowToast("角色名称长度应在2-12个字符之间", ToastType.Warning);
                return false;
            }
            
            // 检查属性分配
            if (!_attributeAllocator.IsAllocationValid())
            {
                UIHelper.ShowToast("请正确分配属性点", ToastType.Warning);
                return false;
            }
            
            return true;
        }

        private void RandomizeCharacter()
        {
            var random = new Random();
            
            // 随机名称
            var randomNames = new[] { "无名剑客", "青衣剑士", "紫霞仙子", "铁血战士", "影舞者", "星辰法师" };
            _nameInput.Text = randomNames[random.Next(randomNames.Length)];
            
            // 随机性别
            _genderCombo.SelectedIndex = random.Next(2);
            
            // 随机职业
            _classCombo.SelectedIndex = random.Next(_availableClasses.Count);
            
            // 随机属性分配
            _attributeAllocator.RandomizeAllocation();
            
            // 随机外观
            _appearanceCombo.SelectedIndex = random.Next(_availableAppearances.Count);
            
            UpdateCharacterPreview();
            UIHelper.ShowToast("角色已随机生成", ToastType.Info);
        }

        private void UpdateCharacterPreview()
        {
            // 这里应该更新角色预览图像
            // 根据选择的职业、外观等显示对应的预览图
            Debug.Log("[CharCreation] 角色预览已更新");
        }

        private void UpdateCreateButtonState()
        {
            _createButton.Enabled = !string.IsNullOrWhiteSpace(_nameInput.Text);
        }

        private void ResetForm()
        {
            _nameInput.Text = "";
            _genderCombo.SelectedIndex = 0;
            _classCombo.SelectedIndex = 0;
            _appearanceCombo.SelectedIndex = 0;
            _attributeAllocator.ResetAllocation();
            _previewName.Text = "预览角色";
            UpdateCreateButtonState();
        }
        #endregion

        #region 公共接口
        public void Show()
        {
            Visible = true;
            ResetForm();
            UIHelper.ShowToast("欢迎创建新角色！", ToastType.Info);
        }

        public void Hide()
        {
            Visible = false;
        }

        public bool IsValid => ValidateInput();
        #endregion
    }

    /// <summary>
    /// 属性分配器
    /// </summary>
    public class AttributeAllocator : Panel
    {
        private const int TOTAL_POINTS = 20;
        private const int MIN_ATTRIBUTE = 1;
        private const int MAX_ATTRIBUTE = 10;
        
        private Dictionary<string, int> _attributes;
        private Dictionary<string, Label> _valueLabels;
        private Dictionary<string, Button> _increaseButtons;
        private Dictionary<string, Button> _decreaseButtons;
        private Label _pointsLabel;
        private int _remainingPoints;

        public AttributeAllocator()
        {
            _attributes = new Dictionary<string, int>
            {
                ["力量"] = 5,
                ["敏捷"] = 5,
                ["智力"] = 5,
                ["体质"] = 5
            };
            
            _valueLabels = new Dictionary<string, Label>();
            _increaseButtons = new Dictionary<string, Button>();
            _decreaseButtons = new Dictionary<string, Button>();
            
            _remainingPoints = 0;
            CreateUI();
        }

        private void CreateUI()
        {
            // 标题
            var titleLabel = new Label
            {
                Text = "属性分配",
                Bounds = new Rectangle(0, 0, Width, 25),
                TextColor = UIStyleTokens.TextGold,
                HorizontalAlignment = TextAlignment.Center
            };
            AddChild(titleLabel);
            
            // 剩余点数显示
            _pointsLabel = new Label
            {
                Text = $"剩余点数: {_remainingPoints}",
                Bounds = new Rectangle(0, 30, Width, 20),
                TextColor = UIStyleTokens.TextPrimary,
                HorizontalAlignment = TextAlignment.Center
            };
            AddChild(_pointsLabel);
            
            // 属性行
            float yPos = 60f;
            float rowHeight = 35f;
            
            foreach (var attr in _attributes)
            {
                CreateAttributeRow(attr.Key, yPos);
                yPos += rowHeight;
            }
            
            UpdateUI();
        }

        private void CreateAttributeRow(string attributeName, float yPos)
        {
            // 属性名称
            var nameLabel = new Label
            {
                Text = attributeName,
                Bounds = new Rectangle(0, yPos, 60, 25),
                TextColor = UIStyleTokens.TextPrimary
            };
            AddChild(nameLabel);
            
            // 减少按钮
            var decreaseBtn = new Button
            {
                Text = "-",
                Bounds = new Rectangle(70, yPos, 30, 25),
                BackgroundColor = UIStyleTokens.BloodPrimary * 0.5f // 血色 危险操作（--ink-blood-primary）
            };
            decreaseBtn.Tag = attributeName;
            decreaseBtn.ButtonClicked += (btn) => OnDecreaseAttribute(attributeName);
            AddChild(decreaseBtn);
            _decreaseButtons[attributeName] = decreaseBtn;
            
            // 数值显示
            var valueLabel = new Label
            {
                Text = _attributes[attributeName].ToString(),
                Bounds = new Rectangle(110, yPos, 30, 25),
                TextColor = UIStyleTokens.TextPrimary,
                HorizontalAlignment = TextAlignment.Center
            };
            AddChild(valueLabel);
            _valueLabels[attributeName] = valueLabel;
            
            // 增加按钮
            var increaseBtn = new Button
            {
                Text = "+",
                Bounds = new Rectangle(150, yPos, 30, 25),
                BackgroundColor = UIStyleTokens.JadePrimary * 0.5f // 水墨青 正向操作（--ink-jade-primary）
            };
            increaseBtn.Tag = attributeName;
            increaseBtn.ButtonClicked += (btn) => OnIncreaseAttribute(attributeName);
            AddChild(increaseBtn);
            _increaseButtons[attributeName] = increaseBtn;
        }

        private void OnIncreaseAttribute(string attributeName)
        {
            if (_remainingPoints <= 0) return;
            
            if (_attributes[attributeName] < MAX_ATTRIBUTE)
            {
                _attributes[attributeName]++;
                _remainingPoints--;
                UpdateUI();
            }
        }

        private void OnDecreaseAttribute(string attributeName)
        {
            if (_attributes[attributeName] > MIN_ATTRIBUTE)
            {
                _attributes[attributeName]--;
                _remainingPoints++;
                UpdateUI();
            }
        }

        private void UpdateUI()
        {
            foreach (var attr in _attributes)
            {
                _valueLabels[attr.Key].Text = attr.Value.ToString();
                _increaseButtons[attr.Key].Enabled = _remainingPoints > 0 && attr.Value < MAX_ATTRIBUTE;
                _decreaseButtons[attr.Key].Enabled = attr.Value > MIN_ATTRIBUTE;
            }
            
            _pointsLabel.Text = $"剩余点数: {_remainingPoints}";
        }

        public bool IsAllocationValid()
        {
            return _remainingPoints == 0 && _attributes.Values.All(v => v >= MIN_ATTRIBUTE && v <= MAX_ATTRIBUTE);
        }

        public void RandomizeAllocation()
        {
            var random = new Random();
            var values = new int[4];
            int sum = 0;
            
            // 生成4个随机数，总和为20
            for (int i = 0; i < 3; i++)
            {
                int max = Math.Min(MAX_ATTRIBUTE, TOTAL_POINTS - sum - (3 - i) * MIN_ATTRIBUTE);
                int min = Math.Max(MIN_ATTRIBUTE, TOTAL_POINTS - sum - (3 - i) * MAX_ATTRIBUTE);
                values[i] = random.Next(min, max + 1);
                sum += values[i];
            }
            values[3] = TOTAL_POINTS - sum;
            
            var attrNames = _attributes.Keys.ToArray();
            for (int i = 0; i < 4; i++)
            {
                _attributes[attrNames[i]] = values[i];
            }
            
            _remainingPoints = 0;
            UpdateUI();
        }

        public void ResetAllocation()
        {
            foreach (var key in _attributes.Keys)
            {
                _attributes[key] = 5;
            }
            _remainingPoints = TOTAL_POINTS - 20; // 4个属性 × 5点 = 20点
            UpdateUI();
        }

        public Dictionary<string, int> GetAllocatedAttributes()
        {
            return new Dictionary<string, int>(_attributes);
        }
    }
}