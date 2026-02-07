using FlaxEngine;
using Game;
using System;

namespace HundunWorld.Game.ClimbingSystem
{
    /// <summary>
    /// 攀爬控制器，处理角色的攀爬逻辑
    /// </summary>
    public class ClimbingController : Script
    {
        #region 攀爬参数
        
        /// <summary>
        /// 攀爬速度
        /// </summary>
        [Tooltip("攀爬速度")]
        public float ClimbSpeed { get; set; } = 2.0f;
        
        /// <summary>
        /// 攀爬到顶部的速度
        /// </summary>
        [Tooltip("攀爬到顶部的速度")]
        public float MantleSpeed { get; set; } = 1.5f;
        
        /// <summary>
        /// 抓取边缘的速度
        /// </summary>
        [Tooltip("抓取边缘的速度")]
        public float GrabSpeed { get; set; } = 3.0f;
        
        /// <summary>
        /// 是否启用攀爬
        /// </summary>
        [Tooltip("是否启用攀爬")]
        public bool EnableClimbing { get; set; } = true;
        
        /// <summary>
        /// 攀爬冷却时间（秒）
        /// </summary>
        [Tooltip("攀爬冷却时间（秒）")]
        public float ClimbCooldown { get; set; } = 1.0f;
        
        #endregion
        
        #region 攀爬状态
        
        /// <summary>
        /// 当前攀爬状态
        /// </summary>
        public ClimbingState CurrentClimbingState { get; private set; } = ClimbingState.None;
        
        /// <summary>
        /// 上次攀爬时间
        /// </summary>
        private float _lastClimbTime = 0f;
        
        /// <summary>
        /// 攀爬开始时间
        /// </summary>
        private float _climbStartTime = 0f;
        
        /// <summary>
        /// 攀爬起始位置
        /// </summary>
        private Vector3 _climbStartPosition = Vector3.Zero;
        
        /// <summary>
        /// 攀爬目标位置
        /// </summary>
        private Vector3 _climbTargetPosition = Vector3.Zero;
        
        /// <summary>
        /// 攀爬起始旋转
        /// </summary>
        private Quaternion _climbStartRotation = Quaternion.Identity;
        
        /// <summary>
        /// 攀爬目标旋转
        /// </summary>
        private Quaternion _climbTargetRotation = Quaternion.Identity;
        
        #endregion
        
        #region 引用
        
        private PlayerController _playerController;
        private ClimbDetector _climbDetector;
        private InputManager _inputManager;
        private ThirdPersonCamera _camera;
        
        #endregion
        
        #region 生命周期方法
        
        public override void OnStart()
        {
            // 获取引用
            _playerController = Actor.Parent?.GetScript<PlayerController>();
            _climbDetector = Actor.Parent?.GetScript<ClimbDetector>();
            _inputManager = Actor.Parent?.Parent?.GetScript<InputManager>();
            _camera = Actor.Parent?.GetScript<ThirdPersonCamera>();
        }
        
        public override void OnUpdate()
        {
            if (!EnableClimbing || _playerController == null || _climbDetector == null)
                return;
            
            // 更新当前攀爬状态
            UpdateClimbingState();
            
            // 处理输入
            HandleClimbInput();
        }
        
        #endregion
        
        #region 攀爬状态管理
        
        /// <summary>
        /// 更新攀爬状态
        /// </summary>
        private void UpdateClimbingState()
        {
            switch (CurrentClimbingState)
            {
                case ClimbingState.None:
                    // 检查是否可以开始攀爬
                    CheckForClimbStart();
                    break;
                    
                case ClimbingState.ApproachingEdge:
                    UpdateApproachEdge();
                    break;
                    
                case ClimbingState.GrabbingEdge:
                    UpdateGrabEdge();
                    break;
                    
                case ClimbingState.Hanging:
                    UpdateHang();
                    break;
                    
                case ClimbingState.Mantling:
                    UpdateMantle();
                    break;
                    
                case ClimbingState.Climbing:
                    UpdateClimb();
                    break;
                    
                case ClimbingState.Finished:
                    FinishClimbing();
                    break;
            }
        }
        
