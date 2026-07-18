using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Navigation;

namespace NarrativePro.AI
{
    /// <summary>
    /// 自定义路径跟随组件。对应 UE5 UNarrativePathFollowingComp。
    /// 继承 UE5 UPathFollowingComponent，用于扩展路径跟随行为。
    /// 注：Flax 无 PathFollowingComponent 基类，此处作为 Script 占位，
    /// 待导航子系统实现后补全路径跟随逻辑。
    /// </summary>
    public class NarrativePathFollowingComp : Script
    {
        // Flax-不兼容: UE5 的 PathFollowingComponent 基类在 Flax 无对应物，保留占位。原文 TODO: Flax 无 PathFollowingComponent 基类，待导航子系统实现后补全

        /// <summary>缓存的上一次目标位置</summary>
        [NonSerialized]
        public Vector3 CachedLastDestination = Vector3.Zero;

        /// <summary>
        /// 路径完成时调用。对应 UE5 OnPathFinished。
        /// </summary>
        /// <param name="result">路径跟随结果</param>
        public virtual void OnPathFinished(EPathFollowingResult result)
        {
            // Flax-不兼容: UE5 的 PathFollowingComponent.OnPathFinished 在 Flax 无对应物，保留占位。原文 TODO: 实现路径完成处理逻辑
            // UE5 中调用基类 OnPathFinished 并触发相关事件
            NarrativeLog.Log($"路径跟随完成: {result}");
        }
    }
}
