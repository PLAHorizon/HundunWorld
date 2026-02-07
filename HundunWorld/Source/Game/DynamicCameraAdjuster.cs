using FlaxEngine;
using Game;

namespace HundunWorld.Game
{
    /// <summary>
    /// 动态相机调整器，负责处理智能避障、动态FOV调整等高级功能
    /// </summary>
    public class DynamicCameraAdjuster : Script
    {
        #region 动态FOV参数

        /// <summary>
        /// 基础视场角
        /// </summary>
        [Tooltip("基础视场角")]
        public float BaseFOV { get; set; } = 60.0f;

        /// <summary>
        /// 最小视场角
        /// </summary>
        [Tooltip("最小视场角")]
        public float MinFOV { get; set; } = 45.0f;

        /// <summary>
        /// 最大视场角
        /// </summary>
        [Tooltip("最大视场角")]
        public float MaxFOV { get; set; } = 90.0f;

        /// <summary>
        /// FOV调整速度
        /// </summary>
        [Tooltip("FOV调整速度")]
        public float FOVAdjustSpeed { get; set; } = 30.0f;

        /// <summary>
        /// 当前目标FOV
        /// </summary>
        private float _targetFOV = 60.0f;

        /// <summary>
        /// 当前实际FOV
        /// </summary>
        private float _currentFOV = 60.0f;

        #endregion

        #region 智能避障参数

        /// <summary>
        /// 避障检测半径
        /// </summary>
        [Tooltip("避障检测半径")]
        public float ObstacleDetectionRadius { get; set; } = 1.0f;

        /// <summary>
        /// 避障响应速度
        /// </summary>
        [Tooltip("避障响应速度")]
        public float ObstacleResponseSpeed { get; set; } = 5.0f;

        /// <summary>
        /// 避障恢复速度
        /// </summary>
        [Tooltip("避障恢复速度")]
        public float ObstacleRecoverySpeed { get; set; } = 2.0f;

        /// <summary>
        /// 检测层掩码
        /// </summary>
        [Tooltip("检测层掩码")]
        public uint DetectionLayerMask { get; set; } = uint.MaxValue;

        /// <summary>
        /// 上次检测到障碍物的时间
        /// </summary>
        private float _lastObstacleTime = 0f;

        #endregion

        #region 相机引用

        /// <summary>
        /// 相机组件引用
        /// </summary>
        private Camera _camera;

        /// <summary>
        /// 第三人称相机脚本引用
        /// </summary>
        private ThirdPersonCamera _thirdPersonCamera;

        #endregion

        #region 生命周期方法

        public override void OnStart()
        {
            // 获取相机组件
            _camera = Actor.As<Camera>();
            _thirdPersonCamera = Actor.GetScript<ThirdPersonCamera>();

            // 初始化FOV
            _targetFOV = BaseFOV;
            _currentFOV = BaseFOV;

            if (_camera != null)
            {
                _camera.FieldOfView = _currentFOV;
            }
        }

        public override void OnUpdate()
        {
            // 更新动态FOV
            UpdateDynamicFOV();

            // 更新智能避障
            UpdateSmartObstacleAvoidance();
        }

        #endregion

        #region 动态FOV系统

        /// <summary>
        /// 更新动态FOV
        /// </summary>
        private void UpdateDynamicFOV()
        {
            if (_camera == null || _thirdPersonCamera == null)
                return;

            // 根据相机距离调整FOV
            float cameraDistance = _thirdPersonCamera.GetCurrentDistance();
            float normalizedDistance = Mathf.InverseLerp(_thirdPersonCamera.MinDistance, _thirdPersonCamera.MaxDistance, cameraDistance);

            // 距离越远，FOV越大，提供更广阔的视野
            float distanceBasedFOV = Mathf.Lerp(MinFOV, MaxFOV, normalizedDistance);

            // 检查角色移动状态，如果在冲刺则增加FOV
            if (IsCharacterSprinting())
            {
                distanceBasedFOV += 10.0f; // 冲刺时增加10度FOV
            }

            // 限制FOV范围
            _targetFOV = Mathf.Clamp(distanceBasedFOV, MinFOV, MaxFOV);

            // 平滑过渡到目标FOV
            _currentFOV = Mathf.Lerp(_currentFOV, _targetFOV, FOVAdjustSpeed * Time.DeltaTime);
            _camera.FieldOfView = _currentFOV;
        }

        /// <summary>
        /// 检查角色是否在冲刺
        /// </summary>
        /// <returns>是否在冲刺</returns>
        private bool IsCharacterSprinting()
        {
            // 尝试从场景中找到PlayerController
            var playerController = Actor.GetScript<PlayerController>();
            if (playerController != null)
            {
                return playerController.IsSprinting();
            }

            return false;
        }

        /// <summary>
        /// 设置目标FOV
        /// </summary>
        /// <param name="targetFOV">目标FOV</param>
        public void SetTargetFOV(float targetFOV)
        {
            _targetFOV = Mathf.Clamp(targetFOV, MinFOV, MaxFOV);
        }

        /// <summary>
        /// 立即设置FOV
        /// </summary>
        /// <param name="fov">视场角</param>
        public void SetFOVImmediate(float fov)
        {
            _targetFOV = Mathf.Clamp(fov, MinFOV, MaxFOV);
            _currentFOV = _targetFOV;
            if (_camera != null)
            {
                _camera.FieldOfView = _currentFOV;
            }
        }

        #endregion

        #region 智能避障系统

        /// <summary>
        /// 更新智能避障
        /// </summary>
        private void UpdateSmartObstacleAvoidance()
        {
            if (_thirdPersonCamera == null)
                return;

            // 检测相机周围的障碍物
            bool hasObstacle = DetectObstacles();

            if (hasObstacle)
            {
                _lastObstacleTime = Time.GameTime;
                // 临时调整FOV来提供更好的视野
                SetTargetFOV(Mathf.Min(_targetFOV + 15.0f, MaxFOV));
            }
            else
            {
                // 如果没有障碍物一段时间，恢复正常FOV
                if (Time.GameTime - _lastObstacleTime > 2.0f)
                {
                    SetTargetFOV(BaseFOV);
                }
            }
        }

        /// <summary>
        /// 检测障碍物
        /// </summary>
        /// <returns>是否检测到障碍物</returns>
        private bool DetectObstacles()
        {
            // 在相机周围进行球形检测
            Vector3 cameraPosition = Actor.Position;
            
            // 执行球形碰撞检测
            if (Physics.CheckSphere(cameraPosition, ObstacleDetectionRadius, DetectionLayerMask))
            {
                return true;
            }

            // 检测相机前方是否有障碍物
            Vector3 cameraForward = Actor.Transform.Forward;
            Ray forwardRay = new Ray(cameraPosition, cameraForward);
            
            if (Physics.RayCast(forwardRay.Position, forwardRay.Direction, out var hitInfo, ObstacleDetectionRadius * 2.0f, DetectionLayerMask))
            {
                return true;
            }

            return false;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取当前FOV
        /// </summary>
        /// <returns>当前FOV</returns>
        public float GetCurrentFOV()
        {
            return _currentFOV;
        }

        /// <summary>
        /// 获取目标FOV
        /// </summary>
        /// <returns>目标FOV</returns>
        public float GetTargetFOV()
        {
            return _targetFOV;
        }

        /// <summary>
        /// 重置FOV到基础值
        /// </summary>
        public void ResetFOV()
        {
            SetTargetFOV(BaseFOV);
        }

        #endregion
    }
}