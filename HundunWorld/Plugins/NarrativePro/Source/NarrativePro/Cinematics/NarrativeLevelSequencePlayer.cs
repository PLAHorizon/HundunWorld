using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Cinematics
{
    /// <summary>
    /// 叙事序列播放器。对应 UE5 UNarrativeLevelSequencePlayer。
    /// UE5 中继承 ULevelSequencePlayer；Flax 无 LevelSequence 等价物。
    /// 此类为占位，保留接口供未来扩展。
    /// </summary>
    [Serializable]
    public class NarrativeLevelSequencePlayer
    {
        /// <summary>关联的序列 Actor</summary>
        [NonSerialized]
        public NarrativeLevelSequenceActor SequenceActor;

        /// <summary>是否正在播放</summary>
        public bool bIsPlaying = false;

        /// <summary>当前播放时间（秒）</summary>
        public float CurrentTime = 0f;

        /// <summary>序列总时长（秒）</summary>
        public float TotalDuration = 0f;

        /// <summary>播放完成回调</summary>
        public event OnSequenceFinished OnFinished;

        public NarrativeLevelSequencePlayer() { }

        public NarrativeLevelSequencePlayer(NarrativeLevelSequenceActor actor)
        {
            SequenceActor = actor;
        }

        /// <summary>开始播放时调用</summary>
        protected virtual void OnStartedPlaying()
        {
            bIsPlaying = true;
            CurrentTime = 0f;
            NarrativeLog.Log("序列播放开始");
        }

        /// <summary>停止播放时调用</summary>
        protected virtual void OnStopped()
        {
            bIsPlaying = false;
            NarrativeLog.Log("序列播放停止");
            OnFinished?.Invoke(SequenceActor);
        }

        /// <summary>开始播放</summary>
        public virtual void Play()
        {
            OnStartedPlaying();
        }

        /// <summary>停止播放</summary>
        public virtual void Stop()
        {
            OnStopped();
        }

        /// <summary>每帧更新播放器</summary>
        public virtual void Tick(float deltaTime)
        {
            if (!bIsPlaying) return;
            CurrentTime += deltaTime;
            if (TotalDuration > 0f && CurrentTime >= TotalDuration)
            {
                Stop();
            }
        }
    }
}
