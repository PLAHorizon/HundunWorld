# Checklist

## 数据绑定辅助方法
- [x] MainUIManager 新增 `using Game.Character.Attributes;`、`using Game.Combat.Skills;`、`using HundunWorld.Game;` 引用
- [x] `TryGetLocalPlayerAttributes(out CharacterAttributesComponent)` 方法实现正确，null 安全（Instance/LocalPlayerActor/GetScript 任一为 null 时返回 false）
- [x] `TryGetLocalPlayerSkills(out SkillBase[])` 方法实现正确，使用 `GetScripts<SkillBase>()` 获取数组
- [x] 失败路径不抛异常，仅记录 Debug/Warning 日志

## 页面工厂即时绑定
- [x] `CreateCombatHud` 在创建页面后调用 `BindCharacter` + `BindSkills`（若玩家就绪）
- [x] `CreateCombatHudV2` 在创建页面后调用 `BindCharacter`（若玩家就绪）
- [x] `CreateMenuCharAttributesV2` 在创建页面后调用 `BindCharacter`（若玩家就绪）
- [x] 玩家未就绪时记录 Debug 日志说明延迟绑定，不抛异常

## 本地玩家就绪重绑
- [x] MainUIManager 覆写 `OnUpdate()` 方法（注意：Script 用 OnUpdate() 无参数，非 Update(float)）
- [x] 检测 `_localPlayerReady` 从 false → true 的状态翻转
- [x] 状态翻转时记录 Info 日志（不刷屏）
- [x] 依赖 InkPageRouter 每次导航重新创建页面的特性，下次导航自动触发即时绑定

## 动态数据持续刷新
- [x] MainUIManager 新增 `_activeCombatHud`、`_activeCombatHudV2`、`_activeMenuCharAttributesV2` 字段
- [x] 工厂方法中赋值活动页面引用，`DestroyInkWashUI` 中置 null
- [x] `OnUpdate` 仅当 `CurrentPageDomId == CombatHud` 且 `_localPlayerReady == true` 时刷新
- [x] 小地图朝向从 `LocalPlayerActor.Orientation` 计算 Yaw 角（度，使用 EulerAngles.Y）并赋值给 `MinimapPlayerYaw`
- [x] 技能冷却从 `SkillBase.GetCooldownProgress()` 读取并反转为 0=就绪/1=冷却中
- [x] 刷新逻辑无空引用异常（_activeCombatHud、_cachedSkills 均有 null 检查）

## LocalPlayerActor 必挂 CharacterAttributesComponent
- [x] `HundunWorldGame.CreateLocalPlayerActor` 末尾添加 `actor.GetScript<CharacterAttributesComponent>()` + `actor.AddScript<CharacterAttributesComponent>()` 兜底
- [x] 添加 `using Game.Character.Attributes;` 引用（第 21 行）
- [x] 记录 Info 日志说明组件已确保挂载
- [x] 不破坏现有 `CharacterEquipmentManager` 初始化逻辑

## 生命周期清理
- [x] `OnDestroy` 清理 `_cachedAttributes`、`_cachedSkills`、`_activeCombatHud`、`_activeCombatHudV2`、`_activeMenuCharAttributesV2`、`_localPlayerReady` 等字段
- [x] `DestroyInkWashUI` 也清理活动页面引用
- [x] 不影响现有水墨 UI 销毁与事件取消订阅逻辑

## 运行时验证修复
- [x] 定位到"看不到已修改 UI"的根因：CombatHud 头像点击导航到旧版 `nav-character` 而非新版 `nav-character-v2`
- [x] 修改 `CombatHudPage.OnAvatarButtonClicked` 与 `CombatHudV2Page.OnAvatarButtonClicked` 的目标 dom-id 为 `nav-character-v2`
- [x] 编译验证通过

## 编译验证
- [x] Flax.Build 编译通过，0 错误（Game.CSharp.dll 成功生成，Debug/Development/Release 三配置均通过）
- [x] 所有 Bind 接口调用与 null 检查正确
- [x] OnUpdate 刷新逻辑无性能问题（仅活动页面时执行）
- [ ] 运行时验证：进入 GameWorld 后 CombatHud 显示真实角色数据（待运行时验证）
- [ ] 运行时验证：导航到 nav-character-v2 后角色属性页显示真实等级/昵称/阶段/3D 预览（待重新运行游戏验证）
- [ ] 运行时验证：小地图玩家三角形随角色旋转（待运行时验证）
- [ ] 运行时验证：技能槽冷却动画与实际技能冷却同步（待运行时验证）
