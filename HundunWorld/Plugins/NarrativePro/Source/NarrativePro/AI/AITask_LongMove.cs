using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.Navigation;

namespace NarrativePro.AI
{
    /// <summary>
    /// 长距离移动的底层执行器抽象。
    /// <para>
    /// UE5 中 <c>UAITask_LongMove</c> 通过 <c>AAIController::MoveTo</c> + <c>UPathFollowingComponent</c> 驱动角色；
    /// Flax 无 AITask / PathFollowingComponent，故将「如何真正移动角色」抽为可插拔接口：
    /// 未提供执行器时使用内置直线推进（与 <see cref="MoveToLocationAndRotationTask"/> 一致的做法）。
    /// </para>
    /// </summary>
    public interface ILongMoveExecutor
    {
        /// <summary>请求移动到目标点。返回 false 表示无法发起移动（任务应立即判定失败）。</summary>
        bool RequestMove(Actor mover, Vector3 goal, float acceptanceRadius);

        /// <summary>当前是否仍在移动中。</summary>
        bool IsMoving(Actor mover);
    }

    /// <summary>
    /// 长距离移动任务。对应 UE5 <c>UAITask_LongMove</c>。
    /// <para>
    /// 当角色的导航网格尚未覆盖整张地图时，先用 **POI 图 A\*** 在地图级 POI 网络上求出一条高层路径，
    /// 再逐段执行移动；移动过程中按 <see cref="UpdatePOIRate"/> 持续检测是否已能直达更靠后的 POI，从而跳过中间节点。
    /// 若起终点本就存在完整导航路径，则直接单段移动（与 UE5 的早起分支一致）。
    /// </para>
    /// <para>
    /// Flax 无 AITask 基类，故按本插件既有约定实现为普通类 + <see cref="TickTask"/> 显式推进 + 静态工厂。
    /// </para>
    /// </summary>
    public class AITask_LongMove
    {
        /// <summary>默认到达判定半径</summary>
        public const float DefaultAcceptanceRadius = 50f;

        /// <summary>默认直线推进速度（世界单位/秒，仅内置执行器使用）</summary>
        public const float DefaultMoveSpeed = 600f;

        /// <summary>路径点数量上限，防御异常 POI 图导致 A* 失控</summary>
        private const int MaxPathNodes = 4096;

        /// <summary>最终目标位置</summary>
        public Vector3 FinalGoal { get; private set; }

        /// <summary>到达判定半径</summary>
        public float AcceptanceRadius { get; set; } = DefaultAcceptanceRadius;

        /// <summary>重叠时是否结束移动（对齐 UE5 EAIOptionFlag）</summary>
        public EAIOptionFlag StopOnOverlap { get; set; } = EAIOptionFlag.Default;

        /// <summary>是否接受部分路径（对齐 UE5 EAIOptionFlag）</summary>
        public EAIOptionFlag AcceptPartialPath { get; set; } = EAIOptionFlag.Default;

        /// <summary>是否把目标投影到导航网格（对齐 UE5 EAIOptionFlag）</summary>
        public EAIOptionFlag ProjectGoalOnNavigation { get; set; } = EAIOptionFlag.Default;

        /// <summary>是否要求终点可导航（对齐 UE5 EAIOptionFlag）</summary>
        public EAIOptionFlag RequireNavigableEndLocation { get; set; } = EAIOptionFlag.Default;

        /// <summary>是否锁定 AI 逻辑（保留签名以对齐 UE5，Flax 侧无 AI 逻辑锁概念）</summary>
        public bool bLockAILogic { get; set; } = true;

        /// <summary>高层 POI 重评估频率（次/秒）</summary>
        public float UpdatePOIRate { get; set; } = 1f;

        /// <summary>内置直线执行器的移动速度</summary>
        public float MoveSpeed { get; set; } = DefaultMoveSpeed;

        /// <summary>当前正在前往的路径点索引</summary>
        public int CurrentTargetIndex { get; private set; }

