using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.AI
{
    /// <summary>
    /// 行为树节点状态
    /// </summary>
    public enum BTStatus
    {
        /// <summary>成功完成</summary>
        Success,
        /// <summary>执行失败</summary>
        Failure,
        /// <summary>正在执行中</summary>
        Running,
        /// <summary>尚未执行</summary>
        Idle
    }

    /// <summary>
    /// 行为树节点基类
    /// </summary>
    public abstract class BTNode
    {
        public string Name { get; set; } = "";
        public BTStatus Status { get; protected set; } = BTStatus.Idle;

        /// <summary>执行节点（每帧调用）</summary>
        public abstract BTStatus Tick(BTContext context, float deltaTime);

        /// <summary>重置节点状态</summary>
        public virtual void Reset()
        {
            Status = BTStatus.Idle;
        }
    }

    /// <summary>
    /// 行为树上下文（黑板数据）
    /// </summary>
    public class BTContext
    {
        /// <summary>NPC 自身 Actor</summary>
        public FlaxEngine.Actor Self { get; set; }

        /// <summary>当前目标 Actor</summary>
        public FlaxEngine.Actor Target { get; set; }

        /// <summary>NPC 位置</summary>
        public Vector3 SelfPosition { get; set; }

        /// <summary>目标位置</summary>
        public Vector3 TargetPosition { get; set; }

        /// <summary>出生点位置（用于巡逻/回归）</summary>
        public Vector3 HomePosition { get; set; }

        /// <summary>与目标的距离</summary>
        public float DistanceToTarget { get; set; }

        /// <summary>NPC 当前生命值比例（0-1）</summary>
        public float HealthPercent { get; set; } = 1f;

        /// <summary>是否有目标</summary>
        public bool HasTarget => Target != null;

        /// <summary>是否处于战斗状态</summary>
        public bool IsInCombat { get; set; }

        /// <summary>仇恨值表</summary>
        public Dictionary<FlaxEngine.Actor, float> ThreatTable { get; } = new Dictionary<FlaxEngine.Actor, float>();

        /// <summary>自定义黑板数据</summary>
        public Dictionary<string, object> Blackboard { get; } = new Dictionary<string, object>();

        /// <summary>获取黑板值</summary>
        public T Get<T>(string key, T defaultValue = default)
        {
            return Blackboard.TryGetValue(key, out var val) && val is T typed ? typed : defaultValue;
        }

        /// <summary>设置黑板值</summary>
        public void Set<T>(string key, T value)
        {
            Blackboard[key] = value;
        }

        /// <summary>获取仇恨最高的目标</summary>
        public FlaxEngine.Actor GetTopThreatTarget()
        {
            if (ThreatTable.Count == 0) return null;
            return ThreatTable.OrderByDescending(kv => kv.Value).First().Key;
        }

        /// <summary>增加仇恨值</summary>
        public void AddThreat(FlaxEngine.Actor actor, float amount)
        {
            if (actor == null) return;
            ThreatTable[actor] = ThreatTable.GetValueOrDefault(actor, 0f) + amount;
        }
    }

    // ===== 组合节点 =====

    /// <summary>
    /// 顺序节点：依次执行子节点，全部成功则成功，任一失败则失败
    /// </summary>
    public class BTSequence : BTNode
    {
        public List<BTNode> Children { get; } = new List<BTNode>();
        private int _currentIndex = 0;

        public BTSequence(params BTNode[] children)
        {
            Children.AddRange(children);
        }

        public override BTStatus Tick(BTContext context, float deltaTime)
        {
            while (_currentIndex < Children.Count)
            {
                var status = Children[_currentIndex].Tick(context, deltaTime);
                if (status == BTStatus.Running)
                {
                    Status = BTStatus.Running;
                    return Status;
                }
                if (status == BTStatus.Failure)
                {
                    Status = BTStatus.Failure;
                    Reset();
                    return Status;
                }
                _currentIndex++;
            }

            Status = BTStatus.Success;
            Reset();
            return Status;
        }

        public override void Reset()
        {
            base.Reset();
            _currentIndex = 0;
            foreach (var child in Children) child.Reset();
        }
    }

    /// <summary>
    /// 选择节点：依次尝试子节点，任一成功则成功，全部失败则失败
    /// </summary>
    public class BTSelector : BTNode
    {
        public List<BTNode> Children { get; } = new List<BTNode>();
        private int _currentIndex = 0;

        public BTSelector(params BTNode[] children)
        {
            Children.AddRange(children);
        }

        public override BTStatus Tick(BTContext context, float deltaTime)
        {
            while (_currentIndex < Children.Count)
            {
                var status = Children[_currentIndex].Tick(context, deltaTime);
                if (status == BTStatus.Running)
                {
                    Status = BTStatus.Running;
                    return Status;
                }
                if (status == BTStatus.Success)
                {
                    Status = BTStatus.Success;
                    Reset();
                    return Status;
                }
                _currentIndex++;
            }

            Status = BTStatus.Failure;
            Reset();
            return Status;
        }

        public override void Reset()
        {
            base.Reset();
            _currentIndex = 0;
            foreach (var child in Children) child.Reset();
        }
    }

    /// <summary>
    /// 并行节点：同时执行所有子节点
    /// </summary>
    public class BTParallel : BTNode
    {
        public List<BTNode> Children { get; } = new List<BTNode>();
        public bool RequireAllSuccess = true;

        public BTParallel(params BTNode[] children)
        {
            Children.AddRange(children);
        }

        public override BTStatus Tick(BTContext context, float deltaTime)
        {
            int successCount = 0;
            int failCount = 0;
            bool anyRunning = false;

            foreach (var child in Children)
            {
                var status = child.Tick(context, deltaTime);
                if (status == BTStatus.Success) successCount++;
                else if (status == BTStatus.Failure) failCount++;
                else anyRunning = true;
            }

            if (RequireAllSuccess)
            {
                if (failCount > 0) { Status = BTStatus.Failure; Reset(); }
                else if (successCount == Children.Count) { Status = BTStatus.Success; Reset(); }
                else Status = BTStatus.Running;
            }
            else
            {
                if (successCount > 0) { Status = BTStatus.Success; Reset(); }
                else if (failCount == Children.Count) { Status = BTStatus.Failure; Reset(); }
                else Status = BTStatus.Running;
            }
            return Status;
        }

        public override void Reset()
        {
            base.Reset();
            foreach (var child in Children) child.Reset();
        }
    }

    // ===== 装饰节点 =====

    /// <summary>
    /// 条件节点：满足条件时执行子节点
    /// </summary>
    public class BTCondition : BTNode
    {
        public Func<BTContext, bool> Predicate { get; set; }
        public BTNode Child { get; set; }

        public BTCondition(Func<BTContext, bool> predicate, BTNode child)
        {
            Predicate = predicate;
            Child = child;
        }

        public override BTStatus Tick(BTContext context, float deltaTime)
        {
            if (Predicate == null || !Predicate(context))
            {
                Status = BTStatus.Failure;
                return Status;
            }
            Status = Child?.Tick(context, deltaTime) ?? BTStatus.Failure;
            return Status;
        }

        public override void Reset()
        {
            base.Reset();
            Child?.Reset();
        }
    }

    /// <summary>
    /// 重复节点：重复执行子节点N次（-1=无限）
    /// </summary>
    public class BTRepeater : BTNode
    {
        public BTNode Child { get; set; }
        public int RepeatCount = -1;
        private int _currentCount = 0;

        public BTRepeater(BTNode child, int count = -1)
        {
            Child = child;
            RepeatCount = count;
        }

        public override BTStatus Tick(BTContext context, float deltaTime)
        {
            if (RepeatCount >= 0 && _currentCount >= RepeatCount)
            {
                Status = BTStatus.Success;
                return Status;
            }

            var status = Child?.Tick(context, deltaTime) ?? BTStatus.Failure;
            if (status == BTStatus.Running)
            {
                Status = BTStatus.Running;
            }
            else
            {
                _currentCount++;
                Child?.Reset();
                Status = BTStatus.Running; // 继续重复
            }
            return Status;
        }

        public override void Reset()
        {
            base.Reset();
            _currentCount = 0;
            Child?.Reset();
        }
    }

    /// <summary>
    /// 冷却节点：执行后进入冷却期
    /// </summary>
    public class BTCooldown : BTNode
    {
        public BTNode Child { get; set; }
        public float CooldownDuration = 5f;
        private float _lastExecuteTime = -999f;

        public BTCooldown(BTNode child, float cooldown)
        {
            Child = child;
            CooldownDuration = cooldown;
        }

        public override BTStatus Tick(BTContext context, float deltaTime)
        {
            float gameTime = Time.GameTime;
            if (gameTime - _lastExecuteTime < CooldownDuration)
            {
                Status = BTStatus.Failure;
                return Status;
            }

            var status = Child?.Tick(context, deltaTime) ?? BTStatus.Failure;
            if (status == BTStatus.Success || status == BTStatus.Failure)
            {
                _lastExecuteTime = gameTime;
            }
            Status = status;
            return Status;
        }

        public override void Reset()
        {
            base.Reset();
            Child?.Reset();
        }
    }

    // ===== 叶子节点（动作） =====

    /// <summary>
    /// 动作节点：执行一个具体行为
    /// </summary>
    public class BTAction : BTNode
    {
        public Func<BTContext, float, BTStatus> Action { get; set; }

        public BTAction(string name, Func<BTContext, float, BTStatus> action)
        {
            Name = name;
            Action = action;
        }

        public override BTStatus Tick(BTContext context, float deltaTime)
        {
            Status = Action?.Invoke(context, deltaTime) ?? BTStatus.Failure;
            return Status;
        }
    }

    /// <summary>
    /// 等待节点：等待指定时间
    /// </summary>
    public class BTWait : BTNode
    {
        public float Duration = 1f;
        private float _elapsed = 0f;

        public BTWait(float duration)
        {
            Duration = duration;
        }

        public override BTStatus Tick(BTContext context, float deltaTime)
        {
            _elapsed += deltaTime;
            if (_elapsed >= Duration)
            {
                Status = BTStatus.Success;
                _elapsed = 0f;
            }
            else
            {
                Status = BTStatus.Running;
            }
            return Status;
        }

        public override void Reset()
        {
            base.Reset();
            _elapsed = 0f;
        }
    }

    /// <summary>
    /// 行为树组件 - 挂载到NPC Actor上驱动AI行为。
    /// 产品级特性：
    /// - 完整行为树框架（组合/装饰/叶子节点）
    /// - 黑板数据共享
    /// - 仇恨系统
    /// - 动态难度调整接口
    /// </summary>
    public class BehaviorTreeComponent : Script
    {
        /// <summary>行为树根节点</summary>
        public BTNode Root { get; set; }

        /// <summary>行为树上下文</summary>
        public BTContext Context { get; private set; } = new BTContext();

        /// <summary>更新间隔（秒，降低CPU开销）</summary>
        public float TickInterval = 0.1f;

        /// <summary>感知范围（发现玩家的距离）</summary>
        public float PerceptionRange = 15f;

        /// <summary>攻击范围</summary>
        public float AttackRange = 3f;

        /// <summary>巡逻范围（离出生点的最大距离）</summary>
        public float PatrolRange = 10f;

        /// <summary>是否启用</summary>
        public bool IsEnabled = true;

        private float _tickTimer = 0f;

        public override void OnStart()
        {
            Context.Self = Actor;
            Context.HomePosition = Actor.Position;

            // 如果未设置根节点，创建默认AI行为树
            if (Root == null)
            {
                Root = CreateDefaultCombatAI();
            }
        }

        public override void OnUpdate()
        {
            if (!IsEnabled || Root == null) return;

            _tickTimer += Time.DeltaTime;
            if (_tickTimer < TickInterval) return;
            float dt = _tickTimer;
            _tickTimer = 0f;

            // 更新上下文
            UpdateContext();

            // 执行行为树
            Root.Tick(Context, dt);
        }

        private void UpdateContext()
        {
            Context.SelfPosition = Actor.Position;

            if (Context.Target != null)
            {
                Context.TargetPosition = Context.Target.Position;
                Context.DistanceToTarget = Vector3.Distance(Context.SelfPosition, Context.TargetPosition);
            }
            else
            {
                Context.DistanceToTarget = float.MaxValue;
            }
        }

        /// <summary>设置目标</summary>
        public void SetTarget(FlaxEngine.Actor target)
        {
            Context.Target = target;
            Context.IsInCombat = target != null;
        }

        /// <summary>增加仇恨</summary>
        public void AddThreat(FlaxEngine.Actor attacker, float damage)
        {
            Context.AddThreat(attacker, damage);
            // 自动切换目标到最高仇恨
            var topTarget = Context.GetTopThreatTarget();
            if (topTarget != null && topTarget != Context.Target)
            {
                SetTarget(topTarget);
            }
        }

        /// <summary>
        /// 创建默认战斗AI行为树：
        /// Selector(
        ///   Sequence(有目标? -> 在攻击范围? -> 攻击)
        ///   Sequence(有目标? -> 追击)
        ///   Sequence(血量低? -> 逃跑/回血)
        ///   巡逻
        /// )
        /// </summary>
        private BTNode CreateDefaultCombatAI()
        {
            return new BTSelector(
                // 分支1：战斗（有目标且在攻击范围内）
                new BTSequence(
                    new BTCondition(ctx => ctx.HasTarget && ctx.DistanceToTarget <= AttackRange,
                        new BTAction("Attack", (ctx, dt) =>
                        {
                            // 执行攻击逻辑
                            ctx.Set("AttackTimer", ctx.Get<float>("AttackTimer") + dt);
                            if (ctx.Get<float>("AttackTimer") >= 1.5f) // 1.5秒攻击间隔
                            {
                                ctx.Set("AttackTimer", 0f);
                                return BTStatus.Success;
                            }
                            return BTStatus.Running;
                        })
                    )
                ),
                // 分支2：追击（有目标但不在攻击范围）
                new BTSequence(
                    new BTCondition(ctx => ctx.HasTarget && ctx.DistanceToTarget > AttackRange && ctx.DistanceToTarget < PerceptionRange * 2f,
                        new BTAction("Chase", (ctx, dt) =>
                        {
                            // 向目标移动
                            var direction = Vector3.Normalize(ctx.TargetPosition - ctx.SelfPosition);
                            ctx.Set("MoveDirection", direction);
                            return BTStatus.Running;
                        })
                    )
                ),
                // 分支3：低血量回退
                new BTSequence(
                    new BTCondition(ctx => ctx.HealthPercent < 0.2f,
                        new BTAction("Retreat", (ctx, dt) =>
                        {
                            // 向出生点撤退
                            var direction = Vector3.Normalize(ctx.HomePosition - ctx.SelfPosition);
                            ctx.Set("MoveDirection", direction);
                            ctx.IsInCombat = false;
                            return Vector3.Distance(ctx.SelfPosition, ctx.HomePosition) < 2f ? BTStatus.Success : BTStatus.Running;
                        })
                    )
                ),
                // 分支4：脱战回归
                new BTSequence(
                    new BTCondition(ctx => !ctx.HasTarget && Vector3.Distance(ctx.SelfPosition, ctx.HomePosition) > 2f,
                        new BTAction("ReturnHome", (ctx, dt) =>
                        {
                            var direction = Vector3.Normalize(ctx.HomePosition - ctx.SelfPosition);
                            ctx.Set("MoveDirection", direction);
                            return Vector3.Distance(ctx.SelfPosition, ctx.HomePosition) < 2f ? BTStatus.Success : BTStatus.Running;
                        })
                    )
                ),
                // 分支5：巡逻
                new BTAction("Patrol", (ctx, dt) =>
                {
                    // 简单巡逻：在出生点附近随机移动
                    if (!ctx.Blackboard.ContainsKey("PatrolTarget") || Vector3.Distance(ctx.SelfPosition, ctx.Get<Vector3>("PatrolTarget")) < 1f)
                    {
                        var offset = new Vector3(
                            (float)(new Random().NextDouble() * 2 - 1) * PatrolRange,
                            0,
                            (float)(new Random().NextDouble() * 2 - 1) * PatrolRange
                        );
                        ctx.Set("PatrolTarget", ctx.HomePosition + offset);
                    }
                    var patrolTarget = ctx.Get<Vector3>("PatrolTarget");
                    var dir = Vector3.Normalize(patrolTarget - ctx.SelfPosition);
                    ctx.Set("MoveDirection", dir);
                    return BTStatus.Running;
                })
            );
        }
    }
}
