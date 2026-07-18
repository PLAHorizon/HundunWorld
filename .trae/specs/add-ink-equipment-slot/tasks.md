# Tasks

- [ ] Task 1: 创建 InkEquipmentSlot 控件文件
  - 新建文件 `Source/Game/UI/Ink/Components/InkEquipmentSlot.cs`
  - 命名空间 `HundunWorld.Game.UI.Ink.Components`，类继承 `FlaxEngine.GUI.ContainerControl`
  - using 语句：`FlaxEngine`、`FlaxEngine.GUI`、`HundunWorld.Game.Equipment`、`HundunWorld.Game.UI`、`HundunWorld.Game.UI.StyleSystem`、`System`、`System.Collections.Generic`
  - 字段：`SlotType`(EquipmentSlot)、`CurrentEquipment`(EquipmentData)、`Icon`(SpriteHandle)、`EmptySlotIcon`(SpriteHandle)
  - 私有字段：`_lastClickTime`(float)、`_isHovered`(bool)、`_borderColor`(Color)
  - 静态字典 `SlotNames`：8 个 EquipmentSlot 枚举值到中文名映射（身体/头部/背部/右手/左手/腰部/面部/颈部）
  - 默认尺寸 64×64，构造函数初始化 `BackgroundColor = InkWashTheme.BaseTertiary`、`ClipChildren = true`
  - **验证**: 文件创建成功，类结构完整

- [ ] Task 2: 实现 Draw 绘制方法
  - 覆写 `Draw()`，整体用 `try-catch` 包裹
  - 先调用 `base.Draw()` 绘制背景与子控件
  - 装备态（CurrentEquipment != null）：用 `Render2D.DrawSprite` 绘制 Icon 填充 80% 内部区域（中心对齐），绘制 2px 品质色边框（`InkWashTheme.QualityColor(映射InkQuality)`，悬停时改为 `GoldBright`），右下角绘制 `ItemLevel` 文本（`InkRenderHelper.GetFontRef(Heading, 11)`）
  - 空槽态（CurrentEquipment == null）：绘制 1px 暗色边框（`InkWashTheme.BorderNeutralL3`，悬停时改为 `GoldBright`），中心绘制 `SlotNames[SlotType]` 中文名（`TextTertiary` 色，`GetFontRef(Heading, 12)`，居中），若 `EmptySlotIcon.IsValid` 则绘制空槽图标（50% 区域）
  - 品质映射辅助方法 `MapQuality(int)`：`Mathf.Clamp(quality, 0, 4)` 后转 `InkQuality`
  - **验证**: 装备态与空槽态绘制逻辑符合 spec

- [ ] Task 3: 实现鼠标交互与事件
  - 覆写 `OnMouseDown(Float2, MouseButton)`：左键时记录点击时间，调用 base，返回 true
  - 覆写 `OnMouseUp(Float2, MouseButton)`：左键释放时若 `Time.UnscaledGameTime - _lastClickTime < 0.5f` 触发 `DoubleClicked(SlotType, CurrentEquipment)`；更新 `_lastClickTime`；返回 true
  - 覆写 `OnMouseEnter(Float2)`：设置 `_isHovered=true`，触发 `Hovered(SlotType, location)`
  - 覆写 `OnMouseLeave()`：设置 `_isHovered=false`，触发 `HoverEnded(SlotType)`
  - 事件声明：`event Action<EquipmentSlot, EquipmentData> DoubleClicked`、`event Action<EquipmentSlot, Float2> Hovered`、`event Action<EquipmentSlot> HoverEnded`
  - **验证**: 双击阈值 500ms、事件签名符合 spec

- [ ] Task 4: 实现 Refresh 接口与 csproj 注册
  - `public void Refresh(EquipmentData equipment)`：赋值 `CurrentEquipment`，无显式重绘调用（依赖宿主布局刷新）
  - 在 `Source/Game.csproj` 的 `<ItemGroup>` 中追加 `<Compile Include="Source\Game\UI\Ink\Components\InkEquipmentSlot.cs" />`
  - **验证**: Refresh 签名正确，csproj 条目路径与现有 Components 文件一致

- [ ] Task 5: 编译验证
  - 关闭 Flax Editor（避免锁定 Game.CSharp.dll）
  - 执行 `dotnet build Source/Game.csproj`（或 Flax.Build），确保 0 错误
  - **验证**: 编译通过，无报错

# Task Dependencies
- [Task 2] 依赖 [Task 1]（字段已定义）
- [Task 3] 依赖 [Task 1]
- [Task 4] 依赖 [Task 1]
- [Task 5] 依赖 [Task 2, Task 3, Task 4]
