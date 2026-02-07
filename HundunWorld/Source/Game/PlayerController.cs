﻿﻿﻿﻿﻿﻿﻿﻿﻿using FlaxEngine;
using Horizon.Game.Message.Enums;
using System.Collections.Generic;
using System.Linq;
using Game;

namespace HundunWorld.Game
{
    /// <summary>
    /// 角色控制脚本，参照剑侠情缘和魔兽世界的角色控制系统设计
    /// </summary>
    public class PlayerController : Script
    {
        #region 角色基本参数

        /// <summary>
        /// 角色状态枚举
        /// </summary>
        

        /// <summary>
        /// 移动速度（基础速度）
        /// </summary>
        [Tooltip("角色移动速度")]
        public float MoveSpeed { get; set; } = 5.0f;

        /// <summary>
        /// 跑步速度倍数
        /// </summary>
        [Tooltip("跑步速度倍数")]
        public float RunSpeedMultiplier { get; set; } = 2.0f;

        /// <summary>
        /// 跳跃力度
        /// </summary>
        [Tooltip("角色跳跃力度")]
        public float JumpForce { get; set; } = 10.0f;

        /// <summary>
        /// 重力
        /// </summary>
        [Tooltip("重力")]
        public float Gravity { get; set; } = -9.81f;

        /// <summary>
        /// 地面检测距离
        /// </summary>
        [Tooltip("地面检测距离")]
        public float GroundCheckDistance { get; set; } = 0.1f;

        /// <summary>
        /// 角色当前状态
        /// </summary>
        public CharacterState CurrentState { get; private set; } = CharacterState.Idle;

        #endregion

        #region 角色控制参数

        /// <summary>
        /// 是否在地面上
        /// </summary>
        private bool _isGrounded = true;
        
        /// <summary>
        /// 获取角色是否在地面上
        /// </summary>
        public bool IsGrounded => _isGrounded;

        /// <summary>
        /// 垂直速度
        /// </summary>
        private float _verticalVelocity = 0.0f;

        /// <summary>
        /// 目标位置（用于点击移动）
        /// </summary>
        private Vector3 _targetPosition = Vector3.Zero;

        /// <summary>
        /// 是否正在移动到目标位置
        /// </summary>
        private bool _isMovingToTarget = false;

        /// <summary>
        /// 角色朝向平滑度
        /// </summary>
        public float RotationSmoothing { get; set; } = 0.1f;

        /// <summary>
        /// 输入缓冲时间（秒）
        /// </summary>
        [Tooltip("输入缓冲时间（秒）")]
        public float InputBufferTime { get; set; } = 0.1f;

        /// <summary>
        /// 上次输入时间
        /// </summary>
        private float _lastInputTime = 0f;

        /// <summary>
        /// 输入管理器引用
        /// </summary>
        private InputManager _inputManager;

        /// <summary>
        /// 状态转换记录
        /// </summary>
        private CharacterState _previousState = CharacterState.Idle;

        /// <summary>
        /// 状态持续时间
        /// </summary>
        private float _stateTime = 0f;

        /// <summary>
        /// 移动预测缓冲
        /// </summary>
        private Queue<Vector3> _movementBuffer = new Queue<Vector3>();

        /// <summary>
        /// 最大预测缓冲大小
        /// </summary>
        private const int MaxBufferSize = 5;

        #endregion

        #region 角色移动参数

        /// <summary>
        /// 当前移动方向
        /// </summary>
        private Vector3 _moveDirection = Vector3.Zero;

        /// <summary>
        /// 当前移动速度
        /// </summary>
        private float _currentMoveSpeed = 0f;

        /// <summary>
        /// 是否正在跑步
        /// </summary>
        private bool _isRunning = false;

        /// <summary>
        /// 是否正在蹲伏
        /// </summary>
        private bool _isCrouching = false;

        /// <summary>
        /// 蹲伏速度倍数
        /// </summary>
        [Tooltip("蹲伏速度倍数")]
        public float CrouchSpeedMultiplier { get; set; } = 0.5f;

        /// <summary>
        /// 是否正在冲刺
        /// </summary>
        private bool _isSprinting = false;

        /// <summary>
        /// 冲刺速度倍数
        /// </summary>
        [Tooltip("冲刺速度倍数")]
        public float SprintSpeedMultiplier { get; set; } = 2.5f;

