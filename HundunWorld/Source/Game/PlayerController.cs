using System;
using FlaxEngine;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
// 消除 PredictedTransformComponent 命名空间歧义：
// ECS.Arch.Components.PredictedTransformComponent 是客户端本地预测组件（LocalSimulationSystem 操作），
// Message.Sync.Components.PredictedTransformComponent 是网络序列化版本。
// 客户端本地预测/读写应使用 ECS.Arch.Components 版本。
using PredictedTransformComponent = Horizon.Game.ECS.Arch.Components.PredictedTransformComponent;
using CharacterState = Horizon.Game.Message.Enums.CharacterState;
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
        private global::Game.Character.Movement.QinggongSystem _qinggongSystem;

        /// <summary>
        /// 目标选择系统引用
        /// </summary>
        private Combat.TargetSelectionSystem _targetSelectionSystem;

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

        private long _clientTick = 0;
        private float _inputSendAccumulator = 0f;
        private const float InputSendInterval = 1f / 60f;

        /// <summary>Arch ECS 中本地玩家实体的缓存引用。</summary>
        private Entity _localPlayerEntity;
        private bool _localPlayerEntityFound = false;

        /// <summary>
        /// [边沿触发] 上一帧跳跃键是否按下，用于计算 JumpPressedThisFrame 边沿。
        /// 由 WriteInputToEcs 维护：当前帧 jumpPressed 且 _prevJumpPressed=false 时
        /// 写入 input.JumpPressedThisFrame=true，否则 false，确保持续按住空格时
        /// 仅在按下边沿的那一帧触发 jumpCount++，避免三段跳在 50ms 内被消耗完。
        /// </summary>
        private bool _prevJumpPressed = false;

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
        /// 动画参数：是否行走（IsWalking）。
        /// 由于打包构建中角色资源可能尚未加载，延迟初始化直到 AnimatedModel 资源就绪。
        /// </summary>
        private AnimGraphParameter _isWalkingParam;

        /// <summary>
        /// 动画参数是否已完成初始化。
        /// </summary>
        private bool _animationParamsInitialized;

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
            _qinggongSystem = Actor.Parent?.GetScript<global::Game.Character.Movement.QinggongSystem>();

            // 初始化目标选择系统
            _targetSelectionSystem = Scene.FindScript<Combat.TargetSelectionSystem>();
            if (_targetSelectionSystem == null)
            {
                // 如果场景中没有，则添加一个
                var targetSystemActor = Scene.FindActor("TargetSelectionSystem");
                if (targetSystemActor == null)
                {
                    targetSystemActor = new EmptyActor { Name = "TargetSelectionSystem" };
                    Level.SpawnActor(targetSystemActor);
                }
                _targetSelectionSystem = targetSystemActor.AddScript<Combat.TargetSelectionSystem>();
                _targetSelectionSystem.MaxSelectDistance = 50f;
                _targetSelectionSystem.ShowSelectionBox = true;
                Debug.Log("[玩家控制器] 已创建目标选择系统");
            }

            // 初始化角色控制器
            CurrentState = CharacterState.Idle;
            _previousState = CharacterState.Idle;
            _stateTime = 0f;

            // 初始化移动缓冲
            _movementBuffer = new Queue<Vector3>();

            // 尝试初始化动画参数；若资源尚未加载，将在 OnUpdate 中重试
            TryInitializeAnimationParameters();
        }

        /// <summary>
        /// 安全初始化动画参数。仅在 AnimatedModel 的 SkinnedModel 与 AnimationGraph 都已加载时执行，
        /// 避免在打包构建中资源未就绪时调用 GetParameter 触发引擎断言。
        /// </summary>
        /// <returns>是否成功初始化</returns>
        private bool TryInitializeAnimationParameters()
        {
            if (_animationParamsInitialized)
                return true;

            var animatedModel = Actor.GetChild<AnimatedModel>();
            if (animatedModel == null)
            {
                Debug.LogWarning("[PlayerController] 未找到 AnimatedModel 子 Actor，动画参数初始化跳过");
                return false;
            }

            // 安全校验：SkinnedModel 与 AnimationGraph 必须都已加载
            if (animatedModel.SkinnedModel == null || !animatedModel.SkinnedModel.IsLoaded
                || animatedModel.AnimationGraph == null || !animatedModel.AnimationGraph.IsLoaded)
            {
                Debug.Log("[PlayerController] AnimatedModel 资源尚未加载，延迟初始化动画参数");
                return false;
            }

            try
            {
                _isWalkingParam = animatedModel.GetParameter("IsWalking");
                if (_isWalkingParam != null)
                {
                    _isWalkingParam.Value = _isRunning;
                }
                _animationParamsInitialized = true;
                Debug.Log("[PlayerController] 动画参数初始化完成");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerController] 初始化动画参数失败，将在下一帧重试: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 安全设置 IsWalking 参数值。
        /// </summary>
        private void SetIsWalking(bool value)
        {
            if (_isWalkingParam != null)
            {
                _isWalkingParam.Value = value;
            }
        }

        public override void OnUpdate()
        {
            // 如果输入被禁用，跳过所有输入处理
            if (!EnableInput)
            {
                return;
            }

            // 资源未就绪时，每帧尝试初始化动画参数
            if (!_animationParamsInitialized)
            {
                TryInitializeAnimationParameters();
            }

            // 更新状态时间
            _stateTime += Time.DeltaTime;

            // 更新体力系统
            UpdateStaminaSystem();

            // 更新滑行系统
            UpdateSlideSystem();

            // [重构] 本地物理模拟已删除：位置/朝向/跳跃全部由 ECS LocalSimulationSystem 计算，
            // 由 LocalPlayerActorSyncSystem 应用到 Actor。本控制器退化为"输入采集器 + ECS 写入器"。
            // 原 HandleCharacterMovement/HandleGroundClick/UpdateCharacterState/UpdateMovementBuffer
            // 直接修改 Actor.Position 与 Actor.Orientation，与 ECS 双轨脱节。
            SetIsWalking(false);
            _moveDirection = GetInputDirection();
            HandleAuxiliaryInputs();
            UpdateGroundedFromEcs();

            // 将输入写入 ECS 管线（供 InputSendSystem 打包发送）
            bool ecsInputSent = WriteInputToEcs();

            // 备用路径：仅当 ECS 管线不可用时直接发送
            if (!ecsInputSent)
            {
                _inputSendAccumulator += Time.DeltaTime;
                if (_inputSendAccumulator >= InputSendInterval)
                {
                    _inputSendAccumulator -= InputSendInterval;
                    BuildAndSendInputPacket();
                }
            }
            else
            {
                _inputSendAccumulator = 0f;
            }
        }

        /// <summary>
        /// [重构] 从 ECS 读取服务端权威的 IsGrounded 状态，覆盖本地 _isGrounded 字段。
        /// 原 CheckGroundStatus 使用 Actor.Position.Y <= GetGroundHeight() 检测地面，
        /// 但本地物理模拟移除后 Actor.Position 不再由本控制器更新，应改由服务端权威判定。
        /// </summary>
        private void UpdateGroundedFromEcs()
        {
            var archWorld = HundunWorldGame.Instance?.ArchWorld;
            if (archWorld == null || !TryFindLocalPlayerEntity())
            {
                return;
            }

            try
            {
                if (archWorld.Has<MovementStateAuthComponent>(_localPlayerEntity))
                {
                    ref var movement = ref archWorld.Get<MovementStateAuthComponent>(_localPlayerEntity);
                    _isGrounded = movement.IsGrounded;
                }
            }
            catch
            {
                _localPlayerEntityFound = false;
            }
        }

        #endregion

        #region 输入同步包发送

        /// <summary>
        /// 查找 Arch World 中本地玩家实体（IsLocalPlayer=true）。
        /// </summary>
        private bool TryFindLocalPlayerEntity()
        {
            if (_localPlayerEntityFound) return true;

            var archWorld = HundunWorldGame.Instance?.ArchWorld;
            if (archWorld == null) return false;

            var query = new QueryDescription()
                .WithAll<NetworkIdentityComponent, PlayerInputComponent, PredictedTransformComponent>();

            archWorld.Query(in query, (Entity entity, ref NetworkIdentityComponent netId) =>
            {
                if (netId.IsLocalPlayer)
                {
                    _localPlayerEntity = entity;
                    _localPlayerEntityFound = true;
                }
            });

            return _localPlayerEntityFound;
        }

        /// <summary>
        /// 将当前帧的输入数据写入 Arch World 中本地玩家实体的 PlayerInputComponent，
        /// 供 InputSendSystem 在 NetworkSend 阶段打包发送到服务端。
        /// </summary>
        private bool WriteInputToEcs()
        {
            if (!TryFindLocalPlayerEntity()) return false;

            var archWorld = HundunWorldGame.Instance?.ArchWorld;
            if (archWorld == null || !archWorld.IsAlive(_localPlayerEntity))
            {
                _localPlayerEntityFound = false;
                return false;
            }

            try
            {
                ref var input = ref archWorld.Get<PlayerInputComponent>(_localPlayerEntity);
                input.MoveX = _moveDirection.X;
                input.MoveY = _moveDirection.Z;

                // 获取视角朝向（含 Camera 缺失降级逻辑）
                TryGetLookYawPitch(out float lookYaw, out float lookPitch);
                input.LookYaw = lookYaw;
                input.LookPitch = lookPitch;

                byte inputBits = _inputManager != null ? _inputManager.GetCurrentInputBits() : (byte)0;
                if (_qinggongSystem != null)
                {
                    inputBits |= (byte)_qinggongSystem.GetQinggongInputBits();
                }

                // [边沿触发修复] 计算跳跃按下边沿并改写 InputBits bit0。
                // 服务端 ZoneShardGrain 通过 InputBits bit0 推断跳跃（(inputBits & 0x1) != 0 → jumpCount++），
                // 若客户端持续按住空格，每帧 bit0=1 会导致三段跳在 50ms 内被消耗完。
                // 修复策略：客户端先行做边沿触发——仅在 jumpPressedRaw && !_prevJumpPressed 的那一帧
                //   保留 bit0=1 并设置 JumpPressedThisFrame=true；下一帧即使持续按住也清除 bit0=0。
                // LocalSimulationSystem 已改用 input.JumpPressedThisFrame 推断跳跃，
                // 但 InputBits bit0 仍需保持边沿语义以兼容服务端原逻辑（服务端无需改动）。
                bool jumpPressedRaw = (inputBits & 0x1) != 0;
                bool jumpPressedThisFrame = jumpPressedRaw && !_prevJumpPressed;
                if (!jumpPressedThisFrame)
                {
                    inputBits &= 0xFE; // 清除 bit0（跳跃位）
                }
                input.InputBits = inputBits;
                input.JumpPressedThisFrame = jumpPressedThisFrame;
                _prevJumpPressed = jumpPressedRaw;

                // [MoveSpeed 链路修复] 计算当帧目标最大水平速度并写入 PlayerInputComponent.MaxSpeed。
                // 原实现只写归一化方向 MoveX/MoveY，LocalSimulationSystem 与服务端 MovementValidator
                // 都固定用 MovementFormula.DefaultMaxSpeed=6 m/s 推进，导致 PlayerController.MoveSpeed
                // （含 Run/Sprint/Crouch 倍数）完全不生效。
                // 这里复用 CalculateTargetSpeed 的状态机逻辑（Run/Sprint/Crouch/Slide/AirControl），
                // 但跳过其内部 Lerp 平滑（_currentVelocity），直接输出目标速度，
                // 让 ECS 端按精确速度推进，避免客户端预测与服务端权威因平滑差异产生 correction。
                input.MaxSpeed = ComputeCurrentMaxSpeed();

                // [修复] ClientTick 由 LocalSimulationSystem（FixedUpdate）统一递增，
                // 避免 OnUpdate 渲染帧率与 FixedUpdate 60Hz 不一致导致 tick 漂移。
                // 此处仅写入输入数据，不修改 predicted.ClientTick。
            }
            catch
            {
                _localPlayerEntityFound = false;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 尝试获取视角朝向（Yaw/Pitch，弧度）。
        /// 优先使用已配置的 <see cref="Camera"/>；若未配置则尝试自动查找主相机并缓存；
        /// 若仍找不到则降级使用 <see cref="Actor.Orientation"/> 的 Yaw，避免 LookYaw 永远为 0
        /// 导致服务端 entity.Yaw 不更新、远程角色朝向不变的问题。
        /// </summary>
        /// <param name="yaw">输出的水平视角（弧度）</param>
        /// <param name="pitch">输出的俯仰视角（弧度）</param>
        private void TryGetLookYawPitch(out float yaw, out float pitch)
        {
            if (Camera != null)
            {
                yaw = Camera.Yaw * Mathf.DegreesToRadians;
                pitch = Camera.Pitch * Mathf.DegreesToRadians;
                return;
            }

            // Camera 未配置，尝试自动查找主相机
            var mainCamera = FlaxEngine.Camera.MainCamera?.GetScript<ThirdPersonCamera>();
            if (mainCamera != null)
            {
                Camera = mainCamera; // 缓存以避免下次再查找
                yaw = Camera.Yaw * Mathf.DegreesToRadians;
                pitch = Camera.Pitch * Mathf.DegreesToRadians;
                return;
            }

            // 降级：使用 Actor.Orientation 的 Yaw 作为 LookYaw 来源
            FlaxEngine.Debug.LogWarning("[PlayerController] Camera 未配置，使用 Actor.Orientation.Yaw 作为降级");
            yaw = Actor.Orientation.EulerAngles.Y * Mathf.DegreesToRadians;
            pitch = 0f;
        }

        private void BuildAndSendInputPacket()
        {
            _clientTick++;

            byte inputBits = _inputManager != null ? _inputManager.GetCurrentInputBits() : (byte)0;
            if (_qinggongSystem != null)
            {
                inputBits |= (byte)_qinggongSystem.GetQinggongInputBits();
            }

            float moveX = _moveDirection.X;
            float moveY = _moveDirection.Z;

            // 获取视角朝向（含 Camera 缺失降级逻辑）
            TryGetLookYawPitch(out float lookYaw, out float lookPitch);

            // [修复] 设置 CharacterId：服务端 SyncPacketHandler 为单例，不能在实例字段中缓存 characterId，
            // 要求客户端在每个 InputPacket 中显式携带，否则 characterId==0 时服务端会拒绝该输入包。
            var characterId = HundunWorldGame.Instance?.PlayerId ?? 0;

            // 修复（角色被吸附回原点）：备用路径必须携带 PredictedEndX/Y/Z 和 MaxSpeed。
            // 原实现未设置这些字段（默认 0），导致服务端 MovementValidator 的 clientEnd=(0,0,0)，
            // 与权威回放结果产生巨大 drift，触发 Correction 将角色拉回原点。
            // 从 ECS PredictedTransformComponent 读取当前预测位置（ECS Z-up）。
            float predictedX = 0f, predictedY = 0f, predictedZ = 0f;
            var archWorld = HundunWorldGame.Instance?.ArchWorld;
            if (archWorld != null && TryFindLocalPlayerEntity() && archWorld.IsAlive(_localPlayerEntity))
            {
                try
                {
                    ref var pred = ref archWorld.Get<Horizon.Game.ECS.Arch.Components.PredictedTransformComponent>(_localPlayerEntity);
                    predictedX = pred.X;
                    predictedY = pred.Y;
                    predictedZ = pred.Z;
                }
                catch { /* 实体已销毁，使用默认值 */ }
            }

            var inputPacket = new InputPacket
            {
                ClientTick = _clientTick,
                InputBits = inputBits,
                LookYaw = lookYaw,
                LookPitch = lookPitch,
                MoveX = moveX,
                MoveY = moveY,
                CharacterId = characterId,
                PredictedEndX = predictedX,
                PredictedEndY = predictedY,
                PredictedEndZ = predictedZ,
                MaxSpeed = ComputeCurrentMaxSpeed(),
            };

            SyncPacketCodec.Encode(inputPacket, out var frame, out var frameLength);
            try
            {
                var payload = new byte[frameLength];
                System.Buffer.BlockCopy(frame, 0, payload, 0, frameLength);

                var syncFrame = new SyncFrameMessage
                {
                    Frame = payload,
                    PacketKind = (byte)inputPacket.Kind,
                    ProtocolVersion = inputPacket.ProtocolVersion,
                };

                var networkManager = HundunWorldGame.Instance?.NetworkManager;
                if (networkManager != null && networkManager.CanSendMessage() && networkManager.IsSyncHandshakeComplete)
                {
                    _ = networkManager.SendAsync(syncFrame);
                }
            }
            finally
            {
                SyncPacketCodec.ReturnFrame(frame);
            }
        }

        #endregion

        #region 角色移动处理

        /// <summary>
        /// [重构] 原本地物理模拟入口已禁用。
        /// 位置/朝向/跳跃全部由 ECS LocalSimulationSystem 计算并通过 LocalPlayerActorSyncSystem 应用到 Actor。
        /// 此方法保留为空操作以兼容潜在的反射调用，不再修改 Actor.Position/Orientation。
        /// </summary>
        private void HandleCharacterMovement()
        {
            // 空操作：本地物理模拟已迁移至 ECS LocalSimulationSystem
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

                if (Camera != null && Camera.ShouldUseCameraRelativeMovement())
                {
                    Vector2 cameraInput = new Vector2(inputDirection.X, inputDirection.Z);
                    inputDirection = Camera.GetCameraRelativeMoveDirection(cameraInput);
                }
                else if (Camera != null)
                {
                    Vector3 cameraRight = Camera.Actor.Transform.Right;
                    Vector3 cameraForward = Camera.Actor.Transform.Forward;
                    cameraRight.Y = 0;
                    cameraForward.Y = 0;
                    cameraRight.Normalize();
                    cameraForward.Normalize();

                    inputDirection = inputDirection.X * cameraRight + inputDirection.Z * cameraForward;
                }

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

            return ComputeCurrentMaxSpeed();
        }

        /// <summary>
        /// [MoveSpeed 链路修复] 计算当前状态下的目标最大水平移动速度（米/秒），
        /// 不依赖 inputDirection（即使无输入也返回当前状态的速度上限）。
        /// 供 PlayerInputComponent.MaxSpeed 使用，由 LocalSimulationSystem 与服务端 MovementValidator
        /// 共同调用 MovementFormula.Step 时传入，保证两端按同一速度推进。
        /// 状态机逻辑与 CalculateTargetSpeed 一致，但跳过"无输入返回 0"分支：
        /// 静止时仍返回速度上限，由 MoveX/MoveY=0 自然保证不产生位移。
        /// </summary>
        private float ComputeCurrentMaxSpeed()
        {
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
        /// [重构] 原本地重力/跳跃模拟已禁用。
        /// 跳跃由 PlayerInputComponent.JumpPressedThisFrame 边沿触发，服务端 ZoneShardGrain 通过 InputBits bit0
        /// 推断并在 MovementFormula.Step 中应用 jumpImpulse；垂直速度由服务端权威计算并下发
        /// MovementStateAuthComponent.VelocityXZ。本方法保留为空操作以兼容旧调用点。
        /// </summary>
        private void HandleVerticalMovement()
        {
            // 空操作：垂直速度与跳跃已迁移至服务端权威模拟
        }

        /// <summary>
        /// [重构] 原本地移动应用已禁用。
        /// Actor.Position/Orientation 由 LocalPlayerActorSyncSystem 从 ECS PredictedTransformComponent
        /// 应用，本方法保留为空操作以兼容旧调用点（不再修改 Actor.Position 或调用 CheckGroundStatus）。
        /// </summary>
        private void ApplyMovement()
        {
            // 空操作：Actor 变换已由 LocalPlayerActorSyncSystem 从 ECS 应用
        }

        /// <summary>
        /// [重构] 原本地朝向更新已禁用。
        /// Actor.Orientation 由 LocalPlayerActorSyncSystem 从 ECS PredictedTransformComponent.Yaw
        /// 应用（yaw 弧度 → 度 → Quaternion.Euler(0, yawDeg, 0)）。本方法保留为空操作以兼容旧调用点。
        /// RotationSmoothing 属性仍保留以兼容 SystemOptimizer 等外部调用方。
        /// </summary>
        /// <param name="inputDirection">输入方向（已忽略）</param>
        private void UpdateCharacterRotation(Vector3 inputDirection)
        {
            // 空操作：朝向已由 LocalPlayerActorSyncSystem 从 ECS PredictedTransformComponent.Yaw 应用
        }

        /// <summary>
        /// [重构] 点击移动已禁用。
        /// 原实现直接修改 Actor.Position/Orientation，与 ECS 单一事实源原则冲突。
        /// 后续如需启用，应改造为生成虚拟输入方向写入 PlayerInputComponent.MoveX/MoveY，
        /// 让 ECS LocalSimulationSystem 统一计算移动。本方法保留为空操作以兼容旧调用点。
        /// </summary>
        private void HandleClickToMove()
        {
            // 空操作：点击移动功能已禁用，等待后续基于 ECS 输入采集的重新实现
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
