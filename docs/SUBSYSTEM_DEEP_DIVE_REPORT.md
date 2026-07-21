# 混沌世界（HundunWorld）全量子系统代码级深度剖析报告

> 生成日期：2026-07-21 · 基于全量源码逐文件阅读  
> 配套报告：[DESIGN_PHILOSOPHY_REPORT.md](./DESIGN_PHILOSOPHY_REPORT.md)（设计思想总报告）  
> 解决方案：Horizon.sln（36+ csproj，.NET 10 全栈 Monorepo）

---

## 目录

1. [服务端核心子系统](#一服务端核心子系统)
2. [网关层与协议层](#二网关层与协议层)
3. [ECS 层与客户端](#三ecs-层与客户端)
4. [AI 与 RAG 子系统](#四ai-与-rag-子系统)
5. [花卉电商子系统](#五花卉电商子系统)
6. [即时通讯（IM）子系统](#六即时通讯im子系统)
7. [IoT/MQTT 子系统](#七iotmqtt-子系统)
8. [桌面启动器（耕地）](#八桌面启动器耕地)
9. [数据层与持久化](#九数据层与持久化)
10. [工具链](#十工具链)
11. [Orleans Silo 宿主配置](#十一orleans-silo-宿主配置)
12. [WebApi 与 WebAdmin](#十二webapi-与-webadmin)
13. [跨子系统架构关系图](#十三跨子系统架构关系图)
14. [代码质量与工程实践评估](#十四代码质量与工程实践评估)

---

## 一、服务端核心子系统

### 1.1 ZoneShardGrain — 空间权威模拟核心

**文件**：`Horizon.Orleans.Grains/World/ZoneShardGrain.cs`（2256 行）

| 维度 | 实现细节 |
|------|---------|
| **Tick 驱动** | 1/60s 固定步长 `RegisterTimer` → `TickAsync`，每 Tick 处理所有 `_simulatedEntities` |
| **实体租约** | `EntityLeaseDuration=90s`、`OrphanCheckInterval=10s`、`MaxFailedDespawnAttempts=10`；超时未续约的实体自动 Despawn |
| **AOI 兴趣集** | `ZoneShardAoi` 基于 Morton 键空间索引，按 chunk 粒度管理订阅/退订 |
| **增量快照** | `FullSnapshotIntervalTicks=60`（每 60 tick 发一次全量），其余 Tick 仅发增量 diff |
| **场景对象持久化** | 30s 定时器将场景对象状态落盘（Redis GrainStorage） |
| **反作弊集成** | 构造 `MovementValidator(PositionEpsilon=0.5, HardSpeedCap=200, MaxSpeed=6, TickDt=1/60)` |
| **fanout 通知** | 每 Tick 结束后通过 `IZoneShardFanoutObserver` 将快照推送给所有已注册 Gateway |

**关键设计决策**：
- 租约机制是对分布式故障的**最终一致性保障**——即使 Gateway 崩溃，孤儿实体也会在 90s 内被清理
- `_simulatedEntities` 字典以 `characterId` 为键，每个实体维护独立的 `SimulatedEntityState`（位置/速度/输入历史/租约到期时间）
- Despawn 失败有 10 次重试兜底，超过后强制移除并记录告警日志

### 1.2 PlayerSessionGrain — 瞬态会话管理

**文件**：`Horizon.Orleans.Grains/World/PlayerSessionGrain.cs`（114 行）

```
职责链：HandshakeAsync → ReceiveInputAsync → ResumeAsync → BuildInputAckAsync
```

| 方法 | 功能 |
|------|------|
| `HandshakeAsync` | 验证协议版本、绑定 characterId ↔ sessionId |
| `ReceiveInputAsync` | 输入去重（per-characterId `_lastInputTick`），转发给 ZoneShardGrain |
| `ResumeAsync` | 返回 `ResumeDecision`（4 种：FullSnapshot/IncrementalResume/Reject/ForceReconnect） |
| `BuildInputAckAsync` | 构造 InputAck 包（含确认 tick + 权威位置） |

**设计精髓**：Grain 本身是**无状态委托层**，核心逻辑在 `PlayerSessionState`（POCO）中，便于单元测试。

### 1.3 CharacterGrain — 持久化角色

**文件**：`Horizon.Orleans.Grains/CharacterGrain.cs`（1203 行）

| 特性 | 实现 |
|------|------|
| **持久化** | `[PersistentState("character", "GameStore")]` → Redis GrainStorage |
| **双轨在线状态** | Redis presence key（TTL 90s）+ Grain 内部 `_isOnline` 标志 |
| **防御性初始化** | `OnActivateAsync` 中检测 `State == null` 时从数据库 fallback 加载 |
| **RPG 系统** | 属性/技能/背包/装备/称号/五行铸造/轻功/交易/社交 |
| **指纹服务** | `ICharacterFingerprintService` 写入 `character:fingerprint:{id}`（TTL 5min），离线时主动清理 |

### 1.4 MovementFormula — 确定性移动数学

**文件**：`Horizon.Game.Core/Sim/MovementFormula.cs`（134 行）

```csharp
FormulaVersion = 2;  Gravity = 9.81f;  TerminalVelocity = 50f;  DefaultMaxSpeed = 6f;
```

| 方法 | 功能 |
|------|------|
| `StepHorizontal` | 水平位移（输入方向 × maxSpeed × dt） |
| `StepVertical` | 垂直位移（重力积分 + 终端速度钳制） |
| `Step` | 组合水平+垂直，返回 `(nx, ny, nz, nvz)` |
| `Distance3D` | 欧氏距离（用于反作弊位移校验） |

**关键约束**：
- **禁用 SIMD**（`[MethodImpl(MethodImplOptions.NoInlining)]`），确保跨平台浮点一致性
- **固定运算顺序**，客户端/服务端共享同一实现 → 预测/回放的数学基础
- 纯静态类，**零依赖**（不引用 Orleans/Flax/任何 DI）

### 1.5 MovementValidator — 权威回放反作弊

**文件**：`Horizon.Game.Core/Sim/MovementValidator.cs`（299 行）

| 参数 | 值 | 含义 |
|------|-----|------|
| `DefaultPositionEpsilon` | 0.5m | 预测漂移容忍阈值 |
| `DefaultHardSpeedCap` | 200 m/s | 绝对速度上限（超出即判定作弊） |
| `MaxJumpCount` | 2 | 普通跳跃次数上限 |
| `MaxQinggongJumpCount` | 3 | 轻功三段跳上限 |
| `GroundHeightSampler` | 委托注入 | 地面高度采样（可替换实现） |

**校验流程**：
1. 收到客户端输入 → 从权威位置用 `MovementFormula.Step` 回放
2. 计算回放位置与客户端声称位置的偏差
3. 偏差 > `PositionEpsilon` → 触发修正（Correction 包）
4. 瞬时速度 > `HardSpeedCap` → 判定速度外挂

---

## 二、网关层与协议层

### 2.1 SyncPacketHandler — 协议路由核心

**文件**：`Horizon.Game.Core/Handlers/SyncPacketHandler.cs`（804 行）

**路由表**（switch on `SyncPacketKind`）：

| Kind | 处理逻辑 |
|------|---------|
| `Handshake` | 协议版本校验 → 绑定 session → 触发 ZoneShard Spawn |
| `Input` | per-characterId 去重（`_lastInputTickPerCharacter`）→ 转发 PlayerSessionGrain |
| `ReconnectResume` | 调用 `ResumeAsync` → 返回 ResumeDecision |
| `InteractionSync` | 交互速率限制（10/s）→ 转发业务 Grain |
| `SceneObjectSync` | 场景对象状态同步 |
| `SubscriptionUpdate` | AOI chunk 订阅/退订更新 |

**关键设计**：
- `DefaultShardId = 0` 硬编码（当前单 shard，预留多 shard 路由接口）
- 严格版本检查：`SyncProtocolVersion.Current = 6`，不匹配仅告警不拒绝（渐进式升级策略）
- 单例 Handler + per-characterId 字典 → 解决 v4 之前的竞态问题

### 2.2 SyncPackets — 协议定义

**文件**：`Horizon.Game.Message/Sync/SyncPackets.cs`（690 行）

```csharp
SyncProtocolVersion.Current = 6;

[MemoryPackUnion(0, typeof(HandshakePacket))]
[MemoryPackUnion(1, typeof(InputPacket))]
...
[MemoryPackUnion(10, typeof(SubscriptionUpdatePacket))]
```

| 特性 | 实现 |
|------|------|
| **双重序列化** | 每个类型同时标注 `[MemoryPackable]` + `[GenerateSerializer]` |
| **帧格式** | 6B 帧头（Length:4B + Kind:1B + Flags:1B），Kind 冗余写入实现 fast-path 路由 |
| **压缩策略** | LZ4 仅对 >256B 的 Snapshot 压缩；Input 永不压缩（保延迟）；4MB 解压上限 |
| **Kind 枚举** | Unknown=0 → SubscriptionUpdate=11，共 12 种包类型 |

### 2.3 GatewaySyncDispatcher — 带宽守门分发

**文件**：`Horizon.Game.Core/Sim/Server/GatewaySyncDispatcher.cs`（487 行）

| 参数 | 值 | 功能 |
|------|-----|------|
| `BandwidthThresholdKbps` | 100 | 带宽阈值 |
| `NormalSnapshotHz` | 20 | 正常快照频率 |
| `ThrottledSnapshotHz` | 10 | 弱网降频 |
| `RecoverySeconds` | 3 | 恢复等待时间 |

**分发流程**：
1. 从 `IZoneShardFanoutSource` drain 有界 Channel
2. per-session 计算瞬时带宽（`Interlocked` 无锁计数器）
3. 超阈值 → 降频到 10Hz；恢复需持续 3s 低于阈值
4. `Parallel.ForEach` 并行序列化 + 发送（每 session 独立 Try/catch）

### 2.4 GatewayZoneShardFanoutSource — 有界 Channel 适配

**文件**：`Horizon.Game.Gateway/Services/GatewaySyncWiring.cs`（345 行）

```csharp
Channel.CreateBounded<SyncPacket>(new BoundedChannelOptions(8192)
{
    FullMode = BoundedChannelFullMode.DropOldest  // 丢旧保新
});
```

**双接口实现**：
- `IZoneShardFanoutObserver`：被 Grain 回调推送快照 → `Channel.Writer.TryWrite`
- `IZoneShardFanoutSource`：被 Dispatcher drain → `Channel.Reader.TryRead`

**设计价值**：Grain 回调线程与 Gateway 工作线程完全解耦，8192 容量 ≈ 6.8s@1200pkt/s 的缓冲。

---

## 三、ECS 层与客户端

### 3.1 ArchWorldHost — 五阶段调度器

**文件**：`Horizon.Game.ECS.Arch/Core/ArchWorldHost.cs`（193 行）

```
SystemGroup 调度顺序：NetworkReceive → FixedUpdate → Update → Render → NetworkSend
```

| 特性 | 实现 |
|------|------|
| **不可重入** | `IsTicking` 标志防止递归 Tick |
| **自动排序** | 按 `[ArchSystem(group, order)]` 的 order 值升序排列 |
| **反射装配** | 扫描所有 `ArchSystemBase` 子类，按 Attribute 分组注册 |

### 3.2 LocalSimulationSystem — 客户端预测

**文件**：`Horizon.Game.ECS.Arch/Systems/LocalSimulationSystem.cs`（215 行）

`[ArchSystem(SystemGroup.FixedUpdate, order: 10)]`

| 功能 | 实现细节 |
|------|---------|
| **本地玩家判定** | `NetworkIdentityComponent.IsLocalPlayer`（修复了旧版 `entity.Id == LocalPlayerEntityId` 永远为 false 的 BUG） |
| **轻功三段跳** | 边沿触发 `JumpPressedThisFrame`（修复持续按住 3 帧耗尽的 BUG），冲量 5.5/4.5/3.5 |
| **地面约束** | `GroundHeightSampler` 委托注入（Flax `Physics.RayCast`），返回 NaN 时跳过 |
| **AOI 订阅** | chunk 边界检测 `WorldCoord.ToChunkMortonKey` → 触发 `PlayerChunkChanged` 事件 |
| **内存管理** | `_jumpCounts` 懒清理：每 60 帧扫描，移除 >600 tick 未访问条目 |
| **朝向同步** | `pred.Yaw = input.LookYaw`（修复旧版 Yaw 永远为 0 的 BUG） |

### 3.3 ReconciliationSystem — 回滚修正

**文件**：`Horizon.Game.ECS.Arch/Systems/ReconciliationSystem.cs`（225 行）

`[ArchSystem(SystemGroup.FixedUpdate, order: 20)]`

| 功能 | 实现 |
|------|------|
| **修正阈值** | `CorrectionThreshold = 0.5f`（与 MovementValidator 对齐） |
| **InputAck 处理** | 仅清理已确认输入（不重放） |
| **Correction 处理** | Drain 所有修正包仅保留最新（#13 修复：避免累积修正抖动） |
| **重放逻辑** | 从权威位置重新执行所有未确认输入（`MovementFormula.Step`） |

### 3.4 其他关键 ECS 系统

| 系统 | Order | 职责 |
|------|-------|------|
| `InputSendSystem` | FixedUpdate:30 | 打包本地输入 → 上行 InputPacket |
| `SnapshotApplySystem` | NetworkReceive:10 | 应用服务端快照到远程实体组件 |
| `InterpolationSystem` | Update:10 | 远程实体 100ms 线性插值追平 |
| `FlaxActorSyncSystem` | Render:10 | ECS 组件 → Flax Actor Transform 写回 |
| `LocalPlayerActorSyncSystem` | Render:5 | 本地玩家直写 Actor（跳过插值） |

---

## 四、AI 与 RAG 子系统

### 4.1 Horizon.AI.Kernel — Semantic Kernel 集成

**文件**：`Horizon.AI.Kernel/KernelBuilder.cs`（26 行）

```csharp
builder.AddAzureOpenAIChatCompletion(deploymentName, endpoint, apiKey);
builder.AddAzureOpenAITextEmbeddingGeneration(deploymentName, endpoint, apiKey);
```

- 使用 **Azure OpenAI**（非直连 OpenAI），通过 `SemanticKernelConfig` 配置
- 同时注册 Chat + Embedding 两个能力 → 支撑对话 + 向量化

### 4.2 RAGRetrieverGrain — Redis 向量检索

**文件**：`Horizon.Orleans.Grains/RAGRetrieverGrain.cs`（250 行）

| 参数 | 值 |
|------|-----|
| `IndexName` | `flower_doc_chunks_idx` |
| `KeyPrefix` | `flower:chunk:` |
| `EmbeddingDimension` | 1536（text-embedding-ada-002） |
| 持久化 | `[PersistentState("ragretriever", "FlowerStore")]` |

**检索流程**：
1. `OnActivateAsync` → `InitializeIndexAsync`（`FT._LIST` 检查索引是否存在）
2. 不存在 → `FT.CREATE` 创建 RediSearch 向量索引（HNSW 算法，1536 维）
3. 查询时：用户问题 → Embedding → `FT.SEARCH` KNN 检索 → Top-K 文档块
4. 检索结果 + 用户问题 → Azure OpenAI Chat → 生成回答

**设计价值**：将 RAG 封装为 Grain，天然获得 Orleans 的激活管理、持久化、单线程保证。

### 4.3 KnowledgeBaseGrain — 知识库管理

- 文档分块（chunking）→ Embedding → 写入 Redis（`flower:chunk:{id}`）
- 支持增量更新（`EnsureIndexInitialized` 标志避免重复建索引）
- 与花卉电商深度集成（种植建议、品种知识、病虫害防治）

---

## 五、花卉电商子系统

### 5.1 Grain 矩阵（25+ 个业务 Grain）

| 领域 | Grain | 职责 |
|------|-------|------|
| **商品** | FlowerProductGrain | 商品 CRUD、库存、上下架 |
| **分类** | FlowerCategoryGrain | 多级分类树 |
| **购物车** | ShoppingCartGrain | 加购/改量/结算 |
| **订单** | OrderTimeoutSchedulerGrain | 超时未支付自动取消（Reminder 驱动） |
| **支付** | PaymentTransactionGrain | 支付宝/微信双通道 |
| **结算** | SettlementGrain / BillingGrain | 商户结算、账单 |
| **对账** | ReconciliationGrain | 交易对账 |
| **物流** | ShippingAddressGrain | 收货地址 + 快递鸟 API |
| **评价** | ProductCommentGrain / TradeCommentGrain | 商品/交易双维度评价 |
| **归档** | TradeArchiveGrain | 历史交易归档 |
| **订阅** | SubscriptionGrain | 花卉订阅服务（定期配送） |
| **复购** | RepurchaseReminderGrain | 智能复购提醒 |
| **需求** | RegionDemandGrain | 区域需求分析 |
| **商户** | ShopGradeGrain / ShopBillingGrain | 商户评级/商户账单 |
| **品种** | SpeciesGrain | 花卉品种百科 |
| **模板** | ProductDescriptionTemplateGrain | AI 商品描述生成模板 |
| **IoT** | FlowerSensorDataGrain | 传感器数据（温湿度/光照/土壤） |
| **AI** | RAGRetrieverGrain / KnowledgeBaseGrain | 种植建议 RAG |

### 5.2 支付集成

**文件**：`Horizon.Orleans.Silo/Program.cs`（ConfigureApplicationServices）

```csharp
// 支付宝
services.AddSingleton<AlipayChannel>(...);  // AppId + PrivateKey + AlipayPublicKey
// 微信支付 V3
services.AddSingleton<WechatPaymentChannel>(...);  // MerchantId + V3Secret + CertSerial
// 补偿服务
services.AddHostedService<PaymentCompensationService>();  // 掉单补偿
```

- 支持**环境变量覆盖**配置（`ALIPAY_APP_ID` 等），适配容器化部署
- `PaymentCompensationService`：后台轮询未完成支付，主动查询第三方支付状态

### 5.3 花卉数据模型（Horizon.Model/Flower/）

20+ 实体类，继承体系：
- `BaseIdentityAggregateRootModel<long>`：聚合根（FlowerUser/Species/Subscription/ShopGrade 等）
- `BaseIdentityModel<long>`：普通实体（ShoppingCart/SensorReading/SettlementDetail 等）
- `ISoftDeleted`：软删除接口

---

## 六、即时通讯（IM）子系统

### 6.1 三件套架构

| 项目 | 职责 |
|------|------|
| `Horizon.IM.Message` | IM 协议定义（消息类型/群通知/已读回执） |
| `Horizon.IM.Core` | IM 业务逻辑（Handler 体系 + 适配器） |
| `Horizon.IM.Gateway` | IM 独立网关（TouchSocket TCP 服务） |

### 6.2 Handler 体系

| Handler | 职责 |
|---------|------|
| `IMMessageHandler` | 消息路由分发 |
| `IMContactHandler` | 好友/联系人管理 |
| `IMGroupHandler` | 群组 CRUD/成员管理/邀请审批 |
| `IMChatHandler` | 会话管理/消息历史/已读状态 |
| `IMGatewayHandler` | 网关连接管理/心跳/断线重连 |

### 6.3 网络适配

- `IMMessageAdapter`：继承 `CustomFixedHeaderDataHandlingAdapter`（固定头协议解析）
- `LZ4Pickler`：消息压缩（与游戏侧共享 LZ4 策略）
- 独立于游戏网关运行，支持独立扩缩容

### 6.4 数据模型

- `IMConversation`（Guid 主键）：会话实体
- `ChatGroupMember`（Guid 主键）：群成员关系
- EF Core 迁移：`InitIM` → `ExpandIMPersistenceForGrainSqlCutover` → `IMGroupModify`

---

## 七、IoT/MQTT 子系统

### 7.1 架构组件

**项目**：`Horizon.IoT.MQTT`

| 类 | 职责 |
|----|------|
| `MqttBrokerService` | 内嵌 MQTT Broker（IHostedService） |
| `MqttClientProvider` | MQTT 客户端工厂（`IMqttClientProvider`） |
| `MqttBridgeHostedService` | MQTT ↔ Orleans Grain 桥接 |
| `MqttTopicAuthorizer` | Topic 级别 ACL 授权 |
| `MqttConnectionValidator` | 连接认证（用户名/密码/Token） |
| `MqttBrokerOptions` | 配置绑定（`MqttBrokerOptions.SectionName`） |

### 7.2 与花卉电商的集成

```
传感器设备 → MQTT Broker → MqttBridgeHostedService → FlowerSensorDataGrain
                                                        ↓
                                              FlowerIoTService（启动器端）
                                                        ↓
                                              告警规则 → FlowerAlertService
```

- 传感器上报温湿度/光照/土壤数据 → 实时写入 Grain 状态
- 启动器端 `FlowerMqttClientService` 订阅设备 Topic → 实时数据大屏
- 告警规则引擎：阈值触发 → 推送通知

---

## 八、桌面启动器（耕地）

### 8.1 技术栈

**项目**：`Horizon.Game.GengDi`（221 个 .cs 文件）

- **UI 框架**：Avalonia 11.3 + FluentAvalonia（Windows 11 风格）
- **架构**：MVVM（ViewModel + View + Service + Repository）
- **本地存储**：LiteDB（`LiteDataContext`）
- **自动更新**：`AutoUpdateService` + `DownloadService`

### 8.2 功能矩阵

| 模块 | 服务/ViewModel | 功能 |
|------|---------------|------|
| **游戏启动** | GameService / GameGatewayClient / GatewayDiscoveryService | 游戏启动、网关发现、凭证注入 |
| **账户** | AccountService / LoginViewModel / RegisterViewModel | 登录/注册/Token 管理 |
| **花卉市场** | FlowerMarketService / FlowerShopService / FlowerOrderService / FlowerCartViewModel | 完整电商客户端 |
| **花卉 AI** | FlowerAIService / FlowerAIAssistantViewModel | AI 种植助手 |
| **花卉 IoT** | FlowerIoTService / FlowerMqttClientService / FlowerDataScreenViewModel | 实时传感器数据大屏 |
| **即时通讯** | ConversationService / FriendService / GroupService / ImGatewayContactClient | 完整 IM 客户端 |
| **音乐** | MusicPlayerViewModel / MusicSearchViewModel / MusicDiscoverViewModel / RealAudioEngine | 音乐播放器 |
| **天气** | WeatherService / QWeatherClient / SolarTermService / WeatherNewsService | 天气 + 节气 + 生活指数 |
| **社交** | SocialService / SocialLinkParser / SocialLinkPreviewService | 社交动态/链接预览 |
| **新闻** | NewsViewModel / NewsRepository | 新闻资讯 |
| **下载** | DownloadService / DownloadsViewModel / DownloadTaskRepository | 游戏补丁/资源下载管理 |
| **Excel 工具** | ExcelMergeService / ExcelReaderService / ExcelProcessorViewModel | Excel 合并处理工具 |

### 8.3 数据层

- `DatabaseManager`：LiteDB 数据库管理
- 12 个 Repository：Activity/DownloadTask/Game/Group/Message/News/PendingUpdate/PlayHistory/Playlist/Song/User
- `LocalPassportStore`：本地凭证持久化
- `RegionStore`：区域数据缓存

### 8.4 自定义控件

| 控件 | 功能 |
|------|------|
| `ToastContainer` / `ToastService` | 全局 Toast 通知 |
| `WeatherAnimationControl` / `WeatherIconControl` | 天气动画 |
| `WeatherMapView` / `LogisticsMapView` | 地图可视化 |
| `SwipeRevealPanel` | 滑动操作面板 |
| `VideoMessageCard` | 视频消息卡片 |
| `ConfirmDialog` / `ReviewDialog` | 对话框 |

---

## 九、数据层与持久化

### 9.1 多数据库架构

**项目**：`Horizon.Entities`

| DbContext | 数据库 | 职责 |
|-----------|--------|------|
| `BasicEntityContext` | Basic | 基础数据（用户/配置） |
| `GameEntityContext` | Game | 游戏核心数据（30+ DbSet） |
| `ArticleEntityContext` | Article | 文章/资讯 |
| `SupportsEntityContext` | Supports | 点赞/收藏 |
| `XingguangEntityContext` | Xingguang | 星光业务 |
| `FlowerEntityContext` | Flower | 花卉电商 |
| `IMEntityContext` | IM | 即时通讯 |

### 9.2 GameEntityContext 详解（301 行）

**30+ DbSet**：
- 角色系统：Users / Characters / CharacterAttributes
- 物品系统：Items / ItemAttributes / ItemTemplates / ItemGems / Materials / MaterialSynthesisLogs
- 货币/技能：Currencies / SkillTemplates / CharacterSkills / SkillAdvancePaths / SkillCustomCreates / SkillBooks
- 社交系统：ChatMessages / ChatPrivateMessages / ChatChannelSettings / ChatBlacklists / Guilds
- 经济系统：TradeLogs / Bags / SetItems
- 竞技系统：ArenaSeasons / ArenaPlayerRecords / ArenaMatchRecords
- 跨服系统：CrossServerMatches / CrossServerPlayers
- 世界同步：ChunkStates / DiffLogs（Morton 键复合主键）

**索引优化**（`GameEntityIndexConfiguration`）：
- Characters：UserId / UserId+GameId / LastLoginTime / CharacterName
- TradeLogs：SellerId / BuyerId / TradeTime
- ChatMessages：SendTime / Channel+SendTime / SenderId
- ChunkStates：复合主键 (MortonBucket, MortonKey) + MortonKey 索引
- DiffLogs：复合主键 (MortonBucket, Seq) + MortonKey+Seq / Seq / CreatedAt 索引

### 9.3 设计时/运行时双模式

```csharp
// 设计时：IDesignTimeDbContextFactory → repository.json → UseSqlServer
// 运行时：DI 注入 DbContextOptions → DatabaseOptions 配置
```

- `DesignTimeContextChecker.IsDesignTime()`：检测 EF 工具调用
- `FastActivator.Create<T>()`：反射快速实例化（避免 Activator.CreateInstance 性能损耗）

### 9.4 ORM 策略

| 场景 | 技术 |
|------|------|
| 复杂查询/迁移 | EF Core 10.0.2（Code-First + Migration） |
| 高性能批量读写 | Dapper 2.1.66 |
| Grain 状态持久化 | Orleans Redis GrainStorage（CustomGrainStorageSerializer） |
| 缓存/在线状态 | StackExchange.Redis + CSRedis |

### 9.5 AutoMapper 映射

**项目**：`Horizon.Mapper`

| Profile | 映射范围 |
|---------|---------|
| `GameProfile` | 游戏实体 ↔ DTO |
| `ArticleProfile` | 文章实体 ↔ DTO |
| `BasicProfile` | 基础实体 ↔ DTO |

### 9.6 Redis 存储策略

**项目**：`Horizon.Strategy.Storage.Redis`

| 类 | 职责 |
|----|------|
| `RedisCache` | 通用缓存（实现 `ICache`，含 `RedisLock` 分布式锁） |
| `RedisConnection` | 连接管理单例 |
| `RedisCharacterPresenceStore` | 角色在线状态（TTL 90s 心跳） |
| `RedisCharacterFingerprintStore` | 角色指纹（TTL 5min，离线清理） |
| `GatewayRegistry` | 网关注册表（服务发现） |
| `RedisGuildRepository` | 公会数据缓存 |

---

## 十、工具链

### 10.1 UE5ToFlaxConverter — 资产转换管线

**结构**：3 个子项目

| 项目 | 职责 |
|------|------|
| `UE5ToFlaxConverter.Core` | 转换核心（Reader/Writer/Mapper/Pipeline） |
| `UE5ToFlaxConverter.Cli` | 命令行工具 |
| `UE5ToFlaxConverter.UI` | Avalonia GUI（MVVM） |
| `UE5ToFlaxConverter.Tests` | 单元测试 |

**转换管线**（`ConversionPipeline`）：

```
UE5 .uasset → Reader → Intermediate Model → Writer → Flax 资产
```

| Reader | 输入 | Writer | 输出 |
|--------|------|--------|------|
| `MeshReader` | StaticMesh/SkeletalMesh | `ModelWriter` | Flax Model（含 LOD/材质/骨骼） |
| `AnimationReader` | AnimationSequence | `AnimationWriter` | Flax Animation |
| `ParticleReader` | Niagara/ParticleSystem | `ParticleWriter` | Flax Particle |
| `GasReader` | GameplayAbilitySystem | `GasWriter` | Flax 技能数据 |
| `UassetProvider` | .uasset 二进制解析 | `EditorScriptWriter` | 编辑器脚本 |

**中间模型**（Intermediate）：
- `IntermediateMesh`：MeshLOD / MeshSection / MeshMaterial / MeshBone / BoneInfluence / MorphTarget
- `IntermediateAnimation`：动画曲线/关键帧
- `IntermediateGas`：GAS 技能数据
- `IntermediateParticleSystem`：粒子系统

**映射层**：
- `TypeMap`：UE5 类型 → Flax 类型映射
- `MappingRules`：转换规则引擎
- `GameplayTagMapper`：GameplayTag 映射

### 10.2 TypeDumper — 类型导出

- 从编译后的 DLL 导出类型信息
- 用于 UE5 蓝图 ↔ C# 类型同步（迁移残留）

### 10.3 构建脚本

| 脚本 | 功能 |
|------|------|
| `build-installer.ps1` | Inno Setup 安装包构建 |
| `GengDi.Setup.iss` | Inno Setup 配置（启动器安装器） |

---

## 十一、Orleans Silo 宿主配置

### 11.1 启动流程（Program.cs，866 行）

```
Main → LoadConfig → StartSilo → ConfigureOrleansCluster
                                → ConfigureOrleansStorage
                                → ConfigureOrleansServices
                                → ConfigureApplicationServices
                                → Build → StartAsync → PostStartupDiagnostics
```

### 11.2 集群配置

| 配置项 | 实现 |
|--------|------|
| **集群存储** | Redis Clustering（主方案）/ SQL Server（备份方案，注释保留） |
| **Grain 存储** | 7 个命名存储：Default / PubSub / GameStore / PassportStore / WorldSqlStore / FlowerStore / AIStore |
| **Reminder** | Redis Reminder Service |
| **Stream** | Memory Stream Provider（`CommonMessageStreamProvider`） |
| **版本策略** | `BackwardCompatible` + `AllCompatibleVersions`（支持滚动升级） |
| **序列化** | Orleans Serializer + NewtonsoftJson（`Horizon.Share` 命名空间） |

### 11.3 Grain 调用管线（5 层 Filter）

```
请求 → RetryFilter → CorrelationIdFilter → GrainExceptionFilter
     → GrainCallValidationFilter → ClientConnectionTrackingFilter → Grain 方法
```

| Filter | 职责 |
|--------|------|
| `RetryFilter` | 瞬态故障自动重试 |
| `CorrelationIdFilter` | 分布式追踪（CorrelationId 传播） |
| `GrainExceptionFilter` | 统一异常处理（业务异常 vs 系统异常） |
| `GrainCallValidationFilter` | 请求参数验证 |
| `ClientConnectionTrackingFilter` | 客户端连接跟踪/统计 |

### 11.4 后台服务矩阵

| 服务 | 职责 |
|------|------|
| `DelayedServiceInitializer` | 非关键服务延迟初始化（启动优化） |
| `TaskStatusMonitor` / `TaskStatusReporterService` | 任务状态监控/上报 |
| `ClientConnectionTracker` / `ClientConnectionMonitorService` | 客户端连接跟踪/统计 |
| `SiloLifecycleLogger` | 生命周期日志 |
| `StartupReportService` | 启动报告 |
| `FlowerUserSyncStartupService` | 花卉用户数据同步 |
| `OrderTimeoutStartupService` | 订单超时定时任务激活 |
| `PaymentCompensationService` | 支付掉单补偿 |
| `MqttBrokerService` / `MqttBridgeHostedService` | MQTT Broker + 桥接 |
| `OpenTelemetry` | APM + Prometheus 指标导出（端口 9464） |

### 11.5 启动优化

- **并行端口获取**：`Task.WhenAll` 并行探测 3 组端口（11111/30000/8880）
- **延迟初始化**：`EnableParallelInitialization` 配置开关，非关键服务后台注册
- **后台诊断**：`RunPostStartupDiagnosticsAsync` 不阻塞启动
- **EF 设计时检测**：`IsEfDesignTimeInvocation()` 避免 EF 工具触发 Silo 启动

---

## 十二、WebApi 与 WebAdmin

### 12.1 Horizon.WebApi

**25+ Controller**，继承体系：
- `OrleansControllerBase`：通过 Orleans Client 调用 Grain（大部分 Controller）
- 标准 `ControllerBase`：纯 HTTP 接口

| 领域 | Controller |
|------|-----------|
| 账户 | AccountController |
| 游戏 | GameUserRoleController / GamesController |
| 花卉种植 | FlowerPlantingController（含 SensorData/Cost/Yield/Advice 子 Controller） |
| 花卉商城 | MiniProgramFlowerController（小程序端） |
| 花卉管理 | FlowerShopController / FlowerOrderController / FlowerSpeciesController |

### 12.2 Horizon.WebAdmin

- 后台管理系统（独立部署）
- 通过 Orleans Client 调用管理 Grain
- 支持角色管理/封禁/数据查询/运营工具

---

## 十三、跨子系统架构关系图

```
┌─────────────────────────────────────────────────────────────────────┐
│                        客户端层                                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐  │
│  │ Flax Engine  │  │  耕地启动器   │  │  小程序 (MiniProgram)    │  │
│  │ (3D 游戏)    │  │ (Avalonia)   │  │  (花卉电商)              │  │
│  └──────┬───────┘  └──────┬───────┘  └────────────┬─────────────┘  │
└─────────┼──────────────────┼───────────────────────┼────────────────┘
          │ TouchSocket TCP  │ HTTP/MQTT            │ HTTP
          ▼                  ▼                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        网关层                                        │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐  │
│  │  Game Gateway    │  │  IM Gateway      │  │  WebApi          │  │
│  │  (TouchSocket)   │  │  (TouchSocket)   │  │  (ASP.NET Core)  │  │
│  └────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘  │
└───────────┼──────────────────────┼──────────────────────┼───────────┘
            │ Orleans Client       │ Orleans Client       │ Orleans Client
            ▼                      ▼                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    Orleans Silo 集群                                  │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐  │
│  │ ZoneShard   │ │ Character   │ │ Flower 25+  │ │ IM/AI/IoT   │  │
│  │ (空间权威)  │ │ (角色RPG)   │ │ (电商Grain) │ │ (平台Grain) │  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └──────┬──────┘  │
└─────────┼────────────────┼───────────────┼───────────────┼─────────┘
          │                │               │               │
          ▼                ▼               ▼               ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        数据层                                        │
│  ┌──────────┐  ┌──────────────┐  ┌──────────────────────────────┐  │
│  │  Redis   │  │  SQL Server  │  │  Azure OpenAI (AI/RAG)       │  │
│  │(集群/缓存│  │  (7 个 DB)   │  │  + MQTT Broker (IoT)         │  │
│  │/Grain存储│  │              │  │                              │  │
│  │/向量检索)│  │              │  │                              │  │
│  └──────────┘  └──────────────┘  └──────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 十四、代码质量与工程实践评估

### 14.1 优势

| 维度 | 评价 |
|------|------|
| **架构分层** | 严格的 L0-L4 单向依赖，契约与实现物理分离 |
| **注释质量** | 关键修复均有详细注释（如 `#13 修复`、`修复：边沿触发`），可追溯设计决策 |
| **防御性编程** | 租约兜底、Despawn 重试、null 检测、TTL 自动过期 |
| **协议演进纪律** | 版本化管理（v1→v6），每版有明确修复目标 |
| **测试覆盖** | Gateway.Tests / IM.Gateway.Tests / GengDi.Tests / UE5ToFlaxConverter.Tests / PerformanceTests |
| **可观测性** | OpenTelemetry + Prometheus + CorrelationId 分布式追踪 + 结构化 JSON 日志 |
| **启动优化** | 并行端口探测、延迟初始化、后台诊断、EF 设计时检测 |

### 14.2 技术债与风险

| 优先级 | 问题 | 影响范围 |
|--------|------|---------|
| 🔴 高 | 两套角色系统语义重叠（ZoneShard 瞬态 vs Character 持久） | 状态一致性 |
| 🔴 高 | 单 shard 硬编码（`DefaultShardId=0`） | 水平扩展 |
| 🟡 中 | 包版本分散（无 Directory.Packages.props） | 版本漂移 |
| 🟡 中 | SQL Server 备份方案以注释形式保留（200+ 行） | 代码噪音 |
| 🟡 中 | 协议版本不匹配仅告警不拒绝 | 生产安全 |
| 🟢 低 | UE5 迁移残留（TypeDumper/CopyToUE5） | 维护成本 |
| 🟢 低 | 内网 IP/密码硬编码在默认值中 | 安全性 |

### 14.3 代码规模统计

| 子系统 | 核心文件数 | 代码行（估） |
|--------|-----------|-------------|
| 服务端 Grain | 104+ | ~30,000 |
| 游戏核心（Game.Core） | 30+ | ~8,000 |
| ECS（Arch） | 20+ | ~4,000 |
| 网关 | 15+ | ~5,000 |
| 协议（Message） | 20+ | ~4,000 |
| 启动器（GengDi） | 221 | ~40,000 |
| 花卉电商（Grain+Model） | 50+ | ~12,000 |
| IM 三件套 | 30+ | ~6,000 |
| IoT/MQTT | 10+ | ~2,000 |
| 数据层（Entities+Model） | 60+ | ~10,000 |
| 工具链（Converter） | 33 | ~6,000 |
| WebApi/WebAdmin | 40+ | ~8,000 |
| **总计** | **600+** | **~135,000** |

---

## 十五、总结

混沌世界解决方案是一个**以 MMORPG 为核心场景的分布式应用平台**，其代码级实现展现了以下工程特质：

1. **Orleans 虚拟 Actor 贯穿始终**：从游戏核心（ZoneShard/Character）到电商（25+ Grain）到 AI（RAG）到 IoT，统一编程模型
2. **混合 Netcode 达到工业级**：预测/权威/插值/反作弊/带宽优化/弱网降级/协议版本化一应俱全
3. **平台化野心有实质支撑**：花卉电商不是 Demo，而是含支付/结算/对账/物流/AI/IoT 的完整商业系统
4. **工程务实主义**：在引擎限制（Flax 禁 NuGet）、分布式故障（租约兜底）、反压（DropOldest）等现实约束下做出合理妥协
5. **持续演进纪律**：协议版本化、BUG 修复注释、文档与代码同步，体现成熟的工程文化

**最大架构风险**：两套角色系统的语义边界需要在下一阶段的架构演进中明确划定。
