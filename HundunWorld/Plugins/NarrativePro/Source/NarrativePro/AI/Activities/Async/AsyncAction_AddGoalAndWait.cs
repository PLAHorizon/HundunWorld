using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NarrativePro.Core;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// 向 NPC 活动组件添加目标并等待其完成/移除的异步动作。
    /// 对应 UE5 <c>UAsyncAction_AddGoalAndWait</c>（AsyncAction_AddGoalAndWait.h/.cpp）。
    /// <para>
    /// Flax 无 <c>UBlueprintAsyncActionBase</c> 等价物，故实现为普通 C# 对象：
    /// <list type="bullet">
    /// <item><see cref="GoalSucceeded"/> / <see cref="GoalRemoved"/> 事件对应 UE5 的
    /// BlueprintAssignable 委托，仅在目标真正成功或被移除时触发一次（与 UE5 OnGoalSucceeded/OnGoalRemoved 一致）。</item>
    /// <item><see cref="WaitAsync"/> 提供 C# <see cref="Task{TResult}"/> await 路径。</item>
    /// <item><see cref="EndTask"/> 解绑事件（对应 UE5 SetReadyToDestroy 中
    /// <c>CachedNPCGoal-&gt;OnGoalSucceeded.RemoveAll(this)</c>），触发后自动调用，防止悬挂订阅。</item>
    /// <item><paramref name="bPersistant"/> 时注册到静态保活表（对应 UE5 RegisterWithGameInstance），
    /// 允许在调用方脚本销毁后继续存活，直到 EndTask。</item>
    /// </list>
    /// </para>
    /// </summary>
    public class AsyncAction_AddGoalAndWait
    {
        /// <summary>UE5：persistent 动作由 GameInstance 保活；此处用静态表等价实现。</summary>
        private static readonly List<AsyncAction_AddGoalAndWait> PersistentActions = new List<AsyncAction_AddGoalAndWait>();

        /// <summary>缓存的已添加目标。对应 UE5 <c>CachedNPCGoal</c>。</summary>
        public NPCGoalItem CachedNPCGoal;

        /// <summary>目标完成时广播（携带当时正在运行的活动）。对应 UE5 <c>GoalSucceeded</c>。</summary>
        public event Action<NPCActivity> GoalSucceeded;

        /// <summary>目标被移除（未完成）时广播；若目标已成功则不会触发。对应 UE5 <c>GoalRemoved</c>。</summary>
        public event Action<NPCActivity> GoalRemoved;

        private readonly TaskCompletionSource<NPCActivity> _completion =
            new TaskCompletionSource<NPCActivity>(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool bDone = false;

        /// <summary>等待目标完成/移除的 awaitable。对应 UE5 蓝图 ExposedAsyncProxy 的等待语义。</summary>
        public Task<NPCActivity> WaitAsync() => _completion.Task;

        /// <summary>动作是否已结束（成功、被移除或 EndTask）。</summary>
        public bool IsDone => bDone;

        /// <summary>
        /// 静态入口。对应 UE5 <c>AddGoalAndWait</c> UFUNCTION：
        /// 组件或目标为空返回 null；AddGoal 失败立即 EndTask；成功则订阅目标事件。
        /// </summary>
        /// <param name="npcActivityComponent">NPC 活动组件</param>
        /// <param name="newGoal">要添加的目标</param>
        /// <param name="bTriggerReselect">添加后是否请求组件重选行为</param>
        /// <param name="bPersistant">true 时动作保活到手动调用 EndTask（可用于循环）</param>
        /// <param name="goal">输出：组件实际创建/合并后的目标句柄</param>
        public static AsyncAction_AddGoalAndWait AddGoalAndWait(
            NPCActivityComponent npcActivityComponent,
            NPCGoalItem newGoal,
            bool bTriggerReselect,
            bool bPersistant,
            out NPCGoalItem goal)
        {
            goal = null;
            if (npcActivityComponent == null || newGoal == null)
            {
                return null;
            }

            var action = new AsyncAction_AddGoalAndWait();
            if (bPersistant)
            {
                lock (PersistentActions)
                {
                    PersistentActions.Add(action);
                }
            }

            goal = npcActivityComponent.AddGoal(newGoal, bTriggerReselect);
            if (goal != null)
            {
                action.CachedNPCGoal = goal;
                goal.OnGoalSucceeded += action.HandleGoalSucceeded;
                goal.OnGoalRemoved += action.HandleGoalRemoved;
            }
            else
            {
                // UE5：no goal, end task
                action.EndTask();
            }

            return action;
        }

        private void HandleGoalSucceeded(NPCActivity activity, NPCGoalItem goal)
        {
            if (bDone) return;
            GoalSucceeded?.Invoke(activity);
            Complete(activity);
        }

        private void HandleGoalRemoved(NPCActivity activity, NPCGoalItem goal)
        {
            if (bDone) return;
            GoalRemoved?.Invoke(activity);
            Complete(activity);
        }

        private void Complete(NPCActivity activity)
        {
            _completion.TrySetResult(activity);
            EndTask();
        }

        /// <summary>
        /// 结束动作：解绑目标事件并解除保活。对应 UE5 <c>EndTask → SetReadyToDestroy</c>。
        /// </summary>
        public void EndTask()
        {
            if (bDone) return;
            bDone = true;

            if (CachedNPCGoal != null)
            {
                CachedNPCGoal.OnGoalSucceeded -= HandleGoalSucceeded;
                CachedNPCGoal.OnGoalRemoved -= HandleGoalRemoved;
            }
            _completion.TrySetResult(null);

            lock (PersistentActions)
            {
                PersistentActions.Remove(this);
            }
        }
    }
}
