using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Equipment.Material;
using Game.Equipment.Crafting;
using FlaxEngine.Utilities;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 材料合成UI组件
    /// 提供材料选择、合成预览、一键合成功能
    /// 支持配方浏览、材料检查、批量合成
    /// </summary>
    public class CraftingUI : Script
    {
        #region 配置参数

        [Header("合成UI配置")]
        [Tooltip("窗口宽度")]
        public float WindowWidth = 700f;

        [Tooltip("窗口高度")]
        public float WindowHeight = 500f;

        [Tooltip("配方列表宽度")]
        public float RecipeListWidth = 250f;

        #endregion

        #region UI组件

        private Panel _craftingWindow;
        private Panel _titleBar;
        private Label _titleLabel;
        private Button _closeButton;

        // 左侧配方列表
        private Panel _recipeListPanel;
        private ScrollableControl _recipeScrollView;
        private List<Button> _recipeButtons = new List<Button>();

        // 右侧合成区域
        private Panel _craftingPanel;
        private Label _recipeNameLabel;
        private Label _recipeDescLabel;
        
        // 材料需求显示
        private Panel _materialsPanel;
        private List<MaterialRequirementUI> _materialRequirements = new List<MaterialRequirementUI>();
        
        // 产出预览
        private Panel _outputPanel;
        private Image _outputIcon;
        private Label _outputNameLabel;
        private Label _outputCountLabel;
        
        // 合成控制
        private Panel _craftingControlPanel;
        private Label _costLabel;
        private Label _successRateLabel;
        private TextBox _craftCountInput;
        private Button _craftButton;
        private Button _quickCraftButton;

        // 数据引用
        private MaterialCraftingSystem _craftingSystem;
        private InventoryUI _inventoryUI;
        private CraftingRecipe _selectedRecipe;
        private bool _isVisible = false;
        private int _playerGold = 1000;  // TODO: 从角色数据获取

        #endregion

        #region 材料需求UI类

        /// <summary>
        /// 材料需求显示组件
        /// </summary>
        private class MaterialRequirementUI
        {
            public Panel Panel;
            public Image IconImage;
            public Label NameLabel;
            public Label CountLabel;
            public int RequiredMaterialId;
            public int RequiredCount;
            public bool IsSufficient;
        }

        #endregion

        #region 生命周期

        public override void OnStart()
        {
            FindDependencies();
            InitializeCraftingUI();
            HideCrafting();  // 默认隐藏
            Debug.Log("[CraftingUI] 材料合成UI初始化完成");
        }

        public override void OnUpdate()
        {
            // 快捷键切换合成界面
            if (Input.GetKeyDown(KeyboardKeys.C))
            {
                ToggleCrafting();
            }

            // 更新材料需求显示
            if (_isVisible && _selectedRecipe != null)
            {
                UpdateMaterialRequirements();
            }
        }

        public override void OnDestroy()
        {
            CleanupCrafting();
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 查找依赖组件
        /// </summary>
        private void FindDependencies()
        {
            // 查找合成系统
            var craftingSystemActor = Scene.FindActor("MaterialCraftingSystem");
            if (craftingSystemActor != null)
            {
                _craftingSystem = craftingSystemActor.GetScript<MaterialCraftingSystem>();
            }

            if (_craftingSystem == null)
            {
                Debug.LogWarning("[CraftingUI] 未找到MaterialCraftingSystem");
            }

            // 查找背包UI
            _inventoryUI = Actor.GetScript<InventoryUI>();
            if (_inventoryUI == null)
            {
                Debug.LogWarning("[CraftingUI] 未找到InventoryUI组件");
            }
        }

        /// <summary>
        /// 初始化合成UI
        /// </summary>
        private void InitializeCraftingUI()
        {
            // 创建合成窗口
            _craftingWindow = new Panel
            {
                AnchorPreset = AnchorPresets.MiddleCenter,
                Offsets = new Margin(-WindowWidth / 2, -WindowHeight / 2, -WindowWidth / 2, -WindowHeight / 2),
                Size = new Float2(WindowWidth, WindowHeight),
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f)
            };

            // 添加到GUI
            var canvas = Actor.GetScript<UICanvas>();
            if (canvas?.GUI != null)
            {
                canvas.GUI.AddChild(_craftingWindow);
            }
            else
            {
                Debug.LogWarning("[CraftingUI] 未找到UICanvas组件");
                return;
            }

            CreateTitleBar();
            CreateRecipeList();
            CreateCraftingPanel();
        }

        /// <summary>
        /// 创建标题栏
        /// </summary>
        private void CreateTitleBar()
        {
            _titleBar = new Panel
            {
                Bounds = new Rectangle(0, 0, WindowWidth, 40),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 1.0f)
            };
            _craftingWindow.AddChild(_titleBar);

            // 标题文本
            _titleLabel = new Label
            {
                Bounds = new Rectangle(10, 8, 200, 24),
                Text = "材料合成",
                TextColor = new Color(0.9f, 0.8f, 0.5f),
                TextColorHighlighted = new Color(0.9f, 0.8f, 0.5f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            _titleBar.AddChild(_titleLabel);

            // 关闭按钮
            _closeButton = new Button
            {
                Bounds = new Rectangle(WindowWidth - 35, 5, 30, 30),
                Text = "×",
                TextColor = Color.White,
                BackgroundColor = new Color(0.5f, 0.2f, 0.2f, 0.8f)
            };
            _closeButton.ButtonClicked += (btn) => HideCrafting();
            _titleBar.AddChild(_closeButton);
        }

        /// <summary>
        /// 创建配方列表
        /// </summary>
        private void CreateRecipeList()
        {
            _recipeListPanel = new Panel
            {
                Bounds = new Rectangle(10, 50, RecipeListWidth, WindowHeight - 60),
                BackgroundColor = new Color(0.12f, 0.12f, 0.17f, 1.0f)
            };
            _craftingWindow.AddChild(_recipeListPanel);

            // 标题
            var listTitle = new Label
            {
                Bounds = new Rectangle(0, 5, RecipeListWidth, 25),
                Text = "配方列表",
                TextColor = Color.White,
                TextColorHighlighted = Color.White,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            _recipeListPanel.AddChild(listTitle);

            // 滚动视图
            _recipeScrollView = new ScrollableControl
            {
                Bounds = new Rectangle(5, 35, RecipeListWidth - 10, WindowHeight - 105),
                BackgroundColor = Color.Transparent
            };
            _recipeListPanel.AddChild(_recipeScrollView);

            LoadRecipeList();
        }

        /// <summary>
        /// 加载配方列表
        /// </summary>
        private void LoadRecipeList()
        {
            if (_craftingSystem == null) return;

            var recipes = _craftingSystem.GetAllRecipes();
            float yPos = 0;

            foreach (var recipe in recipes)
            {
                var recipeBtn = new Button
                {
                    Bounds = new Rectangle(0, yPos, RecipeListWidth - 20, 50),
                    Text = recipe.RecipeName,
                    TextColor = Color.White,
                    BackgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.8f)
                };

                // 闭包捕获
                var capturedRecipe = recipe;
                recipeBtn.ButtonClicked += (btn) => SelectRecipe(capturedRecipe);

                _recipeScrollView.AddChild(recipeBtn);
                _recipeButtons.Add(recipeBtn);

                yPos += 55;
            }

            Debug.Log($"[CraftingUI] 加载了 {recipes.Count} 个配方");
        }

        /// <summary>
        /// 创建合成面板
        /// </summary>
        private void CreateCraftingPanel()
        {
            float leftMargin = 20 + RecipeListWidth;
            float panelWidth = WindowWidth - leftMargin - 10;

            _craftingPanel = new Panel
            {
                Bounds = new Rectangle(leftMargin, 50, panelWidth, WindowHeight - 60),
                BackgroundColor = new Color(0.12f, 0.12f, 0.17f, 1.0f)
            };
            _craftingWindow.AddChild(_craftingPanel);

            CreateRecipeInfoPanel();
            CreateMaterialsPanel();
            CreateOutputPanel();
            CreateControlPanel();
        }

        /// <summary>
        /// 创建配方信息面板
        /// </summary>
        private void CreateRecipeInfoPanel()
        {
            var infoPanel = new Panel
            {
                Bounds = new Rectangle(10, 10, _craftingPanel.Width - 20, 60),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.9f)
            };
            _craftingPanel.AddChild(infoPanel);

            _recipeNameLabel = new Label
            {
                Bounds = new Rectangle(10, 8, infoPanel.Width - 20, 20),
                Text = "请选择配方",
                TextColor = new Color(0.9f, 0.8f, 0.5f),
                TextColorHighlighted = new Color(0.9f, 0.8f, 0.5f),
                HorizontalAlignment = TextAlignment.Near
            };
            infoPanel.AddChild(_recipeNameLabel);

            _recipeDescLabel = new Label
            {
                Bounds = new Rectangle(10, 30, infoPanel.Width - 20, 20),
                Text = "",
                TextColor = Color.LightGray,
                TextColorHighlighted = Color.LightGray,
                HorizontalAlignment = TextAlignment.Near
            };
            infoPanel.AddChild(_recipeDescLabel);
        }

        /// <summary>
        /// 创建材料需求面板
        /// </summary>
        private void CreateMaterialsPanel()
        {
            _materialsPanel = new Panel
            {
                Bounds = new Rectangle(10, 80, _craftingPanel.Width - 20, 150),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.9f)
            };
            _craftingPanel.AddChild(_materialsPanel);

            var title = new Label
            {
                Bounds = new Rectangle(10, 5, 100, 20),
                Text = "所需材料:",
                TextColor = Color.White,
                TextColorHighlighted = Color.White,
                HorizontalAlignment = TextAlignment.Near
            };
            _materialsPanel.AddChild(title);
        }

        /// <summary>
        /// 创建产出预览面板
        /// </summary>
        private void CreateOutputPanel()
        {
            _outputPanel = new Panel
            {
                Bounds = new Rectangle(10, 240, _craftingPanel.Width - 20, 80),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.9f)
            };
            _craftingPanel.AddChild(_outputPanel);

            var title = new Label
            {
                Bounds = new Rectangle(10, 5, 100, 20),
                Text = "合成产出:",
                TextColor = Color.White,
                TextColorHighlighted = Color.White,
                HorizontalAlignment = TextAlignment.Near
            };
            _outputPanel.AddChild(title);

            // 产出图标
            _outputIcon = new Image
            {
                Bounds = new Rectangle(20, 30, 40, 40),
                Brush = new TextureBrush(),
                KeepAspectRatio = true,
                Color = Color.Gray
            };
            _outputPanel.AddChild(_outputIcon);

            // 产出名称
            _outputNameLabel = new Label
            {
                Bounds = new Rectangle(70, 35, 200, 20),
                Text = "",
                TextColor = Color.White,
                TextColorHighlighted = Color.White,
                HorizontalAlignment = TextAlignment.Near
            };
            _outputPanel.AddChild(_outputNameLabel);

            // 产出数量
            _outputCountLabel = new Label
            {
                Bounds = new Rectangle(70, 52, 100, 18),
                Text = "",
                TextColor = Color.LightGray,
                TextColorHighlighted = Color.LightGray,
                HorizontalAlignment = TextAlignment.Near
            };
            _outputPanel.AddChild(_outputCountLabel);
        }

        /// <summary>
        /// 创建合成控制面板
        /// </summary>
        private void CreateControlPanel()
        {
            _craftingControlPanel = new Panel
            {
                Bounds = new Rectangle(10, 330, _craftingPanel.Width - 20, 100),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.9f)
            };
            _craftingPanel.AddChild(_craftingControlPanel);

            // 货币消耗
            _costLabel = new Label
            {
                Bounds = new Rectangle(10, 10, 200, 20),
                Text = "消耗金币: 0",
                TextColor = new Color(1.0f, 0.9f, 0.3f),
                TextColorHighlighted = new Color(1.0f, 0.9f, 0.3f),
                HorizontalAlignment = TextAlignment.Near
            };
            _craftingControlPanel.AddChild(_costLabel);

            // 成功率
            _successRateLabel = new Label
            {
                Bounds = new Rectangle(10, 32, 200, 20),
                Text = "成功率: 100%",
                TextColor = Color.Green,
                TextColorHighlighted = Color.Green,
                HorizontalAlignment = TextAlignment.Near
            };
            _craftingControlPanel.AddChild(_successRateLabel);

            // 合成数量输入
            var countLabel = new Label
            {
                Bounds = new Rectangle(10, 58, 80, 20),
                Text = "合成数量:",
                TextColor = Color.White,
                TextColorHighlighted = Color.White,
                HorizontalAlignment = TextAlignment.Near
            };
            _craftingControlPanel.AddChild(countLabel);

            _craftCountInput = new TextBox
            {
                Bounds = new Rectangle(95, 56, 60, 24),
                Text = "1",
                BackgroundColor = new Color(0.2f, 0.2f, 0.25f, 1.0f),
                TextColor = Color.White
            };
            _craftingControlPanel.AddChild(_craftCountInput);

            // 合成按钮
            _craftButton = new Button
            {
                Bounds = new Rectangle(170, 54, 100, 28),
                Text = "合成",
                TextColor = Color.White,
                BackgroundColor = new Color(0.2f, 0.6f, 0.3f, 0.9f)
            };
            _craftButton.ButtonClicked += OnCraftButtonClick;
            _craftingControlPanel.AddChild(_craftButton);

            // 一键合成按钮
            _quickCraftButton = new Button
            {
                Bounds = new Rectangle(280, 54, 100, 28),
                Text = "一键合成",
                TextColor = Color.White,
                BackgroundColor = new Color(0.3f, 0.5f, 0.7f, 0.9f)
            };
            _quickCraftButton.ButtonClicked += OnQuickCraftButtonClick;
            _craftingControlPanel.AddChild(_quickCraftButton);
        }

        #endregion

        #region 配方选择

        /// <summary>
        /// 选择配方
        /// </summary>
        private void SelectRecipe(CraftingRecipe recipe)
        {
            _selectedRecipe = recipe;
            Debug.Log($"[CraftingUI] 选择配方: {recipe.RecipeName}");

            // 更新配方信息
            _recipeNameLabel.Text = recipe.RecipeName;
            _recipeDescLabel.Text = $"配方ID: {recipe.RecipeId}";

            // 更新材料需求
            UpdateMaterialRequirementsUI();

            // 更新产出预览
            UpdateOutputPreview();

            // 更新合成信息
            UpdateCraftingInfo();

            // 高亮选中的配方按钮
            HighlightSelectedRecipe(recipe);
        }

        /// <summary>
        /// 高亮选中的配方按钮
        /// </summary>
        private void HighlightSelectedRecipe(CraftingRecipe recipe)
        {
            // 重置所有按钮颜色
            foreach (var btn in _recipeButtons)
            {
                btn.BackgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.8f);
            }

            // 高亮选中的按钮
            var selectedBtn = _recipeButtons.Find(b => b.Text == recipe.RecipeName);
            if (selectedBtn != null)
            {
                selectedBtn.BackgroundColor = new Color(0.3f, 0.4f, 0.6f, 0.9f);
            }
        }

        #endregion

        #region UI更新

        /// <summary>
        /// 更新材料需求UI
        /// </summary>
        private void UpdateMaterialRequirementsUI()
        {
            // 清除旧的材料需求显示
            foreach (var req in _materialRequirements)
            {
                if (req.Panel.Parent != null)
                {
                    req.Panel.Parent.RemoveChild(req.Panel);
                    req.Panel.Dispose();
                }
            }
            _materialRequirements.Clear();

            if (_selectedRecipe == null) return;

            // 创建新的材料需求显示
            float yPos = 30;
            for (int i = 0; i < _selectedRecipe.RequiredMaterialIds.Count; i++)
            {
                int materialId = _selectedRecipe.RequiredMaterialIds[i];
                int count = _selectedRecipe.RequiredMaterialCounts[i];

                var reqUI = CreateMaterialRequirementUI(materialId, count, yPos);
                _materialRequirements.Add(reqUI);
                _materialsPanel.AddChild(reqUI.Panel);

                yPos += 35;
            }
        }

        /// <summary>
        /// 创建材料需求UI组件
        /// </summary>
        private MaterialRequirementUI CreateMaterialRequirementUI(int materialId, int requiredCount, float yPos)
        {
            var material = MaterialDatabase.GetMaterial(materialId);
            
            var reqUI = new MaterialRequirementUI
            {
                RequiredMaterialId = materialId,
                RequiredCount = requiredCount,
                Panel = new Panel
                {
                    Bounds = new Rectangle(10, yPos, _materialsPanel.Width - 20, 30),
                    BackgroundColor = Color.Transparent
                }
            };

            // 图标
            reqUI.IconImage = new Image
            {
                Bounds = new Rectangle(0, 0, 30, 30),
                Brush = new TextureBrush(),
                KeepAspectRatio = true,
                Color = material?.GetElementColor() ?? Color.Gray
            };
            reqUI.Panel.AddChild(reqUI.IconImage);

            // 名称
            reqUI.NameLabel = new Label
            {
                Bounds = new Rectangle(35, 5, 200, 20),
                Text = material?.MaterialName ?? "未知材料",
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Near
            };
            reqUI.Panel.AddChild(reqUI.NameLabel);

            // 数量
            reqUI.CountLabel = new Label
            {
                Bounds = new Rectangle(_materialsPanel.Width - 120, 5, 100, 20),
                Text = $"0/{requiredCount}",
                TextColor = Color.Red,
                HorizontalAlignment = TextAlignment.Far
            };
            reqUI.Panel.AddChild(reqUI.CountLabel);

            return reqUI;
        }

        /// <summary>
        /// 更新材料需求显示
        /// </summary>
        private void UpdateMaterialRequirements()
        {
            if (_inventoryUI == null) return;

            foreach (var req in _materialRequirements)
            {
                int currentCount = _inventoryUI.GetMaterialCount(req.RequiredMaterialId);
                req.IsSufficient = currentCount >= req.RequiredCount;

                req.CountLabel.Text = $"{currentCount}/{req.RequiredCount}";
                req.CountLabel.TextColor = req.IsSufficient ? Color.Green : Color.Red;
            }
        }

        /// <summary>
        /// 更新产出预览
        /// </summary>
        private void UpdateOutputPreview()
        {
            if (_selectedRecipe == null) return;

            var outputMaterial = MaterialDatabase.GetMaterial(_selectedRecipe.OutputMaterialId);
            if (outputMaterial != null)
            {
                _outputIcon.Color = outputMaterial.GetElementColor();
                _outputNameLabel.Text = outputMaterial.MaterialName;
                _outputCountLabel.Text = $"数量: {_selectedRecipe.OutputCount}";

                // 根据品质设置名称颜色
                _outputNameLabel.TextColor = outputMaterial.GetQualityColor();
            }
        }

        /// <summary>
        /// 更新合成信息
        /// </summary>
        private void UpdateCraftingInfo()
        {
            if (_selectedRecipe == null) return;

            _costLabel.Text = $"消耗金币: {_selectedRecipe.CurrencyCost}";
            _successRateLabel.Text = $"成功率: {_selectedRecipe.SuccessRate:F1}%";

            // 根据成功率设置颜色
            if (_selectedRecipe.SuccessRate >= 90f)
            {
                _successRateLabel.TextColor = Color.Green;
            }
            else if (_selectedRecipe.SuccessRate >= 50f)
            {
                _successRateLabel.TextColor = Color.Yellow;
            }
            else
            {
                _successRateLabel.TextColor = Color.Red;
            }
        }

        #endregion

        #region 合成逻辑

        /// <summary>
        /// 合成按钮点击
        /// </summary>
        private void OnCraftButtonClick(Button button)
        {
            if (_selectedRecipe == null)
            {
                Debug.LogWarning("[CraftingUI] 未选择配方");
                return;
            }

            // 解析合成数量
            if (!int.TryParse(_craftCountInput.Text, out int craftCount) || craftCount <= 0)
            {
                Debug.LogWarning("[CraftingUI] 无效的合成数量");
                return;
            }

            // 检查材料是否足够
            if (!CheckMaterialsSufficient(craftCount))
            {
                Debug.LogWarning("[CraftingUI] 材料不足");
                return;
            }

            // 检查金币是否足够
            int totalCost = _selectedRecipe.CurrencyCost * craftCount;
            if (_playerGold < totalCost)
            {
                Debug.LogWarning($"[CraftingUI] 金币不足，需要 {totalCost}，当前 {_playerGold}");
                return;
            }

            // 执行合成
            PerformCrafting(craftCount);
        }

        /// <summary>
        /// 一键合成按钮点击
        /// </summary>
        private void OnQuickCraftButtonClick(Button button)
        {
            if (_selectedRecipe == null)
            {
                Debug.LogWarning("[CraftingUI] 未选择配方");
                return;
            }

            // 计算最多可以合成多少次
            int maxCraftCount = CalculateMaxCraftCount();
            if (maxCraftCount <= 0)
            {
                Debug.LogWarning("[CraftingUI] 无法合成，材料或金币不足");
                return;
            }

            Debug.Log($"[CraftingUI] 一键合成 {maxCraftCount} 次");
            PerformCrafting(maxCraftCount);
        }

        /// <summary>
        /// 检查材料是否足够
        /// </summary>
        private bool CheckMaterialsSufficient(int craftCount)
        {
            if (_inventoryUI == null) return false;

            for (int i = 0; i < _selectedRecipe.RequiredMaterialIds.Count; i++)
            {
                int materialId = _selectedRecipe.RequiredMaterialIds[i];
                int requiredCount = _selectedRecipe.RequiredMaterialCounts[i] * craftCount;
                int currentCount = _inventoryUI.GetMaterialCount(materialId);

                if (currentCount < requiredCount)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 计算最多可合成次数
        /// </summary>
        private int CalculateMaxCraftCount()
        {
            if (_inventoryUI == null) return 0;

            int maxCount = int.MaxValue;

            // 根据材料计算
            for (int i = 0; i < _selectedRecipe.RequiredMaterialIds.Count; i++)
            {
                int materialId = _selectedRecipe.RequiredMaterialIds[i];
                int requiredCount = _selectedRecipe.RequiredMaterialCounts[i];
                int currentCount = _inventoryUI.GetMaterialCount(materialId);

                int possibleCount = currentCount / requiredCount;
                maxCount = Mathf.Min(maxCount, possibleCount);
            }

            // 根据金币计算
            if (_selectedRecipe.CurrencyCost > 0)
            {
                int goldLimit = _playerGold / _selectedRecipe.CurrencyCost;
                maxCount = Mathf.Min(maxCount, goldLimit);
            }

            return maxCount;
        }

        /// <summary>
        /// 执行合成
        /// </summary>
        private void PerformCrafting(int craftCount)
        {
            if (_craftingSystem == null || _inventoryUI == null)
            {
                Debug.LogWarning("[CraftingUI] 缺少必要的系统组件");
                return;
            }

            int successCount = 0;

            for (int i = 0; i < craftCount; i++)
            {
                // 消耗材料
                bool materialsConsumed = true;
                for (int j = 0; j < _selectedRecipe.RequiredMaterialIds.Count; j++)
                {
                    int materialId = _selectedRecipe.RequiredMaterialIds[j];
                    int count = _selectedRecipe.RequiredMaterialCounts[j];

                    if (!_inventoryUI.RemoveMaterial(materialId, count))
                    {
                        materialsConsumed = false;
                        break;
                    }
                }

                if (!materialsConsumed)
                {
                    Debug.LogWarning($"[CraftingUI] 材料消耗失败，已成功合成 {successCount} 次");
                    break;
                }

                // 消耗金币
                _playerGold -= _selectedRecipe.CurrencyCost;

                // 判断成功率
                float roll = RandomUtil.Random.NextFloat() * 100f;
                if (roll <= _selectedRecipe.SuccessRate)
                {
                    // 合成成功，添加产出
                    var outputMaterial = MaterialDatabase.GetMaterial(_selectedRecipe.OutputMaterialId);
                    if (outputMaterial != null)
                    {
                        _inventoryUI.AddMaterial(outputMaterial, _selectedRecipe.OutputCount);
                        successCount++;
                    }
                }
                else
                {
                    Debug.Log($"[CraftingUI] 合成失败（概率）");
                }
            }

            Debug.Log($"[CraftingUI] 合成完成: 成功 {successCount}/{craftCount} 次");

            // 更新显示
            UpdateMaterialRequirements();
        }

        #endregion

        #region 显示/隐藏

        /// <summary>
        /// 切换合成界面显示
        /// </summary>
        public void ToggleCrafting()
        {
            if (_isVisible)
            {
                HideCrafting();
            }
            else
            {
                ShowCrafting();
            }
        }

        /// <summary>
        /// 显示合成界面
        /// </summary>
        public void ShowCrafting()
        {
            _craftingWindow.Visible = true;
            _isVisible = true;
            
            if (_selectedRecipe != null)
            {
                UpdateMaterialRequirements();
            }

            Debug.Log("[CraftingUI] 显示合成界面");
        }

        /// <summary>
        /// 隐藏合成界面
        /// </summary>
        public void HideCrafting()
        {
            _craftingWindow.Visible = false;
            _isVisible = false;
            Debug.Log("[CraftingUI] 隐藏合成界面");
        }

        /// <summary>
        /// 是否可见
        /// </summary>
        public bool IsVisible => _isVisible;

        #endregion

        #region 清理

        /// <summary>
        /// 清理合成UI
        /// </summary>
        private void CleanupCrafting()
        {
            if (_craftingWindow != null && _craftingWindow.Parent != null)
            {
                _craftingWindow.Parent.RemoveChild(_craftingWindow);
                _craftingWindow.Dispose();
            }

            _recipeButtons.Clear();
            _materialRequirements.Clear();
        }

        #endregion
    }
}
