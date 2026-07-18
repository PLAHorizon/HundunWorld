using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 运行时对话设置。对应 UE5 UNarrativeDialogueSettings。
    /// 配置可在 DefaultEngine.ini 中覆盖（Flax 中通过 NarrativeSettings 暴露）。
    /// </summary>
    [Serializable]
    public class NarrativeDialogueSettings
    {
        /// <summary>对话行尾追加的静音缓冲时长（秒）。</summary>
        public float DialogueLineAudioSilence = 0.2f;

        /// <summary>对话文本最短显示时长（秒），防止过短回答一闪而过。</summary>
        public float MinDialogueTextDisplayTime = 2f;

        /// <summary>无音频时，按每秒字符数推算文本显示时长。</summary>
        public float LettersPerSecondLineDuration = 25f;

        /// <summary>仅有一个玩家回复时是否自动选择，无视 bAutoSelect 标志。</summary>
        public bool bAutoSelectSingleResponse = true;

        /// <summary>启用垂直走线（实验性，旧对话不自动重排）。</summary>
        public bool bEnableVerticalWiring = false;

        /// <summary>默认说话者颜色列表。</summary>
        public List<Color> SpeakerColors = new List<Color>
        {
            new Color(1f, 0.5f, 0.2f, 1f),
            new Color(0.4f, 0.8f, 1f, 1f),
            new Color(0.6f, 1f, 0.4f, 1f),
            new Color(1f, 0.9f, 0.3f, 1f),
            new Color(0.9f, 0.5f, 1f, 1f),
        };

        /// <summary>单例实例（Flax 中由 NarrativeProPlugin 初始化）。</summary>
        public static NarrativeDialogueSettings Instance { get; set; } = new NarrativeDialogueSettings();

        /// <summary>根据说话者索引获取颜色，超出范围则循环。</summary>
        public Color GetSpeakerColor(int speakerIndex)
        {
            if (SpeakerColors == null || SpeakerColors.Count == 0)
                return Color.White;
            int idx = speakerIndex % SpeakerColors.Count;
            if (idx < 0) idx += SpeakerColors.Count;
            return SpeakerColors[idx];
        }
    }
}