        /// <summary>生成的路径点列表（POI 位置序列 + 最终目标）</summary>
        public List<Vector3> Path { get; } = new List<Vector3>();

        /// <summary>请求是否已失败</summary>
        public bool HasMoveFailed => bRequestFailed;

        /// <summary>任务是否仍在运行</summary>
        public bool IsRunning { get; private set; }

        /// <summary>被驱动的角色 Actor</summary>
        public Actor Mover { get; private set; }

        /// <summary>所属 NPC 控制器</summary>
        public NarrativeNPCController Controller { get; private set; }

        /// <summary>可插拔的移动执行器；为 null 时使用内置直线推进</summary>
        public ILongMoveExecutor Executor { get; set; }

        /// <summary>移动请求失败时触发</summary>
        public event Action OnRequestFailed;

        /// <summary>移动完成时触发。参数：路径跟随结果、AI 控制器</summary>
        public event Action<EPathFollowingResult, NarrativeNPCController> OnMoveFinished;

        private bool bRequestFailed;
        private float _poiReevaluateTimer;
        private bool _activated;

        /// <summary>
        /// 初始化长距离移动任务。对应 UE5 <c>UAITask_LongMove::Setup</c>。
        /// </summary>
        public void Setup(NarrativeNPCController controller, Vector3 goalLocation, float acceptanceRadius,
            EAIOptionFlag stopOnOverlap = EAIOptionFlag.Default,
            EAIOptionFlag acceptPartialPath = EAIOptionFlag.Default,
            bool bLockAILogic = true,
            EAIOptionFlag projectGoalOnNavigation = EAIOptionFlag.Default,
            EAIOptionFlag requireNavigableEndLocation = EAIOptionFlag.Default)
        {
            Controller = controller;
            FinalGoal = goalLocation;
            AcceptanceRadius = acceptanceRadius;
            StopOnOverlap = stopOnOverlap;
            AcceptPartialPath = acceptPartialPath;
            this.bLockAILogic = bLockAILogic;
            ProjectGoalOnNavigation = projectGoalOnNavigation;
            RequireNavigableEndLocation = requireNavigableEndLocation;

            Mover = controller?.GetControlledNPC() ?? controller?.Actor;
        }

        /// <summary>
        /// 创建并激活一个长距离移动任务。对应 UE5 <c>UAITask_LongMove::RunLongMove</c>。
        /// 返回的任务需要由调用方按帧推进 <see cref="TickTask"/>。
        /// </summary>
        public static AITask_LongMove RunLongMove(NarrativeNPCController controller, Vector3 goalLocation,
            float acceptanceRadius = DefaultAcceptanceRadius,
            EAIOptionFlag stopOnOverlap = EAIOptionFlag.Default,
            EAIOptionFlag acceptPartialPath = EAIOptionFlag.Default, bool bLockAILogic = true,
            EAIOptionFlag projectGoalOnNavigation = EAIOptionFlag.Default,
            EAIOptionFlag requireNavigableEndLocation = EAIOptionFlag.Default)
        {
            // 注意：Flax 的 Vector3.IsZero() 未导出到 C#（头文件存在但脚本侧不可用），故用 LengthSquared 判定
            if (goalLocation.LengthSquared > 1e-8f && controller == null)
            {
                NarrativeLog.LogWarning("AITask_LongMove.RunLongMove: controller 为空，无法执行长距离移动");
                return null;
            }

            var task = new AITask_LongMove();
            task.Setup(controller, goalLocation, acceptanceRadius, stopOnOverlap, acceptPartialPath, bLockAILogic,
                projectGoalOnNavigation, requireNavigableEndLocation);
            task.Activate();
            return task;
        }

