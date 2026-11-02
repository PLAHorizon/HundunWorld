using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 交互槽位状态。适配 UE5 EInteractionSlotStatus。
    /// </summary>
    public enum EInteractionSlotStatus
    {
        /// <summary>空闲</summary>
        Free = 0,
        /// <summary>已被瞄准（未实际占用，可用于 NPC 准备前往）</summary>
        Targeted = 1,
        /// <summary>已占用（正在交互中）</summary>
        Occupied = 2
    }

    /// <summary>
    /// 活跃交互槽位。适配 UE5 FActiveInteractionSlot。
    /// </summary>
    [Serializable]
    public class ActiveInteractionSlot
    {
        public EInteractionSlotStatus SlotStatus { get; set; } = EInteractionSlotStatus.Free;

        /// <summary>占用此槽位的交互组件</summary>
        public NarrativeInteractionComponent SlotUser { get; set; }
    }

    /// <summary>
    /// 调试可视化槽位。适配 UE5 FInteractionDebugVisualizeSlot。
    /// </summary>
    [Serializable]
    public class InteractionDebugVisualizeSlot
    {
        public Transform SlotDebugTransform { get; set; } = Transform.Identity;
        public Color SlotDebugColor { get; set; } = Color.Black;
        public string SlotDebugString { get; set; } = "";
    }

    /// <summary>
    /// 交互槽位行为。挂到槽位上的自定义行为类。
    /// 适配 UE5 UInteractionSlotBehavior。GAS 相关以字符串 ID 引用占位。
    /// </summary>
    [Serializable]
    public abstract class InteractionSlotBehavior
    {
        /// <summary>触发此槽位交互时启动的能力 ID（对应 UE 的 UNarrativeInteractAbility）</summary>
        public string SlotInteractBehaviorId { get; set; } = "";

        /// <summary>提示用户结束交互的文本</summary>
        public string FinishInteractText { get; set; } = "";

        /// <summary>默认是否可被偷窃，最终由 IsStealable 决定</summary>
        public bool bIsStealableByDefault { get; set; } = false;

        /// <summary>若非空，仅允许此列表中的交互组件使用此槽位（用于任务锁定）</summary>
        public List<NarrativeInteractionComponent> AllowedInteractors { get; set; } = new List<NarrativeInteractionComponent>();

        /// <summary>
        /// 交互此槽位时授予并激活的能力类型（对应 UE5 UInteractionSlotBehavior::SlotInteractBehavior，
        /// 即 TSubclassOf&lt;UNarrativeInteractAbility&gt;）。为 null 且 InteractAbilityScript 也为 null 时，
        /// 此槽位没有可运行的交互行为，调用方应退化为事件广播路径。
        /// </summary>
        public Type InteractAbilityType { get; set; }

        /// <summary>
        /// 直接引用一个已挂在 Owner Actor 上的交互能力脚本实例（Flax 侧替代 UE5 内联实例化配置，
        /// 因为它可以携带 Cost/Cooldown/Tags 等已序列化配置）。优先级高于 InteractAbilityType。
        /// </summary>
        public NarrativePro.GAS.NarrativeGameplayAbility InteractAbilityScript { get; set; }

        /// <summary>获取此槽位的交互能力类型。对应 UE5 UInteractionSlotBehavior::GetInteractAbility()。</summary>
        public virtual Type GetInteractAbility()
        {
            if (InteractAbilityScript != null) return InteractAbilityScript.GetType();
            if (InteractAbilityType != null) return InteractAbilityType;

            // 作者通路：UE5 的 SlotInteractBehavior 是内联实例上的
            // TSubclassOf<UNarrativeInteractAbility>（InteractableComponent.h:90-92），
            // Flax 没有 Instanced 内联对象，所以按能力 id 走 GAS 注册表换取类型。
            // 没有这一条，槽位能力（按下后由能力自己管理交互时长/结束时释放槽位）在本端口
            // 就只能靠代码硬塞 Type，内容侧一律退回事件广播。
            if (string.IsNullOrEmpty(SlotInteractBehaviorId)) return null;
            return NarrativePro.GAS.NarrativeGASAssetRegistry.ResolveAbilityTypeById(SlotInteractBehaviorId);
        }

        /// <summary>
        /// 取得此槽位可用的交互能力实例。优先返回 InteractAbilityScript（已配置、已挂在 Owner 上）；
        /// 否则按 InteractAbilityType 在 Owner Actor 上动态挂载一个新脚本。
        /// 返回 null 表示此槽位没有交互行为。
        /// </summary>
        public virtual NarrativePro.GAS.NarrativeGameplayAbility ResolveInteractAbilityInstance(Actor ownerActor)
        {
            if (InteractAbilityScript != null) return InteractAbilityScript;

            // 走 GetInteractAbility()：它除了代码直填的 Type，还认内容侧的 SlotInteractBehaviorId
            var abilityType = GetInteractAbility();
            if (abilityType == null || ownerActor == null) return null;

            // 复用 Owner 上已存在的同类型能力脚本，避免重复挂载
            foreach (var script in ownerActor.GetScripts(abilityType))
            {
                if (script is NarrativePro.GAS.NarrativeGameplayAbility existing) return existing;
            }

            if (ownerActor.AddScript(abilityType) is NarrativePro.GAS.NarrativeGameplayAbility created)
            {
                created.Enabled = true;
                return created;
            }
            return null;
        }

        /// <summary>
        /// 槽位是否允许结束当前交互。对应 UE5 UNarrativeInteractAbility::TryFinishInteraction 的否决语义
        /// （例如载具移动过快时不允许下车）。返回 false 会阻止他人偷取此槽位。
        /// </summary>
        public virtual bool TryFinishInteraction(bool bWasStolen, NarrativeInteractionComponent stealer, NarrativeInteractionComponent owner)
        {
            return true;
        }

        // GetDebugSlots 已删除（缺陷3/5：零调用方）。UE5 侧它的调用方是编辑器调试可视化
        // （Private/Interaction/InteractableComponent.cpp 的 GetDebugSlots → AInteractionDebugComponent 绘制），
        // Flax 没有等价的运行时调试绘制宿主，InteractionDebugVisualizeSlot 也无任何消费者；
        // 保留空实现只会复现「代码存在、无人调用」。

        /// <summary>此槽位是否可被偷窃。</summary>
        public virtual bool IsStealable(int ourSlot, NarrativeInteractableComponent ourInteractable, NarrativeInteractionComponent interactor)
        {
            return bIsStealableByDefault;
        }

        /// <summary>此槽位是否可被指定交互者使用。</summary>
        public virtual bool IsUsable(int ourSlot, NarrativeInteractableComponent ourInteractable, NarrativeInteractionComponent interactor)
        {
            if (AllowedInteractors == null || AllowedInteractors.Count == 0) return true;
            return AllowedInteractors.Contains(interactor);
        }
    }

    /// <summary>
    /// 交互槽位配置。适配 UE5 FInteractionSlotConfig。
    /// </summary>
    [Serializable]
    public class InteractionSlotConfig
    {
        /// <summary>槽位标签（可选）</summary>
        public GameplayTag SlotTag { get; set; } = GameplayTag.None;

        /// <summary>调试颜色</summary>
        public Color DebugColor { get; set; } = Color.Green;

        /// <summary>关联槽位索引列表（同一逻辑槽位的多个物理位置）</summary>
        public List<int> LinkedSlots { get; set; } = new List<int>();

        /// <summary>槽位世界变换（NPC 移动目标、玩家 motion warp 目标）</summary>
        public Transform SlotTransform { get; set; } = Transform.Identity;

        /// <summary>槽位行为实例</summary>
        public InteractionSlotBehavior SlotInteractBehavior { get; set; }
    }

    /// <summary>
    /// 交互槽位占用句柄。适配 UE5 FInteractionSlotClaimHandle。
    /// </summary>
    [Serializable]
    public class InteractionSlotClaimHandle
    {
        public int HandleIndex { get; set; } = -1;
        public NarrativeInteractableComponent HandleOwner { get; set; }

        public InteractionSlotClaimHandle() { }

        public InteractionSlotClaimHandle(int inHandleIndex, NarrativeInteractableComponent inHandleOwner)
        {
            HandleIndex = inHandleIndex;
            HandleOwner = inHandleOwner;
        }

        public bool IsValidHandle()
        {
            return HandleIndex != -1 && HandleOwner != null;
        }

        public static InteractionSlotClaimHandle InvalidHandle()
        {
            return new InteractionSlotClaimHandle { HandleIndex = -1 };
        }
    }

    /// <summary>
    /// 可交互组件，挂到可被交互的 Actor 上。
    /// 适配 UE5 UNarrativeInteractableComponent，移除复制/RPC，改为本地逻辑 + 事件回调。
    /// 包含交互槽位系统（轻量级 SmartObject 实现，支持 NPC/玩家交互）。
    /// </summary>
    public class NarrativeInteractableComponent : Script
    {
        protected NarrativeInteractionComponent _friendInteractionComp;

        /// <summary>交互槽位配置列表</summary>
        public List<InteractionSlotConfig> InteractionSlots { get; set; } = new List<InteractionSlotConfig>();

        /// <summary>活跃交互槽位状态列表</summary>
        public List<ActiveInteractionSlot> SlotStatuses { get; set; } = new List<ActiveInteractionSlot>();

        /// <summary>玩家需按住交互键的时长（秒）</summary>
        public float InteractionTime { get; set; } = 0f;

        /// <summary>最大交互距离</summary>
        public float InteractionDistance { get; set; } = 200f;

        /// <summary>玩家注视时显示的名称</summary>
        public string InteractableNameText { get; set; } = "";

        /// <summary>交互动词（如"坐"、"吃"、"点燃"）</summary>
        public string InteractableActionText { get; set; } = "";

        /// <summary>聚焦时叠加到 Owner 网格上的材质资源路径</summary>
        public string FocusedOverlayMaterialPath { get; set; } = "";

        // 事件
        public event Action<Actor, NarrativeInteractionComponent> OnBeginInteracted;
        public event Action<Actor, NarrativeInteractionComponent> OnEndInteracted;
        public event Action<Actor, NarrativeInteractionComponent> OnBeginFocus;
        public event Action<Actor, NarrativeInteractionComponent> OnEndFocus;
        public event Action<Actor, NarrativeInteractionComponent> OnInteracted;
        public event Action<int, NarrativeInteractionComponent, NarrativeInteractableComponent> OnTargetedSlotTaken;

        public override void OnEnable()
        {
            base.OnEnable();
            // 初始化槽位状态列表
            if (SlotStatuses == null) SlotStatuses = new List<ActiveInteractionSlot>();
            while (SlotStatuses.Count < InteractionSlots.Count)
            {
                SlotStatuses.Add(new ActiveInteractionSlot());
            }
            // 注册到交互子系统
            InteractionSubsystem.Instance?.CacheInteractable(this);

            // 进程内活跃登记表：InteractionSubsystem 需要场景里存在它的宿主脚本，
            // 而代码生成的 Actor（如 NPC）在没有该宿主时仍然必须能被交互检查发现，
            // 所以 PlayerInteractionComponent 的邻近回退查询读这张表。
            if (!s_activeInteractables.Contains(this)) s_activeInteractables.Add(this);
        }

        public override void OnDisable()
        {
            // 注销交互子系统
            InteractionSubsystem.Instance?.UncacheInteractable(this);
            s_activeInteractables.Remove(this);
            base.OnDisable();
        }

        private static readonly List<NarrativeInteractableComponent> s_activeInteractables =
            new List<NarrativeInteractableComponent>();

        /// <summary>
        /// 当前场景里所有已启用（OnEnable 未走过 OnDisable）的可交互组件。
        /// 供无碰撞体的代码生成 Actor（NPC/掉落物）参与交互检查。
        /// </summary>
        public static IReadOnlyList<NarrativeInteractableComponent> ActiveInteractables => s_activeInteractables;

        /// <summary>获取交互名称文本（可被子类覆盖以动态生成）。</summary>
        public virtual string GetInteractableNameText(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            return InteractableNameText;
        }

        /// <summary>获取交互动作文本（可被子类覆盖）。</summary>
        public virtual string GetInteractableActionText(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            return InteractableActionText;
        }

        /// <summary>设置交互名称文本。</summary>
        public void SetInteractableNameText(string newNameText)
        {
            InteractableNameText = newNameText;
        }

        /// <summary>设置交互动作文本。</summary>
        public void SetInteractableActionText(string newActionText)
        {
            InteractableActionText = newActionText;
        }

        /// <summary>
        /// 返回最接近 claimer 的可用槽位索引，找不到返回 -1。
        /// 注意：Occupied 槽位不会被跳过 —— 它们已由 GetAvailableSlots 过滤为"可偷取"，
        /// 调用方需要靠它们识别偷取对象（对应 UE5 GetBestAvailableSlot_Implementation）。
        /// </summary>
        public virtual int GetBestAvailableSlot(NarrativeInteractionComponent claimer, List<int> slotsToCheck)
        {
            if (claimer == null) return -1;
            Vector3 claimerPos = claimer.GetOwnerPosition();

            int bestIdx = -1;
            float bestDist = float.MaxValue;

            List<int> candidates = slotsToCheck ?? GetAvailableSlots(claimer, true, true);
            if (candidates == null) return -1;

            foreach (int idx in candidates)
            {
                if (idx < 0 || idx >= InteractionSlots.Count || idx >= SlotStatuses.Count) continue;

                Vector3 slotPos = Actor != null
                    ? Actor.Transform.LocalToWorld(InteractionSlots[idx].SlotTransform.Translation)
                    : InteractionSlots[idx].SlotTransform.Translation;
                float d = Vector3.Distance(claimerPos, slotPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = idx;
                }
            }
            return bestIdx;
        }

        /// <summary>
        /// 返回 claimer 可用的所有槽位索引。
        /// 对应 UE5 GetAvailableSlots —— 必须由槽位行为判定 IsUsable / IsStealable。
        /// </summary>
        public List<int> GetAvailableSlots(NarrativeInteractionComponent claimer, bool bIncludeTargeted = true, bool bIncludeStealable = true)
        {
            var result = new List<int>();
            int count = Math.Min(SlotStatuses.Count, InteractionSlots.Count);
            for (int i = 0; i < count; i++)
            {
                var behavior = InteractionSlots[i].SlotInteractBehavior;
                if (behavior == null) continue;
                if (!behavior.IsUsable(i, this, claimer)) continue;

                var status = SlotStatuses[i].SlotStatus;
                if (status == EInteractionSlotStatus.Free)
                {
                    result.Add(i);
                }
                else if (status == EInteractionSlotStatus.Targeted && bIncludeTargeted)
                {
                    result.Add(i);
                }
                else if (status == EInteractionSlotStatus.Occupied && bIncludeStealable)
                {
                    if (behavior.IsStealable(i, this, claimer))
                    {
                        result.Add(i);
                    }
                }
            }
            return result;
        }

        /// <summary>返回指定索引处的所有关联槽位（含自身）。</summary>
        public List<int> GetSlotsAtIndex(int slotIndex)
        {
            var result = new List<int> { slotIndex };
            if (slotIndex >= 0 && slotIndex < InteractionSlots.Count)
            {
                var cfg = InteractionSlots[slotIndex];
                if (cfg.LinkedSlots != null)
                {
                    foreach (int linked in cfg.LinkedSlots)
                    {
                        if (!result.Contains(linked)) result.Add(linked);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 占用槽位，返回占用句柄。对应 UE5 ClaimSlot。
        /// 若目标槽位已被他人 Occupied，必须先与占用者做"偷取握手"：
        /// 调用其 StopInteractBehavior(true, Claimer)，失败则中止占用。
        /// </summary>
        public InteractionSlotClaimHandle ClaimSlot(NarrativeInteractionComponent claimer, int slotToClaimIdx, bool bMarkTargeted = false)
        {
            if (claimer == null) return InteractionSlotClaimHandle.InvalidHandle();
            if (slotToClaimIdx < 0 || slotToClaimIdx >= SlotStatuses.Count) return InteractionSlotClaimHandle.InvalidHandle();
            if (slotToClaimIdx >= InteractionSlots.Count) return InteractionSlotClaimHandle.InvalidHandle();

            // 偷取：通知现有占用者结束交互，失败则放弃占用（对应 UE5 InteractableComponent.cpp:175-181）
            if (SlotStatuses[slotToClaimIdx].SlotStatus == EInteractionSlotStatus.Occupied)
            {
                var occupant = SlotStatuses[slotToClaimIdx].SlotUser;
                if (occupant != null && occupant != claimer)
                {
                    if (!occupant.StopInteractBehavior(true, claimer))
                    {
                        return InteractionSlotClaimHandle.InvalidHandle();
                    }
                }
            }

            UpdateSlotStatus(claimer, slotToClaimIdx, bMarkTargeted ? EInteractionSlotStatus.Targeted : EInteractionSlotStatus.Occupied);
            return new InteractionSlotClaimHandle(slotToClaimIdx, this);
        }

        /// <summary>
        /// 更新槽位状态（含关联槽位）。对应 UE5 UpdateSlotStatus：
        /// 1. 把槽位标记为 Targeted 时不得覆盖已 Occupied 的槽位；
        /// 2. 只有 Occupied 才写入 SlotUser（Targeted 不写，否则偷取判定会被破坏）；
        /// 3. 结束后必须广播 OnSlotStatusesChanged（替代 UE5 的 OnRep_SlotStatuses）。
        /// </summary>
        public void UpdateSlotStatus(NarrativeInteractionComponent claimer, int slotIndex, EInteractionSlotStatus newStatus)
        {
            if (slotIndex < 0 || slotIndex >= SlotStatuses.Count) return;

            var oldStatuses = SnapshotSlotStatuses();

            foreach (int linked in GetSlotsAtIndex(slotIndex))
            {
                if (linked < 0 || linked >= SlotStatuses.Count) continue;

                // 目标化一个已被占用的槽位不应该改动它
                if (newStatus == EInteractionSlotStatus.Targeted && SlotStatuses[linked].SlotStatus == EInteractionSlotStatus.Occupied)
                {
                    return;
                }

                SlotStatuses[linked].SlotStatus = newStatus;

                if (newStatus == EInteractionSlotStatus.Free)
                {
                    SlotStatuses[linked].SlotUser = null;
                }
                else if (newStatus == EInteractionSlotStatus.Occupied)
                {
                    // Targeted 不记录 SlotUser：那会破坏偷取时"占用者是谁"的判定
                    SlotStatuses[linked].SlotUser = claimer;
                }
            }

            OnSlotStatusesChanged(oldStatuses);
        }

        /// <summary>当前槽位状态的浅拷贝，用于变更前后对比（对应 UE5 的 OldSlotStatuses 快照）。</summary>
        protected List<ActiveInteractionSlot> SnapshotSlotStatuses()
        {
            var snapshot = new List<ActiveInteractionSlot>(SlotStatuses.Count);
            foreach (var s in SlotStatuses)
            {
                snapshot.Add(new ActiveInteractionSlot
                {
                    SlotStatus = s.SlotStatus,
                    SlotUser = s.SlotUser
                });
            }
            return snapshot;
        }

        /// <summary>返回指定槽位的配置。</summary>
        public InteractionSlotConfig GetConfigAtSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= InteractionSlots.Count) return null;
            return InteractionSlots[slotIndex];
        }

        /// <summary>是否有可用槽位。</summary>
        public bool HasSlotAvailable(bool bAllowTargeted)
        {
            foreach (var s in SlotStatuses)
            {
                if (s.SlotStatus == EInteractionSlotStatus.Free) return true;
                if (bAllowTargeted && s.SlotStatus == EInteractionSlotStatus.Targeted) return true;
            }
            return false;
        }

        /// <summary>获取交互对象的包围盒。</summary>
        public BoundingBox GetInteractableBounds()
        {
            var actor = Actor;
            if (actor == null) return new BoundingBox(Vector3.Zero, Vector3.Zero);
            return actor.Box;
        }

        // 内部回调（由 PlayerInteractionComponent 触发）
        internal void BeginFocus(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            OnBeginFocus?.Invoke(interactor, interactionComp);
        }

        internal void EndFocus(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            OnEndFocus?.Invoke(interactor, interactionComp);
        }

        internal void BeginInteract(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            OnBeginInteracted?.Invoke(interactor, interactionComp);
        }

        internal void EndInteract(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            OnEndInteracted?.Invoke(interactor, interactionComp);
        }

        public virtual bool Interact(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            OnInteracted?.Invoke(interactor, interactionComp);
            return true;
        }

        /// <summary>
        /// 是否可交互（可由子类覆盖）。
        /// 对应 UE5 CanInteract_Implementation：配置了槽位却没有空闲槽位时拒绝交互。
        /// </summary>
        public virtual bool CanInteract(Actor interactor, NarrativeInteractionComponent interactionComp, out string errorText)
        {
            errorText = "";

            if (InteractionSlots.Count > 0 && !HasSlotAvailable(true))
            {
                errorText = "This is being used right now.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 槽位状态改变回调，替代 UE5 的 OnRep_SlotStatuses。
        /// 检测 Targeted → Occupied 且占用者易主，广播 OnTargetedSlotTaken，
        /// 让正在前往该槽位的 NPC 重新挑选槽位。
        /// </summary>
        protected virtual void OnSlotStatusesChanged(List<ActiveInteractionSlot> oldStatuses)
        {
            if (oldStatuses == null) return;

            int count = Math.Min(oldStatuses.Count, SlotStatuses.Count);
            for (int i = 0; i < count; i++)
            {
                if (oldStatuses[i] == null || SlotStatuses[i] == null) continue;

                // 处理被他人拿走的已目标化槽位，便于 NPC 重新寻找新槽位
                if (oldStatuses[i].SlotStatus == EInteractionSlotStatus.Targeted &&
                    SlotStatuses[i].SlotStatus == EInteractionSlotStatus.Occupied &&
                    !ReferenceEquals(oldStatuses[i].SlotUser, SlotStatuses[i].SlotUser))
                {
                    // 缺陷5：统一经 FireTargetedSlotTaken 广播（该内部入口此前零调用方，
                    // 与本行是同一事件的两条写法；合并为单一漏斗，保证口径一致）
                    FireTargetedSlotTaken(i, SlotStatuses[i].SlotUser, this);
                }
            }
        }

        /// <summary>公共入口：批量改动 SlotStatuses 后手动触发一次变更通知（等价 UE5 主动调 OnRep）。</summary>
        public void NotifySlotStatusesChanged(List<ActiveInteractionSlot> oldStatuses)
        {
            OnSlotStatusesChanged(oldStatuses);
        }

        // TakeSlotStatusSnapshot 已删除（缺陷3/5：全仓零调用方的公开死 API）。它的「批量改槽位前拿快照 +
        // NotifySlotStatusesChanged 做差异」语义在端口内已由 UpdateSlotStatus 用受保护的 SnapshotSlotStatuses
        // 自闭环（本文件 UpdateSlotStatus 开头处），UE5 对应物是属性复制 OnRep_SlotStatuses 的前值快照胶水
        // （Private/Interaction/InteractableComponent.cpp），Flax 无复制、无外部消费者。

        /// <summary>组件是否活跃（对应 UE5 ActorComponent::IsActive，交互检查与占用均需此门）。</summary>
        public virtual bool IsActive()
        {
            if (Actor == null) return false;
            if (!Actor.IsActiveInHierarchy) return false;
            return IsEnabledInHierarchy;
        }

        /// <summary>触发槽位被偷事件。</summary>
        internal void FireTargetedSlotTaken(int slot, NarrativeInteractionComponent interactionComp, NarrativeInteractableComponent interactableComp)
        {
            OnTargetedSlotTaken?.Invoke(slot, interactionComp, interactableComp);
        }
    }
}
