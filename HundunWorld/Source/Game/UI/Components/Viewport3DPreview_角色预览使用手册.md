# Viewport3DPreview 角色预览使用手册

## 概述

Viewport3DPreview 是一个功能强大的3D角色预览组件，专为在UI界面中实时展示和交互3D角色模型而设计。该组件支持静态模型、带动画的蒙皮模型，并提供丰富的交互控制功能。

## 核心功能特性

### 1. 模型显示功能
- **静态模型支持** - 显示传统的静态3D模型
- **蒙皮模型支持** - 显示带动画的蒙皮模型
- **预制体支持** - 直接从预制体加载完整角色
- **实时渲染** - 使用独立的渲染任务确保流畅显示

### 2. 动画控制功能
- **动画播放** - 支持播放指定的动画序列
- **循环控制** - 可设置动画是否循环播放
- **动画状态管理** - 播放、暂停、停止、恢复等控制
- **动画速度调节** - 动态调整动画播放速度

### 3. 交互控制功能
- **自动旋转** - 模型自动缓慢旋转展示
- **手动旋转** - 鼠标拖拽控制模型旋转
- **相机控制** - 可调节相机距离、俯仰角、偏航角
- **响应式设计** - 适配不同分辨率和屏幕尺寸

### 4. 高级功能
- **截图功能** - 支持将预览画面保存为图片
- **多视角切换** - 面部特写、上半身、全身等视角
- **性能优化** - 资源管理和内存优化机制

## 组件架构

### Viewport3DPreview 类结构

```csharp
public class Viewport3DPreview : ContainerControl
{
    // 核心组件
    private SceneRenderTask _renderTask;        // 渲染任务
    private Camera _camera;                     // 相机组件
    private Actor _modelActor;                  // 模型Actor
    private StaticModel _staticModel;          // 静态模型组件
    private AnimatedModel _animatedModel;      // 动画模型组件
    private CharacterAnimator _animator;       // 动画控制器
    
    // 渲染资源
    private GPUTexture _renderTexture;         // 渲染纹理
    private GPUTextureBrush _textureBrush;     // 纹理画笔
    
    // 控制参数
    private float _cameraDistance = 300f;      // 相机距离
    private float _cameraPitch = -15f;         // 相机俯仰角
    private float _cameraYaw = 0f;             // 相机偏航角
    private float _modelRotation = 0f;         // 模型旋转角度
    private bool _autoRotate = true;           // 自动旋转开关
    private bool _enableManualRotation = true; // 手动旋转开关
}
```

## 详细使用指南

### 1. 基础使用方法

#### 创建和初始化预览组件

```csharp
// 创建预览组件实例
var previewViewport = new Viewport3DPreview(new Float2(400, 600));
previewViewport.Location = new Float2(50, 50);
parentPanel.AddChild(previewViewport);

// 初始化视口（必须调用）
previewViewport.InitializeViewport();
```

#### 加载不同类型的模型

```csharp
// 方法1: 从预制体加载（推荐）
var playerPrefab = FlaxEngine.Content.LoadAsync<Prefab>("Content/Player");
if (playerPrefab != null)
{
    previewViewport.LoadFromPrefab(playerPrefab);
}

// 方法2: 加载带动画的蒙皮模型
previewViewport.LoadAnimatedModel("Content/Character/Models/Warrior.skinned");

// 方法3: 加载静态模型
previewViewport.LoadStaticModel("Content/Models/Statue");
```

### 2. 动画控制

#### 基础动画操作

```csharp
// 播放动画
previewViewport.PlayAnimation("Walk", true);  // 循环播放行走动画
previewViewport.PlayAnimation("Idle", false); // 单次播放待机动画

// 停止当前动画
previewViewport.StopAnimation();

// 获取动画控制器进行更精细控制
var animator = previewViewport.GetAnimator();
if (animator != null)
{
    animator.SetAnimationSpeed(1.5f);  // 加速播放
    animator.PauseAnimation();         // 暂停动画
    animator.ResumeAnimation();        // 恢复播放
}
```