        /// <summary>激活任务：求解路径并发起首段移动。对应 UE5 <c>Activate</c>。</summary>
        public void Activate()
        {
            if (_activated) return;
            _activated = true;

            if (Mover == null)
            {
                NarrativeLog.LogError("AITask_LongMove: 无法解析被驱动的角色 Actor，长距离移动失败");
                FailLongMove();
                return;
            }

            // 若有现成的导航网格路径，则无需走 POI 图，直接单段移动
            if (ArsenalStatics.CheckPathExists(Mover.Position, FinalGoal, Mover))
            {
                Path.Clear();
                Path.Add(FinalGoal);
                CurrentTargetIndex = 0;
                IsRunning = true;
                PerformMove(Path[CurrentTargetIndex]);
                return;
            }

            if (!GetPOIPoints(out var startingPOI, out var endingPOI))
            {
                NarrativeLog.LogError("AITask_LongMove: 找不到可用的起止 POI，无法执行长距离移动");
                FailLongMove();
                return;
            }

            if (!CalculatePath(startingPOI, endingPOI, out var poiPath))
            {
                NarrativeLog.LogError($"AITask_LongMove: POI 图 A* 求解失败（{startingPOI.POITag} → {endingPOI.POITag}）");
                FailLongMove();
                return;
            }

            Path.Clear();
            foreach (var poi in poiPath)
            {
                Path.Add(poi.POILocation);
            }
            Path.Add(FinalGoal);

            CurrentTargetIndex = 0;
            IsRunning = true;

            UpdateNavigablePOI();
            PerformMove(Path[CurrentTargetIndex]);
        }

        /// <summary>
        /// 每帧推进任务。需由调用方（通常是 Script.OnUpdate）驱动。
        /// </summary>
        public void TickTask(float deltaTime)
        {
            if (!IsRunning || Mover == null) return;

            // 按频率重评估是否可跳过后续 POI
            _poiReevaluateTimer += deltaTime;
            float interval = UpdatePOIRate > 0f ? 1f / UpdatePOIRate : 1f;
            if (Path.Count > 1 && _poiReevaluateTimer >= interval)
            {
                _poiReevaluateTimer = 0f;
                UpdateNavigablePOI();
            }

            if (CurrentTargetIndex < 0 || CurrentTargetIndex >= Path.Count)
            {
                FinishMove(EPathFollowingResult.Success);
                return;
            }

            Vector3 target = Path[CurrentTargetIndex];

            if (Executor != null)
            {
                if (!Executor.IsMoving(Mover))
                {
                    // 执行器报告已停下：到达则前进，否则视为失败
                    if (Vector3.Distance(Mover.Position, target) <= AcceptanceRadius)
                    {
                        AdvanceWaypoint();
                    }
                    else
                    {
                        FailLongMove();
                    }
                }
                return;
            }

            TickLinearMover(target, deltaTime);
        }

        /// <summary>内置直线推进（无外部执行器时使用）。</summary>
        private void TickLinearMover(Vector3 target, float deltaTime)
        {
            Vector3 current = Mover.Position;
            Vector3 toTarget = target - current;
            float distance = toTarget.Length;

            if (distance <= AcceptanceRadius)
            {
                AdvanceWaypoint();
                return;
            }

            if (distance <= 0.001f) return;

            Vector3 step = toTarget / distance * MoveSpeed * deltaTime;
            Mover.Position = step.Length > distance ? target : current + step;
        }

        private void AdvanceWaypoint()
        {
            CurrentTargetIndex++;
            if (CurrentTargetIndex >= Path.Count)
            {
                FinishMove(EPathFollowingResult.Success);
                return;
            }

            PerformMove(Path[CurrentTargetIndex]);
        }

