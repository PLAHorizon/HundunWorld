# 混沌世界项目 - 后续开发路线图

**文档日期**: 2026年2月8日  
**最后更新**: 2026年2月8日（Phase 3.3增强/3.5 AreaGrain+ActivityGrain实现后更新）  
**基于**: 完整源代码审查  
**文档版本**: v1.5

---

## 📋 概述

本文档基于对混沌世界（HundunWorld）全部源代码的深入审查，提供详细的后续开发路线图。项目由三大部分组成：

1. **Orleans服务端** — 分布式Actor模型后端（Grains, Silo, Gateway）
2. **Flax引擎客户端** — 含ECS架构的游戏客户端（200+文件）
3. **共享基础设施** — 数据模型、消息协议、缓存、数据库（Model, Share, Core, Entities）

---

## 📊 当前状态总览

### 已完成模块

| 模块 | 完成度 | 说明 |
|------|--------|------|
| 安全加固（Phase 0） | ✅ 100% | PBKDF2哈希、环境变量、.gitignore |
| 会话管理 | ✅ 100% | Redis持久化、24小时TTL、分布式支持 |
| 认证系统 | ✅ 90% | PassportGrain登录/注册/改密/注销 |
| ECS框架 | ✅ 85% | Arch.Core引擎、14个系统组件 |
| UI框架 | ✅ 80% | 状态管理、响应式布局、性能监控 |
| 网络通信 | ✅ 75% | TCP客户端、消息处理器、协议适配 |
| 战斗系统 | ⚠️ 75% | 五行相克、伤害计算、效果系统、Energy分离、闪避格挡、技能冷却 |
| 社交系统 | ⚠️ 75% | SocialGrain好友管理、GuildGrain公会管理、TeamGrain组队系统、消息频道系统 |
| 游戏系统 | ⚠️ 80% | 背包/装备/技能/合成系统基础实现、技能树依赖验证、合成品质系统 |
| 区域管理 | ⚠️ 60% | AreaGrain场景实例管理、跨服传送、副本入口 |
| 活动系统 | ⚠️ 60% | ActivityGrain活动调度、奖励发放、参与记录 |
| 角色渲染 | ⚠️ 55% | MetaHuman集成、材质编辑（缺动画完善） |
| 文档 | ✅ 95% | README、安全指南、迁移指南、监控指南 |
| 代码质量（Phase 1.1） | ✅ 100% | Cache修复、死代码清理、CombatCalculator提取 |
| 测试基础设施（Phase 1.2） | ✅ 90% | 272个单元测试（SecurePasswordHasher/SessionManager/CombatCalculator/GameSystem/SocialSystem/TeamSystem/GameServer/AreaActivity） |
| CI/CD（Phase 1.3） | ✅ 100% | GitHub Actions工作流配置 |

### 缺失模块

| 模块 | 状态 | 优先级 |
|------|------|--------|
| 监控告警 | 🟡 基础完成 | P1 |
| 任务/副本系统 | 🔴 仅接口定义 | P2 |
| 社交系统（好友/公会/组队） | 🟡 基础实现完成 | P2 |
| 消息频道系统 | 🟡 基础实现完成 | P2 |
| 游戏系统（背包/技能/合成） | 🟡 增强实现完成 | P2 |
| 服务器状态管理 | 🟡 基础实现完成 | P2 |
| 区域管理系统 | 🟡 基础实现完成 | P2 |
| 活动系统 | 🟡 基础实现完成 | P2 |
| 交易/市场系统 | 🔴 未开始 | P3 |

---

## ✅ 第一阶段：代码质量与测试（已完成）

> **完成日期**: 2026年2月8日  
> **状态**: 已完成

### 1.1 修复已知代码缺陷 ✅

#### PassportGrain.cs 缺陷修复

