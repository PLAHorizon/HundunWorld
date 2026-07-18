using System;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 运行时任务设置。对应 UE5 UNarrativeQuestSettings。
    /// </summary>
    [Serializable]
    public class NarrativeQuestSettings
    {
        /// <summary>
        /// 完成任务后是否重置其进度，便于设计可重复完成的步骤。
        /// </summary>
        public bool bResetTasksWhenCompleted = false;

        /// <summary>单例实例（Flax 中由 NarrativeProPlugin 初始化）。</summary>
        public static NarrativeQuestSettings Instance { get; set; } = new NarrativeQuestSettings();
    }
}
