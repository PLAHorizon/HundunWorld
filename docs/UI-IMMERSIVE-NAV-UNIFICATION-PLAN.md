# 界面沉浸式全屏化 + 导航统一（已落地 · 含验收清单）

> ## 现状速览（2026-10-07 收尾）
>
> **落地状态**：实现已全部完成（S1–S8），未提交（工作树里的改动）。
> **跑这两个就够**：`python scripts/verify-ink-page-nav.py`（15 条不变量，PASS）＋ `dotnet build HundunWorld/Source/Game.csproj -c Editor.Windows.Development -p:Platform=x64`（0 CS error，需在仓库根执行）。
> **已取证**：Hud / Immersive / Modal 三形态的外壳层可见性、叠加层、返回栈、光标门控（marker 日志 + `ui_tree`）。
> **未验两项（都不在实现层）**：① S7/S8 的运行态观测（环境不允许：新起播放 RootScene+World 实测 3/3 让编辑器退出）；② 像素观感（本环境截不出 UI，必须人眼）。
> **你的下一步**：按文末「交付验收清单」8 条走一遍，通过即视为验收；不符就给编号+页面。
> **顺带待决**：`HundunWorld/Content` 下 108 个已删资产（含 `Character_1.flax`）导致世界场景无角色，疑似也是编辑器退出的诱因，去留待定。

> 目标：**除游戏主界面（战斗 HUD）外，其余所有 UI 统一为沉浸式全屏页，并把所有 UI 导航收敛到一条通道。**

> 范围排除：战斗 HUD（`combat-hud` / `combat-hud-v2` / `combat-hud-traditional`）保持现状（透明叠加层，不铺底、不显示外壳返回）。
> 四条口径已确认并落地：弹窗族「展示类全屏 / 确认类模态」、页面自绘底全部收归外壳、引入返回栈、重复入口合并删除；非 Ink 遗留栈（登录/旧选角/常驻部件）按裁定排除。本文既是方案留档，也是接手与验收说明。

---

## 六、侧边栏统一（2026-10-07 续作）

**需求**：侧边栏要能到达"除弹框与事件 UI 之外的所有 UI"。

**做法**：新增 `Ink/InkSidebarNav.cs`（唯一清单：36 项 / 4 组：世界·成长·社交·系统，含可选角标）+ `Ink/Components/InkSidebarList.cs`（共用渲染：分组标题、激活高亮、悬停/按下反馈、**溢出滚轮滚动**、细滚动条）。各页面不再自维护侧边栏数据。

**已接入 8 页**：商店 `MenuShopPage`、任务 `MenuQuestsPage`、设置 `SettingsPage`、活动 `MenuActivitiesPage`、组队 `MenuTeamPage`、门派 `MenuSectPage`、博物志 `MenuBestiaryPage`、战令 `MenuBattlePassPage`（战令"热"角标改由清单条目承载）。
**待接入**：外观 `MenuAppearancePage`（该文件正被并行会话编辑，本轮刻意未动）；`UIGalleryPage` 为开发页永久豁免。

**顺带清掉的死导航（本次的真收获）**：`MenuTeamPage` 与 `MenuSectPage` 的侧边栏原来传的是 `"menu-quests"`/`"menu-team"`/`"settings"` 这类**未注册字面量**（真实 id 是 `nav-*`），**20 个入口点了完全没反应**；另 `SocialGuildPage` 有一个 `tabDomIds` 数组喂给从未使用的局部变量（死代码）。合计 24 处，现已清零。

**闸门新增三条（A–R 共 18 条）**：
- **P** 侧边栏清单必须覆盖所有"功能页"（Immersive 且非流程/事件/开发页）→ 36 项 / 0 遗漏；
- **Q** 有侧边栏的页面必须用共用 `InkSidebarList`（待接入名单显式列出）→ 0 违规；
- **R** 页面里不得出现"像 dom-id 但未注册"的字面量（就是这条抓出了上面 20 个死入口）→ 0 违规。

**任务入口合并（2026-10-07，用户要求"以任务样式为基准"）**：`nav-quest-log` 与 `nav-quests` 合并为同一页 —— 两个 id 都指向 `MenuQuestsPage`（即"任务"页，卷宗式），原 `QuestLogPage` 及其工厂、`Pages/Quest/` 命名空间内的唯一文件一并删除；侧边栏只保留「任务」一项；战斗 HUD 导航栏的「任务」按钮也从 `NavQuestLog` 改指正名 `NavQuests`。`nav-quest-log` 作为兼容别名保留（老链接着陆点不变），并加入闸门 P 的别名排除表。

**任务内容样式回补（2026-10-07，用户要求"任务内容使用原来任务日志中的任务内容样式"）**：合并页右侧那块"任务背景"从本页原本的亮纸卷（`InkPaperPanel` + `TextOnPaper`）改为原任务日志页描述卷轴的画法 —— 页内新增私有 `QuestScrollPanel`：`BaseTertiary` @0.50 圆角(4) 底 + `BorderFaint` 发丝边 + 四角 8px 金色 L 饰（`GoldPrimary` @0.35），小标题改 `GoldPrimary`、正文改 `TextDefault` 13px Body。外壳/侧边栏/标签页仍保持"任务"页原样，只换这一块的内容呈现。

