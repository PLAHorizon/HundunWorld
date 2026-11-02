# UI 设计稿示例数据：初值落地与数据源覆盖（2026-09-23）

> 设计稿：`hundun-yy-ui/pages/*.html`（56 页）
> 实现侧：`HundunWorld/Source/Game/UI/Ink/Pages/**`
> 审计脚本：`.compile-temp/data_gap_precise.py`（可复跑）

## 0. 结论先行：示例数据已经落成组件初值

上一轮报告曾把「示例数据未替换为设计稿初值」列为视觉差异主因，这一判断**不成立**。
先前的文案比对脚本用「字面串精确匹配」，而实现侧大量使用插值与合并文本
（如 `"铜钱 12,450"` 对设计稿的 `12,450`），于是把已落地的数据误报成了缺口。

本轮的严格审计只考察**多位数值**（金额／属性／人数／进度／倒计时等），
判定链路为「设计稿 token 精确匹配 → 去掉非数字后匹配 → 都不成立才算缺口」：

| 设计页 | 真缺口 | 判定 |
| --- | --- | --- |
| `menu-char-attributes` | 12,450 / 328 / 156 / 168 / 642 | **排除区**（角色属性，已微调，未改动） |
| `menu-char-attributes-v2` | 52,840 / 12,450 / 28.5 | **排除区**（角色属性 v2，未改动） |
| `menu-equipment` | 12,450 | **排除区**（背包／装备，未改动） |
| `menu-shop` | 8722 | 伪缺口：设计稿 `&#8722;`（减号 HTML 实体） |
| `combat-hud` | 720 | 设计稿为固定示例值，实现改为绑定角色属性组件显示「当前/上限」，保留实现 |

其余 51 页的多位示例数据**均已是组件初值**。逐页抽查证据（读代码确认，非脚本推断）：

- `menu-battle-pass`：Lv.42、铜钱 12,450、元宝 328、28天 06:30:00、1,284,567、Lv.23、
  EXP 1,950/3,000、65%、再获得 1,050 EXP、680 元宝、90 元宝/级、至尊与免费双轨 Lv.20—Lv.29 奖励、
  已领取 6 / 可领取 1 / 未解锁 13、里程碑 Lv.10·20·30·40·50。
- `menu-gacha`：已祈愿 47、小保底 47/90、大保底 0/90、概率 1.20%/8.80%/30.00%/60.00%、
  单抽 160、十连 1600、卡池剩余 12 天 07 时 23 分。
- `menu-livelihood`：采药 Lv.8 2400/3200 … 缝纫 Lv.2 250/1000、配方「已掌握/未解锁 + 产出数量」。
- `shop-rare-items`：Lv.42、12,450、328、02:45:30、共 9 件珍品、6480 元宝。
- `menu-bestiary`：12,580 / 8,320 / 3,450、收集进度 56/120。
- `settings`：分类计数 10/8/12/6/5、分辨率档位、帧率 60 fps、视距 75。
- `photo-mode`：f/2.8 · 1/125s · ISO 400、FOV 50°、曝光 +0.5、焦距 35mm、相册 12。

## 1. 真正缺的是「接入真实数据源时覆盖」

设计初值大多以 `const` 或静态规格数组写死，**没有任何覆盖入口**：
数据源接入后只能改页面源码。全仓原本只有少数页面提供了绑定点：

- `CombatHudPage.BindCharacter(component)`（未绑定时回退 mock，是最完整的样板）
- `RewardLevelUpPage.SetLevelUp`、`PopupPages.SetItem/SetMessage`、`GuideHudPage.SetStep`、`LoadingPages.SetProgress`

## 2. 本轮落地：4 页新增数据绑定 API

约定（与 `CombatHudPage.BindCharacter` 一致）：

1. **字段初值 = 设计稿示例数据**；构建方法直接读字段，页面构造完成即与设计稿一致，无需数据源。
2. **`Bind*` 只写字段并调用私有 `RefreshXxx`**，不重建控件、不改布局。
3. **不调用 `Bind*` 时永远保留设计稿初值**，便于设计比对与无数据场景复现。

| 页面 | 新增 API | 覆盖的数据 |
| --- | --- | --- |
| `Ink/Pages/BattlePass/MenuBattlePassPage.cs` | `BindAccount`、`BindSeasonInfo`、`BindPassProgress`、`BindPassPrices` | 玩家等级与货币、剩余时间与参与人数、通行证等级与经验进度、奖励统计、解锁/升级价格 |
| `Ink/Pages/Gacha/MenuGachaPage.cs` | `BindAccount`、`BindPity`、`BindPoolCountdown`、`BindDrawCost` | 元宝与命星、已祈愿/大小保底/剩余保底、倒计时、单抽与十连价格 |
| `Ink/Pages/Bestiary/MenuBestiaryPage.cs` | `BindCollectionProgress`、`BindCurrency` | 56/120（总进度条 + 详情条 + 「已收录 N 株」文案）、宝玉/铜钱/战功 |
| `Ink/Pages/Combat/MenuBattlePrepPage.cs` | `BindCombatPower`、`BindEquipmentBonus`、`BindStatBars`、`BindSupplies` | 战力 12,450/15,000 与达成率、6 项装备属性加成、气血/内力/体力细条、药品数量 |

实现要点：

- 原 `const` 改为实例字段（不再被编译器内联），百分比/比例改为按字段实时计算，避免初值与字段脱钩。
- `MenuBattlePrepPage.BuildEquipStatRow` 现在返回数值 Label 并留存，使 `BindEquipmentBonus` 能就地刷新。
- 绑定方法均可传「不完整数据」：未传入的项保留原值（`BindEquipmentBonus`/`BindSupplies` 跳过空项）。

## 3. 两处设计稿自身的数据不一致（需产品/策划定夺）

1. **`menu-gacha`**：设计稿「小保底 47/90」与「距离保底还有 3 次」不自洽（90 − 47 = 43）。
   本轮按设计稿示例数据落地为 **3**，并保留 `BindPity(..., pityRemaining)` 显式覆盖位；
   真实数据接入后以数据源为准。
2. **`combat-hud`**：设计稿气血示例为固定值 720，实现改为绑定 `CharacterAttributesComponent`
   显示「当前/上限」。保留实现（更正确），未回退为设计稿固定值。

## 4. 下一批（仍缺覆盖入口的高数据量页）

`menu-livelihood`（技能等级/经验/配方产出）、`menu-sect`（贡献度/已学/任务进度）、
`menu-mail`（未读数/附件/日期）、`menu-team`（队伍人数/成员等级）、`menu-quests`、
`menu-shop`、`menu-time`、`settings`、`photo-mode`、`element-vision`、`multiplayer`。

轻量做法（供参考）：`MenuSectPage` 已有 `_dailyTaskProgress` 等可变字段，接 `Bind*` 成本最低；
`SettingsPage`/`PhotoModePage` 的初值是「控件当前值」（滑块/下拉），适合用
`BindSettings(...)` 一次性写回并触发自身变更回调。

## 5. 验证

```
powershell -ExecutionPolicy Bypass -File .compile-temp/verify-game-compile.ps1
--- 0 个编译错误 ---
warnings: 142      # 与改动前基线一致
```

复跑审计：

```
python .compile-temp/data_gap_precise.py     # 多位数值真缺口
python .compile-temp/data_init_scan.py <page>  # 单页「设计稿数值 vs 实现字面量」对照
```

## 6. 音频设置页（nav-settings-audio）本轮接通真实设置

这一页此前比"示例数据"更糟：**所有行/滑块/开关都没有 `Location`**，全部叠在内容区左上角；
滑块是 `InkBar` 视觉条、开关是两块静态面板，点了什么都不发生。

| 层 | 改动 |
|---|---|
| 布局 | 标题 → 音量组 → 功能组 → 设备组纵向排布；左侧分类标签占 24..224，内容列从 268 起，互不重叠；底部操作条按钮右对齐。顶栏（返回战斗/头像/名字/等级/铜钱/元宝/时辰）与分类标签文字同样补上位置；页面改为 `ImmersiveOwnBack`，隐藏外壳返回钮，避免与顶栏「返回战斗」叠在同一角 |
| 音量 | 5 条改为 Flax `Slider`（0~1），初值读 `GameSettings`；拖动即时写回并刷新百分比 |
| 顶栏 | 共享 `InkPlayerHeaderBinder.Bind(...)`：昵称/等级取本地玩家 `CharacterAttributesComponent`，铜钱取 `ShopClientService.Gold`（`Revision > 0` 才算服务端给过，避免没开过商店就显示"0"）；元宝/时辰暂无数据源，保留设计示例值。另补设计稿的「小游戏」快捷入口 → `mini-game-complete` |
| 分类标签 | 「音效」= 当前页；「画面」→ `nav-settings`；「操作/账号/其他」→ toast「该分类的设置项尚未接入」，不再静默改高亮 |
| 开关 | 新增 `AudioToggle`（40×20 圆角轨道 + 圆形滑块），写回 `UISfxEnabled` / `VoiceChatEnabled` |
| 应用 | `GameStateManager.ApplySettings` 不再为空：Master/Sfx → `GameAudioManager`；BGM → `MusicManager.SetGlobalVolume(BGM×Master)`；环境音 → `AmbientAudioSystem.SetVolume(Ambient×Master)`；UI 音效 → UI 类别音量 0/1 |
| 持久化 | `GameStateManager.Update` 的 5 秒自动保存此前**零调用方**，已在 `HundunWorldGame.OnUpdate` 挂上；「保存设置」立即写盘，「恢复默认」回填控件 |

**已知缺口**：`VoiceChatEnabled` 与「语音」音量目前只持久化——客户端没有语音通话消费方；
等 IM 语音接入后再把它们接到对应通道，页面无需再改。

## 7. 设置主页分类标签不再"假切换"

`SettingsPage.SelectCategory` 原先对「操作/账号/其他」只改高亮，内容仍是画面那 10 行，看起来像"切了但没变"。
现在这三类直接 toast「该分类的设置项尚未接入」且**不改高亮**；「画面」保持原行为，「音效」跳独立音频页。

## 8. 顶栏玩家信息统一绑定（`InkPlayerHeaderBinder`）

此前多个菜单页把「江湖过客 / Lv.42 / 12,450」写死为设计稿示例，玩家登录后顶栏仍是假身份。
新增 `UI/Ink/Components/InkPlayerHeaderBinder.cs`，规则只有一条：**有真值才覆盖，没有就保留示例**。

| 数据 | 来源 | 判定 |
|---|---|---|
| 昵称 / 等级 | 本地玩家 `CharacterAttributesComponent`（`Nickname` / `Level`） | actor/组件为空则跳过 |
| 铜钱 | `ShopClientService.Gold` | `Revision > 0`（服务端确实给过快照）才用；否则 0 不是"余额"而是"没数据" |

本次接入：活动、通行证、休闲模式、多人模式、奇珍阁、设置主页、音频设置、任务、商店、博物志、营生、外观、个人信息、时辰、组队、门派、武林录（17 页）。
前缀由页面传（如通行证 `▲ ` / `铜钱 `、多人 `★ `），避免把"▲"/"铜钱"这类版式差异塞进共享代码。
元宝与时辰目前没有数据源，各页继续显示设计示例值；后加的 10 页多数只有昵称/等级字段，铜钱仍为示例（武林录除外，已绑 `_coinValue`）。

## 9. 好友页接入服务端名单（`FriendClientService`）

`FriendsPage` 此前整页是设计稿静态名单，从未调用已有的 `FriendClientService`。本轮接通：

| 项 | 实现 |
|---|---|
| 数据 | `FriendInfo`（Id/名字/等级/职业/在线）→ 页面条目；按在线优先排序；`Revision` 变化才重建 |
| 初始 | 构造期先放设计稿示例并发一次 `RequestList(force)`；**收到回包（哪怕空名单）后整份替换**，示例不会和真实好友混在一起 |
| 缺失字段 | 所在地/称号/亲密度服务端名单没有 → 显示「—」或留空，不编数字、不画进度条 |
| 分类 | 好友 / 仇人 / 黑名单：后两类没有服务端数据，切过去是空列表 + 「暂未接入服务端」提示，不再拿好友冒充 |
| 删除 | 「删除」调 `RemoveFriend(id)`；不在本地假删，等回包新名单；`Id=0` 的示例条目拒绝删除并提示 |
| 容量 | 左栏一屏最多 12 行（`ClipChildren=true`），超出显示「还有 N 位未显示（滚动待接入）」；滚动尚未实现 |
| 时序 | `BuildLeft` 建行时详情栏尚未创建，`UpdateDetail/ShowEmptyDetail` 对 `_ctr == null` 直接返回；整页构建完再填一次详情，避免构造期 NRE |

**仍未接**：添加好友（服务端有 AddFriend 请求，但页面没有输入目标 id 的入口）、私聊/组队/传送/赠礼动作、亲密度与共同好友（无数据源）。

好友页动作补充（与名单同轮）：

- 「私聊」→ `WhisperRequest`（一次性静态交接）→ 跳 `combat-hud`；传统 HUD 构造完成时消费请求并进入密聊会话。
  不跨页持有 HUD 控件：HUD 会随传统/V2 切换或场景切换被重建。
  已知限制：若当前激活的是 V2（沉浸式）HUD，V2 没有聊天面板、不会消费请求——请求会留到下次打开传统 HUD 时生效。
- 「添加好友」「组队」「传送」「赠礼」没有入口/数据源，点击给出「功能尚未接入」提示，不再静默无反应。

## 10. 传统 HUD 控制状态行（眩晕/沉默/定身…）

左上角色面板原有一行 4 个假 Buff 图标（极/罡/风/+），点了没意义、也不反映任何状态。本轮换成真实控制状态：

| 项 | 实现 |
|---|---|
| 数据源 | `ControlStateManager.GetActiveControlStates(本地角色 id)`：服务端状态位由 `LocalPlayerActorSyncSystem` 镜像（TTL 1.5s，随 1Hz 快照续期），本地技能效果由 `SkillEffectSystem` 施加 |
| 展示 | 最多 6 个 24×34 小方块：状态字（眩/默/定/惧/乱/缴/盲/嘲/眠/冰）+ 剩余秒数 + 按状态着色；无状态时整行留空 |
| 计时 | `ControlStateManager.Update` 此前**零调用方**（状态施加后永不到期），已在 `HundunWorldGame.OnUpdate` 每帧驱动 |
| 刷新 | HUD `Update` 内 0.15s 节流重读快照，不在绘制里查状态 |

**说明**：这一行现在为"实时状态"，不是设计稿的装饰 Buff；没有状态时留白是有意为之。

## 11. 设置主页 7 个画面项：先落库（引擎应用仍待字段确定）

`SettingsPage` 的帧率上限 / 视距 / 特效质量 / 阴影质量 / 抗锯齿 / 自动隐藏UI / 省电模式
此前只打日志，关掉页面即回到设计稿默认。本轮接到**已有的图形后端** `GameConfigurationService`（LiteDB），
没有新增第三套持久化：

| 项 | 配置键 | 初值读取 | 写入时机 |
|---|---|---|---|
| 帧率上限 | `FrameRate` | `GetFrameRate()`（默认 60） | 滑块 `SlidingEnd`（松手一次） |
| 视距 | `ViewDistance` | `GetViewDistance()`（默认 75） | 滑块 `SlidingEnd` |
| 特效质量 | `EffectQuality` | `GetEffectQuality()`（默认 2=高） | 下拉切换即写 |
| 阴影质量 | `ShadowQuality` | `GetShadowQuality()`（默认 2=中） | 下拉切换即写 |
| 抗锯齿 | `AntiAliasing` | `GetAntiAliasing()`（默认开） | 开关切换即写 |
| 自动隐藏UI | `AutoHideUI` | `GetAutoHideUi()`（默认开） | 开关切换即写 |
| 省电模式 | `PowerSave` | `GetPowerSave()`（默认关） | 开关切换即写 |

「恢复默认」通过同一条 `HandleSliderCommitted/HandleToggleChanged` 落库（`Slider.Value` 与
`InkToggle.Checked` 的程序化赋值都不会触发交互事件，所以显式调用）。

**已知边界**：`GameConfigurationService` 的 `Apply*Change` 目前仍是 log-only，这 7 项与既有的
画质/分辨率/垂直同步一样"会记住、但尚未真正改变引擎渲染"。等引擎侧可应用字段确定后统一补。

**顺手修的静默失效**：`GameConfigurationService.GetCategoryForKey` 原先按子串猜类别——
`FrameRate`/`ViewDistance`/`AntiAliasing`/`AutoHideUI`/`PowerSave` 会被推断成 `Gameplay`，
而写入时用的是 `Graphics`；LiteDB 按 (key, category) 查询，于是**同进程靠缓存看得见、重开就永远读不到**。
`MouseSensitivity` 同样写 `Controls`、读 `Gameplay`。现在改为显式键白名单（含 `Fullscreen`）。

## 12. V2 HUD 不再叠一层假技能栏/假道具栏

`MainUIManager` 的滚轮技能栏（10 列 × 6 行，可滚轮翻行）在进入世界后**常驻**挂在 shell 上，
位置是屏幕底部中央（设计空间 `sh-82 .. sh-20`）。V2 页面自己又画了一排 8 格 mock 技能
（`sh-104 .. sh-44`）和键位提示（`sh-38 .. sh-24`）：

- 两者重叠，mock 排会从真实技能栏上方露出约 22px 的假槽位/假键位；
- 右下「道具栏」是可点击的假消耗品（点一下把 mock 数量减一），真实消耗品在滚轮栏/背包里。

处理（只隐藏、不删布局，避免底部系统导航条位置跳动）：

| 控件 | 处理 |
|---|---|
| `_skillBarRow` / `_skillHintRow` | `Visible=false`；真实滚轮技能栏是唯一技能入口 |
| `_itemTitleRow` / `_itemColumn` | `Visible=false`；假点击不再误导 |

**仍为 mock**：无（V2 玩家面板下方原 3 个「攻/防/轻」假 Buff 已替换为真实的控制状态方块，
与传统 HUD 共用 `ControlStateChipView` 组件与 `ControlStateManager` 数据源）。

## 13. 队伍面板与 V2 状态效果：假成员/假增益改为诚实空态

客户端**没有队伍名册服务**（服务端只有部分 `CreateTeam`），两套 HUD 的队伍面板原先写死假队友：

| 位置 | 原状 | 现在 |
|---|---|---|
| 传统 HUD 左上队伍面板 | `4/5` + 青衣客/红袖招/醉道人/玉面狐 4 名假成员与血条 | `0/5` + 一行「未组队」 |
| V2 右上队伍面板 | `队伍 (4/4)` + 3 名假成员卡 | `队伍 (0/4)` + 「未组队」 |
| V2 右上「状态效果」 | 攻击力+10% / 防御力+15% / 中毒·每秒-120（无数据源） | 整块隐藏；真实控制状态已在左下玩家面板显示 |

队伍数据接入后，只需把空态换成按名册重建行（行模板仍在 `BuildPartyMember`，目前未调用）。

**保存按钮改为全量提交**：`SaveAllSettings` 原先只写画质/分辨率/垂直同步；
现在遍历 10 个设置行按类型调用同一条落库路径（下拉→`HandleOptionChanged`、滑块→`HandleSliderCommitted`、
开关→`HandleToggleChanged`），"保存设置"与逐项即时落库的语义一致，也覆盖恢复默认后的值。

## 14. 外壳返回钮与页面自带返回不再叠在同一个角

`InkPageShell` 的返回按钮固定画在内容区左上角（设计坐标 16,16）；而 20+ 个沉浸式页面
在自己的顶栏/侧栏里也画了「返回战斗 / 退出」按钮。两者都注册为 `Immersive`（非 OwnBack）时，
同一个位置会出现两个返回入口（外壳圆钮 + 页面按钮重叠）。

本轮把**确实自带返回入口**的 24 个页面改为 `ImmersiveOwnBack`（拍照页为 `SelfLitImmersiveOwnBack`）：

`nav-shop / nav-social-shop / nav-settings / nav-battle-prep / acupoint / nav-appearance /
nav-activities / nav-bestiary / nav-battle-pass / nav-gacha / nav-personal-info / nav-martial-record /
nav-casual-mode / nav-time / nav-shop-rare / nav-element-vision / guide-hud / nav-skill-tree /
nav-world-map / nav-compass / nav-dungeon-entry / nav-social-guild / nav-friends / nav-photo-mode`

判定方法：`grep BackOneLevel|ExitRequested` 找出有自有返回的页面，再对照 `RegisterPage` 的 frame。
弹层页（`Modal`，如系统菜单）本来就不显示外壳返回，未改；没有自有返回的页面继续用外壳返回。

## 15. 拍照模式从静态 mock 变成取景器（一期）

| 项 | 实现 |
|---|---|
| 舞台 | 新增 `InkPageFrame.ImmersiveTransparent`（`NoBackdrop`）：外壳不铺墨底/暗角，页面背景改为透明并停画设计稿墨晕背景 → 直接透出 3D 场景 |
| 常驻 UI | 页面新增 `OverlayVisibilityRequested`；`MainUIManager` 订阅后把滚轮技能栏/五行气息指示藏起来，退出或页面 `OnDestroy` 时恢复 |
| 相机 | 进入时停掉 `ThirdPersonCamera` 与 `DynamicCameraAdjuster`（两者会每帧写 FOV），记录原 FOV；FOV 滑块直接写 `Camera.MainCamera.FieldOfView`，焦距滑块按设计稿区间（14~200mm ↔ 90°~20°）线性联动 FOV；退出/销毁时恢复原 FOV 与脚本启用状态 |
| 快门 | 点击或空格 → 隐藏全部页面子控件（含闪光层）→ 等 2 帧 → `Screenshot.Capture(Globals.ProjectFolder/Screenshots/Photo_*.png)` → 再多等 1 帧恢复 UI → toast「照片已保存到 Screenshots 文件夹」；截图期间不允许重入 |

**仍为 mock**：曝光滑块的数值只改标签（没有接入后处理曝光）；过滤器只是 UI 选中态；「角度」按钮事件没有订阅者。

## 16. 登录 → 选角流程恢复真实校验（2026-10-29）

**症状**：PIE 启动直接落在选角页（`CcRoleListPage`），没有任何登录动作；进游戏后服务端一路回 `Error Code=1007 鉴权失败`，`TokenRefresh` 报「没有可用的 RefreshToken」。

**根因**：`AuthenticationManager.LoginWithTokenAsync` 在本地拼了一个 `LoginResponse { IsSuccess = true }`，既不连服务端也不校验 Token；`TryAutoLoginFromIniAsync`（读 `HorizonGame.ini`）与 `TryAutoLoginFromSavedAsync` 的 Token 分支都调它，成功后 `AuthenticationController.HideSceneAuthUI()` 收起登录页并按"已登录"请求角色列表。过期的 Token 于是也能"登录成功"。

| 项 | 现状 |
|---|---|
| 假 Token 登录 | `LoginWithTokenAsync` 已删除；全仓无调用 |
| 启动期 | `AuthenticationManager.PrepareLoginPageAsync()` 只做**预填**：优先 `HorizonGame.ini` 的 `PassportId/UserId`，否则本机存档账号；返回 `IsSuccess=false` + "请登录后选择角色" |
| 场景登录页 | `AuthenticationController` 收到失败结果后 `ShowSceneAuthUI()`：恢复 CanvasScaler、预填账号、状态条给提示 |
| 两套登录页 | 运行时 `AuthenticationUI` 会自建第二套登录卡；现在它默认收起，`Show/HideAuthenticationUI` 与 `ShowLoginPanelInternal` 都优先委托给 `AuthenticationController.Current`（场景那套），避免双份叠加 |
| 进入选角 | 只有真实 `LoginResponse.IsSuccess` 才会触发 `LoginResponseHandler`/`CharacterListHandler` 切 `CharacterSelection`，`MainUIManager` 才建水墨选角页 |

**实测（PIE）**：启动 → `LoginPanel visible=True` 且无 `CcRoleListPage`；点「登录」（账号已预填 + 本机记住的密码）→ 服务端 `GengDiAuth 登录成功` → `CcRoleListPage` 出现、登录页 `visible=False`。

**仍未做**：`HorizonGame.ini` 的外部启动器免密链路需要服务端签发可校验会话（当前 Token 只能当预填用）；「记住密码」只影响预填，不再触发自动登录。

### 15.1 一期 PIE 取证（2026-10-29）与随之修掉的两个缺陷

自测通路（运行时 Ink 控件不在场景树里，编辑器工具按类型点不到）：三个标记文件都在 `%APPDATA%/HundunWorld/` 下，正常环境不存在。

| 标记 | 作用 |
|---|---|
| `enter-world.on` | 登录后自动用第一个角色进入游戏（`MainUIManager.AutoEnterWorldForSelfTest`） |
| `open-photo.on` | 本地玩家就绪 3 秒后自动打开拍照页 |
| `photo-selftest.on` | 拍照页内：1.5s 拨 FOV 滑块→3s 拍照→5s 拨到最小→6.5s 再拍→9s 走退出路径；回执写到 `photo-selftest.out` |

取证结果：

- 外壳背景层 `InkBackgroundLayer` 与 `InkVignette` 均 `visible=False`，`InkActionBarControl` 与 `InkWuxingCycleIndicator` 在取景期间 `visible=False`，退出后恢复 `True` 并回到 `CombatHudTraditionalPage`。
- FOV 滑块真的写到相机：回执 `fov slider=0.5 before=50.0 after=55.0`、`slider=0 before=55.0 after=20.0`；同机位两张成片（55°/20°）肉眼可见广角↔长焦差异。
- 快门：`Screenshots/Photo_2026....png` 连续两张、连拍不互相阻塞。**注意**：后续用 `ui-shot.on` 对照发现 `Screenshot.Capture` 在本工程 PIE 下只抓 3D、不含 GUI（同一帧里屏幕上明明有整页 UI，成片里是干净的）——所以"成片没有取景 UI"不能当作"隐藏子控件生效"的证据，那条链路仍只有代码保证。
- 退出：回执 `release restoredFov=60.0`，相机 FOV 回到进入前的 60。

取证暴露并已修的两个缺陷：

1. **相机拿到后又被场景重建掉**：`_cameraAcquired` 还是 true 但 `_photoCamera` 已失效（Flax 对象被销毁后比较为 null），FOV 滑块静默无效。现在 `Update` 里检测 `_cameraAcquired && _photoCamera == null` 就作废重拿，并按 0.5s 节流重试（`_cameraRetryTimer`）。
2. **相机查找只认 `Camera.MainCamera`**：PIE 首帧可能还没注册主相机。现在退到 `ThirdPersonCamera` / `DynamicCameraAdjuster` 所在 Actor 取相机（本项目相机与这两个脚本同 Actor，见 `DynamicCameraAdjuster.OnStart` 的 `Actor.As<Camera>()`）。

仍未接：曝光滑块只改标签（没有后处理曝光）、滤镜只是选中态、「角度」按钮无订阅者；本轮自测的 PIE 世界里 `GetScript<ThirdPersonCamera>()` / `DynamicCameraAdjuster` 都是 null（玩家相机脚本不在该相机 Actor 上），所以"停掉每帧写 FOV 的脚本"这条只在真实玩家相机下才会生效，未取得运行期证据。

## 17. 运行时页面走查通路 + 选角页空列表缺陷（2026-10-29）

Ink 页面是运行时控件、不在场景树里，编辑器侧的 `ui_click_control` 按类型点不到；所以走查只能靠标记文件驱动（都在 `%APPDATA%/HundunWorld/`，正常环境不存在）：

| 标记 | 作用 |
|---|---|
| `enter-world.on` | 登录后自动选中第一个角色进入游戏（等角色列表就绪、等选角页切换落定再点，最多 4 次） |
| `open-page.txt` | 第一行写页面 dom-id；文件变化后 0.5 秒自动导航过去（走查时不必重启 PIE） |
| `photo-selftest.on` | 拍照页自测（见 §15.1） |
| `ui-selftest.out` | 上述自测的落盘回执（本机运行日志不落盘） |

**顺带修掉的缺陷：选角页会渲染成空列表**。`CharacterManager` 只在 `InitializeManager` 里订阅 `UIStateManager.CharacterListUpdated`，不采纳订阅前就到的快照；如果角色列表回执比本单例的首次访问更早，镜像永远为空——选角页 0 行、点「进入游戏」也选不中。回执证据：`enter-world attempt#1 mgr=0 state=3`。修复：初始化时若状态管理器已有列表就先采纳一次（修复后 `mgr=3 state=3`，三行正常渲染）。

走查结果（10 个页面：settings / shop / friends / world-map / quests / inventory / team / mail / achievement / leaderboard）：

- 外壳返回钮只在「页面没有自有返回」时出现（settings/shop/friends/world-map 为 False，其余 True），与 §14 的 `OwnBack` 口径一致。
- 没有整块越界的页面；`RoundedPanel 0×6` 是未激活的下划线类装饰，不可见也不吃事件。
- `cc-face-sculpt` 有 20 行参数在 y>1080，属于 `MetaHumanSculptPanel` 的滚动内容（面板带可见 `VScrollBar`），不是布局溢出。

### 17.1 视觉取证通路与滚轮技能栏越界（同日补）

**UI 截图**：`Screenshot.Capture` 不含 GUI，`viewport_screenshot` 是黑的，编辑器工具又点不到运行时控件——能拿到"游戏画面 + UI"的只有 OS 级窗口抓图：`.compile-temp/window_capture.ps1`（`PrintWindow(PW_RENDERFULLCONTENT)` 抓 FlaxEditor 主窗口，含 Game 面板）。配合 `open-page.txt` 导航即可逐页出图。

**缺陷：滚轮技能栏压在所有整页界面上**。`MainUIManager.UpdateActionBar` 原来的判据只有 `localPlayerReady && !photoOverlayHidden`，于是设置页（铺满 1920 设计空间）打开后，底部 60 格技能栏和五行气息指示仍浮在页面上方，盖住"恢复默认"一行（截图实测）。现在改为只在战斗 HUD 上下文显示：

- 当前页是 `combat-hud` / `combat-hud-v2` / `combat-hud-traditional`，或
- 有弹层且底页是上述三者（`IsOverlayOpen && IsHudPageDomId(_lastKnownDomId)`）。

实测：设置页两项 `visible=False`，回到传统 HUD 后恢复 `True`。

**另注**：抓图里页面右缘看起来被切，是编辑器窗口比 Game 面板窄所致（设计空间按 `min(W/1920,H/1080)` 等比缩放并居中，`ui_tree` 里 `SettingsPage` 仍是完整 1920×1080），不是布局溢出。

## 18. 组队页不再摆假队友/假候选人（2026-10-29）

§13 只把战斗 HUD 的队伍面板改成了诚实空态，`MenuTeamPage`（`nav-team`）仍留着设计稿的三名假队友（无名侠 Lv42 / 剑随风 Lv38 / 花满楼 Lv35）、两个「邀请加入」空位，以及三名假候选（叶知秋/柳如烟/萧无涯）。客户端至今没有 team handler/service，这些数据全部是编的。

现在：

| 区域 | 之前 | 现在 |
|---|---|---|
| 队伍标题 | 逍遥小队 · 3/5 · 编号 #2048 | 未组队 · 0/5 · 编号留空 |
| 成员列表 | 3 张假成员卡 + 2 个邀请空位 | 一行「暂无队友 · 组队功能尚未接入服务端」 |
| 邀请面板 | 3 名假候选 + 邀请按钮 | 「暂无可邀请的队友 · 组队功能尚未接入服务端」 |
| 顶栏 | 无名侠 / Lv.42 / 逍遥派 / 12,450 / 328 | `—`，并由 `InkPlayerHeaderBinder` 绑真实姓名/等级/金币 |

PIE 截图核对：顶栏显示玩家真名 `张青衫`、等级 `Lv.1`，队伍区与邀请区都是上面的空态文案，没有假成员。

**仍缺**：组队协议（服务端 CreateTeam 只部分实现）——接入前这一页只能是空态。

## 19. 博物志不再伪造收集进度（2026-10-29）

`nav-bestiary` 原来把设计稿示例当数据：总进度写死 `56/120`、详情写"已收录 56 株"、花草分类的前 8 张卡显示为"已收录"，顶栏宝玉/铜钱/战功写死 12,580 / 8,320 / 3,450。客户端没有任何图鉴数据源，这些全是编的。

现在：

| 项 | 之前 | 现在 |
|---|---|---|
| 总进度 / 详情进度 | 56/120 | `—`（`_collectedCount/_collectionTotal` 默认 -1，`BindCollectionProgress` 一旦被调用就恢复显示） |
| 详情底部说明 | 已收录 56 株… | 收录进度尚未接入服务端 |
| 副标题 | 收录天下奇珍异物… | 无数据时改为「图鉴数据尚未接入服务端，收录状态暂不可用」，有数据恢复原文案 |
| 卡片 | 花草 8 张显示已收录 | 全部 `???` / 未发现（判定加上 `hasCollectionData` 前置条件） |
| 顶栏货币 | 12,580 / 8,320 / 3,450 | `—`（`BindCurrency` 可覆盖） |

PIE 截图核对：标题下方显示「图鉴数据尚未接入服务端，收录状态暂不可用」，右上总收集进度为 `—`，网格全部 `??? / 未发现`，顶栏是玩家真名。

**仍缺**：图鉴数据源（收集状态与分类内容需服务端下发），接入后调用 `BindCollectionProgress` / `BindCurrency` 即可恢复。

## 20. 设计稿示例值的"假名字/假余额"清扫（2026-10-29）

图鉴页（§19）之后把同类残留一起扫了：这些值只在数据没到位时露出来，但玩家看到的就是"我叫无名侠、我有 12,450 铜钱"。

| 位置 | 之前 | 现在 |
|---|---|---|
| 本地聊天发送者兜底（`ChatClient`） | `无名侠` | `我`（属性组件就绪后仍用真实昵称） |
| 传统 HUD 角色名兜底 | `无名侠` | `—` |
| 战斗 HUD V2 初始等级/血量 | `Lv.42`、`12,450 / 12,450` | `Lv.—`、`— / —` |
| 角色面板货币格（铜币/银两/金锭/元宝） | 12,450 / 3,200 / 85 / 12 | 全部 `—` |
| 活动页 / 休闲页 / 外观页顶栏铜钱 | `12,450` / `铜钱 12,450` | `—` / `铜钱 —` |
| 通行证页等级与余额 | `Lv. 42`、铜钱 12,450、元宝 328 | `Lv. —`、`铜钱 —`、`元宝 —`（字段默认 -1，`Bind*` 覆盖后恢复数字） |
| 外观页 / 图鉴页顶栏名字与等级 | `无名侠` / `Lv.42` / `逍遥派` | `—`（`InkPlayerHeaderBinder` 绑定真实值） |

判据：全部走 `dotnet` 编译 0 错误 + 脚本热重载；角色面板/组队页/图鉴页的运行时截图确认顶栏是玩家真名，其余 `—` 只在数据源缺失时出现。

### 15.2 「角度」按钮接上相机俯仰（2026-10-29）

原先 `AngleClicked` 没有任何订阅者，点「角度」什么都不会发生。现在：

- 进入拍照时记录相机的 `EulerAngles`（`Camera` 在 1.13 里本身就是 Actor，可直接读写变换）；
- 每点一次按 **平视 → 俯 +12° → 仰 12°** 循环，写回 `_savedEuler + (pitch, 0, 0)`；
- 退出/销毁时若改过角度，连同 FOV 一起还原。

自测回执（`photo-selftest.on`）在第二次快门前插入一次「角度」：

```
angle preset=1 pitch=12.0 euler=X:42.00 Y:0.00 Z:0.00
```

两张成片对比：第一张 FOV 55°/平视，第二张 FOV 20°/俯 12°——地面植被明显铺满画面、角色只剩下半身，证明角度预设确实写进了相机。

**仍为 mock**：曝光滑块（只改标签，Flax 相机没有曝光字段，需要 PostFxVolume 才能接）、滤镜（只有选中态）。

## 21. 活动页整页崩在构造中途 + 门派列表两行文字重叠（2026-10-29）

**活动页（nav-activities）在运行时只有横幅和空列表**：`ui_tree` 里只有 1 个 `ActivityEntry`（尺寸还是默认 100×84，没排版），右侧详情面板整块不存在 —— 说明 `MenuActivitiesPage` 构造过程抛异常被 catch 吞掉。

探针（标记文件 `ui-probe.on` → `ui-probe.out`）拿到真因：

```
entry[0] 武林大会 NullReferenceException
```

`ActivityEntry` 构造函数一开始就 `Height = EntryHeight;`，这会立刻触发 `OnSizeChanged`，而该重写里要读 `_rewardIcons.Count` —— 此时字段还没初始化。修复：

- 构造函数把 `Height` 赋值挪到所有子控件建好之后；
- `OnSizeChanged` 增加 `_iconBox == null || _rewardIcons == null` 早退守卫；
- 每个条目的创建包 try/catch，单条坏掉不再拖垮整页（异常写探针 + `Debug.LogError`）。

修复后截图：6 条活动（图标/状态标签/时间/进度条）+ 右侧「武林大会」详情与奖励都正常渲染；同页顶栏也从 `江湖过客` 变成真实昵称（此前 ctor 死在绑定之前）。

**门派列表（nav-sect）每行两行文字叠在一起**：布局代码只在子控件是 `InkAvatar` 时给徽记让位，而徽记底其实是个 `ContainerControl`，于是门派名和描述都从 x=12 起、y 只差 10px。改为按下标定位（0=徽记底 1=门派名 2=描述 3=加入状态），名字/描述上下两行、右侧状态对齐。修复前后截图对比明显。

**探针保留**：只有 `%APPDATA%/HundunWorld/ui-probe.on` 存在时才写文件，正常环境零影响——这台机器的编辑器日志不落盘，下次"整页缺一块"还能靠它定位。

## 22. 通行证页顶栏把"通行证等级"写进了"玩家等级"格（2026-10-29）