        /// <summary>
        /// 冲刺体力消耗率
        /// </summary>
        [Tooltip("冲刺体力消耗率")]
        public float SprintStaminaCost { get; set; } = 20.0f;

        /// <summary>
        /// 当前体力值
        /// </summary>
        public float CurrentStamina { get; private set; } = 100.0f;

        /// <summary>
        /// 最大体力值
        /// </summary>
        [Tooltip("最大体力值")]
        public float MaxStamina { get; set; } = 100.0f;

        /// <summary>
        /// 体力恢复速度
        /// </summary>
        [Tooltip("体力恢复速度")]
        public float StaminaRegenRate { get; set; } = 15.0f;

        /// <summary>
        /// 是否正在滑行
        /// </summary>
        private bool _isSliding = false;

        /// <summary>
        /// 滑行持续时间
        /// </summary>
        private float _slideDuration = 0f;

        /// <summary>
        /// 最大滑行时间
        /// </summary>
        [Tooltip("最大滑行时间")]
        public float MaxSlideTime { get; set; } = 1.5f;

        /// <summary>
        /// 是否启用移动
        /// </summary>
        public bool EnableMovement { get; set; } = true;
        
        /// <summary>
        /// 是否启用输入（当UI激活时禁用）
        /// </summary>
        public bool EnableInput { get; set; } = true;

        /// <summary>
        /// 滑行速度倍数
        /// </summary>
        [Tooltip("滑行速度倍数")]
        public float SlideSpeedMultiplier { get; set; } = 1.8f;

        /// <summary>
        /// 滑行减速度
        /// </summary>
        [Tooltip("滑行减速度")]
        public float SlideDeceleration { get; set; } = 8.0f;

        /// <summary>
        /// 台阶高度阈值
        /// </summary>
        [Tooltip("可以自动踏上的台阶高度")]
        public float StepHeight { get; set; } = 0.3f;

        /// <summary>
        /// 坡度限制角度
        /// </summary>
        [Tooltip("角色可以行走的最大坡度角度")]
        public float MaxSlopeAngle { get; set; } = 45.0f;

        /// <summary>
        /// 移动加速度
        /// </summary>
        [Tooltip("移动加速度")]
        public float Acceleration { get; set; } = 20.0f;

        /// <summary>
        /// 移动减速度
        /// </summary>
        [Tooltip("移动减速度")]
        public float Deceleration { get; set; } = 25.0f;

        /// <summary>
        /// 空中移动控制力度
        /// </summary>
        [Tooltip("空中移动控制力度")]
        public float AirControl { get; set; } = 0.3f;

        /// <summary>
        /// 当前实际速度
        /// </summary>
        private Vector3 _currentVelocity = Vector3.Zero;

        #endregion

        #region 相机引用

        /// <summary>
        /// 相机引用
        /// </summary>
        public ThirdPersonCamera Camera { get; set; }

        #endregion

        #region 生命周期方法

        public override void OnStart()
        {
            // 获取输入管理器
            _inputManager = Actor.Parent?.GetScript<InputManager>();
            
            // 初始化角色控制器
            CurrentState = CharacterState.Idle;
            _previousState = CharacterState.Idle;
            _stateTime = 0f;
            
            // 初始化移动缓冲
            _movementBuffer = new Queue<Vector3>();
        }

        public override void OnUpdate()
        {
            // 如果输入被禁用，跳过所有输入处理
            if (!EnableInput)
            {
                return;
            }
            
            // 更新状态时间
            _stateTime += Time.DeltaTime;
            
            // 更新体力系统
            UpdateStaminaSystem();
            
            // 更新滑行系统
            UpdateSlideSystem();
            
            // 处理角色移动
            HandleCharacterMovement();

            // 处理地面点击
            HandleGroundClick();

            // 更新角色状态
            UpdateCharacterState();
            
            // 更新移动预测缓冲
            UpdateMovementBuffer();
        }

        #endregion

        #region 角色移动处理

        /// <summary>
        /// 处理角色移动
        /// </summary>
        private void HandleCharacterMovement()
        {
            // 获取输入方向
            Vector3 inputDirection = GetInputDirection();
            
            // 处理辅助操作输入
            HandleAuxiliaryInputs();
            
            // 计算目标移动速度
            float targetSpeed = CalculateTargetSpeed(inputDirection);
            
            // 更新移动速度和方向
            UpdateMovementVelocity(inputDirection, targetSpeed);
            
            // 处理重力和垂直移动
            HandleVerticalMovement();
            
            // 应用移动
            ApplyMovement();
            
            // 更新角色朝向
            UpdateCharacterRotation(inputDirection);
            
            // 处理点击移动
            HandleClickToMove();
        }
        