**「江湖结社」→「结伴江湖」并接入真聊天（2026-10-07）**：侧边栏条目与页内标题统一改为「结伴江湖」（`InkSidebarNav.cs`、`SocialGuildPage` 顶栏，Tab 起始 x 由 140 移到 200 让位给四字标题）。该页右栏聊天窗口原来是写死的九条假消息 + 一个只画占位符的假输入框，现在接的是主界面左下聊天框同一份数据：
- 新增 `UI/Ink/Components/InkChatChannelView.cs` —— 频道 Tab 顺序 / 中文标签 / 行内前缀与颜色的唯一来源；主界面聊天框的 `ChatTabChannels`、`ChatChannelLabel`、`ChatChannelStyle` 改为引用它，两处不会再各叫各的。
- `SocialGuildPage`：频道 Tab 可点（点选只看该频道、再点回全部，语义与聊天框一致）；消息区 `ChatArea` 裁剪 + 滚轮滚动 + 到顶按 3s 节流 `RequestHistory`；输入框换成真 `TextBox`，Enter 或「发送」走 `ChatClient.Send`（密聊视图发给该玩家，否则发给所选频道，未选时发世界）；点他人发言行进入密聊，输入行上有 ✕ 退出；正在看的频道 `MarkRead`，回主界面红点不会空亮。同步沿用聊天框的 0.4s 节流，在页面 `Update` 里主线程读缓存（网络线程只写缓存）。
- 顺带修了本页 `DBox.Draw` 的绘制顺序：原来 `base.Draw()`（画子控件）在前、铺底色在后，等于用底色盖住自己的子内容；改为铺底→画子→压边框，并把 `BackgroundColor` 置透明。玩家卡与左右信息卡同受此修复影响。

## 一、现状证据（都已按 file:line 核实）

### 1.1 基础设施其实已经具备"沉浸式全屏"的骨架

`InkPageShell`（`HundunWorld/Source/Game/UI/Ink/InkPageShell.cs`）已经是五层外壳：

| 层 | 控件 | z | 作用 |
| --- | --- | --- | --- |
| 背景层 | `InkBackgroundLayer` | 0 | 全屏墨底（`ScrimOpacity=0.72`、`DrawBaseFill=false`，允许约 28% 场景透出） |
| 暗角层 | `InkVignette` | 1 | 边缘晕影 |
| 内容层 | `ContainerControl` | 2 | 页面唯一挂载点，`ClipChildren=true`，铺满设计空间 1920×1080 |
| 粒子层 | `InkParticleSystem` | 5 | 墨韵动效，不接收鼠标 |
| 返回按钮 | `InkBackButton` | 10 | 左上角 16,16 统一返回入口 |

页面挂载是强制的：`InkPageRouter.NavigateTo` → `_shell.LoadPage(page)`（`InkPageRouter.cs:525`），`LoadPage` 先 `UnloadPage()` 销毁上一个页面并把新页面设成 `StretchAll`（`InkPageShell.cs:209-232`、`:258-275`）。即**同一时刻只有一个页面存在**，页面每次导航都由工厂重建。

### 1.2 但"哪些层给哪些页"是用两张手写白名单 + 字符串前缀决定的

`InkPageRouter.cs:518-542`：

```csharp
bool isHud = domId == _hudDomId;
bool isOverlayPopup = domId.StartsWith("popup-", StringComparison.Ordinal);
bool selfDressed = SelfDressedPages.Contains(domId);          // 5 个 cc-*
bool suppressShellBack = selfDressed || ShellBackSuppressedPages.Contains(domId); // 6 个
_shell.ShowBackButton(!isHud && !suppressShellBack);
_shell.ShowBackgroundLayer(!isHud && !isOverlayPopup);
_shell.ShowVignette(!isHud && !isOverlayPopup && !selfDressed);
```

后果（本仓实测）：

- **双重压暗**：`isOverlayPopup` 只认 `popup-` 前缀，而同样自铺全屏 `Scrim` 的 `reward-*`、`guide-action`、`guide-martial`、`mini-game-complete`、`DeathScreen`、`Qte`、`dialogue-confirm` 不在名单里 → 外壳背景层/暗角**叠在**页面自己的全屏遮罩上。
- **6 个页面把外壳返回按钮关掉、自己画一个**（`ShellBackSuppressedPages:353-361`），于是"返回"位置/样式各页不同。
- **5 个创角页自铺雾白舞台**（`SelfDressedPages:367-374`），不走外壳视觉。
- 名单是手工维护的，新增页面漏加就会出现遮挡/重复压暗。

### 1.3 页面内部的"沉浸感"各写各的

65 个页面文件里：

| 现象 | 文件数 | 代表 |
| --- | --- | --- |
| 自绘全屏 `Scrim`/`Void` 底 | **22** | `PopupPages.cs`、`RewardPages.cs`、`CharacterPanelPage.cs:164`、`MenuShopPage.cs`、`QuestLogPage.cs` … |
| 自绘 `InkBackgroundLayer`/`InkVignette` | **10** | `MenuActivitiesPage`、`MenuGachaPage`、`MenuMailPage`、`PhotoModePage` … |
| 自绘返回按钮 | **8** | 与上表 1.2 的 6 个抑制页 + `SocialMailPage`/`SocialShopPage` |
| 引用设计空间常量 `UILayout.RefWidth/Height` | **仅 3** | 其余硬编码像素（同样是 1920×1080 口径，但不经由常量） |

`MenuCharAttributesPage`（V1）/`MenuCharAttributesV2Page`/`CharacterPanelPage` 三套角色页、`MenuMailPage`（nav-mail）与 `SocialMailPage`（nav-social-mail）两套邮件、`MenuShopPage`（nav-shop）与 `SocialShopPage`（nav-social-shop）两套商店**同时注册且都在导航表里**（`MainUIManager.cs:1025/1049/1032/1084/1101/1105`）。

### 1.4 导航：星型、无返回栈、触发方式五种、Esc 无主

- `InkPageRouter` 只有 `_currentPageDomId` 一个状态；`_pageCache`（`:338`）声明后从未写入；**没有返回栈**——`OnBackButtonClicked`（`:593`）永远 `NavigateToHud()`。
- 页面发起跳转有五种写法：反射绑定 `NavigationRequested`（`MainUIManager.HookNavigationRequested:3115`）、工厂里直接订阅事件、每页自定义事件（`BackRequested`/`Confirmed`/`Claimed`…）、`MainUIManager.OnUpdate` 里的快捷键、场景切换驱动。
- 硬编码 dom-id 字符串绕过 `InkPageDomIds` 常量：`MenuShopPage.cs:1106/1151/1180`、`MenuTeamPage.cs:928`、`MenuSectPage.cs:1076`、`FriendsPage.cs:163`、`AcupointPage.cs:876`、`UIGalleryPage.cs:112+`。
- Esc 处理散在 ≥5 处（`ExitOnEsc`、`CcStagePage.cs:264`、`InkActionBarPicker.cs:120`、`ActionBarDrag.cs:108`、`CharacterSceneController.cs:103`），**路由/外壳层没有 Esc**。
- "返回沉浸模式"只是 2 个页面的按钮文案（`SocialMailPage.cs:1081`、`SocialShopPage.cs:674`），目标都是 HUD，但其它页面叫"← 返回战场"/"返回"/"✕"。
- **零测试覆盖**：`Source/Game/Tests` 只有 `SpringArmCameraTest.cs`，没有任何页面/路由断言。