`MenuBattlePassPage.RefreshAccountData` 用页面字段 `_playerLevel` 直接刷新顶栏等级标签。这个字段本意是玩家等级（`BindAccount(int playerLevel, …)` 的参数名），但页面自己还有一个设计稿示例的通行证等级 `_passLevel = 23`；结果顶栏 `张青衫 [Lv. 23]` 与真实玩家等级（Lv.1）不符，通行证等级和玩家等级混成一个数。

改法：顶栏这一格优先走统一绑定 `InkPlayerHeaderBinder.BindLevel(_playerLevelLabel, "▲ ")` 拿真实玩家等级；绑不到再退回 `_playerLevel`，最后才是 `▲ Lv. —`。页面头部的「当前等级 Lv. 23」仍由 `_passLevel` 驱动，两者不再互相覆盖。

PIE 截图核对：顶栏 `张青衫 ▲ Lv. 1`，页面内「当前等级 Lv. 23 / 1,750 / 2,000」保持不变。

**仍为设计示例**：通行证等级 23、经验 1,750/2,000、赛季剩余时间与参与人数、等级轨道各档奖励文案——等赛季数据源接入后由 `Bind*` 覆盖。

## 23. 个人信息页接真实身份、其余改占位（2026-10-29）

`nav-personal-info` 原来整页照抄设计稿：竖排大字`无名侠`、基本信息 `42 / 逍遥派 / 男 / 2025-01-15`、称号 `剑客初成/逍遥弟子/江湖新秀`、统计 `128 小时 / 击败 3,420 …`、成就进度 `145/320`、顶栏 `12,450 / 328`。

现在（没有数据源的项一律「—」，不再编数）：

| 项 | 现在 |
|---|---|
| 竖排姓名 / 顶栏门派 | 真实昵称；门派取当前角色职业（无门派显示「无门派」） |
| 基本信息 等级 / 门派 / 性别 / 创建日期 | `Lv. 1` / `无门派` / `—` / `—` |
| 称号 | 空态「暂无称号」 |
| 统计六项 | 全部 `—`、单位清空 |
| 成就进度 | 数字 `—`、进度条 0 |
| 顶栏铜钱/元宝 | `—` |

实现要点：这些数组在 `Build*` 阶段就被直接读成文本，所以新增 `PrepareRealData()` 在**建控件之前**填好数组/身份字段，构建完再把竖排名与门派标签补上。

PIE 截图核对：竖排大字为 `张青衫`，基本信息 `Lv. 1 / 无门派 / —`，顶栏 `张青衫 Lv.1 无门派`。

**仍缺**：性别、创建日期、游戏时长/击败/任务/探索等统计、成就进度——都需要服务端角色档案数据。

## 24. 顶栏"门派"接真实职业 + 各页身份/货币默认值统一为「—」（2026-10-29）

此前只有昵称/等级/铜钱走 `InkPlayerHeaderBinder`，**门派**仍是各页写死的 `逍遥派`，铜钱在没开过商店时也显示设计稿的 `12,450 / 8,320 / 328 / 15`。

改动：

1. `InkPlayerHeaderBinder` 新增 `BindSect(Label)`：取 `CharacterManager.SelectedCharacter.Profession` → `CcSectNames`；没选门派显示「无门派」，连角色都没有显示「—」。
2. 八个带门派标签的页面在 `Bind(...)` 后补 `BindSect(...)`：外观、博物志、营生、武林录、任务、商店、门派、组队。
3. 各页身份/货币初值统一改成 `—`（`Lv.42` / `逍遥派` / `逍遥派 · 内门弟子` / `12,450` / `8,320` / `328` / `15` / `江湖过客`），数据到位时由绑定覆盖。

PIE 截图核对（商店页顶栏）：`张青衫 [Lv. 1] 无门派`。

**仍缺**：元宝/宝玉/战功等其余货币没有客户端数据源（只有 `ShopClientService.Gold` 的铜钱），一律 `—`。

## 25. 其余"余额/在线人数"写死值清掉（2026-10-29）

§20/§24 之后还有几处余额与在线人数是设计稿常量，统一改成 `—`（有绑定就由绑定覆盖）：

| 页面 | 位置 | 之前 |
|---|---|---|
| 门派 `nav-sect` | 顶栏金币 / 玉 | 12,450 / 328 |
| 多人 `MultiplayerPage` | 顶栏铜钱 / 金子、侧栏在线人数 | 12,830 / 328 / 1,247 |
| 商店 `nav-shop` | 顶栏银两、详情「合计」「当前银两」 | 328 |
| 任务 `nav-quests` | 顶栏银两 | 328 |
| 休闲 `nav-casual-mode` | 顶栏元宝 | 328 |
| 活动 `nav-activities` | 顶栏元宝常量 | 328 |
| 武林录 `nav-martial-record` | 顶栏宝玉 | 328 |

商品单价（商店详情 `_unitPriceValueLabel = 328`）保留——那是商品目录内容，不是玩家余额。

**仍缺**：除铜钱（`ShopClientService.Gold`）外没有货币数据源，银两/元宝/宝玉/战功与在线人数都只能显示 `—`。

## 26. 拍照相册页：把「相册」按钮接上真实成片（2026-10-29）

此前 `PhotoModePage.AlbumClicked` 只是 `NavigateToPage(BackOneLevel)`——点「相册」等于返回，等于没做。现在新增独立页 `nav-photo-album`：

| 项 | 实现 |
|---|---|
| 数据源 | `Globals.ProjectFolder/Screenshots/Photo_*.png`，按修改时间倒序取最近 12 张（纯本地文件，不依赖服务端） |
| 缩略图 | `Texture.FromFile(path, false)` → `TextureBrush`，`Image` 保持 16:9 单元格与 `KeepAspectRatio` |
| 版面 | 4 列 × 3 行；每张下方显示文件名；顶部「共 N 张，显示最近 M 张」 |
| 空态 | 无照片时居中提示「还没有照片 · 回到取景按快门拍一张」 |
| 导航 | 拍照页「相册」→ `NavPhotoAlbum`；相册「返回」→ `BackOneLevel`（回取景页） |

PIE 截图核对：页面标题「相册」、右上「共 17 张，显示最近 12 张」，12 个缩略图都是之前自测拍下的真实成片（文件名 `Photo_20261029_…` 一一对应），返回按钮在左上。

**未做**：点击缩略图放大预览/删除；缩略图未做缓存（每次进页面重新 `FromFile`）。

### 26.1 相册放大预览（同日补）与两个 Ink 坑

点缩略图 → 页内浮层：暗底 + 居中大图 + 文件名 + 「关闭」。自测标记 `album-preview.on` 存在时进页面自动打开第一张并写回执 `album preview index=0 caption=…`，方便无键鼠取证。

首版不动，截图定位到两个真实原因：

1. **`TextureBrush` 一刷一控件**：预览层复用了缩略图的 `Brush` 实例，结果大图完全不画（回执正常、节点可见、就是没像素）。改成预览层单独 `new TextureBrush(texture)`，并保存 `List<Texture>` 以便重建。
2. **绘制顺序 = 子控件加入顺序**：预览层若在缩略图之前创建，就会被缩略图整片盖住（`ui_tree` 里它明明 `visible=True`）。改成「先建缩略图、最后建预览层」，并在 `RefreshLayout` 里同步重排浮层。

修复后截图：大图居中、下方文件名、四周缩略图被 86% 黑底压暗。

## 27. 休闲页顶栏所有元素叠在左上角（2026-10-29）

截图里休闲页顶栏左上有一团糊在一起的字。原因是 `BuildTopBar` 里头像、姓名、等级、门派、体力条、体力值、时辰、铜钱/元宝**全是构造内的局部变量**，没人给它们定位，于是统统停在 `(0,0)`（部分还压在面板上边框外）。

修法：把它们提升为字段（`_charNameLabel` / `_charLevelLabel` / `_charSchoolLabel` / `_vitalityValueLabel` / `_currencyCoinLabel` / `_currencyIngotLabel` / `_timePeriodLabel` / `_timeQuarterLabel` / `_avatarBox`），在 `ApplyLayout` 里按「头像 → 身份两行 → 体力条 → 右侧时辰/货币」排一行，右侧两项按顶栏宽度右对齐。

修复后截图：`张青衫` / `Lv.1 无门派` / 体力条 120/180 / 右侧 `戌时 三刻` 各就各位。

**注意**：体力 120/180、时辰「戌时 三刻」仍是设计示例，没有数据源。

## 28. V2 HUD 左下角色卡一直显示 `—`（2026-10-29）

截图实测：`combat-hud-v2` 左下是 `—` / `Lv. —` / HP `— / —`，明明已经进世界。

原因：V2 页在**导航时**才创建，创建那一刻 `TryGetLocalPlayerAttributes` 可能还拿不到属性（页面壳/玩家生成时序），于是 `BindCharacter` 被跳过；而"玩家就绪翻转"的回调只重绑了 v1 的 `_activeCombatHud`，V2 与传统 HUD 都没重绑，页面就永远停在占位。

修法：

1. `NavigateToPage` 成功落地后调用 `RebindActivePageData()`——按 `_lastKnownDomId` 把当前页重绑一次（HUD 页可能是玩家就绪前建的）。
2. 玩家就绪翻转时，除 v1 外也重绑 `_activeCombatHudV2`（含技能）与 `_activeCombatHudTraditional`。

修复后截图：左下 `张青衫` / `Lv. 1` / `剑客` / HP `100 / 100` / 内力 `1,000 / 1,000`。

**注意**：右上目标框 `Lv.50 精英 19,546/25,000` 与连击数 `23` 仍是页面初始示例值（选择/命中才更新）。

## 29. V2 HUD 不再常驻"假目标 + 23 连击"（2026-10-29）

`CombatHudV2Page` 顶部中央的目标框和左中连击计数器都是设计稿常量（`墨麒麟 / Lv.50·首领 / 18m / 18,500/25,000`、`23 连击`），且没有任何数据接线——进世界就一直挂着一个不存在的首领。

- 目标框：`_targetPanel.Visible = false`（传统 HUD 的目标框已接 `WorldSelectionService`，V2 还没接，先隐藏而不是显示假目标）。
- 连击计数器：`_comboPanel.Visible = false`（没有连击系统驱动）。

修复后 V2 截图：只剩真实的小地图、任务追踪、左下角色卡（`张青衫 Lv.1`，HP/内力真实）、导航栏与滚轮技能栏。

**待接**：V2 目标框需要把 `WorldSelectionService` 的选中目标投影过来（字段与文案可复用传统 HUD 的实时字段实现）；连击需要战斗侧计数器。

### 29.1 V2 目标框接上世界选中服务

上一节把 V2 目标框先藏了；这一节把它接回真实数据（与传统 HUD 同一份 `WorldSelectionService`）：

- `CombatHudV2Page.RefreshTargetInfo()`：未选中 / 目标已销毁 → 整框隐藏；选中 → 填名称、首字头像、`Lv. n`、类别标签（怪物/玩家/NPC）、与本地玩家的实时距离、血条比例与 `cur/max`。
- `MainUIManager.RefreshCombatHudV2DynamicData` 每帧调用它（仅当当前页是 V2 且玩家就绪）。
- 三条 meta 标签（弱点/狂暴/计时）没有数据源，保持隐藏。

运行期核对：V2 页面节点数 365、目标框 `visible=False`、无异常；**"选中目标后填充"这条分支没法在自动化里点到 3D 目标**（编辑器工具只作用于 GUI，不进游戏视口），目前只有编译期保证——下次有人在 PIE 里点一只怪就能验证。

## 30. 好友页/邮件页不再预置假数据（2026-10-29）

**好友页**：构造函数 `_friends.AddRange(DemoFriends)` 预置了 9 个假好友（剑客张三等，带假亲密度/等级），只有服务端名单回来才覆盖；没加过好友的玩家会看到"好友 4/50、在线 3"。现在删掉演示表，空列表直接走页面自带的「暂无好友」，计数 0/50、在线 0、离线 0。

**邮件页**：`SocialMailPage` 写死 8 封邮件（未读 3、附件明细、"论剑豪礼"正文 5 段）。邮件协议在服务端没有 `MailList` 处理、客户端也没有服务，这些全是编的。现在：

| 项 | 现在 |
|---|---|
| 邮件表 | 空（`Array.Empty`） |
| 未读计数 / 分组计数 | `未读 0 封`、系统/帮派/玩家 均 0 |
| 列表 | 「暂无邮件 · 接入邮件服务后显示」 |
| 右侧详情 + 借物附件 | 整块隐藏（不再显示设计稿的论剑豪礼正文与灵石/玄铁精等附件） |

PIE 截图核对：好友页 `好友 0/50` + 「暂无好友」；邮件页 `未读 0 封` + 三个分组全 0 + 空态提示，右侧空白。

**仍缺**：好友名单服务端已可用（`FriendClientService`），邮件两侧都要先补协议。

### 30.1 全部 nav 页面运行时体检（2026-10-29）

把 40 个 `nav-*` 页面在 PIE 里逐个导航（`open-page.txt`），每个都等页面落定后抓 `ui_tree`，检查"页面类型是否为目标页 + 节点数"：

- 40/40 都能加载，页面类型与 dom-id 对得上（`nav-quest-log` 按要求落到 `MenuQuestsPage`，`nav-social-shop` 落到 `MenuShopPage` 这两个是别名）。
- 节点数区间 247–443，没有出现"整页只建了一半"的（对比：修复前的活动页只有 175 且缺详情面板）。
- 只有活动页挂了文件探针（`ui-probe.on`），本次没有探针回执，说明没有页面在建到一半抛异常。

**过程中编辑器崩过一次**，崩溃栈在编辑器自己的 **Output Log 窗口**（`FlaxEditor.Windows.OutputLogWindow.Draw → RichTextBoxBase.DrawSelf → Render2D.DrawText1` 原生访问冲突，coreclr 0xC0000005），不是游戏页面。诱因大概是这轮之前累积的日志量：本机日志写入从 07:42 起就停摆，编辑器内存里的日志行一直在涨。重启后新日志文件 `Log_2026_10_29_10_49_30.txt` 正常写入。

重启方式（注意参数）：`FlaxEditor.exe -project <工程目录>`；直接传 `.flaxproj` 会弹"选择项目"对话框。

## 31. 成就页不再显示假的解锁进度（2026-10-29）

服务端没有成就处理（`AchievementListResponse` 只有消息类型与测试，`Horizon.Game.Core` 里没有请求处理），页面却写着 `已解锁 238`、`成就点 2,380`、六个分类 `45/120…`、12 张卡里 4 张带解锁日期。

保留成就名录（名称/品质/描述属于图鉴内容），把**状态**改成诚实的：

| 项 | 现在 |
|---|---|
| 已解锁 / 成就点 | `0` / `0` |
| 六个分类进度 | 全部 `0/0` |
| 12 张卡 | 全部 `未解锁` 态、进度 0、文案「尚未解锁」；隐藏成就仍写「隐藏成就 · 条件未知」 |
| 默认选中 | 取消（原来默认高亮一张"进行中"的假卡） |

PIE 截图核对：顶部 `已解锁 0`、`成就点 0`，左侧分类全 `0/0`，卡片全部灰态。

**待接**：`AchievementMessageHandler` 已有事件，服务端补 `AchievementListResponse` 后把状态与进度接进来即可。

## 32. 排行榜不再摆假榜单（2026-10-29）

`RankingQueryRequest` 只有消息类型与测试，服务端没有排行处理；页面却写着三甲「剑无痕 128,450 / 沈青鸾 121,830 / 萧别离 118,920」、25 行榜单（楚留香/叶孤城…）、我的排名「第 47 名 · 85,420」、距上一名 1,230、本周趋势 ↑ +5、本周起始第 55 名。

现在（保留版面与"榜单分类/时间筛选"骨架）：

| 项 | 现在 |
|---|---|
| 三甲卡 | 头像 `?`、名次占位、名称 `—`、等级/战力 `—` |
| 榜单表 | 空；底部提示「暂无排行数据 · 接入服务端后显示」 |
| 我的排名 | 「未上榜」、战力 `⚡ —` |
| 距上一名 / 差距 / 进度条 | `—` / 0 |
| 本周趋势 | `—`、起始「未上榜」，曲线数据全 0 |

PIE 截图核对：三甲为 `?`、榜单区中央提示「暂无排行数据 · 接入服务端后显示」。

**仍缺**：排行协议。左侧各榜的"人数"栏也是设计示例，未清（不影响名次与榜单内容）。

### 15.3 曝光与滤镜接上后处理体积（2026-10-29）

此前曝光滑块只改标签、滤镜只改选中态。现在两者都走一个**临时的 PostFxVolume**：

- `EnsurePhotoVolume()`：进拍照且需要时 `new PostFxVolume + Level.SpawnActor`（PostFxVolume 本身就是 Actor，不是 Script，`AddScript<PostFxVolume>` 编译不过）；
- 曝光：`eye.PostExposure = ev; eye.OverrideFlags = EyeAdaptationSettingsOverride.All;`（滑块 0.5 ⇒ 0 EV 中性，进页面即对齐）；
- 滤镜：`cg.ColorSaturation / ColorGain` 五档（原图/水墨/复古/明快/暗调），**必须同时设 `cg.OverrideFlags = ColorGradingSettingsOverride.All`**——只改数值不设 override 时调色被忽略（实测饱和度反而随曝光升高）；
- 退出/销毁：`FlaxEngine.Object.Destroy(volumeActor)`（`Actor.Delete`/`Level.DestroyActor` 都不存在）。

**取证**（自测第三张快门，同机位对比）：
```
exposure ev=2.0 / filter index=1
无滤镜: 平均亮度 115.7、饱和度代理 20.94
+2EV 水墨: 平均亮度 194.2、饱和度代理 3.4
```
截图对比：右侧整体更亮且几乎变成灰阶，说明两项都进了渲染管线（`Screenshot.Capture` 会带后处理）。返回 HUD 后 `actor_find PhotoModePostFx` 返回 0，体积已销毁。

至此拍照页的曝光、滤镜、角度、相册全部接上真实行为。

## 33. 营生页不再显示"练过"的假进度（2026-10-29）

`nav-livelihood` 六个生活技能写着 `Lv.8 / 2400/3200`、工具耐久 `85/100`、配方「已掌握」，页头「已掌握 4」——全是设计稿示例，客户端没有营生协议。

新增 `ResetUserState()`（`BuildData()` 之后、建控件之前调用）：技能等级/经验/工具耐久归零，所有配方 `Unlocked=false`、`StatusText="未解锁"`；技能与配方**名录保留**（属于内容而非用户数据）。

PIE 截图核对：页头「已掌握 0」，六项技能 `Lv.0`、经验条空、配方全部「未解锁」、工具 `0/100`。

**待接**：生活技能协议（等级/经验/配方解锁/工具耐久）。

## 34. 门派页的贡献/日常/武学进度归零（2026-10-29）

`nav-sect` 原来写着「门派贡献度 3,200 / 5,000（64%）」「距下一职位还需 1,800」、三名门派日常「进行中 2/5、1/3」，门派武学节点带 `Lv.5/Lv.3` 与已学习状态。门派贡献与武学同样没有服务端数据。

现在：

| 项 | 现在 |
|---|---|
| 贡献度 | `—`，进度条 0，提示「尚未加入门派 · 贡献度暂不可用」 |
| 门派日常 | 三条全部「未开始」、进度 `0/5 · 0/3 · 0/1`（任务名/描述/奖励保留为内容） |
| 门派武学 | 12 个节点全部「未修习」、学习标记 false（节点名保留） |
| 列表里的门派行 | 不再显示「内门弟子 · 贡献 3200」（未加入时本来就显示「未加入」） |

PIE 截图核对：贡献度区是 `—` 与替换后的提示，武学节点全部「未修习」，日常任务全为未开始。

**待接**：门派（加入/贡献/职位/日常/武学）协议。

### 30.2 改动后的全页回归（2026-10-29 晚些）

§31–§34（成就/排行榜/营生/门派/邮件/好友/个人信息/V2）改完后再跑一次全页体检，分两批（20 + 21）避免日志量把编辑器 Output Log 再顶崩：

- 41 个 `nav-*` 全部落到目标页面类型，无 `NO-PAGE`、无异常；
- 节点数 247–443，与改动前同量级（活动页 315、成就 315、营生 308、门派 272、排行榜 298、多人 309…）；
- 别名仍成立：`nav-inventory`/`nav-character-panel` → `CharacterPanelPage`，`nav-quest-log`/`nav-quests` → `MenuQuestsPage`，`nav-social-shop` → `MenuShopPage`，`nav-npc-gossip` → `MultiplayerPage`。

## 35. 坐骑页的等级/出战/属性归零（2026-10-29）

`nav-mount-pet` 的坐骑列表写死「墨麒麟 Lv.85·出战 / 踏雪乌骓 Lv.72 …」，详情面板显示 `Lv.3` 技能、速度 320、耐力 4500/4500、跳跃力 8.5——坐骑系统没有服务端数据。

现在：

| 项 | 现在 |
|---|---|
| 列表 | 六只坐骑等级 `Lv.0`、无「出战」标记（名录与品质保留） |
| 详情属性 | 速度 / 耐力 / 跳跃力 全部 `—`（耐力条 0） |
| 技能 | 名称保留，等级「未修习」，升级行 `—` |

PIE 截图核对：列表全 `Lv.0`，中间属性三格 `—`，技能框「未修习」。

**待接**：坐骑协议（拥有/等级/出战/技能/驯养）。

## 36. 侧栏/HUD 的「技能」改指向真技能树，旧技能面板页退场（2026-10-29）

`nav-skill-panel`（SkillPanelPage）是设计稿假数据页：8 个写死招式（太极剑法/凌波微步…）、`BindSkills` 是空实现；而 `nav-skill-tree`（SkillTreePage）是**服务端目录驱动**的真页。但侧栏「技能」、传统 HUD 功能列「技能」、V2 底栏「武学」、`action-char-to-skill` 直达别名全都指向假页。

改指向：

| 入口 | 之前 | 现在 |
|---|---|---|
| 侧栏「技能」 | NavSkillPanel | NavSkillTree（重复的「技能树」条目并入这一个） |
| 传统 HUD 功能列 | NavSkillPanel | NavSkillTree |
| V2 底栏「武学」 | NavSkillPanel | NavSkillTree |
| `action-char-to-skill` | NavSkillPanel | NavSkillTree |

**运行期核对**：用 `open-page.txt` 走 `action-char-to-skill`，落点是 `SkillTreePage`（270 节点）。

**遗留**：`SkillPanelPage` 仍注册在路由表（UIGallery 可打开），但主界面已无入口——它是 `PopupMartialDetail` 弹窗唯一的触发者，删页会连带那个弹窗与 `_pendingMartial` 交接一起消失，本轮没动。

## 37. 「自动隐藏UI」设置真正生效（2026-10-29）

设置页的「自动隐藏UI」此前只落库、不生效。现在 `MainUIManager` 每帧跟踪空闲：

- 空闲判据：鼠标位置不动 **且** 本地角色无位移（>2cm 视为在动），任一成立就清零计时；
- 空闲达到 8 秒（`UiAutoHideSeconds`）→ 收起滚轮技能栏与五行循环指示（与拍照模式同一对显隐目标），任一动立即恢复；
- 开关读 `GameConfigurationService.GetAutoHideUi()`（默认 true）；仅作用于战斗 HUD 上下文，菜单页本来就不显示这两个常驻件。

**取证**（自测标记 `ui-autohide-test.on`：阈值降到 2 秒，并在第 4 秒模拟一次"有动作"）：
- 空闲后 `ui_tree`：`InkActionBarControl visible=False`；
- 模拟动作后：`InkActionBarControl visible=True`。
（系统级移动鼠标无效——游戏窗口没焦点时 `Input.MousePosition` 不更新，所以恢复分支用自测节拍驱动。）

**说明**：隐藏范围只包括滚轮技能栏与五行指示，不含任务追踪/小地图/名牌；默认开启，玩家在设置里关掉即恢复常显。

## 38. 通行证等级/经验/赛季信息归零（2026-10-29）

上一节只清了「已领取/可领取」，页头还挂着 `Lv. 23`、`EXP 1,950 / 3,000（65%）`、`剩余时间 28天 06:30:00`、`参与人数 1,284,567`。现在：

| 项 | 现在 |
|---|---|
| 当前等级 / 升级进度 | `Lv. 0` / `EXP 0 / 3,000`（0%） |
| 赛季剩余时间 / 参与人数 | `—` / `—` |
| 等级轨道 20 档 | 全部「未解锁」（锁形徽章），已领取 `0/20`、可领取 `0`、未解锁 `13` |

**踩了个坑**：第一次改完 PIE 里仍显示旧值——因为改代码时 PIE 还开着，`code_build_wait` 在运行态下不会真的重编（回执会"成功"），`Game.CSharp.dll` 时间戳停在上一轮。先 `sim_stop` 再等编译，DLL 时间戳 11:45:52 更新后才生效（复验截图 `Lv. 0 / EXP 0`）。

## 39. 武林录的探索度/区域成就归零（2026-10-29）

`nav-martial-record` 写着「已探索 23/50」、区域探索度 92%/85%/78%…、成就「已完成/进行中」带 12/20 之类进度——没有探索协议。

新增 `ResetExploration()`（构造早期调用）：`_exploredRegions=0`、六处地标 `Percent=0 / State=Unexplored`、区域成就全部 `AchState.Todo` + 文案「未开始」+ 进度 0；地标名/描述/成就名保留为内容。

PIE 截图核对：右上 `已探索 0/50`、右栏「探索度 0%」、成就全「未开始」、地图地标全部空心态、总探索度条为空。

**待接**：地图探索/区域成就协议。

## 40. 祈愿页的次数/保底/元宝归零（2026-10-29）

`nav-gacha` 写着「已祈愿 47 次」「小保底 47/90」「距离保底还有 3 次」「元宝 328」——没有祈愿协议。

现在：已祈愿 `0`、小保底 `0/90`、大保底 `0/90`、距离保底 `90` 次、元宝 `—`；卡池名/UP 池/概率概览/角色展示保留为内容。

PIE 截图核对：右上保底面板块显示 `大保底 0/90`、`小保底 0/90`，右下「已祈愿 0 次」「距离保底还有 90 次」。

**仍为设计稿**：卡池倒计时「12:07:23:23」与单抽/十连价格（160/1600 元宝）——前者要有赛季数据，后者是商品定价。

## 41. UI 现状总览（2026-10-29 收口）

### 已接真实数据/服务（可直接用）

| 页面 | 数据来源 |
|---|---|
| 登录 → 选角 → 进入世界 | 真实 `LoginResponse` → 角色列表 → 场景切换（§16） |
| 战斗 HUD（传统/V2） | 角色属性、技能、队伍空态、目标框（`WorldSelectionService`）、控制状态（`ControlStateManager`）、滚轮技能栏 |
| 角色面板 / 行囊 | `PlayerInventoryService`（装备/背包/卸下/使用） |
| 外观 | 服务端背包快照（已穿戴/未拥有） |
| 技能树 | `SkillCatalogService` + 服务端目录（等级/学习/封存/拖拽到技能栏） |
| 商店 / 稀有商店 | `ShopClientService`（商品、金币、购买回执） |
| 好友 | `FriendClientService`（名单/删除/私聊交接） |
| 任务 | `QuestClientService`（列表/追踪/日志详情） |
| 世界地图 | 地图数据 + `TryUnprojectWorldMapNormalized` 点击寻路 |
| 设置（音频 5 项 + 画面 7 项） | LiteDB / JSON 双后端持久化；「自动隐藏UI」已驱动 HUD（§37） |
| 拍照 | 舞台/藏栏/FOV/角度/曝光·滤镜（PostFxVolume）/快门/相册（§15.x） |

### 缺口在服务端、客户端已按空态/占位呈现

成就、排行榜、邮件、队伍、门派（贡献/日常/武学）、营生（等级/配方）、坐骑宠物、武林录探索/区域成就、通行证进度/领取、祈愿次数/保底、个人信息统计——全部显示 0 / `—` / 未解锁，**不再出现设计稿的假数字**（§19、§30–§40）。

### 仍是设计内容（可接受，但不是数据）

商品价格与商品名、技能/成就/配方/地标名录、卡池 UP 角色与概率概览、秘境/副本条目与难度、界面文案与插画位；秘境页头「总战力/通关数/秘境积分」已改占位，但条目内的「剩余次数/最近通关」仍是设计值，等副本协议再接。

## 42. 秘境页头统计占位 + 缺字形图标（2026-10-29）

**统计**：秘境页头 `总战力 32,450 / 通关数 47 / 秘境积分 1,280 / 辰时三刻` 改成 `— / 0 / — / —`（条目内的「剩余次数/最近通关/已通关徽章」仍是设计值，等副本协议）。

**字形**：截图发现几个字符在 UI 字体里是**方框（tofu）**，已替换成可渲染字：

| 位置 | 原字符 | 现字符 |
|---|---|---|
| 秘境页头三个统计图标 | `⚔` `★` | `◆` |
| 秘境标题前图标 | `⛰` | `山` |
| 技能面板「伤害」图标 | `⚔` | `伤` |
| 任务奖励「装备」图标 | `⚔` | `装` |
| 排行榜三甲皇冠 / 奖励徽标 | `👑` `🏅` | `冠` `奖` |

（`★` 在罗盘页与排行榜奖励符号里仍保留，字体不同、未确认是否 tofu；下一轮截图复核。）

### 42.1 排行榜/罗盘的缺字形补齐（2026-10-29）

截图复核 §42 里保留的字符：

| 位置 | 原字符 | 现字符 | 说明 |
|---|---|---|---|
| 排行榜标题前 | `◈` | `◆` | 方框 |
| 榜单分类右上 | `☰` | `≡` | 方框 |
| 选中分类图标 | `⚡` | `◆` | **方框（一直是它，不是 ★）** |
| 战力数值前缀 | `⚡` | `◆` | 同上 |
| 三甲皇冠 | `♛` | `冠` | 方框 |
| 奖励徽标 | `🏅` | `奖` | emoji 缺字 |
| 罗盘任务点 | `★` | `◆` | 统一成菱形 |

复验截图：`冠 江湖风云榜`、`≡`、金色 `◆ 战力榜` / 灰色 `◆ 等级榜`，均无方框。

**规律**：字体只覆盖 CJK + 少量制表符；`⚡ ⚔ ⛰ ★ ◈ ☰ ♛` 与 emoji 一律 tofu，菱形 `◆`、`≡`、`›`、`◷`、`✕` 可用。后续新页面尽量只用已验证字符。

## 43. 全仓缺字形清扫 + 收口回归（2026-10-29）

按 §42.1 的"可用字符表"扫了整个 `Source/Game`：

- **UI 文本里**换成可渲染字符：`★`×17、`◈`×3、`✦`×7、`⚡`、`🔍`×2、`💧`、`🔒`、`⛰`、`🚩`、`📍` → `◆ / ≡ / 寻 / 水 / 锁 / 山 / 旗 / 点`；涉及任务、商店、章节过渡、角色面板、技能面板、秘境、世界地图、多人、成就、好友、帮会、排行榜、Toast 掉落图标、组队队长标记。
- **日志/注释里的 `★`** 保留（不出现在界面上）。

**收口回归**：改完全部页面后再跑一次全量体检（20 + 22 页，含新相册页）：42/42 落在目标页面类型，无 `NO-PAGE`/异常，节点数 262–458。

### 43.1 第二批缺字形：dingbat 类（2026-10-29）

按更宽的符号表再扫一遍 `Source/Game/UI`，把 dingbat 类换成 CJK 字体里稳定的等价字符：

| 原字符 | 现字符 | 数量 |
|---|---|---|
| `✓` `✔` | `√` | 48 |
| `✗` | `×` | 1 |
| `⚠` | `※` | 4 |
| `☆` | `◇` | 1 |
| `◉` | `◎` | 1 |
| `▽` | `▼` | 1 |

共 16 个文件、56 处。截图核对「装备强化」页：成功率下方的风险提示显示为 `※ 失败将降低1级强化等级…`，无方框。

**仍保留**（未确认且数量大、多为分隔符/箭头）：`→ ← ↑ ↓ ↔ ▲ ▼ ● ○ ◎ ◇ ※ √ ×`——这些在已截图页面里均正常渲染（如排行榜 `↑`、商店 `◎`、战斗 HUD 的 `▲`）。

### 43.2 字形批量替换后的冒烟（2026-10-29）

替换了 `✓/✔/✗/⚠/☆/◉/▽` 之后抽查 6 个含这些字符的页面（任务/好友/导师/成就/邮件/通行证）：全部正常落到目标页、无异常退出。`ui_tree` 节点数与替换前同量级（264–335），说明没有把控件结构改坏。

（同轮还确认了 HUD 顶栏的 `血/气/修` 三个单字是**行标签**、竖排三行属设计，不是字符重叠。）

## 44. 装备强化页的等级/成功率/材料/属性对比归零（2026-10-29）

`nav-equipment-enhance` 的列表写「+12/+15/+10…」、中栏「+12、成功率 78%」、消耗「玄铁强化石 x5 / 银两 x50,000」、属性对比「1245→1320 (+75)…」——强化没有服务端数据。

现在：列表与中栏等级全 `+0`、成功率 `—`（进度条 0）、消耗 `x0`、五行属性对比五行的当前/强化后/变化全 `—`。

PIE 截图核对：左栏每件装备 `+0`，中栏「玄铁重剑 传说 +0 双手剑·60级」，属性表全 `—`，成功率 `—`，消耗 `x0 / x0`。

**仍为设计内容**：装备名与品质、Tab（强化/镶嵌/精炼/调律/淬火）、右栏材料清单与套装/五行展示。

## 45. 锻造页的技能等级/配方解锁/制作参数归零（2026-10-29）

`nav-crafting` 左侧五个生活技能写着 `Lv.40/35/28/42/20` 带进度条，配方面板 8 张卡大多是"可制作"，详情里「制作 30 秒 / 成功率 85% / 银两 2,000 两 / 技艺经验 +150」。

现在：
- 五个技能等级 `Lv.0`、进度条 0；
- 8 张配方全部未解锁（锁形遮罩），名称/品质/需求等级保留；
- 详情四项参数全部 `—`。

PIE 截图核对：左栏五个 `Lv.0`、右侧配方卡全带锁、详情「制作时间 — / 成功率 — / 银两消耗 — / 技艺经验 —」。

**仍为设计内容**：制作日志、所需材料清单的数量、Tabs（鉴定/制造）。

## 46. 战前备战页接真实穿戴快照 + 清零 mock（2026-10-29）

`nav-battle-prep` 原先整页是设计稿数据：8 个装备格写着「玄铁重剑 / 乌金兜鍪 / 蟒纹战袍…」，加成摘要「攻 +1,240 / 防 +860 / 血 +2,400 / 内 +600 / 暴 +12% / 身 +8%」，武学等级 `Lv.7/6/5/4/6`，药品 `×12/×8/×5/×3`，战力 `12,450 / 15,000（83%）`。

现在：

- **8 个装备格接服务端权威穿戴快照**（`PlayerInventoryService.TryGetEquipped` + `EquipmentDatabase.GetEquipment`）。展示槽位与真实槽位一一对应：`剑→RightHand / 匕→LeftHand / 盔→Head / 袍→Body / 带→Waist / 履→Feet / 护→RightWrist / 玉→Neck`；空槽写「未装备」、品质按 `Common` 描边，已穿按装备表品质上色。
- **加成摘要接装备桥的权威聚合**（`EquipmentAttributeBridge.Summarize`）：攻击 = 物攻+法攻、防御 = 物防+法防；气血/内力**不在这里加**（服务端已把装备生命并进上限，客户端再加就是双倍），暴击/身法无装备属性字段 —— 这三类一律 `—`，不编数字。
- **快照晚到也能刷新**：页面在登录前就构造（那时还没有穿戴快照），因此订阅 `Changed` 原地刷新 8 格与摘要，未收到快照时主动 `RequestFullRefresh()`；`OnDestroy` 退订。
- 武学等级全 `—`、药品数量全 `×0`、战力 `0 / 0`（达成率 0%）；气血/内力/体力三条细条接真实属性（100/100、1,000/1,000、体力 0）。

PIE 核对（`win-battleprep2.png`）：装备格「默认长剑 / 默认头巾 / 默认战袍」+ 5 个「未装备」，加成「攻击 +15 / 防御 +15」其余 `—`，战力 `0 / 0`，气血 100、内力 1,000、体力 0。

**仍为设计内容**：武学卡名称/说明（狂风快剑、降龙掌…）、药品名称与效果（回气丹、金创药…）、顶部「强敌当前」标签。

**环境注意**：编辑器里的游戏视图比 16:9 窄时，右栏「药品补给」会被裁出可视区（页面自身坐标 `x=1606, w=290`，三栏 24/348/1606 排布正确），属视图长宽比所致，不是布局缺陷。

## 47. 时辰页清零当前时辰/天气 + 修布局重叠与文字截断（2026-10-29）

`nav-time` 原来整页按设计稿写死：表盘中心「午时 / 三月初七」、右卡「日期 三月初七 / 时辰 午时 / 天气 晴」，时间快进三个按钮可点却没有任何处理器，顶栏「无名侠 / Lv.42 / 逍遥派 / 铜钱 12,450 / 银两 328」。

现在：

- **当前时辰没有数据源**：服务端虽有 `WorldStateSyncMessage.WorldTime/Weather`，但两端都没接线（`grep` 全仓无消费方），所以表盘中心与右卡日期/时辰/天气一律 `—`，表盘不再标亮任何一格（原来硬编码高亮 `i == 6` 正午），图例里的「● 当前时辰」一并隐藏。
- **时间快进三个按钮改为禁用**（`Enabled = false`），提示语换成「时辰系统尚未开放：世界时间/天气同步未接入，暂不可快进」；主按钮也不再是醒目的 Primary。
- **顶栏接真实数据**：昵称/等级/铜钱走 `InkPlayerHeaderBinder.Bind(..., coin, "铜钱 ")`，门派走 `BindSect`；银两无数据源保持 `—`；顶栏各段改固定槽位（昵称 124/宽 120、门派 252、等级 340、任务 410），真实昵称变长不再和门派叠字。
- **修两个布局缺陷**：`等待至黄昏` 与 `等待至黎明` 原来都定位在 y=100 互相遮挡（现在 100/148）；「时辰影响」三段文字没有换行被面板右缘截断（现在 `Wrapping = WrapWords`，行高 56、面板 264），提示条也有显式宽高。

