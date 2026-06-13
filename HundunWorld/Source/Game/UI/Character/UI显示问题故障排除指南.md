# 角色创建UI显示问题故障排除指南

## 问题诊断步骤

### 1. 检查控制台输出
运行游戏后查看控制台输出，应该能看到以下日志：
```
[CharacterCreationScene] 开始初始化UI...
[CharacterCreationScene] 找到UI Canvas: True
[IntegratedCharacterCreationUI] 开始初始化...
[IntegratedCharacterCreationUI] 创建中央3D预览区域...
[IntegratedCharacterCreationUI] 中央面板创建完成 - 尺寸: xxxxx
[IntegratedCharacterCreationUI] 初始化完成 - 尺寸: 1200x800, 可见: False
[CharacterCreationScene] UI创建完成 - 可见: False, 尺寸: 1200x800
[IntegratedCharacterCreationUI] Show方法调用前 - Visible: False
[IntegratedCharacterCreationUI] Show方法调用后 - Visible: True
[CharacterCreationScene] UI显示完成 - 最终可见: True
```

### 2. 常见问题及解决方案

#### 问题1: 找不到UI Canvas
**错误信息**: `[CharacterCreationScene] 找不到UI Canvas`

**解决方案**:
- 确保场景中有UICanvas组件
- 检查Actor是否正确添加了UICanvas脚本
- 验证场景层级结构是否正确

#### 问题2: UI创建但不可见
**现象**: 控制台显示创建成功但屏幕上看不到UI

**检查点**:
- 确认`Visible = true`已正确设置
- 检查父级容器是否可见
- 验证AnchorPreset设置是否正确
- 确认没有其他UI元素遮挡

#### 问题3: 布局显示异常
**现象**: UI元素位置错乱或尺寸不正确

**解决方案**:
- 检查AnchorPreset设置
- 验证Offsets参数是否合理
- 确认Parent关系正确建立
- 检查是否有布局冲突

### 3. 调试技巧

#### 添加可视化调试
```csharp
// 在关键位置添加颜色标记
panel.BackgroundColor = Color.Red; // 调试时临时使用鲜艳颜色
```

#### 层级检查
```csharp
// 检查Parent关系
Debug.Log($"Parent: {control.Parent?.GetType()}, Index: {control.IndexInParent}");
```

#### 尺寸验证
```csharp
// 验证实际尺寸
Debug.Log($"实际尺寸: {control.Width}x{control.Height}, 锚点: {control.AnchorPreset}");
```

### 4. 性能检查

#### 渲染性能
- 中央3D预览区域可能影响性能
- 检查是否有过多的渲染调用
- 验证GPU资源使用情况

#### 内存使用
- 监控内存占用
- 检查资源加载是否成功
- 验证对象销毁是否正确

### 5. 兼容性检查

#### 分辨率适配
- 测试不同屏幕分辨率下的显示效果
- 验证锚点系统在各种比例下的表现
- 检查字体和图标在不同DPI下的清晰度

#### 设备兼容性
- 测试在目标平台上的表现
- 验证触控操作的响应性
- 检查键盘输入的支持情况

## 紧急修复方案

如果以上方法都无法解决问题，可以尝试以下简化版本：

```csharp
// 简化的UI创建方法
private void CreateSimpleUI()
{
    // 创建一个简单的测试面板
    var testPanel = new Panel
    {
        Parent = this,
        Size = new Float2(400, 300),
        Location = new Float2(100, 100),
        BackgroundColor = Color.Blue,
        Visible = true
    };
    
    var testLabel = new Label
    {
        Parent = testPanel,
        Text = "测试UI显示",
        Location = new Float2(50, 50),
        Size = new Float2(200, 50)
    };
}
```

通过这个简化版本可以快速验证基础显示功能是否正常工作。