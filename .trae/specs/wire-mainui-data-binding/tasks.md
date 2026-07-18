# Tasks

- [x] Task 1: 在 MainUIManager 中新增本地玩家组件获取辅助方法
  - [x] 新增 `using Game.Character.Attributes;`、`using Game.Combat.Skills;`、`using HundunWorld.Game;` 引用
  - [x] 新增私有字段 `private CharacterAttributesComponent _cachedAttributes;`、`private SkillBase[] _cachedSkills;`、`private bool _localPlayerReady;`、`private float _rebindLogThrottle;` 用于状态跟踪与日志节流
  - [x] 新增 `private bool TryGetLocalPlayerAttributes(out CharacterAttributesComponent component)` 方法：从 `HundunWorldGame.Instance?.LocalPlayerActor?.GetScript<CharacterAttributesComponent>()` 获取，null 安全
  - [x] 新增 `private bool TryGetLocalPlayerSkills(out SkillBase[] slots)` 方法：从 `LocalPlayerActor.GetScripts<SkillBase>()` 获取数组（注意 Flax 的 GetScripts 返回数组），null 安全
  - [x] **验证**: 编译通过，方法签名符合 CombatHudPage.BindCharacter/BindSkills 接口契约

- [x] Task 2: 修改 CreateCombatHud 工厂方法，添加即时绑定
  - [x] 在 `CreateCombatHud()` 中创建页面并订阅 NavigationRequested 后，调用 `TryGetLocalPlayerAttributes` 与 `TryGetLocalPlayerSkills`
  - [x] 若获取成功，调用 `page.BindCharacter(component)` 与 `page.BindSkills(slots)`
  - [x] 若获取失败，记录 Debug 日志 `[MainUIManager] CombatHud 创建时本地玩家未就绪，等待重绑`
  - [x] **验证**: 编译通过，工厂方法签名不变（仍返回 CombatHudPage）

- [x] Task 3: 修改 CreateCombatHudV2 工厂方法，添加即时绑定
  - [x] 在 `CreateCombatHudV2()` 中创建页面并订阅 NavigationRequested 后，调用 `TryGetLocalPlayerAttributes`
  - [x] 若获取成功，调用 `page.BindCharacter(component)`
  - [x] 若获取失败，记录 Debug 日志
  - [x] **验证**: 编译通过

- [x] Task 4: 修改 CreateMenuCharAttributesV2 工厂方法，添加即时绑定
  - [x] 在 `CreateMenuCharAttributesV2()` 中创建页面并订阅 NavigationRequested 后，调用 `TryGetLocalPlayerAttributes`
  - [x] 若获取成功，调用 `page.BindCharacter(component)`
  - [x] 若获取失败，记录 Debug 日志
  - [x] **验证**: 编译通过

- [x] Task 5: 实现本地玩家就绪重绑机制（OnUpdate 状态翻转检测）
  - [x] 覆写 `MainUIManager.OnUpdate()`（注意 MainUIManager 继承 Script，使用 `OnUpdate()` 无参数版本，内部用 `Time.DeltaTime`）
  - [x] 检测 `_localPlayerReady` 标志从 false → true 的翻转：每帧调用 `TryGetLocalPlayerAttributes`，若返回 true 且 `_localPlayerReady` 为 false，则标记为 true 并触发一次重绑
  - [x] 备选简化方案：状态翻转时仅记录日志 `[MainUIManager] 本地玩家已就绪，下次导航到 combat-hud/nav-character-v2 时将自动绑定`（因 InkPageRouter 每次导航都重新创建页面，下次导航自然触发即时绑定）
  - [x] 采用备选方案：状态翻转时仅更新标志与日志，依赖 InkPageRouter 每次导航重新创建页面的特性，下次导航自动绑定
  - [x] **验证**: 编译通过，状态翻转日志输出正确，不刷屏

