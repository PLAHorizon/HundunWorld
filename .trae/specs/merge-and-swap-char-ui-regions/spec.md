# 角色属性 UI 区域合并/对调与 3D 预览修复 Spec

## Why

当前角色属性页 V2 采用三栏布局：左侧（战力+基础属性+进阶属性+雷达图）、中间（3D 预览+角色信息）、右侧（装备+武学摘要+背包）。用户根据截图标注要求重组区域：将装备区合并到中间 3D 预览区、角色信息区合并到左侧战力区、右侧武学摘要与背包互换位置，并修复当前中间面板 3D 角色预览黑屏问题以实现实时渲染。

## What Changes

### 区域合并
- **装备区合并到 3D 预览区（1→2）**：将原位于右侧面板顶部的装备区（纸娃娃背景 + 15 装备槽 + 标题栏）整体迁移到中间面板，与 3D 预览共存于同一面板。中间面板布局调整为：3D 预览（上）+ 装备区（下），或左右分栏（视空间而定）。
- **角色信息区合并到战力区（3→4）**：将原位于中间面板底部的角色信息区（角色名 + 等级两段式 + 门派徽章 + 称号装饰线 + 阶段标签 + 门派标识）整体迁移到左侧面板顶部，与战力区共存。左侧面板布局调整为：战力区 + 角色信息区 + 基础属性 + 进阶属性 + 雷达图。

### 区域对调
- **武学摘要与背包互换位置（5↔6、7↔8）**：右侧面板内，原顺序"装备区（已迁走）→ 武学摘要 → 背包"调整为"背包 → 武学摘要"。即背包区移到上方，武学摘要区移到下方。

### 3D 预览实时渲染修复
- 修复 `CharacterPreview3D` 控件运行时黑屏问题，确保打开角色属性页时中间面板实时渲染当前角色（或默认角色）的 3D 模型，支持鼠标左键拖拽旋转。

### 布局比例调整
- **BREAKING**：三栏宽度比例可能需调整以适应新布局（装备区移入中间、角色信息移入左侧）。当前 30%/40%/30% 可能改为 32%/44%/24% 或类似比例，由实现时根据内容尺寸决定。

## Impact

- **Affected specs**：
  - `deep-beautify-char-attributes-v5`（刚完成的视觉美化，本轮在其基础上重组布局，不回滚美化改动）
  - `enhance-character-attribute-ui`（3D 预览与雷达图功能保留，仅调整位置）
  - `merge-attr-equip-tooltip`（装备槽 Tooltip 逻辑不受影响，仅父容器变更）
