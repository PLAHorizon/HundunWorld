# Tasks

- [x] Task 1: EnterGameHandler 回填 CharacterAttributesComponent
  - [x] SubTask 1.1: 读取 [EnterGameHandler.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/Network/MessageHandlers/EnterGameHandler.cs) 定位 `OnEnterGameResponse` 方法,确认 `LocalPlayerActor` 创建位置
  - [x] SubTask 1.2: 新增私有静态方法 `DeriveStageFromLevel(int level)` 返回 `CharacterStage`:`level < 50 → Wuxia`,`50 <= level < 150 → Xianxia`,`level >= 150 → Xuanhuan`
  - [x] SubTask 1.3: 在 `LocalPlayerActor` 创建后,获取其 `CharacterAttributesComponent`(参考 `MainUIManager.TryGetLocalPlayerAttributes` 的获取方式 `actor.GetScript<CharacterAttributesComponent>()`),从 `response.CharacterInfo` 回填:
    - `attrComp.Nickname = response.CharacterInfo.CharacterName`
    - `attrComp.Level = response.CharacterInfo.Level`
    - `attrComp.CurrentStage = DeriveStageFromLevel(response.CharacterInfo.Level)`
  - [x] SubTask 1.4: 添加 null 检查(`response?.CharacterInfo == null` 或 `attrComp == null` 时跳过回填并记录警告日志)
  - [x] SubTask 1.5: 编译验证
  - [x] SubTask 1.6(实施时发现需要): 由于 `RequestCreateLocalPlayerActor` 可能异步创建 Actor,在 `HundunWorldGame.cs` 新增 `ApplyLocalPlayerAttributes(nickname, level, stage)` 方法支持同步/异步双路径回填(同步路径立即回填,异步路径缓存待 Actor 创建后应用)

- [x] Task 2: CombatHudV2Page 新增等级阶段标签与布局调整
  - [x] SubTask 2.1: 读取 [CombatHudV2Page.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/Combat/CombatHudV2Page.cs) 第 56-84 行布局常量区与第 408-518 行 `BuildPlayerStats` 方法
  - [x] SubTask 2.2: 在布局常量区新增 `private const float PlayerHpMpBarHeight = 18f;`(HP/MP 条高度,12→18),保留原 `PlayerBarHeight = 12f` 不删除(XP 条仍用)
  - [x] SubTask 2.3: 在子控件引用区新增字段 `private Label _levelStageLabel;`(等级阶段标签)
  - [x] SubTask 2.4: 在 `BuildPlayerStats` 中,头像按钮创建后、竖排角色名之前,新增 `_levelStageLabel` 创建代码
  - [x] SubTask 2.5: 修改 `_hpBar` 高度从 `PlayerBarHeight` 改为 `PlayerHpMpBarHeight`(18f),位置保持 (110, 8)
  - [x] SubTask 2.6: 修改 `_hpLabel` 位置为 `new Float2(110f, 8f)`,尺寸为 `new Float2(PlayerBarWidth, PlayerHpMpBarHeight)`,`HorizontalAlignment = TextAlignment.Center`,`VerticalAlignment = TextAlignment.Center`,`TextColor = InkWashTheme.Paper`
  - [x] SubTask 2.7: 修改 `_mpBar` 位置为 `new Float2(110f, 32f)`,高度改为 `PlayerHpMpBarHeight`(18f)
  - [x] SubTask 2.8: 修改 `_mpLabel` 位置为 `new Float2(110f, 32f)`,尺寸为 `new Float2(PlayerBarWidth, PlayerHpMpBarHeight)`,`Center/Center`,`Paper`
  - [x] SubTask 2.9: 修改 `_xpBar` 位置为 `new Float2(110f, 56f)`,高度保持 `PlayerBarHeight`(12f)
  - [x] SubTask 2.10: 修改 `_xpLabel` 位置为 `new Float2(110f, 70f)`,尺寸/对齐/颜色保持原状
  - [x] SubTask 2.11: 编译验证

- [x] Task 3: CombatHudV2Page 新增 RefreshPlayerIdentity 与 StageToDisplayName
  - [x] SubTask 3.1: 新增私有静态方法 `StageToDisplayName(CharacterStage stage)`:Wuxia→武侠/Xianxia→仙侠/Xuanhuan→玄幻
  - [x] SubTask 3.2: 新增私有方法 `RefreshPlayerIdentity()`:刷新角色名/等级/阶段标签
  - [x] SubTask 3.3: 修改 `BindCharacter` 方法,在 `_boundCharacter = component;` 后调用 `RefreshPlayerIdentity()`
  - [x] SubTask 3.4: 修改 `RefreshBoundData()` 方法,在方法开头调用 `RefreshPlayerIdentity()`
  - [x] SubTask 3.5: 在 mock 数据区新增 `private int _mockLevel = 42;` 与 `private CharacterStage _mockStage = CharacterStage.Wuxia;`
  - [x] SubTask 3.6: 编译验证

- [x] Task 4: 整体编译验证与回归检查
  - [x] SubTask 4.1: 执行 `dotnet build HundunWorld/Source/Game.csproj -c Editor.Windows.Development -p:Platform=x64 -t:Rebuild`
  - [x] SubTask 4.2: 确认 0 C# 错误(0 个 CS 错误,仅 MSB3073 部署失败因 Flax Editor 锁定 DLL,非代码问题)
  - [x] SubTask 4.3: 确认 `Game.CSharp.dll` 已重新生成(用户关闭 Flax Editor 后重新编译可完成部署)
  - [x] SubTask 4.4: 代码审查确认无回归(12/12 检查项全部通过):
    - 队伍卡(`_partyCards`)Lightweight 变体保留
    - 小地图/技能槽/道具栏未修改
    - `MainUIManager` 绑定调用未修改
    - `MenuCharAttributesV2Page` 未修改
    - XP 条样式保持分离(仅位置上移)
    - `InkBar`/`InkVerticalTitle`/`CharacterAttributesComponent` 类未修改

# Task Dependencies

- [Task 1] 修改 EnterGameHandler.cs + HundunWorldGame.cs,独立(与 Task 2/3 并行)
- [Task 2] 修改 CombatHudV2Page.cs 布局
- [Task 3] 修改 CombatHudV2Page.cs 方法,依赖 [Task 2] 完成(同一文件顺序处理,由同一 Sub-Agent 一并完成)
- [Task 4] 依赖 [Task 1-3] 全部完成 — 整体编译验证
