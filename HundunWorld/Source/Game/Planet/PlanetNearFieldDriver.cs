using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using FlaxEngine;
using Horizon.Planet;
using Horizon.Planet.Dem;

namespace HundunWorld.Game.Planet
{
    /// <summary>
    /// 近景流送的<b>驱动层</b>：每帧 <c>BuildPlan</c> → 缺的块投给 <see cref="PlanetPatchPump"/> 生成 →
    /// <c>TryDequeueCompleted</c> 取件 → 投给 <see cref="PlanetTerrainUploader"/> 落引擎 →
    /// 从 uploader 取回已完成件做账（MarkReady / 还缓冲池 / 换区时 RemovePatch）。
    /// <para>
    /// <b>改 patch 表这一步只能在主线程</b>。§13.15 量到 <c>AddPatch</c>+<c>SetupPatchHeightMap</c>
    /// 是 20–22 ms/块，所以 v6–v8 把它挪到 uploader 的专属线程上，主线程帧长确实降到与不上传时逐次全同
    /// （§13.25）。但 §13.35 的 A/B 证明那条路不能要：同一场景、同一参数，只换"上传在哪条线程"这一个开关，
    /// 后台线程那臂 2/2 崩在 <c>GlobalSignDistanceFieldPass::Render</c>（新编辑器会话的第一次 PIE 也崩，
    /// 距 Enter 2.5 s、才写进 6 块），主线程那臂 2/2 跑完 920 帧、440 次上传 + 270 次卸载、换区 3 次、passed=1。
    /// 所以默认 <see cref="UploadsInline"/>＝true，帧预算改由 <see cref="LoadsPerFrame"/> 管：
    /// 它现在重新变成"每帧往引擎写几块"，一块 ≈20 ms。
    /// </para>
    /// <para>
    /// 生成（采样 + 落盘）仍然在后台：<see cref="PlanetPatchPump"/> 的 worker 不碰引擎，实测把
    /// 178.9 ms/块 的生成成本挡在主线程之外，这一半收益是稳的。
    /// </para>
    /// <para>它同时是一台测量仪：回执里既有"每帧被占掉多少"（驱动自己的耗时 + 引擎报的帧时长），
    /// 也有"投出去到落地隔了几帧"，还有泵与 uploader 两套对账等式、缓存命中数。
    /// 策略参数全部是公开字段，可以由编辑器侧的脚本桥改，不用重编。</para>
    /// <para>触发：存在 <c>&lt;Project&gt;/Saved/planet_near_field.on</c>，内容
    /// <c>"&lt;lat&gt; &lt;lon&gt; [loadsPerFrame] [collisionLod] [stepFrames] [maxFrames] [dem目录]"</c>，
    /// 缺省用解析斜面（不吃读盘，量的是流送机制本身）。跑完吃标记、写
    /// <c>planet_near_field.result.txt</c>。</para>
    /// </summary>
    public class PlanetNearFieldDriver : Script
    {
        private const string BuildTag = "near_field_driver_v18";
        private const string MarkerName = "planet_near_field.on";
        private const string ResultName = "planet_near_field.result.txt";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---- 策略参数（可被脚本桥改）----
        public double LatDeg = 30.0;
        public double LonDeg = 120.0;
        public int LoadsPerFrame = 1;
        public int CollisionLod = -1;
        public int StepFrames = 30;
        public int StepsPerRegion = 6;
        public int MaxFrames = 900;
        public int UploadQueueCap = 6;
        /// <summary>
        /// 上传跑在哪条线程。<b>默认 true＝主线程</b>：§13.35 的 A/B 里，同一场景同一参数只换这一个开关，
        /// 后台线程那臂 2/2 崩在 GlobalSignDistanceFieldPass::Render（新会话的第一次 PIE 也崩），
        /// 主线程那臂 2/2 跑完 900 帧、440 次上传 + 270 次卸载、换区 3 次、passed=1。
        /// 设成 false 只为复现那个崩溃，回执里会专门标一行。
        /// </summary>
        public bool UploadsInline = true;
        public bool UseRealDem = false;
        public string DemDir = "";

        /// <summary>
        /// 换区上行用哪个角色号。<b>默认 0＝不发</b>：0 在本项目里表示"还没进世界"，
        /// <see cref="PlanetRegionTransferRules"/> 会照实拦下并在回执里写"角色号为 0"。
        /// <para>这不是偷懒而是刻意的：让这条链在没有真实角色号时静默不发，比让它拿着一个假号
        /// 去改动服务端归属好得多。真实角色号由登录那一方给（或在这里填一个，用于双客户端验收）。</para>
        /// </summary>
        public ulong TransferCharacterId = 0UL;

        /// <summary>
        /// 换区上行取"角色在哪"的口径：<c>probe</c>（默认）= 只用角色的真实位置做一次<em>对照打印</em>，
        /// 取样仍走这条自测自己的合成走查；<c>flax</c> / <c>ecs</c> = 明说按哪一种轴序口径把角色位置喂给上行。
        /// <para>为什么默认不改行为：仓里"换 Y/Z"这件事已经有六处各写一遍，而星球这套局部帧
        /// (<c>X=东、Y=上、Z=南</c>) 是第三种顺序。角色到底按哪一种摆在这块地面上，今天<em>没有任何证据</em>——
        /// 静默挑一种的结果是上行带一个公里级偏差的点出门，而那条链不会报错，只会拒绝。
        /// 所以默认只把两种口径各自的离地高度与反查区域打进回执（<c>actor_pose</c> 那行），
        /// 让实机那一次把口径<em>读</em>出来；读出来之后 marker 里写 <c>pose=flax</c> 或 <c>pose=ecs</c> 才切换。</para>
        /// <para>判定与置换本体不在这里：<see cref="PlanetActorPoseRules"/> 是全客户端唯一允许写这个换轴的地方
        /// （判据 <c>PlanetActorPoseRulesTests.驱动按这条探针取样_且不许在引擎那一侧再手写一份置换</c>）。</para>
        /// </summary>
        public string PoseMode = "probe";

        /// <summary>marker 写 <c>bind=1</c> 时，Start 末尾向服务端发一条落位请求。
        /// 无登录验收只能靠它把"客户端真发一条"接上；失败不阻断自测，但一定要进回执。</summary>
        private bool _bindOnStart;

