using System;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI;
using HundunWorld.Game.Services;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.UI.Components;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 角色场景控制器 - 角色选择/创建场景
    /// 职责:
    /// 1. 找到/创建 UICanvas,初始化根控件尺寸
    /// 2. 创建并显示 3D 角色预览面板 (CharacterPreviewPanel)
    /// 3. 选择模式: 角色列表 + 进入游戏
    /// 4. 创建模式: 4步角色创建流程 (GenderSelection → FacePreset → DetailedCreation → NamingComplete)
    /// </summary>
    public class CharacterSceneController : Script
    {
        private static bool _uiCreated = false; // 防止同一场景多个实例同时创建UI
        private bool _initialized;
        private StepNavigation _stepNavigation;

        /// <summary>
        /// 当前是否处于角色创建模式（true=创建流程, false=选择列表）
        /// </summary>
        private bool _isCreationMode = false;

        /// <summary>
        /// GUI 根容器引用,用于物理移除/恢复控件
        /// </summary>
        private ContainerControl _guiContainer;

        // 控制器级 NextStepButton（最高 Z-order，确保不被遮挡）
        private NextStepButton _ctrlNextStepButton;

        // 顶部步骤进度指示器
        private Label _stepIndicatorLabel;

        // 全局角色 ID 标签(独立控件，封装阴影效果)
        private CharacterIdLabel _globalIdLabelControl;

        // 3D 角色预览面板(场景中央偏右,用于显示 3D 角色和氛围动效)
        private CharacterPreviewPanel _previewPanel;

        // ==========================================
        // 选择模式 UI 控件
        // ==========================================
        private Panel _selTopBar;
        private Label _selTitleLabel;
        private Panel _selLeftPanel;
        private Label _selLeftTitle;
        private Label _selHintLabel;
        private Panel _selBottomBar;
        private Button _backBtn;
        private Button _createBtn;
        private Button _enterBtn;

        // 角色列表
        private List<CharacterInfo> _characters = new List<CharacterInfo>();
        private CharacterInfo _selectedCharacter;
        private ScrollableControl _selCharacterScrollView;
        private Label _selEmptyHintLabel;

        private static readonly string[] ProfessionNames = { "剑客", "刀客", "枪客", "弓手", "法师", "道士", "刺客", "医师" };
        private static readonly Color GoldColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1f);
        private static readonly Color GoldHighlightBg = new Color(212f / 255f, 175f / 255f, 55f / 255f, 0.25f);
        private const float CharItemHeight = 80f;
        private const float CharItemSpacing = 8f;


        // ==========================================
        // 创建模式 UI 控件 (4步流程)
        // ==========================================
        private GenderSelectionUI _genderSelectionUI;
        private FacePresetSelectionUI _facePresetSelectionUI;
        private IntegratedCharacterCreationUI _integratedCreationUI;
        private NamingCompleteUI _namingCompleteUI;

        /// <summary>
        /// 当前角色 ID(视图层缓存的副本),默认 "0126998214"。
        /// 通过 SetCharacterId 写入时会同步刷新 _globalIdLabel.Text 并触发事件。
        /// </summary>
        public string CurrentCharacterId { get; private set; } = "0126998214";

        /// <summary>
        /// 全局 ID 标签控件的只读访问器(便于外部查询/修改样式)。
        /// </summary>
        public CharacterIdLabel GlobalIdLabelControl => _globalIdLabelControl;

        /// <summary>
        /// 3D 角色预览面板(供 GenderSelectionUI / IntegratedCharacterCreationUI 等子 UI 绑定相机联动)
        /// </summary>
        public CharacterPreviewPanel PreviewPanel => _previewPanel;

        /// <summary>
        /// 当 CurrentCharacterId 改变时触发,参数为新 ID。
        /// </summary>
        public event Action<string> OnCharacterIdChanged;

        public override void OnEnable()
        {
            _initialized = false;
            Debug.Log("[CharacterSceneController] OnEnable");
        }

        public override void OnDisable()
        {
            _uiCreated = false;
            Debug.Log("[CharacterSceneController] OnDisable");
        }

        public override void OnStart()
        {
            Debug.Log("[CharacterSceneController] OnStart");
            _stepNavigation = new StepNavigation();
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
            if (_isCreationMode && Input.GetKeyDown(KeyboardKeys.Escape))
            {
                Debug.Log("[CharacterSceneController] Esc 按下 -> 退出创建模式");
                ExitCreationMode();
            }

            // 创建模式下的强制清理: 每帧确保选择模式UI不在渲染树中
            if (_isCreationMode)
            {
                if (_selTopBar != null && _selTopBar.Parent != null) _selTopBar.Parent = null;
                if (_selTitleLabel != null && _selTitleLabel.Parent != null) _selTitleLabel.Parent = null;
                if (_selLeftPanel != null && _selLeftPanel.Parent != null) _selLeftPanel.Parent = null;
                if (_selBottomBar != null && _selBottomBar.Parent != null) _selBottomBar.Parent = null;
            }
        }

        private void TryCreateUI()
        {
            // 防止重复创建
            if (_uiCreated)
            {
#if DEBUG
                Debug.Log("[CharacterSceneController] UI 已由其他实例创建，跳过");
#endif
                _initialized = true;
                return;
            }

            // ==========================================
            // Step 1: 查找或创建 UICanvas
            // ==========================================
#if DEBUG
            Debug.Log("[CharacterSceneController] Step 1: 查找 UICanvas...");
#endif

            UICanvas uiCanvas = null;

            // 方式1: 从 Actor 子节点查找
            if (Actor != null)
            {
                uiCanvas = Actor.GetChild<UICanvas>();
#if DEBUG
                Debug.Log($"[CharacterSceneController] 从 Actor 子节点查找: {(uiCanvas != null ? "找到 " + uiCanvas.Name : "未找到")}");
#endif
            }

            // 方式2: 从场景中查找
            if (uiCanvas == null)
            {
                uiCanvas = Actor?.Scene?.FindActor<UICanvas>();
#if DEBUG
                Debug.Log($"[CharacterSceneController] 从场景查找: {(uiCanvas != null ? "找到 " + uiCanvas.Name : "未找到")}");
#endif
            }

            // 方式3: 从 Level 查找
            if (uiCanvas == null)
            {
                var allCanvases = Level.GetActors<UICanvas>();
#if DEBUG
                Debug.Log($"[CharacterSceneController] Level 中共有 {allCanvases?.Length ?? 0} 个 UICanvas");
#endif
                if (allCanvases != null && allCanvases.Length > 0)
                {
#if DEBUG
                    foreach (var c in allCanvases)
                    {
                        Debug.Log($"[CharacterSceneController]   - UICanvas: {c.Name}, Scene={c.Scene?.Name}, RenderMode={c.RenderMode}");
                    }
#endif
                    // 优先使用当前场景的 Canvas
                    if (Actor?.Scene != null)
                    {
                        foreach (var c in allCanvases)
                        {
                            if (c.Scene == Actor.Scene)
                            {
                                uiCanvas = c;
                                break;
                            }
                        }
                    }
                    // 如果没有当前场景的，用第一个
                    if (uiCanvas == null)
                    {
                        uiCanvas = allCanvases[0];
                    }
                }
            }

            // 方式4: 自动创建 UICanvas
            if (uiCanvas == null)
            {
                Debug.LogWarning("[CharacterSceneController] 未找到 UICanvas，自动创建...");

                var canvasActor = new EmptyActor { Name = "CharacterUICanvas" };
                if (Actor?.Scene != null)
                {
                    Level.SpawnActor(canvasActor, Actor.Scene);
                }
                else
                {
                    Level.SpawnActor(canvasActor);
                }

                uiCanvas = canvasActor.AddChild<UICanvas>();
                uiCanvas.Name = "CharacterUICanvas";
#if DEBUG
                Debug.Log($"[CharacterSceneController] UICanvas 自动创建完成: {uiCanvas.Name}");
#endif
            }

            if (uiCanvas?.GUI == null)
            {
                Debug.LogError("[CharacterSceneController] UICanvas.GUI 为 null！无法创建 UI");
                return;
            }

            // ==========================================
            // Step 2: 配置 UICanvas
            // ==========================================
#if DEBUG
            Debug.Log($"[CharacterSceneController] Step 2: 配置 UICanvas (当前 RenderMode={uiCanvas.RenderMode})");
#endif

            // 强制设置为 ScreenSpace 模式
            if (uiCanvas.RenderMode != CanvasRenderMode.ScreenSpace)
            {
                uiCanvas.RenderMode = CanvasRenderMode.ScreenSpace;
#if DEBUG
                Debug.Log("[CharacterSceneController] RenderMode 已设置为 ScreenSpace");
#endif
            }

            // 确保 UI Canvas 在最顶层(OOrder 高于 SceneTransitionEffect 的 1000)
            uiCanvas.Order = 1100;
            uiCanvas.IgnoreDepth = true;
            uiCanvas.ReceivesEvents = true;
#if DEBUG
            Debug.Log($"[CharacterSceneController] Canvas Order={uiCanvas.Order}, IgnoreDepth={uiCanvas.IgnoreDepth}, ReceivesEvents={uiCanvas.ReceivesEvents}");
#endif

            var gui = uiCanvas.GUI;
            // 关键修复: 使用 gui 容器自身尺寸,不要用 Screen.Size
            // Screen.Size 是整个屏幕的尺寸(多显示器/编辑器视口下不准)
            // gui.Size 才是当前 UI 树根容器实际可用尺寸
            var containerSize = gui.Size;
            if (containerSize.X <= 1 || containerSize.Y <= 1)
            {
                // gui 还没布局完成,延迟到下一帧再试
#if DEBUG
                Debug.LogWarning($"[CharacterSceneController] gui.Size={containerSize} 尚未布局完成,延迟创建");
#endif
                _initialized = false;
                _uiCreated = false; // 重置标志,允许重试
                return;
            }

            // 关键修复: ScreenSpace 模式下,根容器应使用 StretchAll 锚点 + Zero 偏移
            // 不要手动设置 Size,引擎会根据屏幕尺寸自动调整
            gui.AnchorPreset = AnchorPresets.StretchAll;
            gui.Offsets = Margin.Zero;
            gui.Pivot = new Float2(0.5f, 0.5f);
            // 关键修复: gui 根容器必须透明,3D 角色由 _previewPanel 的 SceneRenderTask 渲染
            // 如果这里不透明,会遮挡 _previewPanel 的 3D 渲染结果
            gui.BackgroundColor = Color.Transparent;
            gui.Visible = true;
            gui.Enabled = true;
            _guiContainer = gui; // 存储引用,用于物理移除/恢复控件
#if DEBUG
            Debug.Log($"[CharacterSceneController] GUI 配置完成: Anchor={gui.AnchorPreset}, Visible={gui.Visible}, Size={gui.Size}");
#endif

            // ==========================================
            // Step 2.5: 确保场景中有地面/光照基础(运行时兜底)
            // ==========================================
            EnsureSceneEnvironment();

            // ==========================================
            // Step 3: 创建基础 UI (3D 预览 + 全局 ID 标签)
            // ==========================================
#if DEBUG
            Debug.Log("[CharacterSceneController] Step 3: 创建 UI 控件...");
#endif

            _uiCreated = true;

            // 3D 角色预览面板(全屏,作为最底层,其他 UI 控件叠在它上面)
            _previewPanel = new CharacterPreviewPanel
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero
            };
            _previewPanel.TargetScene = Actor?.Scene;

            var W = containerSize.X;
            var H = containerSize.Y;

            // 全局角色 ID 标签（独立控件，封装阴影效果）
            _globalIdLabelControl = new CharacterIdLabel
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.TopLeft
            };
            _globalIdLabelControl.SetPosition(new Float2(40, H - 72 - 30));
            _globalIdLabelControl.CharacterId = CurrentCharacterId;

            // ==========================================
            // Step 3.0.1: 电影感渐变遮罩（最先创建，确保在底层）
            // ==========================================
            var topVignette = new Panel
            {
                Parent = gui,
                Location = new Float2(0, 0),
                Size = new Float2(W, 80),
                BackgroundColor = new Color(0.02f, 0.02f, 0.04f, 0.5f)
            };
            var bottomVignette = new Panel
            {
                Parent = gui,
                Location = new Float2(0, H - 60),
                Size = new Float2(W, 60),
                BackgroundColor = new Color(0.02f, 0.02f, 0.04f, 0.4f)
            };

            // ==========================================
            // Step 3.1: 创建选择模式 UI
            // ==========================================
            CreateSelectionModeUI(gui, W, H);

            // ==========================================
            // Step 3.2: 创建创建模式 UI (4步流程,初始隐藏)
            // ==========================================
            CreateCreationModeUI(gui);

            // ==========================================
            // Step 3.3: 初始化步骤导航
            // ==========================================
            _stepNavigation.OnStepChanged -= OnStepChanged;
            _stepNavigation.OnStepChanged += OnStepChanged;

            // 默认进入选择模式
            _isCreationMode = false;
            SetCreationModeVisible(false);

            // ==========================================
            // Step 3.4: 创建控制器级 NextStepButton（最后创建，最高 Z-order）
            // ==========================================
            _ctrlNextStepButton = new NextStepButton();
            _ctrlNextStepButton.Parent = gui;
            _ctrlNextStepButton.Location = new Float2(W - 260, H - 94);
            _ctrlNextStepButton.Visible = false;
            _ctrlNextStepButton.OnClicked += () =>
            {
                // ★ 面容预设步骤: 保存预设数据再前进
                if (_stepNavigation.CurrentStep == CreationStep.FacePreset &&
                    _facePresetSelectionUI?.SelectedPreset != null)
                {
                    _stepNavigation.StepData.SelectedPresetIndex = _facePresetSelectionUI.SelectedPreset.Id;
                    _stepNavigation.StepData.FacePresetName = _facePresetSelectionUI.SelectedPreset.Name;
                }
                Debug.Log($"[CharacterSceneController] CtrlNextStepButton 点击 -> GoNext (step={_stepNavigation.CurrentStep})");
                _stepNavigation.GoNext();
            };

            // 禁用 GenderSelectionUI 内部按钮（已由控制器级按钮替代）
            if (_genderSelectionUI != null)
                _genderSelectionUI.HideExternalButton();

            // ==========================================
            // Step 3.4b: 顶部步骤进度指示器
            // ==========================================
            _stepIndicatorLabel = new Label
            {
                Parent = gui,
                Location = new Float2(W / 2 - 120, 18),
                Size = new Float2(240, 30),
                Font = UIHelper.SetFont(size: 14),
                TextColor = new Color(1, 1, 1, 0.5f),
                Text = "1/4  选择性别",
                HorizontalAlignment = TextAlignment.Center
            };

            Debug.Log($"[CharacterSceneController] 控制器级 NextStepButton 创建完成: Location=({_ctrlNextStepButton.Location.X:F0},{_ctrlNextStepButton.Location.Y:F0}), Z={_ctrlNextStepButton.IndexInParent}");

            // ==========================================
            // Step 3.5: Z-order 管理（最后执行，覆盖所有控件）
            // ==========================================
            if (gui is ContainerControl guiContainerForZ)
            {
                _previewPanel.IndexInParent = 0;
                // 控制器级按钮始终在最高 Z-order
                _ctrlNextStepButton.IndexInParent = guiContainerForZ.ChildrenCount - 1;
                int currentIdx = 1;
                for (int i = 0; i < guiContainerForZ.ChildrenCount; i++)
                {
                    var child = guiContainerForZ.GetChild(i);
                    if (child != _previewPanel && child != _ctrlNextStepButton)
                    {
                        child.IndexInParent = currentIdx;
                        currentIdx++;
                    }
                }
            }

            TryBindPreviewPanelInScene(gui);
            _initialized = true;

            // ★ 关键: 默认自动进入创建模式,跳过选择模式(避免深色面板遮挡)
            // 用户无需点击"创建新角色"按钮,直接看到角色创建流程
            Debug.Log("[CharacterSceneController] ★★★ 自动进入创建模式 ★★★");
            EnterCreationMode();
        }

        /// <summary>
        /// 在 GUI 树中递归查找 CharacterPreviewPanel 实例并订阅其 OnCharacterIdChanged 事件。
        /// 实现"视图层订阅 OnCharacterIdChanged"的修复要求(Fail 4)。
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

                    // 递归查找子树(子树中找到 CharacterPreviewPanel 后会自动绑定并停止)
                    if (child is ContainerControl childContainer)
                    {
                        TryBindPreviewPanelInScene(childContainer);
                    }
                }
            }
        }

        private void DumpControlHierarchy(ContainerControl control, int depth)
        {
#if DEBUG
            var indent = new string(' ', depth * 2);
            Debug.Log($"{indent}[{control.GetType().Name}] Pos=({control.X:F0},{control.Y:F0}) Size=({control.Width:F0}x{control.Height:F0}) " +
                $"Visible={control.Visible} Color={control.BackgroundColor} Children={control.ChildrenCount}");

            for (int i = 0; i < control.ChildrenCount && i < 30; i++)
            {
                var child = control.GetChild(i);
                if (child != null)
                {
                    if (child is ContainerControl childContainer)
                    {
                        DumpControlHierarchy(childContainer, depth + 1);
                    }
                    else
                    {
                        var indent2 = new string(' ', (depth + 1) * 2);
                        Debug.Log($"{indent2}[{child.GetType().Name}] Pos=({child.X:F0},{child.Y:F0}) Size=({child.Width:F0}x{child.Height:F0}) " +
                            $"Visible={child.Visible} Color={child.BackgroundColor}");
                    }
                }
            }
#endif
        }

        // ==========================================
        // 选择模式 UI 创建
        // ==========================================
        // 选择模式面板颜色: 极低透明度,避免遮挡3D角色预览
        private static readonly Color SelPanelBg = new Color(0.03f, 0.04f, 0.06f, 0.15f);
        private static readonly Color SelBarBg = new Color(0.03f, 0.04f, 0.06f, 0.25f);

        private void CreateSelectionModeUI(ContainerControl gui, float W, float H)
        {
            // 顶部条带
            _selTopBar = new Panel
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = SelBarBg
            };
            _selTopBar.Location = new Float2(0, 0);
            _selTopBar.Size = new Float2(W, 70);

            // 金色标题
            _selTitleLabel = new Label
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "选择角色",
                TextColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Font = new FontReference { Size = 32 }
            };
            _selTitleLabel.Location = new Float2(0, 5);
            _selTitleLabel.Size = new Float2(W, 60);

            // 左侧面板 - 角色列表
            _selLeftPanel = new Panel
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = SelPanelBg
            };
            _selLeftPanel.Location = new Float2(20, 90);
            _selLeftPanel.Size = new Float2(300, H - 90 - 80);

            _selLeftTitle = new Label
            {
                Parent = _selLeftPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "角色列表",
                TextColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Font = new FontReference { Size = 22 }
            };
            _selLeftTitle.Location = new Float2(0, 10);
            _selLeftTitle.Size = new Float2(_selLeftPanel.Width, 40);

            _selHintLabel = new Label
            {
                Parent = _selLeftPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "暂无角色,请创建新角色",
                TextColor = new Color(0.7f, 0.7f, 0.75f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Font = new FontReference { Size = 16 }
            };
            _selHintLabel.Location = new Float2(0, _selLeftPanel.Height / 2 - 20);
            _selHintLabel.Size = new Float2(_selLeftPanel.Width, 40);
            _selHintLabel.Visible = true;

            // 角色列表滚动视图
            _selCharacterScrollView = new ScrollableControl
            {
                Parent = _selLeftPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent
            };
            _selCharacterScrollView.Location = new Float2(8, 55);
            _selCharacterScrollView.Size = new Float2(_selLeftPanel.Width - 16, _selLeftPanel.Height - 65);

            // 底部操作栏
            _selBottomBar = new Panel
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = SelBarBg
            };
            _selBottomBar.Location = new Float2(0, H - 72);
            _selBottomBar.Size = new Float2(W, 72);

            var btnWidth = 140f;
            var btnHeight = 40f;
            var btnSpacing = 24f;
            var totalBtnWidth = 3 * btnWidth + 2 * btnSpacing;
            var startX = (W - totalBtnWidth) / 2;
            var btnY = (72 - btnHeight) / 2;

            _backBtn = new Button
            {
                Parent = _selBottomBar,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "返回登录",
                BackgroundColor = new Color(0.12f, 0.13f, 0.16f, 1.0f),
                TextColor = new Color(0.75f, 0.75f, 0.80f),
                Font = new FontReference { Size = 18 }
            };
            _backBtn.Location = new Float2(startX, btnY);
            _backBtn.Size = new Float2(btnWidth, btnHeight);
            _backBtn.Clicked += OnBackBtnClicked;

            _createBtn = new Button
            {
                Parent = _selBottomBar,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "创建新角色",
                BackgroundColor = new Color(0.15f, 0.15f, 0.18f, 1.0f),
                TextColor = new Color(0.90f, 0.90f, 0.95f),
                Font = new FontReference { Size = 18 }
            };
            _createBtn.Location = new Float2(startX + btnWidth + btnSpacing, btnY);
            _createBtn.Size = new Float2(btnWidth, btnHeight);
            _createBtn.Clicked += OnCreateBtnClicked;

            _enterBtn = new Button
            {
                Parent = _selBottomBar,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "进入游戏",
                BackgroundColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1.0f),
                TextColor = new Color(0.10f, 0.08f, 0.05f, 1.0f),
                Font = new FontReference { Size = 18 }
            };
            _enterBtn.Location = new Float2(startX + 2 * (btnWidth + btnSpacing), btnY);
            _enterBtn.Size = new Float2(btnWidth, btnHeight);
            _enterBtn.Clicked += OnEnterBtnClicked;


        }

        // ==========================================
        // 创建模式 UI 创建 (4步角色创建流程)
        // ==========================================
        private void CreateCreationModeUI(ContainerControl gui)
        {
            // 步骤1: 性别选择
            _genderSelectionUI = new GenderSelectionUI
            {
                Parent = gui,
                Visible = false
            };
            _genderSelectionUI.SetPreviewPanel(_previewPanel);
            _genderSelectionUI.OnNextStep += () =>
            {
                _stepNavigation.StepData.Gender = _genderSelectionUI.SelectedGender;
                _stepNavigation.StepData.BodyHeight = _genderSelectionUI.BodyHeight;
                _stepNavigation.StepData.BodyType = _genderSelectionUI.BodyType;
                _stepNavigation.StepData.HeadSize = _genderSelectionUI.HeadSize;
                _stepNavigation.GoNext();
            };
            // 滑块实时联动: 体型参数变化时同步驱动 3D 模型缩放
            _genderSelectionUI.OnBodyParamsChanged += (h, b, hd) =>
            {
                _previewPanel?.ApplyBodyParams(h, b, hd);
            };

            // 步骤2: 脸型预设选择
            _facePresetSelectionUI = new FacePresetSelectionUI
            {
                Parent = gui,
                Visible = false
            };
            _facePresetSelectionUI.OnNextStep += () =>
            {
                if (_facePresetSelectionUI.SelectedPreset != null)
                {
                    _stepNavigation.StepData.SelectedPresetIndex = _facePresetSelectionUI.SelectedPreset.Id;
                    _stepNavigation.StepData.FacePresetName = _facePresetSelectionUI.SelectedPreset.Name;
                }
                _stepNavigation.GoNext();
            };
            _facePresetSelectionUI.OnGoBack += () => _stepNavigation.GoBack();
            _facePresetSelectionUI.HideExternalButton();

            // 步骤3: 精细捏脸
            _integratedCreationUI = new IntegratedCharacterCreationUI(_previewPanel)
            {
                Parent = gui,
                Visible = false
            };
            _integratedCreationUI.OnCompleteStep += () => _stepNavigation.GoNext();
            _integratedCreationUI.OnCancelled += () => _stepNavigation.GoBack();
            _integratedCreationUI.HideExternalButton();

            // 步骤4: 命名完成
            _namingCompleteUI = new NamingCompleteUI
            {
                Parent = gui,
                Visible = false
            };
            _namingCompleteUI.SetPreviewPanel(_previewPanel);
            _namingCompleteUI.OnGoBack += () => _stepNavigation.GoBack();
            _namingCompleteUI.OnComplete += (name) =>
            {
                _stepNavigation.StepData.CharacterName = name;
                _stepNavigation.StepData.Profession = (Profession)(_namingCompleteUI.SelectedProfessionIndex + 1); // +1 跳过 None
                Debug.Log($"[CharacterSceneController] 角色创建完成: 名称={name}, 性别={_stepNavigation.StepData.Gender}, 职业={_stepNavigation.StepData.Profession}");
                OnCharacterCreationComplete();
            };
        }

        // ==========================================
        // 模式切换
        // ==========================================

        /// <summary>
        /// 获取 GUI 根容器(ContainerControl)
        /// </summary>
        private ContainerControl GetGuiContainer()
        {
            return _guiContainer ?? (_previewPanel?.Parent as ContainerControl);
        }

        /// <summary>
        /// 设置选择模式 UI 可见性
        /// 关键修复: 使用 Parent = null 物理移除控件,而非 Visible = false
        /// (某些 Flax 版本中 Visible = false 不能可靠隐藏 Panel 控件的渲染)
        /// 注意: _globalIdLabel 独立于模式切换，始终保持在 GUI 树中
        /// </summary>
        private void SetSelectionModeVisible(bool visible)
        {
            Debug.Log($"[CharacterSceneController] SetSelectionModeVisible({visible})");
            var gui = GetGuiContainer();

            if (visible)
            {
                // 恢复到 GUI 树中
                if (_selTopBar != null && _selTopBar.Parent == null) _selTopBar.Parent = gui;
                if (_selTitleLabel != null && _selTitleLabel.Parent == null) _selTitleLabel.Parent = gui;
                if (_selLeftPanel != null && _selLeftPanel.Parent == null) _selLeftPanel.Parent = gui;
                if (_selBottomBar != null && _selBottomBar.Parent == null) _selBottomBar.Parent = gui;
                // _globalIdLabel 始终保持在 GUI 树中，无需在此处处理
            }
            else
            {
                // 从 GUI 树中物理移除(保证不渲染)
                if (_selTopBar != null) _selTopBar.Parent = null;
                if (_selTitleLabel != null) _selTitleLabel.Parent = null;
                if (_selLeftPanel != null) _selLeftPanel.Parent = null;
                if (_selBottomBar != null) _selBottomBar.Parent = null;
                // _globalIdLabel 始终保持在 GUI 树中，不移除
            }
        }

        /// <summary>
        /// 设置创建模式 UI 可见性（所有步骤 UI 统一控制），并同步管理 Z-order
        /// </summary>
        private void SetCreationModeVisible(bool visible)
        {
            Debug.Log($"[CharacterSceneController] SetCreationModeVisible({visible})");

            // 先全部隐藏
            if (_genderSelectionUI != null) _genderSelectionUI.Visible = false;
            if (_facePresetSelectionUI != null) _facePresetSelectionUI.Visible = false;
            if (_integratedCreationUI != null) _integratedCreationUI.Visible = false;
            if (_namingCompleteUI != null) _namingCompleteUI.Visible = false;

            if (visible)
            {
                // 选择模式UI已被物理移除,创建模式UI自然在最顶层
                ShowCurrentStepUI();
            }
        }

        /// <summary>
        /// 根据当前步骤显示对应的 UI
        /// </summary>
        private void ShowCurrentStepUI()
        {
            // 先隐藏所有步骤 UI
            if (_genderSelectionUI != null) _genderSelectionUI.Hide();
            if (_facePresetSelectionUI != null) _facePresetSelectionUI.Hide();
            if (_integratedCreationUI != null) _integratedCreationUI.Hide();
            if (_namingCompleteUI != null) _namingCompleteUI.Hide();

            // 控制器级按钮默认隐藏，性别选择和精细捏脸步骤显示
            if (_ctrlNextStepButton != null) _ctrlNextStepButton.Visible = false;

            // 显示当前步骤
            switch (_stepNavigation.CurrentStep)
            {
                case CreationStep.GenderSelection:
                    _genderSelectionUI?.Show();
                    if (_ctrlNextStepButton != null) _ctrlNextStepButton.Visible = true;
                    break;
                case CreationStep.FacePreset:
                    if (_facePresetSelectionUI != null)
                    {
                        _facePresetSelectionUI.SetGender(_stepNavigation.StepData.Gender == 0 ? "male" : "female");
                        _facePresetSelectionUI.Show();
                    }
                    if (_ctrlNextStepButton != null) _ctrlNextStepButton.Visible = true;
                    break;
                case CreationStep.DetailedCreation:
                    if (_integratedCreationUI != null)
                    {
                        _integratedCreationUI.SetStepData(_stepNavigation.StepData);
                        _integratedCreationUI.Show();
                    }
                    if (_ctrlNextStepButton != null) _ctrlNextStepButton.Visible = true;
                    break;
                case CreationStep.NamingComplete:
                    _namingCompleteUI?.Show();
                    break;
            }
        }

        /// <summary>
        /// 步骤切换事件处理
        /// </summary>
        private void OnStepChanged(CreationStep oldStep, CreationStep newStep)
        {
            Debug.Log($"[CharacterSceneController] 步骤切换: {oldStep} -> {newStep}");
            ShowCurrentStepUI();

            // 更新步骤指示器文本
            if (_stepIndicatorLabel != null)
            {
                switch (newStep)
                {
                    case CreationStep.GenderSelection:
                        _stepIndicatorLabel.Text = "1/4  选择性别"; break;
                    case CreationStep.FacePreset:
                        _stepIndicatorLabel.Text = "2/4  选择面容"; break;
                    case CreationStep.DetailedCreation:
                        _stepIndicatorLabel.Text = "3/4  精细捏脸"; break;
                    case CreationStep.NamingComplete:
                        _stepIndicatorLabel.Text = "4/4  命名完成"; break;
                }
            }
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
            Debug.Log("[CharacterSceneController] ★★★ 点击: 创建新角色 -> 进入创建模式 ★★★");
            EnterCreationMode();
        }

        private async void OnEnterBtnClicked()
        {
            Debug.Log("[CharacterSceneController] 点击: 进入游戏");

            if (_selectedCharacter == null)
            {
                Debug.LogWarning("[CharacterSceneController] 未选择角色，无法进入游戏");
                UIHelper.ShowToast("请先选择一个角色", ToastType.Warning);
                return;
            }

            try
            {
                var networkManager = HundunWorldGame.Instance?.NetworkManager;
                if (networkManager == null || !networkManager.CanSendMessage())
                {
                    Debug.LogError("[CharacterSceneController] 网络未连接，无法进入游戏");
                    UIHelper.ShowToast("网络未连接", ToastType.Error);
                    return;
                }

                var request = new EnterGameRequest
                {
                    CharacterId = _selectedCharacter.CharacterId,
                    ClientVersion = "1.0.0"
                };

                var messagePacket = new HorizonMessagePacket
                {
                    Header = new MessageHeader
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        MessageType = MessageType.EnterGame,
                        ServiceType = ServiceType.Game,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    },
                    ServiceType = ServiceType.Game,
                    Body = request
                };

                bool sent = await networkManager.SendMessageAsync(messagePacket);
                if (sent)
                {
                    CharacterService.Instance.SelectCharacter(_selectedCharacter);
                    Debug.Log($"[CharacterSceneController] 进入游戏请求已发送: {_selectedCharacter.CharacterName}");
                }
                else
                {
                    Debug.LogError("[CharacterSceneController] 发送进入游戏请求失败");
                    UIHelper.ShowToast("进入游戏失败", ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterSceneController] 进入游戏异常: {ex.Message}");
                UIHelper.ShowToast("进入游戏失败", ToastType.Error);
            }
        }

        /// <summary>
        /// 进入角色创建模式: 隐藏选择 UI, 显示创建流程
        /// </summary>
        public void EnterCreationMode()
        {
            Debug.Log("[CharacterSceneController] === EnterCreationMode 开始 ===");
            _isCreationMode = true;
            _stepNavigation.Reset();

            // 关键修复: 先隐藏选择UI并沉底，再显示创建UI并置顶
            SetSelectionModeVisible(false);
            SetCreationModeVisible(true);

            // 应用默认体型参数到 3D 模型（男性默认值）
            _previewPanel?.ApplyBodyParams(0.55f, 0.55f, 0.50f);

            Debug.Log($"[CharacterSceneController] === EnterCreationMode 完成 === " +
                $"_genderSelectionUI.Visible={_genderSelectionUI?.Visible}, " +
                $"_selTopBar.Visible={_selTopBar?.Visible}, " +
                $"_selLeftPanel.Visible={_selLeftPanel?.Visible}");
        }

        // ==========================================
        // 角色列表管理
        // ==========================================

        private void RefreshCharacterList()
        {
            if (_selCharacterScrollView == null || _selHintLabel == null) return;

            // 从 CharacterService 缓存获取角色列表
            _characters = CharacterService.Instance?.GetCachedCharacters() ?? new List<CharacterInfo>();

            // 清空滚动列表
            _selCharacterScrollView.RemoveChildren();

            if (_characters.Count == 0)
            {
                _selHintLabel.Visible = true;
                _selCharacterScrollView.Visible = false;
                _enterBtn.Enabled = false;
                return;
            }

            _selHintLabel.Visible = false;
            _selCharacterScrollView.Visible = true;

            // 构建角色列表项
            float listWidth = _selCharacterScrollView.Width;
            for (int i = 0; i < _characters.Count; i++)
            {
                var character = _characters[i];
                var itemPanel = CreateCharacterListItem(character, listWidth);
                itemPanel.Parent = _selCharacterScrollView;
                float yPos = i * (CharItemHeight + CharItemSpacing);
                itemPanel.Location = new Float2(0, yPos);
                itemPanel.Size = new Float2(listWidth, CharItemHeight);
            }

            // 启用进入按钮的条件
            _enterBtn.Enabled = _selectedCharacter != null;
        }

        private Panel CreateCharacterListItem(CharacterInfo character, float listWidth)
        {
            bool isSelected = _selectedCharacter != null && _selectedCharacter.CharacterId == character.CharacterId;

            var itemPanel = new ClickablePanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = isSelected ? GoldHighlightBg : new Color(0.10f, 0.10f, 0.13f, 0.8f)
            };

            // 角色名
            var nameLabel = new Label
            {
                Parent = itemPanel,
                Text = character.CharacterName,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10, 8),
                Size = new Float2(listWidth - 20, 28),
                TextColor = new Color(1.0f, 0.95f, 0.8f),
                HorizontalAlignment = TextAlignment.Near,
                Font = new FontReference { Size = 20 }
            };

            // 职业名
            var profIdx = (int)character.Profession;
            var profName = profIdx >= 0 && profIdx < ProfessionNames.Length ? ProfessionNames[profIdx] : "未知";
            var profLabel = new Label
            {
                Parent = itemPanel,
                Text = $"职业: {profName}",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10, 38),
                Size = new Float2(listWidth - 20, 20),
                TextColor = new Color(0.65f, 0.65f, 0.7f),
                HorizontalAlignment = TextAlignment.Near,
                Font = new FontReference { Size = 14 }
            };

            // 等级
            var levelLabel = new Label
            {
                Parent = itemPanel,
                Text = $"Lv.{character.Level}",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10, 58),
                Size = new Float2(100, 18),
                TextColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 0.8f),
                HorizontalAlignment = TextAlignment.Near,
                Font = new FontReference { Size = 13 }
            };

            // 鼠标点击选中
            var capturedChar = character;
            itemPanel.OnMouseDownCallback = () =>
            {
                _selectedCharacter = capturedChar;
                RefreshCharacterList();
            };

            return itemPanel;
        }

        /// <summary>
        /// 自定义 Panel：支持鼠标点击回调
        /// </summary>
        private class ClickablePanel : Panel
        {
            public Action OnMouseDownCallback;

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    OnMouseDownCallback?.Invoke();
                    return true;
                }
                return base.OnMouseDown(location, button);
            }
        }

        /// <summary>
        /// 退出角色创建模式: 隐藏创建流程, 恢复选择 UI
        /// </summary>
        public void ExitCreationMode()
        {
            Debug.Log("[CharacterSceneController] === ExitCreationMode ===");
            _isCreationMode = false;
            SetCreationModeVisible(false);
            SetSelectionModeVisible(true);
            RefreshCharacterList();
        }

        /// <summary>
        /// 角色创建完成: 保存数据, 发送创建请求到服务端
        /// </summary>
        private async void OnCharacterCreationComplete()
        {
            Debug.Log("[CharacterSceneController] 角色创建流程完成，发送创建请求到服务端");
            
            try
            {
                var stepData = _stepNavigation.StepData;
                var appearance = new AppearanceInfo
                {
                    HairModel = stepData.SelectedPresetIndex,
                    HairColor = 0,
                    FaceModel = stepData.SelectedPresetIndex,
                };

                var characterService = CharacterService.Instance;
                await characterService.CreateCharacterAsync(
                    stepData.CharacterName,
                    stepData.Profession,
                    stepData.Gender,
                    appearance
                );
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

        /// <summary>
        /// 运行时兜底:确保场景中有基础的光照,避免角色悬空在虚空中。
        /// 注意:不再使用 Content.Load&lt;Model&gt;("Editor/Primitives/Cube") 路径,
        /// 因为该路径在打包游戏中不可用。仅做灯光补强,不创建地面(地面由场景文件负责)。
        /// </summary>
        private void EnsureSceneEnvironment()
        {
            if (Actor?.Scene == null) return;

            var scene = Actor.Scene;

            // 检查是否有点光源(没有则补一个)
            var existingLights = scene.GetChildren<PointLight>();
            if (existingLights == null || existingLights.Length == 0)
            {
                // ① 主光 Key Light: 右前上方，暖白色，模拟太阳方向
                var keyLightActor = new EmptyActor { Name = "RuntimeKeyLight" };
                keyLightActor.Position = new Vector3(150f, 350f, 200f);
                Level.SpawnActor(keyLightActor, scene);
                var keyLight = keyLightActor.AddChild<PointLight>();
                keyLight.Brightness = 2800f;
                keyLight.Radius = 500f;
                keyLight.Color = new Color(1f, 0.96f, 0.88f); // 暖白

                // ② 补光 Fill Light: 左前上方，冷蓝色，平衡主光阴影
                var fillLightActor = new EmptyActor { Name = "RuntimeFillLight" };
                fillLightActor.Position = new Vector3(-250f, 250f, 100f);
                Level.SpawnActor(fillLightActor, scene);
                var fillLight = fillLightActor.AddChild<PointLight>();
                fillLight.Brightness = 1400f;
                fillLight.Radius = 400f;
                fillLight.Color = new Color(0.65f, 0.75f, 1f); // 冷蓝

                // ③ 轮廓光 Rim Light: 正后上方，暖橙色，勾勒身体轮廓
                var rimLightActor = new EmptyActor { Name = "RuntimeRimLight" };
                rimLightActor.Position = new Vector3(0f, 280f, -300f);
                Level.SpawnActor(rimLightActor, scene);
                var rimLight = rimLightActor.AddChild<PointLight>();
                rimLight.Brightness = 1200f;
                rimLight.Radius = 350f;
                rimLight.Color = new Color(1f, 0.82f, 0.6f); // 暖橙

                // ④ 底部补光 Ground Bounce: 正下方偏前，模拟地面反射
                var groundLightActor = new EmptyActor { Name = "RuntimeGroundBounce" };
                groundLightActor.Position = new Vector3(0f, -50f, 150f);
                Level.SpawnActor(groundLightActor, scene);
                var groundLight = groundLightActor.AddChild<PointLight>();
                groundLight.Brightness = 600f;
                groundLight.Radius = 300f;
                groundLight.Color = new Color(0.85f, 0.88f, 0.95f); // 冷白

                // ⑤ 顶部环境光 Top Ambient: 正上方，极低亮度，消除死黑
                var topLightActor = new EmptyActor { Name = "RuntimeTopAmbient" };
                topLightActor.Position = new Vector3(0f, 500f, 0f);
                Level.SpawnActor(topLightActor, scene);
                var topLight = topLightActor.AddChild<PointLight>();
                topLight.Brightness = 400f;
                topLight.Radius = 600f;
                topLight.Color = new Color(0.9f, 0.92f, 1f); // 微冷

                Debug.Log("[CharacterSceneController] 运行时五点照明系统: KeyLight+FillLight+RimLight+GroundBounce+TopAmbient");
            }
        }

        /// <summary>
        /// 设置当前角色 ID,同步刷新全局 ID 标签,并触发 OnCharacterIdChanged 事件。
        /// </summary>
        public void SetCharacterId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;

            if (CurrentCharacterId == id)
                return;

            CurrentCharacterId = id;

            if (_globalIdLabelControl != null)
            {
                _globalIdLabelControl.CharacterId = id;
            }

            Debug.Log($"[CharacterSceneController] 角色ID已更新: {id}");

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
        /// 绑定 CharacterPreviewPanel 实例,订阅其 ID 变化事件以保持全局 ID 标签同步。
        /// 如果当前没有可用的 PreviewPanel 实例,保留硬编码的 "ID: 0126998214" 默认值。
        /// </summary>
        public void BindPreviewPanel(CharacterPreviewPanel previewPanel)
        {
            if (previewPanel == null)
            {
                Debug.LogWarning("[CharacterSceneController] BindPreviewPanel 收到 null,保留默认 ID 标签");
                return;
            }

            // 取消旧订阅(若多次绑定)
            previewPanel.OnCharacterIdChanged -= OnPreviewPanelIdChanged;
            previewPanel.OnCharacterIdChanged += OnPreviewPanelIdChanged;

            // 立即同步当前 ID
            if (!string.IsNullOrEmpty(previewPanel.CurrentCharacterId))
            {
                SetCharacterId(previewPanel.CurrentCharacterId);
            }
        }

        private void OnPreviewPanelIdChanged(string newId)
        {
            SetCharacterId(newId);
        }
    }
}