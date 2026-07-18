# Tasks

- [x] Task 1: ECS 数据层扩展 — InventoryComponent 与 InventorySystem 支持背包槽
  - [x] SubTask 1.1: 读取 `InventoryComponent.cs` 全文
  - [x] SubTask 1.2: 新增 `EquippedBag` 结构体（BagSlotIndex, TemplateId, ExtraSlots）
  - [x] SubTask 1.3: 新增 `BagSlots` 列表、`BaseCapacity`(36)、`TotalCapacity` 属性
  - [x] SubTask 1.4: 修改 `TryAddItem` 使用 `TotalCapacity`
  - [x] SubTask 1.5: 修改 `IsFull` 使用 `TotalCapacity`
  - [x] SubTask 1.6: 读取 `InventorySystem.cs` 全文
  - [x] SubTask 1.7: 新增 `EquipBag`、`UnequipBag`、`GetTotalCapacity` 方法
  - [x] SubTask 1.8: `EquipBag` 校验逻辑实现
  - [x] SubTask 1.9: `UnequipBag` 校验逻辑实现
  - [x] SubTask 1.10: 编译验证 0 C# 错误

- [x] Task 2: 装备数据支持背包类型
  - [x] SubTask 2.1: 读取 `EquipmentData.cs` 全文
  - [x] SubTask 2.2: `EquipmentType` 枚举新增 `Bag`
  - [x] SubTask 2.3: `EquipmentData` 新增 `ExtraSlots` 字段
  - [x] SubTask 2.4: 读取 `EquipmentDatabase.cs` 全文
  - [x] SubTask 2.5: 新增 4 个 mock 背包物品（2001-2004，ExtraSlots 6/12/18/20）
  - [x] SubTask 2.6: 编译验证 0 C# 错误

- [x] Task 3: 背包 UI 控件重构 — InkBackpackGrid 支持 6×6 默认格与背包槽
  - [x] SubTask 3.1: 读取 `InkBackpackGrid.cs` 全文
  - [x] SubTask 3.2: `DefaultColumns`=6，`DefaultCapacity`=36
  - [x] SubTask 3.3: 新增 `InkBagSlot` 嵌套类
  - [x] SubTask 3.4: 新增 `BagSlots` 列表（4个）和 `BagSlotDoubleClicked`/`BagSlotHovered`/`BagSlotHoverEnded` 事件
  - [x] SubTask 3.5: `Populate` 新增重载支持 `List<EquippedBag>`
  - [x] SubTask 3.6: `ApplyLayout` 先布局背包槽再布局普通格子
  - [x] SubTask 3.7: 扩展格子与默认格子视觉区分
  - [x] SubTask 3.8: 编译验证 0 C# 错误

- [x] Task 4: 角色属性页布局重组 — 红蓝框互换、中间面板 3D 预览与装备槽合并布局
  - [x] SubTask 4.1: 读取相关 Layout 方法
  - [x] SubTask 4.2: 红蓝框互换 — `LayoutLeftPanel` 角色信息区 → 战力区
  - [x] SubTask 4.3: 确认 `_preview3D` 仍在 `_centerPanel`
  - [x] SubTask 4.4: `LayoutCenterPanel` 3D 预览 + 装备槽合并布局
  - [x] SubTask 4.5: `LayoutEquipmentSlots` 从纸娃娃改为 3×5 紧凑网格
  - [x] SubTask 4.6: 面板宽度比例保持 0.32/0.44/0.24（无需调整）
  - [x] SubTask 4.7: `BindCharacter` 中 `_preview3D.SetCharacter` 不受影响
  - [x] SubTask 4.8: 编译验证 0 C# 错误

- [x] Task 5: 角色属性页背包区接入魔兽世界式背包
  - [x] SubTask 5.1: 新增 `_equippedBags` 字段和 `UpdateBagSlots` 方法
  - [x] SubTask 5.2: `BuildBackpackGrid` 订阅背包槽事件
  - [x] SubTask 5.3: 订阅 `BagSlotDoubleClicked` 事件
  - [x] SubTask 5.4: `OnBackpackCellDoubleClicked` 背包类型分支装备到空背包槽
  - [x] SubTask 5.5: mock 数据默认装备 1 个背包（2001 小包 6 格）
  - [x] SubTask 5.6: 编译验证 0 C# 错误

- [x] Task 6: 整体编译验证与回归检查
  - [x] SubTask 6.1: 终止 FlaxEditor 进程
  - [x] SubTask 6.2: 执行 `dotnet build -t:Rebuild` 0 C# 错误
  - [x] SubTask 6.3: 确认 0 C# 错误（96 个预存在警告）
  - [x] SubTask 6.4: `Game.CSharp.dll` 已重新生成（2358.5 KB）
  - [x] SubTask 6.5: 代码审查确认无回归（30+ 项全部通过）

# Task Dependencies

- [Task 1] 与 [Task 2] 可并行（数据层独立）
- [Task 3] 依赖 [Task 2]（需要背包类型数据定义）
- [Task 4] 与 [Task 3] 可并行（UI 布局与背包 UI 独立）
- [Task 5] 依赖 [Task 1]、[Task 2]、[Task 3]
- [Task 6] 依赖 [Task 1-5] 全部完成