PIE 核对（`win-time2.png`）：表盘中心 `—`、右卡三格 `—`、三个等待按钮灰显、影响文案整段换行、图例只剩「其他时辰 / 外圈活动」。

**仍为设计内容**：十二时辰名称与外圈活动词（夜市/巡夜/晨钟…）、三条时辰影响说明。

## 48. 多人页清零房间/匹配 mock + 顶栏接真实网关（2026-10-29）

`nav-multiplayer` 原先写死 6 个房间（华山论剑/帮派对战…，房主 独孤求败…，5/6、8/10…）、匹配横幅「预计等待 15秒 / 匹配范围 Lv. 38-46 / 当前匹配 326人」、顶栏「网络延迟 45ms / 服务器 华东一区 / 江湖过客 Lv.42」。

现在：

- **房间列表清空**：`_roomNames/_roomHosts/_roomModes/_roomCurrent/_roomMax/_roomPlaying` 全为空数组，列表区给一条「暂无房间 · 大厅服务尚未开放」空态；页脚按 0 计数，「加载更多」禁用。
- **匹配横幅与模式栏降级**：预计等待/匹配范围/当前匹配全部 `—`；「开始匹配」按钮文案改为「匹配系统尚未开放」并禁用（原来点一下会直接跳回战斗 HUD，是假动作），提示语「匹配队列尚未接入」；左侧五个模式按钮禁用、不再预选中「快速匹配」，页脚改「匹配系统尚未开放，暂无可用大厅」。
- **顶栏网络状态接真实网关**：新增 `InkPlayerHeaderBinder.GetGatewayText(out region, out latency)` 做唯一取值口径（`NetworkManager.GetCurrentGateway()`；`Latency` 默认 `long.MaxValue` 视为"没测过"，显示 `—`）。多人页显示「服务器: {Region}」与 `{n}ms`，刷新按钮改为真的重读网关；昵称/等级/铜钱仍走 `Bind`，默认值改 `—`。
- **清理死接线**：删除 `StartMatchRequested`/`LoadMoreRequested`/`RefreshRequested` 三个再无触发方的公开事件，`MainUIManager` 侧对应订阅一并删掉。

## 49. 休闲页与武录/时辰页的假延迟、假体力、假时间清零（2026-10-29）

- `nav-casual-mode`：等级默认 `Lv.—` 并补 `BindSect`；体力细条 `120/180 → 0/0`（比例 0，没有体力属性）；时辰「戌时/三刻」→ `—/—`；活跃度 `325/500 → 0 / 500`（比例 0、里程碑刻度不再标亮）；六个活动卡进度全部 `0 / N`、状态「未开始」、剩余时间 `—`；「共 8 项奖励」按实际 9 项改正；「筛选」禁用、「详情」改为跳活动页；左侧服务器/版本由「江南 · 烟雨楼 / v2.4.1 · 烽火连城」改为 `—` / `混沌世界 v1.0.0`。
- `nav-martial-record` 与 `nav-time` 底栏的「延迟 32ms」不再写死：统一调 `GetGatewayText`，未测得时显示 `—`。

**运行时验证状态**：以上三页改动**已通过离线编译**（`Game` 目标 0 error），但 PIE 截图核对**尚未完成**——编辑器脚本构建当前被另一条并行工作线的 `MonsterEditor` 插件半成品挡住（`MonsterEditorWindow.cs` 引用 `MonsterDraftModel.LootWindowSeconds/PickupDistanceMeters`，模型侧字段尚未加），`Game.CSharp.dll` 停在 12:45 那份。等插件可编译后需重跑一次 `nav-multiplayer / nav-casual-mode / nav-time / nav-martial-record` 的截图核对。

## 50. 三页运行时核对 + 本轮抓到的两个真缺陷（2026-10-29 晚）

### 50.1 构建阻塞已解

编辑器脚本构建自 12:45 起一直红：另一条工作线在 `MonsterEditorWindow.cs` 引用了 `MonsterDraftModel.LootWindowSeconds / PickupDistanceMeters`，而模型侧只有 `KnownTopLevelKeys` 里的键名、没有属性。经用户同意补上两个属性（默认值对齐服务端 `MonsterCorpseCore.DefaultLootWindowSeconds = 60`、`DefaultPickupDistanceMeters = 5.5`），插件与 Game 两个程序集都恢复 0 error。

### 50.2 多人页：清空房间数组引入的下标越界（真缺陷）

`_roomRows = new InkPanel[6]` + `for (i = 0; i < 6; i++)` 读 `_roomNames[i]`，而房间数组被清空后长度为 0 ⇒ 构造期 `IndexOutOfRangeException` 被 catch 吞掉，整页只建到表头就停（空态提示、页脚、加入按钮全没有）。日志里 `[MultiplayerPage] 初始化失败: Index was outside the bounds of the array.` 是唯一线索。
修法：`_roomRows` 改按 `_roomNames.Length` 开、循环同长度；空态提示从 `_roomList` 挪到 `_contentArea`(24,208) 并左对齐（列表比视口宽时居中会落到屏幕外）。
PIE 核对（`win-mp9.png`）：表头下「暂无房间 · 大厅服务尚未开放」，左栏页脚「大厅服务尚未开放」，匹配横幅三项 `—`、「匹配系统尚未开放」按钮灰显。

### 50.3 休闲页：活跃度汇总/奖励总览的叠字（真缺陷）

两块面板的子控件（图标、今日活跃度、0/500、进度条、100/200/300/400/500 刻度、已完成/进行中/待开始；奖励总览标题/副标题/共 9 项/详情/9 项奖励名与数量）构造时全是局部变量、**没有任何定位**，全部叠在 (0,0)，渲染成一行糊字。
修法：把子控件收进字段，新增 `LayoutSummaryInner()` / `LayoutRewardPreviewInner()` 在 `ApplyLayout()` 末尾排布（左侧图标+数值+进度条+刻度、右侧三组统计；奖励条左侧标题/数量、右侧详情、9 项奖励横排）；活动卡 `LayoutCard()` 单卡异常单独 catch，不再带走后面两块的排布。
PIE 核对（`win-casual4.png`）：活跃度「0 / 500」+ 进度条 + 五档刻度 + 右侧三组「0」，奖励条「今日奖励总览 / 共 9 项奖励」+ 9 项奖励横排。

### 50.4 武录/时辰底栏

两页底栏「延迟」统一问 `InkPlayerHeaderBinder.GetGatewayText`：无网关时 `延迟 —`；武录的「服务器：乾坤一柱」也改绑真实网关区域（无网关写 `—`）。时辰页顶栏实测「张青衫 / 无门派 / Lv.1」不再叠字。

**仍未接**：多人页房间/匹配（无服务端大厅）、时辰/天气（`WorldStateSyncMessage` 两端都没消费方）、休闲页活动进度（无活动服务）。这些页面保持空态而不是编造数值。

## 51. 秘境页与通行证页的去假数据（2026-10-29 晚，PIE 已核对）

### 51.1 `nav-dungeon-entry`

- 左栏条目「日 3/3、周 1/3」→ 保留周期与上限、剩余改 `—`（`日 —/3`）；限时活动「剩2时/剩3日」→ `—`。
- 三张卡片：推荐战力 `12,000/25,000/45,000` → `—`；剩余次数 `3/2/1` → `—`；「已通关」徽章不再无条件挂（`cleared` 全 false）。
- 右栏战力对比 `32,450 / 25,000 / √战力达标` → `— / — / 战力未评估`、进度条 0；「最近通关」三条历史记录改为空态「暂无通关记录」。
- 「队伍成员」`1/1 + 游侠 + 队长 + 剑客·32,450` → 空态「未组队 · 单人挑战」；随之删除只有它用的 `RolePill` 类。

截图核对（`win-dungeon2.png`、`dg-team2.png`）：左栏 `日 —/3`、卡片「推荐战力 —」「今日 —/3」、右栏「战力未评估」「暂无通关记录」「未组队 · 单人挑战」。

### 51.2 `nav-battle-pass`

- 免费奖励详情卡的右上状态 `已领取` → `未解锁`，底部动作按钮 `√ 已领取` → `未解锁`，并恢复锁形提示（原来「免费=已领取」是写死的）。
- 至尊奖励卡状态 `可领取` → `未解锁`（等级 0，任何一档都拿不到）。

截图核对（`bp-detail2.png`）：卡片右上「未解锁」、左下「锁」提示、右下「未解锁」；页头仍是 `Lv. 0 / EXP 0 / 3,000`、`已领取 0/10`、`可领取 0`、`未解锁 13`。

**仍为设计内容**：秘境名/难度/首领/掉落品质、攻略提示文案、通行证奖励目录（铜钱×2000、名侠碎片×5… 及其品质/类型/说明）。

## 52. 商店页"示例货架"标注（2026-10-29 晚）

`nav-social-shop` 的商品网格来自设计稿目录（9 件、银两/铜钱标价），只有从商人 NPC 打开并拿到 `ShopClientService` 快照时才被服务端货架替换（`ApplyServerShelf`）。从侧栏直接进店时没有任何提示，看起来像"这些价格现在就能买"。

现在：构造末尾若 `_vendorEntityId == 0`，把底部提示写成「未与商人交谈 · 以下为货架示例，价格与库存以商人为准」；`OpenVendor` 绑定成功时清空该提示，交回服务端回执文案（失败仍显示原来的「未连接服务器…」红字）。

**验证边界**：该提示挂在右侧详情栏（`_actionHintLabel`），而当前编辑器游戏视图比 16:9 窄，右栏被裁在可视区外——本次只做了编译与代码路径核对，截图看不到这段文字；16:9 视口下才可见。左中两栏已核对：9 件商品、品质标签（传世/史/珍/良/凡）、银两与铜钱标价、购买按钮位置正常。

**队伍页（`nav-team`）现状**：`未组队 0/5`、「暂无队友 · 组队功能尚未接入服务端」、「暂无可邀请的队友…」，已是空态；右栏「队伍设置」被视口裁切，未能核对内部开关。

**52.1 改挂位置（同轮修正）**：第一版把提示写进详情栏的 `_actionHintLabel`，在当前窄视口下根本看不见（右栏被裁）。改成标题行新标签 `_demoShelfLabel`（`ApplyLayout` 里放在分类 Tab 左侧、宽度按 Tab 左边距算），`ApplyDemoShelfHint()` 统一决定显示/清空；`OpenVendor` 成功与失败都调它。截图核对（`win-shop3.png`）：标题右侧出现「未与商人交谈 · 以下为货架示例」，9 件商品与标价照常展示。

## 53. 任务页空列表时的详情占位（2026-10-29 晚）

`nav-quests` 列表为空（`暂无任务记录`、`0 项`）时，右侧详情仍摆着构造期那套设计稿示例：`推荐等级 Lv.42`、`难度 ◆◆◆◆◆`、`类型 主线`、`委托人 李沧海`、`区域 洛阳城东 · 太行山巅`、`时限 无`，底部还有一个可点的「接取任务」按钮 —— 列表里根本没有任何任务。

现在：
- 构造默认值全部改成 `—`；`_trackBtn` 构造期 `Visible = false`；
- `UpdateQuestDetail()` 的 `quest == null` 分支不再直接 return，而是把标题写「未选择任务」、元信息/背景/委托人/区域/时限全部 `—`、清空目标行与奖励卡、隐藏主动作按钮。

PIE 核对（`win-quests2.png`）：右栏标题「未选择任务」，`推荐等级 —`、`难度 —`、`类型 —`，委托/区域/时限为空占位，无「接取任务」按钮。

**观察（未定性）**：该详情栏在窄视口下元信息行会折到第二行（`类型：` 换行），16:9 下宽度足够不折——按视口自适应处理，未改动布局。

### 53.1 外观页 / 世界地图页抽查

- `nav-appearance`：3D 预览正常出图（角色未穿外观时是内衣模型，属数据空而非渲染错），服装页签里「玄影劲装（已穿戴）/ 玄铁重铠（试穿）」来自 `PlayerInventoryService` 快照，染色三行色板为设计内容。
- `nav-world-map`：区域页签（清河/开封/凉州…）、左栏筛选标记（界碑/跳跃/宝箱/江湖故人/采集点）、地点标记、底部图例与搜索筛选都正常；整页文字对比度偏低（`TextTertiary` 叠暗底），把截图提亮 2.2 倍后才看清——先记为观感问题，未改配色。

## 54. 门派页：布局重复把排布改回去 + 日常叠字（2026-10-29 晚）

`nav-sect` 的 `ApplyLayout()` 里「门派武学 + 门派日常」这一整段被写了**两遍**：第二遍用「按文案猜子控件」的老逻辑（`_dailyTaskNames.Contains(label.Text)` / `label.TextColor == GoldPrimary`）重新覆盖位置——于是第一遍算好的坐标被改坏，日常行里任务名与状态叠在同一处，技能节点被 100px 高的分支框裁掉。

现在：
- 删掉重复的第二遍（134 行），保留单遍排布；
- 两处都改成**按子控件顺序**定位（`0=图标/分支名, 1=状态/已学计数, 其后=节点/任务字段`），不再按文案猜；
- 技能节点改为 2×2 网格（列宽封顶 340）、分支标题左对齐、「已学 0/4」放在标题右侧；分支框 110 步进、技能面板高度 380、后续 `detailY` 同步 +390；
- 日常行：图标 / 名称 / 状态 / 描述 / 进度 / 奖励六段固定槽位；
- 顺带把 `_skillBranchLearned` 归零（原来是 `{3,2,1}`、标题写着「已学 6 / 12」，与节点全「未修习」自相矛盾）。

PIE 核对（`win-sect5.png`、`sect-skill3.png`）：三条分支各 4 个节点，名称 + `未修习` 不重叠，标题右侧 `已学 0/4`；日常三行「门派巡逻/传功解惑/门派试炼」+ `未开始` + 右侧描述，无叠字。

## 55. 坐骑灵兽页的出战/移速归零 + 走查脚本（2026-10-29 晚）

`nav-mount-pet` 右栏原来固定显示「出战中」金色状态点、「休息」按钮（点了没反应）、「骑乘移速 160 / +60%」与 80% 进度条。

现在：
- 出战状态改「未出战」（灰），说明文字改「坐骑出战功能尚未开放；出战期间消耗耐力的规则以服务端为准」；
- 「休息」按钮 `Visible = false`（全仓没有坐骑服务/协议，留着就是死按钮）；
- 移速加成：骑乘移速与百分比都 `—`、进度条 0（基础移速 100 是设计常量，保留）。

PIE 核对（`win-mount.png`）：右栏「未出战」+ 未开放说明；属性「速度/耐力/跳跃力」全 `—`；移速「100 → —」。

**仍为设计内容**：坐骑名册与品质（墨麒麟·传说 / Lv.0 / 速 320…）、特殊技能（踏火穿云）、外观幻化色板、驯养喂养与材料清单——这些没有对应协议，未逐项归零。

### 55.1 走查脚本与两条观察

新增 `.compile-temp/ui_sweep.py`：逐页写 `open-page.txt` → 等页面落地 → `window_capture.ps1` 抓图 → 输出「平均亮度 / 颜色数 / BLANK?」并拼成联系表，一批页一次就能看出谁空了。

- **假空白**：`nav-social-mail` 被判 `BLANK?`（亮度 9.5），实为「未读 0 封 + 三组 0 + 暂无邮件」的合法空态；阈值只做初筛，必须看图确认。
- **dom-id 写错不会报错**：`nav-achievements`（复数）不存在，导航返回 `ok=False` 并停在上一页（`cur=nav-friends`），正确 id 是 `nav-achievement`。走查输出里的 `ok=False` 就是这条判据。
- **世界提示会压在全屏菜单上**：锻造页截图里出现过绿字 `TrafficVehicle_2.2 / [T] 上车`——那是车辆的世界交互提示投到屏幕上，与菜单页无遮挡关系，属世界标签层（未在本轮改动，记入待办）。

## 56. 休闲页左栏假角标 + 成就页「0 但 45%」（2026-10-29 晚）

两处"数字和状态自相矛盾"的残留：

- `nav-casual-mode` 左栏导航写着 `不肝 6`、`活动 3` 两个角标，来源是硬编码的 `badgeIndices/badgeValues`；活动本身已清零（列表 0 项）。删掉角标及其数组，左栏只剩文字项。
- `nav-achievement` 表头左边是 `总体完成度 0`，右边却是 `45%` + 约 45% 的填充条（`Value = 0.45f` 写死）。改为 `0%` / `Value = 0f`。
- 同页 8 条成就里有 5 条描述是设计稿占位符 `??????????`，直接渲染成一串问号。改成「暂无描述」（不自己编文案）。

PIE 核对（`win-casual5.png`、`casual-nav.png`、`win-ach.png`、`ach-head.png`）：左栏无角标；成就表头 `0 / 0%` + 空条；卡片描述为「暂无描述」或真实达成条件。

**仍为设计内容**：成就名/品质/达成条件、休闲页活动奖励目录。

## 57. 师徒页去假关系/假进度（2026-10-29 晚）

`nav-mentor` 原来整页是设计稿示例：表头 `师徒 IV`、`传承 3,820`、`Lv.65`；左卡 `孤云长老 / 少林派·武学宗师 / 亲密度 2,840 / 3,000 / 拜师 42 日`；`门下弟子 2 / 3` 并列出林清河、慕晴雪；日常 5 条 + 周常 3 条带 `已完成/进行中` 与 5/5、2/3、8/10 等进度；右栏传承条 `3,820 / 5,000`。

现在：
- 表头：`尚未拜师`、`传承 —`；等级改绑真实值（`InkPlayerHeaderBinder.BindLevel`，修掉首版多写一个前缀导致的 `Lv. Lv. 1`）。
- 师父卡：`暂无师父 / 尚无师门 / 亲密度 —`；亲密度条 `Value = 0`，并把它从 y=56 挪到文字下方 y=78、在师门天数挪到 y=98（原来三条 16px 高的文字与 6px 条同区间互压）；天数文案改「拜师后显示在师门天数」。
- 弟子：名单清空、计数 `0 / 3`，由页面既有的「+ 虚位以待 · 可再收 1 名弟子」空槽呈现（首版我另加了一条空态文字，与既有空槽重叠，已删）。
- 日常/周常 8 条：状态全 `未开始`、进度 `0 / N`（保留任务名/条件/奖励）。
- 传承条：`0 / 5,000`。

PIE 核对（`win-mentor4.png`、`mentor-left3.png`）：左卡「暂无师父 / 尚无师门 / 亲密度 — / 拜师后显示在师门天数」无叠字，`门下弟子 0/3` + 空槽；右栏任务全「未开始」`0/N`。

**仍为设计内容**：师父/弟子画像、日常与周常任务名与奖励、师徒权益列表。

## 58. 结伴江湖页去掉 11 个虚构好友与门派/帮会信息（2026-10-29 晚）

`nav-social-guild` 原左栏是设计稿样例：`在线(5)` + 5 个好友（剑客张三 Lv.60·武当派 …）、`离线(12)` + 6 个好友（铁掌赵六 Lv.42·5小时前在线 …）；门派卡 `武当派 / 弟子 / 贡献 2,500`；帮会卡 `天下会 / 3级 (300/500) / 精英`。

现在：
- 好友两组计数 `(0)`，11 条好友全部删除，换成一条「暂无好友 · 好友功能尚未接入服务端」；
- 门派卡 `未加入 / 无 / —`，帮会卡 `未加入 / 无 / 无`，「帮会管理」按钮 `Enabled = false`（没有服务端通道）。

PIE 核对（`win-guild2.png`、`guild-info2.png`）：`在线(0)` + 空态；两张信息卡六行值都在。

**一个排版观察**：这几行最初写 `—` 时只有 `贡献`（Number 字体角色）显示出来，Display/Body 角色下那条长破折号在截图里看不见；换成「未加入 / 无」后正常。同页其他 `—` 有正常渲染的（如时间页 Display 30f），暂按字体角色差异记录，未做全局替换。

## 59. 珍品阁：卡片内部从未排布 + 假刷新/假倒计时（2026-10-29 晚）

`nav-shop-rare` 的 9 张珍品卡在 `ApplyLayout` 里只排了卡片本身，**卡片里的角标/图标/名称/描述/价格/倒计时全部停在 (0,0)**，渲染成左上角一坨叠字；页头还有写死的 `刷新时间 02:45:30` 与首张卡的 `12:30:45` 倒计时。

现在：
- 新增 `LayoutCardChildren(index, w, h)`：按"是否需要角标/倒计时"推算子控件下标，角标右上、图标左上 72×72、名称 y=96、描述 y=122（`Wrapping = WrapWords`）、价格 y=164、倒计时右下；
- 嵌套在角标/图标容器里的那一个标签用 `FitOnlyChild` 显式铺满（它们是 `StretchAll` 锚点，父容器尺寸在布局期才改，光设 Size 不保证重排）；
- `刷新时间` 改 `—`、底部说明改「珍品阁的货架与刷新计划尚未接入服务端，当前展示为目录样例」；首卡倒计时清空（整列无服务端计时来源）。

PIE 核对（`win-rare5.png`、`rare-cards2.png`）：九张卡「图标字（青/紫/龙/玄/凌/莲/金/魂/参）+ 角标（限时/热销/珍品/仅剩 3 件/良品）+ 名称 + 描述 + 元宝价」各就各位，无叠字。

**仍待处理（同页）**：页头标题上方还有一行重叠的小字（疑似页内重复表头）、顶部五个筛选胶囊里没有文字（`_filterButtons` 的子标签没排布）——下一轮处理。

### 59.1 珍品阁收尾：索引式排布漂移 + 筛选胶囊无字（2026-10-29 晚）

上一节留下的两处，根因是同一个：`ApplyLayout` 靠 `_contentArea.Children[idx++]` 按**下标**给子控件定位。页头标题/副标题/印章之后还排了 Tab 按钮、刷新标签、筛选胶囊等，下标一漂，底部提示就落回 (0,0) 压在标题上；筛选胶囊的子标签用的是 `StretchAll`，父面板尺寸又是布局期才给，于是永远零尺寸、看不见字。

修法：
- 刷新提示 / 刷新时间 / 件数 / 底部提示四个标签改成**按引用**定位（`_refreshHintLabel`、`_refreshTimeLabel`、`_itemCountLabel`、`_bottomHintLabel`），不再参与下标队列；
- 五个筛选胶囊在设完尺寸后逐个 `FitOnlyChild`，让里面的标签铺满。

PIE 核对（`win-rare6.png`）：标题「奇珍阁 / 江湖异宝·限时珍品」不再被压；筛选行显示「全部 传世 史品 珍品 良品」；底部提示回到卡片下方；九张卡片保持上一节的正常排布。

### 55.2 坐骑页驯养/技能研习的假可点与假进度（2026-10-29 晚）

上一节只处理了出战与移速；右栏还有三处假状态：`喂养 / 驯养` 按钮可点但无通道、`消耗材料 灵草 ×3 / 驯兽丹 ×1` 是写死的、技能「踏火穿云」进度条写死 72%。

现在：四个动作按钮（喂养 / 驯养 / 升级 / + 学习新技能）全部 `Enabled = false`，消耗材料两项改 `—`，技能进度条 `0f`。

PIE 核对（`win-mount2.png`、`mount-right2.png`）：右栏「未出战 + 未开放说明」、四个按钮灰显、材料「— —」、技能条空。

**仍为设计内容**：坐骑名册与品质、技能名、外观幻化色板（麟/焰/霜，仍可点选但只改本地选中态）。

### 54.1 门派渊源面板文字溢出（2026-10-29 晚）

回归走查里发现「门派渊源」纸色面板的正文压到下方条文：正文标签高 80、不换行，长段落直接溢出到规则行。改为 `Wrapping = WrapWords`、高度 120，规则起始位置 100→150，面板 280→340。

PIE 截到面板标题与日常三行正常（`win-sect6.png`）；正文与条文在窄视口下已落到可视区外，未能逐行核对——按"换行 + 让高"的确定性修法处理。

## 60. 回归走查（两批 10 页）+ 装备页明示"设计样例"（2026-10-29 晚）

- 第一批（休闲 / 门派 / 师徒 / 珍品阁 / 坐骑）与第二批（任务 / 多人 / 时辰 / 通行证 / 秘境）各抓一张联系表核对：状态与前面几节描述一致，无互相回退；顺带修了门派「门派渊源」正文溢出（见 §54.1）。
- 第三批抽查「角色 / 装备 / 背包」：角色页（3D 预览 + 五维/五行雷达 + 基础属性 + 属性加点 + 战斗属性 622）与背包页正常；**`MenuEquipmentPage` 整页仍是 mock**（`grep PlayerInventoryService` = 0）——背包 10 件、已装备 3/8、套装（龙纹 2/4、玄铁 1/3）、对比卡（未落幕刀 +116/+12%）都来自 `InitMockData`，点"穿戴"只改本地字典。
- 本轮只做**明示**：标题行右侧加「本页为设计样例：背包 / 套装 / 对比为示例数据，真实穿戴请在「角色」页操作」（`win-eq.png` 已核对）。
- 真接线路径已确认可用：`PlayerInventoryService.Items / TryGetEquipped / RequestEquipAsync / RequestUnequipAsync` + `ItemCatalog.Resolve(templateId).EquipSlot`，照 `MenuBattlePrepPage` 的槽位绑定方式改即可——列为下一步。

## 61. 好友页空态残留的示例资料（2026-10-29 晚）

`nav-friends` 名单为空时，中栏/右栏仍留着"某个好友"的资料：等级 `60`、门派 `武当派`、修为 `渡劫期`、阵营 `正道`；装备简览四件（真武剑/紫霄袍/凌云冠/青玉佩）；右栏「近期互动」五条、「共同好友」四人、「交游里程碑」四条（相识 128 天 / 通关 23 次 / 互赠 56 件…）；六个互动按钮（私聊/组队/传送/拜礼/飞鸽/删除）仍可点。

现在：
- `ShowEmptyDetail()` 把四格信息**全部**清 `—`（原来只清前两格，后两格留着示例值）；
- 装备简览换成一条「装备未公开」；
- 近期互动 / 共同好友 / 里程碑全部改为空态（一条「暂无互动记录」「暂无里程碑」，共同好友数组清空）；
- 六个互动按钮在空态下 `Enabled = false`。

PIE 核对（`win-friends2.png`）：`暂无好友`、四格全 `—`、按钮灰显；右栏在窄视口外，未截图核对。

## 62. 排行榜赛季写死 + 两个"伪安全"字形（2026-10-29 晚）

- `nav-leaderboard` 表头写死 `第3赛季 / 剩余12天`（列表本身已是「暂无排行数据 · 接入服务端后显示」），改为 `赛季 — / 剩余 —`，截图核对（`win-lb3.png`）。
- **字形坑**：同一表头里 `◷`（时钟）与关闭按钮的 `✕` 都渲染成"方框里一个叉"的缺字形方块。全仓替换：`◷ → ◆`（3 文件 5 处）、`✕ → ×`（11 文件 17 处，含 PopupPages 的 7 个关闭按钮），改后编译 0 error，排行榜表头显示 `◆ 赛季 — 剩余 —` 与真正的 `×`。
- 结论修正：之前记的"安全字形"里 `◷`、`✕` 在 Body 11–13f 下并不可用；`◆ ≡ › × ※` 才是稳的。

### 62.1 缺字形清扫第二批（2026-10-29 晚）

用脚本把 UI 源码字符串字面量里的非 ASCII 符号清了一遍（注释/日志不算），再按上下文挑出缺字形方块：

- `▤`（世界地图「筛选标记」、武林录）→ `◆`：地图页截图原显示为"方框叉"，改后为实心菱形；
- `ℹ`（Toast 图标）→ `◆`；
- `✉`（邮件弹窗图标）→ `◆`。

合计 6 处（UIAnimationSystem / ToastNotification / PopupPages / EquipmentEnhancePage / WorldMapPage / MartialRecordPage），编译 0 error，地图页截图核对（`map-filter.png`）：`◆ 筛选标记`。

仍在用且已确认可见的：`→ ← ↑ ↓ ▲ ▼ ● ○ ◎ ◇ ※ √ × · — ≥ ° Δ ≡ ›`（多张截图实证）。`▸` 尚未单独取证，先不动。

### 62.2 缺字形清扫第三批：`▸`（2026-10-29 晚）

秘境页右栏的「队伍成员」「战力对比」和卡片「通关时间」在截图上都是"方框叉"——源头是 `▸`（U+25B8 小三角）在这套字体里没有字形。全仓替换 `▸ → ◆` 共 12 处（QuestTrackerHUD / CcCreationPages / DungeonEntryPage / AchievementPage），编译 0 error；秘境页截图核对（`dg-sections2.png`）：`◆ 通关时间`、`◆ 队伍成员`、`◆ 战力对比` 均为实心菱形。

字形结论更新：`▸` / `◷` / `✕` / `ℹ` / `▤` / `✉` 全部不可用；稳定可用集合 = `◆ ≡ › × ※ √ → ← ↑ ↓ ▲ ▼ ● ○ ◎ ◇ · — ≥ ° Δ`。

## 63. 收口走查（8 页，2026-10-29 晚）

对今晚改动最密集的 8 页做了一次连续走查并拼成联系表（`final-sweep.png`）：

| 页面 | 观察到的状态 |
| --- | --- |
| 休闲 `nav-casual-mode` | 活跃度 `0 / 500`、六张活动卡 `0/N`「未开始」、奖励总览 9 项、左栏无角标 |
| 门派 `nav-sect` | 八个门派「未加入」、贡献「—」、三条武学分支各 4 节点「未修习 / 已学 0/4」、日常三行「未开始」、门派渊源纸卡 |
| 秘境 `nav-dungeon-entry` | 左栏 `日 —/3`、卡片「推荐战力 —」「今日 —/3」、右栏「未组队 · 单人挑战」「战力未评估」「暂无通关记录」、`◆ 通关时间` 等图标正常 |
| 珍品阁 `nav-shop-rare` | 九张卡「图标 + 角标 + 名称 + 描述 + 元宝价」、筛选行有字、底部「货架与刷新计划尚未接入服务端」 |
| 好友 `nav-friends` | `暂无好友`、四格 `—`、互动按钮灰显、装备简览「装备未公开」 |
| 排行榜 `nav-leaderboard` | 领奖台三个 `?`、`暂无排行数据 · 接入服务端后显示`、`◆ 赛季 — 剩余 —`、关闭按钮 `×` |
| 任务 `nav-quests` | `0 项`、`暂无任务记录`、右栏「未选择任务」+ 三项 `—`、无接取按钮 |
| 多人 `nav-multiplayer` | 模式按钮灰显、匹配三项 `—`、`暂无房间 · 大厅服务尚未开放`、页脚「大厅服务尚未开放」 |

未发现回退；本页清单只覆盖这 8 页，另外 30+ 页在 §42/§43 与今晚前面各节有各自的截图与结论。

## 64. 关掉两处"点了会演"的假动作（2026-10-29 晚）

- `nav-equipment-enhance`：「执行强化」按钮原来只调用 `EmitGoldAtButton`（冒一团金光），数值全是 `—`、材料 `x0`。改为 `Enabled = false`（保留可见但不可点）。PIE 核对（`win-enh.png`）：按钮灰显，属性对比/成功率/材料仍是 `—`。
- `nav-equipment`：「更换装备」按钮调用 `OnSwapClicked`，把示例背包里的物品写进本地字典并抬高属性总览——纯本地演示。改为文案「更换装备（未接入）」且 `Enabled = false`，与标题行已有的"本页为设计样例"提示一致。
- 同页 `EquipmentEnhancePage.EmitGoldAtButton` / `MenuEquipmentPage.OnSwapClicked` 现在没有调用方，属于死代码；为控制本轮改动面暂留，下一轮连同装备页真接线一起清。

**仍待接线（已列在项目记忆）**：`MenuEquipmentPage` 整页背包/槽位/套装/对比、强化页的物品与数值。

### 64.1 社交页那批"点了只冒金光"的按钮（2026-10-29 晚）

扫了一遍 `Clicked += … EmitGoldAt…` 这类"唯一效果是粒子"的接线，把已经确认没有服务端通道的按钮统一禁用：

- `nav-social-guild`：私聊 / 组队 / 交易 / 删除（好友名单本身为空）；
- `nav-mail`（`SocialMailPage`）：写信 / 一键领取（邮件服务未接入，`未读 0 封`）；
- `nav-mentor`：收徒纳贤 / 寻访明师 / 解除师徒（无师徒通道）。

PIE 核对（`win-mail.png`）：邮件页「未读 0 封 + 三组 0 + 暂无邮件 · 接入邮件服务后显示」，页脚「返回沉浸模式 / 好友列表」；两个动作按钮已不可点（空态下本就不渲染）。
`SkillPanelPage`（装备/卸下/升级）同样是纯粒子，但该页已无主入口、只作 `PopupMartialDetail` 触发者，留待删页时一并处理。

## 65. 装备页接服务端权威背包/穿戴（2026-10-29 晚）

`MenuEquipmentPage` 原来是整页 mock（`grep PlayerInventoryService` = 0）。现在：

- `InitMockData()` 先调 `TryLoadFromInventory()`：`PlayerInventoryService.HasServerData` 为真时，用 `Items` 里 `ItemCatalog.Resolve(templateId).EquipSlot` 非空的物品构建背包列表（下标与 `_realItems` 一一对应），再把 `TryGetEquipped` 拿到的已装备物品**追加在数组尾部**供槽位取品质，并用 `_equippedExtraIndices` 标记——列表 UI 和品质/类型筛选都会跳过这些下标。
- 「更换装备」在有快照时恢复可点，点击走 `PlayerInventoryService.RequestEquipAsync(_realItems[selected])`，由服务端裁决、`Changed` 回包刷新；没有快照（示例数据）时按钮与文案保持「未接入」禁用态。
- 真实数据下不再有示例数值：属性总览六行全 `—`、`套装 —`、对比卡「当前·（未选择）／候选·（未选择）—」；标题行「本页为设计样例…」提示按 `_realSource` 隐藏。

PIE 核对（`win-eq4.png`）：背包 `10/24` 列出真实物品（流云霓裳 / 紫霞羽缎 / 玄铁重铠 · 1v.0 · 外观），`已装备 3/8`，`套装 —`，属性总览全 `—`，对比卡两张都是「（未选择）」。

**仍缺**：属性总览与套装没有服务端聚合（需要类似 `CharacterAttributeRefresh` 的通道）；背包列表不随 `Changed` 原地重建（要退出再进页面才拿到新快照）。

### 65.1 装备页穿戴态随快照实时刷新（2026-10-29 晚）

§65 留下的"列表不随 `Changed` 重建"先按**不冒险**的方式补了一层：

- 新增 `_liveEquipped`（槽位 → 品质）与 `RefreshEquippedState()`：从 `PlayerInventoryService.TryGetEquipped` 现读，刷新 8 个槽的 `InkCell.Quality` 与「已装备 X/8」；
- 构造末尾订阅 `PlayerInventoryService.Changed`，`OnDestroy` 退订；
- `GetSlotQuality()` 在真实数据下优先用 `_liveEquipped`；`UpdateSummary()` 的计数同理。
- 列表本身仍按构建时的快照渲染（下标与新数据可能错位），因此**不**在 `Changed` 里重建列表——避免"点第 N 行穿到别的物品"。

PIE 核对（`win-eq5.png`）：`已装备 4/8`（真实快照计数，替换掉原先写死的 3/8），槽位品质随快照变化，其余占位（套装 `—`、属性总览 `—`、两张对比卡「（未选择）」）保持一致。

**下一层**：要让列表也实时，需要把 `EquipmentListItem[]` 改成可重建容器（或在 `OnPageEnter` 里整页重建）；这条留在待办。

## 66. 本轮收口对账（2026-10-29）

**交付分三类，证据均为「离线 csc 0 error + PIE 截图」或代码路径核对：**

1. **去假数据（页面不再编造玩家状态）**：战前备战、时辰、多人、休闲、秘境、通行证、任务、师徒、结伴江湖、好友、珍品阁、成就、排行榜、坐骑，及战前备战/装备页的槽位接线。每节记录"改前值 → 改后值 + 仍为设计内容的边界"。
2. **布局缺陷（真实渲染问题）**：门派 `ApplyLayout` 重复两遍导致日常叠字、休闲两块面板子控件从未定位、珍品阁卡片内部从未排布/索引漂移/筛选胶囊零尺寸、好友空态残留示例资料、宗师卡亲密度条压字。
3. **字形（编译与 ui_tree 都看不出的缺字形）**：`✓✗⚠` 早批 + 本轮 `✕→×`(17)、`◷→◆`(5)、`ℹ/▤/✉→◆`(6)、`▸→◆`(12)，全部有截图实证。
4. **假动作**：强化「执行强化」、装备「更换装备」、社交 6 个按钮、师徒 3 个按钮由"点了只冒金光"改为禁用；商店加"示例货架"提示并在 16:9 可见位。

**改动的验证方式**：每批改动 → `dotnet exec csc @verify-check8.response`（0 error）→ 编辑器 `code_build_wait`（`lastCompilationFailed=false`，必要时比对 DLL mtime）→ `sim_play` + 标记文件导航 + `window_capture.ps1` 截图 → 必要时 `ui_tree`/日志核对；批量页面用 `ui_sweep.py` 出联系表。

**仍未完成（按优先级）**：
- 装备页背包列表不随快照重建（要改可重建容器或在 `OnPageEnter` 重建整页）；属性总览与套装缺服务端聚合通道；
- 强化页的物品清单与等级仍是示例，`EmitGoldAtButton`/`OnSwapClicked` 成为死代码待清；
- 世界交互提示（`[T] 上车`）在菜单打开时未隐藏（属 NarrativePro 交互层，需先确认改动边界）；
- `SkillPanelPage` 仍注册但无主入口，删页要连带 `PopupMartialDetail` 交接；
- 窄视口下若干页右栏被裁（编辑器游戏视图非 16:9），属环境问题，未改布局。

