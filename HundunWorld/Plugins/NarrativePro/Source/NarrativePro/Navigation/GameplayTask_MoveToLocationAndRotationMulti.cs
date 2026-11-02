using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 多角色"移动并转向"聚合任务。适配 UE5 UGameplayTask_MoveToLocationAndRotationMulti。
    /// <para>
    /// 为每个角色创建一个 <see cref="MoveToLocationAndRotationTask"/> 子任务，全部成功后广播
    /// <see cref="TaskCompleted"/>；任一子任务失败则中止所有子任务并广播 <see cref="TaskFailed"/>。
    /// UE5 由 GameplayTask 系统自动驱动 Tick，Flax 需要宿主（通常是 Script.OnUpdate）调用
    /// <see cref="TickMulti"/>。
    /// </para>
    /// </summary>
    public class MoveToLocationAndRotationMultiTask
    {
        /// <summary>参与移动的角色（对应 UE5 Characters）。</summary>
        public List<Actor> Characters { get; private set; } = new List<Actor>();

        /// <summary>每个角色的目标变换（对应 UE5 TargetTransforms，与 Characters 一一对应）。</summary>
        public List<Transform> TargetTransforms { get; private set; } = new List<Transform>();

        /// <summary>旋转插值速度。</summary>
        public float InterpSpeed { get; private set; }

        /// <summary>到达判定模式。</summary>
        public EGoalReachMode GoalReachMode { get; private set; }

        /// <summary>移动完成后是否结束各子任务。</summary>
        public bool bEndWhenFinishedMove { get; private set; }

        /// <summary>已完成的子任务数。</summary>
        public int TasksCompleted { get; private set; }

        /// <summary>聚合任务是否仍在运行。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>子任务列表（对应 UE5 MoveToTasks）。</summary>
        public IReadOnlyList<MoveToLocationAndRotationTask> MoveToTasks => _moveToTasks;

        /// <summary>全部子任务成功完成。</summary>
        public event Action<EPathFollowingResult> TaskCompleted;

        /// <summary>任一子任务失败（或参数非法），已中止其余子任务。</summary>
        public event Action<EPathFollowingResult> TaskFailed;

        private readonly List<MoveToLocationAndRotationTask> _moveToTasks = new List<MoveToLocationAndRotationTask>();
        private bool _bSettled;

        /// <summary>
        /// 静态工厂。对应 UE5 MoveToLocationAndRotationMulti：角色列表为空、
        /// 数量与目标变换不匹配或存在空角色时返回 null（并记录与 UE5 一致的错误）。
        /// </summary>
        public static MoveToLocationAndRotationMultiTask MoveToLocationAndRotationMulti(
            List<Actor> characters, List<Transform> targetTransforms, float interpSpeed,
            EGoalReachMode goalReachMode = EGoalReachMode.ExactLocation, bool bEndWhenFinishedMove = true)
        {
            if (characters == null || characters.Count == 0)
            {
                Core.NarrativeLog.LogError("MoveToLocationAndRotationMulti: 没有有效的 Characters，无法创建任务");
                return null;
            }

            if (targetTransforms == null || characters.Count != targetTransforms.Count)
            {
                Core.NarrativeLog.LogError($"MoveToLocationAndRotationMulti: TargetTransforms 数量与 Characters 不匹配！Characters: {characters.Count} | TargetTransforms: {targetTransforms?.Count ?? 0}");
                return null;
            }

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i] == null)
                {
                    Core.NarrativeLog.LogError($"MoveToLocationAndRotationMulti: 索引 {i} 处的角色无效");
                    return null;
                }
            }

            return new MoveToLocationAndRotationMultiTask
            {
                InterpSpeed = interpSpeed,
                GoalReachMode = goalReachMode,
                bEndWhenFinishedMove = bEndWhenFinishedMove
            }.Initialize(characters, targetTransforms);
        }

        private MoveToLocationAndRotationMultiTask Initialize(List<Actor> characters, List<Transform> transforms)
        {
            Characters.AddRange(characters);
            TargetTransforms.AddRange(transforms);
            return this;
        }

        /// <summary>
        /// 激活聚合任务：为每个角色创建并激活子任务。对应 UE5 Activate。
        /// </summary>
        public void Activate()
        {
            if (_bSettled) return;

            for (int i = 0; i < Characters.Count; i++)
            {
                Actor character = Characters[i];
                Transform target = TargetTransforms[i];

                var moveToTask = MoveToLocationAndRotationTask.MoveToLocationAndRotation(
                    character,
                    target.Translation,
                    target.Orientation,
                    InterpSpeed,
                    GoalReachMode,
                    bEndWhenFinishedMove);

                if (moveToTask == null)
                {
                    Core.NarrativeLog.LogError($"MoveToLocationAndRotationMulti: 无法为 {character?.Name} 创建子任务");
                    FailMoveToTask(EPathFollowingResult.Invalid);
                    return;
                }

                moveToTask.OnCompleted += HandleMoveTaskCompleted;
                moveToTask.OnFailed += HandleMoveTaskFailed;
                _moveToTasks.Add(moveToTask);
                moveToTask.Activate();
            }

            IsRunning = true;

            // 零角色已在工厂里被拒绝；这里防止空集合下永远等不到回调
            if (_moveToTasks.Count == 0)
            {
                FailMoveToTask(EPathFollowingResult.Invalid);
            }
        }

        /// <summary>由宿主每帧驱动所有子任务。</summary>
        public void TickMulti(float deltaTime)
        {
            if (!IsRunning || _bSettled) return;

            for (int i = 0; i < _moveToTasks.Count; i++)
            {
                _moveToTasks[i].TickTask(deltaTime);
            }
        }

        private void HandleMoveTaskCompleted(EPathFollowingResult result)
        {
            MoveTaskCompleted(result);
        }

        private void HandleMoveTaskFailed(EPathFollowingResult result)
        {
            MoveTaskFailed(result);
        }

        /// <summary>子任务完成。对应 UE5 MoveTaskCompleted：只有全部成功才广播完成。</summary>
        public void MoveTaskCompleted(EPathFollowingResult result)
        {
            if (_bSettled) return;

            if (result == EPathFollowingResult.Success)
            {
                TasksCompleted++;

                if (TasksCompleted >= TargetTransforms.Count)
                {
                    _bSettled = true;
                    IsRunning = false;
                    UnsubscribeAll();
                    TaskCompleted?.Invoke(result);
                }
            }
            else
            {
                FailMoveToTask(result);
            }
        }

        /// <summary>中止所有子任务并广播失败。对应 UE5 FailMoveToTask。</summary>
        public void FailMoveToTask(EPathFollowingResult result)
        {
            if (_bSettled) return;
            _bSettled = true;
            IsRunning = false;

            UnsubscribeAll();

            foreach (var task in _moveToTasks)
            {
                task.Destroy();
            }

            TaskFailed?.Invoke(result);
        }

        /// <summary>子任务失败。对应 UE5 MoveTaskFailed：Success 不算失败。</summary>
        public void MoveTaskFailed(EPathFollowingResult result)
        {
            if (result != EPathFollowingResult.Success)
            {
                FailMoveToTask(result);
            }
        }

        private void UnsubscribeAll()
        {
            foreach (var task in _moveToTasks)
            {
                task.OnCompleted -= HandleMoveTaskCompleted;
                task.OnFailed -= HandleMoveTaskFailed;
            }
        }

        /// <summary>销毁聚合任务与所有子任务。</summary>
        public void Destroy()
        {
            UnsubscribeAll();
            foreach (var task in _moveToTasks)
            {
                task.Destroy();
            }
            _moveToTasks.Clear();
            IsRunning = false;
            _bSettled = true;
        }
    }
}
