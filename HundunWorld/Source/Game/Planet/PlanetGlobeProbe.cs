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
    /// P2 的第一块引擎侧取证：<b>远/中景球壳在真渲染里画出来是什么样子、有多贵</b>。
    /// <para>
    /// 此前 <see cref="PlanetRegionMesh"/> 只有离线拓扑判据（闭合、流形、绕序、欧拉数），
    /// 而 §9 给 P2 写的验收是"地平线/曲率截图"和"远景与近景高度一致"——那两条都得在引擎里答。
    /// 这里用 <c>DebugDraw.DrawLines(Double3[], …)</c>：它是<b>双精度</b>顶点入口，
    /// 正好避开"把 6.37e8 cm 的 ECEF 直接塞给 float 渲染"这个 10× 半径星球的头号坑
    /// （路线 C 的全部理由就是别在世界原点附近做浮点减法）。
    /// </para>
    /// <para>
    /// 画之前先把每个顶点换成<b>玩家所在切平面的局部双精度坐标</b>，所以相机与引擎都只处理
    /// 小数值；回执里同时量：局部坐标的最大绝对值（这决定 float32 够不够画）、画线本身的耗时、
    /// 以及"远壳顶点到地心的距离"与"同方向近景高程算出的半径"之差——后者就是 §9 那条一致性判据。
    /// </para>
    /// <para>触发：<c>&lt;Project&gt;/Saved/planet_globe.on</c>，内容 <c>"&lt;lat&gt; &lt;lon&gt; [level] [quadsPerEdge] [dem目录]"</c>；
    /// 结果写 <c>planet_globe.result.txt</c>，跑完吃标记。</para>
    /// </summary>
    public class PlanetGlobeProbe : Script
    {
        private const string BuildTag = "planet_globe_v1";
        private const string MarkerName = "planet_globe.on";
        private const string ResultName = "planet_globe.result.txt";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly List<string> _log = new List<string>();

        public override void OnStart()
        {
            string marker = MarkerPath();
            if (marker == null) return;
            try
            {
                Run(marker);
            }
            catch (Exception e)
            {
                Say("crash_type=" + e.GetType().Name);
                Say("crash_message=" + OneLine(truncated(e.Message, 200)));
                Say("crash_stack=" + OneLine(FirstFrames(e.StackTrace, 3)));
            }
            Finish();
            try { File.Delete(marker); } catch (Exception) { }
        }

        private void Run(string markerPath)
        {
            double lat = 30.0, lon = 120.0;
            int level = 3, quads = 8;
            string demDir = null;
            string text = "";
            try { text = File.ReadAllText(markerPath).Trim(); } catch (Exception) { }
            var parts = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            try
            {
                if (parts.Length >= 2) { lat = double.Parse(parts[0], Inv); lon = double.Parse(parts[1], Inv); }
                if (parts.Length >= 3) level = int.Parse(parts[2], Inv);
                if (parts.Length >= 4) quads = int.Parse(parts[3], Inv);
                if (parts.Length >= 5) demDir = parts[4];
            }
            catch (Exception e) { Say("marker_parse_error=" + e.GetType().Name); }

            Say("build_tag=" + BuildTag);
            Say(string.Format(Inv, "request lat={0:0.###} lon={1:0.###} level={2} quads_per_edge={3}", lat, lon, level, quads));

            var playerDir = Geodetic.DirectionFromLatLon(lat * Math.PI / 180.0, lon * Math.PI / 180.0);
            var frame = RegionFrame.Build(RegionKey.FromDirection(playerDir, PlanetConstants.DefaultRegionLevel),
                                          PlanetConstants.PlanetRadiusCm);

            // 高度源：给了 DEM 目录就吃真实基场，否则用解析斜面（可独立复算，且不吃读盘）
            PlanetTerrain terrain;
            DemGrid grid = null;
            HybridHeightSource hybrid = null;
            if (!string.IsNullOrEmpty(demDir) && Directory.Exists(demDir) && DemManifest.TryLoad(demDir, out var manifest, out _))
            {
                grid = new DemGrid(manifest);
                var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, manifest.ArcSecond / 3600.0);
                hybrid = new HybridHeightSource(grid, profile, manifest.ContentSeed, manifest.Format);
                terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, hybrid);
                Say("height_source=real_gebco");
            }
            else
            {
                terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, new Ramp(frame.East, frame.South));
                Say("height_source=analytic_ramp");
            }

            var mesh = PlanetRegionMesh.BuildLevel(level, quads, terrain);
            Say(string.Format(Inv, "mesh vertices={0} triangles={1} ecef_points={2}",
                             mesh.Positions.Length, mesh.TriangleCount, mesh.Ecef.Length));

            // 换到玩家切平面：每个顶点都变成"离玩家多远"的小数值，而不是 6.37e8 cm 的大数
            var lines = new List<Double3>();
            double maxAbs = 0;
            var local = new Double3[mesh.Positions.Length];
            for (int i = 0; i < mesh.Positions.Length; i++)
            {
                var l = frame.LocalFromGlobalCm(mesh.Positions[i]);
                local[i] = new Double3(l.X, l.Y, l.Z);
                maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(l.X), Math.Max(Math.Abs(l.Y), Math.Abs(l.Z))));
            }
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int a = mesh.Triangles[t * 3], b = mesh.Triangles[t * 3 + 1], c = mesh.Triangles[t * 3 + 2];
                lines.Add(new Double3(local[a].X, local[a].Y, local[a].Z)); lines.Add(new Double3(local[b].X, local[b].Y, local[b].Z));
                lines.Add(new Double3(local[b].X, local[b].Y, local[b].Z)); lines.Add(new Double3(local[c].X, local[c].Y, local[c].Z));
                lines.Add(new Double3(local[c].X, local[c].Y, local[c].Z)); lines.Add(new Double3(local[a].X, local[a].Y, local[a].Z));
            }
            Say(string.Format(Inv, "local_max_abs_coord_cm={0:0} lines={1}", maxAbs, lines.Count / 2));

            // §9 的一致性判据：远壳顶点到地心的距离，应当等于 R + 同方向的高程
            double worstAgree = 0;
            int sampled = 0;
            for (int i = 0; i < mesh.Positions.Length; i += Math.Max(1, mesh.Positions.Length / 400))
            {
                var p = mesh.Positions[i];
                double len = p.Length;
                if (len <= 0) continue;
                var dir = p / len;
                double expect = PlanetConstants.PlanetRadiusCm + terrain.ElevationCm(dir);
                worstAgree = Math.Max(worstAgree, Math.Abs(len - expect));
                sampled++;
            }
            Say(string.Format(Inv, "far_vs_near_sampled={0} worst_radius_agreement_cm={1:0.####}", sampled, worstAgree));

            const float DrawLifetimeS = 60f;
            var sw = Stopwatch.StartNew();
            DebugDraw.DrawLines(lines.ToArray(), Matrix.Identity, new Color(0.15f, 0.85f, 1.0f, 1.0f), DrawLifetimeS, true);
            sw.Stop();
            Say(string.Format(Inv, "draw_lines_ms={0:0.###} lifetime_s={1} depth_test=true",
                             sw.Elapsed.TotalMilliseconds, DrawLifetimeS));

            // 近景参考：把玩家所在 tile 的四条边画成暖色，截图里能同时看到"脚下的平面"与"外层的球壳"
            var tile = PlanetRegionMesh.BuildTile(frame.Key, 4, terrain);
            var refLines = new List<Double3>();
            for (int i = 0; i < tile.Positions.Length; i++)
            {
                var l = frame.LocalFromGlobalCm(tile.Positions[i]);
                refLines.Add(new Double3(l.X, l.Y, l.Z));
            }
            int tn = 5;                                   // quadsPerEdge=4 → 每边 5 个点
            var warm = new Color(1.0f, 0.55f, 0.1f, 1.0f);
            for (int row = 0; row < tn; row++)
                for (int col = 0; col < tn - 1; col++)
                {
                    var a = refLines[row * tn + col]; var b = refLines[row * tn + col + 1];
                    DebugDraw.DrawLine(new Vector3((float)a.X, (float)a.Y, (float)a.Z), new Vector3((float)b.X, (float)b.Y, (float)b.Z), warm, DrawLifetimeS, true);
                }
            for (int col = 0; col < tn; col++)
                for (int row = 0; row < tn - 1; row++)
                {
                    var a = refLines[row * tn + col]; var b = refLines[(row + 1) * tn + col];
                    DebugDraw.DrawLine(new Vector3((float)a.X, (float)a.Y, (float)a.Z), new Vector3((float)b.X, (float)b.Y, (float)b.Z), warm, DrawLifetimeS, true);
                }
            Say(string.Format(Inv, "near_tile_reference_lines={0}", 2 * tn * (tn - 1)));

            // float32 在这个量级上的最小步长：它才是"远壳能不能按局部厘米直接画"的判据，
            // 而不是我拍一个绝对上限。ULP 远超 1 cm 就说明远壳必须走缩放/相对空间的渲染路径。
            double floatUlpAtMax = maxAbs * Math.Pow(2.0, -23.0);
            Say(string.Format(Inv, "float32_ulp_at_max_coord_cm={0:0.####} (max_abs={1:0} cm)", floatUlpAtMax, maxAbs));
            // 只要求"近处"这一段还能被 float 表达：500 km 内的 ULP 是判据，远端另说
            double nearUlp = 5.0e6 * Math.Pow(2.0, -23.0);
            Say(string.Format(Inv, "near_range_500km_float32_ulp_cm={0:0.####}", nearUlp));

            // 判别实验：屏幕空间的文字 + 一条贴着相机的粗线（depthTest=false）。
            // 截图里连这个都没有 ⇒ DebugDraw 在 PIE 里根本不显示，远壳得换渲染通路；
            // 有文字而没线 ⇒ 通路正常，是我那 1.27e10 cm 的几何不在视野/被深度裁掉。
            DebugDraw.DrawText("GLOBE_PROBE", new Float2(40, 40), new Color(1f, 0f, 0f, 1f), 48, DrawLifetimeS);
            // 相机 API 不猜（Camera.Main 在这个装配里不存在）：改用与朝向无关的两个标记——
            // 屏幕空间文字 + 一个包围相机附近的大球（depthTest=false，绝不会被遮挡）。
            DebugDraw.DrawSphere(new BoundingSphere(new Vector3(0, 150000, 0), 120000f),
                                 new Color(1f, 0f, 0f, 1f), DrawLifetimeS, false);
            Say("marker_probe=text_at_40_40 + sphere_at_0_1500m_r1200m_depthTest_off");

            bool ok = mesh.Positions.Length > 0 && lines.Count > 0 && worstAgree < 1.0;
            Say("globe_verdict=" + (ok ? "FAR_SHELL_GEOMETRY_AND_DRAW_CALL_OK" : "GLOBE_PROBE_PROBLEM"));
            Say("pixels_verified=0 (要拿截图数像素才知道画没画到，这条不在本回执的 ok 里)");
            Say("render_range_note=" + (floatUlpAtMax > 100.0
                ? "远壳按局部厘米直接画到对端会丢到百米级：需要缩放/相对空间的渲染路径（不是缺陷，是 P2 的输入）"
                : "远壳在当前范围内 float 表达足够"));
            if (!ok)
            {
                var why = new List<string>();
                if (lines.Count == 0) why.Add("no_lines");
                if (worstAgree >= 1.0) why.Add("far_near_disagree_cm=" + worstAgree.ToString("0.####", Inv));
                Say("fail_reason=" + string.Join(",", why));
            }
            grid?.Dispose();
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
            catch (Exception e) { FlaxEngine.Debug.LogWarning("PLANET_GLOBE_RECEIPT_FAILED " + e.GetType().Name); }
        }

        private static string MarkerPath()
        {
            string inSaved = Path.Combine(Globals.ProjectFolder, "Saved", MarkerName);
            if (File.Exists(inSaved)) return inSaved;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string inAppData = Path.Combine(appData, "HundunWorld", MarkerName);
            return File.Exists(inAppData) ? inAppData : null;
        }

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