### 1.5 设计口径（仓库内既有文档，作为本方案的约束）

- 星型拓扑：`game-ui-system/ui-architecture-spec.md:36`（每个二级页左上角 `back-hud`，回退一律回战斗 HUD）。
- 面板即覆盖：`ui-architecture-spec.md:79`（全屏覆盖、同一时刻一个面板、关闭 = 回战场）。
- 单一外壳：`hundun-ink-wash-ui/generation-tree.json:17`（`global-ink-background / immersive-frame / vignette-overlay`）、`hundun-yy-ui/orchestration-summary.json:23`（各页左上角 `ink-back-btn` 统一返回）。
- 弹窗规则：`.trae/specs/beautify-mainui-scene-friendly/spec.md:65-70` + `InkBackgrounds.cs:213/219/280`：菜单页不铺不透明底，用 `Scrim 0.72` + 径向光晕，暗角柔和；**弹窗浮层禁用全屏压暗与暗角**（既有决议）。

---

## 二、目标架构

### A. 页面框架单一来源（沉浸一致的根）

外壳继续做唯一的框架提供者，**页面内自绘的全屏底/暗角/返回入口全部删除**，改为向外壳声明自己的呈现类型，`InkPageRouter` 不再靠字符串前缀与手写 HashSet：

```csharp
public interface IInkPage
{
    void RefreshLayout();
    InkPageFrame Frame { get; }   // 新增：呈现描述符
}

public readonly struct InkPageFrame
{
    public InkPageKind Kind;      // Hud | Immersive | Modal
    public bool OwnBackButton;    // 少数有专属返回位的页面（如创角阶段流）显式声明
}
```

- `Kind = Hud` → 不铺底、不暗角、不返回（仅 3 个战斗 HUD）。
- `Kind = Immersive` → 外壳铺墨底 + 柔暗角 + 左上角统一返回。**所有功能页**（角色/背包/技能/地图/任务/社交/商店/邮件/活动/图鉴/战令/外观/生活/成就/师徒/排行榜/相册…）都是这一档。
- `Kind = Modal` → 由外壳的**弹层层**承载（见 C），不铺全屏压暗（遵守既有决议），仅面板自身带投影/轻遮罩；不做整屏暗角。

### B. 统一页内骨架

外壳补一个轻量页骨架（顶栏 / 侧栏 / 内容区 / 滚动容器），把 `InkScrollArea`(CharacterPanelPage:2818)、`InkScrollBox`(QuestLogPage:1212) 这类各写一份的滚动实现收敛为一处；设计空间一律走 `UILayout.RefWidth/Height`，禁止再写字面量 1920/1080。

### C. 弹层从"页面"升级为"外壳叠加层"

现在弹窗是**页面**：导航过去会把底页 `Dispose` 掉（`UnloadPage`），所以"在角色面板上弹一个确认框，关掉后角色面板就没了"。目标态：外壳新增 overlay 层（z=3，位于内容层之上、返回按钮之下），弹窗叠在任意页面之上，关闭后**底页仍在**。这同时解决 1.2 的双重压暗（弹层不吃外壳背景/暗角）。

### D. 导航统一

1. 单一触发契约：页面只通过 `NavigationRequested(domId)`（`IInkPage` 成员）发起跳转，删掉反射绑定；工厂里的一次性事件订阅保留在 `MainUIManager`，但入口收敛为一个 `NavigateToPage(domId)`。
2. 真实返回栈：`NavigateTo(domId, pushHistory=true)` / `Back()`（弹栈回来源页，栈空回 HUD）；外壳返回按钮与 Esc 都调 `Back()`。
3. Esc 收归一处：`MainUIManager.OnUpdate` 统一处理（HUD 页不响应；Modal 页先关弹层；其余 `Back()`），删掉页内的散装 Esc。
4. 常量与去重：dom-id 一律用 `InkPageDomIds`；两套邮件/两套商店/三套角色页按决策合并。
5. 闸门：新增离线自检（harness 或 EditMode 式断言）——(a) 所有被当作导航目标的 dom-id 都有对应 `RegisterPage`；(b) 每个注册页面都实现了 `IInkPage` 且声明了 `Frame`；(c) 无页面自绘全屏 `Scrim`/`InkVignette`（源码扫描）；(d) 无硬编码 dom-id 字面量。这样"再也不会漏加白名单"。

---

## 三、迁移切片（每片独立可验，逐片截图走查）

| 片 | 内容 | 验证方式 | 预估改动 |
| --- | --- | --- | --- |
| **S1** | 引入 `IInkPage.Frame` + 路由据 Kind 决策；外壳加 overlay 层与页骨架；加导航闸门(a)(b) | 编译 + 闸门绿 + PIE 逐页走查无视觉变化 | 路由/外壳 ~3 文件 + 79 页加 `Frame` 声明 |
| **S2** | 功能页"去自绘底"：22 个自绘 Scrim/Void 页 + 10 个自绘 bg/vignette 页改为只画内容 | 逐页截图比对（允许 28% 场景透出） | ~30 页，机械替换 |
| **S3** | 弹层族迁移（`popup-*`、`reward-*`、`guide-*`、`dialogue-confirm`、`qte`、`death-screen`、`mini-game-complete`）→ 走 overlay 层，底页不销毁，去全屏压暗 | 每类弹层截图 + "底页仍在"断言 | ~15 页 |
| **S4** | 导航清理：返回栈 + 集中 Esc + 常量替换 + 重复入口合并 + 闸门(c)(d) | 闸门绿 + 逐页返回/Esc 走查 | 路由 + ~10 页 |

