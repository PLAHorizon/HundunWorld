using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Equipment.Material;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.Services;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 背包槽位数据
    /// </summary>
    public class InventorySlot
    {
        public int SlotIndex;
        public MaterialData Material;
        public int Count;
        public bool IsLocked;
    }

    /// <summary>
    /// 背包UI组件
    /// 显示物品、材料、装备等
    /// 支持拖拽、排序、过滤、搜索
    /// </summary>
    public class InventoryUI : Script
    {
        public int ColumnCount = 8;
        public int RowCount = 6;
        public float SlotSize = 50f;
        public float SlotSpacing = 4f;
        public float WindowWidth = 500f;
        public float WindowHeight = 450f;

        private const float EmbeddedSlotSize = 44f;
        private const float EmbeddedSpacing = 5f;
        private const int EmbeddedColumns = 4;

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
        private string _currentFilter = "全部";
        private int _selectedSlotIndex = -1;
        private bool _isDragging = false;
        private int _dragSourceSlotIndex = -1;
        private Panel _dragGhost;

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

        private class SlotPanel : Panel
        {
            public Action<int> SlotClicked;
            public Action<int> SlotDoubleClicked;
            public Action<int> SlotDragStarted;
            public Action<int> SlotDragEnded;
            public int SlotIndex;
            private bool _mouseDown;
            private Float2 _mouseDownPos;
            private const float DragThreshold = 5.0f;

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    _mouseDown = true;
                    _mouseDownPos = location;
                }
                return base.OnMouseDown(location, button);
            }

            public override void OnMouseMove(Float2 location)
            {
                if (_mouseDown)
                {
                    float dist = Float2.Distance(location, _mouseDownPos);
                    if (dist > DragThreshold)
                    {
                        _mouseDown = false;
                        SlotDragStarted?.Invoke(SlotIndex);
                    }
                }
                base.OnMouseMove(location);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    if (_mouseDown)
                    {
                        _mouseDown = false;
                        SlotClicked?.Invoke(SlotIndex);
                    }
                    else
                    {
                        SlotDragEnded?.Invoke(SlotIndex);
                    }
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

        private class EmbeddedSlotPanel : Panel
        {
            public int SlotIndex;
            public int ItemId;
            public Action<int> SlotClicked;

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && IsMouseOver)
                {
                    SlotClicked?.Invoke(ItemId);
                }
                return base.OnMouseUp(location, button);
            }
        }

        public override void OnStart()
        {
            InitializeInventoryUI();
            InitializeInventoryData();
            HideInventory();
            Debug.Log("[InventoryUI] 背包UI初始化完成");
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyboardKeys.B) || Input.GetKeyDown(KeyboardKeys.I))
            {
                ToggleInventory();
            }
        }

        public override void OnDestroy()
        {
            CleanupInventory();
        }

        private void InitializeInventoryUI()
        {
            _inventoryWindow = new Panel
            {
                AnchorPreset = AnchorPresets.MiddleCenter,
                Offsets = new Margin(-WindowWidth / 2, -WindowHeight / 2, -WindowWidth / 2, -WindowHeight / 2),
                Size = new Float2(WindowWidth, WindowHeight),
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f)
            };

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

        private void CreateTitleBar()
        {
            _titleBar = new Panel
            {
                Bounds = new Rectangle(0, 0, WindowWidth, 40),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 1.0f)
            };
            _inventoryWindow.AddChild(_titleBar);

            _titleLabel = new Label
            {
                Bounds = new Rectangle(10, 8, 200, 24),
                Text = "背包",
                TextColor = new Color(0.9f, 0.8f, 0.5f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            _titleBar.AddChild(_titleLabel);

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

        private void CreateFilterPanel()
        {
            _filterPanel = new Panel
            {
                Bounds = new Rectangle(0, 40, WindowWidth, 35),
                BackgroundColor = new Color(0.12f, 0.12f, 0.17f, 1.0f)
            };
            _inventoryWindow.AddChild(_filterPanel);

            CreateFilterButton("全部", 10, null);
            CreateFilterButton("材料", 70, MaterialTier.Basic);
            CreateFilterButton("装备", 130, null);
            CreateFilterButton("消耗品", 190, null);

            var sortBtn = new Button
            {
                Bounds = new Rectangle(WindowWidth - 120, 5, 50, 25),
                Text = "排序",
                TextColor = Color.White,
                BackgroundColor = new Color(0.2f, 0.3f, 0.2f, 0.8f)
            };
            sortBtn.ButtonClicked += (btn) => SortInventory();
            _filterPanel.AddChild(sortBtn);

            var searchBtn = new Button
            {
                Bounds = new Rectangle(WindowWidth - 65, 5, 50, 25),
                Text = "搜索",
                TextColor = Color.White,
                BackgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.8f)
            };
            searchBtn.ButtonClicked += (btn) => { Debug.Log("[InventoryUI] 搜索功能触发"); };
            _filterPanel.AddChild(searchBtn);
        }

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

        private void CreateSlotsContainer()
        {
            float containerHeight = WindowHeight - 40 - 35 - 40;

            _slotsContainer = new Panel
            {
                Bounds = new Rectangle(10, 75, WindowWidth - 20, containerHeight),
                BackgroundColor = Color.Transparent
            };
            _inventoryWindow.AddChild(_slotsContainer);

            int totalSlots = ColumnCount * RowCount;
            for (int i = 0; i < totalSlots; i++)
            {
                CreateSlot(i);
            }
        }

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

            var slotPanel = new SlotPanel
            {
                SlotIndex = index,
                Bounds = new Rectangle(xPos, yPos, SlotSize, SlotSize),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.9f)
            };

            slotPanel.SlotClicked = (slotIndex) => OnSlotClick(slotIndex);
            slotPanel.SlotDoubleClicked = (slotIndex) => OnSlotDoubleClick(slotIndex);
            slotPanel.SlotDragStarted = (slotIndex) => OnSlotDragStart(slotIndex);
            slotPanel.SlotDragEnded = (slotIndex) => OnSlotDragEnd(slotIndex);

            slotUI.SlotPanel = slotPanel;
            _slotsContainer.AddChild(slotPanel);

            slotUI.QualityBorder = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                BackgroundColor = Color.Transparent
            };
            slotUI.SlotPanel.AddChild(slotUI.QualityBorder);

            slotUI.IconImage = new Image
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(4, 4, 4, 4),
                Brush = new TextureBrush(),
                KeepAspectRatio = true,
                Color = Color.Gray
            };
            slotUI.SlotPanel.AddChild(slotUI.IconImage);

            slotUI.CountLabel = new Label
            {
                Bounds = new Rectangle(2, SlotSize - 18, SlotSize - 4, 16),
                Text = "",
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Far
            };
            slotUI.SlotPanel.AddChild(slotUI.CountLabel);

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

        private void CreateInfoPanel()
        {
            float yPos = WindowHeight - 40;

            var infoPanel = new Panel
            {
                Bounds = new Rectangle(0, yPos, WindowWidth, 40),
                BackgroundColor = new Color(0.12f, 0.12f, 0.17f, 1.0f)
            };
            _inventoryWindow.AddChild(infoPanel);

            _goldLabel = new Label
            {
                Bounds = new Rectangle(10, 10, 150, 20),
                Text = "金币: 1000",
                TextColor = new Color(1.0f, 0.9f, 0.3f),
                HorizontalAlignment = TextAlignment.Near
            };
            infoPanel.AddChild(_goldLabel);

            _capacityLabel = new Label
            {
                Bounds = new Rectangle(WindowWidth - 160, 10, 150, 20),
                Text = "容量: 0/48",
                TextColor = Color.LightGray,
                HorizontalAlignment = TextAlignment.Far
            };
            infoPanel.AddChild(_capacityLabel);
        }

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
            AddTestMaterials();
        }

        private void AddTestMaterials()
        {
            var ironOre = MaterialDatabase.GetMaterial(10001);
            if (ironOre != null) AddMaterial(ironOre, 25);

            var bamboo = MaterialDatabase.GetMaterial(10002);
            if (bamboo != null) AddMaterial(bamboo, 18);

            var water = MaterialDatabase.GetMaterial(10003);
            if (water != null) AddMaterial(water, 10);

            UpdateAllSlots();
        }

        public bool AddMaterial(MaterialData material, int count)
        {
            if (material == null || count <= 0) return false;

            var existingSlot = _inventorySlots.Find(s => s.Material?.MaterialId == material.MaterialId);
            if (existingSlot != null)
            {
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
                    int remaining = newCount - material.MaxStack;
                    existingSlot.Count = material.MaxStack;
                    UpdateSlot(existingSlot.SlotIndex);
                    return AddMaterial(material, remaining);
                }
            }

            var emptySlot = _inventorySlots.Find(s => s.Material == null);
            if (emptySlot != null)
            {
                emptySlot.Material = material;
                emptySlot.Count = Mathf.Min(count, material.MaxStack);
                UpdateSlot(emptySlot.SlotIndex);

                Debug.Log($"[InventoryUI] 添加材料: {material.MaterialName} × {count}（新槽位）");

                if (count > material.MaxStack)
                    return AddMaterial(material, count - material.MaxStack);

                return true;
            }

            Debug.LogWarning("[InventoryUI] 背包已满，无法添加材料");
            return false;
        }

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

        public int GetMaterialCount(int materialId)
        {
            int total = 0;
            foreach (var slot in _inventorySlots)
            {
                if (slot.Material?.MaterialId == materialId)
                    total += slot.Count;
            }
            return total;
        }

        private void UpdateSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slotUIs.Count) return;

            var slotUI = _slotUIs[slotIndex];
            var slotData = _inventorySlots[slotIndex];

            if (slotData.Material != null && slotData.Count > 0)
            {
                slotUI.IconImage.Color = slotData.Material.GetElementColor();
                slotUI.CountLabel.Text = slotData.Count.ToString();
                slotUI.QualityBorder.BackgroundColor = slotData.Material.GetQualityColor();

                var iconPath = slotData.Material.IconPath;
                if (!string.IsNullOrEmpty(iconPath))
                {
                    var texture = Content.Load<Texture>(iconPath);
                    if (texture != null)
                    {
                        slotUI.IconImage.Brush = new TextureBrush(texture);
                        slotUI.IconImage.Color = Color.White;
                    }
                }
            }
            else
            {
                slotUI.IconImage.Color = Color.Gray;
                slotUI.CountLabel.Text = "";
                slotUI.QualityBorder.BackgroundColor = Color.Transparent;
            }

            UpdateCapacityLabel();
        }

        private void UpdateAllSlots()
        {
            for (int i = 0; i < _inventorySlots.Count; i++)
            {
                UpdateSlot(i);
            }
        }

        private void UpdateCapacityLabel()
        {
            int usedSlots = _inventorySlots.FindAll(s => s.Material != null).Count;
            int totalSlots = _inventorySlots.Count;
            _capacityLabel.Text = $"容量: {usedSlots}/{totalSlots}";
        }

        private void UpdateGoldLabel()
        {
            _goldLabel.Text = $"金币: {_playerGold}";
        }

        private void OnSlotDoubleClick(int slotIndex)
        {
            var slot = _inventorySlots[slotIndex];
            if (slot.Material != null)
            {
                Debug.Log($"[InventoryUI] 双击槽位 {slotIndex}: {slot.Material.MaterialName} × {slot.Count}");
            }
        }

        private void OnSlotClick(int slotIndex)
        {
            if (_selectedSlotIndex >= 0 && _selectedSlotIndex < _slotUIs.Count)
            {
                _slotUIs[_selectedSlotIndex].SelectedOverlay.Visible = false;
                _slotUIs[_selectedSlotIndex].IsSelected = false;
            }

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

        private void SortInventory()
        {
            var filledSlots = new List<InventorySlot>();
            var emptySlots = new List<InventorySlot>();
            foreach (var slot in _inventorySlots)
            {
                if (slot.Material != null)
                    filledSlots.Add(slot);
                else
                    emptySlots.Add(slot);
            }

            filledSlots.Sort((a, b) =>
            {
                int nameCompare = string.Compare(a.Material.MaterialName, b.Material.MaterialName, StringComparison.CurrentCulture);
                if (nameCompare != 0) return nameCompare;
                return b.Count.CompareTo(a.Count);
            });

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

        private void OnSlotDragStart(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _inventorySlots.Count) return;

            var slot = _inventorySlots[slotIndex];
            if (slot.Material == null || slot.IsLocked) return;

            _isDragging = true;
            _dragSourceSlotIndex = slotIndex;

            _dragGhost = new Panel
            {
                Size = new Float2(SlotSize, SlotSize),
                BackgroundColor = new Color(0.3f, 0.3f, 0.4f, 0.7f)
            };

            var ghostIcon = new Image
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(4, 4, 4, 4),
                Color = _slotUIs[slotIndex].IconImage.Color,
                Brush = _slotUIs[slotIndex].IconImage.Brush,
                KeepAspectRatio = true
            };
            _dragGhost.AddChild(ghostIcon);

            _inventoryWindow.AddChild(_dragGhost);

            var sourceUI = _slotUIs[slotIndex];
            _dragGhost.Location = sourceUI.SlotPanel.Location;
            _slotUIs[slotIndex].SelectedOverlay.Visible = true;

            Debug.Log($"[InventoryUI] 开始拖拽: 槽位 {slotIndex}, 物品 {slot.Material.MaterialName}");
        }

        private void OnSlotDragEnd(int targetSlotIndex)
        {
            if (!_isDragging || _dragSourceSlotIndex < 0) return;
            if (targetSlotIndex < 0 || targetSlotIndex >= _inventorySlots.Count) return;
            if (_dragSourceSlotIndex == targetSlotIndex)
            {
                CancelDrag();
                return;
            }

            var sourceSlot = _inventorySlots[_dragSourceSlotIndex];
            var targetSlot = _inventorySlots[targetSlotIndex];

            if (targetSlot.IsLocked)
            {
                Debug.LogWarning("[InventoryUI] 目标槽位已锁定，无法放置");
                CancelDrag();
                return;
            }

            if (targetSlot.Material != null &&
                sourceSlot.Material != null &&
                targetSlot.Material.MaterialName == sourceSlot.Material.MaterialName)
            {
                targetSlot.Count += sourceSlot.Count;
                sourceSlot.Material = null;
                sourceSlot.Count = 0;
                Debug.Log($"[InventoryUI] 物品合并: 槽位 {_dragSourceSlotIndex} → {targetSlotIndex}");
            }
            else
            {
                SwapSlots(_dragSourceSlotIndex, targetSlotIndex);
                Debug.Log($"[InventoryUI] 物品交换: 槽位 {_dragSourceSlotIndex} ↔ {targetSlotIndex}");
            }

            UpdateSlot(_dragSourceSlotIndex);
            UpdateSlot(targetSlotIndex);
            CancelDrag();
        }

        private void SwapSlots(int sourceIndex, int targetIndex)
        {
            var tempMaterial = _inventorySlots[sourceIndex].Material;
            var tempCount = _inventorySlots[sourceIndex].Count;

            _inventorySlots[sourceIndex].Material = _inventorySlots[targetIndex].Material;
            _inventorySlots[sourceIndex].Count = _inventorySlots[targetIndex].Count;

            _inventorySlots[targetIndex].Material = tempMaterial;
            _inventorySlots[targetIndex].Count = tempCount;
        }

        private void CancelDrag()
        {
            if (_dragSourceSlotIndex >= 0 && _dragSourceSlotIndex < _slotUIs.Count)
            {
                _slotUIs[_dragSourceSlotIndex].SelectedOverlay.Visible = false;
            }

            if (_dragGhost != null)
            {
                if (_dragGhost.Parent != null)
                    _dragGhost.Parent.RemoveChild(_dragGhost);
                _dragGhost.Dispose();
                _dragGhost = null;
            }

            _isDragging = false;
            _dragSourceSlotIndex = -1;
        }

        public int BatchSellByFilter(string filterType)
        {
            int soldCount = 0;
            for (int i = 0; i < _inventorySlots.Count; i++)
            {
                var slot = _inventorySlots[i];
                if (slot.Material == null || slot.IsLocked) continue;

                bool matchesFilter = filterType == "全部" ||
                    (filterType == "材料" && slot.Material != null);

                if (matchesFilter)
                {
                    soldCount += slot.Count;
                    slot.Material = null;
                    slot.Count = 0;
                    UpdateSlot(i);
                }
            }

            if (soldCount > 0)
            {
                UpdateCapacityLabel();
                Debug.Log($"[InventoryUI] 批量出售完成: 售出 {soldCount} 件物品");
            }
            return soldCount;
        }

        public void BatchOrganize()
        {
            MergeSameItems();
            SortInventory();
            Debug.Log("[InventoryUI] 背包批量整理完成");
        }

        private void MergeSameItems()
        {
            var materialGroups = new Dictionary<string, List<int>>();
            for (int i = 0; i < _inventorySlots.Count; i++)
            {
                var slot = _inventorySlots[i];
                if (slot.Material == null) continue;
                if (!materialGroups.ContainsKey(slot.Material.MaterialName))
                {
                    materialGroups[slot.Material.MaterialName] = new List<int>();
                }
                materialGroups[slot.Material.MaterialName].Add(i);
            }

            foreach (var group in materialGroups)
            {
                if (group.Value.Count <= 1) continue;
                int primaryIndex = group.Value[0];
                for (int i = 1; i < group.Value.Count; i++)
                {
                    int secondaryIndex = group.Value[i];
                    _inventorySlots[primaryIndex].Count += _inventorySlots[secondaryIndex].Count;
                    _inventorySlots[secondaryIndex].Material = null;
                    _inventorySlots[secondaryIndex].Count = 0;
                }
            }
        }

        public void ToggleInventory()
        {
            if (_isVisible) HideInventory();
            else ShowInventory();
        }

        public void ShowInventory()
        {
            _inventoryWindow.Visible = true;
            _isVisible = true;
            UpdateAllSlots();
            UpdateGoldLabel();
            Debug.Log("[InventoryUI] 显示背包");
        }

        public void HideInventory()
        {
            _inventoryWindow.Visible = false;
            _isVisible = false;
            Debug.Log("[InventoryUI] 隐藏背包");
        }

        public bool IsVisible => _isVisible;

        public void ClearEmbeddedPanel(Panel container)
        {
            if (container == null) return;
            while (container.HasChildren)
            {
                container.RemoveChild(container.Children[0]);
            }
            container.ViewOffset = Float2.Zero;
        }

        public void PopulateEmbeddedPanel(Panel container, List<InventoryItemData> items, Action<int> onItemClick)
        {
            ClearEmbeddedPanel(container);
            if (container == null) return;

            container.ScrollBars = ScrollBars.Vertical;
            container.ClipChildren = true;

            var displayItems = items ?? new List<InventoryItemData>();
            int slotCount = Mathf.Max(displayItems.Count, EmbeddedColumns * 2);
            float slotTotal = EmbeddedSlotSize + EmbeddedSpacing;

            for (int i = 0; i < slotCount; i++)
            {
                var item = i < displayItems.Count ? displayItems[i] : null;
                int row = i / EmbeddedColumns;
                int col = i % EmbeddedColumns;
                float xPos = col * slotTotal + EmbeddedSpacing;
                float yPos = row * slotTotal + EmbeddedSpacing;

                // === 1. 外层金属边框 ===
                var slotPanel = new EmbeddedSlotPanel
                {
                    SlotIndex = i,
                    ItemId = item?.ItemId ?? 0,
                    Bounds = new Rectangle(xPos, yPos, EmbeddedSlotSize, EmbeddedSlotSize),
                    BackgroundColor = HundunWorld.Game.UI.StyleSystem.ChineseClassicalTheme.MetalBorderColor
                };
                slotPanel.SlotClicked = (itemId) => onItemClick?.Invoke(itemId);
                container.AddChild(slotPanel);

                // === 2. 内层凹陷石质背景 ===
                var insetBg = new Panel
                {
                    Bounds = new Rectangle(2f, 2f, EmbeddedSlotSize - 4f, EmbeddedSlotSize - 4f),
                    BackgroundColor = HundunWorld.Game.UI.StyleSystem.ChineseClassicalTheme.DarkStoneInsetColor
                };
                slotPanel.AddChild(insetBg);

                // === 3. 顶部金线（模拟金属反光） ===
                insetBg.AddChild(new Panel
                {
                    Bounds = new Rectangle(0, 0, EmbeddedSlotSize - 4f, 1f),
                    BackgroundColor = HundunWorld.Game.UI.StyleSystem.ChineseClassicalTheme.MetalBorderSoftHighlightColor
                });

                // === 4. 尝试加载图标 ===
                bool hasIcon = false;
                if (item != null)
                {
                    try
                    {
                        var equipment = EquipmentDatabase.GetEquipment(item.ItemId);
                        if (equipment != null && !string.IsNullOrEmpty(equipment.IconPath))
                        {
                            var texture = Content.Load<Texture>(equipment.IconPath);
                            if (texture != null)
                            {
                                var iconImage = new Image
                                {
                                    Bounds = new Rectangle(4f, 4f, EmbeddedSlotSize - 8f, EmbeddedSlotSize - 8f),
                                    Brush = new TextureBrush(texture),
                                    KeepAspectRatio = true,
                                    Color = Color.White
                                };
                                slotPanel.AddChild(iconImage);
                                hasIcon = true;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // 图标加载失败回退到占位文字
                    }
                }

                // === 5. 占位文字 ===
                if (!hasIcon)
                {
                    string placeholderText = item != null ? "物" : "空";
                    var placeholder = new Label
                    {
                        Bounds = new Rectangle(2f, 2f, EmbeddedSlotSize - 4f, EmbeddedSlotSize - 4f),
                        Text = placeholderText,
                        Font = UIHelper.SetFont(size: Mathf.Max(10f, EmbeddedSlotSize * 0.35f)),
                        TextColor = HundunWorld.Game.UI.StyleSystem.ChineseClassicalTheme.WowHintTextColor,
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center
                    };
                    slotPanel.AddChild(placeholder);
                }

                // === 6. 物品数量标签（数量 > 1 时显示） ===
                if (item != null && item.Count > 1)
                {
                    var countBg = new Panel
                    {
                        Bounds = new Rectangle(EmbeddedSlotSize - 22f, EmbeddedSlotSize - 14f, 18f, 10f),
                        BackgroundColor = HundunWorld.Game.UI.StyleSystem.ChineseClassicalTheme.DarkStoneBackgroundColor
                    };
                    slotPanel.AddChild(countBg);

                    var countLabel = new Label
                    {
                        Bounds = new Rectangle(EmbeddedSlotSize - 22f, EmbeddedSlotSize - 14f, 18f, 10f),
                        Text = item.Count.ToString(),
                        Font = UIHelper.SetFont(size: 10),
                        TextColor = HundunWorld.Game.UI.StyleSystem.ChineseClassicalTheme.WowNumberTextColor,
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center
                    };
                    slotPanel.AddChild(countLabel);
                }
            }
        }

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
    }
}
