using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FlaxEngine;
using Horizon.Planet;

namespace HundunWorld.Game.Planet
{
    /// <summary>
    /// #22 的 ①：把 <see cref="PlanetShellPlanner"/> + <see cref="PlanetShellStreamer"/> 接进渲染路径。
    /// <para>每帧只做投放器允许的那几块：给一块 tile 现场造一个 virtual <see cref="Model"/>、
    /// <c>UpdateMesh</c> 写顶点（含它需要的裙边），挂一台 <see cref="StaticModel"/>；
    /// 离开计划的块当场 <c>Destroy</c>。§13.35 的结论在这里仍然成立——写网格是主线程的活，
    /// 所以预算必须按"每帧多少顶点"来卡，而不是"能不能后台"。</para>
    /// <para>它同时是测量仪：回执里既有投放器的账（built/evicted/deferred/peak/budget），
    /// 也有引擎侧的账（活着的模型台数、每帧驱动耗时、帧长），两头必须对得上——
    /// <c>engine_models == BuiltInPlan</c> 是"没漏删也没重复建"唯一的证据。</para>
    /// <para>触发：<c>&lt;Project&gt;/Saved/planet_shell.on</c>，内容
    /// <c>"&lt;lat&gt; &lt;lon&gt; &lt;quads&gt; &lt;maxLevel&gt; &lt;allowedPixels&gt; &lt;budgetVerts&gt; [maxFrames] [moveEveryFrames] [moveStepDeg] [stageRadiusCm]"</c>。</para>
    /// </summary>
    public class PlanetShellDriver : Script
    {
        private const string BuildTag = "planet_shell_driver_v1";
        private const string MarkerName = "planet_shell.on";
        private const string ResultName = "planet_shell.result.txt";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---- 策略参数（marker 可改，不用重编）----
        public double LatDeg = 30.0;
        public double LonDeg = 120.0;
        public int Quads = 8;
        public int MaxLevel = 5;
        public double AllowedPixels = 1.0;
        public long BudgetVertsPerFrame = 900;
        public int MaxFrames = 600;
        public int MoveEveryFrames = 0;        // 0 = 玩家不动；>0 就定期挪一下，把驱逐这条路真的走一遍
        public double MoveStepDeg = 0.0;
        public double StageRadiusCm = 600.0;

