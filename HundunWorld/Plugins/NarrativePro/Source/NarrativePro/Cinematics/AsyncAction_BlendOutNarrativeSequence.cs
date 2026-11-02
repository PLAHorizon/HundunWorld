using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NarrativePro.Cinematics
{
    /// <summary>
    /// 蓝图异步节点「混合输出叙事序列并在完成后通知」的端口。
    /// 对应 UE5 <c>UAsyncAction_BlendOutNarrativeSequence</c>（NarrativeLevelSequenceActor.h:268 / .cpp:466-492）：
    /// Activate 时先订阅播放器 <c>OnPause</c>（混合输出停稳后的暂停事件），再调
    /// <c>LevelSequenceActorRef-&gt;BlendOutAndStop()</c>，收到事件后广播 <c>OnFinished</c>。
    /// <para>
    /// Flax 无序列混合器：本端口 <c>NarrativeLevelSequenceActor.BlendOutAndStop()</c> 的收敛信号是
    /// <see cref="NarrativeLevelSequenceActor.OnFinished"/>（同步广播），因此这里先订阅 Actor 的
    /// OnFinished 再触发 BlendOutAndStop，与 UE5「先挂回调再开始混合输出」的时序一致。
    /// </para>
    /// <para>
    /// 真实调用方：<c>Tales/Dialogue/Dialogue.EndCinematicDialogueOption</c> —— 电影对话选项的
    /// 玩家台词结束/退出对话时，用本节点把目的地序列混合输出并等待完成后再销毁承载 Actor。
    /// </para>
    /// </summary>
    public class AsyncAction_BlendOutNarrativeSequence
    {
        /// <summary>UE5：RegisterWithGameInstance 保活；静态表等价实现（仅 bPersistant 时登记）。</summary>
        private static readonly List<AsyncAction_BlendOutNarrativeSequence> PersistentActions = new List<AsyncAction_BlendOutNarrativeSequence>();

        /// <summary>被混合输出的序列 Actor。对应 UE5 <c>LevelSequenceActor</c> 弱引用。</summary>
        public NarrativeLevelSequenceActor SequenceActor { get; private set; }

        /// <summary>混合输出完成广播。对应 UE5 BlueprintAssignable 委托 <c>OnFinished</c>。</summary>
        public event Action OnFinished;

        private readonly TaskCompletionSource<bool> _completion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool _bReadyToDestroy;

        /// <summary>等待混合输出完成的 awaitable。对应 UE5 蓝图 ExposedAsyncProxy 的等待语义。</summary>
        public Task WaitAsync() => _completion.Task;

        /// <summary>动作是否已结束。</summary>
        public bool IsDone => _bReadyToDestroy;

        /// <summary>
        /// 静态入口。对应 UE5 <c>BlendOutNarrativeSequence(SequenceActor)</c>：Actor 为空返回 null。
        /// </summary>
        public static AsyncAction_BlendOutNarrativeSequence BlendOutNarrativeSequence(
            NarrativeLevelSequenceActor sequenceActor,
            bool bPersistant = false)
        {
            if (sequenceActor == null)
            {
                return null;
            }

            var action = new AsyncAction_BlendOutNarrativeSequence
            {
                SequenceActor = sequenceActor,
            };

            if (bPersistant)
            {
                lock (PersistentActions)
                {
                    PersistentActions.Add(action);
                }
            }

            action.Activate();
            return action;
        }

        /// <summary>对应 UE5 <c>Activate</c>：订阅完成信号后启动混合输出。</summary>
        private void Activate()
        {
            SequenceActor.OnFinished += HandleFinished;
            SequenceActor.BlendOutAndStop(); // Flax 端会同步广播 OnFinished → 本动作通常立即完成

            if (_bReadyToDestroy)
            {
                return;
            }

            // 兜底：若未来 BlendOutAndStop 改为异步收敛而此处仍未完成，等待方也不会悬挂——
            // 与 UE5 相同，超时由 Actor 的停止事件负责，不额外造计时器。
        }

        /// <summary>对应 UE5 <c>Finished</c>。</summary>
        private void HandleFinished(NarrativeLevelSequenceActor actor)
        {
            if (_bReadyToDestroy) return;
            OnFinished?.Invoke();
            _completion.TrySetResult(true);
            EndTask();
        }

        /// <summary>结束动作：解绑事件、解除保活。对应 UE5 <c>SetReadyToDestroy</c>。</summary>
        public void EndTask()
        {
            if (_bReadyToDestroy) return;
            _bReadyToDestroy = true;

            if (SequenceActor != null)
            {
                SequenceActor.OnFinished -= HandleFinished;
                SequenceActor = null;
            }

            lock (PersistentActions)
            {
                PersistentActions.Remove(this);
            }

            _completion.TrySetResult(false);
        }
    }
}