- [x] Task 6: 在 OnUpdate 中持续刷新 CombatHud 动态数据（小地图朝向 + 技能冷却）
  - [x] 仅当 `_inkPageRouter?.CurrentPageDomId == InkPageDomIds.CombatHud` 且 `_localPlayerReady == true` 时执行刷新
  - [x] 获取当前 CombatHudPage 引用：在 MainUIManager 中新增 `private CombatHudPage _activeCombatHud;` 字段，在 `CreateCombatHud` 中赋值，在 `DestroyInkWashUI` 中置 null
  - [x] 类似地新增 `private CombatHudV2Page _activeCombatHudV2;`、`private MenuCharAttributesV2Page _activeMenuCharAttributesV2;` 字段并在对应工厂中赋值（用于动态刷新与重绑）
  - [x] 小地图朝向：从 `LocalPlayerActor.Orientation` 计算 Yaw 角（使用 `EulerAngles.Y`，Flax 项目惯例 Y 轴为 Yaw），赋值给 `_activeCombatHud.MinimapPlayerYaw`
  - [x] 技能冷却：遍历 `_cachedSkills`，调用 `skill.GetCooldownProgress()`，反转为 `1 - progress`（因 SkillBase 0=刚释放/1=就绪，CombatHud 0=就绪/1=冷却中），赋值给 `_activeCombatHud.SkillCooldowns`
  - [x] **验证**: 编译通过，朝向与冷却刷新逻辑无空引用异常

- [x] Task 7: 在 HundunWorldGame.CreateLocalPlayerActor 末尾保证 CharacterAttributesComponent 必挂
  - [x] 在 `CreateLocalPlayerActor` 方法末尾 `return actor;` 之前，添加 `actor.GetScript<CharacterAttributesComponent>()` + `actor.AddScript<CharacterAttributesComponent>()` 兜底
  - [x] 添加 `using Game.Character.Attributes;` 引用（第 21 行）
  - [x] 记录 Info 日志：`[HundunWorldGame] 已确保 LocalPlayerActor 挂载 CharacterAttributesComponent: Level={attrComp.Level}, Nickname={attrComp.Nickname}, Stage={attrComp.CurrentStage}`
  - [x] **验证**: 编译通过，CreateLocalPlayerActor 返回的 Actor 必定包含 CharacterAttributesComponent

- [x] Task 8: 清理 OnDestroy 中的绑定引用
  - [x] 在 `MainUIManager.OnDestroy` 中追加清理：`_cachedAttributes = null;`、`_cachedSkills = null;`、`_activeCombatHud = null;`、`_activeCombatHudV2 = null;`、`_activeMenuCharAttributesV2 = null;`、`_localPlayerReady = false;`
  - [x] 在 `DestroyInkWashUI` 中也清理活动页面引用（_activeCombatHud/_activeCombatHudV2/_activeMenuCharAttributesV2）
  - [x] **验证**: 编译通过，场景切换后无悬空引用

- [x] Task 9: 整体编译验证（Flax.Build 0 错误）
  - [x] 运行 Flax.Build 编译项目，0 错误（Game.CSharp.dll 成功生成，Debug/Development/Release 三配置均通过）
  - [x] 代码审查验证：所有 Bind 接口调用与 null 检查正确
  - [x] 代码审查验证：OnUpdate 刷新逻辑仅在页面活动且玩家就绪时执行，无性能问题
  - [x] 附带修复 NarrativePro 插件 2 处预存在编译错误（AbilityTask_SpawnProjectile.cs 的 SetLifeSpan、NarrativeGameUserSettings.cs 的 Audio.SetVolumeCategory），这些是 UE5 移植遗留错误，与本 spec 修改无关，但阻塞了整体编译

- [x] Task 10（运行时验证发现）：修复 CombatHud 头像导航目标指向新版属性页
  - [x] 定位根因：`CombatHudPage` / `CombatHudV2Page` 头像按钮触发 `NavigationRequested("nav-character")`，打开旧版 `MenuCharAttributesPage`
  - [x] 修改 [CombatHudPage.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/CombatHudPage.cs#L917) 的 `OnAvatarButtonClicked`：`"nav-character"` → `"nav-character-v2"`
  - [x] 修改 [CombatHudV2Page.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/Combat/CombatHudV2Page.cs#L721) 的 `OnAvatarButtonClicked`：`"nav-character"` → `"nav-character-v2"`
  - [x] 同步更新相关 XML 注释中的 dom-id 示例
  - [x] 编译验证：Flax.Build 0 错误

# Task Dependencies
- [Task 2] depends on [Task 1]
- [Task 3] depends on [Task 1]
- [Task 4] depends on [Task 1]
- [Task 5] depends on [Task 1]
- [Task 6] depends on [Task 1, Task 2]
- [Task 7] depends on [Task 1]（独立修改 HundunWorldGame，但语义上属于同一数据链路）
- [Task 8] depends on [Task 1, Task 6]
- [Task 9] depends on [Task 2, Task 3, Task 4, Task 5, Task 6, Task 7, Task 8]
- [Task 10] depends on [Task 9]
