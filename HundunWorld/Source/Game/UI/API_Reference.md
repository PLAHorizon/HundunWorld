# HundunWorld UI系统 API参考

## 核心管理器

### UIStateManager

全局UI状态管理器，负责管理场景状态、用户会话和角色数据。

#### 属性
- `static UIStateManager Instance` - 单例实例
- `SceneType CurrentScene` - 当前场景类型
- `bool IsLoading` - 是否正在加载
- `string ErrorMessage` - 当前错误消息
- `UserSession UserSession` - 用户会话信息
- `CharacterInfo SelectedCharacter` - 选中的角色
- `List<CharacterInfo> CharacterList` - 角色列表

#### 方法
- `bool TransitionToScene(SceneType newScene, bool checkConditions = true)` - 场景转换
- `void SetLoadingState(bool isLoading)` - 设置加载状态
- `void SetError(string errorMessage)` - 设置错误信息
- `void UpdateUserSession(string username, ulong userId, string accessToken, string refreshToken = "")` - 更新用户会话
- `void ClearUserSession()` - 清除用户会话
- `void AddCharacter(CharacterInfo character)` - 添加角色
- `void RemoveCharacter(ulong characterId)` - 移除角色
- `void SetSelectedCharacter(CharacterInfo character)` - 设置选中角色

#### 事件
- `event Action<SceneType, SceneType> SceneChanged` - 场景切换事件
- `event Action<bool> LoadingStateChanged` - 加载状态变化事件
- `event Action<string> ErrorOccurred` - 错误发生事件
- `event Action<UserSession> UserSessionChanged` - 用户会话变化事件
- `event Action<CharacterInfo> SelectedCharacterChanged` - 选中角色变化事件
- `event Action<List<CharacterInfo>> CharacterListUpdated` - 角色列表更新事件

### UIAnimationManager

UI动画管理器，提供各种UI动画效果。

#### 属性
- `static UIAnimationManager Instance` - 单例实例

#### 方法
- `void FadeIn(Control control, float duration = 0.3f, EasingType easing = EasingType.EaseOut, Action onComplete = null)` - 淡入动画
- `void FadeOut(Control control, float duration = 0.3f, EasingType easing = EasingType.EaseOut, Action onComplete = null)` - 淡出动画
- `void SlideIn(Control control, float duration = 0.5f, EasingType easing = EasingType.EaseOut, Action onComplete = null)` - 滑入动画
- `void SlideOut(Control control, float duration = 0.5f, EasingType easing = EasingType.EaseIn, Action onComplete = null)` - 滑出动画
- `void ScaleIn(Control control, float duration = 0.3f, EasingType easing = EasingType.Bounce, Action onComplete = null)` - 缩放进入动画
- `void Shake(Control control, float duration = 0.5f, Action onComplete = null)` - 震动动画
- `void Bounce(Control control, float duration = 0.6f, Action onComplete = null)` - 弹跳动画
- `void StopAnimations(Control control)` - 停止指定控件的所有动画
- `void StopAllAnimations()` - 停止所有动画

### ErrorHandlingManager

错误处理管理器，提供统一的错误处理机制。

#### 属性
- `static ErrorHandlingManager Instance` - 单例实例

#### 方法
- `void HandleError(ErrorInfo errorInfo)` - 处理错误
- `void HandleError(string message, ErrorType type = ErrorType.Unknown, ErrorSeverity severity = ErrorSeverity.Error, string source = "")` - 快速处理错误
- `void HandleNetworkError(string message, string details = "")` - 处理网络错误
- `void HandleAuthenticationError(string message, string code = "")` - 处理认证错误
- `void HandleValidationError(string message, string source = "")` - 处理验证错误
- `void HandleServerError(string message, string code = "", string details = "")` - 处理服务器错误
- `void HandleCriticalError(string message, string details = "")` - 处理严重错误
- `List<ErrorInfo> GetErrorHistory()` - 获取错误历史记录
- `void ClearErrorHistory()` - 清除错误历史记录
- `ErrorInfo GetLastError()` - 获取最近的错误

#### 事件
- `event Action<ErrorInfo> ErrorOccurred` - 错误发生事件
- `event Action<ErrorInfo> CriticalErrorOccurred` - 严重错误发生事件

## UI组件

### ValidatedTextBox

带验证功能的文本输入框。

#### 属性
- `string Text` - 文本内容
- `string WatermarkText` - 水印文本
- `bool IsPassword` - 是否为密码框
- `bool IsValid` - 验证是否通过

#### 方法
- `void SetValidator(Func<string, (bool isValid, string errorMessage)> validator)` - 设置验证器
- `static ValidatedTextBox CreateUsernameInput()` - 创建用户名输入框
- `static ValidatedTextBox CreatePasswordInput()` - 创建密码输入框
- `static ValidatedTextBox CreateEmailInput()` - 创建邮箱输入框

#### 事件
- `event Action<string> TextChanged` - 文本变化事件
- `event Action<bool> ValidationChanged` - 验证状态变化事件

