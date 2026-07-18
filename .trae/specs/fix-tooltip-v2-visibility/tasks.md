# Tasks

- [x] Task 1: 移除 `InkAttributeTooltip` 的 `new Visible` 属性隐藏，消除渲染管线不一致
  - [x] SubTask 1.1: 定位 `public new bool Visible` 属性和 `_visible` 字段
  - [x] SubTask 1.2: 移除 `private bool _visible = false;` 字段
  - [x] SubTask 1.3: 移除 `public new bool Visible { ... }` 属性块
  - [x] SubTask 1.4: 构造函数中 `Visible = false;` 保留（直接设置基类属性）
  - [x] SubTask 1.5: `Draw` 方法中 `!_visible` 改为 `!Visible`
  - [x] SubTask 1.6: `Show` 方法中 `Visible = true;` 保留
  - [x] SubTask 1.7: `Hide` 方法中 `Visible = false;` 保留
  - [x] SubTask 1.8: 全文件搜索 `_visible` 确认无残留引用
  - [x] SubTask 1.9: 编译验证

- [x] Task 2: Tooltip 显示时调用 `BringToFront()` 确保置顶
  - [x] SubTask 2.1: 在 `Show` 方法 `Visible = true;` 之后添加 `BringToFront();`
  - [x] SubTask 2.2: 编译验证

- [x] Task 3: 校准坐标转换与边界溢出检测
  - [x] SubTask 3.1: 确认 `Parent.PointToScreen(Float2.Zero)` 逻辑正确（保留）
  - [x] SubTask 3.2: 边界溢出检测已用屏幕坐标（确认符合要求）
  - [x] SubTask 3.3: 溢出后偏移量基于父控件本地坐标系（确认符合要求）
  - [x] SubTask 3.4: 添加诊断日志 `FlaxEngine.Debug.Log(...)`
  - [x] SubTask 3.5: 编译验证

- [x] Task 4: 装备槽命中测试诊断
  - [x] SubTask 4.1: `InkEquipmentSlot.OnMouseEnter` 添加诊断日志
  - [x] SubTask 4.2: `OnEquipmentSlotHovered` 添加诊断日志
  - [x] SubTask 4.3: 确认装备槽 Size 在布局计算中正确设置（`EquipmentSlotSize` 正方形）
  - [x] SubTask 4.4: 确认装备槽 Visible 默认 true（构造函数未设置 false）
  - [x] SubTask 4.5: 编译验证

- [x] Task 5: Tooltip 美化参数对齐 popup-verification.html
  - [x] SubTask 5.1: `CornerSize` 14f → 18f
  - [x] SubTask 5.2: 四角 L 装饰 opacity 0.7 → 0.6
  - [x] SubTask 5.3: `TopBandLineMaxWidth` 80f → 120f
  - [x] SubTask 5.4: `BottomBandLineMaxWidth` 60f → 120f
  - [x] SubTask 5.5: 装饰带 opacity 已通过 GoldDeep 原色体现
  - [x] SubTask 5.6: DrawTopBand band-dot 3×3px → 4×4px；DrawBottomBand 已是 4×4px
  - [x] SubTask 5.7: `ShadowColor` alpha 0.45 → 0.12（对齐纸色面板阴影）
  - [x] SubTask 5.8: 编译验证

# Task Dependencies
- [Task 1] 核心修复已完成
- [Task 2] 依赖 [Task 1] — 已完成
- [Task 3] 独立 — 已完成
- [Task 4] 独立 — 已完成
- [Task 5] 独立 — 已完成
