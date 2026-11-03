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
    /// P0 的引擎侧验证：<b>5×5 近景 patch 在真 Flax Terrain 上到底怎么落地、要多久、布局对不对</b>。
    /// <para>
    /// 这块离线判据替代不了：<c>index = z*509 + x</c> 与 patch 尺寸 50800 cm 此前只有源码级推断
    /// （docs §13.4 任务 3），引擎里按哪个轴展开、<c>SetupPatchHeightMap</c> 的"返回 true 才是失败"
    /// 是不是真的、上传与碰撞烹饪各花多少——都得在引擎里量一次。
    /// </para>
    /// <para>三轮的量each：b1 拿到契约与耗时；b2 抓到两件事——末顶点那一列/行与邻居共享（探针不许打它），
    /// 以及 <c>CollisionLOD</c> 是上传耗时与碰撞精度的唯一旋钮；b3（本版）把 §9 那条
    /// "5×5 patch 增删不超帧预算"改成<b>按真实帧量</b>：每帧只传一块，量每一帧被占掉多少，
    /// 顺带按 LOD 记碰撞三角形数。把 25 块连着传完再除个平均，量的不是帧预算而是批量吞吐。</para>
    /// <para>
    /// 触发沿用本项目自测约定：<c>&lt;Project&gt;/Saved/planet_terrain_spike.on</c> 存在才跑，
    /// 结果写 <c>planet_terrain_spike.result.txt</c>，跑完把标记吃掉。场景里没有 Terrain 就由脚本造一个
    /// （桥的 actor_create 不认原生 Terrain），绝不改别人的场景。
    /// </para>
    /// </summary>
    public class PlanetTerrainSpike : Script
    {
        /// <summary>装载自证：回执里出现这个串，才说明跑的是这一版装配。</summary>
        private const string BuildTag = "planet_spike_b12";

        private const string MarkerName = "planet_terrain_spike.on";
        private const string ResultName = "planet_terrain_spike.result.txt";
        private const float CmPerVertex = 100.0f;      // 引擎 TERRAIN_UNITS_PER_VERTEX

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly List<string> _log = new List<string>();

        /// <summary>本次运行的标记文件；帧步进结束时也要靠它把标记吃掉，所以存成字段而不是传参。</summary>
        private string _markerPath;

        // ---- 帧步进阶段的状态 ----
        private bool _stepping;
        private Terrain _terrain;
        private PlanetTerrainStreamer _streamer;
        private float[][] _readyBuffers;                 // 先离线填好，帧步进只做"上传"这一步
        private List<Int2> _stepQueue;
        private List<double> _stepUploadMs;
        private List<double> _stepFrameMs;
        private float[] _scratch;
        private int _edge;
        private int _collisionLodForStepping;

        public override void OnStart()
        {
            // 触发只看标记文件：OnStart 每次 PIE 本来就只发一次，加一个静态守卫反而让
            // "同一次编辑器会话里的第二次 PIE"永远不再跑（静态字段不随 PIE 重启清零）
            string marker = MarkerPath();
            if (marker == null) return;
            _markerPath = marker;
            bool passed = false;
            try
            {
                passed = Analyse(marker);
            }
            catch (Exception e)
            {
                // 崩了也必须留回执：没有回执时，"没跑"和"跑了但死在中途"长得一模一样
                Say("crash_type=" + e.GetType().Name);
                Say("crash_message=" + OneLine(e.Message));
                Say("crash_stack=" + OneLine(FirstFrames(e.StackTrace, 4)));
                Say("verdict=FAIL");
                Say("fail_reason=crashed");
                Finish(marker, false);
            }
            if (!passed) return;                        // 帧步进在 OnUpdate 里继续，最终由 Finish 收尾
        }

        public override void OnUpdate()
        {
            if (_liveStarted) { LiveTick(); return; }
            if (_controlStarted) { ControlTick(); return; }
            if (!_stepping) return;
            try
            {
                StepOnce();
            }
            catch (Exception e)
            {
                Say("step_crash_type=" + e.GetType().Name);
                Say("step_crash_message=" + OneLine(e.Message));
                Say("verdict=FAIL");
                Say("fail_reason=crashed_in_frame_stepping");
                _stepping = false;
                Finish(null, false);
            }
        }

        private void Say(string line) => _log.Add(line);

        private static string OneLine(string s) =>
            s == null ? "" : s.Replace("\r", " ").Replace("\n", " ").Trim();

        private static string FirstFrames(string stack, int n)
        {
            if (string.IsNullOrEmpty(stack)) return "";
            var parts = stack.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int take = n < parts.Length ? n : parts.Length;
            var picked = new string[take];
            for (int i = 0; i < take; i++) picked[i] = parts[i].Trim();
            return string.Join(" | ", picked);
        }

        private static string MarkerPath()
        {
            string inSaved = Path.Combine(Globals.ProjectFolder, "Saved", MarkerName);
            if (File.Exists(inSaved)) return inSaved;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string inAppData = Path.Combine(appData, "HundunWorld", MarkerName);
            return File.Exists(inAppData) ? inAppData : null;
        }

        /// <summary>只在切平面里做斜面的高度源：数值不重要，重要的是它能被独立算一遍。</summary>
        private sealed class EastSouthRamp : IHeightSource
        {
            private readonly PlanetVector3 _east;
            private readonly PlanetVector3 _south;

            public EastSouthRamp(in PlanetVector3 east, in PlanetVector3 south)
            {
                _east = east;
                _south = south;
            }

            public int SourceVersion => 1;

            public double SampleElevationCm(in PlanetVector3 unitDirection)
            {
                // 沿东向 0.05、沿南向 0.02：2.5 km 窗口约 ±125 m / ±50 m，斜面明显但不离谱
                return 12_345
                     + 0.05 * PlanetVector3.Dot(unitDirection, _east) * PlanetConstants.PlanetRadiusCm
                     + 0.02 * PlanetVector3.Dot(unitDirection, _south) * PlanetConstants.PlanetRadiusCm;
            }
        }

        /// <summary>同步阶段：契约、布局/量化、生产通路耗时、CollisionLOD 扫描。返回 false 表示可以直接收尾。</summary>
        private bool Analyse(string markerPath)
        {
            int level = PlanetConstants.DefaultRegionLevel;
            double lat = 30.0, lon = 120.0;
            bool threadOnly = false;
            bool concurrentOnly = false;
            bool liveOnly = false;
            bool demMode = false;
            bool controlOnly = false;
            string demDir = null;
            try
            {
                string text = File.ReadAllText(markerPath).Trim();
                if (text.Length > 0)
                {
                    string[] parts = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        lat = double.Parse(parts[0], Inv);
                        lon = double.Parse(parts[1], Inv);
                    }
                    if (parts.Length >= 3)
                    {
                        // 第三个 token 允许是 "thread"：那imes只跑线程探针，不去碰其余测量
                        if (string.Equals(parts[2], "thread", StringComparison.OrdinalIgnoreCase)) threadOnly = true;
                        else if (string.Equals(parts[2], "concurrent", StringComparison.OrdinalIgnoreCase)) concurrentOnly = true;
                        else if (string.Equals(parts[2], "live", StringComparison.OrdinalIgnoreCase)) liveOnly = true;
                        else if (string.Equals(parts[2], "dem", StringComparison.OrdinalIgnoreCase)) { demDir = parts.Length > 3 ? parts[3] : null; demMode = true; }
                        else if (string.Equals(parts[2], "control", StringComparison.OrdinalIgnoreCase)) controlOnly = true;
                        else level = int.Parse(parts[2], Inv);
                    }
                    for (int i = 3; i < parts.Length; i++)
                    {
                        if (string.Equals(parts[i], "thread", StringComparison.OrdinalIgnoreCase)) threadOnly = true;
                        if (string.Equals(parts[i], "concurrent", StringComparison.OrdinalIgnoreCase)) concurrentOnly = true;
                        if (string.Equals(parts[i], "live", StringComparison.OrdinalIgnoreCase)) liveOnly = true;
                    }
                }
            }
            catch (Exception e) { Say("marker_parse_error=" + e.GetType().Name); }

            Say("build_tag=" + BuildTag);
            Say(string.Format(Inv, "request_lat_lon={0:0.###}/{1:0.###} level={2}", lat, lon, level));

            var terrain = Actor.FindActor<Terrain>(false);
            if (terrain == null)
            {
                try
                {
                    var created = new Terrain { Name = "PlanetSpikeTerrain" };
                    created.Parent = Actor.Parent != null ? Actor.Parent : Actor;
                    terrain = created;
                    Say("terrain_created_by_script=1");
                    Say(string.Format(Inv, "terrain_has_scene={0} terrain_has_parent={1}",
                                      Bit(created.HasScene), Bit(created.Parent != null)));
                }
                catch (Exception e)
                {
                    Say("terrain_create_error=" + e.GetType().Name);
                    terrain = null;
                }
            }
            if (terrain == null)
            {
                Say("terrain_actor=missing");
                Say("verdict=FAIL");
                Say("fail_reason=no_terrain_actor_in_scene");
                Finish(markerPath, false);
                return false;
            }
            _terrain = terrain;
            Say("terrain_actor=" + terrain.Name);

            if (threadOnly)
            {
                ThreadProbe(terrain, level);
                Finish(markerPath, _threadProbePassed);
                return false;
            }
            if (concurrentOnly)
            {
                ConcurrentProbe(terrain, level);
                Finish(markerPath, _concurrentPassed);
                return false;
            }
            if (liveOnly)
            {
                StartLiveProbe(terrain, level);
                return false;                      // 收尾在 OnUpdate 的 live 分支里
            }
            if (controlOnly)
            {
                StartControlProbe();
                return false;
            }
            if (demMode)
            {
                bool ok = DemRun(terrain, demDir, lat, lon, level);
                Finish(markerPath, ok);
                return false;
            }

            // ---- A. 契约：引擎自己报的数，就是离线代码一直在假设的那三个数 ----
            const int lodCount = 6, chunkSize = 127;
            terrain.Setup(lodCount, chunkSize);
            int engChunk = terrain.ChunkSize;
            int edge = terrain.HeightmapSize;
            float engPatchSize = terrain.PatchSize;
            int length = edge * edge;
            _edge = edge;
            Say(string.Format(Inv, "chunk_size={0} heightmap_size={1} patch_size_cm={2:0.####}",
                             engChunk, edge, engPatchSize));
            Say(string.Format(Inv, "lod_count_property={0} collision_lod_default={1}",
                             terrain.LODCount, terrain.CollisionLOD));

            bool contractOk = engChunk == chunkSize
                && edge == PlanetPatchSampler.HeightmapEdge
                && Math.Abs(engPatchSize - PlanetConstants.TerrainPatchSizeCm) < 1e-2
                && (long)edge * edge == PlanetPatchSampler.HeightmapLength;
            Say("contract_ok=" + Bit(contractOk));
            Say(string.Format(Inv, "heightmap_edge_offline={0} engine={1} patch_size_offline={2} engine={3:0.####}",
                             PlanetPatchSampler.HeightmapEdge, edge, PlanetConstants.TerrainPatchSizeCm, engPatchSize));

            // ---- B. 布局与量化：不对称的斜面场（i:j = 100:1），转置写法差三个数量级，不会歧义 ----
            float synthBase = 30_000f;
            var synth = new float[length];
            for (int z = 0; z < edge; z++)
                for (int x = 0; x < edge; x++)
                    synth[z * edge + x] = synthBase + 100f * x + 1f * z;
            float synthMin = synthBase, synthMax = synthBase + 100f * (edge - 1) + (edge - 1);
            double impliedStep = (synthMax - synthMin) / 65536.0;

            var probeCoord = new Int2(0, 0);
            terrain.AddPatch(ref probeCoord);
            bool probeFailed = terrain.SetupPatchHeightMap(ref probeCoord, synth, (byte[])null, true);
            Say("setup_returns_true_on_failure_probe=" + Bit(probeFailed));
            var probePatch = terrain.GetPatch(ref probeCoord);
            if (probePatch == null)
            {
                Say("probe_patch=null");
                Say("verdict=FAIL");
                Say("fail_reason=setup_patch_probe_no_patch");
                Finish(markerPath, false);
                return false;
            }
            Say(string.Format(Inv, "probe_patch_height_offset_y={0:0.##}", probePatch.GetHeightY()));
            Say(string.Format(Inv, "synth_span_cm={0:0.#} implied_step_cm_at_16bit={1:0.####}",
                             synthMax - synthMin, impliedStep));

            float top = 200_000f;
            int[] innerIdx = { 0, edge / 4, edge / 2, (3 * edge) / 4, edge - 2 };
            int[] edgeIdx = { edge - 1 };

            var layout = Probe(probePatch, innerIdx, edge, synth, engPatchSize, probeCoord, top);
            Say(string.Format(Inv, "layout_inner_probes={0} hits={1} worst_err_cm={2:0.####} worst_err_in_steps={3:0.###}",
                             layout.Probes, layout.Hits, layout.WorstErr, layout.WorstErr / Math.Max(1e-6, impliedStep)));
            Say(string.Format(Inv, "layout_transposed_worst_err_cm={0:0.#}", layout.WorstTransposed));
            foreach (var pair in layout.Samples) Say("  sample " + pair);

            var edgeProbe = Probe(probePatch, edgeIdx, edge, synth, engPatchSize, probeCoord, top);
            Say(string.Format(Inv, "layout_shared_edge_probes={0} hits={1} note=last_row_col_is_shared_with_neighbour",
                             edgeProbe.Probes, edgeProbe.Hits));

            bool layoutOk = layout.Hits == layout.Probes
                            && layout.WorstErr <= 3.0 * impliedStep
                            && layout.WorstTransposed > 100.0 * impliedStep;
            Say("layout_ok=" + Bit(layoutOk));
            Say(string.Format(Inv, "layout_axis_evidence=as_written {0:0.####} vs transposed {1:0.#} cm",
                             layout.WorstErr, layout.WorstTransposed));

            var rm = new Int2(0, 0);
            terrain.RemovePatch(ref rm);

            // ---- C. 生产通路：5×5 窗口走 Horizon.Planet 的填块，冷热分开量 ----
            // 用权威公式定位区域：原先这里手抄过一份"纬度→方向"，与 Geodetic 的是两套轴序，
            // 同一个 lat/lon 会落到别的区域上（量的是同一套引擎行为，但和离线基线就不在同一块地皮上）
            var frame = RegionFrame.Build(RegionKey.FromDirection(
                Geodetic.DirectionFromLatLon(lat * Math.PI / 180.0, lon * Math.PI / 180.0), level),
                PlanetConstants.PlanetRadiusCm);
            var source = new EastSouthRamp(frame.East, frame.South);
            var planetTerrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, source);
            var streamer = new PlanetTerrainStreamer(frame, planetTerrain);   // 不接磁盘缓存：这一步只量引擎侧
            _streamer = streamer;

            int window = streamer.WindowEdge;
            int cx = streamer.CenterPatchX, cz = streamer.CenterPatchZ;
            Say(string.Format(Inv, "region={0} window_edge={1} centre_patch={2},{3} actor_position={4:0.###},{5:0.###},{6:0.###}",
                             frame.Key, window, cx, cz, terrain.Position.X, terrain.Position.Y, terrain.Position.Z));
            Say(string.Format(Inv, "yardstick_crc32_of_1mb_ms={0:0.###}", MeasureYardstick()));

            // 冷启动：整个 PIE 会话里第一次上传要付资源创建的钱，b1/b2 的总数被它拉高过
            var keys = new List<PatchKey>();
            for (int dz = -(window / 2); dz <= window / 2; dz++)
                for (int dx = -(window / 2); dx <= window / 2; dx++)
                    keys.Add(new PatchKey(cx + dx, cz + dz));

            Costs cold = MeasureWindow(terrain, streamer, keys, edge, engPatchSize, top, innerIdx, true);
            Say(string.Format(Inv,
                "cold_window: per_patch_avg={0:0.###} setup_avg={1:0.###} setup_max={2:0.###} fill_avg={3:0.###} add_avg={4:0.###} extract_avg={5:0.###} remove_avg={6:0.###} window_total={7:0.#}",
                cold.PerPatchAvg, cold.SetupAvg, cold.SetupMax, cold.FillAvg, cold.AddAvg, cold.ColAvg, cold.RemoveAvg, cold.WindowTotal));

            Costs warm = MeasureWindow(terrain, streamer, keys, edge, engPatchSize, top, innerIdx, false);
            Say(string.Format(Inv,
                "warm_window: per_patch_avg={0:0.###} setup_avg={1:0.###} setup_max={2:0.###} fill_avg={3:0.###} add_avg={4:0.###} remove_avg={5:0.###}",
                warm.PerPatchAvg, warm.SetupAvg, warm.SetupMax, warm.FillAvg, warm.AddAvg, warm.RemoveAvg));
            Say(string.Format(Inv, "setup_failures={0} nan_patches={1} missing_patches={2} patches_count_mid_window={3}",
                              warm.SetupFailures, warm.NanPatches, warm.MissingPatches, warm.PatchesCountSeen));
            Say(string.Format(Inv, "production_raycast_worst_err_cm={0:0.####} (default collision lod)", warm.ProductionErr));

            // ---- D. CollisionLOD 扫描：上传耗时与碰撞精度是同一个旋钮的两端 ----
            int[] lods = { -1, 1, 2, 3, 4 };
            var lodRows = new List<LodRow>();
            for (int rep = 0; rep < 2; rep++)
            {
                // 同一个 LOD 连测两轮：第一轮整场都是暖机（b2 里第一轮 setup 107 ms、第二轮 23 ms 就是这个），
                // 只报第二轮，且轮次顺序固定成"先重扫一遍再换档"，档位之间不再互相借暖机
                foreach (int cl in lods)
                {
                    terrain.CollisionLOD = cl;
                    Costs pre = MeasureWindow(terrain, streamer, keys, edge, engPatchSize, top, innerIdx, false);
                    Costs c = MeasureWindow(terrain, streamer, keys, edge, engPatchSize, top, innerIdx, true);
                    var row = new LodRow
                    {
                        Lod = cl,
                        SetupAvg = c.SetupAvg,
                        SetupMax = c.SetupMax,
                        PerPatchAvg = c.PerPatchAvg,
                        TrisPerPatch = c.ColTris / Math.Max(1, c.Patches),
                        ErrCm = c.ProductionErr,
                        WarmSetupAvg = pre.SetupAvg,
                    };
                    if (rep == 0) lodRows.Add(row);
                    else Replace(lodRows, row);
                }
            }
            foreach (var row in lodRows)
                Say(string.Format(Inv,
                    "  collision_lod={0} setup_avg={1:0.###} setup_max={2:0.###} first_round_setup_avg={3:0.###} per_patch_avg={4:0.###} tris_per_patch={5} raycast_err_cm={6:0.####}",
                    row.Lod, row.SetupAvg, row.SetupMax, row.WarmSetupAvg, row.PerPatchAvg, row.TrisPerPatch, row.ErrCm));

            // ---- 交棒给帧步进：每帧只传一块，量真实帧占用（这才是 §9 那条判据）----
            int best = 0;
            for (int i = 1; i < lodRows.Count; i++)
                if (lodRows[i].SetupAvg < lodRows[best].SetupAvg) best = i;
            _collisionLodForStepping = lodRows[best].Lod;
            terrain.CollisionLOD = _collisionLodForStepping;
            Say(string.Format(Inv, "stepping_collision_lod={0} (lowest upload cost in the sweep)", _collisionLodForStepping));

            var rmAll = default(Int2);
            for (int z = -4; z <= 4; z++)
                for (int x = -4; x <= 4; x++)
                {
                    rmAll = new Int2(cx + x, cz + z);
                    if (terrain.HasPatch(ref rmAll)) terrain.RemovePatch(ref rmAll);
                }

            // 先把 25 块的高度都算好（泵的工作），帧步进里只剩引擎上传——分清"算"与"交"两笔账
            _readyBuffers = new float[keys.Count][];
            _scratch = new float[PlanetPatchSampler.HeightmapLength];
            double fillTotal = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                var copy = new float[PlanetPatchSampler.HeightmapLength];
                double ms = ElapsedMs(() => { _streamer.FillPatch(keys[i], _scratch, parallel: true); });
                fillTotal += ms;
                Array.Copy(_scratch, copy, copy.Length);
                _readyBuffers[i] = copy;
            }
            Say(string.Format(Inv, "pre_fill_offline_total_ms={0:0.#} avg={1:0.###} (这部分可以完全离开主线程)",
                             fillTotal, fillTotal / Math.Max(1, keys.Count)));

            _stepQueue = new List<Int2>();
            foreach (var k in keys) _stepQueue.Add(new Int2(k.X, k.Z));
            _stepUploadMs = new List<double>();
            _stepFrameMs = new List<double>();
            _stepping = true;
            return false;                               // 收尾交给帧步进之后的 Finish
        }

        /// <summary>每帧传一块；同时记下引擎报的当帧总耗时，判据看的是"这一帧被占掉多少"。</summary>
        private void StepOnce()
        {
            if (_stepQueue == null || _stepQueue.Count == 0)
            {
                FinishStepping();
                return;
            }
            var coord = _stepQueue[0];
            _stepQueue.RemoveAt(0);
            int index = _stepUploadMs.Count;

            double upload = ElapsedMs(() =>
            {
                _terrain.AddPatch(ref coord);
                _terrain.SetupPatchHeightMap(ref coord, _readyBuffers[index], (byte[])null, true);
            });
            _stepUploadMs.Add(upload);
            // 引擎报的当帧总耗时：判据要的是"这一帧被占掉多少"，不是我自己的分段和
            _stepFrameMs.Add(Time.DeltaTime * 1000.0);

            if (_stepQueue.Count == 0) FinishStepping();
        }

        private void FinishStepping()
        {
            _stepping = false;
            var sorted = new List<double>(_stepUploadMs);
            sorted.Sort();
            double max = sorted[sorted.Count - 1];
            double median = sorted[sorted.Count / 2];
            double sum = 0;
            foreach (var v in sorted) sum += v;
            int over16 = 0, over33 = 0;
            foreach (var v in _stepUploadMs) { if (v > 16.7) over16++; if (v > 33.4) over33++; }
            double maxFrame = 0;
            foreach (var f in _stepFrameMs) maxFrame = Math.Max(maxFrame, f);

            // PIE 的头几帧本身就在着色器/资源暖机里，那部分不该记在上传头上：各算一份"去掉前 3 帧"的数
            double warmMax = 0, postMax = 0, postMaxFrame = 0;
            int postOver16 = 0;
            for (int i = 0; i < _stepUploadMs.Count; i++)
            {
                warmMax = Math.Max(warmMax, _stepUploadMs[i]);
                if (i >= 3)
                {
                    postMax = Math.Max(postMax, _stepUploadMs[i]);
                    postMaxFrame = Math.Max(postMaxFrame, _stepFrameMs[i]);
                    if (_stepUploadMs[i] > 16.7) postOver16++;
                }
            }
            Say(string.Format(Inv, "frame_stepping_warmup_skipped=3 upload_max_first_round={0:0.###} upload_max_post_warmup={1:0.###} post_warmup_frames_over_16_7ms={2} engine_frame_time_max_post={3:0.###}",
                             warmMax, postMax, postOver16, postMaxFrame));

            Say(string.Format(Inv, "frame_stepping: patches={0} lod={1} upload_avg={2:0.###} upload_median={3:0.###} upload_max={4:0.###} total={5:0.#}",
                             _stepUploadMs.Count, _collisionLodForStepping, sum / Math.Max(1, sorted.Count), median, max, sum));
            Say(string.Format(Inv, "frame_stepping_engine_frame_time_max_ms={0:0.###} frames_over_16_7ms={1} frames_over_33_4ms={2}",
                             maxFrame, over16, over33));

            // 5×5 窗口按"每帧一块"要多少帧才能铺完，以及铺完之后要不要卡
            Say(string.Format(Inv, "window_spread_frames={0} window_spread_wall_ms={1:0.#} engine_frame_count_now={2}",
                             _stepUploadMs.Count, sum, Engine.FrameCount));

            bool budgetOk = postMax <= 16.7;
            bool verdict = budgetOk;
            Say(string.Format(Inv, "patches_count_after_stepping={0}", _terrain.PatchesCount));
            Say("one_patch_per_frame_fits_16_7ms=" + Bit(budgetOk));
            Say("verdict=" + (verdict ? "PASS" : "FAIL"));
            if (!verdict)
            {
                Say(string.Format(Inv, "fail_reason=upload_still_over_frame_budget post_warmup_max={0:0.###}ms need<={1}ms", postMax, 16.7));
                Say(string.Format(Inv, "note=upload_is_main_thread_bound per_patch_upload_ms={0:0.###}", median));
            }
            Finish(null, verdict);
        }

        private bool _threadProbePassed;

        /// <summary>
        /// "上传能不能离开主线程"——这条决定流送策略的选项空间，而发行版不带 Terrain.cpp，读不到守卫，只能实测。
        /// <para>判据分两层：机制层是"调用本身怎样"（抛不抛、返回 true/false、线程收没收回来）；
        /// 效果层是"结果对不对"（patch 在不在、射线高程与写入值差多少）。只看一层会把
        /// "没抛异常但根本没生效"读成成功。</para>
        /// <para>正对照在同一次运行里：同一份高度、同一套坐标、只差调用发生在哪个线程。
        /// 先在主线程把这块传成功，才说明坏掉的是线程而不是我的数据。</para>
        /// </summary>
        private void ThreadProbe(Terrain terrain, int level)
        {
            const int lodCount = 6, chunkSize = 127;
            terrain.Setup(lodCount, chunkSize);
            int edge = terrain.HeightmapSize;
            float patchSize = terrain.PatchSize;

            var frame = RegionFrame.Build(RegionKey.FromDirection(
                Geodetic.DirectionFromLatLon(30.0 * Math.PI / 180.0, 120.0 * Math.PI / 180.0), level),
                PlanetConstants.PlanetRadiusCm);
            var source = new EastSouthRamp(frame.East, frame.South);
            var planetTerrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, source);
            var streamer = new PlanetTerrainStreamer(frame, planetTerrain);

            var buffer = new float[PlanetPatchSampler.HeightmapLength];
            var controlKey = new PatchKey(0, 0);
            streamer.FillPatch(controlKey, buffer, parallel: false);      // 数据在主线程算好，两个线程用同一份

            // ---- 正对照：主线程上传同一份数据 ----
            var cCoord = new Int2(0, 0);
            terrain.AddPatch(ref cCoord);
            bool cFailed = terrain.SetupPatchHeightMap(ref cCoord, buffer, (byte[])null, true);
            var cPatch = terrain.GetPatch(ref cCoord);
            double cErr = cPatch == null ? double.NaN : RayErr(cPatch, buffer, edge, patchSize, cCoord);
            Say(string.Format(Inv, "control_main_thread: returned_true={0} patch_null={1} err_cm={2:0.####}",
                             Bit(cFailed), Bit(cPatch == null), cErr));
            bool controlOk = !cFailed && cPatch != null && cErr < 1.0;
            Say("control_ok=" + Bit(controlOk));

            var rCoord = new Int2(0, 0);
            terrain.RemovePatch(ref rCoord);

            // 先落盘：万一下面那句把进程打死，前面的对照还在盘上（"没有回执"与"跑到一半死"必须能分开）
            WriteReceipt();

            var wCoord = new Int2(1, 0);
            Exception thrown = null;
            bool returnedTrue = true;
            double workerMs = 0;
            var t = new System.Threading.Thread(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    terrain.AddPatch(ref wCoord);
                    returnedTrue = terrain.SetupPatchHeightMap(ref wCoord, buffer, (byte[])null, true);
                }
                catch (Exception e)
                {
                    thrown = e;
                }
                sw.Stop();
                workerMs = sw.Elapsed.TotalMilliseconds;
            });
            t.IsBackground = true;
            t.Start();
            bool joined = t.Join(10000);

            Say(string.Format(Inv, "mechanism_off_thread: threw={0} msg={1} returned_true={2} join_ok={3} worker_ms={4:0.###}",
                             thrown == null ? "0" : "1",
                             thrown == null ? "" : (thrown.GetType().Name + ": " + OneLine(truncated(thrown.Message, 140))),
                             Bit(returnedTrue), Bit(joined), workerMs));
            WriteReceipt();

            // ---- 效果层：回主线程看引擎到底认不认这次上传 ----
            bool has = terrain.HasPatch(ref wCoord);
            var wPatch = terrain.GetPatch(ref wCoord);
            double wErr = wPatch == null ? double.NaN : RayErr(wPatch, buffer, edge, patchSize, wCoord);
            Say(string.Format(Inv, "effect_after_off_thread: has_patch={0} patch_null={1} err_cm={2:0.####} patches_count={3}",
                             Bit(has), Bit(wPatch == null), wErr, terrain.PatchesCount));

            bool usable = joined && thrown == null && !returnedTrue && has && wPatch != null && wErr < 1.0;
            // 只有"主线程对照成功、这次不成功"才叫绑在主线程；别的算法会把数据坏掉也算成线程结论
            bool mainThreadOnly = controlOk && !usable;
            Say(string.Format(Inv, "thread_probe_verdict={0}",
                              usable ? "OFF_THREAD_UPLOAD_WORKS"
                                     : (controlOk ? "MAIN_THREAD_BOUND" : "CONTROL_ALSO_FAILED_HARNESS_IS_WRONG")));
            Say("main_thread_bound=" + Bit(mainThreadOnly));
            _threadProbePassed = usable || !controlOk;      // 对照都失败时这条探针说明不了任何事，别报成 PASS
            if (!usable)
            {
                Say("fail_reason=" + (controlOk ? "off_thread_upload_not_accepted" : "control_failed_on_main_thread"));
            }
            WriteReceipt();
        }

        private bool _concurrentPassed = true;
        private int _firstBadWorkers;

        /// <summary>
        /// 单块 off-thread 成功不等于能并发上传。第一轮压测（16 线程 × 81 块）的结果不是崩溃而是
        /// <b>静默错值</b>：81 块里 66 块射线高程偏差 ≥ 1 cm，最大 6439 cm，而只有一个调用返回失败、
        /// 零异常。所以这里改成扫一条 workers 剂量曲线：1 / 2 / 4 / 8 / 16 各跑一遍同一批 81 块，
        /// 每遍之后逐块回验，最后一遍再回到 16 复查可复现性。
        /// <para>这条曲线要分清两个变量——"离开主线程"与"并发"。设计能用的结论只在
        /// workers=1 干净、workers&gt;1 变坏时成立（"可以单线程后台上传"）；两边都坏就说明上传根本
        /// 不能离开主线程。别把"没抛异常"读成"没坏"：这一版判据看的是事后每块的数值。</para>
        /// <para>证不了"与渲染线程无冲突"——那需要引擎契约背书。这里只能说"在这种用法下量到了什么"。</para>
        /// </summary>
        private void ConcurrentProbe(Terrain terrain, int level)
        {
            const int lodCount = 6, chunkSize = 127;
            terrain.Setup(lodCount, chunkSize);
            int edge = terrain.HeightmapSize;
            float patchSize = terrain.PatchSize;

            var frame = RegionFrame.Build(RegionKey.FromDirection(
                Geodetic.DirectionFromLatLon(30.0 * Math.PI / 180.0, 120.0 * Math.PI / 180.0), level),
                PlanetConstants.PlanetRadiusCm);
            var source = new EastSouthRamp(frame.East, frame.South);
            var planetTerrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, source);
            var streamer = new PlanetTerrainStreamer(frame, planetTerrain);

            var jobs = new List<PatchKey>();
            for (int dz = -4; dz <= 4; dz++)
                for (int dx = -4; dx <= 4; dx++) jobs.Add(new PatchKey(dx, dz));

            var plan = new List<int>();
            foreach (int w in new[] { 1, 2, 4, 8, 16 }) plan.Add(Math.Min(w, System.Environment.ProcessorCount));
            plan.Add(plan[plan.Count - 1]);            // 复查一遍最并发的那档
            Say(string.Format(Inv, "concurrent_probe: patches={0} cpu_count={1} sweep={2}",
                             jobs.Count, System.Environment.ProcessorCount, string.Join(",", plan)));
            int reps = 0;

            foreach (int workers in plan)
            {
                reps++;
                var rep = reps;
                // 每轮先把高度都算好（这部分离线已证逐位相同），并发只剩引擎调用本身
                var buffers = new float[jobs.Count][];
                var one = new float[PlanetPatchSampler.HeightmapLength];
                for (int i = 0; i < jobs.Count; i++)
                {
                    streamer.FillPatch(jobs[i], one, parallel: false);
                    var copy = new float[PlanetPatchSampler.HeightmapLength];
                    System.Array.Copy(one, copy, copy.Length);
                    buffers[i] = copy;
                }

                int next = -1, failures = 0, throws = 0;
                var threads = new System.Threading.Thread[workers];
                var sw = Stopwatch.StartNew();
                for (int w = 0; w < workers; w++)
                {
                    threads[w] = new System.Threading.Thread(() =>
                    {
                        for (; ; )
                        {
                            int i = System.Threading.Interlocked.Increment(ref next);
                            if (i >= jobs.Count) return;
                            var coord = new Int2(jobs[i].X, jobs[i].Z);
                            try
                            {
                                terrain.AddPatch(ref coord);
                                if (terrain.SetupPatchHeightMap(ref coord, buffers[i], (byte[])null, true))
                                    System.Threading.Interlocked.Add(ref failures, 1);
                            }
                            catch (Exception)
                            {
                                System.Threading.Interlocked.Add(ref throws, 1);
                            }
                        }
                    });
                    threads[w].IsBackground = true;
                    threads[w].Start();
                }
                bool allJoined = true;
                foreach (var t in threads) if (!t.Join(30000)) allJoined = false;
                sw.Stop();

                // 回主线程逐块验：偏差、缺块都要算进这一轮的账
                int missing = 0, bad = 0;
                double worst = 0;
                foreach (var k in jobs)
                {
                    var coord = new Int2(k.X, k.Z);
                    if (!terrain.HasPatch(ref coord)) { missing++; continue; }
                    var patch = terrain.GetPatch(ref coord);
                    if (patch == null) { missing++; continue; }
                    double err = RayErr(patch, buffers[jobs.IndexOf(k)], edge, patchSize, coord);
                    if (!(err < 1.0)) bad++;
                    if (err > worst) worst = err;
                }

                Say(string.Format(Inv,
                    "  pass {0}: workers={1} wall_ms={2:0.#} join_all={3} setup_failures={4} throws={5} missing={6} bad_err={7}/{8} worst_err_cm={9:0.####} patches_count={10}",
                    rep, workers, sw.Elapsed.TotalMilliseconds, Bit(allJoined), failures, throws, missing, bad, jobs.Count, worst, terrain.PatchesCount));
                WriteReceipt();

                bool roundOk = allJoined && failures == 0 && throws == 0 && missing == 0 && bad == 0 && worst < 1.0;
                _concurrentPassed = _concurrentPassed && roundOk;
                if (!roundOk) _firstBadWorkers = _firstBadWorkers == 0 ? workers : _firstBadWorkers;

                // 下一轮要在干净的地皮上跑，否则"第二轮也绿"可能只是没重来
                foreach (var k in jobs)
                {
                    var coord = new Int2(k.X, k.Z);
                    if (terrain.HasPatch(ref coord)) terrain.RemovePatch(ref coord);
                }
            }
            Say(string.Format(Inv, "first_pass_that_broke_workers={0}", _firstBadWorkers));
            Say(string.Format(Inv, "concurrent_verdict={0}",
                              _concurrentPassed ? "ALL_WORKER_COUNTS_CLEAN_BUT_STILL_NOT_CONTRACT_BACKED"
                              : (_firstBadWorkers > 1 ? "SINGLE_WORKER_THREAD_MAY_BE_OK_CONCURRENCY_IS_NOT"
                                                      : "OFF_THREAD_UPLOAD_BREAKS_AT_ONE_WORKER")));
        }

        // ---- live：一条后台线程在"游戏照常跑帧"的情况上传，主线程不 Join ----
        private bool _liveStarted;
        private bool _liveDone;
        private int _liveFrames;
        private int _livePatchesRemaining;
        private Terrain _liveTerrain;
        private float[][] _liveBuffers;
        private List<Int2> _liveQueue;
        private double _liveMaxMainMs;
        private double _liveMinMainMs;
        private double _liveSumMainMs;
        private int _liveSamples;
        private int _liveBigFrameTicks;
        private ulong _liveFrameBase;
        private System.Threading.Thread _liveWorker;
        private int _liveWorkerFailed;
        private int _liveWorkerThrows;
        private double _liveWorkerMs;

        /// <summary>
        /// 前两条探针都把主线程钉在 <c>Join</c> 上等着，那只证明"这段代码能在别的线程上写完"，
        /// 没证明"设计要的形态"：后台线程上传的同时主线程照常出帧。这一条就是照那个形态量的：
        /// 启一条线程传 25 块，主线程不阻塞、照常 OnUpdate 跑帧，跑完再逐块回验。
        /// <para>两条读数都要：主线程每帧被我自己的记账占了多少（应当≈0，这是这套做法想买的东西），
        /// 以及最后每块高程对不对（这是它能不能买的条件）。</para>
        /// </summary>
        private void StartLiveProbe(Terrain terrain, int level)
        {
            const int lodCount = 6, chunkSize = 127;
            terrain.Setup(lodCount, chunkSize);
            int edge = terrain.HeightmapSize;
            float patchSize = terrain.PatchSize;
            _edge = edge;
            _liveTerrain = terrain;

            var frame = RegionFrame.Build(RegionKey.FromDirection(
                Geodetic.DirectionFromLatLon(30.0 * Math.PI / 180.0, 120.0 * Math.PI / 180.0), level),
                PlanetConstants.PlanetRadiusCm);
            var source = new EastSouthRamp(frame.East, frame.South);
            var planetTerrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, source);
            var streamer = new PlanetTerrainStreamer(frame, planetTerrain);
            _streamer = streamer;
            _livePatchSize = patchSize;

            var keys = new List<PatchKey>();
            for (int dz = -4; dz <= 4; dz++)
                for (int dx = -4; dx <= 4; dx++) keys.Add(new PatchKey(dx, dz));

            _liveBuffers = new float[keys.Count][];
            var scratch = new float[PlanetPatchSampler.HeightmapLength];
            for (int i = 0; i < keys.Count; i++)
            {
                streamer.FillPatch(keys[i], scratch, parallel: false);
                var copy = new float[PlanetPatchSampler.HeightmapLength];
                System.Array.Copy(scratch, copy, copy.Length);
                _liveBuffers[i] = copy;
            }
            _liveQueue = new List<Int2>();
            foreach (var k in keys) _liveQueue.Add(new Int2(k.X, k.Z));
            _livePatchesRemaining = _liveQueue.Count;

            _liveFrameBase = Engine.FrameCount;
            Say(string.Format(Inv, "live_probe: patches={0} edge={1} patch_size={2:0.#} mode=worker_uploads_while_main_thread_ticks",
                             keys.Count, edge, patchSize));
            WriteReceipt();

            _liveStarted = true;
            _liveWorker = new System.Threading.Thread(LiveWorker) { IsBackground = true };
            _liveWorker.Start();
        }

        private float _livePatchSize;

        private void LiveWorker()
        {
            var sw = Stopwatch.StartNew();
            int i = 0;
            foreach (var coord in _liveQueue)
            {
                var c = coord;
                try
                {
                    _liveTerrain.AddPatch(ref c);
                    if (_liveTerrain.SetupPatchHeightMap(ref c, _liveBuffers[i], (byte[])null, true))
                        _liveWorkerFailed++;
                }
                catch (System.Exception)
                {
                    _liveWorkerThrows++;
                }
                i++;
                System.Threading.Interlocked.Decrement(ref _livePatchesRemaining);
            }
            sw.Stop();
            _liveWorkerMs = sw.Elapsed.TotalMilliseconds;
        }

        private void LiveTick()
        {
            if (!_liveStarted || _liveDone) return;
            _liveFrames++;
            double mainMs = Time.DeltaTime * 1000.0;
            if (_livePatchesRemaining > 0)
            {
                // 只有"后台确实还在传"的帧才算样本；否则这条判据会在零样本上报绿（b9 就是这么空的）
                _liveSamples++;
                if (mainMs > _liveMaxMainMs) _liveMaxMainMs = mainMs;
                _liveMinMainMs = _liveSamples == 1 ? mainMs : Math.Min(_liveMinMainMs, mainMs);
                _liveSumMainMs += mainMs;
                if (mainMs > 50.0) _liveBigFrameTicks++;
            }

            if (_livePatchesRemaining > 0 && _liveFrames < 1200) return;

            _liveDone = true;
            Say(string.Format(Inv, "live_window: total_onupdate_ticks={0} ticks_while_uploading={1} engine_frames={2} samples_needed=8",
                             _liveFrames, _liveSamples, Engine.FrameCount - _liveFrameBase));
            bool joined = _liveWorker.Join(15000);

            int missing = 0, bad = 0;
            double worst = 0;
            foreach (var coord in _liveQueue)
            {
                var c = coord;
                if (!_liveTerrain.HasPatch(ref c)) { missing++; continue; }
                var patch = _liveTerrain.GetPatch(ref c);
                if (patch == null) { missing++; continue; }
                int index = _liveQueue.IndexOf(c);
                double err = RayErr(patch, _liveBuffers[index], _edge, _livePatchSize, c);
                if (!(err < 1.0)) bad++;
                if (err > worst) worst = err;
            }

            Say(string.Format(Inv,
                "live_result: frames={0} join_ok={1} worker_ms={2:0.#} worker_throws={3} worker_setup_failures={4} missing={5} bad_err={6}/{7} worst_err_cm={8:0.####}",
                _liveFrames, Bit(joined), _liveWorkerMs, _liveWorkerThrows, _liveWorkerFailed, missing, bad, _liveQueue.Count, worst));
            Say(string.Format(Inv,
                "live_main_thread_frame_ms_while_uploading: samples={0} min={1:0.###} avg={2:0.###} max={3:0.###} over_50ms={4}",
                _liveSamples, _liveMinMainMs, _liveSamples == 0 ? 0 : _liveSumMainMs / _liveSamples, _liveMaxMainMs, _liveBigFrameTicks));

            // 样本不够时这条判据什么都没观察到：报 INCONCLUSIVE，不许报绿
            bool observed = _liveSamples >= 8;
            bool ok = observed && joined && _liveWorkerThrows == 0 && _liveWorkerFailed == 0 && missing == 0 && bad == 0;
            Say(string.Format(Inv, "live_verdict={0}",
                              !observed ? "INCONCLUSIVE_main_thread_did_not_tick_enough_while_uploading"
                              : (ok ? "SINGLE_WORKER_UPLOADS_CORRECTLY_WHILE_MAIN_THREAD_TICKS"
                                    : "LIVE_UPLOAD_NOT_TRUSTWORTHY")));
            Finish(null, ok);
        }

        /// <summary>
        /// 真实 GEBCO 基场 + 混合细节层 + 磁盘 patch 缓存，第一次整条链跑在引擎里。
        /// <para>此前所有引擎侧测量用的都是解析斜面（可控、可独立复算）。这条改吃真实数据，
        /// 要回答的是两件以前没证过的事：① 从 DEM 到 <c>SetupPatchHeightMap</c> 的厘米值，进引擎后
        /// 与写进去的数组是否逐格对上（射线回验，量的是上传保真度）；② 每块的高程统计量与离线
        /// CLI 是不是同一套数（跨进程对账：这里是 PIE 里的 C#，那边是 DemProbe，两份独立产物）。</para>
        /// <para>缓存也一并接上：<c>Saved/planet_patch_cache</c>，冷跑一遍再热跑一遍，命中数与
        /// 二次上传的耗时差都会写进回执——这是"引擎里也吃缓存"的第一次实证。</para>
        /// </summary>
        private bool DemRun(Terrain terrain, string manifestDir, double lat, double lon, int level)
        {
            Say("dem_mode=1");
            if (string.IsNullOrEmpty(manifestDir) || !Directory.Exists(manifestDir))
            {
                Say("dem_manifest_dir_missing=" + (manifestDir ?? ""));
                Say("verdict=FAIL");
                Say("fail_reason=dem_manifest_dir");
                return false;
            }
            if (!DemManifest.TryLoad(manifestDir, out var manifest, out string manifestError))
            {
                Say("dem_manifest_load_error=" + OneLine(manifestError));
                Say("verdict=FAIL");
                Say("fail_reason=dem_manifest_load");
                return false;
            }
            Say(string.Format(Inv, "dem_manifest arcsec={0:0.####} levels={1} content_seed={2:X16}",
                             manifest.ArcSecond / 3600.0, manifest.Levels.Count, manifest.ContentSeed));

            const int lodCount = 6, chunkSize = 127;
            terrain.Setup(lodCount, chunkSize);
            int edge = terrain.HeightmapSize;
            float patchSize = terrain.PatchSize;

            using var grid = new DemGrid(manifest);
            var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, manifest.ArcSecond / 3600.0);
            var hybrid = new HybridHeightSource(grid, profile, manifest.ContentSeed, manifest.Format);
            var sampler = new HeightSampler(grid, hybrid);
            var planetTerrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, hybrid);

            var frame = RegionFrame.Build(RegionKey.FromDirection(
                Geodetic.DirectionFromLatLon(lat * Math.PI / 180.0, lon * Math.PI / 180.0), level),
                PlanetConstants.PlanetRadiusCm);

            string cacheDir = Path.Combine(Globals.ProjectFolder, "Saved", "planet_patch_cache");
            Directory.CreateDirectory(cacheDir);
            var cache = new PlanetPatchCache(cacheDir, manifest.ContentSeed, hybrid.TerrainVersion);
            var streamer = new PlanetTerrainStreamer(frame, planetTerrain, cache, sampler);

            // 材质能加载就挂上（出图用），加载不了也只是没颜色，不影响数值判据
            string matPath = Path.Combine(Globals.ProjectFolder, "Content/Materials/Material.flax");
            try
            {
                var mat = Content.Load<MaterialBase>(matPath);
                if (mat == null) Say("material=null " + matPath);
                else { terrain.Material = mat; Say("material=loaded " + matPath); }
            }
            catch (Exception e) { Say("material_error=" + e.GetType().Name + " " + OneLine(truncated(e.Message, 120))); }

            var keys = new List<PatchKey>();
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++) keys.Add(new PatchKey(dx, dz));

            Say(string.Format(Inv, "dem_region={0} window={1} edge={2} patch_size={3:0.#}", frame.Key, keys.Count, edge, patchSize));

            var scratch = new float[PlanetPatchSampler.HeightmapLength];
            var loaded = new List<Int2>();
            double coldSetup = 0, coldFill = 0, hotFill = 0;
            double worstFidelity = 0;
            int fails = 0, missing = 0;
            bool failed = false;
            double minAll = double.MaxValue, maxAll = double.MinValue;

            foreach (var key in keys)
            {
                var coord = new Int2(key.X, key.Z);
                double fillMs = ElapsedMs(() => streamer.FillPatch(key, scratch, parallel: false));
                coldFill += fillMs;
                double stats = StatsAndCheck(scratch, out double lo, out double hi);
                minAll = Math.Min(minAll, lo); maxAll = Math.Max(maxAll, hi);
                double uploadMs = ElapsedMs(() =>
                {
                    terrain.AddPatch(ref coord);
                    failed = terrain.SetupPatchHeightMap(ref coord, scratch, (byte[])null, true);
                });
                if (failed) fails++;
                coldSetup += uploadMs;
                var patch = terrain.GetPatch(ref coord);
                if (patch == null) missing++;
                else
                {
                    // 上传保真度：读回来比对，量的是"引擎里的地表"与"我写进去的数组"是不是同一份数
                    double err = RayErr(patch, scratch, edge, patchSize, coord);
                    worstFidelity = Math.Max(worstFidelity, err);
                    if (err > 1.0) Say(string.Format(Inv, "  upload_fidelity_bad patch={0} err_cm={1:0.####}", key, err));
                }
                Say(string.Format(Inv, "  patch={0} fill_ms={1:0.###} upload_ms={2:0.###} min_m={3:0.###} max_m={4:0.###} mean_m={5:0.###}",
                                 key, fillMs, uploadMs, lo / 100.0, hi / 100.0, stats / 100.0));
                loaded.Add(coord);
            }

            Say(string.Format(Inv, "dem_cold: fill_total_ms={0:0.#} upload_total_ms={1:0.#} upload_failures={2} missing_patches={3} worst_upload_fidelity_err_cm={4:0.####} elevation_min_m={5:0.###} elevation_max_m={6:0.###}",
                             coldFill, coldSetup, fails, missing, worstFidelity, minAll / 100.0, maxAll / 100.0));
            Say(string.Format(Inv, "cache_after_cold: hits={0} misses={1} writes={2} bytes={3}",
                             cache.Hits, cache.Misses, cache.Writes, cache.BytesUsed));

            // 热跑：同一批块再走一遍，应该全部从盘上回来，且不再付生成成本
            foreach (var item in loaded)
            {
                var gone = item;
                terrain.RemovePatch(ref gone);
            }
            var scratch2 = new float[PlanetPatchSampler.HeightmapLength];
            int hitsBefore = (int)streamer.CacheHits;
            foreach (var key in keys)
            {
                var coord = new Int2(key.X, key.Z);
                hotFill += ElapsedMs(() => streamer.FillPatch(key, scratch2, parallel: false));
                terrain.AddPatch(ref coord);
                if (terrain.SetupPatchHeightMap(ref coord, scratch2, (byte[])null, true)) fails++;
            }
            Say(string.Format(Inv, "dem_hot: fill_total={0:0.#} streamer_hits={1} cache_hits={2} cache_misses={3} ratio_cold_over_hot={4:0.##}",
                             hotFill, streamer.CacheHits - hitsBefore, cache.Hits, cache.Misses,
                             coldFill / Math.Max(0.001, hotFill)));

            bool ok = fails == 0 && missing == 0 && maxAll > minAll && worstFidelity < 1.0 && cache.Writes == keys.Count;
            Say("dem_verdict=" + (ok ? "REAL_DEM_PIPELINE_LANDED_IN_ENGINE" : "DEM_PIPELINE_PROBLEM"));
            Say("note=引擎里的厘米值来自同一份 FillPatchHeights；跨进程对账拿离线 CLI 同点位再跑一遍");
            return ok;
        }

        private static double StatsAndCheck(float[] a, out double lo, out double hi)
        {
            lo = double.MaxValue; hi = double.MinValue; double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                float v = a[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) { lo = double.NaN; hi = double.NaN; sum = double.NaN; return sum; }
                if (v < lo) lo = v;
                if (v > hi) hi = v;
                sum += v;
            }
            return sum / a.Length;
        }

        /// <summary>
        /// §13.16 缺的那条对照：同一次 PIE、同样出帧，但<em>不起任何上传线程</em>，
        /// 量同样的帧数与帧时长分布。没有它，"上传期间帧时长 6.4–100 ms"没法归因。
        /// </summary>
        private void StartControlProbe()
        {
            _controlStarted = true;
            _controlStartFrame = Engine.FrameCount;
            _controlTicks = 0;
            _controlMin = double.MaxValue;
            Say("control_probe: mode=no_uploads_frames=40");
            WriteReceipt();
        }

        private bool _controlStarted;
        private ulong _controlStartFrame;
        private int _controlTicks;
        private double _controlMin, _controlSum, _controlMax;
        private int _controlOver50;

        private void ControlTick()
        {
            _controlTicks++;
            double ms = Time.DeltaTime * 1000.0;
            _controlMin = Math.Min(_controlMin, ms);
            _controlMax = Math.Max(_controlMax, ms);
            _controlSum += ms;
            if (ms > 50.0) _controlOver50++;
            if (_controlTicks < 40) return;
            _controlStarted = false;            // 报一次就收手：否则每个后续帧都在追加同一条结果（实测跑到 133 帧）

            Say(string.Format(Inv,
                "control_result: ticks={0} engine_frames={1} frame_ms min={2:0.###} avg={3:0.###} max={4:0.###} over_50ms={5}",
                _controlTicks, Engine.FrameCount - _controlStartFrame, _controlMin, _controlSum / _controlTicks, _controlMax, _controlOver50));
            Finish(null, true);
        }

        private static string truncated(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n);
        }

        /// <summary>一块里按内部格打射线，取"引擎给的高程"与"我写进去的高程"的最大偏差（cm）。</summary>
        private static double RayErr(TerrainPatch patch, float[] field, int edge, float patchSize, Int2 coord)
        {
            const float top = 200_000f;
            int[] idx = { 0, edge / 4, edge / 2, (3 * edge) / 4, edge - 2 };
            double worst = 0;
            foreach (int pi in idx)
                foreach (int pj in idx)
                {
                    var origin = new Vector3(coord.X * patchSize + pi * 100f, top, coord.Y * patchSize + pj * 100f);
                    if (!patch.RayCast(origin, new Vector3(0, -1, 0), out float dist, top * 2f)) continue;
                    double err = Math.Abs(top - dist - field[pj * edge + pi]);
                    if (err > worst) worst = err;
                }
            return worst;
        }

        private struct LodRow
        {
            public int Lod;
            public double SetupAvg, SetupMax, PerPatchAvg, ErrCm, WarmSetupAvg;
            public long TrisPerPatch;
        }

        /// <summary>第二轮的结果覆盖第一轮：判据只认稳定态，不被暖机带跑。</summary>
        private static void Replace(List<LodRow> rows, LodRow next)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Lod == next.Lod) { rows[i] = next; return; }
            rows.Add(next);
        }

        private struct ProbeResult
        {
            public int Probes;
            public int Hits;
            public double WorstErr;
            public double WorstTransposed;
            public List<string> Samples;
        }

        /// <summary>按局部坐标向下打射线，把引擎给的高程与"我写进去的那个数"对上。</summary>
        private static ProbeResult Probe(TerrainPatch patch, int[] idx, int edge, float[] field,
                                         float patchSize, Int2 coord, float top)
        {
            var r = new ProbeResult { Samples = new List<string>() };
            foreach (int pi in idx)
            {
                foreach (int pj in idx)
                {
                    r.Probes++;
                    var origin = new Vector3(coord.X * patchSize + pi * CmPerVertex, top,
                                             coord.Y * patchSize + pj * CmPerVertex);
                    if (!patch.RayCast(origin, new Vector3(0, -1, 0), out float dist, top * 2f))
                        continue;
                    r.Hits++;
                    double hitY = top - dist;
                    double asWritten = field[pj * edge + pi];
                    double transposed = field[pi * edge + pj];
                    double err = Math.Abs(hitY - asWritten);
                    r.WorstErr = Math.Max(r.WorstErr, err);
                    r.WorstTransposed = Math.Max(r.WorstTransposed, Math.Abs(hitY - transposed));
                    if (r.Samples.Count < 6)
                        r.Samples.Add(string.Format(Inv, "i={0} j={1} expect={2:0.##} hit={3:0.##} err={4:0.####}",
                                                    pi, pj, asWritten, hitY, err));
                }
            }
            return r;
        }

        private struct Costs
        {
            public int Patches;
            public int SetupFailures;
            public int NanPatches;
            public int MissingPatches;
            public int PatchesCountSeen;
            public double FillAvg, AddAvg, SetupAvg, SetupMax, ColAvg, RemoveAvg, WindowTotal, PerPatchAvg, PerPatchMax;
            public long ColVerts, ColTris;
            public double ProductionErr;
        }

        /// <summary>把整个窗口连着传一遍：这是吞吐口径，不是帧预算口径（帧预算在 StepOnce 里量）。</summary>
        private Costs MeasureWindow(Terrain terrain, PlanetTerrainStreamer streamer, List<PatchKey> keys,
                                    int edge, float patchSize, float top, int[] probeIdx, bool collectCollision)
        {
            var c = new Costs();
            var scratch = new float[PlanetPatchSampler.HeightmapLength];
            var loaded = new List<Int2>();
            double fill = 0, add = 0, setup = 0, setupMax = 0, col = 0, worst = 0, perPatchMax = 0;
            var sw = new Stopwatch();
            sw.Start();

            foreach (var key in keys)
            {
                var coord = new Int2(key.X, key.Z);
                var perPatch = new Stopwatch();
                perPatch.Start();

                double fillMs = ElapsedMs(() => streamer.FillPatch(key, scratch, parallel: true));
                double addMs = ElapsedMs(() => terrain.AddPatch(ref coord));

                bool failed = false;
                double setupMs = ElapsedMs(() => failed = terrain.SetupPatchHeightMap(ref coord, scratch, (byte[])null, true));
                if (failed) c.SetupFailures++;

                double colMs = 0;
                var patch = terrain.GetPatch(ref coord);
                if (patch == null) c.MissingPatches++;
                if (patch != null)
                {
                    if (collectCollision)
                    {
                        Float3[] cv = null;
                        int[] ci = null;
                        colMs = ElapsedMs(() => patch.ExtractCollisionGeometry(out cv, out ci));
                        if (cv != null && ci != null) { c.ColVerts += cv.Length; c.ColTris += ci.Length / 3; }
                    }
                    foreach (int pi in probeIdx)
                        foreach (int pj in probeIdx)
                        {
                            var origin = new Vector3(coord.X * patchSize + pi * CmPerVertex, top,
                                                     coord.Y * patchSize + pj * CmPerVertex);
                            if (!patch.RayCast(origin, new Vector3(0, -1, 0), out float dist, top * 2f)) continue;
                            double err = Math.Abs(top - dist - scratch[pj * edge + pi]);
                            if (err > worst) worst = err;
                        }
                }

                int nan = 0;
                for (int i = 0; i < scratch.Length; i++)
                    if (float.IsNaN(scratch[i]) || float.IsInfinity(scratch[i])) nan++;
                if (nan != 0) c.NanPatches++;

                perPatch.Stop();
                fill += fillMs; add += addMs; setup += setupMs; col += colMs;
                setupMax = Math.Max(setupMax, setupMs);
                perPatchMax = Math.Max(perPatchMax, perPatch.Elapsed.TotalMilliseconds);
                loaded.Add(coord);
                c.Patches++;
            }

            c.PatchesCountSeen = terrain.PatchesCount;
            sw.Stop();
            c.WindowTotal = sw.Elapsed.TotalMilliseconds;

            double remove = 0;
            foreach (var item in loaded)
            {
                var coord = item;
                var t0 = new Stopwatch();
                t0.Start();
                terrain.RemovePatch(ref coord);
                t0.Stop();
                remove += t0.Elapsed.TotalMilliseconds;
            }

            int n = Math.Max(1, c.Patches);
            c.FillAvg = fill / n; c.AddAvg = add / n; c.SetupAvg = setup / n; c.SetupMax = setupMax;
            c.ColAvg = col / n; c.RemoveAvg = remove / n; c.PerPatchMax = perPatchMax;
            c.PerPatchAvg = c.WindowTotal / n;
            c.ProductionErr = worst;
            return c;
        }

        /// <summary>同一次运行里的尺子：搬 1 MB 做一次 crc32（离线侧用它代替绝对毫秒）。</summary>
        private static double MeasureYardstick()
        {
            var buffer = new byte[1 << 20];
            new Random(20261103).NextBytes(buffer);
            var sw = new Stopwatch();
            sw.Start();
            uint crc = Crc32.Compute(buffer);
            sw.Stop();
            if (crc == 0) return sw.Elapsed.TotalMilliseconds;    // crc 被消费一次，防优化
            return sw.Elapsed.TotalMilliseconds;
        }

        private static double ElapsedMs(Action a)
        {
            var sw = new Stopwatch();
            sw.Start();
            a();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        private static string Bit(bool b) => b ? "1" : "0";

        /// <summary>只落盘。崩溃风险存在的步骤前后都要调它：没有回执与"跑一半死了"必须能区分。</summary>
        private void WriteReceipt()
        {
            string text = string.Join(Environment.NewLine, _log);
            try
            {
                string dir = Path.Combine(Globals.ProjectFolder, "Saved");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, ResultName), text, Encoding.UTF8);
            }
            catch (Exception e)
            {
                FlaxEngine.Debug.LogWarning("PLANET_SPIKE_RESULT_WRITE_FAILED " + e.GetType().Name);
            }
        }

        private void Finish(string markerPath, bool passed)
        {
            Say("passed=" + Bit(passed));
            WriteReceipt();
            // 两条出口（同步判完 / 帧步进判完）都必须吃掉标记：留着就等于"每次启动自动重跑这套活"
            string marker = _markerPath ?? markerPath;
            if (marker != null)
            {
                try { File.Delete(marker); } catch (Exception) { }
            }
        }
    }
}