### 66.1 死代码清理 + 列表过期提示（2026-10-29 晚）

- 删掉 `EquipmentEnhancePage.EmitGoldAtButton`（禁用「执行强化」后已无调用方）；同类 `EmitGoldAtControl` 仍在用，保留。
- `MenuEquipmentPage.OnInventoryChanged` 增加对账：把当前快照里"可装备物品数"与构建期数量（`_realItems.Count + _equippedExtraIndices.Count`）比较，不等时复用标题行提示显示「背包已更新 · 重开本页可刷新列表」——避免用户点旧行穿到别的物品，同时不冒"重建列表"的险。
- PIE 核对（`win-eq6.png`）：`已装备 4/8`、列表三件真实物品、对比卡与属性总览保持占位，提示在数量一致时保持隐藏。

## 67. 装备强化页明示"设计样例"（2026-10-29 晚）

`nav-equipment-enhance` 左侧物品清单（玄铁重剑 / 赤霄枪 / 紫金冠…，`双手剑 · 60级`）、中栏标题与右侧属性对比全部来自页面内 mock（`grep PlayerInventoryService` = 0），而「执行强化」已在 §64 禁用，页面却没有说明。

现在：顶栏标题右侧加一行「本页为设计样例：物品清单与强化数值为示例，强化功能尚未接入」（`win-enh2.png`、`enh-head.png` 已核对），与装备页、通用户"示例"口径一致。

**下一步**：给强化页接真实可强化物品（照装备页 `Items` + `ItemCatalog.EquipSlot` 的写法），中栏标题/品质/等级随选中项刷新；`+N` 等级与成功率仍需服务端字段。

### 67.1 强化页左栏接真实可强化物品（2026-10-29 晚）

- 新增 `LoadEquipRows()`：有服务端快照时取 `PlayerInventoryService.Items` 里 `ItemCatalog.Resolve(templateId).EquipSlot` 非空的物品（名称/类型/品质/图标字来自物品表），没有快照才回退到原来的 8 件设计样例；列表计数同步为真实件数。
- 中栏名称行改为字段（`_centerName` / `_centerPlus` / 三个 `TagPill`），`TagPill` 增加可写 `Text`；新增 `ShowEquip(index)`：名称与品质色、`+N`、品质/类型/等级三段都随左栏选中项刷新，构建末尾默认展示第一行。
- 顶栏提示改为「强化数值与功能尚未接入；物品清单无服务端快照时显示示例」——无论走哪条数据源都准确。

PIE 核对（`win-enh3.png`）：`装备列表 3件`（流云霓裳 / 素雪罗裙 / 玄铁重铠 · 外观），中栏「流云霓裳 +0 外观」，属性对比与成功率仍 `—`，执行强化灰显。

**仍缺**：`+N` 强化等级与成功率没有服务端字段；列表不随 `Changed` 重建（同装备页的处理方式）。

### 68. 接线后的跨页冒烟（2026-10-29 晚）

对刚接线的两页及其邻居做了一轮抓图冒烟（`smoke2.png`，6 页联系表）：

| 页面 | 观察 |
| --- | --- |
| 装备 | `已装备 4/8`、`套装 —`、属性总览全 `—`、两张对比卡「（未选择）」 |
| 强化 | `装备列表` 3 件真实物品（流云霓裳/素雪罗裙/玄铁重铠·外观），中栏「流云霓裳 +0 外观」，属性对比与成功率 `—`，执行强化灰显 |
| 角色 / 背包 | 3D 预览、五维/五行雷达、基础属性、战斗属性 622 均正常（两页同为角色信息页，符合既有别名） |
| 珍品阁 | 九卡正常、筛选行有字、底部说明在卡片下方 |
| 休闲 | 活跃度 `0/500`、六卡 `0/N`、奖励总览正常 |

未发现两页接线引起的回退。示例边界（`套装 —`/属性总览 `—`/强化成功率 `—`）在截图中保持。

### 65.2 装备页列表随快照重建（2026-10-29 晚）

补上 §65.1 留的"列表不重建"：

- 新增 `RebuildBackpackList()`：先 `Dispose` 旧行、清空数组与选中项，再复用 `BuildBackpackList()` 重新渲染，最后 `ApplyFilter()`；
- `OnInventoryChanged` 在"可装备物品数 ≠ 构建期数量"时改为 `TryLoadFromInventory()` + `RebuildBackpackList()`，不再只显示"请重开本页"的提示（提示仍保留在数量一致但内容变了的场景）。

**路径自证**：临时在构建末尾插入一次 `RebuildBackpackList()` 并在 PIE 里跑通——页面照常渲染三件物品、无重复行、日志无 `[MenuEquipmentPage]` 异常；随后移除该临时调用并重新构建（`lastCompilationFailed=false`）。

### 69. 移除临时自证后的两页复核（2026-10-29 晚）

去掉构建末尾的临时 `RebuildBackpackList()` 后重新构建（`lastCompilationFailed=false`），再各抓一张确认真实渲染（`final2.png`）：

- 装备页：`装备背包 10/24`、三件真实物品（流云霓裳 / 素雪罗裙 / 玄铁重铠 · Lv.0 · 外观）、`已装备 4/8`、`套装 —`、槽位头/身/右手有品质描边、属性总览全 `—`、对比「（未选择）」。
- 强化页：`装备列表 3件`、中栏「流云霓裳 精良 +0 外观」、属性对比全 `—`、顶栏提示「强化数值与功能尚未接入；物品清单无服务端快照时显示示例」。

两页的"真实数据 + 诚实占位"状态在移除临时调用后保持，未出现重建副作用（无重复行、无异常日志）。

### 70. 装备页"仅可装备"标注（2026-10-29 晚）

接线后列表只渲染可装备物品，但右上角计数是整包容量（`10/24`），三行对不上计数容易误读。标题栏在计数左侧补一枚 10px 灰字「仅可装备」（`eq-baghead.png` 已核对：`装备背包 …… 仅可装备 10/24`）。

至此装备页的可见信息自洽：容量是背包容量、列表是可装备子集、空槽/未选择/缺聚合字段都是 `—` 或「（未选择）」。

### 71. 装备页属性总览接装备桥（2026-10-29 晚）

- 真实数据分支下，属性总览的「攻击 / 防御」改为 `EquipmentAttributeBridge.Summarize(inventory)` 的权威聚合（`PhysicalAttack+MagicAttack` / `PhysicalDefense+MagicDefense`），有值显示 `+N`，无值仍 `—`；气血/内力/暴击/命中/闪避保持 `—`（前两项由服务端并入上限、后三项无装备字段）。
- 这与战前备战页用的是**同一个入口**（那里实测 `+15 / +15`），没有再写第二套算法。

**验证边界**：编辑器游戏视图比 16:9 窄，属性总览的数值列被裁在可视区外（截图只能看到行名），本轮以"代码路径 + 同源入口（战备页已实测 +15/+15）"作为证据，未做像素级核对。

### 71.1 属性总览数值的运行时取证（2026-10-29 晚）

§71 因视口裁切无法看到数值列，改用**临时探针**取证：在 `TryLoadFromInventory()` 计算完攻防后打一行 `[EquipPageAttr] atk=… def=…`，跑 PIE 后 `log_scan` 取到
`[EquipPageAttr] atk=15 def=15` —— 与战前备战页同一入口实测的 `+15 / +15` 一致，说明装备页属性总览确实拿到了真实聚合值（不是空转的占位）。取完证后已删除探针并重新构建（`lastCompilationFailed=false`）。

### 72. 交互路径取证的边界（2026-10-29 晚）

想用编辑器工具点一下装备行来验证"选中→对比卡显示真实当前装备"，两种方式都不可用：
- `ui_click_control` 只遍历场景 Canvas 树，报「画布树里没有类型为 EquipmentListItem 的控件」；
- `ui_input` 的坐标点击 `hit:<none>`（它打在 UICanvas 上，Ink 页面画布不在其命中链里）。

结论：**Ink 页面的点击级验证只能人工操作**，自动化证据止步于"页面渲染 + 探针日志 + 代码路径"。这条写在这里，避免后来者把 `ui_input` 的 `success:true` 当成点到了按钮。

## 73. 接手指南（2026-10-29 收尾）

**现在这套 Ink UI 的三种页面状态**
1. **全真实**：登录/选角、角色面板、背包、外观、拍照模式与相册、商店货架（有商人时）、装备页（背包/穿戴/攻防聚合）、强化页（物品清单/中栏标题）、任务、秘境列表、通行证奖励目录。
2. **真实 + 诚实占位**：装备（套装/气血内力暴击命中闪避）、强化（+N/成功率/材料）、秘境（次数/推荐战力/通关记录）、通行证（领取态/赛季时间/参与人数）、师兄/好友/帮会/邮件/多人/时辰/休闲活动——每页都用 `—`、空态或「尚未开放」说明，并在文档对应小节写了边界。
3. **设计样例（明确标注）**：仅剩装备页兜底（无快照时）、强化页兜底、商店无商人时的货架；都在标题行有「示例」提示。

**下次继续时的入口**
- 找实例：`grep -rn "PlayerInventoryService\|ItemCatalog" HundunWorld/Source/Game/UI/Ink/Pages` 看哪些页已接权威数据；
- 找假值：按 §46–§72 的小节逐页对照；扫描脚本在 `.compile-temp/glyph_inventory.py`（字形）、`.compile-temp/ui_sweep.py`（页面走查）；
- 验证阶梯：离线 `dotnet exec csc @.compile-temp/verify-game/verify-check8.response` → 编辑器 `code_build_wait`（看 `lastCompilationFailed` 与 DLL mtime）→ `sim_play` + 标记文件导航 → `window_capture.ps1` 截图；视口裁掉的数值列用**临时探针日志**取证后删除；
- 交互点击（选中/拖拽/按钮）目前只能人工验证（§72）。

**最值得做的下三件事**：装备/强化的 `+N` 与属性聚合补协议；世界交互提示在菜单打开时隐藏；`SkillPanelPage` 与其武学详情弹窗收尾（删页 + 交接）。

## 74. 收尾回归：登录→选角→进世界（2026-10-29）

今天改动量很大，最后跑一遍核心链路（标记文件自测）确认没伤到主流程。回执：

```
nav-retry nav-character-panel ok=True cur=nav-character-panel
enter-world attempt#1 mgr=3 state=3
enter-world fire character=47
enter-game transition=True
enter-world enterGame=True
nav-retry nav-character-panel ok=True cur=nav-character-panel
```

含义：登录后角色列表 3 条（`mgr=3 state=3`，两份账一致）→ 用 character=47 进世界（`transition=True`、`enterGame=True`）→ 自动导航到 `nav-character-panel` 成功。链路无回归。

### 75. 「更换装备」按选中态开关（2026-10-29 晚）

之前只要有快照按钮就可点，但没选物品时点击是空操作。现在：构造期一律 `Enabled = false`，`OnItemClicked` 里按 `_realSource && _selectedIndex >= 0` 打开；点击后仍走 `RequestEquipAsync` 交服务端裁决。

**验证**：编译 0 error、编辑器构建 `lastCompilationFailed=false`。按钮在右栏底部、被当前窄视口裁切，未能截到"灰显"状态——按代码路径记录（同类裁剪限制见 §72）。

## 76. 武学两页收尾：删掉样例面板，详情弹窗改读技能目录（2026-10-30）

**问题**：`SkillPanelPage`（skill-panel.html，1409 行）里 `BindSkills(SkillBase[])` 是空实现，左列 8 条招式（太极剑法/狂风刀法/杨家枪法/降龙掌…）、秘籍库计数（8/15/6/3）、天赋树、底部装备/升级按钮全部是设计稿写死的样例；它还被营生页侧栏「武学」和 UI 画廊「武学技能」两张卡指向，等于一条处处可达的假数据页。而项目真正数据驱动的是 `SkillTreePage`（五行×五阶×三境，读 `SkillCatalogService` + skills.json），HUD 的技能入口（`ActionCharToSkill`）也早就指向技能树。

**改动**
1. 删除 `Pages/Character/SkillPanelPage.cs`，并清掉 `InkPageRouter.NavSkillPanel` 常量、页面注册、`CreateSkillPanelPage()`、`_activeSkillPanel` 字段与两处重绑/清理分支；
2. 营生页侧栏「武学」改指 `nav-skill-tree`（`MenuLivelihoodPage`），UI 画廊同名卡改 dom-id 与副标题（`nav-skill-tree` / 五行·阶境·演化），`CombatHudPage` 的行内文档注释同步；
3. 删掉页面间传参 `_pendingMartial` 与 `PopupMartialDetail.MartialDetailInfo`，新增 `MainUIManager.TryPickMartialForDetail()`：优先取已学（按 id 升序第一条）并带服务端等级，无已学时退到目录首条；
4. `PopupMartialDetail.BindSkill(SkillConfig, learnedLevel)` 注入真实数据：名称/描述/五行/类型/阶境（`Grade`+`GradeStage`→白…混沌·初/中/极）、修炼等级、8 个属性格（伤害倍率按等级算、范围+范围类型、内力消耗、冷却、施法时间、连击段位、暴击率），连招路径沿 `ComboNextId` 走 `SkillDatabase.GetComboChain()`，修炼进度给等级与上限、每级伤害倍率增量，材料门槛读 `learnCost.materials`（物品名走 `ItemCatalog`，已有数量只在 `PlayerInventoryService.HasServerData` 时与背包快照核对）。**没有通道的两项保持占位**：破防（SkillConfig 无此字段）、修为经验（无下行通道，进度条给 0、经验值 `— / —`）。

**顺带修的版式缺陷**（截图发现）
- 连招提示固定在 `ComboY+120=470`、与「修炼进度」标题 `CultivationY=470` 完全重叠 → 修炼区下移到 506、材料区 616、按钮 830（面板高度随 `ActionY+60` 自适应）；
- 材料行全部隐藏时摘要仍落在第 4 行之后（空一大片）→ 摘要位置按实际显示行数 `MaterialsY+34+shown*30` 动态摆放；
- 连招节点只有两行空间，长描述会压出格子 → `Shorten(desc, 22)` 截断加省略号。

**验证**
- 离线 `csc`：0 error（response 里已移除被删源文件那一行）；
- 编辑器构建：`lastCompilationFailed=false`、`compilationsCount=23`、`GameEditorTarget/.../Game.CSharp.dll` 13:36 刷新；
- PIE（标记文件自测）回执：`nav-retry nav-skill-tree ok=True`、`nav-retry popup-martial-detail ok=True`、`ui-shot ui-gallery`；
- 截图：技能树页正常（五行×五阶格 + 「未选中招式」右栏）、武学详情弹窗显示真实招式 **裂金斩**（×1.5 / 5 米·扇形 / 30 内力 / 3 秒 / 0.4 秒 / 起手式 / +15%）、连招链 裂金斩→断岳击→万剑归宗、提示「连招窗口 0.8 秒，链内共 3 式」、材料区「该招式未配置材料门槛（learnCost 为空）」；画廊卡片副标题已变。
- 运行时 `log_errors` 无 `SkillPanelPage` / `PopupMartialDetail` / `未注册 domId` 相关条目（其余为既有环境噪声：缺失音频、NarrativePro 类型扫描、FlaxActorSync）。

**边界**：按钮点击级验证仍只能人工（§72）；本次导航是走 `open-page.txt` 直接进页面，`武学` 按钮的实点未自动化；技能目录在服务端未下行时用的是本地 `Content/GameData/Skills/skills.json` 兜底表。

**补记（同日）**：取证结束后编辑器在热重载途中被关闭（日志停在 `Starting scripts compilation`），重新以 `-project` 拉起后 `code_build_status` 报 `compilationsCount=1 / lastCompilationFailed=false`，再跑一次 `code_build_wait` 得 `status=success、dirtyChangeSeen=true`、`isSourceDirty=false`。此后只剩一处注释改动，`GameEditorTarget/.../Game.CSharp.dll` 未被再次替换——按"注释不改变行为"记录，不把它当作"改完没生效"。

## 77. 菜单打开时让世界交互提示让位（2026-10-30）

**问题**：全屏 Ink 菜单铺开时，世界侧 `<c>[T] 上车</c>` 一类交互提示仍然浮在面板上方，且游戏侧按键没有被屏蔽。

**根因**：提示的显隐条件写死在 NarrativePro 里——
`NarrativeGameplayHUD.RefreshInteractPromptVisibility()`（`Widgets/NarrativeGameplayHUD.cs:995-998`）=
`_bPromptHasTarget && !bHUDHidden && !NarrativeInputRouter.bGameInputBlocked`；
而 `bGameInputBlocked` 只由 `NarrativeActivatableWidget` 的激活栈驱动（`CommonUI/NarrativeInputRouter.cs:118-133`）。Ink 页面根本不在这条栈上，于是"菜单开着"这个事实对世界层不可见。

**改动**（全部在客户端，插件一行未改）
1. 新增 `HundunWorld/Source/Game/UI/Ink/InkWorldInputGate.cs`：
   - `InkMenuInputBlocker`：不渲染任何内容、`InputConfig = Menu` 的可激活控件，只占输入栈一格；
   - `InkWorldInputGate`：`IsMenuOpen = HasOverlay || GetFrame(CurrentPageDomId).Kind != Hud`，状态翻转时 `Activate()/Deactivate()`；页面类型问路由器登记的 frame，不在客户端另立 dom-id 名单；并用 `NarrativeInputRouter.IsInStack` 兜住"场景切换把栈 Reset 了、本地标志还停在已压栈"的错位。
2. `MainUIManager`：新增 `SyncWorldInputGate()`，随 `OnUpdate` 每帧调用（状态不变时零动作）；水墨 UI 销毁与单例清理处 `Release()`，避免留下弹不掉的菜单标记。
3. **顺带补的取证通路**（也是自动化缺口）：登录页是旧式 Flax 面板，桥工具 `ui_input` 打不到（`hit:<none>`）、`ui_tree` 里 `LoginPanel` 是 `visible:false` 的设计期节点，于是"进世界"前的登录无法自动化。新增标记 `%APPDATA%/HundunWorld/auto-login.on`：`AuthenticationUI.SubscribeComponentEvents` 里起一次性任务，1.5 秒后用面板**预填账号密码走真实登录**（不伪造 Token，与 §登录只走真实校验 一致）。正常环境无此文件。

**验证**
- 离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`、`isSourceDirty=false`；重新编译后 DLL 里探针串 `inp-gate` 计数 0（证明装配确实被替换）。
- PIE 回执（本机运行日志落盘会被截断，改走 `%APPDATA%/HundunWorld/ui-selftest.out`）三段状态机：
  ```
  inp-gate menuOpen=True  page=cc-role-list        blocked=True  stack=1
  inp-gate menuOpen=False page=combat-hud-traditional blocked=False stack=0
  inp-gate menuOpen=True  page=nav-character-panel  blocked=True  stack=1
  ```
  即"选角页→屏蔽、回战斗 HUD→解除、开角色面板→再屏蔽"，`bGameInputBlocked` 与栈深和预期一一对应。
- 免点登录通路同时被这条回执证明可用：`enter-world armed`→`mgr=3 state=3`→`character=39`→`transition=True`→`enterGame=True`。

**边界**：提示"像素上消失"的目视证据需要站在载具/NPC 前触发提示，本轮没有可自动化的站位，证据链止于"屏蔽标志翻转 + HUD 判定式直接读这个标志 + HUD 订阅 `OnStackChanged` 即时刷新可见性"。菜单页按钮点击仍见 §72。

## 78. 页面回归体检：三处版式缺陷修复（2026-10-30）

用 `ui_sweep` 对 13 个页面做了一轮运行时走查（祈愿/世界地图/秘境/制造/坐骑/活动/组队/门派/师徒/排行/成就/战斗备战/强化）。BLANK 阈值只作初筛，逐张看图后修了三处**真实缺陷**：

1. **祈愿页价格压住按钮文字**：`_singleDrawCost`/`_multiDrawCost` 落在按钮内 `buttonTop+28`（按钮 52 高、文字垂直居中），"元宝 × 160" 与"祈愿一次"叠字；"节省160元宝"角标在 `buttonTop-12`，压住按钮上沿。改为价格行移到按钮下方并居中（`buttonTop+DrawButtonHeight+2`），角标抬到 `buttonTop-24`，抽卡区高度 120→136 留出底边留白（原先价格行贴底被切）。
2. **世界地图 POI 名字被图标压住**：`DrawPlaces` 以 POI 为中心画 100×28 的文字框，而 `DrawMarker` 的界碑图标画在同一点 —— "开封城"中间的字正好被图标盖住（截图为 `开◈城`）。改为地名从 `pos.Y+9` 起画（图标下方），文字框 120×26。
3. **师徒页任务奖励被行右缘截断**：奖励标签 140×32、字号 11，"＋50 传承 / ＋10000 经验"超出框宽被切（右侧只剩 `+1`）。改为 160 宽、字号 10、与描述同高（`y=26`），周常带「前往」按钮的行仍是顶栏右侧 138 宽。

**验证**：离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`、`isSourceDirty=false`（DLL mtime 每轮刷新）；PIE 重跑 `ui_sweep` 三页并放大截图：祈愿页价格行完整落在色带内且不再压字、角标在按钮上方；世界地图「开封城」图标在上文字在下完整可读；师徒页奖励整句可见。

**同轮发现、尚未修的缺陷**（下一轮目标，截图在 `.compile-temp/verify-game/sweep/`）：
- `nav-achievement`：卡片网格第一行被顶部筛选条压住（卡片上沿被裁），左栏分类计数（`18/4…  15/56`）溢出到边框外；
- `nav-team`：右栏（邀请入队/队伍设置）整体落在 1920 设计宽度的右缘，窄视口下被裁；页头「燕云」与副标「十六声」间距过紧——需先用 `ui_tree`/设计稿宽度确认是"页面超宽"还是"已知视口裁切"，再动手。

## 79. 成就页：首行被压住 + 分类行距 + 残留设计数值（2026-10-30）

**缺陷 1：卡片首行被筛选条压住**。`_gridScroll`（卡片网格容器）建出来后没设 `Location`，卡片从 `y=16` 起画，而筛选条 `_gridHead` 占 `0..44` —— 首行卡片的顶边与品质角标正好被筛选条盖掉。修法：`_gridScroll.Location = (0, 44)`。

**缺陷 2：分类列表展开项叠住下一个分类**。`CategoryItem` 固定步距 60，但展开的「江湖历练」会多出三行子项（3×22=66px，`ClipChildren=false` 直接画到下一项身上）。修法：分类按运行游标排布，展开项额外让出 66px（`catY += expanded ? 126 : 60`）。`OnCategorySelected` 是空实现，展开态不随点击变化，所以静态游标足够；将来接交互时要同时把这段改成可重排。

**缺陷 3：残留设计数值**。分类进度条还是设计稿的 38%/31%/51%/90% 而计数已归零（条有进度、数字 0/0 自相矛盾）；子分类「战斗 12/24 / 探索 18/40 / 社交 15/56」与面包屑「江湖历练 ◆ 战斗 12/24」同样是样例。统一归零：进度条 0、子项 `0/0`、面包屑去掉计数。

**顺带判定**：`nav-team` 右栏（邀请入队 / 队伍设置）不是布局越界——`memberW/actionW` 按 `(1920-240-gap*2)*比例` 分配，合计正好落在 1680 内容宽内；截图里被裁是**已知的窄视口裁切**（Game 面板不足 1:1，右侧 40% 不在画面里），按 §73 口径不改布局。

**验证**：离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`；PIE 重进世界导航 `nav-achievement`，回执 `nav-retry nav-achievement ok=True cur=nav-achievement`，截图确认：首行卡片完整（初出茅庐/百战之将/武林盟主/嗜血修罗 顶边与角标都在）、五个分类均分排布不再叠子项、计数全 `0/0`、面包屑不再带 12/24。

## 80. 侧栏品牌「燕云｜十六声」挤字 + 页面回归基线（2026-10-30）

**缺陷**：组队页与门派页侧栏品牌由两个 Label 拼成（20px Display「燕云」+ 11px「十六声」），副标位置写死 `20 + 22*2 + 6 = 70`——按 22px/字估算，而 20px Display 的「燕云」实测约 48px 宽，副标贴到主标右缘上（放大截图为证）。两页同源同缺陷。

**修法**：两页都把主标尺寸显式设为 `52×33`，副标改为 `x = 20 + 52 + 8`、`y = logoY + 15`、尺寸 `90×16`（`MenuTeamPage.cs`、`MenuSectPage.cs` 各一处）。

**验证**：离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`（DLL mtime 刷新）；PIE 重跑 `nav-team` / `nav-sect`，放大截图确认主副标之间已有 8px 间隙。

**页面回归基线（同轮，30+ 个 `nav-*`）**：对路由表里注册的页面逐个 `open-page.txt` 导航 + 抓图，全部能落位并渲染（`nav-shop`/`nav-social-mail`/`nav-friends`/`nav-time`/`nav-multiplayer`/`nav-bestiary`/`nav-settings`/`nav-shop-rare`/`nav-casual-mode`/`nav-photo-album`/`nav-quests`/`nav-equipment`/`nav-appearance`/`nav-personal-info`/`nav-martial-record`/`nav-achievement`/`nav-team`/`nav-sect`/`nav-mentor`/`nav-leaderboard`/`nav-world-map`/`nav-dungeon-entry`/`nav-crafting`/`nav-mount-pet`/`nav-activities`/`nav-gacha`/`nav-compass`/`nav-battle-prep`/`nav-equipment-enhance`/`nav-skill-tree`/`nav-livelihood`）。
两处 `ok=False` 都是**我写错了 dom-id**（`nav-character` 未注册，真名 `nav-character-panel`；`nav-casual` 真名 `nav-casual-mode`），不是页面拒载——再次印证 §73 的"dom-id 写错不报错"。

**两条被排除的疑似缺陷**（避免后来者重复调查）：
- `nav-personal-info` 左侧竖排的三个字**不是换行 bug**：那是 `InkVerticalTitle`（Width 30 / FontSize 28 / Height 120），设计上就是竖排姓名。
- `nav-team` 右栏超出画面、`nav-shop-rare`/`nav-dungeon-entry` 最右列被裁，都是**窄视口裁切**（游戏画面只显示 1920 里约 1164），不是布局越界。
- HUD 截图里曾出现整屏过曝的白底（14:18 一次），只影响世界渲染不影响 UI 层；属场景/光照（`Cube`/`Sun`/`SkyLight` 在 World 场景，非本次 UI 改动），记录待场景负责方核对。

## 81. 好友页「选中后互动按钮永久禁用」修复（2026-10-30）

**缺陷**：去假数据那一轮在 `ShowEmptyDetail()` 里把六个互动按钮（私聊/组队/传送/赠礼/飞鸽/删除）统一 `Enabled=false`，但 `UpdateDetail()`（选中好友时走）**没有恢复路径**。只要页面经历过一次空名单渲染（加载中、切到空的仇人/黑名单页签、服务端还没回包），之后即使名单到了、选中了真实好友，六个按钮仍然是灰的——私聊/飞鸽/删除这三条**已接线**的功能就此永久失效。

**修法**：`UpdateDetail()` 填完身份字段后恢复全部按钮（`FriendsPage.cs`）。组队/传送/赠礼的处理器本身会弹「功能尚未接入」，属于诚实反馈而非假动作，所以一并恢复；空态那侧继续全禁（没有对象）。

**验证**（本机没有真实好友数据，空名单下这条路径永远走不到，所以加了自测夹具）：
- 新增标记 `%APPDATA%/HundunWorld/friends-selftest.on` → 页面 Update 里注入一条 **Id=0 的示例条目**（与代码既有"示例好友"同语义，真实动作仍会被拒），走 `RebuildRows → SelectFriend(0) → UpdateDetail`，并把按钮启用数写回执；
- 回执：`friends-selftest enabled=6 of=6 sel=0`；
- 开关两侧截图对照：带标记时详情栏是「自测好友/在线」，六个按钮全部可点；移除标记后回到「暂无好友」，六个按钮回到灰态。两个分支都有证据。
- 离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`（DLL mtime 刷新）。

**边界**：夹具注入的是 Id=0（示例）条目——启用逻辑本身不看 Id，但"真实好友（Id≠0）点击私聊→ `WhisperRequest` → HUD 进入密聊"这段跨页链路仍需要真实好友才能完整走通，未在本轮取证。

## 82. 清掉最后一批"示例玩家状态"：HUD 血量 / 商店余额 / 顶栏姓名（2026-10-30）

按"页面初值必须是占位符而不是设计稿数字"的口径做了一次全仓扫描（`grep 无名侠|Lv.42|12,450|逍遥派`），清掉三处会影响真实判断的残留：

1. **默认 HUD 的构造期数值**（`CombatHudTraditionalPage`）：`逍遥客 / Lv.60 / 气血 12450/15000 / 内力 800/1000 / 经验 45%` 全是设计稿数字。虽然进世界后 `BindCharacter` 会覆盖，但 HUD 在玩家就绪前显示的就是这些假值。改为 `— / 0 / 0/0 / 0/0 / 0%`，绑定后由真实属性覆盖。
2. **商店页余额**（`MenuShopPage`）：`铜钱 12,450 / 银两 328` 是写死初值，且**购买校验直接拿这两个数判余额**——没有商人时点确认会真的扣掉这两个编出来的数字并提示"已购入"。改为：两笔余额初值 0；铜钱只在 `ShopClientService` 给过快照（`_hasCopperSnapshot`）时显示数字，否则 `—`；银两没有下行通道，一律 `—`。
3. **商店示例货架不再"假装买到"**：没有商人上下文（`_vendorEntityId == 0`）时确认购买直接提示「未与商人交谈，示例货架不可购买」，不再修改本地余额、不再报"已购入 ×N"。原先那段本地扣款/成功文案整段删除。
4. **五个页面的顶栏姓名占位**（营生/武林录/任务/商店/个人信息）由 `无名侠` 改为 `—`；个人信息页左侧竖排标题同样改 `—`。真实昵称仍由 `InkPlayerHeaderBinder`/`_realNick` 覆盖，改的只是"没有玩家时显示什么"。

**验证**：离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`、DLL mtime 刷新；PIE 进世界后导航 `nav-shop` 截图确认页面结构、货架、空态横幅均正常（顶栏铜钱/银两在画面右缘之外，属 §72 已记录的窄视口裁切，无法截图取证——这两格的显示由代码路径确认：初值 `—`、`RefreshBalances` 在无快照时写 `—`）。示例货架"不可购买"分支与 HUD 构造期中性值同样只能按代码路径记录（没有商人/没有"未绑定玩家"的可控窗口）。

**残余**：`_silverBalance` 仍保留作为商品可购性展示用（恒 0 → 「不足」），等银两有下行通道再接。

## 83. 阵亡界面接上真实死亡状态（2026-10-30）

**问题**：`death-screen` 页面只注册、没有任何导航方——“破招”入口来自 `nav-element-vision` 的设计链链接，真实死亡时玩家什么都看不到。同时页面上的「损失银两 ×500，经验 ×200」既是设计稿数字，字段还用反了（银两取了经验值）。

**改动**
1. `MainUIManager` 新增 `WatchLocalPlayerDeath()`（每帧，边沿检测）：
   - 死亡态来源是**权威值**——同步系统把服务端状态写进本地玩家的 `CharacterAttributesComponent.IsDead`，UI 只做"翻转 → 弹/收"，不自己判生死；
   - `false→true`：仅在战斗 HUD 上下文且当前无弹层时 `NavigateToPage(death-screen)`（选角/创角/其它全屏页上不打扰）；
   - `true→false`（复活）：若当前弹层就是阵亡界面则 `CloseOverlay()`；
   - 玩家在阵亡界面点「返回/破招」自行关掉后，本次死亡不再自动弹回，直到复活后再次阵亡。
2. 损失文案改为占位：「损失银两 ×—，经验 ×—」，删掉 `_expLoss/_coinLoss` 两个设计稿字段；服务端目前没有死亡惩罚字段，注释里写明将来由 `SetLoss` 注入。

**验证**：离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`、DLL mtime 刷新；PIE 进世界后导航 `death-screen`（回执 `nav-retry death-screen ok=True`）截图确认：殒命标题 / 「汝之武途，至此终结」/ 「损失银两 ×—，经验 ×—」/ 原地复活 / 返回最近的城镇 全部正常，弹层盖在战斗 HUD 之上。

**边界**：自动化里造不出真实死亡——MCP 的 actor 工具只认编辑器场景，PIE 的 `LocalPlayer_39` 不在其中，`actor_set_property` 打不到；因此「IsDead 翻转 → 自动弹/收」只能按代码路径记录，未做运行时取证。另：世界层的屏幕空间名牌（`杂役 赵问剑`）仍浮在阵亡界面之上，属世界叠加层，本轮未处理。

## 84. 弹层打开时收起头顶名牌（2026-10-30）

**缺陷**：`_nameplateHost.Visible` 的判定是「玩家开关打开 + 当前页是三种战斗 HUD 之一 + 水墨 UI 健康」。但 Ink 的 Modal 弹层（阵亡界面 / 获得物品 / 留言…）**不占页面槽位**——`CurrentPageDomId` 仍是战斗 HUD，`HasOverlay` 才是"有弹层"的事实。结果名牌（`杂役 / 赵问剑 Lv.1` + 血条）浮在阵亡界面之上（§83 截图已实证）。

**修法**：判定补一条 `!_inkPageRouter.HasOverlay`。名牌宿主是唯一的统一开关（`NameplateUI` 每帧查 `host.Visible` 决定自绘与否），所以一处改动覆盖所有弹层。

**验证**（同一 PIE 会话三段截图）：
1. 战斗 HUD：名牌可见（`杂役 / 赵问剑 Lv.1` + 绿色血条）；
2. 打开 `death-screen` 弹层后：名牌**消失**，只剩殒命标题 / 损失行 / 两个按钮；
3. 回战斗 HUD（导航落地即收起弹层）：名牌**恢复**。
离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`、DLL mtime 刷新。

**顺带记录（场景侧，非 UI）**：本轮 PIE 的世界画面整体过曝发白（地面/天空近白），HUD 的浅色文字与名牌在白底上对比度很低。`Cube/Sun/SkyLight` 都在 World 场景，属于场景/光照状态，不在 UI 层处置——但若这是新的默认观感，HUD 配色需要配套调整，留给场景负责方确认后再决定是否改 UI。

## 85. 战前备战「武学搭配」改读已学技能（2026-10-30）

**问题**：备战页中栏的 3 张主动 + 2 张被动武学卡是写死的设计稿招式（狂风快剑 / 降龙掌 / 凌波微步 / 金刚护体 / 吐纳法），等级格写 `—`。玩家看到的是"自己好像会这些武功"——与删掉的 `SkillPanelPage` 同一类假数据，只是藏在一个可达页面里。

**改动**（`MenuBattlePrepPage`）
1. 删掉 `_mockActiveSkills/_mockPassiveSkills`，字段改为 `_activeSkills/_passiveSkills`，默认只放一行占位（「暂无已习得武学 / 在技能树研读秘籍后显示在这里」，等级 `—`）。
2. 构造期先 `LoadSkillsFromCatalog()` 再建卡：遍历 `SkillCatalogService.Learned` + `SkillDatabase.GetSkill`，`Support` 归被动、其余归主动，按技能 id 排序后各取前 3 / 前 2；卡上等级用服务端已学等级（`Lv.N`），说明用「五行 · 类型 · 倍率 ×X」组合，图标字用五行单字。
3. 目录未到（`Learned.Count == 0`）时保留占位行，不再回退到设计稿招式。

**验证**：离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`、DLL 刷新；PIE 进世界导航 `nav-battle-prep` 截图确认：两栏都显示占位行（本机角色没有已学技能），右栏战力评估 `0 / 0`、达成率 0%，气血 400 / 内力 1000 / 体力 0 来自真实属性——页面不再出现任何设计稿招式名。

**边界**：本机角色 `SkillCatalogService.Learned` 为空，所以"有已学技能时按 3+2 填卡"这条分支没有运行时证据，只按代码路径记录；与 §81 好友按钮同类（无数据就走不到）。

## 86. 战前备战「药品补给」接背包消耗品 + 快速补给不再凭空补满（2026-10-30）

**问题**：右栏四条药品是设计稿写死的（回气丹/金创药/解毒散/聚神散，数量 0），"补给充足度"默认写「良好」；底部「快速补给」按钮会在**本地**把四种药补到 20 个并改汇总为「充足」——等于凭空造出药品，和服务页那个"假购买"是同一类。

**改动**（`MenuBattlePrepPage`）
1. `_mockMedicines` → `_medicines`，默认只留一行占位「暂无可用药品 / 与药师交易或拾取后显示」。
2. 构造期 `LoadMedicinesFromInventory()`：遍历 `PlayerInventoryService.Items`，`ItemCatalog.Resolve` 里 `Kind == Consumable` 且数量 >0 的按数量降序取前 4 条，名称/图标字取物品表，效果取物品描述（超 14 字截断）。
3. 「补给充足度」按真实总数算：0 → 「无补给」（朱红）、<5 → 「偏少」（鎏金）、≥5 → 「充足」（翡翠），初始值由「良好」改 `—`。
4. 「快速补给」改为提示「快速补给尚未接入：请与药师交易补给」，不再改任何数量；`MedicineFullQuantity` 常量随之删除。
5. `BindSupplies(int[])` 保留（没有调用方，作为将来有补给来源时的注入点），改读写 `_medicines`。

**验证**：离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`、DLL 刷新；PIE 导航 `nav-battle-prep` 页面正常渲染、无异常（占位行在右栏，右栏位于 1920 设计宽度的视口外，按 §72 的口径无法截图取证）。「有消耗品时按 4 行填充」与「快速补给只提示」两条分支本机没有可控数据，按代码路径记录。

## 87. 设置页三处：标签压字 / 假硬件信息 / 顶栏不重绑（2026-10-30）

