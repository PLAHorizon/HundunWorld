# Checklist

- [x] `MainUIManager.cs` 中不再注册 `MenuEquipmentPage`，`CreateMenuEquipment` 方法已移除
- [x] `MainUIManager.cs` 中不再注册 `MenuCharAttributesPage`（V1），`CreateMenuCharAttributes` 方法已移除
- [x] 全工程无指向旧装备页/旧属性页 dom-id 的导航调用（CombatHudPage.cs 的"装备"入口已重定向到 nav-character-v2）
- [x] `InkAttributeTooltip.Show` 使用 `Parent.PointToScreen(Float2.Zero)` 坐标转换，Tooltip 左上角精确位于鼠标屏幕坐标右下方 10 像素处
- [x] 边界溢出检测基于 `Screen.Size`，Tooltip 在屏幕右下角自动向左/向上偏移
- [x] 所有 `Show` 调用均传入 `FlaxEngine.Input.MouseScreenPosition`（屏幕坐标）
- [x] 鼠标悬停已装备槽位时 Tooltip 显示装备详情（品质色边框）
- [x] 鼠标悬停空装备槽时 Tooltip 显示槽位名称 + "未装备" + 可装备物品类型
- [x] 鼠标悬停已存放背包格子时 Tooltip 显示装备详情（品质色边框）
- [x] 鼠标悬停空背包格子时 Tooltip 显示"空格子" + "未存放物品"
- [x] 鼠标离开悬停目标时 Tooltip 立即隐藏（无定时器延迟）
- [x] Tooltip 视觉采用纸色卷轴面板风格（纸色背景 + 四角 L 装饰 + 顶/底装饰带 + 品质色边框）
- [x] 装备 Tooltip 边框按品质色显示（普通灰/优秀绿/精良蓝/史诗紫/传说朱红）
- [x] 非装备 Tooltip 边框为默认金色
- [x] 编译通过，0 错误 0 警告（无新增警告）
