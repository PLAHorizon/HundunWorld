# 修复进入游戏后 World 场景不生成角色 BUG Spec

## Why
选择角色进入游戏后，World 场景没有生成游戏角色。根因是角色生成完全依赖网络响应链路（`EnterGameHandler` → `RequestCreateLocalPlayerActor`），World 场景本身没有任何自主生成角色的能力。`CharacterManager.EnterGameAsync()` 采用 fire-and-forget 模式，先发请求再切场景，两者无同步保障，导致场景切换完成后角色可能永远不会生成。

## What Changes
- **在 World 场景添加 `WorldSceneInitializer` 脚本**：场景加载完成后检查是否有待生成的本地玩家，作为网络响应链路的兜底
- **修改 `HundunWorldGame.RequestCreateLocalPlayerActor`**：增加超时重试机制，如果场景切换后一定时间内仍未生成角色，主动触发生成
- **修改 `CharacterManager.EnterGameAsync`**：在场景切换完成后主动检查并触发角色生成，不再完全依赖服务器响应
- **修复 World 场景相机 Target 悬空引用**：ThirdPersonCamera.Target 指向不存在的 Actor，改为动态查找或由生成器设置

## Impact
- Affected code:
  - `GameSceneInitializer.cs` — 新增 World 场景专用初始化逻辑或新建 `WorldSceneInitializer.cs`
  - `HundunWorldGame.cs` — 修改 `RequestCreateLocalPlayerActor` 增加重试
  - `UI/Character/CharacterManager.cs` — 修改 `EnterGameAsync` 增加场景切换后检查
  - `Content/Maps/World.scene` — 需要在编辑器中挂载初始化脚本（代码层面无法直接修改 .scene）

## ADDED Requirements

### Requirement: World 场景角色生成兜底
系统 SHALL 在 World 场景加载完成后，检查是否有待生成的本地玩家，如果没有则主动触发生成。

#### Scenario: 服务器响应正常
- **WHEN** 用户进入 World 场景，且服务器已返回 EnterGameResponse
- **THEN** `EnterGameHandler` 正常调用 `RequestCreateLocalPlayerActor`，角色在 World 场景生成

#### Scenario: 服务器响应延迟
- **WHEN** 用户进入 World 场景，但服务器响应尚未到达
- **THEN** World 场景初始化脚本等待一段时间后，检查是否有待生成请求，如果有则等待响应；如果超时仍无响应，使用本地角色数据主动生成

#### Scenario: 服务器无响应
- **WHEN** 用户进入 World 场景，服务器完全无响应
- **THEN** 超时后 World 场景初始化脚本使用本地缓存的角色数据生成角色，确保玩家不会卡在空场景中

### Requirement: 相机 Target 动态设置
系统 SHALL 在角色生成后自动将 ThirdPersonCamera 的 Target 设置为生成的角色 Actor。

#### Scenario: 角色生成后相机跟随
- **WHEN** 本地玩家 Actor 在 World 场景生成完成
- **THEN** ThirdPersonCamera.Target 自动设置为该 Actor，相机开始跟随角色

## MODIFIED Requirements

### Requirement: CharacterManager.EnterGameAsync
**原有**: 发送 EnterGameRequest 后立即切换场景，不等服务器响应（fire-and-forget）。

**修改为**: 发送请求后切换场景，但在场景切换完成后主动检查角色是否已生成，如果未生成则等待服务器响应并在超时后兜底生成。

### Requirement: HundunWorldGame.RequestCreateLocalPlayerActor
**原有**: 检查是否在 GameWorld 场景，是则立即创建，否则缓存请求并订阅 TransitionCompleted 事件。

**修改为**: 保持原有逻辑，增加超时检查：如果订阅事件后超过 10 秒仍未生成角色，记录警告并使用本地数据兜底生成。