        /// <summary>marker 写 <c>terrain_at=x,y,z</c> 时，<b>自己新建</b>一块 Terrain 并放到这个 Flax 世界坐标，
        /// 不去复用场景里已有的 Terrain——世界场景里那块是产品地形，自测不许改写它。</summary>
        private bool _hasTerrainAt;
        private Vector3 _terrainAt;

        // ---- 运行态 ----
        private readonly List<string> _log = new List<string>();
        private readonly Dictionary<PatchKey, int> _requestFrame = new Dictionary<PatchKey, int>();
        private readonly List<Int2> _engineResident = new List<Int2>();

        private Terrain _terrain;
        private bool _engineTerrainOwned;
        private PlanetTerrainUploader _uploader;
        private PlanetTerrainStreamer _streamer;
        private PlanetPatchPump _pump;
        private PlanetPatchCache _cache;
        private PlanetTerrain _planetTerrain;
        private PlanetHeightField _field;
        private RegionFrame _frame;
        private int _edge;
        private float _patchSizeCm;
        private double _localX, _localZ;
        private RegionFrame _pendingFrame;
        private PlanetVector3 _pendingLocal;
        private bool _swapPending;
        private int _swapFrames, _swapStartFrame, _unloadSubmitted, _queueOverflow;
        private int _staleUploadsDuringSwap, _swapRepairSubmitted;
        private int _frameNo, _steps, _crossings, _badCrossings, _edgeCrossings, _diagonalCrossings;
        private bool _started, _finished, _settling;
        private const int SettleLimitFrames = 400;
        private string _markerPath;

        // ---- 统计 ----
        private int _uploaded, _unloaded, _uploadFailures, _requestsRejected;
        private double _uploadSumMs, _uploadMaxMs, _workSumMs, _workMaxMs;
        private double _frameSumMs, _frameMaxMs;
        private long _lagSumFrames; private int _lagMaxFrames, _lagSamples;
        private int _unsatisfiedFrames, _worstReadyInWindow;

        public override void OnStart()
        {
            _markerPath = MarkerPath();
            if (_markerPath == null) return;
            try
            {
                ParseMarker(_markerPath);
                Start();
            }
            catch (Exception e)
            {
                Say("crash_type=" + e.GetType().Name);
                Say("crash_message=" + OneLine(truncated(e.Message, 200)));
                Say("crash_stack=" + OneLine(FirstFrames(e.StackTrace, 3)));
                Finish(false);
            }
        }

        public override void OnUpdate()
        {
            if (!_started || _finished) return;
            var sw = Stopwatch.StartNew();
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                Say("tick_crash_type=" + e.GetType().Name);
                Say("tick_crash_message=" + OneLine(truncated(e.Message, 200)));
                sw.Stop();
                Finish(false);
                return;
            }
            sw.Stop();
            _workSumMs += sw.Elapsed.TotalMilliseconds;
            _workMaxMs = Math.Max(_workMaxMs, sw.Elapsed.TotalMilliseconds);
            double frameMs = Time.DeltaTime * 1000.0;
            _frameSumMs += frameMs;
            _frameMaxMs = Math.Max(_frameMaxMs, frameMs);

            if (_frameNo >= MaxFrames && !_settling)
            {
                _settling = true;
                Say(string.Format(Inv, "settling at_frame={0} (等投出去的落地、窗口装满，再验)", _frameNo));
            }
            if (_settling && _frameNo > MaxFrames + SettleLimitFrames)
            {
                Say("settle_timeout=1");
                Finish(VerifyAndReport());
                return;
            }
            if (_settling && _frameNo % 5 == 0 && Quiescent()) Finish(VerifyAndReport());
        }

        // ==================== 装配 ====================

