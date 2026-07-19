# Checklist

## P0 — 血条与生命值重叠(Task 2)
- [x] `_hpBar` 高度从 12f 改为 18f(常量 `PlayerHpMpBarHeight`)
- [x] `_hpBar.Location = (110, 8)` 保持不变
- [x] `_hpLabel.Location = (110, 8)`(与 `_hpBar` 同位置)
- [x] `_hpLabel.Size = (180, 18)`(与 `_hpBar` 同尺寸)
- [x] `_hpLabel.HorizontalAlignment = TextAlignment.Center`
- [x] `_hpLabel.VerticalAlignment = TextAlignment.Center`
- [x] `_hpLabel.TextColor = InkWashTheme.Paper`

## P0 — 蓝条与法力值重叠(Task 2)
- [x] `_mpBar` 高度从 12f 改为 18f
- [x] `_mpBar.Location = (110, 32)`(从 44 上移到 32)
- [x] `_mpLabel.Location = (110, 32)`(与 `_mpBar` 同位置)
- [x] `_mpLabel.Size = (180, 18)`(与 `_mpBar` 同尺寸)
- [x] `_mpLabel.HorizontalAlignment = TextAlignment.Center`
- [x] `_mpLabel.VerticalAlignment = TextAlignment.Center`
- [x] `_mpLabel.TextColor = InkWashTheme.Paper`

## P0 — XP 条紧凑化(Task 2,用户未要求重叠,仅位置上移)
- [x] `_xpBar.Location = (110, 56)`(从 80 上移到 56)
- [x] `_xpBar` 高度保持 12f(不变)
- [x] `_xpLabel.Location = (110, 70)`(从 94 上移到 70)
- [x] `_xpLabel` 尺寸/对齐/颜色保持原状(Near/Center/TextBrand)

## P0 — 等级与阶段显示在头像正下方(Task 2, Task 3)
- [x] 新增字段 `_levelStageLabel`(Label)
- [x] `_levelStageLabel.Location = (0, 62)`(头像正下方,间隔 6px)
- [x] `_levelStageLabel.Size = (56, 18)`(与头像同宽)
- [x] `_levelStageLabel.HorizontalAlignment = TextAlignment.Center`
- [x] `_levelStageLabel.VerticalAlignment = TextAlignment.Center`
- [x] `_levelStageLabel.Font = FontReference(GetFont(FontRole.Body), 11f)`
- [x] `_levelStageLabel.TextColor = InkWashTheme.TextBrand`
- [x] 文本格式 `Lv.{level} · {stageName}`(如 `Lv.42 · 武侠`)
- [x] 新增 `StageToDisplayName` 方法:Wuxia→武侠, Xianxia→仙侠, Xuanhuan→玄幻

## P0 — 角色名刷新为真实 Nickname(Task 3)
- [x] 新增 `RefreshPlayerIdentity()` 方法
- [x] `BindCharacter` 调用后立即调用 `RefreshPlayerIdentity()`
- [x] `RefreshBoundData` 每帧调用 `RefreshPlayerIdentity()`
- [x] `_boundCharacter == null` 时保持 mock "慕容凌霄"
- [x] `_boundCharacter.Nickname` 非空时刷新 `_playerNameLabel.Text`

## P0 — EnterGameHandler 回填 CharacterAttributesComponent(Task 1)
- [x] 新增 `DeriveStageFromLevel(int level)` 静态方法
- [x] `level < 50` → `CharacterStage.Wuxia`
- [x] `50 <= level < 150` → `CharacterStage.Xianxia`
- [x] `level >= 150` → `CharacterStage.Xuanhuan`
- [x] `OnEnterGameResponse` 中调用 `ApplyLocalPlayerAttributes(nickname, level, stage)`(支持同步/异步双路径)
- [x] `HundunWorldGame.ApplyLocalPlayerAttributes` 方法:同步路径立即回填 `attrComp.Nickname/Level/CurrentStage`
- [x] 异步路径:`CreateLocalPlayerActor` 中挂载 `CharacterAttributesComponent` 后应用缓存属性
- [x] null 检查:`response?.CharacterInfo == null` 或 `attrComp == null` 时跳过并记录警告

## 编译与回归(Task 4)
- [x] `dotnet build HundunWorld/Source/Game.csproj -c Editor.Windows.Development -p:Platform=x64 -t:Rebuild` 0 C# 错误(0 个 CS 错误,仅 MSB3073 部署失败因 Flax Editor 锁定 DLL)
- [x] `Game.CSharp.dll` 已重新生成(用户关闭 Flax Editor 后重新编译可完成部署)
- [x] 队伍卡(`_partyCards`)Lightweight 变体未回归
- [x] 小地图/技能槽/道具栏未修改
- [x] `MainUIManager` 绑定调用未修改
- [x] `MenuCharAttributesV2Page` 未修改
- [x] `InkBar`/`InkVerticalTitle`/`CharacterAttributesComponent` 类未修改
- [x] XP 条样式保持分离(仅位置上移,未改为重叠)
