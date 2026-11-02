using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NarrativePro.Cinematics
{
    /// <summary>
    /// 蓝图异步节点「播放叙事序列并在结束时通知」的端口。
    /// 对应 UE5 <c>UAsyncAction_PlayNarrativeSequence</c>（NarrativeLevelSequenceActor.h:242 / .cpp:435-463）：
    /// Activate 时 <c>LevelSequenceActorRef-&gt;UpdateSequence(Sequence, PlaybackSettings)</c>
    /// 并订阅播放器 <c>OnFinished</c>，播放结束广播 <c>OnFinished</c> 委托。
    /// <para>
    /// Flax 无 <c>UBlueprintAsyncActionBase</c>，沿用本工程既有异步节点形态
    /// （见 <c>AI/Activities/Async/AsyncAction_AddGoalAndWait</c> 与
    /// <c>Tales/Async/AsyncAction_BeginDialogueAndWait</c>）：
    /// 事件回调 + <see cref="WaitAsync"/> Task 等待路径 + bPersistant 静态保活表。
    /// </para>
    /// <para>
    /// 真实调用方：对话演出「电影对话选项」时由 <c>Tales/Dialogue/Dialogue.BeginCinematicDialogueOption</c>
    /// 调用本节点播放目的地序列；序列停止（自然时长结束或 <c>BlendOutAndStop</c>）时经
    /// <see cref="NarrativeLevelSequenceActor.OnFinished"/> 广播完成。
    /// </para>
    /// </summary>
    public class AsyncAction_PlayNarrativeSequence
    {
        /// <summary>UE5：RegisterWithGameInstance 保活；此处用静态表等价实现（仅 bPersistant 时登记）。</summary>
        private static readonly List<AsyncAction_PlayNarrativeSequence> PersistentActions = new List<AsyncAction_PlayNarrativeSequence>();

        /// <summary>被播放的序列 Actor。对应 UE5 <c>LevelSequenceActor</c> 弱引用。</summary>
        public NarrativeLevelSequenceActor SequenceActor { get; private set; }

        /// <summary>目的地序列资源路径。对应 UE5 <c>Sequence</c>（Flax 无 LevelSequence 资产，用路径）。</summary>
        public string LevelSequencePath { get; private set; }

        /// <summary>播放完成广播。对应 UE5 BlueprintAssignable 委托 <c>OnFinished</c>。</summary>
        public event Action OnFinished;

        private readonly TaskCompletionSource<bool> _completion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool _bReadyToDestroy;

        /// <summary>等待序列播放完成的 awaitable。对应 UE5 蓝图 ExposedAsyncProxy 的等待语义。</summary>
        public Task WaitAsync() => _completion.Task;

        /// <summary>动作是否已结束（完成或 EndTask）。</summary>
        public bool IsDone => _bReadyToDestroy;

        /// <summary>
        /// 静态入口。对应 UE5 <c>PlayNarrativeSequence(SequenceActor, LevelSequence, Settings)</c>：
        /// Actor 或路径为空返回 null（UE5 的 IsValid 校验）；否则订阅完成事件并启动播放。
        /// </summary>
        /// <param name="sequenceActor">承载序列的 NarrativeLevelSequenceActor</param>
        /// <param name="levelSequencePath">序列资源路径（UE5 的 ULevelSequence 在 Flax 以资源路径表达）</param>
        /// <param name="settings">叙事播放设置，null 时沿用 Actor 现有设置</param>
        /// <param name="bPersistant">true 时保活到手动 EndTask（对应 UE5 RegisterWithGameInstance）</param>
        public static AsyncAction_PlayNarrativeSequence PlayNarrativeSequence(
            NarrativeLevelSequenceActor sequenceActor,
            string levelSequencePath,
            NarrativeSequencePlaybackSettings settings,
            bool bPersistant = false)
        {
            if (sequenceActor == null || string.IsNullOrEmpty(levelSequencePath))
            {
                return null;
            }

            var action = new AsyncAction_PlayNarrativeSequence
            {
                SequenceActor = sequenceActor,
                LevelSequencePath = levelSequencePath,
                _settingsToApply = settings,
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

        /// <summary>对应 UE5 <c>Activate</c>：先订阅完成事件，再更新序列并启动播放。</summary>
        private void Activate()
        {
            // UE5 先 AddDynamic(player->OnFinished) 再 UpdateSequence；这里同样先订阅 Actor 的
            // OnFinished（Blax 端 BlendOutAndStop/OnStop 都会广播它），保证任何停止路径都能唤醒等待方。
            SequenceActor.OnFinished += HandleSequenceFinished;

            // 对应 UE5 UpdateSequence(Sequence.Get(), PlaybackSettings)：
            // 设置参数为空时保留 Actor 现有参数（对话选项 bKeepExistingSequenceSettings 的落地路径）。
            var effectiveSettings = _settingsToApply ?? SequenceActor.NarrativeSequenceParams;
            SequenceActor.UpdateSequence(LevelSequencePath, effectiveSettings);

            // 启动播放（绑定校验 + 走到起点或直接 PlaySequence，标签施加等由 Actor 内部完成）。
            if (!SequenceActor.HandleBindingsAndStartSequence())
            {
                // UE5 中 Actor 无效时节点不启动；这里解除订阅并按失败结束，避免悬挂等待。
                SequenceActor.OnFinished -= HandleSequenceFinished;
                SequenceActor = null;
                _completion.TrySetResult(false);
                EndTask();
            }
        }

        // Activate 前暂存的播放设置（UE5 PlaybackSettings 成员）。
        private NarrativeSequencePlaybackSettings _settingsToApply;

        /// <summary>对应 UE5 <c>Finished</c>：广播 OnFinished 并清理。</summary>
        private void HandleSequenceFinished(NarrativeLevelSequenceActor actor)
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
                SequenceActor.OnFinished -= HandleSequenceFinished;
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
