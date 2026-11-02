using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// Foley 脚步消费者：订阅 <see cref="GaspAnimator.Footstep"/>。
    /// 约定资产路径 <c>Content/Audio/Foley/footstep_01..06.flax</c>；资产缺失时进入"事件模式"（只计数/日志），
    /// 让动画链路在没有美术资源的阶段也能被验收。
    /// </summary>
    public class GaspFoley : Script
    {
        public string ClipFolder = "Content/Audio/Foley";
        public float Volume = 0.6f;

        public int StepCount { get; private set; }
        public int LastSide { get; private set; } = -1;

        private GaspAnimator _gasp;
        private List<AudioClip> _clips;
        private AudioSource _source;

        public override void OnStart()
        {
            _gasp = Actor.GetScript<GaspAnimator>();
            if (_gasp == null)
                return;
            _gasp.Footstep += OnFootstep;
            LoadClips();
        }

        private void LoadClips()
        {
            _clips = new List<AudioClip>(6);
            for (int i = 1; i <= 6; i++)
            {
                try
                {
                    var clip = Content.Load<AudioClip>($"{ClipFolder}/footstep_{i:00}.flax");
                    if (clip != null && clip.IsLoaded)
                        _clips.Add(clip);
                }
                catch
                {
                    // 资产缺失/未导入：忽略，走事件模式。
                }
            }
            if (_clips.Count == 0)
                Debug.Log("[GaspFoley] 无脚声音频资产（Content/Audio/Foley/footstep_01..06.flax），事件模式（仅计数）");
        }

        private void OnFootstep(int side, Vector3 world, float speed)
        {
            StepCount++;
            LastSide = side;
            if (_clips.Count > 0)
            {
                if (_source == null)
                {
                    _source = new AudioSource { Name = "GaspFoleySource" };
                    Level.SpawnActor(_source, Actor.Parent);
                }
                _source.Position = world;
                _source.Clip = _clips[StepCount % _clips.Count];
                _source.Volume = Mathf.Clamp(Volume * Mathf.Saturate(0.5f + speed / 1000f), 0.1f, 1f);
                _source.Play();
            }
            else if (StepCount <= 4 || StepCount % 20 == 0)
            {
                Debug.Log($"[GaspFoley] step#{StepCount} side={(side == 0 ? "L" : "R")}"
                          + $" speed={speed:F0} at=({world.X:F0},{world.Y:F0},{world.Z:F0})");
            }
        }

        public override void OnDestroy()
        {
            if (_gasp != null)
                _gasp.Footstep -= OnFootstep;
            if (_source != null)
            {
                FlaxEngine.Object.Destroy(_source);
                _source = null;
            }
        }
    }
}