        private void Start()
        {
            Say("build_tag=" + BuildTag);
            // 静态账倒回干净状态，排在一切装配之前：脚本装配跨 PIE 会话存活，上一轮留下的在飞标记
            // 会把这一轮第一次换区挡掉，上一轮的 ack/地面探针更会拿着已经作废的东西用。
            // 这行 Say 是它在实机上真跑过的唯一证据，回执文件里读得到。
            PlanetRegionTransfers.BeginSession();
            Say("session_reset=clean " + PlanetRegionTransfers.Tracker.Describe());
            Say(string.Format(Inv, "policy loads_per_frame={0} collision_lod={1} step_frames={2} steps_per_region={3} max_frames={4} queue_cap={5} uploads_inline={6}",
                             LoadsPerFrame, CollisionLod, StepFrames, StepsPerRegion, MaxFrames, UploadQueueCap,
                             UploadsInline ? 1 : 0));
            Say(string.Format(Inv, "request_lat_lon={0:0.####}/{1:0.####} height_source={2}",
                             LatDeg, LonDeg, UseRealDem ? "real_gebco" : "analytic_ramp"));
            // 换区上行的两条前置条件先亮出来：角色号与会话。缺任何一个，之后每一条
            // "换区上行跳过/发不出去"都是它们的下游症状，回执里没有这两行就只能猜。
            // 会话与角色号都取"当下那一个"而不是此刻的快照：重连会换 NetworkManager，登录之后才有角色号。
            // 存快照的后果是每条上行都带旧号，而那正是 §13.53 取样顺序那一类病——判定照跑、屏幕上什么都没有。
            PlanetRegionTransfers.SessionProvider = () => HundunWorldGame.Instance?.NetworkManager;
            // 显式号优先：双客户端验收时这里填的就是"我要用这个号"，不能被会话里那个 0（还没登录）顶掉；
            // 反过来，没填就用会话当下的号——两者都不是"启动那一刻的快照"。
            PlanetRegionTransfers.CharacterIdProvider = () => TransferCharacterId != 0UL
                ? TransferCharacterId
                : HundunWorldGame.Instance?.NetworkManager?.CharacterId ?? 0UL;

            Say(string.Format(Inv, "transfer_policy explicit_character_id={0} session_hooked={1}",
                             TransferCharacterId, 1));
            // 重力取值进回执：它是产品决定而不是几何推论，所以每次跑都要能看见"这一轮用的是哪一档"，
            // 以及它与两个物理候选差多少倍——手感出了问题时，第一个要被排除的就是这里。
            Say("gravity " + PlanetGravity.DescribeAgainstPhysics(PlanetConstants.PlanetRadiusCm));


            if (_hasTerrainAt)
            {
                // 自建并归位：世界场景里 Actor.FindActor<Terrain> 会拿到产品地形，改写它等于破坏场景
                _terrain = new Terrain { Name = "PlanetNearFieldTerrain" };
                _terrain.Parent = Actor;
                _terrain.Position = _terrainAt;
                Say(string.Format(Inv, "terrain_created_by_script=1 terrain_at={0:0.#},{1:0.#},{2:0.#}",
                                  _terrainAt.X, _terrainAt.Y, _terrainAt.Z));
                _engineTerrainOwned = true;
            }
            else
            _terrain = Actor.FindActor<Terrain>(false);
            if (_terrain == null)
            {
                var created = new Terrain { Name = "PlanetNearFieldTerrain" };
                created.Parent = Actor.Parent != null ? Actor.Parent : Actor;
                _terrain = created;
                Say("terrain_created_by_script=1");
            }
            _terrain.Setup(6, 127);
            _terrain.CollisionLOD = CollisionLod;
            _edge = _terrain.HeightmapSize;
            _patchSizeCm = _terrain.PatchSize;
            Say(string.Format(Inv, "engine edge={0} patch_size_cm={1:0.#}", _edge, _patchSizeCm));

            _frame = RegionFrame.Build(RegionKey.FromDirection(
                Geodetic.DirectionFromLatLon(LatDeg * Math.PI / 180.0, LonDeg * Math.PI / 180.0),
                PlanetConstants.DefaultRegionLevel), PlanetConstants.PlanetRadiusCm);

            IHeightSource source;
            HeightSampler sampler = null;
            if (UseRealDem && !string.IsNullOrEmpty(DemDir) && Directory.Exists(DemDir))
            {
                // 装配只在这一处：manifest → grid → profile → hybrid → sampler 的顺序由 PlanetHeightField 定，
                // 服务端的地面来源（DemPlanetGroundSource）走的是同一个入口。这里再手拼一遍，
                // 就是"两边各算一份海拔"的开工方式——而那不报错，只让人站在地面下面。
                if (!PlanetHeightField.TryCreateFromDirectory(DemDir, out var field, out string fieldWhy))
                {
                    Say("height_field_error=" + OneLine(fieldWhy));
                    // 原因同时写进 CaliberStatus：回执行（PlanetRegionTransfers.Describe）会把它念出来，
                    // 于是"没口径所以不发"在两个读数里是同一条理由，不是各说各话
                    PlanetRegionTransfers.MarkCaliberMissing(fieldWhy);
                    UseRealDem = false;
                    source = new EastSouthRamp(_frame.East, _frame.South);
                }
                else
                {
                    _field = field;
                    var manifest = field.Manifest;
                    sampler = field.Sampler;
                    source = sampler.Hybrid;
                    Say(string.Format(Inv, "dem_loaded levels={0} content_seed={1:X16} terrain_version={2:X8}",
                                     manifest.Levels.Count, manifest.ContentSeed, field.Caliber.TerrainVersion));
                    // 口径要在这里装上：换区上行带的就是它（§6.2）。装不上时 PlanetRegionTransfers 里的
                    // Caliber 保持默认值，而那条链会"拒发并说明原因"——不会静默发一条没握手的包出去。
                    PlanetRegionTransfers.SetCaliber(field.Caliber, DemDir);
                    Say("planet_caliber " + PlanetRegionTransfers.CaliberStatus);
                    // 地面探针接的是同一份高度场：换区上行前用它把地下点抬到地面上，
                    // 于是服务端那条穿模判定在正常数据下不该被触发（触发了就是两端读的数据不同）。
                    PlanetRegionTransfers.GroundElevationCm = p =>
                        _field is { } f && f.TryGetGroundElevationCm(p, out double elev) ? elev : (double?)null;
                    Say("ground_probe on");
                    _cache = new PlanetPatchCache(
                        Path.Combine(Globals.ProjectFolder, "Saved", "planet_patch_cache"),
                        manifest.ContentSeed, field.Caliber.TerrainVersion);
                }
            }
            else
            {
                source = new EastSouthRamp(_frame.East, _frame.South);
                _cache = new PlanetPatchCache(
                    Path.Combine(Globals.ProjectFolder, "Saved", "planet_patch_cache_ramp"),
                    0x05EED123456789ABL, 0x1234ABCD);
            }

            _planetTerrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, source);
            _streamer = new PlanetTerrainStreamer(_frame, _planetTerrain, _cache, sampler);
            _pump = new PlanetPatchPump(_frame, _planetTerrain, _cache, sampler, maxWorkers: 2);
            _uploader = new PlanetTerrainUploader(new TerrainUploadTarget(_terrain),
                                                  capacity: UploadQueueCap, runInline: UploadsInline);
            Say(string.Format(Inv, "uploader mode={0} capacity={1}",
                              UploadsInline ? "inline(主线程)" : "thread(专属后台线程)", UploadQueueCap));
            if (!UploadsInline)
                Say("uploader_mode_warning=后台线程改 patch 表已实测与渲染线程竞争并崩溃(§13.35)，此项只为复现");
            Say(string.Format(Inv, "region={0} window_edge={1}", _frame.Key, _streamer.WindowEdge));