| 问题 | 位置 | 修复方案 | 状态 |
|------|------|---------|------|
| 空catch块吞没Base64解码异常 | 行114-118, 284-289 | 添加日志记录：`_logger.LogDebug("Base64解码失败，使用原始密码")` | ✅ 已修复 |
| CancelPassportAsync缺少null检查 | 行545-546 | 添加 `if (passport == null) return false;` | ✅ 已修复 |
| 注释掉的死代码（goto语句） | 行476-519 | 删除整块注释代码，保留TODO占位 | ✅ 已修复 |
| Console.WriteLine残留 | 行471 | 替换为 `_logger.LogDebug(...)` | ✅ 已修复 |
| CancelCreatePassportIdAsync冗余await | 行529 | 移除 `await Task.CompletedTask` | ✅ 已修复 |

#### CombatGrain.cs 缺陷修复

| 问题 | 位置 | 修复方案 | 状态 |
|------|------|---------|------|
| `new Random()` 线程不安全 | 行191 | 使用 `Random.Shared.NextDouble()` (.NET 6+) | ✅ 已修复 |
| Health用作Energy的消耗 | 行240, 253 | 在CombatInfo中添加Energy/MaxEnergy属性 | ✅ 已修复 |
| 字符串插值日志（性能问题） | 全文件 | 使用结构化日志 `_logger.LogInformation("消息 {Param}", value)` | ✅ 已修复 |
| effectId仅用Guid首字节 | 行492 | 使用`BitConverter.ToUInt64`生成完整唯一ID | ✅ 已修复 |
| `_characterContext` 使用static | 行148 | 改为 `readonly` 实例字段 | ✅ 已修复 |

#### Cache.cs 缺陷修复

| 问题 | 位置 | 修复方案 | 状态 |
|------|------|---------|------|
| RemoveAsync未正确await | ~行127 | 移除不必要的lock，正确await异步调用 | ✅ 已修复 |
| 冗余 `await Task.CompletedTask` | ~行130 | 移除不必要的代码 | ✅ 已修复 |

#### 架构改进

| 改进 | 说明 | 状态 |
|------|------|------|
| 提取CombatCalculator | 将战斗纯计算逻辑提取为独立静态类，提升可测试性 | ✅ 已完成 |
| CombatGrain重构 | 使用CombatCalculator处理五行相克、防御减免、暴击、复活计算 | ✅ 已完成 |

### 1.2 测试基础设施 ✅

#### 技术栈
```
xUnit 2.9.3 — 测试框架
Moq 4.20.72 — Mock框架  
coverlet 6.0.4 — 代码覆盖率
```

#### 测试项目

**已存在**: `Horizon.Game.Gateway.Tests/`（9个测试文件，272个测试用例）

| 测试文件 | 测试数量 | 覆盖内容 |
|---------|---------|---------|
| SecurePasswordHasherTests.cs | 20 | 密码哈希、验证、强度检查 |
| SessionManagerTests.cs | 31 | 会话创建、获取、终止、验证、刷新 |
| CombatGrainTests.cs | 46 | 五行相克、防御减免、暴击、复活、数据模型 |
| CombatCalculatorExtendedTests.cs | 23 | 闪避系统、格挡系统、技能冷却、CombatInfo扩展属性 |
| GameSystemStateTests.cs | 31 | 背包状态、技能状态、合成状态、物品信息 |
| SocialSystemStateTests.cs | 46 | 社交状态、公会状态、频道状态、路由器状态 |
| TeamSystemStateTests.cs | 12 | 队伍状态、成员管理、队长转移、解散 |
| GameServerStateTests.cs | 15 | 服务器状态、在线人数、维护管理、负载监控 |
| AreaActivityStateTests.cs | 48 | 区域状态、场景实例、传送、活动管理、参与记录、循环依赖检测、合成品质 |

#### 测试覆盖率现状

| 模块 | 当前 | 目标（Phase 3） |
|------|------|----------------|
| SecurePasswordHasher | ~95% | 98% |
| SessionManager | ~80% | 90% |
| CombatCalculator | ~95% | 98% |
| CombatInfo/CombatState | ~90% | 95% |
| PassportGrain | 0% | 85% |
| CharacterGrain | 0% | 75% |

