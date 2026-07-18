# Checklist

## ECS 数据层扩展
- [x] `EquippedBag` 结构体已创建（含 BagSlotIndex、TemplateId、ExtraSlots）
- [x] `InventoryComponent` 新增 `BagSlots` 列表
- [x] `InventoryComponent.BaseCapacity` 常量为 36
- [x] `InventoryComponent.TotalCapacity` 计算属性正确（36 + Σ 已装备背包 ExtraSlots）
- [x] `InventoryComponent.IsFull` 使用 `TotalCapacity`
- [x] `InventoryComponent.TryAddItem` 使用 `TotalCapacity` 判断容量
- [x] `InventorySystem.EquipBag` 实现并校验槽位范围/空槽/背包类型/最大容量
- [x] `InventorySystem.UnequipBag` 实现并校验非空/扩展格无物品
- [x] `InventorySystem.GetTotalCapacity` 实现

## 装备数据支持背包类型
- [x] `EquipmentType.Bag` 枚举值已添加
- [x] `EquipmentData.ExtraSlots` 字段已添加
- [x] `EquipmentDatabase` 包含至少 3 个 mock 背包物品（2001-2004，ExtraSlots 6/12/18/20）

## 背包 UI 控件重构
- [x] `InkBackpackGrid.DefaultColumns` 改为 6
- [x] `InkBackpackGrid.DefaultCapacity` 改为 36
- [x] `InkBackpackGrid` 新增 4 个背包槽显示
- [x] 背包槽支持空槽提示、双击事件、Tooltip
- [x] `InkBackpackGrid.Populate` 支持传入已装备背包数据并计算总容量
- [x] 默认格子与扩展格子视觉区分
- [x] 扩展格子按 6 列网格正确布局

## 角色属性页布局重组 — 红蓝框互换
- [x] `LayoutLeftPanel` 顺序：角色信息区 → 战力区 → 基础属性 → 进阶属性 → 雷达图
- [x] 红框（战力）与蓝框（角色信息）视觉元素保留
- [x] 左侧面板不放置 3D 预览

## 角色属性页布局重组 — 中间面板 3D 预览与装备槽合并
- [x] `_preview3D` 仍在 `_centerPanel`，显示真正的角色 3D 实时预览（非黑屏）
- [x] 中间面板同时显示 3D 预览与装备槽
- [x] 3D 预览居中显示
- [x] 装备槽布局在 3D 预览下方，不遮挡 3D 预览渲染区域
- [x] 装备槽采用紧凑 3×5 网格布局（非纸娃娃人体拓扑）
- [x] 装备槽按类型分组排列
- [x] 15 个装备槽全部可见且可交互
- [x] `_preview3D.SetCharacter` 调用不受影响

## 背包区接入魔兽世界式系统
- [x] `MenuCharAttributesV2Page` 维护 `_equippedBags` 数据
- [x] 背包区默认显示 6×6 = 36 格
- [x] 背包区显示 4 个背包槽
- [x] 双击背包槽可卸下背包
- [x] 双击背包中的背包物品可装备到空背包槽
- [x] 装备背包后扩展格子实时增加
- [x] 卸下背包时扩展格子为空才允许卸下

## 编译与回归
- [x] `dotnet build -c Editor.Windows.Development -p:Platform=x64 -t:Rebuild` 0 C# 错误
- [x] `Game.CSharp.dll` 已重新生成（2358.5 KB）
- [x] Tooltip 显示功能不受影响
- [x] 装备双击换装功能不受影响
- [x] 雷达图 hover Tooltip 功能不受影响
- [x] V5 美化嵌套类保留（9 个全部存在）
- [x] 3D 预览拖拽旋转保留
