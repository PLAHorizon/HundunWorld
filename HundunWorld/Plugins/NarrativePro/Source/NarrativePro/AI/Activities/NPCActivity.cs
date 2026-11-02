using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// 已保存的 NPC 活动。对应 UE5 FSavedNPCActivity。
    /// <see cref="Data"/> 承载 UE5 <c>FMemoryWriter + ArIsSaveGame</c> 归档出的字节。
    /// </summary>
    [Serializable]
    public class SavedNPCActivity
    {
        public string ClassPath = "";

        /// <summary>活动自身 SaveGame 状态的字节快照。</summary>
        public byte[] Data = Array.Empty<byte>();
    }

    /// <summary>
    /// NPC 活动类。对应 UE5 UNPCActivity。
    /// 包装行为树，操作目标项。活动组件通过 PerformActivitySelection 选择评分最高的活动执行。
    /// <para>
    /// Flax 无 <c>UBehaviorTree</c> 资产：行为树以「路径 + 黑板」的形式落地，
    /// 由 <see cref="NarrativeNPCController.RunBehaviorTree"/> 激活、<see cref="TickActivity"/> 推进，
    /// 活动子类通过覆盖 <see cref="TickBehaviourTree"/> 提供节点级逻辑。
    /// </para>
    /// </summary>
    [Serializable]
    public abstract class NPCActivity : NarrativeActivityBase
    {
        /// <summary>活动是否可被打断</summary>
        public bool bIsInterruptable = true;

        /// <summary>最近一次评分（缓存）</summary>
        [JsonIgnore]
        public float LastScore = 0f;

        /// <summary>
        /// 行为树标识（资产路径或逻辑名）。对应 UE5 <c>UPROPERTY BehaviourTree</c>。
        /// </summary>
        public string BehaviourTreePath = "";

        /// <summary>此活动支持的目标类型路径（可为空，如待机活动无需目标）</summary>
        public string SupportedGoalType = "";

        /// <summary>是否将此活动保存到磁盘（由 AddActivity 设置）</summary>
        [JsonIgnore]
        public bool bSaveActivity = false;

        /// <summary>最近一次激活时间（游戏运行时间秒）。-1 表示从未激活，对应 UE5 初值。</summary>
        [JsonIgnore]
        public float LastActivateTime = -1f;

        /// <summary>拥有此活动的 NPC 控制器 ID</summary>
        [JsonIgnore]
        public string OwnerControllerId = "";

        /// <summary>拥有此活动的 NPC 控制器。对应 UE5 <c>OwnerController</c>。</summary>
        [JsonIgnore]
        public NarrativeNPCController OwnerController;

        /// <summary>拥有此活动的活动组件</summary>
        [JsonIgnore]
        public NPCActivityComponent OwnerActivityComponent;

        /// <summary>此活动正在操作的目标（如攻击活动的攻击目标项）</summary>
        [JsonIgnore]
        public NPCGoalItem ActivityGoal;

        /// <summary>活动目标成功完成时触发</summary>
        public event OnGoalSignature OnActivityGoalSucceeded;

        /// <summary>返回活动是否可被打断</summary>
        public bool IsInterruptable()
        {
            return bIsInterruptable;
        }

        /// <summary>
        /// 返回活动是否处于活跃状态。对应 UE5 <c>IsActivityActive</c>（NPCActivity.cpp:15-28）。
        /// </summary>
        /// <param name="outActiveTime">自激活起经过的秒数；从未激活时为极大值</param>
        public virtual bool IsActivityActive(out float outActiveTime)
        {
            outActiveTime = float.MaxValue;
            if (LastActivateTime > 0f)
            {
                outActiveTime = NPCGoalItem.NarrativeRunTime - LastActivateTime;
            }

            return OwnerActivityComponent != null && OwnerActivityComponent.GetCurrentActivityGoal() == ActivityGoal;
        }

        /// <summary>设置拥有者。对应 UE5 <c>SetOwner</c>（:30-37），会复位评分与激活时间。</summary>
        public void SetOwner(NarrativeNPCController ownerController, NPCActivityComponent ownerComp)
        {
            OwnerController = ownerController;
            OwnerActivityComponent = ownerComp;
            if (ownerController != null)
            {
                OwnerControllerId = ownerController.ID.ToString();
            }
            LastScore = -1f;
            LastActivateTime = -1f;
        }

        /// <summary>
        /// 设置拥有者（按控制器 ID）。保留原有签名，控制器实例从活动组件解析。
        /// </summary>
        public void SetOwner(string ownerControllerId, NPCActivityComponent ownerComp)
        {
            OwnerControllerId = ownerControllerId;
            OwnerActivityComponent = ownerComp;
            OwnerController = ownerComp != null ? ownerComp.OwnerController : null;
            LastScore = -1f;
            LastActivateTime = -1f;
        }

        /// <summary>
        /// 黑板就绪：把活动与目标写入黑板键，供行为节点/移动层读取。
        /// 对应 UE5 <c>UBlackboardComponent::SetValueAsObject/Vector</c> 的活动侧用法。
        /// </summary>
        protected virtual void PopulateBlackboard(NarrativeNPCBlackboard blackboard)
        {
            if (blackboard == null) return;

            blackboard.SetValue(BlackboardKeyActivity, DescribeActivity());
            if (!string.IsNullOrEmpty(BehaviourTreePath))
            {
                blackboard.SetValue(BlackboardKeyBehaviourTree, BehaviourTreePath);
            }
            if (ActivityGoal != null)
            {
                blackboard.SetValue(BlackboardKeyGoal, ActivityGoal);
                blackboard.SetValue(BlackboardKeyGoalLocation, ActivityGoal.GoalLocation);
                if (ActivityGoal.GoalActor != null)
                {
                    blackboard.SetValue(BlackboardKeyGoalActor, ActivityGoal.GoalActor);
                }
            }
        }

        /// <summary>黑板键：当前活动名。</summary>
        public const string BlackboardKeyActivity = "Narrative.Activity";
        /// <summary>黑板键：当前行为树。</summary>
        public const string BlackboardKeyBehaviourTree = "Narrative.BehaviourTree";
        /// <summary>黑板键：当前目标项。</summary>
        public const string BlackboardKeyGoal = "Narrative.Goal";
        /// <summary>黑板键：目标位置。</summary>
        public const string BlackboardKeyGoalLocation = "Narrative.GoalLocation";
        /// <summary>黑板键：目标 Actor。</summary>
        public const string BlackboardKeyGoalActor = "Narrative.GoalActor";

        /// <summary>
        /// 设置黑板。对应 UE5 <c>SetupBlackboard_Implementation(UBlackboardComponent*)</c>（:39-42）。
        /// 子类覆盖以写入活动所需键；返回 false 会让活动启动失败并停树。
        /// </summary>
        protected virtual bool SetupBlackboard(NarrativeNPCBlackboard blackboard)
        {
            return true;
        }

        /// <summary>
        /// 评分活动，选择最佳目标。
        /// 对应 UE5 <c>ScoreActivity_Implementation</c>（:44-73）：
        /// 容器为空返回 0；负分目标进失效列表；零分目标忽略；否则取最高分。
        /// </summary>
        /// <param name="goalContainer">目标容器</param>
        /// <param name="outBestGoal">最佳目标（输出）</param>
        /// <param name="outInvalidGoals">失效目标列表（输出，用于清理）</param>
        /// <returns>活动评分</returns>
        public virtual float ScoreActivity(NPCGoalContainer goalContainer, out NPCGoalItem outBestGoal, List<NPCGoalItem> outInvalidGoals)
        {
            outBestGoal = null;
            outInvalidGoals = outInvalidGoals ?? new List<NPCGoalItem>();

            // 对应 UE5「If we have no goals, return <0, this activity shouldnt be doable」→ 返回 0
            if (goalContainer == null || goalContainer.IsEmpty())
            {
                return 0f;
            }

            float bestScore = 0f;
            NPCGoalItem bestGoalItem = null;

            foreach (var goal in goalContainer.Goals)
            {
                if (goal == null) continue;

                float goalScore = ScoreGoalItem(goal);

                // 负分目标应被移除，零分目标被忽略
                if (goalScore < 0f)
                {
                    outInvalidGoals.Add(goal);
                }
                else if (!IsNearlyZero(goalScore) && goalScore > bestScore)
                {
                    bestGoalItem = goal;
                    bestScore = goalScore;
                }
            }

            outBestGoal = bestGoalItem;
            if (bestGoalItem != null)
            {
                bestGoalItem.CurrentScore = bestScore;
            }
            return bestScore;
        }

        /// <summary>
        /// 评分目标项。对应 UE5 <c>ScoreGoalItem_Implementation</c>（:75-110）：
        /// ① 生命周期到了返回 -1（该目标要被清理）；
        /// ② 拥有 BlockTags 或缺少 RequireTags 时返回 0（不参与但保留）；
        /// ③ 否则返回目标自身评分。
        /// </summary>
        public virtual float ScoreGoalItem(NPCGoalItem goal)
        {
            if (goal == null) return float.NegativeInfinity;

            // ① 生命周期
            if (goal.GoalLifetime > 0f)
            {
                if (goal.GetGoalAgeSeconds() >= goal.GoalLifetime)
                {
                    return -1f;
                }
            }

            // ② 标签检查：用 0 分而不是 -1，避免误删目标
            if (OwnerController != null)
            {
                if (OwnerController.HasAnyMatchingGameplayTags(goal.BlockTags))
                {
                    return 0f;
                }

                if (!OwnerController.HasAllMatchingGameplayTags(goal.RequireTags))
                {
                    return 0f;
                }
            }

            return goal.GetGoalScore();
        }

        /// <summary>
        /// 启动活动。对应 UE5 <c>RunActivity</c>（NPCActivity.cpp:112-144）：
        /// 跑行为树 → 配黑板 → 给控制器加本活动与目标的松散标签 → 记录激活时间。
        /// 黑板配置失败时停树并返回 false。
        /// </summary>
        public override bool RunActivity()
        {
            if (OwnerController == null)
            {
                return false;
            }

            // 没有配置行为树的活动（如纯脚本活动）也允许启动，对应 UE5 只在有 BT 时才走 RunBehaviorTree
            if (!string.IsNullOrEmpty(BehaviourTreePath))
            {
                if (!OwnerController.RunBehaviorTree(BehaviourTreePath))
                {
                    return false;
                }
            }

            var blackboard = OwnerController.GetBlackboardComponent();
            PopulateBlackboard(blackboard);
            if (!SetupBlackboard(blackboard))
            {
                NarrativeLog.LogWarning($"活动 {DescribeActivity()} 启动失败：SetupBlackboard() 返回 false，已停止行为树。");
                OwnerController.StopBehaviorTree();
                return false;
            }

            // 活动与目标各自的 OwnedTags 在活动期间挂到控制器上
            OwnerController.AddLooseGameplayTags(OwnedTags, 1);
            if (ActivityGoal != null)
            {
                OwnerController.AddLooseGameplayTags(ActivityGoal.OwnedTags, 1);
            }

            LastActivateTime = NPCGoalItem.NarrativeRunTime;
            OnActivityStarted?.Invoke(this, ActivityGoal);
            return true;
        }

        /// <summary>
        /// 结束活动。对应 UE5 <c>EndActivity</c>（:146-162）：归还活动与目标的松散标签。
        /// </summary>
        public override bool EndActivity()
        {
            if (OwnerController != null)
            {
                OwnerController.RemoveLooseGameplayTags(OwnedTags, 1);
                if (ActivityGoal != null)
                {
                    OwnerController.RemoveLooseGameplayTags(ActivityGoal.OwnedTags, 1);
                }
            }

            OnActivityEnded?.Invoke(this, ActivityGoal);
            return true;
        }

        /// <summary>活动开始事件（对应 UE5 K2_RunActivity 蓝图钩子的托管等价物）。</summary>
        public event OnGoalSignature OnActivityStarted;

        /// <summary>活动结束事件（对应 UE5 K2_EndActivity）。</summary>
        public event OnGoalSignature OnActivityEnded;

        /// <summary>停止行为树。对应 UE5 <c>StopBehaviorTree</c>（:164-167）。</summary>
        public virtual void StopBehaviorTree()
        {
            OwnerController?.StopBehaviorTree();
        }

        /// <summary>
        /// 活动每帧更新（由活动组件调用）。对应 UE5 中行为树自身 tick；
        /// 这里先推进子类挂载的行为节点集合，再派发 <see cref="OnTick"/>。
        /// </summary>
        public virtual void TickActivity(float deltaTime)
        {
            if (OwnerActivityComponent != null && OwnerActivityComponent.GetCurrentActivity() != this)
            {
                // 已被换掉的活动不再 tick，防止旧活动继续写黑板
                return;
            }

            TickBehaviourTree(deltaTime);
            OnTick?.Invoke(this, deltaTime);
        }

        /// <summary>
        /// 行为树推进点。对应 UE5 由 BehaviorTreeComponent 执行的节点逻辑；
        /// 子类可注册 <see cref="BehaviourNodes"/> 或直接覆盖本方法。
        /// </summary>
        protected virtual void TickBehaviourTree(float deltaTime)
        {
            if (_nodes == null || _nodes.Count == 0) return;
            foreach (var node in _nodes)
            {
                node?.Invoke(this, deltaTime);
            }
        }

        private readonly List<Action<NPCActivity, float>> _nodes = new List<Action<NPCActivity, float>>();

        /// <summary>注册一个行为节点回调（每帧随活动 tick）。</summary>
        public void AddBehaviourNode(Action<NPCActivity, float> node)
        {
            if (node == null) return;
            _nodes.Add(node);
        }

        /// <summary>移除全部行为节点。对应 UE5 停止行为树时清执行状态。</summary>
        public void ClearBehaviourNodes()
        {
            _nodes.Clear();
        }

        /// <summary>活动 tick 事件。</summary>
        public event Action<NPCActivity, float> OnTick;

        /// <summary>
        /// 通知活动已成功完成（如攻击目标已死亡、到达目的地）。
        /// 若目标的 bRemoveOnSucceeded 为 true，则移除目标。对应 UE5 :169-182。
        /// </summary>
        public virtual void NotifySucceeded()
        {
            OnActivityGoalSucceeded?.Invoke(this, ActivityGoal);

            if (ActivityGoal != null)
            {
                ActivityGoal.FireOnGoalSucceeded(this);

                if (ActivityGoal.bRemoveOnSucceeded)
                {
                    RemoveActivityGoal();
                }
            }
        }

        /// <summary>移除当前活动的目标。对应 UE5 <c>RemoveActivityGoal</c>（:184-192）。</summary>
        public virtual void RemoveActivityGoal()
        {
            if (OwnerActivityComponent != null && ActivityGoal != null)
            {
                var goalToRemove = ActivityGoal;
                ActivityGoal = null;
                OwnerActivityComponent.RemoveGoal(goalToRemove);
            }
        }

        /// <summary>
        /// 取本活动的 SaveGame 状态字节。对应 UE5 <c>Serialize(ArIsSaveGame)</c>。
        /// 运行时引用（控制器/目标/事件）通过 <see cref="JsonIgnoreAttribute"/> 排除。
        /// </summary>
        public virtual byte[] GetSaveStateBytes()
        {
            return NarrativeActivitySerializer.ToBytes(this);
        }

        /// <summary>从 SaveGame 状态字节恢复。对应 UE5 读档时的 <c>Serialize</c> 回灌。</summary>
        public virtual void ApplySaveStateBytes(byte[] data)
        {
            NarrativeActivitySerializer.OverwriteFrom(data, this);
        }

        /// <summary>UE5 <c>FMath::IsNearlyZero</c> 等价。</summary>
        internal static bool IsNearlyZero(float value, float tolerance = 1e-4f)
        {
            return Math.Abs(value) < tolerance;
        }
    }

    /// <summary>
    /// 待机活动。对应 UE5 配置资产里作为默认活动挂上的「Idle」<c>UNPCActivity</c> 子类：
    /// 不依赖目标、始终可运行，评分固定 0.5——高于「无事可做」（0 分，UE5 :295 要求 Score&gt;0），
    /// 低于任何带目标的活动，保证 NPC 的活动列表在配置加载后始终有兜底项。
    /// </summary>
    [Serializable]
    public class Activity_Idle : NPCActivity
    {
        public Activity_Idle()
        {
            ActivityName = "Idle";
            bIsInterruptable = true;
        }

        public override float ScoreActivity(NPCGoalContainer goalContainer, out NPCGoalItem outBestGoal, List<NPCGoalItem> outInvalidGoals)
        {
            // 待机活动不消费目标，也不判定目标失效
            outBestGoal = null;
            if (outInvalidGoals != null) outInvalidGoals.Clear();
            return 0.5f;
        }
    }
}
