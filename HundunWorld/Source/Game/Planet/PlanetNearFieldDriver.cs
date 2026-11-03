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
    /// 近景流送的<b>驱动层</b>：把 §13.12 定死的调用顺序在引擎里真跑一遍——
    /// 每帧 <c>BuildPlan</c> → 缺的块投给 <see cref="PlanetPatchPump"/> 生成（线程池）→
    /// <c>TryDequeueCompleted</c> 取件 → 每帧最多 <see cref="LoadsPerFrame"/> 块交给
    /// <c>AddPatch</c>/<c>SetupPatchHeightMap</c>（主线程，§13.16 量到并发上传会把高程静默写坏）→
    /// <c>Release</c> 还缓冲池；出窗的块 <c>RemovePatch</c> + <c>MarkUnloaded</c>；
    /// 跨区时按全局点重算局部坐标并 <c>pump.BeginRegion</c> + <c>streamer.AttachRegion</c>。
    /// <para>它同时是一台测量仪：回执里既有"每帧被占掉多少"（驱动自己的耗时 + 引擎报的帧时长），
    /// 也有"投出去到落地隔了几帧"，还有泵那套对账等式与缓存命中数。策略参数全部是公开字段，
    /// 可以由编辑器侧的脚本桥改，不用重编。</para>
    /// <para>触发：存在 <c>&lt;Project&gt;/Saved/planet_near_field.on</c>，内容
    /// <c>"&lt;lat&gt; &lt;lon&gt; [loadsPerFrame] [collisionLod] [stepFrames] [maxFrames] [dem目录]"</c>，
    /// 缺省用解析斜面（不吃读盘，量的是流送机制本身）。跑完吃标记、写
    /// <c>planet_near_field.result.txt</c>。</para>
    /// </summary>
    public class PlanetNearFieldDriver : Script
    {
        private const string BuildTag = "near_field_driver_v5";
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
        public bool UseRealDem = false;
        public string DemDir = "";

        // ---- 运行态 ----
        private readonly List<string> _log = new List<string>();
        private readonly Queue<ResolvedPatch> _toUpload = new Queue<ResolvedPatch>();
        private readonly Dictionary<PatchKey, int> _requestFrame = new Dictionary<PatchKey, int>();
        private readonly List<Int2> _engineResident = new List<Int2>();

        private Terrain _terrain;
        private PlanetTerrainStreamer _streamer;
        private PlanetPatchPump _pump;
        private PlanetPatchCache _cache;
        private PlanetTerrain _planetTerrain;
        private RegionFrame _frame;
        private int _edge;
        private float _patchSizeCm;
        private double _localX, _localZ;
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
            Say(string.Format(Inv, "policy loads_per_frame={0} collision_lod={1} step_frames={2} steps_per_region={3} max_frames={4} queue_cap={5}",
                             LoadsPerFrame, CollisionLod, StepFrames, StepsPerRegion, MaxFrames, UploadQueueCap));
            Say(string.Format(Inv, "request_lat_lon={0:0.####}/{1:0.####} height_source={2}",
                             LatDeg, LonDeg, UseRealDem ? "real_gebco" : "analytic_ramp"));

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
                if (!DemManifest.TryLoad(DemDir, out var manifest, out string err))
                {
                    Say("dem_manifest_error=" + OneLine(err));
                    UseRealDem = false;
                    source = new EastSouthRamp(_frame.East, _frame.South);
                }
                else
                {
                    var grid = new DemGrid(manifest);
                    var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, manifest.ArcSecond / 3600.0);
                    var hybrid = new HybridHeightSource(grid, profile, manifest.ContentSeed, manifest.Format);
                    sampler = new HeightSampler(grid, hybrid);
                    source = hybrid;
                    Say(string.Format(Inv, "dem_loaded levels={0} content_seed={1:X16} terrain_version={2:X8}",
                                     manifest.Levels.Count, manifest.ContentSeed, hybrid.TerrainVersion));
                    _cache = new PlanetPatchCache(
                        Path.Combine(Globals.ProjectFolder, "Saved", "planet_patch_cache"),
                        manifest.ContentSeed, hybrid.TerrainVersion);
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
            Say(string.Format(Inv, "region={0} window_edge={1}", _frame.Key, _streamer.WindowEdge));

            _localX = 0; _localZ = 0;
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

            // 出窗：先从引擎里摘掉，再告诉流送它不在了
            foreach (var key in plan.ToUnload)
            {
                var coord = new Int2(key.X, key.Z);
                if (_terrain.HasPatch(ref coord))
                {
                    _terrain.RemovePatch(ref coord);
                    _engineResident.RemoveAll(c => c.X == coord.X && c.Y == coord.Y);
                    _unloaded++;
                }
                _streamer.MarkUnloaded(key);
            }

            foreach (var key in plan.ToLoad) RequestQuietly(key);

            while (_pump.TryDequeueCompleted(out var resolved))
            {
                if (_toUpload.Count >= UploadQueueCap)
                {
                    // 取件口堵住就把成品退回池子（它下帧会被重新投出去）：宁可重算，也不无界占内存
                    _pump.Release(resolved.Heights);
                    _requestFrame.Remove(resolved.Key);
                    _queueOverflow++;
                    continue;
                }
                _toUpload.Enqueue(resolved);
            }

            int uploadedThisFrame = 0;
            while (_toUpload.Count > 0 && uploadedThisFrame < LoadsPerFrame)
            {
                var resolved = _toUpload.Dequeue();
                var coord = new Int2(resolved.Key.X, resolved.Key.Z);
                double ms = ElapsedMs(() =>
                {
                    _terrain.AddPatch(ref coord);
                    if (_terrain.SetupPatchHeightMap(ref coord, resolved.Heights, (byte[])null, true))
                        _uploadFailures++;
                });
                _engineResident.Add(coord);
                _streamer.MarkReady(resolved.Key);
                _pump.Release(resolved.Heights);

                if (_requestFrame.TryGetValue(resolved.Key, out int at))
                {
                    long lag = _frameNo - at;
                    _lagSumFrames += lag;
                    _lagSamples++;
                    if (lag > _lagMaxFrames) _lagMaxFrames = (int)lag;
                    _requestFrame.Remove(resolved.Key);
                }
                _uploadSumMs += ms;
                _uploadMaxMs = Math.Max(_uploadMaxMs, ms);
                _uploaded++;
                uploadedThisFrame++;
            }
        }

        private int _queueOverflow;

        /// <summary>把玩家沿局部 +X 推进一步；走满 <see cref="StepsPerRegion"/> 步就换区。</summary>
        /// <summary>计划满足、泵排空、取件口空——这一刻才适合做"最后一窗对不对"的验收。</summary>
        private bool Quiescent()
        {
            if (_pump.PendingCount != 0 || _pump.InFlightCount != 0 || _toUpload.Count != 0) return false;
            return _streamer.BuildPlan(_localX, _localZ).IsSatisfied;
        }

        private void AdvancePlayer()
        {
            _steps++;
            _localX += _patchSizeCm;

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

            // 旧区域的所有 patch 在新基下没有意义：整窗摘掉（这是"换区会卡一帧"的代价，量出来）
            var dropSw = Stopwatch.StartNew();
            foreach (var coord in _engineResident)
            {
                var c = coord;
                if (_terrain.HasPatch(ref c)) _terrain.RemovePatch(ref c);
            }
            int dropped = _engineResident.Count;
            _engineResident.Clear();
            while (_toUpload.Count > 0)
            {
                var stale = _toUpload.Dequeue();
                _pump.Release(stale.Heights);
                _requestFrame.Remove(stale.Key);
            }
            _requestFrame.Clear();
            _streamer.AttachRegion(nextFrame);
            _pump.BeginRegion(nextFrame);
            dropSw.Stop();

            _frame = nextFrame;
            _localX = newLocal.X;
            _localZ = newLocal.Z;
            _crossings++;
            Say(string.Format(Inv, "  crossing={0} at_frame={1} into={2} local_now={3:0.#},{4:0.#} dropped_patches={5} swap_ms={6:0.###}",
                             _crossings, _frameNo, _frame.Key, _localX, _localZ, dropped, dropSw.Elapsed.TotalMilliseconds));
            WriteReceipt();
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

            Say("pump " + _pump.Describe());
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

            bool ok = _uploadFailures == 0 && missing == 0 && checkedPatches > 0 && worstErr < 1.0
                      && _uploaded >= plan.DesiredPatches.Count
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
            _pump?.Dispose();
            try { if (_markerPath != null) File.Delete(_markerPath); } catch (Exception) { }
        }

        private static string Bit(bool b) => b ? "1" : "0";

        private static double ElapsedMs(Action a)
        {
            var sw = Stopwatch.StartNew();
            a();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

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
