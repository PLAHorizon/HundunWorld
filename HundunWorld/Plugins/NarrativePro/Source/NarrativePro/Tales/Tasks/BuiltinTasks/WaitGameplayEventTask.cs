using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「等某个 gameplay 事件被派发到指定 Actor」的任务条目。对应 UE5 蓝图任务
    /// <c>BPT_WaitGameplayEvent</c>（Content/Pro/Core/Tales/Tasks/BPT_WaitGameplayEvent.uasset）。
    /// <para>
    /// 二进制证据：图直接调 <c>CallFunc_WaitGameplayEventToActor_ReturnValue</c>
    /// （<c>UAbilitySystemBlueprintLibrary::WaitGameplayEventToActor</c>），
    /// 变量 <c>EventTag</c> / <c>ActorToWaitOn</c>，描述用
    /// <c>CallFunc_GetDebugStringFromGameplayTag_ReturnValue</c>，回调里
    /// <c>K2Node_Event_Payload</c>（<c>FGameplayEventData</c>）+
    /// <c>CallFunc_IsValid_ReturnValue</c> 判空后推进进度。
    /// </para>
    /// <para>
    /// 端口差异：Flax 侧没有 <c>AbilitySystemBlueprintLibrary</c>，端口的等价入口是
    /// <see cref="NarrativeAbilitySystemComponent.RegisterGenericGameplayEventCallback"/>
    /// （NarrativeAbilitySystemComponent.cs:916，由 <c>HandleGameplayEvent</c> 在锁外广播）。
    /// 该表是「一标签一回调」的字典且无读取既有回调的 API，所以本任务结束时只能
    /// <see cref="NarrativeAbilitySystemComponent.UnregisterGenericGameplayEventCallback"/>
    /// 注销自己注册的项（同标签上后注册者会顶掉先注册者 —— 与 UE5 多播委托不同，属端口限制）。
    /// </para>
    /// </summary>
    [Serializable]
    public class WaitGameplayEventTask : NarrativeTask
    {
        /// <summary>要等待的事件标签。对应 UE5 <c>EventTag</c>。</summary>
        public string EventTag { get; set; } = "";

        /// <summary>承载事件的 Actor 提供者；留空则等在自己身上。对应 <c>ActorToWaitOn</c>。</summary>
        public NarrativePro.Core.NarrativeActorProvider TargetProvider { get; set; }

        /// <summary>按 NPC 定义 ID 指定监听对象。</summary>
        public string TargetNpcId { get; set; } = "";

        /// <summary>每次命中推进的进度。对应图里的 <c>ProgressToAdd</c>。</summary>
        public int ProgressToAdd { get; set; } = 1;

        private NarrativeAbilitySystemComponent _asc;
        private GameplayTag _tag;

        protected override void OnBeginTask()
        {
            _tag = new GameplayTag(EventTag);
            if (!_tag.IsValid())
            {
                NarrativeLog.LogWarning("WaitGameplayEventTask: EventTag 为空，任务无等待条件");
                return;
            }

            TryBind();

            // 目标 ASC 可能稍后才出现（流式加载的敌人），用轮询补绑。
            TickInterval = 0.5f;
        }

        private void TryBind()
        {
            if (_asc != null) return;

            var target = BuiltinTaskSupport.ResolveTargetActor(TargetProvider, TargetNpcId, Guid.Empty)
                         ?? BuiltinTaskSupport.ResolvePawnActor(this);
            _asc = target != null ? ArsenalStatics.GetNarrativeAbilitySystemComponent(target) : null;
            if (_asc == null) return;

            _asc.RegisterGenericGameplayEventCallback(_tag, HandleEvent);
        }

        private void HandleEvent(GameplayEventData payload)
        {
            if (!bIsActive) return;
            AddProgress(ProgressToAdd > 0 ? ProgressToAdd : 1);
        }

        protected override void TickTask()
        {
            if (_asc == null) TryBind();
        }

        protected override void OnEndTask()
        {
            if (_asc != null && _tag.IsValid())
                _asc.UnregisterGenericGameplayEventCallback(_tag);
            _asc = null;
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            if (!string.IsNullOrEmpty(EventTag) &&
                ArsenalStatics.GetGameplayTagFriendlyDisplayName(new GameplayTag(EventTag), out var friendly))
                return $"等待事件：{friendly}";
            return $"等待事件：{EventTag}";
        }
    }
}
