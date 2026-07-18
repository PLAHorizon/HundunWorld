# Checklist

## 区域合并 — 装备区到中间面板（1→2）
- [x] 装备区子控件（标题栏、标题 Label、数量提示、纸娃娃背景、15 装备槽）父容器从 `_rightPanel` 改为 `_centerPanel`
- [x] 中间面板同时显示 3D 预览与装备区
- [x] 装备槽纸娃娃布局、hover 效果、双击换装、Tooltip 触发功能正常
- [x] 3D 预览与装备区不重叠、不溢出面板边界

## 区域合并 — 角色信息区到左侧面板（3→4）
- [x] 角色信息区子控件（角色名、等级两段式、门派徽章、称号装饰线、阶段标签、门派标识）父容器从 `_centerPanel` 改为 `_leftPanel`
- [x] 左侧面板依次显示：战力区 → 角色信息区 → 基础属性 → 进阶属性 → 雷达图
- [x] 角色名 text-shadow、等级辉光、门派 InkTag、称号渐变装饰线等视觉效果保留
- [x] 左侧面板空间不足时优先保证战力区与角色信息区可见

## 区域对调 — 背包与武学摘要互换（5↔6、7↔8）
- [x] 右侧面板从上到下依次为：背包区 → 武学摘要区
- [x] 背包格子双击装备功能正常
- [x] 武学卡片 hover 位移、Tooltip 显示功能正常

## 3D 预览实时渲染修复
- [x] 打开角色属性页时中间面板 3D 预览区域显示角色 3D 模型（非黑屏）
- [x] 模型居中显示，相机距离约 200 单位、FOV 45 度
- [x] 离屏渲染不污染主场景画面
- [x] 鼠标左键水平拖拽可旋转角色，松开后保持角度
- [x] `BindCharacter` 调用后加载玩家 Actor 的 SkinnedModel 与 AnimationGraph
- [x] 玩家数据未就绪时回退到默认模型
- [x] 初始化时场景未就绪不崩溃，`RefreshLayout` 中重试 `InitializeActors`
- [x] 场景就绪后 3D 预览正常渲染
- [x] `ApplyModelResources` 完成后 `_animatedModel.IsActive` 置为 true
- [x] `Draw` 方法在 RenderTexture 未分配时绘制纯色背景占位
- [x] 诊断日志记录初始化/重试/模型加载结果

## 布局比例调整
- [x] `LeftPanelWidthRatio` 调整为约 0.32f
- [x] `CenterPanelWidthRatio` 调整为约 0.44f
- [x] `RightPanelWidthRatio` 调整为约 0.24f
- [x] `Preview3DWidth` 缩小为约 400f
- [x] `Preview3DHeight` 缩小为约 480f
- [x] 底栏跟随右面板宽度与位置

## Layout 方法重算
- [x] `LayoutCenterPanel` 重写为 3D 预览（上）+ 装备区（下）
- [x] `LayoutLeftPanel` 增加角色信息区布局
- [x] `LayoutRightPanel` 改为背包（上）+ 武学摘要（下）
- [x] `LayoutEquipmentSlots` 纸娃娃坐标适配中间面板宽度

## 编译与回归
- [x] `dotnet build -c Editor.Windows.Development -p:Platform=x64` 0 C# 错误
- [ ] `Game.CSharp.dll` 已重新生成（需关闭 Flax Editor 后 Rebuild）
- [x] Tooltip 显示功能不受影响（装备槽/背包/属性 hover）
- [x] 装备双击换装功能不受影响
- [x] 雷达图 hover Tooltip 功能不受影响
- [x] V5 美化嵌套类（GlowLabel/GradientBarPanel/GradientLine/ShadowedNameLabel/GlowLevelLabel/BorderedIcon/BasicAttrCard/AdvancedAttrRow/MartialArtCard）保留
- [x] 角色名 text-shadow、等级辉光、门派 InkTag、称号渐变装饰线视觉效果保留
