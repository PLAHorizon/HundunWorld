using NarrativePro.Tales.Quest;
using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Navigation;

namespace NarrativePro.Tales.Requirements
{
    /// <summary>
    /// 「别离某 Actor 太远」的任务要求：超时留在范围外就让任务失败。
    /// 对应 UE5 蓝图要求 <c>BPR_StayNearActor</c>
    /// （Content/Pro/Core/Tales/Requirements/BPR_StayNearActor.uasset）。
    /// <para>
    /// 二进制证据：变量 <c>ActorToStayNear</c> 是 Instanced 的
    /// <c>NarrativeActorProvider</c>（<c>InstancedActorProvider</c> 类型名可见），
    /// <c>CallFunc_ProvideActor_ReturnValue</c> 取 Actor；周期检查由
    /// <c>CallFunc_K2_SetTimerDelegate_ReturnValue</c>（<c>TimerHandle</c> /
    /// <c>bLooping</c> / <c>InitialStartDelay</c> / <c>bMaxOncePerFrame</c>）驱动，
    /// 检查体里 <c>CallFunc_GetDistanceTo_ReturnValue</c>（<c>OtherActor</c>）与
    /// <c>MaxUnits</c> 比大小（<c>CallFunc_Greater_DoubleDouble_ReturnValue</c>），
    /// 出界累加 <c>OutsideBoundsCount</c>（<c>CallFunc_Add_IntInt_ReturnValue</c>），
    /// 达到 <c>SecondsLeeway</c>（<c>GreaterEqual_IntInt</c>）就 <c>FailQuest</c> 并
    /// 带 <c>FailedMessage</c>；另有 <c>WarningMessage</c> / <c>ReturnedMessage</c>
    /// 配 <c>PushHUDNotification</c> 做出界/回界的玩家提示。
    /// </para>
    /// <para>
    /// 端口差异：UE5 的 <c>GetDistanceTo</c> 在 Flax 无同名蓝图入口，改用
    /// <see cref="NavigatorStatics.Distance2D"/>（与 <c>GoToLocationTask</c> 同一判据：
    /// 高度差不该决定「是否留在某人身边」）。
    /// </para>
    /// </summary>
    [Serializable]
    public class StayNearActorRequirement : TickedQuestRequirement
    {
        /// <summary>要跟住的 Actor 提供者。对应 UE5 <c>ActorToStayNear</c>。</summary>
        public NarrativePro.Core.NarrativeActorProvider ActorToStayNear { get; set; }

        /// <summary>提供者缺位时按 NPC 定义 ID 要跟住对象。</summary>
        public string TargetNpcId { get; set; } = "";

        /// <summary>允许的最大距离（世界单位）。对应 UE5 <c>MaxUnits</c>。</summary>
        public float MaxUnits { get; set; } = 1500f;

        /// <summary>可以在范围外停留的秒数。对应 UE5 <c>SecondsLeeway</c>。</summary>
        public int SecondsLeeway { get; set; } = 5;

        /// <summary>计时器周期秒。对应 <c>InitialStartDelay</c>/<c>Interval</c>。</summary>
        public float CheckIntervalSeconds { get; set; } = 1f;

        public string FailedMessage { get; set; } = "";
        public string WarningMessage { get; set; } = "";
        public string ReturnedMessage { get; set; } = "";

        /// <summary>连续出界计数（秒）。对应 UE5 <c>OutsideBoundsCount</c>。</summary>
        private int _outsideBoundsCount;
        private bool _bWarned;

        public override void OnAdded(NarrativePro.Tales.Quest.Quest quest)
        {
            CheckInterval = CheckIntervalSeconds > 0f ? CheckIntervalSeconds : 1f;
            _outsideBoundsCount = 0;
            _bWarned = false;
            base.OnAdded(quest);
        }

        public override void TickRequirement(float deltaTime)
        {
            var pawn = ResolvePawnActor();
            var target = NarrativePro.Tales.Tasks.BuiltinTaskSupport.ResolveTargetActor(
                ActorToStayNear, TargetNpcId, Guid.Empty);
            if (pawn == null || target == null) return;

            float distance = NavigatorStatics.Distance2D(pawn.Position, target.Position);

            if (distance > MaxUnits)
            {
                _outsideBoundsCount++;

                if (!_bWarned)
                {
                    _bWarned = true;
                    Notify(WarningMessage, "别再走远了");
                }

                if (_outsideBoundsCount >= Math.Max(1, SecondsLeeway))
                {
                    Notify(FailedMessage, "离开目标太远，任务失败");
                    OwningQuest?.FailQuest(string.IsNullOrEmpty(FailedMessage) ? "走得太远" : FailedMessage);
                }
            }
            else if (_outsideBoundsCount > 0)
            {
                _outsideBoundsCount = 0;
                _bWarned = false;
                Notify(ReturnedMessage, "已回到目标身边");
            }
        }

        private static void Notify(string message, string fallback)
        {
            NarrativePro.NarrativeProPlugin.ShowNotification(
                string.IsNullOrEmpty(message) ? fallback : message, 4f);
        }
    }
}
