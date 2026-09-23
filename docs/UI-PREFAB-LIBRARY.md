# UI Prefab 组件库

> 将原本完全依靠 C# 代码构建的常用 UI 组件（按钮、面板、动画效果等）预制为 Flax Prefab 资产，
> 提供属性（Inspector 配置）与事件（声明式绑定）接口，支持编辑器复用与代码一行实例化。
>
> 视觉规范以《燕云融合 UI 设计方案》（`hundun-yy-ui/`，colors_and_type.css）为准：
> 深墨黑沉浸底 + 鎏金主 CTA + 朱红战斗/传世强调 + 纸色卷轴面板，古风克制圆角（≤8px），控件高度 28/36/44。

## 1. 架构总览

Flax 的 Prefab 是 Actor 资产，而项目 UI 组件全部是 `Control` 子类，桥接采用三段式：

```
Prefab 根 = UIControl Actor（ControlType = 自定义控件，控件属性随 Prefab 原生序列化）
        + UIComponentProfile 子类脚本（Inspector 参数 → 应用到控件，编辑器内实时刷新）
        + UIEventBinder 脚本（声明式事件绑定：事件名 + 动作 + 回调参数）
```

代码位置：`HundunWorld/Source/Game/UI/Prefabs/`

| 文件 | 职责 |
|---|---|
| `UIComponentProfile.cs` | 参数档案基类：绑定/自动创建宿主控件、登记注册名、编辑器轮询刷新参数 |
| `UIEventBinder.cs` | 声明式事件绑定器：Inspector 配置「控件名+事件名+动作」，运行时反射订阅 |
| `UIPrefabUtils.cs` | 主题令牌解析（`InkWashTheme` 反射）、锚点应用、反射属性写入 |
| `HundunUIPrefab.cs` | 代码侧入口：`Spawn` / `SpawnProfiled<T>` / `FindProfile<T>` / `HookClicked` |
| `InkButtonProfile.cs` | InkButton / InkTag / Label 参数档案 |
| `PanelProfiles.cs` | 圆角面板 / 进度条 / Toast / 确认框 / 加载器 / 输入框参数档案 |
| `EffectProfiles.cs` | 波纹（RippleEffectProfile）与通用动画（UIAnimationProfile）参数档案 |
| `GenericControlProfile.cs` | 通用档案：按类型名实例化任意控件 + 「属性名=值」反射配置（中低频组件用） |
| `YyProfiles.cs` | 燕云融合组件族档案：InkPanel / 纸色卷轴 / 标题栏 / 金角 / 格子 / 列表项 / 头像 / 状态点 / 竖排标题 / 渐变进度条 |
| `PrefabSmokeTest.cs` | 冒烟测试（仅挂在测试场景 `Content/Maps/UI/PrefabGalleryTest.scene`） |

配套编辑器工具（`Plugins/HundunAgent`）：`ui_prefab_build`、`ui_prefab_batch`、`ui_script_add`、`ui_script_set`，
并扩展了 `ui_control_create` 的控件类型解析（支持项目自定义控件，如 `InkButton`）。

### 关键机制

- **控件名解析**：Flax `Control` 无稳定名字来源，`UIComponentProfile` 绑定控件时以
  `ControlName`（留空取宿主 Actor 名）注册到 `UIPrefabRegistry`；`UIEventBinder` 与
  `UIAnimationProfile` 按该名字解析目标控件。
- **主题令牌**：所有颜色参数一律填 `InkWashTheme` 的字段名（如 `GoldPrimary`、`Panel`、
  `TextPrimary`），由 `UIPrefabUtils.TryResolveColorToken` 解析，禁止在 Inspector/代码里硬编码色值。
- **自愈式创建**：Prefab 实例若无序列化控件，Profile 会按 `CreateDefaultControl()` 自动补建，
  因此 Prefab 在「编辑器创建」「运行时 Spawn」「纯代码 Spawn」三条路径下都能还原。
- **编辑器实时预览参数**：Profile 带 `[ExecuteInEditMode]`，编辑器模式按 `EditorRefreshInterval`
  （默认 0.2s）轮询把 Inspector 改动应用到控件（PIE 中即时生效）。

## 2. Prefab 清单（Content/Prefabs/UI/，共 52 个）

