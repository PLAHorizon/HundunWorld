# Checklist

- [ ] `InkEquipmentSlot.cs` 文件已创建于 `Source/Game/UI/Ink/Components/InkEquipmentSlot.cs`
- [ ] 类名 `InkEquipmentSlot`，命名空间 `HundunWorld.Game.UI.Ink.Components`，继承 `ContainerControl`
- [ ] using 语句包含 FlaxEngine / FlaxEngine.GUI / HundunWorld.Game.Equipment / HundunWorld.Game.UI / HundunWorld.Game.UI.StyleSystem / System / System.Collections.Generic
- [ ] 字段 `SlotType`(EquipmentSlot)、`CurrentEquipment`(EquipmentData)、`Icon`(SpriteHandle)、`EmptySlotIcon`(SpriteHandle) 均已声明
- [ ] `SlotNames` 静态字典包含 8 个槽位中文名（身体/头部/背部/右手/左手/腰部/面部/颈部）
- [ ] 构造函数设置默认尺寸 64×64、`BackgroundColor = InkWashTheme.BaseTertiary`、`ClipChildren = true`
- [ ] Draw 方法用 try-catch 包裹
- [ ] 装备态绘制：图标填充 80% 区域 + 2px 品质色边框 + 右下角 ItemLevel 标签
- [ ] 空槽态绘制：1px 暗色边框 + 中心槽位中文名（+ 可选空槽图标 50%）
- [ ] 悬停时边框高亮为 `InkWashTheme.GoldBright`
- [ ] 品质映射使用 `Mathf.Clamp(quality, 0, 4)` 转 InkQuality
- [ ] OnMouseDown/OnMouseUp 双击检测间隔 < 500ms，使用 `Time.UnscaledGameTime`
- [ ] OnMouseEnter 触发 `Hovered(SlotType, location)`，OnMouseLeave 触发 `HoverEnded(SlotType)`
- [ ] 事件签名：`DoubleClicked(SlotType, CurrentEquipment)` / `Hovered(SlotType, Float2)` / `HoverEnded(SlotType)`
- [ ] `Refresh(EquipmentData)` 公共方法已实现
- [ ] `Source/Game.csproj` 已追加 `<Compile Include>` 条目，路径与现有 Components 文件一致
- [ ] 编译通过，0 错误
- [ ] 代码风格遵循项目既有 XML 文档注释约定（参考 InkButtons.cs / InkCells.cs）
