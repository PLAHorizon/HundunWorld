using NarrativePro.Items;

namespace NarrativePro.Music
{
    /// <summary>
    /// 内置音乐主题标签。对应 UE5 NativeMusicThemeTags.cpp 中定义的 TAG_MUSIC_*。
    /// 这些标签用于 <see cref="NarrativeMusicSubsystem"/> 设置当前播放主题。
    /// </summary>
    public static class NativeMusicThemeTags
    {
        /// <summary>环境主题（默认主题，场景进入后自动激活）。</summary>
        public static readonly GameplayTag TAG_MUSIC_AMBIENT = new GameplayTag("Music.Ambient");

        /// <summary>战斗主题（进入战斗时切换）。</summary>
        public static readonly GameplayTag TAG_MUSIC_COMBAT = new GameplayTag("Music.Combat");

        // 预留：
        // public static readonly GameplayTag TAG_MUSIC_PAUSE = new GameplayTag("Music.Pause");
        // public static readonly GameplayTag TAG_MUSIC_LOADING = new GameplayTag("Music.Loading");
    }
}