- **Affected code**：
  - [MenuCharAttributesV2Page.cs](file:///c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\UI\Ink\Pages\Character\MenuCharAttributesV2Page.cs) — Build 方法重组子控件归属面板、Layout 方法重算坐标、面板宽度比例常量调整
  - [CharacterPreview3D.cs](file:///c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\UI\Ink\Components\CharacterPreview3D.cs) — 诊断并修复 3D 渲染管线黑屏问题

## ADDED Requirements

### Requirement: 装备区与 3D 预览合并到中间面板

系统 SHALL 将装备区（纸娃娃背景 + 15 装备槽 + 装备标题栏 + 装备数量提示）从右侧面板迁移到中间面板，与 3D 预览控件共存于同一面板内。

#### Scenario: 中间面板显示 3D 预览与装备区
- **WHEN** 角色属性页显示
- **THEN** 中间面板同时包含 3D 预览控件与装备区
- **AND** 3D 预览位于装备区上方（或左右分栏，视空间而定）
- **AND** 装备槽纸娃娃布局、hover 效果、双击换装、Tooltip 触发等功能不受影响

#### Scenario: 中间面板空间分配
- **WHEN** 屏幕尺寸为 1920×1080
- **THEN** 3D 预览控件尺寸适当缩小以容纳装备区（如宽度 400、高度 480）
- **AND** 装备区纸娃娃背景与装备槽可见且可交互
- **AND** 两区域不重叠、不溢出面板边界

### Requirement: 角色信息区与战力区合并到左侧面板

系统 SHALL 将角色信息区（角色名 + 等级两段式 + 门派徽章 + 称号装饰线 + 阶段标签 + 门派标识）从中间面板迁移到左侧面板顶部，位于战力区下方（或上方）。

#### Scenario: 左侧面板显示战力与角色信息
- **WHEN** 角色属性页显示
- **THEN** 左侧面板顶部依次显示：战力区 → 角色信息区 → 基础属性 → 进阶属性 → 雷达图
- **AND** 角色名 text-shadow、等级辉光、门派 InkTag、称号渐变装饰线等视觉效果保留
- **AND** 若左侧面板空间不足以容纳所有内容，优先保证战力区与角色信息区可见，雷达图可裁剪或滚动

### Requirement: 右侧面板背包与武学摘要互换位置

系统 SHALL 将右侧面板内背包区与武学摘要区的垂直顺序互换，使背包位于上方、武学摘要位于下方。

#### Scenario: 右侧面板顺序
- **WHEN** 角色属性页显示
- **THEN** 右侧面板从上到下依次为：背包区 → 武学摘要区
- **AND** 背包格子双击装备、武学卡片 hover 位移、Tooltip 显示等功能不受影响

### Requirement: 3D 角色实时预览

系统 SHALL 在角色属性页中间面板实时渲染当前角色（或默认角色）的 3D 模型，支持鼠标左键水平拖拽旋转。

#### Scenario: 3D 预览正常渲染
- **WHEN** 角色属性页打开
- **THEN** 中间面板 3D 预览区域显示角色 3D 模型（非黑屏）
- **AND** 模型居中显示，相机距离与 FOV 合理（约 200 单位距离、45 度 FOV）
- **AND** 离屏渲染不污染主场景画面

#### Scenario: 拖拽旋转
- **WHEN** 用户在 3D 预览区域按住鼠标左键并水平拖动
- **THEN** 角色 3D 模型绕 Y 轴旋转
- **AND** 松开鼠标后保持当前角度

#### Scenario: 角色数据绑定
- **WHEN** 本地玩家数据就绪并调用 `BindCharacter`
- **THEN** 3D 预览加载玩家 Actor 上的 `AnimatedModel` 的 `SkinnedModel` 与 `AnimationGraph`
- **AND** 若玩家数据未就绪，回退到默认模型

#### Scenario: 场景延迟就绪重试
- **WHEN** 控件初始化时主场景未加载（如处于过渡场景）
- **THEN** 控件不崩溃
- **AND** 在后续 `RefreshLayout` 调用中重试 Actor 创建与渲染任务绑定
- **AND** 场景就绪后 3D 预览正常渲染

## MODIFIED Requirements

### Requirement: 三栏布局比例

三栏宽度比例 SHALL 根据新布局内容调整：左侧面板需容纳战力+角色信息+属性+雷达图（内容增多），中间面板需容纳 3D 预览+装备区（内容增多），右侧面板仅容纳背包+武学摘要（内容减少）。建议比例 32%/44%/24% 或由实现时根据内容尺寸动态决定。`PanelGap`、`PanelPadding` 等常量保持不变。

### Requirement: CharacterPreview3D 渲染管线健壮性

`CharacterPreview3D` SHALL 在以下场景中保持健壮：
1. 初始化时场景未就绪：推迟 Actor 创建，不抛异常
2. `RefreshLayout` 时检测未初始化状态并重试
3. `RenderTexture` 初始化失败时记录错误并回退到纯色背景
4. `AnimatedModel` 模型加载失败时禁用组件并记录警告
5. 控件销毁时安全释放所有 GPU 资源与 Actor

当前实现中 `InitializeActors` 在场景未就绪时仅记录警告并返回，但 `RefreshLayout` 未实现重试逻辑，导致场景就绪后 3D 预览仍黑屏。 SHALL 在 `RefreshLayout` 中增加未初始化检测与重试调用 `InitializeActors`。
