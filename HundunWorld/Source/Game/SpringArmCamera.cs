using FlaxEngine;
using System;

namespace HundunWorld.Game
{
    /// <summary>
    /// 弹簧臂相机系统 - 基于弹簧臂原理的第三人称相机
    /// </summary>
    public class SpringArmCamera : Script
    {
        #region 公共配置参数

        [Header("目标设置")]
        [Tooltip("相机跟随的目标Actor")]
        public Actor Target;

        [Tooltip("相机聚焦点相对于目标的位置偏移")]
        public Vector3 FocusOffset = new Vector3(0, 1.8f, 0);

        [Header("弹簧臂设置")]
        [Range(1.0f, 30.0f)]
        [Tooltip("弹簧臂的目标长度")]
        public float TargetArmLength = 10.0f;

        [Range(0.1f, 10.0f)]
        [Tooltip("弹簧的弹性系数（越大响应越快）")]
        public float SpringStiffness = 5.0f;

        [Range(0.1f, 5.0f)]
        [Tooltip("弹簧的阻尼系数（越大减速越快）")]
        public float SpringDamping = 2.0f;

        [Tooltip("启用弹簧臂碰撞检测")]
        public bool EnableCollisionDetection = true;

        [Tooltip("碰撞检测半径")]
        public float CollisionRadius = 0.5f;

        [Tooltip("碰撞检测层级掩码")]
        public LayersMask CollisionLayerMask = LayersMask.Default;

        [Tooltip("碰撞时的最小弹簧臂长度")]
        public float MinArmLength = 1.0f;

        [Header("相机设置")]
        [Tooltip("相机的初始俯仰角")]
        public float InitialPitch = 30.0f;

        [Tooltip("相机的初始偏航角")]
        public float InitialYaw = 0.0f;

        [Range(-85.0f, 85.0f)]
        [Tooltip("相机俯仰角的最小值")]
        public float MinPitch = -85.0f;

        [Range(-85.0f, 85.0f)]
        [Tooltip("相机俯仰角的最大值")]
        public float MaxPitch = 85.0f;

        [Header("控制设置")]
        [Tooltip("旋转灵敏度")]
        public float RotationSpeed = 0.1f;

        [Tooltip("鼠标中键滚动调整弹簧臂长度的灵敏度")]
        public float ZoomSpeed = 2.0f;

        [Tooltip("启用鼠标右键旋转相机")]
        public bool EnableMouseRotation = true;

        [Tooltip("启用鼠标中键缩放")]
        public bool EnableMouseZoom = true;

        [Tooltip("旋转相机的鼠标按键")]
        public MouseButton RotationMouseButton = MouseButton.Right;

        [Header("平滑设置")]
        [Tooltip("旋转平滑系数")]
        public float RotationSmoothness = 0.1f;

        [Tooltip("位置平滑系数")]
        public float PositionSmoothness = 0.1f;

        #endregion

        #region 私有字段

        private Camera _camera;
        private float _currentArmLength;
        private float _targetArmLength;
        private float _armVelocity;
        private float _currentPitch;
        private float _currentYaw;
        private float _targetPitch;
        private float _targetYaw;
        private Vector3 _lastFocusPoint;
        private Vector3 _smoothedFocusPoint;
        private bool _isInitialized;

        #endregion

        #region 生命周期方法

        public override void OnStart()
        {
            // 获取Camera组件
            _camera = Actor as Camera;
            if (_camera == null)
            {
                _camera = Actor.GetChild<Camera>();
                if (_camera == null)
                {
                    Debug.LogError("[SpringArmCamera] Camera component not found!");
                    Enabled = false;
                    return;
                }
            }

            // 初始化变量
            _currentArmLength = TargetArmLength;
            _targetArmLength = TargetArmLength;
            _armVelocity = 0.0f;
            _currentPitch = InitialPitch;
            _targetPitch = InitialPitch;
            _currentYaw = InitialYaw;
            _targetYaw = InitialYaw;
            _lastFocusPoint = Target != null ? Target.Position + FocusOffset : Vector3.Zero;
            _smoothedFocusPoint = _lastFocusPoint;
            _isInitialized = true;

            Debug.Log("[SpringArmCamera] Initialized successfully!");
        }

        public override void OnUpdate()
        {
            if (!_isInitialized || Target == null || _camera == null)
                return;

            // 处理输入
            HandleInput();

            // 更新相机
            UpdateCamera();
        }

        #endregion

        #region 输入处理

        private void HandleInput()
        {
            // 鼠标旋转
            if (EnableMouseRotation && Input.GetMouseButton(RotationMouseButton))
            {
                Float2 mouseDelta = Input.MousePositionDelta;
                _targetYaw += mouseDelta.X * RotationSpeed;
                _targetPitch -= mouseDelta.Y * RotationSpeed;
                _targetPitch = Mathf.Clamp(_targetPitch, MinPitch, MaxPitch);
            }

            // 鼠标缩放
            if (EnableMouseZoom)
            {
                float mouseWheelDelta = Input.MouseScrollDelta;
                if (mouseWheelDelta != 0)
                {
                    _targetArmLength -= mouseWheelDelta * ZoomSpeed * 0.1f;
                    _targetArmLength = Mathf.Clamp(_targetArmLength, MinArmLength, 30.0f);
                }
            }
        }

