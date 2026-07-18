# Tasks

- [x] Task 1: 废弃冗余页面注册，统一角色菜单入口
  - [x] SubTask 1.1: 在 `MainUIManager.cs` 中移除 `MenuEquipmentPage` 的页面注册（`RegisterPage` 调用）与 `CreateMenuEquipment` 方法
  - [x] SubTask 1.2: 在 `MainUIManager.cs` 中移除 `MenuCharAttributesPage`（V1）的页面注册与 `CreateMenuCharAttributes` 方法
  - [x] SubTask 1.3: 搜索全工程是否存在指向旧装备页/旧属性页 dom-id 的导航调用，若有则重定向到 `NavCharacterV2`
  - [x] SubTask 1.4: 编译验证无错误

- [x] Task 2: 系统性修复 Tooltip 坐标转换，确保精确跟随鼠标
  - [x] SubTask 2.1: 诊断 `InkAttributeTooltip.Show` 当前坐标转换缺陷 — 边界溢出检测误用 Parent.Width/Height 导致 Tooltip 被判溢出偏移到不可见区域
  - [x] SubTask 2.2: 重构 `Show(Float2 screenPosition)` — 改用 `Parent.PointToScreen(Float2.Zero)` 获取父控件原点屏幕坐标，`Tooltip本地坐标 = screenPosition - parentScreenOrigin + ShowOffset`
  - [x] SubTask 2.3: 边界溢出检测改为基于屏幕尺寸（`Screen.Size`）而非父控件尺寸
  - [x] SubTask 2.4: 在 `MenuCharAttributesV2Page` 中验证所有 8 处 `Show` 调用均传入 `FlaxEngine.Input.MouseScreenPosition`（屏幕坐标）
  - [x] SubTask 2.5: 编译并验证 Tooltip 左上角精确位于鼠标右下方 10 像素

- [x] Task 3: 修复装备槽与背包格子 Tooltip 不触发问题
  - [x] SubTask 3.1: 诊断事件传递链路 — 事件绑定齐全，z-order 正确，装饰控件 Enabled=false 生效
  - [x] SubTask 3.2: 确认装饰控件不覆盖装备槽（装备槽 AddChild 在装饰控件之后）
  - [x] SubTask 3.3: 确认 `InkEquipmentSlot.Hovered` 已绑定到 `OnEquipmentSlotHovered`，空槽分支调用 `_tooltip.Show`
  - [x] SubTask 3.4: 修复 `InkBackpackCell.OnMouseEnter` 事件参数 — 从传局部坐标改为传 `FlaxEngine.Input.MouseScreenPosition`，统一 API 契约
  - [x] SubTask 3.5: 确认所有悬停目标的 `HoverEnded`/`MouseLeave`/`TooltipEnded` 事件均绑定到 `OnAttributeHoverEnded`
  - [x] SubTask 3.6: 不触发现象由 Task 2 坐标转换 bug 导致，修复后恢复正常

- [x] Task 4: Tooltip 视觉美化（参考 popup-verification.html）
  - [x] SubTask 4.1: 确认 `InkAttributeTooltip.Draw` 已实现纸色背景 + 内描边 + 外边框 + 四角 L 装饰 + 顶/底装饰带 + 纸色质感 + 头部分隔线 + 阴影
  - [x] SubTask 4.2: 确认 `SetQualityBorderColor` 在装备 Tooltip 调用时传入品质色，非装备传入 null
  - [x] SubTask 4.3: 确认文字色适配纸色背景（`TextOnPaper`/`PaperDark`）
  - [x] SubTask 4.4: 视觉元素均已存在，无需修改

# Task Dependencies
- [Task 2] 和 [Task 3] 已并行完成
- [Task 1] 已独立完成
- [Task 4] 依赖 [Task 2] 完成 — 已确认视觉元素完整
