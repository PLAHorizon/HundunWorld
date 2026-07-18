using System;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Character
{
    /// <summary>
    /// 自定义移动模式。对应 UE5 ENarrativeCustomMovementMode。
    /// </summary>
    public enum NarrativeCustomMovementMode : byte
    {
        None = 0,
        Climb = 1,
        Ragdoll = 2
    }

    /// <summary>
    /// 标准移动模式。对应 UE5 EMovementMode。
    /// </summary>
    public enum NarrativeMovementMode : byte
    {
        None = 0,
        Walking = 1,
        NavWalking = 2,
        Falling = 3,
        Swimming = 4,
        Flying = 5,
        Custom = 6
    }

    /// <summary>
    /// 掩护状态。对应 UE5 FCoverState。
    /// </summary>
    [Serializable]
    public class CoverState
    {
        /// <summary>掩护位置变换。</summary>
        public Transform CoverTransform = Transform.Identity;

        /// <summary>是否为矮掩护（需蹲伏）。</summary>
        public bool bIsShortCover = false;

        /// <summary>左侧是否开阔（可左探身）。</summary>
        public bool bIsLeftOpen = false;

        /// <summary>右侧是否开阔（可右探身）。</summary>
        public bool bIsRightOpen = false;

        /// <summary>玩家在掩护中的旋转。</summary>
        public Float3 CoverPlayerRotationEuler = Float3.Zero;

        public CoverState() { }
    }

    /// <summary>
    /// 角色移动组件。对应 UE5 UNarrativeCharacterMovement。
    /// 由于 Flax 无 UCharacterMovementComponent 等价物，本类作为 Script 持有移动状态与 Sprint/Cover 接口，
    /// 具体物理（攀爬、Ragdoll、Traversal）留待接入 Flax NavMesh / 物理系统后实现。
    /// 移除了 UE5 网络预测相关代码（FSavedMove、FNetworkPredictionData）。
    /// </summary>
    public class NarrativeCharacterMovement : Script
    {
        // ===== 移动速度 =====
        /// <summary>冲刺速度（单位/秒）。</summary>
        public float SprintSpeed = 700f;

        /// <summary>慢走速度。</summary>
        public float SlowWalkSpeed = 150f;

        /// <summary>常规行走速度。</summary>
        public float WalkSpeed = 500f;

        /// <summary>朝向移动方向的插值速度。</summary>
        public float OrientToMovementInterpSpeed = 8f;

        // ===== Sprint 状态 =====
        /// <summary>是否请求冲刺。</summary>
        public bool bWantsSprint = false;

        /// <summary>是否正在慢走。</summary>
        public bool bIsSlowWalking = false;

        /// <summary>当前局部输入向量（由玩家控制器写入）。</summary>
        public Float2 LocalInputVector = Float2.Zero;

        // ===== 自定义移动模式 =====
        /// <summary>当前自定义移动模式（Climb/Ragdoll）。</summary>
        public NarrativeCustomMovementMode CustomMovementMode = NarrativeCustomMovementMode.None;

        /// <summary>当前标准移动模式。</summary>
        public NarrativeMovementMode MovementMode = NarrativeMovementMode.Walking;

        // ===== 掩护状态 =====
        /// <summary>当前掩护状态。</summary>
        public CoverState CoverState = new CoverState();

        /// <summary>是否拥有掩护。</summary>
        public bool bHasCover = false;

        // ===== 掩护配置 =====
        public bool bCanWalkAroundCornersInCover = true;
        public bool bOrientCrouchToCoverHeight = true;
        public float LeanFromCoverDist = 30f;
        public float PlayerOffsetFromCover = 20f;
        public float NextCoverTraceSpacing = 60f;
        public float FindCoverForwardSearchDist = 100f;
        public float NextCoverTraceDepth = 60f;
        public float CoverInterpSpeed = 10f;

        // ===== Traversal 配置（攀爬/跨越） =====
        public bool bCheckLedgeWhilstFalling = false;
        public float TraversalTraceForwardDistance = 100f;
        public float TraversalTraceForwardDistanceSwimming = 100f;
        public float ClimbTraceJumpVerticalDistance = 140f;
        public float ClimbTraceJumpHorizontalDistance = 100f;
        public float TraversalTraceForwardDistanceFalling = 100f;
        public float TraversalTraceForwardDistanceClimbCheck = 100f;
        public float TraversalTraceHeightMax = 325f;
        public float TraversalTraceHeightMin = 50f;
        public float TraversalTraceCapsuleHalfHeightScale = 0.8f;
        public float TraversalTraceCapsuleHalfHeightScaleFalling = 0.8f;

        // ===== Ragdoll =====
        public float EnterRagdollFallZThreshold = 1500f;
        public float EnterRagdollFallZImpactSlopeThreshold = 1000f;
        public float EnterRagdollFallZImpactGroundThreshold = 1500f;

        // ===== 事件 =====
        /// <summary>进入掩护时触发。</summary>
        public event Action OnEnterCover;
        /// <summary>离开掩护时触发。</summary>
        public event Action OnExitCover;

        // ===== 内部 =====
        private Actor _characterOwner;

        /// <summary>所属角色（简化引用，替代 UE5 NarrativeCharacterOwner）。</summary>
        public Actor CharacterOwner => _characterOwner ?? (_characterOwner = Actor);

        // ===== Sprint API =====

        /// <summary>请求开始冲刺。</summary>
        public void StartSprinting() { bWantsSprint = true; bIsSlowWalking = false; }

        /// <summary>请求停止冲刺。</summary>
        public void StopSprinting() { bWantsSprint = false; }

        /// <summary>是否正在冲刺。</summary>
        public bool IsSprinting() => bWantsSprint && IsMovingForward(10f);

        /// <summary>是否正在慢走。</summary>
        public bool IsSlowWalking() => bIsSlowWalking;

        /// <summary>
        /// 是否在向前移动。判定当前局部输入向量与角色前向夹角是否在容差内。
        /// </summary>
        /// <param name="forwardAngleTolerance">前向夹角容差（度）。</param>
        public bool IsMovingForward(float forwardAngleTolerance = 10f)
        {
            if (LocalInputVector.LengthSquared < 0.01f) return false;
            // 简化版：直接检查 LocalInputVector.Y > 0（向前）
            // 完整实现需要将 LocalInputVector 转换为世界空间并与 Forward 比较
            return LocalInputVector.Y > 0.1f;
        }

        /// <summary>获取最大速度。</summary>
        public float GetMaxSpeed()
        {
            if (IsSprinting()) return SprintSpeed;
            if (bIsSlowWalking) return SlowWalkSpeed;
            return WalkSpeed;
        }

        // ===== 自定义移动模式 API =====

        public bool IsClimbing() => CustomMovementMode == NarrativeCustomMovementMode.Climb;
        public bool IsRagdoll() => CustomMovementMode == NarrativeCustomMovementMode.Ragdoll;

        public bool IsCustomMovementMode(NarrativeCustomMovementMode mode) => CustomMovementMode == mode;
        public bool IsMovementMode(NarrativeMovementMode mode) => MovementMode == mode;

        /// <summary>设置移动模式。</summary>
        public void SetMovementMode(NarrativeMovementMode newMode, byte newCustomMode = 0)
        {
            var prevMode = MovementMode;
            MovementMode = newMode;
            if (newMode == NarrativeMovementMode.Custom)
            {
                CustomMovementMode = (NarrativeCustomMovementMode)newCustomMode;
                if (CustomMovementMode == NarrativeCustomMovementMode.Climb) OnEnterClimbing();
                else if (CustomMovementMode == NarrativeCustomMovementMode.Ragdoll) OnEnterRagdoll();
            }
            else
            {
                if (CustomMovementMode == NarrativeCustomMovementMode.Climb) OnExitClimbing();
                else if (CustomMovementMode == NarrativeCustomMovementMode.Ragdoll) OnExitRagdoll();
                CustomMovementMode = NarrativeCustomMovementMode.None;
            }
            OnMovementModeChanged(prevMode, newMode);
        }

        /// <summary>移动模式改变时调用。</summary>
        protected virtual void OnMovementModeChanged(NarrativeMovementMode prevMode, NarrativeMovementMode newMode) { }

        protected virtual void OnEnterClimbing() { }
        protected virtual void OnExitClimbing() { }
        protected virtual void OnEnterRagdoll() { }
        protected virtual void OnExitRagdoll() { }
        protected virtual void OnEnterSwimming() { }
        protected virtual void OnExitSwimming() { }

        // ===== Cover API =====

        /// <summary>尝试进入掩护。</summary>
        public bool TryEnterCover()
        {
            // TODO [待源码]: 获取 UE5 源 NarrativeCharacterMovement.cpp 的 TryEnterCover forward 胶囊扫描实现后补全
            // 简化版：直接返回 false
            return false;
        }

        /// <summary>使掩护失效。</summary>
        public void InvalidateCover()
        {
            if (bHasCover)
            {
                bHasCover = false;
                CoverState = new CoverState();
                OnExitCover?.Invoke();
            }
        }

        /// <summary>是否有掩护。</summary>
        public bool HasCover() => bHasCover;

        /// <summary>掩护中向左/右移动。</summary>
        public bool CoverMove(Float2 localInput)
        {
            if (!bHasCover) return false;
            // TODO [待源码]: 获取 UE5 源 NarrativeCharacterMovement.cpp 的 CoverMove 沿掩护移动 + 边角检测实现后补全
            return true;
        }

        /// <summary>是否正在掩护中向左平移。</summary>
        public bool IsCoverStrafingLeft() => bHasCover && LocalInputVector.X < -0.1f;

        /// <summary>获取掩护位置和旋转（含探身偏移）。</summary>
        public void CoverToPlayer(out Vector3 location, out Quaternion rotation, bool bIncludeLeaning = true)
        {
            location = CoverState.CoverTransform.Translation;
            rotation = CoverState.CoverTransform.Orientation;

            // 应用玩家距掩护的偏移（沿掩护法线后退）
            Vector3 coverForward = CoverState.CoverTransform.Forward;
            location += coverForward * PlayerOffsetFromCover;

            // 应用探身偏移（沿掩护切线左右平移）
            if (bIncludeLeaning && bHasCover)
            {
                Vector3 coverRight = CoverState.CoverTransform.Right;
                if (IsCoverStrafingLeft())
                {
                    location -= coverRight * LeanFromCoverDist;
                }
                else if (LocalInputVector.X > 0.1f)
                {
                    location += coverRight * LeanFromCoverDist;
                }
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            _characterOwner = Actor;
        }

        public override void OnDisable()
        {
            InvalidateCover();
            _characterOwner = null;
            base.OnDisable();
        }
    }
}
