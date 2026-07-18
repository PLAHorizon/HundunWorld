using System;
using FlaxEngine;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// Narrative CommonUI 视频播放器占位。对应 UE5 UNarrativeCommonVideoPlayer（继承 UCommonVideoPlayer）。
    /// 在基类之上暴露几个 BP 友好的函数。
    ///
    /// 移植简化点：
    /// 1. Flax 完全没有 UMG / CommonUI 控件系统，也无 CommonVideoPlayer 等价物。
    ///    这里以 [Serializable] plain class 占位，保留 UE5 的类名、字段定义、方法签名。
    /// 2. UE5 中 UMediaSource* 改为 string 路径占位。
    /// 3. UE5 中基类的 SetVideo / PlayFromStart / Play / Close 未暴露给蓝图，这里以包装方法保留。
    /// 4. UI 渲染与视频解码部分需用 Flax 视频播放 API 重新实现。
    /// </summary>
    [Serializable]
    public class NarrativeCommonVideoPlayer
    {
        /// <summary>当前视频源路径（替代 UE5 UMediaSource*）。空字符串表示未设置。</summary>
        public string VideoSourcePath = string.Empty;

        /// <summary>是否循环播放。对应 UE5 SetLooping。</summary>
        public bool IsLooping = false;

        /// <summary>是否正在播放。</summary>
        public bool IsPlaying = false;

        /// <summary>
        /// 设置视频源。对应 UE5 BPSetVideo。
        /// UE5 中调用基类的 SetVideo（未暴露给蓝图）。
        /// </summary>
        /// <param name="newVideoPath">新视频源路径（替代 UE5 UMediaSource*）。</param>
        public void BPSetVideo(string newVideoPath)
        {
            // Flax-不兼容: UE5 的 CommonVideoPlayer 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonVideoPlayer 等价物，需用 Flax 视频播放 API 重新实现。
            VideoSourcePath = newVideoPath ?? string.Empty;
        }

        /// <summary>
        /// 从头开始播放。对应 UE5 BPPlayFromStart。
        /// UE5 中调用基类的 PlayFromStart（未暴露给蓝图）。
        /// </summary>
        public void BPPlayFromStart()
        {
            // Flax-不兼容: UE5 的 CommonVideoPlayer 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonVideoPlayer 等价物，需用 Flax 视频播放 API 重新实现。
            IsPlaying = true;
        }

        /// <summary>
        /// 播放视频。对应 UE5 BPPlay。
        /// UE5 中调用基类的 Play（未暴露给蓝图）。
        /// </summary>
        public void BPPlay()
        {
            // Flax-不兼容: UE5 的 CommonVideoPlayer 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonVideoPlayer 等价物，需用 Flax 视频播放 API 重新实现。
            IsPlaying = true;
        }

        /// <summary>
        /// 关闭视频。对应 UE5 BPClose。
        /// UE5 中调用基类的 Close（未暴露给蓝图）。
        /// </summary>
        public void BPClose()
        {
            // Flax-不兼容: UE5 的 CommonVideoPlayer 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonVideoPlayer 等价物，需用 Flax 视频播放 API 重新实现。
            IsPlaying = false;
        }
    }
}
