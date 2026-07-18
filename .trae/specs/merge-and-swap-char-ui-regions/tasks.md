# Tasks

- [ ] Task 1: 修复 CharacterPreview3D 实时渲染黑屏问题（CharacterPreview3D.cs 独立修改）
  - [ ] SubTask 1.1: 读取 `CharacterPreview3D.cs` 全文，理解当前渲染管线（RenderTexture + SceneRenderTask + 离屏 Camera + AnimatedModel）
  - [ ] SubTask 1.2: 诊断黑屏根因，重点排查以下方向：
    - `InitializeActors` 中 `GetTargetScene()` 是否返回 null（控件初始化时主场景未加载，处于过渡场景）
    - `RefreshLayout` 是否在场景就绪后重试 `InitializeActors`（当前未实现重试，是黑屏主因）
    - `_renderTexture.Init` 返回值判断是否正确（当前 `if (!_renderTexture.Init(ref desc))` 表示 Init 成功返回 0 时进入创建 brush 分支，逻辑正确）
    - `_textureBrush` 是否在 Init 成功后正确创建
    - `_renderTask.Enabled` 是否为 true
    - `_animatedModel.IsActive` 是否为 true（ApplyModelResources 中保留原状态，可能为 false）
    - `Draw` 方法中 `_textureBrush.Draw` 是否被调用
  - [ ] SubTask 1.3: 在 `RefreshLayout` 中增加未初始化重试逻辑：若 `_initialized` 为 false 或 `_cameraRoot`/`_modelRoot` 为 null 且场景已就绪，调用 `InitializeActors` 并标记 `_initialized = true`
  - [ ] SubTask 1.4: 确保 `ApplyModelResources` 在设置完 SkinnedModel 与 AnimationGraph 后将 `_animatedModel.IsActive` 置为 true（当前保留 `wasActive` 原状态，首次初始化时原状态为 false 导致模型不渲染）
  - [ ] SubTask 1.5: 验证 `Draw` 方法在 `_textureBrush` 为 null 或 `_renderTexture` 未分配时绘制纯色背景占位（避免完全黑屏）
  - [ ] SubTask 1.6: 增加诊断日志：初始化成功/失败、RefreshLayout 重试、模型加载结果均记录 `FlaxEngine.Debug.Log/LogWarning/LogError`
  - [ ] SubTask 1.7: 编译验证 `dotnet build HundunWorld\Source\Game.csproj -c Editor.Windows.Development -p:Platform=x64 -t:Rebuild`，0 C# 错误

- [ ] Task 2: 区域合并与对调 — Build 方法重组（MenuCharAttributesV2Page.cs）
  - [ ] SubTask 2.1: 定位 `BuildCenterPanel`（约 1310 行）、`BuildRightPanel`（约 1481 行）、`BuildEquipmentSlots`（约 1522 行）、`BuildBackpackGrid`（约 1575 行）方法
  - [ ] SubTask 2.2: 装备区迁移到中间面板（1→2 合并）：
    - 将 `BuildEquipmentSlots` 中的子控件（`_equipmentTitleBar`、`_equipmentTitleLabel`、`_equipmentHintLabel`、`_paperDollBackground`、`_equipmentSlots` 数组）的父容器从 `_rightPanel` 改为 `_centerPanel`
    - 修改 `BuildEquipmentSlots` 内所有 `_rightPanel.AddChild(...)` 为 `_centerPanel.AddChild(...)`
  - [ ] SubTask 2.3: 角色信息区迁移到左侧面板（3→4 合并）：
    - 将 `BuildCenterPanel` 中的角色信息控件（`_previewNameLabel`、`_previewLevelContainer`、`_previewLevelPrefixLabel`、`_previewLevelValueLabel`、`_previewSectLabel`、`_previewTitleContainer`、`_titleLineLeft`、`_previewTitleLabel`、`_titleLineRight`、`_stageTag`、`_sectEmblemLabel`）的父容器从 `_centerPanel` 改为 `_leftPanel`
    - 这些控件的 `AddChild` 调用从 `_centerPanel.AddChild` 改为 `_leftPanel.AddChild`
    - 保留 `_centerPanel` 中的 `_centerBgLayer`、4 个 InkSplash、`_preview3D`
  - [ ] SubTask 2.4: 右侧面板背包与武学摘要对调（5↔6、7↔8）：
    - 在 `BuildRightPanel` 中，将 `BuildMartialArtsSummary()` 与 `BuildBackpackGrid()` 的调用顺序对调，改为先 `BuildBackpackGrid()` 再 `BuildMartialArtsSummary()`
  - [ ] SubTask 2.5: 检查 `BuildMartialArtsSummary` 与 `BuildBackpackGrid` 内部是否有对 `_rightPanel` 的硬编码引用，统一确认父容器正确
  - [ ] SubTask 2.6: 编译验证