        /// <summary>
        /// 检查是否可以开始攀爬
        /// </summary>
        private void CheckForClimbStart()
        {
            // 检查冷却时间
            if (Time.GameTime - _lastClimbTime < ClimbCooldown)
                return;
                
            // 检查是否检测到可攀爬边缘且角色在地面上
            if (_climbDetector.IsClimbableEdgeDetected && _playerController.IsGrounded)
            {
                // 检查是否按下攀爬键
                bool climbPressed = _inputManager != null ? 
                    _inputManager.IsActionPressed("Climb") : 
                    Input.GetKey(KeyboardKeys.F);
                
                if (climbPressed)
                {
                    StartClimbing();
                }
            }
        }
        
        /// <summary>
        /// 开始攀爬
        /// </summary>
        private void StartClimbing()
        {
            // 设置起始状态
            _climbStartTime = Time.GameTime;
            _climbStartPosition = Actor.Parent.Position;
            
            // 根据检测到的攀爬类型设置目标位置和状态
            switch (_climbDetector.DetectedClimbType)
            {
                case ClimbType.LowEdge:
                case ClimbType.HighEdge:
                    // 对于边缘攀爬，先接近边缘
                    CurrentClimbingState = ClimbingState.ApproachingEdge;
                    _climbTargetPosition = _climbDetector.ClimbEdgePosition - 
                        _climbDetector.ClimbSurfaceNormal * 0.5f; // 离边缘稍远一点的位置
                    break;
                    
                case ClimbType.VerticalWall:
                    // 垂直墙面攀爬，直接抓取
                    CurrentClimbingState = ClimbingState.GrabbingEdge;
                    _climbTargetPosition = _climbDetector.ClimbEdgePosition;
                    break;
                    
                case ClimbType.HorizontalBar:
                    // 横杆攀爬，直接抓取
                    CurrentClimbingState = ClimbingState.GrabbingEdge;
                    _climbTargetPosition = _climbDetector.ClimbEdgePosition;
                    break;
            }
            
            // 设置目标朝向（面向攀爬表面）
            Vector3 targetDirection = -_climbDetector.ClimbSurfaceNormal;
            targetDirection.Y = 0;
            if (targetDirection.LengthSquared > 0.001f)
            {
                targetDirection.Normalize();
                _climbTargetRotation = Quaternion.LookRotation(targetDirection);
            }
            else
            {
                _climbTargetRotation = Actor.Parent.Orientation;
            }
            
            // 禁用角色控制器的移动
            if (_playerController != null)
            {
                _playerController.EnableMovement = false;
            }
            
            Debug.Log($"开始攀爬: {_climbDetector.DetectedClimbType}");
        }
        
        /// <summary>
        /// 更新接近边缘状态
        /// </summary>
        private void UpdateApproachEdge()
        {
            // 移动到边缘位置
            Vector3 currentPosition = Actor.Parent.Position;
            Vector3 directionToTarget = _climbTargetPosition - currentPosition;
            float distanceToTarget = directionToTarget.Length;
            
            if (distanceToTarget > 0.05f)
            {
                directionToTarget.Normalize();
                Vector3 moveVector = directionToTarget * (ClimbSpeed * 0.5f) * Time.DeltaTime;
                
                // 更新位置
                Actor.Parent.Position += moveVector;
                
                // 平滑旋转到目标朝向
                Actor.Parent.Orientation = Quaternion.Lerp(
                    Actor.Parent.Orientation, 
                    _climbTargetRotation, 
                    5.0f * Time.DeltaTime);
            }
            else
            {
                // 接近完成，开始抓取边缘
                CurrentClimbingState = ClimbingState.GrabbingEdge;
                _climbStartTime = Time.GameTime;
                _climbTargetPosition = _climbDetector.ClimbEdgePosition;
            }
        }
        
        /// <summary>
        /// 更新抓取边缘状态
        /// </summary>
        private void UpdateGrabEdge()
        {
            float elapsedTime = Time.GameTime - _climbStartTime;
            float grabDuration = _climbDetector.GetGrabDuration();
            
            if (elapsedTime < grabDuration)
            {
                // 插值到边缘位置
                float t = elapsedTime / grabDuration;
                Actor.Parent.Position = Vector3.Lerp(_climbStartPosition, _climbTargetPosition, t);
                
                // 旋转到面向表面
                Actor.Parent.Orientation = Quaternion.Lerp(
                    _climbStartRotation, 
                    _climbTargetRotation, 
                    t);
            }
            else
            {
                // 抓取完成，进入悬挂状态
                CurrentClimbingState = ClimbingState.Hanging;
                _climbStartTime = Time.GameTime;
            }
        }
        
