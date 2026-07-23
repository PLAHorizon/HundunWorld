# 客户端功能清单与开发计划

> **最后更新**：2026-07-22 · 配套文档：[CLIENT](./CLIENT.md) · [ARCHITECTURE](./ARCHITECTURE.md) · [TECH_DEBT](./TECH_DEBT.md)

---

## 一、已实现功能清单

### 1. 核心架构层

| 模块 | 状态 | 关键文件 | 说明 |
|------|------|----------|------|
| 双世界ECS架构 | ✅ 已完成 | `ECSUpdateDriver.cs`, `ArchWorldHost` | Flax Actor视觉层 + Arch ECS逻辑层并存 |
| 网络管理器 | ✅ 已完成 | `Network/NetworkManager.cs` (1449行) | TouchSocket TCP长连接、消息路由、心跳、重连 |
| 消息协议 | ✅ 已完成 | `HorizonMessageAdapter.cs` | 8字节头 + MemoryPack + LZ4压缩 |
| 消息处理器 | ✅ 已完成 | 26个Handler | 认证/角色/战斗/社交/同步全覆盖 |
| 客户端预测 | ✅ 已完成 | `LocalSimulationSystem`, `ReconciliationSystem` | CSP + 快照插值混合Netcode |
| 同步握手 | ✅ 已完成 | `SendSyncHandshakeAsync` | 进入游戏世界时的CSP握手流程 |
| AOI订阅 | ✅ 已完成 | `SendSubscriptionUpdateAsync` | 跨Chunk边界的兴趣区域订阅 |

### 2. 角色系统

| 功能 | 状态 | 关键文件 | 说明 |
|------|------|----------|------|
| 角色管理器 | ✅ 已完成 | `UI/Character/CharacterManager.cs` | 角色列表/选择/创建/删除/进入游戏 |
| 角色移动 | ✅ 已完成 | `PlayerController.cs` | WASD + 点击移动 + 本地预测 |
| 轻功系统 | ✅ 已完成 | `QinggongSystem.cs` (330行) | 多段跳(3段) + 滑翔 + 内力消耗 |
| 角色属性 | ✅ 已完成 | `CharacterAttributesComponent.cs` | 生命/内力/体力/攻击/防御 |
| 五行属性 | ✅ 已完成 | `WuxingElement.cs`, `WuxingComponent` | 金木水火土亲和度系统 |
| 动画控制 | 🟡 部分完成 | `AnimationController.cs` | 基础动画状态机，部分占位 |

### 3. 战斗系统

| 功能 | 状态 | 关键文件 | 说明 |
|------|------|----------|------|
| 技能系统 | ✅ 已完成 | `ECS/Systems/SkillSystem.cs` (406行) | 施法/冷却/效果/五行加成 |
| 技能类型 | ✅ 已完成 | `SkillType`枚举 | 攻击/控制/位移/辅助/终结技 |
| 伤害计算 | ✅ 已完成 | `CombatSystem.ApplyDamage` | 暴击/五行克制/伤害类型 |
| 技能效果 | ✅ 已完成 | `EffectSystem` | Buff/Debuff/HoT/控制效果 |
| 技能同步 | ✅ 已完成 | `SkillCastResponseHandler` | 服务端验证与响应 |

### 4. UI系统

| 功能 | 状态 | 关键文件 | 说明 |
|------|------|----------|------|
| UI状态管理 | ✅ 已完成 | `UIStateManager.cs` (681行) | 场景切换/用户会话/角色管理 |
| 认证UI | ✅ 已完成 | `AuthenticationUI.cs`, `LoginPanel.cs` | 登录/注册界面 |
| 角色选择 | ✅ 已完成 | `CharacterSceneController.cs` | 3D预览 + 角色列表 |
| 角色创建 | ✅ 已完成 | `CharacterCreationPanel.cs` | 外观/职业/性别选择 |
| 水墨风UI | ✅ 已完成 | `UI/Ink/` (40+页面) | 中国水墨画风格UI框架 |
| UI事件总线 | ✅ 已完成 | `UIEventBus.cs` | 全局事件发布/订阅 |
| 错误恢复 | ✅ 已完成 | `EnhancedSceneRecoveryManager` | 场景转换错误恢复 |

**水墨风UI页面清单** (40+):

| 分类 | 页面 |
|------|------|
| 角色相关 | 角色(Character)、角色创建(CharacterCreation)、外观(Appearance) |
| 战斗相关 | 战斗(Combat)、副本(Dungeon)、元素视野(ElementVision) |
| 社交相关 | 社交(Social)、组队(Team)、多人(Multiplayer)、门派(Sect) |
| 经济相关 | 商城(Shop)、背包(Inventory)、制造(Crafting) |
| 内容相关 | 任务(Quest)、活动(Activities)、战令(BattlePass)、图鉴(Bestiary) |
| 生活相关 | 生活(Livelihood)、坐骑宠物(MountPet)、休闲(Casual) |
| 系统相关 | 设置(Settings)、邮件(Mail)、地图(Map)、引导(Guide) |
| 其他 | 抽卡(Gacha)、拍照(PhotoMode)、奖励(Reward)、时辰(Time)、个人(Personal)、章节过渡(ChapterTransition)、武学录(MartialRecord) |

