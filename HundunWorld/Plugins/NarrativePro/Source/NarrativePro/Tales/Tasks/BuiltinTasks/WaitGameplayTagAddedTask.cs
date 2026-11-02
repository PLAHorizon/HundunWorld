using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「等指定 Actor 获得某个 gameplay 标签」的任务条目。对应 UE5 蓝图任务
    /// <c>BPT_WaitGameplayTagAdded</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_WaitGameplayTagAdded.uasset）。
    /// <para>
    /// 二进制证据：图调 <c>CallFunc_WaitGameplayTagAddToActor_ReturnValue</c>
    /// （<c>UAbilitySystemBlueprintLibrary::WaitGameplayTagAddToActor</c>），变量
    /// <c>Tag</c> / <c>ActorToWaitOn</c> / <c>bCountOncePerChange</c>，描述用
    /// <c>GetDebugStringFromGameplayTag</c>，回调里 <c>IsValid</c> 判空后推进。
    /// </para>
    /// <para>
    /// 端口差异：<see cref="NarrativeAbilitySystemComponent.GameplayTags"/> 是普通
    /// <see cref="GameplayTagContainer"/>（NarrativeAbilitySystemComponent.cs:208），
    /// 增删（<c>AddTag</c>/<c>RemoveTag</c>，Items/GameplayTag.cs:91/:96）不广播任何变更事件，
    /// 因此端口没有 UE5 那套 tag-register 委托可挂，只能按
    /// <see cref="NarrativeTask.TickInterval"/> 轮询 <c>HasMatchingGameplayTag</c>
    /// （NarrativeAbilitySystemComponent.cs:2087）。轮询语义与「等标签出现」等价，
    /// 差别是最多有 <see cref="CheckInterval"/> 秒的延迟。
    /// </para>
    /// </summary>
    [Serializable]
    public class WaitGameplayTagAddedTask : NarrativeTask
    {
        /// <summary>要等待出现的标签。对应 UE5 <c>Tag</c>。</summary>
        public string Tag { get; set; } = "";

        /// <summary>标签所属 Actor 的提供者；留空则看自己身上。</summary>
        public NarrativePro.Core.NarrativeActorProvider TargetProvider { get; set; }

        public string TargetNpcId { get; set; } = "";

        /// <summary>轮询间隔秒。</summary>
        public float CheckInterval { get; set; } = 0.25f;

        private NarrativeAbilitySystemComponent _asc;
        private GameplayTag _tag;

        protected override void OnBeginTask()
        {
            _tag = new GameplayTag(Tag);
            if (!_tag.IsValid())
            {
                NarrativeLog.LogWarning("WaitGameplayTagAddedTask: Tag 为空，任务无等待条件");
                return;
            }

            TickInterval = BuiltinTaskSupport.PositiveOr(CheckInterval, 0.25f);
            TryResolveTarget();
        }

        private void TryResolveTarget()
        {
            if (_asc != null) return;

            var target = BuiltinTaskSupport.ResolveTargetActor(TargetProvider, TargetNpcId, Guid.Empty)
                         ?? BuiltinTaskSupport.ResolvePawnActor(this);
            _asc = target != null ? ArsenalStatics.GetNarrativeAbilitySystemComponent(target) : null;
        }

        protected override void TickTask()
        {
            TryResolveTarget();
            if (_asc == null) return;

            if (_asc.HasMatchingGameplayTag(_tag))
                CompleteTask();
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            if (!string.IsNullOrEmpty(Tag) &&
                ArsenalStatics.GetGameplayTagFriendlyDisplayName(new GameplayTag(Tag), out var friendly))
                return $"等待状态：{friendly}";
            return $"等待获得标签：{Tag}";
        }
    }
}