### 1.3 CI/CD流程 ✅

```yaml
# 已配置的 GitHub Actions 工作流 (.github/workflows/ci.yml)
工作流内容:
  ✅ dotnet restore → dotnet build → dotnet test
  ✅ 测试结果报告上传（trx格式）
  ✅ PR和Push触发自动运行
  □ 代码覆盖率报告（coverlet + Codecov）— 后续增强
  □ CodeQL安全扫描 — 后续增强
  □ 依赖项漏洞检查（Dependabot）— 后续增强
```

---

## 🟡 第二阶段：监控与可观测性（部分已完成）

> **状态**: 基础设施已完成，增强项进行中

### 2.1 OpenTelemetry增强

**现有基础**: OpenTelemetry 1.15.0已集成，Prometheus导出已配置

```
✅ Silo自定义指标（HorizonMetrics.cs）
  - Grain激活/调用计数和耗时
  - 认证成功/失败率
  - 会话活跃数量/创建/销毁
  - 任务运行/失败计数
  - 数据库查询计数和耗时
  - 战斗攻击/技能/死亡计数和处理耗时

✅ Gateway自定义指标（GatewayMetrics.cs）
  - 连接数实时统计
  - 消息接收/发送/处理耗时
  - 网络延迟和字节统计
  - Orleans调用计数/错误

✅ 基础告警规则（alert_rules.yml）
  - 服务可用性告警
  - Grain性能告警
  - 认证安全告警
  - Gateway连接告警
  - .NET运行时告警
  - 任务监控告警
```

### 2.2 结构化日志改进

```
✅ 替换Console.WriteLine为ILogger调用
✅ 替换字符串插值日志为结构化日志模板
□ 统一日志格式（JSON结构化输出）
□ 添加全局关联ID（CorrelationId）
□ 集成Seq社区版（开发环境日志聚合）
```

### 2.3 Grafana仪表板完善

**现有**: `monitoring/grafana/hundunworld-dashboard.json`

```
□ 完善Silo性能面板
□ 添加Gateway连接池面板
□ 添加认证流程面板
□ 添加战斗系统性能面板
□ 配置告警通知渠道（邮件/webhook）
```

---

## 🔵 第三阶段：服务端核心功能（建议6-8周）

### 3.1 完善战斗系统 Grain（2周）

#### CombatGrain增强

```
✅ 添加Energy/Mana属性到CombatInfo
  - 技能消耗从Energy扣除（而非Health）
  □ 自然回复机制（每秒回复比例）
  
✅ 暴击系统完善
  - 角色暴击率属性（CritRate）
  - 暴击伤害加成属性（CritDamageMultiplier）
  - 使用Random.Shared替代new Random()

✅ 闪避和格挡系统
  - 基础闪避率计算（RollDodge）
  - 格挡减伤公式（ApplyBlock）
  - 响应中标记IsDodged/IsBlocked

✅ 技能冷却管理
  - 服务端冷却时间验证（IsSkillReady）
  - 技能冷却进度查询（GetRemainingCooldown）
  - CombatInfo.SkillCooldowns冷却记录
  □ 全局冷却时间(GCD)控制

□ 战斗日志系统
  - 记录战斗流水
  - 伤害统计聚合
  - 战斗回放数据
```

#### 五行系统深化

```
□ 五行属性影响扩展
  - 金：暴击率增加、物理伤害加成
  - 木：生命恢复、持续治疗效果
  - 水：闪避增加、冰冻控制效果
  - 火：燃烧DOT、范围伤害加成
  - 土：防御增加、护盾效果

□ 五行连携系统
  - 相生关系加成（金生水、水生木...）
  - 组队五行匹配加成
  - 五行共鸣技能触发
```

### 3.2 社交系统 Grain 实现（2周）

**现有接口**: `ISocialSystemGrains.cs`（ISocialGrain, IGuildGrain, IMapGrain）