### 按钮类（燕云融合配色：Primary=鎏金、Secondary=深墨金线、Vermilion=朱红战斗、Brand=亮鎏金；高度 28/36/44）
| Prefab | 控件 | Profile | 可用事件 |
|---|---|---|---|
| `Btn_InkButton` | InkButton | InkButtonProfile | `Clicked`、`ButtonClicked` |
| `Btn_Back` | InkBackButton | GenericControlProfile | `Clicked` |
| `Btn_Tab` | InkTabButton | GenericControlProfile | `Clicked` |
| `Btn_NextStep` | NextStepButton | GenericControlProfile | `Clicked` |

### 容器 / 卡片类
| Prefab | 控件 | Profile |
|---|---|---|
| `Panel_Rounded` | RoundedPanel | RoundedPanelProfile |
| `Panel_FrostedGlass` | FrostedGlassPanel | GenericControlProfile |
| `Panel_InkWashBackground` | InkWashBackground | GenericControlProfile |
| `Card_Kungfu` | KungfuCard | GenericControlProfile（完整卡面需代码调 `Initialize()`） |
| `Card_FacePreset` | FacePresetCard | GenericControlProfile |
| `Slot_Equipment` | EquipmentSlotView | GenericControlProfile |
| `Slot_InkEquipment` | InkEquipmentSlot | GenericControlProfile |

### 交互类
| Prefab | 控件 | Profile | 可用事件 |
|---|---|---|---|
| `Input_ValidatedTextBox` | ValidatedTextBox | TextBoxProfile | `TextChanged`、`ValidationChanged` |
| `Input_ParameterSliderGroup` | ParameterSliderGroup | GenericControlProfile | — |
| `Dialog_Confirm` | ConfirmDialog | ConfirmDialogProfile | `Confirmed`、`Cancelled` |
| `Toast_Notification` | ToastNotification | ToastProfile | —（代码调 `Show()`） |
| `Tip_AttributeTooltip` | InkAttributeTooltip | GenericControlProfile | — |
| `Indicator_Loading` | LoadingIndicator | LoadingIndicatorProfile | —（`Show()/Hide()`） |

### 显示 / 布局类
| Prefab | 控件 | Profile |
|---|---|---|
| `Tag_Ink` | InkTag | InkTagProfile |
| `Progress_RoundedBar` | RoundedProgressBar | ProgressBarProfile |
| `Chart_WuxingRadar` | WuxingRadarChart | GenericControlProfile |
| `Deco_ChineseInk` | ChineseInkDecoration | GenericControlProfile |
| `Grid_Backpack` | InkBackpackGrid | GenericControlProfile |
| `Grid_SkillSlots` | InkSkillSlotGrid | GenericControlProfile |
| `Layout_CategorySidebar` | CategorySidebar | GenericControlProfile |
| `Layout_SubCategoryPanel` | SubCategoryPanel | GenericControlProfile |
| `Layout_BottomActionBar` | BottomActionBar | GenericControlProfile |
| `Map_InkMinimap` | InkMinimap | GenericControlProfile |
| `Clock_InkDial` | InkDialClock | GenericControlProfile |
| `Diagram_Meridian` | InkMeridianDiagram | GenericControlProfile |
| `Bubble_ToolBar` | InkToolBarBubble | GenericControlProfile |

### 动画效果类（FX）
| Prefab | 用途 | Profile / 属性 |
|---|---|---|
| `FX_Ripple` | 点击波纹（可叠加到任意按钮下） | RippleEffectProfile：`Duration`、`MaxRadius`、`ColorToken`、`TriggerOnHostClick` |
| `FX_Transition` | 通用转场动画（淡入/淡出/滑入/滑出/缩放/抖动/弹跳） | UIAnimationProfile：`Kind`、`Duration`、`Easing`、`Trigger(OnLoad/OnClick/Manual)`、`LoopInterval`、`SlideOffset`、`TargetControlName` |
| `FX_PulseDot` | 呼吸圆点 | GenericControlProfile |
| `FX_FloatParticle` | 漂浮粒子 | GenericControlProfile |
| `FX_Underline` | 流动下划线 | GenericControlProfile |

