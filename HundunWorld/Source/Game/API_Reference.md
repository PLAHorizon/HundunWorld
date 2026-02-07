# HundunWorld 相机控制系统 API 参考

## PlayerController API

### 公共属性

#### 基础移动参数
```csharp
public float MoveSpeed { get; set; }                    // 基础移动速度 (默认: 5.0f)
public float RunSpeedMultiplier { get; set; }           // 跑步速度倍数 (默认: 2.0f)
public float CrouchSpeedMultiplier { get; set; }        // 蹲伏速度倍数 (默认: 0.5f)
public float JumpForce { get; set; }                    // 跳跃力度 (默认: 10.0f)
public float Gravity { get; set; }                      // 重力加速度 (默认: -9.81f)
```

#### 地形适应参数
```csharp
public float StepHeight { get; set; }                   // 可攀爬台阶高度 (默认: 0.3f)
public float MaxSlopeAngle { get; set; }                // 最大坡度角度 (默认: 45.0f)
public float Acceleration { get; set; }                 // 移动加速度 (默认: 20.0f)
public float Deceleration { get; set; }                 // 移动减速度 (默认: 25.0f)
public float AirControl { get; set; }                   // 空中移动控制力 (默认: 0.3f)
```

#### 控制参数
```csharp
public float RotationSmoothing { get; set; }            // 转向平滑度 (默认: 0.1f)
public float InputBufferTime { get; set; }              // 输入缓冲时间 (默认: 0.1f)
public ThirdPersonCamera Camera { get; set; }           // 关联的相机实例
```

#### 只读属性
```csharp
public CharacterState CurrentState { get; private set; } // 当前角色状态
```

### 公共方法

#### 状态查询
```csharp
/// <summary>
/// 获取当前角色状态
/// </summary>
/// <returns>当前角色状态</returns>
public CharacterState GetCharacterState()

/// <summary>
/// 获取角色位置
/// </summary>
/// <returns>角色位置</returns>
public Vector3 GetPosition()
```

#### 位置控制
```csharp
/// <summary>
/// 设置角色位置
/// </summary>
/// <param name="position">目标位置</param>
public void SetPosition(Vector3 position)
```

#### 网络同步
```csharp
/// <summary>
/// 应用服务器位置校正
/// </summary>
/// <param name="serverPosition">服务器权威位置</param>
public void ApplyServerCorrection(Vector3 serverPosition)
```

### 枚举类型

#### CharacterState
```csharp
public enum CharacterState
{
    Idle,        // 空闲状态
    Walking,     // 行走状态
    Running,     // 跑步状态
    Jumping,     // 跳跃状态
    Falling,     // 下落状态
    Crouching,   // 蹲伏状态
    Climbing     // 攀爬状态
}
```

---

## ThirdPersonCamera API

### 公共属性

#### 基础相机参数
```csharp
public float Distance { get; set; }                     // 相机距离 (默认: 10.0f)
public float MinDistance { get; set; }                  // 最小距离 (默认: 2.0f)
public float MaxDistance { get; set; }                  // 最大距离 (默认: 20.0f)
public float Pitch { get; set; }                        // 俯仰角度 (默认: 30.0f)
public float Yaw { get; set; }                          // 偏航角度 (默认: 45.0f)
public float MinPitch { get; set; }                     // 最小俯仰角 (默认: -45.0f)
public float MaxPitch { get; set; }                     // 最大俯仰角 (默认: 45.0f)
public Vector3 Offset { get; set; }                     // 相机偏移 (默认: (0,2,0))
public Actor Target { get; set; }                       // 跟随目标
```

#### 控制参数
```csharp
public float RotationSpeed { get; set; }                // 旋转速度 (默认: 2.0f)
public float MinInputValue { get; set; }                // 最小输入值 (默认: 0.01f)
public float CameraSmoothing { get; set; }              // 移动平滑度 (默认: 0.1f)
public float RotationSmoothing { get; set; }            // 旋转平滑度 (默认: 0.1f)
```

#### 聚焦参数
```csharp
public float FocusRadius { get; set; }                  // 聚焦死区半径 (默认: 1.0f)
public float FocusCentering { get; set; }               // 聚焦居中速度 (默认: 0.5f)
```

#### 自动对齐参数
```csharp
public float AlignDelay { get; set; }                   // 自动对齐延迟 (默认: 5.0f)
public float AlignSmoothRange { get; set; }             // 对齐平滑范围 (默认: 45.0f)
public float AlignRotationSpeed { get; set; }           // 对齐旋转速度 (默认: 90.0f)
public float MinMovementThreshold { get; set; }         // 最小移动阈值 (默认: 0.01f)
public bool EnableSmartAlignment { get; set; }          // 启用智能对齐 (默认: true)
```