```
✅ SocialGrain实现
  - 好友请求/接受/拒绝
  - 好友列表查询（在线状态）
  - 黑名单管理
  - 私聊消息转发

✅ GuildGrain实现
  - 门派/公会创建
  - 成员招募/审核/退出
  - 职位系统（帮主、副帮主、长老、精英、成员）
  - 公会资源管理

✅ TeamGrain实现
  - 组队创建/加入/退出
  - 队长转移/踢出成员/解散队伍
  - 队伍信息查询
  □ 队伍状态同步
  □ 组队副本入口
```

### 3.3 游戏系统 Grain 完善（2周）

**现有接口**: `IGameSystemGrains.cs`（IInventoryGrain, ISkillGrain, ICraftingGrain）

```
✅ InventoryGrain实现
  - 背包物品增删改查
  - 物品堆叠和拆分
  - 物品使用（消耗品）
  - 背包容量管理
  ✅ 装备穿戴/卸下

✅ SkillGrain实现
  - 技能学习/遗忘
  - 技能升级系统
  - 技能冷却验证和施放
  ✅ 技能树依赖验证（含循环依赖检测）
  ✅ 技能配点重置（ResetAllSkillsAsync）

✅ CraftingGrain实现
  - 配方学习
  - 材料检查
  ✅ 材料合成配方执行（CraftItemAsync）
  □ 五行炼制系统
  ✅ 制作概率和品质（CalculateCraftingQuality，5级品质系统）
  ✅ 制作历史记录（含品质追踪）
```

### 3.4 消息频道系统（1周）

**现有接口**: `IMessageChannelGrains.cs`

```
✅ 世界频道
  - 全服广播消息
  - 频道订阅/取消订阅
  - 消息缓存管理

✅ 门派频道
  - 公会成员专属频道
  - 成员管理（添加/移除）

✅ 组队频道
  - 队伍内即时通讯
  - 成员管理

✅ 系统频道
  - 系统公告
  - 系统消息广播

✅ 消息路由器
  - 单条消息路由
  - 批量消息路由
  - 路由统计

□ 消息速率限制
□ 敏感词过滤
□ 首杀/活动通知
```

### 3.5 GameGrain完善（1周）

**现有**: GameGrain`GetServerListAsync`方法 + GameServerGrain服务器状态管理

```
✅ 服务器状态管理（GameServerGrain）
  - 服务器负载监控（CPU/内存/延迟）
  - 在线人数统计（玩家上下线跟踪）
  - 服务器维护状态（设置/退出维护）
  - 服务器初始化和自动状态检测

✅ 区域管理（AreaGrain）
  - 场景实例创建/销毁
  - 玩家进入/离开场景实例
  - 跨服传送逻辑
  - 区域信息查询

✅ 活动系统框架（ActivityGrain）
  - 活动创建/结束
  - 玩家参与/退出
  - 活动奖励发放
  - 活动参与记录
  - 自动活动状态更新
```

---

## 🟢 第四阶段：客户端功能完善（建议4-6周）

### 4.1 战斗特效与动画（2周）

**现有**: 基础框架已搭建，大量TODO待完成

```
□ 五行技能特效
  - 金系：剑气/金光粒子效果
  - 木系：藤蔓/生命光环效果
  - 水系：水流/冰晶效果
  - 火系：火焰/爆炸效果
  - 土系：岩石/护盾效果

□ 技能动画完善
  - SkillAnimationController动画绑定
  - 攻击/施法动画状态机
  - 受击/死亡动画
  - 技能蓄力动画

□ 战斗反馈系统
  - 伤害数字弹出（DamageNumberSystem已有框架）
  - 暴击特效
  - 屏幕震动（CameraShakeSystem已有框架）
  - 击杀特写
```

### 4.2 网络同步完善（1周）

**现有TODO**: `TODO: 使用网络系统发送移动数据`

