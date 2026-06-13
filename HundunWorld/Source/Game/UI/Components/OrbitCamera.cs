using FlaxEngine;
using System;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 轨道相机控制器 - 基于球坐标的相机数学
    /// 用于围绕目标点（角色）的第三人称 / 预览相机控制
    /// 
    /// 球坐标数学原理：
    /// 球坐标系使用三个参数描述三维空间中的点：
    /// - 方位角 (Azimuth)：水平旋转角度，绕 Y 轴旋转，范围 0°~360°（可环绕）
    /// - 仰角 (Elevation)：垂直旋转角度，绕 X 轴旋转，范围 -15°~+25°
    /// - 距离 (Distance)：相机到目标点的径向距离，范围 1.5m~4.5m（150cm~450cm）
    /// 
    /// 球坐标到笛卡尔坐标的转换公式：
    /// x = distance * cos(elevation) * cos(azimuth)
    /// y = distance * sin(elevation)
    /// z = distance * cos(elevation) * sin(azimuth)
    /// 
    /// 相机位置 = 目标点 + 偏移向量
    /// 相机朝向 = 看向目标点
    /// </summary>
    public class OrbitCamera
    {
        #region 限制常量

        /// <summary>仰角最小值（度）- 限制相机不能过低</summary>
        public const float MinElevation = -15f;

        /// <summary>仰角最大值（度）- 限制相机不能过高</summary>
        public const float MaxElevation = 25f;

        /// <summary>距离最小值 (cm) - 1.5米，防止相机穿入模型</summary>
        public const float MinDistance = 150f;

        /// <summary>距离最大值 (cm) - 4.5米，限制最远观察距离</summary>
        public const float MaxDistance = 450f;

        /// <summary>平移灵敏度（屏幕像素 -> 世界单位的比例系数）</summary>
        public float PanSpeed = 0.01f;

        /// <summary>Idle 计时器阈值（秒）- 5秒无输入后启动自动旋转</summary>
        public const float IdleThreshold = 5f;

        /// <summary>自动旋转速度（度/秒）- 30秒一圈 = 360°/30s = 12°/s</summary>
        public const float AutoRotateSpeed = 12f;

        /// <summary>双击时间间隔阈值（秒）</summary>
        public const float DoubleClickThreshold = 0.3f;

        #endregion

        #region 球坐标字段

        /// <summary>方位角（水平旋转），单位度，范围 0°~360°</summary>
        public float Azimuth { get; set; } = 0f;

        /// <summary>仰角（垂直旋转），单位度，范围 -15°~+25°</summary>
        public float Elevation { get; set; } = 0f;

        /// <summary>距离（相机到目标点的距离, cm），范围 150cm~450cm</summary>
        public float Distance { get; set; } = 250f;

        /// <summary>目标点（角色位置）</summary>
        public Vector3 Target { get; set; } = Vector3.Zero;

        #endregion

        #region 初始状态（用于复位）

        private float _initialAzimuth = 0f;
        private float _initialElevation = 0f;
        private float _initialDistance = 250f;
        private Vector3 _initialTarget = Vector3.Zero;

        #endregion

        #region Idle 自动旋转

        private float _idleTimer = 0f;
        private bool _isIdle = false;

        /// <summary>是否处于 Idle 自动旋转状态</summary>
        public bool IsIdle => _isIdle;

        #endregion

        #region 右键双击复位

        private float _lastRightClickTime = -1f;
        private bool _isResetting = false;
        private float _resetProgress = 0f;
        private float _resetDuration = 0.5f;
        private float _resetStartAzimuth;
        private float _resetStartElevation;
        private float _resetStartDistance;
        private Vector3 _resetStartTarget;

        /// <summary>是否正在执行复位动画</summary>
        public bool IsResetting => _isResetting;

        #endregion

        #region 构造函数

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public OrbitCamera()
        {
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置初始状态（用于复位功能）
        /// </summary>
        public void SetInitialState(float azimuth, float elevation, float distance, Vector3 target)
        {
            _initialAzimuth = azimuth;
            _initialElevation = elevation;
            _initialDistance = distance;
            _initialTarget = target;
        }

        /// <summary>
        /// 增加方位角和仰角
        /// </summary>
        /// <param name="deltaAzimuth">方位角增量（度）</param>
        /// <param name="deltaElevation">仰角增量（度）</param>
        public void Rotate(float deltaAzimuth, float deltaElevation)
        {
            Azimuth += deltaAzimuth;
            Elevation += deltaElevation;
            ClampLimits();
            ResetIdleTimer();
        }

        /// <summary>
        /// 增加距离（缩放）
        /// </summary>
        /// <param name="deltaDistance">距离增量（cm）</param>
        public void Zoom(float deltaDistance)
        {
            Distance += deltaDistance;
            ClampLimits();
            ResetIdleTimer();
        }

        /// <summary>
        /// 平移目标点（根据屏幕坐标）
        /// 将屏幕坐标增量按相机当前右方向和上方向投影到世界空间，移动目标点
        /// </summary>
        /// <param name="deltaScreenPos">屏幕坐标增量（像素）</param>
        public void Pan(Float2 deltaScreenPos)
        {
            // 计算相机视线方向（从相机指向目标），与 ApplyToCamera 中的 offset 互为反向
            float azimuthRad = Azimuth * Mathf.DegreesToRadians;
            float elevationRad = Elevation * Mathf.DegreesToRadians;

            float cosE = Mathf.Cos(elevationRad);
            float sinE = Mathf.Sin(elevationRad);
            float cosA = Mathf.Cos(azimuthRad);
            float sinA = Mathf.Sin(azimuthRad);

            // 视线方向（单位向量）：从相机指向 Target
            Vector3 forward = new Vector3(-cosE * cosA, -sinE, -cosE * sinA);
            forward.Normalize();

            // 相机右方向 = forward × World.Up
            Vector3 right = Vector3.Cross(forward, Vector3.Up);
            if (right.LengthSquared < 1e-6f)
            {
                // 视线接近垂直，使用 Azimuth 推算一个水平方向
                right = new Vector3(-sinA, 0f, cosA);
            }
            else
            {
                right.Normalize();
            }

            // 相机上方向 = right × forward
            Vector3 up = Vector3.Cross(right, forward);
            up.Normalize();

            // 屏幕 X 增大 -> 目标沿 -right 移动（鼠标拖世界跟随）
            // 屏幕 Y 增大 -> 目标沿 -up 移动
            // PanSpeed 与 Distance 关联，距离越远平移幅度越大，更符合直觉
            Vector3 panDelta = (-right * deltaScreenPos.X - up * deltaScreenPos.Y) * (PanSpeed * Distance);
            Target += panDelta;
            ResetIdleTimer();
        }

        /// <summary>
        /// 将球坐标转换为相机位置和朝向，并应用到指定相机
        /// offset = (Distance * cos(Elevation) * cos(Azimuth), Distance * sin(Elevation), Distance * cos(Elevation) * sin(Azimuth))
        /// camera.Position = Target + offset
        /// 然后让相机看向 Target
        /// </summary>
        /// <param name="camera">目标相机</param>
        public void ApplyToCamera(Camera camera)
        {
            if (camera == null)
                return;

            float azimuthRad = Azimuth * Mathf.DegreesToRadians;
            float elevationRad = Elevation * Mathf.DegreesToRadians;

            float cosE = Mathf.Cos(elevationRad);
            float sinE = Mathf.Sin(elevationRad);
            float cosA = Mathf.Cos(azimuthRad);
            float sinA = Mathf.Sin(azimuthRad);

            // 球坐标到笛卡尔坐标的转换
            Vector3 offset = new Vector3(
                Distance * cosE * cosA,
                Distance * sinE,
                Distance * cosE * sinA);

            camera.Position = Target + offset;
            camera.LookAt(Target, Vector3.Up);
        }

        /// <summary>
        /// 处理右键点击（用于双击检测）
        /// </summary>
        public void OnRightClick()
        {
            float currentTime = Time.GameTime;
            if (_lastRightClickTime >= 0 && (currentTime - _lastRightClickTime) < DoubleClickThreshold)
            {
                // 检测到双击，开始复位
                StartReset();
                _lastRightClickTime = -1f;
            }
            else
            {
                _lastRightClickTime = currentTime;
            }
        }

        /// <summary>
        /// 重置 Idle 计时器（有输入时调用）
        /// </summary>
        public void ResetIdleTimer()
        {
            _idleTimer = 0f;
            _isIdle = false;
        }

        /// <summary>
        /// 每帧更新 - 处理 Idle 自动旋转和复位动画
        /// 需要由调用方（如 CharacterPreviewPanel）在 Update 中调用
        /// </summary>
        /// <param name="deltaTime">帧间隔时间（秒）</param>
        /// <param name="canAutoRotate">是否允许自动旋转（由调用方根据拖拽状态等判断）</param>
        public void Update(float deltaTime, bool canAutoRotate = true)
        {
            // 处理复位动画
            if (_isResetting)
            {
                UpdateResetAnimation(deltaTime);
                return;
            }

            // 处理 Idle 自动旋转
            _idleTimer += deltaTime;
            if (_idleTimer >= IdleThreshold && canAutoRotate)
            {
                _isIdle = true;
                // 自动旋转：绕 Y 轴（方位角）旋转
                Azimuth += AutoRotateSpeed * deltaTime;
                ClampLimits();
            }
            else
            {
                _isIdle = false;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 限制字段值到合法范围
        /// </summary>
        private void ClampLimits()
        {
            // Azimuth 自由旋转，归一化到 0°~360°
            Azimuth = Azimuth % 360f;
            if (Azimuth < 0f)
                Azimuth += 360f;

            // Elevation 限制在 -15°~+25°
            Elevation = Mathf.Clamp(Elevation, MinElevation, MaxElevation);

            // Distance 限制在 150cm~450cm (1.5m~4.5m)
            Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
        }

        /// <summary>
        /// 开始复位动画
        /// </summary>
        private void StartReset()
        {
            _isResetting = true;
            _resetProgress = 0f;
            _resetStartAzimuth = Azimuth;
            _resetStartElevation = Elevation;
            _resetStartDistance = Distance;
            _resetStartTarget = Target;
        }

        /// <summary>
        /// 更新复位动画
        /// </summary>
        private void UpdateResetAnimation(float deltaTime)
        {
            _resetProgress += deltaTime / _resetDuration;

            if (_resetProgress >= 1f)
            {
                // 复位完成
                Azimuth = _initialAzimuth;
                Elevation = _initialElevation;
                Distance = _initialDistance;
                Target = _initialTarget;
                _isResetting = false;
            }
            else
            {
                // 使用平滑插值（EaseOutCubic）
                float t = 1f - Mathf.Pow(1f - _resetProgress, 3f);

                // 方位角插值需要考虑环绕（取最短路径）
                float deltaAzimuth = _initialAzimuth - _resetStartAzimuth;
                if (deltaAzimuth > 180f) deltaAzimuth -= 360f;
                if (deltaAzimuth < -180f) deltaAzimuth += 360f;
                Azimuth = _resetStartAzimuth + deltaAzimuth * t;

                Elevation = Mathf.Lerp(_resetStartElevation, _initialElevation, t);
                Distance = Mathf.Lerp(_resetStartDistance, _initialDistance, t);
                Target = Vector3.Lerp(_resetStartTarget, _initialTarget, t);
            }
        }

        #endregion
    }
}
