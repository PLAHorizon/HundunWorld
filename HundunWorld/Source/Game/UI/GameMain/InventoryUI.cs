using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Equipment.Material;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 背包槽位数据
    /// </summary>
    public class InventorySlot
    {
        public int SlotIndex;                // 槽位索引
        public MaterialData Material;        // 材料数据
        public int Count;                    // 数量
        public bool IsLocked;                // 是否锁定
    }

    /// <summary>
    /// 背包UI组件
    /// 显示物品、材料、装备等
    /// 支持拖拽、排序、过滤、搜索
    /// </summary>
    public class InventoryUI : Script
    {
        #region 配置参数

        [Header("背包配置")]
        [Tooltip("背包槽位列数")]
        public int ColumnCount = 8;

        [Tooltip("背包槽位行数")]
        public int RowCount = 6;

        [Tooltip("槽位大小")]
        public float SlotSize = 50f;

        [Tooltip("槽位间距")]
        public float SlotSpacing = 4f;

        [Tooltip("背包窗口宽度")]
        public float WindowWidth = 500f;

        [Tooltip("背包窗口高度")]
        public float WindowHeight = 450f;

        #endregion

        #region UI组件

        private Panel _inventoryWindow;
        private Panel _titleBar;
        private Label _titleLabel;
        private Button _closeButton;
        private Panel _filterPanel;
        private Panel _slotsContainer;
        private Label _goldLabel;
        private Label _capacityLabel;

        private List<SlotUI> _slotUIs = new List<SlotUI>();
        private List<InventorySlot> _inventorySlots = new List<InventorySlot>();

        private bool _isVisible = false;
        private int _playerGold = 1000;

        // 排序和过滤状态
        private string _currentFilter = "全部";
        private int _selectedSlotIndex = -1;

        #endregion

        #region 槽位UI类

        /// <summary>
        /// 槽位UI组件
        /// </summary>
        private class SlotUI
        {
            public Panel SlotPanel;
            public Image IconImage;
            public Label CountLabel;
            public Panel QualityBorder;
            public Panel SelectedOverlay;
            public int SlotIndex;
            public bool IsSelected;
        }

        /// <summary>
        /// 自定义槽位面板，用于处理鼠标点击事件
        /// </summary>
        private class SlotPanel : Panel
        {
            public Action<int> SlotClicked;
            public Action<int> SlotDoubleClicked;
            public int SlotIndex;

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    SlotClicked?.Invoke(SlotIndex);
                }
                
                return base.OnMouseUp(location, button);
            }

            public override bool OnMouseDoubleClick(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    SlotDoubleClicked?.Invoke(SlotIndex);
                }
                
                return base.OnMouseDoubleClick(location, button);
            }
        }

        #endregion

        #region 生命周期

        public override void OnStart()
        {
            InitializeInventoryUI();
            InitializeInventoryData();
            HideInventory();  // 默认隐藏
            Debug.Log("[InventoryUI] 背包UI初始化完成");
        }

        public override void OnUpdate()
        {
            // 快捷键切换背包显示
            if (Input.GetKeyDown(KeyboardKeys.B) || Input.GetKeyDown(KeyboardKeys.I))
            {
                ToggleInventory();
            }
        }

        public override void OnDestroy()
        {
            CleanupInventory();
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化背包UI
        /// </summary>
        private void InitializeInventoryUI()
        {
            // 创建背包窗口
            _inventoryWindow = new Panel
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
                canvas.GUI.AddChild(_inventoryWindow);
            }
            else
            {
                Debug.LogWarning("[InventoryUI] 未找到UICanvas组件");
                return;
            }

            CreateTitleBar();
            CreateFilterPanel();
            CreateSlotsContainer();
            CreateInfoPanel();
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
            _inventoryWindow.AddChild(_titleBar);

            // 标题文本
            _titleLabel = new Label
            {
                Bounds = new Rectangle(10, 8, 200, 24),
                Text = "背包",
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
            _closeButton.ButtonClicked += (btn) => HideInventory();
            _titleBar.AddChild(_closeButton);
        }

        /// <summary>
        /// 创建过滤面板
        /// </summary>
        private void CreateFilterPanel()
        {
            _filterPanel = new Panel
            {
                Bounds = new Rectangle(0, 40, WindowWidth, 35),
                BackgroundColor = new Color(0.12f, 0.12f, 0.17f, 1.0f)
            };
            _inventoryWindow.AddChild(_filterPanel);

            // 过滤按钮组
            CreateFilterButton("全部", 10, null);
            CreateFilterButton("材料", 70, MaterialTier.Basic);
            CreateFilterButton("装备", 130, null);
            CreateFilterButton("消耗品", 190, null);

            // 排序按钮
            var sortBtn = new Button
            {
                Bounds = new Rectangle(WindowWidth - 120, 5, 50, 25),
                Text = "排序",
                TextColor = Color.White,
                BackgroundColor = new Color(0.2f, 0.3f, 0.2f, 0.8f)
            };
            sortBtn.ButtonClicked += (btn) => SortInventory();
            _filterPanel.AddChild(sortBtn);

            // 搜索按钮
            var searchBtn = new Button
            {
                Bounds = new Rectangle(WindowWidth - 65, 5, 50, 25),
                Text = "搜索",
                TextColor = Color.White,
                BackgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.8f)
            };
            searchBtn.ButtonClicked += (btn) =>
            {
                Debug.Log("[InventoryUI] 搜索功能触发");
            };
            _filterPanel.AddChild(searchBtn);
        }

        /// <summary>
        /// 创建过滤按钮
        /// </summary>
        private void CreateFilterButton(string text, float xPos, MaterialTier? filterTier)
        {
            var filterBtn = new Button
            {
                Bounds = new Rectangle(xPos, 5, 55, 25),
                Text = text,
                TextColor = Color.White,
                BackgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.8f)
            };
            filterBtn.ButtonClicked += (btn) =>
            {
                Debug.Log($"[InventoryUI] 过滤器: {text}");
                _currentFilter = text;
                ApplyFilter(text);
            };
            _filterPanel.AddChild(filterBtn);
        }

        /// <summary>
        /// 创建槽位容器
        /// </summary>
        private void CreateSlotsContainer()
        {
            float containerHeight = WindowHeight - 40 - 35 - 40;  // 减去标题栏、过滤栏和信息栏

            _slotsContainer = new Panel
            {
                Bounds = new Rectangle(10, 75, WindowWidth - 20, containerHeight),
                BackgroundColor = Color.Transparent
            };
            _inventoryWindow.AddChild(_slotsContainer);

            // 创建槽位
            int totalSlots = ColumnCount * RowCount;
            for (int i = 0; i < totalSlots; i++)
            {
                CreateSlot(i);
            }
        }

        /// <summary>
        /// 创建单个槽位
        /// </summary>
        private void CreateSlot(int index)
        {
            int row = index / ColumnCount;
            int col = index % ColumnCount;

            float xPos = col * (SlotSize + SlotSpacing);
            float yPos = row * (SlotSize + SlotSpacing);

            var slotUI = new SlotUI
            {
                SlotIndex = index
            };

            // 槽位面板
            var slotPanel = new SlotPanel
            {
                SlotIndex = index,
                Bounds = new Rectangle(xPos, yPos, SlotSize, SlotSize),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.9f)
            };
            
            // 设置事件处理
            slotPanel.SlotClicked = (slotIndex) => {
                OnSlotClick(slotIndex);
            };
            
            slotPanel.SlotDoubleClicked = (slotIndex) => {
                OnSlotDoubleClick(slotIndex);
            };
            
            slotUI.SlotPanel = slotPanel;
            _slotsContainer.AddChild(slotPanel);

            // 品质边框
            slotUI.QualityBorder = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                BackgroundColor = Color.Transparent
            };
            slotUI.SlotPanel.AddChild(slotUI.QualityBorder);

            // 图标
            slotUI.IconImage = new Image
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(4, 4, 4, 4),
                Brush = new TextureBrush(),
                KeepAspectRatio = true,
                Color = Color.Gray  // 空槽位显示灰色
            };
            slotUI.SlotPanel.AddChild(slotUI.IconImage);

            // 数量标签
            slotUI.CountLabel = new Label
            {
                Bounds = new Rectangle(2, SlotSize - 18, SlotSize - 4, 16),
                Text = "",
                TextColor = Color.White,
                TextColorHighlighted = Color.White,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Far
            };
            slotUI.SlotPanel.AddChild(slotUI.CountLabel);

            // 选中遮罩
            slotUI.SelectedOverlay = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                BackgroundColor = new Color(1f, 1f, 1f, 0.3f),
                Visible = false
            };
            slotUI.SlotPanel.AddChild(slotUI.SelectedOverlay);

            _slotUIs.Add(slotUI);
        }

        /// <summary>
        /// 创建信息面板
        /// </summary>
        private void CreateInfoPanel()
        {
            float yPos = WindowHeight - 40;

            var infoPanel = new Panel
            {
                Bounds = new Rectangle(0, yPos, WindowWidth, 40),
                BackgroundColor = new Color(0.12f, 0.12f, 0.17f, 1.0f)
            };
            _inventoryWindow.AddChild(infoPanel);

            // 金币显示
            _goldLabel = new Label
            {
                Bounds = new Rectangle(10, 10, 150, 20),
                Text = "金币: 1000",
                TextColor = new Color(1.0f, 0.9f, 0.3f),
                TextColorHighlighted = new Color(1.0f, 0.9f, 0.3f),
                HorizontalAlignment = TextAlignment.Near
            };
            infoPanel.AddChild(_goldLabel);

            // 容量显示
            _capacityLabel = new Label
            {
                Bounds = new Rectangle(WindowWidth - 160, 10, 150, 20),
                Text = "容量: 0/48",
                TextColor = Color.LightGray,
                TextColorHighlighted = Color.LightGray,
                HorizontalAlignment = TextAlignment.Far
            };
            infoPanel.AddChild(_capacityLabel);
        }

        /// <summary>
        /// 初始化背包数据
        /// </summary>
        private void InitializeInventoryData()
        {
            int totalSlots = ColumnCount * RowCount;
            for (int i = 0; i < totalSlots; i++)
            {
                _inventorySlots.Add(new InventorySlot
                {
                    SlotIndex = i,
                    Material = null,
                    Count = 0,
                    IsLocked = false
                });
            }

            // 添加一些测试材料
            AddTestMaterials();
        }

        /// <summary>
        /// 添加测试材料
        /// </summary>
        private void AddTestMaterials()
        {
            // 铁矿石
            var ironOre = MaterialDatabase.GetMaterial(10001);
            if (ironOre != null)
            {
                AddMaterial(ironOre, 25);
            }

            // 青竹
            var bamboo = MaterialDatabase.GetMaterial(10002);
            if (bamboo != null)
            {
                AddMaterial(bamboo, 18);
            }

            // 寒泉水
            var water = MaterialDatabase.GetMaterial(10003);
            if (water != null)
            {
                AddMaterial(water, 10);
            }

            UpdateAllSlots();
        }

        #endregion

        #region 背包逻辑

        /// <summary>
        /// 添加材料到背包
        /// </summary>
        public bool AddMaterial(MaterialData material, int count)
        {
            if (material == null || count <= 0) return false;

            // 查找是否已有该材料
            var existingSlot = _inventorySlots.Find(s => s.Material?.MaterialId == material.MaterialId);
            if (existingSlot != null)
            {
                // 堆叠
                int newCount = existingSlot.Count + count;
                if (newCount <= material.MaxStack)
                {
                    existingSlot.Count = newCount;
                    UpdateSlot(existingSlot.SlotIndex);
                    Debug.Log($"[InventoryUI] 添加材料: {material.MaterialName} × {count}（堆叠）");
                    return true;
                }
                else
                {
                    // 超过堆叠上限，需要新槽位
                    int remaining = newCount - material.MaxStack;
                    existingSlot.Count = material.MaxStack;
                    UpdateSlot(existingSlot.SlotIndex);
                    return AddMaterial(material, remaining);  // 递归添加剩余部分
                }
            }

            // 查找空槽位
            var emptySlot = _inventorySlots.Find(s => s.Material == null);
            if (emptySlot != null)
            {
                emptySlot.Material = material;
                emptySlot.Count = Mathf.Min(count, material.MaxStack);
                UpdateSlot(emptySlot.SlotIndex);
                
                Debug.Log($"[InventoryUI] 添加材料: {material.MaterialName} × {count}（新槽位）");

                // 如果超过堆叠上限，递归添加剩余部分
                if (count > material.MaxStack)
                {
                    return AddMaterial(material, count - material.MaxStack);
                }

                return true;
            }

            Debug.LogWarning("[InventoryUI] 背包已满，无法添加材料");
            return false;
        }

        /// <summary>
        /// 移除材料
        /// </summary>
        public bool RemoveMaterial(int materialId, int count)
        {
            int remaining = count;

            foreach (var slot in _inventorySlots)
            {
                if (slot.Material?.MaterialId == materialId && slot.Count > 0)
                {
                    int removeCount = Mathf.Min(slot.Count, remaining);
                    slot.Count -= removeCount;
                    remaining -= removeCount;

                    if (slot.Count <= 0)
                    {
                        slot.Material = null;
                        slot.Count = 0;
                    }

                    UpdateSlot(slot.SlotIndex);

                    if (remaining <= 0)
                    {
                        Debug.Log($"[InventoryUI] 移除材料成功: ID {materialId} × {count}");
                        return true;
                    }
                }
            }

            Debug.LogWarning($"[InventoryUI] 材料不足，无法移除: ID {materialId} × {count}");
            return false;
        }

        /// <summary>
        /// 获取材料数量
        /// </summary>
        public int GetMaterialCount(int materialId)
        {
            int total = 0;
            foreach (var slot in _inventorySlots)
            {
                if (slot.Material?.MaterialId == materialId)
                {
                    total += slot.Count;
                }
            }
            return total;
        }

        #endregion

        #region UI更新

        /// <summary>
        /// 更新槽位显示
        /// </summary>
        private void UpdateSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slotUIs.Count) return;

            var slotUI = _slotUIs[slotIndex];
            var slotData = _inventorySlots[slotIndex];

            if (slotData.Material != null && slotData.Count > 0)
            {
                // 显示材料
                slotUI.IconImage.Color = slotData.Material.GetElementColor();
                slotUI.CountLabel.Text = slotData.Count.ToString();
                
                // 品质边框颜色
                slotUI.QualityBorder.BackgroundColor = slotData.Material.GetQualityColor();

                // TODO: 加载实际图标纹理
            }
            else
            {
                // 空槽位
                slotUI.IconImage.Color = Color.Gray;
                slotUI.CountLabel.Text = "";
                slotUI.QualityBorder.BackgroundColor = Color.Transparent;
            }

            UpdateCapacityLabel();
        }

        /// <summary>
        /// 更新所有槽位
        /// </summary>
        private void UpdateAllSlots()
        {
            for (int i = 0; i < _inventorySlots.Count; i++)
            {
                UpdateSlot(i);
            }
        }

        /// <summary>
        /// 更新容量标签
        /// </summary>
        private void UpdateCapacityLabel()
        {
            int usedSlots = _inventorySlots.FindAll(s => s.Material != null).Count;
            int totalSlots = _inventorySlots.Count;
            _capacityLabel.Text = $"容量: {usedSlots}/{totalSlots}";
        }

        /// <summary>
        /// 更新金币显示
        /// </summary>
        private void UpdateGoldLabel()
        {
            _goldLabel.Text = $"金币: {_playerGold}";
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 槽位双击事件
        /// </summary>
        private void OnSlotDoubleClick(int slotIndex)
        {
            var slot = _inventorySlots[slotIndex];
            if (slot.Material != null)
            {
                Debug.Log($"[InventoryUI] 双击槽位 {slotIndex}: {slot.Material.MaterialName} × {slot.Count}");
            }
        }

        /// <summary>
        /// 槽位单击选中
        /// </summary>
        private void OnSlotClick(int slotIndex)
        {
            // 取消之前的选中
            if (_selectedSlotIndex >= 0 && _selectedSlotIndex < _slotUIs.Count)
            {
                _slotUIs[_selectedSlotIndex].SelectedOverlay.Visible = false;
                _slotUIs[_selectedSlotIndex].IsSelected = false;
            }

            // 选中当前槽位
            if (slotIndex >= 0 && slotIndex < _slotUIs.Count)
            {
                var slotData = _inventorySlots[slotIndex];
                if (slotData.Material != null)
                {
                    _slotUIs[slotIndex].SelectedOverlay.Visible = true;
                    _slotUIs[slotIndex].IsSelected = true;
                    _selectedSlotIndex = slotIndex;
                    Debug.Log($"[InventoryUI] 选中槽位 {slotIndex}: {slotData.Material.MaterialName}");
                }
                else
                {
                    _selectedSlotIndex = -1;
                }
            }
        }

        /// <summary>
        /// 应用物品过滤
        /// </summary>
        private void ApplyFilter(string filterType)
        {
            for (int i = 0; i < _inventorySlots.Count && i < _slotUIs.Count; i++)
            {
                var slot = _inventorySlots[i];
                var slotUI = _slotUIs[i];

                if (filterType == "全部" || slot.Material == null)
                {
                    slotUI.SlotPanel.Visible = true;
                }
                else if (filterType == "材料")
                {
                    slotUI.SlotPanel.Visible = slot.Material != null;
                }
                else
                {
                    slotUI.SlotPanel.Visible = slot.Material == null;
                }
            }

            Debug.Log($"[InventoryUI] 已应用过滤器: {filterType}");
        }

        /// <summary>
        /// 排序背包物品（按名称排序，空槽位移到末尾）
        /// </summary>
        private void SortInventory()
        {
            // 分离非空和空槽位
            var filledSlots = _inventorySlots.FindAll(s => s.Material != null);
            var emptySlots = _inventorySlots.FindAll(s => s.Material == null);

            // 按材料名称排序
            filledSlots.Sort((a, b) =>
            {
                int nameCompare = string.Compare(a.Material.MaterialName, b.Material.MaterialName, StringComparison.Ordinal);
                if (nameCompare != 0) return nameCompare;
                return b.Count.CompareTo(a.Count);
            });

            // 重建槽位列表
            _inventorySlots.Clear();
            int slotIndex = 0;
            foreach (var slot in filledSlots)
            {
                slot.SlotIndex = slotIndex++;
                _inventorySlots.Add(slot);
            }
            foreach (var slot in emptySlots)
            {
                slot.SlotIndex = slotIndex++;
                _inventorySlots.Add(slot);
            }

            UpdateAllSlots();
            Debug.Log("[InventoryUI] 背包物品已排序");
        }

        #endregion

        #region 显示/隐藏

        /// <summary>
        /// 切换背包显示
        /// </summary>
        public void ToggleInventory()
        {
            if (_isVisible)
            {
                HideInventory();
            }
            else
            {
                ShowInventory();
            }
        }

        /// <summary>
        /// 显示背包
        /// </summary>
        public void ShowInventory()
        {
            _inventoryWindow.Visible = true;
            _isVisible = true;
            UpdateAllSlots();
            UpdateGoldLabel();
            Debug.Log("[InventoryUI] 显示背包");
        }

        /// <summary>
        /// 隐藏背包
        /// </summary>
        public void HideInventory()
        {
            _inventoryWindow.Visible = false;
            _isVisible = false;
            Debug.Log("[InventoryUI] 隐藏背包");
        }

        /// <summary>
        /// 是否可见
        /// </summary>
        public bool IsVisible => _isVisible;

        #endregion

        #region 清理

        /// <summary>
        /// 清理背包UI
        /// </summary>
        private void CleanupInventory()
        {
            if (_inventoryWindow != null && _inventoryWindow.Parent != null)
            {
                _inventoryWindow.Parent.RemoveChild(_inventoryWindow);
                _inventoryWindow.Dispose();
            }

            _slotUIs.Clear();
            _inventorySlots.Clear();
        }

        #endregion
    }
}
