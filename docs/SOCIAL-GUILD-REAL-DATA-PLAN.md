# 结伴江湖 · 真实数据接入方案（好友 / 门派 / 帮会 / 队伍）

> 状态：**待确认**。本文只描述方案与决策点，未落任何代码。
> 范围来源：2026-10-07 用户要求「继续把没接的逻辑也补全」，并在决策清单里选了
> 好友=端到端打通、门派/帮会/队伍=连同服务端一起打通、玩家卡=真值只填昵称+等级+称号其余「—」。

## 〇、一句话结论

聊天已经是真的（`ChatClient` 端到端可用，本次已接进本页）。剩下四块里：

| 域 | 服务端现状 | 缺什么 |
|---|---|---|
| 好友 | **逻辑与落库都在**：`SocialGrain` 有持久化 `Friends` 字典（`Horizon.Orleans.Grains/SocialGrain.cs:26,30` `[PersistentState("social","GameStore")]`；`GetFriendsAsync` 在 `:276`） | 网关从没解析过这个 grain（`Horizon.Game.Core/Handlers/SocialHandler.cs:81-101` 只回 canned「添加好友成功」）；客户端 `FriendMessageHandler` 4 个事件零订阅；没有"请求好友列表"的 DTO |
| 帮会 | **grain 齐全**：`GuildGrain` 持久化 `GuildState`（名称/帮主/等级/成员/申请/职位，`Horizon.Game.Message/Network/GrainStateModels.cs:679+`），接口 `IGuildGrain`（`ISocialSystemGrains.cs:123-191`） | 同上无网关路由；**没有"玩家→帮会"归属索引**（`SocialState` 里没有任何 GuildId 字段） |
| 队伍 | **grain 齐全**：`TeamGrain` 持久化 `TeamState`（`GrainStateModels.cs:815+`），接口 `ITeamGrain`（`ISocialSystemGrains.cs:243-300`，含 `GetTeamStateVersionAsync` 可做轮询版本号） | 同上无路由；同样**没有玩家→队伍索引**；客户端 `MenuTeamPage` 现在全是写死数组（`Pages/Team/MenuTeamPage.cs:72-82`） |
| 门派 | **只有 DTO**：`SectInfoMessage`（`SocialMessages.cs:14-73`：SectId/SectName/SectLeader/MemberCount/SectLevel/Reputation/Resources） | 没有任何 grain、没有门派目录数据源、`CharacterInfo` 没有门派字段（`AccountMessages.cs:355-404`）；`SocialHandler` 的 SectInfo/JoinSect 分支返回 `Success=true` 空壳 |
| 交易 | 只有 `TradeState` POCO（`GrainStateModels.cs:912`）与无人订阅的 `TradeMessageHandler`；`Horizon.Game.Core` 里 grep `Trade` 0 命中 | 整条链都不存在 |

可直接复用的既有资产（避免另起炉灶）：

- 数字 id → Guid grain key 的既有口径：`GrainKeys.ForEntity(ulong)`（`Horizon.Game.Core/GrainKeys.cs:17-22`），`QuestHandler.cs:209`、`SkillHandler.cs:100,135` 都这么用；`SocialGrain` 自己的 `GuidToUInt64`（`SocialGrain.cs:456-459`）取同样前 8 字节，字节兼容。
- 处理器注册是**自动扫描**：`services.AddAllMessageHandlers(Assembly.GetAssembly(typeof(MessageHandlerBase)))`（`Horizon.Game.Gateway/Program.cs:597-598`）；客户端同样反射注册 `BaseMessageHandler`（`Network/NetworkManager.cs:889-935`）。加新 handler 不用改清单。
- 新 DTO 的写法样板：`SectInfoMessage` —— `[MemoryPackable][GenerateSerializer][NetworkMessage(tag, ServiceType.Social)]` + 每字段 `[MemoryPackOrder(n)][Id(n)]` + 尾部 `Type`/`ServiceType`；`[MemoryPackUnion]` 由 `Horizon.Game.Message.Generator` 生成，**不要手写**（`Attributes/NetworkMessageAttribute.cs:9-15`）。tag 必须唯一且 ≤255，132 是墓碑位。
- 服务端→客户端推送：`IPlayerPushService.PushAsync<T>(T message, IReadOnlyCollection<ulong> targetCharacterIds)`（`Handlers/IPlayerPushService.cs:19`，实现 `Horizon.Game.Gateway/Services/PlayerPushService.cs:43-68`）。没有单发 API，传单元素集合即可。注意它按本 Gateway 的在线连接广播，跨网关扇出要另接 Orleans 流。
- 在线状态：`ICharacterGrain.IsOnlineAsync()`（`Horizon.Orleans.Interface/ICharacterGrain.cs:65`）——好友列表的「在线」有真值可取。
- 页面侧真实数据页的范式：`MenuQuestsPage` + `QuestClientService`（`Game/Network/QuestClientService.cs:50-72` EnsureAttached / `:82-97` 请求（PlayerId==0 直接拒）/ `:41` Revision / 页面 `:984-1019` 8 秒节流轮询 + 只在 Revision 变化时重画 + `:837-843` 服务端没给就显示「—」）。

