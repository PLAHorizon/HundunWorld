# ARPG第三人称相机系统使用说明

## 概述

这是一个面向ARPG动作游戏的第三人称相机系统，借鉴了黑神话悟空、只狼、怪物猎人、尼尔机械纪元等优秀ARPG游戏的相机设计。该系统实现了越肩视角、锁定目标、战斗动态镜头等ARPG核心功能，并具备完善的碰撞检测和地面穿透防护。

## 核心特性

1. **越肩视角（OTS）**：战斗时相机自动切换到角色肩部上方，提供精确的瞄准感，支持左右肩切换
2. **锁定目标系统**：支持软锁定（自动选择前方敌人）和硬锁定（手动锁定+目标切换），Boss战特殊处理
3. **战斗动态镜头**：攻击命中推进、重击FOV缩放、闪避偏移、受击方向偏移、处决镜头
4. **ARPG相机状态机**：Normal/Combat/OverShoulder/LockOn/BossBattle/Climbing/Swimming/Flying/Cutscene
5. **固定相对距离控制**：相机与角色保持相对固定的距离，支持鼠标滚轮缩放
6. **碰撞检测**：防止相机穿透墙体或其他物体，越肩视角偏移碰撞检测
7. **地面穿透防护**：防止相机位置过低导致穿透地面
8. **弹性跟随**：模仿剑侠情缘3/魔兽世界的延迟跟随效果

## 相机状态说明

| 状态 | 说明 | 距离 | 俯仰角 | FOV |
|------|------|------|--------|-----|
| Normal | 标准第三人称 | 15m | 30° | 60° |
| Combat | 战斗模式 | 10m | 25° | 60° |
| OverShoulder | 越肩视角 | 4m | 5° | 60° |
| LockOn | 锁定目标 | 5m | 10° | 动态 |
| BossBattle | Boss战 | 10m | 15° | 70° |
| Climbing | 攀爬 | 8m | 50° | - |
| Swimming | 游泳 | 12m | 15° | - |
| Flying | 飞行 | 20m | 20° | 70° |
| Cutscene | 过场动画 | 15m | 30° | - |

## 主要参数说明

### 基本参数
- `CurrentState`：当前相机状态
- `Distance`：相机与目标的距离
- `Pitch`：相机俯仰角
- `Yaw`：相机偏航角
- `MinDistance`/`MaxDistance`：距离范围
- `MinPitch`/`MaxPitch`：俯仰角范围
- `FocusOffset`：相机聚焦点偏移

### 越肩视角参数
- `ShoulderOffset`：越肩水平偏移量（默认40厘米）
- `ShoulderHeightOffset`：越肩高度偏移量（默认10厘米）
- `ShoulderRightSide`：越肩侧边（true=右肩，false=左肩）
- `ShoulderSwapSpeed`：越肩切换平滑速度（默认8）

### 锁定目标参数
- `EnableLockOn`：是否启用锁定目标系统
- `LockOnMaxDistance`：锁定目标最大距离（默认20米）
- `LockOnDetectionAngle`：锁定检测角度范围（默认60°）
- `SoftLockAngle`：软锁定检测角度（默认45°）
- `SoftLockInfluence`：软锁定相机偏转强度（默认0.3）
- `BossTag`：Boss检测标签

### 战斗动态镜头参数
- `EnableCombatCameraEffects`：是否启用战斗动态镜头
- `HitPushDistance`/`HitPushDuration`：攻击命中推进距离/持续时间
- `HeavyHitPushDistance`/`HeavyHitFOVPunch`：重击推进距离/FOV缩放
- `DodgeOffsetStrength`/`DodgeRecoverySpeed`：闪避偏移强度/恢复速度
- `HitReactionStrength`/`HitReactionDuration`：受击偏移强度/持续时间
- `ExecutionDistance`/`ExecutionFOV`/`ExecutionDuration`：处决镜头参数

### 碰撞检测参数
- `EnableCameraCollision`：是否启用碰撞检测
- `CollisionDetectionQuality`：碰撞检测质量（Low/Medium/High）
- `CollisionLayerMask`：碰撞检测层
- `EnableSmartAvoidance`：是否启用智能避障

