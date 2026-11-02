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

        /// <summary>描述文本（JSON 资产可填；空则回落类型名，对应 UE5 GetDescription BP 覆写）。</summary>
        public string Description = "";

        /// <summary>初始化触发器，绑定委托或设置定时器。子类可重写。</summary>
        public virtual void Initialize()
        {
        }

        /// <summary>
        /// 复制模板并绑定宿主角色。对应 UE5 <c>DuplicateObject&lt;UNarrativeTrigger&gt;(Template, this)</c>
        /// 后设 <c>OwnerCharacter</c> 再 <c>Initialize()</c>（NarrativeCharacter.cpp:1921-1935）。
        /// 事件实例为不可变执行器（执行时才接收 target/controller/component 上下文），
        /// 因此副本共享事件引用、仅复制列表本身；运行态（激活标记、事件注册句柄）不复用。
        /// </summary>
        public virtual NarrativeTrigger CopyForOwner(Actor ownerCharacter)
        {
            var copy = (NarrativeTrigger)MemberwiseClone();
            copy.TriggerEvents = TriggerEvents != null
                ? new List<NarrativeEvent>(TriggerEvents)
                : new List<NarrativeEvent>();
            copy.OwnerCharacter = ownerCharacter;
            copy.bIsActive = false;
            return copy;
        }

        /// <summary>解绑触发器持有的运行时注册（时辰事件等）。默认无操作；
        /// UE5 里对象随 Outer 销毁、委托自动失效，Flax 需显式反注册入口。</summary>
        public virtual void Unbind()
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
            return string.IsNullOrEmpty(Description) ? GetType().Name : Description;
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

            // UE5 把触发器所属组件一并传进事件上下文，事件内的 Conditions 才有判定依据；
            // 传 null 会让所有条件走「无组件」分支恒失败，因此这里按 OwnerCharacter 反查。
            var component = ResolveTalesComponent();
            var controller = (object)component?.GetOwningController() ?? OwnerCharacter;

            foreach (var evt in TriggerEvents)
            {
                if (evt == null) continue;
                if (evt.EventRuntime == runtime || evt.EventRuntime == EEventRuntime.Both)
                {
                    evt.ExecuteEvent(OwnerCharacter, controller, component);
                }
            }
        }

        /// <summary>触发器宿主角色上的叙事组件（找不到时返回 null）。</summary>
        protected TalesComponent ResolveTalesComponent()
        {
            if (OwnerCharacter == null) return null;
            return OwnerCharacter.GetScript<TalesComponent>();
        }
    }
}
