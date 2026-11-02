using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Spawners;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「等某个刷新点被清空」的任务条目。对应 UE5 蓝图任务 <c>BPT_ClearSpawn</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_ClearSpawn.uasset）。
    /// <para>
    /// 二进制证据：该资产自带本地函数图（<c>FunctionGraphs</c> +
    /// <c>FindSpawnerInWorld.K2Node_FunctionEntry_0</c> /
    /// <c>IsSpawnCleared.K2Node_FunctionEntry_0</c>），实现方式为
    /// <c>CallFunc_ProvideActor_ReturnValue</c>（<c>AsyncAction_ProvideActor</c> 的
    /// <c>OnProvided</c>/<c>OnFailed</c>，属性 <c>SpawnFinder_Provider</c>/<c>PollInterval</c>）
    /// 拿到刷新 Actor → <c>CallFunc_K2_GetComponentsByClass_ReturnValue</c> 取
    /// <c>NPCSpawnComponent</c> → <c>DynamicCast_AsSpawner_Base</c> →
    /// <c>CallFunc_IsSpawnCleared_Cleared</c> 为真时 <c>CallFunc_Add_IntInt_ReturnValue</c>；
    /// 图里另有 <c>LookupActorByGUID</c>、<c>TickInterval</c>/<c>Interval</c>/<c>Duration</c>
    /// 与 <c>GetNavigationMarkerAttachActor</c>/<c>GetTaskNodeDescription</c> 覆写。
    /// </para>
    /// <para>
    /// 端口依据：<c>IsSpawnCleared</c> 在 UE5 插件与游戏内容里都查无实现
    /// （<c>grep -rIs SpawnCleared</c> 只命中本资产），故按名取义落在端口已有的
    /// 刷新判定上：<see cref="SpawnComponent.SpawnedActor"/> 为空且
    /// <see cref="NPCSpawnComponent.ShouldSpawnActor"/> 判定「不该再刷」（击杀记录生效）时算清空。
    /// </para>
    /// </summary>
    [Serializable]
    public class ClearSpawnTask : NarrativeTask
    {
        /// <summary>刷新点 Actor 的提供者。对应 <c>SpawnFinder_Provider</c>。</summary>
        public NarrativePro.Core.NarrativeActorProvider SpawnerProvider { get; set; }

        /// <summary>按 NPC 定义 ID 找回装点（端口 <c>FindNPCByID</c> 通路）。</summary>
        public string SpawnerNpcId { get; set; } = "";

        /// <summary>按存档 GUID 找回装点。对应图里的 <c>LookupActorByGUID</c>。</summary>
        public Guid SpawnerSaveGUID { get; set; } = Guid.Empty;

        /// <summary>轮询间隔秒。对应图里的 <c>Interval</c>。</summary>
        public float CheckInterval { get; set; } = 1f;

        private NPCSpawnComponent _spawner;
        private Actor _spawnerActor;
        private ProvideActorRequest _provideRequest;

        protected override void OnBeginTask()
        {
            TickInterval = BuiltinTaskSupport.PositiveOr(CheckInterval, 1f);

            var actor = BuiltinTaskSupport.ResolveTargetActor(SpawnerProvider, SpawnerNpcId, SpawnerSaveGUID);
            if (actor != null)
            {
                AttachSpawner(actor);
                return;
            }

            _provideRequest = new ProvideActorRequest(this, SpawnerProvider, HandleSpawnerProvided, TickInterval);
            if (_provideRequest == null || !_provideRequest.IsPending)
                NarrativeLog.LogWarning("ClearSpawnTask: 取不到刷新点 Actor（无提供者或提供者不可用）");
        }

        private void HandleSpawnerProvided(Actor actor)
        {
            _provideRequest = null;
            if (!bIsActive || actor == null) return;
            AttachSpawner(actor);
        }

        private void AttachSpawner(Actor actor)
        {
            _spawnerActor = actor;
            _spawner = actor.GetScript<NPCSpawnComponent>();
            if (_spawner == null)
                NarrativeLog.LogWarning($"ClearSpawnTask: Actor '{actor.Name}' 上没有 NPCSpawnComponent");
        }

        protected override void TickTask()
        {
            if (IsSpawnCleared())
                CompleteTask();
        }

        /// <summary>
        /// 刷新点是否已清空：当前没有活着的生成 Actor，且组件自身判定「不该再生成」
        /// （<c>bWasKilled</c> + <c>bDontSpawnIfPreviouslyKilled</c> 的既有裁决，
        /// 见 SpawnDecisionRules.cs:118-124）。
        /// 注意该裁决里「没有 NPC 定义」同样返回 false，所以配错的空刷新点会被判为已清空——
        /// 与 UE5 侧 <c>IsSpawnCleared</c> 由项目蓝图自行实现、无统一口径的情况一致。
        /// </summary>
        private bool IsSpawnCleared()
        {
            if (_spawner == null)
            {
                var actor = BuiltinTaskSupport.ResolveTargetActor(SpawnerProvider, SpawnerNpcId, SpawnerSaveGUID);
                if (actor != null) AttachSpawner(actor);
                return false;
            }

            return _spawner.SpawnedActor == null && !_spawner.ShouldSpawnActor();
        }

        /// <summary>把标记挂到刷新点上（对应图内的 GetNavigationMarkerAttachActor 覆写）。</summary>
        public override Actor GetNavigationMarkerAttachActor()
        {
            if (_spawnerActor == null)
                _spawnerActor = BuiltinTaskSupport.ResolveTargetActor(SpawnerProvider, SpawnerNpcId, SpawnerSaveGUID);
            return _spawnerActor;
        }

        protected override void OnEndTask()
        {
            _provideRequest?.Cancel();
            _provideRequest = null;
            _spawner = null;
            _spawnerActor = null;
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return string.IsNullOrEmpty(SpawnerNpcId) ? "清空该处的刷新点" : $"清空 {SpawnerNpcId} 的刷新点";
        }
    }
}
