using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI.StyleSystem;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.UI.Components;
using Game.UI.Character;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 角色场景控制器 - 场景协调器
    /// 重构后职责：
    /// 1. 协调各个管理器（UIFactory, StepNavigationManager, CharacterStateManager, UIComponentManager）
    /// 2. 处理场景生命周期（OnEnable/OnDisable/OnStart/OnUpdate）
    /// 3. 连接各管理器的事件流
    /// 4. 处理业务流程（进入游戏、创建角色等）
    /// </summary>
    public class CharacterSceneController : Script
    {
        private static bool _uiCreated = false;
        private bool _initialized;

        // 管理器
        private UIFactory _uiFactory;
        private StepNavigationManager _stepNavigationManager;
        private CharacterStateManager _characterStateManager;
        private UIComponentManager _uiComponentManager;

        // UI 组件引用
        private ContainerControl _guiContainer;
        private CharacterPreviewPanel _previewPanel;
        private SelectionModeUIComponents _selectionUI;
        private CreationModeUIComponents _creationUI;
        private StepIndicatorComponents _stepIndicator;
        private NextStepButton _ctrlNextStepButton;
        private CharacterIdLabel _globalIdLabelControl;

        /// <summary>
        /// 当前角色 ID
        /// </summary>
        public string CurrentCharacterId => _characterStateManager?.CurrentCharacterId ?? "0126998214";

        /// <summary>
        /// 全局 ID 标签控件
        /// </summary>
        public CharacterIdLabel GlobalIdLabelControl => _globalIdLabelControl;

        /// <summary>
        /// 3D 角色预览面板
        /// </summary>
        public CharacterPreviewPanel PreviewPanel => _previewPanel;

        /// <summary>
        /// 角色 ID 变更事件
        /// </summary>
        public event Action<string> OnCharacterIdChanged;

        public override void OnEnable()
        {
            _initialized = false;
            _uiCreated = false;
            Debug.Log("[CharacterSceneController] OnEnable");
        }

        public override void OnDisable()
        {
            var charMgr = CharacterManager.Instance;
            if (charMgr != null)
            {
                charMgr.CharacterListUpdated -= OnCharacterListUpdated;
            }
            _uiCreated = false;
            Debug.Log("[CharacterSceneController] OnDisable");
        }

        public override void OnStart()
        {
            Debug.Log("[CharacterSceneController] OnStart");
            _uiFactory = new UIFactory();
            _stepNavigationManager = new StepNavigationManager();
            _characterStateManager = new CharacterStateManager();
            _uiComponentManager = new UIComponentManager(_uiFactory);

            SubscribeManagerEvents();
            TryCreateUI();
        }

        public override void OnUpdate()
        {
            if (!_initialized)
            {
                TryCreateUI();
                return;
            }

            // 键盘快捷键: 按 Esc 退出创建模式
            if (_uiComponentManager.IsCreationMode && Input.GetKeyDown(KeyboardKeys.Escape))
            {
                Debug.Log("[CharacterSceneController] Esc 按下 -> 退出创建模式");
                ExitCreationMode();
            }

            // 步骤过渡动画
            if (_stepNavigationManager.UpdateTransition())
            {
                _uiComponentManager.UpdateStepTransition(_stepNavigationManager);
            }
            else
            {
                _uiComponentManager.RestoreButtonState();
            }

            // 创建模式下的强制清理
            _uiComponentManager.EnforceCreationModeCleanup();

            // 选择模式: 按钮 hover 效果
            _uiComponentManager.UpdateButtonHover();
        }

        /// <summary>
        /// 订阅各管理器事件
        /// </summary>
        private void SubscribeManagerEvents()
        {
            _stepNavigationManager.OnStepChanged += OnStepChanged;
            _characterStateManager.OnCharacterIdChanged += OnCharacterIdChangedInternal;
            _characterStateManager.OnCharacterListChanged += OnCharacterListChangedInternal;
            _uiComponentManager.OnCharacterItemSelected += OnCharacterItemSelected;
        }

        /// <summary>
        /// 创建 UI
        /// </summary>
        private void TryCreateUI()
        {
            if (_uiCreated)
            {
                _initialized = true;
                return;
            }

            // Step 1: 查找或创建 UICanvas
            var uiCanvas = _uiFactory.FindOrCreateUICanvas(Actor);
            if (uiCanvas?.GUI == null)
            {
                Debug.LogError("[CharacterSceneController] UICanvas.GUI 为 null！");
                return;
            }

            // Step 2: 配置 UICanvas
            _guiContainer = _uiFactory.ConfigureCanvas(uiCanvas);
            if (_guiContainer == null) return;

            var containerSize = _guiContainer.Size;
            if (containerSize.X <= 1 || containerSize.Y <= 1)
            {
                _initialized = false;
                _uiCreated = false;
                return;
            }

            // Step 2.5: 确保场景环境
            _uiFactory.EnsureSceneEnvironment(Actor?.Scene);

            _uiCreated = true;

            // Step 3: 创建基础 UI
            _previewPanel = _uiFactory.CreatePreviewPanel(_guiContainer, Actor?.Scene);
            _globalIdLabelControl = _uiFactory.CreateGlobalIdLabel(_guiContainer, 40, containerSize.Y - 72 - 30, _characterStateManager.CurrentCharacterId);
            _uiFactory.CreateVignette(_guiContainer, containerSize.X, containerSize.Y);

            // Step 3.1: 创建选择模式 UI
            _selectionUI = _uiFactory.CreateSelectionModeUI(_guiContainer, containerSize.X, containerSize.Y);
            SubscribeSelectionUIEvents();

            // Step 3.2: 创建创建模式 UI
            _creationUI = _uiFactory.CreateCreationModeUI(_guiContainer, _previewPanel);
            SubscribeCreationUIEvents();

            // Step 3.3: 创建控制器级 NextStepButton
            _ctrlNextStepButton = _uiFactory.CreateNextStepButton(_guiContainer, containerSize.X, containerSize.Y);
            _ctrlNextStepButton.OnClicked += OnCtrlNextStepButtonClicked;
            _creationUI.GenderSelectionUI.HideExternalButton();

            // Step 3.4: 创建步骤指示器
            _stepIndicator = _uiFactory.CreateStepIndicator(_guiContainer, containerSize.X);

            // Step 3.5: 初始化组件管理器
            _uiComponentManager.Initialize(_guiContainer, _selectionUI, _creationUI, _stepIndicator, _ctrlNextStepButton, _globalIdLabelControl);

            // Step 3.6: Z-order 管理
            _uiFactory.ManageZOrder(_guiContainer, _previewPanel, _ctrlNextStepButton);

            // 绑定预览面板
            TryBindPreviewPanelInScene(_guiContainer);

            _initialized = true;

            // 订阅 CharacterManager 事件
            var charMgr = CharacterManager.Instance;
            if (charMgr != null)
            {
                charMgr.CharacterListUpdated += OnCharacterListUpdated;
            }

            // 默认显示选择模式
            _uiComponentManager.SetCreationModeVisible(false);
            RefreshCharacterList();
        }

        /// <summary>
        /// 订阅选择模式 UI 事件
        /// </summary>
        private void SubscribeSelectionUIEvents()
        {
            _selectionUI.BackBtn.Clicked += OnBackBtnClicked;
            _selectionUI.CreateBtn.Clicked += OnCreateBtnClicked;
            _selectionUI.EnterBtn.Clicked += OnEnterBtnClicked;
        }

        /// <summary>
        /// 订阅创建模式 UI 事件
        /// </summary>
        private void SubscribeCreationUIEvents()
        {
            // 步骤1: 性别选择
            _creationUI.GenderSelectionUI.OnNextStep += () =>
            {
                _stepNavigationManager.StepData.Gender = _creationUI.GenderSelectionUI.SelectedGender;
                _stepNavigationManager.StepData.BodyHeight = _creationUI.GenderSelectionUI.BodyHeight;
                _stepNavigationManager.StepData.BodyType = _creationUI.GenderSelectionUI.BodyType;
                _stepNavigationManager.StepData.HeadSize = _creationUI.GenderSelectionUI.HeadSize;
                _stepNavigationManager.GoNext();
            };
            _creationUI.GenderSelectionUI.OnBodyParamsChanged += (h, b, hd) =>
            {
                _previewPanel?.ApplyBodyParams(h, b, hd);
            };

            // 步骤2: 脸型预设选择
            _creationUI.FacePresetSelectionUI.OnNextStep += () =>
            {
                if (_creationUI.FacePresetSelectionUI.SelectedPreset != null)
                {
                    _stepNavigationManager.StepData.SelectedPresetIndex = _creationUI.FacePresetSelectionUI.SelectedPreset.Id;
                    _stepNavigationManager.StepData.FacePresetName = _creationUI.FacePresetSelectionUI.SelectedPreset.Name;
                }
                _stepNavigationManager.GoNext();
            };
            _creationUI.FacePresetSelectionUI.OnGoBack += () => _stepNavigationManager.GoBack();

            // 步骤3: 精细捏脸
            _creationUI.IntegratedCreationUI.OnCompleteStep += () => _stepNavigationManager.GoNext();
            _creationUI.IntegratedCreationUI.OnCancelled += () => _stepNavigationManager.GoBack();

            // 步骤4: 命名完成
            _creationUI.NamingCompleteUI.OnGoBack += () => _stepNavigationManager.GoBack();
            _creationUI.NamingCompleteUI.OnComplete += (name) =>
            {
                _stepNavigationManager.StepData.CharacterName = name;
                _stepNavigationManager.StepData.Profession = (Profession)(_creationUI.NamingCompleteUI.SelectedProfessionIndex + 1);
                Debug.Log($"[CharacterSceneController] 角色创建完成: 名称={name}, 性别={_stepNavigationManager.StepData.Gender}, 职业={_stepNavigationManager.StepData.Profession}");
                OnCharacterCreationComplete();
            };
        }

        /// <summary>
        /// 控制器级 NextStepButton 点击事件
        /// </summary>
        private void OnCtrlNextStepButtonClicked()
        {
            if (_stepNavigationManager.CurrentStep == CreationStep.FacePreset &&
                _creationUI.FacePresetSelectionUI?.SelectedPreset != null)
            {
                _stepNavigationManager.StepData.SelectedPresetIndex = _creationUI.FacePresetSelectionUI.SelectedPreset.Id;
                _stepNavigationManager.StepData.FacePresetName = _creationUI.FacePresetSelectionUI.SelectedPreset.Name;
            }
            Debug.Log($"[CharacterSceneController] CtrlNextStepButton 点击 -> GoNext (step={_stepNavigationManager.CurrentStep})");
            _stepNavigationManager.GoNext();
        }

        /// <summary>
        /// 步骤切换事件处理
        /// </summary>
        private void OnStepChanged(CreationStep oldStep, CreationStep newStep)
        {
            Debug.Log($"[CharacterSceneController] 步骤切换: {oldStep} -> {newStep}");

            // 根据步骤更新 UI
            switch (newStep)
            {
                case CreationStep.FacePreset:
                    _uiComponentManager.SetFacePresetGender(_stepNavigationManager.StepData.Gender);
                    break;
                case CreationStep.DetailedCreation:
                    _uiComponentManager.SetIntegratedCreationStepData(_stepNavigationManager.StepData);
                    break;
            }

            _uiComponentManager.ShowCurrentStepUI(newStep);
            _uiComponentManager.UpdateStepIndicator(newStep);
        }

        // ==========================================
        // 按钮事件处理
        // ==========================================

        private void OnBackBtnClicked()
        {
            Debug.Log("[CharacterSceneController] 点击: 返回登录");
            var sceneManager = GameSceneManager.Instance;
            sceneManager?.TransitionTo(SceneType.Login);
        }

        private void OnCreateBtnClicked()
        {
            Debug.Log("[CharacterSceneController] 点击: 创建新角色 -> 进入创建模式");
            EnterCreationMode();
        }

        private async void OnEnterBtnClicked()
        {
            Debug.Log("[CharacterSceneController] 点击: 进入游戏");

            var selectedCharacter = _characterStateManager.SelectedCharacter;
            if (selectedCharacter == null)
            {
                Debug.LogWarning("[CharacterSceneController] 未选择角色，无法进入游戏");
                UIHelper.ShowToast("请先选择一个角色", ToastType.Warning);
                return;
            }

            try
            {
                var charMgr = CharacterManager.Instance;
                if (charMgr == null)
                {
                    Debug.LogError("[CharacterSceneController] CharacterManager 不可用");
                    UIHelper.ShowToast("系统错误", ToastType.Error);
                    return;
                }

                if (!charMgr.SelectCharacter(selectedCharacter))
                {
                    Debug.LogError("[CharacterSceneController] 选择角色失败");
                    UIHelper.ShowToast("选择角色失败", ToastType.Error);
                    return;
                }

                bool success = await charMgr.EnterGameAsync();
                if (success)
                {
                    Debug.Log($"[CharacterSceneController] 进入游戏请求已发送: {selectedCharacter.CharacterName}");
                }
                else
                {
                    Debug.LogError("[CharacterSceneController] 进入游戏失败");
                    UIHelper.ShowToast("进入游戏失败", ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterSceneController] 进入游戏异常: {ex.Message}");
                UIHelper.ShowToast("进入游戏失败", ToastType.Error);
            }
        }

        // ==========================================
        // 模式切换
        // ==========================================

        /// <summary>
        /// 进入角色创建模式
        /// </summary>
        public void EnterCreationMode()
        {
            Debug.Log("[CharacterSceneController] EnterCreationMode");
            _stepNavigationManager.Reset();
            _uiComponentManager.EnterCreationMode();
            _previewPanel?.ApplyBodyParams(0.55f, 0.55f, 0.50f);
        }

        /// <summary>
        /// 退出角色创建模式
        /// </summary>
        public void ExitCreationMode()
        {
            Debug.Log("[CharacterSceneController] ExitCreationMode");
            _uiComponentManager.ExitCreationMode();
            RefreshCharacterList();
        }

        // ==========================================
        // 角色列表管理
        // ==========================================

        private void OnCharacterListUpdated(List<CharacterInfo> characters)
        {
            Debug.Log($"[CharacterSceneController] OnCharacterListUpdated: {characters?.Count ?? 0} 个角色");
            _characterStateManager.UpdateCharacterList(characters);
        }

        private void OnCharacterListChangedInternal(List<CharacterInfo> characters)
        {
            if (!_uiComponentManager.IsCreationMode)
            {
                // 直接刷新 UI，不再调用 RefreshCharacterList()，避免无限递归：
                // RefreshCharacterList -> UpdateCharacterList -> OnCharacterListChanged -> RefreshCharacterList ...
                _uiComponentManager.RefreshCharacterList(characters, _characterStateManager.SelectedCharacter);
            }
        }

        private void OnCharacterItemSelected(CharacterInfo character)
        {
            _characterStateManager.SelectCharacter(character);
            // 直接刷新 UI，不触发 UpdateCharacterList 事件
            _uiComponentManager.RefreshCharacterList(_characterStateManager.GetCharacters(), _characterStateManager.SelectedCharacter);
        }

        private void RefreshCharacterList()
        {
            var characters = GetCharacterListFromSources();
            _characterStateManager.UpdateCharacterList(characters);
            // UpdateCharacterList 会触发 OnCharacterListChanged 事件，
            // 由 OnCharacterListChangedInternal 负责刷新 UI，这里不再重复调用
        }

        private List<CharacterInfo> GetCharacterListFromSources()
        {
            var charMgr = CharacterManager.Instance;
            if (charMgr != null && charMgr.CharacterList != null && charMgr.CharacterList.Count > 0)
            {
                return charMgr.CharacterList;
            }

            var stateCharacters = UIStateManager.Instance?.CharacterList;
            if (stateCharacters != null && stateCharacters.Count > 0)
            {
                return stateCharacters;
            }

            return CharacterService.Instance?.GetCachedCharacters() ?? new List<CharacterInfo>();
        }

        // ==========================================
        // 角色创建完成
        // ==========================================

        private async void OnCharacterCreationComplete()
        {
            Debug.Log("[CharacterSceneController] 角色创建流程完成，发送创建请求到服务端");

            try
            {
                var stepData = _stepNavigationManager.StepData;
                var appearance = new AppearanceInfo
                {
                    HairModel = stepData.SelectedPresetIndex,
                    HairColor = 0,
                    FaceModel = stepData.SelectedPresetIndex,
                };

                var charMgr = CharacterManager.Instance;
                if (charMgr == null)
                {
                    Debug.LogError("[CharacterSceneController] CharacterManager 不可用，降级使用 CharacterService");
                    var characterService = CharacterService.Instance;
                    await characterService.CreateCharacterAsync(
                        stepData.CharacterName,
                        stepData.Profession,
                        stepData.Gender,
                        appearance
                    );
                }
                else
                {
                    await charMgr.CreateCharacterAsync(
                        stepData.CharacterName,
                        (int)stepData.Profession,
                        stepData.Gender,
                        appearance
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterSceneController] 创建角色请求发送失败: {ex.Message}");
            }
            finally
            {
                ExitCreationMode();
            }
        }

        // ==========================================
        // 角色 ID 管理
        // ==========================================

        private void OnCharacterIdChangedInternal(string id)
        {
            _uiComponentManager.UpdateGlobalIdLabel(id);
            try
            {
                OnCharacterIdChanged?.Invoke(id);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterSceneController] OnCharacterIdChanged 订阅者抛出异常: {ex}");
            }
        }

        /// <summary>
        /// 设置当前角色 ID
        /// </summary>
        public void SetCharacterId(string id)
        {
            _characterStateManager.SetCharacterId(id);
        }

        /// <summary>
        /// 绑定 CharacterPreviewPanel 实例
        /// </summary>
        public void BindPreviewPanel(CharacterPreviewPanel previewPanel)
        {
            if (previewPanel == null)
            {
                Debug.LogWarning("[CharacterSceneController] BindPreviewPanel 收到 null");
                return;
            }

            previewPanel.OnCharacterIdChanged -= OnPreviewPanelIdChanged;
            previewPanel.OnCharacterIdChanged += OnPreviewPanelIdChanged;

            if (!string.IsNullOrEmpty(previewPanel.CurrentCharacterId))
            {
                SetCharacterId(previewPanel.CurrentCharacterId);
            }
        }

        private void OnPreviewPanelIdChanged(string newId)
        {
            SetCharacterId(newId);
        }

        /// <summary>
        /// 在 GUI 树中递归查找 CharacterPreviewPanel 实例并绑定
        /// </summary>
        private void TryBindPreviewPanelInScene(Control root)
        {
            if (root == null) return;

            if (root is CharacterPreviewPanel preview)
            {
                BindPreviewPanel(preview);
                return;
            }

            if (root is ContainerControl container)
            {
                for (int i = 0; i < container.ChildrenCount; i++)
                {
                    var child = container.GetChild(i);
                    if (child == null) continue;

                    if (child is ContainerControl childContainer)
                    {
                        TryBindPreviewPanelInScene(childContainer);
                    }
                }
            }
        }
    }
}
