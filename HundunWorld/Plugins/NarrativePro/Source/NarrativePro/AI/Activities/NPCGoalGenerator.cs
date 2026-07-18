using System;
using NarrativePro.Items;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// 已保存的目标生成器。对应 UE5 FSavedNPCGoalGenerator。
    /// </summary>
    [Serializable]
    public class SavedNPCGoalGenerator
    {
        public string ClassPath = "";
        public byte[] Data = new byte[0];
    }

    /// <summary>
    /// 目标生成器基类。对应 UE5 UNPCGoalGenerator。
    /// 为 NPC 生成可执行的目标项。子类覆盖 InitializeGoalGenerator 以绑定事件或启动逻辑。
    /// 优势：只需添加 NPC 需要的生成器，减少不必要的处理；支持配置（如传奇敌人搜索距离更远）。
    /// </summary>
    [Serializable]
    public abstract class NPCGoalGenerator
    {
        /// <summary>是否将此生成器保存到磁盘</summary>
        public bool bSaveGoalGenerator = false;

        /// <summary>拥有此生成器的 NPC 控制器 ID（运行时设置）</summary>
        [NonSerialized]
        public string OwnerControllerId = "";

        /// <summary>拥有此生成器的活动组件（运行时设置）</summary>
        [NonSerialized]
        public NPCActivityComponent OwnerActivityComponent;

        /// <summary>
        /// 初始化生成器。由活动组件在添加生成器时调用。
        /// </summary>
        public virtual void Initialize(string ownerControllerId, NPCActivityComponent ownerComp)
        {
            OwnerControllerId = ownerControllerId;
            OwnerActivityComponent = ownerComp;
            InitializeGoalGenerator();
        }

        /// <summary>子类覆盖以设置生成器（绑定事件、启动逻辑等）</summary>
        public virtual void InitializeGoalGenerator() { }

        /// <summary>添加目标项到活动组件</summary>
        /// <param name="goal">要添加的目标</param>
        /// <param name="bTriggerReselect">是否触发活动重新选择</param>
        /// <returns>添加成功的目标项</returns>
        protected NPCGoalItem AddGoalItem(NPCGoalItem goal, bool bTriggerReselect = false)
        {
            if (OwnerActivityComponent != null && goal != null)
            {
                return OwnerActivityComponent.AddGoal(goal, bTriggerReselect);
            }
            return null;
        }

        /// <summary>移除目标项</summary>
        protected void RemoveGoalItem(NPCGoalItem goal)
        {
            if (OwnerActivityComponent != null && goal != null)
            {
                OwnerActivityComponent.RemoveGoal(goal);
            }
        }
    }
}
