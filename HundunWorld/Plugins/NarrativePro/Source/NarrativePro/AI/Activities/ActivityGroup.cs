using System;
using System.Collections.Generic;
using NarrativePro.Items;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// 活动组。对应 UE5 UActivityGroup。
    /// 将活动分组以便提前过滤无效活动，支持嵌套子组。
    /// </summary>
    [Serializable]
    public class ActivityGroup
    {
        /// <summary>组标签（可选，用于按标签查找组以便动态增删活动）</summary>
        public GameplayTag GroupTag = GameplayTag.None;

        /// <summary>此组支持的目标类型路径（用于提前过滤，无此类型目标则跳过组检查）</summary>
        public string SupportedGoalType = "";

        /// <summary>子活动组</summary>
        public List<ActivityGroup> Subgroups = new List<ActivityGroup>();

        /// <summary>组内活动类型路径列表</summary>
        public List<string> GroupActivities = new List<string>();

        /// <summary>组内活动实例列表（运行时通过类型路径加载）</summary>
        [NonSerialized]
        public List<NPCActivity> GroupActivityInstances = new List<NPCActivity>();

        /// <summary>拥有此组的 NPC 控制器（运行时设置）</summary>
        [NonSerialized]
        public string OwnerControllerId = "";

        /// <summary>拥有此组的活动组件（运行时设置）</summary>
        [NonSerialized]
        public NPCActivityComponent OwnerActivityComp;

        /// <summary>设置拥有者</summary>
        public void SetOwner(string ownerControllerId, NPCActivityComponent ownerComp)
        {
            OwnerControllerId = ownerControllerId;
            OwnerActivityComp = ownerComp;
            foreach (var subgroup in Subgroups)
            {
                subgroup.SetOwner(ownerControllerId, ownerComp);
            }
        }

        /// <summary>
        /// 返回此组是否可用。子类可覆盖以定义可用性条件。
        /// </summary>
        /// <param name="failReason">失败原因（输出）</param>
        public virtual bool CanUseGroup(out string failReason)
        {
            failReason = "";
            return true;
        }

        /// <summary>获取组内所有活动实例</summary>
        public void GetActivitesInGroup(List<NPCActivity> outActivities)
        {
            if (outActivities == null) return;
            outActivities.AddRange(GroupActivityInstances);
            foreach (var subgroup in Subgroups)
            {
                subgroup.GetActivitesInGroup(outActivities);
            }
        }
    }
}
