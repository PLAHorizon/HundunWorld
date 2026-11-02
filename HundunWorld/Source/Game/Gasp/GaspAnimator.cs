using System;
using System.Collections.Generic;
using FlaxEngine;
using HundunWorld.Game.Character;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// GASP 运行时：在角色根 Actor 上驱动 Motion Matching，并在动画图求值后覆盖骨骼姿态。
    /// 位移仍由 ECS 权威；这里只读 Actor 位姿，不写 Transform。
    /// </summary>
    public class GaspAnimator : Script
    {
        [Header("GASP")]
        public bool EnableGasp = true;
        [Tooltip("远端表现模式：不强制每帧求值/离屏更新，尊重网络 LOD；默认关闭脚部射线")]
        public bool RemoteMode;
        /// <summary>诊断：只把引擎图姿态转正后写回，不写 GASP 姿态。</summary>
        public bool WriteGraphPoseOnly;

        [Tooltip("留空则读 Content/Gasp/gasp_anims.bin")]
        public string DatabasePath = "";

        [Tooltip("激活时停用旧 LocomotionGraphDriver，避免它继续写图参数")]
        public bool DisableLegacyLocomotion = true;

        [Header("检索")]
        public float ContinuationBias = 150f;
        public float SwitchThreshold = 150f;
        public float BlendInTime = 0.20f;
        public float BlendOutTime = 0.20f;

        [Tooltip("把游戏速度换算成动画库标称速度（原地剪辑没有根运动，必须校准）")]
        public bool UseGaitCalibration = true;
        [Tooltip("没有 PlayerController 时的步态基准速度（厘米/秒，对应 MoveSpeed=500）")]
        public float GaitReferenceSpeed = 500f;
        public float PlayRateMin = 0.6f;
        public float PlayRateMax = 1.8f;

        [Header("速度/平滑")]
        public float VelocitySmoothing = 0.08f;
        public float YawSmoothing = 0.10f;
        public float TeleportSpeed = 6000f;
        public float LandHardSpeed = -800f;
        public float LandStateSeconds = 0.55f;

        [Header("脚部贴地 / IK")]
        public bool EnableFootPlacement = true;
        [Tooltip("脚踝目标离地高度（厘米）")]
        public float AnkleHeight = 8f;
        public float TraceUp = 50f;
        public float TraceDown = 100f;
        public float MaxPelvisDrop = 25f;
        public float MaxFootTiltDegrees = 30f;
        public uint FootLayerMask = uint.MaxValue;

        [Header("调试")]
        public bool DebugLog;
        public float DebugLogInterval = 0.5f;

        private AnimatedModel _model;
        private SkinnedModel _asset;
        private GaspSkeleton _skel;
        private GaspAnimationDatabase _db;
        private GaspMotionDatabase _motionDb;
        private GaspBlendStack _blend;
        private GaspMotionMatcher _matcher;
        private GaspQueryBuilder _query;
        private PlayerController _controller;
        private LocomotionGraphDriver _legacy;

        private GaspPoseBuffer _blended;
        private Matrix[] _rawPose;
        private Matrix[] _graphLocal;
        private Matrix[] _localPose;
        private Matrix[] _componentPose;
        private float[] _queryValues;
        private bool _inside;
        private bool _ready;
        private bool _subscribed;
        private bool _traversalActive;
        private int _applyCount;
        private float _logTimer;
        private bool _loggedInitFailure;

        private Vector3 _lastPosition;
        private bool _hasLastPosition;
        private Vector3 _smoothedVelocity;
        private float _lastYaw;
        private bool _hasLastYaw;
        private float _smoothedYawRate;

        private enum AirPhase { Ground, Rising, Falling, Landing }
        private AirPhase _phase = AirPhase.Ground;
        private bool _wasGrounded = true;
        private float _landTimer;
        private int _restIdleClip = -1;
        private bool _restIdle;
        private const float IdleEnterSpeed = 20f;
        private const float IdleExitSpeed = 50f;
        private float _fallPeak = 0f;
        private float _lastVerticalSpeed;
        private bool _hasGroundedSource;

        private int _idleClip = -1, _jumpClip = -1, _fallClip = -1, _fallFastClip = -1;
        private int _landLightClip = -1, _landHeavyClip = -1;

        // 遥测（自检/取证用）
        public bool IsReady => _ready;

        /// <summary>落脚事件：(0=左脚, 1=右脚, 世界坐标, 当前水平速度 cm/s)。Foley/特效的接入点。</summary>
        public event Action<int, Vector3, float> Footstep;
        private int _lastFootstepClip = -1;
        private float _lastFootstepTime;
        private readonly List<int> _footstepHits = new List<int>(4);

        /// <summary>转向扭曲（Steering Warping）：把剪辑的标称朝向扭到实际移动朝向。</summary>
        public bool EnableSteeringWarp = true;
        public float MaxSteerDegrees = 30f;
        public float SteerRateDegreesPerSecond = 360f;
        /// <summary>当前实际应用的转向扭曲角（度；诊断/测试用）。</summary>
        public float LastSteerWarpDegrees => _steerApplied * Mathf.RadiansToDegrees;

        private float _steerTarget;
        private float _steerApplied;

        /// <summary>KD 树候选上限：稠密（本地）/稀疏（远端降级）。</summary>
        public int DenseCandidateLimit = 128;
        public int SparseCandidateLimit = 32;

        private float _lastTraversalDiagAt;
        private float _lastStateDiagAt;
        private bool _lastGrounded;
        public string LastClipName { get; private set; } = "";
        public string LastMode { get; private set; } = "";
        public float LastCost { get; private set; }
        public float LastContinuingCost { get; private set; }
        public bool LastSwitched { get; private set; }
        public int PoseApplyCount => _applyCount;
        public string LastError { get; private set; } = "";
        public int MotionPoseCount => _motionDb != null ? _motionDb.Poses.Count : 0;
        public int MappedChannelCount { get; private set; }

        /// <summary>最近一帧查询使用的世界速度与两个朝向（弧度），调试绘制用。</summary>
        public Vector3 LastPlanarVelocity { get; private set; }
        public float LastDesiredHeadingRad { get; private set; }
        public float LastClipHeadingRad { get; private set; }
        /// <summary>最近一帧查询轨迹的世界坐标（索引同 GaspFeatureSchema.TrajectoryTimes）。</summary>
        public Vector3[] DebugTrajectory => _query.LastWorldTrajectory;

        public override void OnStart()
        {
            TryInitialize();
        }

        public override void OnUpdate()
        {
            if (!EnableGasp)
                return;
            if (_ready && (_model == null || !_model || _model.SkinnedModel != _asset ||
                           _model.AnimationGraph == null || !_model.AnimationGraph.IsLoaded))
            {
                // 换装/重建身体会把 AnimatedModel 换掉或清空图；停掉旧连接，下一帧重新初始化。
                ShutdownRuntime();
            }
            if (!_ready && !TryInitialize())
            {
                if (!_loggedInitFailure && Time.GameTime > 5f)
                {
                    _loggedInitFailure = true;
                    Debug.LogWarning("[GaspAnimator] 初始化未完成: " + LastError);
                }
                return;
            }
            if (!_ready)
                return;

            float dt = Mathf.Max(Time.DeltaTime, 0.0001f);
            if (_traversalActive)
            {
                // 翻越期间由 Traversal 控制器独占姿态，不跑 MM/空中状态机/脚部 IK。
                _steerTarget = 0f;
                _blend.Update(dt, _db);
                return;
            }

            MeasureActor(dt, out Vector3 velocity, out float yawRate, out bool grounded);
            _lastGrounded = grounded;
            _query.AddHistory(Time.GameTime, Actor.Position);

            // 运行态自证（1s 节流）：翻越退出后姿态不对时，先看是 MM 选错、空中状态卡住，还是脚部 IK 压秤。
            if (DebugLog && Time.GameTime - _lastStateDiagAt > 1f)
            {
                _lastStateDiagAt = Time.GameTime;
                int mc = _blend.MainClip;
                string mcName = mc >= 0 && mc < _db.Clips.Count ? _db.Clips[mc].Name : "-";
                Debug.Log($"[GaspAnimator] state phase={_phase} mode={LastMode} clip={LastClipName}"
                          + $" blend={mcName} t={_blend.MainTime:F2} players={_blend.PlayerCount}"
                          + $" grounded={grounded} speed={velocity.Length:F0}");
            }

            if (grounded)
            {
                // 只在“空中段”落地时进入 Landing；Landing 期间再调 EnterGround 会把 _landTimer 每帧重置，
                // 角色永远回不到 Ground（实机翻越后卡在 Land 蹲姿、MM 不恢复的根因）。
                if (_phase == AirPhase.Rising || _phase == AirPhase.Falling)
                    EnterGround(dt);
                if (_phase == AirPhase.Ground)
                    UpdateGroundMM(dt, velocity, yawRate, grounded);
            }
            else
            {
                _steerTarget = 0f;
                if (_phase == AirPhase.Ground || _phase == AirPhase.Landing)
                    EnterAir();
                UpdateAir(velocity);
            }

            _blend.Update(dt, _db);
            TickFootsteps();

            if (DebugLog)
            {
                _logTimer += dt;
                if (_logTimer >= DebugLogInterval)
                {
                    _logTimer = 0f;
                    Debug.Log($"[GaspAnimator:{Actor.Name}] mode={LastMode} clip={LastClipName}"
                              + $" cost={LastCost:F2} cont={LastContinuingCost:F2} switched={(LastSwitched ? 1 : 0)}"
                              + $" speed={_smoothedVelocity.Length:F0} warp={LastSteerWarpDegrees:F0}deg"
                              + $" players={_blend.PlayerCount} apply={_applyCount}");
                }
            }
        }

        /// <summary>按主剪辑时间推进检测落脚（跨过预计算的支撑相起点即触发一次）。</summary>
        private void TickFootsteps()
        {
            int clip = _blend.MainClip;
            if (clip < 0 || clip >= _db.Clips.Count || _motionDb == null
                || clip >= _motionDb.ClipPlantTimes.Count)
            {
                _lastFootstepClip = -1;
                return;
            }

            float t = _blend.MainTime;
            if (_lastFootstepClip != clip)
            {
                _lastFootstepClip = clip;
                _lastFootstepTime = t;
                return;
            }

            var times = _motionDb.ClipPlantTimes[clip];
            var sides = _motionDb.ClipPlantSides[clip];
            var c = _db.Clips[clip];
            if (times.Length > 0 && t != _lastFootstepTime)
            {
                int n = GaspFootstepTimeline.Collect(times, _lastFootstepTime, t, c.Loop, c.Length, _footstepHits);
                for (int i = 0; i < n; i++)
                {
                    int idx = _footstepHits[i];
                    int side = sides[idx];
                    int node = side == 0 ? _skel.FootL : _skel.FootR;
                    Vector3 world = node >= 0
                        ? _model.Transform.LocalToWorld(_componentPose[node].TranslationVector)
                        : _model.Position;
                    Footstep?.Invoke(side, world, _smoothedVelocity.Length);
                }
            }
            _lastFootstepTime = t;
        }

        private bool TryInitialize()
        {
            if (_ready)
                return true;
            if (!EnableGasp)
                return false;
            if (Actor == null)
                return false;

            if (_model == null || !_model)
                _model = Actor.FindActor<AnimatedModel>() ?? Actor.GetChild<AnimatedModel>();
            if (_model == null)
                return true; // 等待身体装配完成
            if (_model.SkinnedModel == null || !_model.SkinnedModel.IsLoaded)
                return true;
            if (_model.AnimationGraph == null || !_model.AnimationGraph.IsLoaded)
                return true;

            _asset = _model.SkinnedModel;
            // Ada 282 根骨时 PerBoneMotionBlur 会触发引擎首块缓冲断言，必须关闭。
            _model.PerBoneMotionBlur = false;
            if (!RemoteMode)
            {
                // GASP 需要每帧求值图姿态；远端尊重网络 LOD，不强行提高更新频率。
                _model.UpdateWhenOffscreen = true;
                _model.UpdateMode = AnimatedModel.AnimationUpdateMode.EveryUpdate;
            }
            _skel = GaspSkeleton.FromModel(_asset);
            if (_skel == null || _skel.Pelvis < 0)
            {
                LastError = "骨架缺少 pelvis";
                return true;
            }

            if (string.IsNullOrEmpty(DatabasePath))
            {
                // 打包版优先从 Content 的 RawDataAsset 读；本地/远端共享同一份只读库。
                _db = GaspRuntimeCache.GetDatabase(GaspAnimationDatabase.DefaultContentAssetPath);
            }
            else
            {
                _db = GaspAnimationDatabase.Load(DatabasePath);
            }
            if (!_db.IsLoaded)
            {
                LastError = "动画库加载失败(" + _db.SourcePath + "): " + _db.Error;
                Debug.LogWarning("[GaspAnimator] " + LastError);
                return true;
            }

            string skeletonKey = (_asset.Path ?? "<unknown>") + "|" + _skel.Count;
            _motionDb = GaspRuntimeCache.GetMotionDatabase(_db, _skel, skeletonKey);
            if (_motionDb == null || !_motionDb.IsBuilt)
            {
                LastError = "特征库构建失败: " + (_motionDb != null ? _motionDb.Error : "<null>");
                Debug.LogWarning("[GaspAnimator] " + LastError);
                return true;
            }

            _restIdleClip = -1;
            for (int i = 0; i < _db.Clips.Count; i++)
            {
                var ci = _db.Clips[i];
                if (ci.Tag != GaspClipTag.Ground || !ci.Loop)
                    continue;
                if (ci.Name.IndexOf("SecondaryMotion", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _restIdleClip = i;
                    break;
                }
                if (_restIdleClip < 0 && ci.Name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0)
                    _restIdleClip = i;
            }

            int mapped = 0;
            for (int i = 0; i < _db.Clips.Count; i++)
            {
                var clip = _db.Clips[i];
                for (int c = 0; c < clip.Channels.Length; c++)
                {
                    if (clip.Channels[c].NodeIndex >= 0)
                        mapped++;
                }
            }
            MappedChannelCount = mapped;

            _blend = new GaspBlendStack();
            _blend.Init(_skel, _db.MaxStride);
            _matcher = new GaspMotionMatcher
            {
                Database = _motionDb,
                // LOD：本地玩家用宽候选保证选库质量；远端角色只花稀疏候选的成本。
                CandidateLimit = RemoteMode ? SparseCandidateLimit : DenseCandidateLimit,
            };
            _blended = new GaspPoseBuffer(_skel.Count);
            _localPose = new Matrix[_skel.Count];
            _graphLocal = new Matrix[_skel.Count];
            _componentPose = new Matrix[_skel.Count];
            _queryValues = new float[GaspFeatureSchema.Dimension];

            int idle = FindClipByName("SecondaryMotion");
            if (idle < 0)
                idle = FindClipByName("Idle");
            _idleClip = idle;
            _jumpClip = FindClipByName("JumpLoop");
            _fallClip = FindClipByName("FallLoop");
            _fallFastClip = FindClipByName("FallLoop_Fast");
            _landLightClip = FindClipByName("Land_Light");
            _landHeavyClip = FindClipByName("Land_Heavy");

            var initialPose = new GaspPoseBuffer(_skel.Count);
            var initialScratch = new float[Mathf.Max(_db.MaxStride, 1)];
            if (_idleClip >= 0)
                GaspPoseUtil.SampleClip(_db.Clips[_idleClip], 0f, _skel, initialPose, initialScratch);
            else
                initialPose.ResetFromBind(_skel);
            var initialLocal = new Matrix[_skel.Count];
            var initialComp = new Matrix[_skel.Count];
            GaspPoseUtil.ComposeComponent(initialPose, _skel, initialLocal, initialComp, true);
            _query = new GaspQueryBuilder(_skel, initialComp, _motionDb.ForwardYaw);

            _controller = Actor.GetScript<PlayerController>();
            if (DisableLegacyLocomotion)
            {
                _legacy = Actor.GetScript<LocomotionGraphDriver>();
                if (_legacy != null)
                    _legacy.Enabled = false;
            }

            MetaHumanClipFrameFix.Detach(_model);
            _model.AnimationUpdated += OnAnimationUpdated;
            _subscribed = true;

            _ready = true;
            Debug.Log($"[GaspAnimator:{Actor.Name}] READY clips={_db.Clips.Count} poses={_motionDb.Poses.Count}"
                      + $" skeleton={_skel.Count} mappedChannels={MappedChannelCount} buildMs={_motionDb.BuildMilliseconds:F1}"
                      + $" db={_db.SourcePath} cache={GaspRuntimeCache.Summary}");

            GaspSelfTest.TryRun(this);
            return true;
        }

        private int FindClipByName(string token)
        {
            for (int i = 0; i < _db.Clips.Count; i++)
            {
                if (_db.Clips[i].Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
            return -1;
        }

        private void MeasureActor(float dt, out Vector3 velocity, out float yawRate, out bool grounded)
        {
            var pos = Actor.Position;
            velocity = Vector3.Zero;
            _lastVerticalSpeed = 0f;
            if (_hasLastPosition)
            {
                var delta = pos - _lastPosition;
                _lastVerticalSpeed = delta.Y / dt;
                delta.Y = 0f;
                float speed = delta.Length / dt;
                if (speed <= TeleportSpeed)
                    velocity = delta / dt;
                else
                    velocity = _smoothedVelocity;
            }
            _lastPosition = pos;
            _hasLastPosition = true;
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, VelocitySmoothing));
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, velocity, k);

            float yaw = Actor.EulerAngles.Y * Mathf.DegreesToRadians;
            yawRate = 0f;
            if (_hasLastYaw)
            {
                float d = Mathf.UnwindRadians(yaw - _lastYaw);
                yawRate = d / dt;
            }
            _lastYaw = yaw;
            _hasLastYaw = true;
            float ky = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, YawSmoothing));
            _smoothedYawRate = Mathf.Lerp(_smoothedYawRate, yawRate, ky);

            grounded = true;
            _hasGroundedSource = false;
            if (_controller != null)
            {
                grounded = _controller.IsGrounded;
                _hasGroundedSource = true;
            }
            if (!_hasGroundedSource)
                grounded = Mathf.Abs(_lastVerticalSpeed) < 20f;
            _wasGrounded = grounded;
            yawRate = _smoothedYawRate;
            velocity = _smoothedVelocity;
        }

        private void UpdateGroundMM(float dt, Vector3 velocity, float yawRate, bool grounded)
        {
            // 静止待机：库里没有可循环 Idle，MM 会在速度 0 时选中非循环 Stop/Turn 剪辑并停在末帧。
            // 等这些剪辑播完那一刻切到循环待机（SecondaryMotion），并跳过 MM 直到重新移动。
            LastPlanarVelocity = velocity;
            float planarSpeed = velocity.Length;
            if (_restIdleClip >= 0)
            {
                if (_restIdle)
                {
                    if (planarSpeed > IdleExitSpeed)
                    {
                        _restIdle = false;
                        _matcher.HasCurrent = false;
                    }
                    else
                    {
                        LastMode = "Idle";
                        LastClipName = _db.Clips[_restIdleClip].Name;
                        return;
                    }
                }
                else if (planarSpeed < IdleEnterSpeed)
                {
                    int cur = _matcher.HasCurrent ? _matcher.Current.Clip : -1;
                    bool nonLoopEnded = false;
                    if (cur >= 0 && cur < _db.Clips.Count && _blend.MainClip == cur)
                    {
                        var c = _db.Clips[cur];
                        nonLoopEnded = !c.Loop && _blend.MainTime >= c.Length - 0.02f;
                    }
                    if (nonLoopEnded)
                    {
                        _restIdle = true;
                        _matcher.HasCurrent = false;
                        _blend.Play(_restIdleClip, 0f, true, 0.25f, 0.25f);
                        _blend.SetMainPlayRate(1f);
                        LastMode = "Idle";
                        LastClipName = _db.Clips[_restIdleClip].Name;
                        return;
                    }
                }
            }

            var input = new GaspMotionInput
            {
                Position = Actor.Position,
                Yaw = _lastYaw,
                YawRate = yawRate,
                Velocity = velocity,
                Grounded = grounded,
                VerticalSpeed = _lastVerticalSpeed,
            };

            if (_controller != null)
            {
                var dirWorld = _controller.MoveDirectionWorld;
                if (dirWorld.LengthSquared > 0.0001f)
                {
                    var d = dirWorld;
                    d.Y = 0f;
                    d.Normalize();
                    float speed = ResolveGaitSpeed(_controller.CurrentTargetSpeed, _controller.MoveSpeed);
                    var calibratedWorld = d * speed;
                    input.Velocity = calibratedWorld;
                    input.DesiredVelocityLocal = RotateY(calibratedWorld, -_lastYaw - _motionDb.ForwardYaw);
                    input.Gait = GaitOfSpeed(speed);
                    input.HasInput = true;
                }
            }
            else if (velocity.Length > 20f)
            {
                var d = velocity;
                d.Y = 0f;
                d.Normalize();
                float speed = ResolveGaitSpeed(velocity.Length, GaitReferenceSpeed);
                var calibratedWorld = d * speed;
                input.Velocity = calibratedWorld;
                input.DesiredVelocityLocal = RotateY(calibratedWorld, -_lastYaw - _motionDb.ForwardYaw);
                input.Gait = GaitOfSpeed(speed);
                input.HasInput = true;
            }

            // 转向扭曲目标：仅在地面 MM 且有速度时生效（剪辑标称朝向 vs 实际移动朝向）。
            _steerTarget = 0f;
            if (EnableSteeringWarp && _matcher.HasCurrent && planarSpeed > 20f)
            {
                int curClip = _matcher.Current.Clip;
                if (curClip >= 0 && curClip < _motionDb.ClipNominalVelocity.Length)
                {
                    var clipNominal = _motionDb.ClipNominalVelocity[curClip];
                    // 模型→世界：与查询构建的世界→模型（-yaw-ForwardYaw）严格互逆。
                    var world = RotateY(clipNominal, _lastYaw + _motionDb.ForwardYaw);
                    float clipHeading = Mathf.Atan2(world.X, world.Z);
                    float desiredHeading = Mathf.Atan2(velocity.X, velocity.Z);
                    LastClipHeadingRad = clipHeading;
                    LastDesiredHeadingRad = desiredHeading;
                    _steerTarget = GaspPoseWarp.ComputeSteer(desiredHeading, clipHeading, planarSpeed,
                        _motionDb.WalkSpeed, MaxSteerDegrees);
                }
            }

            _query.Build(input, dt, _queryValues);
            bool switched = _matcher.Update(_queryValues, dt, Time.GameTime, ContinuationBias, SwitchThreshold, input.Gait, input.YawRate);
            LastCost = _matcher.LastCost;
            LastContinuingCost = _matcher.LastContinuingCost;
            LastSwitched = switched;
            LastMode = "MM";
            if (switched)
            {
                var clip = _db.Clips[_matcher.Current.Clip];
                LastClipName = clip.Name;
                _blend.Play(_matcher.Current.Clip, _matcher.CurrentTime, clip.Loop, BlendInTime, BlendOutTime);
            }

            // 原地剪辑按实际速度调播放速率，减少滑步（有上限，避免动作过快）。
            float nominal = NominalSpeedOf(LastClipName);
            if (nominal > 20f)
            {
                float rate = Mathf.Clamp(velocity.Length / nominal, PlayRateMin, PlayRateMax);
                _blend.SetMainPlayRate(rate);
            }
            else
            {
                _blend.SetMainPlayRate(1f);
            }

        }

        /// <summary>
        /// 游戏速度 → 动画库标称速度。原地剪辑没有根运动，绝对速度与库中特征不可直接比较；
        /// 按 MoveSpeed 的倍数归类到走/跑/冲刺，再取该族标称速度。
        /// </summary>
        private float ResolveGaitSpeed(float targetSpeed, float baseSpeed)
        {
            if (!UseGaitCalibration)
                return targetSpeed;
            float walk = _motionDb.WalkSpeed;
            float run = _motionDb.RunSpeed;
            float sprint = _motionDb.SprintSpeed;
            if (walk <= 1f && run <= 1f && sprint <= 1f)
                return targetSpeed;
            if (targetSpeed < 20f)
                return 0f;

            float reference = baseSpeed > 1f ? baseSpeed : GaitReferenceSpeed;
            float ratio = targetSpeed / Mathf.Max(1f, reference);
            if (ratio <= 1.15f) return walk > 1f ? walk : targetSpeed;
            if (ratio <= 2.15f) return run > 1f ? run : targetSpeed;
            return sprint > 1f ? sprint : targetSpeed;
        }

        private byte GaitOfSpeed(float speed)
        {
            if (speed <= 1f)
                return 0;
            if (Mathf.Abs(speed - _motionDb.WalkSpeed) < 1f)
                return 1;
            if (Mathf.Abs(speed - _motionDb.RunSpeed) < 1f)
                return 2;
            if (Mathf.Abs(speed - _motionDb.SprintSpeed) < 1f)
                return 3;
            return 0;
        }

        private float NominalSpeedOf(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return 0f;
            if (clipName.IndexOf("Sprint", StringComparison.OrdinalIgnoreCase) >= 0)
                return _motionDb.SprintSpeed;
            if (clipName.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0)
                return _motionDb.RunSpeed;
            if (clipName.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0)
                return _motionDb.WalkSpeed;
            return 0f;
        }

        private void EnterAir()
        {
            _restIdle = false;
            _phase = _lastVerticalSpeed > 50f ? AirPhase.Rising : AirPhase.Falling;
            _fallPeak = _lastVerticalSpeed;
            PlayAirClip();
        }

        private void UpdateAir(Vector3 velocity)
        {
            _fallPeak = Mathf.Min(_fallPeak, _lastVerticalSpeed);
            if (_phase == AirPhase.Rising && _lastVerticalSpeed < -20f)
            {
                _phase = AirPhase.Falling;
                PlayAirClip();
            }
            else if (_phase == AirPhase.Falling && _lastVerticalSpeed > 50f)
            {
                _phase = AirPhase.Rising;
                PlayAirClip();
            }
        }

        private void PlayAirClip()
        {
            int clip = _jumpClip;
            string name = "Jump";
            if (_phase == AirPhase.Falling)
            {
                bool fast = _fallPeak < LandHardSpeed;
                clip = fast ? _fallFastClip : _fallClip;
                if (clip < 0) clip = _fallClip;
                name = "Fall";
            }
            LastMode = name;
            if (clip >= 0)
            {
                var c = _db.Clips[clip];
                LastClipName = c.Name;
                _blend.Play(clip, 0f, c.Loop, 0.15f, 0.15f);
            }
        }

        private void EnterGround(float dt)
        {
            bool hard = _fallPeak < LandHardSpeed;
            int clip = hard ? _landHeavyClip : _landLightClip;
            if (clip < 0) clip = _landLightClip;
            _phase = AirPhase.Landing;
            _landTimer = LandStateSeconds;
            LastMode = "Land";
            if (clip >= 0)
            {
                LastClipName = _db.Clips[clip].Name;
                _blend.Play(clip, 0f, false, 0.10f, 0.20f);
            }
            _matcher.HasCurrent = false;
            _fallPeak = 0f;
        }

        private void UpdateLanding(float dt)
        {
            _landTimer -= dt;
            if (_landTimer <= 0f)
            {
                _phase = AirPhase.Ground;
                _matcher.HasCurrent = false;
            }
        }

        private void ApplyFootPlacement()
        {
            int thighL = _skel.ThighL, calfL = _skel.CalfL, footL = _skel.FootL;
            int thighR = _skel.ThighR, calfR = _skel.CalfR, footR = _skel.FootR;
            if (thighL < 0 || calfL < 0 || footL < 0 || thighR < 0 || calfR < 0 || footR < 0)
                return;

            var modelXf = _model.Transform;
            Vector3 ankL = _componentPose[footL].TranslationVector;
            Vector3 ankR = _componentPose[footR].TranslationVector;
            bool hasL = TryGroundTarget(ankL, modelXf, out Vector3 targetL, out Vector3 normalL);
            bool hasR = TryGroundTarget(ankR, modelXf, out Vector3 targetR, out Vector3 normalR);
            if (!hasL && !hasR)
                return;
            if (!hasL) targetL = ankL;
            if (!hasR) targetR = ankR;

            float drop = GaspLegSolver.ComputePelvisDrop(ankL, targetL, ankR, targetR, MaxPelvisDrop);
            if (drop < -0.01f)
            {
                GaspLegSolver.OffsetPelvis(_componentPose, _skel, drop);
                ankL.Y += drop;
                ankR.Y += drop;
            }

            if (hasL)
            {
                GaspLegSolver.SolveLegComponent(_componentPose, _skel, thighL, calfL, footL, targetL, 3, out _, out _);
                if (hasR)
                    GaspLegSolver.SolveLegComponent(_componentPose, _skel, thighR, calfR, footR, targetR, 3, out _, out _);
                var nL = modelXf.WorldToLocalVector(normalL);
                if (nL.LengthSquared > 1e-6f)
                    GaspLegSolver.AlignFootToGround(_componentPose, _skel, footL, nL.Normalized, MaxFootTiltDegrees);
                if (hasR)
                {
                    var nR = modelXf.WorldToLocalVector(normalR);
                    if (nR.LengthSquared > 1e-6f)
                        GaspLegSolver.AlignFootToGround(_componentPose, _skel, footR, nR.Normalized, MaxFootTiltDegrees);
                }
            }
            else if (hasR)
            {
                GaspLegSolver.SolveLegComponent(_componentPose, _skel, thighR, calfR, footR, targetR, 3, out _, out _);
                var nR = modelXf.WorldToLocalVector(normalR);
                if (nR.LengthSquared > 1e-6f)
                    GaspLegSolver.AlignFootToGround(_componentPose, _skel, footR, nR.Normalized, MaxFootTiltDegrees);
            }
        }

        private bool TryGroundTarget(Vector3 ankleModel, Transform modelXf, out Vector3 target, out Vector3 groundNormal)
        {
            target = ankleModel;
            groundNormal = Vector3.Up;
            Vector3 world = modelXf.LocalToWorld(ankleModel);
            Vector3 start = world + Vector3.Up * TraceUp;
            if (!Physics.RayCastAll(start, Vector3.Down, out RayCastHit[] hits, TraceUp + TraceDown, FootLayerMask, true))
                return false;

            RayCastHit? ground = null;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.Collider != null && IsSelfOrChild(h.Collider))
                    continue;
                ground = h;
                break;
            }
            if (!ground.HasValue)
                return false;

            groundNormal = ground.Value.Normal;
            Vector3 groundWorld = ground.Value.Point + Vector3.Up * AnkleHeight;
            Vector3 groundModel = modelXf.WorldToLocal(groundWorld);
            float y = Mathf.Clamp(groundModel.Y, ankleModel.Y - 40f, ankleModel.Y + 25f);
            target = new Vector3(ankleModel.X, y, ankleModel.Z);
            return true;
        }

        private bool IsSelfOrChild(Actor candidate)
        {
            Actor a = candidate;
            while (a != null)
            {
                if (a == Actor || a == _model)
                    return true;
                a = a.Parent;
            }
            return false;
        }

        private void LogPoseCompare(string label, int node)
        {
            if (node < 0 || node >= _componentPose.Length || _rawPose == null || node >= _rawPose.Length)
                return;
            Vector3 ours = _componentPose[node].TranslationVector;
            Vector3 graph = _rawPose[node].TranslationVector;
            int parent = _skel.Parents[node];
            Matrix graphLocal = parent >= 0 ? _rawPose[node] * Matrix.Invert(_rawPose[parent]) : _rawPose[node];
            graphLocal.Decompose(out Float3 gs, out Quaternion gq, out Float3 gt);
            var oursLocal = _blended.Local[node];
            float qd = Mathf.Abs(Quaternion.Dot(Quaternion.Normalize(gq), Quaternion.Normalize(oursLocal.Orientation)));
            Debug.Log($"[GaspPoseCompare] {label} ours=({ours.X:F1},{ours.Y:F1},{ours.Z:F1})"
                      + $" graph=({graph.X:F1},{graph.Y:F1},{graph.Z:F1}) d={(ours - graph).Length:F1}"
                      + $" | local oursT=({oursLocal.Translation.X:F1},{oursLocal.Translation.Y:F1},{oursLocal.Translation.Z:F1})"
                      + $" graphT=({gt.X:F1},{gt.Y:F1},{gt.Z:F1}) qdot={qd:F3}");
        }

        private void OnAnimationUpdated()
        {
            if (_inside || !_ready)
                return;
            _inside = true;
            try
            {
                if (_phase == AirPhase.Landing)
                    UpdateLanding(Mathf.Max(Time.DeltaTime, 0.0001f));

                int count = _skel.Count;
                if (_rawPose == null || _rawPose.Length != count)
                    _rawPose = new Matrix[count];
                _model.GetCurrentPose(out _rawPose, false);
                if (_rawPose == null || _rawPose.Length != count)
                    return;

                // 图的组件矩阵反解成相对父节点的局部矩阵，供权重<1时的回混使用。
                for (int i = 0; i < count; i++)
                {
                    int p = _skel.Parents[i];
                    _graphLocal[i] = p >= 0 ? _rawPose[i] * Matrix.Invert(_rawPose[p]) : _rawPose[i];
                }

                _blend.Evaluate(_db, _graphLocal, _blended);

                // 翻越收尾兜底：驱动若错过 finish（实机出现过），动画层也必须自己退出，
                // 否则会一直停在 Mantle 末帧。主剪辑播到末尾 = 该退出。
                if (_traversalActive)
                {
                    int mainClip = _blend.MainClip;
                    if (mainClip >= 0 && mainClip < _db.Clips.Count)
                    {
                        var travClip = _db.Clips[mainClip];
                        if (_blend.MainTime >= travClip.Length - 0.02f)
                        {
                            Debug.Log($"[GaspAnimator] traversal clip end ({travClip.Name} t={_blend.MainTime:F2}/{travClip.Length:F2}) → EndTraversal");
                            EndTraversal();
                        }
                    }
                    if (DebugLog && Time.GameTime - _lastTraversalDiagAt > 1f)
                    {
                        _lastTraversalDiagAt = Time.GameTime;
                        var pelvis = _componentPose[_skel.Pelvis].TranslationVector;
                        Float3 footL = _skel.FootL >= 0 ? _componentPose[_skel.FootL].TranslationVector : new Float3();
                        Float3 footR = _skel.FootR >= 0 ? _componentPose[_skel.FootR].TranslationVector : new Float3();
                        Debug.Log($"[GaspAnimator] trav diag clip={LastClipName} t={_blend.MainTime:F2}"
                                  + $" pelvis=({pelvis.X:F0},{pelvis.Y:F0},{pelvis.Z:F0})"
                                  + $" footL=({footL.X:F0},{footL.Y:F0},{footL.Z:F0})"
                                  + $" footR=({footR.X:F0},{footR.Y:F0},{footR.Z:F0})");
                    }
                }

                // 局部姿态 → 未转正的组件矩阵；未被 GASP 覆盖的骨骼沿用图的组件矩阵。
                for (int i = 0; i < count; i++)
                {
                    var t = _blended.Local[i];
                    _localPose[i] = Matrix.Transformation(t.Scale, t.Orientation, t.Translation);
                }
                for (int i = 0; i < count; i++)
                {
                    int p = _skel.Parents[i];
                    _componentPose[i] = p >= 0 ? _localPose[i] * _componentPose[p] : _localPose[i];
                }
                // 未覆盖骨骼也保留在“我们的局部姿态”里（绑定/缺省），避免与图的组件姿态混成两套骨架。

                if (WriteGraphPoseOnly)
                {
                    for (int i = 0; i < count; i++)
                        _componentPose[i] = _rawPose[i];
                }

                // Z-up→Y-up 修正作用在组件矩阵上（与 MetaHumanClipFrameFix 一致）。
                if (DebugLog && _applyCount < 3)
                {
                    LogPoseCompare("pelvis", _skel.Pelvis);
                    LogPoseCompare("foot_l", _skel.FootL);
                    LogPoseCompare("hand_l", _skel.HandL);
                }
                for (int i = 0; i < count; i++)
                    _componentPose[i] = GaspSkeleton.ToModel * _componentPose[i] * GaspSkeleton.ToClip;

                // 转向扭曲：绕骨盆竖轴把整套姿态扭向实际移动方向（IK 之前，脚部对齐随后纠正）。
                _steerApplied = GaspPoseWarp.Approach(_steerApplied, _traversalActive ? 0f : _steerTarget,
                    SteerRateDegreesPerSecond, Mathf.Max(Time.DeltaTime, 0.0001f));
                if (Mathf.Abs(_steerApplied) > 1e-4f)
                    GaspPoseWarp.RotateYAbout(_componentPose, _skel.Pelvis, _steerApplied);

                if (EnableFootPlacement && !_traversalActive && _phase == AirPhase.Ground)
                    ApplyFootPlacement();

                _model.SetCurrentPose(_componentPose, false);
                _applyCount++;
                _query.SetAppliedComponentPose(_componentPose, Mathf.Max(Time.DeltaTime, 0.0001f));
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                if (_applyCount < 3)
                    Debug.LogWarning("[GaspAnimator] 姿态写回失败: " + ex);
            }
            finally
            {
                _inside = false;
            }
        }

        public override void OnDestroy()
        {
            ShutdownRuntime();
            base.OnDestroy();
        }

        private void ShutdownRuntime()
        {
            if (_subscribed && _model)
            {
                _model.AnimationUpdated -= OnAnimationUpdated;
                MetaHumanClipFrameFix.Attach(_model);
            }
            _subscribed = false;
            _ready = false;
            _asset = null;
            _model = null;
            _matcher = null;
            _blend = null;
            _query = null;
            if (_legacy != null)
                _legacy.Enabled = true;
            LostModelCount++;
        }

        public int LostModelCount { get; private set; }

        public GaspAnimationDatabase Database => _db;
        public GaspMotionDatabase MotionDatabase => _motionDb;
        public GaspSkeleton Skeleton => _skel;
        public GaspMotionMatcher Matcher => _matcher;
        public GaspPoseBuffer BlendedPose => _blended;
        public bool IsTraversalActive => _traversalActive;

        /// <summary>翻越：由 MGaspTraversalDriver 在选好 Mantle 剪辑后调用，暂停普通 MM。</summary>
        public bool BeginTraversal(int clipIndex, float fadeIn = 0.10f, float fadeOut = 0.20f)
        {
            if (!_ready || clipIndex < 0 || clipIndex >= _db.Clips.Count)
                return false;
            _traversalActive = true;
            _restIdle = false;
            _phase = AirPhase.Ground;
            var clip = _db.Clips[clipIndex];
            LastMode = "Traversal";
            LastClipName = clip.Name;
            Debug.Log($"[GaspAnimator] BeginTraversal clip={clip.Name} len={clip.Length:F2}");
            _blend.Play(clipIndex, 0f, false, fadeIn, fadeOut);
            return true;
        }

        /// <summary>翻越结束：恢复普通 MM 检索。</summary>
        public void EndTraversal()
        {
            if (!_traversalActive)
                return;
            Debug.Log("[GaspAnimator] EndTraversal");
            _traversalActive = false;
            _matcher.HasCurrent = false;
            _phase = AirPhase.Ground;
            LastMode = "MM";
        }

        private static Vector3 RotateY(Vector3 v, float radians)
        {
            float c = Mathf.Cos(radians);
            float s = Mathf.Sin(radians);
            return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }
    }
}