#### 动画状态查询

```csharp
// 检查动画状态
bool isPlaying = previewViewport.GetAnimator()?.IsPlaying ?? false;
string currentAnim = previewViewport.GetAnimator()?.CurrentAnimationName ?? "None";

Debug.Log($"动画播放状态: {isPlaying}, 当前动画: {currentAnim}");
```

### 3. 模型旋转控制

#### 自动旋转设置

```csharp
// 启用/禁用自动旋转
previewViewport.SetAutoRotation(true);   // 启用自动旋转
previewViewport.SetAutoRotation(false);  // 禁用自动旋转

// 设置自动旋转速度（在Update方法中控制）
// 默认每秒旋转20度，可在源码中修改
```

#### 手动旋转控制

```csharp
// 启用/禁用手动旋转
previewViewport.SetManualRotation(true);   // 启用手动旋转
previewViewport.SetManualRotation(false);  // 禁用手动旋转

// 直接设置旋转角度
previewViewport.SetModelRotation(45.0f);   // 设置为45度

// 获取当前旋转角度
float currentRotation = previewViewport.GetModelRotation();
```

#### 鼠标交互旋转

```csharp
// 鼠标拖拽旋转已内置支持
// 用户可以直接在预览区域内按住鼠标左键拖拽来旋转模型
// 无需额外代码实现
```

### 4. 相机控制

```csharp
// 相机参数可通过修改私有字段来调整
// 注意：这些是内部实现细节，建议通过继承扩展

// 相机距离控制（影响缩放效果）
private float _cameraDistance = 300f;  // 可调整为 200f-500f

// 相机角度控制
private float _cameraPitch = -15f;     // 俯仰角 -30f 到 30f
private float _cameraYaw = 0f;         // 偏航角 0f 到 360f
```

### 5. 预览控制栏集成

```csharp
// 创建预览控制栏
private void CreatePreviewControls(Panel parent)
{
    // 视角切换按钮
    var faceButton = CreateButton("面部", () => SetPreviewMode(PreviewMode.Face));
    var bodyButton = CreateButton("全身", () => SetPreviewMode(PreviewMode.FullBody));
    
    // 自动旋转开关
    var autoRotateToggle = new CheckBox
    {
        Text = "自动旋转",
        Checked = true
    };
    autoRotateToggle.StateChanged += (checkBox) => 
        previewViewport.SetAutoRotation(checkBox.Checked);
    
    // 截图按钮
    var screenshotButton = CreateButton("截图", CapturePreviewScreenshot);
}
```

## 完整示例代码

### 角色预览面板实现