### 5. NarrativePro插件

| 模块 | 状态 | 关键文件 | 说明 |
|------|------|----------|------|
| 插件核心 | ✅ 已完成 | `NarrativeProPlugin.cs` (178行) | 插件初始化/网络桥接 |
| 任务系统 | ✅ 已完成 | `Tales/Quest/Quest.cs` (257行) | 任务状态机/分支/任务链 |
| 对话系统 | ✅ 已完成 | `Tales/Dialogue/Dialogue.cs` (437行) | NPC/玩家对话树/条件/事件 |
| GAS能力系统 | 🟡 部分完成 | `GAS/NarrativeGameplayAbility.cs` | 能力框架，Cost/Cooldown待完善 |
| 存档系统 | 🟡 框架完成 | `SaveSystem/` | 模块入口，子系统待实现 |
| 技能树 | ✅ 已完成 | `SkillTrees/` | TreePerk/TreeSkill |
| 物品系统 | ✅ 已完成 | `Items/` | 物品/碎片/弹药 |
| 载具系统 | ✅ 已完成 | `Vehicles/` | 载具移动/交通灯 |
| 时间系统 | ✅ 已完成 | `TimeOfDay/` | 昼夜循环 |
| 音乐系统 | ✅ 已完成 | `Music/` | 动态音乐切换 |
| AI系统 | ✅ 已完成 | `AI/` | NPC活动/掩护/目标 |
| 角色创建器 | ✅ 已完成 | `CharacterCreator/` | 外观/选项配置 |
| 导航系统 | ✅ 已完成 | `Navigation/` | 寻路/标记 |
| 世界触发器 | ✅ 已完成 | `World/Triggers/` | 触发区域 |
| 编辑器模块 | ✅ 已完成 | `EditorModules/` | 任务/对话/武器编辑器 |

### 6. 渲染与特效

| 功能 | 状态 | 关键文件 | 说明 |
|------|------|----------|------|
| 大气效果 | ✅ 已完成 | `AtmosphericParticleEffect.cs` | 粒子特效 |
| 角色光照 | ✅ 已完成 | `CharacterLightingSystem.cs` | 角色专用光照 |
| 后处理 | ✅ 已完成 | `PostProcess/` | 视觉效果 |
| 五行粒子 | ✅ 已完成 | `WuxingParticleEffects.cs` | 五行技能特效 |
| 星空粒子 | ✅ 已完成 | `StarParticleSystem.cs` | 环境粒子 |

### 7. 工具与编辑器

| 功能 | 状态 | 关键文件 | 说明 |
|------|------|----------|------|
| TraeBridge | ✅ 已完成 | `TraeBridgeServer.cs` | HTTP API控制Flax Editor |
| 场景管理 | ✅ 已完成 | `SceneController.cs` | 场景加载/切换 |
| 性能监控 | ✅ 已完成 | `PerformanceMonitor.cs` | 性能指标采集 |

---

## 二、待完善/未完成功能

### 高优先级 🔴

| 项目 | 当前状态 | 待完成内容 | 影响范围 |
|------|----------|------------|----------|
| GAS Cost/Cooldown | TODO标记 | 实际效果应用机制 | 能力系统完整性 |
| Flax输入系统集成 | TODO标记 | 能力输入映射配置 | 能力触发方式 |
| NarrativeSyncManager | 返回null | 实例化与注入机制 | 叙事系统网络同步 |
| 动画系统 | 部分占位 | 完整动画状态机/混合树 | 角色表现力 |
| 音频系统 | 未实现 | 音效/BGM/环境音 | 游戏沉浸感 |

### 中优先级 🟡

| 项目 | 当前状态 | 待完成内容 | 影响范围 |
|------|----------|------------|----------|
| 存档子系统 | 框架完成 | 具体存档/读档逻辑 | 单机进度保存 |
| 技能数据库 | 硬编码2个 | 数据驱动配置 | 技能扩展性 |
| AI行为树 | 基础完成 | 更复杂的NPC AI | NPC智能度 |
| 物理交互 | 未实现 | 可破坏物/交互物 | 世界互动性 |

### 低优先级 🟢

| 项目 | 当前状态 | 待完成内容 | 影响范围 |
|------|----------|------------|----------|
| 多语言支持 | 未实现 | 本地化系统 | 国际化 |
| 无障碍功能 | 未实现 | 辅助功能 | 可访问性 |
| 回放系统 | 未实现 | 战斗录像 | 观战/复盘 |

---

## 三、后续开发计划

### 阶段一：核心玩法完善 (4-6周)

**目标**: 完善战斗与角色核心循环

