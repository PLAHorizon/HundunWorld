using System;
using Arch.Core;
using FlaxEngine;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.Message.Sync.Components;
using HundunWorld.Game.Character;
// 消除 PredictedTransformComponent 命名空间歧义：
// ECS.Arch.Components.PredictedTransformComponent 是客户端本地预测组件（LocalSimulationSystem 操作），
// Message.Sync.Components.PredictedTransformComponent 是网络序列化版本。
// 本系统从 ECS 读取本地预测值应用到 Actor，应使用 ECS.Arch.Components 版本。
using PredictedTransformComponent = Horizon.Game.ECS.Arch.Components.PredictedTransformComponent;

namespace HundunWorld.Game
{
    /// <summary>
    /// 本地玩家 Actor 同步系统：每帧从 ECS 中本地玩家实体的 PredictedTransformComponent
    /// 读取位置/朝向，应用到 Flax Actor；并从 MovementStateAuthComponent 读取移动模式驱动动画。
    /// </summary>
    /// <remarks>
    /// 设计原则：PredictedTransformComponent 是本地玩家的单一事实源。
    /// PlayerController 不再直接修改 Actor.Position/Orientation，改由本系统从 ECS 同步。
    /// 这样客户端显示位置 = 本地预测位置 = 服务端校验输入（PredictedEndX/Y/Z），
    /// 三者一致，避免双轨脱节导致服务端持续发 Correction。
    ///
    /// 挂载位置：由 HundunWorldGame.CreateLocalPlayerActor 末尾通过 AddScript 挂载到 LocalPlayerActor。
    /// 执行顺序：Flax 中同一 Actor 上的 Script 按 AddScript 顺序执行 OnUpdate，
    /// LocalPlayerActorSyncSystem 在 PlayerController 之后挂载，OnUpdate 在其后执行；
    /// 而 ECSUpdateDriver 挂在不同 Actor 上，按场景树顺序先于 LocalPlayerActor 执行，
    /// 保证本系统读取的 PredictedTransformComponent 是本帧最新值。
    /// </remarks>
    public class LocalPlayerActorSyncSystem : Script
    {
        /// <summary>单例引用，供外部（如 PlayerController 公共 API）查询本地玩家 ECS 状态。</summary>
        public static LocalPlayerActorSyncSystem? Instance { get; private set; }

        /// <summary>Arch World 引用（在 OnStart 中获取，失败时每帧重试）。</summary>
        private World? _archWorld;

        /// <summary>本地玩家 ECS 实体缓存。</summary>
        private Entity _localPlayerEntity;
        private bool _localPlayerEntityFound;

        /// <summary>本地玩家 AnimatedModel 缓存（用于设置动画参数，回退方案）。</summary>
        private AnimatedModel? _animatedModel;

        /// <summary>角色动画控制器引用。</summary>
        private CharacterAnimationController _animationController;

        /// <summary>动画参数是否已初始化（资源未加载时延迟初始化，回退方案用）。</summary>
        private bool _animationParamsInitialized;

        /// <summary>上一帧的 MovementMode（变化检测，避免每帧设置参数）。</summary>
        private MovementMode _lastMovementMode = MovementMode.Walk;

        /// <summary>上一帧的 IsGrounded 状态（变化检测）。</summary>
        private bool _lastIsGrounded = true;

        /// <summary>是否已输出首帧诊断日志。</summary>
        private bool _firstFrameDiag = true;

        public override void OnStart()
        {
            Instance = this;
            _archWorld = HundunWorldGame.Instance?.ArchWorld;

            if (_archWorld == null)
            {
                Debug.LogWarning("[LocalPlayerActorSyncSystem] Arch World 未就绪，将在首次 Update 时重试");
            }

            // 查找 AnimatedModel 子 Actor（角色 Prefab 层级中）
            _animatedModel = FindAnimatedModel(Actor);

            // 查找角色动画控制器
            _animationController = Actor.GetScript<CharacterAnimationController>();

            Debug.Log($"[LocalPlayerActorSyncSystem] 初始化完成。AnimatedModel={_animatedModel != null}, AnimationController={_animationController != null}");
        }

        public override void OnUpdate()
        {
            // 确保 Arch World 已获取
            if (_archWorld == null)
            {
                _archWorld = HundunWorldGame.Instance?.ArchWorld;
                if (_archWorld == null) return;
            }

            // 查找本地玩家 ECS 实体
            if (!TryFindLocalPlayerEntity()) return;

            if (!_archWorld.IsAlive(_localPlayerEntity))
            {
                _localPlayerEntityFound = false;
                return;
            }

            // 资源未就绪时每帧尝试初始化动画参数
            if (!_animationParamsInitialized)
            {
                TryInitializeAnimationParameters();
            }

            try
            {
                // 读取预测位置/朝向（ECS Z-up：X=左右, Y=前后, Z=上下）
                ref var pred = ref _archWorld.Get<PredictedTransformComponent>(_localPlayerEntity);

                // 应用位置：ECS Z-up → Flax Y-up
                // Flax.X = ECS.X（左右）
                // Flax.Y = ECS.Z（上下）
                // Flax.Z = ECS.Y（前后）
                var targetPos = new Vector3(pred.X, pred.Z, pred.Y);
                Actor.Position = targetPos;

                // 应用朝向：Yaw 弧度 → 度
                float yawDeg = pred.Yaw * Mathf.RadiansToDegrees;
                Actor.Orientation = Quaternion.Euler(0, yawDeg, 0);

                // 首帧诊断日志
                if (_firstFrameDiag)
                {
                    _firstFrameDiag = false;
                    Debug.Log($"[LocalPlayerActorSyncSystem] 首帧同步: Pos=({pred.X:F2},{pred.Y:F2},{pred.Z:F2}), Yaw={yawDeg:F1}deg, ActorPos={Actor.Position}");
                }

                // 驱动动画状态
                ApplyAnimationState();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalPlayerActorSyncSystem] 读取 PredictedTransformComponent 失败: {ex.Message}");
                _localPlayerEntityFound = false;
            }
        }