        /// <summary>使用 A* 在 POI 图上求解从起点 POI 到终点 POI 的高层路径（含首尾）。</summary>
        /// <param name="startingPOI">起点 POI</param>
        /// <param name="endingPOI">终点 POI</param>
        /// <param name="outPath">输出的 POI 路径（起点在前）</param>
        /// <returns>是否求解成功</returns>
        protected virtual bool CalculatePath(POIData startingPOI, POIData endingPOI, out List<POIData> outPath)
        {
            outPath = new List<POIData>();
            if (startingPOI == null || endingPOI == null) return false;

            var lookup = NavigationSubsystem.Instance?.POILookupMap;
            if (lookup == null || lookup.Count == 0) return false;

            // 起终点相同：退化路径
            if (startingPOI.POITag == endingPOI.POITag)
            {
                outPath.Add(startingPOI);
                return true;
            }

            float Heuristic(POIData node) => NavigatorStatics.Distance2D(node.POILocation, endingPOI.POILocation);

            var gScore = new Dictionary<GameplayTag, float> { [startingPOI.POITag] = 0f };
            var fScore = new Dictionary<GameplayTag, float> { [startingPOI.POITag] = Heuristic(startingPOI) };
            var cameFrom = new Dictionary<GameplayTag, POIData>();
            var inOpen = new HashSet<GameplayTag> { startingPOI.POITag };

            // 开放集用最小堆（按 FScore 排序），对应 UE5 的 TArray::HeapPush/HeapPop
            var openSet = new List<POIData> { startingPOI };
            int visited = 0;

            while (openSet.Count > 0)
            {
                if (++visited > MaxPathNodes)
                {
                    NarrativeLog.LogWarning("AITask_LongMove: A* 搜索节点数超限，判定为失败");
                    return false;
                }

                var current = HeapPop(openSet, fScore);
                inOpen.Remove(current.POITag);

                if (current.POITag == endingPOI.POITag)
                {
                    // 沿 CameFrom 回溯并反转
                    var reversed = new List<POIData> { current };
                    while (cameFrom.TryGetValue(current.POITag, out var previous))
                    {
                        current = previous;
                        reversed.Add(current);
                    }
                    for (int i = reversed.Count - 1; i >= 0; i--)
                    {
                        outPath.Add(reversed[i]);
                    }
                    return true;
                }

                var links = current.LinkedPOIs;
                if (links == null) continue;

                foreach (var linkedTag in links)
                {
                    if (!lookup.TryGetValue(linkedTag, out var neighbor) || neighbor == null) continue;

                    float currentG = gScore.TryGetValue(current.POITag, out var cg) ? cg : float.MaxValue;
                    float tentativeG = currentG + NavigatorStatics.Distance2D(neighbor.POILocation, current.POILocation);

                    float neighborG = gScore.TryGetValue(neighbor.POITag, out var ng) ? ng : float.MaxValue;
                    if (tentativeG >= neighborG) continue;

                    cameFrom[neighbor.POITag] = current;
                    gScore[neighbor.POITag] = tentativeG;
                    fScore[neighbor.POITag] = tentativeG + Heuristic(neighbor);

                    if (inOpen.Add(neighbor.POITag))
                    {
                        HeapPush(openSet, neighbor, fScore);
                    }
                    else
                    {
                        // 已在开放集内但分数下降：重新堆化该项
                        HeapPush(openSet, neighbor, fScore);
                    }
                }
            }

            return false;
        }

        /// <summary>取距角色最近与距目标最近的两个 POI。对应 UE5 <c>GetPOIPoints</c>。</summary>
        protected virtual bool GetPOIPoints(out POIData outStartingPOI, out POIData outEndingPOI)
        {
            outStartingPOI = null;
            outEndingPOI = null;

            if (Mover == null) return false;

            var pois = GetPOIPool();
            if (pois == null || pois.Count == 0) return false;

            Vector3 moverPosition = Mover.Position;
            float closestToMover = float.MaxValue;
            float closestToGoal = float.MaxValue;

            foreach (var poi in pois)
            {
                if (poi == null || !poi.POITag.IsValid()) continue;

                float distanceToMover = NavigatorStatics.Distance2D(poi.POILocation, moverPosition);
                if (distanceToMover < closestToMover)
                {
                    outStartingPOI = poi;
                    closestToMover = distanceToMover;
                }

                float distanceToGoal = NavigatorStatics.Distance2D(poi.POILocation, FinalGoal);
                if (distanceToGoal < closestToGoal)
                {
                    outEndingPOI = poi;
                    closestToGoal = distanceToGoal;
                }
            }

            return outStartingPOI != null && outEndingPOI != null;
        }

