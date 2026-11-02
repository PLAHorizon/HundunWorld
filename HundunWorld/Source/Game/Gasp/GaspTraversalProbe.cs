using FlaxEngine;
using System;
using System.Linq;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// Flax 侧翻越探测器：胸高前向射线找墙 → 从墙内上方向下找平台顶 → 高度/坡度/距离过滤。
    /// 全部单位厘米。
    /// </summary>
    public sealed class FlaxTraversalProbe : IGaspTraversalProbe
    {
        public Actor Owner;
        public uint LayerMask = uint.MaxValue;

        public float ChestHeight = 70f;
        public float ForwardDistance = 120f;
        public float MinHeight = 45f;
        public float MaxHeight = 220f;
        public float TopProbeExtraUp = 60f;
        public float TopProbeExtraDown = 80f;
        public float MaxWallNormalY = 0.4f;
        public float MinTopNormalY = 0.7f;
        public float MaxHorizontalDistance = 200f;

        /// <summary>膝高兜底射线高度（cm）。胸高射线会从低于胸口的台面上方掠过。</summary>
        public float LowProbeHeight = 30f;

        public bool TryFindLedge(Vector3 feetWorld, Vector3 moveDirWorld, out GaspLedgeInfo info)
        {
            info = default;
            var dir = moveDirWorld;
            dir.Y = 0f;
            if (dir.LengthSquared < 1e-5f)
                return false;
            dir.Normalize();

            // 先按胸高找；矮台（顶面 < 胸口）会被胸高射线从上方掠过，所以再来一条膝高射线兜底。
            if (TryProbeAtHeight(feetWorld, dir, ChestHeight, out info))
                return true;
            if (LowProbeHeight > 0f && TryProbeAtHeight(feetWorld, dir, LowProbeHeight, out info))
            {
                Debug.Log($"[GaspTraversalProbe] low-ledge fallback hit h={LowProbeHeight:F0}cm");
                return true;
            }
            return false;
        }

        /// <summary>在指定射线高度做一次「找墙 → 从墙内侧向下找顶面」的完整探测。</summary>
        private bool TryProbeAtHeight(Vector3 feetWorld, Vector3 dir, float probeHeight, out GaspLedgeInfo info)
        {
            info = default;
            var start = feetWorld + Vector3.Up * probeHeight;
            if (!RayCastFirstNonSelf(start, dir, ForwardDistance, out RayCastHit wallHit))
            {
                Debug.Log($"[GaspTraversalProbe] wall miss h={probeHeight:F0} start=({start.X:F0},{start.Y:F0},{start.Z:F0}) dir=({dir.X:F2},{dir.Z:F2}) dist={ForwardDistance:F0} mask={LayerMask}");
                if (probeHeight >= ChestHeight && Physics.RayCast(start, dir, out RayCastHit anyHit, ForwardDistance, uint.MaxValue, true))
                    Debug.Log($"[GaspTraversalProbe] any-layer hit collider={anyHit.Collider?.Name} at=({anyHit.Point.X:F0},{anyHit.Point.Y:F0},{anyHit.Point.Z:F0}) n=({anyHit.Normal.X:F2},{anyHit.Normal.Y:F2},{anyHit.Normal.Z:F2})");
                return false;
            }

            var n = wallHit.Normal;
            if (Mathf.Abs(n.Y) > MaxWallNormalY)
            {
                Debug.Log($"[GaspTraversalProbe] wall normal too vertical h={probeHeight:F0} n=({n.X:F2},{n.Y:F2},{n.Z:F2}) at=({wallHit.Point.X:F0},{wallHit.Point.Y:F0},{wallHit.Point.Z:F0})");
                return false;
            }
            var planarNormal = n;
            planarNormal.Y = 0f;
            if (planarNormal.LengthSquared < 1e-5f)
                return false;
            planarNormal.Normalize();

            // 从“墙内侧上方”向下找平台顶：法线指向角色，向墙内 = -normal。
            var topStart = wallHit.Point - planarNormal * 20f + Vector3.Up * (MaxHeight + TopProbeExtraUp);
            float downDistance = MaxHeight + TopProbeExtraUp + TopProbeExtraDown;
            if (!RayCastFirstNonSelf(topStart, Vector3.Down, downDistance, out RayCastHit topHit))
            {
                Debug.Log($"[GaspTraversalProbe] top miss h={probeHeight:F0} from=({topStart.X:F0},{topStart.Y:F0},{topStart.Z:F0}) down={downDistance:F0}");
                return false;
            }
            if (topHit.Normal.Y < MinTopNormalY)
            {
                Debug.Log($"[GaspTraversalProbe] top not walkable h={probeHeight:F0} nY={topHit.Normal.Y:F2} at=({topHit.Point.X:F0},{topHit.Point.Y:F0},{topHit.Point.Z:F0})");
                return false;
            }

            float height = topHit.Point.Y - feetWorld.Y;
            if (height < MinHeight || height > MaxHeight)
            {
                Debug.Log($"[GaspTraversalProbe] height reject h={probeHeight:F0} {height:F0}cm (allowed {MinHeight:F0}..{MaxHeight:F0})");
                return false;
            }

            var flatFeet = new Vector3(feetWorld.X, 0f, feetWorld.Z);
            var flatTop = new Vector3(topHit.Point.X, 0f, topHit.Point.Z);
            if (Vector3.Distance(flatFeet, flatTop) > MaxHorizontalDistance)
                return false;

            var right = Vector3.Cross(Vector3.Up, dir);
            float sideOffset = Vector3.Dot(wallHit.Point - feetWorld, right);
            info = new GaspLedgeInfo
            {
                Found = true,
                WallPoint = wallHit.Point,
                TopPoint = topHit.Point,
                WallNormal = planarNormal,
                Height = height,
                Side = sideOffset >= 0f ? (byte)1 : (byte)0,
            };
            return true;
        }

        private bool RayCastFirstNonSelf(Vector3 start, Vector3 dir, float distance, out RayCastHit hit)
        {
            hit = default;
            if (!Physics.RayCastAll(start, dir, out RayCastHit[] hits, distance, LayerMask, true))
                return false;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.Collider != null && IsSelfOrChild(h.Collider))
                    continue;
                hit = h;
                return true;
            }
            return false;
        }

        private bool IsSelfOrChild(Actor candidate)
        {
            Actor a = candidate;
            while (a != null)
            {
                if (a == Owner)
                    return true;
                a = a.Parent;
            }
            return false;
        }
    }

    /// <summary>
    /// 翻越驱动：检测 → 选 Mantle 剪辑 → 让 GaspAnimator 独占姿态 → 按计划输出位置。
    /// 正式游戏里位移权威在 ECS/服务端，应保持 AutoApplyMovement=false，由权威适配层消费
    /// Controller.CurrentPosition；测试/单机可直接开启。
    /// </summary>
    public class GaspTraversalDriver : Script
    {
        public bool Enable = true;
        [Tooltip("是否直接把计划位置写到 Actor（测试/单机）；正式联机必须由 ECS 权威消费")]
        public bool AutoApplyMovement;
        [Tooltip("正式联机：位移权威在服务端。关闭后仅本地播放动画/位移（单机夹具）")]
        public bool UseAuthorityMovement = true;
        public bool UseControllerMoveDirection = true;
        public float CooldownSeconds = 0.4f;

        [Tooltip("调试：请求一次翻越（夹具/输入层调用）")]
        public bool RequestStart;

        [Tooltip("翻越触发键（默认 F；旧 ClimbingSystem 已不再挂到玩家，此键当前空闲）")]
        public KeyboardKeys TriggerKey = KeyboardKeys.F;

        private GaspAnimator _gasp;
        private GaspTraversalController _controller;
        private FlaxTraversalProbe _probe;
        private float _nextAllowedTime;

        // ── 联机权威（UseAuthorityMovement）状态 ──────────────────────────
        private LocalSimulationSystem _localSim;
        private int _localEntityId;
        private ulong _pendingCharacterId;
        private long _pendingClientTick;
        private bool _localSubscribed;
        private float _authorityRequestAt = -1f;
        private bool _authorityResolved;
        private float _lastHeartbeatAt;

        /// <summary>
        /// 自测：<c>%APPDATA%/HundunWorld/gasp_traversal_request.on</c> 存在时，启动 2 秒后自动请求一次翻越。
        /// PIE 自动化没有键盘注入通道，靠标记文件才能走到真实射线/权威链路；正常环境不会有这个文件。
        /// <c>gasp_traversal_synthetic.on</c> 同时存在时用合成边缘（不依赖真实地形），
        /// 以便在空地上验证「请求→服务端受理→权威位移→本地预测」整条链路。
        /// </summary>
        private float _autoRequestAt = -1f;
        private bool _autoRequested;
        private bool _autoSynthetic;
        private bool _autoRealBox;
        private bool _autoRealBoxLow;
        private Actor _testBox;
        private int _autoAttempts;

        /// <summary>合成边缘探针：把一条固定计划交给正常选库/校验/上行路径（只给自测用）。</summary>
        private sealed class StaticLedgeProbe : IGaspTraversalProbe
        {
            private readonly float _height;

            public StaticLedgeProbe(float height) => _height = height;

            public bool TryFindLedge(Vector3 feetWorld, Vector3 moveDirWorld, out GaspLedgeInfo info)
            {
                var dir = moveDirWorld;
                dir.Y = 0f;
                if (dir.LengthSquared < 1e-5f)
                    dir = Vector3.Forward;
                dir.Normalize();

                // 与真实探针同一几何口径：墙在身前，台面高 60cm，落点再往里 55cm。
                info = new GaspLedgeInfo
                {
                    Found = true,
                    WallPoint = feetWorld + dir * 90f + Vector3.Up * _height,
                    TopPoint = feetWorld + dir * 55f + Vector3.Up * _height,
                    WallNormal = -dir,
                    Height = _height,
                    Side = 0,
                };
                return true;
            }
        }

        /// <summary>最近一次提交给权威的翻越计划（ECS 坐标，米）；诊断用。</summary>
        public TraversalPlan LastAuthorityPlan { get; private set; }

        public GaspTraversalController Controller => _controller;
        public IGaspTraversalProbe ProbeOverride { get; set; }
        public string LastMessage { get; private set; } = "";
        public Vector3 LastStartPosition { get; private set; }
        public Vector3 LastEndPosition { get; private set; }
        public string LastClip { get; private set; } = "";

        public override void OnStart()
        {
            _gasp = Actor.GetScript<GaspAnimator>();
            _probe = new FlaxTraversalProbe { Owner = Actor };
            _controller = new GaspTraversalController();
            _controller.Settings.CooldownSeconds = CooldownSeconds;

            try
            {
                var marker = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", "gasp_traversal_request.on");
                if (System.IO.File.Exists(marker))
                {
                    _autoRequestAt = Time.GameTime + 2f;
                    // 自测/取证时打开动画器诊断（生产默认关闭，避免每秒刷 state/trav diag）。
                    if (_gasp != null)
                        _gasp.DebugLog = true;
                    _autoSynthetic = System.IO.File.Exists(System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "HundunWorld", "gasp_traversal_synthetic.on"));
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    _autoRealBox = System.IO.File.Exists(System.IO.Path.Combine(appData, "HundunWorld", "gasp_traversal_realbox.on"));
                    _autoRealBoxLow = System.IO.File.Exists(System.IO.Path.Combine(appData, "HundunWorld", "gasp_traversal_realbox_low.on"));
                    Debug.Log($"[GaspTraversal] selftest armed (marker=gasp_traversal_request.on synthetic={_autoSynthetic})"
                              + $" limits minH={new TraversalLimits().MinHeight:F0} maxH={new TraversalLimits().MaxHeight:F0}"
                              + $" maxD={new TraversalLimits().MaxHorizontalDistance:F0} tol={new TraversalLimits().StartTolerance:F0}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GaspTraversal] selftest marker read failed: {ex.Message}");
            }
        }

        public override void OnUpdate()
        {
            if (!Enable)
                return;
            if (_gasp == null)
                _gasp = Actor.GetScript<GaspAnimator>();
            if (_gasp == null || !_gasp.IsReady)
                return;
            if (_controller.Database == null)
                _controller.Database = _gasp.Database;

            if (RequestStart || Input.GetKeyDown(TriggerKey))
            {
                RequestStart = false;
                TryBeginTraversal();
            }
            else if (_autoRequestAt > 0f && !_autoRequested && Time.GameTime >= _autoRequestAt)
            {
                // 自测/取证时打开动画器诊断（生产默认关闭）。
                if (_gasp != null)
                    _gasp.DebugLog = true;
                if ((_autoRealBox || _autoRealBoxLow) && _testBox == null)
                    SpawnRealTestBox(_autoRealBoxLow ? 60f : 120f);
                if (_autoSynthetic && ProbeOverride == null)
                    ProbeOverride = new StaticLedgeProbe(60f);
                _autoAttempts++;
                if (_autoAttempts == 1)
                    Debug.Log($"[GaspTraversal] selftest request fire synthetic={_autoSynthetic}");
                if (TryBeginTraversal())
                {
                    _autoRequested = true;
                    Debug.Log($"[GaspTraversal] selftest accepted locally attempt={_autoAttempts} clip={LastClip} msg={LastMessage}");
                }
                else if (_autoAttempts >= 30)
                {
                    // 30 秒都没就绪说明不是启动竞态：留一条带原因的终止日志，避免"标记没生效"被误读。
                    _autoRequested = true;
                    Debug.LogError($"[GaspTraversal] selftest give up after {_autoAttempts} attempts: {LastMessage}");
                }
                else
                {
                    // 世界/ECS/GASP 就绪有先后（实测 GaspAnimator READY 比首次 Update 晚约 0.5s），
                    // 自动请求必须重试而不是一次定生死。
                    _autoRequestAt = Time.GameTime + 1f;
                }
            }

            // 权威无回执兜底：网络/旧服务端会让请求石沉大海，本地预测不能无限进行下去
            // （实测：服务端不认这个包时会持续下发 Correction 把人拽回，形成纠正风暴）。
            if (_authorityRequestAt > 0f && !_authorityResolved && Time.GameTime - _authorityRequestAt > 3f)
            {
                _authorityResolved = true;
                _authorityRequestAt = -1f;
                Debug.LogWarning("[GaspTraversal] 权威 3s 无回执，取消本地翻越预测");
                _localSim?.CancelLocalTraversal(_localEntityId);
                if (_controller != null && _controller.Active)
                    _controller.Cancel();
                _gasp?.EndTraversal();
            }

            // 冷却衰减必须在非 Active 时也跑：Cancel() 会把 cooldown 设回 0.4s，
            // 若只在 Active 分支里 Tick，一次取消后 cooldown 永远不递减，翻越被永久锁死（实测 30 次重试全报"冷却中"）。
            _controller.Tick(Mathf.Max(Time.DeltaTime, 0.0001f));
            if (_controller.Active && Time.GameTime - _lastHeartbeatAt > 0.5f)
            {
                _lastHeartbeatAt = Time.GameTime;
                Debug.Log($"[GaspTraversal] hb active elapsed={_controller.Plan?.Elapsed:F2}/{_controller.Plan?.Duration:F2}"
                          + $" dt={Time.DeltaTime:F4} pending={_authorityRequestAt:F1} local={(_localSim != null && _localSim.IsTraversalActive(_localEntityId))}");
            }
            if (!_controller.Active)
                return;

            // 正式联机：位置由 ECS 预测/服务端权威推进，这里绝不写 Actor.Position。
            var applyHere = AutoApplyMovement || !UseAuthorityMovement;
            if (applyHere)
            {
                Actor.Position = _controller.CurrentPosition;
                Actor.Orientation = _controller.CurrentRotation;
            }
            if (_controller.FinishedThisTick)
            {
                if (applyHere && _controller.Plan != null)
                {
                    Actor.Position = _controller.Plan.EndPosition;
                    Actor.Orientation = _controller.Plan.EndRotation;
                }
                LastMessage = "finished";
                Debug.Log($"[GaspTraversal] finished clip={LastClip} end=({Actor.Position.X:F0},{Actor.Position.Y:F0},{Actor.Position.Z:F0})");
                _gasp.EndTraversal();
                _nextAllowedTime = Time.GameTime + CooldownSeconds;
            }
        }

        public bool TryBeginTraversal()
        {
            if (!Enable || _gasp == null || !_gasp.IsReady || _controller == null)
                return false;
            if (_controller.Database == null)
                _controller.Database = _gasp.Database;
            if (_controller.Active || Time.GameTime < _nextAllowedTime)
                return false;

            Vector3 dir = Vector3.Forward;
            if (_probe != null && _probe.Owner != null)
                dir = _probe.Owner.Transform.Forward;
            var pc = Actor.GetScript<PlayerController>();
            if (UseControllerMoveDirection && pc != null && pc.HasMoveInput)
                dir = pc.MoveDirectionWorld;
            dir.Y = 0f;
            if (dir.LengthSquared < 1e-5f)
                dir = Actor.Transform.Forward;
            dir.Y = 0f;
            if (dir.LengthSquared < 1e-5f)
                return false;
            dir.Normalize();

            bool found = _probe.TryFindLedge(Actor.Position, dir, out var ledge);
            if (!found && ProbeOverride != null)
                found = ProbeOverride.TryFindLedge(Actor.Position, dir, out ledge);
            if (!found)
            {
                LastMessage = "no ledge";
                Debug.Log($"[GaspTraversal] probe miss pos=({Actor.Position.X:F0},{Actor.Position.Y:F0},{Actor.Position.Z:F0})"
                          + $" dir=({dir.X:F2},{dir.Y:F2},{dir.Z:F2}) chest={_probe.ChestHeight:F0}");
                return false;
            }
            if (!_controller.TryStart(ledge, Actor.Position, Actor.Orientation, Time.GameTime))
            {
                LastMessage = _controller.LastReason;
                return false;
            }

            LastClip = _controller.Plan.ClipName;
            LastStartPosition = _controller.Plan.StartPosition;
            LastEndPosition = _controller.Plan.EndPosition;
            if (!_gasp.BeginTraversal(_controller.Plan.Clip))
            {
                _controller.Cancel();
                LastMessage = "GASP 未就绪";
                return false;
            }

            // 正式联机：位移不走本地自绘——登记 ECS 预测并把计划提交给权威（服务端按同一曲线推进）。
            if (UseAuthorityMovement && !AutoApplyMovement)
            {
                if (!TryBeginAuthority(_controller.Plan, out string authorityReason))
                {
                    _controller.Cancel();
                    _gasp.EndTraversal();
                    LastMessage = authorityReason;
                    Debug.LogWarning($"[GaspTraversal] 权威侧未开始：{authorityReason}");
                    return false;
                }
            }

            LastMessage = "started";
            Debug.Log($"[GaspTraversal] start clip={LastClip} height={ledge.Height:F0}cm side={ledge.Side}"
                      + $" start=({LastStartPosition.X:F0},{LastStartPosition.Y:F0},{LastStartPosition.Z:F0})"
                      + $" end=({LastEndPosition.X:F0},{LastEndPosition.Y:F0},{LastEndPosition.Z:F0})"
                      + (UseAuthorityMovement && !AutoApplyMovement ? " mode=authority" : " mode=local"));
            return true;
        }

        /// <summary>
        /// 把 Flax（Y-up）的翻越计划换算成权威坐标（ECS Z-up），本地预检后登记预测并上行请求。
        /// <para>
        /// 轴线换算与移动上行一致：ECS X = Flax X、ECS Y = Flax Z、ECS Z = Flax Y；
        /// **单位不变（厘米）**——<c>LocalPlayerActorSyncSystem</c> 写 Actor 位置时也是只换轴不缩放，
        /// 服务端权威位置与客户端 pred 用同一份数值。
        /// 朝向沿用服务端 <c>atan2(dy, dx)</c> 约定（dx = Flax X、dy = Flax Z）。
        /// </para>
        /// </summary>
        private bool TryBeginAuthority(GaspTraversalPlan plan, out string reason)
        {
            reason = null;
            var game = HundunWorldGame.Instance;
            var world = game?.ArchWorld;
            var host = game?.ArchHost;
            if (game == null || world == null || host == null)
            {
                reason = "ECS 世界未就绪";
                return false;
            }

            var sim = host.GetSystems(SystemGroup.FixedUpdate)
                .OfType<LocalSimulationSystem>()
                .FirstOrDefault();
            if (sim == null)
            {
                reason = "LocalSimulationSystem 未注册";
                return false;
            }

            // Arch 的 Entity.Id 从 0 开始，"Id==0"是合法实体，不能当"没找到"的哨兵
            // （实测本地玩家正是 0 号实体，一直被判"未就绪"）。
            bool found = false;
            int entityId = 0;
            ulong characterId = 0;
            float startYaw = 0f;
            float predX = 0f, predY = 0f, predZ = 0f;
            var query = new QueryDescription()
                .WithAll<NetworkIdentityComponent, PredictedTransformComponent>();
            world.Query(in query, (Entity entity, ref NetworkIdentityComponent netId, ref PredictedTransformComponent pred) =>
            {
                if (netId.IsLocalPlayer)
                {
                    found = true;
                    entityId = entity.Id;
                    characterId = netId.EntityId;
                    startYaw = pred.Yaw;
                    predX = pred.X;
                    predY = pred.Y;
                    predZ = pred.Z;
                }
            });
            if (!found || characterId == 0)
            {
                reason = "本地玩家实体未就绪";
                return false;
            }

            var netPlan = new TraversalPlan
            {
                // 起点取**预测位置**而不是渲染 Actor 位置：服务端拿权威位置比的是这一份，
                // 用插值后的 Actor 位置在高速移动时会天然超出 StartTolerance 而被拒。
                // 单位与世界单位一致（厘米）：ECS 与 Flax 只差轴序，不做缩放。
                StartX = predX,
                StartY = predY,
                StartZ = predZ,
                EndX = plan.EndPosition.X,
                EndY = plan.EndPosition.Z,
                EndZ = plan.EndPosition.Y,
                Duration = plan.Duration,
                ArcHeight = plan.ArcHeight,
                EndYaw = YawOfQuaternion(plan.EndRotation),
            };

            // 与服务端同一套限值/判据：本地能过的计划服务端一定也能过（除起点贴合由服务端用权威位置判）。
            var reject = TraversalMath.Validate(netPlan, netPlan.StartX, netPlan.StartY, netPlan.StartZ, new TraversalLimits());
            if (reject != TraversalRejectReason.None)
            {
                reason = "计划非法：" + reject
                    + $" start=({netPlan.StartX:F2},{netPlan.StartY:F2},{netPlan.StartZ:F2})"
                    + $" end=({netPlan.EndX:F2},{netPlan.EndY:F2},{netPlan.EndZ:F2})"
                    + $" dur={netPlan.Duration:F2} arc={netPlan.ArcHeight:F2}";
                return false;
            }

            if (!sim.BeginLocalTraversal(entityId, netPlan, startYaw))
            {
                reason = "本地已有进行中的翻越";
                return false;
            }

            var sender = game.GuardSyncSender;
            if (sender == null)
            {
                sim.CancelLocalTraversal(entityId);
                reason = "未连接同步通道";
                return false;
            }

            _localSim = sim;
            _localEntityId = entityId;
            _pendingCharacterId = characterId;
            _pendingClientTick = sim.CurrentClientTick;
            LastAuthorityPlan = netPlan;

            if (!_localSubscribed)
            {
                sim.LocalTraversalFinished += OnLocalTraversalFinished;
                sim.LocalTraversalCancelled += OnLocalTraversalCancelled;
                _localSubscribed = true;
            }

            var packet = new TraversalRequestPacket
            {
                CharacterId = characterId,
                ClientTick = _pendingClientTick,
                StartX = netPlan.StartX,
                StartY = netPlan.StartY,
                StartZ = netPlan.StartZ,
                EndX = netPlan.EndX,
                EndY = netPlan.EndY,
                EndZ = netPlan.EndZ,
                Duration = netPlan.Duration,
                ArcHeight = netPlan.ArcHeight,
                EndYaw = netPlan.EndYaw,
                CurveVersion = TraversalMath.Version,
            };
            _authorityResolved = false;
            _authorityRequestAt = Time.GameTime;
            _ = sender.SendLocalAsync(packet, characterId);
            return true;
        }

        /// <summary>
        /// 权威裁决回执（由 <c>HundunWorldGame</c> 从 SyncPacketMessageHandler 转来）。
        /// 拒绝时立刻撤销本地预测并结束动画——位置回正由随后的权威快照/修正负责。
        /// </summary>
        public void OnAuthorityResult(TraversalResultPacket result)
        {
            if (!UseAuthorityMovement || result == null)
                return;
            if (_pendingCharacterId == 0 || result.CharacterId != _pendingCharacterId)
                return;
            if (result.ClientTick != _pendingClientTick)
                return;

            _authorityResolved = true;
            _authorityRequestAt = -1f;

            if (result.Accepted)
            {
                LastMessage = "authority accepted";
                Debug.Log($"[GaspTraversal] authority accepted tick={result.ClientTick}"
                          + $" end=({result.EndX:F2},{result.EndY:F2},{result.EndZ:F2}) serverTick={result.ServerTick}");
                return;
            }

            var rejectReason = (TraversalRejectReason)result.Status;
            LastMessage = "authority rejected：" + rejectReason;
            Debug.LogWarning($"[GaspTraversal] 服务端拒绝翻越：{rejectReason}");
            _localSim?.CancelLocalTraversal(_localEntityId);
            if (_controller != null && _controller.Active)
                _controller.Cancel();
            _gasp?.EndTraversal();
        }

        private void OnLocalTraversalFinished(int entityId)
        {
            if (entityId != _localEntityId)
                return;
            LastMessage = "finished(prediction)";
            _gasp?.EndTraversal();
            _nextAllowedTime = Time.GameTime + CooldownSeconds;
        }

        private void OnLocalTraversalCancelled(int entityId)
        {
            if (entityId != _localEntityId)
                return;
            LastMessage = "cancelled(prediction)";
            _gasp?.EndTraversal();
        }

        /// <summary>
        /// 自测：在玩家正前方 1.5m 生成一个 120×60×120cm 的碰撞盒（顶面 60cm），
        /// 用**真实探针**（胸高射线→顶面向下）验证 PIE game 模式里的物理查询与过滤，而不是合成边缘。
        /// </summary>
        private void SpawnRealTestBox(float boxHeight)
        {
            var fwd = Actor.Transform.Forward;
            fwd.Y = 0f;
            if (fwd.LengthSquared < 1e-5f)
                fwd = Vector3.Forward;
            fwd.Normalize();

            var box = new BoxCollider { Name = "GaspTraversalTestBox" };
            Level.SpawnActor(box, Actor.Parent);
            // 探针先按胸高（70cm）射，矮台会被膝高兜底射线接住（LowProbeHeight=30）。
            box.Position = Actor.Position + fwd * 150f + Vector3.Up * (boxHeight * 0.5f);
            box.Scale = Vector3.One;
            box.Size = new Vector3(120f, boxHeight, 120f);
            box.IsActive = true;
            _testBox = box;
            Debug.Log($"[GaspTraversal] selftest real box spawned at=({box.Position.X:F0},{box.Position.Y:F0},{box.Position.Z:F0})");
        }

        public override void OnDestroy()
        {
            if (_testBox != null)
            {
                _testBox.IsActive = false;
                FlaxEngine.Object.Destroy(_testBox);
                _testBox = null;
            }
            if (_localSubscribed && _localSim != null)
            {
                _localSim.LocalTraversalFinished -= OnLocalTraversalFinished;
                _localSim.LocalTraversalCancelled -= OnLocalTraversalCancelled;
                _localSubscribed = false;
            }
        }

        /// <summary>服务端朝向约定：ECS 平面 yaw = atan2(Flax Z, Flax X)。</summary>
        private static float YawOfQuaternion(Quaternion orientation)
        {
            var forward = Vector3.Transform(Vector3.Forward, orientation);
            return Mathf.Atan2(forward.Z, forward.X);
        }

        public override void OnDebugDraw()
        {
            if (_controller == null || !_controller.Active || _controller.Plan == null)
                return;
            DebugDraw.DrawSphere(new BoundingSphere(_controller.Plan.StartPosition, 10f), Color.Yellow);
            DebugDraw.DrawSphere(new BoundingSphere(_controller.Plan.EndPosition, 10f), Color.Green);
            DebugDraw.DrawSphere(new BoundingSphere(_controller.CurrentPosition, 8f), Color.Cyan);
        }
    }
}
