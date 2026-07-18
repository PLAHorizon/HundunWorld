using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Tales.Events;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 叙事触发器基类。对应 UE5 UNarrativeTrigger。
    /// 触发器可在任意时间激活/停用，激活时触发事件，可用于让 NPC 在特定时间做特定活动（例如睡觉）。
    /// </summary>
    [Serializable]
    public abstract class NarrativeTrigger
    {
        /// <summary>触发器激活/停用时要执行的事件。</summary>
        public List<NarrativeEvent> TriggerEvents = new List<NarrativeEvent>();

        /// <summary>拥有此触发器的角色（运行期填充）。</summary>
        public Actor OwnerCharacter;

        /// <summary>当前是否激活。</summary>
        public bool bIsActive = false;

        /// <summary>初始化触发器，绑定委托或设置定时器。子类可重写。</summary>
        public virtual void Initialize()
        {
        }

        /// <summary>返回触发器当前是否激活。子类可重写。</summary>
        public virtual bool IsActive()
        {
            return bIsActive;
        }

        /// <summary>返回触发器描述文本。子类可重写。</summary>
        public virtual string GetDescription()
        {
            return GetType().Name;
        }

        /// <summary>激活触发器。执行 OnActivate 事件，并调用所有 TriggerEvents。</summary>
        public virtual void Activate()
        {
            if (bIsActive) return;
            bIsActive = true;
            OnActivate();
            ExecuteTriggerEvents(true);
        }

        /// <summary>停用触发器。执行 OnDeactivate 事件，并调用所有 TriggerEvents。</summary>
        public virtual void Deactivate()
        {
            if (!bIsActive) return;
            bIsActive = false;
            OnDeactivate();
            ExecuteTriggerEvents(false);
        }

        /// <summary>激活回调（子类可重写以添加自定义逻辑）。</summary>
        protected virtual void OnActivate()
        {
        }

        /// <summary>停用回调（子类可重写以添加自定义逻辑）。</summary>
        protected virtual void OnDeactivate()
        {
        }

        /// <summary>执行 TriggerEvents 中符合运行期的事件。</summary>
        protected void ExecuteTriggerEvents(bool bStart)
        {
            if (TriggerEvents == null) return;
            var runtime = bStart ? EEventRuntime.Start : EEventRuntime.End;
            foreach (var evt in TriggerEvents)
            {
                if (evt == null) continue;
                if (evt.EventRuntime == runtime || evt.EventRuntime == EEventRuntime.Both)
                {
                    evt.ExecuteEvent(OwnerCharacter, null, null);
                }
            }
        }
    }
}