        /// <summary>
        /// 获取输入方向
        /// </summary>
        /// <returns>输入方向向量</returns>
        private Vector3 GetInputDirection()
        {
            Vector3 inputDirection = Vector3.Zero;
            
            // 使用输入管理器处理方向键移动
            if (_inputManager != null)
            {
                if (_inputManager.IsActionPressed("MoveForward"))
                    inputDirection += Vector3.Forward;
                if (_inputManager.IsActionPressed("MoveBackward"))
                    inputDirection += Vector3.Backward;
                if (_inputManager.IsActionPressed("MoveLeft"))
                    inputDirection += Vector3.Left;
                if (_inputManager.IsActionPressed("MoveRight"))
                    inputDirection += Vector3.Right;
            }
            else
            {
                // 退回到直接输入检查
                if (Input.GetKey(KeyboardKeys.W) || Input.GetKey(KeyboardKeys.ArrowUp))
                    inputDirection += Vector3.Forward;
                if (Input.GetKey(KeyboardKeys.S) || Input.GetKey(KeyboardKeys.ArrowDown))
                    inputDirection += Vector3.Backward;
                if (Input.GetKey(KeyboardKeys.A) || Input.GetKey(KeyboardKeys.ArrowLeft))
                    inputDirection += Vector3.Left;
                if (Input.GetKey(KeyboardKeys.D) || Input.GetKey(KeyboardKeys.ArrowRight))
                    inputDirection += Vector3.Right;
            }
            
            // 归一化输入方向
            if (inputDirection.LengthSquared > 0.001f)
            {
                inputDirection.Normalize();
                
                // 将移动方向从相机空间转换为世界空间
                if (Camera != null)
                {
                    Vector3 cameraRight = Camera.Actor.Transform.Right;
                    Vector3 cameraForward = Camera.Actor.Transform.Forward;
                    cameraRight.Y = 0;
                    cameraForward.Y = 0;
                    cameraRight.Normalize();
                    cameraForward.Normalize();
                    
                    inputDirection = inputDirection.X * cameraRight + inputDirection.Z * cameraForward;
                }
                
                // 更新上次输入时间
                _lastInputTime = Time.GameTime;
            }
            
            return inputDirection;
        }
        
        /// <summary>
        /// 处理辅助操作输入
        /// </summary>
        private void HandleAuxiliaryInputs()
        {
            // 处理跑步输入
            _isRunning = _inputManager != null ? _inputManager.IsActionPressed("Run") : Input.GetKey(KeyboardKeys.Shift);
            
            // 处理冲刺输入
            bool sprintPressed = _inputManager != null ? _inputManager.IsActionPressed("Sprint") : Input.GetKey(KeyboardKeys.Shift);
            HandleSprintInput(sprintPressed);
            
            // 处理蹲伏输入
            if (_inputManager != null ? _inputManager.IsActionDown("Crouch") : Input.GetKeyDown(KeyboardKeys.C))
            {
                HandleCrouchInput();
            }
            
            // 处理滑行输入（冲刺状态下按蹲伏键）
            bool slidePressed = _inputManager != null ? _inputManager.IsActionDown("Crouch") : Input.GetKeyDown(KeyboardKeys.C);
            if (slidePressed && _isSprinting && _isGrounded)
            {
                StartSlide();
            }
        }
        
        /// <summary>
        /// 计算目标移动速度
        /// </summary>
        /// <param name="inputDirection">输入方向</param>
        /// <returns>目标速度</returns>
        private float CalculateTargetSpeed(Vector3 inputDirection)
        {
            if (inputDirection.LengthSquared <= 0.001f && !_isSliding)
                return 0f;
            
            float baseSpeed = MoveSpeed;
            
            // 根据状态调整速度
            if (_isSliding)
            {
                // 滑行状态下使用当前滑行速度
                baseSpeed = _currentMoveSpeed * SlideSpeedMultiplier;
            }
            else if (_isCrouching)
            {
                baseSpeed *= CrouchSpeedMultiplier;
            }
            else if (_isSprinting && CanSprint())
            {
                baseSpeed *= SprintSpeedMultiplier;
            }
            else if (_isRunning)
            {
                baseSpeed *= RunSpeedMultiplier;
            }
            
            // 在空中时降低移动控制力
            if (!_isGrounded)
            {
                baseSpeed *= AirControl;
            }
            
            return baseSpeed;
        }
        
