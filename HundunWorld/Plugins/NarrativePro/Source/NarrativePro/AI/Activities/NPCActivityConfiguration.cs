using System;
using System.Collections.Generic;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// NPC 活动配置。对应 UE5 UNPCActivityConfiguration。
    /// 数据资产，定义 NPC 可执行的活动和使用的目标生成器。
    /// </summary>
    [Serializable]
    public class NPCActivityConfiguration
    {
        /// <summary>重新评分目标的间隔（秒）。值越小响应越快但性能消耗越大</summary>
        public float RescoreInterval = 1.0f;

        /// <summary>默认活动类型路径列表</summary>
        public List<string> DefaultActivities = new List<string>();

        /// <summary>目标生成器类型路径列表</summary>
        public List<string> GoalGenerators = new List<string>();
    }
}
