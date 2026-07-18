using FlaxEngine;
using NarrativePro.CommonUI.Widgets;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// Narrative CommonUI 静态函数库。对应 UE5 UNarrativeCommonUIFunctionLibrary（继承 UBlueprintFunctionLibrary）。
    ///
    /// 移植简化点：
    /// 1. UE5 中为蓝图可调用的静态函数集合，Flax 中转换为 static class。
    /// 2. UE5 中函数参数为 UCommonVideoPlayer* 与 UMediaSource*，Flax 中改为 NarrativeCommonVideoPlayer 占位类型
    ///    与字符串路径占位（替代 UMediaSource）。
    /// 3. 视频播放底层依赖 UE5 MediaFramework，Flax 中需用 Flax 视频播放相关 API 重新实现。
    /// </summary>
    public static class NarrativeCommonUIFunctionLibrary
    {
        /// <summary>
        /// 通知 CommonVideoPlayer 开始播放。对应 UE5 PlayCommonVideoPlayer。
        /// UE5 中此函数被暴露为蓝图可调用，因为基类的 Play 函数未暴露给蓝图。
        /// </summary>
        /// <param name="videoPlayer">要播放的视频播放器实例。</param>
        public static void PlayCommonVideoPlayer(NarrativeCommonVideoPlayer videoPlayer)
        {
            if (videoPlayer == null) return;

            // Flax-不兼容: UE5 的 CommonVideoPlayer 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonVideoPlayer 等价物，需用 Flax 视频播放 API 重新实现。
            videoPlayer.BPPlayFromStart();
            videoPlayer.IsLooping = true;
        }

        /// <summary>
        /// 设置 CommonVideoPlayer 的视频源。对应 UE5 SetCommonVideoPlayerSource。
        /// </summary>
        /// <param name="videoPlayer">要设置的视频播放器实例。</param>
        /// <param name="newVideoPath">新视频源路径（替代 UE5 UMediaSource*）。</param>
        public static void SetCommonVideoPlayerSource(NarrativeCommonVideoPlayer videoPlayer, string newVideoPath)
        {
            if (videoPlayer == null) return;

            // Flax-不兼容: UE5 的 CommonVideoPlayer 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonVideoPlayer 等价物，需用 Flax 视频播放 API 重新实现。
            videoPlayer.BPSetVideo(newVideoPath);
        }
    }
}