        /// <summary>
        /// 更新移动速度和方向
        /// </summary>
        /// <param name="inputDirection">输入方向</param>
        /// <param name="targetSpeed">目标速度</param>
        private void UpdateMovementVelocity(Vector3 inputDirection, float targetSpeed)
        {
            Vector3 targetVelocity = inputDirection * targetSpeed;
            
            // 平滑过渡到目标速度
            if (targetSpeed > 0f)
            {
                // 加速
                _currentVelocity = Vector3.Lerp(_currentVelocity, targetVelocity, Acceleration * Time.DeltaTime);
            }
            else
            {
                // 减速
                _currentVelocity = Vector3.Lerp(_currentVelocity, Vector3.Zero, Deceleration * Time.DeltaTime);
            }
            
            _moveDirection = inputDirection;
            _currentMoveSpeed = _currentVelocity.Length;
        }
        
        /// <summary>
        /// 处理垂直移动和重力
        /// </summary>
        private void HandleVerticalMovement()
        {
            // 处理跳跃
            bool jumpPressed = _inputManager != null ? _inputManager.IsActionPressed("Jump") : Input.GetKey(KeyboardKeys.Spacebar);
            if (_isGrounded && jumpPressed)
            {
                _verticalVelocity = JumpForce;
                _isGrounded = false;
                ChangeState(CharacterState.Jumping);
            }
            
            // 应用重力
            if (!_isGrounded)
            {
                _verticalVelocity += Gravity * Time.DeltaTime;
            }
            else
            {
                _verticalVelocity = 0f;
            }
        }
        
        /// <summary>
        /// 应用移动
        /// </summary>
        private void ApplyMovement()
        {
            // 检查是否启用移动
            if (!EnableMovement)
                return;
            
            // 水平移动
            Vector3 horizontalMovement = _currentVelocity * Time.DeltaTime;
            
            // 垂直移动
            Vector3 verticalMovement = Vector3.Up * _verticalVelocity * Time.DeltaTime;
            
            // 合并移动
            Vector3 totalMovement = horizontalMovement + verticalMovement;
            
            // 应用移动
            Actor.Position += totalMovement;
            
            // 检查地面状态
            CheckGroundStatus();
        }
        
        /// <summary>
        /// 更新角色朝向
        /// </summary>
        /// <param name="inputDirection">输入方向</param>
        private void UpdateCharacterRotation(Vector3 inputDirection)
        {
            // 检查是否启用移动
            if (!EnableMovement)
                return;
            
            if (inputDirection.LengthSquared > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(inputDirection);
                if (RotationSmoothing > 0.0f)
                {
                    Actor.Orientation = Quaternion.Lerp(Actor.Orientation, targetRotation, RotationSmoothing);
                }
                else
                {
                    Actor.Orientation = targetRotation;
                }
            }
        }
        
        /// <summary>
        /// 处理点击移动
        /// </summary>
        private void HandleClickToMove()
        {
            // 检查是否启用移动
            if (!EnableMovement)
                return;
            
            if (_isMovingToTarget)
            {
                Vector3 directionToTarget = _targetPosition - Actor.Position;
                directionToTarget.Y = 0; // 忽略Y轴差异
                
                if (directionToTarget.LengthSquared > 0.1f)
                {
                    directionToTarget.Normalize();
                    
                    // 计算点击移动速度
                    float clickMoveSpeed = _isRunning ? MoveSpeed * RunSpeedMultiplier : MoveSpeed;
                    Vector3 moveVelocity = directionToTarget * clickMoveSpeed;
                    
                    Actor.Position += moveVelocity * Time.DeltaTime;
                    
                    // 平滑旋转角色朝向移动方向
                    Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                    if (RotationSmoothing > 0.0f)
                    {
                        Actor.Orientation = Quaternion.Lerp(Actor.Orientation, targetRotation, RotationSmoothing);
                    }
                    else
                    {
                        Actor.Orientation = targetRotation;
                    }
                }
                else
                {
                    _isMovingToTarget = false;
                    ChangeState(CharacterState.Idle);
                }
            }
        }
        
