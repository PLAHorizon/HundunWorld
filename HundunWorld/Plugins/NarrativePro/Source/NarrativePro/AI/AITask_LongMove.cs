using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Navigation;

namespace NarrativePro.AI
{
    /// <summary>
    /// 长距离移动任务。对应 UE5 UAITask_LongMove。
    /// 使用 POI（兴趣点）链路在导航网格未完全生成时确定到目的地的路径。
    /// 若存在直接路径，则回退到传统的 AITask_MoveTo。
    /// 注：Flax 无 BehaviorTree/AITask 系统，此处简化为 Script 占位（Flax-不兼容）。
    /// </summary>
    public class AITask_LongMove : Script
    {
        // Flax-不兼容: UE5 的 AITask 基类在 Flax 无对应物，保留占位。原文 TODO: Flax 无 AITask 基类，完整逻辑待导航子系统/行为树就绪后实现

        /// <summary>最终目标位置</summary>
        [NonSerialized]
        public Vector3 FinalGoal = Vector3.Zero;

        /// <summary>当前路径点索引</summary>
        [NonSerialized]
        public int CurrentTargetIndex = 0;

        /// <summary>POI 更新频率（秒）</summary>
        public float UpdatePOIRate = 1f;

        /// <summary>请求是否已失败</summary>
        [NonSerialized]
        public bool bRequestFailed = false;

        /// <summary>生成的 POI 间路径点列表</summary>
        [NonSerialized]
        public List<Vector3> Path = new List<Vector3>();

        /// <summary>移动请求失败时触发</summary>
        public event Action OnRequestFailed;

        /// <summary>移动完成时触发。参数：路径跟随结果、AI 控制器</summary>
        public event Action<EPathFollowingResult, NarrativeNPCController> OnMoveFinished;

        /// <summary>
        /// 初始化长距离移动任务。
        /// </summary>
        /// <param name="controller">执行移动的 AI 控制器</param>
        /// <param name="goalLocation">目标位置</param>
        /// <param name="acceptanceRadius">到达判定半径</param>
        /// <param name="stopOnOverlap">重叠时是否完成移动</param>
        /// <param name="acceptPartialPath">是否接受部分路径</param>
        /// <param name="bLockAILogic">是否锁定 AI 逻辑</param>
        /// <param name="projectGoalOnNavigation">是否将目标投影到导航网格</param>
        /// <param name="requireNavigableEndLocation">是否要求终点可导航</param>
        public void Setup(NarrativeNPCController controller, Vector3 goalLocation, float acceptanceRadius,
            EAIOptionFlag stopOnOverlap, EAIOptionFlag acceptPartialPath, bool bLockAILogic,
            EAIOptionFlag projectGoalOnNavigation, EAIOptionFlag requireNavigableEndLocation)
        {
            // Flax-不兼容: UE5 的 AITask 初始化在 Flax 无对应物，保留占位。原文 TODO: 实现初始化逻辑（设置移动请求、绑定导航完成回调）
            FinalGoal = goalLocation;
        }

        /// <summary>
        /// 执行长距离移动。使用 POI 链路在导航网格未完全生成时确定路径。
        /// 若存在直接路径，则回退到传统 AITask_MoveTo。
        /// </summary>
        /// <param name="controller">执行移动的 AI 控制器</param>
        /// <param name="goalLocation">目标位置</param>
        /// <param name="acceptanceRadius">到达判定半径</param>
        /// <param name="stopOnOverlap">重叠时是否完成移动</param>
        /// <param name="acceptPartialPath">是否接受部分路径</param>
        /// <param name="bLockAILogic">是否锁定 AI 逻辑</param>
        /// <param name="projectGoalOnNavigation">是否将目标投影到导航网格</param>
        /// <param name="requireNavigableEndLocation">是否要求终点可导航</param>
        /// <returns>长距离移动任务实例</returns>
        public static AITask_LongMove RunLongMove(NarrativeNPCController controller, Vector3 goalLocation,
            float acceptanceRadius = -1f, EAIOptionFlag stopOnOverlap = EAIOptionFlag.Default,
            EAIOptionFlag acceptPartialPath = EAIOptionFlag.Default, bool bLockAILogic = true,
            EAIOptionFlag projectGoalOnNavigation = EAIOptionFlag.Default,
            EAIOptionFlag requireNavigableEndLocation = EAIOptionFlag.Default)
        {
            // Flax-不兼容: UE5 的 AITask 系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 AITask 系统，待导航子系统实现后补全
            NarrativeLog.LogWarning("AITask_LongMove.RunLongMove 尚未实现（Flax 无 BehaviorTree/AITask）");
            return null;
        }

        /// <summary>移动是否已失败</summary>
        public bool HasMoveFailed()
        {
            return bRequestFailed;
        }

        /// <summary>使用 A* 寻路计算起点 POI 到终点 POI 之间的路径</summary>
        /// <param name="startingPOI">起点 POI 数据</param>
        /// <param name="endingPOI">终点 POI 数据</param>
        /// <param name="outPath">输出的路径 POI 列表</param>
        /// <returns>是否计算成功</returns>
        protected virtual bool CalculatePath(POIData startingPOI, POIData endingPOI, out List<POIData> outPath)
        {
            // Flax-不兼容: UE5 的 POI A* 寻路依赖 NavMesh/AITask，在 Flax 无对应物，保留占位。原文 TODO: 实现基于 POI 的 A* 寻路
            outPath = new List<POIData>();
            return false;
        }

        /// <summary>移动完成回调</summary>
        /// <param name="result">路径跟随结果</param>
        protected virtual void MoveFinished(EPathFollowingResult result)
        {
            OnMoveFinished?.Invoke(result, Actor?.GetScript<NarrativeNPCController>());
        }

        /// <summary>执行移动</summary>
        /// <param name="goalLocation">目标位置</param>
        protected virtual void PerformMove(Vector3 goalLocation)
        {
            // Flax-不兼容: UE5 的 AITask 移动请求在 Flax 无对应物，保留占位。原文 TODO: 实现移动请求逻辑
        }

        /// <summary>获取起点和终点 POI</summary>
        /// <param name="outStartingPOI">输出的起点 POI</param>
        /// <param name="outEndingPOI">输出的终点 POI</param>
        /// <returns>是否成功获取</returns>
        protected virtual bool GetPOIPoints(out POIData outStartingPOI, out POIData outEndingPOI)
        {
            // Flax-不兼容: UE5 的 POI 查找依赖 NavMesh/AITask，在 Flax 无对应物，保留占位。原文 TODO: 实现最近 POI 查找
            outStartingPOI = null;
            outEndingPOI = null;
            return false;
        }

        /// <summary>导航过程中更新可导航 POI（跳过已可直达的后续 POI）</summary>
        protected virtual void UpdateNavigablePOI()
        {
            // Flax-不兼容: UE5 的可导航 POI 更新依赖 NavMesh/AITask，在 Flax 无对应物，保留占位。原文 TODO: 实现可导航 POI 更新
        }

        /// <summary>使长距离移动失败</summary>
        protected void FailLongMove()
        {
            bRequestFailed = true;
            OnRequestFailed?.Invoke();
        }
    }

    /// <summary>
    /// AI 选项标志。对应 UE5 EAIOptionFlag。
    /// 用于控制 AI 任务的可选行为，Default 表示使用默认值。
    /// </summary>
    public enum EAIOptionFlag
    {
        /// <summary>使用默认值</summary>
        Default,
        /// <summary>启用</summary>
        Enable,
        /// <summary>禁用</summary>
        Disable
    }
}
