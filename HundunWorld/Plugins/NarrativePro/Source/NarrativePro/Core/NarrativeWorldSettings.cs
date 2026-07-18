using FlaxEngine;
using NarrativePro.Music;

namespace NarrativePro.Core
{
    /// <summary>
    /// 关卡世界设置。对应 UE5 ANarrativeWorldSettings。
    /// UE5 中继承 AWorldSettings，Flax 中无 WorldSettings 基类，以 Script 实现等价功能。
    /// 每个 Scene 应挂载一个此 Script。
    /// </summary>
    public class NarrativeWorldSettings : Script
    {
        /// <summary>覆盖默认音乐集（为空时使用全局默认）。</summary>
        public TaggedMusicSet DefaultMusicSetOverride;

        /// <summary>获取当前场景的 NarrativeWorldSettings。</summary>
        public static NarrativeWorldSettings Get(Scene scene)
        {
            if (scene == null) return null;
            return scene.GetScript<NarrativeWorldSettings>();
        }

        /// <summary>获取当前激活场景的 NarrativeWorldSettings。</summary>
        public static NarrativeWorldSettings GetCurrent()
        {
            return Get(Level.GetScene(0));
        }

        public override void OnEnable()
        {
            base.OnEnable();
            if (DefaultMusicSetOverride != null)
            {
                NarrativeLog.Log($"NarrativeWorldSettings: 覆盖默认音乐集 = {DefaultMusicSetOverride.GetType().Name}");
            }
        }
    }
}
