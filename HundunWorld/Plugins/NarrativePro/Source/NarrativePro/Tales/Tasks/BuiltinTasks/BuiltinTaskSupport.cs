using System;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;
using NarrativePro.Tales.Async;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 内置任务共用的「上下文解析」帮助器。
    /// <para>
    /// UE5 依据：<c>UNarrativeTask</c> 在 <c>BeginTaskInit</c> 里把运行期上下文缓存成成员
    /// （QuestTask.cpp:52-67 的 OwningComp/OwningPawn/OwningController），蓝图任务再经
    /// <c>UNarrativeFunctionLibrary::GetTalesComponentFromTarget</c> /
    /// <c>UInventoryFunctionLibrary::GetInventoryComponentFromTarget</c> /
    /// <c>UArsenalStatics::GetNarrativeAbilitySystemComponent</c> 取组件 ——
    /// BPT_EquipItem / BPT_FindItem / BPT_UseItem / BPT_CompleteDataTask 二进制里的
    /// <c>CallFunc_GetTalesComponentFromTarget_ReturnValue</c>、
    /// <c>CallFunc_GetInventoryComponentFromTarget_ReturnValue</c> 即证据。
    /// 端口逐个任务重复这套样板没有意义，集中到此处。
    /// </para>
    /// </summary>
    internal static class BuiltinTaskSupport
    {
        /// <summary>取任务所属角色的 Actor（先角色后裸 Actor，与 GoToLocationTask 同口径）。</summary>
        public static Actor ResolvePawnActor(NarrativeTask task)
        {
            if (task == null) return null;

            var character = ArsenalStatics.ResolveCharacterFromContext(task.OwningPawn, task.OwningController);
            if (character?.Actor != null) return character.Actor;

            return task.OwningPawn as Actor ?? task.OwningController as Actor;
        }

        public static NarrativeInventoryComponent GetInventory(NarrativeTask task)
        {
            return ArsenalStatics.GetNarrativeInventoryComponent(ResolvePawnActor(task));
        }

        public static EquipmentComponent GetEquipment(NarrativeTask task)
        {
            var pawn = ResolvePawnActor(task);
            return pawn != null ? pawn.GetScript<EquipmentComponent>() : null;
        }

        public static NarrativeAbilitySystemComponent GetAsc(NarrativeTask task)
        {
            return ArsenalStatics.GetNarrativeAbilitySystemComponent(ResolvePawnActor(task));
        }

        /// <summary>
        /// 即时解析「任务目标 Actor」。三条通路按 UE5 蓝图的实际取法排序：
        /// ① 内联 Actor 提供者（资产里 Instanced 的 <c>NarrativeActorProvider</c>，
        /// BPT_InteractWithObject / BPT_TakePickup / BPT_ClearSpawn 的
        /// <c>CallFunc_ProvideActor_ReturnValue</c>）；② NPC 定义 ID（
        /// BPT_AddGoalAndWait / BPR_KeepNPCAlive 的 <c>CallFunc_FindNPC_ReturnValue</c>）；
        /// ③ 存档 GUID 查表（BPT_ClearSpawn 的 <c>LookupActorByGUID</c>）。
        /// </summary>
        public static Actor ResolveTargetActor(NarrativeActorProvider provider, string npcId, Guid actorGuid)
        {
            var actor = provider?.ProvideActor(null);
            if (actor != null) return actor;

            if (!string.IsNullOrEmpty(npcId))
            {
                var subsystem = NarrativeCharacterSubsystem.Instance;
                actor = subsystem?.FindNPCByID(npcId);
                if (actor != null) return actor;
            }

            if (actorGuid != Guid.Empty)
            {
                var saveSubsystem = NarrativePro.SaveSystem.Subsystems.NarrativeSaveSubsystem.Instance;
                actor = saveSubsystem?.LookupActorByGUID(actorGuid);
            }

            return actor;
        }

        /// <summary>把 0/负数视为「未填写」的距离参数归一，避免资产漏填导致立即达标或永不达标。</summary>
        public static float PositiveOr(float value, float fallback)
        {
            return value > 0f ? value : fallback;
        }
    }

    /// <summary>
    /// 目标 Actor 的异步索取句柄：提供者取不到时交给
    /// <see cref="AsyncAction_ProvideActor"/> 轮询（对应 UE5 蓝图
    /// <c>K2Node_AsyncAction</c> + <c>OnProvided</c>/<c>OnFailed</c>，见
    /// BPT_InteractWithObject / BPT_TakePickup / BPT_ClearSpawn 的 <c>ProvideActor</c> 节点）。
    /// 任务 <c>EndTask</c> 必须 <see cref="Cancel"/>，否则轮询器会替已死任务续命。
    /// </summary>
    internal sealed class ProvideActorRequest
    {
        private readonly Action<Actor> _onProvided;
        private AsyncAction_ProvideActor _action;

        public ProvideActorRequest(NarrativeTask task, NarrativeActorProvider provider, Action<Actor> onProvided, float pollInterval = 1f)
        {
            _onProvided = onProvided;
            if (provider == null) return;

            var immediate = provider.ProvideActor(task);
            if (immediate != null)
            {
                _onProvided?.Invoke(immediate);
                return;
            }

            _action = AsyncAction_ProvideActor.ProvideActor(task, provider, pollInterval);
            if (_action == null) return;

            _action.OnProvided += HandleProvided;
        }

        /// <summary>是否仍在等待（用于任务决定要不要挂回退位置的标记）。</summary>
        public bool IsPending => _action != null && !_action.IsDone;

        private void HandleProvided(Actor actor)
        {
            var callback = _onProvided;
            Cancel();
            callback?.Invoke(actor);
        }

        public void Cancel()
        {
            if (_action == null) return;
            _action.OnProvided -= HandleProvided;
            _action.EndTask();
            _action = null;
        }
    }
}