- [ ] Task 3: 区域合并与对调 — Layout 方法重算（MenuCharAttributesV2Page.cs）
  - [ ] SubTask 3.1: 调整面板宽度比例常量（约 52-58 行）：
    - `LeftPanelWidthRatio` 从 0.30f 调整为 0.32f（容纳角色信息区）
    - `CenterPanelWidthRatio` 从 0.40f 调整为 0.44f（容纳装备区）
    - `RightPanelWidthRatio` 从 0.30f 调整为 0.24f（仅背包+武学摘要）
  - [ ] SubTask 3.2: 调整 3D 预览尺寸常量（约 176-179 行）：
    - `Preview3DWidth` 从 480f 缩小为 400f（为装备区腾出空间）
    - `Preview3DHeight` 从 640f 缩小为 480f
  - [ ] SubTask 3.3: 重写 `LayoutCenterPanel`（约 1875 行）：
    - 移除角色信息区布局代码（已迁移到左侧）
    - 上半部分布局 3D 预览（居中）
    - 下半部分布局装备区（标题栏 + 纸娃娃 + 装备槽），调用 `LayoutEquipmentSlots`
    - 调用 `_preview3D.RefreshLayout()`
  - [ ] SubTask 3.4: 重写 `LayoutLeftPanel`（约 1783 行）：
    - 保留战力区布局（`LayoutCombatPowerSection`）
    - 在战力区下方增加角色信息区布局（角色名 + 等级两段式 + 门派徽章 + 称号装饰线 + 阶段标签 + 门派标识）
    - 角色信息区布局逻辑从原 `LayoutCenterPanel` 迁移过来，坐标改为相对左侧面板
    - 保留基础属性、进阶属性、雷达图布局
    - 若空间不足，优先保证战力区与角色信息区可见，雷达图可裁剪
  - [ ] SubTask 3.5: 重写 `LayoutRightPanel`（约 2002 行）：
    - 移除装备区布局代码（已迁移到中间）
    - 先布局背包区（`LayoutBackpack`）
    - 再布局武学摘要区（`LayoutMartialArtsSummary`）
    - 调整春色晕染与金色辉光位置以适应新顺序
  - [ ] SubTask 3.6: 检查 `LayoutEquipmentSlots`（约 2183 行）中的纸娃娃坐标计算是否依赖父面板宽度，若依赖需调整为中间面板宽度
  - [ ] SubTask 3.7: 编译验证

- [ ] Task 4: 底栏与清理逻辑适配（MenuCharAttributesV2Page.cs）
  - [ ] SubTask 4.1: 确认底栏（`LayoutBottomBar`）仍跟随右面板宽度与位置（右面板宽度变小后底栏相应缩小）
  - [ ] SubTask 4.2: 确认 `DestroyInkWashUI` 或清理方法中所有控件引用的销毁逻辑不受父容器变更影响（控件引用字段未变，仅父容器变更）
  - [ ] SubTask 4.3: 确认 `BindCharacter` 中 `_preview3D.SetCharacter` 调用不受影响
  - [ ] SubTask 4.4: 编译验证

- [x] Task 5: 整体编译验证与回归检查
  - [ ] SubTask 5.1: 关闭 Flax Editor（避免 DLL 锁定）
  - [ ] SubTask 5.2: 执行 `dotnet build HundunWorld\Source\Game.csproj -c Editor.Windows.Development -p:Platform=x64 -t:Rebuild`
  - [ ] SubTask 5.3: 确认 0 C# 错误（预存在 XML 文档警告可忽略）
  - [ ] SubTask 5.4: 检查 `Binaries\GameEditorTarget\Windows\x64\Development\Game.CSharp.dll` 已重新生成
  - [ ] SubTask 5.5: 代码审查确认无回归：
    - Tooltip 显示功能（装备槽/背包格子/属性 hover）不受影响
    - 装备双击换装功能不受影响
    - 雷达图 hover Tooltip 功能不受影响
    - V5 美化效果（GlowLabel、GradientBarPanel、GradientLine、ShadowedNameLabel、GlowLevelLabel、BorderedIcon、BasicAttrCard、AdvancedAttrRow、MartialArtCard 等嵌套类）保留
    - 角色名 text-shadow、等级辉光、门派 InkTag、称号渐变装饰线等视觉效果保留

# Task Dependencies

- [Task 1] 独立（CharacterPreview3D.cs）— 可与 Task 2-4 并行
- [Task 2] [Task 3] [Task 4] 均修改 MenuCharAttributesV2Page.cs，**必须顺序执行**（同一 Sub-Agent 顺序处理）
- [Task 5] 依赖 [Task 1-4] 全部完成
- 推荐执行顺序：Task 1（Sub-Agent A，并行）+ Task 2 → Task 3 → Task 4（Sub-Agent B 顺序）→ Task 5（验证）
