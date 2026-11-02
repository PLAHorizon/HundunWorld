using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI.Cover;
using NarrativePro.AI.Navigation;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;
using NarrativePro.World;

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
        /// <summary>
        /// 落地砸趴的下落速度门槛。对应 UE5 <c>EnterRagdollFallZImpactGroundThreshold</c>，
        /// 上游默认值是**负**数（NarrativeCharacterMovement.cpp:95 = -1200），
        /// 判定式是 <c>ZVel &lt; 门槛</c>（NarrativeCharacter.cpp:252）。
        /// 端口此前写成正 1500：照 UE5 的比较式接下去就是"任何落地都砸趴"（ZVel 恒小于 +1500）。
        /// 同类的 <see cref="EnterRagdollFallZThreshold"/> 在 UE5 是正数用法（<c>ZVel &lt; -该值</c>，
        /// NarrativeCharacterMovement.cpp:990），两个字段符号约定不同，别抄错。
        /// </summary>
        public float EnterRagdollFallZImpactGroundThreshold = -1200f;

        /// <summary>
        /// 坠落伤害的起算下落速度（负值，厘米/秒）。对应 UE5 <c>FallDamageCurve</c>
        /// 的左端点（曲线资产在 Flax 侧没有等价作者面，这里用三点线性近似，见 <see cref="FallDamageRules"/>）。
        /// </summary>
        public float FallDamageStartZVelocity = -600f;

        /// <summary>坠落伤害封顶的下落速度（负值）。对应 UE5 曲线的右端点。</summary>
        public float FallDamageFullZVelocity = -1800f;

        /// <summary>满伤按最大生命扣掉的百分比。对应 UE5 曲线的纵轴上限。</summary>
        public float FallDamageMaxPercent = 1f;

        // ===== 事件 =====
        /// <summary>进入掩护时触发。</summary>
        public event Action OnEnterCover;
        /// <summary>离开掩护时触发。</summary>
        public event Action OnExitCover;

        // ===== 内部 =====
        private Actor _characterOwner;

        /// <summary>所属角色（简化引用，替代 UE5 NarrativeCharacterOwner）。</summary>
        public Actor CharacterOwner => _characterOwner ?? (_characterOwner = Actor);

        // ===== SequencerControlled 读取端（缺陷1） =====
        // UE5 语义：角色被序列绑定（Narrative.State.SequencerControlled，由
        // NarrativeDialogueSequence.ApplyControlledTags / NarrativeLevelSequenceActor.OnPlay 施加）
        // 期间不再接受移动/输入。此前该标签全仓零读取方，镜头进场等于没锁。
        // 标签经 ASC 查询（ASC 由 Core.ArsenalStatics 多级链解析，宿主装配模式同样命中），
        // 与 State_Invulnerable 在 GAS/NarrativeAbilitySystemComponent.cs:2010/2041 的读取方式同构。

        /// <summary>锁位复查节奏（秒）：解析 ASC 成本不低（可能触发多级查找），无需逐帧。</summary>
        private const float SequencerTagRecheckSeconds = 0.25f;
        private float _sequencerCheckCountdown;
        private bool _bSequencerControlled;

        /// <summary>本角色当前是否处于序列受控态（移动/输入被锁定）。</summary>
        public bool IsSequencerControlled => _bSequencerControlled;

        /// <summary>
        /// 查询任意 Actor（角色模式或宿主装配模式）是否被序列绑定。
        /// 读取端统一入口：移动组件、玩家控制器与交互组件都经由它门禁。
        /// </summary>
        public static bool ActorIsSequencerControlled(Actor actor)
        {
            if (actor == null) return false;
            var asc = NarrativePro.Core.ArsenalStatics.GetNarrativeAbilitySystemComponent(actor);
            return asc != null && asc.HasMatchingGameplayTag(NarrativePro.Core.NarrativeGameplayTags.State_SequencerControlled);
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            float dt = (float)Time.DeltaTime;

            // 攀爬积分：对应 UE5 蒙太奇/PoseSearch 驱动的位移段（Flax 无此二者，见 UpdateClimb 注释）。
            if (IsClimbing()) UpdateClimb(dt);

            // 缺陷2：位移层自带的重力/落地积分。UE5 的 MOVE_Falling 由 CMC 内建积分驱动，
            // Flax 无 CMC，全仓此前没有任何 Falling 积分：CancelClimb(Falling) 之后 NPC 会永久悬空，
            // ShouldSuspendGravityAndCollision 也因此零消费方。这里由本组件承担该职责：
            // 攀爬期间（标志为真）整段跳过，出攀爬的同一帧恢复重力/碰撞。
            StepGravityAndGround(dt);

            // 掩体位移接管（对应 UE5 掩体移动分支）。攀爬/布娃娃期间不接管：
            // 前者有自己的积分，后者根本不该响应输入。
            if (bHasCover && !IsClimbing() && !IsRagdoll()) UpdateCover(dt);

            _sequencerCheckCountdown -= dt;
            if (_sequencerCheckCountdown <= 0f)
            {
                _sequencerCheckCountdown = SequencerTagRecheckSeconds;
                _bSequencerControlled = ActorIsSequencerControlled(Actor);
            }

            if (!_bSequencerControlled)
            {
                // 布娃娃期间宿主/AI 仍会每帧写入移动意图：立即作废（UE5 的 Custom 档不吃 Acceleration）
                if (ShouldSuppressMovementInput)
                {
                    if (LocalInputVector.LengthSquared > 0f) LocalInputVector = Float2.Zero;
                    bWantsSprint = false;
                    bIsSlowWalking = false;
                }
                return;
            }

            // 攀爬中途被序列接管 → 攀爬中断，起身交还控制权（中断规则之一）。
            if (IsClimbing()) SetMovementMode(NarrativeMovementMode.Walking);

            // 锁移动输入：清掉宿主/AI 侧写入的残余输入与冲刺意图（对应 UE5 序列期间的 IgnoreMovementInput），
            // 使 IsSprinting/GetMaxSpeed/朝向移动判定全部归零，任何按 LocalInputVector 驱动的通路停摆。
            if (LocalInputVector.LengthSquared > 0f) LocalInputVector = Float2.Zero;
            bWantsSprint = false;
            bIsSlowWalking = false;
        }

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
            // UE5 NarrativeCharacterMovement.cpp:526：已经在目标模式里就整条早退。
            // 端口原来只在 Custom→Custom 时用 prevCustom != CustomMovementMode 挡了一半，
            // 同档重进（例如布娃娃中再次被击杀）会再跑一次 OnEnter*，
            // 把自身碰撞挂起计数推到 2，而退档只减 1 —— 角色从此没有碰撞体，直到 OnDisable 兜底。
            if (newMode == MovementMode && (newMode != NarrativeMovementMode.Custom
                || (NarrativeCustomMovementMode)newCustomMode == CustomMovementMode)) return;

            // UE5 :528-542：布娃娃期间禁止任何模式变更，只有「入水」例外，且入水要通过 owner 的
            // SetRagdoll(false) 走（状态归 owner 管）。锁的判据是角色侧 bIsRagdoll **加上**移动侧仍在
            // Ragdoll 自定义档 —— 退出布娃娃时 SetRagdoll(false) 先清角色标志再改模式，
            // 所以第一半已经为 false，锁不会把自己关死（UE5 同构：NarrativeCharacter.cpp:1369-1384）。
            var ragdollOwner = Actor != null ? Actor.GetScript<UnrealFramework.NarrativeCharacter>() : null;
            if (ragdollOwner != null && ragdollOwner.bIsRagdoll && IsRagdoll())
            {
                if (newMode == NarrativeMovementMode.Swimming)
                {
                    ragdollOwner.SetRagdoll(false);
                }
                return;
            }

            var prevMode = MovementMode;
            var prevCustom = CustomMovementMode;
            MovementMode = newMode;
            if (newMode == NarrativeMovementMode.Custom)
            {
                CustomMovementMode = (NarrativeCustomMovementMode)newCustomMode;
                // 早退已经保证了这里是真转换，退出回调必须无条件派发，进入/退出才成对。
                ExitCustomMovementMode(prevCustom);

                if (CustomMovementMode == NarrativeCustomMovementMode.Climb) OnEnterClimbing();
                else if (CustomMovementMode == NarrativeCustomMovementMode.Ragdoll) OnEnterRagdoll();
            }
            else
            {
                ExitCustomMovementMode(prevCustom);
                CustomMovementMode = NarrativeCustomMovementMode.None;
            }
            OnMovementModeChanged(prevMode, newMode);
        }

        /// <summary>按上一档自定义模式派发退出回调（与进入一一对应，状态归还原路返回）。</summary>
        private void ExitCustomMovementMode(NarrativeCustomMovementMode prevCustom)
        {
            if (prevCustom == NarrativeCustomMovementMode.Climb) OnExitClimbing();
            else if (prevCustom == NarrativeCustomMovementMode.Ragdoll) OnExitRagdoll();
        }

        /// <summary>移动模式改变时调用。</summary>
        protected virtual void OnMovementModeChanged(NarrativeMovementMode prevMode, NarrativeMovementMode newMode) { }

        protected virtual void OnEnterClimbing()
        {
            // 对应 UE5 OnEnterClimbing（NarrativeCharacterMovement.cpp:1174-1190）：
            // UE5 经 AnimInstance 施加/移除攀爬 Override 层（PoseSearch 特征通道驱动攀爬动画）。
            // Flax 1.12 无 PoseSearch（已核查 FlaxEngine.CSharp.xml：0 个 PoseSearch 类型），
            // 位移由本组件 UpdateClimb 确定性积分承担；这里保留标签解析并暴露读取端
            // （ClimbOverrideLayerTag），动画/特效侧接线时按 UE5 同款语义消费。
            _climbOverrideLayerTag = GetClimbOverrideLayerTag();

            // 缺陷2：进入攀爬即重置重力/计时，并把自身碰撞挂起
            //（UE5 PlayAttachWarp 把胶囊 SetCollisionEnabled(NoCollision)，Movement.cpp:2341-2342）。
            _fallVelocity = Vector3.Zero;
            _fallElapsed = 0f;
            _climbTotalElapsed = 0f;
            SetSelfCollisionSuspended(true);
        }

        protected virtual void OnExitClimbing()
        {
            // 对应 UE5 OnMontageEnded/OnTraversalMontageEndedTraversal（:2349-2410）的收尾：
            // 清攀爬态、还原 attach-warp 播放标记（重力/碰撞经 ShouldSuspendGravityAndCollision 归位）。
            _climbProps = null;
            _climbCharacter = null;
            _bClimbEntryPhase = false;
            _bClimbJumpQueued = false;
            _climbTotalElapsed = 0f;

            // 缺陷2：任何离场路径（站上顶部 Walking / 松手坠落 Falling / 超时 / 死亡 / 序列接管）
            // 都必然经过这里 —— 恢复碰撞、清零下落速度，重力从下一帧起重新生效。
            SetSelfCollisionSuspended(false);
            _fallVelocity = Vector3.Zero;
            _fallElapsed = 0f;

            var character = Actor != null ? Actor.GetScript<NarrativeCharacter>() : null;
            if (character != null) character.IsPlayingAttachWarpMontage = false;
        }

        // ===== 攀爬运行时（UE5 CMOVE_Climb + AttachWarp 蒙太奇位移的端口等价） =====

        /// <summary>总开关。对应 UE5 bDisableClimbAndMantles（Movement.cpp:1563）。</summary>
        public bool bDisableClimbAndMantles = false;

        /// <summary>props.Speed 未给出时的兜底攀爬速度（单位/秒）。</summary>
        public float ClimbMoveSpeedFallback = 100f;

        /// <summary>发现扫描的球半径（UE5 CapR()-1 的语义等价，端口角色尺度半径 40）。</summary>
        public float ClimbTraceRadius = 40f;

        /// <summary>起跳时前向发现距离（UE5 跳跃扫描 extent≈50+半径，:1587）。</summary>
        public float ClimbDiscoveryDistanceJump = 90f;

        /// <summary>行走贴面时的前向发现距离（UE5 非跳扫描 extent=CapR()，:1593）。</summary>
        public float ClimbDiscoveryDistanceWalking = 60f;

        /// <summary>ledge 之上的站立足净空高度（UE5 下行扫描 ledge+80 口径，:1634）。</summary>
        public float MantleStandClearance = 90f;

        // ===== 扫描掩码（缺陷1：不再全图层） =====

        /// <summary>
        /// 攀爬发现/跨越探测用的碰撞图层掩码。
        /// 旧实现一律传 <c>uint.MaxValue</c>（全图层），于是角色自身的碰撞体（NPC 现已带
        /// BoxCollider，见 AI/NarrativeCharacterSubsystem.cs:323 <c>AddChild&lt;BoxCollider&gt;</c>）
        /// 也会被打成「legacy ledge」，对自身做 mantle。这里改按本仓既有口径只查世界几何层：
        /// 本作的地面/墙体查询一律用 <see cref="FlaxEngine.LayersMask.Default"/>
        /// （见 Source/Game/PlayerController.cs:1596、Source/Game/ECS/Components/CameraComponent.cs:161），
        /// 角色则分布在单位层（如敌对单位 Layer 8，见 Source/Game/Combat/TargetSelectionSystem.cs:136）。
        /// 可在编辑器/宿主侧按需放宽（例如世界几何被挪到其它层时）。
        /// </summary>
        public uint ClimbDiscoveryMask = LayersMask.Default.Mask;

        /// <summary>重力/落地探测用的图层掩码（与发现扫描同一口径，可由宿主覆盖）。</summary>
        public uint GroundProbeMask = LayersMask.Default.Mask;

        // ===== 重力/落地积分（缺陷2） =====

        /// <summary>重力加速度（单位/秒²），与 <c>NarrativePathFollowingComp.LaunchGravity</c> 同口径。</summary>
        public float GravityAcceleration = -981f;

        /// <summary>脚下探测的向上余量（角色中心抬到近似脚底/碰撞体底部的容差）。</summary>
        public float GroundProbeUpOffset = 10f;

        /// <summary>Falling 最长持续秒数：超时视为掉进无底洞，停止下坠并回 Walking（UE5 fall-out 兜底）。</summary>
        public float MaxFallSeconds = 8f;

        /// <summary>是否由本组件在 Falling 态积分重力（外部位移层自行积分时置 false 以免双重下落）。</summary>
        public bool bAutoResolveFalling = true;

        /// <summary>
        /// 攀爬安全超时（秒）。对应 UE5 由遍历蒙太奇自然结束收口（MontageEnded，Movement.cpp:2349-2410）；
        /// Flax 无蒙太奇，位移段若因支撑点抖动/数据异常永不收敛就会把角色挂在攀爬态，故显式计时中止。
        /// </summary>
        public float ClimbTimeoutSeconds = 12f;

        private Vector3 _fallVelocity;
        private float _fallElapsed;
        private float _climbTotalElapsed;

        // ===== 自定义模式期间的自身碰撞挂起（UE5 PlayAttachWarp 胶囊 NoCollision 的 Flax 等价） =====

        private sealed class SuspendedCollider
        {
            public Collider Col;
            public bool bWasTrigger;
        }

        private readonly List<SuspendedCollider> _suspendedColliders = new List<SuspendedCollider>();

        /// <summary>
        /// 挂起层数：攀爬与布娃娃各挂一层（Custom→Custom 直接转档时两层并存过），
        /// 归零才真正还回碰撞，保证进入/退出严格对称。
        /// </summary>
        private int _collisionSuspensionDepth;

        private NarrativeCharacter _climbCharacter;
        private AttachWarpProps _climbProps;
        private Vector3 _climbStartLocation;
        private Vector3 _climbTargetLocation;
        private float _climbElapsed;
        private float _climbDuration;
        private bool _bClimbEntryPhase;
        private bool _bClimbJumpQueued;
        private GameplayTag _climbOverrideLayerTag;

        /// <summary>当前攀爬应施加的动画 Override 层标签（动画侧读取端，UE5 GetClimbOverrideLayerTag）。</summary>
        public GameplayTag ClimbOverrideLayerTag => _climbOverrideLayerTag;

        /// <summary>
        /// 位移层读取：为真时关闭重力/胶囊推进（UE5 在 PlayAttachWarp 里把胶囊
        /// SetCollisionEnabled(NoCollision) 并接管位移，Movement.cpp:2341-2342；
        /// Flax 无 CMC 内建重力，等价动作由写 Actor.Position 的宿主驱动执行）。
        /// <para>
        /// 缺陷2 之后本标志有了真实消费方：本组件的 <see cref="StepGravityAndGround"/> 与
        /// <see cref="SetSelfCollisionSuspended"/> 都按它挂起/恢复重力与自身碰撞，
        /// 外部位移层（宿主驱动的角色）也应照此门禁。
        /// </para>
        /// </summary>
        public bool ShouldSuspendGravityAndCollision => IsClimbing();

        /// <summary>
        /// 位移层读取：布娃娃（躯体已失去行动能力）期间，宿主/AI 侧写入的移动输入一律作废。
        /// 对应 UE5 Custom/Ragdoll 档下 CMC 根本不吃 Acceleration。
        /// 攀爬不在列——攀爬的位移方向正是由 <c>UpdateClimb</c> 消费 LocalInputVector 得出的。
        /// </summary>
        public bool ShouldSuppressMovementInput => IsRagdoll();

        /// <summary>
        /// 重力/落地积分（缺陷2）。对应 UE5 CMC 的 MOVE_Falling 内建积分 + 落地回 MOVE_Walking。
        /// 只在 Falling 态写位置：Walking/NavWalking/Custom 一律不动，
        /// 因此既有 click-to-move 与 NPC 路径跟随的行为完全不变。
        /// 攀爬（<see cref="ShouldSuspendGravityAndCollision"/>）期间整段跳过，出攀爬自动恢复。
        /// 布娃娃是唯一例外：UE5 由 PhysRagdoll（Movement.cpp:1090-1120）让胶囊跟着骨盆重力沉底，
        /// Flax 骨骼网格无物理模拟，这里用同一段积分把躯体落到地面上，但保持在 Custom/Ragdoll 不弹回 Walking。
        /// </summary>
        private void StepGravityAndGround(float dt)
        {
            if (ShouldSuspendGravityAndCollision)
            {
                _fallVelocity = Vector3.Zero;
                _fallElapsed = 0f;
                return;
            }

            var owner = Actor;
            if (owner == null) return;

            if (_bSequencerControlled) return;      // 序列受控期间位置由演出层驱动（UE5 同款门禁）

            // Falling 与布娃娃都要吃重力：其余模式一律不动位置（既有位移行为不变）。
            if (MovementMode != NarrativeMovementMode.Falling && !IsRagdoll())
            {
                _fallVelocity = Vector3.Zero;
                _fallElapsed = 0f;
                return;
            }

            if (!bAutoResolveFalling) return;

            // 外部位移驱动（路径跟随的 jump-off 弹道自救，NarrativePathFollowingComp._bLaunching）
            // 已在积分同一条重力时不重复施加，避免双倍下落。
            var pathFollow = owner.GetScript<NarrativePro.AI.NarrativePathFollowingComp>();
            if (pathFollow != null && pathFollow.IsLaunchActive)
            {
                _fallVelocity = Vector3.Zero;
                _fallElapsed = 0f;
                return;
            }

            _fallElapsed += dt;
            _fallVelocity.Z += GravityAcceleration * dt;
            float dz = _fallVelocity.Z * dt;
            if (dz >= 0f) return;                   // 仍在上升：交还起跳的驱动方

            Vector3 pos = owner.Position;
            float drop = -dz;

            // 脚下探测：从角色中心上方一点向下打 drop + 余量，命中即为可落地面。
            Vector3 probe = pos + Vector3.Up * GroundProbeUpOffset;
            if (Physics.RayCast(probe, Vector3.Down, out RayCastHit groundHit,
                    drop + GroundProbeUpOffset + 1f, GroundProbeMask, false)
                && groundHit.Collider != null && !IsOwnedByClimber(groundHit.Collider))
            {
                float floorZ = groundHit.Point.Z;
                if (pos.Z + dz <= floorZ)
                {
                    // 落地：贴地、清零速度、回 Walking（UE5 FALLING→WALKING 的等价收口）。
                    // 布娃娃只沉底不改档——尸体留在地上，档由 SetRagdoll(false) 还回 Walking。
                    Vector3 landed = pos;
                    landed.Z = floorZ;
                    owner.Position = landed;
                    // 落地瞬间的下落速度要在清零前取：UE5 的 Landed 用 GetVelocity().Z 结算
                    // 坠落伤害与砸趴（NarrativeCharacter.cpp:234-256），端口这条积分器就是
                    // 本工程唯一的落地点，所以那两个门槛在这里才有消费者。
                    float impactZVelocity = _fallVelocity.Z;
                    _fallVelocity = Vector3.Zero;
                    _fallElapsed = 0f;
                    if (!IsRagdoll()) SetMovementMode(NarrativeMovementMode.Walking);

                    var landedCharacter = Actor != null ? Actor.GetScript<UnrealFramework.NarrativeCharacter>() : null;
                    if (landedCharacter != null)
                    {
                        landedCharacter.LandedFromFallImpact(impactZVelocity, this);
                    }
                    return;
                }
            }

            if (_fallElapsed >= MaxFallSeconds)
            {
                // 深渊/漏碰撞保护：停止下坠并回到 Walking，角色不会越掉越远。
                Vector3 rest = pos;
                rest.Z += dz;
                owner.Position = rest;
                _fallVelocity = Vector3.Zero;
                _fallElapsed = 0f;
                if (!IsRagdoll()) SetMovementMode(NarrativeMovementMode.Walking);
                return;
            }

            owner.Position = pos + new Vector3(0f, 0f, dz);
        }

        /// <summary>
        /// 命中体是否属于正在扫描/攀爬的角色自身（自身及其整条父链 —— 角色碰撞体是
        /// <c>AddChild&lt;BoxCollider&gt;</c> 挂出来的子 Actor）。
        /// 与近战命中过滤同一思路（GAS/NarrativeCombatAbility.cs:660 <c>IsOwnedBySelf</c> 沿
        /// <c>Actor.Parent</c> 回溯到宿主），本组件不引用 GAS 类型以免层级倒置。
        /// 缺陷1：NPC 自带碰撞体后，radius 40 / 前向 60cm 的扫描必然打中自己，旧实现把它
        /// 当作 legacy ledge（:401-406）→ 对自己发起 mantle。
        /// </summary>
        private bool IsOwnedByClimber(Actor testActor)
        {
            if (testActor == null) return false;
            var self = CharacterOwner;
            if (self == null) return false;

            var current = testActor;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                if (ReferenceEquals(current, self)) return true;
                current = current.Parent;
            }
            return false;
        }

        /// <summary>
        /// 自定义模式期间挂起/恢复角色自身碰撞（UE5 胶囊 NoCollision / 网格改 PhysicsBody 的 Flax 等价）：
        /// Flax 托管 API 没有 Actor/Collider 级的 enable-collision 开关（已核查 FlaxEngine.CSharp.xml，
        /// <c>Actor</c> 只有 Layer/LayerName，<c>Collider</c> 只有 Center/ContactOffset/IsTrigger/Material），
        /// 而本仓所有位移/扫描查询都按 <c>hitsTriggers=false</c> 走 —— 把自身碰撞体临时置为 trigger
        /// 即可让扫描打不中它（既修缺陷1 的自Hit，也避免攀爬位移被自身物理阻挡），
        /// 恢复时按进入前记录的原值逐一体还，绝不把作者手设的 trigger 关掉。
        /// </summary>
        private void SetSelfCollisionSuspended(bool suspended)
        {
            if (suspended)
            {
                if (_collisionSuspensionDepth > 0)
                {
                    _collisionSuspensionDepth++;
                    return;
                }

                var owner = CharacterOwner;
                if (owner == null) return;

                _suspendedColliders.Clear();
                CollectColliders(owner, _suspendedColliders, 0);

                for (int i = 0; i < _suspendedColliders.Count; i++)
                {
                    var entry = _suspendedColliders[i];
                    if (entry.Col == null || entry.bWasTrigger) continue;
                    entry.Col.IsTrigger = true;
                }
                _collisionSuspensionDepth = 1;
                return;
            }

            ReleaseCollisionSuspension();
        }

        /// <summary>退一层挂起；最后一层才把碰撞体按原值还回。</summary>
        private void ReleaseCollisionSuspension()
        {
            if (_collisionSuspensionDepth <= 0) return;

            _collisionSuspensionDepth--;
            if (_collisionSuspensionDepth > 0) return;

            for (int i = 0; i < _suspendedColliders.Count; i++)
            {
                var entry = _suspendedColliders[i];
                if (entry.Col == null || entry.bWasTrigger) continue;
                entry.Col.IsTrigger = false;
            }
            _suspendedColliders.Clear();
        }

        /// <summary>无条件还清全部挂起层（禁用/回收时的兜底，防止预制体复用时角色永久失去碰撞）。</summary>
        private void ResetSelfCollisionSuspension()
        {
            if (_collisionSuspensionDepth <= 0) return;
            _collisionSuspensionDepth = 1;
            ReleaseCollisionSuspension();
        }

        /// <summary>递归收集 Actor 链上的碰撞体（深度上限防预制体异常嵌套）。</summary>
        private static void CollectColliders(Actor actor, List<SuspendedCollider> outList, int depth)
        {
            if (actor == null || depth > 8) return;
            if (actor is Collider col)
            {
                outList.Add(new SuspendedCollider { Col = col, bWasTrigger = col.IsTrigger });
            }

            int count = actor.ChildrenCount;
            for (int i = 0; i < count; i++)
            {
                CollectColliders(actor.GetChild(i), outList, depth + 1);
            }
        }

        /// <summary>对应 UE5 GetClimbOverrideLayerTag（Movement.cpp:1491-1505）。</summary>
        private GameplayTag GetClimbOverrideLayerTag()
        {
            if (_climbProps != null && _climbProps.ClimbableActor != null)
            {
                return _climbProps.ClimbableActor.GetClimbOverrideTag();
            }
            return NarrativePro.Core.NarrativeGameplayTags.Narrative_Anim_OverrideLayer_Climbing;
        }

        /// <summary>
        /// 遍历/攀爬发现入口。对应 UE5 TryFindAttachTransform（Movement.cpp:1507-1558）：
        /// Walking/Falling 态做前向扫描 → 命中 IClimbable 交给攀爬物自校（AdjustInitialWarpProps，
        /// 可否决）；命中普通几何走旧式 ledge 扫描（UE5 TryFindClimbTransform 几何段 :1613-1766）。
        /// 攀爬中的连续驱动由 <see cref="UpdateClimb"/> 每帧调 TryFindClimbTransform（对应 UE5 :1527）。
        /// </summary>
        public bool TryFindAttachTransform(AttachWarpProps props, bool pressedJump, Float2 inputVector, float optionalBlendInTime)
        {
            if (props == null || Actor == null || bDisableClimbAndMantles) return false;
            var character = Actor.GetScript<NarrativeCharacter>();
            if (character == null) return false;
            if (IsClimbing()) return false;

            // UE5 :1551 只在 Walking/Falling/Swimming 发现遍历；端口无 Swimming 驱动，同判 Walking/Falling/NavWalking。
            if (MovementMode != NarrativeMovementMode.Walking
                && MovementMode != NarrativeMovementMode.Falling
                && MovementMode != NarrativeMovementMode.NavWalking) return false;

            props.bPressedJump = pressedJump;
            AttachWarpProps.ClearProps(props);

            Vector3 facing = FlattenToHorizontal(Actor.Direction);
            if (facing.LengthSquared < 0.0001f)
            {
                facing = FlattenToHorizontal(Vector3.Transform(Vector3.Forward, Actor.Orientation));
            }
            if (facing.LengthSquared < 0.0001f) return false;
            facing = Vector3.Normalize(facing);

            // 扫描起点复现 UE5 :1585-1594：跳=竖直±ClimbTraceJumpVerticalDistance、横向±ClimbTraceJumpHorizontalDistance；
            // 走=+20 单位高度贴面。
            Vector3 right = FlattenToHorizontal(Vector3.Transform(Vector3.Right, Actor.Orientation));
            float verticalOffset = pressedJump
                ? (inputVector.Y >= 0f ? ClimbTraceJumpVerticalDistance : -ClimbTraceJumpVerticalDistance)
                : 20f;
            float lateralOffset = Mathf.Abs(inputVector.X) > 0.01f
                ? (inputVector.X > 0f ? ClimbTraceJumpHorizontalDistance : -ClimbTraceJumpHorizontalDistance)
                : 0f;
            Vector3 start = Actor.Position + Vector3.Up * verticalOffset + right * lateralOffset;
            float dist = pressedJump ? ClimbDiscoveryDistanceJump : ClimbDiscoveryDistanceWalking;

            ClimbableActor scripted = null;
            Vector3 scriptedPoint = Vector3.Zero;
            Vector3 scriptedNormal = Vector3.Zero;
            bool hasLegacyHit = false;
            Vector3 legacyPoint = Vector3.Zero;
            Vector3 legacyNormal = Vector3.Zero;

            // 缺陷1：扫描按世界几何层（ClimbDiscoveryMask）而不是全图层，并过滤掉属于攀爬者自身的
            // 命中体（自身 + 子碰撞体），否则 radius 40 / 前向 60cm 的必然打中 NPC 自己的 BoxCollider。
            if (Physics.SphereCastAll(start, ClimbTraceRadius, facing, out RayCastHit[] hits, dist, ClimbDiscoveryMask, false) && hits != null)
            {
                foreach (var h in hits)
                {
                    var hitActor = h.Collider;
                    if (hitActor == null) continue;
                    if (IsOwnedByClimber(hitActor)) continue;
                    var climbable = hitActor.GetScript<ClimbableActor>();
                    if (climbable != null)
                    {
                        if (scripted == null && climbable.CanClimbFrom(Actor.Position, facing, TraversalTraceForwardDistanceClimbCheck))
                        {
                            scripted = climbable;
                            scriptedPoint = h.Point;
                            scriptedNormal = h.Normal;
                        }
                        continue;
                    }
                    if (!hasLegacyHit)
                    {
                        legacyPoint = h.Point;
                        legacyNormal = h.Normal;
                        hasLegacyHit = true;
                    }
                }
            }

            // 无 PhysX 碰撞体的代码生成攀爬物射线打不中：登记表邻近回退
            //（与 PlayerInteractionComponent 的邻近回退同一理由）。
            // 缺陷5：登记表为空/未内容摆放时该调用安全返回 null；缺陷1：传入攀爬者自身，
            // 挂在角色子树上的攀爬物不会被当成可抓目标。
            if (scripted == null)
            {
                scripted = ClimbableActor.FindNearestClimbable(Actor.Position, facing,
                    TraversalTraceForwardDistanceClimbCheck, Actor);
            }

            if (scripted != null)
            {
                props.ClimbableActor = scripted;
                // UE5 :1525-1527：攀爬物自校初始 warp 属性并保留否决权（返回 false 中止）。
                if (!scripted.AdjustInitialWarpProps(props, scriptedPoint, scriptedNormal, character, pressedJump, inputVector, optionalBlendInTime))
                {
                    props.ClimbableActor = null;
                    return false;
                }
                return true;
            }

            if (hasLegacyHit) return TryFindMantleFromHit(props, legacyPoint, legacyNormal, optionalBlendInTime);
            return false;
        }

        /// <summary>
        /// 旧式几何可跨扫描。对应 UE5 TryFindClimbTransform 的 ledge 推导段
        /// （Movement.cpp:1626-1670：前面 hitPoint + 外法线*15 + 上*80 → 下行 120 找顶面），
        /// 以及 :1770-1785 的 climb-to-standing（NewMovementMode=MOVE_Walking）。
        /// UE5 旧式同样要播遍历蒙太奇；Flax 无蒙太奇根运动，位移由本组件积分承担（BeginClimb 入场段）。
        /// </summary>
        private bool TryFindMantleFromHit(AttachWarpProps props, Vector3 impactPoint, Vector3 impactNormal, float optionalBlendInTime)
        {
            if (impactPoint == Vector3.Zero) return false;

            Vector3 targetDirection = FlattenToHorizontal(-impactNormal); // UE5 :1628 TargetDirection = -ImpactNormal
            if (targetDirection.LengthSquared < 0.0001f) return false;
            targetDirection = Vector3.Normalize(targetDirection);

            Vector3 downStart = new Vector3(
                impactPoint.X + targetDirection.X * 15f,
                impactPoint.Y + targetDirection.Y * 15f,
                impactPoint.Z + 80f);

            // 顶面探测（UE5 :1645-1656：下行 120，打不到顶面=几何太薄，early out）。
            // 缺陷1：同样按世界几何层查询，且命中自身（攀爬者的子碰撞体）不算顶面。
            if (!Physics.RayCast(downStart, Vector3.Down, out RayCastHit topHit, 120f, ClimbDiscoveryMask, false)
                || topHit.Collider == null || IsOwnedByClimber(topHit.Collider)) return false;

            Vector3 ledge = topHit.Point;
            Vector3 standTarget = ledge + Vector3.Up * MantleStandClearance;

            // 站立空间校验（UE5 IsClimbLocationIsValidForCharacter :1761 的等价）：ledge 上方净空扫描不应被挡。
            // 净空判定要排除自身（缺陷1：翻越完成后角色本就站在这里）与 trigger 之外的非几何层。
            if (Physics.SphereCastAll(ledge + Vector3.Up * 60f, 35f, Vector3.Up, out RayCastHit[] blockers, 60f, ClimbDiscoveryMask, false)
                && blockers != null)
            {
                for (int i = 0; i < blockers.Length; i++)
                {
                    var b = blockers[i].Collider;
                    if (b == null || IsOwnedByClimber(b)) continue;
                    return false;
                }
            }

            props.LedgeTransform = new Transform(standTarget, Actor.Orientation, Vector3.One);
            props.CurrentLedgeTransform = props.LedgeTransform;
            props.HasFrontLedge = true;
            props.ActionType = ETraversalActionType.Mantle;
            props.NewMovementMode = NarrativeMovementMode.Walking;
            props.OptionalBlendInTime = optionalBlendInTime;
            return true;
        }

        /// <summary>
        /// 依据已求出的 warp 属性进入攀爬并入场（对应 UE5 MultiCastPlayAttachWarp 的
        /// SetMovementMode(MOVE_Custom, CMOVE_Climb)，Movement.cpp:2340）。
        /// </summary>
        public void BeginClimb(AttachWarpProps props)
        {
            if (props == null || Actor == null) return;
            _climbProps = props;
            _climbCharacter = Actor.GetScript<NarrativeCharacter>();
            _climbStartLocation = Actor.Position;
            _climbTargetLocation = props.LedgeTransform.Translation;

            float distance = Vector3.Distance(_climbStartLocation, _climbTargetLocation);
            float speed = props.Speed > 0f ? props.Speed : ClimbMoveSpeedFallback;
            _climbDuration = Mathf.Max(distance / Mathf.Max(speed, 1f), 0.15f);
            _climbElapsed = 0f;
            _climbTotalElapsed = 0f;   // 缺陷2：安全超时从入场起计时（不是每段重置）
            _bClimbEntryPhase = true;
            _bClimbJumpQueued = false;

            SetMovementMode(NarrativeMovementMode.Custom, (byte)NarrativeCustomMovementMode.Climb);
        }

        /// <summary>攀爬中收到跳跃（角色顶部的起跳=翻越起身请求，UE5 把 PressedJump 传入 TryFindClimbTransform，:1527）。</summary>
        public void RequestClimbJump()
        {
            if (IsClimbing()) _bClimbJumpQueued = true;
        }

        /// <summary>
        /// 攀爬主循环。UE5 里位移由遍历蒙太奇 + MotionWarping + PoseSearch 特征通道完成；
        /// Flax 1.12 三者皆无（PoseSearch 已核查不存在，MotionWarping 端口在 NarrativeCharacter.cs
        /// 类注释中标注为 Flax-不兼容占位），故这里用确定性积分推进角色沿攀爬路径移动：
        /// 入场段按 smoothstep 时间曲线（对应蒙太奇 blend-in），持续段按攀爬物每帧给出的
        /// 目标点定速跟随——全程无传送、可用时长反算。
        /// 中断规则：死亡/失去行动能力 → 坠落；底部向下拉满松手 → 坠落（UE5 :577）；
        /// 顶部向上/跳 → 站立离场 Walking（UE5 :1770-1785）；被序列接管 → Walking（OnUpdate）。
        /// </summary>
        private void UpdateClimb(float dt)
        {
            if (_climbProps == null || Actor == null)
            {
                CancelClimb(NarrativeMovementMode.Falling);
                return;
            }

            var character = _climbCharacter ?? Actor.GetScript<NarrativeCharacter>();
            if (character == null || !character.IsAlive())
            {
                // 攀爬中被击杀/失去行动能力 → 打断，坠落接管。
                CancelClimb(NarrativeMovementMode.Falling);
                return;
            }

            // 缺陷2：安全超时。UE5 由遍历蒙太奇结束（MontageEnded）强制收口，Flax 的位移段没有那个
            // 天然终点：支撑点抖动/攀爬物数据异常时角色会永远挂在 Custom/Climb（重力与碰撞被挂起 → 悬空）。
            // 超时按松手坠落处理：CancelClimb(Falling) 经 OnExitClimbing 恢复重力与碰撞，
            // 再由 StepGravityAndGround 把角色落回地面。
            _climbTotalElapsed += dt;
            if (ClimbTimeoutSeconds > 0f && _climbTotalElapsed >= ClimbTimeoutSeconds)
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"攀爬超过 {ClimbTimeoutSeconds:0.#}s 未收敛，安全中止为 Falling（{Actor?.Name}）。");
                CancelClimb(NarrativeMovementMode.Falling);
                return;
            }

            if (_bClimbEntryPhase)
            {
                _climbElapsed += dt;
                float t = Mathf.Clamp(_climbElapsed / Mathf.Max(_climbDuration, 0.0001f), 0f, 1f);
                float eased = t * t * (3f - 2f * t); // 手写 smoothstep（Flax Mathf 无 SmoothStep 用法先例，避免依赖）
                Actor.Position = _climbStartLocation + (_climbTargetLocation - _climbStartLocation) * eased;
                if (t < 1f) return;
                _bClimbEntryPhase = false;

                if (_climbProps.ClimbableActor == null)
                {
                    // 旧式跨越（无 IClimbable）：入场位移即全程，到达按 NewMovementMode 离场
                    //（UE5 OnTraversalMontageEndedTraversal :2370-2385）。
                    CompleteClimbByMode();
                    return;
                }
            }

            var climbable = _climbProps.ClimbableActor;
            if (climbable == null) return;

            // 本地玩家用本地输入；NPC（路径跟随触发的攀爬）自动向上爬到顶离场。
            Float2 input = character.IsLocallyControlled() ? LocalInputVector : new Float2(0f, 1f);
            bool pressedJump = _bClimbJumpQueued;
            _bClimbJumpQueued = false;

            if (!climbable.TryFindClimbTransform(_climbProps, _climbProps, character, pressedJump, input, dt))
            {
                // 无支撑点（底部向下拉到底松手）→ 坠落（UE5 Movement.cpp:577）。
                CancelClimb(NarrativeMovementMode.Falling);
                return;
            }

            float speed = _climbProps.Speed > 0f ? _climbProps.Speed : ClimbMoveSpeedFallback;
            Vector3 cur = Actor.Position;
            Vector3 target = _climbProps.LedgeTransform.Translation;
            Vector3 delta = target - cur;
            float step = speed * dt;
            if (delta.LengthSquared <= step * step)
            {
                Actor.Position = target;
            }
            else
            {
                Actor.Position = cur + Vector3.Normalize(delta) * step;
            }

            // 顶部起身指令（NewMovementMode=Walking）且已到目标 → 离场。
            if (_climbProps.NewMovementMode == NarrativeMovementMode.Walking
                && Vector3.Distance(Actor.Position, target) < 1f)
            {
                CancelClimb(NarrativeMovementMode.Walking);
            }
        }

        private void CompleteClimbByMode()
        {
            var mode = _climbProps != null ? _climbProps.NewMovementMode : NarrativeMovementMode.Walking;
            if (mode == NarrativeMovementMode.Custom) mode = NarrativeMovementMode.Walking;
            CancelClimb(mode);
        }

        /// <summary>离开攀爬态并按指定模式交还（Walking=站上顶部 / Falling=松手坠落）。</summary>
        public void CancelClimb(NarrativeMovementMode exitMode)
        {
            if (!IsClimbing()) return;
            SetMovementMode(exitMode);
        }

        // ===== 布娃娃运行时（UE5 OnEnterRagdoll / OnExitRagdoll 的端口等价） =====

        /// <summary>
        /// 进入布娃娃。对应 UE5 <c>OnEnterRagdoll</c>（NarrativeCharacterMovement.cpp:1197-1226）：
        /// 胶囊降到 QueryOnly、骨骼网格 Detach 后 SetSimulatePhysics(true) 换成 PhysicsBody、再叠 Ragdoll 覆盖层。
        /// Flax 1.12 骨骼网格无逐骨物理（已核查 FlaxEngine.CSharp.xml：AnimatedModel 无刚体/模拟开关），
        /// 等价动作复用攀爬那套机制：挂起角色全部自身碰撞（网格从此不挡人也不挡弹）、断掉输入与寻路，
        /// 躯体由 <see cref="StepGravityAndGround"/> 吃重力沉到地面后停在 Custom/Ragdoll 档。
        /// </summary>
        protected virtual void OnEnterRagdoll()
        {
            LocalInputVector = Float2.Zero;
            bWantsSprint = false;
            bIsSlowWalking = false;
            InvalidateCover();

            _fallVelocity = Vector3.Zero;
            _fallElapsed = 0f;
            SetSelfCollisionSuspended(true);

            // 停 AI：倒地后不得再有移动请求（UE5 靠 Custom 模式不吃 Acceleration 达成同一结果）
            Actor?.GetScript<NarrativePro.AI.NarrativePathFollowingComp>()?.AbortMove();

            SyncRagdollMovementTag(true);

            var anim = GetCharacterAnimInstance();
            if (anim != null)
            {
                // UE5 :1220-1223：布娃娃覆盖层接管姿态（0.2s 淡入），起身时按同标签淡出
                anim.ApplyOverrideLayer(NarrativePro.Core.NarrativeGameplayTags.Narrative_Anim_OverrideLayer_Ragdoll, 0.2f);
            }
        }

        /// <summary>
        /// 退出布娃娃。对应 UE5 <c>OnExitRagdoll</c>（NarrativeCharacterMovement.cpp:1228-1319）：
        /// 胶囊回 QueryAndPhysics、网格停物理并重新挂回胶囊、按骨盆朝向转正 Actor、淡出覆盖层。
        /// 进入时挂起的每一项状态都在这里沿原路还回（碰撞、重力、标签、覆盖层），Actor 朝向不动——
        /// Flax 无逐骨物理，也就没有“骨盆朝向”可取。
        /// </summary>
        protected virtual void OnExitRagdoll()
        {
            SetSelfCollisionSuspended(false);

            _fallVelocity = Vector3.Zero;
            _fallElapsed = 0f;

            SyncRagdollMovementTag(false);

            var anim = GetCharacterAnimInstance();
            if (anim != null &&
                anim.GetOverrideLayerTag() == NarrativePro.Core.NarrativeGameplayTags.Narrative_Anim_OverrideLayer_Ragdoll)
            {
                anim.RemoveOverrideLayer(0.3f);
            }
        }

        /// <summary>
        /// State_Movement_Ragdoll 松散标签同步。对应 UE5 <c>OnMovementModeChanged</c>
        /// （NarrativeCharacter.cpp:170-177），供 GAS 侧按标签读移动状态。
        /// </summary>
        private void SyncRagdollMovementTag(bool bWantsRagdollTag)
        {
            var asc = NarrativePro.Core.ArsenalStatics.GetNarrativeAbilitySystemComponent(Actor);
            if (asc == null) return;

            var tag = NarrativePro.Core.NarrativeGameplayTags.State_Movement_Ragdoll;
            if (bWantsRagdollTag)
            {
                if (!asc.HasMatchingGameplayTag(tag)) asc.GameplayTags.AddTag(tag);
            }
            else if (asc.HasMatchingGameplayTag(tag))
            {
                asc.GameplayTags.RemoveTag(tag);
            }
        }

        /// <summary>取所属角色的动画实例（对应 UE5 <c>GetCharacterAnimInstance</c>，Movement.cpp:1220/1305）。</summary>
        private NarrativeAnimInstance GetCharacterAnimInstance()
        {
            return Actor?.GetScript<NarrativeCharacter>()?.GetCharacterAnimInstance();
        }

        protected virtual void OnEnterSwimming() { }
        protected virtual void OnExitSwimming() { }

        // ===== Cover API =====

        /// <summary>向前寻找掩体的距离（厘米）。对应 UE5 前向扫描的 SearchRadius。</summary>
        public float CoverSearchDistance = 400f;

        /// <summary>
        /// 尝试进入掩护。对应 UE5 <c>NarrativeCharacterMovement.cpp:2441</c> 的「向前扫描 → 填充 CoverState」路径。
        /// <para>
        /// UE5 用角色移动组件自带的前向胶囊扫描；Flax 无对应组件，这里改用本插件移植的瓦片掩体查询
        /// （<see cref="NarrativeNavigationSystem.FindNearestCoverToPoint"/>，与 AI 掩体系统共用同一份掩体数据），
        /// 掩体高低判定用 <see cref="Physics.CapsuleCastAll"/> 在越视高度上做一次前向扫描，
        /// 与 UE5 用掩体高度分档的语义等价。
        /// </para>
        /// </summary>
        public bool TryEnterCover()
        {
            var owner = CharacterOwner;
            if (owner == null) return false;

            Vector3 facing = FlattenToHorizontal(Vector3.Transform(Vector3.Forward, owner.Orientation));
            if (facing.LengthSquared < 0.0001f) return false;

            var config = NarrativeNavigationSystem.GetCoverTraceConfig();
            Vector3 probePoint = owner.Position + facing * CoverSearchDistance;

            var links = new List<ChainLink>();
            if (!NarrativeNavigationSystem.FindNearestCoverToPoint(owner, probePoint, links,
                    out int chainLinkIndex, out Vector3 coverPoint, CoverSearchDistance, facing, 60f)
                || chainLinkIndex < 0)
            {
                return false;
            }

            Vector3 toCover = FlattenToHorizontal(coverPoint - owner.Position);
            if (toCover.LengthSquared < 0.0001f) return false;
            toCover = Vector3.Normalize(toCover);

            // 越视高度上不再被挡 → 这是矮掩体（需要蹲伏才能藏住）。
            Vector3 castStart = coverPoint + Vector3.Up * config.PeekOverHeight;
            bool blockedAtPeekHeight = Physics.CapsuleCastAll(castStart, config.HalfHeight, 0f,
                toCover, out RayCastHit[] _ , Quaternion.Identity, config.CoverSpacing, GetCoverTraceMask(), false);

            // 角色贴站在掩体后方半个身位，背对掩体（探身时按 CoverPlayerRotationEuler 回转）。
            Vector3 standLocation = coverPoint - toCover * (config.HalfHeight + 10f) + Vector3.Up * config.HalfHeight;
            float yawDegrees = (float)(System.Math.Atan2(-toCover.X, -toCover.Z) * (180.0 / System.Math.PI));

            Vector3 wallAxis = FlattenToHorizontal(
                Vector3.Transform(Vector3.Right, Quaternion.RotationY(yawDegrees * Mathf.DegreesToRadians)));
            if (wallAxis.LengthSquared < 0.0001f) wallAxis = Vector3.Right;
            wallAxis = Vector3.Normalize(wallAxis);

            // 左右开阔度：两侧各探一个身位，问同一份掩体查询"那里背后还有掩体吗"。
            // UE5 的 FCoverState 只有这两枚布尔（NarrativeCharacterMovement.h 的 bIsLeftOpen/bIsRightOpen），
            // 决定能不能从对应一侧探身/绕边，之前端口恒写 false 等于把探身能力全关死了。
            bool bLeftOpen = StillCoveredAt(standLocation - wallAxis * NextCoverTraceSpacing);
            bool bRightOpen = StillCoveredAt(standLocation + wallAxis * NextCoverTraceSpacing);

            // 进掩体时的朝向：UE5 定成 ±掩护右轴（:2476-2487 用 Dot(CoverRight, Forward) 挑一侧），
            // 即"侧身贴墙"，不是背对掩体。
            Vector3 charForward = FlattenToHorizontal(Vector3.Transform(Vector3.Forward, owner.Orientation));
            bool bFaceNegativeSide = Vector3.Dot(wallAxis, charForward) < 0f;
            float playerYaw = (float)(System.Math.Atan2(
                bFaceNegativeSide ? -wallAxis.X : wallAxis.X,
                bFaceNegativeSide ? -wallAxis.Z : wallAxis.Z) * (180.0 / System.Math.PI));

            var state = new CoverState
            {
                CoverTransform = new Transform(standLocation, Quaternion.RotationY(yawDegrees * Mathf.DegreesToRadians), Vector3.One),
                bIsShortCover = !blockedAtPeekHeight,
                bIsLeftOpen = bLeftOpen,
                bIsRightOpen = bRightOpen,
                CoverPlayerRotationEuler = new Float3(0f, playerYaw, 0f),
            };

            CoverState = state;
            bHasCover = true;
            OnEnterCover?.Invoke();
            return true;
        }

        /// <summary>把向量压平到水平面（UE5 的 FRotator(0, Yaw, 0) 口径）。</summary>
        private static Vector3 FlattenToHorizontal(Vector3 value)
        {
            return new Vector3(value.X, 0f, value.Z);
        }

        /// <summary>掩体扫描用的碰撞掩码（全图层，与 GAS 默认武器通道未配置时的取值一致）。</summary>
        private static uint GetCoverTraceMask()
        {
            return uint.MaxValue;
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

        /// <summary>
        /// 掩护中向左/右平移。对应 UE5 <c>NarrativeCharacterMovement.cpp:2616</c> 的 CoverMove：
        /// 沿掩护面右轴推进，推进前先用同一份掩体查询确认**新位置背后仍有掩体**，
        /// 没有就说明走到了掩体尽头 → 停住并报告失败（UE5 在此处做边角探测）。
        /// </summary>
        public bool CoverMove(Float2 localInput)
        {
            if (!bHasCover) return false;
            float strafe = localInput.X;
            if (System.Math.Abs(strafe) < 0.01f) return false;

            var owner = CharacterOwner;
            if (owner == null) return false;

            Vector3 axis = CoverSlideAxis();
            Vector3 target = CoverState.CoverTransform.Translation
                             + axis * (strafe > 0f ? NextCoverTraceSpacing : -NextCoverTraceSpacing);

            if (!StillCoveredAt(target))
            {
                return false;
            }

            ApplyCoverPosition(target, strafe > 0f);
            return true;
        }

        /// <summary>掩护面右轴（水平化 + 归一）；退化时给零向量的模长保护。</summary>
        private Vector3 CoverSlideAxis()
        {
            Vector3 right = FlattenToHorizontal(Vector3.Transform(Vector3.Right, CoverState.CoverTransform.Orientation));
            if (right.LengthSquared < 0.0001f) return Vector3.Zero;
            return Vector3.Normalize(right);
        }

        /// <summary>候选点上是否仍有掩体可躲（复用导航侧的掩体查询，不新造一套物理探测）。</summary>
        private bool StillCoveredAt(Vector3 candidate)
        {
            var owner = CharacterOwner;
            if (owner == null) return false;

            Vector3 facing = FlattenToHorizontal(owner.Position - candidate);
            var links = new List<ChainLink>();
            return NarrativeNavigationSystem.FindNearestCoverToPoint(owner, candidate, links,
                out int chainLinkIndex, out Vector3 coverPoint, NextCoverTraceDepth, facing, NextCoverTraceSpacing)
                && chainLinkIndex >= 0;
        }

        /// <summary>把角色贴到掩体后方的站位（背对掩体；探身时朝掩体方向偏移）。</summary>
        private void ApplyCoverPosition(Vector3 coverPoint, bool bLeaningRight)
        {
            var owner = CharacterOwner;
            if (owner == null) return;

            Vector3 axis = CoverSlideAxis();
            if (axis.LengthSquared < 0.0001f) return;

            // 站位 = 掩体线后撤半个身位；探身（输入向前）时按 LeanFromCoverDist 朝掩体贴过去。
            Vector3 awayFromCover = FlattenToHorizontal(owner.Position - coverPoint);
            float depth = System.Math.Max(1f, awayFromCover.Length);
            awayFromCover = depth > 1f ? awayFromCover / depth : -axis;

            float lean = 0f;
            if (LocalInputVector.Y > 0.1f)
            {
                lean = LeanFromCoverDist;
            }

            Vector3 stand = coverPoint - awayFromCover * (PlayerOffsetFromCover - lean);
            owner.Position = stand;

            // 朝向：UE5 在进掩体时把 CoverPlayerRotation 定成 ±掩护右轴（:2476-2487），
            // 也就是"侧身贴着墙"，之后沿墙移动/瞄准才改变这个朝向。
            float yawDegrees = (float)(System.Math.Atan2(axis.X, axis.Z) * (180.0 / System.Math.PI));
            if (!bLeaningRight)
            {
                yawDegrees += 180f;
            }
            CoverState.CoverPlayerRotationEuler = new Float3(0f, yawDegrees, 0f);
        }

        /// <summary>
        /// 掩护期间的每帧约束：对应 UE5 掩体状态下的位移接管
        /// （UE5 由 Cover 移动分支处理；Flax 无 CMC，所以本组件在 OnUpdate 里做同等积分）。
        /// </summary>
        private void UpdateCover(float dt)
        {
            if (!bHasCover) return;

            if (System.Math.Abs(LocalInputVector.X) > 0.1f)
            {
                CoverMove(LocalInputVector);
            }
            else
            {
                // 无平移输入时也要按探身输入重贴站位（否则松手后不会收回身体）。
                ApplyCoverPosition(CoverState.CoverTransform.Translation, IsCoverStrafingLeft());
            }
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

            // 缺陷2：禁用（切关卡/回收）时若仍在攀爬，必须把挂起的自身碰撞还回去，
            // 否则预制体复用时角色会永久失去碰撞。
            if (IsClimbing()) SetMovementMode(NarrativeMovementMode.Falling);
            if (IsRagdoll()) SetMovementMode(NarrativeMovementMode.Walking);
            ResetSelfCollisionSuspension();

            _characterOwner = null;
            base.OnDisable();
        }
    }
}
