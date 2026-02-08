using System;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// UI场景类型枚举
    /// </summary>
    public enum SceneType
    {
        /// <summary>
        /// 
        /// </summary>
        Start,
        /// <summary>
        /// 登录场景
        /// </summary>
        Login,
        /// <summary>
        /// 
        /// </summary>
        Register,
        /// <summary>
        /// 角色选择场景
        /// </summary>
        CharacterSelection,
        /// <summary>
        /// 
        /// </summary>
        CharacterCreation,
        /// <summary>
        /// 游戏世界场景
        /// </summary>
        GameWorld,

        /// <summary>
        /// 设置场景
        /// </summary>
        Settings,

        /// <summary>
        /// 商城场景
        /// </summary>
        Shop,

        /// <summary>
        /// 背包场景
        /// </summary>
        Inventory
    }

    /// <summary>
    /// UI错误类型枚举
    /// </summary>
    public enum UIErrorType
    {
        /// <summary>
        /// 系统错误
        /// </summary>
        System,
        /// <summary>
        /// 网络错误
        /// </summary>
        Network,
        /// <summary>
        /// 
        /// </summary>
        Authentication,
        /// <summary>
        /// 数据错误
        /// </summary>
        Data,

        /// <summary>
        /// 逻辑错误
        /// </summary>
        Logic,

        /// <summary>
        /// 渲染错误
        /// </summary>
        Rendering,

        /// <summary>
        /// 输入错误
        /// </summary>
        Input,

        /// <summary>
        /// 权限错误
        /// </summary>
        Permission,
        /// <summary>
        /// 
        /// </summary>
        Validation,
        /// <summary>
        /// 未知错误
        /// </summary>
        Unknown,
        Component,
        Transition,
        /// <summary>
        /// 一般错误
        /// </summary>
        General,
        /// <summary>
        /// 角色相关错误
        /// </summary>
        Character,
        /// <summary>
        /// 战斗相关错误
        /// </summary>
        Combat,
        /// <summary>
        /// UI相关错误
        /// </summary>
        UI,
        /// <summary>
        /// 资源相关错误
        /// </summary>
        Resource,
    }

    /// <summary>
    /// 错误处理策略枚举
    /// </summary>
    public enum ErrorHandlingStrategy
    {
        /// <summary>
        /// 忽略错误
        /// </summary>
        Ignore,

        /// <summary>
        /// 显示警告
        /// </summary>
        ShowWarning,

        /// <summary>
        /// 显示错误
        /// </summary>
        ShowError,

        /// <summary>
        /// 自动重试
        /// </summary>
        AutoRetry,

        /// <summary>
        /// 回退到安全状态
        /// </summary>
        Fallback,

        /// <summary>
        /// 终止操作
        /// </summary>
        Terminate,
        ShowMessage,
        Retry,
        Rollback,
        Restart,
        Escalate
    }

    /// <summary>
    /// 动画类型枚举
    /// </summary>
    public enum AnimationType
    {
        /// <summary>
        /// 淡入淡出
        /// </summary>
        FadeOut,
        /// <summary>
        /// 
        /// </summary>
        FadeIn,

        /// <summary>
        /// 滑动
        /// </summary>
        SlideOut,
        /// <summary>
        /// 
        /// </summary>
        SlideIn,

        /// <summary>
        /// 缩放
        /// </summary>
        ScaleOut,
        /// <summary>
        /// 
        /// </summary>
        ScaleIn,
        /// <summary>
        /// 
        /// </summary>
        Shake,
        /// <summary>
        /// 旋转
        /// </summary>
        Rotate,

        /// <summary>
        /// 弹跳
        /// </summary>
        Bounce,

        /// <summary>
        /// 闪烁
        /// </summary>
        Flash,

        /// <summary>
        /// 脉冲
        /// </summary>
        Pulse,
        /// <summary>
        /// 
        /// </summary>
        EaseOutBack,

        /// <summary>
        /// 无动画
        /// </summary>
        None
    }

    /// <summary>
    /// 缓动函数类型枚举
    /// </summary>
    public enum EasingType
    {
        /// <summary>
        /// 线性
        /// </summary>
        Linear,

        /// <summary>
        /// 缓入
        /// </summary>
        EaseIn,

        /// <summary>
        /// 缓出
        /// </summary>
        EaseOut,

        /// <summary>
        /// 缓入缓出
        /// </summary>
        EaseInOut,
        /// <summary>
        /// 
        /// </summary>
        EaseOutBack,
        /// <summary>
        /// 弹性
        /// </summary>
        Elastic,

        /// <summary>
        /// 弹跳
        /// </summary>
        Bounce
    }

    /// <summary>
    /// 按钮类型枚举
    /// </summary>
    public enum ButtonType
    {
        /// <summary>
        /// 主要按钮
        /// </summary>
        Primary,

        /// <summary>
        /// 次要按钮
        /// </summary>
        Secondary,

        /// <summary>
        /// 小按钮
        /// </summary>
        Small,

        /// <summary>
        /// 大按钮
        /// </summary>
        Large
    }

    /// <summary>
    /// 间距类型枚举
    /// </summary>
    public enum SpacingType
    {
        /// <summary>
        /// 小间距
        /// </summary>
        Small,

        /// <summary>
        /// 中间距
        /// </summary>
        Medium,

        /// <summary>
        /// 大间距
        /// </summary>
        Large,

        /// <summary>
        /// 超大间距
        /// </summary>
        ExtraLarge,

        /// <summary>
        /// 巨大间距
        /// </summary>
        Big
    }

    /// <summary>
    /// 视觉层次枚举
    /// </summary>
    public enum VisualHierarchy
    {
        /// <summary>
        /// 主要操作
        /// </summary>
        Primary,

        /// <summary>
        /// 次要操作
        /// </summary>
        Secondary,

        /// <summary>
        /// 输入区域
        /// </summary>
        Tertiary,

        /// <summary>
        /// 辅助信息
        /// </summary>
        Auxiliary
    }

    /// <summary>
    /// 中式边框样式枚举
    /// </summary>
    public enum ChineseBorderStyle
    {
        /// <summary>
        /// 优雅
        /// </summary>
        Elegant,

        /// <summary>
        /// 传统
        /// </summary>
        Traditional,

        /// <summary>
        /// 华丽
        /// </summary>
        Ornate
    }

    /// <summary>
    /// 切换类型枚举
    /// </summary>
    public enum TransitionType
    {
        /// <summary>
        /// 立即切换
        /// </summary>
        Instant,

        /// <summary>
        /// 淡入淡出
        /// </summary>
        Fade,

        /// <summary>
        /// 滑动
        /// </summary>
        Slide,

        /// <summary>
        /// 缩放
        /// </summary>
        Zoom,

        /// <summary>
        /// 翻转
        /// </summary>
        Flip
    }

    /// <summary>
    /// 切换优先级枚举
    /// </summary>
    public enum SwitchPriority
    {
        /// <summary>
        /// 低优先级
        /// </summary>
        Low = 0,

        /// <summary>
        /// 普通优先级
        /// </summary>
        Normal = 1,

        /// <summary>
        /// 高优先级
        /// </summary>
        High = 2,

        /// <summary>
        /// 关键优先级
        /// </summary>
        Critical = 3
    }

    /// <summary>
    /// 场景加载策略枚举
    /// </summary>
    public enum SceneLoadStrategy
    {
        /// <summary>
        /// 立即加载
        /// </summary>
        Immediate,

        /// <summary>
        /// 懒加载
        /// </summary>
        Lazy,

        /// <summary>
        /// 预加载
        /// </summary>
        Preload,

        /// <summary>
        /// 按需加载
        /// </summary>
        OnDemand
    }

    /// <summary>
    /// 状态快照类型枚举
    /// </summary>
    public enum SnapshotType
    {
        /// <summary>
        /// 自动快照
        /// </summary>
        Automatic,

        /// <summary>
        /// 手动快照
        /// </summary>
        Manual,

        /// <summary>
        /// 错误恢复快照
        /// </summary>
        ErrorRecovery,

        /// <summary>
        /// 场景切换快照
        /// </summary>
        SceneTransition,

        /// <summary>
        /// 关键操作快照
        /// </summary>
        CriticalOperation
    }

    /// <summary>
    /// 警告类型枚举
    /// </summary>
    public enum WarningType
    {
        /// <summary>
        /// 网络警告
        /// </summary>
        Network,

        /// <summary>
        /// 性能警告
        /// </summary>
        Performance,

        /// <summary>
        /// 内存警告
        /// </summary>
        Memory,

        /// <summary>
        /// 配置警告
        /// </summary>
        Configuration,

        /// <summary>
        /// 兼容性警告
        /// </summary>
        Compatibility,
        /// <summary>
        /// 
        /// </summary>
        LowFrameRate,
        /// <summary>
        /// 
        /// </summary>
        HighMemoryUsage,
        /// <summary>
        /// 
        /// </summary>
        SlowSwitch
    }

    /// <summary>
    /// 提示类型枚举
    /// </summary>
    public enum ToastType
    {
        /// <summary>
        /// 信息提示
        /// </summary>
        Info,

        /// <summary>
        /// 成功提示
        /// </summary>
        Success,

        /// <summary>
        /// 警告提示
        /// </summary>
        Warning,

        /// <summary>
        /// 错误提示
        /// </summary>
        Error
    }

    /// <summary>
    /// 角色状态枚举
    /// </summary>
    public enum CharacterState
    {
        /// <summary>
        /// 空闲
        /// </summary>
        Idle,

        /// <summary>
        /// 移动
        /// </summary>
        Moving,

        /// <summary>
        /// 攻击
        /// </summary>
        Attacking,

        /// <summary>
        /// 受伤
        /// </summary>
        Hurt,

        /// <summary>
        /// 死亡
        /// </summary>
        Dead,
        
        /// <summary>
        /// 跳跃
        /// </summary>
        Jumping,
        
        /// <summary>
        /// 下落
        /// </summary>
        Falling,
        
        /// <summary>
        /// 跑步
        /// </summary>
        Running,
        
        /// <summary>
        /// 蹲伏
        /// </summary>
        Crouching,
        
        /// <summary>
        /// 行走
        /// </summary>
        Walking,
        
        /// <summary>
        /// 滑行
        /// </summary>
        Sliding
    }

    /// <summary>
    /// 游戏状态枚举
    /// </summary>
    public enum GameState
    {
        /// <summary>
        /// 未初始化
        /// </summary>
        Uninitialized,

        /// <summary>
        /// 加载中
        /// </summary>
        Loading,

        /// <summary>
        /// 主菜单
        /// </summary>
        MainMenu,

        /// <summary>
        /// 游戏中
        /// </summary>
        InGame,

        /// <summary>
        /// 暂停
        /// </summary>
        Paused,

        /// <summary>
        /// 游戏结束
        /// </summary>
        GameOver
    }

    /// <summary>
    /// 分辨率类型枚举
    /// </summary>
    public enum ResolutionType
    {
        /// <summary>
        /// 低分辨率
        /// </summary>
        Low,

        /// <summary>
        /// 中等分辨率
        /// </summary>
        Medium,

        /// <summary>
        /// 高分辨率
        /// </summary>
        High,

        /// <summary>
        /// 超高清
        /// </summary>
        UltraHD,

        /// <summary>
        /// 标准分辨率
        /// </summary>
        Standard,

        /// <summary>
        /// 超高分辨率
        /// </summary>
        UltraHigh
    }

    /// <summary>
    /// 过渡阶段枚举
    /// </summary>
    public enum TransitionPhase
    {
        /// <summary>
        /// 开始
        /// </summary>
        Start,

        /// <summary>
        /// 进行中
        /// </summary>
        InProgress,

        /// <summary>
        /// 完成
        /// </summary>
        Complete,

        /// <summary>
        /// 取消
        /// </summary>
        Cancelled,
        Preparing,
        Validating,
        ExitAnimation,
        SceneSwitch,
        DataLoading,
        EnterAnimation,
        Completed,
        Failed,
        /// <summary>
        /// 无过渡
        /// </summary>
        None,
        /// <summary>
        /// 淡出中
        /// </summary>
        FadingOut,
        /// <summary>
        /// 加载中
        /// </summary>
        Loading,
        /// <summary>
        /// 淡入中
        /// </summary>
        FadingIn
    }

    /// <summary>
    /// 场景生命周期状态枚举
    /// </summary>
    public enum SceneLifecycleState
    {
        /// <summary>
        /// 未加载
        /// </summary>
        Unloaded,

        /// <summary>
        /// 加载中
        /// </summary>
        Loading,

        /// <summary>
        /// 已加载
        /// </summary>
        Loaded,

        /// <summary>
        /// 激活
        /// </summary>
        Active,
        /// <summary>
        /// 
        /// </summary>
        Ready,
        /// <summary>
        /// 
        /// </summary>
        Error,
        /// <summary>
        /// 隐藏
        /// </summary>
        Hidden,
        /// <summary>
        /// 
        /// </summary>
        Uninitialized,
        /// <summary>
        /// 卸载中
        /// </summary>
        Unloading
    }

    /// <summary>
    /// 相机模式枚举
    /// </summary>
    public enum CameraMode
    {
        /// <summary>
        /// 第一人称
        /// </summary>
        FirstPerson,

        /// <summary>
        /// 第三人称
        /// </summary>
        ThirdPerson,

        /// <summary>
        /// 自由视角
        /// </summary>
        FreeLook,

        /// <summary>
        /// 固定视角
        /// </summary>
        Fixed,

        /// <summary>
        /// 跟随视角
        /// </summary>
        Follow
    }

    /// <summary>
    /// 测试状态枚举
    /// </summary>
    public enum TestStatus
    {
        /// <summary>
        /// 未开始
        /// </summary>
        NotStarted,

        /// <summary>
        /// 进行中
        /// </summary>
        InProgress,

        /// <summary>
        /// 通过
        /// </summary>
        Passed,

        /// <summary>
        /// 失败
        /// </summary>
        Failed,

        /// <summary>
        /// 跳过
        /// </summary>
        Skipped
    }

    /// <summary>
    /// 世界事件类型枚举
    /// </summary>
    public enum WorldEventType
    {
        /// <summary>
        /// 天气变化
        /// </summary>
        WeatherChange,

        /// <summary>
        /// 时间变化
        /// </summary>
        TimeChange,

        /// <summary>
        /// 季节变化
        /// </summary>
        SeasonChange,

        /// <summary>
        /// 特殊事件
        /// </summary>
        SpecialEvent,

        /// <summary>
        /// 节日事件
        /// </summary>
        FestivalEvent,

        /// <summary>
        /// 实体添加
        /// </summary>
        EntityAdded,

        /// <summary>
        /// 实体移除
        /// </summary>
        EntityRemoved,

        /// <summary>
        /// 实体更新
        /// </summary>
        EntityUpdated
    }
}