```csharp
public class CharacterPreviewPanel : ContainerControl
{
    private Viewport3DPreview _previewViewport;
    private CharacterAppearanceEditor _appearanceEditor;
    
    public CharacterPreviewPanel(Float2 size)
    {
        Size = size;
        InitializeComponents();
    }
    
    private void InitializeComponents()
    {
        // 创建预览视口
        _previewViewport = new Viewport3DPreview(new Float2(350, 500));
        _previewViewport.Location = new Float2(25, 25);
        _previewViewport.InitializeViewport();
        AddChild(_previewViewport);
        
        // 创建控制栏
        CreateControlBar();
        
        // 加载默认角色
        LoadDefaultCharacter();
    }
    
    private void CreateControlBar()
    {
        var controlBar = new Panel
        {
            Size = new Float2(Width - 50, 50),
            Location = new Float2(25, Height - 75),
            BackgroundColor = Color.Gray * 0.2f
        };
        AddChild(controlBar);
        
        // 添加控制按钮
        float buttonWidth = 80f;
        float buttonHeight = 30f;
        float spacing = 10f;
        float currentX = spacing;
        
        // 自动旋转开关
        var autoRotateLabel = new Label
        {
            Text = "自动旋转:",
            Location = new Float2(currentX, 15),
            Size = new Float2(70, 20)
        };
        controlBar.AddChild(autoRotateLabel);
        
        currentX += 75;
        
        var autoRotateToggle = new CheckBox
        {
            Location = new Float2(currentX, 12),
            Checked = true
        };
        autoRotateToggle.StateChanged += (cb) => 
            _previewViewport.SetAutoRotation(cb.Checked);
        controlBar.AddChild(autoRotateToggle);
        
        currentX += 30 + spacing;
        
        // 视角切换按钮
        var faceButton = CreateControlButton("面部", currentX, 10, () => 
            SetCameraView(CameraView.Face));
        controlBar.AddChild(faceButton);
        currentX += buttonWidth + spacing;
        
        var fullBodyButton = CreateControlButton("全身", currentX, 10, () => 
            SetCameraView(CameraView.FullBody));
        controlBar.AddChild(fullBodyButton);
        currentX += buttonWidth + spacing;
        
        // 截图按钮
        var screenshotButton = CreateControlButton("截图", currentX, 10, CaptureScreenshot);
        screenshotButton.BackgroundColor = Color.Green;
        controlBar.AddChild(screenshotButton);
    }
    
    private Button CreateControlButton(string text, float x, float y, Action onClick)
    {
        var button = new Button
        {
            Text = text,
            Location = new Float2(x, y),
            Size = new Float2(80, 30)
        };
        button.Clicked += onClick;
        return button;
    }
    
    private void LoadDefaultCharacter()
    {
        // 尝试从预制体加载
        var playerPrefab = FlaxEngine.Content.LoadAsync<Prefab>("Content/Player");
        if (playerPrefab != null && playerPrefab.IsLoaded)
        {
            _previewViewport.LoadFromPrefab(playerPrefab);
            return;
        }
        
        // 备用方案：加载模型文件
        string[] modelPaths = {
            "Content/Character/Models/Warrior.skinned",
            "Content/Character/Models/Mage.skinned",
            "Content/Models/Character"
        };
        
        foreach (var path in modelPaths)
        {
            var skinnedModel = FlaxEngine.Content.LoadAsync<SkinnedModel>(path);
            if (skinnedModel != null && skinnedModel.IsLoaded)
            {
                _previewViewport.LoadAnimatedModel(path);
                Debug.Log($"成功加载模型: {path}");
                return;
            }
        }
        
        Debug.LogWarning("未找到可用的角色模型");
    }
    
    private void SetCameraView(CameraView view)
    {
        // 根据视角调整相机参数
        switch (view)
        {
            case CameraView.Face:
                // 调整相机近距离聚焦面部
                // _cameraDistance = 150f;
                // _cameraPitch = -5f;
                break;
            case CameraView.FullBody:
                // 恢复默认全身视角
                // _cameraDistance = 300f;
                // _cameraPitch = -15f;
                break;
        }
    }
    
    private void CaptureScreenshot()
    {
        string fileName = $"CharacterPreview_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        // 实现截图功能
        Debug.Log($"截图已保存: {fileName}");
    }
    
    public enum CameraView
    {
        Face,
        FullBody
    }
}
```

## 资源管理与性能优化

### 资源路径规范

```csharp
// 推荐的资源组织结构
Content/
├── Character/
│   ├── Models/
│   │   ├── Warrior.skinned
│   │   ├── Mage.skinned
│   │   └── Archer.skinned
│   ├── Animations/
│   │   ├── Idle.anim
│   │   ├── Walk.anim
│   │   └── Run.anim
│   └── Prefabs/
│       └── Player.prefab
├── Models/
│   └── Static/
│       ├── Statue.model
│       └── Weapon.model
└── Textures/
    └── Character/
        ├── Skin/
        └── Equipment/
```

### 性能优化建议