1. **行标签压标题**：`ApplyLayout` 里标题宽度和标签 X 都按 `字数×15` 估算，而 15f Heading 的中文实宽约 17~18px ——「画面质量+推荐」「抗锯齿+推荐」「画质省电模式+实验」三处标签直接压在标题上（放大截图实证）。改为 `18px/字`、标签再留 8px 间距。
2. **画面信息是假硬件**：写死「NVIDIA RTX 4070 / 4.2 GB / 12 GB / 58 fps」。显卡型号与显存占用没有可用的引擎读取接口 → 一律显示 `—`；**当前帧率改接 `Engine.FramesPerSecond`**，`Update` 里每 0.5 秒写一次（截图实测 12 fps，随场景负载变化）。
3. **顶栏停在设计占位**：页面在"刚进世界、玩家属性还没就绪"时构造，`InkPlayerHeaderBinder.Bind` 拿不到属性，之后没有重绑，顶栏一直是「江湖过客 / Lv. 42」。初值改 `—`，并在 `Update` 里做一次性自愈：每 0.5 秒重试绑定，名字不再是 `—` 即停；上限 20 次，避免无昵称时无限轮询。

**验证**：离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`、DLL 刷新；PIE「进世界后打开 nav-settings」截图确认：三处标签与标题有间距、画面信息为「显卡 — / 显存占用 — / 当前帧率 12 fps」、顶栏为「赵问剑 Lv. 1」。修复前的同页截图（`sweep/nav-settings.png`）可作对照。

**边界**：显卡型号/显存占用等 Flax 未暴露的接口保持 `—`；设置项本身的写回行为仍以 §46–§75 的记录为准（只有「自动隐藏UI」真正生效）。

## 88. 设置项真正下发引擎：只有 VSync 即时生效，质量档位写进去不读回（2026-10-30）

**背景**：`GameConfigurationService` 的十几个 `Apply*Change` 全是 `Debug.Log` 占位，设置值只落 LiteDB、引擎侧从没变过。

**这轮做的事**
1. 在设置页打开时调用新增的 `GameConfigurationService.ApplyGraphicsSettings()`，把已持久化的画面项统一下发一次（否则重启后画面仍是引擎默认）：
   - `Graphics.UseVSync = GetVSync()`
   - `Graphics.AAQuality = 抗锯齿 ? High : Low`
   - `Graphics.ShadowsQuality = ToQuality(阴影档)`
   - `Graphics.SSRQuality / VolumetricFogQuality = ToQuality(特效档)`
   - 画面质量预设（流畅/标准/高清/极致）→ 三项质量一起改；三个单项 setter 保存后也各自即时下发。
2. 帧率上限那一行**没有**引擎接口（`FlaxEngine.dll` 里只有 `UnfocusedMaxFPS`、`PhysicsFPS/NetworkFPS/DrawFPS`，没有帧率上限字段），行描述改为「引擎暂无帧率上限接口，此项只记录」。

**验证与反证**（临时回执 `gfx-apply ...` 写 `ui-selftest.out`，验完已删、DLL 里该串计数 0）
- `UseVSync`：把值改成 `false` 后读回 `False` → **确实生效**；
- 质量三项：分别把 `VolumetricFogQuality`/`AAQuality` 置 `Low` 后读回仍是 `High` → **运行时写不进去**（同一组属性和同一套 `Graphics::Internal_*` 绑定，只有 VSync 能即时读回）。
- 因此代码里保留这些赋值（打包后可能在渲染器初始化时被消费），但注释明确写了"实测读回不变、不要在 UI 上承诺切了立刻变"。

离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`、DLL 刷新。

**边界/后续**：质量档位要真正即时生效，需要找渲染器的运行时接口（`GraphicsDevice`/渲染任务上的质量字段），本轮只确认 `Graphics.*Quality` 不是它。这条已写进文档，避免后来者重复"写了 setter 就当生效"。

## 89. 鼠标灵敏度接真实相机输入（2026-10-30）

**问题**：`GameConfigurationService.SetMouseSensitivityAsync` 的值只落 LiteDB、`ApplyMouseSensitivityChange` 只打日志；ECS 相机里写死 `camera.Yaw += mouseX * 2.0f`，所以设置页的灵敏度滑块**改了没有任何效果**。

**改动**
1. `GameConfigurationService`：新增 `static float MouseSensitivityFactor`（0.1~3）+ `EnsureMouseSensitivityLoaded()`，在服务构造、`ApplyMouseSensitivityChange`（保存成功即调用）里维护；相机每帧只读这个静态值，不查库。
2. `CameraSystem.Initialize` 起来时调用一次 `EnsureMouseSensitivityLoaded()`（否则玩家在设置页动手之前一直是默认 1）。
3. `CameraSystem.UpdateCameraInput`：`yaw/pitch` 的增量由写死的 `2.0f` 改为 `2.0f * MouseSensitivityFactor`。

**验证**：离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`、DLL 刷新；临时回执（验后已删，DLL 里 `sens-load` 串计数 0）在进世界时打出 `sens-load factor=1`，证明相机系统初始化路径确实把持久化值载入了因子（默认 1）。

**边界**：鼠标拖动 yaw 的实际手感需要人工右键拖拽验证；自动化桥没有鼠标注入能力。同一批里「画面质量/抗锯齿/阴影/特效」四项经回执实测**写进去读不回**（见 §88），只有 `Graphics.UseVSync` 与灵敏度这两条是真正即时生效的。

## 90. 纠正 §89 的挂钩点：真实相机是 ThirdPersonCamera，并接上视距（2026-10-30）

**§89 的错误**：灵敏度先是挂在 `HundunWorld.Game.ECS.Systems.CameraSystem` 上，回执也打出了 `sens-load factor=1`，看起来通了。但这套 ECS 系统**从未注册**（全仓 `new CameraSystem` / `AddSystem` 搜索为 0），游戏里真正处理视角的是 `ThirdPersonCamera`（`RotationSpeed = 0.2f`，`Input.MousePositionDelta`）。也就是说 §89 那条回执只证明"initialize 里的那行跑过"，不证明视角会变——典型的"机制层绿灯、效果层没接"。已把 `CameraSystem` 的改动整文件回退。

**真正的落地**（`ThirdPersonCamera`）
1. `OnStart`（相机组件解析成功之后）载入 `EnsureMouseSensitivityLoaded()` / `EnsureViewDistanceLoaded()` 并下发一次视距；
2. `OnUpdate` 每帧 `ApplyViewDistance()`：视距百分比 → `Camera.FarPlane`（100%→150m、0%→20m，线性；值没变只做一次浮点比较）；
3. 旋转：`Yaw += mouseDelta.X * RotationSpeed * MouseSensitivityFactor`（Pitch 同理）。

**验证**：离线 `csc` 0 error；编辑器构建 green、DLL 刷新；临时回执（验后已删，DLL 里 `vd-apply` 串计数 0）在进世界后打出
`vd-apply percent=75 far=11750 sens=1` —— 75% 映射出的 11750cm 与公式一致、且是从相机读回来的值（setter 确实写进去了），灵敏度因子也确实载入了。
**边界**：鼠标拖拽手感仍需人工；本次只证明"配置 → 相机远裁剪面/因子"这条链在真实相机上跑通。

## 91. 阵亡界面「原地复活」接服务端权威复活（2026-10-30）

**背景**：§83 让阵亡界面在真实死亡时弹出，但「原地复活」按钮当时按设计稿链到 `qte` 演示页（QTE 页没有任何服务端支持，进去只会看到一个不会结算的假小游戏）。而服务端其实早有权威复活：`CharacterHandler.HandleResurrectAsync` → `ICharacterGrain.ResurrectAsync`，成功后以"血量真的回了"为判据回包（不是只看 grain 没抛异常）。

**改动**（`MainUIManager.CreateDeathScreen`）
- `ReviveRequested` 不再跳 QTE，改为发 `ResurrectMessage { ResurrectedId = PlayerId, ResurrectPosition = 本地玩家坐标, ResurrectType = 0 }`（`NetworkManager.SendAsync`，与制作/好友请求同一发送通道）；
- 未连接时提示「未连接服务器，无法复活」，发出后提示「已请求复活…」；
- 界面收起仍由 §83 的死亡态监听负责：服务端把 `IsDead` 写回 false 后自动 `CloseOverlay()`，客户端不自己改死亡标记。

**验证**：离线 `csc` 0 error（第一次报 CS0246 是因为 `MainUIManager` 没有 `using Horizon.Game.Message.Network`，改用全限定名后通过）；服务端 handler 与消息定义已按源码核对（`ResurrectMessage` 含 ResurrectedId/ResurrectPosition/ResurrectType，回包含 RemainingHealth）。
**边界**：完整链路（真死→点复活→服务端回血→界面自动收起）需要真实死亡与服务端在线，本轮无法自动化；另外本轮编辑器重启后 HundunAgent 桥（21901）一直没起来，所以**编辑器构建未执行**，只有离线 csc 与代码审查证据。QTE / 点穴两页因此不再有入口（设计页，保留文件不动）。

## 92. 阵亡界面去掉假的「15秒」冷却 + 一次构建失败的归因（2026-10-30）

**改动**：`DeathScreenPage` 复活按钮下方原本写死设计稿的「15秒」冷却提示——服务端没有复活冷却字段、页面里也没有倒计时，它永远不会变。改成「由服务端结算」，尺寸 120×18 并居中（原 60 宽放不下新文案）。

**验证**：离线 `csc` 0 error；PIE 打开 `death-screen` 放大截图确认文案为「由服务端结算」；`Game.CSharp.dll` 里该串计数 1。

**同轮构建事故（值得记的坑）**：`code_build_wait` 连续两次 `failed`，日志真因是
`xcopy Horizon.Game.Message/bin/Debug → Binaries/GameEditorTarget/...` exit code 4 ——
- 并行会话 15:28 重建了 `Horizon.Game.Message.dll`（源比编辑器里的副本新），构建因此每次都要拷贝；
- 编辑器**自己**加载着目标副本，拷贝被占用失败，整个构建标记 failed；
- 由于构建在拷贝步就失败，**热重载没有应用**：`Game.CSharp.dll` 虽然已更新，但 PIE 里打开的阵亡界面仍显示旧的「15秒」。
处理：关掉编辑器（`Stop-Process` 掉本次会话启动的那个 PID）→ `cp -f` 同步被占用的 DLL → 重启编辑器，启动编译 `compilationsCount=1 / lastCompilationFailed=false`，再跑 PIE 才看到新文案。
教训：**构建 failed 时要看是"编译错"还是"拷贝被占用"**；后者不是代码问题，但同样会让运行中的装配停在旧版本。

## 93. 活动页去假状态：目录保留、进度与倒计时归零（2026-10-30）

**问题**：`nav-activities` 六个活动条目的状态/倒计时/参与进度全是设计稿值（进行中 4 / 即将开始 1 / 已结束 1、60%/35%/80%/15%/100%、「3天剩余」「14:23:08」），右侧「参与活动」还会把人直接送进备战页——服务端没有任何活动报名/进度/结算通路。

**改动**（`MenuActivitiesPage`）
1. 六个 `ActivitySpec` 的 `StatusText` 统一「未开始」、`TimeText` 统一「—」、`Progress` 归零、`ProgressValueText` 统一「0%」；两处异常口径的 `ProgressCaption`（「未开始」「已完成」）统一成「参与进度」。
2. 精选横幅：副标题追加「（活动系统未接入，以下为目录样例）」，倒计时天数与时间都改 `—`。
3. 顶栏等级占位 `Lv. 42` → `—`（真实等级由 `InkPlayerHeaderBinder` 覆盖）。
4. 「参与活动」不再跳备战，改为提示「活动系统尚未接入服务端，暂不可参与」。
活动名称/简介/奖励/规则属目录内容，保留不动。

**验证**：离线 `csc` 0 error；编辑器构建 `lastCompilationFailed=false`；PIE 导航 `nav-activities`（回执 `nav-retry nav-activities ok=True cur=nav-activities`）截图确认：标签页计数「全部 6 / 进行中 0 / 即将开始 0 / 已结束 0」、六条目「未开始 · 时— · 参与进度 0%」、横幅「剩余 — —」且带样例说明。

## 94. 2026-10-30 收尾审计：页面三态与验证证据（覆盖 §76–§93）

**本轮新增/修改的页面状态**

| 页面 | 状态 | 证据 |
|---|---|---|
| `nav-skill-tree`（武学入口） | 真数据（服务端目录 + skills.json） | §76：样例面板删除、入口改指、截图 |
| `popup-martial-detail` | 真数据（SkillConfig：名称/属性/连招链/learnCost） | §76：截图显示「裂金斩 ×1.5 / 3 秒 / 连招链 3 式」 |
| `nav-achievement` | 诚实占位（计数/进度全 0，卡片网格与分类行距已修） | §79：截图 |
| `nav-gacha` | 目录内容 + 定价（进度/保底 0），按钮区排版已修 | §78：截图 |
| `nav-world-map` | 真数据（POI 方位/名称，文字不再被图标压住） | §78：放大截图 |
| `nav-mentor` | 诚实占位（任务 0/N），奖励文案裁切已修 | §78：放大截图 |
| `nav-team` / `nav-sect` | 空态 + 按钮禁用；侧栏品牌间距已修 | §80：放大截图 |
| `nav-settings` | VSync/灵敏度/视距真生效；显卡/显存 `—`；帧率实时 | §87–§90：回执 + 截图 |
| `nav-battle-prep` | 武学卡读 `SkillCatalogService`；药品读背包消耗品；快速补给只提示 | §85–§86：截图/代码路径 |
| `nav-shop` | 余额 `—`（快照前）、示例货架不可购买 | §82：截图 |
| `nav-activities` | 目录保留、状态/进度/倒计时归零 + 样例说明 | §93：截图 |
| `death-screen` | 真实死亡自动弹出、复活走服务端权威、损失行占位 | §83/§91/§92：截图 + 代码路径 |
| 头顶名牌 | 弹层打开时收起 | §84：三段截图 |
| `nav-bestiary` / `nav-battle-pass` | 诚实空态（「图鉴数据尚未接入」「Lv.0 / EXP 0 / —」） | 本轮截图 |

**本轮回归**：`ui_sweep` 连跑 12 个代表页（角色/装备/技能树/强化/制造/商店/好友/师徒/成就/秘境/世界地图/坐骑宠物），全部 `ok` 且亮度/颜色数各异（页面确实渲染），无 `ok=False`、无 BLANK。

**仍然缺的（都不是客户端能单独补的）**
1. 装备/强化：`+N`、成功率、属性聚合（气血/内力/暴击/命中/闪避/套装）——服务端字段缺失，且 `HandleEquipmentEnhanceAsync` 是空壳；
2. 组队/帮会/邮件/排行/成就/活动：`HandleTeamAsync`、`HandleGuildAsync` 等服务端也是空壳（返回成功 + 空对象），客户端只能保持禁用/空态；
3. 死亡→复活的端到端（真死 + 服务端在线）与鼠标拖拽手感：自动化环境造不出来，只能人工；
4. 点击级 UI 自动化仍不存在（§72），涉及"点了会怎样"的结论都标了代码路径。

**§93 补记**：六个活动的详情「活动时间」原本是写死的排期（`2026.07.08 — 2026.07.15` 等），同样属于"看起来有真实档期"的假状态，统一改为 `—`。截图确认右侧详情面板的活动时间显示为「—」，条目仍为「未开始 · 时— · 参与进度 0%」，横幅「剩余 — —（活动系统未接入，以下为目录样例）」。全仓再扫 `"20xx.` 字面量，只剩一处验证弹窗的示例内容（设计样张，保留）。

## 95. 多人模式页：网络状态条压在标题上（构造期坐标没跟着页面尺寸重排）（2026-10-30）

**缺陷**：`MultiplayerPage` 的「网络延迟 / 服务器」状态条只在构造函数里按**当时的窗口宽度**定位（`_screenSize.X * 0.5f - 120f`）。页面进 Ink 内容层后宽度变成 1920 设计宽，`RefreshLayout()` 只更新了 `_bgLayer` / `_topBar` / 内容区，**没有重排这条**，于是它落在左上角，和「多人模式」标题、返回按钮叠在一起（放大截图可见「网络延迟:」被标题压住半截）。

**修法**：`RefreshLayout()` 里按真实页面宽度重新居中：`_netStatus.Location = ((_screenSize.X - Width) * 0.5f, 16f)`。
顺带扫了同款「构造期按 `_screenSize.X * 0.5f` 定位」的写法：`ChapterTransitionPage` 两处都在 `RefreshLayout` 里重排（无此问题），只剩多人页这一处。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE 导航 `nav-multiplayer`（回执 `nav-retry nav-multiplayer ok=True`）截图确认：左侧「返回 / 多人模式 / 江湖徽章」不再被压，状态条移到页头中央（1920 设计宽的中心，在本机窄视口里落在右缘）。

## 96. 音效设置页：构造期异常让整页只剩一半 + 音量从来没被持久化读回（2026-10-30）

本节三件事，前两件是缺陷，第三件是顺带纠正的口径。

**缺陷一：`AddChild` 撞上"已经是父级"，构造链在设备面板处整段中断。**
`BuildSettingsList()` 里的 `MakeDeviceLabel(...)` 是"自挂载"辅助方法——它在对象初始化器里就写了 `Parent = _devicePanel`，而调用方又在外面套了一层 `_devicePanel.AddChild(...)`。Flax 的 `ContainerControl.AddChild` 对"child 已经是自己的子级"直接抛 `InvalidOperationException`（`ContainerControl.cs:214`），于是：

- 设备面板只挂上第一行就抛；`BuildActionBar()`、`ApplyLayout()`、`RefreshAllData()` **全都没执行**；
- 页面看起来"能打开"，实际是：设备面板空着、没有 Location（贴在内容区左上角）、底部操作条整块不存在、顶栏还是设计稿占位。
- 异常被构造函数外层的 `catch` 吞成一条 `LogError`，界面上没有任何提示——这正是"整页缺一块但没人报错"的老形态。

修法：删掉外面那层 `AddChild`（Parent 已由辅助方法给出），并留一行注释说明为什么不能两个都写。
**验证**：截图对比 —— 修复前设备面板是一个空白长条、底部无操作条；修复后三行（输出设备 / 输入设备 / 采样率）与底部「改动即时生效 · 5 秒内自动保存」同时出现。

**缺陷二：`GameStateManager.Initialize()` 没有任何调用方 ⇒ 设置从来没从 `settings.json` 读回来。**
`GameStateManager.Instance` 只做 `??= new GameStateManager()`，而 `Initialize()`（`LoadSettings()` + `ApplySettings()`）全仓零调用。后果是**所有** `GameSettings` 都跑默认值：

- 设置页显示的 100 / 70 / 80 / 90 恰好等于 `GameSettings` 的默认值，掩盖了没读盘这件事；
- 环境音在 `settings.json` 里是 `0.7924928`，页面却显示 `60`（默认值）——这就是没读盘的直接证据；
- 启动时 `ApplySettings()` 也没跑，音频管理器拿到的是引擎默认音量，与磁盘上的设置无关。

修法：把 `Instance` 改成"首次取实例时 `Initialize()` 一次"（带 try/catch 兜底，失败退回默认值并告警）。
**验证**：同一台机、同一份 `settings.json`，修复后 PIE 进 `nav-settings-audio`，环境音显示 **79**（= 0.7924928），其余四项 100/70/80/90 与文件一致。

**顺带纠正一：不要把 LiteDB 的音量盖到音效页上。**
本轮先前曾让音效页构造时调用 `GameConfigurationService.ApplyAudioSettings()`（LiteDB `MasterVolume=0.77 / Music=0.8 / SFX=0.85`）。但音效页的滑条读写的是 `GameStateManager`（JSON），两套后端各有一份音量：页面显示的是 JSON，运行时被 LiteDB 覆盖，属于"界面 100、实际 0.77"的假同步。已删除该调用与 `ApplyAudioSettings()`；音频的唯一真源是 `GameStateManager.ApplySettings()`（`UpdateSettings` 每次写入即应用，`HundunWorldGame.OnUpdate` 驱动 5 秒自动落盘）。

**顺带纠正二：分类标签上的计数是设计稿数字。**
设置页与音效页都把 `.cat-count` 硬编码成 `{10, 8, 12, 6, 5}` / `{10, 7, 12, 6, 5}`。实测：画面 = 设置主页确实有 10 个设置行，音效 = 音频子页 5 滑条 + 2 开关 = 7；操作/账号/其他三个分类页根本不存在，12/6/5 是凭空的。两页统一改成 `{"10","7","—","—","—"}`，未接入的分类显示 `—`，点它仍给「该分类的设置项尚未接入」提示。

**仍然缺的**：设备枚举（仍是「系统默认（设备枚举尚未接入）」）、语音聊天（只持久化开关，客户端没有语音通话链路）、点击级自动化（滑条拖动只能人工验证手感）。

### 96.1 设置页底栏的「有 3 项设置已修改，尚未保存」是样张（同日补记）

**缺陷**：`SettingsPage.BuildActionBar()` 把设计稿那句「有 3 项设置已修改，尚未保存」直接写进 `Label.Text`，全页没有任何"待保存"计数逻辑（`grep 尚未保存` 只命中这一处），旁边还画了一颗红色 `InkChangeDot`。而实际行为相反：每一项交互（下拉/滑条/开关）都会立刻写回 `GameConfigurationService` 即时落库，`SaveAllSettings()` 只是再全量刷一遍。于是**什么都没改也显示"3 项未保存"**，属于凭空造的界面状态。

**修法**：文案改为「改动即时生效并已保存」，去掉红点与其 `InkChangeDot` 内部类（无其它使用者），标签左边距 40 → 24 补齐空出的位。
**验证**：离线 `csc` 0 error；编辑器构建 success（`lastCompilationFailed=false`，DLL mtime 18:34:46）；PIE 导航 `nav-settings` 截图：底栏左侧显示「改动即时生效并已保存」，无红点；分类计数同时显示 10 / 7 / — / — / —。

## 97. 顶栏「戌时 三刻 / 元宝 328 / 江湖过客 Lv.42」成批清理 + 奇珍阁顶栏九个控件叠在 (0,0)（2026-10-30）

**假状态一：世界时间**。世界时间同步没有接入客户端（`MenuTimePage` 自己就写着"当前时辰没有数据源"，快进按钮禁用）。但顶栏在 6 处摆着设计稿的「戌时 三刻」：设置页 `_gameTimeLabel`、音效页 `_topTime`、奇珍阁页、活动页 `TimeText` 常量、通行证页 `_timeLabel`、拍照页 `时间 · 戌时`（连同「天候 薄暮」「风向 东南微风」也都没有数据源）。全部改为 `—`。

**假状态二：元宝/银两**。`ShopClientService` 只有 `Gold` 一个快照字段，没有元宝/银两。设置页 `_ingotValueLabel`、音效页 `_topIngot`、奇珍阁页 `currencyIngot`、外观页 `_currencyIngotLabel`（"银两 328"）原本都摆着 328。

**假状态三：昵称/等级初值**。奇珍阁页、门派页、个人信息页的初值仍是「江湖过客 / Lv. 42 / 无名侠 / Lv.42 / 逍遥派」。`InkPlayerHeaderBinder` 只在拿到真实值时才覆盖，拿不到就永远显示样张。初值统一改 `—`（真实值一到就被覆盖，没到也不骗人）。

**布局缺陷：奇珍阁顶栏九个控件都是局部变量且没有 `Location`/`Size`**。头像圈、昵称、等级、铜钱/元宝各自的值与文字、时辰全部叠在 `_topBar` 的 (0,0)，尺寸为 0 ⇒ 画面上一个都看不见（能看到的「赵问剑」是页面外壳的，不是这一页的）。修法：提升为字段并给固定 `Size`，在 `ApplyLayout` 里按顶栏实际宽度排：左簇 头像(16,12)+昵称(62)+等级(186)；右簇自右向左排 时辰 / 元宝值+标签 / 铜钱值+标签。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 18:42:24）；PIE 导航 `nav-shop-rare` 截图 + `ui_tree` 实测坐标：头像 (16,12,36,36)、昵称 (62,0,120,60)、等级 (186,0,80,60)、铜钱标签 1312、铜钱值 1406、元宝标签 1460、元宝值 1534、时辰 1584，顶栏实测 1680×60，全部落在设计画布内；截图里昵称显示真实角色名「赵问剑 / Lv. 1」。

**边界**：本机 PIE 的窗口比 16:9 更"高"，外壳按高度居中后横向裁掉右栏（已知的 `InkPageShell` letterbox 行为，不是页面缺陷），所以截图里只看到右簇最左侧的一个 `—`。判断这类"看不见"一律以 `ui_tree` 的设计坐标为准。

## 98. 祈愿页：本地自增保底 + 假倒计时 + 假概率（2026-10-30）

**最严重的一处**：`OnDrawRequested()` 点击「祈愿一次 / 祈愿十次」时**在本地**累加 `_drawCount` / `_smallPity` / `_greatPity` 并刷新保底面板与进度条，没有任何服务端调用、没有发放任何物品、也没有任何"不可用"提示。玩家会以为真的抽了卡、保底真的推进了。现改为：点击只弹「祈愿系统尚未接入服务端，暂不可祈愿」，计数一律不动。

**假倒计时**：「卡池剩余 12 天 07 时 23 分」是设计稿常量。卡池排期在服务端，客户端没有来源，却摆成一个正在走的限时倒计时。现改为未绑定时只显示一个 `—`，连"天/时/分"单位一起隐藏（避免「— 天 — 时 — 分」）；`BindPoolCountdown` 绑到真实值后单位与数字一起回来。

**假概率**：「概率概览」四行 `1.20% / 8.80% / 30.00% / 60.00%` 与两条保底规则「90抽必得传说名侠」「10抽必得精良以上」都是设计稿样例。概率是必须公示的合规信息，摆假数字比摆空更糟。现改为四行概率 `—`、标题「概率概览（未接入）」、底部一句「概率与保底规则尚未接入服务端」。

**假余额**：命星初值 5 改为未知态 `-1` → 显示 `—`（元宝本来就是 `-1` 显示 `—`）。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 18:48:16）；PIE `nav-gacha` 截图：左下「卡池剩余」只剩一个 `—`，四行概率不可见部分由 `ui_tree` 确认为 `—`；`ui_tree` 实测页面 1920×1080、顶栏 1920×60、左栏 200×1020、底部抽卡区 1720×136 均在设计画布内。

**边界**：按钮点击效果（toast）本环境无法用鼠标注入验证，只能靠代码路径 + 同页其它 toast 的既有表现；`BindAccount/BindPity/BindPoolCountdown/BindDrawCost` 仍无调用方，等祈愿服务端接入后由宿主调用即可恢复真值。

## 99. 组队页「队伍设置」被面板裁掉一行 + 闲趣页筛选按钮叠在左上角（2026-10-30）

**组队页（`nav-team`）**：右侧「队伍设置」面板写死 200 高，内部四行（队伍类型 / 自动接受 / 最低等级 / 分配方式）按 `settingsY = 20+32 → +44 → +44 → +44` 排，最后一行从 y=184 起、高 28，底边 212 —— 超出面板 12px，截图里「自由 / 轮流 / 队长分配」被切掉一半。修法：面板高 200 → 232，并在代码里写清这四行的累加过程。顺带把「最低等级」数值标签显式设成 50×20（Label 默认 100 宽，右缘会伸出面板 30px）。

**闲趣页（`nav-casual-mode`）**：
1. 「筛选」按钮是局部变量、从未定位，一直贴在内容区 (0,0)，被「今日活跃度」面板压住；现提为字段，排在筛选条行右缘（`contentRight-60, 166`）。
2. 页面所有任务卡与「今日奖励总览」（9 项奖励、铜钱 ×800 等）全是设计稿目录数据，页面没有任何"未接入"字样。现在筛选条行右侧加一句「闲趣任务与奖励尚未接入服务端，以下为目录样例」，放在筛选按钮左边（`contentRight-60-16-320, 160`），宽 320 右对齐。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 18:57:21）；PIE 连跑 `nav-casual-mode` / `nav-team` 均 `ok`（亮度 21.6 / 17.0，颜色数 1980+）；`ui_tree` 实测：
- 组队页「队伍设置」面板 738×**232**，最后一行按钮 y=184 底边 212 < 232；「最低等级」标签右缘 718 < 738；
- 闲趣页筛选条行：四个 Tab 24/124/224/324，样例说明 1260..1580，筛选按钮 1596..1656（内容区右缘 1656），三者互不重叠。

**插曲（记录在案）**：中途一次 `nav-casual-mode` 抓图返回 `BLANK?`（亮度 0.1），复查 `sim_status` 发现 `isPlayMode=false`——PIE 已经退出，截的是空窗口，不是页面崩溃。判 BLANK 前先确认 PIE 还在跑。

## 100. 珍品阁详情弹窗：整套死 UI 接通 + 排版 + 真实目录数据（2026-10-30）

**缺陷**：`ShopRareItemsPage.BuildModal()` 造了遮罩、弹窗面板和 13 个子控件，但——
1. `_modalOverlay.Visible` **全仓没有一处设为 true**，弹窗永远打不开（死 UI）；
2. 即使打开也看不出东西：13 个子控件（关闭按钮、图标、标题、品质角标、描述、价格、取消/购买）**全部没有 `Location`**，会叠在面板 (0,0)；
3. 文案全是设计稿硬编码（青锋剑·绝世 / 6480 元宝 / 上古名剑… / 剑气纵横 15% 概率 200% 伤害），与选中的珍品无关；
4. 两个 `StretchAll` 子标签（图标字、品质角标文字）实测尺寸停在 **180×100 / 172×42**（父容器只有 80×80 / 72×22），居中后整个落到父容器外被裁——与珍品卡片同款"晚设尺寸"陷阱，卡片侧靠 `LayoutCardChildren → FitOnlyChild` 兜住了，弹窗侧没有。

**修法**：
- `InkPanel` 没有 `Clicked`，新增 `ClickableCard : InkPanel`（`OnMouseDown` 左键触发事件），珍品卡改用它，点卡片 → `OpenDetail(index)`；
- `OpenDetail` 把目录里的图标字/名称/品质/描述/价格写进弹窗（`_itemNames/_itemDescs/_itemPrices/_itemCurrencies/_itemQualities/_itemIllustChars`），品质走 `QualityNameOf` + 品质色；遮罩与面板同时 `Visible = true`；
- 关闭：右上 ×、取消按钮、点遮罩空白（新增 `ModalOverlayHit`，弹窗本体盖在它上面吞点击）；
- 「购买」只弹「珍品阁购买尚未接入服务端」（没有下单/扣款链路）；
- 删掉设计稿硬编码的「属性详情 / 特殊效果」两段，换成一句「属性与特殊效果数据尚未接入服务端」（目录里确实没有这些字段）；
- 弹窗子控件按 500×600 面板显式定位；对图标面板与品质角标补 `FitOnlyChild`。

**取证**（环境没有鼠标注入，按仓库既有 `friends-selftest.on` 的做法加了 `rare-detail.on` 标记夹具，跑完已删除标记文件）：
- 离线 `csc` 0 error；编辑器构建 success（DLL 19:03:58）；
- 回执：`rare-detail open=1 overlay=True panel=True title=青锋剑·绝世 price=6480 元宝 desc=上古名剑，锋利无比`；
- `ui_tree` 实测弹窗面板 (710,240,500,600)，15 个子控件坐标与尺寸全部正常（关闭 444,16 / 图标 24,24,80,80 / 标题 120,28 / 角标 120,64,72,22 / 描述 24,150 / 价格 24,492 / 取消 256,540 / 购买 372,540）；
- 截图：点开第 1 件珍品后弹窗居中显示「青」图标 + 标题 + 传世角标 + 描述 + 「属性与特殊效果数据尚未接入服务端」+ 6480 元宝 + 取消/购买，背景页面被遮罩压暗。

**边界**：卡片点击与遮罩点击这两个鼠标通路本环境无法注入，验证走的是"标记夹具直接调 `OpenDetail`"；点击事件本身只有代码路径保证。

## 101. 个人信息页：左卡片标题压边、两条分隔线变"符号"、统计宫格六卡全叠一起（2026-10-30）

**左卡片（头像/基本信息/已获称号）**
- `divider1 / basicTitle / divider2 / titleTitle` 四个控件是局部变量、从未定位 ⇒ 全部停在面板 (0,0)：截图里「基本信息」压在卡片上边框上，卡片左上角还多出两条短横线（两条 `InkDivider`）。
- 信息行的值列用固定 `leftWidth - padding - 80` 宽 100，右缘比卡片宽 20px，「无门派」被裁。
- 已获称号的 `_titlePanel` 也没定位（停在 0,0 的 100×30），称号格根本没出现。
- 修法：四个控件提升为字段，`ApplyLayout` 里按 头像(32,32,80×80) → 分隔线(32,128) → 基本信息(32,138) → 四行(168 起、步长 28) → 分隔线(+8) → 已获称号(+18) → 称号格(+48, 540×108) 顺序排；值列改成 `leftWidth - padding - 100` 宽 100；称号格的父容器是 `_titlePanel`，标签坐标改用面板内相对坐标（原先按左卡片坐标写，会偏出去）。

**头像圈里的「无」字被裁成「= / -」**：`charAvatarLabel` 用 `StretchAll` 放进 80×80 的圈里，实测仍是默认 180×100，居中后字的一半落在圈外，只剩两笔横线 —— 这正是本仓库反复出现的"StretchAll 不跟父尺寸"陷阱（珍品卡片/弹窗图标同款）。这里直接给死 80×80。

**右栏统计宫格**
- `_statsGrid` 的 `Location` 写在循环里、每轮都被改，最后停在最后一行的 y；六张卡片本身**从未定位**（全叠在网格 (0,0) 的 100×30 上）；三个标签的父级其实是卡片，却按"网格坐标"赋值 ⇒ 第二行起全叠在第一行上，画面上只剩几条横线。
- 修法：卡片存进 `_statCards[]`，按 `col=i%3 / row=i/3` 铺（间距 `padding*0.5`、行距 112），标签改用卡片内相对坐标（类别 16,16 / 数值 16,40 / 单位右对齐 52）。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 19:11:35）；PIE `nav-personal-info` 截图 + `ui_tree` 实测：左卡片分隔线 32,128 与 32,288、标题 32,138/32,298、四行 168/196/224/252、值列 464..564（卡宽 564，不再溢出）、称号格 32,328,540×108；统计卡第一行 y=0、第二行 y=112，卡内标签 16/40/52。

**边界**：右栏最右一列与成就条数值仍在本机窗口的横向裁切之外（外壳 letterbox），按 `ui_tree` 设计坐标确认在画布内。

## 102. 时辰页：日期/时辰/天气三个标签压在标题上、表盘别称被裁、时辰影响长句冲出面板（2026-10-30）

1. **三个小标签未定位**：`dateLabelTag / shichenLabelTag / weatherLabelTag`（「日期 / 时辰 / 天气」）是局部变量，从没定位 ⇒ 叠在「当前时辰」面板 (0,0) 压着标题。现提升为字段，在 `ApplyLayout` 里按三格列宽排：数值 y=56、标签 y=84，宽度都用 `gridWidth`（原先数值也没宽，居中基准是默认 100）。
2. **表盘两侧别称被切**：12 个时辰别称画在半径 255 处（左右极点 x≈485、宽 40），越过 500 宽的表盘面板；`InkPanel` 默认 `ClipChildren = true`，于是「卯·宵中」「酉·吃饭」只剩半截。表盘面板本来就是透明容器，直接 `ClipChildren = false`（实测底部「午·集市」随之完整出现）。
3. **时辰影响的长句冲出面板**：三条说明标签没开自动换行，整句直接画到面板外。补 `Wrapping = TextWrapping.WrapWords`。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-time` 截图：当前时辰面板下「日期 / 时辰 / 天气」三个标签就位，表盘别称完整（夜行/熬夜/花灯/晨钟/宵中/早课/集市），说明文字换行在面板内；`ui_tree` 复核三格标签与数值同列。

**回归**：本轮改动过的 8 个页面连跑 `ui_sweep` 全部渲染（设置 21.1/1975、音效 14.7/1683、奇珍阁 20.4/2121、祈愿 11.6/1865、组队 17.0/923、闲趣 21.6/1980、个人信息 13.1/1277、好友 17.6/797，颜色数各异）；祈愿页仍被判 `BLANK?` 是亮度阈值对深色页面误报（颜色数 1865，截图为正常卡池界面）。

## 103. 好友页「装备简览」标题被占位文字压住（2026-10-30）

**缺陷**：`_equipBox` 里两行文字（标题「装备简览」与占位「装备未公开」）都是用 `NewL(...)` 造的，**都没给 `Location`**，于是两行都停在面板 (0,0) 叠成一坨——截图里「装备简览」被另一行的字盖住，左侧还露出半个字形。

**修法**：两行显式定位（标题 12,0 宽 120；占位 12,30 宽 200），并注明原因。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-friends` 截图标题恢复可读；`ui_tree` 实测装备简览面板 (0,354,1360,88) 下两行 label 分别为 (12,0,120,22) 与 (12,30,200,18)，不再重叠。

**顺带产出（工具，不是产品改动）**：写了一个 `.compile-temp/sibling_overlap.py`，把 `ui_tree` 的运行时树按"同一父节点下的兄弟叶子控件"两两算重叠面积，用来批量找"叠在一起"的控件。首轮跑 4 份树样本：好友页抓到上面这处；奇珍阁那份是修复前的旧树（大量 0,0 堆叠，修复后已消失）；也有假阳性——两个 100 宽左对齐 Label 相距 50px 会被算成 41% 重叠，实际文字并不相撞。**结论：它只能当初筛，判定还要看文字对齐方式与截图。**

## 104. 司南页：朝向/坐标/区域/附近方位全是写死的样例（2026-10-30）

**缺陷**：底部信息面板三格是常量（`北偏东 15°` / `X:1234 Y:5678 Z:100` / `清河 · 开封城郊`），「附近方位」三行也是写死的地标与距离（开封城门 北 200m / 药师张老 东南 50m / 山贼营地 西 500m），计数写死「3 处」。玩家会以为这些是自己的实时位置与周边地标。

