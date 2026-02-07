# 3D角色预览功能使用说明

## 概述

本文档介绍了如何在角色创建界面中使用3D角色预览功能，包括模型显示、动画播放和用户交互控制。

## 功能特性

1. **3D模型显示** - 支持静态模型和带动画的蒙皮模型
2. **动画播放** - 自动播放默认动画，支持手动选择和播放特定动画
3. **模型旋转** - 支持自动旋转和手动旋转（鼠标拖拽或按钮控制）
4. **响应式设计** - 适配不同屏幕尺寸

## 核心组件

### Viewport3DPreview 类

这是主要的3D预览组件，负责渲染3D模型并在UI中显示。

#### 主要方法：

- `LoadModel(string modelName)` - 加载静态模型
- `LoadAnimatedModel(string modelName)` - 加载带动画的蒙皮模型
- `PlayAnimation(string animationName, bool loop)` - 播放指定动画
- `SetModelRotation(float rotation)` - 设置模型旋转角度
- `SetAutoRotate(bool autoRotate)` - 设置是否自动旋转
- `SetManualRotationEnabled(bool enable)` - 设置是否启用手动旋转

### CharacterAnimator 类

用于控制角色动画的播放。

#### 主要方法：

- `PlayAnimation(string animationName, bool loop)` - 播放动画
- `StopAnimation()` - 停止动画
- `PauseAnimation()` - 暂停动画
- `ResumeAnimation()` - 恢复动画

## 使用方法

### 1. 在UI中添加3D预览组件

```csharp
// 创建预览组件
var modelPreview = new Viewport3DPreview(new Float2(400, 500));
modelPreview.Location = new Float2(10, 10);
parentPanel.AddChild(modelPreview);

// 加载模型
modelPreview.LoadAnimatedModel("SkinnedModels/Characters/Male_Warrior_Skinned");
```

### 2. 控制动画播放

```csharp
// 获取动画控制器
var animator = modelPreview.Animator;

// 播放特定动画
animator.PlayAnimation("Walk", true);

// 获取可用动画列表
string[] animations = animator.AvailableAnimations;
```

### 3. 控制模型旋转

```csharp
// 设置自动旋转
modelPreview.SetAutoRotate(true);

// 手动设置旋转角度
modelPreview.SetModelRotation(45.0f);

// 启用手动旋转控制
modelPreview.SetManualRotationEnabled(true);
```

## 资源要求

### 模型资源

1. **静态模型** - 存放在 `Content/Models/` 目录下
2. **蒙皮模型** - 存放在 `Content/SkinnedModels/` 目录下
3. **动画资源** - 与蒙皮模型关联的动画数据

### 命名约定

推荐使用以下命名约定：

- 静态模型: `Models/Characters/{Gender}_{Profession}`
- 蒙皮模型: `SkinnedModels/Characters/{Gender}_{Profession}_Skinned`

示例：
- `Models/Characters/Male_Warrior`
- `SkinnedModels/Characters/Female_Mage_Skinned`

## 常见动画名称

为了确保兼容性，建议为角色模型提供以下标准动画：

1. `Idle` - 待机动画
2. `Walk` - 行走动画
3. `Run` - 跑步动画
4. `Attack` - 攻击动画
5. `Death` - 死亡动画

## 故障排除

### 模型无法显示

1. 检查模型路径是否正确
2. 确认模型资源已正确导入到项目中
3. 检查控制台是否有加载错误信息

### 动画无法播放

1. 确认使用的是蒙皮模型而非静态模型
2. 检查模型是否包含动画数据
3. 验证动画名称是否正确

### 旋转功能异常

1. 确认已调用 `SetManualRotationEnabled(true)` 启用手动旋转
2. 检查鼠标事件是否被其他UI元素拦截

## 性能优化建议

1. **及时清理资源** - 不再需要预览时调用 `ClearModel()` 方法
2. **合理设置自动旋转** - 在不需要时关闭自动旋转以节省性能
3. **限制同时显示的预览数量** - 避免同时创建过多预览组件

## 扩展功能

开发者可以根据需要扩展以下功能：

1. **多模型显示** - 修改组件以支持同时显示多个模型
2. **模型换装** - 通过更换模型材质实现装备可视化
3. **光照控制** - 添加光源控制以改善模型显示效果
4. **摄像机控制** - 提供更多的摄像机视角控制选项