        /// <summary>
        /// 检查地面状态
        /// </summary>
        private void CheckGroundStatus()
        {
            bool wasGrounded = _isGrounded;
            _isGrounded = CheckIsGrounded();
            
            // 如果刚落地，调整位置
            if (!wasGrounded && _isGrounded)
            {
                Actor.Position = new Vector3(Actor.Position.X, GetGroundHeight(), Actor.Position.Z);
                _verticalVelocity = 0;
                
                // 触发相机震动效果
                if (Camera != null)
                {
                    Camera.TriggerShake(0.1f, 0.2f);
                }
            }
        }

        /// <summary>
        /// 检查角色是否在地面上
        /// </summary>
        /// <returns>是否在地面上</returns>
        private bool CheckIsGrounded()
        {
            // 简单的地面检测（在实际项目中应该使用碰撞检测）
            return Actor.Position.Y <= GetGroundHeight() + GroundCheckDistance;
        }

        /// <summary>
        /// 获取地面高度
        /// </summary>
        /// <returns>地面高度</returns>
        private float GetGroundHeight()
        {
            // 简单的地面高度（在实际项目中应该从地形或碰撞体获取）
            return 0f;
        }

        #endregion

        #region 角色状态处理

        /// <summary>
        /// 更新角色状态
        /// </summary>
        private void UpdateCharacterState()
        {
            CharacterState newState = DetermineNewState();
            
            if (newState != CurrentState)
            {
                ChangeState(newState);
            }
        }
        
        /// <summary>
        /// 确定新状态
        /// </summary>
        /// <returns>新的角色状态</returns>
        private CharacterState DetermineNewState()
        {
            // 检查是否有输入缓冲
            bool hasInputBuffer = (Time.GameTime - _lastInputTime) < InputBufferTime;
            bool hasMovement = _currentVelocity.LengthSquared > 0.001f || hasInputBuffer;
            bool hasInput = _moveDirection.LengthSquared > 0.001f;
            
            // 优先检查垂直状态
            if (!_isGrounded)
            {
                return _verticalVelocity > 0 ? CharacterState.Jumping : CharacterState.Falling;
            }
            
            // 检查特殊状态
            if (_isSliding)
            {
                return CharacterState.Sliding;
            }
            
            if (_isCrouching)
            {
                return CharacterState.Crouching;
            }
            
            // 检查移动状态
            if (hasMovement || _isMovingToTarget)
            {
                if (_isSprinting && !_isCrouching)
                {
                    return CharacterState.Running;
                }
                else if (_isRunning && !_isCrouching)
                {
                    return CharacterState.Running;
                }
                else
                {
                    return CharacterState.Walking;
                }
            }
            
            // 默认为空闲状态
            return CharacterState.Idle;
        }
        
        /// <summary>
        /// 改变角色状态
        /// </summary>
        /// <param name="newState">新状态</param>
        private void ChangeState(CharacterState newState)
        {
            if (newState == CurrentState)
                return;
                
            // 执行退出操作
            OnStateExit(CurrentState);
            
            // 更新状态
            _previousState = CurrentState;
            CurrentState = newState;
            _stateTime = 0f;
            
            // 执行进入操作
            OnStateEnter(newState);
        }
        
        /// <summary>
        /// 状态进入回调
        /// </summary>
        /// <param name="state">进入的状态</param>
        private void OnStateEnter(CharacterState state)
        {
            switch (state)
            {
                case CharacterState.Jumping:
                    // 跳跃状态进入时的处理
                    break;
                    
                case CharacterState.Falling:
                    // 降落状态进入时的处理
                    break;
                    
                case CharacterState.Running:
                    // 跑步状态进入时的处理
                    break;
                    
                case CharacterState.Crouching:
                    // 蹲伏状态进入时的处理
                    break;
                    
                case CharacterState.Sliding:
                    // 滑行状态进入时的处理
                    break;
            }
        }
        