**真实数据其实就在同一个文件里**：罗盘表盘早已通过 `PluginNavigationBridge.TryGetMarkers` / `TryGetPlayerPose` 取到 POI 与玩家位姿，并在 `CompassDial` 里按"世界正北 = -Z、正东 = +X"算方位角画点。底部面板却没复用它。

**修法**：
- 坐标：`TryGetPlayerPose` 的玩家世界坐标，格式 `X:{x} Y:{y} Z:{z}`；取不到显示 `—`。
- 附近方位：`TryGetMarkers` 取全部 POI，按平面距离排序取前 3 行（字形 ◆=可快速旅行 / ●=已发现 / ○=未发现，名称取 Title 缺省 Tag，距离单位按 Flax 米），计数显示真实总数（实测 `10 处`）；方位词用与表盘一致的 `atan2(dx, -dz)` 八向分档。
- 朝向：需要玩家 yaw 的权威语义（工程里没有约定，`TryGetPlayerPose` 的 yaw 在别处只用于小地图旋转），不猜，保持 `—`；区域没有按点查名的数据，同样 `—`。
- 取不到导航数据时三行留空、计数 `—`，并显示一行「附近方位数据尚未接入服务端」，不摆虚构地标。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 19:32:38）；PIE `nav-compass` 截图：坐标 `X:-623 Y:0 Z:336`、三行真实 POI（落雁峰 西南 100m / 开封城 东北 670m / 青云山 西南 807m）、计数 `10 处`、朝向与区域为 `—`；`ui_tree` 实测三行 label 位于 y=92/126/160，占位提示 `Visible=false`（有数据时自动隐藏）。

**顺带**：`BLANK?` 判定对这张深色页仍是误报（亮度 12.7 但颜色数 2020，截图为正常罗盘）。

## 105. HUD「成就」入口点开的是"假成就解锁"庆祝页 + 设置页铜钱初值仍是 12,450（2026-10-30）

**入口指错页**：战斗 HUD 底部导航「成就」原先跳到 `reward-achievement`，那是设计稿的"成就解锁"庆祝页——里面写死「成就解锁 / 初心者称号 / 已获得 / 已解锁传送功能 · 前往查看 / 初入江湖」等文案。玩家点「成就」看到的是一枚自己并没有获得的成就。现改跳成就列表页 `nav-achievement`（实测该页总完成度 0、成就点 0、12 张卡片全部「尚未解锁」，与"没有成就服务"一致）。`RewardAchievementPage` 本身保留（将来由真实解锁事件驱动），但不再从导航进入。

**铜钱初值**：`SettingsPage._coinValueLabel` 初值仍是设计稿 `12,450`。binder 只在 `ShopClientService.Revision > 0`（服务端给过快照）时才覆盖，没开过商店就一直显示 12,450。改为 `—`。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-settings`（亮度 21.1 / 颜色 1983）与 `nav-achievement`（25.4 / 1295）均正常渲染；成就页截图为列表形态而非庆祝弹窗。

**说明**：`RewardPages.cs` 其余四个庆祝页（任务完成/恭喜获得/地图解锁/传送点解锁）与 `RewardLevelUpPage` 仍带设计稿写死的奖励与属性变化，但它们没有任何导航或事件入口（只能靠 dom-id 标记打开），本轮不动，已在 §94 的"待接服务端"清单里。

## 106. 飞鸽传书：收件箱为空时右侧三分之二全空白（2026-10-30）

**缺陷**：没有邮件数据时，详情面板与附件面板都 `Visible = false`（这一步本身是对的——不能显示设计稿的"论剑豪礼"正文），但页面右侧 920+528 的设计宽度就完全空白，整页看起来像"加载失败/坏了一半"，只有左侧列表里一行小字「暂无邮件 · 接入邮件服务后显示」。

**修法**：空态时在内容区（列表右侧整片区域）居中加一句「收件箱为空 · 邮件服务尚未接入服务端」，位置在 `RefreshLayout` 里按 `panelX + leftW + RegionGap` 与内容区高度算；有邮件时不创建该标签（仍在原来的空分支里构造）。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-social-mail` 截图：左侧列表保持空态提示，右侧居中显示新的空态文案；`ui_tree` 复核详情/附件面板仍为 `visible=false`（没有数据时不显示假信纸内容）。

**判据备注**：该页亮度 9.7 仍触发 `BLANK?` 误报（颜色数 892、截图为正常两栏空态页）——深色页面用亮度阈值判空白不可靠，需结合颜色数与截图。

## 107. 秘境入口：「进入秘境」静默回 HUD + 邀请按钮点了没反应（2026-10-30）

**缺陷**：
1. 「进入秘境」按钮的处理器是 `NavigationRequested?.Invoke(InkPageDomIds.EnterDungeon)`，而 `MainUIManager` 把 `EnterDungeon` 与 `BackHud` 归到同一分支——直接 `NavigateToHud()`。玩家按下"进入秘境"只会退回战斗 HUD，**没有任何提示**，看起来像"进本了但界面没了"。
2. 右侧「队伍配置」的邀请按钮只做了金色粒子点击反馈（`EmitGoldAtControl`），没有任何后续动作。
3. 页面卡片上的推荐战力/可能掉落/今日次数全是设计稿目录数据，页面没有任何"样例/未接入"标注。

**修法**：进入秘境与邀请都改成明确提示（「秘境副本尚未接入服务端，暂不可进入」/「组队邀请尚未接入服务端」），不再假装跳转；「进入后将消耗今日次数」那行改成本义提示「秘境目录为界面样例，副本服务端接入前方可进入」。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 19:42:31）；PIE `nav-dungeon-entry` 截图正常（26.6 亮度 / 3542 颜色）；另用二进制扫描确认新装配里三条新文案各出现 1 次、旧文案「进入后将消耗今日次数」为 0 —— 证明确实换掉了而不是只改了源码。

**边界**：按钮点击本身无法鼠标注入，"点了弹提示"只有代码路径与文案在装配中的存在性作证。

## 108. 两处"点了只有金色粒子"的列表项：秘境分类 / 帮会成员（2026-10-30）

**缺陷**：`DungeonEntryPage` 左侧「秘境分类」条目和 `SocialGuildPage` 的成员条目，点击处理器都是 `EmitGoldAtControl(x)` —— 只播放金色粒子，没有任何状态变化。两个列表本身都画了"选中态"（左侧金条 / 金色左边线），于是玩家点了看起来像坏了。

**修法**：两个条目的选中态从 `readonly` 改成可写，列表各自持有一份条目集合；点击时先复位其它条目再点亮自己（互斥选中）。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 19:45:58）；PIE 两页均正常渲染（秘境 26.6/3537、帮会 24.3/1172，改构造/字段没有引入崩溃）。

**边界**：本环境没有鼠标注入，"点一下会高亮"只能靠代码路径保证，截图证不到；这条与 §72 的点击级自动化缺口同源。

## 109. 全量页面回归（40 页）+ 两条判据教训（2026-10-30）

本轮改动集中在设置/祈愿/组队/闲趣/个人信息/时辰/奇珍阁/好友/司南/秘境/邮件，收尾跑一次全量抓图确认没有连带破坏（PIE 全程 `isPlayMode=true`）：

| 页面 | 亮度 / 颜色数 | 结论 | | 页面 | 亮度 / 颜色数 | 结论 |
| --- | --- | --- | --- | --- | --- | --- |
| nav-achievement | 25.4 / 1295 | ok | | nav-martial-record | 27.0 / 2245 | ok |
| nav-activities | 22.9 / 3429 | ok | | nav-mentor | 24.2 / 1591 | ok |
| nav-appearance | 26.8 / 3564 | ok | | nav-mount-pet | 21.1 / 3029 | ok |
| nav-battle-pass | 27.6 / 3302 | ok | | nav-multiplayer | 24.1 / 1183 | ok |
| nav-battle-prep | 23.4 / 1951 | ok | | nav-personal-info | 13.0 / 1283 | ok |
| nav-bestiary | 19.2 / 1220 | ok | | nav-photo-album | 51.2 / 14390 | ok |
| nav-casual-mode | 21.5 / 1981 | ok | | nav-photo-mode | 151.4 / 8718 | ok |
| nav-character-panel | 29.1 / 6289 | ok | | nav-quest-log | 21.6 / 1312 | ok |
| nav-compass | 10.2 / 1929 | 深色页误报 | | nav-sect | 30.0 / 1319 | ok |
| nav-crafting | 21.1 / 2510 | ok | | nav-settings | 21.1 / 1982 | ok |
| nav-dungeon-entry | 26.6 / 3538 | ok | | nav-settings-audio | 14.6 / 1706 | ok |
| nav-equipment-enhance | 25.0 / 1989 | ok | | nav-shop | 21.3 / 2553 | ok |
| nav-equipment | 21.6 / 1315 | ok | | nav-shop-rare | 20.7 / 2155 | ok（复抓） |
| nav-friends | 17.6 / 803 | ok | | nav-skill-tree | 19.3 / 1182 | ok |
| nav-gacha | 11.5 / 1929 | 深色页误报 | | nav-social-guild | 24.3 / 1181 | ok |
| nav-inventory | 29.1 / 6305 | ok | | nav-social-mail | 9.7 / 934 | 深色空态页误报 |
| nav-leaderboard | 25.6 / 1181 | ok | | nav-team | 17.0 / 926 | ok |
| nav-livelihood | 21.0 / 1703 | ok | | nav-time | 28.3 / 1405 | ok |
| death-screen | 11.3 / 820 | 深色模态页误报 | | nav-world-map | 12.6 / 914 | ok |

**教训一（抓图会抖）**：`nav-shop-rare` 第一次抓到 7.6 / 744（疑似弹窗遮罩态），紧接着复抓是 20.7 / 2155。判 BLANK/异常前先复抓一次；`ui_sweep` 的亮度阈值对深色页本来就偏保守。

**教训二（扫描器会误报死链）**：写了个"用到的 `InkPageDomIds.X` 是否都有 `RegisterPage`"的静态检查，报出 6 条"死链"，其中 `action-friends-to-mail` / `action-mentor-to-dungeon` 等其实是**前缀路由**——`MainUIManager` 里 `domId.StartsWith("action-")` 统一交给 `InkPageRouter.NavigateToAction()` 解析，不走逐个 `==` 比较。判断"死链"必须先把前缀/别名类路由规则读全，否则会把正常通路判成缺陷。

## 110. 坐骑灵兽页：名录/培养/技能/幻化全是目录数据却没有标注（2026-10-30）

**现状核查**：这一页的"诚实"部分其实做得不错——出战状态固定「未出战」、休息按钮 `Visible=false`、喂养/升级按钮 `Enabled=false` 且各自带注释「没有服务端通道」，页面也写了「坐骑出战功能尚未开放」。**但头部没有任何"目录样例"标注**：名录 06 只、Lv.0、速 329/280/265…、驯养材料、技能研习列表、幻化外观全是设计稿数据，玩家无法分辨。

**修法**：在顶栏标题右侧加一句「名录为界面样例 · 坐骑系统尚未接入服务端」（11px、`TextTertiary`、右对齐），宽度按 `MainPanelSize.X - 76 - noteX` 算，与右上返回按钮留 20px 间距。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-mount-pet` 截图正常（21.0 / 2949）；`ui_tree` 实测该标签 (812,0,512,56)，右缘 1324、返回按钮 1344..1380，不重叠。

**口径**：本轮同时扫了全仓 UI 字符串里的"缺字形"（⚡⚔⛰★◈☰♛✦✓⚠▸◷✕ℹ▤✉），只剩 `ConfirmDialog` 里两条 `Debug.Log` 用到 ★（不渲染到界面），界面文本已清零。

## 111. §96 读盘修复的端到端复验：改盘上值 → 重启 PIE → 页面跟着变（2026-10-30）

§96 的证据只有一次"环境音 79"（那也可能是历史巧合）。这轮做了 A/B：

1. 备份 `%APPDATA%/HundunWorld/settings.json`，把 `MasterVolume` 从 `1` 改成 `0.33`；
2. 重启 PIE（`sim_stop` → `sim_play`，等世界就绪）后进 `nav-settings-audio`；
3. 截图放大：主音量滑块停在 **约 1/3** 处（其余 背景音乐 70 / 音效 80 / 语音 90 / 环境音 79 不变）——与盘上值一致；
4. 停止 PIE 后从备份还原 `settings.json`（复核 `MasterVolume=1`），备份文件删除。

**结论**：`GameStateManager.Instance` 首次访问时的 `Initialize()`（LoadSettings + ApplySettings）确实在每次启动把盘上设置读回来并下发，不是一次性巧合。

**说明**：本环境无法直接在滑条上拖动（没有鼠标注入），所以"改完是否落盘"这一半仍只有代码路径（`UpdateSettings` → 脏标记 → `HundunWorldGame.OnUpdate` 每 5 秒 `SaveSettings`）作证；「读盘 → 上屏」这一半现在有 A/B 截图。

## 112. 设置页：几项"看着能切、其实不生效"的画质开关缺说明（2026-10-30）

**核查**（代码 + 早前实测）：
- `Graphics.UseVSync` 运行时写入有效；
- `Graphics.AAQuality / ShadowsQuality / SSRQuality / VolumetricFogQuality` 写入后读回不变（只在渲染器初始化时消费）→ 抗锯齿、阴影质量、特效质量三行改了不会立刻生效；
- 「画质省电模式」`PowerSave` 除了被 `SetPowerSaveAsync` 存下来，**全仓没有任何消费者**；
- `ApplyGraphicsSettings()` 只被设置页自己调用，启动流程里没有。

也就是说这四行是"只存不改画面"的开关，但行描述写的是"降低画质以节省电量""使画面边缘更平滑"这类效果承诺。

**修法**：
1. 省电模式行描述改为「记录偏好：当前引擎无法运行时降档，此项仅保存」；
2. 「画面信息」面板加一行脚注：「※ 抗锯齿 / 阴影质量 / 特效质量 / 省电模式在当前引擎版本运行时不生效，仅保存设置」，面板高度 84 → 108 容纳脚注。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 20:03:20）；PIE `nav-settings` 截图正常（21.2 / 1985）；`ui_tree` 实测信息面板 (24,874,1432,108)，脚注 label 位于 (24,72,1384,18)，三列信息不变。

**口径**：不删这些开关（保存的偏好对未来引擎版本仍有意义），但页面不再承诺立即生效——与「帧率上限」那条"引擎暂无接口，此项只记录"保持同一写法。

## 113. 世界地图右栏：假坐标 / 假名望 / 假任务数 / 假区域简介（2026-10-30）

**缺陷**：`WorldMapPage.BuildInfo()` 里「当前区域 清河」「◎ 坐标 X:1234 Y:5678」「◆ 名望等级」「◆ 众生任务 3 个可接」以及一整段「清河渡口，水陆交汇之地…」都是设计稿常量。地图本身已经在用 `PluginNavigationBridge` 的真 POI（截图里的凉州/开封城/落雁峰/青云山/清河村/江南渡口来自服务端数据，不是演示回退），但右栏信息是编的。

**修法**：
- 坐标改为玩家真实世界坐标，0.5 秒刷新（平面轴是 **X/Z**，纵向 Y 不上地图，标签如实写 X/Z）；
- 区域名、名望等级、众生任务数没有数据源 → 一律 `—`；
- 区域简介改为「区域简介尚未接入服务端」。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 20:06:30）；PIE `nav-world-map` 截图正常（12.6 / 915）；二进制扫描：新文案「区域简介尚未接入服务端」1 次、动态坐标格式 `◎ 坐标    X:` 1 次，旧文案「X:1234  Y:5678」「众生任务    3 个可接」均为 0 次 —— 确证换掉了而不只是改了源码。

**边界**：右栏在 1920 设计画布右侧，本机窗口横向裁切看不到，截图只能证明页面整体正常；坐标是否随移动刷新未做动态验证（需要移动角色，属点击/输入注入缺口）。

## 114. 司南页「追踪目标」写死山贼营地（2026-10-30）

**缺陷**：底部「◎ 追踪目标」旁边是一个自绘下拉控件 `TrackSelect`，`Draw()` 里直接把「山贼营地」当常量画出来。玩家会以为当前正在追踪某个营地。而同一页的自动移动/目的地链路（`PluginNavigationBridge.TryGetMoveDestination`）本来就是通的。

**修法**：`TrackSelect` 增加可写 `Text`，默认「未设定」；在已有的 0.5 秒刷新里：有自动移动目的地时，取离目的地最近的 POI 名（没有名字退回 Tag，一个都对不上则「自定义位置」），没有目的地就显示「未设定」。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 20:08:45）；PIE `nav-compass` 截图正常（深色页亮度 10.2、颜色数 1911）；二进制扫描：「山贼营地」**0 次**，「未设定」「自定义位置」各 1 次 —— 写死文案确实已从装配移除。

**边界**：追踪目标随目的地变化的动态效果需要先在地图上设目的地（点击注入缺口），本轮只验证了默认态与代码路径。

## 115. 正常启动回归：不带自测标记时的登录页（2026-10-30）

**为什么做**：本机一直靠 `auto-login.on` / `enter-world.on` / `open-page.txt` 三个标记免点进世界，今天的改动（`GameStateManager` 懒初始化、设置页、多页布局）从没在"普通启动"路径上验过。

**做法**：临时把三个标记改名成 `*.verify-bak`，`sim_play` 后截主窗口，再原样改回（已复核三个文件都回到原位）。

**结果**：登录页正常——水墨背景、`混沌世界 / 一花一世界 · 一叶一菩提`、右侧「欢迎归来」卡片（预填账号 101125808、密码掩码、记住密码已勾、自动登录未勾、进入江湖按钮）、左下「服务器：烽火连城 · 流畅」、右下 `v1.0.0`。没有空面板、没有成串异常；「自动登录」保持未勾，与"只预填账号、走真实校验"的口径一致。

**说明**：本环境无鼠标注入，登录→选角→进世界这一段仍只能靠标记夹具走；本轮只证明"普通启动时登录页渲染正常且没有回归"。

## 116. 结伴江湖「联系人名片」写死一个不存在的玩家（2026-10-30）

**缺陷**：`SocialGuildPage.BuildPlayerCard()` 整张卡是常量——头像「张」、`剑客张三`、`Lv.60`、`武当派`、`战力 12,345`、`● 在线`、`点 武当山`。四个操作按钮确实已 `Enabled=false`（这点之前是对的），但名片本身摆着一个根本不存在的玩家，截图里看就像"好友列表里有个叫剑客张三的人"。好友页详情栏的初值同样是 `剑客张三 / 武当山 · 紫霄宫 / 在线 / Lv60 / 武当派 / 渡劫期 / 正道`（运行期会被 `ShowEmptyDetail()` 覆盖，但构造到首帧之间仍是假数据）。

**修法**：两处初值全部中性化——结伴江湖名片改为「未选择联系人」+ 头像 `—` + 等级/门派角标 `—` + 战力/在线/位置 `—`；好友页详情栏的姓名/称号/位置/状态与四格信息初值改 `—`（真实值仍由选中好友的绑定写入）。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 20:13:18）；PIE 截图：结伴江湖右栏名片显示「未选择联系人」，等级/门派/战力/在线/位置均为 `—`，四个按钮仍是禁用态；好友页 `17.6 / 806` 正常。

**边界**：名片随联系人选择刷新的动态效果需要真实好友数据（服务端空壳），本轮只验证空态。

## 117. 门派列表徽章是空白方块 + 编辑器中途退出后的重启方式（2026-10-30）

**缺陷**：门派列表每一行左侧有 32×32 的徽章框，里面的字（道/少/武/峨/华/丐/唐/全）一个都看不见——`emblemLabel` 用 `AnchorPresets.StretchAll` 放进固定 32×32 的 `emblemBg`，实测标签仍停在默认 180×100，居中后整个落到框外。这是本仓库反复出现的同一类陷阱（珍品卡片、弹窗图标、头像圈都是它）。

**修法**：`emblemLabel` 显式 `Location=Zero; Size=32×32`。截图确认八个门派徽章字全部出现。

**插曲：编辑器中途退出**。验证到一半发现 MCP 桥 `ConnectionRefused`、`tasklist` 里没有 `FlaxEditor.exe`（编辑器在 20:18 前后自己退出了）。重启时**不能裸启动**——`Start-Process FlaxEditor.exe` 只会弹「Select project to open in Editor」文件对话框、桥不恢复；带上项目参数即可：

```powershell
Start-Process -FilePath 'C:\Program Files (x86)\Flax\Flax_1.13\Binaries\Editor\Win64\Development\FlaxEditor.exe' `
  -ArgumentList '-project','C:\Works\GitHubProjects\HundunWorld\HundunWorld\HundunWorld.flaxproj'
```

重启后 `code_build_wait` 返回 success、`sim_play` 正常、页面走查恢复。（此前记忆里"传 .flaxproj 只会弹选项目对话框"应指路径/引号写错的情况，本次实测带 `-project <绝对路径>` 可直达工程。）

**验证**：离线 `csc` 0 error；重启后编辑器构建 success；PIE `nav-sect` 截图 30.0 / 1386，八个徽章字齐全。

## 118. 批量清剿"StretchAll 撑出图标框"：从逐页找改成运行时扫描（2026-10-30）

同一类缺陷今天已经修过五处（珍品卡片、弹窗图标、头像圈、门派徽章），说明靠肉眼截图找不可靠。这轮写了个运行时检查 `.compile-temp/child_overflow.py`：逐页 `open-page.txt` 导航 → `ui_tree` → 找**任何叶子控件的 bounds 大于其父容器**（这正是"32×32 的框里塞着 132×52 的字"的特征），父容器路径一并打印便于回代码定位。

**机械结论**：溢出标签的尺寸几乎恒等于「父尺寸 + 100」（32→132、36→136、40→140、48→148、120→220），即 `StretchAll` 在这里既不跟随父尺寸、也不停在默认值，而是按父尺寸+100 撑开，居中后文字整体落到框外。

**本轮修掉（全部改为 `AnchorPresets.TopLeft` + 显式 Size，或让容器在 `OnSizeChanged` 里把子控件钉回父尺寸）**：

| 页面 | 控件 | 溢出 → 修复 |
| --- | --- | --- |
| 门派 `nav-sect` | 顶栏头像「无」、右侧 80×80 门派徽章「逍」、列表徽章 | 136×56 / 180×100 → 36×36 / 80×80 / 32×32 |
| 外观 `nav-appearance` | 顶栏头像「无」、套装缩略图「衣」 | 136×56 / 148×68 → 36×36 / 48×48 |
| 营生 `nav-livelihood` | 头像「侠」、技能/配方图标字、详情大图标、工具图标 | 5 类共 15 处 → 分别钉成 36/32/40/24/32 |
| 商店 `nav-shop` | `CircleAvatar`、`QualityTint`（9 张商品卡） | 组件级：两个类补 `OnSizeChanged` 同步子控件 |
| 任务 `nav-quest-log` | `CircleAvatar` | 同上（共享组件，一处修两页） |
| 多人 `nav-multiplayer` | 「江湖」印章、头像「◎」、「推荐」角标、房间模式角标 | 32×20 / 36×36 / 48×20 / 50×22 |
| 奇珍阁 `nav-shop-rare` | 顶栏头像「客」、三个页签文字、筛选胶囊 | 36×36 / 120×44 |
| 时辰 `nav-time` | 顶栏头像「无」 | 40×40 |

**判为良性（不是缺陷）**：队伍页最低等级滑条的 16×16 滑块塞在 4px 高的轨道里（滑块本来就该比轨道大）；相册页 100×30 面板里 120×36 的按钮（按钮略大于底框，视觉正常）；战斗备战的 `InkPanelTitle` 100×44 标题条里 200 宽左对齐文字（文字短，不越界）。

**验证**：全部改动离线 `csc` 0 error、编辑器构建 success；改完后对 20+ 页面跑扫描，上述页面 `overflow=0`；截图抽查 门派（徽章字齐全）、多人、奇珍阁正常。

## 119. 溢出批检第二轮：闲趣卡 / 音效页头像 / 拍照 HUD 标签（2026-10-30）

用 `child_overflow.py` 把剩下的页面扫完，又抓到三处同族问题并修掉：

- **闲趣 `nav-casual-mode`（18 处）**：活动卡的插画块 64×64 里塞着 164×84 的插画字（`_illustChar`），5 张卡 ×1；每卡的 3 个奖励图标块 28×28 里塞着 128×48 的字（`RewardIconBox`）。两处都改成显式尺寸；`RewardIconBox` 因为尺寸由布局期给，补了 `OnSizeChanged` 把字形标签钉回父尺寸。
- **音效设置 `nav-settings-audio`（1 处）**：顶栏头像圈 36×36 里 136×56 的「客」字。
- **拍照 `nav-photo-mode`（1 处，判为良性）**：`PhotoHudTag` 84×22 里 100×22 的标签——左对齐、文字远短于 84，实际不越界。

**验证**：离线 `csc` 0 error；编辑器构建 success；改后重扫 `nav-casual-mode / nav-settings-audio / nav-quests` 均为 `overflow=0`，仅剩拍照那处良性项。

**当前溢出台账**：已扫描 24 个页面；真实缺陷 8 页全部清零，良性例外记为 4 类（滑条滑块、按钮略大于底框、标题条长容器、HUD 小标签）。

## 120. 世界地图的"演示标记回退"不告诉玩家（2026-10-30）

**问题**：`MapCanvas.BuildMarkerData()` 在 `PluginNavigationBridge.TryGetMarkers` 失败时会静默回退到内置的 18 个演示点 + 3 个演示地名（清河 / 落雁峰 / 北山道）。正常情况下看不到（本机导航可用、地图上是真 POI：凉州/开封城/青云山…），但一旦插件导航不可用，玩家看到的就是一堆凭空出现的点位与地名，且没有任何提示。

**修法**：回退分支置 `_usingDemoMarkers = true`（取到真数据时清位），`Draw()` 里在该状态下于地图左下角画一行「导航数据不可用 · 当前为示意图标」。真数据下不出现。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 20:43:01）；二进制扫描确认新文案已进装配（1 次）；PIE `nav-world-map` 截图正常（12.5 / 917，与改动前同量级，说明真数据路径下提示没有误显示）。

**边界**：本机导航可用，无法制造"回退态"截图；该分支的可见性只有代码路径 + 文案在装配中的存在性作证。

## 121. 司南表盘也在静默回退演示点（2026-10-30）

与 §120 同源：`CompassDial.DrawPois()` 在 `_navPois == null`（导航不可用）时画三个内置演示点（金菱/金圆/带脉冲的玉菱），画面上与真实 POI 完全同形，玩家无从分辨。补一句盘下的「导航数据不可用 · 方位为示意」，只在回退分支绘制。

**验证**：离线 `csc` 0 error；编辑器构建 success；二进制扫描确认新文案进装配（1 次）；PIE `nav-compass` 仍在 10.2 量级（真数据路径下不显示提示）。

**边界**：本机导航可用，回退态截图造不出来，同 §120。

## 122. UI 系统现状台账（2026-10-30 收束，供交接/后续排期）

**数据接线三类（判断一个页面"是不是真的"先看它在哪一类）**

| 类别 | 判据 | 页面 |
| --- | --- | --- |
| 真数据 | 读客户端服务/快照，无数据时显示 `—` 或空态 | 背包/装备（`PlayerInventoryService`）、技能树（`SkillCatalogService` + `SkillDatabase`）、制造（`CraftClientService`）、商店余额（`ShopClientService.Gold`，`Revision>0` 才算）、音效设置（`GameStateManager` JSON，启动即读盘）、画面设置（`GameConfigurationService` LiteDB）、司南坐标与附近方位、世界地图 POI 与导航路径、战斗 HUD 血/气/等级/技能冷却 |
| 目录样例（有服务端缺口，但页面已明示） | 文案里带"样例/尚未接入" | 活动页、闲趣、珍品阁、坐骑灵兽、博物志、成就列表、排行榜、秘境入口、祈愿（概率与保底已改 `—`）、邮件（空态）、好友/结伴江湖（空态） |
| 服务端空壳，只能停用/提示 | 点了给明确提示或按钮禁用 | 装备强化数值、组队/帮会/邮件/排行/成就/活动服务、祈愿抽取、珍品购买、秘境进入、聊天发送、私聊/组队/交易（结伴江湖四按钮） |

**已建立的验证阶梯（改动后按序跑）**
1. 离线编译闸：`dotnet exec csc.dll @.compile-temp/verify-game/verify-check8.response`，判据 `error CS` 计数为 0；
2. 编辑器构建：`code_build_wait` → `code_build_status.lastCompilationFailed=false`，并用 DLL mtime 排除"假成功"（PIE 在跑时构建可能不生效）；
3. 装配替换证据：对 DLL 做 UTF-16 文本扫描，确认新文案进、旧文案出（本轮多次用它证明"不是只改了源码"）；
4. 运行时页面：`open-page.txt` 导航 + `ui_sweep.py` 抓图（注意 BLANK 对深色页误报，先查 `isPlayMode`、再复抓一次）；
5. 结构判据：`ui_tree` 看设计坐标（页面恒 1920×1080），`child_overflow.py` 查"子控件大于父容器"，`sibling_overlap.py` 只当初筛；
6. 端到端行为：能改磁盘状态的用 A/B（如 §111 改 `settings.json` 验证读盘）。

**已知验证盲区（结论里必须标注）**
- 无鼠标/键盘注入：所有"点一下会怎样"（弹窗、提示、选中态、滑条拖动落库）只有代码路径与装配文案作证；
- 深色页面亮度阈值不可靠；
- 本机导航可用，地图/司南的"回退态"分支无法截图；
- 真实服务端在线数据（好友、组队、拍卖、祈愿）不可造，相关页面只能验空态。

**收尾待办（交付前必须做）**
- 删除 `%APPDATA%/HundunWorld/` 下的自测标记：`auto-login.on`、`enter-world.on`、`open-page.txt`（连同 `*.verify-bak`）。留着会让玩家一启动就自动登录并跳到上次走查到的那一页。

## 123. 收束回归：39 页全量抓图（2026-10-30 晚）

本轮所有改动（溢出批修 8 页、假数据清理、演示回退提示、设置页脚注等）之后的最终回归，PIE 全程在线：

- 35 页 `ok`；4 页 `BLANK?` 全是已知深色页误报（司南 10.2 / 祈愿 11.5 / 邮件 9.6 / 阵亡 11.4，颜色数 833–1926，截图为正常界面）。
- 与 §109 那次回归相比，颜色数变化与改动方向一致：门派 1319→1431（八个徽章字画出来了）、奇珍阁 2121→2147、闲趣 1980→2217（卡片插画与奖励图标字出现）、设置 1982→2003（新增脚注）。
- 无崩溃、无 `ok=False`、无导航卡死；收尾时 `sim_stop` 正常退出 PIE。

**仍然空着的两条待办**（见 §122）：真实服务端链路的页面（好友/组队/拍卖/祈愿/秘境）只能验空态；`%APPDATA%/HundunWorld` 下的自测标记需要在交付前清掉。

## 124. 战斗 HUD 小地图上的"敌人/队友"是写死的样例点（2026-10-30）

**缺陷**：两套 HUD 在构造小地图时都手工塞了点位——传统式 `1 友方 + 1 敌人 + 2 NPC`，V2 式 `2 敌人 + 3 队友`，坐标是写死的相对值。`InkMinimap.RefreshFromNavigation()` 只有在导航数据可用时才会 `ClearEntities()` 重写；一旦导航不可用（或首帧刷新前），这些点就一直在图上，看起来像"附近真的有人/有怪"。

**修法**：删掉全部演示实体，只保留「自己」这一个真实点；其余由小地图每 0.5 秒从 `PluginNavigationBridge` 刷新写入。地形装饰块（`AddLandmark`）保留——那是纯视觉。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 20:57:07）；PIE 回传统 HUD 截图正常（168.7 / 14477，场景明亮）；玩家血/气/修为仍是真实值（400/400、1998/1998、0%）。

**边界**：小地图点位本身在截图里不易分辨，本轮以"写死点已从源码移除 + 页面无异常"作证；真实附近单位是否上小地图取决于导航数据，属服务端链路。

## 125. 改动集卫生检查：无 BOM、行尾 LF 是仓库级现状（2026-10-30）

收尾前对本轮改过的文件做了一次卫生检查，两条结论：

1. **没有 BOM**：本轮所有被 Python 重写过的文件首字节仍是 `usi`（`using`），没有出现"加 BOM 导致整文件 diff"的老问题。
2. **行尾 LF 是仓库级现状，不是本轮引入**：`git diff` 会对 **所有** UI 文件（包括我根本没碰过的 `Authentication/LoginPanel.cs`）提示 "LF will be replaced by CRLF"，实测该文件也是 0 个 CRLF、631 行全是 LF；`git config core.autocrlf` 为 `true`，提交时由 git 统一规范化。**因此不要为此批量改行尾**——那会产生几百个无关文件的噪音改动。
3. **工作区里有大量并行会话的改动**（`Horizon.*` 服务端、`CharacterPreview3D` 等），统计编辑范围不能从 `git diff` 反推，只能按自己本轮写过的文件清单核对。

**方法记录**：`git diff --numstat` 会因其它会话的改动而失真；判断"我有没有把某个文件整份改写"要直接看字节（BOM / CRLF 计数）而不是看 diff 行数。

## 126. V2 战斗 HUD：背景层把整个 3D 世界盖住了（2026-10-30）

**最严重的一处**：`CombatHudV2Page.BuildBackdrop()` 会加一层 `HudBackdrop` —— 它 `Draw()` 里先整屏铺 14 段深墨渐变（`BaseSecondary → BaseDefault`，全不透明），再叠水墨晕染、远景/近景墨山脊线。也就是说**切到 V2 HUD 后玩家根本看不见游戏世界**，只看见设计稿那张"假场景"。同页还画着居中标牌「游戏场景 · 墨麒麟巢穴」，进一步强化"这是某个场景"的错觉。

**修法**：整层删除（`BuildBackdrop` 调用、方法体、`HudBackdrop` 类、相关常量与布局定位一并移除）；`_backdrop` 层级不存在后，HUD 与外壳口径一致——只叠控件、不铺底。

**顺手**：HUD 直接浮在 3D 世界上，亮场景（本地是雪地）里左下玩家状态的名称/血条糊成一片，给 `_playerPanel` 加 0.45 透明度墨底（比菜单面板淡，不挡场景）。

**量化证据（同一页面、同一场景）**：改前 `lum=20.9 / colors=1306`（整屏深墨，看不见世界）；删背景层后 `114.3 / 17929`（雪地世界出现）；再加深色底板后 `177.9 / 12987`。放大截图确认「赵问剑 / Lv.1 / 剑客 / 400·400 / 1000·1000」在雪地上清晰可读；二进制扫描确认「墨麒麟巢穴」已从装配移除（0 次）。

## 127. 传统 HUD 玩家状态条在亮场景里不可读（2026-10-30）

V2 修完背景层后顺手回看传统 HUD：它的 `_charPanel` 是纯透明的，本地场景是雪地，"无门派"（浅青）与"血/气/修"三行小字几乎完全糊在白底上（放大截图可见只有名称和数值勉强能看）。

**修法**：给 `_charPanel` 加 0.45 透明度墨底（`BaseDefault`），与 V2 玩家状态条一致；菜单面板是 0.85~1.0，HUD 用 0.45，既保证可读又不至于挡场景。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE 回传统 HUD 截图 172.3 / 13701；放大截图确认「赵问剑 / 无门派 / 血 400·400 / 气 1000·1000 / 修 0%」在雪地上清晰可读。

## 128. 抓图判据修正：深色页面不该算"空白"（2026-10-30）

`ui_sweep.py` 原来的判据是 `blank = lum < 12 或 colors < 24`，对罗盘（10.2）、祈愿（11.5）、邮件（9.7）、阵亡（10.3）这类深色页面**长期误报 BLANK**，每次都要人工解释一遍。

**新判据**：`blank = colors < 200 或 (lum < 3 且 colors < 600)` —— 真正"空白"的特征是**颜色数极少**，而不是暗；`lum < 12` 但颜色数正常的页面标为 `ok_dark`（ASCII，避免 Windows 控制台 GBK 把 `·` 打成乱码）。

**证伪**（合成两张探针图，判据必须能分清）：
- 纯色深底（RGB 8,9,11）：`lum=8.9 / colors=1` → **blank=True**；
- 深底+彩色点阵：`lum=36.8 / colors=13192` → False。

**回归**：改判据后重扫同一批页面，`nav-compass 10.2/1931`、`nav-gacha 11.5/1960`、`nav-social-mail 9.7/896`、`death-screen 10.3/887` 全部变成 `ok_dark`，`nav-settings 21.2/1981` 仍是 `ok`。

## 129. 传统 HUD 顶部罗盘带在亮场景里不可读（2026-10-30）

**缺陷**：`CompassBar.Draw()` 的底色是 `new Color(0.68f, 0.72f, 0.42f, 0.30f)`——30% 透明度的青金浅色，本身是为了"低底凸显文字"。但在雪地/沙漠这类亮场景里，它和背景几乎同亮，浅色的方位字（`PaperAged` / `TextDefault`）完全糊掉，整条带子只剩一片淡橄榄色。

**修法**：底色改为 `BaseDefault` 的 55% 深墨（与 HUD 其它浮层同一口径）。透明度仍保留半透，不挡场景三分之二。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE 回传统 HUD 截图 171.4 / 13528；放大截图确认「西北 / 东北」与刻度线在雪地上清晰可读、带子有明确边界。

## 130. HUD 亮场景可读性收尾清单（2026-10-30）

按"同一场景（雪地）截图 + 局部放大"逐个看过两套 HUD 的浮层，结论与处置：

| 浮层 | 原状 | 处置 |
| --- | --- | --- |
| 传统 HUD 角色状态（头像/名称/血气血气/修为） | 纯透明，浅色小字糊掉 | 加 0.45 墨底（§127） |
| 传统 HUD 顶部罗盘带 | 0.3 青金浅底，方位字糊掉 | 改 0.55 深墨底（§129） |
| 传统 HUD 聊天框 | 本身深底 | 已可读，不动 |
| 传统 HUD 底部系统导航 / 动作栏 | 深色瓦片 | 已可读，不动 |
| V2 玩家状态条 | 纯透明 | 加 0.45 墨底（§126） |
| V2 整屏背景层 | 不透明深墨 + 墨山，**盖住 3D 世界** | 整层删除（§126） |