风险与回退：S1 是纯新增（旧路径保留开关，可随时回退到"前缀+白名单"判定）；S2/S3 按页提交，出问题单页 `git checkout --` 回退；S4 的返回栈若与预期不符，把 `Back()` 退回 `NavigateToHud()` 即可恢复星型。

---

## 四、已确认口径（2026-10-06 用户拍板）

1. **弹窗族**：展示类（奖励/成就/小游戏完成等）→ 全屏沉浸页；确认类弹窗（物品获得/消息/校验/详情/对话确认）与 QTE、死亡屏 → 保持模态。
2. **页面自绘底**：全部收归外壳——页面内自绘的全屏 `Scrim`/`Void`/`InkVignette`/`InkBackgroundLayer` 一律删除。
3. **返回语义**：引入真实返回栈（返回/Esc 回来源页，栈空回战斗 HUD）。
4. **重复入口**：合并删除（邮件留 Social 版、商店留 MenuShop 版、角色页留 CharacterPanelPage）。

## 五、进度

### S1 框架描述符 + 返回栈 + 集中 Esc —— ✅ 已完成（编译 0 error）

- 新增 `HundunWorld/Source/Game/UI/Ink/InkPageFrame.cs`：`InkPageKind{Hud,Immersive,Modal}` + `InkPageFrame{Kind,OwnBack,SelfLitStage}` 与 6 个预设。
- `InkPageRouter`：`RegisterPage` 增加框架参数并新增 `_pageFrames` 表；**删除** `ShellBackSuppressedPages`/`SelfDressedPages` 两张白名单与 `popup-` 前缀判定；新增 `_history` 返回栈、`Back()`、`CanGoBack`、`HasOverlay`、`ShowOverlay/CloseOverlay`；外壳返回按钮改调 `Back()`。
- `InkPageShell`：新增叠加层（z=3，位于内容层与粒子层之间，空置时不可见）与 `ShowOverlay/HideOverlay`，坐标系与内容层一致。
- `MainUIManager`：79 处注册逐条标注框架；Esc 统一处理（弹层优先关闭 → 非 HUD 页 `Back()`；输入框持有焦点时不拦截）。
- 顺带修复：三个 HUD id（`combat-hud`/`-v2`/`-traditional`）都登记为 `Hud`，此前 `沉浸式 ⇄ 传统式` 切换会把非当前枢纽的 HUD 当成菜单页、给游戏场景铺上墨底。
- 口径变更（有意为之）：非 `popup-` 前缀的模态页（`reward-*`/`guide-action`/`guide-martial`/`mini-game-complete`/`death-screen`/`qte`/`dialogue-confirm`）不再叠加外壳墨底与暗角——这正是原 1.2 的"双重压暗"缺陷。

### S2 功能页去自绘底 —— ✅ 已完成（编译 0 error；页面根自绘底与重复氛围层已清零）

处理方式统一为"保留控件树、只去掉全屏铺底"，不改布局：

| 处理 | 页面 |
| --- | --- |
| 页面根 `BackgroundColor`：`Scrim`/`Void` → `Transparent` | `Map/CompassPage`、`Map/WorldMapPage`、`Dungeon/DungeonEntryPage`、`Crafting/CraftingPage`、`Quest/QuestLogPage`、`MountPet/MountPetPage`、`Character/CharacterPanelPage`、`Character/SkillPanelPage`、`Inventory/EquipmentEnhancePage`、`Inventory/InventoryPage`、`MiniGameCompletePage`、`Reward/RewardLevelUpPage`、`RewardPages`(5 处页根) |
| 删自绘 `InkBackgroundLayer`+`InkVignette`（与外壳重复） | `Time/MenuTimePage`、`Activities/MenuActivitiesPage`、`Mail/MenuMailPage`、`Character/MenuCharAttributesV2Page`、`MartialRecord/MenuMartialRecordPage`、`Livelihood/MenuLivelihoodPage`、`Appearance/MenuAppearancePage`、`MenuShopPage` |
| 只删 bg+vignette、保留本页专属装饰 | `Gacha/MenuGachaPage`（保留三处 `InkSplash` 晕染） |
| 改为"自铺舞台"框架（保留页面自己的定制暗角，外壳不叠暗角） | `MenuTimePage` 同类外的 `LoadingPages`×2 + `ChapterTransition`（自定义 `VignetteEdgeOpacity`）、`PhotoModePage`（取景器暗角 `EdgeOpacity=0.5`） |

**审计证据**（脚本扫描 `Ink/Pages`）：

- `new InkBackgroundLayer` / `new InkVignette` 仅剩 3 处，全部属于上表第 4 行的"自铺舞台页"，无重复层。
- `AnchorPreset=StretchAll` + `BackgroundColor=Scrim/Void` 的页根铺底仅剩 **模态页**（`PopupPages`×8、`DeathScreen`、`GuideAction`、`GuideMartial`）——它们按口径保留自有遮罩，S3 迁到叠加层时一并处理。
- 保留的内层 `Scrim`/`Void`（如 `MenuMartialRecordPage` 的地图面板/标题牌/图例、`MenuLivelihoodPage` 侧栏与页脚、`SettingsPage` 搜索框、`MenuShopPage` 侧栏）属于面板自身底色，不在"全屏铺底"范围内。
- 顺带清掉的隐患：`MenuShopPage`/`MenuMartialRecordPage`/`MenuAppearancePage`/`MenuMailPage`/`MenuActivitiesPage` 里对这些层的**无保护解引用**排版代码（直接删构造会 NPE，已先删引用再删字段）。

### S3 弹层迁移 —— ✅ 已完成（编译 0 error）

