# 第三人称相机和角色控制器改进说明

## 概述

本文档说明了对第三人称相机和角色控制器的改进功能。

## 改进的功能

### ThirdPersonCamera 改进

1. **增强的碰撞检测**
   - 添加了相机阻挡检测偏移量参数
   - 可以启用/禁用相机碰撞检测
   - 更精确的碰撞点计算

2. **平滑跟随功能**
   - 添加了相机移动平滑度参数
   - 添加了相机旋转平滑度参数
   - 可以启用/禁用平滑跟随

3. **更多可调参数**
   - 相机偏移量默认设置为(0, 2, 0)，使相机更贴近角色
   - 调整了距离限制范围
   - 添加了相机碰撞检测偏移量

4. **改进的相机抖动**
   - 使用NextFloat方法生成更平滑的抖动效果

### PlayerController 改进

1. **角色朝向优化**
   - 添加了旋转平滑度参数
   - 使用Quaternion.Lerp实现平滑旋转
   - 统一了键盘移动和点击移动的朝向逻辑

2. **移动逻辑优化**
   - 修复了PerformGroundRaycast方法
   - 改进了角色移动和朝向的同步

3. **参数调整**
   - 添加了旋转平滑度参数
   - 保持了原有的移动速度、跳跃力度和重力参数

## 测试脚本

### CameraControllerTest
用于测试相机控制器的各种功能：
- 按 T 键开始/停止自动旋转测试
- 按 Y 键应用测试参数
- 按 U 键重置相机参数

### CharacterControllerTest
用于测试角色控制器的各种功能：
- 按 I 键开始/停止自动移动测试
- 按 O 键应用测试参数
- 按 P 键重置角色参数

## 使用方法

1. 确保场景中包含名为"Camera"和"Player"的Actor
2. 将GameSceneInitializer脚本添加到场景中的某个Actor上
3. 根据需要调整初始化参数
4. 运行游戏，相机和角色控制器将自动初始化

## 参数说明

### ThirdPersonCamera 参数
- `Distance`: 相机与目标的距离 (2.0f - 50.0f)
- `Pitch`: 相机的俯仰角 (-89.0f - 89.0f)
- `Yaw`: 相机的偏航角
- `MinDistance`: 相机最小距离
- `MaxDistance`: 相机最大距离
- `MinPitch`: 相机最小俯仰角
- `MaxPitch`: 相机最大俯仰角
- `Offset`: 相机偏移量，默认(0, 2, 0)
- `CameraSmoothing`: 相机移动平滑度 (0.0f - 1.0f)
- `RotationSmoothing`: 相机旋转平滑度 (0.0f - 1.0f)
- `CameraCollisionOffset`: 相机阻挡检测偏移量
- `EnableCameraCollision`: 是否启用相机碰撞检测
- `EnableSmoothFollow`: 是否启用相机平滑跟随

### PlayerController 参数
- `MoveSpeed`: 角色移动速度
- `JumpForce`: 角色跳跃力度
- `Gravity`: 重力
- `RotationSmoothing`: 角色旋转平滑度 (0.0f - 1.0f)

### GameSceneInitializer 参数
- `CameraActorName`: 相机Actor名称
- `PlayerActorName`: 玩家Actor名称
- `EnableCameraCollision`: 是否启用相机碰撞检测
- `EnableSmoothFollow`: 是否启用相机平滑跟随