**判据**：同一场景前后 `lum/colors` + 局部放大截图；`colors` 大幅上升通常意味着"世界露出来了"，`lum` 上升则可能是场景本身变亮，不能单独作为"对比度变好"的证据。

## 131. 品牌串成"燕云十六声"、版本号两套并存（2026-10-30）

**缺陷**：页面上出现的是**另一款游戏的名字**——门派、组队两页左栏品牌是「燕云」+「十六声」两行；武林录页左栏品牌与页脚分别写着「燕云十六声」「燕云十六声 v1.0.0」。同时版本号两套并存：登录页 / 闲趣 / 加载页×2 / 商店 / 时辰 / 武林录 写 `v1.0.0`，设置页 / 音效页 / 奇珍阁 写 `v1.2.0`。

**修法**：
- 三页品牌统一为「混沌世界」，门派/组队的小字副标写「江湖公测」；
- 版本号统一到 `v1.2.0`（含加载页的 `VER 1.2.0 · CN`）；
- 品牌从 2 字变 4 字后，左栏 logo 宽度 52→100、副标起点 20+52+8 → 20+100+8，否则品牌被裁成两字、副标压在字上（第一次截图就是这样，改宽度后才正常）。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-sect / nav-team / nav-martial-record` 均正常（30.1/1451、17.0/951、27.1/2238）；放大截图确认「混沌世界 江湖公测」完整不重叠；二进制扫描：「燕云十六声」0 次、「十六声」0 次、`v1.0.0` 0 次、「混沌世界」12 次。

## 132. V2 小地图：写死地名「昆仑墟·深渊」+ 圆盘在雪地上几乎看不见（2026-10-30）

**两处**：
1. 小地图下方那行原本是前缀「向」+ 设计稿地名「昆仑墟 · 深渊」，看着像当前所在区域，且从未被任何数据覆盖（`_minimapCoordLabel` 只有构造时赋一次值）。
2. `InkMinimap` 的圆盘内部是透明的：雪地/沙漠这类亮场景里圆盘轮廓与四方位字几乎消失（放大截图只剩几个半透明色块）。

**修法**：
- 坐标行改为玩家真实世界坐标（`TryGetPlayerPose` → `X:… Z:…`，平面轴与地图页口径一致），0.5 秒刷新，拿不到位姿显示 `—`；前缀「向」随之换成「坐标」（`向 X:414 Z:353` 读不通）。
- `InkMinimap.Draw()` 在画实体前先铺一层 `BaseDefault` 50% 墨底圆盘——两个 HUD 共用该组件，一处修好两边。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `combat-hud-v2` 177.5 / 13087、`combat-hud-traditional` 171.5 / 13554 均正常；放大截图确认圆盘有明确底色、北/东/南/西 可读，坐标行显示真实值 `X: 414 Z:353`。

## 133. 师徒页与成就页的写死数值（2026-10-30）

用"UI 里 3 位以上的数字字面量"扫了一遍，抓出三处**构造后永远不会被更新**的字段：

| 位置 | 原值 | 说明 |
| --- | --- | --- |
| 师徒页 传承值 | `0 / 5,000  传承值` | 没有师徒服务；`_levelValueLabel` 全类只在这一处赋值，等于永远显示"计数为零" |
| 师徒页 传承余额 | `3,820 传承` | 同上，且是局部变量，连改名都做不到 |
| 成就页 详情 成就点 | `成就点 +120` | `_detailPoints` 只构造一次，任何成就都显示 +120 |

**修法**：三处改成 `— 传承值` / `— 传承` / 空串（成就点留空，等将来有成就服务再绑）。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-mentor` 24.3/1593、`nav-achievement` 25.4/1295 正常；二进制扫描：`3,820 传承` 0 次、`0 / 5,000  传承值` 0 次、`成就点 +120` 0 次，新占位 `— 传承值`/`— 传承` 已在装配里。

## 134. 小游戏结算页的分数与最高记录（2026-10-30）

`MiniGameCompletePage` 的得分与最高记录都写死 `8,650`：得分字段虽然有 `SetScore(...)` 会在调用时覆盖（第 391 行），但页面可以从「音效设置 → 快速跳转」直接进入，此时没有任何调用方，于是永远显示 8,650；"最高记录"那行则完全没有存储来源。

**修法**：得分初值改 `—`（真调用方设值时覆盖）；最高记录改 `最高记录: —`。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 21:38:52）；二进制扫描 `8,650` 已 **0 次**、`最高记录: 8,650` 0 次，新占位 `最高记录: —` 已在装配里。

## 135. 角色面板物品详情的四格属性写死（2026-10-30）

`CharacterPanelPage` 物品详情右侧的「强化 / 五行 / 绑定 / 耐久」四个字段（`_invMetaEnhance/_invMetaElement/_invMetaBind/_invMetaDura`）都只在构造时赋值一次（`+12 / 金 / 拾取绑定 / 850/1000`），全类再无写入点 ⇒ 无论选中哪件装备，显示的都是同一组假属性；耐久条虽然初始宽度为 0，但那行数字仍声称 85%。

**修法**：四格一律改 `—`（颜色降为次要色）；装备实例数据目前没有这些字段，等有来源再由绑定写入。耐久条保持 0 宽。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-character-panel` 29.1 / 6304 正常；二进制扫描 `850/1000`、`拾取绑定` 均为 0 次（`+12` 的另外两处属于别的页面）。

## 136. 清掉传统 HUD 里"存了没人读"的 BindSkills（2026-10-30）

`CombatHudTraditionalPage.BindSkills(SkillBase[])` 只把参数存进 `_boundSkills`，全类再无读取点；注释还写着「预留接口，当前技能栏使用 mock 配置」，容易让人以为 HUD 技能栏是假的。实际上传统 HUD 的技能入口是独立的 `InkActionBarControl`（走 `ActionBarService` + `SkillCatalogService`，真数据），HUD 本身不画技能格。

**修法**：删除 `_boundSkills` 字段与 `BindSkills` 方法；`MainUIManager` 三处调用（重绑块 ×1、自愈块 ×1、即时绑定块 ×1）一并去掉，即时绑定处只保留 `_cachedSkills = skills`（冷却刷新仍在用它）。遗留的 CombatHudPage（旧 HUD）仍保留同名方法——那边的 `_boundSkills` 是真的每帧被读来算冷却。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE 两套 HUD 均正常（171.4/13554、177.5/13087）；`grep BindSkills` 只剩旧 HUD 一处定义与一处调用。

## 137. HUD/品牌/小地图改动后的 39 页全量回归（2026-10-30）

今天后半段改动面很宽（两套 HUD 的底色与假数据、`InkMinimap` 组件加墨底、三页品牌与版本号、师徒/成就/小游戏/角色面板的写死数值、死接口清理），收尾再跑一轮全量：

- **39 页全部 `ok` 或 `ok_dark`，没有一页 `BLANK?`、没有 `ok=False`、没有崩溃**。`ok_dark` 四页即司南 10.2、祈愿 11.5、邮件 9.6、阵亡 11.4——新判据（§128）下它们不再被误报。
- 与上一轮相比的变化都能对应到改动：门派 1451→1459（品牌四字 + 徽章）、组队 951→955、奇珍阁 2147→2160、商店 2531→2542、时辰 1451→1457、小地图加墨底后两套 HUD 颜色数分别 13554 / 13087。
- 拍照页 152→157.5、颜色 8215→12378：那是场景本身（雪地 vs 取景框）差异，不是 UI 改动引起的。

**结论**：今天所有 UI 改动在运行时是整体自洽的；剩余待办仍是 §122 里那两条（真实服务端链路只能验空态；交付前清自测标记）。

## 138. 设置页「显卡 / 显存占用」由占位改为引擎真值（2026-10-30）

之前这两格一直是 `—`，理由是"没有可用的引擎读取接口"。这次先按规程 dump 接口面（`.compile-temp/agent/FlaxEngine.CSharp.xml`），确认下面三个成员存在且语义明确：

- `GPUDevice.Instance.Adapter.Description` —— 适配器描述字符串（"Used for presentation to the user"）；
- `GPUDevice.Instance.MemoryUsage` —— 所有 GPU 资源占用字节数；
- `GPUDevice.Instance.TotalGraphicsMemory` —— 显存总量（字节）。

**实现**：`SettingsPage.ReadGpuInfo()` 读三者 → 显卡名（超过 26 字符截断，避免顶破三列布局）、显存 `已用 / 总量 GB`；构造时写入一次，之后随 0.5 秒的帧率刷新一起更新显存占用；`GPUDevice.Instance` 为空或抛异常时回退 `—`。

**验证**：离线 `csc` 0 error；编辑器构建 success（DLL 21:55:07）；PIE `nav-settings` 21.2 / 2013；放大截图实测「显卡 AMD Radeon Pro 5600M / 显存占用 1.2 / 8 GB / 当前帧率 29 fps」——三项都是真实读数。

## 139. 「分辨率」也是只存不下发，补进设置页脚注（2026-10-30）

设置页「分辨率」下拉的落地处理只有 `GameConfigurationService.SetResolutionAsync(...)`，全链路没有任何 `Window.ClientSize`／`Screen` 的写入——和抗锯齿/阴影/特效/省电同一档：**改了不生效，只留偏好**。脚注原来只列后四项，容易让人以为分辨率是真生效的。

**修法**：脚注改成「※ 分辨率 / 抗锯齿 / 阴影质量 / 特效质量 / 省电模式在当前引擎版本运行时不生效，仅保存设置」。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-settings` 21.2 / 2017；放大截图确认脚注已含「分辨率」，同屏「AMD Radeon Pro 5600M / 1.2 / 8 GB / 23 fps」仍为真实读数。

**备注**：`Window.ClientSize` 在 XML 接口面里确实存在，将来若要真生效，应在 `GameConfigurationService.ApplyGraphicsSettings()` 里按存档设置一次（编辑器内 PIE 会改动 Game 面板尺寸，需要额外判断）。

## 140. 通行证轨道「< / >」是假翻页（2026-10-30）

`MenuBattlePassPage` 的轨道左右按钮 `OnTrackPrevClicked / OnTrackNextClicked` 两个处理器都只有一句 `NavigationRequested?.Invoke(InkPageDomIds.NavBattlePass)`——等于"重新打开本页"，画面完全不变。轨道也只有设计稿的 10 个等级节点（Lv.20—29）可显示，没有别的分段。

**修法**：两个按钮 `Enabled = false`，删掉两个空处理器（连同注释）。等将来通行证有服务端分段数据再实现真正的翻页。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-battle-pass` 28.1 / 3450 正常；`grep OnTrackPrevClicked|OnTrackNextClicked` 0 命中。

**插曲**：这轮 PIE 中途自己停了（`isPlayMode=false`），第一次抓图是 0.1/76 的真空屏——按 §128 的判据它被正确判成 `BLANK?`；重启 PIE 后同一页面 28.1/3450。说明新判据既能放过深色页，也没放过真的空屏。

## 141. 成就页：分类/卡片/筛选三处交互都是空的（2026-10-30）

扫"空处理器"时抓到 `AchievementPage` 三个方法体为空的交互入口：

- `OnCategorySelected(string)` —— 左侧六大分类点了没反应；
- `OnCardClicked(string)` —— 12 张成就卡点了右侧详情不变；
- `OnChipClicked` 只改芯片高亮，**不过滤卡片**（"已解锁/进行中/未解锁"永远显示全部）。

另外页脚「最近解锁：嗜血修罗」也是写死的成就名。

**修法**：
- `OnCardClicked` 按名称找到卡片 → 切换所有卡片的 `IsSelected`，并把名称/状态/描述写进右侧详情（状态沿用卡片上的「尚未解锁 / 隐藏成就」）；
- `OnCategorySelected` 更新面包屑（成就目录没有按分类的真实数据，所以只切分类名，不假装过滤）；
- 新增 `ApplyStateFilter`：按芯片切 `Card.Visible`（全部 / 已解锁 / 进行中 / 未解锁 ↔ 卡片 `CardState` 0/1/2），全被过滤时显示「暂无符合条件的成就」空态；
- `AchievementCard` 的 `CardName / CardDesc / CardProgressText` 补 `get`（原来只有 set，处理器读不到值）；
- 页脚改 `—`。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-achievement` 25.4 / 1294 正常，`ui_tree` 确认页面与 12 张卡片仍在。

**边界**：点击行为没有鼠标注入可验，只有代码路径与"页面无回归"作证（同 §108 的缺口）。

## 142. 又是两处"点领取 = 重新打开本页"（2026-10-30）

顺着"处理器体只有一句 `NavigationRequested?.Invoke(...)`"扫了一遍，抓到：

| 位置 | 原行为 |
| --- | --- |
| 通行证 `OnClaimRewardClicked` | `Invoke(NavBattlePass)` —— 重开本页，点了等于没反应 |
| 通行证 `OnClaimAllClicked` | 同上 |
| 成就 `OnClaimClicked` | `Invoke(NavAchievement)` —— 同上（还带了金色粒子，更像"成功了"） |

**修法**：三处改成明确提示「通行证奖励领取尚未接入服务端」/「成就奖励领取尚未接入服务端」（成就页保留粒子反馈）。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-battle-pass` 27.6/3301、`nav-achievement` 25.4/1293 正常；二进制扫描确认两条新文案各 1 次。

**判据记录**：这类缺陷的静态特征是「方法体只有一句导航、且目标就是本页的 dom-id」；通行证的"解锁/升级"跳奇珍阁属于真实跳转，不在此列。

## 143. 多人页「网络延迟」改为实时 RTT（2026-10-30）

原来顶栏的时延来自 `InkPlayerHeaderBinder.GetGatewayText`——那是**选路时测一次的静态值**，进页之后不再变化；手动刷新按钮才更新。而客户端同步链路本来每包都在更新 `ClientSyncMetrics.EstimatedRttMs`。

**修法**：`ApplyNetworkStatus()` 优先显示实时 `EstimatedRttMs`（>0 才用），否则回落静态值；页面 `Update` 每 1 秒自动刷新一次（只有刷新按钮时才更新是没意义的）。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-multiplayer` 24.1 / 1176；放大截图实测「网络延迟: **6 ms**  服务器: —」——6ms 是本机回环的真实 RTT，服务器区域仍是 `—`（网关绑定尚未给出区域名）。

## 144. 本轮 UI 工作汇总与验证状态（2026-10-30，交接用）

**一、把"假数据 / 假交互"清到只剩两类**（§96–§143 逐条有证据）
- 去假数值：装备强化 `+12/850/1000`、师徒传承 `3,820`、成就 `+120`、小游戏 `8,650`、角色面板四格、世界地图 `X:1234 Y:5678 / 3 个可接`、司南 `山贼营地`、V2 小地图 `昆仑墟·深渊`、结伴江湖 `剑客张三` 名片、各页写死版本号；
- 去假交互：秘境"进入"、成就"领取"、通行证"领取/翻页"、V2 场景占位名牌、两套 HUD 小地图的写死敌人点。
  （注：好友"飞鸽"入口查证后是 `action-*` 前缀路由、正常可达；结伴江湖的四个操作按钮在本轮之前就已 `Enabled=false`，本轮只把名片上的假玩家信息改成空态——这两条不属于本轮修复。）
- 去假视觉：V2 整屏 `HudBackdrop`（**它把 3D 世界整块盖住**，实测 20.9/1306 → 114.3/17929）、HUD 各浮层在雪地上的可读性（角色面板 0.45、罗盘带 0.55、小地图圆盘 0.50）。

**二、接上的真实数据**（有运行期读数作证）
| 位置 | 数据源 | 实测 |
| --- | --- | --- |
| 设置页 显卡 / 显存 | `GPUDevice.Adapter.Description` / `MemoryUsage` / `TotalGraphicsMemory` | AMD Radeon Pro 5600M · 1.2 / 8 GB |
| 设置页 帧率 | `Engine.FramesPerSecond` | 23–29 fps |
| 多人页 网络延迟 | `ClientSyncMetrics.EstimatedRttMs`（1 秒刷新） | 6 ms |
| 司南 坐标 / 附近方位 | `PluginNavigationBridge` | X:-623 Z:336 · 落雁峰 100m 等 |
| HUD 小地图坐标 | 同上 | X:414 Z:353 |
| 头像/等级/血气血 | `CharacterAttributesComponent` | 400/400 · 1000/1000 |

**三、验证阶梯与盲区**：见 §122（六道门 + 四条盲区）；本轮新增两条判据工具——`child_overflow.py`（子控件大于父容器，已清零 8 页真实缺陷）与抓图判据修正（§128）。

**四、仍然只能做到"空态"的页面**：好友 / 组队 / 帮会 / 邮件 / 排行 / 成就服务 / 活动 / 祈愿 / 秘境 / 商店购买 / 通行证 / 师徒——服务端空壳，页面已全部改成空态或明确提示，不摆虚构内容。

**五、交付前必做**：清掉 `%APPDATA%/HundunWorld/` 的 `auto-login.on`、`enter-world.on`、`open-page.txt`（否则玩家启动会被自动登录并跳到走查页）。

## 145. 传统 HUD 修为百分比不跟随经验变化（2026-10-30）

`RefreshBoundCharacterDisplay()` 已经把 `c.Exp / c.MaxExp` 写进 `_expBar.Value` 与底部经验条，但右侧那个百分比文字 `_expPercentLabel` 只在构造时写成 `0%`，此后不再更新——经验涨了，条在动、数字不动。

**修法**：同一处绑定里按 `c.MaxExp > 0` 计算并写入百分比，`MaxExp <= 0` 时显示 `—`。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE 两套 HUD 正常（171.5/13515、177.5/12970）。

**边界**：本地测试角色 `Exp = 0`，所以截图上的数字仍是 0%——这一处只能靠代码路径作证，等有真实经验增长（战斗结算）才能看到数字变化。

## 146. 收工全量回归：40 个页面（含两套 HUD）全部通过（2026-10-30）

本轮最后一次全量抓图，覆盖了收尾这几项改动（小地图圆盘墨底与坐标、成就页交互、通行证领取提示、多人页实时 RTT、修为百分比）：

- 36 页 `ok` + 4 页 `ok_dark`（司南 10.2 / 祈愿 11.4 / 邮件 9.6 / 阵亡 11.4）；
- **没有 `BLANK?`、没有 `ok=False`、没有崩溃、没有导航卡死**；
- 两套 HUD：传统 171.4 / 13542，V2 177.5 / 13011（世界可见、控件可读）；
- 收尾 `sim_stop` 正常退出 PIE。

至此本轮 UI 工作的代码侧与运行时侧都已自洽；剩下的只有服务端链路（§144 第四节）与交付前清自测标记两件事。

## 147. 战备页「战力」有绑定接口但没人调用（2026-10-30）

`MenuBattlePrepPage.BindCombatPower(current, recommended)` 存在、注释还写着"不调用时保持设计稿初值 12,450 / 15,000"，但**全仓没有任何调用方**，页面就停在 0 / 0、达成率 0%。

**修法**：
- `MainUIManager.CreateMenuBattlePrep()` 里按角色面板同一占位口径算战力（`(物攻+法攻)*2 + (物防+法防)*1.5 + 最大生命*0.2`），调用 `BindCombatPower(power, 0)`；推荐战力没有数据源传 0；
- 页面在 `recommended <= 0` 时把推荐值显示为 `—`、达成率那行改成「推荐战力尚未接入服务端」，不显示 0/0%。

**验证**：离线 `csc` 0 error；编辑器构建 success；PIE `nav-battle-prep` 23.4 / 1978；放大截图实测「当前战力 **682** / 推荐战力 **—**」＋「推荐战力尚未接入服务端」——682 与角色面板的战斗力完全一致，说明两处走的是同一公式。

## 148. 认证回退路径把全屏登录页钉进 480×297 小框（2026-10-30）

`AuthenticationUI.ShowLoginPanelInternal()` 里有一段"强制中心对齐"：

```csharp
_loginPanel.Size = GoldenRatioLayout.CalculateLoginPanelSize(); // 480 × 296.66
_loginPanel.AnchorPreset = AnchorPresets.MiddleCenter;
_loginPanel.Location = new Float2(-_loginPanel.Size.X / 2f, 0);
```

而 `LoginPanel` 本身是**全屏三栏设计**：背景图铺满父控件，内容层按 1920×1080 用 `min(W/1920, H/1080)` 等比收敛（`LayoutZones`）。把它钉成 480×297 后，内容层只会收敛到 25%，整页既小又偏出屏幕——UI 树实测 `LoginPanel bounds {x:-540, y:0, width:480, height:296.66}`。

**修法**：回退路径改为与面板设计一致的拉伸铺满（`AnchorMin=0 / AnchorMax=1 / Offsets=0 / Pivot=0 / Scale=1`）后再 `RefreshLayout()`；注册面板不做改动（它确实是固定尺寸卡片）。

**验证**：
- 离线 `csc`（496 个源文件，含本文件）`0 error CS`；编辑器 `code_build_status` = `lastCompilationFailed:false`，`Game.CSharp.dll` 于 00:12:37 重建；
- PIE（RootScene）`ui_tree` A/B：改前 `LoginPanel {x:-540, w:480, h:296.66}` → 改后 `{x:-300, w:1264, h:736}`（等于其父容器尺寸，不再是固定小框）。

**边界**：Login.scene 里存在 `Game.AuthenticationController`（`sceneAuth`），此时本方法在第 820 行就转交给场景自带认证页并 return，**这段回退代码在正式登录场景不会执行**；因此本节的运行时证据来自 RootScene 的回退分支，正式登录页仍走 §96 起那套全屏 Ink 登录页。

## 149. 本轮环境侧三条取证事实（2026-10-30 深夜）

1. **编辑器启动即崩**：`AssetsCache::Init()` 处 `EXCEPTION_ACCESS_VIOLATION writing 0x8`，前面紧跟 `HashSetBase.h:386 That key has been already added to the collection` 与大量 `FileReadStream.cpp:119 _virtualPosInBuffer == 0`。处置：结束该实例 → `cp` 备份后 `mv HundunWorld/Cache/AssetsCache.dat Tools/_moved_AssetsCache_<时间>.dat` → 带 `-project` 重启，桥恢复（`items count: 2762`）。
2. **`scene_load` 只认带连字符的 .NET 串**：传 `"Content/Maps/RootScene.scene"` 与文件里的 32 位 ID 都报"未找到场景资产"；传 `asset_search` 回执里的 `b3a1f0d2-6a87-4e5c-f3e2-d190d7c6b5a4` 才 `loaded`。原因是 `Guid.TryParse` 对无连字符串按 .NET 布局解析，得到的 16 字节与资产头不是同一组。
3. **`game_screenshot` 不含 UI 层**：同一帧 `ui_tree` 明确报 `LoginPanel visible:true, 1164×706`，导出的 PNG 却只有 3D 天空与地面。**判 UI 必须用 `window_capture.ps1`（PrintWindow）**；另外 Game 视图可能带着缩放，窗口截图里右侧内容被裁不一定是布局越界（登录页内容层按 `min()` 收敛，1920 宽在当前视口下应完整可见）。

## 150. 登录页「找回密码」是死交互（2026-10-30）

登录卡右下角那个 `找回密码` 按钮是 `LoginPanel._forgotButton`，构造后**从未订阅 `ButtonClicked`**，全仓也没有任何 `ForgotPassword` 服务/协议——玩家点它毫无反应，看起来像卡住。

**修法**：订阅点击并复用卡片自带的状态行如实提示「找回密码尚未接入服务端，请联系客服重置」（不弹假的输入流程、不发假请求），`OnDestroy` 里对称退订。

**验证**：离线 `csc` 0 error；编辑器构建 success，`Game.CSharp.dll` 00:17:32 重建；对 DLL 做 UTF-16 字面量扫描，新增文案 `找回密码尚未接入服务端` 命中 1 次（旧装配里必然 0 次）。

**边界**：桥的 `ui_click_control` 进不到登录面板（旧式 Flax 控件，`ui_input` 返回 `hit:<none>`），所以"点一下看状态行变没变"这条只能等人工点验；代码侧已确认按钮→`SetStatus`→`StatusLabel.Text` 是同一条链（`SetStatus` 就是注册页与登录失败共用的那条）。

## 151. 制造页：死按钮与不跟随的配方计数（2026-10-30）

**一、`搜索配方` 是死交互**：`CraftingPage` 顶栏那个按钮创建后既没有 `ButtonClicked`，也没有 `Enabled=false`（对比同工程其余 12 处"服务端没通道就禁用"的按钮），点上去毫无反馈。全仓也没有配方搜索的数据通路。修法：`Enabled = false` + 注释，与其余同族按钮一致。

**二、区头计数写死**：中栏区头写死 `"8 个配方"`，而 `ApplyServerRecipes()` 绑到制作设施后卡片会换成服务端清单（数量可能不是 8）⇒ 数字与列表脱节；未连接设施时也没有任何"这些是示例"的提示（只有详情区底部一行"演示数据"）。修法：
- 计数改成字段 `_recipeCountLabel`，初始文案 `"示例 · 8 个配方"`，`ApplyServerRecipes()` 里同步成 `$"{count} 个配方"`；
- 位置从「TagBox 左侧 150px、宽 130」挪到「TagBox 左侧 238px、宽 170」——旧位置右边缘会压进 `全部` TagBox，文字被压掉一截（实测第一版改完仍只显示 `8 个配方`）。

**验证**（三轮 PIE + 窗口截图，均先 `sim_stop → code_build_wait → 等 DLL mtime → sim_play`）：
- 离线 `csc` 0 error；编辑器构建 success；
- 第一次截图仍是旧文案（`8 个配方`）+ 按钮未置灰 ⇒ 查明是**热重载没换装配**（见下）；
- 重启编辑器后同一页：区头 `示例 · 8 个配方`、`搜索配方` 灰度不可点，其余 8 张卡片、详情、示例数据提示均正常。

**三、热重载陷阱（本轮新增判据）**：00:23 那次构建日志末尾出现 `xcopy Horizon.Game.Message.dll …` 失败（编辑器占着该 DLL），`Game.CSharp.dll` 时间戳明明更新、DLL 里也能扫到新字面量，但 PIE 用的还是编辑器启动时（00:20:15）载入的那份装配。**判据组合**：DLL mtime 新 + DLL 内有新字面量 + 运行画面仍旧文案 ⇒ 装配没换，必须重启编辑器；只重启 PIE 不够。

## 152. 两处写死的「服务器/延迟/版本」改成真实数据（2026-10-30）

**一、登录页底部状态行**：`_serverLabel` 写死 `服务器：烽火连城 · 流畅`，绿色呼吸点也是纯装饰——玩家断网时它照样显示"流畅"。修法：新增 `UpdateServerLine(ConnectionStatus)`，在构造期 `CheckAndUpdateButtonState()` 与每次 `OnConnectionStatusChanged` 时同步：
- `Connected` → 「服务器：已连接」+ 成功绿点；`Connecting/Reconnecting/GatewaySwitching` → 「服务器：连接中…」+ 警示金点；
- `Disconnected` → 「服务器：未连接」+ 中性灰点；其余异常 → 「服务器：连接异常」+ 错误红点。

**二、营生页页脚**：`服务器：逍遥江湖` 与 `延迟 32ms · 版本 v1.2.4` 都是设计稿里的假数（版本号还与本项目统一的 `v1.2.0` 不一致）。修法：改用 `InkPlayerHeaderBinder.GetGatewayText(out region, out latency)`（它与武学录页同一个数据源，没测过延迟时给 `—`），无网关时服务器显示「未连接」、延迟不可得时只显示「版本 v1.2.0」。

**验证**：
- 离线 `csc` 0 error；编辑器构建 success；对 `Game.CSharp.dll` 做 UTF-16 扫描：新字面量 `服务器：未连接`/`服务器：已连接` 各 1 次，旧字面量 `服务器：逍遥江湖`、`延迟 32ms · 版本 v1.2.4` 均为 0；
- 重启编辑器后 PIE 抓图：登录页底部为「服务器：未连接」且圆点变灰（原为绿色 + "烽火连城 · 流畅"）；营生页页脚为「服务器：未连接」（原「逍遥江湖」）。
- 版本号那一格没有截到（Game 视图右侧被编辑器裁掉），只能靠 DLL 里 `版本 v1.2.0` 的来源与武学录页同口径作证。

**副产品（字形坑）**：营生页页脚用的是 `InkRenderHelper.GetFontRef(FontRole.Body, 11f)`，第一版写成 `服务器：—`，实测只显示「服务器：」，破折号无字形 ⇒ 该字体族下别用 `—` 兜底，改成「未连接」这类正文字。

## 153. 「服务器/延迟」占位统一收口到 `GetGatewayText`（2026-10-30）

§152 只修了营生页，其余三个消费者仍在用会"消失"的占位：
- 武学录页脚：初值 `服务器：—` / `延迟 —`，无网关时刷新成 `服务器：` + 空；
- 多人页顶栏：初值 `服务器: —`，`_netValue` 的 `—` 在 13f 下也可能空白；
- 时间页底栏：`混沌盛世 · —`（设计稿里的世界名）+ 无延迟时只剩 `混沌盛世 · `。

**修法**：把兜底收进唯一的读取点——`InkPlayerHeaderBinder.GetGatewayText` 现在直接返回 `未连接` / `未测得`（注释里写明为什么不用 `—`），四个调用方都改成直接用返回值：
- 营生页：`服务器：` + region、`延迟 x · 版本 v1.2.0`（未测得时省略延迟段）；
- 武学录：初值与刷新都改成 `服务器：未连接` / `延迟 未测得`；
- 多人页：服务器行同样走 region；延迟数值列未知时用 ASCII `--`（保持 40px 列宽不撑破）；
- 时间页：底栏改成 `延迟 未测得` / `延迟 xms`，不再挂设计稿的世界名。

**验证**：
- 离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 00:53:32）；
- DLL UTF-16 扫描：`未测得` ×2、`未连接` ×26；旧字面量 `混沌盛世 · `、`服务器：—`、`延迟 —` 全部 0；
- 重启编辑器后 PIE（武学录）截图：页脚实读「服务器：未连接   延迟 未测得」；营生页上一节已实读「服务器：未连接」。
- **边界**：同一 PIE 会话里第二次写 `open-page.txt`（→ `nav-time`）没有再产生导航回执，时间页与多人页只做到代码 + DLL 级验证，没有本轮截图。

**同轮环境记录**：`open-page.txt` 的 watcher 在本轮"首次导航成功"之后不再响应（文件内容已变、回执停在上一条），与本工程此前"可连续换页"的经验不一致；遇到时先重启 PIE 再续，别把"没换页"当成页面拒载。

## 154. 登录页「自动登录」勾选框是纯装饰（2026-10-30）

`_autoLoginCheckBox` 建出来之后**全仓没有任何读取点**：`PassportInfo` 里也没有对应字段（只有 `RememberPassword`），`AuthenticationUI` 里只有自测标记 `auto-login.on` 那一套。玩家勾上它再重启，什么都不会发生，属于典型的"能点但没功能"。

**修法**（按本工程"没有通道就禁用并说明"的既有口径，不新增未经确认的自动登录行为）：
- 勾选框 `Enabled = false`，图标色降到 `Ink400`；
- 文案改成「自动登录·未开放」，颜色同降到 `Ink400`，让人一眼看出未开通；
- 注释写明"PassportInfo 无对应字段、勾选状态无处可存"。

**验证**：离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 01:00:51 重建）；DLL UTF-16 扫描 `自动登录·未开放` 命中 1 次；重启编辑器后 PIE 抓图，登录页其余部分正常（`服务器：未连接`、`v1.2.0`、记住密码勾选态不变）。

**边界**：该勾选框位于登录卡右半列，本机 Game 视图右侧被编辑器裁掉，截图里只能看到「自动登…」被切断——**这一条只有 DLL 字面量 + 代码路径作证**，没有截到完整控件；时间页 `延迟 未测得` 同样只在 DLL 里核到（底栏右侧同样被裁）。

## 155. 版本号又一处不一致：活动页页脚 `v2.0.1`（2026-10-30）

活动页侧边栏页脚写死 `v2.0.1 · 江湖公测`，与全项目统一口径 `v1.2.0` 不一致（本项目只允许一套版本号，见既有约定）。改成 `v1.2.0 · 江湖公测`，并把商店页两处过时注释里的 `v1.0.0` 一并订正（页脚正文本来就是 `v1.2.0`，只有注释过期）。

**验证**：离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 01:09:48 重建）；重启编辑器后 PIE 抓图，活动页侧边栏底部实读「v1.2.0 · 江湖公测」。

**同轮反证（重要，避免改错）**：审计时怀疑「目标框/司南的 `xxm` 把厘米当米」，查了场景 POI 坐标——`POI_Kaifeng(-118,50,-104)`、`POI_Qingyunshan(-1200,300,900)`、`POI_YaowangGu(-1900,150,1200)`，彼此相距 100–2000 **场景单位**，与「米」量级吻合；若按 Flax 默认厘米口径再 `/100`，会把这些地标显示成 1–20 米，**改错**。结论：本工程世界坐标按米作者化，UI 里 `Distance(...)` 直接标 `m` 是对的，不动。

## 156. 装备页在真实数据下仍显示样例容量与编造属性（2026-10-30）

`MenuEquipmentPage` 已接 `PlayerInventoryService` 快照（真实物品/穿戴态/聚合攻防），但两处仍停留在设计样例：

1. **背包标题计数写死 `10/24`**：`_backpackCountLabel` 只在 `BuildBackpackTitle()` 里建一次。真正的根因是**构造顺序**——`InitMockData()` 在构造函数第 3 步就调 `TryLoadFromInventory()`，那时标签还不存在，所以"加载真实数据时顺手更新计数"的代码（`_backpackCountLabel != null` 守卫）整段跳过；此后没有 `Changed` 事件，计数就永远停在样例值。修法：初值直接按数据源给（`_realSource ? $"{_realItems.Count} 件" : "10/24"`），服务端没有容量字段就只报可装备件数。
2. **对比面板按等级编属性**：`_compareCandidateAttr` 用 `攻击 +{120 + Level*3}` / `暴击 +{Level%10}%`、差值写死 `+24 / -3%`，而快照里根本没有单项属性。修法：`_realSource` 为真时显示「单项属性尚未接入服务端」、差值留空；只有样例模式（页面顶部有「设计样例」提示）才保留设计稿那两行。

**验证**：
- 离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 01:21:12 重建）；
- 重启编辑器后 PIE（`nav-equipment`，真实快照 3 件可装备）：抓图实读背包标题为 **「3 件」**（改前是 `10/24`），列表 3 行、`已装备 4/8` 未变；`ui_tree` 查样例提示 Label 为 `visible:false`（真实数据下确实收起）。
- **边界**：对比面板要选中物品才出内容，桥点不动列表项，本轮只做到代码级；样例模式（无快照）下的提示与 `10/24` 未再截图复核。

**判据补充**：`ui_tree` 不报 `Visible` 之外的状态，但能查「某个 Label 是否 visible」——本节就是用 `bounds w=760,h=18` 定位到提示标签并确认 `visible:false`，从而反推出"构造期就已判定为真实数据"。同类页面判"提示该不该显示"可复用这招。

## 157. 「无区域名」被写成「未连接」，与同一行的实时延迟自相矛盾（2026-10-30）

§153 把兜底统一成 `未连接/未测得` 之后，多人页顶栏出现一句自相矛盾的读数：**「网络延迟: 15 ms　服务器: 未连接」**——延迟是 `ClientSyncMetrics` 的实时 RTT（已连上），只是网关没下发 `Region` 字段。

**修法**：`GetGatewayText` 区分"没有网关"和"网关在但没区域名"：前者仍给 `未连接`，后者给 `未知区域`；延迟逻辑不变（没有测过才给 `未测得`）。

**验证**：离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 01:28:20 重建）；重启编辑器后 PIE（`nav-multiplayer`）抓图实读「网络延迟: 9 ms　服务器: 未知…」（右侧被 Game 视图裁掉一格，未截全"区域"）。改动前同一位置为「…服务器: 未连接」。

**顺带确认的走查事实**：`open-page.txt` 的导航**确实能连续换页**（本轮 crafting → multiplayer 成功），只是 `ui-selftest.out` 的回执有时不追加新行——**判"有没有换页"要看截图/`ui_tree`，别只看回执**（上一节把它记成"watcher 停摆"，这里修正）。

## 158. 成就页：12 张样例卡片没有"这是示例"的明示（2026-10-30）

`AchievementPage` 的卡片来自本页 `cardNames/cardDescs` 数组（"初出茅庐 / 百战之将 / 武林盟主…"），右侧详情默认也是样例（"武林盟主 · 7 / 10 · 70%"），页面上只有"尚未解锁"这一处诚实文案，读者会把这些条目当成自己的成就目录。页脚"本月新增 +7"同样没有数据源。

**修法**：
- 标题行补一行 11f 提示：**「成就服务尚未接入 · 卡片与详情为目录示例」**（放在副标题右侧 y=32，避开"总体完成度"那一列）；
- 页脚"本月新增"的值从 `+7` 改成 ASCII `--`（小字号不用 `—`，见 §152 的字形坑）。

**验证**：离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 01:33:35 重建）；重启编辑器后 PIE（`nav-achievement`）抓图实读标题行第二行为「成就服务尚未接入 · 卡片与详情为目录示例」，卡片网格、分类计数、"尚未解锁"文案均正常。

**边界**：详情面板的 `7/10 · 70%` 仍是样例数值，靠这行提示统一兜住；等成就服务接上后应把这些字段改成真实绑定而非删提示。

## 159. 通行证页：赛季等级/经验/奖励全是设计稿初值，页面无提示（2026-10-30）

`MenuBattlePassPage` 的注释写着"组件初值 = 设计稿示例数据…接入真实数据源后由 Bind* 覆盖"，但**全仓没有任何人调这些 Bind***，页面上也没有一句"这是示例"：顶部「第三赛季 · 江湖行」、`Lv. 0`、`0%`、`再获得 3,000 EXP 即可升至 Lv.1`、解锁横幅里的三种奖励预览，玩家会当成自己的赛季进度。