```
□ 移动同步
  - 客户端预测+服务端验证
  - 位置插值和外推
  - 移动速度校验（防外挂）

□ 技能同步
  - SkillSyncHandler完善
  - 技能施放确认
  - 技能打断同步

□ AOI系统完善
  - AoiManager优化
  - 视野范围管理
  - 实体进出视野通知
```

### 4.3 UI系统完善（2周）

**现有**: 60+文件的完整UI框架

```
□ 背包UI完善
  - 物品拖拽（InventoryUI已有框架）
  - 物品筛选和排序
  - 装备对比面板
  - 批量操作

□ 技能栏完善
  - 技能图标加载（SkillBarUI已有框架）
  - 技能拖拽绑定
  - 冷却动画
  - 快捷键自定义

□ 角色面板
  - 属性详情展示
  - 装备面板
  - 五行属性雷达图
  - 战力评分

□ 社交UI
  - 好友列表面板
  - 聊天窗口
  - 公会管理面板
  - 组队邀请面板
```

### 4.4 场景优化（1周）

```
□ ChunkBasedSceneSystem完善
  - 分块加载/卸载逻辑
  - LOD级别切换
  - 异步资源加载

□ 世界地图系统
  - 小地图渲染
  - 传送点管理
  - 任务标记显示
```

---

## 🟣 第五阶段：性能与稳定性（建议3-4周）

### 5.1 服务端性能优化

```
□ Orleans Grain优化
  - Grain状态序列化优化（MemoryPack已使用）
  - 减少不必要的WriteStateAsync调用
  - Grain激活超时配置调整
  - 热点Grain分散策略

□ 数据库优化
  - EF Core查询优化（减少N+1问题）
  - 索引添加（PassportId, UserId等高频查询字段）
  - 读写分离配置
  - 连接池优化

□ Redis缓存优化
  - 缓存命中率监控
  - 热点数据预加载
  - 缓存穿透保护
  - 缓存一致性策略
```

### 5.2 客户端性能优化

```
□ 渲染优化
  - 材质LOD和合批
  - 遮挡剔除
  - 粒子系统性能预算

□ 内存优化
  - MemoryOptimizationManager完善
  - 资源池化（对象池）
  - 纹理流送

□ 网络优化
  - 消息合批发送
  - 压缩优化
  - 断线重连机制
```

### 5.3 压力测试

```
□ 测试场景设计
  - 万人同服模拟
  - 大规模战斗（50v50）
  - 高并发登录测试

□ 性能基线建立
  - 单Silo承载容量
  - 消息处理延迟基线
  - 数据库QPS极限

□ 容量规划
  - 集群扩展策略
  - 自动扩缩容配置
  - 资源使用监控
```

---

## 📌 优先级排序与时间线

```
2026年2月中旬  ✅ Phase 0: 安全加固完成
               ✅ Phase 1.1: 代码缺陷修复完成
               ✅ Phase 1.2: 测试基础设施建设完成（272个测试）
               ✅ Phase 1.3: CI/CD流程建立完成
               ✅ Phase 3.1: 战斗系统增强（闪避/格挡/暴击/冷却）
               ✅ Phase 3.2: 社交系统基础实现（SocialGrain/GuildGrain/TeamGrain）
               ✅ Phase 3.3: 游戏系统Grain实现（背包/装备/技能树/合成品质）
               ✅ Phase 3.4: 消息频道系统实现（频道/路由/广播）
               ✅ Phase 3.5: GameServerGrain/AreaGrain/ActivityGrain实现
               📍 当前位置（2026-02-08）
               ↓
2026年3月下旬  ┌─ Phase 2: 监控可观测性
               │
2026年4-5月    ├─ Phase 3: 服务端核心功能完善
               │    ├─ 3.1 战斗系统完善（五行深化、战斗日志）
               │    └─ 3.3 五行炼制系统
               │
2026年5-6月    ├─ Phase 4: 客户端功能完善
               │    ├─ 4.1 战斗特效与动画
               │    ├─ 4.2 网络同步
               │    ├─ 4.3 UI系统完善
               │    └─ 4.4 场景优化
               │
2026年7月      ├─ Phase 5: 性能与稳定性
               │
2026年8月      🎯 公开测试准备就绪
```

