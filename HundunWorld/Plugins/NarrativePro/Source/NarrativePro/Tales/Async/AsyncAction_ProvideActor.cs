using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Tales.Async
{
    /// <summary>
    /// 蓝图异步节点「向 Actor 提供者索取 Actor 并在就绪后通知」的端口。
    /// 对应 UE5 <c>UAsyncAction_ProvideActor</c>（NarrativeActorProvider.h:164 / .cpp:227-318）：
    /// Activate 先同步 <c>ProvideActor(World)</c>，取不到则按 PollInterval 起世界计时器轮询
    /// （PollInterval&lt;=0 时改订阅提供者 <c>OnProviderActorReady</c> 事件），成功广播
    /// <c>OnProvided</c>、失败广播 <c>OnFailed</c>，SetReadyToDestroy 清计时器与事件。
    /// <para>
    /// Flax 无 <c>UBlueprintAsyncActionBase</c>/World Timer，沿用本工程异步节点形态
    /// （事件 + <see cref="WaitAsync"/> Task + 保活；见 <c>AI/Activities/Async/AsyncAction_AddGoalAndWait</c>）。
    /// 轮询由 <see cref="AsyncActionProvideActorPoller"/>（主线程 Script）驱动，活跃动作清空后自动销毁宿主。
    /// Flax 端口的提供者未实现 OnProviderActorReady 委托（Core/NarrativeActorProvider.cs 只有同步
    /// ProvideActor），故 PollInterval&lt;=0 且即时取不到时按 UE5 失败路径广播 OnFailed。
    /// </para>
    /// <para>
    /// 真实调用方：<c>Tales/Tasks/NarrativeTask.SpawnDefaultNavigationMarker</c> 的
    /// <c>MarkerAttachActorOverride</c> 分支（UE5 QuestTask.cpp:218-229 的「提供者暂时取不到就用
    /// 回退位置并持续重试」语义），Actor 就绪后把导航标记重新挂到该 Actor 上。
    /// </para>
    /// </summary>
    public class AsyncAction_ProvideActor
    {
        /// <summary>活跃轮询动作表（对应 UE5 RegisterWithGameInstance + 计时器存活期）。</summary>
        private static readonly List<AsyncAction_ProvideActor> ActiveActions = new List<AsyncAction_ProvideActor>();

        private static AsyncActionProvideActorPoller _poller;
        private static Actor _pollerHost;

        /// <summary>缓存的提供者。对应 UE5 <c>CachedProvider</c>。</summary>
        public NarrativeActorProvider CachedProvider { get; private set; }

        /// <summary>世界上下文对象（UE5 WorldContextObject；端口传给 ProvideActor）。</summary>
        public object WorldContextObject { get; private set; }

        /// <summary>轮询间隔（秒）。对应 UE5 <c>PollInterval</c>。</summary>
        public float PollInterval { get; private set; }

        /// <summary>
        /// 调用方把 PollInterval 传成 0（UE5 走事件模式）时的兜底轮询间隔：
        /// 本端口的提供者多为同步实现，事件可能永不触发，不兜底就会既不成功也不失败地悬着。
        /// </summary>
        private const float DefaultPollIntervalSeconds = 0.25f;

        /// <summary>Actor 成功提供时广播。对应 UE5 <c>OnProvided</c>。</summary>
        public event Action<Actor> OnProvided;

        /// <summary>提供者缺失/无法提供时广播。对应 UE5 <c>OnFailed</c>。</summary>
        public event Action OnFailed;

        private readonly TaskCompletionSource<Actor> _completion =
            new TaskCompletionSource<Actor>(TaskCreationOptions.RunContinuationsAsynchronously);

        private float _elapsed;
        private bool _bReadyToDestroy;

        /// <summary>等待 Actor 就绪的 awaitable；失败时结果为 null。对应 UE5 ExposedAsyncProxy 等待语义。</summary>
        public Task<Actor> WaitAsync() => _completion.Task;

        /// <summary>动作是否已结束。</summary>
        public bool IsDone => _bReadyToDestroy;

        /// <summary>
        /// 静态入口。对应 UE5 <c>ProvideActor(WorldContextObject, Provider, PollInterval)</c>。
        /// </summary>
        public static AsyncAction_ProvideActor ProvideActor(object worldContextObject, NarrativeActorProvider provider, float pollInterval = 1f)
        {
            var action = new AsyncAction_ProvideActor
            {
                CachedProvider = provider,
                WorldContextObject = worldContextObject,
                PollInterval = pollInterval,
            };
            action.Activate();
            return action;
        }

        /// <summary>对应 UE5 <c>Activate</c>：先即时提供，取不到再轮询/失败。</summary>
        private void Activate()
        {
            if (CachedProvider != null)
            {
                var actor = CachedProvider.ProvideActor(WorldContextObject);
                if (actor != null)
                {
                    BroadcastProvided(actor);
                    return;
                }

                if (PollInterval > 0f)
                {
                    // 对应 UE5 SetTimer(PollHandle, PollActor, PollInterval, true)
                    lock (ActiveActions)
                    {
                        ActiveActions.Add(this);
                    }
                    EnsurePoller();
                    return;
                }

                // UE5 在 PollInterval<=0 时改等 OnProviderActorReady 事件（现已在
                // Core/NarrativeActorProvider.cs 上提供）。这里保留一条兜底轮询：
                // 提供者是同步实现的场合事件永远不会触发，不兜底就会静默既不成功也不失败。
                if (PollInterval <= 0f)
                {
                    PollInterval = DefaultPollIntervalSeconds;
                }

                lock (ActiveActions)
                {
                    ActiveActions.Add(this);
                }
                EnsurePoller();
                return;
            }

            BroadcastFailed();
        }

        /// <summary>由轮询器驱动：到达间隔则再次索取 Actor。对应 UE5 <c>PollActor</c>。</summary>
        internal void Tick(float deltaTime)
        {
            if (_bReadyToDestroy) return;

            _elapsed += deltaTime;
            if (_elapsed < PollInterval) return;
            _elapsed = 0f;

            if (CachedProvider == null)
            {
                BroadcastFailed();
                return;
            }

            var actor = CachedProvider.ProvideActor(WorldContextObject);
            if (actor != null)
            {
                BroadcastProvided(actor);
            }
        }

        private void BroadcastProvided(Actor actor)
        {
            if (_bReadyToDestroy) return;
            OnProvided?.Invoke(actor);
            _completion.TrySetResult(actor);
            EndTask();
        }

        private void BroadcastFailed()
        {
            if (_bReadyToDestroy) return;
            OnFailed?.Invoke();
            _completion.TrySetResult(null);
            EndTask();
        }

        /// <summary>对应 UE5 <c>EndTask/ SetReadyToDestroy</c>：移出轮询表并在表空时销毁轮询器。</summary>
        public void EndTask()
        {
            if (_bReadyToDestroy) return;
            _bReadyToDestroy = true;

            lock (ActiveActions)
            {
                ActiveActions.Remove(this);
                if (ActiveActions.Count == 0)
                {
                    StopPoller();
                }
            }

            CachedProvider = null;
            WorldContextObject = null;
            _completion.TrySetResult(null);
        }

        private static void EnsurePoller()
        {
            if (_poller != null && _poller.Actor != null) return;
            _poller = null;

            try
            {
                // FlaxEngine.Actor 为抽象类，用具体 Actor 作脚本宿主（与 PlaySoundEvent/Bars 同款生成通路）。
                var host = new AudioSource();
                host.Name = "AsyncActionProvideActorPoller";
                // 玩法期临时宿主：显式生成进玩法场景，不靠"无父级默认落 RootScene"
                if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(host, host.Name))
                {
                    // Flax 约定：SpawnActor 返回 true 表示失败
                    FlaxEngine.Object.Destroy(host);
                    return;
                }
                _pollerHost = host;
                _poller = host.AddScript<AsyncActionProvideActorPoller>();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("AsyncAction_ProvideActor：创建轮询器失败：" + ex.Message);
                _poller = null;
            }
        }

        private static void StopPoller()
        {
            if (_pollerHost != null)
            {
                FlaxEngine.Object.Destroy(_pollerHost);
            }
            _pollerHost = null;
            _poller = null;
        }

        /// <summary>主线程轮询泵：每帧驱动所有活跃 <see cref="AsyncAction_ProvideActor"/>。对应 UE5 World Timer。</summary>
        internal class AsyncActionProvideActorPoller : Script
        {
            public override void OnUpdate()
            {
                float dt = Time.DeltaTime;
                AsyncAction_ProvideActor[] snapshot;
                lock (ActiveActions)
                {
                    if (ActiveActions.Count == 0) return;
                    snapshot = ActiveActions.ToArray();
                }

                for (int i = 0; i < snapshot.Length; i++)
                {
                    snapshot[i].Tick(dt);
                }
            }
        }
    }
}
