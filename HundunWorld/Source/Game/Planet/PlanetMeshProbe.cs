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
    /// #17 的第三条：<b>远/中景壳能不能在运行时程序化改网格</b>——不靠"每块 tile 都事先导一次资产"。
    /// <para>
    /// 托管面上有两个候选入口，这里一次跑完并把两条都量出来：
    /// <b>template</b> 先导入一份模板模型，运行时拿到 <see cref="Mesh"/> 再 <c>UpdateMesh</c> 改写顶点；
    /// <b>build</b> 连模板都不要，<c>new Model()</c> + <c>SetupLODs</c> 现场造一个内存资产挂到 StaticModel。
    /// </para>
    /// <para>
    /// 判据不靠"接口没报错"：<c>control</c>（不动）与 <c>rewrite_same</c>（用同一份几何重写一遍）
    /// 必须截图几乎一致，而 <c>grow</c>（半径 ×2）必须显著变大——这样"变大"才归因于几何改动，
    /// 而不是"我调了个 API"。回执只写引擎侧状态；像素差在离线脚本里量。
    /// </para>
    /// <para>触发：<c>&lt;Project&gt;/Saved/planet_mesh.on</c>，内容 <c>"&lt;mode&gt; [模板资产绝对路径]"</c>；
    /// 模式：<c>control | rewrite_same | grow | template_probe | build | build_empty</c>。</para>
    /// </summary>
    public class PlanetMeshProbe : Script
    {
        private const string BuildTag = "planet_mesh_v2";
        private const string MarkerName = "planet_mesh.on";
        private const string ResultName = "planet_mesh.result.txt";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private const int Rings = 24;
        private const int Sectors = 32;
        private const float Radius = 150f;
        private const float StageWorldSize = 300f;
        private const float CameraDistance = 1400f;
        private const int ReportAfterFrames = 20;
        private const double StageSpan = 1200.0;   // 拼图归一化后的半径（引擎在这个量级上绘制正常）

        private readonly List<string> _log = new List<string>();
        private string[] _parts = new string[0];
        private Camera _cameraRef;
        private string _markerPath;
        private int _ticks;
        private bool _done;

        public override void OnStart()
        {
            _markerPath = MarkerPath();
            if (_markerPath == null) return;

            string mode = "control";
            string assetPath = Path.Combine(Globals.ProjectFolder, "Content", "Probe", "planet_probe_sphere.flax");
            try
            {
                var text = File.ReadAllText(_markerPath).Trim();
                var parts = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                _parts = parts;
                if (parts.Length >= 1) mode = parts[0].ToLowerInvariant();
                if (parts.Length >= 2) assetPath = parts[1];
            }
            catch (Exception e) { Say("marker_parse_error=" + e.GetType().Name); }

            Say("build_tag=" + BuildTag);
            Say("mode=" + mode);
            Say("template_geometry rings=" + Rings + " sectors=" + Sectors +
                " radius=" + Radius.ToString(Inv) + " verts=" + ((Rings + 1) * (Sectors + 1)) +
                " tris=" + (Rings * Sectors * 2));

            BuildStage();

            try
            {
                if (mode == "empty_stage")
                {
                    Say("verdict=STAGE_ONLY_NO_MODEL");
                }
                else if (mode == "build" || mode == "build_empty")
                {
                    RunBuiltModel(mode == "build");
                }
                else if (mode == "virtual_multi")
                {
                    RunVirtualGrid(3, 1, Radius, 340f);
                }
                else if (mode == "virtual_grid")
                {
                    RunVirtualGrid(3, 3, 90f, 220f);
                }
                else if (mode == "virtual_globe")
                {
                    RunVirtualGlobe();
                }
                else if (mode == "seam")
                {
                    RunSeam();
                }
                else if (mode.StartsWith("virtual_"))
                {
                    RunVirtual(mode);
                }
                else
                {
                    RunTemplate(mode, assetPath);
                }
            }
            catch (Exception e)
            {
                Say("crash_type=" + e.GetType().Name);
                Say("crash_message=" + OneLine(truncated(e.Message, 200)));
                Say("crash_stack=" + OneLine(FirstFrames(e.StackTrace, 3)));
                Say("verdict=CRASH");
            }

            Say("probe_state=READY");
        }

        public override void OnUpdate()
        {
            if (_done || ++_ticks < ReportAfterFrames) return;
            _done = true;
            Finish();
            try { File.Delete(_markerPath); } catch (Exception) { }
        }

        /// <summary>相机与目标由脚本自己搭：关卡里只留灯与天空，免得两处相机抢"主相机"。
        /// 注意 spawn 出来的 Actor 不挂父就 <c>HasScene=False</c>，画面里根本不会出现它。</summary>
        private void BuildStage()
        {
            var host = Actor.Parent != null ? Actor.Parent : Actor;

            var light = new DirectionalLight { Name = "ProbeLight" };
            light.Parent = host;
            light.Transform = new Transform(new Vector3(0, 400, -400),
                                            Quaternion.Euler(-35f, 25f, 0f));
            Say("light_spawned=" + Bit(light != null) + " light_has_scene=" + Bit(light.HasScene));

            var camera = new Camera { Name = "ProbeCamera" };
            camera.Parent = host;
            camera.Transform = new Transform(new Vector3(0, 0, -CameraDistance), Quaternion.Identity);
            camera.FarPlane = 20000f;
            Say(string.Format(Inv, "camera_pos={0:0} {1:0} {2:0} far={3:0}",
                              camera.Transform.Translation.X, camera.Transform.Translation.Y,
                              camera.Transform.Translation.Z, camera.FarPlane));
            Say("camera_near=" + camera.NearPlane.ToString(Inv) + " fov=" + camera.FieldOfView.ToString(Inv) +
                " camera_has_scene=" + Bit(camera.HasScene));
            _cameraRef = camera;

            var target = new StaticModel { Name = "ProbeTarget" };
            target.Parent = host;
            target.Transform = new Transform(Vector3.Zero, Quaternion.Identity);
            Say("target_spawned=" + Bit(target != null) + " target_has_scene=" + Bit(target.HasScene));
            _targetRef = target;
        }

        private StaticModel _targetRef;

        private void RunTemplate(string mode, string assetPath)
        {
            Say("template_asset=" + assetPath + " exists=" + Bit(File.Exists(assetPath)));
            Model model = null;
            try { model = Content.Load<Model>(assetPath); }
            catch (Exception e) { Say("template_load_error=" + e.GetType().Name + " " + OneLine(truncated(e.Message, 140))); }
            if (model == null)
            {
                Say("template_model=null");
                Say("verdict=NO_TEMPLATE_ASSET");
                return;
            }
            Say(string.Format(Inv, "template_lods={0} loaded_lods={1} slots={2}",
                              model.LODsCount, model.LoadedLODs, model.MaterialSlotsCount));

            var meshBase = model.GetMesh(0, 0);
            var mesh = meshBase as Mesh;
            Say("template_mesh=" + (meshBase == null ? "null" : meshBase.GetType().Name) + " as_mesh=" + Bit(mesh != null));
            if (meshBase == null) { Say("verdict=NO_MESH_ON_TEMPLATE"); return; }
            Say("before " + MeshState(meshBase));

            if (mode == "template_probe")
            {
                ProbeMesh(meshBase);
                Say("verdict=TEMPLATE_PROBE_DONE");
                return;
            }
            if (mode != "control" && mode != "rewrite_same" && mode != "grow")
            {
                Say("unknown_mode=" + mode);
                Say("verdict=BAD_MODE");
                return;
            }

            // 导入器会按它自己的单位约定放大几何（实测这只球进来是 300 m 而非 150 cm），
            // 所以"模板通路"的所有跑法都先把 actor 缩放到同一个屏幕尺寸，再把写入半径换算回网格单位：
            // 半径 ×2 才是"只有几何变了"，而不是"缩放参数变了"。
            float maxDim = Math.Max(meshBase.Box.Size.X, Math.Max(meshBase.Box.Size.Y, meshBase.Box.Size.Z));
            float importUnit = maxDim > 1e-4f ? maxDim / (2f * Radius) : 1f;
            float actorScale = maxDim > 1e-4f ? StageWorldSize / maxDim : 1f;
            Say(string.Format(Inv, "imported_max_dim_cm={0:0.###} import_unit={1:0.####} actor_scale={2:0.#####}",
                              maxDim, importUnit, actorScale));
            _targetRef.LocalTransform = new Transform(Vector3.Zero, Quaternion.Identity,
                                                     new Vector3(actorScale, actorScale, actorScale));

            if (mode == "control")
            {
                _targetRef.Model = model;
                Say("target_model_assigned=" + Bit(_targetRef.Model != null));
                Say("verdict=CONTROL_IN_PLACE");
                return;
            }

            Float3[] pos, nrm;
            UInt32[] idx;
            bool grow = mode == "grow";
            float meshRadius = (grow ? Radius * 2f : Radius) * importUnit;
            Sphere(meshRadius, out pos, out nrm, out idx);
            Say("rewrite_verts=" + pos.Length + " tris=" + (idx.Length / 3) + " mesh_radius=" +
                meshRadius.ToString(Inv));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (mesh != null)
                {
                    mesh.UpdateMesh(pos, idx, nrm);
                    sw.Stop();
                    Say("update_mesh_api=Mesh.UpdateMesh ms=" + sw.Elapsed.TotalMilliseconds.ToString("0.###", Inv));
                }
                else
                {
                    Say("update_skipped=meshBase_is_not_Mesh");
                }
            }
            catch (Exception e)
            {
                Say("update_error=" + e.GetType().Name + " " + OneLine(truncated(e.Message, 160)));
            }

            Say("after " + MeshState(meshBase));
            TryMeshAccessor(meshBase);

            _targetRef.Model = model;
            Say("target_model_assigned=" + Bit(_targetRef.Model != null));
            Say("verdict=" + (grow ? "GROW_WRITTEN" : "REWRITE_SAME_WRITTEN"));
        }

        /// <summary>
        /// 流式远壳真正的考验：<b>很多个 virtual 网格同时活着</b>，以及<b>每块 tile 的建+传成本</b>。
        /// 一次跑完 cols×rows 个，成本按 tile 摊平记进回执。
        /// </summary>
        private void RunVirtualGrid(int cols, int rows, float radius, float spacing)
        {
            var host = Actor.Parent != null ? Actor.Parent : Actor;
            int created = 0, inScene = 0, meshed = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string failure = null;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    try
                    {
                        var model = Content.CreateVirtualAsset<Model>();
                        model.SetupLODs(new[] { 1 });
                        model.SetupMaterialSlots(1);
                        var mesh = model.GetMesh(0, 0) as Mesh;
                        if (mesh == null) { failure = "no_mesh_at_" + r + "_" + c; break; }
                        Float3[] pos, nrm;
                        UInt32[] idx;
                        Sphere(radius, out pos, out nrm, out idx);
                        mesh.UpdateMesh(pos, idx, nrm);
                        new MeshAccessor().UpdateMesh(mesh, true);
                        meshed++;
                        var actor = new StaticModel { Name = "ProbeTile_" + r + "_" + c, Model = model };
                        actor.Parent = host;
                        actor.Transform = new Transform(
                            new Vector3((c - (cols - 1) * 0.5f) * spacing,
                                        (r - (rows - 1) * 0.5f) * spacing, 0f),
                            Quaternion.Identity);
                        created++;
                        if (actor.HasScene) inScene++;
                    }
                    catch (Exception e)
                    {
                        failure = e.GetType().Name + "@" + r + "_" + c + " " + truncated(e.Message, 90);
                        break;
                    }
                }
                if (failure != null) break;
            }
            sw.Stop();
            int want = cols * rows;
            Say(string.Format(Inv, "grid_want={0} models_created={1} meshes_written={2} actors_in_scene={3}",
                              want, created, meshed, inScene));
            Say(string.Format(Inv, "grid_total_ms={0:0.###} per_tile_ms={1:0.###}",
                              sw.Elapsed.TotalMilliseconds, created > 0 ? sw.Elapsed.TotalMilliseconds / created : 0));
            Say("grid_radius=" + radius.ToString(Inv) + " spacing=" + spacing.ToString(Inv) +
                " verts_per_tile=" + ((Rings + 1) * (Sectors + 1)) + " tris_per_tile=" + (Rings * Sectors * 2));
            if (failure != null) Say("grid_failure=" + OneLine(failure));
            Say("verdict=" + (failure == null && created == want && inScene == want ? "GRID_OK" : "GRID_PROBLEM"));
        }

        /// <summary>
        /// 引擎给的报错是"只有 virtual 模型能在运行时改写"，而 <c>Content.CreateVirtualAsset&lt;T&gt;()</c>
        /// 正是造 virtual 资产的入口——这条能走通，远/中景 shell 就能按 tile 流式改写，不依赖"每块先导入一次"。
        /// <c>virtual_same</c> 与 <c>virtual_grow</c> 只差半径一倍，<c>virtual_empty</c> 是不写几何的反证。
        /// </summary>
        private void RunVirtual(string mode)
        {
            Model model = null;
            try
            {
                model = Content.CreateVirtualAsset<Model>();
                Say("virtual_created=" + Bit(model != null) +
                    (model == null ? "" : " is_virtual=" + Bit(model.IsVirtual) + " path=" + OneLine(model.Path)));
            }
            catch (Exception e)
            {
                Say("virtual_create_error=" + e.GetType().Name + " " + OneLine(truncated(e.Message, 160)));
            }
            if (model == null) { Say("verdict=NO_VIRTUAL_MODEL"); return; }

            try
            {
                bool lods = model.SetupLODs(new[] { 1 });
                model.SetupMaterialSlots(1);
                Say("virtual_setup_lods=" + Bit(lods) + " lods_count=" + model.LODsCount +
                    " slots=" + model.MaterialSlotsCount);
            }
            catch (Exception e)
            {
                Say("virtual_setup_error=" + e.GetType().Name + " " + OneLine(truncated(e.Message, 160)));
            }

            var viaGet = model.GetMesh(0, 0) as Mesh;
            Mesh viaLods = null;
            try
            {
                if (model.LODs != null && model.LODs.Length > 0 && model.LODs[0].Meshes != null &&
                    model.LODs[0].Meshes.Length > 0)
                    viaLods = model.LODs[0].Meshes[0];
            }
            catch (Exception e) { Say("virtual_lods_walk_error=" + e.GetType().Name); }
            Say("virtual_mesh_getmesh0=" + Bit(viaGet != null) + " virtual_mesh_viaLods=" + Bit(viaLods != null));
            var mesh = viaGet ?? viaLods;
            if (mesh == null) { Say("verdict=NO_VIRTUAL_MESH_SLOT"); return; }

            Say("virtual_before " + MeshState(mesh));
            // reuse_* 连写两次，只有"最后一次"留在屏上才算证明流式改写在重新上传：
            // large 与 small 用同一批 API、只差写入顺序，两张截图必须一大一小。
            var radii = new List<float>();
            if (mode == "virtual_same") radii.Add(Radius);
            else if (mode == "virtual_grow") radii.Add(Radius * 2f);
            else if (mode == "virtual_reuse_large") { radii.Add(Radius); radii.Add(Radius * 2f); }
            else if (mode == "virtual_reuse_small") { radii.Add(Radius * 2f); radii.Add(Radius); }
            for (int step = 0; step < radii.Count; step++)
            {
                Float3[] pos, nrm;
                UInt32[] idx;
                Sphere(radii[step], out pos, out nrm, out idx);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    mesh.UpdateMesh(pos, idx, nrm);
                    sw.Stop();
                    Say(string.Format(Inv, "virtual_update_step={0} radius={1:0} ms={2:0.###}",
                                      step, radii[step], sw.Elapsed.TotalMilliseconds));
                }
                catch (Exception e)
                {
                    sw.Stop();
                    Say("virtual_update_error step=" + step + " " + e.GetType().Name + " " +
                        OneLine(truncated(e.Message, 160)));
                }
                Say("virtual_after_step" + step + " " + MeshState(mesh));
            }
            if (radii.Count == 0) Say("virtual_empty=skipped_update");
            else TryMeshAccessor(mesh);

            _targetRef.Model = model;
            Say("target_model_assigned=" + Bit(_targetRef.Model != null));
            Say("verdict=" + mode.ToUpperInvariant());
        }

        /// <summary>不导入任何资产：new Model() + SetupLODs 现场造一个内存模型，看它画不画得出来。</summary>
        private void RunBuiltModel(bool withGeometry)
        {
            Model model = null;
            try
            {
                model = new Model();
                Say("built_model_new=" + Bit(model != null));
                bool lods = model.SetupLODs(new[] { 1 });
                Say("built_setup_lods=" + Bit(lods) + " lods_count=" + model.LODsCount +
                    " loaded_lods=" + model.LoadedLODs);
                model.SetupMaterialSlots(1);
                Say("built_slots=" + model.MaterialSlotsCount);
            }
            catch (Exception e)
            {
                Say("built_create_error=" + e.GetType().Name + " " + OneLine(truncated(e.Message, 160)));
                Say("verdict=BUILT_MODEL_UNAVAILABLE");
                return;
            }

            var viaGet = model.GetMesh(0, 0) as Mesh;
            Mesh viaLods = null;
            try
            {
                if (model.LODs != null && model.LODs.Length > 0 && model.LODs[0].Meshes != null &&
                    model.LODs[0].Meshes.Length > 0)
                    viaLods = model.LODs[0].Meshes[0];
            }
            catch (Exception e) { Say("built_lods_walk_error=" + e.GetType().Name); }
            Say("built_mesh_getmesh0=" + Bit(viaGet != null) + " built_mesh_viaLods=" + Bit(viaLods != null));

            var mesh = viaGet ?? viaLods;
            if (mesh == null)
            {
                Say("verdict=BUILT_MODEL_NO_MESH_SLOT");
                return;
            }
            Say("before " + MeshState(mesh));

            if (withGeometry)
            {
                Float3[] pos, nrm;
                UInt32[] idx;
                Sphere(Radius, out pos, out nrm, out idx);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    mesh.UpdateMesh(pos, idx, nrm);
                    sw.Stop();
                    Say("built_update_ms=" + sw.Elapsed.TotalMilliseconds.ToString("0.###", Inv));
                }
                catch (Exception e)
                {
                    Say("built_update_error=" + e.GetType().Name + " " + OneLine(truncated(e.Message, 160)));
                }
                Say("after " + MeshState(mesh));
                TryMeshAccessor(mesh);
            }
            else
            {
                Say("built_empty=skipped_update");
            }

            _targetRef.Model = model;
            Say("target_model_assigned=" + Bit(_targetRef.Model != null));
            Say("verdict=" + (withGeometry ? "BUILT_MODEL_WITH_GEOMETRY" : "BUILT_MODEL_EMPTY_TWIN"));
        }

        /// <summary>把 MeshAccessor 那条通路能量到的东西都记下来：它才知道要不要走"缓冲区直传"。</summary>
        private void ProbeMesh(MeshBase mesh)
        {
            Say("probe_vertex_count=" + mesh.VertexCount + " triangle_count=" + mesh.TriangleCount +
                " index16=" + Bit(mesh.Use16BitIndexBuffer) + " vcol=" + Bit(mesh.HasVertexColors));
            Say("probe_layout=" + mesh.VertexLayout);
            Say("probe_box " + Box(mesh.Box));
            try
            {
                var ib = mesh.DownloadIndexBuffer(false);
                Say("probe_index_buffer=" + (ib == null ? "null" : ib.Length.ToString(Inv)));
            }
            catch (Exception e) { Say("probe_index_error=" + e.GetType().Name); }
            TryMeshAccessor(mesh);
        }

        private void TryMeshAccessor(MeshBase mesh)
        {
            try
            {
                var acc = new MeshAccessor();
                bool loaded = acc.LoadMesh(mesh, false);
                int pc = acc.Positions == null ? -1 : acc.Positions.Length;
                int tc = acc.Triangles == null ? -1 : acc.Triangles.Length;
                Say(string.Format(Inv, "accessor_load={0} positions={1} triangles={2}", Bit(loaded), pc, tc));
                if (pc > 0)
                {
                    var p0 = acc.Positions[0];
                    Say(string.Format(Inv, "accessor_first_pos={0:0.###} {1:0.###} {2:0.###}", p0.X, p0.Y, p0.Z));
                }
                bool updated = acc.UpdateMesh(mesh, true);
                Say("accessor_update=" + Bit(updated) + " then " + MeshState(mesh));
            }
            catch (Exception e)
            {
                Say("accessor_error=" + e.GetType().Name + " " + OneLine(truncated(e.Message, 140)));
            }
        }

        private static string MeshState(MeshBase mesh)
        {
            if (mesh == null) return "mesh=null";
            return string.Format(Inv, "verts={0} tris={1} box={2}",
                                 mesh.VertexCount, mesh.TriangleCount, Box(mesh.Box));
        }

        private static string Box(BoundingBox b) =>
            string.Format(Inv, "[c={0:0.##},{1:0.##},{2:0.##} size={3:0.##},{4:0.##},{5:0.##}]",
                          b.Center.X, b.Center.Y, b.Center.Z, b.Size.X, b.Size.Y, b.Size.Z);

        /// <summary>与离线生成的模板 OBJ 用的是同一套参数化与索引序，rewrite_same 才能和 control 逐像素对齐。</summary>
        private static void Sphere(float radius, out Float3[] positions, out Float3[] normals, out UInt32[] indices)
        {
            int rings = Rings, sectors = Sectors;
            positions = new Float3[(rings + 1) * (sectors + 1)];
            normals = new Float3[positions.Length];
            for (int i = 0; i <= rings; i++)
            {
                double theta = Math.PI * i / rings;
                float st = (float)Math.Sin(theta), ct = (float)Math.Cos(theta);
                for (int j = 0; j <= sectors; j++)
                {
                    double phi = 2.0 * Math.PI * j / sectors;
                    var n = new Float3(st * (float)Math.Cos(phi), ct, st * (float)Math.Sin(phi));
                    int k = i * (sectors + 1) + j;
                    positions[k] = n * radius;
                    normals[k] = n;
                }
            }
            indices = new UInt32[rings * sectors * 6];
            int w = 0;
            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < sectors; j++)
                {
                    uint a = (uint)(i * (sectors + 1) + j);
                    uint b = a + 1;
                    uint c = a + (uint)(sectors + 1);
                    uint d = c + 1;
                    indices[w++] = a; indices[w++] = c; indices[w++] = b;
                    indices[w++] = b; indices[w++] = c; indices[w++] = d;
                }
            }
        }

        /// <summary>
        /// #17 的 ①+②：<b>整颗 10× 星球在一个 virtual 网格里画出来</b>。
        /// <para>
        /// 这里必须整体缩放：ECEF 在 6.37e8 cm 量级上 float 的步长是 1520 cm（§13.21 实测），
        /// 直接把局部厘米塞进渲染Buffer 等于把远壳糊成米到百米级的抖动。
        /// 所以顶点先取方向（双精度归一），再按 <c>stageRadius*(1+elevation/R)</c> 折算成显示半径——
        /// 相对空间与高程比例都保住，坐标量级降到 stageRadius。回执里量的就是这两件事：
        /// 写进 Buffer 的最大坐标（决定 float 精度）与地形起伏是否真的进了几何。
        /// </para>
        /// <para>marker：<c>virtual_globe &lt;lat&gt; &lt;lon&gt; &lt;level&gt; &lt;quadsPerEdge&gt; [stageRadiusCm] [reliefGain]</c></para>
        /// </summary>
        private void RunVirtualGlobe()
        {
            double lat = 30.0, lon = 120.0;
            int level = 3, quads = 8;
            float stageRadius = 600f;
            double reliefGain = 1.0;
            try
            {
                if (_parts.Length >= 3) { lat = double.Parse(_parts[1], Inv); lon = double.Parse(_parts[2], Inv); }
                if (_parts.Length >= 4) level = int.Parse(_parts[3], Inv);
                if (_parts.Length >= 5) quads = int.Parse(_parts[4], Inv);
                if (_parts.Length >= 6) stageRadius = float.Parse(_parts[5], Inv);
                if (_parts.Length >= 7) reliefGain = double.Parse(_parts[6], Inv);
            }
            catch (Exception e) { Say("globe_args_error=" + e.GetType().Name); }
            Say(string.Format(Inv, "globe_request lat={0:0.###} lon={1:0.###} level={2} quads={3} stage_radius={4:0} relief_gain={5:0.###}",
                              lat, lon, level, quads, stageRadius, reliefGain));

            var playerDir = Geodetic.DirectionFromLatLon(lat * Math.PI / 180.0, lon * Math.PI / 180.0);
            var frame = RegionFrame.Build(RegionKey.FromDirection(playerDir, PlanetConstants.DefaultRegionLevel),
                                          PlanetConstants.PlanetRadiusCm);
            var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, new Ramp(frame.East, frame.South));

            var mesh = PlanetRegionMesh.BuildLevel(level, quads, terrain);
            Say(string.Format(Inv, "globe_mesh vertices={0} triangles={1}",
                             mesh.Positions.Length, mesh.TriangleCount));

            int nv = mesh.Positions.Length;
            var pos = new Float3[nv];
            var nrm = new Float3[nv];
            double maxAbs = 0, minRadius = double.MaxValue, maxRadius = 0;
            int radialOk = 0;
            for (int i = 0; i < nv; i++)
            {
                var p = mesh.Positions[i];
                double len = p.Length;
                if (len <= 0) continue;
                double dx = p.X / len, dy = p.Y / len, dz = p.Z / len;
                var dir = new PlanetVector3(dx, dy, dz);
                double el = terrain.ElevationCm(dir);
                double r = stageRadius * (1.0 + reliefGain * el / PlanetConstants.PlanetRadiusCm);
                pos[i] = new Float3((float)(dx * r), (float)(dy * r), (float)(dz * r));
                nrm[i] = new Float3((float)dx, (float)dy, (float)dz);
                maxAbs = Math.Max(maxAbs, r);
                minRadius = Math.Min(minRadius, r);
                maxRadius = Math.Max(maxRadius, r);
                radialOk++;
            }
            var idx = new UInt32[mesh.TriangleCount * 3];
            for (int i = 0; i < idx.Length; i++) idx[i] = (UInt32)mesh.Triangles[i];
            Say(string.Format(Inv, "globe_scaled_radius min={0:0.###} max={1:0.###} relief_px_span_ratio={2:0.#####}",
                              minRadius, maxRadius, (maxRadius - minRadius) / stageRadius));
            Say(string.Format(Inv, "globe_radial_vertices={0}/{1} max_abs_coord_cm={2:0} float32_ulp_at_max_cm={3:0.#####}",
                              radialOk, nv, maxAbs, maxAbs * Math.Pow(2.0, -23.0)));

            var model = Content.CreateVirtualAsset<Model>();
            model.SetupLODs(new[] { 1 });
            model.SetupMaterialSlots(1);
            var gm = model.GetMesh(0, 0) as Mesh;
            if (gm == null) { Say("verdict=GLOBE_NO_VIRTUAL_MESH"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                gm.UpdateMesh(pos, idx, nrm);
                sw.Stop();
                Say(string.Format(Inv, "globe_update_ms={0:0.###}", sw.Elapsed.TotalMilliseconds));
            }
            catch (Exception e)
            {
                sw.Stop();
                Say("globe_update_error=" + e.GetType().Name + " " + OneLine(truncated(e.Message, 160)));
            }
            new MeshAccessor().UpdateMesh(gm, true);
            Say("globe_after " + MeshState(gm));

            // 相机距离按 stageRadius 而不是实际最大半径定：起伏开关两次跑必须用同一个相机，
            // 否则天空/地平线跟着变，截图差出来的就不只是几何。
            float camDist = CameraDistance;
            _cameraRef.Transform = new Transform(new Vector3(0, 0, -camDist), Quaternion.Identity);
            _cameraRef.NearPlane = Math.Max(1f, camDist * 0.01f);
            _cameraRef.FarPlane = camDist * 12f;
            Say(string.Format(Inv, "globe_camera_dist={0:0} near={1:0} far={2:0}",
                              camDist, _cameraRef.NearPlane, _cameraRef.FarPlane));

            _targetRef.Transform = new Transform(Vector3.Zero, Quaternion.Identity);
            _targetRef.Model = model;
            bool reliefExpected = reliefGain > 0;
            Say("verdict=" + (radialOk == nv && gm.VertexCount == nv &&
                              (!reliefExpected || maxRadius > minRadius)
                ? "GLOBE_VIRTUAL_MESH_WRITTEN" : "GLOBE_PROBLEM"));
        }

        /// <summary>
        /// 跨级缝隙实机台（#22 的"缝后残差"）。摆一块粗 tile 与它 UPlus 侧的两块更细邻居，
        /// 这条共边就是真正的裂缝（细侧弦比粗侧弦更贴球面，中间是一个楔形空洞，两侧都不盖它）；
        /// 可选给粗 tile 挂裙边把洞堵住。三种臂由 marker 的 skirtFactor 选：0＝不挂、1.5＝生产公式、0.2＝反证（盖不住）。
        /// <para>高程源必须取常数 0：起伏的幅度是星球半径的 1% 量级，比弦高大一到两个数量级，
        /// 留着它截图里量到的明暗带就不是缝隙了。</para>
        /// <para>marker：<c>seam &lt;level&gt; &lt;quadsCoarse&gt; &lt;quadsFine&gt; &lt;skirtFactor&gt; [相机高度cm]</c></para>
        /// </summary>
        private void RunSeam()
        {
            int level = 3, nc = 2, nf = 8;
            double skirtFactor = 1.5;
            double camFactor = 0.7;             // 视距 = camFactor × 拼图半径（缩放后单位）
            double tiltDeg = 25.0;              // 视线与法线的夹角：缝隙的楔形洞只在掠射角下才朝相机张开
            int flipWind = 0;                   // 1 = 把裙边三角形的绕序反一反（测"墙要朝哪一面才不被剔掉"）
            int relief = 0;                     // 1 = 用岭脊高程源（2 km 振幅 / 20 km 波长），0 = 常数 0
            int depthMode = 0;                  // 0 = 纯弯曲深度（老公式），1 = 按边实测深度（新公式）
            int face = 2, tx = 3, ty = 5;       // 粗块的坐标：岭脊的相位随位置变，要能指到那一条边上
            try
            {
                if (_parts.Length >= 2) level = int.Parse(_parts[1], Inv);
                if (_parts.Length >= 3) nc = int.Parse(_parts[2], Inv);
                if (_parts.Length >= 4) nf = int.Parse(_parts[3], Inv);
                if (_parts.Length >= 5) skirtFactor = double.Parse(_parts[4], Inv);
                if (_parts.Length >= 6) camFactor = double.Parse(_parts[5], Inv);
                if (_parts.Length >= 7) tiltDeg = double.Parse(_parts[6], Inv);
                if (_parts.Length >= 8) flipWind = int.Parse(_parts[7], Inv);
                if (_parts.Length >= 9) relief = int.Parse(_parts[8], Inv);
                if (_parts.Length >= 10) depthMode = int.Parse(_parts[9], Inv);
                if (_parts.Length >= 13) { face = int.Parse(_parts[10], Inv); tx = int.Parse(_parts[11], Inv); ty = int.Parse(_parts[12], Inv); }
            }
            catch (Exception e) { Say("seam_args_error=" + e.GetType().Name); }

            IHeightSource source = relief != 0 ? (IHeightSource)new Ridge() : FlatHeight.Instance;
            var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, source);
            var coarseKey = new RegionKey(face, level, tx, ty);
            var fineLower = new RegionKey(face, level + 1, 2 * (coarseKey.X + 1), 2 * coarseKey.Y);
            var fineUpper = new RegionKey(face, level + 1, 2 * (coarseKey.X + 1), 2 * coarseKey.Y + 1);
            Say(string.Format(Inv, "seam_request level={0} quads_coarse={1} quads_fine={2} skirt_factor={3} cam_factor={4:0.###} tilt_deg={5:0.#} flip_wind={6} relief={7} depth_mode={8} coarse_face={9} coarse_xy={10},{11}",
                              level, nc, nf, skirtFactor, camFactor, tiltDeg, flipWind, relief, depthMode,
                              face, tx, ty));
            Say(string.Format(Inv, "seam_keys coarse={0} fine_a={1} fine_b={2}", coarseKey, fineLower, fineUpper));

            var cm = PlanetRegionMesh.BuildTile(coarseKey, nc, terrain);
            var fa = PlanetRegionMesh.BuildTile(fineLower, nf, terrain);
            var fb = PlanetRegionMesh.BuildTile(fineUpper, nf, terrain);

            double stepCm = MaxStepAcrossEdge(cm, nc, fa, nf);
            double predicted = PlanetRegionSeams.ChordSagCm(coarseKey, nc, terrain.RadiusCm)
                             - PlanetRegionSeams.ChordSagCm(fineLower, nf, terrain.RadiusCm);
            double curvatureDepth = PlanetRegionSeams.SkirtDepthCm(coarseKey, nc, terrain.RadiusCm);
            double edgeDepth = depthMode != 0
                ? PlanetRegionSeams.SkirtDepthForEdgeCm(coarseKey, nc, terrain, RegionEdge.UPlus)
                : curvatureDepth;
            double measuredEdgeStep = PlanetRegionSeams.MeasuredStepCm(coarseKey, nc, terrain, RegionEdge.UPlus);
            double depth = edgeDepth * skirtFactor;
            Say(string.Format(Inv, "seam_step_measured_cm={0:0.#} predicted_from_sag_cm={1:0.#} skirt_depth_cm={2:0.#}",
                              stepCm, predicted, depth));
            // 老公式 vs 新公式：同一块地、同一条边，深度差多少、能不能盖住实测步距
            Say(string.Format(Inv, "seam_depth_curvature_cm={0:0.#} seam_depth_edge_cm={1:0.#} edge_step_cm={2:0.#} curvature_covers={3} edge_covers={4}",
                              curvatureDepth, edgeDepth, measuredEdgeStep,
                              Bit(PlanetRegionSeams.CoversSag(curvatureDepth, measuredEdgeStep)),
                              Bit(PlanetRegionSeams.CoversSag(edgeDepth, measuredEdgeStep))));

            RegionMesh skirt = null;
            if (skirtFactor > 0)
            {
                skirt = PlanetRegionSeams.BuildSkirt(coarseKey, nc, terrain, RegionEdge.UPlus, depth);
                // 自证跑的是哪一份 Horizon.Planet.dll：编辑器可能仍加载它 Binaries 里的旧副本，
                // 而裙边的垒向正是这一轮要量的东西——符号错了整条判据就是假的。
                double dTop = skirt.Positions[0].Length, dBottom = skirt.Positions[nc + 1].Length;
                Say(string.Format(Inv, "seam_skirt_radial_delta={0:0} (正=朝外/新码，负=朝球心/旧 dll)", dBottom - dTop));
            }

            // 把几何平到原点附近：绝对坐标 6.4e8 cm 上 float32 的步长是 76 cm，
            // 比要量的缝隙还大，写进 buffer 会把台阶 itself 抹平——这是"缩放相对空间"在近景同样成立的原因。
            var center = CoarseEdgeMidpoint(cm, nc);
            double rawMax = 0;
            foreach (var m0 in new[] { cm, fa, fb })
                for (int i = 0; i < m0.Positions.Length; i++)
                {
                    var q = m0.Positions[i] - center;
                    rawMax = Math.Max(rawMax, Math.Max(Math.Abs(q.X), Math.Max(Math.Abs(q.Y), Math.Abs(q.Z))));
                }
            // 绝对坐标 1.5e9 单位这一档，引擎的绘制/剔除会给出全黑帧（实测），而缝隙的可见性是角度量，
            // 缩放不改变它：先把拼图归一到 ±StageSpan 单位再交给 UpdateMesh。
            double k = StageSpan / rawMax;
            Say(string.Format(Inv, "seam_raw_max_cm={0:0} stage_span={1:0} scale={2:0.#####e0}", rawMax, StageSpan, k));
            double maxAbs = 0;
            var meshes = new List<RegionMesh> { cm, fa, fb };
            if (skirt != null) meshes.Add(skirt);
            int totalVerts = 0, totalIdx = 0;
            foreach (var m in meshes) { totalVerts += m.Positions.Length; totalIdx += m.Triangles.Length; }
            var pos = new Float3[totalVerts];
            var nrm = new Float3[totalVerts];
            var idx = new UInt32[totalIdx];
            int vo = 0, io = 0;
            int mi = 0;
            foreach (var m in meshes)
            {
                double rMin = double.MaxValue, rMax = 0, eMin = double.MaxValue, eMax = double.MinValue;
                for (int i = 0; i < m.Positions.Length; i++)
                {
                    double len = m.Positions[i].Length;
                    rMin = Math.Min(rMin, len); rMax = Math.Max(rMax, len);
                    var rel = m.Positions[i] - center;
                    eMin = Math.Min(eMin, rel.X); eMax = Math.Max(eMax, rel.X);
                }
                Say(string.Format(Inv, "seam_part[{0}] key={1} verts={2} radius_cm={3:0}..{4:0} x_rel_cm={5:0}..{6:0}",
                                  mi, m.Key, m.Positions.Length, rMin, rMax, eMin, eMax));
                mi++;
                for (int i = 0; i < m.Positions.Length; i++)
                {
                    var p = (m.Positions[i] - center) * k;
                    var d = m.Positions[i];
                    double len = d.Length;
                    if (len <= 0) len = 1;
                    pos[vo + i] = new Float3((float)p.X, (float)p.Y, (float)p.Z);
                    nrm[vo + i] = new Float3((float)(d.X / len), (float)(d.Y / len), (float)(d.Z / len));
                    maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(p.X), Math.Max(Math.Abs(p.Y), Math.Abs(p.Z))));
                }
                bool isSkirt = ReferenceEquals(m, skirt);
                for (int t = 0; t < m.Triangles.Length; t += 3)
                {
                    int i0 = m.Triangles[t], i1 = m.Triangles[t + 1], i2 = m.Triangles[t + 2];
                    if (isSkirt && flipWind != 0) { int tmp = i1; i1 = i2; i2 = tmp; }   // 交换后两个顶点 = 法线反向
                    idx[io + t] = (UInt32)(i0 + vo);
                    idx[io + t + 1] = (UInt32)(i1 + vo);
                    idx[io + t + 2] = (UInt32)(i2 + vo);
                }
                vo += m.Positions.Length;
                io += m.Triangles.Length;
            }
            Say(string.Format(Inv, "seam_merged vertices={0} triangles={1} max_abs_coord_cm={2:0} float32_ulp_cm={3:0.####}",
                              totalVerts, totalIdx / 3, maxAbs, maxAbs * Math.Pow(2.0, -23.0)));

            var model = Content.CreateVirtualAsset<Model>();
            model.SetupLODs(new[] { 1 });
            model.SetupMaterialSlots(1);
            var gm = model.GetMesh(0, 0) as Mesh;
            if (gm == null) { Say("verdict=SEAM_NO_VIRTUAL_MESH"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { gm.UpdateMesh(pos, idx, nrm); }
            catch (Exception e)
            {
                Say("seam_update_error=" + e.GetType().Name + " " + OneLine(truncated(e.Message, 160)));
                Say("verdict=SEAM_UPDATE_FAILED");
                return;
            }
            sw.Stop();
            new MeshAccessor().UpdateMesh(gm, true);
            Say(string.Format(Inv, "seam_update_ms={0:0.###}", sw.Elapsed.TotalMilliseconds));
            Say("seam_after " + MeshState(gm));

            // 相机压在共边中点的法线上方，视线偏离法线 25°：正对着看缝隙就侧成一条线，张角量不出来。
            // 灯跟着视线走，保证两侧表面都亮、只有洞是黑的。
            var up = center * (1.0 / center.Length);
            double tilt = tiltDeg * Math.PI / 180.0;
            var tangent = new PlanetVector3(-up.Y, up.X, 0);
            double tl = tangent.Length;
            if (tl <= 0) { tangent = new PlanetVector3(0, 1, 0); tl = 1; }
            tangent = tangent * (1.0 / tl);
            double camDist = camFactor * StageSpan;
            var camPos = (up * Math.Cos(tilt) + tangent * Math.Sin(tilt)) * camDist;
            var look = -camPos;
            double ll = look.Length;
            look = look * (1.0 / (ll <= 0 ? 1 : ll));
            _cameraRef.Transform = new Transform(new Float3((float)camPos.X, (float)camPos.Y, (float)camPos.Z),
                                                 Quaternion.LookRotation(new Vector3((float)look.X, (float)look.Y, (float)look.Z)));
            _cameraRef.FarPlane = (float)(camDist * 30.0);
            _cameraRef.NearPlane = (float)(camDist * 0.01);
            Say(string.Format(Inv, "seam_camera_dist_units={0:0} tilt_deg={1:0.#} near={2:0.###} far={3:0}",
                              camDist, tiltDeg, _cameraRef.NearPlane, _cameraRef.FarPlane));
            Say(string.Format(Inv, "seam_step_stage_units={0:0.####} predicted_aperture_px={1:0.##} (@1080p/60°fov)",
                              stepCm * k, stepCm * k / camDist * 1200.0));
            var pt = _cameraRef.Transform.Translation;
            Say(string.Format(Inv, "seam_probe_camera_pos={0:0.###} {1:0.###} {2:0.###} has_scene={3}",
                              pt.X, pt.Y, pt.Z, Bit(_cameraRef.HasScene)));
            // 黑屏的第一嫌疑人是朝向：把引擎读回来的 forward 与"应该看向原点"的方向点乘打出来，
            // 是 +1 还是 -1 一次就能定死，不用猜 LookRotation 的约定。
            var fwd = _cameraRef.Transform.Forward;
            double fl = System.Math.Sqrt(fwd.X * fwd.X + fwd.Y * fwd.Y + fwd.Z * fwd.Z);
            double toMidX = -camPos.X, toMidY = -camPos.Y, toMidZ = -camPos.Z;
            double ml = System.Math.Sqrt(toMidX * toMidX + toMidY * toMidY + toMidZ * toMidZ);
            Say(string.Format(Inv, "seam_camera_forward={0:0.####} {1:0.####} {2:0.####} dot_with_to_origin={3:0.####}",
                              fwd.X / fl, fwd.Y / fl, fwd.Z / fl,
                              (fwd.X * toMidX + fwd.Y * toMidY + fwd.Z * toMidZ) / (fl * ml)));

            var light = new DirectionalLight { Name = "SeamLight" };
            light.Parent = Actor.Parent != null ? Actor.Parent : Actor;
            light.Transform = new Transform(Vector3.Zero, Quaternion.LookRotation(
                                    new Vector3((float)look.X, (float)look.Y, (float)look.Z)));
            Say("seam_light_spawned=" + Bit(light.HasScene));

            _targetRef.Transform = new Transform(Vector3.Zero, Quaternion.Identity);
            _targetRef.Model = model;
            bool plugged = skirt == null || PlanetRegionSeams.CoversSag(depth, stepCm);
            Say("verdict=" + (gm.VertexCount == totalVerts && stepCm > 0 && plugged
                ? "SEAM_RIG_WRITTEN" : "SEAM_PROBLEM"));
        }

        /// <summary>细侧边界顶点到粗侧直线弦的最大垂直距离（cm）——这条边实际的裂缝宽度，量出来的不是公式。</summary>
        private static double MaxStepAcrossEdge(RegionMesh coarse, int nc, RegionMesh fine, int nf)
        {
            int cStride = nc + 1, fStride = nf + 1;
            double worst = 0;
            for (int k = 0; k <= nf; k++)
            {
                var p = fine.Positions[k * fStride];            // UMinus 边：u = u0
                for (int j = 0; j < nc; j++)
                {
                    var a = coarse.Positions[j * cStride + nc];
                    var b = coarse.Positions[(j + 1) * cStride + nc];
                    var ab = b - a;
                    double l2 = PlanetVector3.Dot(ab, ab);
                    if (l2 <= 0) continue;
                    double t = PlanetVector3.Dot(p - a, ab) / l2;
                    if (t < 0 || t > 1) continue;               // 落在这段粗弦之外
                    var foot = a + ab * t;
                    worst = Math.Max(worst, Dist(p, foot));
                }
            }
            return worst;
        }

        private static PlanetVector3 CoarseEdgeMidpoint(RegionMesh coarse, int nc)
        {
            int stride = nc + 1;
            var first = coarse.Positions[nc];                    // j=0 的 UPlus 点
            var last = coarse.Positions[nc * stride + nc];       // j=nc 的 UPlus 点
            return (first + last) * 0.5;
        }

        private static double Dist(in PlanetVector3 a, in PlanetVector3 b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        /// <summary>岭脊高程源：2 km 振幅 / 20 km 波长。§13.39 标定过——60 km 波长还超不过纯弯曲深度，
        /// 20 km 就把实测步距推到 1.9 倍；真实 GEBCO 的网格是 463 m，比这锐得多。</summary>
        private sealed class Ridge : IHeightSource
        {
            private const double AmpCm = 200_000.0;
            private const double WaveCm = 2_000_000.0;
            public int SourceVersion => 1;
            public double SampleElevationCm(in PlanetVector3 d)
            {
                double dot = PlanetVector3.Dot(d, PlanetVector3.AxisX);
                if (dot > 1.0) dot = 1.0;
                if (dot < -1.0) dot = -1.0;
                return AmpCm * Math.Cos(2.0 * Math.PI * (PlanetConstants.PlanetRadiusCm * Math.Acos(dot)) / WaveCm);
            }
        }

        /// <summary>常数高程源：把起伏从这条判据里摘掉，剩下的偏差全是 LOD 几何自己的。</summary>
        private sealed class FlatHeight : IHeightSource
        {
            public static readonly FlatHeight Instance = new FlatHeight();
            public int SourceVersion => 1;
            public double SampleElevationCm(in PlanetVector3 dir) => 0.0;
        }

        private sealed class Ramp : IHeightSource
        {
            private readonly PlanetVector3 _e, _s;
            public Ramp(in PlanetVector3 e, in PlanetVector3 s) { _e = e; _s = s; }
            public int SourceVersion => 1;
            public double SampleElevationCm(in PlanetVector3 dir)
                => 12_345 + 0.05 * PlanetVector3.Dot(dir, _e) * PlanetConstants.PlanetRadiusCm
                                   + 0.02 * PlanetVector3.Dot(dir, _s) * PlanetConstants.PlanetRadiusCm;
        }

        private void Say(string line) { _log.Add(line); }

        private void Finish()
        {
            try
            {
                string dir = Path.Combine(Globals.ProjectFolder, "Saved");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, ResultName), string.Join(Environment.NewLine, _log), Encoding.UTF8);
            }
            catch (Exception e) { FlaxEngine.Debug.LogWarning("PLANET_MESH_RECEIPT_FAILED " + e.GetType().Name); }
        }

        private static string MarkerPath()
        {
            string inSaved = Path.Combine(Globals.ProjectFolder, "Saved", MarkerName);
            if (File.Exists(inSaved)) return inSaved;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string inAppData = Path.Combine(appData, "HundunWorld", MarkerName);
            return File.Exists(inAppData) ? inAppData : null;
        }

        private static bool Bit(bool b) => b;
        private static string OneLine(string s) => s == null ? "" : s.Replace("\r", " ").Replace("\n", " ").Trim();
        private static string truncated(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));
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
