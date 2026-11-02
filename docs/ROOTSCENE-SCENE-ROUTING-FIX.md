# RootScene 污染修复方案（场景路由统一）

日期：2026-10-07
状态：已实施（D1–D5 均按建议拍板；Game/插件编译 0 错误；闸门 scripts/verify-rootscene-spawn-routing.sh 双向验证通过；待 PIE 人眼复核）

## 1. 现象

PIE 运行态层级树中，RootScene（常驻根场景）下堆积了大量本应属于 World 玩法场景的运行时对象：
Monster_*、NPC_赶集村民/村中铁匠·鲁石/匪徒哨探、TrafficVehicle_1_1/2_2、climbable_village_wall、
climbable_smith_ladder、MusicTrackOne/Two、MusicOverride、NarrativeRuntimeHost 等。

RootScene.scene 资产本身只含全局脚本宿主：RootLifecycle、GameSceneManager、MainUIManager、
UIStateManager、SceneTransitionEffect。以上堆积物全部是运行期动态生成、误落入 RootScene 的。

## 2. 根因（引擎源码级证据）

1. **无父级 spawn 默认落 Scenes[0]**
   `FlaxEngine/Source/Engine/Level/Level.cpp:335-336`：
   `Level.SpawnActor(actor)`（不带 parent/scene）→ `parent = Level::Scenes[0]`。
   RootScene 永远第一个加载（SceneType.Start → Content/Maps/RootScene.scene），
   所以所有"忘了传场景"的生成点全部堆进 RootScene。
   `PrefabManager.SpawnPrefab` 同走此路径。

2. **FlaxActorSyncSystem 的场景判定失效（Monster/远程玩家堆积的直接原因）**
   `Source/Game/FlaxActorSyncSystem.cs:1175-1186` 的 `FindGameWorldScene()` 按
   `scene.Name == "World"` 判定；但 Flax 的 `Scene.Name` 是**根对象名**而非资产文件名
   （World 场景根对象名为 "Scene"，编辑器里显示 "Scene (World)"），该判定恒为 null →
   生成后不搬移 → 留在 RootScene。跨会话残留即成幽灵实体。
   （同一坑已有项目记忆：`flax-scene-name-is-root-actor-not-asset`。）

3. **NarrativePro 插件全部运行期生成都不传场景**
   运行期宿主（NarrativeRuntimeBootstrap.cs:327）、NPC 宿主
   （NarrativeCharacterSubsystem.cs:370）、载具（VehicleSpawnerSubsystem.cs:242）、
   攀爬物、投射物、对白/ Foley 音效等均调无场景版 `Level.SpawnActor`。
   插件内其实已有可靠的玩法场景判定逻辑
   （NarrativeRuntimeBootstrap.IsGameplayScene / ResolveSceneAssetPath，按资产路径判定），
   只是生成 Actor 时没有用上。

## 3. 修复方案

### 3.1 统一口径：三类对象的归属

| 类别 | 归属 | 例子 |
|---|---|---|
| 全局共享脚本/宿主（常驻 RootScene） | RootScene（保持） | GameSceneManager 兜底宿主、MainUICanvas、ToastCanvas、GameMainUICanvas、InkWashUICanvas、SceneTransitionEffect、NarrativeSaveSubsystemHost |
| 玩法世界对象 | 当前玩法场景（World 等） | Monster_*、RemotePlayer_*、NPC_*、TrafficVehicle_*、climbable_*、技能特效、伤害数字、本地玩家 |
| 随宿主存亡的附属对象 | **挂到宿主 Actor 之下**（Parent），不再平铺场景根 | BGM/环境音 AudioSource、预览 rig |

判定原则：对象生命周期是否绑定某个玩法场景。绑定 → 进该场景；全局 → 留 RootScene；
纯附属组件 → 成为宿主子级。

### 3.2 Game 侧（Source/Game）

- `HundunWorldGame.FindGameWorldScene()`（2695 行，三级判定：WorldSceneInitializer 脚本 →
  Terrain → 资产路径）提升为 public static，作为全 Game 程序集唯一入口；
  第三级由 `Scene.Name=="World"` 改为按资产路径 `Content/Maps/World.scene` 判定（Content.GetAssetInfo 反查）。