        /// <summary>
        /// 状态退出回调
        /// </summary>
        /// <param name="state">退出的状态</param>
        private void OnStateExit(CharacterState state)
        {
            switch (state)
            {
                case CharacterState.Jumping:
                    // 跳跃状态退出时的处理
                    break;
                    
                case CharacterState.Falling:
                    // 降落状态退出时的处理
                    break;
                    
                case CharacterState.Running:
                    // 跑步状态退出时的处理
                    break;
                    
                case CharacterState.Crouching:
                    // 蹲伏状态退出时的处理
                    break;
                    
                case CharacterState.Sliding:
                    // 滑行状态退出时的处理
                    break;
            }
        }

        #region 移动预测和缓冲
        
        /// <summary>
        /// 更新移动预测缓冲
        /// </summary>
        private void UpdateMovementBuffer()
        {
            // 娣诲姞褰撳墠浣嶇疆鍒扮紦鍐?
            _movementBuffer.Enqueue(Actor.Position);
            
            // 淇濇寔缂撳啿澶у皬
            while (_movementBuffer.Count > MaxBufferSize)
            {
                _movementBuffer.Dequeue();
            }
        }
        
        /// <summary>
        /// 获取预测移动方向
        /// </summary>
        /// <returns>预测的移动方向</returns>
        private Vector3 GetPredictedMovementDirection()
        {
            if (_movementBuffer.Count < 2)
                return _moveDirection;
                
            var positions = _movementBuffer.ToArray();
            Vector3 totalDirection = Vector3.Zero;
            
            for (int i = 1; i < positions.Length; i++)
            {
                Vector3 direction = positions[i] - positions[i - 1];
                totalDirection += direction;
            }
            
            if (totalDirection.LengthSquared > 0.001f)
            {
                totalDirection.Normalize();
                return totalDirection;
            }
            
            return _moveDirection;
        }
        
        /// <summary>
        /// 应用移动预测校正
        /// </summary>
        /// <param name="serverPosition">服务器位置</param>
        public void ApplyServerCorrection(Vector3 serverPosition)
        {
            float distance = Vector3.Distance(Actor.Position, serverPosition);
            
            // 濡傛灉璺濈宸紓杈冨ぇ锛岃繘琛屼綅缃牎姝?
            if (distance > 0.5f)
            {
                // 骞虫粦杩囨浮鍒版湇鍔″櫒浣嶇疆
                Actor.Position = Vector3.Lerp(Actor.Position, serverPosition, 0.8f);
                
                // 娓呯┖绉诲姩缂撳啿
                _movementBuffer.Clear();
            }
        }
        
        #endregion

        /// <summary>
        /// 处理地面点击
        /// </summary>
        private void HandleGroundClick()
        {
            // 浣跨敤杈撳叆绠＄悊鍣ㄥ鐞嗗湴闈㈢偣鍑昏緭鍏?
            bool groundClickDown = _inputManager != null ? _inputManager.IsActionDown("GroundClick") : Input.GetMouseButtonDown(MouseButton.Left);
            
            // 澶勭悊鍦伴潰鐐瑰嚮杈撳叆
            if (groundClickDown)
            {
                // 鎵ц灏勭嚎妫€娴嬫潵纭畾鐐瑰嚮浣嶇疆
                if (PerformGroundRaycast(Input.MouseScreenPosition, out Vector3 hitPoint))
                {
                    // 璁剧疆鐩爣浣嶇疆
                    _targetPosition = hitPoint;
                    _targetPosition.Y = Actor.Position.Y; // 淇濇寔Y杞翠竴鑷?
                    _isMovingToTarget = true;
                    
                    // 鏇存柊鐘舵€?
                    CurrentState = CharacterState.Walking;
                }
            }
        }

