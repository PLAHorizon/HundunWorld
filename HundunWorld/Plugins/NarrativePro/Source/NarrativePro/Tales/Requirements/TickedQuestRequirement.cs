using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Tales.Quest;
using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.Tales.Requirements
{
    /// <summary>
    /// 需要周期检查的任务要求基类。
    /// <para>
    /// UE5 依据：<c>UQuestRequirement</c> 本身只有 <c>OnAdded</c>/<c>OnRemoved</c>/
    /// <c>GetOwningQuest</c> 三个入口（Quest.h:100-118，实现见 Quest.cpp:628-647），
    /// <b>没有任何 tick</b>；三个内置要求是靠蓝图里的
    /// <c>CallFunc_K2_SetTimerDelegate_ReturnValue</c>（BPR_StayNearActor /
    /// BPR_KeepNPCAlive 的 <c>CheckTimerHandle</c>、<c>InitialStartDelay</c>、
    /// <c>bLooping</c>）自建世界计时器来做周期检查的。
    /// </para>
    /// <para>
    /// 端口差异：Flax 的 C# 普通对象没有 World Timer，也不允许在
    /// <see cref="QuestRequirement"/>（他人所有的基类）上加 tick 钩子，
    /// 故这里用与 <c>Tales/Async/AsyncAction_ProvideActor.cs</c> 相同的做法 ——
    /// 一个由静默 <see cref="AudioSource"/> 承载的 <see cref="Script"/> 泵，
    /// 活跃要求清空后自动销毁宿主。<see cref="NarrativeTaskManager"/> 之类的
    /// 既有静态泵都不适合承载要求生命周期，因此单独建一条。
    /// </para>
    /// </summary>
    public abstract class TickedQuestRequirement : QuestRequirement
    {
        /// <summary>检查间隔秒。对应蓝图计时器的 <c>InitialStartDelay</c>/<c>Interval</c>。</summary>
        public float CheckInterval { get; set; } = 1f;

        /// <summary>读档态：由 <see cref="Quest.Deinitialize"/> 与 quest 生命周期决定，不参与判定。</summary>
        [NonSerialized] internal float Accumulator;

        public override void OnAdded(QuestClass quest)
        {
            base.OnAdded(quest);
            Accumulator = 0f;
            QuestRequirementTicker.Register(this);
        }

        public override void OnRemoved(QuestClass quest)
        {
            QuestRequirementTicker.Unregister(this);
            base.OnRemoved(quest);
        }

        /// <summary>周期检查。对应蓝图里挂到 <c>SetTimerDelegate</c> 上的那个函数。</summary>
        public virtual void TickRequirement(float deltaTime)
        {
        }

        /// <summary>取要求所属角色的 Actor（对应蓝图 <c>GetOwningQuest</c> → <c>GetOwningPawn</c>）。</summary>
        protected Actor ResolvePawnActor()
        {
            if (OwningQuest == null) return null;

            var character = NarrativePro.Core.ArsenalStatics.ResolveCharacterFromContext(
                OwningQuest.GetOwningPawn(), OwningQuest.GetOwningController());
            if (character?.Actor != null) return character.Actor;

            return OwningQuest.GetOwningPawn() as Actor ?? OwningQuest.GetOwningController() as Actor;
        }
    }

    /// <summary>
    /// 任务要求的主线程轮询泵。仅在存在活跃 <see cref="TickedQuestRequirement"/> 时存活。
    /// </summary>
    internal static class QuestRequirementTicker
    {
        private static readonly List<TickedQuestRequirement> Active = new List<TickedQuestRequirement>();
        private static FlaxEngine.Actor _host;
        private static Pump _pump;

        public static void Register(TickedQuestRequirement requirement)
        {
            if (requirement == null) return;
            lock (Active)
            {
                if (Active.Contains(requirement)) return;
                Active.Add(requirement);
            }
            EnsurePump();
        }

        public static void Unregister(TickedQuestRequirement requirement)
        {
            if (requirement == null) return;
            bool bEmpty;
            lock (Active)
            {
                Active.Remove(requirement);
                bEmpty = Active.Count == 0;
            }
            if (bEmpty) StopPump();
        }

        private static void EnsurePump()
        {
            if (_pump != null && _pump.Actor != null) return;
            _pump = null;

            try
            {
                // FlaxEngine.Actor 为抽象类，用 AudioSource 作脚本宿主（AsyncAction_ProvideActor 同法）。
                var host = new FlaxEngine.AudioSource();
                host.Name = "QuestRequirementTicker";
                // 玩法期临时宿主：显式生成进玩法场景，不靠"无父级默认落 RootScene"
                if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(host, host.Name))
                {
                    // Flax 约定：SpawnActor 返回 true 表示失败
                    FlaxEngine.Object.Destroy(host);
                    return;
                }
                _host = host;
                _pump = host.AddScript<Pump>();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("QuestRequirementTicker：创建要求轮询泵失败：" + ex.Message);
                _pump = null;
            }
        }

        private static void StopPump()
        {
            if (_host != null) FlaxEngine.Object.Destroy(_host);
            _host = null;
            _pump = null;
        }

        private static void TickAll(float deltaTime)
        {
            TickedQuestRequirement[] snapshot;
            lock (Active)
            {
                if (Active.Count == 0) return;
                snapshot = Active.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                var requirement = snapshot[i];
                if (requirement == null) continue;

                if (requirement.OwningQuest == null)
                {
                    // 没人 OnRemoved 就被抛弃的要求（任务被直接丢弃而没 Deinitialize）留在这里只会让
                    // 泵 Actor 永不回收；顺手摘掉，Active 空了 StopPump 自然生效。
                    Unregister(requirement);
                    continue;
                }

                float interval = requirement.CheckInterval > 0f ? requirement.CheckInterval : 1f;
                requirement.Accumulator += deltaTime;
                if (requirement.Accumulator < interval) continue;

                // 先清零再回调：TickRequirement 里可能把它自己摘掉（要求达成 → quest 失败 → Deinitialize）。
                requirement.Accumulator = 0f;
                requirement.TickRequirement(interval);
            }
        }

        /// <summary>主线程泵。对应 UE5 的 World Timer。</summary>
        internal class Pump : Script
        {
            public override void OnUpdate()
            {
                TickAll(Time.DeltaTime);
            }
        }
    }
}
