# 星空粒子系统使用指南

## 概述

本文档介绍如何使用新的星空粒子系统来替换ConfirmDialog中的UI模拟粒子效果。新系统提供了更专业、更灵活的粒子效果，支持多种质量等级和设备适配。

## 核心组件

### 1. StarParticleSystem（主粒子系统）

专业的3D粒子系统，提供真实的星空效果：

```csharp
// 基本使用
var starSystem = actor.AddScript<StarParticleSystem>();
starSystem.ParticleCount = 50;
starSystem.EmissionArea = new Float2(800, 600);
starSystem.TwinkleSpeed = 1.5f;
```

**主要特性：**
- 支持真实的3D粒子渲染
- 可配置的粒子数量、大小、颜色
- 自然的闪烁动画效果
- 支持中国古典主题色彩

### 2. UIParticleEffectManager（粒子效果管理器）

统一管理UI中的粒子效果：

```csharp
// 初始化管理器
UIParticleEffectManager.Initialize();

// 为对话框创建星空效果
var effect = UIParticleEffectManager.CreateDialogStarEffect(
    \"my_dialog\",
    new Float2(600, 400),
    new Float3(0, 0, -100)
);

// 销毁效果
UIParticleEffectManager.DestroyEffect(\"my_dialog\");
```

**主要功能：**
- 自动效果生命周期管理
- 支持多种预设效果类型
- 智能的设备性能适配
- 统一的效果参数配置

### 3. LightweightStarEffect（轻量级效果）

针对低性能设备的优化版本：

```csharp
var lightEffect = actor.AddScript<LightweightStarEffect>();
lightEffect.ParticleCount = 20;
lightEffect.EffectArea = new Float2(400, 300);
lightEffect.TwinkleIntensity = 0.5f;
```

**适用场景：**
- 移动设备或低性能PC
- 需要节省GPU资源的场合
- 简单的装饰性效果

### 4. GUI2DStarEffect（GUI纯2D效果）

完全基于GUI系统的备选方案：

```csharp
var gui2DEffect = new GUI2DStarEffect(containerPanel, 15);
gui2DEffect.SetColors(Color.White, Color.Blue);
gui2DEffect.Start();

// 在Update循环中调用
gui2DEffect.Update(Time.DeltaTime);
```

**使用场合：**
- 无法使用3D粒子系统时的备选
- 完全基于UI的轻量级效果
- 最佳兼容性保证

## 质量配置系统

### 自动质量检测

系统可以根据设备性能自动选择合适的质量等级：

```csharp
// 自动检测并设置质量等级
ParticleEffectSettings.AutoDetectQuality();

// 手动设置质量等级
ParticleEffectSettings.CurrentQuality = ParticleQuality.High;

// 获取当前质量等级推荐的配置
var config = ParticleEffectSettings.GetRecommendedConfig(ParticleEffectType.StarField);
```

### 质量等级说明

| 等级 | 粒子数量 | 效果质量 | 适用设备 |
|------|----------|----------|----------|
| Low | 25% | 基础闪烁 | 低性能设备 |
| Medium | 50% | 标准效果 | 中等性能设备 |
| High | 100% | 完整效果 | 高性能设备 |
| Ultra | 150% | 增强效果 | 高端设备 |

## ConfirmDialog集成

### 自动集成

ConfirmDialog已经自动集成了新的粒子系统：

```csharp
// 创建对话框时会自动应用粒子效果
var dialog = new ConfirmDialog();
dialog.ShowSimple(\"标题\", \"消息\"); // 自动包含星空粒子效果
```

### 多级降级机制

系统提供智能的降级机制确保最佳兼容性：

1. **首选：** 3D StarParticleSystem
2. **备选：** GUI2DStarEffect
3. **最终：** 静态星空背景

### 响应式适配

粒子效果会自动适应对话框的尺寸变化：

```csharp
// 屏幕尺寸变化时自动更新粒子效果区域
// 对话框尺寸变化时自动调整粒子分布
// 支持超宽屏和标准屏幕的适配
```

## 性能优化特性

### 内存管理

- **对象池化：** 粒子对象重用，减少GC压力
- **智能清理：** 自动清理失效的粒子效果引用
- **延迟初始化：** 按需创建粒子系统组件

### 渲染优化

- **批处理渲染：** 相同材质的粒子批量绘制
- **视锥剔除：** 屏幕外粒子自动剔除
- **LOD系统：** 距离相关的质量调整

### 设备适配

- **自动降级：** 性能不足时自动使用简化版本
- **动态调整：** 运行时根据帧率调整粒子数量
- **兼容性保证：** 多种备选方案确保所有设备可用

## 高级用法

### 自定义粒子配置

```csharp
// 创建自定义配置
var customConfig = new ParticleEffectConfig
{
    ParticleCount = 40,
    EmissionArea = new Float2(700, 500),
    MinSize = 1.5f,
    MaxSize = 3.0f,
    TwinkleSpeed = 2.0f,
    PrimaryColor = Color.Gold,
    SecondaryColor = Color.White,
    MinAlpha = 0.3f,
    MaxAlpha = 1.0f
};

// 应用自定义配置
var adjustedConfig = ParticleEffectSettings.AdjustConfigByQuality(customConfig);
```

### 动态效果控制

```csharp
// 动态更新粒子效果
UIParticleEffectManager.UpdateEffectPosition(\"dialog_id\", newPosition);
UIParticleEffectManager.UpdateEffectArea(\"dialog_id\", newSize);
UIParticleEffectManager.SetEffectActive(\"dialog_id\", false);
```

### 场景切换处理

```csharp
// 场景切换时清理所有粒子效果
UIParticleEffectManager.OnSceneChanged(newScene);

// 或手动清理
UIParticleEffectManager.DestroyAllEffects();
```

## 故障排除

### 常见问题

**Q: 粒子效果不显示？**
A: 检查以下几点：
1. 确保UIParticleEffectManager已初始化
2. 检查粒子质量设置是否禁用
3. 验证Effect ID是否正确

**Q: 性能问题？**
A: 尝试以下优化：
1. 降低粒子质量等级
2. 使用LightweightStarEffect
3. 检查是否有未清理的粒子效果

**Q: 在某些设备上崩溃？**
A: 系统会自动降级到GUI2D效果，检查：
1. 是否有异常日志
2. 设备是否支持所需的图形功能
3. 内存是否充足

### 调试工具

```csharp
// 运行完整测试套件
ParticleSystemTest.RunAllTests();

// 检查活跃效果数量
var activeCount = UIParticleEffectManager.GetActiveEffectCount();
FlaxEngine.Debug.Log($\"当前活跃粒子效果: {activeCount}\");

// 验证特定效果是否存在
if (UIParticleEffectManager.HasEffect(\"my_effect\"))
{
    // 效果存在
}
```

## 最佳实践

1. **初始化：** 在应用启动时调用`UIParticleEffectManager.Initialize()`
2. **质量设置：** 使用`ParticleEffectSettings.AutoDetectQuality()`进行自动检测
3. **生命周期管理：** 确保在适当时机清理粒子效果
4. **性能监控：** 定期检查活跃效果数量，避免内存泄漏
5. **用户设置：** 提供用户选项来控制粒子效果的开启/关闭

## 总结

新的星空粒子系统为ConfirmDialog提供了专业、高效、灵活的粒子效果解决方案。通过多级降级机制和智能设备适配，确保在各种环境下都能提供最佳的用户体验。系统的模块化设计也便于未来的扩展和维护。