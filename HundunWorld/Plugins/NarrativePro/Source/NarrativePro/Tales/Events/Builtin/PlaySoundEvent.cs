using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 播放音效事件。对应 UE5 <c>UNarrativeEvent_PlaySound</c>（UGameplayStatics::PlaySoundAtLocation/Attached）。
    /// </summary>
    public class PlaySoundEvent : NarrativeEvent
    {
        /// <summary>声音资产路径。</summary>
        public string SoundPath { get; set; } = "";

        /// <summary>音量（0-1）。</summary>
        public float Volume { get; set; } = 1.0f;

        /// <summary>是否 2D 播放（不随距离衰减）。</summary>
        public bool bIs2D { get; set; } = false;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (string.IsNullOrEmpty(SoundPath))
            {
                NarrativeLog.LogWarning("PlaySoundEvent: SoundPath 为空，跳过");
                return;
            }

            var clip = Content.Load<AudioClip>(SoundPath);
            if (clip == null)
            {
                NarrativeLog.LogWarning($"PlaySoundEvent: 无法加载声音资产 '{SoundPath}'");
                return;
            }

            // Flax 的音源是 Actor 组件 AudioSource（音频资产类型为 AudioClip，无 Sound/SoundSource）。
            // 优先挂在事件目标角色上，拿不到角色时不播放并告警。
            var character = ArsenalStatics.ResolveCharacterFromContext(target, controller);
            var host = character?.Actor ?? target as Actor ?? controller as Actor;

            if (host != null)
            {
                var source = new AudioSource
                {
                    Name = "NarrativeEventSound",
                    Clip = clip,
                    Volume = Volume,
                    IsLooping = false,
                    PlayOnStart = true,
                    // Is3D 只读：2D/3D 由可写的 AllowSpatialization 控制（关掉空间化即 2D）
                    AllowSpatialization = !bIs2D,
                };

                // Level.SpawnActor 返回 true 表示失败（Flax 约定）；parent 用宿主角色，声音随之移动
                if (Level.SpawnActor(source, host))
                {
                    NarrativeLog.LogWarning($"PlaySoundEvent: 音源生成失败，声音未播放：{SoundPath}");
                    return;
                }

                source.Play();

                // 对应 UE5 一次性 UAudioComponent 的自动销毁：按片段时长延迟回收，避免累积音源 Actor
                // （FlaxEngine.Mathf 无 Max，这里用 System.Math）
                FlaxEngine.Object.Destroy(source, System.Math.Max(clip.Length, 0.1f));
                return;
            }

            NarrativeLog.LogWarning("PlaySoundEvent: 事件上下文没有可挂载音源的 Actor，声音未播放");
        }

        public override string GetGraphDisplayText() => $"Play Sound: {SoundPath}";
    }
}
