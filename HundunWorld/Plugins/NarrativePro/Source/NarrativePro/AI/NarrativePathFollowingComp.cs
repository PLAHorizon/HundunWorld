using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Navigation;
using NarrativePro.UnrealFramework;

namespace NarrativePro.AI
{
    /// <summary>
    /// 自定义路径跟随组件。对应 UE5 UNarrativePathFollowingComp（继承 UPathFollowingComponent）。
    /// <para>
    /// Flax 无 PathFollowingComponent 基类：移动由 <see cref="AITask_LongMove"/> /
    /// MoveToLocationAndRotationTask 等托管任务驱动，它们通过
    /// <see cref="NotifyMoveRequested"/> / <see cref="NotifyMoveArrived"/> 把请求终点登记到这里，
    /// 路径结束时由控制器转发 <see cref="OnPathFinished"/>。
    /// 本组件因此承担 UE5 中 <c>UNarrativePathFollowingComp::OnPathFinished</c>（缓存 OriginalMoveRequestGoalLocation）
    /// 与 <c>ANarrativeNPCController::OnMoveComplete</c>（NarrativeNPCController.cpp:308-364 的部分路径自救）两段职责。
    /// </para>
    /// </summary>
    public class NarrativePathFollowingComp : Script
    {
        /// <summary>缓存的上一次目标位置（对应 UE5 <c>CachedLastDestination</c>，OnPathFinished 里由请求终点赋值）。</summary>
        [NonSerialized]
        public Vector3 CachedLastDestination = Vector3.Zero;

        /// <summary>本次移动请求的原始终点。对应 UE5 <c>UPathFollowingComponent::OriginalMoveRequestGoalLocation</c>。</summary>
        [NonSerialized]
        public Vector3 OriginalMoveRequestGoalLocation = Vector3.Zero;

        /// <summary>本次移动请求的接受半径（用于 <see cref="DidMoveReachGoal"/> 判定）。</summary>
        [NonSerialized]
        public float MoveRequestAcceptanceRadius = 50f;

        /// <summary>
        /// 跳跃下落的初速度（对应 UE5 LaunchVect = 水平 300 + 垂直 250，cm/s）。
        /// 端口常量按同尺度（本插件 1 单位 = 1 cm）换算。
        /// </summary>
        public float JumpOffLaunchHorizontalSpeed = 300f;
        public float JumpOffLaunchVerticalSpeed = 250f;

        /// <summary>方向与发射向量夹角阈值（度）。UE5 :349 判定 dot &lt; cos(25°) 才发射。</summary>
        public float LaunchFacingDegrees = 25f;

        /// <summary>自救发射的重力加速度（单位/秒²，Flax 无移动组件 LaunchCharacter，用弹道积分近似）。</summary>
        public float LaunchGravity = -981f;

        /// <summary>自救发射的最大持续时间（秒），到时停止弹道。</summary>
        public float LaunchMaxDuration = 2f;

        private NarrativeNPCController _ownerController;

        // ③ 转身朝向后延迟一拍的穿越/攀爬重试（UE5 里转身与 mantle 检测同帧由 CMC tick 完成，
        // Flax 无 tick 内 mantle 扫描，这里转身 → 下一帧调 TryAttachWarp 复现「mantle facing」恢复）。
        private NarrativeCharacter _pendingClimbCharacter;
        private float _pendingClimbDelay;

        /// <summary>转身完成后到发起攀爬尝试的间隔（秒，等朝向生效一帧）。</summary>
        public float PendingClimbRetryDelay = 0.15f;

        // 自救发射（jump-off）运行态：Flax 无 LaunchCharacter，这里用弹道积分在 LaunchMaxDuration 内推进位置
        private bool _bLaunching;
        private Vector3 _launchVelocity;
        private float _launchTimer;
        private float _launchFloorZ;
        private Action _abortMoveHandler;

        /// <summary>
        /// 弹道自救是否正在由本组件积分重力（缺陷2 的门禁读取端）：
        /// <c>Character/NarrativeCharacterMovement.StepGravityAndGround</c> 在此期间跳过自身的
        /// Falling 积分，避免同一条重力被施加两次。只读，不改变本组件任何行为。
        /// </summary>
        public bool IsLaunchActive => _bLaunching;

        public override void OnEnable()
        {
            base.OnEnable();
            _ownerController = Actor != null ? Actor.GetScript<NarrativeNPCController>() : null;
        }