## 使用方法

### 1. 基本设置
```csharp
var camera = cameraActor.GetScript<ThirdPersonCamera>();
camera.Target = playerCharacter;
camera.Distance = 15.0f;
camera.Pitch = 30.0f;
```

### 2. 相机控制
- **旋转相机**：按住鼠标右键并拖动
- **缩放相机**：使用鼠标滚轮
- **越肩切换**：按V键切换左右肩
- **锁定目标**：按Tab键锁定/解锁目标
- **切换目标**：按Q/E键切换锁定目标
- **恢复基准视角**：按R键

### 3. 战斗动态镜头
```csharp
var camera = player.GetScript<ThirdPersonCamera>();

// 攻击命中
camera.TriggerHitCamera();

// 重击命中
camera.TriggerHitCamera(isHeavy: true);

// 闪避
camera.TriggerDodgeCamera(dodgeDirection);

// 受击
camera.TriggerHitReactionCamera(hitDirection, damage);

// 处决
camera.TriggerExecutionCamera();
```

### 4. 锁定目标
```csharp
// 激活锁定
camera.ActivateLockOn();

// 取消锁定
camera.DeactivateLockOn();

// 切换锁定目标（-1=左，1=右）
camera.SwitchLockOnTarget(1f);

// 获取当前锁定目标
Actor target = camera.GetLockOnTarget();

// 检查是否处于锁定状态
bool isLocked = camera.IsLockOnActive();
```

### 5. 相机相对移动
```csharp
// 在LockOn或OverShoulder状态下，使用相机相对移动
if (camera.ShouldUseCameraRelativeMovement())
{
    Vector3 moveDir = camera.GetCameraRelativeMoveDirection(inputDir);
}
```

## 技术实现要点

### 1. 越肩视角
在OverShoulder/LockOn状态下，相机位置计算增加水平偏移量，通过`CalculateCameraOffsetWithShoulder`方法实现。碰撞检测从肩部位置发射射线，空间不足时自动回退到标准视角。

### 2. 锁定目标系统
硬锁定通过`ActivateLockOn()`激活，相机自动面向锁定目标。软锁定在战斗中自动选择前方锥形范围内最近的敌人。Boss级目标自动切换到BossBattle状态，拉远距离增大FOV。

### 3. 战斗动态镜头
通过`GetCombatCameraOffset()`和`GetCombatFOVOffset()`方法将战斗效果叠加到相机位置和FOV上，使用正弦曲线实现自然的推进-回弹效果。

### 4. 状态机
相机状态机根据角色行为自动检测和切换状态，ARPG状态（OverShoulder/LockOn/BossBattle）优先级高于基础状态（Climbing/Swimming/Flying），状态切换过渡时间≤0.3秒。

## 常见问题及解决方案

### 1. 越肩视角穿墙
确保`CollisionLayerMask`包含墙壁层，并启用`EnableSmartAvoidance`。空间不足时系统会自动回退到标准视角。

### 2. 锁定目标不准确
调整`LockOnDetectionAngle`和`LockOnMaxDistance`参数，确保敌人Actor有正确的碰撞体和Tag设置。

### 3. 战斗镜头效果过强
降低`HitPushDistance`、`HeavyHitPushDistance`等参数值，或设置`EnableCombatCameraEffects = false`关闭战斗镜头效果。

### 4. 相机旋转不流畅
调整`RotationSpeed`参数，或检查是否有其他脚本干扰相机旋转。在LockOn状态下相机会自动面向目标，手动旋转会被覆盖。

## 注意事项

1. 确保目标角色具有正确的碰撞体设置
2. 合理设置`CollisionLayerMask`，避免检测不必要的物体
3. Boss级敌人需要设置`BossTag`对应的Tag
4. 锁定目标系统依赖场景中Actor的Tag和碰撞体
5. 战斗动态镜头效果需要通过外部脚本（如战斗系统）调用触发
6. 拍照功能已提取为独立的PhotoMode脚本