- `InkPageRouter.NavigateTo` 按框架分派：`Kind == Modal` → `_shell.ShowOverlay(page)`，用 `CurrentOverlayDomId` 单独记账，**不动** `CurrentPageDomId`、不进返回栈、不改外壳铺底/返回按钮（那些属于底页）；任何非弹层导航先 `HideOverlay()`（"关弹层"= 回到它下面那一页）。`Back()`/Esc 优先关弹层。
- `UIMouseCapture.Captured` 增加 `IsOverlayOpen`：弹层遮罩是引擎 `Panel`、不在可交互白名单内，不补这条会点穿到世界。
- `MainUIManager.NavigateToPage` 的 `_lastKnownDomId` 改记真实落点（`CurrentPageDomId`），画布自愈重建后恢复底页而不是弹层。
- **口径修正（用户拍板）**：奖励/成就/升级族按设计源（`yy-reward-system` 的 `reward-overlay` + `*-modal`，`role=dialog aria-modal`）是**浮层模态**，不是全屏页 → 已改回 `Modal` 并恢复其自绘遮罩（S2 里删掉的 6 处已回滚）。当前 Modal 共 20 个注册。

### S4 导航清理与闸门 —— ✅ 已完成（编译 0 error；闸门 PASS）

- 硬编码 dom-id 字面量全部改用 `InkPageDomIds` 常量（`MenuShopPage`/`MenuQuestsPage`/`MenuSectPage`/`MenuTeamPage`/`FriendsPage`/`AcupointPage`/`MenuCharAttributesV2Page`）。
- 重复入口合并删除：`nav-mail` → `SocialMailPage`（原 `MenuMailPage` 删除）、`nav-social-shop` → `MenuShopPage`（原 `SocialShopPage` 删除）、三套角色页 → `CharacterPanelPage`（原 `MenuCharAttributesV2Page`/`MenuCharAttributesPage` 删除），同步清理自愈重绑分派、字段与 `using`。
- Esc 归属唯一化：新增 `InkPageRouter.CurrentPageOwnsBackKey`——自带返回入口的页面（创角阶段流等）自己处理 Esc，中央 Esc 只在"非 HUD 且页面不自带返回"时改派，避免同一次按键退两层。
- **离线闸门** `scripts/verify-ink-page-nav.py`（四条不变量：A 注册必带框架 / B 导航目标必已注册 / C 仅自铺舞台页可自绘氛围层 / D 页面不得硬编码 dom-id）。当前实测：`A 79/0、B 88 referenced/0 missing、C 0、D 0` → PASS。
- 闸门**已做反证**（不是"只会绿"）：临时插入违规文件令 B/C/D 各报 1 条，用合成文本令 A 报错；删除临时文件后回到 PASS。

### S5 导航触发契约改为编译期接口 —— ✅ 已完成（编译 0 error；闸门 7 条全绿）

- 新增 `IInkNavigationSource`（`event Action<string> NavigationRequested`）；**43 个**声明该事件的页面类全部显式实现该接口。
- `MainUIManager.HookNavigationRequested(object page)` 的**反射订阅删除**，改签名为 `HookNavigationRequested(IInkNavigationSource page)` 编译期订阅——事件改名/漏实现直接编译不过，不再静默失效。
- **接口化当场抓出两处死导航**：`MultiplayerPage` 与 `ElementVisionPage` 既没声明 `NavigationRequested`、也从未被真正订阅，旧反射钩子对它们是静默 no-op（工厂里那行 `HookNavigationRequested(page)` 一直是空动作）。二者本就靠自有事件（`BackRequested`/`ExitRequested` 等）跳转，已删掉那两行无效调用。
- 审计脚本核对：实现接口的 **43/43** 页面，其工厂都真的订阅了该事件（hook 或 inline），无"永无订阅方"的页面。

### 离线闸门（已扩到 7 条，每条都做过反证）

`scripts/verify-ink-page-nav.py`：

| 规则 | 内容 | 现状 |
| --- | --- | --- |
| A | 注册必带 `InkPageFrame` | 79/0 |
| B | 导航目标必已注册（或别名/动作白名单） | referenced 88 / missing 0 |
| C | 仅"自铺舞台页"可自绘 `InkBackgroundLayer`/`InkVignette` | 0 |
| D | 页面不得硬编码 dom-id | 0（豁免调试画廊） |
| E | 声明 `NavigationRequested` 必须实现 `IInkNavigationSource` | 43 |
| F | 实现接口的页面其工厂必须订阅（hook 或 inline），否则死导航 | 43 checked / 0 违规 |
| G | 中央 Esc 存在、先关弹层、回落 `Back()`、尊重 `CurrentPageOwnsBackKey` | 通过 |
| H | 页根全屏铺底（`StretchAll` 后紧跟 `Scrim`/`Void`）只允许 Modal 页 | 18 处，全部 Modal |
| I | 路由器分派只由框架推导：Modal 走叠加层、其余走页面槽、铺底/暗角/返回开关实参来自 `frame`；且不得回退到 `popup-` 前缀、`ShellBackSuppressedPages`/`SelfDressedPages` 白名单、反射找 `NavigationRequested` | 6 锚点 + 4 禁项 |
| J | 实现 `IInkPage` 的类必须能经某个已注册工厂到达（基类/组件白名单：`CcStagePage`/`InkAnimationRefPageBase`/`CharacterPreview3D`） | 68 checked / 0 孤儿 |
| K | 页面不得绕过路由器：不得 `new 另一个页面类()`、不得直接调 `MainUIManager.Instance.*` 做导航（只读查询 `IsOnCombatHudPage`/`InkUiRoot`/`IsOverlayOpen` 放行） | bypass 0 / direct-manager 0 |
| L | Modal 页的关闭/返回不得直跳 `BackHud`，必须走 `BackOneLevel`（返回栈） | 20 个 Modal 工厂 |
| M | 枢纽身份跟随激活变体：进入 HUD 变体更新 `_hudDomId`、`Back()` 把 `_hudDomId` 当根停下、入栈排除 `_hudDomId`、有 `HudDomId` 只读出口 | 4 锚点 |

| N | 声明 `OwnBack`（隐藏外壳返回）的页面必须自带出口（返回/关闭/退出事件，或进入世界的推进钮）；自动推进的 `loading-1/2` 豁免，创角五页把基类 `CcStagePage` 也算作信号来源 | 14 checked / 0 死路 |
| O | 非 HUD 页面不得再出现直跳枢纽的 `BackHud`（必须走 `BackOneLevel` 返回栈） | 0 违规（3 个 HUD 页豁免） |