#### 碰撞检测参数
```csharp
public float CameraCollisionOffset { get; set; }        // 碰撞偏移量 (默认: 0.2f)
public bool EnableCameraCollision { get; set; }         // 启用碰撞检测 (默认: true)
public bool EnableSmoothFollow { get; set; }            // 启用平滑跟随 (默认: true)
public float DistanceAdjustmentSpeed { get; set; }      // 距离调整速度 (默认: 5.0f)
public float ObstructionRecoverySpeed { get; set; }     // 遮挡恢复速度 (默认: 2.0f)
public uint ObstructionLayerMask { get; set; }          // 遮挡检测层 (默认: uint.MaxValue)
public int CollisionRayCount { get; set; }              // 碰撞检测射线数 (默认: 5)
public float CollisionSphereRadius { get; set; }        // 碰撞检测球半径 (默认: 0.3f)
```

#### 模式控制
```csharp
public CameraMode CurrentMode { get; private set; }     // 当前相机模式
public Vector3 FirstPersonOffset { get; set; }          // 第一人称偏移 (默认: (0,1.8f,0))
```

### 公共方法

#### 模式控制
```csharp
/// <summary>
/// 切换相机模式
/// </summary>
public void SwitchCameraMode()

/// <summary>
/// 设置相机模式
/// </summary>
/// <param name="mode">相机模式</param>
public void SetCameraMode(CameraMode mode)
```

#### 距离控制
```csharp
/// <summary>
/// 设置理想相机距离
/// </summary>
/// <param name="distance">目标距离</param>
public void SetIdealDistance(float distance)

/// <summary>
/// 获取当前相机距离
/// </summary>
/// <returns>当前距离</returns>
public float GetCurrentDistance()
```

#### 相机抖动
```csharp
/// <summary>
/// 触发相机抖动
/// </summary>
/// <param name="intensity">抖动强度</param>
/// <param name="duration">持续时间</param>
public void TriggerShake(float intensity, float duration)
```

#### 射线检测
```csharp
/// <summary>
/// 执行地面射线检测
/// </summary>
/// <param name="screenPosition">屏幕位置</param>
/// <param name="hitPoint">击中点</param>
/// <returns>是否击中地面</returns>
public bool PerformGroundRaycast(Float2 screenPosition, out Vector3 hitPoint)
```

### 枚举类型

#### CameraMode
```csharp
public enum CameraMode
{
    ThirdPerson,  // 第三人称模式
    FirstPerson   // 第一人称模式
}
```

---

## InputManager API

### 公共属性

#### 手势检测参数
```csharp
public float DoubleClickWindow { get; set; }            // 双击检测时间窗口 (默认: 0.3f)
public float LongPressThreshold { get; set; }           // 长按检测阈值 (默认: 1.0f)
public float GestureMinDistance { get; set; }           // 手势最小距离 (默认: 50.0f)
```

#### 配置参数
```csharp
public string ConfigFilePath { get; set; }              // 配置文件路径
public bool EnableInputPrediction { get; set; }         // 启用输入预测 (默认: true)
```

### 公共方法

#### 基础输入检测
```csharp
/// <summary>
/// 检查动作是否处于激活状态
/// </summary>
/// <param name="actionName">动作名称</param>
/// <returns>是否激活</returns>
public bool IsActionPressed(string actionName)

/// <summary>
/// 检查动作是否在当前帧按下
/// </summary>
/// <param name="actionName">动作名称</param>
/// <returns>是否按下</returns>
public bool IsActionDown(string actionName)

/// <summary>
/// 检查动作是否在当前帧抬起
/// </summary>
/// <param name="actionName">动作名称</param>
/// <returns>是否抬起</returns>
public bool IsActionUp(string actionName)
```

#### 高级输入检测
```csharp
/// <summary>
/// 检查双击动作
/// </summary>
/// <param name="actionName">动作名称</param>
/// <returns>是否双击</returns>
public bool IsActionDoubleClick(string actionName)

/// <summary>
/// 检查长按动作
/// </summary>
/// <param name="actionName">动作名称</param>
/// <returns>是否长按</returns>
public bool IsActionLongPress(string actionName)

/// <summary>
/// 检查组合键动作
/// </summary>
/// <param name="comboName">组合键名称</param>
/// <returns>是否触发组合键</returns>
public bool IsComboTriggered(string comboName)
```

#### 输入强度和方向
```csharp
/// <summary>
/// 获取输入强度
/// </summary>
/// <param name="actionName">动作名称</param>
/// <returns>输入强度 (0-1)</returns>
public float GetActionStrength(string actionName)

/// <summary>
/// 获取手势方向向量
/// </summary>
/// <param name="actionName">动作名称</param>
/// <returns>方向向量</returns>
public Vector2 GetGestureDirection(string actionName)
```