        #endregion

        #region 相机更新

        private void UpdateCamera()
        {
            // 计算聚焦点
            Vector3 targetFocusPoint = Target.Position + FocusOffset;
            _smoothedFocusPoint = Vector3.Lerp(_smoothedFocusPoint, targetFocusPoint, PositionSmoothness / Time.DeltaTime);

            // 平滑旋转
            _currentYaw = Mathf.Lerp(_currentYaw, _targetYaw, RotationSmoothness / Time.DeltaTime);
            _currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, RotationSmoothness / Time.DeltaTime);

            // 计算相机方向
            Quaternion rotation = Quaternion.Euler(_currentPitch, _currentYaw, 0);
            Vector3 cameraDirection = Vector3.Transform(Vector3.Backward, rotation);

            // 计算理想相机位置
            Vector3 idealCameraPosition = _smoothedFocusPoint + cameraDirection * _targetArmLength;

            // 执行碰撞检测
            if (EnableCollisionDetection)
            {
                float collisionDistance = CalculateCollisionDistance(_smoothedFocusPoint, cameraDirection);
                _targetArmLength = Mathf.Min(_targetArmLength, collisionDistance);
            }

            // 应用弹簧臂物理
            UpdateSpringArm();

            // 计算最终相机位置
            Vector3 finalCameraPosition = _smoothedFocusPoint + cameraDirection * _currentArmLength;

            // 更新相机位置和旋转
            _camera.Position = finalCameraPosition;
            _camera.Orientation = Quaternion.LookAt(finalCameraPosition, _smoothedFocusPoint);
        }

        private void UpdateSpringArm()
        {
            // 弹簧臂物理模拟
            float springForce = (TargetArmLength - _currentArmLength) * SpringStiffness;
            float dampingForce = -_armVelocity * SpringDamping;
            float totalForce = springForce + dampingForce;

            // 更新速度和位置
            _armVelocity += totalForce * Time.DeltaTime;
            _currentArmLength += _armVelocity * Time.DeltaTime;

            // 确保长度在有效范围内
            _currentArmLength = Mathf.Clamp(_currentArmLength, MinArmLength, 30.0f);
        }

        private float CalculateCollisionDistance(Vector3 origin, Vector3 direction)
        {
            // 从聚焦点向相机方向发射射线
            RayCastHit hit;
            float maxDistance = _targetArmLength + 2.0f; // 稍微超出目标长度

            if (Physics.RayCast(origin, direction, out hit, maxDistance, CollisionLayerMask))
            {
                // 计算碰撞点到原点的距离，并减去碰撞半径
                float distanceToHit = Vector3.Distance(origin, hit.Point);
                return Mathf.Max(distanceToHit - CollisionRadius, MinArmLength);
            }

            // 没有碰撞，使用目标长度
            return _targetArmLength;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置相机的目标俯仰角
        /// </summary>
        /// <param name="pitch">俯仰角（度）</param>
        public void SetPitch(float pitch)
        {
            _targetPitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
        }

        /// <summary>
        /// 设置相机的目标偏航角
        /// </summary>
        /// <param name="yaw">偏航角（度）</param>
        public void SetYaw(float yaw)
        {
            _targetYaw = yaw;
        }

        /// <summary>
        /// 设置弹簧臂的目标长度
        /// </summary>
        /// <param name="length">长度</param>
        public void SetArmLength(float length)
        {
            _targetArmLength = Mathf.Clamp(length, MinArmLength, 30.0f);
        }

        /// <summary>
        /// 重置相机到初始状态
        /// </summary>
        public void ResetCamera()
        {
            _targetPitch = InitialPitch;
            _targetYaw = InitialYaw;
            _targetArmLength = TargetArmLength;
        }

        /// <summary>
        /// 立即更新相机（用于外部调用）
        /// </summary>
        public void UpdateCameraImmediately()
        {
            if (_isInitialized && Target != null && _camera != null)
            {
                UpdateCamera();
            }
        }

        #endregion

        #region 属性

        /// <summary>
        /// 获取或设置当前的弹簧臂长度
        /// </summary>
        public float CurrentArmLength
        {
            get => _currentArmLength;
            set => _targetArmLength = Mathf.Clamp(value, MinArmLength, 30.0f);
        }

        /// <summary>
        /// 获取或设置目标俯仰角
        /// </summary>
        public float CurrentPitch
        {
            get => _currentPitch;
            set => SetPitch(value);
        }

        /// <summary>
        /// 获取或设置目标偏航角
        /// </summary>
        public float CurrentYaw
        {
            get => _currentYaw;
            set => SetYaw(value);
        }

        #endregion
    }
}