反证记录：A（合成文本）、E（临时违规文件）、F（临时摘掉 `CreateCompassPage` 的订阅）、G（合成文本两种形态）、H（临时把 `MiniGameComplete` 从 Modal 改成 Immersive）、I（临时把 `domId.StartsWith("popup-")` 塞回路由器）、J（临时加孤儿页面类）、K（临时加一个既 `new` 别的页面又直调管理器的类）、L（把 `PopupVerification` 关闭改回 `BackHud`）、M（空文本/缺锚点文本）、N（临时造一个无出口的 OwnBack 页面）、O（把 `MenuTimePage` 的返回改回 `BackHud`）各自都能报错，删/复原后回到 PASS——闸门不是"只会绿"。

**S6 孤儿页面清理（规则 J 的由来）**：按"每个 `IInkPage` 都必须有导航入口"审计，抓到 **`InventoryPage` 是死页**——背包早已合并进 `CharacterPanelPage`（`nav-inventory` 指向它），但 `CreateInventoryPage` 工厂还留着、且没有任何 `RegisterPage` 调用它，页面永远打不开。已删除该页与其工厂（与前面"合并删除"同一处置），并把这条不变量固化成规则 J。

### 范围边界（2026-10-07 用户确认：排除，只标注边界）

本方案的"其余UI"= **Ink 页面系统**（路由器内 79 个注册页）。以下明确**排除**，保持现状、不做沉浸化改造：

1. **登录场景**（`Content/Maps/Login.scene`，`LoginPanel`/`AuthenticationUI`）：有独立的"100% 还原设计稿"口径，不是 Ink 页面。
2. **旧选角/创角栈**（`MainUI`、`AuthenticationUI`、`CharacterSelectionUI`、`CharacterSceneController`）：已被 Ink 的 `cc-*` 创角页取代（`MainUIManager.UseInkCharacterFlow = true`），属**待删死栈**；删除它是独立任务，不属本方案。
3. **常驻 HUD 部件**（技能栏 `InkActionBarControl`、头顶名牌宿主、伤害数字等挂在外壳的常驻控件）：它们是战斗 HUD 的一部分，随 HUD 一起排除。
4. `ExitOnEsc.cs`（Esc 直接退出程序）全仓零引用，与统一 Esc 语义冲突；保留待定，未删。

**S7 返回语义真正统一（BackOneLevel）**：此前"页面自带返回按钮"和"弹层关闭"发的是 `BackHud`（**直跳枢纽、跳过返回栈**），而外壳左上返回按钮/Esc 走 `Back()`（**回来源页、栈空才回枢纽**）——同一动作两套语义，二级链路（罗盘→世界地图）里按页面自带按钮会丢上一层，"在角色面板上弹出的确认框"关掉后会被顶回枢纽。

- 新增 `InkPageDomIds.BackOneLevel`，`MainUIManager.NavigateToPage` 解析为 `_inkPageRouter.Back()`（与外壳按钮/Esc 同一实现）。
- 全量迁移 **43 处**：21 个页面的自带返回/关闭按钮 + 19 个工厂里的弹层关闭/返回接线。保留 `BackHud` 的只有两处——战斗 HUD V2 的"退出"热区（本就是回枢纽）与路由器自身那条分支判断。
- 闸门新增规则 L：**Modal 页的关闭不得再直跳 `BackHud`**（否则弹层关掉后底页被顶掉）。反证：把 `PopupVerification` 的关闭改回 BackHud → 报 `[L] Modal page PopupVerification … still closes via BackHud`；撤回后 PASS。

**S8 枢纽身份跟随激活变体**：`_hudDomId` 原先只在 `Initialize` 时设一次。因此点"切换沉浸模式"到 V2 之后：别名 `combat-hud` 仍解析成传统式、`isRootHud` 判定失效（V2 被当成普通子页入栈）、`NavigateToHud()` 会把用户**静默送回传统式**——等于回退掉他的选择。修法：`NavigateTo` 进入任一 HUD 变体时更新 `_hudDomId`，`Back()` 弹栈把 `_hudDomId` 也当根停下，入栈条件排除 `_hudDomId`，并补只读出口 `HudDomId`。闸门规则 M 钉住这四处锚点。

### 尚未完成

