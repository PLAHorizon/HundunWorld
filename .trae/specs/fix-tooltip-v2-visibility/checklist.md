# Checklist

- [x] `InkAttributeTooltip.cs` 中不再有 `public new bool Visible` 属性和 `_visible` 字段
- [x] `InkAttributeTooltip.Draw` 中使用基类 `Visible` 而非 `_visible` 判断
- [x] `InkAttributeTooltip.Show` 中 `Visible = true` 后调用 `BringToFront()` 确保置顶
- [x] 边界溢出检测统一使用屏幕坐标（`Screen.Size`）判断
- [x] 溢出后偏移量基于父控件本地坐标系计算
- [x] `InkEquipmentSlot.OnMouseEnter` 添加 Debug 诊断日志
- [x] `OnEquipmentSlotHovered` 添加 Debug 诊断日志
- [x] 四角 L 装饰尺寸 18px、opacity 0.6（对齐 popup-verification.html）
- [x] 装饰带线条 max-width 120px、整体 opacity 0.7
- [x] band-dot 4×4px 圆点
- [x] 阴影参数对齐纸色面板阴影（柔和 0.12 alpha）
- [x] 编译通过，0 错误 0 新增警告