        public override void OnUpdate()
        {
            // 转身后延迟重试的穿越/攀爬请求（mantle facing 恢复环的后半段）。
            if (_pendingClimbCharacter != null)
            {
                _pendingClimbDelay -= Time.DeltaTime;
                if (_pendingClimbDelay <= 0f)
                {
                    var ch = _pendingClimbCharacter;
                    _pendingClimbCharacter = null;
                    ch.TryAttachWarp(true, new Float2(0f, 1f), -1f);
                }
            }

            if (_bLaunching)
            {
                // 自救弹道途中抓到攀爬面：攀爬接管位移，停弹道（避免两套积分互写位置）。
                var mv = Actor != null ? Actor.GetScript<NarrativePro.Character.NarrativeCharacterMovement>() : null;
                if (mv != null && mv.IsClimbing())
                {
                    StopLaunch();
                    return;
                }

                float dt = Time.DeltaTime;
                _launchTimer += dt;
                _launchVelocity.Z += LaunchGravity * dt;

                if (Actor != null)
                {
                    Actor.Position += _launchVelocity * dt;

                    // 近似落地：低于起跳平面且已在下落则停
                    if (Actor.Position.Z <= _launchFloorZ && _launchVelocity.Z < 0f)
                    {
                        var pos = Actor.Position;
                        pos.Z = _launchFloorZ;
                        Actor.Position = pos;
                        StopLaunch();
                        return;
                    }
                }

                if (_launchTimer >= LaunchMaxDuration)
                {
                    StopLaunch();
                }
            }
        }

        /// <summary>
        /// 移动任务发起一次请求时登记终点。对应 UE5 SimpleMove 内部记录 OriginalMoveRequestGoalLocation。
        /// </summary>
        /// <param name="goalLocation">请求终点</param>
        /// <param name="acceptanceRadius">接受半径</param>
        /// <param name="abortHandler">中断当前移动的回调（对应 UE5 <c>AbortMove</c>），可为 null</param>
        public virtual void NotifyMoveRequested(Vector3 goalLocation, float acceptanceRadius, Action abortHandler = null)
        {
            OriginalMoveRequestGoalLocation = goalLocation;
            MoveRequestAcceptanceRadius = acceptanceRadius;
            _abortMoveHandler = abortHandler;
        }

        /// <summary>移动真正抵达终点后清空请求缓存（避免完成后的其它事件误触发自救）。对应 UE5 抵达时 DidMoveReachGoal()==true。</summary>
        public virtual void NotifyMoveArrived()
        {
            OriginalMoveRequestGoalLocation = Vector3.Zero;
            _abortMoveHandler = null;
        }

        /// <summary>
        /// 本次请求的终点是否已抵达（近似 UE5 <c>UPathFollowingComponent::DidMoveReachGoal</c>）：
        /// 比较当前 Actor 位置与请求终点的 2D 距离是否在接受半径内。
        /// </summary>
        public bool DidMoveReachGoal()
        {
            if (Actor == null || IsNearlyZeroVector(OriginalMoveRequestGoalLocation)) return true;

            Vector3 delta = Actor.Position - OriginalMoveRequestGoalLocation;
            delta.Z = 0f;
            return delta.Length <= MoveRequestAcceptanceRadius;
        }

        /// <summary>Flax 无 Vector3::IsNearlyZero，等价判定（UE5 FVector::IsNearlyZero 容差）。</summary>
        private static bool IsNearlyZeroVector(Vector3 value, float tolerance = 0.01f)
        {
            return value.LengthSquared < tolerance * tolerance;
        }

        /// <summary>
        /// 路径完成时调用。对应 UE5 <c>UNarrativePathFollowingComp::OnPathFinished</c>（缓存请求终点）
        /// 加上 <c>ANarrativeNPCController::OnMoveComplete</c>（NarrativeNPCController.cpp:308-364）的部分路径自救。
        /// </summary>
        /// <param name="result">路径跟随结果</param>
        public virtual void OnPathFinished(EPathFollowingResult result)
        {
            // UE5 NarrativePathFollowingComp.cpp:5-9：完成时把原始请求终点缓存进 CachedLastDestination
            CachedLastDestination = OriginalMoveRequestGoalLocation;

            if (result != EPathFollowingResult.Success)
            {
                return;
            }

            // 成功但没到达终点（部分路径）：尝试穿越自救（NarrativeNPCController.cpp:318-363）
            if (DidMoveReachGoal() || IsNearlyZeroVector(CachedLastDestination))
            {
                return;
            }

            var character = Actor != null ? Actor.GetScript<NarrativeCharacter>() : null;
            if (character == null)
            {
                NarrativeLog.Log($"路径跟随完成: {result}（无角色脚本，跳过部分路径自救）");
                return;
            }

            RecoverFromPartialPath(character);
        }