### 燕云融合面板/装饰类（.ink-panel 族，对应 hundun-yy-ui 设计包）
| Prefab | 控件 | Profile | 关键属性 |
|---|---|---|---|
| `Panel_Ink` | InkPanel | InkPanelProfile | `Radius`(4)、`Variant`(Default/Lightweight) |
| `Panel_InkLightweight` | InkPanel | InkPanelProfile | 半透变体（HUD 场景） |
| `Panel_InkSolid` | InkPanelSolid | GenericControlProfile | 纯色面板 |
| `Panel_InkElevated` | InkPanelElevated | GenericControlProfile | 浮层面板（弹窗/抽屉） |
| `Panel_Paper` | InkPaperPanel | PaperPanelProfile | 纸色卷轴/信笺面板（文字用 TextOnPaper） |
| `Panel_BrushBorder` | InkBrushBorder | GenericControlProfile | 水墨笔触金线描边 |
| `Panel_TitleBar` | InkPanelTitle | PanelTitleProfile | `Title`（左金条+品牌色标题） |
| `Deco_Corner` | InkCornerDeco | CornerDecoProfile | `ShowTL/TR/BL/BR` 四角金角 |
| `Divider_H` / `Divider_V` | InkDivider / InkDividerVertical | GenericControlProfile | 渐变分割线 |
| `Title_Vertical` | InkVerticalTitle | VerticalTitleProfile | `Text`、`FontSize`（竖排书法标题） |

### 燕云融合交互单元类
| Prefab | 控件 | Profile | 关键属性 |
|---|---|---|---|
| `Cell_Grid` | InkCell | CellProfile | `Quality`(Common~Legendary，传世=朱红)、`Badge`、`Selected` |
| `List_StandardItem` | InkListItem | ListItemProfile | `Active`（左金竖线+微金底；内容作子控件叠加） |
| `Avatar_Ink` | InkAvatar | AvatarProfile | `AvatarTexture`（36px 金边头像框） |
| `Dot_Status` | InkDot | StatusDotProfile | `Online`（在线绿点发光） |
| `Bar_Ink` | InkBar | InkBarProfile | `Value`、`FillVariant`(Gold/Jade/Blood/Vermilion/Alert)、`SlotColorToken`、`BorderColorToken`、400ms 平滑过渡 |
| `Bar_InkVertical` | InkBar(Vertical) | InkBarProfile | 竖向属性条（从下往上） |

### 排除项（不 Prefab 化）
`CharacterPreview3D`、`Viewport3DPreview`、`UICharacterCameraSystem`、`OrbitCamera` ——
依赖 RenderTarget / 相机 / 网络数据流，编辑器预制无意义；`ToastMessage`（静态管理器，非控件）。

## 3. 属性接口（Inspector）

所有 Profile 共有字段（`UIComponentProfile` 基类）：

| 字段 | 说明 |
|---|---|
| `ControlName` | 控件登记名（事件/动画按此名解析），留空用宿主 Actor 名 |
| `AutoCreateControl` | 宿主无控件时自动创建默认控件 |
| `EditorRefreshInterval` | 编辑器模式参数刷新间隔（秒） |

专用 Profile 的关键属性（节选）：

- **InkButtonProfile**：`Text`、`Variant`(Primary 鎏金/Secondary 深墨金线/Ghost/Danger/Brand/Vermilion 朱红)、`ButtonSize`(Sm 28/Md 36/Lg 44)、`Width/Height`、`AnchorPreset`
- **InkTagProfile**：`Text`、`TagVariant`(Default/Brand/Vermilion)、`FontSize`
- **RoundedPanelProfile**：`CornerRadius`、`BackgroundToken`（主题令牌名）
- **ProgressBarProfile**：`Value(0~1)`、`BarColorToken`、`BackgroundToken`、`CornerRadius`
- **TextBoxProfile**：`Text`、`Watermark`、`IsPassword`
- **ConfirmDialogProfile**：`Title`、`Message`、`ConfirmText`、`CancelText`、`VisibleOnInit`
- **ToastProfile**：`Message`、`Type`、`Duration`、`AutoShow`
- **UIAnimationProfile**：见上表
- **燕云融合族**（YyProfiles.cs）：`InkPanelProfile`、`PaperPanelProfile`、`PanelTitleProfile`、`CornerDecoProfile`、`CellProfile`、`ListItemProfile`、`AvatarProfile`、`StatusDotProfile`、`VerticalTitleProfile`、`InkBarProfile`（枚举/品质/填充变体均为 Inspector 下拉，颜色走令牌名）
- **GenericControlProfile**：`ControlTypeName`（类型短名或全名）、`PropertyNames`/`PropertyValues`
  （反射写任意公共可写属性；值支持 `true/false`、数字、枚举名、`"x,y"`(Float2)、`"r,g,b,a"`(Color 0~1)）