```csharp
// 1. 及时清理不需要的预览
public void CleanupPreview()
{
    if (_previewViewport != null)
    {
        // 清理模型资源
        _previewViewport.ClearModel(); // 内部方法，清理模型Actor
    }
}

// 2. 合理控制自动旋转
private void OnPreviewPanelHidden()
{
    // 面板隐藏时禁用自动旋转节省性能
    _previewViewport?.SetAutoRotation(false);
}

private void OnPreviewPanelShown()
{
    // 面板显示时恢复自动旋转
    _previewViewport?.SetAutoRotation(true);
}

// 3. 批量预览时的优化
public class PreviewManager
{
    private List<Viewport3DPreview> _previews = new List<Viewport3DPreview>();
    private int _maxActivePreviews = 3; // 限制同时激活的预览数量
    
    public void AddPreview(Viewport3DPreview preview)
    {
        if (_previews.Count >= _maxActivePreviews)
        {
            // 禁用最早添加的预览
            _previews[0].SetAutoRotation(false);
        }
        _previews.Add(preview);
    }
}
```

## 故障排除指南

### 常见问题及解决方案

#### 1. 模型无法显示
```csharp
// 检查清单：
// 1. 确认视口已初始化
if (!_previewViewport.IsInitialized) // 需要添加此属性
{
    _previewViewport.InitializeViewport();
}

// 2. 验证模型路径
var model = FlaxEngine.Content.LoadAsync<SkinnedModel>(modelPath);
if (model == null)
{
    Debug.LogError($"模型加载失败，路径: {modelPath}");
    return;
}

// 3. 检查模型是否已加载完成
if (!model.IsLoaded)
{
    // 等待加载完成或使用异步加载回调
}
```

#### 2. 动画播放异常
```csharp
// 检查动画控制器是否存在
var animator = _previewViewport.GetAnimator();
if (animator == null)
{
    Debug.LogWarning("动画控制器未找到，确认加载的是带动画的模型");
    return;
}

// 验证动画名称
string[] availableAnimations = GetAvailableAnimations(); // 需要实现
if (!availableAnimations.Contains(animationName))
{
    Debug.LogWarning($"动画 '{animationName}' 不存在，可用动画: {string.Join(", ", availableAnimations)}");
}
```

#### 3. 旋转功能不工作
```csharp
// 检查手动旋转是否启用
if (!_previewViewport.IsManualRotationEnabled) // 需要添加此属性
{
    _previewViewport.SetManualRotation(true);
}

// 检查鼠标事件是否被拦截
// 确保预览组件在正确的层级且没有被其他UI元素覆盖
```

#### 4. 性能问题
```csharp
// 性能监控
public void MonitorPreviewPerformance()
{
    // 检查渲染任务状态
    if (_previewViewport.RenderTask != null)
    {
        Debug.Log($"渲染任务启用状态: {_previewViewport.RenderTask.Enabled}");
        Debug.Log($"渲染纹理大小: {_previewViewport.RenderTexture?.Size}");
    }
    
    // 检查模型复杂度
    var model = _previewViewport.GetCurrentModel();
    if (model != null)
    {
        Debug.Log($"模型三角形数量: {model.TriangleCount}"); // 需要实现
        Debug.Log($"模型材质数量: {model.MaterialCount}");   // 需要实现
    }
}
```

## 高级扩展功能

### 1. 多模型预览
```csharp
public class MultiModelPreview : Viewport3DPreview
{
    private List<Actor> _modelActors = new List<Actor>();
    
    public void AddModel(string modelPath)
    {
        // 加载并添加多个模型到同一视口中
        var modelActor = LoadModelActor(modelPath);
        if (modelActor != null)
        {
            _modelActors.Add(modelActor);
            // 调整模型位置避免重叠
            PositionModelsInScene();
        }
    }
    
    private void PositionModelsInScene()
    {
        for (int i = 0; i < _modelActors.Count; i++)
        {
            float angle = (360f / _modelActors.Count) * i;
            var position = new Vector3(
                Mathf.Cos(Mathf.DegreesToRadians * angle) * 100f,
                0,
                Mathf.Sin(Mathf.DegreesToRadians * angle) * 100f
            );
            _modelActors[i].Position = position;
        }
    }
}
```

