# MainUIManager 数据绑定接入 Spec

## Why
阶段三（`enhance-character-attribute-ui`）已完成 5 个新 UI 控件（CharacterPreview3D / HexRadarChartOverlay / InkAttributeTooltip / InkEquipmentSlot / InkBackpackGrid）和 `MenuCharAttributesV2Page` 的重构，并为 `CombatHudPage`、`CombatHudV2Page`、`MenuCharAttributesV2Page` 提供了 `BindCharacter(CharacterAttributesComponent)` 等数据绑定 API。

但 [MainUIManager.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/MainUIManager.cs) 中的页面工厂方法（`CreateCombatHud` / `CreateMenuCharAttributesV2` / `CreateCombatHudV2`）目前仅 `new` 出页面并订阅 `NavigationRequested`，**未调用任何 Bind 接口**，导致新 UI 在运行时只能显示 mock 数据，无法反映真实角色属性。本 Spec 旨在打通 MainUIManager → LocalPlayerActor → CharacterAttributesComponent → Ink 页面的数据链路。

## What Changes
- 在 [MainUIManager.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/MainUIManager.cs) 中新增本地玩家组件获取辅助方法（`TryGetLocalPlayerAttributes` / `TryGetLocalPlayerSkills`），统一从 `HundunWorldGame.Instance.LocalPlayerActor` 获取 `CharacterAttributesComponent` 与 `SkillBase[]`
- 修改 `CreateCombatHud`、`CreateCombatHudV2`、`CreateMenuCharAttributesV2` 工厂方法：创建页面后立即尝试 `BindCharacter` / `BindSkills`（若 LocalPlayerActor 已就绪）
- 新增本地玩家就绪重绑机制：在 `MainUIManager.OnUpdate` 中检测 `LocalPlayerActor` 从 null 变为非 null 的瞬间，对当前活动页面执行一次重绑（事件驱动需侵入 `HundunWorldGame`，故采用轻量轮询，仅在状态翻转时触发一次）
- 在 `MainUIManager.OnUpdate` 中持续刷新动态数据：`CombatHudPage.MinimapPlayerYaw`（从 `LocalPlayerActor.Orientation` 计算 Yaw）、`CombatHudPage.SkillCooldowns`（从 `SkillBase.GetCooldownProgress` 读取并反转）
- **BREAKING**：在 `HundunWorldGame.CreateLocalPlayerActor` 末尾对 `LocalPlayerActor` 执行 `GetOrAddScript<CharacterAttributesComponent>()`，保证 UI 绑定时组件必定存在（当前 Prefab 不一定挂载该组件）
- 在 `MainUIManager.OnDestroy` 中清理绑定引用，避免悬空指针

## Impact
- 受影响 specs：
  - `enhance-character-attribute-ui`（上游依赖，提供 Bind API）
  - `character-attribute-panel`（角色属性组件定义）
  - `character-equipment-system`（装备管理器挂载点）
  - `land-inkwash-ui-foundation`（InkPageRouter / InkPageShell 基础）