---

## 📐 架构改进建议

### 短期改进（Phase 1-2）

1. **~~移除CombatGrain中的static字段~~** ✅ 已完成
   ```csharp
   // 已修复：改为readonly实例字段
   private readonly IDataContext<GameEntityContext, CharacterEntity, long> _characterContext;
   ```

2. **提取CombatCalculator** ✅ 已完成
   ```csharp
   // 纯计算逻辑提取为独立静态类
   public static class CombatCalculator
   {
       public static float GetWuxingMultiplier(...);
       public static float CalculateDefenseReduction(...);
       public static float CalculateWuxingDamage(...);
       public static float ApplyCriticalDamage(...);
       public static float ClampHealth(...);
       public static float CalculateResurrectHealth(...);
   }
   ```

3. **统一异常处理模式**
   ```csharp
   // 建议在Grain基类中添加标准化的异常处理
   // 避免每个方法重复try-catch-log模式
   ```

3. **添加请求验证中间件**
   ```
   □ 统一的DTO验证（FluentValidation）
   □ 请求参数范围检查
   □ 防止空引用传入Grain
   ```

### 中期改进（Phase 3-4）

4. **事件驱动架构**
   ```
   □ 使用Orleans Stream进行事件发布
   □ 解耦战斗结果通知与UI更新
   □ 异步处理非关键路径（日志、统计）
   ```

5. **Grain接口版本管理**
   ```
   □ 为Grain接口添加版本标记
   □ 实现滚动升级支持
   □ 保持接口向后兼容
   ```

### 长期改进（Phase 5+）

6. **微服务拆分准备**
   ```
   □ 认证服务独立
   □ 聊天服务独立
   □ 战斗计算服务独立
   □ 使用Orleans Streams通信
   ```

---

## 🔒 安全持续改进

```
□ 定期安全审计（每季度）
□ GitHub Dependabot自动依赖更新
□ CodeQL静态分析集成到CI
□ 敏感数据加密存储（Azure Key Vault / AWS Secrets Manager）
□ API速率限制（反DDoS）
□ 游戏协议加密（TLS 1.3）
□ 反作弊基础设施（服务端校验框架）
```

---

## 📝 本次代码审查发现的具体修复清单

以下是本次审查中发现并直接修复的代码问题：

### ✅ 已修复

1. **PassportGrain.cs** — 空catch块添加日志记录
2. **PassportGrain.cs** — CancelPassportAsync添加null检查
3. **PassportGrain.cs** — Base64Decode中Console.WriteLine替换为ILogger
4. **PassportGrain.cs** — 删除CreatePassportIdAsync中注释掉的死代码（含goto语句）
5. **PassportGrain.cs** — 移除CancelCreatePassportIdAsync中冗余await Task.CompletedTask
6. **CombatGrain.cs** — `new Random()` 替换为 `Random.Shared`
7. **CombatGrain.cs** — static字段改为readonly实例字段
8. **CombatGrain.cs** — 字符串插值日志替换为结构化日志
9. **CombatGrain.cs** — 添加Energy/MaxEnergy属性，技能消耗从Energy扣除
10. **CombatGrain.cs** — effectId生成改为BitConverter.ToUInt64完整唯一ID
11. **CombatGrain.cs** — 提取CombatCalculator纯计算逻辑类
12. **Cache.cs** — 修复RemoveAsync异步等待问题，移除不必要的lock

### ⏳ 建议后续修复

1. GameGrain — 添加错误处理和日志
2. CharacterGrain — DateTime.UtcNow.Ticks修复为DateTime类型
3. PassportGrain — 添加更多单元测试覆盖（Orleans TestKit集成）

---

**文档结束**

*本文档由GitHub Copilot AI Agent基于完整源代码审查生成。*
