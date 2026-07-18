# UE Iris 与现有网络组件比较报告 Spec

## Why

需要评估 Unreal Engine 5 的 Iris 网络复制系统与当前项目（HundunWorld/Flax Engine）现有网络组件的功能差异，为后续网络架构优化或迁移提供技术参考决策依据。

## What Changes

- **不落地任何功能代码**
- 仅生成技术比较报告文档
- 对比两大系统的架构设计、核心能力、性能特性、开发体验

## Impact

- **Affected specs**：无直接依赖
- **Affected code**：无
- **输出物**：比较报告文档（Markdown 格式）

## ADDED Requirements

### Requirement: UE Iris 系统研究分析

研究者 SHALL 深入研究 UE5 Iris 网络复制系统的核心架构与特性，包括但不限于：

#### Scenario: Iris 核心架构研究
- **WHEN** 研究者查阅 UE 官方文档与源码
- **THEN** 输出以下方面的分析：
  - Iris 的基于 "Replication Graph" 的架构设计
  - 支持的复制类型（Actor、Property、Component）
  - 带宽优化机制（只读复制、条件复制、优先级）
  - 断线重连与状态恢复机制
  - 客户端预测与服务器回滚机制
  - 移动同步（MoveComponent）支持

### Requirement: 现有网络组件研究分析

研究者 SHALL 深入分析 HundunWorld 项目现有网络组件的架构与能力，包括：

#### Scenario: 现有网络架构研究
- **WHEN** 研究者分析项目代码
- **THEN** 输出以下方面的分析：
  - 基于 TouchSocket 的 TCP 通信层
  - HorizonMessageAdapter 消息打包/解包机制
  - NetworkSyncManager 同步管理
  - ECS 架构下的网络实体管理（NetworkIdentityComponent）
  - 输入预测与处理流程（InputSendSystem、LocalSimulationSystem）
  - 服务端 authoritative 机制（ZoneShardGrain、GatewaySyncDispatcher）
  - 断线重连处理（ReconnectionManager）

### Requirement: 功能特性对比矩阵

研究者 SHALL 输出结构化的功能对比矩阵，包含但不限于：

#### Scenario: 功能对比
- **WHEN** 完成两个系统的研究
- **THEN** 输出包含以下维度的对比表格：
  | 维度 | UE Iris | 现有组件 |
  |------|---------|----------|
  | 架构模式 | Replication Graph | ? |
  | 复制粒度 | ? | ? |
  | 带宽优化 | ? | ? |
  | 预测回滚 | ? | ? |
  | 移动同步 | ? | ? |
  | 开发集成 | ? | ? |
  | 扩展性 | ? | ? |

### Requirement: 优劣势综合评估

研究者 SHALL 输出客观的优劣势评估：

#### Scenario: 优势分析
- **WHEN** 完成对比研究
- **THEN** 明确指出：
  - UE Iris 的优势（成熟度、生态、官方支持）
  - 现有组件的优势（定制化、语言一致性、架构简洁）

#### Scenario: 劣势分析
- **WHEN** 完成对比研究
- **THEN** 明确指出：
  - UE Iris 的局限性（UE 强绑定、复杂度过高）
  - 现有组件的不足（缺乏官方支持、功能需自研）

### Requirement: 结论与建议

研究者 SHALL 输出最终结论：

#### Scenario: 迁移可行性评估
- **WHEN** 完成全面分析
- **THEN** 给出明确结论：
  - 现有架构是否有必要/值得迁移到类 Iris 模式
  - 如保留现有架构，建议的改进方向
  - 关键决策参考因素

## MODIFIED Requirements

无

## REMOVED Requirements

无

## 输出物规范

- 报告语言：中文
- 格式：Markdown
- 存放位置：`c:\Works\GitHubProjects\HundunWorld\.trae\specs\network-iris-comparison\comparison-report.md`