        /// <summary>
        /// 更新悬挂状态
        /// </summary>
        private void UpdateHang()
        {
            float elapsedTime = Time.GameTime - _climbStartTime;
            float hangDuration = _climbDetector.GetHangDuration();
            
            // 在悬挂状态下保持位置不变
            Actor.Parent.Position = _climbTargetPosition;
            
            // 悬挂一段时间后开始攀爬到顶部
            if (elapsedTime >= hangDuration)
            {
                CurrentClimbingState = ClimbingState.Mantling;
                _climbStartTime = Time.GameTime;
                _climbStartPosition = Actor.Parent.Position;
                _climbTargetPosition = _climbDetector.MantleTargetPosition;
            }
        }
        
        /// <summary>
        /// 更新攀爬到顶部状态
        /// </summary>
        private void UpdateMantle()
        {
            float elapsedTime = Time.GameTime - _climbStartTime;
            float mantleDuration = _climbDetector.GetMantleDuration();
            
            if (elapsedTime < mantleDuration)
            {
                // 插值到顶部位置
                float t = elapsedTime / mantleDuration;
                Actor.Parent.Position = Vector3.Lerp(_climbStartPosition, _climbTargetPosition, t);
                
                // 逐渐调整朝向到向前
                if (_camera != null)
                {
                    Vector3 forward = _camera.Actor.Transform.Forward;
                    forward.Y = 0;
                    forward.Normalize();
                    Quaternion targetRotation = Quaternion.LookRotation(forward);
                    
                    Actor.Parent.Orientation = Quaternion.Lerp(
                        Actor.Parent.Orientation, 
                        targetRotation, 
                        t);
                }
            }
            else
            {
                // 攀爬完成
                CurrentClimbingState = ClimbingState.Finished;
            }
        }
        
        /// <summary>
        /// 更新垂直攀爬状态
        /// </summary>
        private void UpdateClimb()
        {
            // 垂直攀爬逻辑可以在这里实现
            // 目前简化处理，直接完成
            CurrentClimbingState = ClimbingState.Finished;
        }
        
        /// <summary>
        /// 完成攀爬
        /// </summary>
        private void FinishClimbing()
        {
            // 重新启用角色控制器的移动
            if (_playerController != null)
            {
                _playerController.EnableMovement = true;
            }
            
            // 重置状态
            CurrentClimbingState = ClimbingState.None;
            _lastClimbTime = Time.GameTime;
            
            Debug.Log("攀爬完成");
        }
        
        #endregion
        
        #region 输入处理
        
        /// <summary>
        /// 处理攀爬输入
        /// </summary>
        private void HandleClimbInput()
        {
            // 在攀爬过程中，某些输入可能会中断攀爬
            if (CurrentClimbingState != ClimbingState.None)
            {
                // 检查是否按下跳跃键中断攀爬
                bool jumpPressed = _inputManager != null ? 
                    _inputManager.IsActionPressed("Jump") : 
                    Input.GetKey(KeyboardKeys.Spacebar);
                
                if (jumpPressed)
                {
                    CancelClimbing();
                }
                
                // 检查是否按下蹲伏键中断攀爬
                bool crouchPressed = _inputManager != null ? 
                    _inputManager.IsActionPressed("Crouch") : 
                    Input.GetKey(KeyboardKeys.C);
                
                if (crouchPressed)
                {
                    CancelClimbing();
                }
            }
        }
        
        /// <summary>
        /// 取消攀爬
        /// </summary>
        private void CancelClimbing()
        {
            // 重新启用角色控制器的移动
            if (_playerController != null)
            {
                _playerController.EnableMovement = true;
            }
            
            // 重置状态
            CurrentClimbingState = ClimbingState.None;
            _lastClimbTime = Time.GameTime;
            
            Debug.Log("攀爬已取消");
        }
        
        #endregion
        
        #region 公共方法
        
        /// <summary>
        /// 获取是否正在攀爬
        /// </summary>
        /// <returns>是否正在攀爬</returns>
        public bool IsClimbing()
        {
            return CurrentClimbingState != ClimbingState.None;
        }
        
        /// <summary>
        /// 强制取消攀爬
        /// </summary>
        public void ForceCancelClimbing()
        {
            CancelClimbing();
        }
        
        #endregion
    }
}