        // ---- 运行态 ----
        private readonly List<string> _log = new List<string>();
        private readonly Dictionary<RegionKey, StaticModel> _live = new Dictionary<RegionKey, StaticModel>();
        private PlanetShellPlanner _planner;
        private PlanetShellStreamer _streamer;
        private PlanetTerrain _terrain;
        private string _markerPath;
        private Camera _cameraRef;
        private double _lat, _lon;
        private int _frame, _batches, _peakBatch;
        private long _vertsWritten, _skirtVerts, _tileVerts;
        private double _workSumMs, _workMaxMs, _frameSumMs, _frameMaxMs;
        private long _modelCreates, _modelDestroys, _noMeshFailures;
        private bool _started, _finished;

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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                sw.Stop();
                Say("tick_crash_type=" + e.GetType().Name);
                Say("tick_crash_message=" + OneLine(truncated(e.Message, 200)));
                Finish(false);
                return;
            }
            sw.Stop();
            _workSumMs += sw.Elapsed.TotalMilliseconds;
            _workMaxMs = Math.Max(_workMaxMs, sw.Elapsed.TotalMilliseconds);
            double frameMs = Time.DeltaTime * 1000.0;
            _frameSumMs += frameMs;
            _frameMaxMs = Math.Max(_frameMaxMs, frameMs);
            if (_frame >= MaxFrames) Finish(VerifyAndReport());
        }

        private void Start()
        {
            Say("build_tag=" + BuildTag);
            Say(string.Format(Inv,
                "policy lat={0:0.####} lon={1:0.####} quads={2} max_level={3} allowed_pixels={4:0.###} " +
                "budget_verts={5} max_frames={6} move_every={7} move_step_deg={8:0.###} stage_radius={9:0}",
                LatDeg, LonDeg, Quads, MaxLevel, AllowedPixels, BudgetVertsPerFrame, MaxFrames,
                MoveEveryFrames, MoveStepDeg, StageRadiusCm));

            _planner = new PlanetShellPlanner(PlanetConstants.PlanetRadiusCm, Quads, MaxLevel,
                                              AllowedPixels, 60.0, 1080.0);
            _streamer = new PlanetShellStreamer(BudgetVertsPerFrame);
            // 常数高程：这条路上要量的是"壳的投放与回收"，起伏会把裙边深度与弦高一起改掉，
            // 混进来之后 peak 顶点数与预算的对账就不是纯 LOD 的账了。
            _terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, new FlatHeight());

            BuildStage();
            _lat = LatDeg; _lon = LonDeg;
            ResyncPlan("initial");
            _started = true;
        }

        /// <summary>相机与灯由脚本自己搭：关卡里只留一台脚本宿主，免得两处相机抢主相机。</summary>
        private void BuildStage()
        {
            var host = Actor.Parent != null ? Actor.Parent : Actor;

            var camera = new Camera { Name = "ShellCamera" };
            camera.Parent = host;
            // 整颗星球缩到 stage_radius，相机在 -2.4× 半径处：能看见大半面，也看得见块与块之间的缝
            float dist = (float)(StageRadiusCm * 2.4);
            camera.Transform = new Transform(new Vector3(0, 0, -dist), Quaternion.Identity);
            camera.NearPlane = (float)(StageRadiusCm * 0.01);
            camera.FarPlane = dist * 8f;
            _cameraRef = camera;
            Say(string.Format(Inv, "camera_dist={0:0} near={1:0.###} far={2:0} has_scene={3}",
                              dist, camera.NearPlane, camera.FarPlane, Bit(camera.HasScene)));

            var light = new DirectionalLight { Name = "ShellLight" };
            light.Parent = host;
            light.Transform = new Transform(new Vector3(0, 0, 0), Quaternion.Euler(-30f, 20f, 0f));
            Say("light_has_scene=" + Bit(light.HasScene));
            _hostRef = host;
        }

        private Actor _hostRef;

        private void ResyncPlan(string why)
        {
            var dir = Geodetic.DirectionFromLatLon(_lat * Math.PI / 180.0, _lon * Math.PI / 180.0);
            var plan = _planner.Build(dir);
            _streamer.Sync(plan);
            Say(string.Format(Inv, "plan[{0}] lat={1:0.####} lon={2:0.####} tiles={3} verts={4} skirts={5} deepest={6} truncated={7}",
                              why, _lat, _lon, plan.Tiles.Count, plan.TotalVertices, plan.SkirtCount,
                              DeepestLevel(plan), Bit(plan.Truncated)));
        }

        private static int DeepestLevel(ShellPlan plan)
        {
            int d = 0;
            foreach (var t in plan.Tiles) d = Math.Max(d, t.Key.Level);
            return d;
        }

        private void Tick()
        {
            _frame++;
            if (MoveEveryFrames > 0 && _frame % MoveEveryFrames == 0 && MoveStepDeg != 0.0)
            {
                _lon += MoveStepDeg;
                ResyncPlan("move_at_frame_" + _frame);
            }

            var batch = _streamer.Step(out var evict);

            foreach (var key in evict)
            {
                if (_live.TryGetValue(key, out var model))
                {
                    if (model != null)
                    {
                        // Flax 的销毁入口是静态的 Object.Destroy(Object, timeOffset)；
                        // 虚拟 Model 只丢引用不删资产（它从没落过盘），交给 GC。
                        FlaxEngine.Object.Destroy(model, 0.0f);
                        _modelDestroys++;
                    }
                    _live.Remove(key);
                }
                else
                {
                    // 投放器交出来的块一定曾经建过；对不上就是两边账本分叉了
                    Say("evict_unknown key=" + key + " at_frame=" + _frame);
                }
            }

            if (batch.Count > 0) _batches++;
            if (batch.Count > _peakBatch) _peakBatch = batch.Count;
            foreach (var tile in batch) BuildTile(tile);
        }

        /// <summary>一块 tile（含它的裙边）现场造一个 virtual model 挂上去。写网格是主线程的活，预算已经在外层卡过了。</summary>
        private void BuildTile(ShellTile tile)
        {
            var mesh = PlanetRegionMesh.BuildTile(tile.Key, Quads, _terrain);
            double k = StageRadiusCm / PlanetConstants.PlanetRadiusCm;

            int tileVerts = mesh.Positions.Length;
            int skirtVerts = 0;
            var skirts = new List<RegionMesh>(tile.Skirts.Count);
            if (tile.Skirts.Count > 0)
            {
                // 深度按**这条边**量，不按整块算：纯球面弦高只算半径那一项，真实地形自己的
                // 短波弯曲会把它顶上去（§13.39：GEBCO 上 60 块抽样里 2 块超 1.4 倍）。
                foreach (var edge in tile.Skirts)
                {
                    double depth = PlanetRegionSeams.SkirtDepthForEdgeCm(tile.Key, Quads, _terrain, edge);
                    var s = PlanetRegionSeams.BuildSkirt(tile.Key, Quads, _terrain, edge, depth);
                    skirts.Add(s);
                    skirtVerts += s.Positions.Length;
                }
            }

            int total = tileVerts + skirtVerts;
            var pos = new Float3[total];
            var nrm = new Float3[total];
            var idx = new UInt32[mesh.Triangles.Length + (skirts.Count > 0 ? SumTris(skirts) : 0)];

            for (int i = 0; i < tileVerts; i++)
            {
                var p = mesh.Positions[i];
                double len = p.Length <= 0 ? 1.0 : p.Length;
                pos[i] = new Float3((float)(p.X * k), (float)(p.Y * k), (float)(p.Z * k));
                nrm[i] = new Float3((float)(p.X / len), (float)(p.Y / len), (float)(p.Z / len));
            }
            for (int t = 0; t < mesh.Triangles.Length; t++) idx[t] = (UInt32)mesh.Triangles[t];

            int vo = tileVerts, io = mesh.Triangles.Length;
            foreach (var s in skirts)
            {
                for (int i = 0; i < s.Positions.Length; i++)
                {
                    var p = s.Positions[i];
                    double len = p.Length <= 0 ? 1.0 : p.Length;
                    pos[vo + i] = new Float3((float)(p.X * k), (float)(p.Y * k), (float)(p.Z * k));
                    nrm[vo + i] = new Float3((float)(p.X / len), (float)(p.Y / len), (float)(p.Z / len));
                }
                for (int t = 0; t < s.Triangles.Length; t++) idx[io + t] = (UInt32)(s.Triangles[t] + vo);
                vo += s.Positions.Length;
                io += s.Triangles.Length;
            }

            var model = Content.CreateVirtualAsset<Model>();
            model.SetupLODs(new[] { 1 });
            model.SetupMaterialSlots(1);
            var gm = model.GetMesh(0, 0) as Mesh;
            if (gm == null)
            {
                _noMeshFailures++;
                Say("tile_no_mesh key=" + tile.Key);
                return;
            }
            gm.UpdateMesh(pos, idx, nrm);
            new MeshAccessor().UpdateMesh(gm, true);

            var actor = new StaticModel { Name = "shell_" + tile.Key.Face + "_" + tile.Key.Level + "_" + tile.Key.X + "_" + tile.Key.Y };
            actor.Parent = _hostRef;
            actor.Model = model;

            _live[tile.Key] = actor;
            _modelCreates++;
            _vertsWritten += total;
            _tileVerts += tileVerts;
            _skirtVerts += skirtVerts;
        }

        private static int SumTris(List<RegionMesh> skirts)
        {
            int n = 0;
            foreach (var s in skirts) n += s.Triangles.Length;
            return n;
        }

        private bool VerifyAndReport()
        {
            Say(string.Format(Inv,
                "frames={0} batches={1} peak_batch={2} model_creates={3} model_destroys={4} live={5}",
                _frame, _batches, _peakBatch, _modelCreates, _modelDestroys, _live.Count));
            Say(string.Format(Inv, "verts_written={0} tile_verts={1} skirt_verts={2} skirt_share={3:0.##}%",
                              _vertsWritten, _tileVerts, _skirtVerts,
                              _vertsWritten == 0 ? 0 : 100.0 * _skirtVerts / _vertsWritten));
            Say(string.Format(Inv, "driver_work_ms avg={0:0.####} max={1:0.####} | engine_frame_ms avg={2:0.###} max={3:0.###}",
                              _workSumMs / Math.Max(1, _frame), _workMaxMs,
                              _frameSumMs / Math.Max(1, _frame), _frameMaxMs));
            Say("streamer " + _streamer.Describe());

            // 引擎里的台数必须等于投放器在册的块数：多一台就是漏删，少一台就是没建上
            bool ledger = _live.Count == _streamer.BuiltInPlan;
            var dir = Geodetic.DirectionFromLatLon(_lat * Math.PI / 180.0, _lon * Math.PI / 180.0);
            var plan = _planner.Build(dir);
            int notInPlan = 0;
            foreach (var key in _live.Keys)
            {
                bool found = false;
                foreach (var t in plan.Tiles) if (t.Key.Equals(key)) { found = true; break; }
                if (!found) notInPlan++;
            }
            Say(string.Format(Inv, "final: live={0} built_in_plan={1} settled={2} live_not_in_plan={3} peak_frame_verts={4} budget={5}",
                              _live.Count, _streamer.BuiltInPlan, Bit(_streamer.IsSettled), notInPlan,
                              _streamer.PeakFrameVertices, BudgetVertsPerFrame));

            // 三条等式各自独立：投放器说建了几块 ↔ 引擎里真的建了几台；说回收了几块 ↔ 真的删了几台；
            // 在册块数 ↔ 活着的台数。任何一条不成立都是两边账本分叉，而不是"数错了"。
            bool creates = _modelCreates == _streamer.Built;
            bool destroys = _modelDestroys == _streamer.Evicted;
            bool liveMath = _live.Count == _modelCreates - _modelDestroys;
            bool budget = _streamer.PeakFrameVertices <= BudgetVertsPerFrame;

            Say(string.Format(Inv, "equations creates={0}({1}/{2}) destroys={3}({4}/{5}) live_math={6}({7}={8}-{9}) budget={10}",
                              Bit(creates), _modelCreates, _streamer.Built,
                              Bit(destroys), _modelDestroys, _streamer.Evicted,
                              Bit(liveMath), _live.Count, _modelCreates, _modelDestroys, Bit(budget)));

            bool ok = ledger && creates && destroys && liveMath && budget && notInPlan == 0 && _live.Count > 0
                      && _noMeshFailures == 0;
            if (!ledger) Say("fail_reason=live_count_mismatch");
            if (!creates) Say("fail_reason=create_count_mismatch");
            if (!destroys) Say("fail_reason=destroy_count_mismatch");
            if (!liveMath) Say("fail_reason=live_math");
            if (!budget) Say("fail_reason=budget_exceeded");
            if (notInPlan != 0) Say("fail_reason=live_tiles_not_in_plan");
            if (_live.Count == 0) Say("fail_reason=nothing_built");
            if (_noMeshFailures != 0) Say("fail_reason=no_mesh_tiles=" + _noMeshFailures);
            Say("verdict=" + (ok ? "SHELL_STREAMER_RUNS_IN_ENGINE" : "SHELL_PROBLEM"));
            return ok;
        }

        // ==================== 工具 ====================

        private void ParseMarker(string path)
        {
            string text = "";
            try { text = File.ReadAllText(path).Trim(); } catch (Exception) { }
            string[] p = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            try
            {
                if (p.Length >= 2) { LatDeg = double.Parse(p[0], Inv); LonDeg = double.Parse(p[1], Inv); }
                if (p.Length >= 3) Quads = int.Parse(p[2], Inv);
                if (p.Length >= 4) MaxLevel = int.Parse(p[3], Inv);
                if (p.Length >= 5) AllowedPixels = double.Parse(p[4], Inv);
                if (p.Length >= 6) BudgetVertsPerFrame = long.Parse(p[5], Inv);
                if (p.Length >= 7) MaxFrames = int.Parse(p[6], Inv);
                if (p.Length >= 8) MoveEveryFrames = int.Parse(p[7], Inv);
                if (p.Length >= 9) MoveStepDeg = double.Parse(p[8], Inv);
                if (p.Length >= 10) StageRadiusCm = double.Parse(p[9], Inv);
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

        private void Finish(bool passed)
        {
            if (_finished) return;
            _finished = true;
            Say("passed=" + Bit(passed));
            try
            {
                string dir = Path.Combine(Globals.ProjectFolder, "Saved");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, ResultName), string.Join(Environment.NewLine, _log), Encoding.UTF8);
            }
            catch (Exception) { }
            try { File.Delete(_markerPath); } catch (Exception) { }
        }

        private static string Bit(bool b) => b ? "1" : "0";
        private static string OneLine(string s) => s == null ? "" : s.Replace("\r", " ").Replace("\n", " ").Trim();
        private static string truncated(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));
        private static string FirstFrames(string stack, int n)
        {
            if (string.IsNullOrEmpty(stack)) return "";
            var lines = stack.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var take = new List<string>();
            for (int i = 0; i < lines.Length && take.Count < n; i++) take.Add(OneLine(lines[i]));
            return string.Join(" | ", take);
        }

        private sealed class FlatHeight : IHeightSource
        {
            public int SourceVersion => 1;
            public double SampleElevationCm(in PlanetVector3 dir) => 0.0;
        }
    }
}
