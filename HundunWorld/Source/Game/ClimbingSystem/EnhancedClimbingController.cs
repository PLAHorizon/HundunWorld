using FlaxEngine;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.ClimbingSystem
{
    /// <summary>
    /// 增强的爬墙控制器
    /// 提供更流畅和真实的爬墙体验
    /// </summary>
    public class EnhancedClimbingController : Script
    {
        #region 爬墙参数
        [Header("基础爬墙设置")]
        [Tooltip("基础爬墙速度")]
        public float BaseClimbSpeed = 2.5f;
        
        [Tooltip("水平移动速度（沿墙面）")]
        public float HorizontalMoveSpeed = 1.8f;
        
        [Tooltip("垂直移动速度（向上/向下）")]
        public float VerticalMoveSpeed = 2.0f;
        
        [Tooltip("抓取边缘的速度")]
        public float GrabSpeed = 4.0f;
        
        [Tooltip("翻越顶部的速度")]
        public float MantleSpeed = 2.0f;
        
        [Header("物理参数")]
        [Tooltip("爬墙时的重力倍数（0=无重力，1=正常重力）")]
        public float ClimbGravityMultiplier = 0.1f;
        
        [Tooltip("爬墙时的摩擦系数")]
        public float ClimbFriction = 0.8f;
        
        [Tooltip("离开墙面的最大距离")]
        public float WallDetachDistance = 0.8f;
        
        [Header("检测参数")]
        [Tooltip("墙面检测距离")]
        public float WallDetectionDistance = 0.5f;
        
        [Tooltip("头顶空间检测距离")]
        public float OverheadClearance = 2.2f;
        
        [Tooltip("脚部空间检测距离")]
        public float FootClearance = 0.3f;
        
        [Tooltip("检测频率（每秒）")]
        public float DetectionFrequency = 15.0f;
        
        [Header("动画控制")]
        [Tooltip("是否启用动画混合")]
        public bool EnableAnimationBlending = true;
        
        [Tooltip("动画过渡时间")]
        public float AnimationBlendTime = 0.3f;
        #endregion

        #region 爬墙状态
        public enum EnhancedClimbingState
        {
            None,           // 无状态
            Approaching,    // 接近墙面
            Grabbing,       // 抓取墙面
            Climbing,       // 爬墙中
            Hanging,        // 悬挂状态
            Mantling,       // 翻越中
            SlidingDown,    // 滑落
            Exiting         // 退出爬墙
        }

        public EnhancedClimbingState CurrentState { get; private set; } = EnhancedClimbingState.None;
        
        private Vector3 _wallNormal = Vector3.Zero;
        private Vector3 _wallPosition = Vector3.Zero;
        private Vector3 _grabPosition = Vector3.Zero;
        private Vector3 _mantleTarget = Vector3.Zero;
        private float _lastDetectionTime = 0f;
        private float _stateStartTime = 0f;
        private float _verticalInput = 0f;
        private float _horizontalInput = 0f;
        private bool _isNearWall = false;
        private bool _canMantle = false;
        #endregion

        #region 引用组件
        private PlayerController _playerController;
        private CharacterController _characterController;
        private InputManager _inputManager;
        private ThirdPersonCamera _camera;
        #endregion

        #region 动画相关
        private float _animationWeight = 0f;
        private float _targetAnimationWeight = 0f;
        private string _currentAnimation = "";
        #endregion

        public override void OnStart()
        {
            InitializeReferences();
            Debug.Log("[EnhancedClimbing] 爬墙控制器已初始化");
        }

        public override void OnUpdate()
        {
            if (!IsClimbingEnabled())
                return;

            UpdateInputs();
            UpdateDetection();
            UpdateStateMachine();
            UpdateAnimation();
        }

        public override void OnFixedUpdate()
        {
            if (CurrentState != EnhancedClimbingState.None)
            {
                ApplyClimbingPhysics();
            }
        }

        #region 初始化和引用
        private void InitializeReferences()
        {
            _playerController = Actor.Parent?.GetScript<PlayerController>();
            _characterController = Actor.Parent?.GetScript<CharacterController>();
            _inputManager = Actor.Parent?.Parent?.GetScript<InputManager>();
            _camera = Actor.Parent?.GetScript<ThirdPersonCamera>();
        }

        private bool IsClimbingEnabled()
        {
            return _playerController != null && _characterController != null;
        }
        #endregion

        #region 输入处理
        private void UpdateInputs()
        {
            if (_inputManager != null)
            {
                // 使用InputManager的动作检测替代轴输入
                _verticalInput = 0f;
                _horizontalInput = 0f;
                
                // 垂直输入
                if (_inputManager.IsActionPressed("MoveForward"))
                    _verticalInput += 1.0f;
                if (_inputManager.IsActionPressed("MoveBackward"))
                    _verticalInput -= 1.0f;
                    
                // 水平输入
                if (_inputManager.IsActionPressed("MoveRight"))
                    _horizontalInput += 1.0f;
                if (_inputManager.IsActionPressed("MoveLeft"))
                    _horizontalInput -= 1.0f;
                
                // 检查爬墙按键
                if (_inputManager.IsActionPressed("Climb") && 
                    CurrentState == EnhancedClimbingState.None && 
                    _isNearWall)
                {
                    StartClimbing();
                }
                
                // 检查退出爬墙
                if ((CurrentState != EnhancedClimbingState.None) && 
                    _inputManager.IsActionPressed("Jump"))
                {
                    ExitClimbing();
                }
            }
            else
            {
                // 备用输入方式
                _verticalInput = 0f;
                _horizontalInput = 0f;
                
                // 垂直输入
                if (Input.GetKey(KeyboardKeys.W) || Input.GetKey(KeyboardKeys.ArrowUp))
                    _verticalInput += 1.0f;
                if (Input.GetKey(KeyboardKeys.S) || Input.GetKey(KeyboardKeys.ArrowDown))
                    _verticalInput -= 1.0f;
                    
                // 水平输入
                if (Input.GetKey(KeyboardKeys.D) || Input.GetKey(KeyboardKeys.ArrowRight))
                    _horizontalInput += 1.0f;
                if (Input.GetKey(KeyboardKeys.A) || Input.GetKey(KeyboardKeys.ArrowLeft))
                    _horizontalInput -= 1.0f;
                
                if (Input.GetKeyDown(KeyboardKeys.F) && 
                    CurrentState == EnhancedClimbingState.None && 
                    _isNearWall)
                {
                    StartClimbing();
                }
                
                // 检查Spacebar键
                if ((CurrentState != EnhancedClimbingState.None) && 
                    Input.GetKeyDown(KeyboardKeys.Spacebar))
                {
                    ExitClimbing();
                }
            }
        }
        #endregion

        #region 检测系统
        private void UpdateDetection()
        {
            if (Time.GameTime - _lastDetectionTime < 1.0f / DetectionFrequency)
                return;

            _lastDetectionTime = Time.GameTime;
            
            Vector3 characterPosition = Actor.Parent.Position;
            Vector3 forwardDirection = Actor.Parent.Transform.Forward;
            forwardDirection.Y = 0;
            forwardDirection.Normalize();

            // 墙面检测
            Vector3 detectStart = characterPosition + Vector3.Up * 1.2f;
            if (Physics.RayCast(detectStart, forwardDirection, out var wallHit, WallDetectionDistance))
            {
                _isNearWall = true;
                _wallNormal = wallHit.Normal;
                _wallPosition = wallHit.Point;
                
                // 检查头顶空间
                Vector3 overheadStart = _wallPosition + _wallNormal * 0.1f + Vector3.Up * 0.5f;
                _canMantle = !Physics.RayCast(overheadStart, Vector3.Up, OverheadClearance);
                
                // 检查脚部空间
                Vector3 footStart = _wallPosition + _wallNormal * 0.1f - Vector3.Up * 0.2f;
                bool hasFootSpace = !Physics.RayCast(footStart, Vector3.Down, FootClearance);
            }
            else
            {
                _isNearWall = false;
                // 如果离墙太远，自动退出爬墙状态
                if (CurrentState != EnhancedClimbingState.None && 
                    Vector3.Distance(characterPosition, _wallPosition) > WallDetachDistance)
                {
                    ExitClimbing();
                }
            }
        }
        #endregion

        #region 状态机
        private void UpdateStateMachine()
        {
            switch (CurrentState)
            {
                case EnhancedClimbingState.None:
                    UpdateNoneState();
                    break;
                case EnhancedClimbingState.Approaching:
                    UpdateApproachingState();
                    break;
                case EnhancedClimbingState.Grabbing:
                    UpdateGrabbingState();
                    break;
                case EnhancedClimbingState.Climbing:
                    UpdateClimbingState();
                    break;
                case EnhancedClimbingState.Hanging:
                    UpdateHangingState();
                    break;
                case EnhancedClimbingState.Mantling:
                    UpdateMantlingState();
                    break;
                case EnhancedClimbingState.SlidingDown:
                    UpdateSlidingState();
                    break;
                case EnhancedClimbingState.Exiting:
                    UpdateExitingState();
                    break;
            }
        }

        private void UpdateNoneState()
        {
            // 空闲状态下检查是否可以开始爬墙
            if (_isNearWall && _verticalInput > 0.1f)
            {
                StartClimbing();
            }
        }

        private void UpdateApproachingState()
        {
            // 向墙面移动
            Vector3 targetPosition = _wallPosition - _wallNormal * 0.3f;
            Vector3 moveDirection = (targetPosition - Actor.Parent.Position).Normalized;
            
            _characterController.Move(moveDirection * BaseClimbSpeed * Time.DeltaTime);
            
            // 检查是否到达抓取位置
            if (Vector3.Distance(Actor.Parent.Position, targetPosition) < 0.2f)
            {
                CurrentState = EnhancedClimbingState.Grabbing;
                _stateStartTime = Time.GameTime;
                _grabPosition = Actor.Parent.Position;
                PlayAnimation("Climb_Grab");
            }
        }

        private void UpdateGrabbingState()
        {
            // 固定在墙上，播放抓取动画
            if (Time.GameTime - _stateStartTime > 0.5f)
            {
                CurrentState = EnhancedClimbingState.Climbing;
                PlayAnimation("Climb_Idle");
            }
        }

        private void UpdateClimbingState()
        {
            // 处理爬墙移动
            HandleClimbingMovement();
            
            // 检查翻越条件
            if (_canMantle && _verticalInput > 0.8f)
            {
                StartMantling();
            }
            // 检查滑落条件
            else if (_verticalInput < -0.8f)
            {
                CurrentState = EnhancedClimbingState.SlidingDown;
                _stateStartTime = Time.GameTime;
                PlayAnimation("Climb_Slide");
            }
        }

        private void UpdateHangingState()
        {
            // 悬挂状态，等待输入
            if (Math.Abs(_verticalInput) > 0.1f || Math.Abs(_horizontalInput) > 0.1f)
            {
                CurrentState = EnhancedClimbingState.Climbing;
                PlayAnimation("Climb_Move");
            }
        }

        private void UpdateMantlingState()
        {
            // 向翻越目标移动
            Vector3 currentPosition = Actor.Parent.Position;
            Vector3 direction = (_mantleTarget - currentPosition).Normalized;
            float distance = Vector3.Distance(currentPosition, _mantleTarget);
            
            _characterController.Move(direction * MantleSpeed * Time.DeltaTime);
            
            // 完成翻越
            if (distance < 0.1f || Time.GameTime - _stateStartTime > 2.0f)
            {
                FinishMantling();
            }
        }

        private void UpdateSlidingState()
        {
            // 沿墙面下滑
            Vector3 slideDirection = -Vector3.Up + _wallNormal * 0.2f;
            _characterController.Move(slideDirection * VerticalMoveSpeed * 0.7f * Time.DeltaTime);
            
            // 滑落一段时间后退出
            if (Time.GameTime - _stateStartTime > 1.5f)
            {
                ExitClimbing();
            }
        }

        private void UpdateExitingState()
        {
            // 退出动画完成后回到正常状态
            if (Time.GameTime - _stateStartTime > 0.5f)
            {
                CurrentState = EnhancedClimbingState.None;
                _targetAnimationWeight = 0f;
            }
        }
        #endregion

        #region 核心功能实现
        private void StartClimbing()
        {
            CurrentState = EnhancedClimbingState.Approaching;
            _stateStartTime = Time.GameTime;
            PlayAnimation("Climb_Approach");
            Debug.Log("[EnhancedClimbing] 开始爬墙");
        }

        private void HandleClimbingMovement()
        {
            if (Math.Abs(_verticalInput) < 0.1f && Math.Abs(_horizontalInput) < 0.1f)
            {
                CurrentState = EnhancedClimbingState.Hanging;
                PlayAnimation("Climb_Hang");
                return;
            }

            // 计算移动方向（沿墙面的本地坐标系）
            Vector3 upDirection = Vector3.Up;
            Vector3 rightDirection = Vector3.Cross(_wallNormal, upDirection).Normalized;
            upDirection = Vector3.Cross(rightDirection, _wallNormal).Normalized;

            Vector3 moveDirection = (_horizontalInput * rightDirection + _verticalInput * upDirection).Normalized;
            
            // 应用移动
            float speed = (_verticalInput > 0) ? VerticalMoveSpeed : HorizontalMoveSpeed;
            _characterController.Move(moveDirection * speed * Time.DeltaTime);
            
            // 调整角色朝向
            if (moveDirection.LengthSquared > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(-_wallNormal, Vector3.Up);
                var currentTransform = Actor.Parent.Transform;
                currentTransform.Orientation = Quaternion.Lerp(
                    currentTransform.Orientation, 
                    targetRotation, 
                    Time.DeltaTime * 5.0f
                );
                Actor.Parent.Transform = currentTransform;
            }
            
            PlayAnimation("Climb_Move");
        }

        private void StartMantling()
        {
            CurrentState = EnhancedClimbingState.Mantling;
            _stateStartTime = Time.GameTime;
            _mantleTarget = _wallPosition + Vector3.Up * OverheadClearance;
            PlayAnimation("Climb_Mantle");
            Debug.Log("[EnhancedClimbing] 开始翻越");
        }

        private void FinishMantling()
        {
            CurrentState = EnhancedClimbingState.None;
            _targetAnimationWeight = 0f;
            Debug.Log("[EnhancedClimbing] 翻越完成");
        }

        private void ExitClimbing()
        {
            CurrentState = EnhancedClimbingState.Exiting;
            _stateStartTime = Time.GameTime;
            PlayAnimation("Climb_Exit");
            Debug.Log("[EnhancedClimbing] 退出爬墙");
        }

        private void ApplyClimbingPhysics()
        {
            // 减少重力影响 - 直接修改位置而不是使用AddForce
            var gravityEffect = Physics.Gravity * ClimbGravityMultiplier * Time.DeltaTime;
            Actor.Parent.Position += gravityEffect;
            
            // 应用墙面摩擦 - 通过修改实际速度变量
            // 注意：这里假设_characterController有_velocity字段或者类似机制
            // 如果没有，我们需要自己维护速度变量
            Vector3 currentPosition = Actor.Parent.Position;
            Vector3 newPosition = currentPosition;
            newPosition.X *= ClimbFriction;
            newPosition.Z *= ClimbFriction;
            Actor.Parent.Position = newPosition;
        }
        #endregion

        #region 动画系统
        private void UpdateAnimation()
        {
            if (!EnableAnimationBlending)
                return;

            // 平滑过渡动画权重
            _animationWeight = Mathf.Lerp(_animationWeight, _targetAnimationWeight, Time.DeltaTime / AnimationBlendTime);
            
            // 这里应该连接到实际的动画系统
            // ApplyAnimationWeights();
        }

        private void PlayAnimation(string animationName)
        {
            _currentAnimation = animationName;
            _targetAnimationWeight = 1.0f;
            Debug.Log($"[EnhancedClimbing] 播放动画: {animationName}");
        }
        #endregion

        #region 公共接口
        public bool IsClimbing()
        {
            return CurrentState != EnhancedClimbingState.None;
        }

        public EnhancedClimbingState GetClimbingState()
        {
            return CurrentState;
        }

        public Vector3 GetWallNormal()
        {
            return _wallNormal;
        }

        public void EnableClimbing(bool enable)
        {
            if (!enable && CurrentState != EnhancedClimbingState.None)
            {
                ExitClimbing();
            }
        }
        #endregion
    }
}