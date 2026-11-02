using NarrativePro.Tales.Quest;
using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Vehicles;

namespace NarrativePro.Tales.Requirements
{
    /// <summary>
    /// 「玩家得上车」的任务要求。对应 UE5 蓝图要求 <c>BPR_GetIntoVehicle</c>
    /// （Content/Pro/Core/Tales/Requirements/BPR_GetIntoVehicle.uasset）。
    /// <para>
    /// 二进制证据：它只注册 <c>CallFunc_GetOwningController_ReturnValue</c> 上的
    /// <c>OnPossessedPawnChanged</c> 委托（<c>CustomEvent_NewPawn</c> / <c>CustomEvent_OldPawn</c>），
    /// 用 <c>CallFunc_ClassIsChildOf_ReturnValue</c> 把新 Pawn 与 <c>VehicleType</c>
    /// （<c>NarrativeVehicleBase</c>）比对，进车走 <c>GetInMessage</c>、
    /// 离车走 <c>GetBackInMessage</c> 并经 <c>PushHUDNotification</c> 提示；
    /// <c>DoOnce</c> 宏 + <c>Temp_bool_Has_Been_Initd_Variable</c>/<c>IsClosed</c>
    /// 表示首次上车只提示一次。图里的 <c>TODO</c> 注释（"requirement … extend … tell the quest"）
    /// 说明 UE5 侧它本身<b>不会</b>让任务失败，只是提示层的要求。
    /// </para>
    /// <para>
    /// 端口差异：Flax 没有 <c>APlayerController::OnPossessedPawnChanged</c>，端口的
    /// 「上车」事实源是 <see cref="NarrativeVehicleBase.GetVehicleController"/>
    /// （<c>PossessedBy</c> 在第一名乘者上车时置位，见 TrafficVehicleActor.cs:274-281）与
    /// <see cref="MountComponent.ActorToSeatIndexMap"/>，两者都没有变更事件，
    /// 故按 <see cref="TickedQuestRequirement.CheckInterval"/> 轮询状态跃迁；
    /// 与 UE5 一致地只做提示、不判失败。
    /// </para>
    /// </summary>
    [Serializable]
    public class GetIntoVehicleRequirement : TickedQuestRequirement
    {
        /// <summary>上车提示。对应 UE5 <c>GetInMessage</c>。</summary>
        public string GetInMessage { get; set; } = "";

        /// <summary>离车提示。对应 UE5 <c>GetBackInMessage</c>。</summary>
        public string GetBackInMessage { get; set; } = "";

        /// <summary>轮询间隔秒。</summary>
        public float CheckIntervalSeconds { get; set; } = 0.5f;

        private bool _bInVehicle;
        private bool _bNotifiedFirstEntry;

        public override void OnAdded(NarrativePro.Tales.Quest.Quest quest)
        {
            CheckInterval = CheckIntervalSeconds > 0f ? CheckIntervalSeconds : 0.5f;
            base.OnAdded(quest);

            // UE5 的 DoOnce 语义：绑定时点已在车上就不重复补一条「上车」提示。
            _bInVehicle = IsInVehicle();
            _bNotifiedFirstEntry = _bInVehicle;
        }

        public override void TickRequirement(float deltaTime)
        {
            bool bNowIn = IsInVehicle();
            if (bNowIn == _bInVehicle) return;

            _bInVehicle = bNowIn;

            if (bNowIn)
            {
                if (_bNotifiedFirstEntry)
                    Notify(GetInMessage, "已上车");
                _bNotifiedFirstEntry = true;
            }
            else
            {
                Notify(GetBackInMessage, "请回到载具上");
            }
        }

        /// <summary>
        /// 角色当前是否在载具里：先看拥有者登记的驾驶员，再看挂载组件的座位表
        /// （乘客不算驾驶，但 UE5 的 <c>ClassIsChildOf(NewPawn, VehicleType)</c>
        /// 同样覆盖「被载具承载」这一层语义）。
        /// </summary>
        private bool IsInVehicle()
        {
            var pawn = ResolvePawnActor();
            if (pawn == null) return false;

            var vehicles = FlaxEngine.Level.GetScripts<NarrativeVehicleBase>(null);
            if (vehicles != null)
            {
                foreach (var vehicle in vehicles)
                {
                    if (vehicle != null && ReferenceEquals(vehicle.GetVehicleController(), pawn)) return true;
                }
            }

            var mounts = FlaxEngine.Level.GetScripts<MountComponent>(null);
            if (mounts != null)
            {
                foreach (var mount in mounts)
                {
                    if (mount?.ActorToSeatIndexMap != null && mount.ActorToSeatIndexMap.ContainsKey(pawn)) return true;
                }
            }

            return false;
        }

        private static void Notify(string message, string fallback)
        {
            NarrativePro.NarrativeProPlugin.ShowNotification(
                string.IsNullOrEmpty(message) ? fallback : message, 4f);
        }
    }
}
