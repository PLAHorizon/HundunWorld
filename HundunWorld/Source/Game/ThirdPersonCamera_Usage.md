# 第三人称相机系统使用说明

## 概述

这是一个全新的第三人称相机系统，借鉴了剑侠情缘3、魔兽世界、燕云十六声等优秀游戏的相机设计。该系统实现了固定相对距离的相机跟随效果，并具备完善的碰撞检测和地面穿透防护功能。

## 核心特性

1. **固定相对距离控制**：相机与角色保持相对固定的距离，支持鼠标滚轮缩放
2. **碰撞检测**：防止相机穿透墙体或其他物体
3. **地面穿透防护**：防止相机位置过低导致穿透地面
4. **双模式支持**：支持第三人称和第一人称模式切换
5. **参数可调**：提供丰富的参数供开发者调整相机行为

## 主要参数说明

### 基本参数
- `CurrentMode`：当前相机模式（第三人称/第一人称）
- `Distance`：相机与目标的距离（5.0f-30.0f）
- `Pitch`：相机俯仰角（-89.0f-89.0f）
- `Yaw`：相机偏航角（0.0f-360.0f）
- `MinDistance`：相机最小距离（默认5.0f）
- `MaxDistance`：相机最大距离（默认30.0f）
- `MinPitch`：相机最小俯仰角（默认-45.0f）
- `MaxPitch`：相机最大俯仰角（默认45.0f）
- `Offset`：相机相对于目标的偏移量

### 控制参数
- `EnableFixedRelativeDistance`：是否启用固定相对距离模式
- `TargetFixedDistance`：目标固定距离（不受碰撞影响的理想距离）
- `DistanceRecoverySpeed`：距离恢复速度
- `RotationSpeed`：相机旋转速度
- `MinInputValue`：鼠标输入的最小值
- `EnableCameraCollision`：是否启用相机碰撞检测
- `CameraCollisionOffset`：相机阻挡检测偏移量
- `ObstructionLayerMask`：相机遮挡检测层

## 使用方法

### 1. 基本设置
```csharp
// 获取相机脚本
var camera = cameraActor.GetScript<ThirdPersonCamera>();

// 设置目标角色
camera.Target = playerCharacter;

// 设置相机参数
camera.Distance = 15.0f;
camera.Pitch = 30.0f;
camera.Yaw = 45.0f;
```

### 2. 相机控制
- **旋转相机**：按住鼠标右键并拖动
- **缩放相机**：使用鼠标滚轮
- **切换模式**：调用`SwitchCameraMode()`方法

### 3. 参数调整
```csharp
// 启用固定相对距离模式
camera.EnableFixedRelativeDistance = true;

// 设置目标固定距离
camera.TargetFixedDistance = 20.0f;

// 调整距离恢复速度
camera.DistanceRecoverySpeed = 5.0f;
```

## 技术实现要点

### 1. 固定相对距离控制
在固定相对距离模式下，相机会努力保持`TargetFixedDistance`指定的距离。当发生碰撞时，相机会自动调整到安全距离，障碍物消失后会逐渐恢复到目标距离。

### 2. 碰撞检测
使用射线检测技术检测相机与物体之间的碰撞，确保相机不会穿透墙体或其他物体。

### 3. 地面穿透防护
通过地面高度检测和安全检查机制，防止相机位置过低导致穿透地面。

### 4. 角度规范化
自动规范化偏航角到0-360度范围，确保角度值的合理性。

## 常见问题及解决方案

### 1. 相机与角色重叠
确保`MinDistance`参数设置合理（建议不小于5.0f），并启用碰撞检测。

### 2. 相机穿透地面
检查地面物体是否正确设置了碰撞层，并确保`ObstructionLayerMask`包含地面层。

### 3. 相机旋转不流畅
调整`RotationSpeed`参数，或检查是否有其他脚本干扰相机旋转。

## 扩展建议

1. 可以添加弹性跟随效果，使相机移动更加自然
2. 可以实现自动对齐功能，让相机自动跟随角色移动方向
3. 可以添加相机抖动效果，增强游戏体验
4. 可以实现古典美学模式，借鉴燕云十六声的相机设计

## 注意事项

1. 确保目标角色具有正确的碰撞体设置
2. 合理设置`ObstructionLayerMask`，避免检测不必要的物体
3. 根据游戏需求调整距离和角度参数
4. 在复杂场景中测试碰撞检测效果