## 一、分阶段落地（每阶段都能独立编译、独立验收）

### A 阶段 · 好友端到端（不动数据模型，纯接线）

1. `Horizon.Game.Message`：新增 `FriendListRequestMessage { ulong CharacterId }`、`FriendOpRequestMessage { ulong CharacterId; ulong TargetId; int Op }`（Op：1 加 2 删），`Type = MessageType.FriendList / FriendOperation`。
2. `SocialHandler`：`MessageTypes` 补 `FriendList/FriendOperation/FriendStatusUpdate/FriendRequestNotify`（**这是现在的路由缺口**：客户端监听这四个，`SocialHandler` 只声明了 `Friend(309)`，请求进来会命中"无 handler 匹配"，`ClientPacketPipeline.cs:83`）；`HandleFriendListAsync` 调 `GetGrain<ISocialGrain>(GrainKeys.ForEntity(id)).GetFriendsAsync()` 回 `FriendListUpdateMessage`；好友增删走 `AddFriendAsync/RemoveFriendAsync`。分支改成 **body 判空 + 异常回真错**（现状 `SocialHandler.cs:85,96-100` 吞异常并回 500）。
3. 客户端新增 `Game/Network/FriendClientService.cs`（照 `QuestClientService` 的 EnsureAttached / Request / Revision / 缓存 / 事件），订阅 `FriendMessageHandler.FriendListUpdated`。
4. `SocialGuildPage`：左栏好友改为数据驱动 —— 真值取 `FriendInfo`（FriendId/FriendName/Level/Profession/IsOnline/LastLoginTime/Remark/Intimacy，`InteractionMessages.cs:240-300`）；**门派一列服务端没给 → 显示「—」**；无好友显示空态；搜索框做本地真过滤（现在它只是个画占位符的假控件）；点好友行 = 进密聊（已具备）；删除好友接 `RemoveFriendAsync`。
5. 闸门：新增 `scripts/verify-social-friend-wiring.py`（规则：客户端监听的每个 MessageType 必须在某个网关 handler 的清单里出现；`FriendClientService` 必须被订阅且页面不得残留写死好友名；仿现有闸门"注入违规即变红"的写法）。

**A 阶段不改任何已有状态 schema，重启即生效，风险最低。**

### B 阶段 · 玩家归属索引（帮会与队伍的共同前置）

`SocialState` 没有 GuildId/TeamId，而两个 grain 都按"公会 id / 队伍 id"做主键，网关拿到的是玩家 id，**必须有一处能回答"这个玩家属于哪个队/公会"**。这是本方案唯一的结构性新增，落点见决策 D1。

### C 阶段 · 帮会端到端

`GuildGrain` 接口已够（`GetGuildInfoAsync/GetMembersAsync/ApplyToJoinAsync/ProcessApplicationAsync/AppointPositionAsync`），补网关路由 + `GuildClientService` + `MenuSectPage`/本页帮会卡数据驱动（成员列表、等级、职位、贡献）。

### D 阶段 · 队伍端到端

`TeamGrain` 接口已够（含 `GetTeamStateVersionAsync` 版本号 → 客户端按版本号轮询，与 `QuestClientService.Revision` 同一口径），补路由 + `TeamClientService`，把 `MenuTeamPage` 的写死数组换成真成员；本页「组队」按钮 = 对选中好友 `CreateTeamAsync/JoinTeamAsync`。实时性见决策 D3。

### E 阶段 · 门派

缺的是**数据源**而不是接线：见决策 D2。

### F 阶段 · 交易

服务端整条链不存在（`Horizon.Game.Core` 里 `Trade` 0 命中）。本轮建议**移除死按钮**（项目口径：没有通道的按钮不留占位，`MenuQuestsPage.cs:1328` 已经这么删过一批），交易另立方案。

