using FlaxEngine;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 用角色实际位移速度驱动动画图的 Idle→Walk→Run 两级混合。
    /// <para>
    /// 图侧（Content/Character/Models/Animation Graph.flax）有 8 个节点：
    /// Output ← BlendRun ← (BlendWalk ← Idle/Walk, Run)，两个公开 Float 参数
    /// LocomotionWalk / LocomotionRun 分别是两级混合的 α。这里只负责把速度换算成 α，
    /// 不碰骨骼、不换图（热换图会卡死主线程）。
    /// </para>
    /// <para>
    /// 本地玩家与远端玩家都挂：位置分别由 LocalPlayerActorSyncSystem 与 ECS 插值写入，
    /// 所以统一用"根 Actor 的位移 / 时间"测速，不依赖输入或网络包类型。
    /// 速度单位与 PlayerController.MoveSpeed 同口径（走 5、跑 ×2）。
    /// </para>
    /// </summary>
    public class LocomotionGraphDriver : Script
    {
        [Header("参数名")]
        public string WalkParamName = "LocomotionWalk";
        public string RunParamName = "LocomotionRun";
        public string SprintParamName = "LocomotionSprint";
        public string TurnLeftParamName = "TurnLeft";
        public string TurnRightParamName = "TurnRight";
        public string StopLeftParamName = "StopLeft";
        public string StopRightParamName = "StopRight";
        public string TurnLeft180ParamName = "TurnLeft180";
        public string TurnRight180ParamName = "TurnRight180";
        public string JumpParamName = "Jump";
        public string FallParamName = "Fall";
        public string LandParamName = "Land";

        /// <summary>视为"原地转向"的最小角速度（度/秒）。低于它就当普通转身，不触发剪辑。</summary>
        [Header("原地转向（UE Turn In Place 对齐）")]
        public float TurnRateThreshold = 50f;

        /// <summary>停止转动后剪辑继续播的保持时间（秒），避免 0.2s 的抖动被立刻掐断。</summary>
        public float TurnHoldSeconds = 0.35f;

        /// <summary>转向权重平滑时间常数（秒）。</summary>
        public float TurnBlendTime = 0.12f;

        /// <summary>累计转角超过该值（度）时改用 180° 转身剪辑。</summary>
        public float TurnBigThreshold = 100f;

        /// <summary>"一整段转身"的静默窗口（秒）：期间只要还有偏航变化就继续累计。</summary>
        public float TurnEpisodeSeconds = 0.8f;

        /// <summary>调试：>= 0 时强制左转权重（0..1）并忽略角速度判定；&lt; 0 走自动。用于自动化取证。</summary>
        public float DebugForceTurn { get; set; } = -1f;

        /// <summary>调试：>= 0 时强制左 180° 转身权重（0..1）。</summary>
        public float DebugForceTurnBig { get; set; } = -1f;

        /// <summary>从"在走"跌到"几乎静止"时，停步剪辑继续播的保持时间（秒）。</summary>
        [Header("停步过渡（UE Stop 对齐）")]
        public float StopHoldSeconds = 0.25f;

        /// <summary>停步权重平滑时间常数（秒）。</summary>
        public float StopBlendTime = 0.10f;

        /// <summary>调试：>= 0 时强制左停步权重（0..1）并忽略速度判定；&lt; 0 走自动。</summary>
        public float DebugForceStop { get; set; } = -1f;

        /// <summary>空中混合的平滑时间常数（秒）。</summary>
        [Header("空中（Jump/Fall/Land）")]
        public float AirBlendTime = 0.10f;

        /// <summary>落地剪辑的保持时间（秒）：落地瞬间播一段，然后淡回地面姿态。</summary>
        public float LandHoldSeconds = 0.25f;

        /// <summary>
        /// 垂直速度高于该值（cm/s）算"上升"（Jump），否则算"下落"（Fall）。
        /// 判据是**方向**不是幅度：当前模拟层的跳跃顶点只有 1.5cm（米/厘米口径缺陷），
        /// 用 50cm/s 会把起跳也判成下落；取 1cm/s 后，将来把跳跃修成正常高度也仍然成立。
        /// </summary>
        public float AscendThreshold = 1f;

        /// <summary>没有 PlayerController 可问时（远端），垂直速度低于该值（cm/s）视为贴地。</summary>
        public float GroundedVerticalTolerance = 20f;

        /// <summary>调试：>= 0 时强制 Jump 权重（0..1）。</summary>
        public float DebugForceJump { get; set; } = -1f;

        /// <summary>调试：>= 0 时强制 Fall 权重（0..1）。</summary>
        public float DebugForceFall { get; set; } = -1f;

        /// <summary>调试：>= 0 时强制 Land 权重（0..1）。</summary>
        public float DebugForceLand { get; set; } = -1f;

        [Header("速度阈值（米/秒；Actor 位移是厘米，由 SpeedScale 换算）")]
        public float WalkSpeed = 5f;
        public float RunSpeed = 10f;
        public float SprintSpeed = 15f;
        /// <summary>
        /// 位移单位→阈值单位：本工程世界位置是**厘米**，阈值按米/秒标定，所以默认 0.01。
        /// 之前默认 1 时，任何真实移动都会先撞上 TeleportSpeed（60 被当成 60cm/s）而整段丢弃，
        /// 表现为"人在跑、LocomotionWalk 恒为 0、远端/本地都像 T-pose 滑行"（2026-10-25 实测）。
        /// </summary>
        public float SpeedScale = 0.01f;

        /// <summary>速度低通时间常数（秒）。越大越迟滞。</summary>
        [Header("平滑")]
        public float SmoothingTime = 0.12f;
        /// <summary>超过该瞬时速度（原始位移单位/秒）视为传送/出生点跳变：默认 6000 cm/s = 60 m/s。</summary>
        public float TeleportSpeed = 6000f;

        [Header("调试")]
        public bool DebugLog { get; set; }
        /// <summary>大于等于 0 时忽略位移测量、直接用这个速度（自动取证/联调用）。</summary>
        public float DebugForceSpeed { get; set; } = -1f;

        private AnimatedModel _model;
        private AnimationGraph _boundGraph;
        private AnimGraphParameter _walkParam;
        private AnimGraphParameter _runParam;
        private AnimGraphParameter _sprintParam;
        private AnimGraphParameter _turnLeftParam;
        private AnimGraphParameter _turnRightParam;
        private AnimGraphParameter _stopLeftParam;
        private AnimGraphParameter _stopRightParam;
        private AnimGraphParameter _turnLeft180Param;
        private AnimGraphParameter _turnRight180Param;
        private AnimGraphParameter _jumpParam;
        private AnimGraphParameter _fallParam;
        private AnimGraphParameter _landParam;
        private bool _paramsMissing;
        private bool _readyLogged;

        private float _lastYaw;
        private bool _hasLastYaw;
        private float _turnHold;
        private float _turnDir;
        private float _turnLeftAlpha;
        private float _turnRightAlpha;
        private float _lastTurnRate;
        private float _lastTurnHold;
        private float _lastTurnSpeed;
        private float _peakTurnRate;
        private float _prevSmoothedSpeed;
        private float _stopHold;
        private float _stopDir;
        private float _stopLeftAlpha;
        private float _stopRightAlpha;
        private float _turnLeft180Alpha;
        private float _turnRight180Alpha;
        private float _turnAccum;
        private float _turnEpisodeTimer;
        private bool _bigTurn;
        private float _movingMemory;
        private const float MovingMemorySeconds = 0.8f;
        private float _jumpAlpha;
        private float _fallAlpha;
        private float _landAlpha;
        private float _landHold;
        private bool _wasGrounded = true;
        private bool _hasGroundedSource;
        private float _lastVerticalSpeed;

        private Vector3 _lastPosition;
        private bool _hasLastPosition;
        private float _smoothedSpeed;
        private float _logTimer;

        public override void OnStart()
        {
            ResolveModel();
        }

        public override void OnUpdate()
        {
            if (!ResolveModel())
                return;

            if (!TryBindParameters())
                return;

            float dt = Mathf.Max(Time.DeltaTime, 0.0001f);
            float speed = MeasureSpeed(dt);

            float prevSmoothed = _smoothedSpeed;
            float target = Mathf.Max(0f, speed) * SpeedScale;
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, SmoothingTime));
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, target, k);
            _prevSmoothedSpeed = prevSmoothed;

            float walkAlpha = Mathf.Saturate(_smoothedSpeed / Mathf.Max(0.01f, WalkSpeed));
            float runAlpha = Mathf.Saturate((_smoothedSpeed - WalkSpeed) / Mathf.Max(0.01f, RunSpeed - WalkSpeed));
            float sprintAlpha = Mathf.Saturate((_smoothedSpeed - RunSpeed) / Mathf.Max(0.01f, SprintSpeed - RunSpeed));

            // 用引擎公开 setter 写：AnimGraphParameter 句柄若被按值拷贝，写它不会影响实例参数。
            _walkParam.Value = walkAlpha;
            _runParam.Value = runAlpha;
            _sprintParam.Value = sprintAlpha;

            UpdateTurnInPlace(dt, _smoothedSpeed);
            UpdateStopTransition(dt);
            UpdateAir(dt);

            if (DebugLog)
            {
                _logTimer += dt;
                if (_logTimer >= 0.5f)
                {
                    _logTimer = 0f;
                    Debug.Log($"[LocomotionGraphDriver:{Actor.Name}] speed={_smoothedSpeed:F2} walk={walkAlpha:F2} run={runAlpha:F2}"
                              + $" turnL={_turnLeftAlpha:F2} turnR={_turnRightAlpha:F2}"
                              + $" turnRate={_lastTurnRate:F1} peak={_peakTurnRate:F1} turnHold={_lastTurnHold:F2} turnSpeed={_lastTurnSpeed:F2}"
                              + $" stopL={_stopLeftAlpha:F2} stopR={_stopRightAlpha:F2} stopHold={_stopHold:F2}"
                              + $" big={(_bigTurn ? 1 : 0)} accum={_turnAccum:F1} t180L={_turnLeft180Alpha:F2} t180R={_turnRight180Alpha:F2}"
                              + $" ground={(_wasGrounded ? 1 : 0)} gsrc={(_hasGroundedSource ? 1 : 0)} vy={_lastVerticalSpeed:F1}"
                              + $" jump={_jumpAlpha:F2} fall={_fallAlpha:F2} land={_landAlpha:F2}");
                    _peakTurnRate = 0f;
                }
            }
        }

        /// <summary>
        /// 原地转向（UE Turn In Place）：几乎静止且角速度够大时，把左/右 90° 转身剪辑混进来；
        /// 松开后保持 <see cref="TurnHoldSeconds"/>，让 0.2–0.5s 的短转身也能播完。
        /// </summary>
        private void UpdateTurnInPlace(float dt, float speed)
        {
            if (DebugForceTurn >= 0f)
            {
                _turnLeftAlpha = Mathf.Saturate(DebugForceTurn);
                _turnRightAlpha = 0f;
                if (_turnLeftParam != null)
                    _turnLeftParam.Value = _turnLeftAlpha;
                if (_turnRightParam != null)
                    _turnRightParam.Value = _turnRightAlpha;
                _turnLeft180Alpha = 0f;
                _turnRight180Alpha = 0f;
                if (_turnLeft180Param != null)
                    _turnLeft180Param.Value = 0f;
                if (_turnRight180Param != null)
                    _turnRight180Param.Value = 0f;
                _lastYaw = Actor.EulerAngles.Y;
                _hasLastYaw = true;
                return;
            }

            float yaw = Actor.EulerAngles.Y;
            float rate = 0f;
            float deltaYaw = 0f;
            if (_hasLastYaw)
            {
                float d = yaw - _lastYaw;
                while (d > 180f) d -= 360f;
                while (d < -180f) d += 360f;
                deltaYaw = d;
                rate = d / dt;
            }
            _lastYaw = yaw;
            _hasLastYaw = true;

            if (DebugForceTurnBig >= 0f)
            {
                _turnLeft180Alpha = Mathf.Saturate(DebugForceTurnBig);
                _turnRight180Alpha = 0f;
                _turnLeftAlpha = 0f;
                _turnRightAlpha = 0f;
                if (_turnLeft180Param != null) _turnLeft180Param.Value = _turnLeft180Alpha;
                if (_turnRight180Param != null) _turnRight180Param.Value = _turnRight180Alpha;
                if (_turnLeftParam != null) _turnLeftParam.Value = 0f;
                if (_turnRightParam != null) _turnRightParam.Value = 0f;
                _lastYaw = Actor.EulerAngles.Y;
                _hasLastYaw = true;
                return;
            }

            bool nearlyStill = speed < WalkSpeed * 0.25f;
            bool turning = nearlyStill && Mathf.Abs(rate) >= TurnRateThreshold;
            if (turning)
            {
                _turnHold = TurnHoldSeconds;
                _turnDir = rate > 0f ? 1f : -1f;
            }
            else if (_turnHold > 0f)
            {
                _turnHold = Mathf.Max(0f, _turnHold - dt);
            }

            // 累计转角按"整段转身"累加：只要最近 TurnEpisodeSeconds 内还看到过偏航变化，就继续累加。
            // 逐帧阈值只看"这一帧转得够不够快"，离散注入（每步 10–20°）会让大部分帧速率为 0；
            // 用 hold 当窗口也不够——外部注入的步间隔可能超过 hold，accum 会被反复清零（实测卡在 10.0）。
            if (Mathf.Abs(deltaYaw) > 0.01f)
            {
                _turnEpisodeTimer = TurnEpisodeSeconds;
                _turnAccum += deltaYaw;
                if (Mathf.Abs(_turnAccum) >= TurnBigThreshold)
                    _bigTurn = true;
            }
            else if (_turnEpisodeTimer > 0f)
            {
                _turnEpisodeTimer = Mathf.Max(0f, _turnEpisodeTimer - dt);
                if (_turnEpisodeTimer <= 0f)
                {
                    _turnAccum = 0f;
                    _bigTurn = false;
                }
            }

            _lastTurnRate = rate;
            if (Mathf.Abs(rate) > _peakTurnRate) _peakTurnRate = Mathf.Abs(rate);
            _lastTurnHold = _turnHold;
            _lastTurnSpeed = speed;

            bool big = _bigTurn;
            bool active = _turnHold > 0f;
            float targetLeft = active && !big && _turnDir > 0f ? 1f : 0f;
            float targetRight = active && !big && _turnDir < 0f ? 1f : 0f;
            float targetLeft180 = active && big && _turnDir > 0f ? 1f : 0f;
            float targetRight180 = active && big && _turnDir < 0f ? 1f : 0f;
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, TurnBlendTime));
            _turnLeftAlpha = Mathf.Lerp(_turnLeftAlpha, targetLeft, k);
            _turnRightAlpha = Mathf.Lerp(_turnRightAlpha, targetRight, k);
            _turnLeft180Alpha = Mathf.Lerp(_turnLeft180Alpha, targetLeft180, k);
            _turnRight180Alpha = Mathf.Lerp(_turnRight180Alpha, targetRight180, k);

            if (_turnLeftParam != null)
                _turnLeftParam.Value = _turnLeftAlpha;
            if (_turnRightParam != null)
                _turnRightParam.Value = _turnRightAlpha;
            if (_turnLeft180Param != null)
                _turnLeft180Param.Value = _turnLeft180Alpha;
            if (_turnRight180Param != null)
                _turnRight180Param.Value = _turnRight180Alpha;
        }

        /// <summary>
        /// 停步过渡（UE Stop）：从"在走"跌到"几乎静止"时，按最后转向方向混入左/右停步剪辑；
        /// 原地转向进行中不触发（转身本身已有剪辑）。方向只能取"最后一次转向的方向"做近似。
        /// </summary>
        private void UpdateStopTransition(float dt)
        {
            float targetLeft = 0f;
            float targetRight = 0f;

            if (DebugForceStop >= 0f)
            {
                targetLeft = Mathf.Saturate(DebugForceStop);
                _stopHold = StopHoldSeconds;
            }
            else
            {
                // "刚在走"记忆窗：速度是平滑量，单帧不会从 3.0 直接掉到 1.25，
                // 用"最近 N 秒内确实走过"代替单帧前后比较（否则永远不触发）。
                if (_smoothedSpeed >= WalkSpeed * 0.6f)
                    _movingMemory = MovingMemorySeconds;
                else if (_movingMemory > 0f)
                    _movingMemory = Mathf.Max(0f, _movingMemory - dt);

                bool nowSlow = _smoothedSpeed < WalkSpeed * 0.25f;
                bool turning = _turnHold > 0f;
                if (nowSlow && _movingMemory > 0f && _stopHold <= 0f && !turning)
                {
                    _stopHold = StopHoldSeconds;
                    _stopDir = _turnDir != 0f ? _turnDir : 1f;
                    _movingMemory = 0f;
                }
                else if (_stopHold > 0f)
                {
                    _stopHold = Mathf.Max(0f, _stopHold - dt);
                }

                if (_stopHold > 0f)
                {
                    if (_stopDir > 0f) targetLeft = 1f;
                    else targetRight = 1f;
                }
            }

            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, StopBlendTime));
            _stopLeftAlpha = Mathf.Lerp(_stopLeftAlpha, targetLeft, k);
            _stopRightAlpha = Mathf.Lerp(_stopRightAlpha, targetRight, k);

            if (_stopLeftParam != null)
                _stopLeftParam.Value = _stopLeftAlpha;
            if (_stopRightParam != null)
                _stopRightParam.Value = _stopRightAlpha;
        }

        private bool ResolveModel()
        {
            if (_model != null && _model)
                return true;

            _model = Actor.FindActor<AnimatedModel>() ?? Actor.GetChild<AnimatedModel>();
            if (_model == null)
                return false;

            _boundGraph = null;
            _paramsMissing = false;
            return true;
        }

        private bool TryBindParameters()
        {
            if (_walkParam != null && _runParam != null && _sprintParam != null && _boundGraph == _model.AnimationGraph)
                return true;

            var graph = _model.AnimationGraph;
            if (graph == null || !graph.IsLoaded)
                return false;

            // 换过图（例如剪辑试播）就重新解析；新图没有这两个参数时只警告一次。
            if (graph != _boundGraph)
            {
                _boundGraph = graph;
                _walkParam = null;
                _runParam = null;
                _sprintParam = null;
                _turnLeftParam = null;
                _turnRightParam = null;
                _stopLeftParam = null;
                _stopRightParam = null;
                _turnLeft180Param = null;
                _turnRight180Param = null;
                _paramsMissing = false;
                _readyLogged = false;
            }

            if (_paramsMissing)
                return false;

            try
            {
                _walkParam = _model.GetParameter(WalkParamName);
                _runParam = _model.GetParameter(RunParamName);
                _sprintParam = _model.GetParameter(SprintParamName);
                // 转向参数是可选的：老图没有它时只停用转向层，不影响速度混合。
                _turnLeftParam = TryGetOptionalParameter(TurnLeftParamName);
                _turnRightParam = TryGetOptionalParameter(TurnRightParamName);
                _stopLeftParam = TryGetOptionalParameter(StopLeftParamName);
                _stopRightParam = TryGetOptionalParameter(StopRightParamName);
                _turnLeft180Param = TryGetOptionalParameter(TurnLeft180ParamName);
                _turnRight180Param = TryGetOptionalParameter(TurnRight180ParamName);
                _jumpParam = TryGetOptionalParameter(JumpParamName);
                _fallParam = TryGetOptionalParameter(FallParamName);
                _landParam = TryGetOptionalParameter(LandParamName);
                if (_turnLeftParam == null || _turnRightParam == null)
                {
                    Debug.LogWarning($"[LocomotionGraphDriver] 动画图 {graph.Path} 缺少转向参数 {TurnLeftParamName}/{TurnRightParamName}，原地转向层停用");
                }
                if (_stopLeftParam == null || _stopRightParam == null)
                {
                    Debug.LogWarning($"[LocomotionGraphDriver] 动画图 {graph.Path} 缺少停步参数 {StopLeftParamName}/{StopRightParamName}，停步层停用");
                }
            }
            catch (System.Exception ex)
            {
                _paramsMissing = true;
                Debug.LogWarning($"[LocomotionGraphDriver] 读取动画参数失败: {ex.GetType().Name}: {ex.Message}");
                return false;
            }

            if (_walkParam == null || _runParam == null || _sprintParam == null)
            {
                _paramsMissing = true;
                Debug.LogWarning($"[LocomotionGraphDriver] 动画图 {graph.Path} 缺少参数 {WalkParamName}/{RunParamName}/{SprintParamName}，locomotion 驱动停用");
                return false;
            }

            if (!_readyLogged)
            {
                _readyLogged = true;
                Debug.Log($"[LocomotionGraphDriver] READY graph={graph.Path} walk={WalkParamName} run={RunParamName} sprint={SprintParamName}"
                          + $" turnL={( _turnLeftParam != null ? TurnLeftParamName : "<无>")} turnR={( _turnRightParam != null ? TurnRightParamName : "<无>")}");
            }
            return true;
        }

        /// <summary>参数缺失时不抛（引擎在找不到名字时可能返回 null 或抛），只当"没有这一层"。</summary>
        private AnimGraphParameter TryGetOptionalParameter(string name)
        {
            try
            {
                return _model.GetParameter(name);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 空中层（UE Jump/Fall/Land 对齐）：不在地面时按垂直速度切 Jump/Fall；
        /// 落回地面的那一帧起播 <see cref="LandHoldSeconds"/> 的落地剪辑，然后淡回地面姿态。
        /// 本地玩家问 <see cref="PlayerController.IsGrounded"/>（ECS 权威）；远端没有控制器时用垂直速度推断。
        /// </summary>
        private void UpdateAir(float dt)
        {
            bool grounded = _wasGrounded;
            bool hasSource = false;
            var controller = Actor != null ? Actor.GetScript<PlayerController>() : null;
            if (controller != null)
            {
                grounded = controller.IsGrounded;
                hasSource = true;
            }
            if (!hasSource)
            {
                grounded = Mathf.Abs(_lastVerticalSpeed) < GroundedVerticalTolerance;
            }
            _hasGroundedSource = hasSource;

            if (grounded && !_wasGrounded)
            {
                _landHold = LandHoldSeconds;
            }
            _wasGrounded = grounded;

            float targetJump = 0f;
            float targetFall = 0f;
            float targetLand = 0f;
            if (!grounded)
            {
                _landHold = 0f;
                if (_lastVerticalSpeed > AscendThreshold)
                    targetJump = 1f;
                else
                    targetFall = 1f;
            }
            else if (_landHold > 0f)
            {
                targetLand = 1f;
                _landHold = Mathf.Max(0f, _landHold - dt);
            }

            if (DebugForceJump >= 0f)
            {
                targetJump = Mathf.Saturate(DebugForceJump);
                targetFall = 0f;
                targetLand = 0f;
            }
            else if (DebugForceFall >= 0f)
            {
                targetFall = Mathf.Saturate(DebugForceFall);
                targetJump = 0f;
                targetLand = 0f;
            }
            else if (DebugForceLand >= 0f)
            {
                targetLand = Mathf.Saturate(DebugForceLand);
                targetJump = 0f;
                targetFall = 0f;
            }

            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, AirBlendTime));
            _jumpAlpha = Mathf.Lerp(_jumpAlpha, targetJump, k);
            _fallAlpha = Mathf.Lerp(_fallAlpha, targetFall, k);
            _landAlpha = Mathf.Lerp(_landAlpha, targetLand, k);

            if (_jumpParam != null)
                _jumpParam.Value = _jumpAlpha;
            if (_fallParam != null)
                _fallParam.Value = _fallAlpha;
            if (_landParam != null)
                _landParam.Value = _landAlpha;
        }

        private float MeasureSpeed(float dt)
        {
            if (DebugForceSpeed >= 0f)
                return DebugForceSpeed;

            var position = Actor.Position;
            if (!_hasLastPosition)
            {
                _hasLastPosition = true;
                _lastPosition = position;
                return 0f;
            }

            var delta = position - _lastPosition;
            _lastPosition = position;
            // 垂直速度先取出来：空中层用它区分起跳/下落（水平速度仍只取 XZ）。
            _lastVerticalSpeed = delta.Y / dt;
            // 只取水平位移：垂直掉落/跳跃不该把角色推向跑步姿态。
            delta.Y = 0f;
            float speed = delta.Length / dt;
            if (speed > TeleportSpeed)
            {
                // 传送/重生：本帧读数无意义，沿用上一帧速度。
                _lastVerticalSpeed = 0f;
                return _smoothedSpeed;
            }
            return speed;
        }
    }
}
