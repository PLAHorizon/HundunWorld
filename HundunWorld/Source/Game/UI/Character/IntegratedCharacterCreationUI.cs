using System;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.UI.MetaHuman;
using HundunWorld.Game.Services;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using static HundunWorld.Game.UI.UIHelper;
using HundunWorld.Game.UI.Components;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 集成的角色创建界面
    /// 结合基础信息输入和MetaHuman外观编辑功能
    /// </summary>
    public class IntegratedCharacterCreationUI : ContainerControl
    {
        #region 事件
        public event Action<CharacterInfo> OnCharacterCreated;
        public event Action OnCancelled;
        #endregion

        #region UI组件
        private Panel _leftPanel;          // 左侧：基础信息输入
        private Panel _rightPanel;         // 右侧：MetaHuman编辑器
        
        // 基础信息输入
        private ValidatedTextBox _nameInput;
        private Dropdown _genderDropdown;
        private Dropdown _professionDropdown;
        
        // 外观编辑器
        private MetaHumanEditorUI _metaHumanEditor;
        
        // 底部按钮
        private Panel _buttonPanel;
        private Button _createButton;
        private Button _cancelButton;
        private Button _randomButton;
        
        // 加载指示器
        private LoadingIndicator _loadingIndicator;
        
        // 提示标签
        private Label _statusLabel;
        #endregion

        #region 数据
        private AppearanceInfo _currentAppearance;
        private bool _isProcessing;
        #endregion

        #region 初始化
        public IntegratedCharacterCreationUI()
        {
            // 使用固定大小，确保布局正确
            Width = 820;
            Height = 670;  // 600内容区 + 70按钮区
            AnchorPreset = AnchorPresets.MiddleCenter;  // 居中显示
            
            BackgroundColor = new Color(0.08f, 0.08f, 0.10f, 1.0f);
            
            _currentAppearance = new AppearanceInfo();
            
            CreateUI();
            InitializeAppearanceDefaults();
        }

        private void CreateUI()
        {
            CreateLeftPanel();
            CreateRightPanel();
            CreateLoadingIndicator();
            CreateBottomButtonPanel();  // 最后创建，确保在最上层
        }

        private void CreateLeftPanel()
        {
            // 左侧面板只占用左半部分，不处理底部预留
            _leftPanel = new Panel
            {
                Parent = this,
                X = 0,
                Y = 0,
                Width = 320,
                Height = 600,  // 固定高度，确保显示
                BackgroundColor = new Color(0.12f, 0.12f, 0.15f, 1.0f)
            };

            float y = 20;
            float padding = 20;
            float labelWidth = 80;
            float inputWidth = 200;
            float rowHeight = 35;
            float spacing = 10;

            // 标题
            var titleLabel = new Label
            {
                Parent = _leftPanel,
                Text = "创建角色",
                X = padding,
                Y = y,
                Width = _leftPanel.Width - padding * 2,
                Height = 40,
                TextColor = new Color(1.0f, 0.84f, 0.0f),
                HorizontalAlignment = TextAlignment.Center
            };
            y += 50;

            // 角色名称
            var nameLabel = new Label
            {
                Parent = _leftPanel,
                Text = "角色名称",
                X = padding,
                Y = y + 5,
                Width = labelWidth,
                Height = 25,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };

            _nameInput = new ValidatedTextBox
            {
                Parent = _leftPanel,
                X = padding + labelWidth + 5,
                Y = y,
                Width = inputWidth,
                Height = rowHeight,
                WatermarkText = "请输入角色名",
                BackgroundColor = MetaHumanStyles.Colors.BackgroundDark,
                TextColor = MetaHumanStyles.Colors.TextPrimary
            };
            _nameInput.SetValidator(text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return (false, "角色名不能为空");
                if (text.Length < 2)
                    return (false, "角色名至少2个字符");
                if (text.Length > 12)
                    return (false, "角色名最多12个字符");
                return (true, "");
            });
            _nameInput.TextChanged += OnInputChanged;
            y += rowHeight + spacing;

            // 性别选择
            var genderLabel = new Label
            {
                Parent = _leftPanel,
                Text = "性别",
                X = padding,
                Y = y + 5,
                Width = labelWidth,
                Height = 25,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };

            _genderDropdown = new Dropdown
            {
                Parent = _leftPanel,
                X = padding + labelWidth + 5,
                Y = y,
                Width = inputWidth,
                Height = rowHeight,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundDark,
                TextColor = MetaHumanStyles.Colors.TextPrimary
            };
            _genderDropdown.AddItem("男");
            _genderDropdown.AddItem("女");
            _genderDropdown.SelectedIndex = 0;
            y += rowHeight + spacing;

            // 职业选择
            var professionLabel = new Label
            {
                Parent = _leftPanel,
                Text = "职业",
                X = padding,
                Y = y + 5,
                Width = labelWidth,
                Height = 25,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };

            _professionDropdown = new Dropdown
            {
                Parent = _leftPanel,
                X = padding + labelWidth + 5,
                Y = y,
                Width = inputWidth,
                Height = rowHeight,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundDark,
                TextColor = MetaHumanStyles.Colors.TextPrimary
            };
            _professionDropdown.AddItem("剑客");
            _professionDropdown.AddItem("刀客");
            _professionDropdown.AddItem("枪客");
            _professionDropdown.AddItem("弓手");
            _professionDropdown.AddItem("法师");
            _professionDropdown.AddItem("道士");
            _professionDropdown.AddItem("刺客");
            _professionDropdown.AddItem("医师");
            _professionDropdown.SelectedIndex = 0;
            y += rowHeight + spacing + 20;

            // 分隔线
            var separator = new Panel
            {
                Parent = _leftPanel,
                X = padding,
                Y = y,
                Width = _leftPanel.Width - padding * 2,
                Height = 1,
                BackgroundColor = MetaHumanStyles.Colors.Separator
            };
            y += 20;

            // 外观提示
            var appearanceHint = new Label
            {
                Parent = _leftPanel,
                Text = "右侧可自定义外观\n• 皮肤颜色和材质\n• 眼睛颜色和大小\n• 发型和发色",
                X = padding,
                Y = y,
                Width = _leftPanel.Width - padding * 2,
                Height = 100,
                TextColor = MetaHumanStyles.Colors.TextMuted,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                Wrapping = TextWrapping.WrapWords
            };
            y += 110;

            // 状态标签 - 固定在底部
            _statusLabel = new Label
            {
                Parent = _leftPanel,
                Text = "填写基础信息后点击创建",
                AnchorPreset = AnchorPresets.HorizontalStretchBottom,
                Offsets = new Margin(padding, padding, 0, 20),
                Height = 60,
                TextColor = MetaHumanStyles.Colors.TextMuted,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Wrapping = TextWrapping.WrapWords
            };
        }

        private void CreateRightPanel()
        {
            // 右侧面板固定在右边
            _rightPanel = new Panel
            {
                Parent = this,
                X = 320,
                Y = 0,
                Width = 500,  // 固定宽度
                Height = 600,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundDark
            };

            // 创建MetaHuman编辑器
            _metaHumanEditor = new MetaHumanEditorUI
            {
                Parent = _rightPanel,
                AnchorPreset = AnchorPresets.StretchAll
            };

            // 绑定外观变更事件
            _metaHumanEditor.OnTabChanged += OnEditorTabChanged;
        }

        private void CreateBottomButtonPanel()
        {
            // 底部按钮面板 - 绝对定位在底部，使用明亮的背景色确保可见
            _buttonPanel = new Panel
            {
                Parent = this,
                X = 0,
                Y = 600,  // 固定在左侧面板底部下方
                Width = 820,  // 320 + 500
                Height = 70,
                BackgroundColor = new Color(0.15f, 0.15f, 0.18f, 1.0f)  // 比主背景稍亮
            };
            
            // 确保按钮面板在最上层
            _buttonPanel.IndexInParent = 100;

            float buttonWidth = 120;
            float buttonHeight = 40;
            float spacing = 20;
            float leftMargin = 30;

            // 随机按钮 - 左侧
            _randomButton = MetaHumanStyles.CreateStyledButton("随机生成", buttonWidth, buttonHeight, ButtonStyle.Ghost);
            _randomButton.Parent = _buttonPanel;
            _randomButton.X = leftMargin;
            _randomButton.Y = 15;
            _randomButton.Clicked += OnRandomClicked;

            // 取消按钮 - 右侧
            _cancelButton = MetaHumanStyles.CreateStyledButton("取消", buttonWidth, buttonHeight, ButtonStyle.Default);
            _cancelButton.Parent = _buttonPanel;
            _cancelButton.X = _buttonPanel.Width - buttonWidth * 2 - spacing - leftMargin;
            _cancelButton.Y = 15;
            _cancelButton.Clicked += OnCancelClicked;

            // 创建按钮 - 最右侧，高亮
            _createButton = MetaHumanStyles.CreateStyledButton("创建角色", buttonWidth, buttonHeight, ButtonStyle.Accent);
            _createButton.Parent = _buttonPanel;
            _createButton.X = _buttonPanel.Width - buttonWidth - leftMargin;
            _createButton.Y = 15;
            _createButton.Clicked += OnCreateClicked;
            _createButton.Enabled = false;
        }

        private void CreateLoadingIndicator()
        {
            _loadingIndicator = UIHelper.CreateLoadingIndicator();
            _loadingIndicator.Parent = this;
            _loadingIndicator.Visible = false;
        }

        private void InitializeAppearanceDefaults()
        {
            _currentAppearance.HairModel = 0;
            _currentAppearance.HairStyle = 0;
            _currentAppearance.HairColor = 0;
            _currentAppearance.FaceModel = 0;
            _currentAppearance.SkinColor = 0;
        }
        #endregion

        #region 事件处理
        private void OnInputChanged(string text)
        {
            UpdateCreateButtonState();
        }

        private void OnEditorTabChanged(MetaHumanEditorUI.EditorTab tab)
        {
            // 可以根据选项卡切换更新提示信息
            Debug.Log($"[CharacterCreation] 切换到编辑器标签: {tab}");
        }

        private void OnRandomClicked()
        {
            // 随机生成角色名和职业
            var random = new Random();
            string[] surnames = { "李", "王", "张", "刘", "陈", "杨", "赵", "黄", "周", "吴" };
            string[] names = { "明", "华", "伟", "芳", "娜", "敏", "静", "丽", "强", "磊" };
            
            _nameInput.Text = surnames[random.Next(surnames.Length)] + names[random.Next(names.Length)];
            _genderDropdown.SelectedIndex = random.Next(2);
            _professionDropdown.SelectedIndex = random.Next(8);

            ShowStatus("已随机生成角色信息", new Color(0.3f, 0.8f, 0.3f));
        }

        private void OnCancelClicked()
        {
            OnCancelled?.Invoke();
        }

        private async void OnCreateClicked()
        {
            if (_isProcessing) return;

            // 验证输入
            if (!_nameInput.IsValid)
            {
                ShowStatus("请检查角色名称", new Color(0.9f, 0.3f, 0.3f));
                return;
            }

            string characterName = _nameInput.Text.Trim();
            int gender = _genderDropdown.SelectedIndex;
            Profession profession = (Profession)_professionDropdown.SelectedIndex;

            try
            {
                _isProcessing = true;
                _loadingIndicator.Show("正在创建角色...");
                _createButton.Enabled = false;

                // 从MetaHuman编辑器获取外观数据
                UpdateAppearanceFromEditor();

                // 调用CharacterService创建角色
                var characterService = CharacterService.Instance;
                var response = await characterService.CreateCharacterAsync(
                    characterName,
                    profession,
                    gender,
                    _currentAppearance
                );

                if (response != null && response.IsSuccess)
                {
                    ShowStatus("角色创建成功！", new Color(0.3f, 0.8f, 0.3f));
                    ToastMessage.ShowSuccess("角色创建成功！");
                    await Task.Delay(500);
                    
                    OnCharacterCreated?.Invoke(response.Character);
                }
                else
                {
                    string errorMsg = response?.Message ?? "未知错误";
                    ShowStatus($"创建失败: {errorMsg}", new Color(0.9f, 0.3f, 0.3f));
                    ToastMessage.ShowError(errorMsg);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterCreation] 创建角色异常: {ex.Message}");
                ShowStatus($"创建失败: {ex.Message}", new Color(0.9f, 0.3f, 0.3f));
                ToastMessage.ShowError($"创建失败: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                _loadingIndicator.Hide();
                UpdateCreateButtonState();
            }
        }

        private void UpdateAppearanceFromEditor()
        {
            // 从MetaHuman编辑器中提取外观数据
            // 这里需要MetaHumanEditorUI提供获取当前外观数据的接口
            // 暂时使用默认值
            // TODO: 添加MetaHumanEditorUI.GetCurrentAppearance()方法
        }

        private void UpdateCreateButtonState()
        {
            if (_isProcessing)
            {
                _createButton.Enabled = false;
                return;
            }

            bool isValid = _nameInput.IsValid && 
                          !string.IsNullOrWhiteSpace(_nameInput.Text);
            
            _createButton.Enabled = isValid;

            if (isValid)
            {
                _statusLabel.Text = "点击创建按钮完成角色创建";
                _statusLabel.TextColor = MetaHumanStyles.Colors.TextSecondary;
            }
            else
            {
                _statusLabel.Text = "请填写完整的角色信息";
                _statusLabel.TextColor = MetaHumanStyles.Colors.TextMuted;
            }
        }

        private void ShowStatus(string message, Color color)
        {
            _statusLabel.Text = message;
            _statusLabel.TextColor = color;
            
            // 3秒后恢复默认提示
            Task.Delay(3000).ContinueWith(_ =>
            {
                if (!_isProcessing)
                {
                    _statusLabel.Text = "填写基础信息后点击创建";
                    _statusLabel.TextColor = MetaHumanStyles.Colors.TextMuted;
                }
            });
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 显示UI
        /// </summary>
        public void Show()
        {
            Visible = true;
            Reset();
        }

        /// <summary>
        /// 隐藏UI
        /// </summary>
        public void Hide()
        {
            Visible = false;
        }

        /// <summary>
        /// 重置表单
        /// </summary>
        public void Reset()
        {
            _nameInput.Text = "";
            _genderDropdown.SelectedIndex = 0;
            _professionDropdown.SelectedIndex = 0;
            InitializeAppearanceDefaults();
            UpdateCreateButtonState();
            _statusLabel.Text = "填写基础信息后点击创建";
            _statusLabel.TextColor = MetaHumanStyles.Colors.TextMuted;
        }
        #endregion
    }
}