## 二、玩家信息卡口径（已定）

真值：`CharacterAttributesComponent` 的 `Nickname:40` / `Level:31` / `Title:43`（`MainUIManager` 已在建页时 `BindCharacter`）。
一律「—」：战力（只有遗留本机加权式 `GameMainUI.cs:2068-2085`，非服务端权威）、在线（本地玩家恒在线，不显示）、区域名（服务端只有 `Vector3`，无地图名）。

## 三、风险与既有地雷

- **Orleans 混跑**：改了 `Horizon.Orleans.Interface`/`Grains` 必须**先停旧 Silo 再启**，否则客户端 `TypeLoadException`（项目已踩过：`orleans-silo-typeloadexception`）。
- **追加序列化字段要测槽位对位**：给已有 `[MemoryPackable]` 状态加字段必须新 Order/Id，并补"老数据回退"测试（`appending-serialized-parameters-needs-slot-order-tests`）。
- **GameStore 存储提供者**：三个 grain 都写 `"GameStore"`，落地前要确认 silo 配置里该 provider 真实存在（否则状态"看起来持久"其实每次从空开始）。
- **网关进程共享**：Silo/网关是本机长期运行的服务，重启会影响并行会话。
- **`ISocialGrain` 与 `IChatGrain` 都有 ChatHistory**：聊天走的是 `IChatGrain`，本轮不得让好友链路误写 `SocialState.ChatHistory` 造成双写。

## 四、交付验收清单（人眼项，用户在 PIE 里看）

1. 好友列表出现真实角色名/等级/职业，名字与服务端一致；没有好友时是空态而不是 11 条假人。
2. 搜索框输入能真过滤（现在它连光标都没有）。
3. 点好友行 → 聊天输入框水位变「密聊 ××」，发出去对方能收到，主界面左下聊天框同步看到。
4. 删除好友后重进页面该好友不再出现（真落库，不是本地假删）。
5. 帮会/队伍卡片的成员列表与另一名册真实一致；「—」只出现在服务端确实没给的字段上。
6. 页顶 好友/门派/聊天/队伍 四个 Tab 有真行为（切内容或真跳转），不再只是高亮。
7. 玩家卡昵称/等级与左上角 HUD 一致；战力/在线/位置显示「—」。
8. 服务端日志无 500 吞栈；`FriendList` 请求能在网关日志里看到命中的分支。

## 五、决策清单（等确认，未动代码）

- **D1 玩家→帮会/队伍 归属索引落在哪**
  1. 新建 `IPlayerAffiliationGrain : IGrainWithGuidKey`（`{GuildId, TeamId, Sect, SectRank, Contribution}`，新 `[PersistentState("affiliation","GameStore")]`）。不动任何既有状态 schema，加/改字段都在新 POCO 里，槽位风险最小。**（推荐）**
  2. 往 `SocialState` 追加 `GuildId/TeamId` 字段（Order 6/7）。少一个 grain，但动已有落库结构，必须补老数据回退测试。
  3. 往 `CharacterInfo`/角色档案加门派与队伍字段。语义最正，但要动角色建档链路与所有客户端握手口径，面最大。
- **D2 门派（sect）数据源**
  1. 服务端静态目录：把 `MenuSectPage.cs:79-112` 那份写死的门派名单搬成服务端 `SectCatalog`（id→名称/掌门），成员/职位/贡献落 D1 的归属 grain。**（推荐：既有名单不动，权威转移到服务端）**
  2. 落库 SQL Server（新表 + 迁移脚本，我只生成不执行，等你点头再跑）。
  3. 本轮不做门派：卡片保留但字段全「—」，顶栏「门派」Tab 改成真路由（→ `nav-sect`）。
- **D3 队伍/帮会成员变更的实时性**
  1. 客户端轮询版本号（`GetTeamStateVersionAsync` / 帮会同理），页面 8s 节流——与 `QuestClientService.Revision` 同口径，改动小。
  2. 变更时服务端经 `IPlayerPushService.PushAsync(..., memberIds)` 广播 TeamUpdate/GuildUpdate 包，真即时，代码面大。
- **D4 好友"添加/申请"入口**
  1. A 阶段只做「列表 + 移除 + 点行密聊」；申请/按名字搜索等有角色名索引后再开（服务端现在**没有**按名字查角色的能力，`ICharacterGrain` 只有按 id）。**（推荐）**
  2. 顺带补名索引：`ICharacterGrain` 加 `FindByNicknameAsync`（要一个新 grain 名字表，或复用 D1 grain 存 name→id 索引）。
