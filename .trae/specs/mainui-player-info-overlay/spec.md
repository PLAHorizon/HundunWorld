# 主界面玩家信息重叠显示 Spec

## Why

当前战斗 HUD 左下角玩家状态面板(`CombatHudV2Page.BuildPlayerStats`)存在4项与用户预期不符的问题:

1. **血条与生命值数值分离** — `_hpBar` 在 (110, 8) 高12px,`_hpLabel` 在 (110, 22) 高16px,两者垂直分离14px,数值显示在条下方而非"叠"在条上,视觉割裂。用户要求"血条和生命值重叠"。
2. **蓝条与法力值数值分离** — `_mpBar` 在 (110, 44) 高12px,`_mpLabel` 在 (110, 58) 高16px,同样分离。用户要求"蓝条和法力值重叠"。
3. **角色名为 mock "慕容凌霄"** — [CombatHudV2Page.cs:178](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/Combat/CombatHudV2Page.cs#L178) 硬编码 mock,`BindCharacter` 与 `RefreshBoundData` 均未刷新 `_playerNameLabel.Text`。`CharacterAttributesComponent.Nickname` 字段已存在(默认"无名侠"),但 `EnterGameHandler` 收到服务端 `CharacterInfo.CharacterName` 后**未回填**到 `CharacterAttributesComponent.Nickname`,导致 UI 始终显示 mock。用户要求"角色名改为实际进入游戏的角色名"。
4. **无等级与阶段显示** — 当前 `_playerStats` 容器只有头像/竖排角色名/HP条/MP条/XP条,**完全没有等级(Level)与阶段(Stage)控件**。`CharacterAttributesComponent.Level`(int)与 `CurrentStage`(枚举 `CharacterStage: Wuxia/Xianxia/Xuanhuan`)字段已存在但未被消费。用户要求"角色等级和阶段展示在角色头像正下方"。

## What Changes

### P0 — 血条与生命值重叠
- `_hpBar` 高度从 `PlayerBarHeight=12f` 增加到 `18f`(独立常量 `PlayerHpMpBarHeight = 18f`)
- `_hpLabel` 位置与尺寸改为与 `_hpBar` 完全重合:`Location = (110, 8)`,`Size = (180, 18)`
- `_hpLabel` 水平居中(`HorizontalAlignment = Center`)+ 垂直居中(`VerticalAlignment = Center`),字号 11f,颜色 `InkWashTheme.Paper`(浅色,在朱红填充上可读)
- **BREAKING**:HP 数值从"条下方独立行"变为"叠在条上居中"

### P0 — 蓝条与法力值重叠
- `_mpBar` 高度从 12f 增加到 18f,位置上移到 `(110, 32)`(因 HP 区块紧凑化)
- `_mpLabel` 位置与尺寸改为与 `_mpBar` 完全重合:`Location = (110, 32)`,`Size = (180, 18)`
- `_mpLabel` 水平+垂直居中,字号 11f,颜色 `InkWashTheme.Paper`
- **BREAKING**:MP 数值从"条下方独立行"变为"叠在条上居中"

### P0 — XP 条紧凑化(保持分离样式,仅位置上移)
- `_xpBar` 位置从 `(110, 80)` 上移到 `(110, 56)`(因 HP/MP 区块紧凑化节省24px)
- `_xpLabel` 位置从 `(110, 94)` 上移到 `(110, 70)`
- XP 条高度保持 12f,数值保持下方独立行样式(用户未要求 XP 重叠,严格按用户要求只改 HP/MP)

### P0 — 角色名刷新为真实角色名
- `CombatHudV2Page.BindCharacter` 在保存 `_boundCharacter` 引用后,立即调用 `RefreshPlayerIdentity()` 刷新角色名(避免等下一帧 Update)
- 新增私有方法 `RefreshPlayerIdentity()`:若 `_boundCharacter != null` 且 `!string.IsNullOrEmpty(_boundCharacter.Nickname)`,则 `_playerNameLabel.Text = _boundCharacter.Nickname`
- `RefreshBoundData` 每帧也调用 `RefreshPlayerIdentity()`(防止运行时改名)
- mock 字段 `_playerName = "慕容凌霄"` 保留作为兜底(未绑定时显示),不删除

### P0 — 数据源回填:EnterGameHandler 写入 CharacterAttributesComponent
- `EnterGameHandler.OnEnterGameResponse` 收到 `EnterGameResponse.CharacterInfo` 后,在创建 `LocalPlayerActor` 之后,获取其 `CharacterAttributesComponent` 并回填:
  - `attrComp.Nickname = response.CharacterInfo.CharacterName`
  - `attrComp.Level = response.CharacterInfo.Level`
  - `attrComp.CurrentStage = DeriveStageFromLevel(response.CharacterInfo.Level)`(服务端协议暂无 Stage 字段,按 Level 推算)
- 新增私有静态方法 `DeriveStageFromLevel(int level)`:
  - `level < 50` → `CharacterStage.Wuxia`
  - `50 <= level < 150` → `CharacterStage.Xianxia`
  - `level >= 150` → `CharacterStage.Xuanhuan`
  - 推算阈值参考 `CharacterStage` 枚举注释(武侠/仙侠/玄幻三阶段)

### P0 — 等级与阶段显示在头像正下方
- 新增私有字段 `_levelStageLabel`(Label 类型)
- 在 `BuildPlayerStats` 中创建,位置 `(0, 62)`,尺寸 `(56, 18)`(与头像同宽,头像正下方,间隔 6px)
- 字号 11f,字体 `InkWashTheme.FontRole.Body`,颜色 `InkWashTheme.TextBrand`(鎏金)
- 水平居中 + 垂直居中
- 文本格式:`$"Lv.{level} · {stageName}"`,如 `"Lv.42 · 武侠"`
- 新增私有静态方法 `StageToDisplayName(CharacterStage stage)`:
  - `Wuxia` → `"武侠"`
  - `Xianxia` → `"仙侠"`
  - `Xuanhuan` → `"玄幻"`
- `RefreshPlayerIdentity()` 同时刷新 `_levelStageLabel.Text`(角色名/等级/阶段统一在身份刷新方法中处理,避免散落)

## Impact

- **Affected specs**:
  - `beautify-mainui-scene-friendly`(刚完成,本轮在其 Lightweight 队伍卡基础上调整玩家面板布局,不破坏场景透出效果)
  - `wire-mainui-data-binding`(数据绑定基础):本轮扩展 `CombatHudV2Page.BindCharacter` 行为,从"仅保存引用"变为"保存引用 + 立即刷新身份信息",向后兼容
  - `land-inkwash-ui-foundation`(Ink 组件库):不修改 InkBar/InkVerticalTitle/Label 等组件,仅调整使用方式
  - `audit-enter-game-network-sync`(进入游戏网络同步):本轮在 `EnterGameHandler` 中新增 `CharacterAttributesComponent` 回填逻辑,补充了之前缺失的"服务端数据 → 客户端组件"链路
- **Affected code**:
  - [CombatHudV2Page.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/Combat/CombatHudV2Page.cs) — 新增常量 `PlayerHpMpBarHeight=18f`、新增字段 `_levelStageLabel`、重写 `BuildPlayerStats` 中 HP/MP/XP 控件位置与尺寸、新增 `RefreshPlayerIdentity`/`StageToDisplayName` 方法、扩展 `BindCharacter`/`RefreshBoundData`
  - [EnterGameHandler.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/Handlers/EnterGameHandler.cs) — 在 `OnEnterGameResponse` 中新增 `CharacterAttributesComponent` 回填逻辑、新增 `DeriveStageFromLevel` 静态方法
- **不做**:
  - 不修改 `InkBar` 类本身(继承 ContainerControl 已支持,且当前项目惯例是平级 Label)
  - 不修改 `InkVerticalTitle` 类(`Text` 属性已支持运行时修改)
  - 不修改 `CharacterAttributesComponent` 类(字段已齐全)
  - 不修改 XP 条样式(用户未要求,严格按用户需求只改 HP/MP)
  - 不修改队伍卡(`_partyCards`)样式(用户未要求)
  - 不扩展服务端 `CharacterInfo` 协议(暂用 Level 推算 Stage,服务端协议扩展留作后续)
  - 不修改 `MenuCharAttributesV2Page`(其已有等级/阶段显示,本轮只补主UI)

## ADDED Requirements

### Requirement: HP 条与生命值数值重叠显示

`CombatHudV2Page.BuildPlayerStats` SHALL 将 `_hpBar` 与 `_hpLabel` 设置为相同的位置与尺寸(`Location=(110,8)`,`Size=(180,18)`),`_hpLabel` 水平居中+垂直居中显示在 `_hpBar` 之上,使生命值数值"叠"在血条上而非显示在条下方。

#### Scenario: HP 条与数值重叠渲染
- **WHEN** `BuildPlayerStats` 创建 `_hpBar` 与 `_hpLabel`
- **THEN** `_hpBar.Location = (110, 8)`,`_hpBar.Size = (180, 18)`
- **AND** `_hpLabel.Location = (110, 8)`,`_hpLabel.Size = (180, 18)`
- **AND** `_hpLabel.HorizontalAlignment = TextAlignment.Center`
- **AND** `_hpLabel.VerticalAlignment = TextAlignment.Center`
- **AND** `_hpLabel.TextColor = InkWashTheme.Paper`
- **AND** `_hpLabel.Font = FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f)`

### Requirement: MP 条与法力值数值重叠显示

`CombatHudV2Page.BuildPlayerStats` SHALL 将 `_mpBar` 与 `_mpLabel` 设置为相同的位置与尺寸(`Location=(110,32)`,`Size=(180,18)`),`_mpLabel` 水平居中+垂直居中显示在 `_mpBar` 之上。

#### Scenario: MP 条与数值重叠渲染
- **WHEN** `BuildPlayerStats` 创建 `_mpBar` 与 `_mpLabel`
- **THEN** `_mpBar.Location = (110, 32)`,`_mpBar.Size = (180, 18)`
- **AND** `_mpLabel.Location = (110, 32)`,`_mpLabel.Size = (180, 18)`
- **AND** `_mpLabel.HorizontalAlignment = TextAlignment.Center`
- **AND** `_mpLabel.VerticalAlignment = TextAlignment.Center`
- **AND** `_mpLabel.TextColor = InkWashTheme.Paper`

### Requirement: 等级与阶段显示在头像正下方

`CombatHudV2Page` SHALL 新增 `_levelStageLabel`(Label),位置在头像正下方(`Location=(0,62)`,`Size=(56,18)`),显示格式 `Lv.{level} · {stageName}`,阶段名通过 `StageToDisplayName` 转换为中文。

#### Scenario: 等级阶段标签创建
- **WHEN** `BuildPlayerStats` 创建 `_levelStageLabel`
- **THEN** `_levelStageLabel.Location = (0, 62)`
- **AND** `_levelStageLabel.Size = (56, 18)`
- **AND** `_levelStageLabel.HorizontalAlignment = TextAlignment.Center`
- **AND** `_levelStageLabel.VerticalAlignment = TextAlignment.Center`
- **AND** `_levelStageLabel.Font = FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f)`
- **AND** `_levelStageLabel.TextColor = InkWashTheme.TextBrand`

#### Scenario: 阶段枚举转中文名
- **WHEN** 调用 `StageToDisplayName(CharacterStage.Wuxia)`
- **THEN** 返回 `"武侠"`
- **WHEN** 调用 `StageToDisplayName(CharacterStage.Xianxia)`
- **THEN** 返回 `"仙侠"`
- **WHEN** 调用 `StageToDisplayName(CharacterStage.Xuanhuan)`
- **THEN** 返回 `"玄幻"`

### Requirement: 角色名刷新为真实 Nickname

`CombatHudV2Page.BindCharacter` SHALL 在保存 `_boundCharacter` 引用后立即调用 `RefreshPlayerIdentity()` 刷新角色名标签,`RefreshBoundData` 每帧也调用 `RefreshPlayerIdentity()`。`RefreshPlayerIdentity` 在 `_boundCharacter != null` 且 `Nickname` 非空时设置 `_playerNameLabel.Text = Nickname`。

#### Scenario: 绑定角色时刷新角色名
- **WHEN** 调用 `BindCharacter(component)` 且 `component.Nickname = "李逍遥"`
- **THEN** `_playerNameLabel.Text` 立即变为 `"李逍遥"`(不等下一帧 Update)
- **AND** 后续每帧 `RefreshBoundData` 也调用 `RefreshPlayerIdentity` 保持同步

#### Scenario: 未绑定或 Nickname 为空时保留 mock
- **WHEN** `_boundCharacter == null`
- **THEN** `_playerNameLabel.Text` 保持构造时的 mock 值 `"慕容凌霄"`
- **WHEN** `_boundCharacter.Nickname` 为空字符串或 null
- **THEN** `_playerNameLabel.Text` 保持原值不覆盖

### Requirement: EnterGameHandler 回填 CharacterAttributesComponent

`EnterGameHandler.OnEnterGameResponse` SHALL 在 `LocalPlayerActor` 创建后,获取其 `CharacterAttributesComponent` 并从 `EnterGameResponse.CharacterInfo` 回填 `Nickname`/`Level`/`CurrentStage`。`CurrentStage` 按 Level 推算(因服务端协议暂无 Stage 字段)。

#### Scenario: 收到 EnterGameResponse 后回填
- **WHEN** `EnterGameHandler` 收到 `EnterGameResponse` 且 `CharacterInfo.CharacterName = "李逍遥"`,`CharacterInfo.Level = 42`
- **THEN** `LocalPlayerActor` 的 `CharacterAttributesComponent.Nickname` 被设置为 `"李逍遥"`
- **AND** `CharacterAttributesComponent.Level` 被设置为 `42`
- **AND** `CharacterAttributesComponent.CurrentStage` 被设置为 `CharacterStage.Wuxia`(42 < 50)

#### Scenario: Level 推算 Stage 规则
- **WHEN** `level = 1` 调用 `DeriveStageFromLevel(1)`
- **THEN** 返回 `CharacterStage.Wuxia`
- **WHEN** `level = 49` 调用 `DeriveStageFromLevel(49)`
- **THEN** 返回 `CharacterStage.Wuxia`
- **WHEN** `level = 50` 调用 `DeriveStageFromLevel(50)`
- **THEN** 返回 `CharacterStage.Xianxia`
- **WHEN** `level = 149` 调用 `DeriveStageFromLevel(149)`
- **THEN** 返回 `CharacterStage.Xianxia`
- **WHEN** `level = 150` 调用 `DeriveStageFromLevel(150)`
- **THEN** 返回 `CharacterStage.Xuanhuan`
- **WHEN** `level = 300` 调用 `DeriveStageFromLevel(300)`
- **THEN** 返回 `CharacterStage.Xuanhuan`

## MODIFIED Requirements

### Requirement: CombatHudV2Page 玩家面板布局

**原有**(`beautify-mainui-scene-friendly` 之后):
- `_hpBar` (110, 8) 180x12,`_hpLabel` (110, 22) 180x16(分离)
- `_mpBar` (110, 44) 180x12,`_mpLabel` (110, 58) 180x16(分离)
- `_xpBar` (110, 80) 180x12,`_xpLabel` (110, 94) 180x16
- 无等级阶段标签

**修改为**:
- `_avatarButton` (0, 0) 56x56(不变)
- `_levelStageLabel` (0, 62) 56x18(新增,头像正下方)
- `_playerNameLabel` (70, 0) 30x120(不变)
- `_hpBar` (110, 8) 180x18(高度12→18)
- `_hpLabel` (110, 8) 180x18(与HP条重叠,居中)
- `_mpBar` (110, 32) 180x18(Y从44→32,高度12→18)
- `_mpLabel` (110, 32) 180x18(与MP条重叠,居中)
- `_xpBar` (110, 56) 180x12(Y从80→56,紧凑化)
- `_xpLabel` (110, 70) 180x16(Y从94→70,紧凑化)

### Requirement: CombatHudV2Page.BindCharacter 行为

**原有**:仅 `_boundCharacter = component;` 保存引用,不刷新任何 UI。

**修改为**:`_boundCharacter = component;` 后立即调用 `RefreshPlayerIdentity()` 刷新角色名/等级/阶段标签,与 `MenuCharAttributesV2Page.BindCharacter` 行为一致。

## REMOVED Requirements

无
