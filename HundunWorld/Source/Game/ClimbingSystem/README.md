# 攀爬系统使用说明

## 简介

本攀爬系统为Flax引擎中的角色控制器提供了一个完整的攀爬解决方案，支持多种攀爬类型，包括：
- 低边缘攀爬（如窗台）
- 高边缘攀爬（如墙壁顶部）
- 垂直墙面攀爬
- 水平横杆攀爬

## 系统组件

### 1. ClimbDetector（攀爬检测器）
负责检测角色周围的可攀爬表面。

### 2. ClimbingController（攀爬控制器）
处理角色的攀爬逻辑和状态管理。

### 3. ClimbingEnums（枚举定义）
定义了攀爬状态和类型枚举。

## 安装和设置

### 1. 添加脚本组件

在角色的Actor上添加以下组件：
1. `ClimbDetector`
2. `ClimbingController`

确保这些组件与`PlayerController`在同一父级Actor上。

### 2. 配置输入

在输入管理器中添加"Climb"动作，通常映射到"F"键。

### 3. 配置参数

根据需要调整以下参数：

#### ClimbDetector参数：
- `ForwardDetectionDistance`: 前方检测距离（默认1.5）
- `UpwardDetectionDistance`: 上方检测距离（默认2.0）
- `EdgeHeightOffset`: 边缘检测高度偏移（默认0.2）
- `MinClimbableHeight`: 可攀爬表面的最小高度（默认0.5）
- `MaxClimbableHeight`: 可攀爬表面的最大高度（默认3.0）

#### ClimbingController参数：
- `ClimbSpeed`: 攀爬速度（默认2.0）
- `MantleSpeed`: 攀爬到顶部的速度（默认1.5）
- `GrabSpeed`: 抓取边缘的速度（默认3.0）
- `EnableClimbing`: 是否启用攀爬（默认true）
- `ClimbCooldown`: 攀爬冷却时间（秒，默认1.0）

## 使用方法

### 基本使用
1. 确保角色面向可攀爬表面
2. 按下"F"键（或配置的攀爬键）开始攀爬
3. 系统会自动检测边缘类型并执行相应的攀爬动画

### 攀爬控制
- **开始攀爬**：按下攀爬键（默认F）
- **取消攀爬**：在攀爬过程中按下跳跃键（空格）或蹲伏键（C）
- **自动完成**：攀爬会自动完成，角色会移动到攀爬目标位置

## 攀爬状态

系统支持以下攀爬状态：
1. `None`: 无攀爬状态
2. `ApproachingEdge`: 接近可攀爬边缘
3. `GrabbingEdge`: 抓住边缘
4. `Hanging`: 悬挂在边缘
5. `Mantling`: 攀爬到顶部
6. `Climbing`: 垂直攀爬
7. `Finished`: 攀爬结束

## 攀爬类型

系统支持以下攀爬类型：
1. `LowEdge`: 低边缘攀爬（如窗台）
2. `HighEdge`: 高边缘攀爬（如墙壁顶部）
3. `VerticalWall`: 垂直墙面攀爬
4. `HorizontalBar`: 水平横杆攀爬

## 扩展和自定义

### 添加新的攀爬类型
1. 在`ClimbType`枚举中添加新的类型
2. 在`ClimbDetector`中实现新的检测逻辑
3. 在`ClimbingController`中实现新的攀爬行为

### 调整攀爬动画
可以通过修改`ClimbingController`中的时间参数来调整攀爬动画的速度：
- `GetMantleDuration()`: 攀爬到顶部所需的时间
- `GetGrabDuration()`: 抓取边缘所需的时间
- `GetHangDuration()`: 悬挂时间

## 注意事项

1. 确保可攀爬表面使用了正确的碰撞体
2. 攀爬系统会暂时禁用角色的移动控制
3. 攀爬过程中角色不会受到重力影响
4. 攀爬完成后角色会恢复正常控制

## 故障排除

### 攀爬检测不到
- 检查表面是否有碰撞体
- 检查检测距离参数是否合适
- 检查检测层掩码设置

### 攀爬动画异常
- 检查时间参数设置
- 检查角色控制器是否被正确禁用
- 检查是否有其他脚本干扰角色位置

## 版本信息

当前版本：1.0.0
兼容Flax引擎版本：1.10+