using System;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 交互类型。对应 UE5 EInteractType。
    /// </summary>
    public enum EInteractType : byte
    {
        /// <summary>按下交互（瞬时）。</summary>
        Press = 0,
        /// <summary>按住交互（持续）。</summary>
        Hold = 1,
        /// <summary>松开触发。</summary>
        Release = 2,
        /// <summary>切换交互（开/关）。</summary>
        Toggle = 3
    }

    /// <summary>
    /// 交互能力。对应 UE5 UNarrativeInteractAbility。
    /// 处理与场景 Actor 的交互：射线检测、交互范围、交互时序。
    /// 简化点：
    /// - 移除 UE5 ETraceTypeQuery/EObjectTypeQuery，用字符串 CollisionProfileName 占位
    /// - 碰撞检测用 Physics.RayCast
    /// - 交互由 InteractableComponent 触发（Phase 2 已实现）
    /// </summary>
    public class NarrativeInteractAbility : NarrativeGameplayAbility
    {
        // ===== 配置字段 =====

        /// <summary>交互类型。</summary>
        public EInteractType InteractType = EInteractType.Press;

        /// <summary>交互范围（cm）。</summary>
        public float InteractionRange = 200f;

        /// <summary>追踪通道名称。</summary>
        public string TraceChannel = "Visibility";

        /// <summary>碰撞 Profile 名。</summary>
        public string CollisionProfileName = "Visibility";

        /// <summary>可交互次数（0 = 无限）。</summary>
        public int NumInteractions = 0;

        /// <summary>两次交互之间的最小间隔（秒）。</summary>
        public float InteractionDelay = 0f;

        /// <summary>交互 Cooldown（秒）。</summary>
        public float InteractionCooldown = 1f;

        /// <summary>单次交互执行时长（秒，用于 Hold 类型）。</summary>
        public float InteractionTime = 0f;

        /// <summary>交互总持续时长（秒，0 = 立即）。</summary>
        public float InteractionDuration = 0f;

        /// <summary>是否与阻塞 Actor 交互。</summary>
        public bool bInteractWithBlockingActors = true;

        // ===== 运行时状态 =====

        /// <summary>已执行交互次数。</summary>
        public int InteractionsPerformed = 0;

        /// <summary>下次可交互时间。</summary>
        public float NextInteractionTime = 0f;

        /// <summary>当前交互的目标 Actor。</summary>
        public Actor CurrentInteractableActor;

        /// <summary>激活能力：执行交互检测。</summary>
        public override void ActivateAbility(GameplayAbilitySpecHandle handle, GameplayAbilitySpec spec)
        {
            base.ActivateAbility(handle, spec);

            // 检查 Cooldown
            if (Time.GameTime < NextInteractionTime)
            {
                EndAbility();
                return;
            }

            // 检查次数
            if (NumInteractions > 0 && InteractionsPerformed >= NumInteractions)
            {
                EndAbility();
                return;
            }

            // 执行交互检测
            var interactable = FindInteractable();
            if (interactable != null)
            {
                CurrentInteractableActor = interactable;
                InteractionsPerformed++;
                NextInteractionTime = Time.GameTime + InteractionCooldown;

                // 触发交互（通过 NarrativeInteractableComponent）
                var interactableComp = interactable.GetScript<Interaction.NarrativeInteractableComponent>();
                if (interactableComp != null)
                {
                    // 获取拥有者的 NarrativeInteractionComponent 用于交互回调
                    var ownerInteractor = Actor != null ? Actor.GetScript<Interaction.NarrativeInteractionComponent>() : null;
                    interactableComp.Interact(Actor, ownerInteractor);
                    NarrativePro.Core.NarrativeLog.Log($"[InteractAbility] Interacted with {interactable.Name}");
                }
            }

            // 立即结束（Press/Toggle 类型）
            if (InteractType == EInteractType.Press || InteractType == EInteractType.Toggle || InteractType == EInteractType.Release)
            {
                EndAbility();
            }
            // Hold 类型保持激活，直到 InputTagReleased 时结束
        }

        /// <summary>输入释放时结束能力（Hold 类型）。</summary>
        public override void InputTagReleased(GameplayTag inputTag)
        {
            if (InteractType == EInteractType.Hold)
            {
                EndAbility();
            }
        }

        /// <summary>查找可交互的 Actor。</summary>
        protected virtual Actor FindInteractable()
        {
            if (Actor == null) return null;

            Vector3 start = Actor.Position;
            Vector3 forward = Actor.Transform.Forward;
            Vector3 end = start + forward * InteractionRange;

            // 使用 RayCast 检测
            if (Physics.RayCast(start, end, out RayCastHit hit))
            {
                return hit.Collider;
            }

            return null;
        }

        /// <summary>重置交互次数。</summary>
        public virtual void ResetInteractions()
        {
            InteractionsPerformed = 0;
            NextInteractionTime = 0f;
            CurrentInteractableActor = null;
        }
    }
}