- **D5 进程与验证**
  1. 我按阶段改完并跑离库编译 + 闸门，**服务重启（停旧 Silo → 启 Silo → 启网关）由你执行**，PIE 像素与真机链路你验收。**（推荐）**
  2. 我自己重启本机 Silo/网关并跑自检 —— 但会影响并行会话，且本会话早前强推 PIE 已把编辑器弄崩过，我不建议。

## 六、接手须知

- 客户端编译：仓库根执行 `dotnet build HundunWorld/Source/Game.csproj -c Editor.Windows.Development -p:Platform=x64`（`global.json` 钉 SDK 10.0.300，子目录里跑会报 SDK 错）。服务端改动另编 `Horizon.Game.Gateway` / `Horizon.Orleans.Grains`。
- 闸门：`python scripts/verify-ink-page-nav.py`（18 条，改 UI 不能让它变红）。
- 本页已经历的改动（不要重复劳动）：聊天窗口已接 `ChatClient`（真消息/真输入/真频道过滤/到顶加载历史/点行密聊），侧边栏与页标题已改名「结伴江湖」，`DBox.Draw` 铺底顺序已修。

## 七、当前进度与阻塞（2026-10-07 记录）

已落地并验证：

- 协议层：`FriendListRequestMessage` / `RemoveFriendRequest` / `FriendOperationResponse`（tag 243/244/245，实测 0–242 已用、132 为墓碑），`MessageUnion.cs` 同步补手写 `[MemoryPackUnion]`；`dotnet build Horizon.Game.Message` **0 错误**，新产物已按授权分发到 `HundunWorld/Output/Windows/`。
- 客户端：`Game/Network/FriendClientService.cs`（照 `QuestClientService` 的 EnsureAttached / Revision / 节流 / `PlayerId==0` 直接拒 / 回执透传，名单只照抄服务端，缺字段由界面出占位）；`FriendMessageHandler` 补 `FriendOperationResolved` 事件，并把 `MessageType.Friend` 加进注册清单——`FriendListUpdateMessage.Type` 的默认值就是 `Friend`，只注册 1338/1339 会让整包在分发阶段被静默丢弃。

**阻塞（都不是本任务造成的，属并发会话的在途重构）：**

1. 服务端：`Horizon.Game.Core` 编不过 —— `World/ChunkNeighborQuery.cs:24,33 → CS0246 ChunkCoord`。`ChunkCoord` 在 HEAD 位于 `Horizon.Game.World/WorldCoord.cs:134`，现被搬进新工程 `Horizon.Game.World.Contracts`（`git status`：`A MortonCodec.cs` / `AM WorldCoord.cs` / csproj 未跟踪），但只有 `Horizon.Game.World.csproj` 拿到了新的 ProjectReference，`Horizon.Game.Core.csproj:45-51` 没有。→ 网关那一步（任务 #11）与后端单测（任务 #14）无法编译、无法跑。
2. 客户端：`Game/Game.Build.cs`（已被并发会话改为 `M`）的 DLL 清单换成 `Horizon.Share.Client.dll` + `Horizon.Game.World.Contracts.dll` + `Horizon.Game.Sim.Contracts.dll`，而这三份在 `HundunWorld/Output/Windows/` 里**都还不存在**；清单同时去掉了 `Horizon.Core.Abstract`，而 `Game/Services/GengDiAuthService.cs:8-9` 仍写着 `using Horizon.Core.Abstract;` → `CS0234`，客户端整体编不过，我这轮新增的客户端文件因此尚未取得编译证明。
   - 佐证：`Horizon.Share.Client` 工程本身 `dotnet build` **0 错误**，缺的只是"构建 + 分发 + 换 using"这三步收尾。
   - 规范分发脚本：`scripts/copy-shared-dlls-to-flax-tools.ps1`（我这次是手工拷的 Message.dll，配置为 Debug；收尾时建议以脚本口径为准复核一遍）。
3. 因此本轮按用户拍板「只做不依赖 Core 的层」推进：好友端到端的网关分支、页面数据驱动改造、`verify-social-wiring.py` 与网关单测，待上面两条收口后再接。任务 #11/#13/#14/#15 保持 pending。

复核命令（等并发会话收口后跑）：

```
dotnet build Horizon.Game.Core/Horizon.Game.Core.csproj
dotnet build HundunWorld/Source/Game.csproj -c Editor.Windows.Development -p:Platform=x64
```

