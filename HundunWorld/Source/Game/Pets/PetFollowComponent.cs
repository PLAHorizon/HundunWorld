using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Pets
{
    /// <summary>
    /// 宠物跟随与自动寻路（客户端表现层）。
    /// <para>
    /// <b>判据住在服务端内核</b>（<c>Horizon.Game.Sim/Pets/PetFollowCore</c>），本组件只做一件事：
    /// 把"宠物应该在哪里"这个结论，用 Flax 的原生导航与角色位移表现到屏幕上。
    /// 数值（跟随距离 / leash / 传送阈值 / 速度）由服务端下发的宠物行为表提供，
    /// 本组件的默认值只是"还没连上服务端时先看个样子"。
    /// </para>
    /// <para>
    /// <b>寻路走 NarrativePro 的 <c>NavigatorStatics</c>，与玩家点击移动是同一条通路</b>
    /// （<c>PlayerController.BeginMoveTo</c> 就是这三件套：IsNavigationReady → TryFindPath → ResamplePath）。
    /// 刻意复用而不是另写一份：两条通路对同一个导航网格迟早给出不同答案，
    /// 那时的症状是"玩家能走到的地方宠物说走不到"。
    /// </para>
    /// <para>
    /// <b>导航不可用时降级直线</b>（与玩家侧同口径）。降级必须仍然走得动，
    /// 否则一处导航资产缺失会让宠物在玩家面前变成雕像。
    /// </para>
    /// </summary>
    public sealed class PetFollowComponent : Script
    {
        /// <summary>跟随指令（镜像服务端 <c>PetCommand</c> 的跟随相关子集）。</summary>
        public enum FollowCommand
        {
            /// <summary>跟随主人。</summary>
            Follow = 0,

            /// <summary>待命：原地不动，主人走远了也不追（WoW 的 Wait）。</summary>
            Wait = 1,

            /// <summary>交战：由攻击逻辑接管位置，本组件不再移动。</summary>
            Engaged = 2,

            /// <summary>撤退：立刻回到主人身边。</summary>
            Retreat = 3,
        }

        [Serialize]
        public float FollowDistance = 3f;

        [Serialize]
        public float LeashRadius = 12f;

        [Serialize]
        public float TeleportBeyondRadius = 40f;

        [Serialize]
        public float MoveSpeed = 5.5f;

        /// <summary>到点判定距离（米）。太大会"停在半路"，太小会在主人身边抖。</summary>
        [Serialize]
        public float ArriveEpsilon = 0.6f;

        /// <summary>重算路径的最小间隔（秒）。跟随目标一直在动，每帧重寻既贵又会抖。</summary>
        [Serialize]
        public float RepathIntervalSeconds = 1f;

        /// <summary>路径点重采样间距（米）。与玩家侧 ClickToMoveWaypointSpacing 同族。</summary>
        [Serialize]
        public float WaypointSpacing = 2f;

        /// <summary>主人 Actor；为空时本组件什么都不做（宠物留在原地，不报错刷屏）。</summary>
        public Actor Owner;

        /// <summary>复活施法秒数（尸体 → 可动作之间的等待，WoW 经典 4 秒）。</summary>
        [Serialize]
        public float ReviveCastSeconds = 4f;

        public FollowCommand Command = FollowCommand.Follow;

        private readonly Queue<Vector3> _path = new Queue<Vector3>();
        private float _repathCooldown;
        private bool _isCorpse;
        private float _reviveTimer;

        /// <summary>最近一次是否用上了导航网格（诊断与自检用；false = 直线降级）。</summary>
        public bool LastStepUsedNavigation { get; private set; }

        /// <summary>与主人的最近距离（诊断用）。</summary>
        public float DistanceToOwner { get; private set; }

        /// <summary>当前是否在尸体态。</summary>
        public bool IsCorpse => _isCorpse;

        public override void OnStart()
        {
            if (Owner == null)
            {
                // 不刷屏：没设主人是"还没接上"，不是错误状态。诊断信息取一次就够。
                Debug.Log("[PetFollow] 未设置 Owner，跟随暂停（宠物会留在原地）");
            }
        }

        /// <summary>血 0：进入尸体态（滞留原地，不再跟随，直到 Revive 或 SetAlive）。</summary>
        public void OnPetDied()
        {
            _isCorpse = true;
            _reviveTimer = 0f;
            _path.Clear();
            _repathCooldown = 0f;
        }

        /// <summary>开始复活施法；施法时长走完后自动恢复跟随。</summary>
        public void BeginRevive()
        {
            if (!_isCorpse) return;
            _reviveTimer = ReviveCastSeconds;
        }

        /// <summary>直接判定存活（服务端确认复活完成时调用）。</summary>
        public void SetAlive()
        {
            _isCorpse = false;
            _reviveTimer = 0f;
        }

        public override void OnUpdate()
        {
            if (Owner == null || _isCorpse)
            {
                if (_isCorpse && ReviveCastSeconds > 0f && _reviveTimer > 0f)
                {
                    _reviveTimer -= Time.DeltaTime;
                    if (_reviveTimer <= 0f) SetAlive();
                }
                return;
            }

            var self = Actor.Position;
            var ownerPos = Owner.Position;
            var distance = Vector3.Distance(ref self, ref ownerPos);
            DistanceToOwner = distance;

            // ① 超距直接出现在主人身边：不再演一段追不上的戏（WoW 就是这么处理的）
            if (distance > TeleportBeyondRadius)
            {
                TeleportBesideOwner(ownerPos, self);
                return;
            }

            // ② 待命与交战：本组件不移动
            if (Command == FollowCommand.Wait || Command == FollowCommand.Engaged)
            {
                _path.Clear();
                return;
            }

            // ③ 已在跟随圈内：停住。继续寻路会让他们互相绕圈。
            if (distance <= FollowDistance + ArriveEpsilon && Command != FollowCommand.Retreat)
            {
                _path.Clear();
                return;
            }

            _repathCooldown -= Time.DeltaTime;
            var goal = FollowGoal(ownerPos, self);

            // ④ 只有距离足够远才寻路：近处走 A* 会让宠物在主人脚边反复改折线（看起来像抖）
            if (distance > FollowDistance * 2f
                && (_path.Count == 0 || _repathCooldown <= 0f))
            {
                if (TryBuildNavigationPath(self, goal))
                {
                    LastStepUsedNavigation = true;
                    _repathCooldown = RepathIntervalSeconds;
                }
                else
                {
                    // 导航不可用/无路：直线兜底。降级不是失败——玩家要看见宠物还在跟。
                    LastStepUsedNavigation = false;
                    _path.Clear();
                    _path.Enqueue(goal);
                    _repathCooldown = RepathIntervalSeconds;
                }
            }

            AdvanceAlongPath(goal);
        }

        /// <summary>
        /// 跟随目标 = 主人身边、朝宠物方向回退 <see cref="FollowDistance"/> 的那一点。
        /// <para>不是主人本体：寻到本体就会让宠物反复撞在主人身上。</para>
        /// </summary>
        private Vector3 FollowGoal(Vector3 ownerPos, Vector3 selfPos)
        {
            var toPet = selfPos - ownerPos;
            var planar = new Vector3(toPet.X, 0f, toPet.Z);
            var length = planar.Length;
            if (length < 0.0001f) return ownerPos + new Vector3(FollowDistance, 0f, 0f);
            var back = length < FollowDistance ? length : FollowDistance;
            return ownerPos + planar / length * back;
        }

        /// <summary>出现在主人身边（保持水平面方向上的跟随距离，高度跟主人）。</summary>
        private void TeleportBesideOwner(Vector3 ownerPos, Vector3 selfPos)
        {
            var goal = FollowGoal(ownerPos, selfPos);
            Actor.Position = goal;
            _path.Clear();
            _repathCooldown = 0f;
            Debug.Log(string.Format("[PetFollow] 超距 {0:0.#}m > {1:0.#}m，传送到主人身边",
                DistanceToOwner, TeleportBeyondRadius));
        }

        private bool TryBuildNavigationPath(Vector3 start, Vector3 goal)
        {
            if (!NarrativePro.Navigation.NavigatorStatics.IsNavigationReady()) return false;

            Vector3[] corners;
            if (!NarrativePro.Navigation.NavigatorStatics.TryFindPath(start, goal, out corners)) return false;

            List<Vector3> waypoints;
            if (!NarrativePro.Navigation.NavigatorStatics.ResamplePath(corners, WaypointSpacing, out waypoints)) return false;
            if (waypoints == null || waypoints.Count < 2) return false;

            // waypoints[0] 就是当前位置，从第二个点开始走（与玩家侧同一条注释）
            _path.Clear();
            for (var i = 1; i < waypoints.Count; i++) _path.Enqueue(waypoints[i]);
            return _path.Count > 0;
        }

        private void AdvanceAlongPath(Vector3 goal)
        {
            var budget = MoveSpeed * Time.DeltaTime;
            if (budget <= 0f) return;

            var pos = Actor.Position;

            while (budget > 0f && _path.Count > 0)
            {
                var target = _path.Peek();
                var delta = target - pos;
                delta.Y = 0f; // 水平面推进；高度由地面/服务端快照决定，不让寻路把宠物抬起来
                var planar = delta.Length;
                if (planar <= ArriveEpsilon)
                {
                    _path.Dequeue();
                    continue;
                }
                if (planar <= budget)
                {
                    pos = new Vector3(target.X, pos.Y, target.Z);
                    budget -= planar;
                    _path.Dequeue();
                }
                else
                {
                    pos += delta / planar * budget;
                    budget = 0f;
                }
            }

            if (_path.Count == 0 && budget > 0f)
            {
                // 路点走完了还有余量：直接靠向最终目标，避免"走到了标记却没到"
                var tail = goal - pos;
                tail.Y = 0f;
                var tailLength = tail.Length;
                if (tailLength > ArriveEpsilon)
                {
                    var step = tailLength < budget ? tailLength : budget;
                    pos += tail / tailLength * step;
                }
            }

            Actor.Position = pos;
        }

        /// <summary>调试快照（给自检脚本与日志用，不参与任何判据）。</summary>
        public string Describe()
        {
            return string.Format("cmd={0} dist={1:0.#} nav={2} path={3} corpse={4}",
                Command, DistanceToOwner, LastStepUsedNavigation ? "有" : "无(直线)", _path.Count, _isCorpse);
        }
    }
}