        /// <summary>POI 数据来源：优先地图边界（UE5 一致），缺失时回退导航子系统。</summary>
        private List<POIData> GetPOIPool()
        {
            var bounds = NavigationSubsystem.Instance?.MapTileBounds;
            if (bounds?.POIs != null && bounds.POIs.Count > 0) return bounds.POIs;

            return NavigationSubsystem.Instance?.GetAllPOIs();
        }

        /// <summary>
        /// 重评估可导航的 POI：若当前位置已能直达更靠后的路径点，则前移目标索引并重发移动。
        /// 对应 UE5 <c>UpdateNavigablePOI</c>。
        /// </summary>
        protected virtual void UpdateNavigablePOI()
        {
            if (Mover == null || Path.Count == 0) return;

            int oldTargetIndex = CurrentTargetIndex;
            Vector3 moverPosition = Mover.Position;

            for (int i = CurrentTargetIndex; i < Path.Count; i++)
            {
                if (!ArsenalStatics.CheckPathExists(moverPosition, Path[i], Mover))
                {
                    break;
                }
                CurrentTargetIndex = i;
            }

            if (oldTargetIndex != CurrentTargetIndex)
            {
                PerformMove(Path[CurrentTargetIndex]);
            }
        }

        /// <summary>发起一次到目标点的移动。对应 UE5 <c>PerformMove</c>。</summary>
        protected virtual void PerformMove(Vector3 goalLocation)
        {
            if (!IsRunning || Mover == null) return;

            if (Executor != null)
            {
                if (!Executor.RequestMove(Mover, goalLocation, AcceptanceRadius))
                {
                    FailLongMove();
                }
                return;
            }

            // 内置执行器无需额外发起点，实际推进在 TickLinearMover 中完成
            _poiReevaluateTimer = 0f;
        }

        /// <summary>标记任务失败并结束。对应 UE5 <c>FailLongMove</c>。</summary>
        public void FailLongMove()
        {
            if (bRequestFailed) return;

            bRequestFailed = true;
            IsRunning = false;
            OnRequestFailed?.Invoke();
        }

        /// <summary>结束任务。对应 UE5 <c>EndTask</c>。</summary>
        public void EndTask()
        {
            IsRunning = false;
        }

        /// <summary>暂停任务。</summary>
        public void Pause()
        {
            if (IsRunning) IsRunning = false;
        }

        /// <summary>恢复任务。</summary>
        public void Resume()
        {
            if (bRequestFailed || Mover == null || Path.Count == 0) return;

            IsRunning = true;
            PerformMove(Path[Mathf.Clamp(CurrentTargetIndex, 0, Path.Count - 1)]);
        }

        private void FinishMove(EPathFollowingResult result)
        {
            IsRunning = false;
            OnMoveFinished?.Invoke(result, Controller);
        }

        // ===================================================================
        // 最小堆（按 FScore 排序）
        // ===================================================================

        private static void HeapPush(List<POIData> heap, POIData item, Dictionary<GameplayTag, float> fScore)
        {
            heap.Add(item);
            int index = heap.Count - 1;

            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (GetFScore(heap[parent], fScore) <= GetFScore(heap[index], fScore)) break;

                (heap[parent], heap[index]) = (heap[index], heap[parent]);
                index = parent;
            }
        }

        private static POIData HeapPop(List<POIData> heap, Dictionary<GameplayTag, float> fScore)
        {
            var root = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);

            int index = 0;
            int count = heap.Count;

            while (true)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                int smallest = index;

                if (left < count && GetFScore(heap[left], fScore) < GetFScore(heap[smallest], fScore)) smallest = left;
                if (right < count && GetFScore(heap[right], fScore) < GetFScore(heap[smallest], fScore)) smallest = right;
                if (smallest == index) break;

                (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
                index = smallest;
            }

            return root;
        }

        private static float GetFScore(POIData node, Dictionary<GameplayTag, float> fScore)
        {
            return fScore.TryGetValue(node.POITag, out var value) ? value : float.MaxValue;
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
