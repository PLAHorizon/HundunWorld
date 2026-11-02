using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Interaction;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「与指定对象交互若干次」的任务条目。对应 UE5 蓝图任务 <c>BPT_InteractWithObject</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_InteractWithObject.uasset，类名 <c>BPT_Interact</c>）。
    /// <para>
    /// 二进制证据：变量 <c>InteractableActor</c>（类型是 Instanced 的
    /// <c>NarrativeActorProvider</c>，图里 <c>K2Node_AsyncAction</c> 跑
    /// <c>AsyncAction_ProvideActor</c> 的 <c>OnProvided</c>/<c>OnFailed</c>），
    /// <c>CallFunc_GetInteractionComponent_ReturnValue</c> 取玩家交互组件，
    /// <c>K2Node_CreateDelegate</c> 绑可交互组件的 <c>OnBeginUseInteractable</c> /
    /// <c>OnFinishUseInteractable</c>（<c>OnUseInteractable__DelegateSignature</c>），
    /// 用 <c>CallFunc_EqualEqual_ObjectObject_ReturnValue</c> 校验「交互掉的确实是本任务指定的
    /// 那个组件」，变量 <c>RequiredInteract</c> 决定需要几次，达标走 <c>Add_IntInt</c>；
    /// 另实现 <c>GetNavigationMarkerAttachActor</c> 把标记挂到交互对象上。
    /// </para>
    /// <para>
    /// 端口映射：<c>OnBeginUseInteractable</c>/<c>OnFinishUseInteractable</c> 对应
    /// <see cref="NarrativeInteractableComponent.OnBeginInteracted"/> /
    /// <see cref="NarrativeInteractableComponent.OnEndInteracted"/>
    /// （InteractableComponent.cs:486-494，由 PlayerInteractionComponent 触发）。
    /// 计一次完成只认 End，避免 Begin/End 双计。
    /// </para>
    /// </summary>
    [Serializable]
    public class InteractWithObjectTask : NarrativeTask
    {
        /// <summary>交互对象提供者（UE5 资产里的 Instanced 提供者）。对应 <c>InteractableActor</c>。</summary>
        public NarrativePro.Core.NarrativeActorProvider InteractableProvider { get; set; }

        /// <summary>提供者缺位时按 NPC/角色定义 ID 找对象。</summary>
        public string TargetNpcId { get; set; } = "";

        /// <summary>需要交互的次数。对应 UE5 <c>RequiredInteract</c>。</summary>
        public int RequiredInteract { get; set; } = 1;

        private NarrativeInteractableComponent _interactable;
        private ProvideActorRequest _provideRequest;

        protected override void OnBeginTask()
        {
            if (RequiredInteract > 0)
                RequiredQuantity = RequiredInteract;

            var immediate = BuiltinTaskSupport.ResolveTargetActor(InteractableProvider, TargetNpcId, Guid.Empty);
            if (immediate != null)
            {
                BindInteractable(immediate);
                return;
            }

            // 对象还没生成（流式加载/脚本生成）：交给 AsyncAction_ProvideActor 轮询。
            _provideRequest = new ProvideActorRequest(this, InteractableProvider, HandleActorProvided);
            if (_provideRequest == null || !_provideRequest.IsPending)
                NarrativeLog.LogWarning("InteractWithObjectTask: 取不到交互对象，也没有可用的提供者");
        }

        private void HandleActorProvided(Actor actor)
        {
            _provideRequest = null;
            if (!bIsActive || actor == null) return;
            BindInteractable(actor);
        }

        private void BindInteractable(Actor actor)
        {
            _interactable = InteractionFunctionLibrary.GetInteractableComponent(actor);
            if (_interactable == null)
            {
                NarrativeLog.LogWarning($"InteractWithObjectTask: Actor '{actor.Name}' 上没有交互组件");
                return;
            }

            _interactable.OnEndInteracted += HandleEndInteracted;
        }

        private void HandleEndInteracted(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            if (!bIsActive) return;

            // UE5 的 EqualEqual_ObjectObject 校验：必须是「本任务所属角色」发起的交互。
            var ownComp = InteractionFunctionLibrary.GetInteractionComponent(BuiltinTaskSupport.ResolvePawnActor(this));
            if (ownComp != null && interactionComp != null && interactionComp != ownComp) return;

            AddProgress(1);
        }

        /// <summary>把导航标记挂到交互对象上（对应图内实现的 GetNavigationMarkerAttachActor）。</summary>
        public override Actor GetNavigationMarkerAttachActor()
        {
            return _interactable != null ? _interactable.Actor : null;
        }

        protected override void OnEndTask()
        {
            _provideRequest?.Cancel();
            _provideRequest = null;

            if (_interactable != null)
            {
                _interactable.OnEndInteracted -= HandleEndInteracted;
                _interactable = null;
            }
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return RequiredInteract > 1 ? $"与该对象交互 {RequiredInteract} 次" : "与该对象交互";
        }
    }
}