#### 1. GAS系统完善
- [ ] 实现Cost效果应用机制
- [ ] 实现Cooldown效果应用机制
- [ ] 接入Flax输入系统

#### 2. 技能系统扩展
- [ ] 数据驱动技能配置(JSON/ScriptableObject)
- [ ] 技能编辑器(可视化)
- [ ] 技能连招系统

#### 3. 动画系统完善
- [ ] 完整动画状态机
- [ ] 动画混合树
- [ ] 根运动支持

#### 4. 音频系统
- [ ] 音效管理器
- [ ] BGM动态切换
- [ ] 环境音效

### 阶段二：内容系统 (6-8周)

**目标**: 完善RPG内容框架

#### 1. 任务系统扩展
- [ ] 任务编辑器完善
- [ ] 任务追踪UI
- [ ] 任务奖励系统

#### 2. 对话系统扩展
- [ ] 对话编辑器完善
- [ ] 语音集成
- [ ] 表情系统

#### 3. 物品/装备系统
- [ ] 背包系统完善
- [ ] 装备强化/精炼
- [ ] 物品掉落系统

#### 4. NPC AI增强
- [ ] 行为树编辑器
- [ ] 动态难度调整
- [ ] 群体AI协调

### 阶段三：社交与多人 (4-6周)

**目标**: 完善MMO社交体验

#### 1. 组队系统
- [ ] 组队匹配
- [ ] 队伍同步
- [ ] 团队副本

#### 2. 公会/门派系统
- [ ] 门派功能
- [ ] 门派任务
- [ ] 门派战

#### 3. 交易系统
- [ ] 玩家交易
- [ ] 拍卖行
- [ ] 商城完善

#### 4. 聊天系统
- [ ] 频道聊天
- [ ] 私聊
- [ ] 表情/语音

### 阶段四：优化与打磨 (持续)

**目标**: 性能优化与体验提升

#### 1. 性能优化
- [ ] LOD系统
- [ ] 遮挡剔除
- [ ] 内存优化

#### 2. 网络优化
- [ ] 带宽优化
- [ ] 延迟补偿
- [ ] 断线重连完善

#### 3. UI/UX打磨
- [ ] 动画过渡
- [ ] 反馈效果
- [ ] 无障碍支持

---

## 四、技术债务清理

| 优先级 | 项目 | 现状 | 建议 |
|--------|------|------|------|
| 🔴 高 | 两套角色系统并行 | ZoneShardGrain + CharacterGrain职责重叠 | 明确边界，逐步统一 |
| 🔴 高 | 包版本分散 | 无Directory.Packages.props | 引入集中包管理 |
| 🟡 中 | UE5迁移残留 | CopyToUE5 Target残留 | 清理残留代码 |
| 🟡 中 | 单shard硬编码 | 固定使用shard 0 | 设计shard路由策略 |
| 🟢 低 | 遗留序列化库 | Newtonsoft/protobuf并存 | 逐步迁移到MemoryPack |

---

## 五、关键文件索引

### 客户端核心
| 文件 | 说明 |
|------|------|
| `HundunWorld/Source/Game/HundunWorldGame.cs` | 游戏主入口 |
| `HundunWorld/Source/Game/Network/NetworkManager.cs` | 网络管理 |
| `HundunWorld/Source/Game/PlayerController.cs` | 玩家控制 |
| `HundunWorld/Source/Game/ECSUpdateDriver.cs` | ECS驱动 |

### NarrativePro插件
| 文件 | 说明 |
|------|------|
| `Plugins/NarrativePro/Source/NarrativePro/NarrativeProPlugin.cs` | 插件入口 |
| `Plugins/NarrativePro/Source/NarrativePro/Tales/Quest/Quest.cs` | 任务系统 |
| `Plugins/NarrativePro/Source/NarrativePro/Tales/Dialogue/Dialogue.cs` | 对话系统 |
| `Plugins/NarrativePro/Source/NarrativePro/GAS/NarrativeGameplayAbility.cs` | 能力系统 |

### 相关文档
- [CLIENT.md](./CLIENT.md) - 客户端架构详解
- [ARCHITECTURE.md](./ARCHITECTURE.md) - 整体架构
- [NETCODE.md](./NETCODE.md) - 网络同步设计
- [TECH_DEBT.md](./TECH_DEBT.md) - 技术债务清单

---

## 六、统计摘要

| 类别 | 已完成 | 部分完成 | 未实现 |
|------|--------|----------|--------|
| 核心架构 | 7 | 0 | 0 |
| 角色系统 | 5 | 1 | 0 |
| 战斗系统 | 5 | 0 | 0 |
| UI系统 | 7 | 0 | 0 |
| NarrativePro | 12 | 2 | 0 |
| 渲染特效 | 5 | 0 | 0 |
| 工具编辑器 | 3 | 0 | 0 |
| **总计** | **44** | **3** | **0** |

> 注：统计基于代码文件存在性和基础功能实现，部分模块虽标记"已完成"但仍有扩展空间。