- 删除 `FlaxActorSyncSystem.FindGameWorldScene()` 坏名字版，改调统一入口。
- 新增小工具 `SpawnUtil.SpawnToGameplayScene(Actor)`：解析玩法场景 → `Level.SpawnActor(actor, scene)`；
  解析失败时 LogError（大声，不静默）并退回现状。
- 逐点修复无场景生成：
  - SkillEffectManager.cs:498、DamageNumberSystem.cs:55 → 玩法场景
  - MusicManager.cs:204/359/431、GameAudioManager.cs:137/192 → AudioSource 改为 `Parent = Actor`（宿主子级）
  - UIFactory.cs:88、GameMainUI.cs:386、UICharacterCameraSystem.cs:233/401/423、
    AtmosphericParticleEffect.cs:131、HundunWorldGame.cs:2323/2758 等 fallback 分支 → 补 LogError + 显式路由
  - AuthenticationUI.cs:1054、UIHelper.cs:630/654（MainUICanvas/ToastCanvas）→ 全局 UI，保留 RootScene，
    但改为显式 `Level.SpawnActor(canvas, rootScene)` 并注释"有意常驻"，不再依赖默认落点。

### 3.3 NarrativePro 插件侧

- 新增 `NarrativeSceneRouter`（静态）：
  - `NarrativeRuntimeBootstrap.InitializeForScene` 成功装配时记录 `(scene, sceneAssetId)`；
  - `TryGetGameplayScene(out Scene)` 对外提供当前玩法场景（含 IsGameplayScene 校验与卸载后置空）。
- 世界对象生成点改传场景：NarrativeCharacterSubsystem.cs:370（NPC 宿主）、
  VehicleSpawnerSubsystem.cs:242（载具）、ClimbableActor 生成路径、
  AbilityTask_SpawnProjectile.cs:84、NarrativeFoleyPlayback.cs:106/267、
  NarrativeWeaponFXPlayback、Dialogue.cs:1291、PlaySoundEvent 等。
  解析不到玩法场景 → LogError 并跳过生成（这些系统本就只在玩法场景激活）。
- NarrativeRuntimeHost：InitializeForScene 的语义本就是"按玩法场景装配/Teardown"，
  生成时补传 `scene`，使其随场景卸载自然销毁，不再滞留 RootScene。
- NarrativeSaveSubsystemHost / 全局 HUD Canvas：跨场景持久宿主，保留 RootScene，显式传场景。

### 3.4 永久防线（防复发）

1. **静态扫描闸门**（新增脚本，接入现有门禁体系）：
   扫描 `Source/Game` 与 `Plugins/*/Source` 中 `Level.SpawnActor(` 单参调用与
   `PrefabManager.SpawnPrefab` 后不搬移的生成点，白名单外即 FAIL，报错信息写明
   "RootScene 只允许全局共享宿主；世界对象必须显式传玩法场景"。
2. **项目记忆**：RootScene 归属口径 + 引擎默认落点 Level.cpp:335 证据 + Scene.Name 陷阱链接。
3. 文档：本方案归档。

## 4. 验证

- `dotnet build`（--no-incremental）过编译门。
- 静态闸门自跑：改前 FAIL（复现堆积点）、改后 PASS。
- PIE 由用户执行：进 World 后 RootScene 下应只剩
  RootLifecycle / GameSceneManager / MainUICanvas（及全局 UI Canvas）/ SceneTransitionEffect /
  UIStateManager / NarrativeSaveSubsystemHost 等全局项；Monster/NPC/载具/攀爬物全部出现在 Scene (World) 下。

## 5. 决策清单（待拍板）

| # | 问题 | 建议 |
|---|---|---|
| D1 | 全局持久 UI（MainUICanvas/ToastCanvas/GameMainUICanvas/InkWashUICanvas）留在 RootScene？ | 留（符合既有 persist-mainui spec），改为显式传场景+注释 |
| D2 | BGM/环境音 AudioSource 归属 | 挂到各自宿主 Actor 子级（不再平铺 RootScene 根） |
| D3 | NarrativeRuntimeHost 归属 | 进玩法场景（与其按场景装配/Teardown 的既有语义一致） |
| D4 | 玩法场景解析失败时：A 大声报错+按现状落 RootScene；B 报错+跳过生成 | Game 侧 A（保证可见性优先），插件侧 B（本就只在玩法场景激活） |
| D5 | 修复范围 | Game + NarrativePro 全量（含闸门），一次到位 |