        /// <summary>
        /// 执行地面射线检测
        /// </summary>
        /// <param name="screenPosition">屏幕位置</param>
        /// <param name="hitPoint">命中点</param>
        /// <returns>是否命中</returns>
        private bool PerformGroundRaycast(Float2 screenPosition, out Vector3 hitPoint)
        {
            hitPoint = Vector3.Zero;

            // 鑾峰彇涓荤浉鏈?
            Camera mainCamera = Camera?.Actor?.GetScript<Camera>();
            if (mainCamera == null)
                return false;

            // 浠庡睆骞曚綅缃垱寤哄皠绾?
            Ray ray = mainCamera.ConvertMouseToRay(screenPosition);

            // 鎵ц灏勭嚎妫€娴嬶紝浣跨敤FlaxEngine鐨勬纭瓵PI
            if (Physics.RayCast(ray.Position, ray.Direction, out var hitInfo, 1000.0f))
            {
                hitPoint = hitInfo.Point;
                return true;
            }

            return false;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取当前角色状态
        /// </summary>
        /// <returns>当前角色状态</returns>
        public CharacterState GetCharacterState()
        {
            return CurrentState;
        }

        /// <summary>
        /// 设置角色位置
        /// </summary>
        /// <param name="position">位置</param>
        public void SetPosition(Vector3 position)
        {
            Actor.Position = position;
        }

        /// <summary>
        /// 获取角色位置
        /// </summary>
        /// <returns>角色位置</returns>
        public Vector3 GetPosition()
        {
            return Actor.Position;
        }

        /// <summary>
        /// 获取当前体力值
        /// </summary>
        /// <returns>当前体力值</returns>
        public float GetCurrentStamina()
        {
            return CurrentStamina;
        }

        /// <summary>
        /// 获取体力百分比
        /// </summary>
        /// <returns>体力百分比（0-1）</returns>
        public float GetStaminaPercentage()
        {
            return CurrentStamina / MaxStamina;
        }

        /// <summary>
        /// 检查是否正在冲刺
        /// </summary>
        /// <returns>是否正在冲刺</returns>
        public bool IsSprinting()
        {
            return _isSprinting;
        }

        /// <summary>
        /// 检查是否正在滑行
        /// </summary>
        /// <returns>是否正在滑行</returns>
        public bool IsSliding()
        {
            return _isSliding;
        }

        #endregion

        #region 冲刺和滑行系统

        /// <summary>
        /// 处理冲刺输入
        /// </summary>
        /// <param name="sprintPressed">冲刺键是否按下</param>
        private void HandleSprintInput(bool sprintPressed)
        {
            if (sprintPressed && CanSprint() && !_isCrouching && !_isSliding)
            {
                _isSprinting = true;
            }
            else
            {
                _isSprinting = false;
            }
        }

        /// <summary>
        /// 处理蹲伏输入
        /// </summary>
        private void HandleCrouchInput()
        {
            if (!_isSliding)
            {
                _isCrouching = !_isCrouching;
                if (_isCrouching)
                {
                    _isSprinting = false;
                }
            }
        }

        /// <summary>
        /// 检查是否可以冲刺
        /// </summary>
        /// <returns>是否可以冲刺</returns>
        private bool CanSprint()
        {
            return _isGrounded && CurrentStamina > 10.0f && _moveDirection.LengthSquared > 0.001f;
        }

        /// <summary>
        /// 更新体力系统
        /// </summary>
        private void UpdateStaminaSystem()
        {
            if (_isSprinting && _moveDirection.LengthSquared > 0.001f)
            {
                // 消耗体力
                CurrentStamina -= SprintStaminaCost * Time.DeltaTime;
                CurrentStamina = Mathf.Max(CurrentStamina, 0.0f);
            }
            else
            {
                // 恢复体力
                CurrentStamina += StaminaRegenRate * Time.DeltaTime;
                CurrentStamina = Mathf.Min(CurrentStamina, MaxStamina);
            }
        }

        /// <summary>
        /// 开始滑行
        /// </summary>
        private void StartSlide()
        {
            if (_isSliding) return;
            
            _isSliding = true;
            _slideDuration = MaxSlideTime;
            _isCrouching = true;
            _isSprinting = false;
            
            // 触发相机震动效果
            if (Camera != null)
            {
                Camera.TriggerShake(0.05f, 0.3f);
            }
            
            ChangeState(CharacterState.Sliding);
        }

        /// <summary>
        /// 更新滑行系统
        /// </summary>
        private void UpdateSlideSystem()
        {
            if (!_isSliding) return;
            
            _slideDuration -= Time.DeltaTime;
            
            // 滑行结束条件
            if (_slideDuration <= 0.0f || _currentMoveSpeed <= 1.0f)
            {
                EndSlide();
                return;
            }
            
            // 应用滑行减速
            _currentMoveSpeed = Mathf.Max(_currentMoveSpeed - SlideDeceleration * Time.DeltaTime, 1.0f);
        }

        /// <summary>
        /// 结束滑行
        /// </summary>
        private void EndSlide()
        {
            _isSliding = false;
            _slideDuration = 0f;
            _isCrouching = false;
        }

        #endregion
    }
}