            _localX = 0; _localZ = 0;
            ProbeActorPose("start");
            if (_bindOnStart)
            {
                var bindPoint = _frame.GlobalFromLocalCm(PlanetVector3.Zero);
                // 落位的 tick 用挂钟毫秒：帧号每次会话从 0 重来，而 grain 对同一角色判 tick 倒退，
                // 于是同一角色第二次进世界会被永远拒（"一直在重连"的另一个来源）。
                long bindTick = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                _ = PlanetRegionTransfers.SendBindAsync(bindPoint, bindTick);
                Say("bind_request " + PlanetRegionTransfers.DescribeBind());
            }
            Prime();
            _started = true;
        }

        /// <summary>开局把整窗投出去：第一帧就该看到"计划里 25 块全缺"。</summary>
        private void Prime()
        {
            var plan = _streamer.BuildPlan(_localX, _localZ);
            Say(string.Format(Inv, "prime: to_load={0} to_unload={1} budget={2}", plan.ToLoad.Count, plan.ToUnload.Count, plan.Budget));
            foreach (var key in plan.ToLoad) RequestQuietly(key);
        }

        private void RequestQuietly(PatchKey key)
        {
            if (_requestFrame.ContainsKey(key)) return;              // 已经投过或正在排队，别重复投
            _requestFrame[key] = _frameNo;
            if (!_pump.Request(key))
            {
                _requestsRejected++;
                _requestFrame.Remove(key);
            }
        }

        // ==================== 每帧 ====================

        private void Tick()
        {
            _frameNo++;

            if (StepFrames > 0 && _frameNo % StepFrames == 0) AdvancePlayer();

            var plan = _streamer.BuildPlan(_localX, _localZ);
            if (plan.ReadyInWindow > _worstReadyInWindow) _worstReadyInWindow = plan.ReadyInWindow;
            if (!plan.IsSatisfied) _unsatisfiedFrames++;

            // 取件必须每帧都做，换区等待期间也不例外：IsIdle 里含"取件口已空"，
            // 若等空闲的那条路径上不取件，就永远等不到空闲（v6 第一跑就是这么卡死的：
            // swap_begin 每 180 帧重复一次、crossings 一直是 0、queue_overflow 33）。
            DrainCompletions();

            if (_swapPending)
            {
                // 换区进行中：等唯一那条上传线程把旧 patch 摘干净，再改流送的坐标系。
                // 主线程这期间既不写 patch 表，也不读它——读到的会是写到一半的地形。
                if (_uploader.IsIdle) CompleteSwap();
                return;
            }

            // 出窗：卸载也投给那条上传线程，然后告诉流送它不在了
            foreach (var key in plan.ToUnload)
            {
                if (_uploader.TrySubmitRemove(key, _frame.Key)) _unloadSubmitted++;
                else _queueOverflow++;                    // 投不进就下帧再试：流送会再列一次 ToUnload
                _streamer.MarkUnloaded(key);
            }

            foreach (var key in plan.ToLoad) RequestQuietly(key);

            int submittedThisFrame = 0;
            while (submittedThisFrame < LoadsPerFrame && _pump.TryDequeueCompleted(out var resolved))
            {
                submittedThisFrame++;
                if (_uploader.TrySubmit(resolved.Key, resolved.Heights, resolved.Region)) continue;
                // uploader 排不下就把成品退回池子（它下帧会被重新投出去）：宁可重算，也不无界占内存
                _pump.Release(resolved.Heights);
                _requestFrame.Remove(resolved.Key);
                _queueOverflow++;
            }

        }

        /// <summary>取件口每帧清空：这里只有账要记、没有引擎调用，所以一次取完。</summary>
        private void DrainCompletions()
        {
            while (_uploader.TryDequeueCompleted(out var done))
            {
                if (done.Failed) _uploadFailures++;
                if (_swapPending && !done.IsRemove && !done.Stale)
                {
                    // 换区已经投过卸载、但这件上传是在那之后才落进引擎的：它不在那批卸载里，
                    // 直接记账成一个"驻留"的旧区域 patch，换区收尾后就再没人摘它——引擎里会留一片
                    // 永远不属于新坐标系的 patch。所以这里补投一次卸载，并且不 MarkReady。
                    if (!_uploader.TrySubmitRemove(done.Key, done.Region)) _queueOverflow++;
                    else _swapRepairSubmitted++;
                    _staleUploadsDuringSwap++;
                    if (done.Heights != null) _pump.Release(done.Heights);
                    continue;
                }
                if (done.IsRemove)
                {
                    var c = new Int2(done.Key.X, done.Key.Z);
                    _engineResident.RemoveAll(v => v.X == c.X && v.Y == c.Y);
                    _unloaded++;
                }
                else
                {
                    // 同一个坐标只能驻留一次：窗口在换区后复用同一批整数坐标，先去重再记，
                    // 否则这张账会变成"50 块"而引擎里其实只有 25 块（实测过），换区时就会多投一倍卸载。
                    var resident = new Int2(done.Key.X, done.Key.Z);
                    _engineResident.RemoveAll(v => v.X == resident.X && v.Y == resident.Y);
                    _engineResident.Add(resident);
                    _streamer.MarkReady(done.Key);
                    _uploaded++;
                    _uploadSumMs += done.Ms;
                    _uploadMaxMs = Math.Max(_uploadMaxMs, done.Ms);
                    if (_requestFrame.TryGetValue(done.Key, out int at))
                    {
                        long lag = _frameNo - at;
                        _lagSumFrames += lag;
                        _lagSamples++;
                        if (lag > _lagMaxFrames) _lagMaxFrames = (int)lag;
                        _requestFrame.Remove(done.Key);
                    }
                }
                if (done.Heights != null) _pump.Release(done.Heights);
            }
        }


        /// <summary>把玩家沿局部 +X 推进一步；走满 <see cref="StepsPerRegion"/> 步就换区。</summary>
        /// <summary>计划满足、泵排空、取件口空——这一刻才适合做"最后一窗对不对"的验收。</summary>
        private bool Quiescent()
        {
            if (_pump.PendingCount != 0 || _pump.InFlightCount != 0) return false;
            if (_swapPending || !_uploader.IsIdle) return false;
            return _streamer.BuildPlan(_localX, _localZ).IsSatisfied;
        }

        private void AdvancePlayer()
        {
            if (_swapPending) return;      // 坐标系正在换手的这几帧里不许再推进玩家，否则会连着投第二次换区
            _steps++;
            _localX += _patchSizeCm;

            // 边界预取（§6.3 第 1 步）：还没跨过去就得先问一次邻区清单。
            // 放在这个位置是因为它要在"要不要换区"的判断之前跑——那一层下面有个 early return。
            MaybeRequestWatch(crossedRegionId: 0);

            if (_steps % Math.Max(1, StepsPerRegion) != 0) return;

            // 换区：先拿"这一步的全局点"，再按新区域重算局部坐标（否则切平面基变了，坐标会整体错位）
            var globalCm = _frame.GlobalFromLocalCm(new PlanetVector3(_localX, 0, _localZ));
            var dir = globalCm / (PlanetConstants.PlanetRadiusCm + 100_000.0);   // 归一到单位方向（高度只当校验用）
            // 局部 +X 是这一面的"东"，而 cube 面的 uv 轴向六个面各不相同：按固定 UPlus 取邻居
            // 会在某些面上走到玩家根本没去的那一块（v1 实测换区后局部坐标凭空多出一整个区域宽）。
            // 正确做法与 §13.9 走查一致：拿全局点反查真正所在的区域，再用共边关系校验它确实是邻家。
            var nextKey = RegionKey.FromDirection(dir, _frame.Key.Level);
            if (nextKey == _frame.Key) return;                    // 还没真的走出去：不该换区

            // 共边校验属于服务端（一次输入只能跨一格）；客户端换基必须无条件跟到真正所在的区域，
            // 因为沿局部东向走会合法地同时对角换格（回归判据见 PlanetRegionWalkGeometryTests）。
            // 这里该守的是"一次检查最多跨一格"——跨到两格以外才说明检查间隔太粗。
            int reach = Math.Max(Math.Abs(nextKey.X - _frame.Key.X), Math.Abs(nextKey.Y - _frame.Key.Y));
            bool diagonal = nextKey.X != _frame.Key.X && nextKey.Y != _frame.Key.Y;
            if (reach > 1) _badCrossings++;
            else if (diagonal) _diagonalCrossings++;
            else _edgeCrossings++;
            if (reach > 1)
                Say(string.Format(Inv, "  crossing_multi_tile from={0} to={1} reach={2} (检查间隔比区域还宽)", _frame.Key, nextKey, reach));

            var nextFrame = RegionFrame.Build(nextKey, PlanetConstants.PlanetRadiusCm);
            var newLocal = nextFrame.LocalFromGlobalCm(globalCm);

            // 换区第一步（不阻塞）：泵先抬代次，旧区域的在飞结果一律不许落地；
            // 旧 patch 的卸载投给那条上传线程。主线程不再为换区一次付掉 81 次 RemovePatch。
            _pump.BeginRegion(nextFrame);
            int dropped = _engineResident.Count;
            foreach (var coord in _engineResident)
            {
                if (!_uploader.TrySubmitRemove(new PatchKey(coord.X, coord.Y), _frame.Key)) _queueOverflow++;
            }
            _engineResident.Clear();
            _requestFrame.Clear();

            _pendingFrame = nextFrame;
            _pendingLocal = newLocal;
            _swapPending = true;
            _swapStartFrame = _frameNo;
            Say(string.Format(Inv, "  swap_begin at_frame={0} into={1} drop_submitted={2}",
                             _frameNo, nextFrame.Key, dropped));
            WriteReceipt();
        }

        /// <summary>
        /// 问一问快照。<paramref name="crossedRegionId"/> 为 0 表示"只是贴到边界上了"。
        /// 预取带用库里的区域半边长算，客户端不许自己写一份"区域有多大"——那会变成第二套尺寸口径。
        /// </summary>
        private void MaybeRequestWatch(long crossedRegionId)
        {
            var (halfX, halfZ) = PlanetRegionAoi.LocalHalfExtents(_frame.Key, PlanetConstants.PlanetRadiusCm);
            bool inBand = PlanetRegionWatchRules.WithinPrefetchMargin(_localX, _localZ, halfX, halfZ);
            _ = PlanetRegionWatches.TryRequestAsync(crossedRegionId, inBand);
        }

        /// <summary>换区第二步：上传线程排空之后才真的换坐标系（这时 patch 表是干净的，也没人在写）。</summary>
        private void CompleteSwap()
        {
            var sw = Stopwatch.StartNew();
            var fromKey = _frame.Key;                     // 必须在 _frame 被替换之前取：之后就没有"旧区"了
            var crossedGlobalCm = _pendingFrame.GlobalFromLocalCm(_pendingLocal);
            _uploader.BeginRegion();          // 队列已空，这里只是把"旧坐标不许再落地"钉成代次
            _streamer.AttachRegion(_pendingFrame);
            _frame = _pendingFrame;
            _localX = _pendingLocal.X;
            _localZ = _pendingLocal.Z;
            _swapPending = false;
            _crossings++;
            _swapFrames = _frameNo - _swapStartFrame;
            sw.Stop();

            // 换区上行（§6.2 / §6.3 的第 2 步）。发在这里而不是 BeginSwap 那一侧的理由：
            // 那一刻坐标系还没换、局部点还是旧帧算的；服务端要的是"我此刻真在新块里的全局点"。
            // 不 await：这条链有自己的在飞账与超时（PlanetRegionTransferTracker），
            // 让换区的完成等一趟网络往返，等于把 §13.26 那条"每帧预算"交给网络延迟决定。
            // 取点走 PoseSendPoint：默认（probe）还是这个合成点，只有 marker 明说口径才换成角色位置。
            ProbeActorPose("crossing");
            // tick 用挂钟毫秒、与落位同一时钟：帧号每次会话从 0 重来，而 grain 对同一角色判 tick 倒退，
            // 于是"先落位（tick≈6.4e14）再发帧号（tick≈361）"必然被判 TickRegression（实测：1 条被拒 + 3 次自挡）。
            _ = PlanetRegionTransfers.TrySendAsync(fromKey, PoseSendPoint(crossedGlobalCm, "crossing"),
                                                   PlanetRegionTransfers.NowMsClockTick());

            // 换区之后订阅集作废了（旧区变成邻区、新区的另一侧进了视野），立刻再问一趟。
            // 这一条绕过节流：规则层把"刚跨过区"与"边界附近重复问"分开处理，正是为了这里。
            PlanetRegionWatches.NoteCrossed(_frame.Key.ToId());
            MaybeRequestWatch(_frame.Key.ToId());

            Say(string.Format(Inv, "  crossing={0} at_frame={1} into={2} local_now={3:0.#},{4:0.#} swap_wait_frames={5} attach_ms={6:0.###}",
                             _crossings, _frameNo, _frame.Key, _localX, _localZ, _swapFrames, sw.Elapsed.TotalMilliseconds));
            WriteReceipt();
        }

        // ==================== 角色位姿口径 ====================

        /// <summary>角色相对这块近景地面锚点的三元数（厘米）。<b>锚点用 Terrain 自己的位置</b>：
        /// 星球局部帧的原点在这儿，不在 Flax 世界原点——减掉锚点之前，三个数描述的是"离场景原点多远"，
        /// 而我们要的是"离脚下这块切平面中心多远"。</summary>
        private bool TryActorPoseTriple(out double x, out double y, out double z, out string why)
        {
            x = y = z = 0;
            why = "";
            var actor = HundunWorldGame.Instance?.LocalPlayerActor;
            if (actor == null) { why = "no_local_actor（还没进世界，或这条链跑在没有本地角色的场景里）"; return false; }
            if (_field is null) { why = "no_ground（没有基场就没有<em>离地高度</em>这个参照，两种口径分不开）"; return false; }
            var ap = actor.Position;
            var anchor = _terrain != null ? _terrain.Position : Vector3.Zero;
            x = ap.X - anchor.X; y = ap.Y - anchor.Y; z = ap.Z - anchor.Z;
            return true;
        }

        /// <summary>把两种口径各自的读数打进回执。<b>这一步不改任何取样</b>。</summary>
        private void ProbeActorPose(string at)
        {
            if (!TryActorPoseTriple(out double x, out double y, out double z, out string why))
            {
                Say("actor_pose at=" + at + " skipped=" + why);
                return;
            }
            if (!_field.TryGetGroundElevationCm(_frame.OriginCm, out double groundCm))
            {
                Say("actor_pose at=" + at + " skipped=地面海拔取不出来（帧原点那一点）");
                return;
            }
            var p = PlanetActorPoseRules.Decide(x, y, z, _frame, groundCm);
            Say(string.Format(Inv,
                "actor_pose at={0} pose_mode={1} raw_actor={2:0.#},{3:0.#},{4:0.#} ground_cm={5:0.###} {6}",
                at, PoseMode, x, y, z, groundCm, p));
        }

        /// <summary>
        /// 上行取点的唯一出口：marker 里明说 <c>pose=flax</c> / <c>pose=ecs</c> 才改用角色位置，
        /// 其余一切情况（含 <c>probe</c> 与任何拼错的值）都照旧返回合成走查点，并把"为什么没换"打出来。
        /// <para>这里宁可啰嗦也不能静默：口径选错的症状是"角色在地下两公里"，
        /// 而那条链上的每一格读数看着都合法。</para>
        /// </summary>
        private PlanetVector3 PoseSendPoint(PlanetVector3 synthetic, string at)
        {
            if (PoseMode != "flax" && PoseMode != "ecs") return synthetic;
            if (!TryActorPoseTriple(out double x, out double y, out double z, out string why))
            {
                Say("actor_pose_send=" + PoseMode + " at=" + at + " skipped=" + why + " 仍用合成走查点");
                return synthetic;
            }
            var conv = PoseMode == "flax"
                ? PlanetActorPoseRules.AxisConvention.FlaxYUp
                : PlanetActorPoseRules.AxisConvention.EcsZUp;
            PlanetVector3 global = _frame.GlobalFromLocalCm(PlanetActorPoseRules.ToLocalCm(x, y, z, conv));
            Say(string.Format(Inv, "actor_pose_send={0} at={1} raw={2:0.#},{3:0.#},{4:0.#} region={5}",
                              PoseMode, at, x, y, z, PlanetActorPoseRules.RegionOf(
                                  PlanetActorPoseRules.ToLocalCm(x, y, z, conv), _frame)));
            return global;
        }

        // ==================== 收尾 ====================

        private bool VerifyAndReport()
        {
            Say(string.Format(Inv,
                "frames={0} steps={1} crossings={2} uploaded={3} unloaded={4} upload_failures={5} requests_rejected={6} queue_overflow={7}",
                _frameNo, _steps, _crossings, _uploaded, _unloaded, _uploadFailures, _requestsRejected, _queueOverflow));
            Say(string.Format(Inv, "upload_ms avg={0:0.###} max={1:0.###} | driver_work_ms avg={2:0.####} max={3:0.####} | engine_frame_ms avg={4:0.###} max={5:0.###}",
                             _uploadSumMs / Math.Max(1, _uploaded), _uploadMaxMs,
                             _workSumMs / Math.Max(1, _frameNo), _workMaxMs,
                             _frameSumMs / Math.Max(1, _frameNo), _frameMaxMs));
            Say(string.Format(Inv, "plan_lag_frames avg={0:0.##} max={1} samples={2} | unsatisfied_frames={3}/{4} worst_ready_in_window={5}",
                             _lagSamples == 0 ? 0 : (double)_lagSumFrames / _lagSamples, _lagMaxFrames, _lagSamples,
                             _unsatisfiedFrames, _frameNo, _worstReadyInWindow));

            Say("uploader " + _uploader.Describe());
            Say(string.Format(Inv, "uploader_equation submitted={0} balanced={1} unload_submitted={2} unloaded={3} swaps={4}",
                             _uploader.Submitted, Bit(_uploader.EquationBalanced), _unloadSubmitted, _unloaded, _crossings));
            Say(string.Format(Inv, "swap_repair stale_uploads_during_swap={0}", _staleUploadsDuringSwap));
            Say("pump " + _pump.Describe());
            // 换区那条链的账（在飞/确认/待重新落位/最近一次 ack）。它不是这条验收的判定项——
            // 没有会话与角色号时这里如实写着"没发出去"，而不是让整条近景验收跟着变红。
            Say("transfers " + PlanetRegionTransfers.Describe());
            Say("bind " + PlanetRegionTransfers.DescribeBind());
            Say("watches " + PlanetRegionWatches.Describe());

            long inflightish = _pump.PendingCount + _pump.InFlightCount;
            Say(string.Format(Inv, "pump_equation requested={0} completed={1} stale={2} duplicate={3} pending_or_inflight={4} balanced={5}",
                             _pump.Requested, _pump.Completed, _pump.StaleDropped, _pump.DuplicatesRejected, inflightish,
                             Bit(_pump.Requested == _pump.Completed + _pump.StaleDropped + _pump.DuplicatesRejected + inflightish)));
            if (_cache != null)
            {
                Say("cache " + _cache.DescribeStats());
                Say(string.Format(Inv, "cache_bytes_used={0} budget={1} over_budget={2}",
                                 _cache.BytesUsed, PlanetPatchCache.DefaultBudgetBytes,
                                 Bit(_cache.BytesUsed > PlanetPatchCache.DefaultBudgetBytes)));
            }
            Say(string.Format(Inv, "crossings_total={0} edge={1} diagonal={2} multi_tile_rejected={3}",
                             _crossings, _edgeCrossings, _diagonalCrossings, _badCrossings));

            // 最后一窗的正确性：引擎里的高程必须与"我写进去的那份"逐格对得上
            var plan = _streamer.BuildPlan(_localX, _localZ);
            int checkedPatches = 0, missing = 0;
            double worstErr = 0;
            var scratch = new float[PlanetPatchSampler.HeightmapLength];
            foreach (var key in plan.DesiredPatches)
            {
                var coord = new Int2(key.X, key.Z);
                if (!_terrain.HasPatch(ref coord)) { missing++; continue; }
                var patch = _terrain.GetPatch(ref coord);
                if (patch == null) { missing++; continue; }
                _streamer.FillPatch(key, scratch, parallel: false);
                worstErr = Math.Max(worstErr, RayErr(patch, scratch, coord));
                checkedPatches++;
            }
            Say(string.Format(Inv, "final_window: desired={0} checked={1} missing={2} worst_ray_err_cm={3:0.####}",
                             plan.DesiredPatches.Count, checkedPatches, missing, worstErr));

            // 驻留真相：patch 表只有引擎说了算。_engineResident / unloaded 是我们的账本，
            // 而 440 次上传每次都吃了 "Cannot add patch ... already exists" 警告——
            // 说明"加了没有"与"摘了没有"都不能由回执里的计数证明，这里直接扫一遍引擎。
            var window = new HashSet<Int2>();
            foreach (var key in plan.DesiredPatches) window.Add(new Int2(key.X, key.Z));
            int present = 0, inWindow = 0, span = _streamer.WindowEdge + 3;
            for (int z = -span; z <= span; z++)
            {
                for (int x = -span; x <= span; x++)
                {
                    var coord = new Int2(x, z);
                    if (!_terrain.HasPatch(ref coord)) continue;
                    present++;
                    if (window.Contains(coord)) inWindow++;
                }
            }
            Say(string.Format(Inv,
                "engine_residency present_in_scanbox={0} of_which_in_window={1}/{2} outside_window={3} " +
                "ledger_resident={4} unload_counted={5} scan_span=+/-{6}",
                present, inWindow, window.Count, present - inWindow, _engineResident.Count, _unloaded, span));

            bool ok = _uploadFailures == 0 && missing == 0 && checkedPatches > 0 && worstErr < 1.0
                      && _uploaded >= plan.DesiredPatches.Count
                      && _uploader.EquationBalanced && _uploader.Failures == 0 && _uploader.MaxInFlight == 1
                      && _staleUploadsDuringSwap == _swapRepairSubmitted
                      && _pump.GenerationFailures == 0 && _pump.CacheWriteFailures == 0
                      && _crossings >= 1 && _badCrossings == 0 && _diagonalCrossings >= 1
                      && _pump.Requested == _pump.Completed + _pump.StaleDropped + _pump.DuplicatesRejected
                                                    + _pump.PendingCount + _pump.InFlightCount;
            Say("driver_verdict=" + (ok ? "NEAR_FIELD_DRIVER_RUNS_IN_ENGINE" : "DRIVER_PROBLEM"));
            // 每个合取分支各自报一条：verdict 为假而 fail_reason 为空，等于把"为什么"整个藏起来
            var why = new List<string>();
            if (_uploadFailures != 0) why.Add("upload_failures=" + _uploadFailures);
            if (missing != 0) why.Add("missing_patches=" + missing);
            if (checkedPatches == 0) why.Add("nothing_checked");
            if (worstErr >= 1.0) why.Add("height_mismatch_cm=" + worstErr.ToString("0.####", Inv));
            if (_uploaded < plan.DesiredPatches.Count) why.Add("uploaded_lt_desired=" + _uploaded);
            if (_staleUploadsDuringSwap != _swapRepairSubmitted) why.Add("swap_repair_not_submitted=" + (_staleUploadsDuringSwap - _swapRepairSubmitted));
            if (!_uploader.EquationBalanced) why.Add("uploader_equation_unbalanced");
            if (_uploader.Failures != 0) why.Add("uploader_failures=" + _uploader.Failures);
            if (_uploader.MaxInFlight != 1) why.Add("uploader_max_inflight=" + _uploader.MaxInFlight);
            if (_pump.GenerationFailures != 0) why.Add("generation_failures=" + _pump.GenerationFailures);
            if (_pump.CacheWriteFailures != 0) why.Add("cache_write_failures=" + _pump.CacheWriteFailures);
            if (_crossings < 1) why.Add("no_region_crossing");
            if (_badCrossings != 0) why.Add("crossings_multi_tile=" + _badCrossings);
            if (_diagonalCrossings < 1) why.Add("no_diagonal_crossing_seen");
            if (_pump.Requested != _pump.Completed + _pump.StaleDropped + _pump.DuplicatesRejected
                              + _pump.PendingCount + _pump.InFlightCount) why.Add("pump_equation_unbalanced");
            // 只在判据为假时报名：PASS 也印一行 fail_reason 会让读回执的人以为"哪里不对但没说"
            if (!ok) Say("fail_reason=" + (why.Count == 0 ? "(none_but_verdict_false)" : string.Join(",", why)));
            WriteReceipt();
            return ok;
        }

        private double RayErr(TerrainPatch patch, float[] field, Int2 coord)
        {
            const float top = 400_000f;
            int[] idx = { 0, _edge / 4, _edge / 2, (3 * _edge) / 4, _edge - 2 };
            double worst = 0;
            foreach (int pi in idx)
                foreach (int pj in idx)
                {
                    var origin = new Vector3(coord.X * _patchSizeCm + pi * 100f, top, coord.Y * _patchSizeCm + pj * 100f);
                    if (!patch.RayCast(origin, new Vector3(0, -1, 0), out float dist, top * 2f)) continue;
                    worst = Math.Max(worst, Math.Abs(top - dist - field[pj * _edge + pi]));
                }
            return worst;
        }

        // ==================== 工具 ====================

        /// <summary>
        /// 引擎侧那两个调用。<b>只会被 uploader 的那一条线程调用</b>——主线程直接动 patch 表
        /// 就等于绕过 §13.16 那条"并发上传会静默写坏高程"的封条。
        /// </summary>
        private sealed class TerrainUploadTarget : IPatchUploadTarget
        {
            private readonly Terrain _terrain;
            public TerrainUploadTarget(Terrain terrain) { _terrain = terrain; }

            public bool Upload(PatchKey key, float[] heights)
            {
                var coord = new Int2(key.X, key.Z);
                _terrain.AddPatch(ref coord);
                return _terrain.SetupPatchHeightMap(ref coord, heights, (byte[])null, true);   // true = 失败
            }

            public void Remove(PatchKey key)
            {
                var coord = new Int2(key.X, key.Z);
                if (_terrain.HasPatch(ref coord)) _terrain.RemovePatch(ref coord);
            }
        }

        private sealed class EastSouthRamp : IHeightSource
        {
            private readonly PlanetVector3 _east, _south;
            public EastSouthRamp(in PlanetVector3 east, in PlanetVector3 south) { _east = east; _south = south; }
            public int SourceVersion => 1;
            public double SampleElevationCm(in PlanetVector3 dir)
                => 12_345
                 + 0.05 * PlanetVector3.Dot(dir, _east) * PlanetConstants.PlanetRadiusCm
                 + 0.02 * PlanetVector3.Dot(dir, _south) * PlanetConstants.PlanetRadiusCm;
        }

        private void ParseMarker(string path)
        {
            string text = "";
            try { text = File.ReadAllText(path).Trim(); } catch (Exception) { }
            string[] parts = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            try
            {
                if (parts.Length >= 2)
                {
                    LatDeg = double.Parse(parts[0], Inv);
                    LonDeg = double.Parse(parts[1], Inv);
                }
                if (parts.Length >= 3) LoadsPerFrame = int.Parse(parts[2], Inv);
                if (parts.Length >= 4) CollisionLod = int.Parse(parts[3], Inv);
                if (parts.Length >= 5) StepFrames = int.Parse(parts[4], Inv);
                if (parts.Length >= 6) MaxFrames = int.Parse(parts[5], Inv);
                if (parts.Length >= 7)
                {
                    DemDir = parts[6];
                    UseRealDem = Directory.Exists(DemDir);
                }
                // A/B 开关：inline=1 让"改 patch 表"回到调用线程（主线程）上跑，其余语义一概不动。
                // 判"后台线程改 patch 表是否安全"必须只改这一个变量，否则结论不成立。
                foreach (string part in parts)
                {
                    if (part.StartsWith("inline=", StringComparison.OrdinalIgnoreCase))
                        UploadsInline = int.Parse(part.Substring(7), Inv) != 0;
                    // 位姿口径的开关。默认不写＝probe：只对照打印，不改取样（见 PoseMode 的理由）。
                    if (part.StartsWith("pose=", StringComparison.OrdinalIgnoreCase))
                        PoseMode = part.Substring(5).Trim().ToLowerInvariant();
                    // 显式角色号也认 marker：无登录的验收要一个受控绑定（探针在服务端先 Bind 同一个号），
                    // 否则 TransferCharacterId 只能靠场景序列化字段，改场景就把手写的东西带进资产里。
                    if (part.StartsWith("char=", StringComparison.OrdinalIgnoreCase))
                        TransferCharacterId = ulong.Parse(part.Substring(5), Inv);
                    if (part.StartsWith("bind=", StringComparison.OrdinalIgnoreCase))
                        _bindOnStart = int.Parse(part.Substring(5), Inv) != 0;
                    if (part.StartsWith("terrain_at=", StringComparison.OrdinalIgnoreCase))
                    {
                        var xyz = part.Substring(11).Split(',');
                        if (xyz.Length == 3)
                        {
                            _terrainAt = new Vector3(float.Parse(xyz[0], Inv), float.Parse(xyz[1], Inv), float.Parse(xyz[2], Inv));
                            _hasTerrainAt = true;
                        }
                    }
                }
            }
            catch (Exception e) { Say("marker_parse_error=" + e.GetType().Name); }
        }

        private static string MarkerPath()
        {
            string inSaved = Path.Combine(Globals.ProjectFolder, "Saved", MarkerName);
            if (File.Exists(inSaved)) return inSaved;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string inAppData = Path.Combine(appData, "HundunWorld", MarkerName);
            return File.Exists(inAppData) ? inAppData : null;
        }

        private void Say(string line) { _log.Add(line); }

        private void WriteReceipt()
        {
            try
            {
                string dir = Path.Combine(Globals.ProjectFolder, "Saved");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, ResultName), string.Join(Environment.NewLine, _log), Encoding.UTF8);
            }
            catch (Exception e) { FlaxEngine.Debug.LogWarning("PLANET_DRIVER_RECEIPT_FAILED " + e.GetType().Name); }
        }

        private void Finish(bool passed)
        {
            if (_finished) return;
            _finished = true;
            Say("passed=" + Bit(passed));
            WriteReceipt();
            _uploader?.Dispose();
            _pump?.Dispose();
            // 探针排在高度场之前拆：它是静态的，先 Dispose 再清就等于留着一个"能调但一调就抛"的钩子
            //（下一个 PIE 会话没建高度场时，换区上行会敲在这个已关闭的文件流上）
            PlanetRegionTransfers.GroundElevationCm = null;
            // 高度场排在两个消费者之后：pump 的工作线程可能还在采样，先关 DemGrid 的文件流
            // 会让正在进行的这一次采样抛在离这里很远的地方
            _field?.Dispose();
            _field = null;
            try { if (_markerPath != null) File.Delete(_markerPath); } catch (Exception) { }
        }

        private static string Bit(bool b) => b ? "1" : "0";

        private static string OneLine(string s) => s == null ? "" : s.Replace("\r", " ").Replace("\n", " ").Trim();

        private static string truncated(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));

        private static string FirstFrames(string stack, int n)
        {
            if (string.IsNullOrEmpty(stack)) return "";
            var parts = stack.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int take = n < parts.Length ? n : parts.Length;
            var picked = new string[take];
            for (int i = 0; i < take; i++) picked[i] = parts[i].Trim();
            return string.Join(" | ", picked);
        }
    }
}