### LoadingIndicator

加载指示器组件。

#### 属性
- `string Message` - 加载消息
- `float Progress` - 进度值 (0-1)
- `bool ShowProgress` - 是否显示进度条

#### 方法
- `void Show(string message = "正在加载...", bool showProgress = false)` - 显示加载指示器
- `void Hide()` - 隐藏加载指示器
- `void UpdateProgress(float progress, string message = null)` - 更新进度

### ToastNotification

消息提示组件。

#### 方法
- `void Show(string message, ToastType type = ToastType.Info, float duration = 3f)` - 显示Toast消息
- `void Hide()` - 隐藏Toast消息

### ToastManager

Toast管理器，管理多个Toast消息的显示。

#### 方法
- `void ShowToast(string message, ToastType type = ToastType.Info, float duration = 3f)` - 显示Toast
- `void ShowInfo(string message, float duration = 3f)` - 显示信息消息
- `void ShowSuccess(string message, float duration = 3f)` - 显示成功消息
- `void ShowWarning(string message, float duration = 3f)` - 显示警告消息
- `void ShowError(string message, float duration = 3f)` - 显示错误消息

### ConfirmDialog

确认对话框组件。

#### 属性
- `string Title` - 标题
- `string Message` - 消息内容
- `string ConfirmButtonText` - 确认按钮文本
- `string CancelButtonText` - 取消按钮文本

#### 方法
- `void Show(string title, string message, string confirmText = "确认", string cancelText = "取消")` - 显示对话框
- `void Hide()` - 隐藏对话框
- `static ConfirmDialog CreateDeleteDialog(string itemName, Action onConfirm)` - 创建删除确认对话框
- `static ConfirmDialog CreateLogoutDialog(Action onConfirm)` - 创建登出确认对话框

#### 事件
- `event Action Confirmed` - 确认事件
- `event Action Cancelled` - 取消事件

## 网络通信

### UINetworkAdapter

UI网络适配器，处理UI与后端服务的通信。

#### 属性
- `static UINetworkAdapter Instance` - 单例实例

#### 认证相关方法
- `async Task<bool> SendLoginRequestAsync(string accountName, string password, string deviceId = "")` - 发送登录请求
- `async Task<bool> SendRegisterRequestAsync(string nickname, string password, string email = "", string phoneNumber = "", string verificationCode = "")` - 发送注册请求
- `void HandleLoginResponse(LoginResponse response)` - 处理登录响应
- `void HandleRegisterResponse(RegisterResponse response)` - 处理注册响应

#### 角色管理相关方法
- `async Task<bool> SendGetCharacterListRequestAsync()` - 发送获取角色列表请求
- `async Task<bool> SendCreateCharacterRequestAsync(string characterName, Profession profession, byte gender, AppearanceInfo appearance)` - 发送创建角色请求
- `async Task<bool> SendDeleteCharacterRequestAsync(ulong characterId)` - 发送删除角色请求
- `async Task<bool> SendEnterGameRequestAsync(ulong characterId)` - 发送进入游戏请求

#### 公共方法
- `async Task LogoutAsync()` - 登出
- `bool IsNetworkConnected()` - 检查网络连接状态

#### 事件
- `event Action<LoginResponse> LoginResponseReceived` - 登录响应接收事件
- `event Action<RegisterResponse> RegisterResponseReceived` - 注册响应接收事件
- `event Action<GetCharacterListResponse> CharacterListReceived` - 角色列表接收事件
- `event Action<CreateCharacterResponse> CharacterCreated` - 角色创建事件
- `event Action<DeleteCharacterResponse> CharacterDeleted` - 角色删除事件
- `event Action<EnterGameResponse> GameEntered` - 进入游戏事件

## 工具类

### UIHelper

UI辅助工具类，提供常用的UI创建和样式设置功能。

#### 颜色常量
- `static readonly Color PrimaryColor` - 主要颜色
- `static readonly Color SecondaryColor` - 次要颜色
- `static readonly Color DangerColor` - 危险颜色
- `static readonly Color InfoColor` - 信息颜色
- `static readonly Color BackgroundColor` - 背景颜色
- `static readonly Color PanelColor` - 面板颜色
- `static readonly Color InputColor` - 输入框颜色

#### 属性
- `static ToastManager ToastManager` - Toast管理器实例