## 4. 事件接口（UIEventBinder 声明式绑定）

在 Prefab/实例 Actor 上添加 `UIEventBinder` 脚本，按下标一一对应填写列表：

| 字段 | 说明 |
|---|---|
| `TargetNames` | 目标控件登记名（留空=本 Actor 的 UIControl 控件） |
| `EventNames` | 事件名：`Clicked` / `ButtonClicked` / `TextChanged` / `Confirmed` / `Cancelled` 等 |
| `Actions` | `InvokeScriptMethod` / `SpawnPrefab` / `ShowToast` / `DestroyTarget` |
| `TargetActors` | 动作目标 Actor（可空=本 Actor） |
| `TargetTypeNames` / `MethodNames` | 脚本类型名（全名/短名）与方法名（0 参、1 参 Control 或 string） |
| `SpawnPrefabs` | `SpawnPrefab` 动作的 Prefab 资产 |
| `Messages` | Toast 文本（前缀 `info:`/`success:`/`warning:`/`error:`）或方法 string 参数 |

示例（`Btn_InkButton.prefab` 已内置）：`BtnInk` 的 `Clicked` → `ShowToast`「success:按钮被点击」。

代码侧还可订阅 `UIEventBinder.BindingTriggered`（参数为绑定下标）。

## 5. 编辑器使用流程

1. 场景里 `ui_canvas_create`（或已有 UICanvas）→ 把 `Content/Prefabs/UI/*.prefab` 拖入 / `prefab_spawn`。
2. 选中实例 Actor，在 Inspector 修改 Profile 字段（PIE 或编辑器轮询即时生效）。
3. 需要事件时：给实例加 `UIEventBinder` 脚本，按上表填列表。
4. 批量重建资产：调用 HundunAgent 工具 `ui_prefab_batch`（spec 格式见
   `.compile-temp/prefab-specs.json` 样例）；单个用 `ui_prefab_build`。

## 6. 代码使用示例

```csharp
using HundunWorld.Game.UI.Prefabs;

// 一行 Spawn 预制体到画布 Actor 下
var btn = HundunUIPrefab.Spawn(
    Content.Load<Prefab>("Content/Prefabs/UI/Btn_InkButton.prefab"),
    parentCanvas, "OkButton");

// 订阅点击（自动反射 Button.Clicked）
HundunUIPrefab.HookClicked(btn, () => Components.ToastMessage.ShowSuccess("已确认"));

// 取回强类型 Profile 改参数
var profile = HundunUIPrefab.SpawnProfiled<InkButtonProfile>(prefab, parentCanvas);
profile.Variant = InkButtonVariant.Danger;
profile.RefreshComponent();
```

## 7. 验证结论（PrefabGalleryTest 场景 + PrefabSmokeTest）

- **52/52 Prefab 运行时加载与实例化成功**（`[PREFAB-SMOKE] GALLERY: 成功 52 / 失败 0`，含燕云融合新增 17 个）。
- 按钮已对齐燕云规范：Primary 背景 #C8A858（RGBA 0.784,0.659,0.345）、Md 高 36px；控件、变体配色、尺寸、中文文本随 Prefab 正确还原。
- Profile 脚本随 Prefab 还原并自动绑定控件；`UIPrefabRegistry` 按名解析可用。
- `Clicked` 事件链路与 `UIEventBinder` 声明式绑定（含 Toast 动作）在 PIE 验证通过。
- 现有纯代码页面不受影响（控件类仅新增无参构造与公开属性，构造签名保持兼容；
  按钮尺寸/配色按 yy 规范统一升级，`InkWashTheme.ControlHSm/Md/Lg` 同步为 28/36/44）。

## 8. 已知限制

- Flax 编辑器视口没有 Unity 式 UI 画布预览；「编辑器为主」的工作流是：
  Prefab 拖入场景 + Inspector 配参数 + PIE 确认效果（Profile 编辑器轮询已保证数据即时应用）。
- `ui_prefab_build` 依赖 HundunAgent 的 `PrefabManager.CreatePrefab` 单次调用（重复调用会产生
  prefab-in-prefab 循环引用，工具内已规避）。
- 编辑器自动编译在窗口聚焦时触发；批量产出资产前确认日志出现 `Scripts reloading end`。
- `KungfuCard` 等复合卡片的内部子 UI 需代码调用 `Initialize()` 构建（Prefab 提供骨架与尺寸）。
