# Checklist

- [x] `CharacterAttributes.cs` 已创建，包含基础属性与五行属性数据结构
- [x] `CharacterPersistenceService` 支持读写角色五行属性与背包列表
- [x] `EquipmentDatabase` 中的装备数据包含图标与属性加成字段
- [x] `WuxingRadarChart.cs` 已创建，能正确绘制五行六边形雷达图并显示数值标签
- [x] `EquipmentSlotView.cs` 已创建，支持显示空槽/已装备状态与点击事件
- [x] `InventoryUI.PopulateEmbeddedPanel` 已实现，可在角色面板中内嵌显示可点击的背包格子
- [x] `EquipmentComparisonUI.PopulateEmbeddedPreview` 已实现，支持在角色面板内显示装备对比与穿戴/卸下按钮
- [x] `GameMainUI.PopulateCharacterPanel` 已重构为三栏布局，集成属性、装备插槽、装备预览、背包、五行雷达图
- [x] 角色面板打开时自动从 `CharacterPersistenceService`/`EquipmentDatabase` 读取最新数据并刷新
- [x] 穿戴/卸下装备后，装备插槽、背包、五行雷达图能及时刷新
- [x] 项目编译通过，0 错误
- [x] 在 Flax 编辑器中进入 World 场景并打开角色面板，所有区域正常显示且中文无乱码