1. **运行态核对已完成（Hud / Immersive / Modal 三形态 + 返回语义全部取证）；仅剩人眼像素确认**。

   已证实的运行态结构（`ui_tree`，当时当前页是 `WorldMapPage`，属沉浸式全屏页）：

   | 外壳子控件 | Visible | 判定 |
   | --- | --- | --- |
   | `InkBackgroundLayer` | true | 沉浸式页铺墨底 ✓ |
   | `InkVignette` | true | 暗角开 ✓ |
   | 内容层(1920×1080，位于 -389,-187 居中信箱) | true | 只挂一个页面 `WorldMapPage` ✓ |
   | **叠加层** | **false** | 无弹层时隐藏，不参与命中测试 ✓（S1/S3 新增层行为正确） |
   | `InkParticleSystem` | true | — |
   | `InkBackButton` | true | 沉浸式页显示统一返回入口 ✓ |
   | `InkActionBarControl` / `InkWuxingCycleIndicator` | true | 常驻部件贴窗口底 ✓ |

   **已证实的运行态结论（2026-10-07，PIE + marker 日志取证，一次性跑完全部四种形态）**：

   取证方式：编辑器 PIE（RootScene+World）→ 临时挂一个探针脚本（用完即删）→ 探针按时间轴依次 初始化 / 切 HUD / 切弹层 / 切沉浸式页 / 返回，并把外壳各层可见性打进日志。原始日志行（`Log_2026_10_07_00_32_26.txt`）：

   | 形态 | 日志 | 结论 |
   | --- | --- | --- |
   | 初始化后 | `[AFTER-INIT] bg=false vignette=false contentPage=CombatHudTraditionalPage overlayVisible=False hasOverlay=False back=false` | HUD 页：不铺墨底、不叠暗角、无外壳返回 ✓ |
   | 显式切 HUD | `navigate HUD ok=True page=combat-hud-traditional overlay=False` + `[HUD] bg=false vignette=false … back=false` | 同上，可重复 ✓ |
   | **弹层** | `navigate POPUP ok=True page=combat-hud-traditional overlay=True overlayDom=popup-item-acquired` + `[POPUP] bg=false vignette=false contentPage=CombatHudTraditionalPage overlayVisible=True hasOverlay=True back=false` | **弹层叠在底页之上、底页（HUD）仍挂在内容层不被销毁**，且弹层不铺全屏压暗 ✓（S3 的核心不变量） |
   | 沉浸式页 | `navigate WORLDMAP ok=True page=nav-world-map` + `[IMMERSIVE] bg=true vignette=true contentPage=WorldMapPage overlayVisible=False back=true` | 沉浸式页：墨底+暗角+统一返回入口 ✓ |
   | 返回 | `back ok=True page=combat-hud-traditional canGoBack=False` + `[AFTER-BACK] bg=false vignette=false … back=false` | 返回栈按预期回落到枢纽，且外壳层状态随框架正确回切 ✓ |

   叠加此前 `ui_tree` 取的层结构（叠加层存在且空置时 `visible=false`），**三种页面形态（Hud / Immersive / Modal）与返回语义都拿到了运行态背书**。

   **仍未取到的证据**：像素级截图（`game_screenshot` 只出 3D、`viewport_screenshot` 整张白图），因此"看起来是否对"仍建议由人眼在编辑器/PIE 窗口确认一次。
   另：曾尝试再挂一个探针做"页面子树里是否还有全屏不透明块"的数值审计（probe v2），但那一次 PIE 启动落到了 **Login 场景**（不是 World），Ink UI 根本没起来、探针无输出；该审计与规则 C/H + 编译期证据重复，已放弃尝试并清理探针。

   **S7/S8 的运行态观测本次没拿到**：需要的会话是"RootScene+World 都在场且已进世界"。实测**新起** `sim_play` 走这条路 → 编辑器进程 **3/3 退出**（退出码 0、非崩溃，疑与 Content 下 108 个已删资产有关），日志也随之丢失；此前两次成功取证都是**编辑器自己恢复的、已经在播放中的**会话。因此这两条改动目前是"构造正确 + 闸门 L/M 钉住（均已反证）"，运行时请在人眼走查时一并确认（见下）。

### 接手须知（改哪里 / 跑什么 / 别踩什么）

**改哪里**
- 页面类型：`Ink/InkPageFrame.cs`（`Hud`/`Immersive`/`Modal` + `OwnBack`/`SelfLitStage`）。
- 注册即声明：`MainUIManager.RegisterInkPages()` —— 每行 `RegisterPage(id, factory, frame)`，框架在这里声明，是"哪些层给哪些页"的唯一来源。
- 分派与导航：`Ink/InkPageRouter.cs`（按 frame 分派 / 返回栈 `Back()` / `CurrentPageOwnsBackKey` / 叠加层 / `_hudDomId` 跟随变体）。
- 外壳五层 + 叠加层：`Ink/InkPageShell.cs`。

**新增一个功能页（三步）**：① 写类实现 `IInkPage`，需要跳转就发 `NavigationRequested`（并实现 `IInkNavigationSource`）；② 在 `RegisterInkPages` 注册并声明框架（通常 `Immersive`；自带返回才加 `OwnBack`）；③ 跑闸门 + 编译。
**新增一个弹层**：frame 用 `Modal`；关闭/确认发 `InkPageDomIds.BackOneLevel`（**不要**发 `BackHud`，那会把底页顶掉）。

**跑什么**：`python scripts/verify-ink-page-nav.py`（15 条不变量，全绿才算过）+ `dotnet build HundunWorld/Source/Game.csproj -c Editor.Windows.Development -p:Platform=x64`（0 CS error；注意在**仓库根**执行）。

**别踩什么**（每条都有对应规则，违反会被闸门挡住）：页面里自绘全屏底/暗角（C/H）；按 `popup-` 前缀判弹层、手写返回白名单、反射绑事件（I/E/F）；页面 `new` 另一个页面或直调 `MainUIManager` 做导航（K）；Modal 关到 `BackHud`（L）；切了 HUD 变体却仍把旧变体当枢纽（M）；隐藏外壳返回又不给出口（N）；非 HUD 页面直跳枢纽（O）。

**环境限制（做运行态核对前必读）**：Ink UI 只在"进世界"后存在（宿主 `MainUIManager` 挂在 **RootScene**，页面要 GameWorld/创角场景触发）；`flax_mcp` 客户端调用会超时，需 HTTP 直连 `http://localhost:21901/mcp`；**不要新起播放 `RootScene+World`**（实测 3/3 让编辑器进程退出）；`game_screenshot` 只出 3D、`viewport_screenshot` 是白图，像素必须人眼看。

### 交付验收清单（人眼 2 分钟版）

1. 打开任一菜单页（如 商店/时辰/武行录）：**只有一层墨底**，无"底上再叠一层暗"；左上角返回按钮只有一个、位置一致。
2. 反复开关同一页面：墨底深浅稳定（不会越开越暗 → 那是页面自绘底又回来了）。
3. 打开任意弹窗（物品获得/消息/校验）：弹窗盖住底页，关掉后**底页还在**（不是回到枢纽）、且弹窗没有把整个屏幕再压暗一层。
4. 战斗 HUD 页：背景层/暗角层都不出现（3D 场景不被墨底遮）、也没有左上返回按钮。
5. 按 Esc：弹窗→关弹窗；菜单页→回上一层（有返回栈时）/回战斗 HUD；HUD 页→无反应。
6. 光标停在按钮/输入框/技能格上时：左键点选世界、Alt+左键移动、滚轮缩放、右键转视角都不响应；光标移回世界即恢复。
7. 点"切换沉浸模式"切到沉浸式 HUD → 进任意菜单 → 按 Esc 或点左上返回：应回到**沉浸式**（不是被悄悄送回去传统式）。
8. 二级链路（罗盘 → 世界地图）里点**页面自带**的返回：应回**罗盘**，不是直接跳枢纽。

