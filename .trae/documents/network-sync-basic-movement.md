# 客户端角色基础移动/旋转/跳跃网络同步真正实现 — 实施计划

## 一、背景与目标

### 问题诊断

当前客户端"仅能看到角色彼此"但移动/旋转/跳跃的网络同步未真正落地。代码审查确认存在**双轨脱节**这一致命缺陷：

* [PlayerController.cs:847](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/PlayerController.cs#L847) `Actor.Position += totalMovement` — 用 `MoveSpeed=5.0`、`JumpForce=10.0`、`Gravity=-9.81` 做本地物理模拟

* [LocalSimulationSystem.cs:127](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Game.ECS.Arch/Systems/LocalSimulationSystem.cs#L127) `MovementFormula.Step(...)` — 用 `DefaultMaxSpeed=6.0` 在 ECS 中跑预测，写入 `PredictedTransformComponent`

* **两套移动并行运行、互不通信**：玩家看到的是 PlayerController 直接移动的 Actor，服务端校验用的是 LocalSimulationSystem 的 `PredictedEndX/Y/Z`，两者完全脱钩

### 表现

* 本地玩家看起来"能走"，但走的是错误的本地物理（速度/重力/跳跃冲量全不对）

* 服务端基于 `PredictedEndX/Y/Z` 做权威校验，与显示位置无关 → 持续发 Correction 但 `ReconciliationSystem` 修正 `PredictedTransformComponent`，PlayerController 不读取，修正无效

* 其他客户端看到的本地玩家位置是服务端权威位置，与本地显示不一致

* 旋转/跳跃同理：服务端权威 Yaw 来自 `InputPacket.LookYaw`，但本地 `Actor.Orientation` 由 `UpdateCharacterRotation` 用移动方向驱动

### 目标

让 `PredictedTransformComponent` 成为本地玩家的单一事实源（位置+朝向），PlayerController 退化为输入采集器，Actor 显示位置由 ECS 驱动。

### 范围决策

采用推荐方案：

1. **完全重构 PlayerController**：删除本地物理模拟代码（约 600 行），保留外部依赖所需的公共 API 兼容签名
2. **禁用 ClickToMove**：本期 `EnableClickToMove=false`，后续 PR 通过 ECS 输入路径重写
3. **仅修边沿触发**：在 `PlayerInputComponent` 新增 `JumpPressedThisFrame` 字段修复三段跳快速消耗问题，QinggongSystem 本期不动
4. **仅用 IsWalking 动画参数**：本期不接入 IsRunning/IsJumping 等参数，留待 AnimationGraph 资源扩展

***

## 二、关键架构原则

### 2.1 单一事实源

| 实体   | 位置                                      | 朝向                                                      | 动画状态                                |
| ---- | --------------------------------------- | ------------------------------------------------------- | ----------------------------------- |
| 本地玩家 | `PredictedTransformComponent`（ECS Z-up） | `PredictedTransformComponent.Yaw`（由 `input.LookYaw` 写入） | `MovementStateAuthComponent`（服务端权威） |
| 远程实体 | `InterpolatedTransformComponent`（已实现）   | `AuthTransformComponent.Yaw`（已实现）                       | `MovementStateAuthComponent`（待实现）   |

### 2.2 坐标系映射（已通过 ZoneShardGrain.TickAsync:539-548 与 SnapshotApplySystem.HandleSpawn:431-442 双向确认）

* ECS Z-up：X=左右, Y=前后, Z=上下

* Flax Y-up：X=左右, Y=上下, Z=前后

* 双向映射：`Flax.X = ECS.X`、`Flax.Y = ECS.Z`、`Flax.Z = ECS.Y`

* 即 `Actor.Position = new Vector3(pred.X, pred.Z, pred.Y)`

### 2.3 每帧执行顺序

```
PlayerController.OnUpdate       → 采集输入 → 写入 PlayerInputComponent
ECSUpdateDriver.OnUpdate        → archHost.Tick：
  ├─ FixedUpdate 组
  │   ├─ LocalSimulationSystem (order:10)  → 写 PredictedTransformComponent（含 Yaw）
  │   └─ ReconciliationSystem  (order:20)  → 校正 PredictedTransformComponent
  ├─ NetworkReceive 组
  │   └─ SnapshotApplySystem               → 写 MovementStateAuthComponent（修复早返回 bug）
  ├─ NetworkSend 组
  │   └─ InputSendSystem                   → 打包 InputPacket 入队
  └─ Render 组
      └─ InterpolationSystem               → 仅远程实体
LocalPlayerActorSyncSystem.OnUpdate（新增）→ 读 PredictedTransformComponent → 写 Actor.Position/Orientation
FlaxActorSyncSystem.OnUpdate               → 远程实体（已存在，扩展动画）
```

***

## 三、文件级改动清单

### 3.1 [PlayerInputComponent.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Game.ECS.Arch/Components/PlayerInputComponent.cs) — 新增字段

新增边沿触发字段，供 LocalSimulationSystem 与 PlayerController 协作修复三段跳快速消耗问题：

```csharp
/// <summary>本帧是否为跳跃按下边沿（前一帧 false → 当前帧 true）。
/// 由 PlayerController 在 WriteInputToEcs 中维护，仅 true 的那一帧触发跳跃。</summary>
public bool JumpPressedThisFrame;
```

### 3.2 [LocalSimulationSystem.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Game.ECS.Arch/Systems/LocalSimulationSystem.cs) — 修复边沿触发 + 写入 Yaw

**改动点 A（行 89-117 跳跃逻辑）**：

* 删除 `var isJumpPressed = (input.InputBits & 0x1) != 0;` 作为 `jumpCount++` 的唯一判定

* 改用 `input.JumpPressedThisFrame` 作为 `jumpCount++` 触发条件

* 当 `JumpPressedThisFrame == false` 时，即使 InputBits bit0=1（持续按住）也不递增 jumpCount

* 这修复了"持续按住空格 → 轻功三段跳在 50ms 内被消耗完"的问题

**改动点 B（行 127 MovementFormula.Step 调用前）**：

* 新增：在 `isLocal` 分支内写入 `pred.Yaw = input.LookYaw;`

* 当前 LocalSimulationSystem **从不更新** **`pred.Yaw`**，导致 LocalPlayerActorSyncSystem 读到的 Yaw 永远是初始值 0

**改动点 C（行 138-168 isLocal 分支）**：

* 在 `pred.ClientTick = CurrentClientTick;` 之前添加 `pred.Yaw = input.LookYaw;`

### 3.3 [SnapshotApplySystem.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs) — 修复本地玩家早返回 bug

**改动点（行 494-528 HandleUpdate 方法）**：

当前代码：

```csharp
if (delta.Transform != null)
{
    var newTransform = delta.Transform.Value;
    newTransform.ServerTick = serverTick;
    ref var netId = ref world.Get<NetworkIdentityComponent>(archEntity);
    if (netId.IsLocalPlayer)
    {
        world.Set(archEntity, ref newTransform);
        return;  // ← BUG: 本地玩家写入 Transform 后直接返回，跳过 MovementState/State/AnimationState 应用
    }
    // ...远程实体的 InterpolatedTransformComponent 更新...
    world.Set(archEntity, ref newTransform);
}

if (delta.State != null) { ... }       // ← 本地玩家走不到这里
if (delta.MovementState != null) { ... } // ← 本地玩家走不到这里
if (delta.AnimationState != null) { ... } // ← 本地玩家走不到这里
```

**修复**：删除 `return;`，仅保留 `world.Set(archEntity, ref newTransform);`。后续 `if (world.Has<InterpolatedTransformComponent>(archEntity))` 判断天然会跳过本地玩家（本地玩家无此组件），插值不会被错误更新。

修复后本地玩家也能接收服务端的 `MovementStateAuthComponent`（驱动本地动画）、`EntityStateAuthComponent`（HP/Mana 等）、`AnimationStateAuthComponent`（Montage 触发事件）。

### 3.4 [PlayerController.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/PlayerController.cs) — 退化为输入采集器

#### 删除/弃用的部分（保留空 stub 兼容外部依赖）：

* **行 31-49**：删除 `MoveSpeed`/`RunSpeedMultiplier`/`JumpForce`/`Gravity`/`GroundCheckDistance` 物理参数字段

* **行 69-94**：删除 `_isGrounded`（改为从 ECS 读）、`_verticalVelocity`/`_targetPosition`/`_isMovingToTarget`/`_movementBuffer`/`MaxBufferSize` 等本地模拟状态字段

* **行 153-294**：删除 `_moveDirection` 之外的本地物理字段（`_currentMoveSpeed`/`_isSliding`/`_slideDuration`/`StepHeight`/`MaxSlopeAngle`/`Acceleration`/`Deceleration`/`AirControl`/`_currentVelocity` 等）

* **行 605-649 HandleCharacterMovement**：整体删除（替换为只调用 `WriteInputToEcs`）

* **行 740-801 CalculateTargetSpeed/UpdateMovementVelocity**：删除

* **行 805-826 HandleVerticalMovement**：删除（重力/跳跃由 ECS 跑 MovementFormula）

* **行 830-854 ApplyMovement**：删除（不再写 Actor.Position）

* **行 858-878 UpdateCharacterRotation**：删除（朝向由 LocalPlayerActorSyncSystem 应用）

* **行 882-921 HandleClickToMove**：改为 `if (EnableClickToMove) return;`（本期禁用）

* **行 926-963 CheckGroundStatus/CheckIsGrounded/GetGroundHeight**：删除

* **行 970-1110 UpdateCharacterState/DetermineNewState/ChangeState/OnStateEnter/OnStateExit**：删除本地状态机

* **行 1117-1175 UpdateMovementBuffer/GetPredictedMovementDirection/ApplyServerCorrection**：删除

* **行 1180-1228 HandleGroundClick/PerformGroundRaycast**：保留射线检测代码但不调用

* **行 1299-1410 冲刺/滑行系统**：保留 `HandleSprintInput`/`CanSprint`/`UpdateStaminaSystem`（UI 显示需要体力），删除 `StartSlide`/`UpdateSlideSystem`/`EndSlide`（本期不支持滑行）

#### 保留/改造的部分：

* **行 307-340 OnStart**：保留 InputManager/QinggongSystem/TargetSelectionSystem 引用获取

* **行 347-394 TryInitializeAnimationParameters/SetIsWalking**：保留（本地玩家动画参数初始化仍由 PlayerController 完成）

* **行 396-448 OnUpdate**：精简为：

  * 检查 `EnableInput`

  * 调用 `GetInputDirection()` 计算 `_moveDirection`

  * 调用 `HandleAuxiliaryInputs()` 维护 \_isRunning/\_isSprinting/\_isCrouching 标志

  * 调用 `UpdateStaminaSystem()` 维护体力

  * 调用 `WriteInputToEcs()` 写入 PlayerInputComponent（**新增 JumpPressedThisFrame 边沿检测**）

  * 删除：HandleCharacterMovement/HandleGroundClick/UpdateCharacterState/UpdateMovementBuffer/BuildAndSendInputPacket

* **行 483-523 WriteInputToEcs**：保留并改造

  * 新增 `_prevJumpPressed` 私有字段

  * 计算 `JumpPressedThisFrame = jumpPressed && !_prevJumpPressed; _prevJumpPressed = jumpPressed;`

  * 写入 `input.JumpPressedThisFrame`

  * 保留 MoveX/MoveY/LookYaw/LookPitch/InputBits 写入逻辑（不变）

* **行 651-710 GetInputDirection**：保留（输入采集是 PlayerController 核心职责）

#### 公共 API 兼容性改造（行 1232-1297）：

外部脚本依赖的 API 改为从 ECS 读取，保持签名不变：

* `IsGrounded` 属性 → 查询本地玩家实体的 `MovementStateAuthComponent.IsGrounded`（无组件则返回 true 兜底）

* `IsSprinting()` → 返回 `_isSprinting`（仍由本地输入标志驱动）

* `IsSliding()` → 返回 false（本期不支持）

* `GetCharacterState()` → 从 `MovementStateAuthComponent.MovementMode` 映射到 `CharacterState` 枚举

* `GetPosition()` → 返回 `Actor.Position`（由 LocalPlayerActorSyncSystem 同步）

* `SetPosition(Vector3)` → 同时设置 `Actor.Position` 与 `PredictedTransformComponent.X/Y/Z`（调试用）

* `EnableMovement` 属性：保留，当 false 时不写 MoveX/MoveY/JumpPressedThisFrame 到 ECS

* `EnableInput` 属性：保留

* `CurrentStamina`/`GetStaminaPercentage()`：保留

* `RotationSmoothing` setter：保留空实现兜底（SystemOptimizer 调用，但已不影响朝向）

### 3.5 [LocalPlayerActorSyncSystem.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/LocalPlayerActorSyncSystem.cs)（新增文件）

作为 Flax Script，每帧从本地玩家实体的 `PredictedTransformComponent` 读取位置/朝向应用到 Actor，并驱动动画状态。

#### 文件结构：

* 命名空间：`HundunWorld.Game`

* 类：`public class LocalPlayerActorSyncSystem : Script`

* 单例：`public static LocalPlayerActorSyncSystem? Instance { get; private set; }`

* 关键字段：

  * `private Entity _localPlayerEntity;` + `private bool _localPlayerEntityFound;`

  * `private AnimatedModel _animatedModel;`

  * `private AnimGraphParameter _isWalkingParam;`

  * `private MovementMode _lastMovementMode = MovementMode.Walk;`

* `OnStart`：

  * 设置 Instance

  * 查找本地玩家实体（参考 PlayerController.TryFindLocalPlayerEntity 模式）

  * 查找 AnimatedModel 子 Actor

  * 调用 TryInitializeAnimationParameters（参考 PlayerController 现有实现的安全校验，资源未加载时延迟初始化）

* `OnUpdate`：

  * 调用 `TryFindLocalPlayerEntity()`

  * 读取 `PredictedTransformComponent` (X/Y/Z/Yaw)

  * **应用位置**：`Actor.Position = new Vector3(pred.X, pred.Z, pred.Y)`（ECS Z-up → Flax Y-up）

  * **应用朝向**：`Actor.Orientation = Quaternion.Euler(0, pred.Yaw * Mathf.Rad2Deg, 0)`

  * 调用 `ApplyAnimationState()`：读取 `MovementStateAuthComponent.MovementMode`，按映射表设置 `IsWalking` 参数

* `OnDestroy`：清空 Instance

#### 动画参数映射表（本期仅 IsWalking）：

| MovementMode | IsWalking | 备注                                   |
| ------------ | --------- | ------------------------------------ |
| Walk         | true      | 行走                                   |
| Run          | true      | 奔跑（本期与 Walk 共用 IsWalking=true，后续可拆分） |
| Jump         | false     | 跳跃中（上升）                              |
| Fall         | false     | 下落                                   |
| Swim         | false     | 游泳                                   |
| Crouch       | true      | 蹲伏行走                                 |

### 3.6 [FlaxActorSyncSystem.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/FlaxActorSyncSystem.cs) — 优化远程动画判定

**改动点（行 468-480 IsWalking 判定）**：

当前用 `interp.TargetX/Y/Z - interp.StartX/Y/Z` 推断是否移动，改为优先用 `MovementStateAuthComponent`：

```csharp
// 在 SyncInterpolatedPositions 的 QueryDescription 中追加 MovementStateAuthComponent
var query = new QueryDescription()
    .WithAll<InterpolatedTransformComponent, NetworkIdentityComponent, AuthTransformComponent>();

_archWorld.Query(in query, (Entity entity, ref InterpolatedTransformComponent interp, 
    ref NetworkIdentityComponent netId, ref AuthTransformComponent auth) =>
{
    // ... 现有逻辑 ...
    
    // 优先用 MovementStateAuthComponent 判定 IsWalking
    bool isMoving;
    if (_archWorld.TryGet<MovementStateAuthComponent>(entity, out var movement))
    {
        var speedSq = movement.VelocityXZ_X * movement.VelocityXZ_X 
                    + movement.VelocityXZ_Y * movement.VelocityXZ_Y;
        isMoving = speedSq > 0.01f;
    }
    else
    {
        // 兜底：原 interp 推断逻辑
        float moveDelta = (new Vector3(interp.TargetX, interp.TargetY, interp.TargetZ)
                          - new Vector3(interp.StartX, interp.StartY, interp.StartZ)).LengthSquared;
        isMoving = moveDelta > 0.0001f;
    }
    isWalkingParam.Value = isMoving;
});
```

注意：本期**不订阅 AnimationStateChanged 事件播放 Montage**，留待 AnimationGraph 资源支持 Montage 槽位后再接入。

### 3.7 [HundunWorldGame.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/HundunWorldGame.cs) — 挂载新系统

**改动点（CreateLocalPlayerActor 方法末尾，约行 1396 return actor 之前）**：

```csharp
// 在 PlayerController 等脚本挂载后，挂载 LocalPlayerActorSyncSystem
// 该系统必须在 ECSUpdateDriver.OnUpdate 之后执行（Flax 中 Script 按 AddScript 顺序）
if (actor.GetScript<LocalPlayerActorSyncSystem>() == null)
{
    actor.AddScript<LocalPlayerActorSyncSystem>();
    Debug.Log("[HundunWorldGame] LocalPlayerActorSyncSystem 已挂载到本地玩家 Actor");
}
```

### 3.8 ECSUpdateDriver.cs — 无需改动

LocalPlayerActorSyncSystem 挂在 LocalPlayerActor 上（而非 ECSUpdateDriver 所在 Actor），Flax 中不同 Actor 的 Script 按场景树顺序执行，LocalPlayerActor 在 ECSUpdateDriver 之后即可满足要求。

***

## 四、跳跃边沿触发修复细节

### 4.1 问题

当前 [LocalSimulationSystem.cs:89-117](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Game.ECS.Arch/Systems/LocalSimulationSystem.cs#L89) 的跳跃逻辑：

```csharp
var isJumpPressed = (input.InputBits & 0x1) != 0;  // 持续按住空格 → 每帧 true
if (isJumpPressed) {
    if (isQinggongJump) {
        jumpCount++;  // ← BUG: 持续按住每帧 +1，3 帧（50ms）消耗完三段跳
    } else {
        jumpCount = 1;  // 非轻功 OK，覆盖只跳一次
    }
}
```

### 4.2 修复

* `PlayerInputComponent` 新增 `JumpPressedThisFrame` 字段（仅按下边沿为 true）

* `PlayerController.WriteInputToEcs` 维护 `_prevJumpPressed`，计算 `JumpPressedThisFrame = jumpPressed && !_prevJumpPressed`

* `LocalSimulationSystem` 改用 `input.JumpPressedThisFrame` 作为 `jumpCount++` 条件

* InputBits bit0 仍保留（用于服务端校验跳跃状态，服务端 `MovementValidator` 也用 bit0 推断）

### 4.3 服务端配合

服务端 `MovementValidator.Validate` 也存在同样问题（持续按住每帧 `jumpCount++`），但本期不改服务端。客户端通过 `JumpPressedThisFrame` 边沿触发，每帧 InputBits bit0=1 仅一帧，下一帧即使持续按住也置 0，相当于客户端先行做了边沿触发。服务端跟随客户端 InputBits bit0 走，行为与客户端一致。

具体约定：`PlayerController.WriteInputToEcs` 中：

```csharp
// 仅在 JumpPressedThisFrame=true 的帧才设 InputBits bit0=1
if (input.JumpPressedThisFrame)
    inputBits |= 0x1;
else
    inputBits &= ~0x1u;
```

***

## 五、端到端测试方案

### 5.1 单客户端验证（Flax Editor Play-Start）

**前置条件**：

* 启动本地 Orleans Silo（Horizon.Orleans.Silo）

* 启动本地 GameGateway（Horizon.Game.Gateway）

* Flax Editor 中打开 GameWorld 场景

**测试步骤**：

1. **登录并进入游戏世界**：

   * 验证 `[HundunWorldGame] LocalPlayerActorSyncSystem 已挂载到本地玩家 Actor` 日志输出

   * 验证 LocalPlayerActor 生成在初始位置

2. **WASD 移动**：

   * 按 W/A/S/D 观察角色移动

   * 临时在 LocalPlayerActorSyncSystem.OnUpdate 加 Debug.Log 打印 `Actor.Position` 与 `PredictedTransformComponent` 对比，应一致

   * 验证服务端不再持续发 Correction（在 ReconciliationSystem 中加日志统计 Correction 频次）

3. **鼠标视角旋转**：

   * 拖动鼠标改变相机 Yaw

   * 验证 `Actor.Orientation.Y` 与 `PredictedTransformComponent.Yaw` 一致

   * 验证服务端下发的 `AuthTransformComponent.Yaw` 与客户端 `PredictedTransformComponent.Yaw` 一致

4. **空格跳跃**：

   * 单次按空格：验证角色跳起，`MovementStateAuthComponent.MovementMode = Jump → Fall → Walk`

   * 持续按住空格：验证只跳一次（非轻功模式）或三段跳后才停止（轻功模式，不再 50ms 消耗完）

5. **Shift 跑步**：

   * 按住 Shift+W：验证 `IsSprinting=true`，ThirdPersonCamera FOV 调整生效

6. **爬墙兼容性**：

   * 触发 ClimbingController：验证 `EnableMovement=false` 时本地玩家不再移动

   * 退出爬墙：验证 `EnableMovement=true` 后恢复正常移动

### 5.2 双客户端验证（多机器或同机两实例）

**前置条件**：同一 GameGateway 接入两个客户端（用不同账号登录），在同一 ZoneShard 视野范围内

**测试步骤**：

1. **相互可见性**：

   * 客户端 A 与客户端 B 互看对方角色

   * 验证 `FlaxActorSyncSystem` 创建了 RemotePlayer\_{EntityId} Actor

   * 验证远程角色位置与对方本地位置一致（误差 < 0.5m）

2. **A 移动，B 观察**：

   * A 按 W 向前走 5 秒

   * B 观察 A 的角色：位置应平滑插值移动，无瞬移

   * 验证 A 在 B 端的 IsWalking=true，移动方向与 A 本地一致

3. **A 跳跃，B 观察**：

   * A 按空格跳跃

   * B 观察 A 的角色：应跳起并落下

   * 验证 A 在 B 端的 IsWalking 动画参数正确切换（跳跃时 false，落地行走时 true）

4. **A 旋转视角，B 观察**：

   * A 拖动鼠标旋转 360°

   * B 观察 A 的角色朝向：应跟随 A 的 LookYaw 变化

   * 验证 A 在 B 端的 `Actor.Orientation` 与 A 本地 `Actor.Orientation` 一致（误差 < 1°）

5. **网络抖动模拟**：

   * 在 GameGateway 中人为注入 200ms 延迟

   * A 移动：A 本地应流畅（预测管线），B 端应有 200ms 滞后但平滑

   * 验证 ReconciliationSystem 的 Correction 频次仍接近 0

### 5.3 边界 case 验证

1. **断线重连**：

   * A 断网 5 秒后重连

   * 验证 A 的 PredictedTransformComponent 被服务端 Correction 校正到权威位置

   * 验证 LocalPlayerActor 平滑过渡到校正后位置（无瞬移感）

2. **AOI 边界跨越**：

   * A 走出 B 的视野 chunk

   * 验证 B 端的 RemotePlayer\_{A\_EntityId} Actor 被销毁

   * A 走回 B 视野：验证 Actor 重新创建

### 5.4 编译验证

```powershell
dotnet build c:\Works\GitHubProjects\HundunWorld\HundunWorld\HundunWorld.csproj
```

* 0 errors（96 pre-existing warnings 仍可接受）

* 需在编译前关闭 Flax Editor（释放 Game.CSharp.dll 锁）

***

## 六、实施顺序

1. **第一步：ECS 层修复**（独立可测）

   * `PlayerInputComponent` 新增 `JumpPressedThisFrame` 字段

   * `LocalSimulationSystem` 写 `pred.Yaw = input.LookYaw` + 边沿触发修复

   * `SnapshotApplySystem` 修复本地玩家早 return bug

   * 编译验证

2. **第二步：新增 LocalPlayerActorSyncSystem**（独立新文件）

   * 实现位置/朝向同步

   * 实现动画状态机驱动（仅 IsWalking）

   * 编译验证

3. **第三步：PlayerController 重构 + HundunWorldGame 挂载**（破坏性改动）

   * PlayerController 删除本地物理模拟代码

   * 改造 WriteInputToEcs 写入 JumpPressedThisFrame

   * 公共 API 改为从 ECS 读取

   * HundunWorldGame.CreateLocalPlayerActor 末尾挂载 LocalPlayerActorSyncSystem

   * 编译验证 + 单客户端跑通

4. **第四步：FlaxActorSyncSystem 远程动画优化**

   * 用 MovementStateAuthComponent 判定 IsWalking

   * 双客户端跑通

5. **第五步：端到端验证与调优**

   * 双客户端完整测试（5.2 全流程）

   * 边界 case 验证（5.3）

***

## 七、潜在风险与缓解

1. **风险：PlayerController 删除导致编译错误**

   * 已通过 grep 确认外部依赖：`ClimbingController`（IsGrounded/EnableMovement）、`ThirdPersonCamera`/`DynamicCameraAdjuster`（IsSprinting）、`SystemOptimizer`（RotationSmoothing setter）

   * 缓解：保留所有公共 API 签名，仅改实现；IsGrounded/IsSprinting/EnableMovement/RotationSmoothing 均保留为兼容 stub

2. **风险：LocalPlayerActorSyncSystem 与 ECSUpdateDriver 执行顺序不对**

   * 缓解：将 LocalPlayerActorSyncSystem 挂在 LocalPlayerActor 上，Flax 中不同 Actor 的 Script 按场景树顺序执行，LocalPlayerActor 在 ECSUpdateDriver 之后

3. **风险：AnimatedModel 参数名与 AnimationGraph 资源不匹配**

   * 缓解：参考 PlayerController.TryInitializeAnimationParameters 现有实现，已确认 `IsWalking` 参数存在

4. **风险：服务端 MovementValidator 不识别边沿触发**

   * 缓解：客户端在 `JumpPressedThisFrame=true` 的帧才设 InputBits bit0=1，下一帧清除，相当于客户端先行做边沿触发；服务端无需改动

5. **风险：本地玩家 Actor 由 LocalPlayerActorSyncSystem 写位置，但 ClickToMove 也想写**

   * 缓解：本期 `EnableClickToMove=false` 默认禁用，HandleClickToMove 直接 return

6. **风险：QinggongSystem 的 currentJumpCount 与 LocalSimulationSystem.\_jumpCounts 双轨**

   * 缓解：本期保留双轨，仅通过 JumpPressedThisFrame 边沿触发缓解三段跳快速消耗问题；QinggongSystem 的 currentJumpCount 仅供本地表现

***

## 八、关键文件清单

实施时需要修改的文件：

* [PlayerInputComponent.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Game.ECS.Arch/Components/PlayerInputComponent.cs) — 新增 JumpPressedThisFrame 字段

* [LocalSimulationSystem.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Game.ECS.Arch/Systems/LocalSimulationSystem.cs) — 写 Yaw + 边沿触发修复

* [SnapshotApplySystem.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs) — 修复本地玩家早 return bug

* [PlayerController.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/PlayerController.cs) — 退化为输入采集器

* [LocalPlayerActorSyncSystem.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/LocalPlayerActorSyncSystem.cs)（新增）

* [FlaxActorSyncSystem.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/FlaxActorSyncSystem.cs) — 远程动画判定优化

* [HundunWorldGame.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/HundunWorldGame.cs) — CreateLocalPlayerActor 末尾挂载新系统