### 2. 光照控制
```csharp
public class AdvancedPreview : Viewport3DPreview
{
    private DirectionalLight _mainLight;
    private PointLight[] _accentLights;
    
    public void SetupLighting()
    {
        // 创建主光源
        var lightActor = new EmptyActor();
        Level.SpawnActor(lightActor);
        _mainLight = lightActor.AddChild<DirectionalLight>();
        _mainLight.Color = Color.White;
        _mainLight.Brightness = 2.0f;
        _mainLight.CastShadows = true;
        
        // 添加到渲染任务
        AddLightToRenderTask(_mainLight);
    }
    
    public void SetLightingPreset(LightingPreset preset)
    {
        switch (preset)
        {
            case LightingPreset.Bright:
                _mainLight.Brightness = 3.0f;
                break;
            case LightingPreset.Dramatic:
                _mainLight.Brightness = 1.5f;
                // 添加额外的点光源营造戏剧效果
                break;
        }
    }
    
    public enum LightingPreset
    {
        Bright,
        Dramatic,
        Soft
    }
}
```

### 3. 材质编辑预览
```csharp
public class MaterialPreview : Viewport3DPreview
{
    public void PreviewMaterial(string materialPath, string targetSlot = "Body")
    {
        var material = FlaxEngine.Content.LoadAsync<Material>(materialPath);
        if (material == null) return;
        
        // 应用材质到指定槽位
        ApplyMaterialToModel(material, targetSlot);
    }
    
    private void ApplyMaterialToModel(Material material, string slot)
    {
        if (_animatedModel != null)
        {
            // 应用到动画模型
            _animatedModel.SetMaterial(slot, material);
        }
        else if (_staticModel != null)
        {
            // 应用到静态模型
            _staticModel.SetMaterial(0, material);
        }
    }
}
```

## API参考

### Viewport3DPreview 公共方法

| 方法 | 参数 | 说明 |
|------|------|------|
| `InitializeViewport()` | 无 | 初始化渲染视口 |
| `LoadFromPrefab(Prefab)` | 预制体对象 | 从预制体加载角色 |
| `LoadStaticModel(string)` | 模型路径 | 加载静态模型 |
| `LoadAnimatedModel(string)` | 模型路径 | 加载带动画的模型 |
| `PlayAnimation(string, bool)` | 动画名称, 是否循环 | 播放指定动画 |
| `StopAnimation()` | 无 | 停止当前动画 |
| `SetAutoRotation(bool)` | 是否启用 | 设置自动旋转开关 |
| `SetManualRotation(bool)` | 是否启用 | 设置手动旋转开关 |
| `SetModelRotation(float)` | 旋转角度 | 直接设置模型旋转 |
| `GetModelRotation()` | 无 | 获取当前旋转角度 |
| `GetAnimator()` | 无 | 获取动画控制器实例 |

### 事件回调

```csharp
// 可通过继承添加自定义事件
public class CustomPreview : Viewport3DPreview
{
    public Action OnModelLoaded;
    public Action<string> OnAnimationStarted;
    public Action<Float2> OnModelRotated;
    
    protected override void OnModelLoadedInternal()
    {
        OnModelLoaded?.Invoke();
        base.OnModelLoadedInternal();
    }
    
    public override void PlayAnimation(string animationName, bool loop = true)
    {
        OnAnimationStarted?.Invoke(animationName);
        base.PlayAnimation(animationName, loop);
    }
}
```

## 最佳实践

### 1. 资源管理
- 使用资源池管理预览组件实例
- 及时释放不再使用的模型和纹理资源
- 预加载常用模型以提升用户体验

### 2. 用户体验
- 提供加载进度指示器
- 添加预览质量设置选项
- 支持键盘快捷键操作

### 3. 性能优化
- 根据设备性能动态调整预览分辨率
- 实现视锥剔除减少不必要的渲染
- 使用LOD技术优化复杂模型显示

### 4. 错误处理
- 提供友好的错误提示信息
- 实现降级方案（如模型加载失败时显示占位图）
- 记录详细的错误日志便于调试

本手册提供了Viewport3DPreview组件的完整使用指南，涵盖了从基础使用到高级扩展的各个方面。建议开发者根据具体项目需求选择合适的功能进行实现。