**修法**：赛季头卡片左下补一行 11f 提示 **「赛季数据尚未接入 · 等级 / 经验 / 奖励均为示例」**（`Place(_sampleNote, pad, 146f, 420f, 16f)`，在左列赛季信息之下、卡片高度内，不压中列进度条）。领取/解锁按钮此前已改为 toast 说明，未动。

**验证**：离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 01:39:12 重建）；重启编辑器后 PIE（`nav-battle-pass`）抓图实读该行提示，且「剩余时间 -」「参与人数 -」与「Lv. 0 / 0%」仍在原位、无重叠。

**顺带观察（判字形的新证据）**：同一页里 `_seasonRemainText = "—"`（12f Number）**正常显示成短横**，而 §152 的营生页 11f Body 下同样的 `—` 是空白 ⇒ 缺字形是**字体角色**相关，不是字号一刀切；以后遇到空白先看该标签用的是哪个 `FontRole`。

## 160. 回归走查：时间页 / 设置页（2026-10-30）

同一 PIE 会话内先 `nav-time` 再 `nav-settings`（导航生效，回执未追加，见 §157 的更正）：

- **时间页**：底部栏 `混沌世界 v1.2.0` 正常；右侧「延迟 未测得」落在编辑器 Game 视图被裁的那一列，未截到（代码 + DLL 已证）。
- **设置页**：显卡「AMD Radeon Pro 5600M」、显存「0.9 / 8 GB」、当前帧率「50 fps」为实时值；省电模式说明为「记录偏好：当前引擎无法运行时降档」；页脚 `v1.2.0 · Build 20260715`；底部脚注列出不生效的四项。无布局破损。

**关键判据（避免下一次误报）**：设置页截图里「分辨率 / 帧率上限 / 视野 / 特效质量 / 阴影质量 / 抗锯齿 / 垂直同步 / 自动隐藏UI」这些行看起来**右侧没有控件**，但 `ui_tree` 查得 `InkDropdown ×4（x=1226,w=190）`、`Slider ×2（x=1148,w=180）`、`InkToggle ×4（x=1368,w=48）` 全部 `visible:true`——它们位于设计坐标 x≥1148，正好落在编辑器 Game 视图被裁掉的右列。⇒ **"截图里看不见控件"在本机不能当"控件缺失"，一律用 `ui_tree` 复核。**

## 161. 新增页面体检闸门 `.compile-temp/page_audit.py`（2026-10-30）

一条命令跑多个页面，逐页给 `PASS/FAIL` 与节点数。做法：写 `open-page.txt` → 等 9s → 拉 `ui_tree` → 判四件事：

1. 当前页类型与目标一致（`*Page` 节点出现在运行时树里）；
2. 节点数 > 200（防"整页只建一半"）；
3. **可见的交互控件零尺寸**（`InkButton/Button/CheckBox/Slider/TextBox/InkToggle/InkDropdown/InkBackButton/InkTag/TagBox` 宽或高 ≤1）——这是"控件建了但用户点不到"的指纹；
4. 交互控件坐标 `x/y < -50`（越出页面层左/上边界）。

**本轮实跑（11 页，同一 PIE 会话连跳，全部 PASS）**：`nav-shop 483`、`nav-battle-pass 500`、`nav-achievement 464`、`nav-equipment 336`、`nav-social-mail 313`、`nav-settings 446`、`nav-character-panel 500`、`nav-livelihood 389`、`nav-time 327`、`nav-multiplayer 320`、`nav-crafting 419`。零尺寸交互控件 **0 处**。

**两个必须知道的假阳性/假阴性**：
- 第 4 条最初每页都报一条 `InkBackButton(-368,-177)`——那是**外壳自己那一层**（页面层原点是 `-378,-187`），不是越界；脚本已按路径含 `InkPageShell` 跳过。判"越界"必须分清是哪一层的坐标系。
- 本闸门只看"控件存在与尺寸"，**不判文案/数据真假**，也不判"控件是否被禁用"；数据类问题仍靠 §122 那一套截图 + 回执判据。

**顺带推翻一个旧结论**：`open-page.txt` 在同一 PIE 会话里**可以连续跳很多页**（本轮 11 页连跳全部落位）；之前记的"watcher 首跳后停摆"作废——回执不追加只是 `ui-selftest.out` 没写，导航本身照常生效。

## 162. 全量巡检：45 个页面一次跑完，零尺寸交互控件 0 处（2026-10-30）

用 §161 的闸门，同一个 PIE 会话分三批跑完 45 个已注册页面，**全部 PASS**：

| 批次 | 页面（节点数） |
| --- | --- |
| 一（15） | nav-quests 375 / nav-mail 313 / nav-sect 482 / nav-team 355 / nav-bestiary 475 / nav-personal-info 324 / nav-casual-mode 458 / nav-photo-mode 327 / nav-photo-album 282 / nav-element-vision 294 / nav-appearance 368 / nav-skill-tree 245 / nav-inventory 500 / nav-equipment-enhance 381 / nav-mount-pet 344 |
| 二（15） | nav-quest-log 375 / nav-world-map 280 / nav-compass 275 / nav-dungeon-entry 389 / nav-social-guild 305 / nav-friends 303 / nav-mentor 416 / nav-leaderboard 388 / nav-social-shop 483 / nav-npc-gossip 489 / nav-shop-rare 407 / nav-battle-prep 346 / nav-gacha 367 / nav-settings-audio 331 / nav-character-v2 500 |
| 三（15） | combat-hud 427 / combat-hud-traditional 427 / combat-hud-v2 347 / acupoint 270 / qte 357 / system-menu 354 / death-screen 354 / menu-char-attributes 500 / guide-action 500 / guide-hud 246 / cc-role-list 145 / cc-gender 138 / cc-face-preset 142 / cc-face-sculpt 498 / cc-name 144 |

（§161 已跑过的 shop / battle-pass / achievement / equipment / social-mail / settings / character-panel / livelihood / time / multiplayer / crafting 有过重叠，不重复计。）

**阈值的修正**：初版用 `nodes>200`，把四个创角分步页判成 FAIL（138–145 节点）。看截图核体：`cc-name` 是单步命名页（标题 + 输入框 + 计数 + 提示 + 返回 + 3D 预览），本来就是小页面，不是"只建一半"。闸门阈值改为 `nodes>120`（仍远高于半建页量级），四页随即 PASS。

**结论**：45 页在当前版本下都能落位、节点数 120–500、**可见交互控件零尺寸 0 处**、跨页坐标越界 0 处（外壳层那条已排除）。这条可当作后续每次 UI 改动后的快速回归：`python .compile-temp/page_audit.py <dom...>`。

## 163. 祈愿页：保底/记录/价格仍是设计稿数值，背包「丢弃」挂着不可达回调（2026-10-30）

**祈愿页**：`BindPity` 之外没有任何数据源，页面却直接显示「已祈愿 0 次」「距离保底还有 47 次」「大/小保底 47/90」和「元宝 × 160 / × 1600」，还挂着一枚「节省160元宝」的折扣徽章——与同页「概率概览（未接入）」自相矛盾。修法：
- 新增 `_hasServerData`（`BindPity` 置位）；未置位时 `RefreshPity`/`RefreshDrawProgress` 一律输出 `—` 与「祈愿记录尚未接入 / 保底次数尚未接入」，进度条归零；
- 折扣徽章 `Visible = false`；
- 单抽/十连价格初值改成 `元宝 × —`，只有 `BindDrawCost` 被调用时才显示真实价格。

**背包页**：`_invDropBtn` 在 `UpdateDetailButtons` 里被恒置 `Enabled=false`，却仍挂着一个 `ButtonClicked` 提示——禁用态永远收不到点击，属不可达代码，删掉回调并保留"丢弃路由未开通"的注释。

**验证**：离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 02:10:23 重建）；重启编辑器后 PIE（`nav-gacha`）抓图实读：保底 `- / -`、底部「祈愿记录尚未接入 / 保底次数尚未接入」、卡池剩余 `-`、两个按钮下方 `元宝 × -`、折扣徽章消失。

**踩坑记录**：给 `MakeLabel(...)` 那行加行尾注释时，`//` 把同一行后半截的参数一起注释掉了（`InkWashTheme.FontRole.Body, 11f,`），编译立刻 CS7036 两条。⇒ 多行调用的第一行不要挂行尾注释，注释要单独成行。

## 164. 剩下一批"未接入页"的静态复核（2026-10-30）

对"页面上已写明未接入/示例、正文却还有具体数字"做了一次静态扫描，逐页核对结论：

| 页面 | 命中 | 结论 |
| --- | --- | --- |
| 邮件 | 赏赐明细 4 件 / 剩余可领取 3 件 | **不需要改**：无邮件时 `_detailPanel` 与 `_attachmentPanel` 整块 `Visible=false`，样例数字不会上屏 |
| 活动 | 卡片 0% / +1 +2 +3 | 页面顶部已写"活动系统未接入，以下为目录样例"，奖励加成是样例内容，保留 |
| 休闲 | 0/0、0 / 500、共 9 项奖励 | "共 9 项奖励"与画出来的 9 个图标一致；前两处是零值 |
| 商店 | 单价 328、数量 1 | 选中货架商品后由 `product.Price` / `_qty` 覆盖；未交谈时整页标"示例货架" |
| 通行证 / 成就 / 珍品阁 / 装备 / 时辰 / 组队 | 各种数字 | 均已有上屏提示或为真实绑定（见 §156/§158/§159） |

**再验一次「—」的字形问题**：用 `nav-dungeon-entry` 现场截图核对——「总战力 —」「秘境积分 —」「推荐战力 —」「今日 — /3」「队伍战力 —」这些 11–14f 的破折号**全部正常显示**，说明 §152 里营生页那次空白不是"小字号一律没字形"，更像该字体/颜色组合下的极细笔画被抗锯齿吃掉。⇒ **不要把 `—` 全局替换成 `--`**；只有实测空白的那个标签按 §152 的办法换成文字（「未连接」「未测得」）。

**同页顺带确认**：秘境入口页的队伍区在未组队时显示「未组队 · 单人挑战」，战力为 `—`，没有摆假队友或假战力。

## 165. 当前构建二次全量回归：45 页仍全 PASS（2026-10-30）

在 §163（祈愿/背包）与 §156（装备）等最后一次改动**之后**的构建（`Game.CSharp.dll` 02:10:23）上，把 §162 的 45 页再跑一遍：

- 批次一 15 页全 PASS，节点数 245–500；
- 批次二 15 页全 PASS，280–500（含 `nav-gacha 367`，说明祈愿页改动没有改动页面结构量级）；
- 批次三 15 页全 PASS，138–500。

**结果与首轮一致**：无 FAIL、无零尺寸交互控件、无跨页越界。结合 §162 的首轮，等于是"改动前 / 改动后"两次同尺度的全量巡检，今天的 UI 改动没有造成任何页面级回归。

**仍然存在的盲区（如实记录）**：
- 点击级行为（选中物品后的对比面板、领取按钮的 toast、拖拽技能栏）桥工具点不到，只能代码级或人工点验；
- 服务端空壳页只能验到"空态 + 明示"，真实数据到手后的表现未验；
- 本机 Game 视图裁右列，凡落在设计坐标 x≳1100 的控件只能靠 `ui_tree` 证存在。

## 166. 服务端数据路径普查：谁真的"按版本号重建界面"（2026-10-30）

对引用 `*ClientService.Instance` 的 UI 文件逐个核对"有没有把回包应用到界面"。判据三件：`Update` 覆写、`Revision` 比对、`Apply*/Rebuild*` 方法。

| 页面 | 服务 | Update | Revision | 应用方法 | 结论 |
| --- | --- | --- | --- | --- | --- |
| 制作 `CraftingPage` | CraftClientService | ✔ | ✔ | `ApplyServerRecipes` | 真实重建（卡片必须重建，改字段不刷字） |
| 商店 `MenuShopPage` | ShopClientService | ✔ | ✔ | `ApplyServerShelf` | 真实重建，价格/库存来自 `ShopItemDto` |
| 任务 `MenuQuestsPage` | QuestClientService | ✔ | ✔ | `RebuildFromServer` 等 | 真实重建 |
| 好友 `FriendsPage` | FriendClientService | ✔ | ✔ | `RebuildRows` / `RefreshFromServer` | 真实重建 |
| 技能训练 | SkillTrainerClientService | ✔ | —（走 `.Changed` 事件） | `RebuildData` | 事件驱动，等价 |
| 背包/角色 `CharacterPanelPage` | PlayerInventoryService | —（事件） | — | `RefreshInventoryView`（订阅/退订成对） | 事件驱动，等价 |
| NPC 闲聊 `NpcGossipPage` | Crafts/Quests | — | — | 只 `RequestList`（点商人时拉清单，界面由其它页承接） | 合理 |
| 名牌 `NameplateUI` | QuestClientService | — | — | 只读当前列表做显示 | 合理 |

**结论**：没有发现"收了回包但只改字段、界面不刷新"的页面；五处需要重建的页面都走 `Update + Revision`，两处走 `.Changed` 事件，两处只发请求或只读。这条与 §161/§162 的"结构体检"互补：一个看**界面结构**，一个看**数据有没有真正落到界面**。

## 167. 行为级复验：好友页"选中 → 操作按钮恢复"（2026-10-30）

点击级行为桥工具点不到，但工程里留了 `friends-selftest.on` 夹具：注入一条 `Id=0` 的示例好友并选中，走一遍「选好友 → 6 个操作按钮恢复可点」，把结果写回执。当前构建上跑：

```
friends-selftest enabled=6 of=6 sel=0
```

- `enabled=6 of=6`：6 个操作按钮全部恢复（不是永远灰着）；
- `sel=0`：选中的是注入的那条；
- 同轮窗口截图实读：列表里出现「自测好友」并高亮，右侧详情显示「自测好友 / 在线」，`私聊 / 组队 / 传送 / 弹扎 / 飞鸽 / 删除` 六个按钮均为可点样式，`装备简介 · 装备未公开`。

**这一条补的是 §165 盲区里的"点击级行为"**：虽然不能真的点，但用"程序化选中 + 回执计数"证明状态机分支是通的（旧缺陷正是"空态禁用后没有恢复路径"）。夹具用完已删标记，正常启动不受影响。

**重复回执说明**：`ui-selftest.out` 里这条出现了 3 次——页面在导航/重建过程中被创建了多次，夹具按"每个实例只跑一次"的规则各跑了一遍，不是异常。

## 168. 行为级回归第二批：拍照模式与相册预览（2026-10-30）

同一天当前构建上跑两个自带夹具（回执文件 `photo-selftest.out` / `ui-selftest.out`）：

**拍照模式**（`photo-selftest.on` + `nav-photo-mode`）

```
angle preset=1 pitch=12.0 euler=X:47.21 Y:0.00 Z:0.00
fov slider=0 before=55.0 after=20.0 acquired=True cam=ok instance=03358DC5
capture path=…\Screenshots\Photo_20261031_023216.png
exposure ev=2.0 / filter index=1 / capture path=…023219.png
release instance=03358DC5 restoredFov=60.0 adjuster=null
camera via=MainCamera savedFov=60.0 instance=00A8FBA7 tpc=off adjuster=null
fov slider=0.5 before=50.0 after=55.0 acquired=True cam=ok instance=00A8FBA7
exposure ev=0.5 / filter index=0 / capture path=…023227.png
```

覆盖了：角度预设（Euler 与 pitch 同步）、FOV 滑块真实写入相机（55→20、50→55 两轮）、曝光 EV、滤镜切换、**快门真的出了文件**（三张 PNG）、退出时 FOV 还原到 60 且 `adjuster=null`；两条相机获取路径（`ThirdPersonCamera` 与 `MainCamera`）都走通。

**相册预览**（`album-preview.on` + `nav-photo-album`）：`album preview index=0 caption=Photo_20261031_023233`，窗口截图实读：整屏放大预览显示上一轮拍的第 1 张、右上「共 3 张 · 最近拍摄」、底部文件名与回执一致。

**这两个夹具补的是"控件点不到"的那类行为**：滑块是否真的写进相机、快门是否真的落盘、退出是否还原——都不是"看代码像是接了"能替代的。

## 169. 行为级回归第三批：HUD 空闲自动隐藏 / 有动作恢复（2026-10-30）

`ui-autohide-test.on` 把阈值降到 2 秒，并在第 4 秒模拟一次"有动作"（自动化里动不了游戏鼠标）。夹具不写回执，改用**按秒采样 `ui_tree` 里 `InkActionBarControl.visible`** 取证：

```
0.0s None / 0.9s None / 1.8s False / 2.7s False / 3.6s True / 4.5s True / 5.4s True / 6.3s True / 7.2s True / 8.1s True
```

- 前两拍 `None` = HUD 控件还没建出来（刚进世界）；
- **1.8s / 2.7s 为 False**：空闲超阈值后动作栏确实收起；
- **3.6s 起恢复 True 并持续**：模拟"有动作"后恢复，且夹具的 `_selfTestAutoHideRestored` 让它不再反复隐藏。

**这条补的是"状态机有没有两条腿"**：只验"会隐藏"会把"永远收不回来"的缺陷放过去；采样序列同时给出隐藏与恢复两段证据。**方法可复用**：凡是输出到 `visible` 的定时行为，都可以用"进场景后按秒拉 `ui_tree` 打时间线"来取证，不必等回执。

## 170. V2 HUD 队伍面板：空态仍占 3 行高度 + 一段已死代码（2026-10-30）

`CombatHudV2Page.BuildPartyPanel` 早先已把"3 名设计稿队友"改成「未组队」空态，但：
- 面板高度仍按 3 名成员算（`PartyHeaderHeight + 3*行高 + 间距` ≈ 122px），空态下面挂着两大块空白；
- `BuildPartyMember(...)` 再没有任何调用方，配套的 `HudPartyRow` 内嵌类、6 个队友名字/等级常量、三个成员数组字段全是死代码。

**修法**：`PartyHeight` 改成 `PartyHeaderHeight + PartyRowGap + 24f`（正好等于"头部 + 一行空态"），删掉 `BuildPartyMember`、`HudPartyRow`、6 个队员常量、3 个数组字段及其分配、以及只被死代码用到的 6 个尺寸常量（净删约 4.7 KB 源码）。buff 面板顶部用 `Mathf.Max(BuffPanelTop, …)`，面板变矮后仍落在 256，不会上移错位。

**验证**：离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 02:37:18 重建）；重启编辑器后 PIE（`combat-hud-v2`）`ui_tree` 实读队伍面板容器 `ContainerControl x=1676 y=24 w=220 h=48 visible:true`（改前高度约 122），空态标签 24px 恰好落在面板内。

**边界**：该面板在设计坐标 x=1676，落在本机 Game 视图被裁的右列，**截图看不到**，所以这一条用 `ui_tree` 的尺寸/可见性取证；若要目视，需要更大的游戏视口。

## 171. 中文标签里的冒号混用半角（2026-10-30）

全仓扫「中文文案里用 ASCII `:`」：多人页顶栏 `网络延迟:` / `服务器: `（同页其它文案、其它页的服务器行都是全角 `：`），迷你游戏结算 `最高记录: `（两处），奖励页 `总价值:`。中文排版里半角冒号会显得挤、且与相邻全角标点不一致。

**修法**：这 5 处统一改成全角 `：`。**时间格式不动**（`剩余 03:42`、`每日 05:00 刷新` 属于数值分隔，必须保持 ASCII）。

**验证**：离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 02:47:37 重建）；重启编辑器后 PIE（`nav-multiplayer`）抓图实读「网络延迟…　服务器：未知」，服务器行冒号为全角；复扫 `服务器: ` / `网络延迟:` / `最高记录:` / `总价值:` 已 0 命中。

**顺带确认**：V2 HUD 的「状态效果」面板默认 `Visible=false`、连击面板同样默认隐藏并由 `_comboCount` 驱动（`_buffPanel.Visible=false` / `_comboPanel.Visible=false`），不存在"没数据却摆假 buff/假连击"的问题。

## 172. 本段（§148–§171）汇总与验证状态（2026-10-30 深夜，交接用）

**改掉的界面问题（按性质分）**
1. **登录/认证**：回退路径把全屏登录页钉成 480×297（§148）→ 改为拉伸铺满；`找回密码` 死交互 → 如实提示（§150）；`自动登录` 勾选框无任何读取点 → 禁用并改文案（§154）；服务器行/圆点跟随真实 `ConnectionStatus`（§152）。
2. **"未接入"页的诚实化**：通行证赛季初值补提示（§159）、成就样例卡片补提示 + 页脚 `+7` 改 `--`（§158）、祈愿页保底/记录/价格改占位 + 隐藏折扣徽章（§163）、背包「丢弃」删不可达回调（§163）。
3. **真实数据下的残留样例**：装备页真实快照仍显示 `10/24`、对比面板按等级编属性 → 改为真实件数与「单项属性尚未接入」（§156）。
4. **状态文案自相矛盾**：`服务器: 未连接` 与 15ms 实时延迟并排 → 区分"无网关/无区域名"（§153、§157）；中文标签半角冒号统一（§171）。
5. **结构/布局**：V2 队伍空态面板从 3 行高度收到 1 行并删死代码（§170）；制造页死按钮置灰、区头计数跟随真实数量（§151）。

**新增/沿用的验证通道**
| 通道 | 工具/方法 | 本轮覆盖 |
| --- | --- | --- |
| 结构体检 | `.compile-temp/page_audit.py` | 45 页 ×2 轮全 PASS（§161/§162/§165） |
| 数据落界面 | `Update + Revision + Apply*` 普查 | 8 个服务消费点逐条核对（§166） |
| 行为夹具 | `friends-selftest` / `photo-selftest` / `album-preview` / `ui-autohide-test` | 4 组全部通过（§167–§169） |
| 像素证据 | `window_capture.ps1`（PrintWindow） | 登录/设置/活动/成就/通行证/装备/祈愿/多人/秘境等 |
| 编译门 | 离线 `csc @verify-check8.response` + 编辑器构建 | 每次改动 0 error / success |

**仍然不懂的/做不到的**
- 真实鼠标点击（列表选中后的对比面板、领取 toast、拖拽技能栏）桥工具做不到；
- 服务端空壳页拿到真实数据后的表现未验（当前只验空态与明示）；
- 本机 Game 视图裁右列，设计坐标 x≳1100 的控件只能靠 `ui_tree` 证存在；
- 环境侧：编辑器启动偶发 `AssetsCache` 崩溃、热重载在 `xcopy` 失败时不换装配（§149/§151 已记录处置）。

## 173. 组队页「队伍设置」只改高亮、却像会保存（2026-10-30）

`MenuTeamPage` 右侧「队伍设置」里的 3 个队伍类型、3 个分配方式、自动接受开关、最低等级滑块，回调分别只做两件事：改自己的高亮色 + 写 `Debug.Log`（`OnTeamTypeTabClicked` / `OnLootTabClicked` / `OnAutoAcceptToggleClicked` / `OnMinLevelSliderClicked`），既不落库也没有服务端通道——典型的"只改高亮"假交互。页面左侧虽然有「组队功能尚未接入服务端」，但没有说明**这些设置本身也不会保存**。

**修法**（选最小且不破坏布局的一种）：左侧空态文案改成 **「暂无队友 · 组队功能与队伍设置均未接入服务端」**，一句话把两块都交代清楚；右侧控件的本地高亮行为保留（至少点击有反馈，不会像死按钮）。

**验证**：离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 02:59:10 重建）；重启编辑器后 PIE（`nav-team`）抓图实读该行完整文案，未截断（标签宽 = 成员区宽度，足够容纳）。

**同轮顺带核对**：任务页在真实快照下显示「暂无任务记录 / 0 项 / 未选择任务」且`RequestList()` 确实在打开时发出（`MenuQuestsPage` 第 1055 行）——空态来自服务端"确实没有任务"，不是页面没请求；死亡界面无假冷却、`ReviveRequested/ReturnRequested` 在 `MainUIManager` 有订阅方。

## 174. 最终构建第三次全量巡检：45 页仍全 PASS（2026-10-30）

在 §170（V2 队伍面板收高）/§171（冒号统一）/§173（组队页文案）之后的构建（`Game.CSharp.dll` 02:59:10）上，第三次跑 45 页闸门：**三批全部 PASS**，节点数与上两轮一致（`nav-team 355`、`nav-gacha 367`、两套 HUD 427/347、`cc-*` 138–498 等）。

至此本段共做了 **三次同尺度全量巡检**（§162 改动中、§165 改动后、§174 最终），都是 0 FAIL / 0 零尺寸交互控件 / 0 跨页越界；说明这 20 余处 UI 改动都没有引入页面级回归。

**最终门状态**：离线 `csc` 0 error；编辑器最后一次构建 success（02:59:10）；PIE 已停；`%APPDATA%/HundunWorld` 仅剩 `settings.json`、`ui-selftest.out`、`photo-selftest.out`、`sculpt-probe.txt`（无任何 `.on` / `open-page.txt` 自测标记）。

**还差什么（下一段可接）**
1. 服务端真实数据到位后的页面回归（任务/组队/好友/邮件/排行/成就服务）；
2. 真实鼠标路径：列表选中→对比面板、领取 toast、技能栏拖拽；
3. `page_audit.py` 接到每次改动后的固定流程（目前是手动三批）。

## 175. 「只改高亮」扫描收口 + 排行榜空态复核（2026-10-30）

写脚本扫所有 `On*Clicked` 回调体：**既不含 `Service/Navigate/Requested/Save/Async/Request/Apply/Commit/Set*/Try` 等动作词、又出现 `BackgroundColor/BorderColor` 赋值**的方法。全仓只剩 3 处，都在 `MenuTeamPage`（队伍类型 / 分配方式 / 自动接受），已由 §173 的空态文案覆盖说明——这一类到此收口，没有第二页还在悄悄"只改高亮"。

**排行榜页复核（负结果，不用改）**：`LeaderboardPage._rankRows` 是空数组、没有填充路径，但表格底部有 `暂无排行数据 · 接入服务端后显示` 的空态文案（第 891 行），三甲区与本榜/我的排名都用 `—` 占位——不存在"空表却没有任何说明"的问题。

## 176. 本段改动文件清单与提交注意（2026-10-30，交接用）

**本段实际改过的文件（16 个 + 文档）**

| 文件 | 对应小节 |
| --- | --- |
| `UI/Authentication/AuthenticationUI.cs` | §148 登录回退路径铺满 |
| `UI/Authentication/LoginPanel.cs` | §150/§152/§154 找回密码、服务器状态行、自动登录禁用 |
| `UI/Ink/Components/InkPlayerHeaderBinder.cs` | §153/§157 网关文案兜底（未连接/未知区域/未测得） |
| `UI/Ink/Pages/Livelihood/MenuLivelihoodPage.cs` | §152 页脚服务器/延迟/版本 |
| `UI/Ink/Pages/MartialRecord/MenuMartialRecordPage.cs` | §153 页脚初值与占位 |
| `UI/Ink/Pages/Multiplayer/MultiplayerPage.cs` | §153/§157/§171 延迟列、区域名兜底、冒号 |
| `UI/Ink/Pages/Time/MenuTimePage.cs` | §153 底栏延迟 |
| `UI/Ink/Pages/Character/MenuEquipmentPage.cs` | §156 真实件数、对比属性 |
| `UI/Ink/Pages/Character/CharacterPanelPage.cs` | §163 删「丢弃」不可达回调 |
| `UI/Ink/Pages/Crafting/CraftingPage.cs` | §151 搜索按钮与配方计数 |
| `UI/Ink/Pages/Activities/MenuActivitiesPage.cs` | §155 版本号 |
| `UI/Ink/Pages/Social/AchievementPage.cs` | §158 示例提示与页脚占位 |
| `UI/Ink/Pages/BattlePass/MenuBattlePassPage.cs` | §159 赛季示例提示 |
| `UI/Ink/Pages/Gacha/MenuGachaPage.cs` | §163 保底/记录/价格/折扣徽章 |
| `UI/Ink/Pages/Team/MenuTeamPage.cs` | §173 队伍设置未接入说明 |
| `UI/Ink/Pages/Combat/CombatHudV2Page.cs` | §170 队伍空态面板收高 + 删死代码 |
| `UI/Ink/Pages/MenuShopPage.cs` | §155 注释里的过期版本号 |
| `docs/UI-DESIGN-DATA-BINDING-20260923.md` | §148–§176 |

**提交注意（重要）**：`HundunWorld/Source/Game/UI/Ink/Components/InkPlayerHeaderBinder.cs` 目前是**未跟踪文件**（`git status` 显示 `??`，`git ls-files` 查不到），但营生/武录/多人/时辰四页都 `using` 它——提交时**必须一并 `git add`**，否则按已跟踪文件提交会编译不过。它不是本段新建的（改动前就已存在），只是此前一直没入库。

## 177. UI 目录有 16 个 `.cs` 从未入库（2026-10-30）

用 `.compile-temp/untracked_ui_deps.py` 把"未跟踪的 UI 源文件"与"是否被已跟踪文件引用"对齐，结果：

```
untracked UI .cs = 16   (全仓未跟踪 .cs = 554)
REF  Ink/Components/ControlStateChipView.cs
REF  Ink/Components/InkChatChannelView.cs
REF  Ink/Components/InkPlayerHeaderBinder.cs
REF  Ink/Components/InkSidebarList.cs
REF  Ink/Components/WhisperRequest.cs
REF  Ink/InkSidebarNav.cs
REF  Ink/InkWorldInputGate.cs
REF  Ink/Pages/Character/SkillTreePage.cs
REF  Ink/Pages/CharacterCreation/CcFacePresets.cs
REF  Ink/Pages/CharacterCreation/CcSession.cs
REF  Ink/Pages/CharacterCreation/MetaHumanSculptPanel.cs
REF  Ink/Pages/NpcGossipPage.cs
REF  Ink/Pages/PhotoMode/PhotoAlbumPage.cs
REF  Ink/Pages/SystemMenuPage.cs
REF  UIMouseCapture.cs
---  Ink/InkPageFrame.cs   （枚举，脚本按 class 名没匹配到，实际被 InkPageFrame.Immersive 使用）
```

**这意味着**：当前本地能编译、能在 PIE 里跑通 45 页，是因为这些文件**在盘上**；一旦"只提交已跟踪文件"，干净检出会直接编译失败（缺类型）。**没有任何一个是我这段新建的**——它们在本段开始前就在工作区里、只是从未 `git add`。

**建议动作（未执行，等确认）**：提交本段 UI 改动前，先 `git add` 上述 16 个文件（共约 4 千行），再一起提交；脚本可随时重跑复查：`python .compile-temp/untracked_ui_deps.py`。

**判据说明**：脚本用 `class XXX` 反查已跟踪源码里的引用；`InkPageFrame.cs` 是枚举所以显示 `---`，属假阴性，需人工确认这类非 `class` 文件（enum/struct/interface）也要一并入库。

## 178. 组队页「搜索」输入能打字、按钮点了只打日志（2026-10-30）

`MenuTeamPage` 邀请面板里的搜索框与「搜索」按钮：`OnSearchClicked` 只写一行 `Debug.Log`，既过滤不了列表也没有服务端通道——玩家能输入、能点，界面毫无变化。

**修法**：输入框加 `WatermarkText = "组队功能未接入，暂不可搜索"`（灰字），搜索按钮 `Enabled = false`；并**删掉那个不可达的点击回调与 `OnSearchClicked` 方法**（与 §163 背包「丢弃」同一口径：禁用态收不到点击，挂着就是死代码）。

**验证**：离线 `csc` 0 error；编辑器构建 success（`Game.CSharp.dll` 03:18:15 重建）；重启编辑器后 PIE（`nav-team`）抓图实读输入框灰字「组队功能未接入，暂不可搜索」，下方「暂无可邀请的队友 · 组队功能…」仍在。

**边界**：「搜索」按钮位于设计坐标最右列，被本机 Game 视图裁掉，截图看不到它的禁用态（代码与构建可证）。

## 179. 一条命令的 UI 自检门 `.compile-temp/ui_gate.py`（2026-10-30）

把两件必须同时成立的事合成一条命令：

```
python .compile-temp/ui_gate.py
[1/2] compile gate: PASS  (error CS = 0)
[2/2] untracked UI deps: FAIL  (referenced-but-untracked = 15)
   REF  Ink/Components/ControlStateChipView.cs ...
```

- **第 1 关**：用 `csc @verify-check8.response` 离线编 496 个源文件，只认 `error CS` 条数；
- **第 2 关**：调 `untracked_ui_deps.py`，列出"未跟踪但被已跟踪源码引用"的 UI 文件——**当前 15 个 REF，意味着仓库还没法干净检出编译**（见 §177）；把它们 `git add` 之后这一关才会转 PASS。
- 输出保持 **ASCII**（`PASS/FAIL`、`error CS`、`referenced-but-untracked`），规避 Windows GBK 控制台把中文标签后的数字吞掉的问题；退出码非 0 即视为不通过，便于接进脚本或 CI。

**状态**：第 1 关长期应保持 PASS；第 2 关当前是**有意的红**，它指出的正是需要先入库的那批文件，不是回归。

## 180. 最后一次改动后的复核（2026-10-30）

§178（组队页搜索框禁用 + 删不可达回调）发生在 §174 全量巡检之后，因此单独补跑：

```
== PASS nav-team nodes=355 pages=['MenuTeamPage']
== PASS nav-gacha nodes=367 pages=['MenuGachaPage']
```

两页结构量级与全量巡检时一致；随后 `sim_stop`、清理 `%APPDATA%/HundunWorld` 的 `.on` 与 `open-page.txt`，最终只剩 `settings.json`、`ui-selftest.out`、`photo-selftest.out`、`sculpt-probe.txt`。

**最终门状态**：`ui_gate.py` 第 1 关 PASS（0 error），第 2 关仍红（15 个未入库 UI 源文件，见 §177/§179）；文档共 175 个小节、编号无重复。

## 181. 基础背包格数 36/50 → 108，且保持可扩展（2026-10-30）

需求：基础背包 108 格，可继续扩展（参考魔兽世界"基础背包 + 扩展背包"）。

**改动（权威值，非界面假数）**
- `Services/PlayerInventoryService.cs`：新增 `public const int BaseCapacity = 108;`，`Capacity` 默认值由 `50` 改为 `BaseCapacity`。服务端快照里的 `capacity` 仍会覆盖它（§该文件 `ApplyInventorySnapshot`），所以"扩展背包/扩容"走服务端下发即可生效，客户端不再把 50 写死。
- `UI/Ink/Components/InkBackpackGrid.cs`：`DefaultCapacity` 由 `36`（6×6）改为 `108`；该组件的扩容模型不变——`总容量 = DefaultCapacity + Σ(已装备扩展背包的 ExtraSlots)`，扩展格索引 `≥ DefaultCapacity` 仍与基础格做视觉区分。

**验证**：离线 `csc` 0 error（`ui_gate.py` 第 1 关 PASS）。

**边界（未做，下一步）**：
1. ~~角色面板的背包格子仍是固定的 8×5=40 格展示~~ → 已在 §183 解决（分页 + 扩展背包槽行）。
2. `InkBackpackGrid`（带扩展背包槽的组件）当前没有被任何页面实例化（全仓只有自身引用），若要按 WoW 口径做"4 个扩展背包槽 + 扩容"，应先把它接进角色/背包页。

## 183. 背包截断修复：40 格分页 + 4 个扩展背包槽（2026-11-01）

**改动**（`UI/Ink/Pages/Character/CharacterPanelPage.cs`）
- 背包容量改为**分页驱动**：`InitInvCellData(page)` 先按筛选收集**全部**物品，再取当前页切片（8×5=40/页），不再"超过 40 件直接截断"。页数 = `ceil(容量 / 40)`，容量取服务端快照（未收到时用 `PlayerInventoryService.BaseCapacity` = 108），所以 108 格 = 3 页。
- 页脚新增 `‹ 页码 ›` 三个控件（`SetInvPage`），切页时复用现有 40 个格子控件只换数据（`InvCell.SetData`），并清空选中、刷新详情按钮；容量条宽度相应缩短，容量文案显示 `x/108`。
- 行囊卡片在筛选行与网格之间新增**扩展背包槽行**：`扩展背包 [空][空][空][空] 未扩展|扩容 +N 格`，`N = 容量 - BaseCapacity`——即扩容完全由服务端容量驱动；槽位内容（装了哪个背包）要等协议下发，当前如实显示"空"。

**同时修掉一个假绿闸门**：`.compile-temp/ui_gate.py` 原来用 `verify-check8.response`，它**没有编到本次改动的文件**（`ItemInfo` 类型错误没报出来），编辑器启动编译却直接失败（`Cannot load scene without game modules loaded`）。现在第 1 关改用编辑器自己的 `Cache/Intermediate/GameEditorTarget/.../Game.CSharp.response` 并带 `MSYS_NO_PATHCONV=1`，与真实构建同源；修正后 `ItemInfo` 需写成 `Horizon.Game.Message.Network.ItemInfo`（页内其它方法也是这么写的）。

**验证**：
- 编辑器级离线编译 0 error（用编辑器 response 复跑）；编辑器启动不再报 `Cannot load scene`。
- PIE（`nav-character-panel`）实读：行囊卡片里出现「扩展背包 [空][空][空][空] 未扩展」整行，卡片随之下移、无重叠；网格与页脚的页码/按钮位于视口下缘（需要滚动才完整可见）。
- **运行时证据**（`Log_2026_11_01_23_14_09.txt`，首次构建背包时打印一次）：
  ```
  [InvPaging] capacity=108 pages=3 page=1 filtered=0  shown=0     ← 快照未到
  [InvPaging] capacity=108 pages=3 page=1 filtered=62 shown=40    ← 快照到达
  ```
  62 件可显示物品 > 单页 40 —— 改前会被截断 22 件，现在按 3 页承载；容量恒为 108（服务端没给更大值时取 `BaseCapacity`）。
- **边界**：翻页与"扩容 +N"需要点击/服务端给更大容量才能复核；本机 Game 视图视口高度不足，截图只到背包网格上沿。