#### 绑定管理
```csharp
/// <summary>
/// 添加自定义输入绑定
/// </summary>
/// <param name="actionName">动作名称</param>
/// <param name="keys">键盘按键列表</param>
/// <param name="mouseButtons">鼠标按键列表</param>
public void AddInputBinding(string actionName, List<KeyboardKeys> keys = null, List<MouseButton> mouseButtons = null)

/// <summary>
/// 移除输入绑定
/// </summary>
/// <param name="actionName">动作名称</param>
public void RemoveInputBinding(string actionName)
```

### 数据结构

#### InputBinding
```csharp
public class InputBinding
{
    public string ActionName;                            // 动作名称
    public List<KeyboardKeys> Keys;                      // 键盘按键列表
    public List<MouseButton> MouseButtons;               // 鼠标按键列表
    public float DeadZone;                               // 死区值
    public bool Enabled;                                 // 是否启用
}
```

#### ComboBinding
```csharp
public class ComboBinding
{
    public string ActionName;                            // 组合键名称
    public List<string> RequiredActions;                 // 需要的动作列表
    public float TimeWindow;                             // 时间窗口
    public bool RequireSimultaneous;                     // 是否需要同时按下
}
```

---

## PerformanceMonitor API

### 公共属性

#### 监控配置
```csharp
public int SampleWindowSize { get; set; }               // 采样窗口大小 (默认: 60)
public float UpdateFrequency { get; set; }              // 更新频率 (默认: 1.0f)
public bool ShowPerformanceStats { get; set; }          // 显示性能统计 (默认: true)
```

#### 性能阈值
```csharp
public float FrameRateThreshold { get; set; }           // 帧率阈值 (默认: 60.0f)
public float InputLatencyThreshold { get; set; }        // 输入延迟阈值 (默认: 50.0f)
public float MemoryThreshold { get; set; }              // 内存阈值 (默认: 150.0f)
```

### 公共方法

#### 性能监控
```csharp
/// <summary>
/// 记录射线检测调用
/// </summary>
public void RecordRaycast()

/// <summary>
/// 获取当前性能指标
/// </summary>
/// <returns>性能指标</returns>
public PerformanceMetrics GetCurrentMetrics()

/// <summary>
/// 获取平均帧率
/// </summary>
/// <returns>平均帧率</returns>
public float GetAverageFrameRate()

/// <summary>
/// 获取平均输入延迟
/// </summary>
/// <returns>平均输入延迟（毫秒）</returns>
public float GetAverageInputLatency()

/// <summary>
/// 重置性能统计
/// </summary>
public void ResetStatistics()
```

### 数据结构

#### PerformanceMetrics
```csharp
public struct PerformanceMetrics
{
    public float FrameTime;                              // 帧时间 (毫秒)
    public float InputLatency;                           // 输入延迟 (毫秒)
    public int RaycastCount;                             // 射线检测次数
    public float MemoryUsage;                            // 内存使用量 (MB)
    public float CPUUsage;                               // CPU使用率
}
```

---

## DynamicCameraAdjuster API

### 公共属性

#### 功能开关
```csharp
public bool EnableDynamicDistance { get; set; }         // 启用动态距离调整 (默认: true)
public bool EnableDynamicFOV { get; set; }              // 启用动态视野调整 (默认: true)
public bool EnablePerformanceAdaptive { get; set; }     // 启用性能自适应 (默认: true)
```

#### 调整参数
```csharp
public float DistanceSensitivity { get; set; }          // 距离调整敏感度 (默认: 1.0f)
public float FOVSensitivity { get; set; }               // 视野调整敏感度 (默认: 0.5f)
public float OpenAreaDistanceMultiplier { get; set; }   // 开阔环境距离倍数 (默认: 1.5f)
public float NarrowAreaDistanceMultiplier { get; set; } // 狭窄环境距离倍数 (默认: 0.7f)
public float HighSpeedFOVAdjustment { get; set; }       // 高速移动视野调整 (默认: 10.0f)
public float HighSpeedThreshold { get; set; }           // 高速移动阈值 (默认: 8.0f)
```

#### 环境检测参数
```csharp
public int EnvironmentRayCount { get; set; }            // 环境检测射线数 (默认: 8)
public float EnvironmentDetectionDistance { get; set; } // 环境检测距离 (默认: 20.0f)
public float OpenAreaThreshold { get; set; }            // 开阔环境阈值 (默认: 15.0f)
public float NarrowAreaThreshold { get; set; }          // 狭窄环境阈值 (默认: 5.0f)
```

### 公共方法

#### 控制方法
```csharp
/// <summary>
/// 重置到原始参数
/// </summary>
public void ResetToOriginal()

/// <summary>
/// 获取当前环境开阔度
/// </summary>
/// <returns>环境开阔度 (0-1)</returns>
public float GetEnvironmentOpenness()

/// <summary>
/// 设置调整敏感度
/// </summary>
/// <param name="distanceSensitivity">距离敏感度</param>
/// <param name="fovSensitivity">视野敏感度</param>
public void SetSensitivity(float distanceSensitivity, float fovSensitivity)
```

