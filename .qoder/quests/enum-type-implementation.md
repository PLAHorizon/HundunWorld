# 枚举类型实现设计文档

## 1. 概述

本文档描述了在 Horizon.Game.Message 项目中实现 AnimationType、AnimationState 和 EasingType 枚举类型的设计。这些枚举类型将用于支持游戏中的动画系统，包括动画类型、动画状态和缓动效果的定义。

## 2. 架构设计

### 2.1 项目结构
枚举类型将实现在 `Horizon.Game.Message` 项目的 `Enums` 目录下，与现有的枚举类型保持一致的组织结构。

### 2.2 命名空间
所有枚举类型将使用统一的命名空间：`Horizon.Game.Message.Enums`

## 3. 枚举类型定义

### 3.1 AnimationType 枚举
该枚举定义了游戏中可用的动画类型。

| 枚举值 | 描述 |
|--------|------|
| FadeIn | 淡入动画 |
| FadeOut | 淡出动画 |
| SlideIn | 滑入动画 |
| SlideOut | 滑出动画 |
| ScaleIn | 缩放进入动画 |
| ScaleOut | 缩放退出动画 |
| Bounce | 弹跳动画 |
| Elastic | 弹性动画 |
| Pulse | 脉冲动画 |

### 3.2 AnimationState 枚举
该枚举定义了动画的状态。

| 枚举值 | 描述 |
|--------|------|
| Idle | 空闲状态 |
| Playing | 播放中状态 |
| Paused | 暂停状态 |
| Completed | 完成状态 |
| Stopped | 停止状态 |

### 3.3 EasingType 枚举
该枚举定义了动画的缓动效果类型。

| 枚举值 | 描述 |
|--------|------|
| Linear | 线性缓动 |
| EaseIn | 渐入缓动 |
| EaseOut | 渐出缓动 |
| EaseInOut | 渐入渐出缓动 |
| EaseInQuad | 二次方渐入缓动 |
| EaseOutQuad | 二次方渐出缓动 |
| EaseInOutQuad | 二次方渐入渐出缓动 |
| EaseInCubic | 三次方渐入缓动 |
| EaseOutCubic | 三次方渐出缓动 |
| EaseInOutCubic | 三次方渐入渐出缓动 |
| EaseInQuart | 四次方渐入缓动 |
| EaseOutQuart | 四次方渐出缓动 |
| EaseInOutQuart | 四次方渐入渐出缓动 |
| EaseInQuint | 五次方渐入缓动 |
| EaseOutQuint | 五次方渐出缓动 |
| EaseInOutQuint | 五次方渐入渐出缓动 |
| EaseInSine | 正弦渐入缓动 |
| EaseOutSine | 正弦渐出缓动 |
| EaseInOutSine | 正弦渐入渐出缓动 |
| EaseInExpo | 指数渐入缓动 |
| EaseOutExpo | 指数渐出缓动 |
| EaseInOutExpo | 指数渐入渐出缓动 |
| EaseInCirc | 圆形渐入缓动 |
| EaseOutCirc | 圆形渐出缓动 |
| EaseInOutCirc | 圆形渐入渐出缓动 |
| EaseInBack | 后退渐入缓动 |
| EaseOutBack | 后退渐出缓动 |
| EaseInOutBack | 后退渐入渐出缓动 |
| EaseInBounce | 弹跳渐入缓动 |
| EaseOutBounce | 弹跳渐出缓动 |
| EaseInOutBounce | 弹跳渐入渐出缓动 |
| EaseInElastic | 弹性渐入缓动 |
| EaseOutElastic | 弹性渐出缓动 |
| EaseInOutElastic | 弹性渐入渐出缓动 |

## 4. 文件组织

### 4.1 新增文件
将在 `Horizon.Game.Message/Enums` 目录下创建以下文件：

1. `AnimationEnums.cs` - 包含 AnimationType 和 AnimationState 枚举
2. `EasingEnums.cs` - 包含 EasingType 枚举

### 4.2 文件内容结构
每个文件将遵循以下结构：
- 使用 MemoryPackable 特性以支持序列化
- 使用统一的命名空间
- 包含详细的注释说明每个枚举值的用途

## 5. 实现要求

### 5.1 特性标记
所有枚举类型都需要添加 `[MemoryPackable]` 特性，以便支持序列化功能。

### 5.2 注释规范
每个枚举值都需要添加注释，说明其用途和含义。

### 5.3 命名规范
枚举值采用 PascalCase 命名规范，保持与项目中其他枚举的一致性。