2. **顺带发现（与本方案无关，但要报）**：本机 `HundunWorld/Content` 下有 **108 个已删除的受控资产**（`Character_1.flax`、`Animation Graph.flax`、大量 `Audio/Foley/*.wav` 等）。PIE 跑 World 场景时日志里 `Missing file ...Character_1.flax` 出现 37 次、`AssignAnimatedModelResources: 没有已加载的 SkinnedModel` 15 次 —— 世界是空场景（无角色/无脚步声）。
   另有两次**编辑器进程自行退出**（2026-10-07，均发生在"已加载 World 场景后进播放"这条路径上）：进程退出码 **0**（属正常退出而非崩溃），未找到崩溃转储；`ExitOnEsc`（唯一会调 `Engine.RequestExit()` 的脚本）在六个场景里都**未挂载**，故排除；最可能就是上面那批缺资产在加载/播放路径上触发的引擎侧正常退出。本次 UI 改动在日志里**零报错**。
   → 因此**不建议再用编辑器强行 PIE World 场景**取证；HUD / 弹层两种形态的运行态核对，建议由你在自己的登录会话里跑（或把会话留在世界内，我用 `ui_tree` 取证）。

**取证环境备注**（供后续复核）：编辑器 MCP 的客户端调用会超时，但 HTTP 直连 `http://localhost:21901/mcp`（JSON-RPC `tools/call`）可用；本仓 `LaunchFlaxEditor.ps1` 指向的 exe 路径已失效，真实路径是 `C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Editor\Win64\Development\FlaxEditor.exe`。

## 七、ESC 语义收口（2026-10-07 续作）

用户反馈"ESC 现在的功能完全无意义且还遮挡"（附图：战斗 HUD 上按 ESC 弹出一个居中的旧「暂停」框）。取证结论：

- 抢键的是插件侧 `NarrativeGameplayHUD.bOpenMenuOnEscape=true` → `TickMenuHotkey()` 拉起 `NarrativePauseMenu`。该框**不停游戏**（「继续游戏」只是 `Close()`），第二项「任务日志」调 `NarrativeRuntimeBootstrap.ToggleQuestLog`，是插件自带的另一套旧面板，与已合并的 Ink「任务」页重复；它还整块盖在 HUD 上，样式另一套体系。
- 另有同帧竞态：插件用 `Input.GetKey(Escape)` 边沿检测开菜单，菜单自己的 `bDeactivateOnBack` 又吃同一次 Escape。
- Ink 侧中央 ESC 当时**故意在战斗 HUD 页不响应**，所以 HUD 上 ESC 唯一效果就是弹那个遮挡框。
- 同一批取证还抓到 6 个页面（活动/时间/个人信息/休闲模式/稀有商店/设置-音频）注册成 `OwnBack=true`（中央 ESC 跳过它们）却一行 `KeyboardKeys.Escape` 都不处理 → 这些整屏页里 ESC 是死键，且外壳返回钮被 OwnBack 藏掉。第 7 个同类是 `chapter-transition`，核实为定时过场（`Update` 自推进），按"自动推进豁免"处理而不是当缺陷。

落地口径（用户拍板：HUD 上 ESC ＝ Ink 版系统菜单＋真暂停；优先级 焦点→弹层→页面）：

1. `NarrativeGameplayHUD.bOpenMenuOnEscape` 改 `false`（旧框由此下线；`NarrativePauseMenu` 类保留，仍可经 `ToggleDefaultMenu()` 显式调用，插件的 UE5 对等面不缩）。
2. 新增 `Pages/SystemMenuPage.cs`（注册为 `InkPageFrame.Modal`，走外壳叠加层、不进返回栈）：继续游戏＝收本层 / 设置＝真导航 / 退出到角色选择＝`MainUIManager.ExitGameToCharacterSelection()`（与 HUD「退」钮同一流程）。`RefreshLayout` 用设计尺寸兜底，避开"AddChild 当刻尺寸为 0 → 面板停在负坐标看不见"这个已知坑。
3. `MainUIManager` 中央 ESC 改成一条链：输入框聚焦 → `UIMouseCapture.ReleaseTextInputFocus()`（`RootControl.FocusedControl?.Defocus()`，已对引擎源码 `Source\Engine\UI\GUI\` 核实过该签名）；有弹层 → 关弹层；非 HUD 页且不自管 Esc → `Back()`；停在 HUD → 开系统菜单。
4. 暂停必须真有消费者，否则只是换个标题的假动作：`UIMouseCapture` 新增 `KeyboardCaptured`（打字/弹层/整屏页三种情形），`PlayerController.GetInputDirection()` 据此归零，`MainUIManager` 的 C 快捷键在弹层开着时不响应；`GameStateManager.PauseGame/ResumeGame` 由 `SyncPauseToSystemMenu()` **每帧对账**（弹层可能被页面自己的关闭钮或返回栈收掉，只在 ESC 分支里同步会留下解不开的暂停）。刻意不冻结时间/世界——服务器权威，本地停摆只会把移动同步弄坏。
5. 补一处连带风险：`HundunWorldGame` 的"网关离线→退回选角"原来只看 `IsInGame`，暂停态会被判成"不在游戏里"而静默跳过，玩家就被关在一个连不上服务器的菜单里；改成 `IsInGame || IsPaused`。
6. 假键位提示清账：HUD 右下四个功能钮原来写「背包 B / 角色 C / 技能 K / 设置 ESC」，实测只有 C 真绑（且绑的是 `NavInventory`，不是"角色"页）。改为「背包 C / 角色 / 技能 / 设置」——只印真话。
7. 闸门加固：`verify-ink-page-nav.py` 规则 N 增加"声明 OwnBack 就必须处理 `KeyboardKeys.Escape`"。这条一上线就抓到 `chapter-transition`（即上面第 7 个同类），说明它确实会红。