---

## TestSceneGenerator API

### 公共属性

#### 场景配置
```csharp
public bool AutoGenerateTestScene { get; set; }         // 自动生成测试场景 (默认: true)
public TestSceneType CurrentSceneType { get; set; }     // 当前场景类型
public Vector3 SceneSize { get; set; }                  // 场景尺寸 (默认: (50,10,50))
public bool GeneratePerformanceObjects { get; set; }    // 生成性能测试对象 (默认: false)
public int PerformanceObjectCount { get; set; }         // 性能测试对象数量 (默认: 100)
```

### 公共方法

#### 场景生成
```csharp
/// <summary>
/// 生成测试场景
/// </summary>
public void GenerateTestScene()

/// <summary>
/// 切换测试场景类型
/// </summary>
/// <param name="sceneType">场景类型</param>
public void SwitchTestScene(TestSceneType sceneType)

/// <summary>
/// 获取当前测试场景信息
/// </summary>
/// <returns>场景信息字符串</returns>
public string GetSceneInfo()
```

### 枚举类型

#### TestSceneType
```csharp
public enum TestSceneType
{
    OpenArea,        // 开阔区域
    NarrowCorridor,  // 狭窄走廊
    MultiLevel,      // 多层环境
    ObstaclesCourse, // 障碍课程
    PerformanceTest  // 性能测试
}
```

---

## CameraControllerTestSuite API

### 公共属性

#### 测试配置
```csharp
public bool AutoRunTests { get; set; }                  // 自动运行测试 (默认: false)
public float TestDuration { get; set; }                 // 测试持续时间 (默认: 10.0f)
public bool ShowTestResults { get; set; }               // 显示测试结果 (默认: true)
```

### 公共方法

#### 测试控制
```csharp
/// <summary>
/// 开始测试套件
/// </summary>
public void StartTestSuite()

/// <summary>
/// 获取测试结果
/// </summary>
/// <returns>测试结果列表</returns>
public List<TestResult> GetTestResults()

/// <summary>
/// 获取测试统计
/// </summary>
/// <returns>测试统计信息</returns>
public string GetTestStatistics()
```

### 数据结构

#### TestResult
```csharp
public struct TestResult
{
    public string TestName;                              // 测试名称
    public bool Passed;                                  // 是否通过
    public string Message;                               // 测试消息
    public float ExecutionTime;                          // 执行时间
    public Dictionary<string, float> Metrics;            // 测试指标
}
```

#### TestStatus
```csharp
public enum TestStatus
{
    NotStarted,      // 未开始
    Running,         // 运行中
    Completed,       // 已完成
    Failed           // 失败
}
```

---

## 使用示例

### 基础设置示例

```csharp
// 创建并配置角色控制器
var playerController = characterActor.AddScript<PlayerController>();
playerController.MoveSpeed = 6.0f;
playerController.RunSpeedMultiplier = 2.5f;
playerController.JumpForce = 12.0f;

// 创建并配置相机
var cameraController = cameraActor.AddScript<ThirdPersonCamera>();
cameraController.Target = characterActor;
cameraController.Distance = 8.0f;
cameraController.EnableSmartAlignment = true;
cameraController.AlignDelay = 3.0f;

// 关联相机到角色控制器
playerController.Camera = cameraController;
```

### 性能监控示例

```csharp
// 添加性能监控
var performanceMonitor = rootActor.AddScript<PerformanceMonitor>();
performanceMonitor.ShowPerformanceStats = true;
performanceMonitor.FrameRateThreshold = 60.0f;

// 定期检查性能
if (performanceMonitor.GetAverageFrameRate() < 30.0f)
{
    // 降低画质或调整设置
}
```

### 自定义输入示例

```csharp
// 添加自定义输入绑定
var customKeys = new List<KeyboardKeys> { KeyboardKeys.E };
inputManager.AddInputBinding("Interact", customKeys);

// 检查自定义输入
if (inputManager.IsActionDown("Interact"))
{
    // 执行交互逻辑
}

// 检查组合键
if (inputManager.IsComboTriggered("CtrlRun"))
{
    // 执行特殊跑步
}
```

### 测试示例

```csharp
// 生成测试场景
var sceneGenerator = Actor.AddScript<TestSceneGenerator>();
sceneGenerator.SwitchTestScene(TestSceneGenerator.TestSceneType.ObstaclesCourse);

// 运行自动化测试
var testSuite = Actor.AddScript<CameraControllerTestSuite>();
testSuite.AutoRunTests = true;
testSuite.TestDuration = 5.0f;
```

---

*API 参考版本: v1.0.0*  
*最后更新: 2025年*