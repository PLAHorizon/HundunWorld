using System;
using FlaxEngine;

namespace NarrativePro.GAS.AbilityTasks
{
    /// <summary>
    /// 能力任务基类。对应 UE5 UAbilityTask。
    /// 简化点：
    /// - 移除 UE5 异步任务系统（改为 OnUpdate 轮询）
    /// - 移除网络复制/预测
    /// - 由所属能力每帧 Tick 调用 OnUpdate
    /// - 完成时调用 OnComplete 事件并销毁
    /// </summary>
    public abstract class AbilityTask
    {
        /// <summary>所属能力。</summary>
        [NonSerialized]
        public NarrativeGameplayAbility OwningAbility;

        /// <summary>是否已激活。</summary>
        public bool bIsActive = false;

        /// <summary>是否已完成。</summary>
        public bool bIsComplete = false;

        /// <summary>完成事件。</summary>
        public event Action OnComplete;

        /// <summary>激活任务。</summary>
        public virtual void Activate()
        {
            bIsActive = true;
            bIsComplete = false;
        }

        /// <summary>每帧更新（由所属能力的 OnUpdate 调用）。</summary>
        public virtual void OnUpdate(float deltaTime) { }

        /// <summary>完成任务。</summary>
        public virtual void Complete()
        {
            if (bIsComplete) return;
            bIsComplete = true;
            bIsActive = false;
            OnComplete?.Invoke();
        }

        /// <summary>取消任务。</summary>
        public virtual void Cancel()
        {
            bIsActive = false;
            bIsComplete = true;
        }
    }
}