#### 方法
- `static FontReference SetFont(string fontPath = "Engine/Fonts/Roboto-Regular", float size = 12)` - 设置字体
- `static Button CreateButton(string text, Color? backgroundColor = null, Color? textColor = null)` - 创建按钮
- `static Button CreatePrimaryButton(string text)` - 创建主要按钮
- `static Button CreateSecondaryButton(string text)` - 创建次要按钮
- `static Button CreateDangerButton(string text)` - 创建危险按钮
- `static Panel CreatePanel(Float2 size, Color? backgroundColor = null)` - 创建面板
- `static TextBox CreateTextBox(string watermark = "", bool isPassword = false)` - 创建文本框
- `static Label CreateTitleLabel(string text, float fontSize = 16)` - 创建标题标签
- `static Label CreateLabel(string text, Color? textColor = null)` - 创建标签
- `static LoadingIndicator CreateLoadingIndicator()` - 创建加载指示器
- `static ConfirmDialog CreateConfirmDialog(string title, string message, Action onConfirm = null)` - 创建确认对话框
- `static void ShowSuccess(string message, float duration = 3f)` - 显示成功消息
- `static void ShowError(string message, float duration = 5f)` - 显示错误消息
- `static void ShowWarning(string message, float duration = 4f)` - 显示警告消息
- `static void ShowInfo(string message, float duration = 3f)` - 显示信息消息
- `static void ApplyStandardStyle(Control control)` - 应用标准样式
- `static void ApplyStandardStyles(params Control[] controls)` - 批量应用标准样式

## 性能优化

### UIObjectPool<T>

UI对象池，用于复用频繁创建和销毁的UI组件。

#### 方法
- `T Get()` - 从池中获取对象
- `void Return(T item)` - 归还对象到池中
- `void Clear()` - 清空对象池

#### 属性
- `int PoolSize` - 对象池大小

### UIPerformanceMonitor

UI性能监控器，监控UI系统的性能指标。

#### 属性
- `static UIPerformanceMonitor Instance` - 单例实例

#### 方法
- `void RecordDrawCall(bool batched = false)` - 记录绘制调用
- `PerformanceReport GetPerformanceReport()` - 获取性能报告
- `void ResetCounters()` - 重置统计计数器

### UIResourceManager

UI资源管理器，管理UI纹理、字体等资源的加载和卸载。

#### 属性
- `static UIResourceManager Instance` - 单例实例

#### 方法
- `FontAsset LoadFont(string path)` - 加载字体资源
- `Texture LoadTexture(string path)` - 加载纹理资源
- `void PreloadResources(string[] fontPaths, string[] texturePaths)` - 预加载资源
- `void CleanupUnusedResources()` - 清理未使用的资源
- `(int fontCount, int textureCount) GetCacheStats()` - 获取缓存统计信息

## 枚举类型

### SceneType
UI场景类型枚举
- `GameStart` - 游戏启动
- `LoginScreen` - 登录界面
- `RegisterScreen` - 注册界面
- `CharacterSelection` - 角色选择
- `CharacterCreation` - 角色创建
- `GameWorld` - 游戏世界

### AnimationType
动画类型枚举
- `FadeIn` - 淡入
- `FadeOut` - 淡出
- `SlideIn` - 滑入
- `SlideOut` - 滑出
- `ScaleIn` - 缩放进入
- `ScaleOut` - 缩放退出
- `Shake` - 震动
- `Bounce` - 弹跳

### EasingType
缓动函数类型枚举
- `Linear` - 线性
- `EaseIn` - 缓入
- `EaseOut` - 缓出
- `EaseInOut` - 缓入缓出
- `Bounce` - 弹跳
- `Elastic` - 弹性

### ToastType
Toast消息类型枚举
- `Info` - 信息
- `Success` - 成功
- `Warning` - 警告
- `Error` - 错误

### ErrorType
错误类型枚举
- `Network` - 网络错误
- `Validation` - 验证错误
- `Authentication` - 认证错误
- `ServerError` - 服务器错误
- `Unknown` - 未知错误

### ErrorSeverity
错误严重级别枚举
- `Info` - 信息
- `Warning` - 警告
- `Error` - 错误
- `Critical` - 严重错误

## 测试框架

### UITestFramework

UI测试框架，提供UI组件和系统的单元测试功能。

#### 方法
- `async void RunAllTests()` - 运行所有测试用例

### UIIntegrationTests

UI集成测试，测试UI组件之间的交互。

#### 方法
- `async void RunIntegrationTests()` - 运行集成测试

## 数据结构

### UserSession
用户会话信息
- `ulong UserId` - 用户ID
- `string Username` - 用户名
- `string AccessToken` - 访问令牌
- `string RefreshToken` - 刷新令牌
- `bool IsAuthenticated` - 是否已认证

### ErrorInfo
错误信息
- `ErrorType Type` - 错误类型
- `ErrorSeverity Severity` - 严重级别
- `string Code` - 错误代码
- `string Message` - 错误消息
- `string Details` - 错误详情
- `DateTime Timestamp` - 时间戳
- `string Source` - 错误源

### PerformanceReport
性能报告
- `float FPS` - 帧率
- `int TotalUIElements` - 总UI元素数
- `int VisibleUIElements` - 可见UI元素数
- `long MemoryUsage` - 内存使用量
- `int DrawCalls` - 绘制调用次数
- `int BatchedDrawCalls` - 批处理绘制调用次数
- `float BatchingEfficiency` - 批处理效率

---

*此文档涵盖了HundunWorld UI系统的主要API。更多详细信息请参考源代码注释。*