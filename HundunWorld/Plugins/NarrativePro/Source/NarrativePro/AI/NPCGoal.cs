using System;
using System.Collections.Generic;
using NarrativePro.AI.Activities;

namespace NarrativePro.AI
{
    /// <summary>
    /// NPC 目标。对应 UE5 UNPCGoal。
    /// 目标存在于 NPC 活动组件上，由目标生成器添加，持有目标项（NPCGoalItem）。
    /// 例如 AttackEnemy 目标包含描述可用攻击目标的 AttackGoal 项。
    /// NPC 的活动通常通过检查是否存在目标来决定是否可运行。
    /// 例如攻击活动检查是否存在带有有效 AttackGoal 项的 AttackEnemy 目标。
    /// </summary>
    [Serializable]
    public class NPCGoal
    {
        /// <summary>拥有此目标的 NPC 控制器（运行时由系统设置）</summary>
        [NonSerialized]
        public NarrativeNPCController OwnerController;

        /// <summary>拥有此目标的活动组件（运行时由系统设置）</summary>
        [NonSerialized]
        public NPCActivityComponent OwnerActivityComponent;

        /// <summary>此目标当前持有的目标项列表</summary>
        public List<NPCGoalItem> GoalItems = new List<NPCGoalItem>();

        /// <summary>
        /// 添加目标项到此目标。
        /// </summary>
        /// <param name="goalItem">要添加的目标项</param>
        public void AddGoalItem(NPCGoalItem goalItem)
        {
            if (goalItem == null) return;
            if (GoalItems.Contains(goalItem)) return;
            GoalItems.Add(goalItem);
        }

        /// <summary>
        /// 从此目标移除目标项。
        /// </summary>
        /// <param name="goalItem">要移除的目标项</param>
        public void RemoveGoalItem(NPCGoalItem goalItem)
        {
            if (goalItem == null) return;
            GoalItems.Remove(goalItem);
        }

        /// <summary>
        /// 设置此目标的拥有者。
        /// </summary>
        /// <param name="ownerController">拥有此目标的 NPC 控制器</param>
        /// <param name="ownerComp">拥有此目标的活动组件</param>
        public void SetOwner(NarrativeNPCController ownerController, NPCActivityComponent ownerComp)
        {
            OwnerController = ownerController;
            OwnerActivityComponent = ownerComp;
        }
    }
}
