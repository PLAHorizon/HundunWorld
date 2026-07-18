using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// UI 创建工厂 - 负责所有角色场景 UI 组件的创建
    /// 从 CharacterSceneController 中提取的职责：
    /// - UICanvas 查找与创建
    /// - 选择模式 UI 创建
    /// - 创建模式 UI 创建
    /// - 步骤指示器创建
    /// - 通用 UI 组件创建
    /// </summary>
    public class UIFactory
    {
        // 颜色常量
        private static readonly Color GoldColor = ChineseClassicalTheme.SecondaryColor;
        private static readonly Color GoldHighlightBg = ChineseClassicalTheme.SecondaryColorWithAlpha(0.25f);
        private static readonly Color SelPanelBg = new Color(0.05f, 0.06f, 0.10f, 0.85f);
        private static readonly Color SelBarBg = new Color(0.05f, 0.06f, 0.10f, 0.92f);
        private static readonly Color CharItemBg = new Color(0.12f, 0.13f, 0.18f, 0.90f);

        private static readonly string[] ProfessionNames = { "剑客", "刀客", "枪客", "弓手", "法师", "道士", "刺客", "医师" };
        private static readonly string[] StepNames = { "选择性别", "选择面容", "精细捏脸", "命名完成" };
        private const int TotalSteps = 4;
        private const float CharItemHeight = 80f;
        private const float CharItemSpacing = 8f;

        /// <summary>
        /// 查找或创建 UICanvas
        /// </summary>
        public UICanvas FindOrCreateUICanvas(Actor actor)
        {
            UICanvas uiCanvas = null;

            // 方式1: 从 Actor 子节点查找
            if (actor != null)
            {
                uiCanvas = actor.GetChild<UICanvas>();
            }

            // 方式2: 从场景中查找
            if (uiCanvas == null)
            {
                uiCanvas = actor?.Scene?.FindActor<UICanvas>();
            }

            // 方式3: 从 Level 查找
            if (uiCanvas == null)
            {
                var allCanvases = Level.GetActors<UICanvas>();
                if (allCanvases != null && allCanvases.Length > 0)
                {
                    if (actor?.Scene != null)
                    {
                        foreach (var c in allCanvases)
                        {
                            if (c.Scene == actor.Scene)
                            {
                                uiCanvas = c;
                                break;
                            }
                        }
                    }
                    if (uiCanvas == null)
                    {
                        uiCanvas = allCanvases[0];
                    }
                }
            }

            // 方式4: 自动创建 UICanvas
            if (uiCanvas == null)
            {
                Debug.LogWarning("[UIFactory] 未找到 UICanvas，自动创建...");
                var canvasActor = new EmptyActor { Name = "CharacterUICanvas" };
                if (actor?.Scene != null)
                {
                    Level.SpawnActor(canvasActor, actor.Scene);
                }
                else
                {
                    Level.SpawnActor(canvasActor);
                }
                uiCanvas = canvasActor.AddChild<UICanvas>();
                uiCanvas.Name = "CharacterUICanvas";
            }

            return uiCanvas;
        }

        /// <summary>
        /// 配置 UICanvas
        /// </summary>
        public ContainerControl ConfigureCanvas(UICanvas uiCanvas)
        {
            if (uiCanvas?.GUI == null)
            {
                Debug.LogError("[UIFactory] UICanvas.GUI 为 null！");
                return null;
            }

            if (uiCanvas.RenderMode != CanvasRenderMode.ScreenSpace)
            {
                uiCanvas.RenderMode = CanvasRenderMode.ScreenSpace;
            }

            uiCanvas.Order = 1100;
            uiCanvas.IgnoreDepth = true;
            uiCanvas.ReceivesEvents = true;

            var gui = uiCanvas.GUI;
            gui.AnchorPreset = AnchorPresets.StretchAll;
            gui.Offsets = Margin.Zero;
            gui.Pivot = new Float2(0.5f, 0.5f);
            gui.BackgroundColor = Color.Transparent;
            gui.Visible = true;
            gui.Enabled = true;

            return gui;
        }

        /// <summary>
        /// 创建 3D 角色预览面板
        /// </summary>
        public CharacterPreviewPanel CreatePreviewPanel(ContainerControl gui, FlaxEngine.Scene targetScene)
        {
            var previewPanel = new CharacterPreviewPanel
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero
            };
            previewPanel.TargetScene = targetScene;
            return previewPanel;
        }

        /// <summary>
        /// 创建全局角色 ID 标签
        /// </summary>
        public CharacterIdLabel CreateGlobalIdLabel(ContainerControl gui, float x, float y, string characterId)
        {
            var idLabel = new CharacterIdLabel
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.TopLeft
            };
            idLabel.SetPosition(new Float2(x, y));
            idLabel.CharacterId = characterId;
            return idLabel;
        }

        /// <summary>
        /// 创建电影感渐变遮罩
        /// </summary>
        public void CreateVignette(ContainerControl gui, float W, float H)
        {
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
        }

        /// <summary>
        /// 创建选择模式 UI
        /// </summary>
        public SelectionModeUIComponents CreateSelectionModeUI(ContainerControl gui, float W, float H)
        {
            var components = new SelectionModeUIComponents();

            // 顶部条带
            components.TopBar = new Panel
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = SelBarBg
            };
            components.TopBar.Location = new Float2(0, 0);
            components.TopBar.Size = new Float2(W, 70);

            // 金色标题
            components.TitleLabel = new Label
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "选择角色",
                TextColor = GoldColor,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 32)
            };
            components.TitleLabel.Location = new Float2(0, 5);
            components.TitleLabel.Size = new Float2(W, 60);

            // 左侧面板 - 角色列表
            components.LeftPanel = new Panel
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = SelPanelBg
            };
            components.LeftPanel.Location = new Float2(20, 90);
            components.LeftPanel.Size = new Float2(300, H - 90 - 80);

            components.LeftTitle = new Label
            {
                Parent = components.LeftPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "角色列表",
                TextColor = GoldColor,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 22)
            };
            components.LeftTitle.Location = new Float2(0, 10);
            components.LeftTitle.Size = new Float2(components.LeftPanel.Width, 40);

            components.HintLabel = new Label
            {
                Parent = components.LeftPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "暂无角色,请创建新角色",
                TextColor = new Color(0.7f, 0.7f, 0.75f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 16)
            };
            components.HintLabel.Location = new Float2(0, components.LeftPanel.Height / 2 - 20);
            components.HintLabel.Size = new Float2(components.LeftPanel.Width, 40);
            components.HintLabel.Visible = true;

            // 角色列表滚动视图
            components.CharacterScrollView = new ScrollableControl
            {
                Parent = components.LeftPanel,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent
            };
            components.CharacterScrollView.Location = new Float2(8, 55);
            components.CharacterScrollView.Size = new Float2(components.LeftPanel.Width - 16, components.LeftPanel.Height - 65);

            // 底部操作栏
            components.BottomBar = new Panel
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = SelBarBg
            };
            components.BottomBar.Location = new Float2(0, H - 72);
            components.BottomBar.Size = new Float2(W, 72);

            var btnWidth = 140f;
            var btnHeight = 40f;
            var btnSpacing = 24f;
            var totalBtnWidth = 3 * btnWidth + 2 * btnSpacing;
            var startX = (W - totalBtnWidth) / 2;
            var btnY = (72 - btnHeight) / 2;

            components.BackBtn = new Button
            {
                Parent = components.BottomBar,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "返回登录",
                BackgroundColor = new Color(0.12f, 0.13f, 0.16f, 1.0f),
                TextColor = new Color(0.75f, 0.75f, 0.80f),
                Font = UIHelper.SetFont(size: 18)
            };
            components.BackBtn.Location = new Float2(startX, btnY);
            components.BackBtn.Size = new Float2(btnWidth, btnHeight);

            components.CreateBtn = new Button
            {
                Parent = components.BottomBar,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "创建新角色",
                BackgroundColor = new Color(0.15f, 0.15f, 0.18f, 1.0f),
                TextColor = new Color(0.90f, 0.90f, 0.95f),
                Font = UIHelper.SetFont(size: 18)
            };
            components.CreateBtn.Location = new Float2(startX + btnWidth + btnSpacing, btnY);
            components.CreateBtn.Size = new Float2(btnWidth, btnHeight);

            components.EnterBtn = new Button
            {
                Parent = components.BottomBar,
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "进入游戏",
                BackgroundColor = GoldColor,
                TextColor = new Color(0.10f, 0.08f, 0.05f, 1.0f),
                Font = UIHelper.SetFont(size: 18)
            };
            components.EnterBtn.Location = new Float2(startX + 2 * (btnWidth + btnSpacing), btnY);
            components.EnterBtn.Size = new Float2(btnWidth, btnHeight);

            return components;
        }

        /// <summary>
        /// 创建创建模式 UI
        /// </summary>
        public CreationModeUIComponents CreateCreationModeUI(ContainerControl gui, CharacterPreviewPanel previewPanel)
        {
            var components = new CreationModeUIComponents();

            // 步骤1: 性别选择
            components.GenderSelectionUI = new GenderSelectionUI
            {
                Parent = gui,
                Visible = false
            };
            components.GenderSelectionUI.SetPreviewPanel(previewPanel);

            // 步骤2: 脸型预设选择
            components.FacePresetSelectionUI = new FacePresetSelectionUI
            {
                Parent = gui,
                Visible = false
            };
            components.FacePresetSelectionUI.HideExternalButton();

            // 步骤3: 精细捏脸
            components.IntegratedCreationUI = new IntegratedCharacterCreationUI(previewPanel)
            {
                Parent = gui,
                Visible = false
            };
            components.IntegratedCreationUI.HideExternalButton();

            // 步骤4: 命名完成
            components.NamingCompleteUI = new NamingCompleteUI
            {
                Parent = gui,
                Visible = false
            };
            components.NamingCompleteUI.SetPreviewPanel(previewPanel);

            return components;
        }

        /// <summary>
        /// 创建控制器级 NextStepButton
        /// </summary>
        public NextStepButton CreateNextStepButton(ContainerControl gui, float W, float H)
        {
            var button = new NextStepButton
            {
                Parent = gui,
                Location = new Float2(W - 260, H - 94),
                Visible = false
            };
            return button;
        }

        /// <summary>
        /// 创建步骤进度指示器
        /// </summary>
        public StepIndicatorComponents CreateStepIndicator(ContainerControl gui, float W)
        {
            var components = new StepIndicatorComponents();
            float dotSize = 10f;
            float lineW = 40f;
            float totalW = TotalSteps * dotSize + (TotalSteps - 1) * lineW;
            float startX = (W - totalW) / 2f;
            float indicatorY = 22f;

            components.Dots = new Panel[TotalSteps];
            components.Lines = new Panel[TotalSteps - 1];

            for (int i = 0; i < TotalSteps; i++)
            {
                float dotX = startX + i * (dotSize + lineW);
                components.Dots[i] = new Panel
                {
                    Parent = gui,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(dotX, indicatorY),
                    Size = new Float2(dotSize, dotSize),
                    BackgroundColor = (i == 0) ? GoldColor : new Color(0.3f, 0.3f, 0.35f, 0.6f)
                };

                if (i < TotalSteps - 1)
                {
                    components.Lines[i] = new Panel
                    {
                        Parent = gui,
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(dotX + dotSize, indicatorY + dotSize / 2f - 1),
                        Size = new Float2(lineW, 2),
                        BackgroundColor = new Color(0.25f, 0.25f, 0.3f, 0.5f)
                    };
                }
            }

            components.NameLabel = new Label
            {
                Parent = gui,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(startX - 10, indicatorY + dotSize + 6),
                Size = new Float2(totalW + 20, 20),
                Font = UIHelper.SetFont(size: 12),
                TextColor = new Color(1, 1, 1, 0.5f),
                Text = StepNames[0],
                HorizontalAlignment = TextAlignment.Center
            };

            return components;
        }

        /// <summary>
        /// 创建角色列表项
        /// </summary>
        public HoverPanel CreateCharacterListItem(Horizon.Game.Message.Network.CharacterInfo character, float listWidth,
            bool isSelected, Action<Horizon.Game.Message.Network.CharacterInfo> onClick)
        {
            Color normalBg = isSelected ? GoldHighlightBg : CharItemBg;
            Color hoverBg = isSelected ? GoldHighlightBg : new Color(0.16f, 0.17f, 0.22f, 0.95f);

            var itemPanel = new HoverPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = normalBg,
                NormalColor = normalBg,
                HoverColor = hoverBg
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
                Font = UIHelper.SetFont(size: 20)
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
                Font = UIHelper.SetFont(size: 14)
            };

            // 等级
            var levelLabel = new Label
            {
                Parent = itemPanel,
                Text = $"Lv.{character.Level}",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10, 58),
                Size = new Float2(100, 18),
                TextColor = ChineseClassicalTheme.SecondaryColorWithAlpha(0.8f),
                HorizontalAlignment = TextAlignment.Near,
                Font = UIHelper.SetFont(size: 13)
            };

            // 鼠标点击选中
            var capturedChar = character;
            itemPanel.OnClickCallback = () =>
            {
                onClick?.Invoke(capturedChar);
            };

            return itemPanel;
        }

        /// <summary>
        /// 管理 Z-order
        /// </summary>
        public void ManageZOrder(ContainerControl gui, CharacterPreviewPanel previewPanel, NextStepButton ctrlButton)
        {
            if (gui is ContainerControl guiContainer)
            {
                previewPanel.IndexInParent = 0;
                ctrlButton.IndexInParent = guiContainer.ChildrenCount - 1;
                int currentIdx = 1;
                for (int i = 0; i < guiContainer.ChildrenCount; i++)
                {
                    var child = guiContainer.GetChild(i);
                    if (child != previewPanel && child != ctrlButton)
                    {
                        child.IndexInParent = currentIdx;
                        currentIdx++;
                    }
                }
            }
        }

        /// <summary>
        /// 运行时兜底:确保场景中有基础的光照
        /// </summary>
        public void EnsureSceneEnvironment(FlaxEngine.Scene scene)
        {
            if (scene == null) return;

            var existingLights = scene.GetChildren<PointLight>();
            if (existingLights == null || existingLights.Length == 0)
            {
                // 主光 Key Light
                var keyLightActor = new EmptyActor { Name = "RuntimeKeyLight" };
                keyLightActor.Position = new Vector3(150f, 350f, 200f);
                Level.SpawnActor(keyLightActor, scene);
                var keyLight = keyLightActor.AddChild<PointLight>();
                keyLight.Brightness = 2800f;
                keyLight.Radius = 500f;
                keyLight.Color = new Color(1f, 0.96f, 0.88f);

                // 补光 Fill Light
                var fillLightActor = new EmptyActor { Name = "RuntimeFillLight" };
                fillLightActor.Position = new Vector3(-250f, 250f, 100f);
                Level.SpawnActor(fillLightActor, scene);
                var fillLight = fillLightActor.AddChild<PointLight>();
                fillLight.Brightness = 1400f;
                fillLight.Radius = 400f;
                fillLight.Color = new Color(0.65f, 0.75f, 1f);

                // 轮廓光 Rim Light
                var rimLightActor = new EmptyActor { Name = "RuntimeRimLight" };
                rimLightActor.Position = new Vector3(0f, 280f, -300f);
                Level.SpawnActor(rimLightActor, scene);
                var rimLight = rimLightActor.AddChild<PointLight>();
                rimLight.Brightness = 1200f;
                rimLight.Radius = 350f;
                rimLight.Color = new Color(1f, 0.82f, 0.6f);

                // 底部补光 Ground Bounce
                var groundLightActor = new EmptyActor { Name = "RuntimeGroundBounce" };
                groundLightActor.Position = new Vector3(0f, -50f, 150f);
                Level.SpawnActor(groundLightActor, scene);
                var groundLight = groundLightActor.AddChild<PointLight>();
                groundLight.Brightness = 600f;
                groundLight.Radius = 300f;
                groundLight.Color = new Color(0.85f, 0.88f, 0.95f);

                // 顶部环境光 Top Ambient
                var topLightActor = new EmptyActor { Name = "RuntimeTopAmbient" };
                topLightActor.Position = new Vector3(0f, 500f, 0f);
                Level.SpawnActor(topLightActor, scene);
                var topLight = topLightActor.AddChild<PointLight>();
                topLight.Brightness = 400f;
                topLight.Radius = 600f;
                topLight.Color = new Color(0.9f, 0.92f, 1f);

                Debug.Log("[UIFactory] 运行时五点照明系统已创建");
            }
        }
    }

    /// <summary>
    /// 选择模式 UI 组件集合
    /// </summary>
    public class SelectionModeUIComponents
    {
        public Panel TopBar;
        public Label TitleLabel;
        public Panel LeftPanel;
        public Label LeftTitle;
        public Label HintLabel;
        public Panel BottomBar;
        public Button BackBtn;
        public Button CreateBtn;
        public Button EnterBtn;
        public ScrollableControl CharacterScrollView;
    }

    /// <summary>
    /// 创建模式 UI 组件集合
    /// </summary>
    public class CreationModeUIComponents
    {
        public GenderSelectionUI GenderSelectionUI;
        public FacePresetSelectionUI FacePresetSelectionUI;
        public IntegratedCharacterCreationUI IntegratedCreationUI;
        public NamingCompleteUI NamingCompleteUI;
    }

    /// <summary>
    /// 步骤指示器组件集合
    /// </summary>
    public class StepIndicatorComponents
    {
        public Panel[] Dots;
        public Panel[] Lines;
        public Label NameLabel;
    }

    /// <summary>
    /// 自定义 Panel：支持鼠标点击 + hover 背景色变化
    /// </summary>
    public class HoverPanel : Panel
    {
        public Action OnClickCallback;
        public Color NormalColor;
        public Color HoverColor;
        private bool _wasHovered = false;

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            bool hovered = IsMouseOver;
            if (hovered != _wasHovered)
            {
                _wasHovered = hovered;
                BackgroundColor = hovered ? HoverColor : NormalColor;
            }
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                OnClickCallback?.Invoke();
                return true;
            }
            return base.OnMouseDown(location, button);
        }
    }
}
