using System;
using FlaxEngine;
using NarrativePro.Cinematics;
using NarrativePro.Core;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「播一段过场并等它演完」的任务条目。对应 UE5 蓝图任务
    /// <c>BPT_PlayCutsceneAndWait</c>
    /// （Content/Pro/Core/Tales/Tasks/PlayCutsceneAndWait/BPT_PlayCutsceneAndWait.uasset）。
    /// <para>
    /// 二进制证据：图先过 <c>CallFunc_HasAuthority_ReturnValue</c> 权威门，再用
    /// <c>CallFunc_CreateNarrativeLevelSequencePlayer_OutActor</c>
    /// （<c>UNarrativeFunctionLibrary::CreateNarrativeLevelSequencePlayer</c>）拿到承载 Actor，
    /// 属性 <c>AutoStartSequence</c> / <c>CutscenePath</c> 决定即时启动还是等手动播放，
    /// 之后等 <c>OnFinished</c> 委托 → <c>CallFunc_CompleteTask</c>；图里还引用了
    /// <c>BPT_GoToLocation_C</c>（把玩家先走到过场起点）与 <c>BP_NarrativePlayerController</c>
    /// （输入屏蔽），端口由播放设置的 <c>bWalkBindingsToStartTransform</c> 与
    /// <c>TagsToApplyWhilstBound</c> 承担。
    /// </para>
    /// <para>
    /// 端口差异：Flax 没有 <c>CreateNarrativeLevelSequencePlayer</c> 这个「生成并返回 Actor」的
    /// 入口，端口既有做法（Dialogue.cs:624-662 的电影对话选项）是新 spawn 一个静默
    /// <c>AudioSource</c> 当脚本宿主再 <c>AddScript&lt;NarrativeLevelSequenceActor&gt;</c>，
    /// 本任务沿用同一通路；<c>AutoStartSequence=false</c> 时只准备宿主不启动播放，
    /// 因为没有对外暴露的「稍后再播」句柄，此时任务会立即完成并告警。
    /// </para>
    /// </summary>
    [Serializable]
    public class PlayCutsceneAndWaitTask : NarrativeTask
    {
        /// <summary>过场序列资源路径。对应 UE5 <c>CutscenePath</c>。</summary>
        public string SequencePath { get; set; } = "";

        /// <summary>是否自动开播。对应 UE5 <c>AutoStartSequence</c>。</summary>
        public bool bAutoStartSequence { get; set; } = true;

        public bool bShowCinematicBars { get; set; } = true;
        public bool bCanSkip { get; set; } = false;
        public bool bStopDialogue { get; set; } = true;
        public bool bHideEvenEssentialHUDElements { get; set; } = false;
        public float PlayRate { get; set; } = 1f;

        /// <summary>先把绑定角色走到序列起点（对应图里内嵌的 GoToLocation 行为）。</summary>
        public bool bWalkBindingsToStartTransform { get; set; } = true;

        /// <summary>任务结束时是否混合输出停掉仍在播的过场。</summary>
        public bool bBlendOutOnEndTask { get; set; } = true;

        private FlaxEngine.Actor _host;
        private NarrativeLevelSequenceActor _sequenceActor;
        private AsyncAction_PlayNarrativeSequence _playAction;

        protected override void OnBeginTask()
        {
            if (string.IsNullOrEmpty(SequencePath))
            {
                NarrativeLog.LogWarning("PlayCutsceneAndWaitTask: SequencePath 为空，无过场可播");
                return;
            }

            var comp = NarrativeComp;
            if (comp != null && !comp.HasAuthority())
            {
                // 对应图里的 HasAuthority 门：非权威端不重复播过场，直接按完成放行。
                NarrativeLog.Log("PlayCutsceneAndWaitTask: 非权威端跳过过场播放");
                CompleteTask();
                return;
            }

            if (!SpawnSequenceActor())
                return;

            if (!bAutoStartSequence)
            {
                NarrativeLog.LogWarning("PlayCutsceneAndWaitTask: 端口没有「稍后再播」句柄，bAutoStartSequence=false 时按完成处理");
                CompleteTask();
                return;
            }

            var settings = _sequenceActor.NarrativeSequenceParams ?? new NarrativeSequencePlaybackSettings();
            settings.bShowCinematicBars = bShowCinematicBars;
            settings.bCanSkip = bCanSkip;
            settings.bStopDialogue = bStopDialogue;
            settings.bHideEvenEssentialHUDElements = bHideEvenEssentialHUDElements;
            settings.bWalkBindingsToStartTransform = bWalkBindingsToStartTransform;
            settings.PlayRate = PlayRate > 0f ? PlayRate : 1f;
            settings.bAutoPlay = true;

            _playAction = AsyncAction_PlayNarrativeSequence.PlayNarrativeSequence(_sequenceActor, SequencePath, settings);
            if (_playAction == null)
            {
                NarrativeLog.LogWarning($"PlayCutsceneAndWaitTask: 启动过场失败（{SequencePath}）");
                DestroyHost();
                return;
            }

            _playAction.OnFinished += HandleFinished;
        }

        private bool SpawnSequenceActor()
        {
            try
            {
                // FlaxEngine.Actor 抽象不可实例化：用 AudioSource 作序列脚本宿主（Dialogue.cs:628 同法）。
                _host = new FlaxEngine.AudioSource();
                _host.Name = "NarrativeTaskCutscene";
                // 过场属玩法期对象：显式生成进玩法场景，不靠"无父级默认落 RootScene"
                if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(_host, _host.Name))
                {
                    // Flax 约定：SpawnActor 返回 true 表示失败
                    FlaxEngine.Object.Destroy(_host);
                    _host = null;
                    return false;
                }
                _sequenceActor = _host.AddScript<NarrativeLevelSequenceActor>();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("PlayCutsceneAndWaitTask: 生成过场宿主失败：" + ex.Message);
                if (_host != null) FlaxEngine.Object.Destroy(_host);
                _host = null;
                return false;
            }

            if (_sequenceActor == null)
            {
                NarrativeLog.LogWarning("PlayCutsceneAndWaitTask: 宿主上没有 NarrativeLevelSequenceActor");
                DestroyHost();
                return false;
            }

            return true;
        }

        private void HandleFinished()
        {
            if (_playAction != null)
            {
                _playAction.OnFinished -= HandleFinished;
                _playAction = null;
            }

            DestroyHost();

            if (bIsActive)
                CompleteTask();
        }

        private void DestroyHost()
        {
            if (_host != null)
            {
                FlaxEngine.Object.Destroy(_host);
                _host = null;
            }
            _sequenceActor = null;
        }

        protected override void OnEndTask()
        {
            if (_playAction != null)
            {
                _playAction.OnFinished -= HandleFinished;
                _playAction.EndTask();
                _playAction = null;
            }

            var actor = _sequenceActor;
            var host = _host;
            _sequenceActor = null;
            _host = null;

            if (actor == null)
            {
                if (host != null) FlaxEngine.Object.Destroy(host);
                return;
            }

            if (!bBlendOutOnEndTask)
            {
                FlaxEngine.Object.Destroy(host);
                return;
            }

            // 与 Dialogue.cs:695-712 同法：混合输出窗口跑完再销毁宿主，否则收尾形同虚设。
            var blend = AsyncAction_BlendOutNarrativeSequence.BlendOutNarrativeSequence(actor);
            if (blend != null)
            {
                blend.OnFinished += () =>
                {
                    blend.EndTask();
                    if (host != null) FlaxEngine.Object.Destroy(host);
                };
                return;
            }

            if (host != null) FlaxEngine.Object.Destroy(host);
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return string.IsNullOrEmpty(SequencePath) ? "看过场" : $"看过场：{SequencePath}";
        }
    }
}