        public override void OnDestroy()
        {
            Instance = null;
            Debug.Log("[LocalPlayerActorSyncSystem] 已销毁");
        }

        /// <summary>
        /// 查找 Arch World 中本地玩家实体（IsLocalPlayer=true）。
        /// 与 PlayerController.TryFindLocalPlayerEntity 同模式，但独立维护缓存避免相互干扰。
        /// </summary>
        private bool TryFindLocalPlayerEntity()
        {
            if (_localPlayerEntityFound) return true;

            var query = new QueryDescription()
                .WithAll<NetworkIdentityComponent, PlayerInputComponent, PredictedTransformComponent>();

            _archWorld!.Query(in query, (Entity entity, ref NetworkIdentityComponent netId) =>
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
        /// 安全检查动画资源是否已加载（回退方案用）。仅在 AnimatedModel 的 SkinnedModel 与 AnimationGraph 都已加载时返回 true。
        /// </summary>
        private bool TryInitializeAnimationParameters()
        {
            if (_animationParamsInitialized) return true;

            if (_animatedModel == null)
            {
                _animatedModel = FindAnimatedModel(Actor);
                if (_animatedModel == null) return false;
            }

            if (_animatedModel.SkinnedModel == null || !_animatedModel.SkinnedModel.IsLoaded
                || _animatedModel.AnimationGraph == null || !_animatedModel.AnimationGraph.IsLoaded)
            {
                return false;
            }

            _animationParamsInitialized = true;
            Debug.Log("[LocalPlayerActorSyncSystem] 动画资源检查完成");
            return true;
        }

        /// <summary>
        /// 从 MovementStateAuthComponent 读取移动模式，通过 CharacterAnimationController 设置动画状态。
        /// 支持 Idle/Walk/Run/Crouch/Jump/Fall/Death 等状态。
        /// </summary>
        private void ApplyAnimationState()
        {
            if (!_archWorld!.Has<MovementStateAuthComponent>(_localPlayerEntity)) return;

            ref var movement = ref _archWorld.Get<MovementStateAuthComponent>(_localPlayerEntity);
            var mode = movement.MovementMode;

            // 使用 CharacterAnimationController 设置动画状态
            if (_animationController != null)
            {
                // 设置移动速度参数（用于动画混合）- 每帧更新，不受变化检测限制
                if (_archWorld.Has<PlayerInputComponent>(_localPlayerEntity))
                {
                    ref var input = ref _archWorld.Get<PlayerInputComponent>(_localPlayerEntity);
                    _animationController.SetMoveSpeed(input.MaxSpeed);
                }

                // 变化检测：仅在模式变化或落地状态变化时更新动画状态参数
                if (mode != _lastMovementMode || movement.IsGrounded != _lastIsGrounded)
                {
                    _lastMovementMode = mode;
                    _lastIsGrounded = movement.IsGrounded;

                    var animState = mode switch
                    {
                        MovementMode.Walk => CharacterAnimationState.Walk,
                        MovementMode.Run => CharacterAnimationState.Run,
                        MovementMode.Crouch => CharacterAnimationState.Crouch,
                        MovementMode.Jump => CharacterAnimationState.Jump,
                        MovementMode.Fall => CharacterAnimationState.Fall,
                        _ => CharacterAnimationState.Idle
                    };

                    _animationController.SetAnimationState(animState);
                }
            }
            else if (_animatedModel != null)
            {
                // 回退：直接设置 IsWalking 参数
                bool isWalking = mode == MovementMode.Walk
                              || mode == MovementMode.Run
                              || mode == MovementMode.Crouch;

                var isWalkingParam = _animatedModel.GetParameter("IsWalking");
                if (isWalkingParam != null)
                {
                    isWalkingParam.Value = isWalking;
                }
            }
        }

        /// <summary>递归查找 Actor 层级中的 AnimatedModel（与 FlaxActorSyncSystem.FindAnimatedModel 同模式）。</summary>
        private static AnimatedModel? FindAnimatedModel(Actor actor)
        {
            if (actor is AnimatedModel am) return am;
            for (int i = 0; i < actor.ChildrenCount; i++)
            {
                var child = actor.GetChild(i);
                var result = FindAnimatedModel(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