- 受影响代码：
  - [MainUIManager.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/MainUIManager.cs) — 主要修改对象
  - [HundunWorldGame.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/HundunWorldGame.cs#L1240-L1387) — `CreateLocalPlayerActor` 末尾追加 `GetOrAddScript<CharacterAttributesComponent>`
  - [CombatHudPage.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/CombatHudPage.cs) — 被调用方，无需修改
  - [MenuCharAttributesV2Page.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/Character/MenuCharAttributesV2Page.cs) — 被调用方，无需修改
  - [CombatHudV2Page.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/Combat/CombatHudV2Page.cs) — 被调用方，无需修改

## ADDED Requirements

### Requirement: 本地玩家组件获取辅助
MainUIManager SHALL 提供私有辅助方法 `TryGetLocalPlayerAttributes(out CharacterAttributesComponent)` 与 `TryGetLocalPlayerSkills(out SkillBase[])`，从 `HundunWorldGame.Instance.LocalPlayerActor` 安全获取组件，null 安全，失败记录警告日志但不抛异常。

#### Scenario: LocalPlayerActor 已就绪
- **WHEN** `HundunWorldGame.Instance.LocalPlayerActor` 非 null
- **AND** Actor 上存在 `CharacterAttributesComponent`
- **THEN** `TryGetLocalPlayerAttributes` 返回 true 并输出组件实例

#### Scenario: LocalPlayerActor 未就绪
- **WHEN** `HundunWorldGame.Instance` 为 null 或 `LocalPlayerActor` 为 null
- **THEN** `TryGetLocalPlayerAttributes` 返回 false，输出 null，记录一条 Debug 级日志（不刷屏）

#### Scenario: 组件缺失
- **WHEN** LocalPlayerActor 非 null 但未挂载 `CharacterAttributesComponent`
- **THEN** `TryGetLocalPlayerAttributes` 返回 false，输出 null，记录一条 Warning 日志

### Requirement: 页面工厂即时绑定
`CreateCombatHud`、`CreateCombatHudV2`、`CreateMenuCharAttributesV2` SHALL 在创建页面实例后立即尝试调用 `BindCharacter`；`CreateCombatHud` 额外尝试调用 `BindSkills`。若 LocalPlayerActor 尚未就绪，则跳过绑定并依赖后续重绑机制。

#### Scenario: 工厂调用时玩家已就绪
- **WHEN** 用户从 CombatHud 头像按钮导航到 `nav-character-v2`
- **AND** `LocalPlayerActor` 已存在且挂载了 `CharacterAttributesComponent`
- **THEN** `CreateMenuCharAttributesV2` 调用 `page.BindCharacter(component)` 后返回页面
- **AND** 页面立即显示真实等级、昵称、阶段、3D 预览、属性、雷达图

#### Scenario: 工厂调用时玩家未就绪
- **WHEN** `InitializeInkWashUI` 首次导航到 `combat-hud`
- **AND** `LocalPlayerActor` 为 null（网络响应未到达）
- **THEN** `CreateCombatHud` 跳过 `BindCharacter`，返回未绑定的页面（使用 mock 数据）
- **AND** 记录一条 Debug 日志说明延迟绑定

### Requirement: 本地玩家就绪重绑
MainUIManager SHALL 在 `OnUpdate` 中监测 `LocalPlayerActor` 从 null 变为非 null 的状态翻转，并对当前活动页面（通过 `_inkPageRouter.CurrentPageDomId` 判断）执行一次重绑。

#### Scenario: 玩家延迟就绪
- **GIVEN** CombatHud 页面已显示但使用 mock 数据
- **WHEN** 网络响应到达，`HundunWorldGame.CreateLocalPlayerActor` 完成
- **THEN** 下一帧 `MainUIManager.OnUpdate` 检测到 `LocalPlayerActor` 非 null
- **AND** 若当前页面为 `combat-hud`，调用 `page.BindCharacter` + `page.BindSkills`
- **AND** 若当前页面为 `nav-character-v2`，调用 `page.BindCharacter`
- **AND** 重绑后页面立即显示真实数据

#### Scenario: 玩家尚未就绪时不刷屏
- **WHEN** `LocalPlayerActor` 持续为 null
- **THEN** `OnUpdate` 不输出警告日志（仅 Debug 级且节流），不抛异常

### Requirement: 动态数据持续刷新
MainUIManager SHALL 在 `OnUpdate` 中持续刷新 CombatHud 页面的动态数据（仅当当前页面为 `combat-hud` 且已绑定角色时）。

#### Scenario: 小地图朝向刷新
- **GIVEN** CombatHud 为当前活动页面且已绑定 LocalPlayerActor
- **WHEN** 玩家旋转角色
- **THEN** `OnUpdate` 从 `LocalPlayerActor.Orientation` 计算 Yaw 角（度），赋值给 `CombatHudPage.MinimapPlayerYaw`
- **AND** 小地图玩家三角形实时旋转

#### Scenario: 技能冷却刷新
- **GIVEN** CombatHud 为当前活动页面且已绑定 SkillBase[]
- **WHEN** 玩家释放技能
- **THEN** `OnUpdate` 遍历 `SkillBase[]`，调用 `GetCooldownProgress()`，反转为 0=就绪/1=冷却中，赋值给 `CombatHudPage.SkillCooldowns`
- **AND** 技能槽显示冷却动画

### Requirement: LocalPlayerActor 必挂 CharacterAttributesComponent
`HundunWorldGame.CreateLocalPlayerActor` SHALL 在 Actor 创建完成、`CharacterEquipmentManager` 添加之后，对 `LocalPlayerActor` 执行 `GetOrAddScript<CharacterAttributesComponent>()`，保证 UI 绑定时组件必定存在。

#### Scenario: Prefab 已挂载组件
- **WHEN** `CreateLocalPlayerActor` 生成的 Prefab 已包含 `CharacterAttributesComponent`
- **THEN** `GetOrAddScript` 返回现有实例，不重复添加

#### Scenario: Prefab 未挂载组件
- **WHEN** Prefab 不包含 `CharacterAttributesComponent`
- **THEN** `GetOrAddScript` 自动添加一个默认实例（Level=1, Nickname="无名侠", CurrentStage=Wuxia）
- **AND** 记录一条 Info 日志说明已自动添加

## MODIFIED Requirements

### Requirement: MainUIManager 生命周期
原 `OnDestroy` 仅销毁水墨 UI 与取消事件订阅。修改后 SHALL 额外清理本地玩家组件引用缓存与重绑状态标志，避免场景切换后悬空引用。

## REMOVED Requirements
无。