        /// <summary>
        /// 部分路径自救。对应 UE5 OnMoveComplete :320-362 三分支：
        /// ① TryAttachWarp 穿越；② 目标在下方：中断移动并朝目标弹道发射（jump-off）；
        /// ③ 目标在上方：转身朝向目标，让 mantle 有机会命中。
        /// Flax 无根运动 warp/montage：①由 <see cref="NarrativeCharacter.TryAttachWarp"/> 表达请求，
        /// 现由 <c>NarrativeCharacterMovement</c> 的攀爬/跨越积分真实执行（IClimbable 或旧式 ledge）；
        /// ③ 转身后经 <see cref="PendingClimbRetryDelay"/> 延迟一拍再重试①，复现 UE5 转身→mantle 的恢复环。
        /// </summary>
        protected virtual void RecoverFromPartialPath(NarrativeCharacter character)
        {
            Vector3 offset = CachedLastDestination - Actor.Position;

            // ① 尝试 AttachWarp 穿越（UE5: TryAttachWarp(true, FVector2D(0,1), -1)）
            if (character.TryAttachWarp(true, new Float2(0f, 1f), -1f))
            {
                return;
            }

            if (CachedLastDestination.Z < Actor.Position.Z)
            {
                // ② 只向下跳：先中断当前移动，移动层随后会重新评估（UE5:327 AbortMove(UserAbort)）
                AbortMove();

                Vector3 offsetFlat = offset;
                offsetFlat.Z = 0f;
                if (offsetFlat.Length < 0.001f) return;

                Vector3 launchVelocity = offsetFlat / offsetFlat.Length * JumpOffLaunchHorizontalSpeed
                                         + new Vector3(0f, 0f, JumpOffLaunchVerticalSpeed);

                // 把焦点设在发射方向（UE5:334 SetFocalPoint(ActorLocation + LaunchVect, Move)）
                _ownerController?.SetFocalPoint(Actor.Position + launchVelocity);

                // 仅当角色朝向与发射向量夹角在阈值内才发射（UE5:338-341 的 Dot 判定原样移植，
                // 注意 UE5 源码条件是 dot < cos(25°)，注释与代码不一致，此处忠实于代码）
                Vector3 facing = Actor.Transform.Forward;
                facing.Z = 0f;
                Vector3 launchFlat = launchVelocity;
                launchFlat.Z = 0f;
                float dot = facing.Length > 0.001f && launchFlat.Length > 0.001f
                    ? Vector3.Dot(facing.Normalized, launchFlat.Normalized)
                    : 1f;

                if (dot < Math.Cos(LaunchFacingDegrees * Mathf.DegreesToRadians))
                {
                    // Flax 无 LaunchCharacter：用弹道积分近似「起跳后抛物线下落」，落地/超时即停
                    _bLaunching = true;
                    _launchTimer = 0f;
                    _launchFloorZ = CachedLastDestination.Z;
                    _launchVelocity = launchVelocity;

                    // 发射后清除焦点（UE5:346 ClearFocus(Move)）
                    _ownerController?.ClearFocalPoint();
                }
            }
            else
            {
                // ③ 想 mantle 但没成：把朝向转到目标方向，给 mantle/穿越检测创造条件（UE5:352-356，保留 pitch/roll 只改 yaw）
                Vector3 facingOffset = offset;
                facingOffset.Z = 0f;
                if (facingOffset.Length < 0.001f) return;

                Vector3 targetEuler = Quaternion.LookRotation(facingOffset.Normalized).EulerAngles;
                Vector3 currentEuler = Actor.Orientation.EulerAngles;
                Actor.Orientation = Quaternion.Euler(currentEuler.X, targetEuler.Y, currentEuler.Z);

                // 转身完成 → 延迟一拍重试穿越/攀爬（UE5 的 mantle facing 恢复环：
                // NarrativeNPCController.cpp:352-356 转身由 CMC tick 立刻复检，这里在 OnUpdate 补）。
                _pendingClimbCharacter = character;
                _pendingClimbDelay = PendingClimbRetryDelay;
            }
        }

        /// <summary>
        /// 中断当前移动。对应 UE5 <c>AbortMove(*this, FPathFollowingResultFlags::UserAbort)</c>。
        /// Flax 中移动由登记的托管任务驱动，这里回调其提供的中断入口。
        /// </summary>
        public void AbortMove()
        {
            _abortMoveHandler?.Invoke();
        }

        /// <summary>停止自救弹道。</summary>
        private void StopLaunch()
        {
            _bLaunching = false;
            _launchVelocity = Vector3.Zero;
            _launchTimer = 0f;
            // 自救发射结束：这次请求视为处理完，清缓存终点防止重复触发
            OriginalMoveRequestGoalLocation = Vector3.Zero;
        }
    }
}
