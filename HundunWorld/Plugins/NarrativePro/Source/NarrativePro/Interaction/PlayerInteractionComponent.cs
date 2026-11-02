using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 玩家交互组件。包含 NPC 不需要的交互追踪（trace）逻辑。
    /// 适配 UE5 UPlayerInteractionComponent，移除复制/RPC，改为本地逻辑 + 事件回调。
    /// </summary>
    public class PlayerInteractionComponent : NarrativeInteractionComponent
    {
        /// <summary>发现新的可交互对象时触发</summary>
        public event Action<NarrativeInteractableComponent> OnFoundInteractable;

        /// <summary>失去当前可交互对象时触发</summary>
        public event Action<NarrativeInteractableComponent> OnLostInteractable;

        /// <summary>按下交互键时触发</summary>
        public event Action<NarrativeInteractionComponent> OnInteractPressed;

        /// <summary>松开交互键时触发</summary>
        public event Action<NarrativeInteractionComponent> OnInteractReleased;

        /// <summary>当前正注视的可交互对象</summary>
        public NarrativeInteractableComponent ViewedInteractable { get; protected set; }

        /// <summary>上次交互检查时间</summary>
        public float LastInteractionCheckTime { get; set; } = 0f;

        /// <summary>本地玩家是否按住交互键</summary>
        public bool bInteractHeld { get; set; } = false;

        /// <summary>
        /// 长按取消窗口（秒）：按住交互键不超过该时长就松手 → 立即完成本次交互（短按即交互）；
        /// 超过它仍未完成才松手 → 视为放弃并取消（对应 UE5 held-interact 的松手/取消语义）。
        /// </summary>
        public float HeldInteractCancelTime { get; set; } = 3f;

        /// <summary>
        /// 按住交互期间是否锁定当前注视目标。节流重检（<see cref="InteractionCheckFrequency"/>）
        /// 会因视角微调把目标换成另一个候选或直接判空，若不锁定就会每次重检都打断正在进行的按住交互。
        /// </summary>
        public bool bLockHeldInteractTarget { get; set; } = true;

        /// <summary>本次按下交互键的游戏时间（-1 表示未按下），供 <see cref="HeldInteractCancelTime"/> 判定。</summary>
        private float _interactHoldStartGameTime = -1f;

        /// <summary>
        /// 按住交互起始时，注视目标宿主 Actor 上的能力系统组件。
        /// 用于在宿主于「按住期间」死亡时强制取消交互（对尸体的进行中对话/交互），
        /// 而不影响「先死亡、后按下」发起的搜刮（起始即记录为死亡状态，不会误伤）。
        /// </summary>
        private NarrativePro.GAS.NarrativeAbilitySystemComponent _interactHeldOwnerAsc;

        /// <summary>触发交互的输入动作名称列表</summary>
        public List<string> InteractionInputs { get; set; } = new List<string>();

        /// <summary>当前交互剩余时间（按住时间）。IdleRemainingInteractTime 表示空闲。</summary>
        public float RemainingInteractTime { get; set; } = -999f;

        /// <summary>
        /// 交互检查频率（秒）。0 = 每帧检查。
        /// <para>
        /// 默认 0.15s：一次检查要跑射线 + 可选 SphereCastAll + 全可交互注册表扫描，
        /// 每帧做会白烧 CPU（UE5 侧同样是节流检查）。注视手感在 0.15s 档无可感差别，
        /// 按住交互的目标锁定由 <see cref="bLockHeldInteractTarget"/> / <see cref="IsHeldTargetStillValid"/>
        /// 维持，不会因节流而抖动或中断。
        /// </para>
        /// </summary>
        public float InteractionCheckFrequency { get; set; } = 0.15f;

        /// <summary>交互检查的最大距离</summary>
        public float InteractionCheckDistance { get; set; } = 1000f;

        /// <summary>
        /// 邻近回退选中无碰撞体可交互对象所需的视野锥余弦（与相机朝向的点积下限）。
        /// 0.85 ≈ ±31.8°，够窄以免背后/侧面的 NPC 抢走注视目标，够宽让面前的 NPC 稳定可交互。
        /// </summary>
        public float InteractionViewConeCos { get; set; } = 0.85f;

        /// <summary>若大于 0，使用球体追踪而非射线</summary>
        public float InteractionCheckSphereRadius { get; set; } = 0f;

        /// <summary>摄像机引用（用于交互追踪的起点和方向）</summary>
        public FlaxEngine.Camera InteractionCamera { get; set; }

        /// <summary>交互空闲哨兵值（对应 UE5 的 RemainingInteractTime = -999）。</summary>
        public const float IdleRemainingInteractTime = -999f;

        private float _nextCheckTime = 0f;

        /// <summary>
        /// 场景中活跃的本地玩家交互组件注册表（Flax 无 UE5 的 GetOwningPlayerController，
        /// HUD 层需要靠它拿到当前玩家交互组件）。
        /// </summary>
        private static readonly List<PlayerInteractionComponent> _activeInstances = new List<PlayerInteractionComponent>();

        /// <summary>当前本地玩家的交互组件（不存在时为 null）。</summary>
        public static PlayerInteractionComponent LocalPlayerInteraction
        {
            get
            {
                for (int i = _activeInstances.Count - 1; i >= 0; i--)
                {
                    var instance = _activeInstances[i];
                    if (instance != null && instance.IsActive()) return instance;
                }
                return null;
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            _nextCheckTime = 0f;
            _interactHoldStartGameTime = -1f;
            _interactHeldOwnerAsc = null;
            bInteractHeld = false;
            RemainingInteractTime = IdleRemainingInteractTime;

            if (!_activeInstances.Contains(this)) _activeInstances.Add(this);
        }

        // Flax 的 ActorComponent::Activate/Deactivate 等价物就是 Script.Enabled 开关，
        // 因此 UE5 Deactivate() 里的 ClearViewedInteractable 放在 OnDisable 中执行。
        public override void OnDisable()
        {
            ClearViewedInteractable();
            _activeInstances.Remove(this);
            base.OnDisable();
        }

        /// <summary>每帧执行交互检查与按住交互进度。对应 UE5 TickComponent。</summary>
        public override void OnUpdate()
        {
            // 对应 UE5 的 IsActive() 门：组件不活跃时不做任何交互检查
            if (!IsActive()) return;

            // 缺陷1（SequencerControlled 读取端）：玩家被序列绑定期间不接受交互——
            // 取消进行中的按住并清空注视目标（UE5 序列期间 PlayerInput IgnoreAllInput），
            // 序列结束标签回收后下一帧自动恢复正常检查。
            if (OwningPawn != null && NarrativePro.Character.NarrativeCharacterMovement.ActorIsSequencerControlled(OwningPawn))
            {
                if (ViewedInteractable != null) ClearViewedInteractable();   // 内部含 EndInteract
                else if (bInteractHeld) EndInteract();
                return;
            }

            float dt = Time.DeltaTime;

            // 目标在按住期间被销毁或宿主死亡 → 立即丢弃注视并取消按住，
            // 否则 bInteractHeld 会黏在尸体上，直到下一次节流重检（最长 InteractionCheckFrequency）才纠正，
            // 期间还可能继续对坏掉的目标倒计时并「完成」一次本该失效的交互。
            if (bInteractHeld && !IsHeldTargetStillInteractable())
            {
                CancelHeldInteractBecauseTargetLost();
                return;
            }

            if (InteractionCheckFrequency <= 0f || Time.GameTime >= _nextCheckTime)
            {
                PerformInteractionCheck(dt);
            }

            if (bInteractHeld && ViewedInteractable != null)
            {
                if (RemainingInteractTime > 0f)
                {
                    RemainingInteractTime -= dt;
                }

                if (RemainingInteractTime <= 0f && RemainingInteractTime > IdleRemainingInteractTime + 1f)
                {
                    CompleteHeldInteract();
                    RemainingInteractTime = IdleRemainingInteractTime;
                }
            }
        }

        /// <summary>
        /// 按住交互键计时结束后真正执行交互。对应 UE5 PlayerInteractionComponent.cpp:91-129：
        /// 配置了槽位时走槽位认领 + 偷取握手；没有槽位时保留旧式即时交互。
        /// </summary>
        protected virtual void CompleteHeldInteract()
        {
            var viewed = ViewedInteractable;
            if (viewed == null) return;

            if (viewed.InteractionSlots.Count > 0)
            {
                // 有槽位：挑选最佳槽位（含可偷取的已占用槽位）
                int bestInteractionSlot = viewed.GetBestAvailableSlot(this, viewed.GetAvailableSlots(this, true));

                if (bestInteractionSlot != -1 && bestInteractionSlot < viewed.SlotStatuses.Count)
                {
                    NarrativeInteractionComponent stealingFrom = null;

                    // 若最佳槽位已被占用，说明这是一次偷取
                    if (viewed.SlotStatuses[bestInteractionSlot].SlotStatus == EInteractionSlotStatus.Occupied)
                    {
                        stealingFrom = viewed.SlotStatuses[bestInteractionSlot].SlotUser;
                    }

                    // ClaimInteractionSlot 内部会把现有占用者请走（偷取握手）
                    if (ClaimInteractionSlot(viewed, bestInteractionSlot))
                    {
                        RunInteractBehavior(stealingFrom != null, stealingFrom);
                    }
                }
            }
            else
            {
                // 旧式无槽位即时交互
                bool bInteracted = viewed.Interact(OwningPawn, this);

                if (bInteracted && ViewedInteractable != null)
                {
                    // 对应 UE5 PlayerInteractionComponent.cpp:123-124：广播的是可交互对象的宿主 Actor
                    BroadcastOnBeginUseInteractable(ViewedInteractable.Actor, ViewedInteractable);
                    BroadcastOnFinishUseInteractable(ViewedInteractable.Actor, ViewedInteractable);
                }
            }
        }

        /// <summary>
        /// 执行交互检查（射线，未命中可交互对象时回退球体扫描）。
        /// 对应 UE5 PerformInteractionCheck：用视点（而非 Pawn）定方向，
        /// 并把 Pawn 投影到视线上来抵消长镜头臂对可交互距离的影响。
        /// </summary>
        public virtual void PerformInteractionCheck(float deltaTime)
        {
            // 检查频率
            if (InteractionCheckFrequency > 0f && Time.GameTime < _nextCheckTime) return;
            _nextCheckTime = Time.GameTime + InteractionCheckFrequency;
            LastInteractionCheckTime = Time.GameTime;

            if (OwningController == null || OwningPawn == null)
            {
                ClearViewedInteractable();
                return;
            }

            Vector3 eyesLoc;
            Vector3 aimDir;
            FlaxEngine.Camera cam = InteractionCamera ?? FlaxEngine.Camera.MainCamera;
            if (cam != null)
            {
                eyesLoc = cam.Position;
                aimDir = cam.Direction;
            }
            else
            {
                eyesLoc = OwningPawn.Position + new Vector3(0f, 50f, 0f);
                aimDir = OwningPawn.Direction;
            }

            if (aimDir.LengthSquared <= 0.0001f) aimDir = Vector3.Forward;
            aimDir = Vector3.Normalize(aimDir);

            // 镜头臂补偿：把 Pawn 投影到视线上作为追踪起点
            Vector3 focalLoc = eyesLoc + aimDir * 1024f;
            float projection = Vector3.Dot(OwningPawn.Position - focalLoc, aimDir);
            Vector3 traceStart = focalLoc + aimDir * projection;
            Vector3 traceEnd = traceStart + aimDir * InteractionCheckDistance;

            NarrativeInteractableComponent found = null;
            Vector3 hitPoint = Vector3.Zero;

            RayCastHit lineHit;
            if (Physics.RayCast(traceStart, aimDir, out lineHit, InteractionCheckDistance))
            {
                found = FindInteractableOnActor(lineHit.Collider);
                hitPoint = lineHit.Point;
            }

            // 射线没打中可交互对象时，用球体扫描补一次（对应 UE5 的 InteractionCheckSphereRadius 分支）
            if (found == null && InteractionCheckSphereRadius > 0f)
            {
                if (Physics.SphereCastAll(traceStart, InteractionCheckSphereRadius, aimDir, out var hits, InteractionCheckDistance))
                {
                    foreach (var hit in hits)
                    {
                        var interactable = FindInteractableOnActor(hit.Collider);
                        if (interactable != null)
                        {
                            found = interactable;
                            hitPoint = hit.Point;
                            break;
                        }
                    }
                }
            }

            // 视线扫描全部落空时的邻近回退：Flax 里代码生成的 Actor（EmptyActor 上的 NPC）
            // 没有 PhysX 碰撞体，射线永远不会命中它们 —— 没有这一步，
            // NPCInteractable（对话/搜刮/交易入口）在运行期根本不可能被注视到。
            // 只在射线/球体都没命中时启用，且要求目标落在视野锥内、可交互距离内，
            // 因此不会改变任何有碰撞体可交互对象（椅子/拾取物/载具）的既有选中结果。
            if (found == null)
            {
                found = FindInteractableByViewCone(traceStart, aimDir, out Vector3 coneHitPoint);
                if (found != null)
                {
                    hitPoint = coneHitPoint;
                }
            }

            // 只有活跃的可交互组件才参与注视
            if (found != null && !found.IsActive())
            {
                found = null;
            }

            // 可交互距离检查：以命中点为基准
            if (found != null)
            {
                float distance = Vector3.Distance(OwningPawn.Position, hitPoint);
                if (distance > found.InteractionDistance)
                {
                    found = null;
                }
            }

            // 按住交互期间的目标锁：节流重检可能因视角微调把目标换成另一个候选或判空，
            // 换目标/判空都会走 EndInteract 从而打断正在计时的交互。当前目标仍然有效时保持不变。
            if (bLockHeldInteractTarget && bInteractHeld && ViewedInteractable != null
                && !ReferenceEquals(found, ViewedInteractable)
                && IsHeldTargetStillValid(ViewedInteractable, traceStart, aimDir))
            {
                found = ViewedInteractable;
            }

            if (found != null)
            {
                SetViewedInteractable(found);
            }
            else
            {
                ClearViewedInteractable();
            }
        }

        /// <summary>
        /// 无碰撞体可交互对象的邻近回退查询：取视野锥（默认约 ±30°）内、各自
        /// <c>InteractionDistance</c> 范围内最近的一个。
        /// 对应 UE5 靠碰撞体命中即可完成的选择；Flax 代码生成的 NPC 没有 PhysX 碰撞体，
        /// 少了这一步 NPC 的对话/搜刮/交易入口在运行期不可达。
        /// </summary>
        protected virtual NarrativeInteractableComponent FindInteractableByViewCone(
            Vector3 traceStart, Vector3 aimDir, out Vector3 outHitPoint)
        {
            outHitPoint = Vector3.Zero;

            // 候选源（缺陷3：InteractionSubsystem.GetInteractableActors 的消费方）：优先子系统缓存，
            // 对应 UE5 注视查询走 UInteractionSubsystem::GetInteractableActors；子系统无场景宿主时
            // 退回静态活跃登记表（两个登记面同源，见 InteractableComponent.cs 的 OnEnable/OnDisable 同时写两边）。
            var subsystem = NarrativePro.Interaction.InteractionSubsystem.Instance;
            var subsystemCache = subsystem != null ? subsystem.GetInteractableActors() : null;
            System.Collections.Generic.IEnumerable<NarrativeInteractableComponent> candidates =
                subsystemCache != null && subsystemCache.Count > 0
                    ? (System.Collections.Generic.IEnumerable<NarrativeInteractableComponent>)subsystemCache
                    : NarrativeInteractableComponent.ActiveInteractables;

            NarrativeInteractableComponent best = null;
            float bestDistance = float.MaxValue;
            Vector3 bestPoint = Vector3.Zero;

            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;
                if (!candidate.IsActive()) continue;

                var actor = candidate.Actor;
                if (actor == null) continue;
                if (OwningPawn != null && ReferenceEquals(actor, OwningPawn)) continue;

                Vector3 toTarget = actor.Position - traceStart;
                float distance = toTarget.Length;
                if (distance <= 0.0001f) continue;

                float range = candidate.InteractionDistance > 0f ? candidate.InteractionDistance : InteractionCheckDistance;
                if (distance > range) continue;
                if (distance > InteractionCheckDistance) continue;

                // 视野锥判定：与相机朝向的夹角余弦
                float facing = Vector3.Dot(toTarget * (1f / distance), aimDir);
                if (facing < InteractionViewConeCos) continue;

                // 遮挡校验（逐个候选，只对「比当前最优更近」的候选做，省下无用的射线）：
                // 邻近回退不允许玩家隔着墙交互。判定必须落在每个候选身上，而不是只验最终选出的
                // 那一个 —— 否则「锥内最近的 NPC 在墙后」会把整条回退一票否决，
                // 而视线畅通的次近 NPC 反而永远选不出来（旧写法两种结果都错）。
                if (distance >= bestDistance) continue;
                if (!HasClearLineOfSight(candidate, actor.Position, traceStart)) continue;

                bestDistance = distance;
                best = candidate;
                bestPoint = actor.Position;
            }

            if (best == null) return null;

            outHitPoint = bestPoint;
            return best;
        }

        /// <summary>
        /// 注视射线起点的自遮挡前移量（Flax 的场景查询不排除任何 Actor，
        /// 射线起点常与玩家自身碰撞体重合，不前移会把一切判成被遮挡）。
        /// </summary>
        public const float LineOfSightStartOffset = 60f;

        /// <summary>
        /// 目标与注视起点之间是否没有遮挡。用与射线分支同一 API
        /// （<see cref="Physics.RayCast(Vector3,Vector3,out RayCastHit,float)"/>，见 <see cref="PerformInteractionCheck"/>）：
        /// 无命中 或 命中物属于目标自身 / 玩家自身（含其子 Actor）→ 通；
        /// 命中物上解析出的交互组件正是该目标 → 通（可交互对象自己的碰撞体不算墙）。
        /// </summary>
        protected virtual bool HasClearLineOfSight(
            NarrativeInteractableComponent target, Vector3 targetPoint, Vector3 from)
        {
            Vector3 delta = targetPoint - from;
            float length = delta.Length;
            if (length <= LineOfSightStartOffset + 1f) return true;

            Vector3 dir = delta * (1f / length);
            Vector3 start = from + dir * LineOfSightStartOffset;
            float maxDistance = length - LineOfSightStartOffset - 1f;
            if (maxDistance <= 0f) return true;

            if (!Physics.RayCast(start, dir, out var losHit, maxDistance)) return true;

            var hitActor = losHit.Collider;
            if (hitActor == null) return true;

            var targetActor = target != null ? target.Actor : null;
            for (Actor node = hitActor; node != null; node = node.Parent)
            {
                if (targetActor != null && ReferenceEquals(node, targetActor)) return true;
                if (OwningPawn != null && ReferenceEquals(node, OwningPawn)) return true;
                if (OwningController != null && ReferenceEquals(node, OwningController)) return true;
            }

            return FindInteractableOnActor(hitActor) == target;
        }

        /// <summary>
        /// 按住交互期间当前注视目标是否仍然可选：活跃、宿主存在、仍在自身
        /// <c>InteractionDistance</c> 与交互距离内、落在视野锥内、视线未被遮挡。
        /// 供节流重检维持目标锁（避免按住交互被自己的注视刷新打断）。
        /// </summary>
        protected virtual bool IsHeldTargetStillValid(
            NarrativeInteractableComponent held, Vector3 traceStart, Vector3 aimDir)
        {
            if (held == null || !held.IsActive()) return false;

            var actor = held.Actor;
            if (actor == null) return false;

            Vector3 toTarget = actor.Position - traceStart;
            float distance = toTarget.Length;
            if (distance <= 0.0001f) return true;

            float range = held.InteractionDistance > 0f ? held.InteractionDistance : InteractionCheckDistance;
            if (distance > range) return false;
            if (distance > InteractionCheckDistance) return false;

            Vector3 dir = toTarget * (1f / distance);
            if (Vector3.Dot(dir, aimDir) < InteractionViewConeCos) return false;

            return HasClearLineOfSight(held, actor.Position, traceStart);
        }

        /// <summary>查找 Actor 上的 NarrativeInteractableComponent。</summary>
        private NarrativeInteractableComponent FindInteractableOnActor(Actor hitActor)
        {
            if (hitActor == null) return null;

            // Flax 的碰撞体是 Actor（BoxCollider/SphereCollider/CapsuleCollider），
            // 交互组件通常挂在它的宿主 Actor 上，因此沿 Parent 向上找一层
            // （与 Vehicles/NarrativeVehicleBase.cs:309 的命中归因同一走法）。
            for (var actor = hitActor; actor != null; actor = actor.Parent)
            {
                var interactable = actor.GetScript<NarrativeInteractableComponent>();
                if (interactable != null) return interactable;
            }

            return null;
        }

        /// <summary>清除当前注视的可交互对象。对应 UE5 ClearViewedInteractable。</summary>
        public void ClearViewedInteractable()
        {
            if (ViewedInteractable != null)
            {
                var lost = ViewedInteractable;
                ViewedInteractable = null;

                lost.EndFocus(OwningPawn, this);
                OnLostInteractable?.Invoke(lost);

                // 注视消失时取消按住中的交互
                EndInteract();
            }
        }

        /// <summary>设置当前注视的可交互对象。对应 UE5 SetViewedInteractable。</summary>
        public void SetViewedInteractable(NarrativeInteractableComponent interactable)
        {
            if (interactable == ViewedInteractable) return;

            // 切换目标时先结束进行中的交互与旧目标的聚焦
            EndInteract();

            if (ViewedInteractable != null)
            {
                ViewedInteractable.EndFocus(OwningPawn, this);
            }

            ViewedInteractable = interactable;

            if (ViewedInteractable != null)
            {
                ViewedInteractable.BeginFocus(OwningPawn, this);
                OnFoundInteractable?.Invoke(ViewedInteractable);
            }
        }

        /// <summary>
        /// 开始交互（按下交互键）。对应 UE5 BeginInteract：
        /// 只置位 bInteractHeld 并起计时，真正的交互完成由 OnUpdate 的计时到期分支或
        /// <see cref="ReleaseInteract"/>（松手）处理。
        /// <para>
        /// 幂等护栏：按住状态下重复按下直接返回。控制器对 <c>Narrative.Input.Interact</c>
        /// 同时做 ASC 标签广播与组件直调（NarrativePlayerController.AbilityInputPressed），
        /// 若日后有人把绑定了该输入标签的能力授予 ASC，两条入口汇聚到这里也只会启动一次
        /// 按住交互状态机（ReleaseInteract/EndInteract 本就自校验 bInteractHeld）。
        /// </para>
        /// </summary>
        public virtual void BeginInteract()
        {
            if (bInteractHeld) return;

            // 节流档下注视结果最多可能旧一个检查周期，按下瞬间补一次检查，
            // 确保交互落在当前准星目标上（此时还没置 bInteractHeld，重检不会打断本帧的按下）
            if (InteractionCheckFrequency > 0f
                && Time.GameTime - LastInteractionCheckTime > InteractionCheckFrequency)
            {
                PerformInteractionCheck(0f);
            }

            bInteractHeld = true;
            _interactHoldStartGameTime = Time.GameTime;
            OnInteractPressed?.Invoke(this);

            if (ViewedInteractable == null) return;

            // 记录起始时目标宿主的 ASC：按住期间该角色死亡即视为目标坏掉，需要强制取消（对尸体的进行中交互）。
            // 「先死亡、后按下」的搜刮会在起始时就记录为已死亡，因此不会被这条即时取消误伤。
            _interactHeldOwnerAsc = ViewedInteractable.Actor != null
                ? ViewedInteractable.Actor.GetScript<NarrativePro.GAS.NarrativeAbilitySystemComponent>()
                : null;

            string errorText;
            if (ViewedInteractable.CanInteract(OwningPawn, this, out errorText))
            {
                ViewedInteractable.BeginInteract(OwningPawn, this);
                RemainingInteractTime = ViewedInteractable.InteractionTime;

                // InteractionTime 为 0 时 UE5 会立刻把 RemainingInteractTime 置 0，
                // 下一帧的计时到期分支即完成交互（含槽位与偷取路径）。
            }
            else
            {
                if (!string.IsNullOrEmpty(errorText))
                {
                    NarrativeLog.Log($"[Interaction] 不可交互: {errorText}");
                }
                RemainingInteractTime = IdleRemainingInteractTime;
            }
        }

        /// <summary>
        /// 松开交互键。对应 UE5 held-interact 的 release 分支：
        /// 在 <see cref="HeldInteractCancelTime"/> 内松手 → 立即完成本次交互（短按即交互）；
        /// 按住超时才松手 → 取消本次交互。
        /// 计时自然到期时已由 OnUpdate 完成并把剩余时间复位，这里不会重复触发交互。
        /// </summary>
        public virtual void ReleaseInteract()
        {
            if (!bInteractHeld) return;

            var viewed = ViewedInteractable;
            float heldSeconds = _interactHoldStartGameTime >= 0f
                ? Time.GameTime - _interactHoldStartGameTime
                : 0f;

            // 仍处于计时中（RemainingInteractTime 大于空闲哨兵）= 本次交互还没落地
            bool bPendingInteract = viewed != null && RemainingInteractTime > IdleRemainingInteractTime + 1f;

            if (bPendingInteract && heldSeconds <= HeldInteractCancelTime)
            {
                CompleteHeldInteract();
                RemainingInteractTime = IdleRemainingInteractTime;
            }

            EndInteract();
        }

        /// <summary>结束交互（松开交互键）。对应 UE5 EndInteract。</summary>
        public virtual void EndInteract()
        {
            if (ViewedInteractable != null)
            {
                ViewedInteractable.EndInteract(OwningPawn, this);
                RemainingInteractTime = IdleRemainingInteractTime;
            }

            bInteractHeld = false;
            _interactHoldStartGameTime = -1f;
            _interactHeldOwnerAsc = null;
            OnInteractReleased?.Invoke(this);
        }

        /// <summary>
        /// 强制丢弃注视目标并取消按住交互（幂等）。供引导在宿主重生/换角色时清理旧的每宿主交互状态，
        /// 确保 <see cref="bInteractHeld"/> 不会黏在已被销毁的旧宿主上。
        /// </summary>
        public void ForceCancelInteraction()
        {
            if (ViewedInteractable != null) ClearViewedInteractable();   // 内部会 EndFocus + EndInteract（含 bInteractHeld 复位）
            else if (bInteractHeld) EndInteract();                       // 无注视目标但仍在按住状态：兜底复位
            _interactHeldOwnerAsc = null;
        }

        /// <summary>
        /// 按住交互的目标是否仍然可交互：组件仍活跃、宿主 Actor 仍存在，
        /// 且起始时记录的宿主 ASC 未进入死亡状态。返回 false 表示目标已在按住期间坏掉（销毁或宿主死亡）。
        /// </summary>
        private bool IsHeldTargetStillInteractable()
        {
            var viewed = ViewedInteractable;
            if (viewed == null) return true; // 没有注视目标：交给正常流程/BeginInteract 判定，不在此强制取消

            var actor = viewed.Actor;
            if (actor == null) return false;      // 交互对象被销毁（Flax：销毁后 Actor 视为 null）
            if (!viewed.IsActive()) return false; // 交互组件被停用

            // 起始时目标宿主存活、按住期间死亡 → 视为目标坏掉（对尸体的进行中对话/交互必须取消）。
            if (_interactHeldOwnerAsc != null && _interactHeldOwnerAsc.IsDead) return false;

            return true;
        }

        /// <summary>注视目标被销毁或宿主死亡导致按住中断时的统一清理。</summary>
        private void CancelHeldInteractBecauseTargetLost()
        {
            if (ViewedInteractable != null) ClearViewedInteractable();
            else if (bInteractHeld) EndInteract();
            _interactHeldOwnerAsc = null;
        }

        public override void Load()
        {
            base.Load();
            // 玩家不恢复占用状态
            OccupiedInteractable = null;
            OccupiedInteractableSlotIdx = -1;
        }
    }
}
