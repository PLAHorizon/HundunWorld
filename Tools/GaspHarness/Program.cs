using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;
using HundunWorld.Game.Gasp;

namespace GaspHarness
{
    internal static class Program
    {
        private static bool SameTransform(Transform a, Transform b, float eps = 1e-2f)
        {
            var d = a.Translation - b.Translation;
            if ((float)Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z) > eps)
                return false;
            float qd = Math.Abs(Quaternion.Dot(Quaternion.Normalize(a.Orientation), Quaternion.Normalize(b.Orientation)));
            if (qd < 1f - 1e-4f)
                return false;
            return Math.Abs(a.Scale.X - b.Scale.X) + Math.Abs(a.Scale.Y - b.Scale.Y) + Math.Abs(a.Scale.Z - b.Scale.Z) <= eps;
        }

        private static bool IsFinite(Vector3 v)
            => !float.IsNaN(v.X) && !float.IsNaN(v.Y) && !float.IsNaN(v.Z)
               && !float.IsInfinity(v.X) && !float.IsInfinity(v.Y) && !float.IsInfinity(v.Z);
        private static int _pass;
        private static int _fail;

        private static void Check(string name, bool ok, string detail = "")
        {
            if (ok) _pass++;
            else _fail++;
            Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : " | " + detail));
        }

        private static int Main(string[] args)
        {
            string bin = args.Length > 0
                ? args[0]
                : Path.GetFullPath(Path.Combine("..", "..", "HundunWorld", "Content", "Gasp", "gasp_anims.bin"));
            Console.WriteLine("db=" + bin);
            var db = GaspAnimationDatabase.Load(bin);
            Check("db.loaded", db.IsLoaded, db.Error ?? "");
            if (!db.IsLoaded)
                return 1;

            var allNames = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < db.Clips.Count; i++)
            {
                foreach (var ch in db.Clips[i].Channels)
                {
                    if (seen.Add(ch.Name))
                        allNames.Add(ch.Name);
                }
            }
            Check("db.clips", db.Clips.Count >= 30, "clips=" + db.Clips.Count);
            Check("db.channels", allNames.Count >= 80, "channels=" + allNames.Count);
            Check("db.rate", Math.Abs(db.SampleRate - 30f) < 0.01f, "rate=" + db.SampleRate);

            // 真实 Ada 骨架（离线从 SkinnedModel .flax 解出）；缺失时退回合成骨架。
            string skelPath = args.Length > 1
                ? args[1]
                : Path.GetFullPath(Path.Combine("..", "_generated", "ada_skel.json"));
            GaspSkeleton skel = null;
            if (File.Exists(skelPath))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(skelPath));
                var arr = doc.RootElement.GetProperty("nodes");
                int n = arr.GetArrayLength();
                var names2 = new string[n];
                var parents2 = new int[n];
                var bind2 = new Transform[n];
                for (int i = 0; i < n; i++)
                {
                    var e = arr[i];
                    names2[i] = e.GetProperty("name").GetString();
                    parents2[i] = e.GetProperty("parent").GetInt32();
                    var t = e.GetProperty("translation");
                    var r = e.GetProperty("rotation");
                    var s = e.GetProperty("scale");
                    bind2[i] = new Transform(
                        new Vector3((float)t[0].GetDouble(), (float)t[1].GetDouble(), (float)t[2].GetDouble()),
                        new Quaternion((float)r[0].GetDouble(), (float)r[1].GetDouble(), (float)r[2].GetDouble(), (float)r[3].GetDouble()))
                    {
                        Scale = new Float3((float)s[0].GetDouble(), (float)s[1].GetDouble(), (float)s[2].GetDouble()),
                    };
                }
                skel = GaspSkeleton.Create(names2, parents2, bind2);
                Console.WriteLine("skeleton source=ada nodes=" + n);
            }
            else
            {
                var names = allNames.ToArray();
                var parents = new int[names.Length];
                var bind = new Transform[names.Length];
                int pelvis = Array.FindIndex(names, n => string.Equals(n, "pelvis", StringComparison.OrdinalIgnoreCase));
                if (pelvis < 0)
                {
                    Console.WriteLine("[FAIL] skeleton.pelvis missing");
                    return 1;
                }
                for (int i = 0; i < names.Length; i++)
                {
                    parents[i] = i == pelvis ? -1 : pelvis;
                    bind[i] = new Transform(Vector3.Zero);
                }
                bind[pelvis] = new Transform(new Vector3(0, 90, 0));
                SetBind(names, bind, "foot_l", new Vector3(-12, -90, 0));
                SetBind(names, bind, "foot_r", new Vector3(12, -90, 0));
                SetBind(names, bind, "head", new Vector3(0, 60, 0));
                SetBind(names, bind, "hand_l", new Vector3(-25, 40, 0));
                SetBind(names, bind, "hand_r", new Vector3(25, 40, 0));
                skel = GaspSkeleton.Create(names, parents, bind);
                Console.WriteLine("skeleton source=synthetic nodes=" + names.Length);
            }
            Check("skeleton.create", skel != null && skel.Pelvis >= 0 && skel.FootL >= 0 && skel.FootR >= 0);
                        {
                var missing = new System.Collections.Generic.List<string>();
                foreach (var n in allNames)
                {
                    if (skel.Find(n) < 0) missing.Add(n);
                }
                Console.WriteLine("  unmatched channels: " + string.Join(",", missing));
            }


            var mdb = new GaspMotionDatabase();
            bool built = mdb.Build(db, skel);
            Check("motiondb.build", built, mdb.Error ?? "");
            Check("motiondb.poses", mdb.Poses.Count > 500, "poses=" + mdb.Poses.Count + " ms=" + mdb.BuildMilliseconds.ToString("F1"));
            if (!built)
                return 1;

            bool finite = true;
            foreach (var p in mdb.Poses)
            {
                for (int i = 0; i < GaspFeatureSchema.Dimension; i++)
                {
                    if (float.IsNaN(p.Features[i]) || float.IsInfinity(p.Features[i]))
                    {
                        finite = false;
                        break;
                    }
                }
                if (!finite) break;
            }
            Check("motiondb.finite", finite);

            // 5.0a) 脚轨迹诊断（临时）：打印脚高/水平速度，给落脚阈值找依据。
            {
                foreach (var nm in new[] { "ALS_N_Walk_F", "ALS_N_Run_F", "ALS_N_SecondaryMotion" })
                {
                    int ci = FindClip(db, nm);
                    if (ci < 0) continue;
                    var c = db.Clips[ci];
                    int n = c.FrameCount;
                    var pp = new GaspPoseBuffer(skel.Count);
                    var sc = new float[Math.Max(db.MaxStride, 1)];
                    var lc = new Matrix[skel.Count];
                    var wc2 = new Matrix[skel.Count];
                    var ys = new float[n]; var yr = new float[n]; var sp = new float[n]; var spr = new float[n];
                    for (int f = 0; f < n; f++)
                    {
                        float t = f / (float)c.SampleRate;
                        GaspPoseUtil.SampleClip(c, t, skel, pp, sc);
                        GaspPoseUtil.ComposeComponent(pp, skel, lc, wc2, true);
                        ys[f] = wc2[skel.FootL].TranslationVector.Y;
                        yr[f] = wc2[skel.FootR].TranslationVector.Y;
                    }
                    for (int f = 0; f < n; f++)
                    {
                        int pv = (f - 1 + n) % n, nx = (f + 1) % n;
                        float dx = wc2[skel.FootL].TranslationVector.X; // placeholder overwritten below
                        GaspPoseUtil.SampleClip(c, pv / (float)c.SampleRate, skel, pp, sc);
                        GaspPoseUtil.ComposeComponent(pp, skel, lc, wc2, true);
                        var a = wc2[skel.FootL].TranslationVector;
                        var ar = wc2[skel.FootR].TranslationVector;
                        GaspPoseUtil.SampleClip(c, nx / (float)c.SampleRate, skel, pp, sc);
                        GaspPoseUtil.ComposeComponent(pp, skel, lc, wc2, true);
                        var b = wc2[skel.FootL].TranslationVector;
                        var br = wc2[skel.FootR].TranslationVector;
                        sp[f] = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z)) * c.SampleRate * 0.5f;
                        spr[f] = (float)Math.Sqrt((br.X - ar.X) * (br.X - ar.X) + (br.Z - ar.Z) * (br.Z - ar.Z)) * c.SampleRate * 0.5f;
                    }
                    var line = new System.Text.StringBuilder();
                    for (int f = 0; f < n; f += Math.Max(1, n / 8))
                        line.Append($"f{f}:L{ys[f]:F0}/{sp[f]:F0} R{yr[f]:F0}/{spr[f]:F0} | ");
                    // 局部旋转差异：确认腿部通道是否真的在动（排除"位置恒定是数据本身"）。
                    GaspPoseUtil.SampleClip(c, 0f, skel, pp, sc);
                    var qa = pp.Local[skel.ThighL].Orientation;
                    GaspPoseUtil.SampleClip(c, c.Length * 0.5f, skel, pp, sc);
                    var qb = pp.Local[skel.ThighL].Orientation;
                    float thighDeg = 2f * (float)Math.Acos(Mathf.Clamp(Mathf.Abs(Quaternion.Dot(Quaternion.Normalize(qa), Quaternion.Normalize(qb))), -1f, 1f)) * Mathf.RadiansToDegrees;
                    GaspPoseUtil.SampleClip(c, 0f, skel, pp, sc);
                    GaspPoseUtil.ComposeComponent(pp, skel, lc, wc2, true);
                    var ank0 = wc2[skel.FootL].TranslationVector;
                    var knee0 = wc2[skel.CalfL].TranslationVector;
                    GaspPoseUtil.SampleClip(c, c.Length * 0.5f, skel, pp, sc);
                    GaspPoseUtil.ComposeComponent(pp, skel, lc, wc2, true);
                    var ankH = wc2[skel.FootL].TranslationVector;
                    var kneeH = wc2[skel.CalfL].TranslationVector;
                    Console.WriteLine($"  FOOT {nm} thighLocalDelta={thighDeg:F1}deg ank0=({ank0.X:F1},{ank0.Y:F1},{ank0.Z:F1}) ankh=({ankH.X:F1},{ankH.Y:F1},{ankH.Z:F1}) knee0=({knee0.X:F1},{knee0.Y:F1},{knee0.Z:F1}) kneeh=({kneeH.X:F1},{kneeH.Y:F1},{kneeH.Z:F1})");
                    Console.WriteLine($"  FOOT {nm} len={c.Length:F2} n={n} " + line);
                }
            }

            // 5.0b) 落脚检测（Foley 数据源）：Walk/Run 每周期左右各一次且交替；待机 0 次。
            {
                int wi = FindClip(db, "ALS_N_Walk_F");
                int ri = FindClip(db, "ALS_N_Run_F");
                int ii = FindClip(db, "ALS_N_SecondaryMotion");
                if (wi >= 0)
                {
                    var wt = mdb.ClipPlantTimes[wi];
                    var ws = mdb.ClipPlantSides[wi];
                    bool alt = true;
                    for (int i = 1; i < ws.Length; i++)
                        if (ws[i] == ws[i - 1]) alt = false;
                    Check("foot.walkCount", wt.Length >= 2,
                        $"n={wt.Length} t=[{string.Join(",", wt)}] sides=[{string.Join(",", ws)}]");
                    Check("foot.walkAlternates", ws.Length >= 2 && alt, $"sides=[{string.Join(",", ws)}]");
                    if (wt.Length >= 2)
                    {
                        float gap = wt[1] - wt[0];
                        Check("foot.walkGap", gap > db.Clips[wi].Length * 0.25f && gap < db.Clips[wi].Length * 0.75f,
                            $"gap={gap:F2} len={db.Clips[wi].Length:F2}");
                    }
                }
                if (ri >= 0)
                {
                    var rt = mdb.ClipPlantTimes[ri];
                    var rs = mdb.ClipPlantSides[ri];
                    bool ralt = true;
                    for (int i = 1; i < rs.Length; i++)
                        if (rs[i] == rs[i - 1]) ralt = false;
                    Check("foot.runCount", rt.Length >= 2, $"n={rt.Length} t=[{string.Join(",", rt)}]");
                    Check("foot.runAlternates", rs.Length >= 2 && ralt, $"sides=[{string.Join(",", rs)}]");
                }
                Check("foot.idleNone", ii < 0 || mdb.ClipPlantTimes[ii].Length == 0,
                    ii >= 0 ? $"n={mdb.ClipPlantTimes[ii].Length}" : "no idle clip");
            }

            // 5.0c) 脚步时间线（Foley 消费端）：(from,to] 跨点、边界、循环回绕、倒走不循环。
            {
                var times = new float[] { 0.1f, 0.6f };
                var hits = new List<int>();

                int n1 = GaspFootstepTimeline.Collect(times, 0f, 0.5f, false, 1f, hits);
                Check("foot.timelineForward", n1 == 1 && hits.Count == 1 && hits[0] == 0,
                    $"n={n1} hits=[{string.Join(",", hits)}]");

                int n2 = GaspFootstepTimeline.Collect(times, 0.1f, 0.6f, false, 1f, hits);
                Check("foot.timelineExclusiveFrom", n2 == 1 && hits.Count == 1 && hits[0] == 1,
                    $"n={n2} hits=[{string.Join(",", hits)}]");

                int n3 = GaspFootstepTimeline.Collect(times, 0f, 1f, false, 1f, hits);
                Check("foot.timelineBothInBigStep", n3 == 2 && hits.Count == 2 && hits[0] == 0 && hits[1] == 1,
                    $"n={n3} hits=[{string.Join(",", hits)}]");

                int n4 = GaspFootstepTimeline.Collect(times, 0.3f, 0.3f, false, 1f, hits);
                Check("foot.timelineEmpty", n4 == 0 && hits.Count == 0, $"n={n4}");

                // 循环回绕：从 0.5 走到 0.2 跨过 0 点，先命中 0.6 再命中 0.1。
                int n5 = GaspFootstepTimeline.Collect(times, 0.5f, 0.2f, true, 1f, hits);
                Check("foot.timelineLoopWrap", n5 == 2 && hits.Count == 2 && hits[0] == 1 && hits[1] == 0,
                    $"n={n5} hits=[{string.Join(",", hits)}]");

                // 非循环剪辑时间回退（不应该发生）不产生命中。
                int n6 = GaspFootstepTimeline.Collect(times, 0.5f, 0.2f, false, 1f, hits);
                Check("foot.timelineNoWrapWhenClamped", n6 == 0 && hits.Count == 0, $"n={n6}");
            }

            // 5.1) KD 树检索：独立暴力参照对拍 + 候选集召回 + LOD 候选上限。
            {
                var rng = new Random(20261101);
                int dim = GaspFeatureSchema.Dimension;

                // (a) 精确 KNN：KD 树 top-1 的平方距离必须等于独立暴力最小距离。
                int knnMismatch = 0;
                int knnCount = 200;
                var oneIdx = new int[1];
                var oneDist = new float[1];
                var q = new float[dim];
                for (int t = 0; t < knnCount; t++)
                {
                    int src = rng.Next(mdb.Poses.Count);
                    var f = mdb.Poses[src].Features;
                    for (int d = 0; d < dim; d++)
                        q[d] = f[d] + (float)((rng.NextDouble() - 0.5) * 0.2);
                    mdb.KdTree.QueryKnn(q, 1, oneIdx, oneDist);

                    float bruteMin = float.MaxValue;
                    for (int i = 0; i < mdb.Poses.Count; i++)
                    {
                        float d2 = GaspFeatureSchema.Distance(q, mdb.Poses[i].Features);
                        if (d2 < bruteMin) bruteMin = d2;
                    }
                    if (Math.Abs(bruteMin - oneDist[0]) > 1e-3f)
                        knnMismatch++;
                }
                Check("kd.knnExact", knnMismatch == 0, $"mismatch={knnMismatch}/{knnCount} visited={mdb.KdTree.LastVisited}/{mdb.Poses.Count}");

                // (b) 稠密候选（64）必须包含最优解（代价不劣于暴力）；剪辑一致率另计（等代价并列帧不算错）。
                //     稀疏（24）给成本/质量权衡：只要求平均代价差距小、剪辑仍大体正确。
                int DenseOptimal = 0, DenseClipMatch = 0, SparseClipMatch = 0, SparseOptimal = 0, DenseTotal = 200;
                float sparseGapSum = 0f, sparseGapMax = 0f;
                int denseScoredMax = 0;
                for (int t = 0; t < DenseTotal; t++)
                {
                    int src = rng.Next(mdb.Poses.Count);
                    var f = mdb.Poses[src].Features;
                    for (int d = 0; d < dim; d++)
                        q[d] = f[d] + (float)((rng.NextDouble() - 0.5) * 0.4);

                    mdb.Search(q, -1, 0f, 0f, 0f, null, 0f, 0, out var brute, out _, 0);
                    mdb.Search(q, -1, 0f, 0f, 0f, null, 0f, 0, out var dense, out _, 128);
                    if (mdb.LastCandidatesScored > denseScoredMax) denseScoredMax = mdb.LastCandidatesScored;
                    mdb.Search(q, -1, 0f, 0f, 0f, null, 0f, 0, out var sparse, out _, 32);

                    if (dense.Cost <= brute.Cost + 1e-3f) DenseOptimal++;
                    if (sparse.Cost <= brute.Cost + 2f) SparseOptimal++;
                    if (dense.Clip == brute.Clip) DenseClipMatch++;
                    if (sparse.Clip == brute.Clip) SparseClipMatch++;
                    float gap = sparse.Cost - brute.Cost;
                    sparseGapSum += gap;
                    if (gap > sparseGapMax) sparseGapMax = gap;
                }
                // 标定：不同 K 的最优率/剪辑一致率（真实逐帧特征下，等代价并列大幅减少）。
                var probeKs = new[] { 16, 24, 32, 48, 64, 96, 128 };
                var probeLine = new System.Text.StringBuilder();
                var rng2 = new Random(20261101);
                var q2 = new float[dim];
                foreach (int kk in probeKs)
                {
                    int opt = 0, clipm = 0;
                    for (int t = 0; t < 120; t++)
                    {
                        int src = rng2.Next(mdb.Poses.Count);
                        var f = mdb.Poses[src].Features;
                        for (int d = 0; d < dim; d++)
                            q2[d] = f[d] + (float)((rng2.NextDouble() - 0.5) * 0.4);
                        mdb.Search(q2, -1, 0f, 0f, 0f, null, 0f, 0, out var bb, out _, 0);
                        mdb.Search(q2, -1, 0f, 0f, 0f, null, 0f, 0, out var kkBest, out _, kk);
                        if (kkBest.Cost <= bb.Cost + 1e-3f) opt++;
                        if (kkBest.Clip == bb.Clip) clipm++;
                    }
                    probeLine.Append($"K{kk}:opt{opt}/120 clip{clipm} | ");
                }
                Check("kd.kCalibration", true, probeLine.ToString());
                Check("kd.denseOptimal", DenseOptimal >= DenseTotal * 97 / 100, $"optimal={DenseOptimal}/{DenseTotal}");
                // 剪辑一致率不是硬门槛：同类剪辑（Stop_L/Stop_R、TurnIP 等）存在等代价并列，
                // 选哪一条取决于遍历顺序；真正的质量判据是 denseOptimal（代价不劣）+ sparseCostGap。
                Check("kd.denseClipMatch", DenseClipMatch >= DenseTotal * 70 / 100,
                    $"clipMatch={DenseClipMatch}/{DenseTotal} scoredMax={denseScoredMax}");
                // 稀疏档的“剪辑一致率”只是参考：方向相近的同类剪辑（Walk_LF/Walk_F…）常等代价；
                // 真正的质量门是代价最优率与平均代价差。
                Check("kd.sparseOptimalRate", SparseOptimal >= DenseTotal * 80 / 100,
                    $"optimal={SparseOptimal}/{DenseTotal} clipMatch={SparseClipMatch}");
                Check("kd.sparseClipMatch", SparseClipMatch >= DenseTotal * 65 / 100,
                    $"clipMatch={SparseClipMatch}/{DenseTotal} avgGap={sparseGapSum / DenseTotal:F2} maxGap={sparseGapMax:F1}");
                Check("kd.sparseCostGap", sparseGapSum / DenseTotal <= 50f,
                    $"avgGap={sparseGapSum / DenseTotal:F2} maxGap={sparseGapMax:F1}");
                Check("kd.lodBudget", denseScoredMax <= 129, $"scoredMax={denseScoredMax} total={mdb.Poses.Count}");
            }
            Check("calibration.forwardYaw", Math.Abs(Math.Abs(mdb.ForwardYaw) - Math.PI) < 0.35f,
                $"forwardYaw={mdb.ForwardYaw:F2}");
            Check("calibration.gaitSpeeds", mdb.WalkSpeed > 20f && mdb.RunSpeed > mdb.WalkSpeed && mdb.SprintSpeed > mdb.RunSpeed,
                $"walk={mdb.WalkSpeed:F0} run={mdb.RunSpeed:F0} sprint={mdb.SprintSpeed:F0}");

            for (int ci = 0; ci < db.Clips.Count; ci++)
            {
                if (db.Clips[ci].Tag != GaspClipTag.Ground) continue;
                Console.WriteLine($"  nominal {db.Clips[ci].Name,-26} speed={mdb.ClipNominalSpeed[ci],7:F0} v=({mdb.ClipNominalVelocity[ci].X:F0},{mdb.ClipNominalVelocity[ci].Z:F0})");
            }
            Console.WriteLine($"  forwardYaw={mdb.ForwardYaw:F2} (rad), walk={mdb.WalkSpeed:F0} run={mdb.RunSpeed:F0} sprint={mdb.SprintSpeed:F0} cm/s");

            // 真实骨架下的脚部世界范围（证明烘焙/坐标修正是否合理）
            {
                var pose = new GaspPoseBuffer(skel.Count);
                var scratch = new float[Math.Max(db.MaxStride, 1)];
                var local = new Matrix[skel.Count];
                var world = new Matrix[skel.Count];
                foreach (var nm in new[] { "ALS_N_Walk_F", "ALS_N_Run_F", "ALS_N_Sprint_F" })
                {
                    int ci = FindClip(db, nm);
                    if (ci < 0) continue;
                    var clip = db.Clips[ci];
                    float minLX = 1e9f, maxLX = -1e9f, minLY = 1e9f, maxLY = -1e9f, minLZ = 1e9f, maxLZ = -1e9f;
                    float minRY = 1e9f, maxRY = -1e9f;
                    for (int f = 0; f < clip.FrameCount; f++)
                    {
                        GaspPoseUtil.SampleClip(clip, f / clip.SampleRate, skel, pose, scratch);
                        GaspPoseUtil.BuildWorld(pose, skel, local, world);
                        var l = world[skel.FootL].TranslationVector;
                        var r = world[skel.FootR].TranslationVector;
                        minLX = Math.Min(minLX, l.X); maxLX = Math.Max(maxLX, l.X);
                        minLY = Math.Min(minLY, l.Y); maxLY = Math.Max(maxLY, l.Y);
                        minLZ = Math.Min(minLZ, l.Z); maxLZ = Math.Max(maxLZ, l.Z);
                        minRY = Math.Min(minRY, r.Y); maxRY = Math.Max(maxRY, r.Y);
                    }
                    Console.WriteLine($"  feet {nm,-20} L X[{minLX:F0},{maxLX:F0}] Y[{minLY:F0},{maxLY:F0}] Z[{minLZ:F0},{maxLZ:F0}] R Y[{minRY:F0},{maxRY:F0}]");
                }
            }

            // 1) 自洽检索：取每个地面剪辑中间帧的特征，应找回同一剪辑或同族。
            var recent = new Dictionary<long, float>();
            int exact = 0, family = 0, tested = 0;
            for (int ci = 0; ci < db.Clips.Count; ci++)
            {
                var clip = db.Clips[ci];
                if (clip.Tag != GaspClipTag.Ground || clip.FrameCount < 3)
                    continue;
                int first = mdb.ClipFirstPose[ci];
                if (first < 0) continue;
                int frame = clip.FrameCount / 2;
                var pose = mdb.Poses[first + frame];
                mdb.Search(pose.Features, -1, 0f, 0f, 0f, recent, -10f, 0, out var best, out _);
                tested++;
                if (best.Valid && best.Clip == ci) exact++;
                if (best.Valid && Family(db.Clips[best.Clip].Name) == Family(clip.Name)) family++;
            }
            Check("search.exact", tested > 0 && exact >= tested / 2, $"exact={exact}/{tested}");
            Check("search.family", tested > 0 && family >= tested * 2 / 3, $"family={family}/{tested}");

            // 2) 逐剪辑检索：Run_F / Walk_F / Sprint 的中间帧应命中各自家族。
            foreach (var probe in new[] { "ALS_N_Walk_F", "ALS_N_Run_F", "ALS_N_Sprint_F" })
            {
                int ci = FindClip(db, probe);
                if (ci < 0)
                {
                    Check("probe." + probe, false, "clip missing");
                    continue;
                }
                int first = mdb.ClipFirstPose[ci];
                int frame = db.Clips[ci].FrameCount / 2;
                mdb.Search(mdb.Poses[first + frame].Features, -1, 0f, 0f, 0f, recent, -10f, 0, out var best, out _);
                Check("probe." + probe, best.Valid && Family(db.Clips[best.Clip].Name) == Family(db.Clips[ci].Name),
                    "best=" + (best.Valid ? db.Clips[best.Clip].Name : "<none>"));
            }

            // 3) 查询构建：上一帧姿态用 Run 中间帧，轨迹给不同目标速度，检索结果应随速度改变。
            int runClip = FindClip(db, "ALS_N_Run_F");
            int walkClip = FindClip(db, "ALS_N_Walk_F");
            int sprintClip = FindClip(db, "ALS_N_Sprint_F");
            int idleClip = FindClip(db, "ALS_N_SecondaryMotion");
            if (runClip >= 0 && idleClip >= 0)
            {
                var initial = new GaspPoseBuffer(skel.Count);
                var scratch = new float[Math.Max(db.MaxStride, 1)];
                GaspPoseUtil.SampleClip(db.Clips[runClip], 0.3f, skel, initial, scratch);
                var initLocal = new Matrix[skel.Count];
                var initComp = new Matrix[skel.Count];
                GaspPoseUtil.ComposeComponent(initial, skel, initLocal, initComp, true);
                var qb = new GaspQueryBuilder(skel, initComp, mdb.ForwardYaw);
                var query = new float[GaspFeatureSchema.Dimension];

                var stopInput = new GaspMotionInput
                {
                    Position = Vector3.Zero,
                    Yaw = 0f,
                    Velocity = Vector3.Zero,
                    Grounded = true,
                };
                qb.Build(stopInput, 1f / 60f, query);
                mdb.Search(query, -1, 0f, 0f, 0f, recent, -10f, 0, out var stopBest, out _);
                Check("query.stop", stopBest.Valid && (Family(db.Clips[stopBest.Clip].Name) == "idle" || Family(db.Clips[stopBest.Clip].Name) == "stop"),
                    "best=" + (stopBest.Valid ? db.Clips[stopBest.Clip].Name : "<none>"));

                var walkInput = new GaspMotionInput
                {
                    Position = Vector3.Zero,
                    Yaw = 0f,
                    Velocity = new Vector3(0, 0, walkClip >= 0 ? mdb.ClipNominalSpeed[walkClip] : 150f),
                    DesiredVelocityLocal = new Vector3(0, 0, -(walkClip >= 0 ? mdb.ClipNominalSpeed[walkClip] : 150f)),
                    HasInput = true,
                    Grounded = true,
                };
                qb.Build(walkInput, 1f / 60f, query);
                mdb.Search(query, -1, 0f, 0f, 0f, recent, -10f, 1, out var walkBest, out _);
                Check("query.walk", walkBest.Valid && (Family(db.Clips[walkBest.Clip].Name) == "walk" || Family(db.Clips[walkBest.Clip].Name) == "run"),
                    "best=" + (walkBest.Valid ? db.Clips[walkBest.Clip].Name : "<none>"));

                var sprintInput = new GaspMotionInput
                {
                    Position = Vector3.Zero,
                    Yaw = 0f,
                    Velocity = new Vector3(0, 0, sprintClip >= 0 ? mdb.ClipNominalSpeed[sprintClip] : 600f),
                    DesiredVelocityLocal = new Vector3(0, 0, -(sprintClip >= 0 ? mdb.ClipNominalSpeed[sprintClip] : 600f)),
                    HasInput = true,
                    Grounded = true,
                };
                qb.Build(sprintInput, 1f / 60f, query);
                mdb.Search(query, -1, 0f, 0f, 0f, recent, -10f, 3, out var sprintBest, out _);
                Check("query.sprint", sprintBest.Valid && (Family(db.Clips[sprintBest.Clip].Name) == "sprint" || Family(db.Clips[sprintBest.Clip].Name) == "run"),
                    "best=" + (sprintBest.Valid ? db.Clips[sprintBest.Clip].Name : "<none>"));
            }

            // 4) 续播稳定性：连续 2 秒喂同一速度查询，剪辑不应每帧切换。
            int switches = 0, frames = 0;
            if (runClip >= 0)
            {
                var matcher = new GaspMotionMatcher { Database = mdb };
                var initial = new GaspPoseBuffer(skel.Count);
                var scratch = new float[Math.Max(db.MaxStride, 1)];
                GaspPoseUtil.SampleClip(db.Clips[runClip], 0f, skel, initial, scratch);
                var initLocal = new Matrix[skel.Count];
                var initComp = new Matrix[skel.Count];
                GaspPoseUtil.ComposeComponent(initial, skel, initLocal, initComp, true);
                var qb = new GaspQueryBuilder(skel, initComp, mdb.ForwardYaw);
                var query = new float[GaspFeatureSchema.Dimension];
                var input = new GaspMotionInput
                {
                    Velocity = new Vector3(0, 0, mdb.ClipNominalSpeed[runClip]),
                    DesiredVelocityLocal = new Vector3(0, 0, -mdb.ClipNominalSpeed[runClip]),
                    HasInput = true,
                    Grounded = true,
                };
                float now = 0f;
                for (int i = 0; i < 120; i++)
                {
                    qb.Build(input, 1f / 60f, query);
                    if (matcher.Update(query, 1f / 60f, now, 150f, 150f, 2))
                        switches++;
                    frames++;
                    now += 1f / 60f;
                }
                Check("matcher.continuity", matcher.HasCurrent && switches <= frames / 4, $"switches={switches}/{frames}");
                Check("matcher.family", matcher.HasCurrent && (Family(db.Clips[matcher.Current.Clip].Name) == "run" || Family(db.Clips[matcher.Current.Clip].Name) == "sprint" || Family(db.Clips[matcher.Current.Clip].Name) == "walk"),
                    "final=" + (matcher.HasCurrent ? db.Clips[matcher.Current.Clip].Name : "<none>"));
            }

            // 4.5) 与引擎探针对照：Walk_F t=0 的组件空间骨骼位置。
            {
                foreach (var cn in new[] { "ALS_N_Walk_F", "ALS_N_Run_F", "ALS_N_SecondaryMotion" })
                {
                    int ci = FindClip(db, cn);
                    if (ci < 0) continue;
                    var clip = db.Clips[ci];
                    var p = new GaspPoseBuffer(skel.Count);
                    var sc = new float[Math.Max(db.MaxStride, 1)];
                    var l = new Matrix[skel.Count];
                    var c = new Matrix[skel.Count];
                    GaspPoseUtil.SampleClip(clip, clip.Length * 0.4f, skel, p, sc);
                    GaspPoseUtil.ComposeComponent(p, skel, l, c, true);
                    int hn = skel.Find("hand_l");
                    float minY = 1e9f, maxY = -1e9f;
                    for (int f = 0; f < clip.FrameCount; f++)
                    {
                        GaspPoseUtil.SampleClip(clip, f / clip.SampleRate, skel, p, sc);
                        GaspPoseUtil.ComposeComponent(p, skel, l, c, true);
                        if (hn >= 0)
                        {
                            float y = c[hn].TranslationVector.Y;
                            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        }
                    }
                    Console.WriteLine($"  handY {clip.Name} [{minY:F1},{maxY:F1}]");
                }
            }

            // 5) LegIK（组件空间）：解析双骨 IK 应把脚踝带到目标、保持骨长与脚朝向。
            {
                int ci = FindClip(db, "ALS_N_Walk_F");
                if (ci >= 0 && skel.ThighL >= 0 && skel.CalfL >= 0 && skel.FootL >= 0)
                {
                    var clip = db.Clips[ci];
                    var scratch = new float[Math.Max(db.MaxStride, 1)];
                    var local = new Matrix[skel.Count];
                    var comp0 = new Matrix[skel.Count];
                    var pose = new GaspPoseBuffer(skel.Count);
                    GaspPoseUtil.SampleClip(clip, clip.Length * 0.35f, skel, pose, scratch);
                    GaspPoseUtil.ComposeComponent(pose, skel, local, comp0, true);

                    Vector3 ank0 = comp0[skel.FootL].TranslationVector;
                    Vector3 hip0 = comp0[skel.ThighL].TranslationVector;
                    Vector3 knee0 = comp0[skel.CalfL].TranslationVector;
                    float l1 = (knee0 - hip0).Length;
                    float l2 = (ank0 - knee0).Length;
                    var footRot0 = Quaternion.RotationMatrix(comp0[skel.FootL]);
                    Vector3 target = ank0 + new Vector3(0, 25f, 0);

                    float[] kerr = new float[4];
                    float[] aerr = new float[4];
                    int bestMode = -1;
                    for (int mode = 0; mode < 4; mode++)
                    {
                        var c = new Matrix[skel.Count];
                        var l = new Matrix[skel.Count];
                        var p2 = new GaspPoseBuffer(skel.Count);
                        GaspPoseUtil.SampleClip(clip, clip.Length * 0.35f, skel, p2, scratch);
                        GaspPoseUtil.ComposeComponent(p2, skel, l, c, true);
                        GaspLegSolver.SolveLegComponent(c, skel, skel.ThighL, skel.CalfL, skel.FootL, target, mode, out kerr[mode], out aerr[mode]);
                        if (kerr[mode] < 0.5f && aerr[mode] < 0.5f) bestMode = mode;
                    }
                    Check("legik.composeOrder", bestMode >= 0,
                        $"knee=[{kerr[0]:F1},{kerr[1]:F1},{kerr[2]:F1},{kerr[3]:F1}] ankle=[{aerr[0]:F1},{aerr[1]:F1},{aerr[2]:F1},{aerr[3]:F1}] best={bestMode}");
                    if (bestMode < 0) bestMode = 0;

                    var comp = new Matrix[skel.Count];
                    var loc = new Matrix[skel.Count];
                    var pose2 = new GaspPoseBuffer(skel.Count);
                    GaspPoseUtil.SampleClip(clip, clip.Length * 0.35f, skel, pose2, scratch);
                    GaspPoseUtil.ComposeComponent(pose2, skel, loc, comp, true);
                    GaspLegSolver.SolveLegComponent(comp, skel, skel.ThighL, skel.CalfL, skel.FootL, target, bestMode, out _, out float ankErr);
                    Vector3 ank1 = comp[skel.FootL].TranslationVector;
                    Vector3 knee1 = comp[skel.CalfL].TranslationVector;
                    float l1b = (knee1 - comp[skel.ThighL].TranslationVector).Length;
                    float l2b = (ank1 - knee1).Length;
                    float rotDot = Math.Abs(Quaternion.Dot(footRot0, Quaternion.RotationMatrix(comp[skel.FootL])));
                    Check("legik.solve", ankErr < 1.0f, $"err={ankErr:F2}cm kneeMove={(knee1 - knee0).Length:F1}cm");
                    Check("legik.boneLengths", Math.Abs(l1b - l1) < 0.5f && Math.Abs(l2b - l2) < 0.5f,
                        $"l1={l1:F1}/{l1b:F1} l2={l2:F1}/{l2b:F1}");
                    Check("legik.footRotation", rotDot > 0.995f, $"dot={rotDot:F4}");

                    // 不可达目标：到达极限但骨长保持。
                    var c3 = new Matrix[skel.Count];
                    var l3 = new Matrix[skel.Count];
                    var p3 = new GaspPoseBuffer(skel.Count);
                    GaspPoseUtil.SampleClip(clip, clip.Length * 0.35f, skel, p3, scratch);
                    GaspPoseUtil.ComposeComponent(p3, skel, l3, c3, true);
                    Vector3 unreachable = ank0 + new Vector3(0, 200f, 0);
                    GaspLegSolver.SolveLegComponent(c3, skel, skel.ThighL, skel.CalfL, skel.FootL, unreachable, bestMode, out _, out _);
                    Vector3 ank3 = c3[skel.FootL].TranslationVector;
                    Vector3 knee3 = c3[skel.CalfL].TranslationVector;
                    float reach = (ank3 - c3[skel.ThighL].TranslationVector).Length;
                    float l1c = (knee3 - c3[skel.ThighL].TranslationVector).Length;
                    float l2c = (ank3 - knee3).Length;
                    Check("legik.unreachable", reach <= (l1 + l2) + 0.5f && Math.Abs(l1c - l1) < 0.5f && Math.Abs(l2c - l2) < 0.5f,
                        $"reach={reach:F1}/{l1 + l2:F1}");

                    // 骨盆下沉要带动整副骨架（脚也跟着降）。
                    var c4 = new Matrix[skel.Count];
                    var l4 = new Matrix[skel.Count];
                    var p4 = new GaspPoseBuffer(skel.Count);
                    GaspPoseUtil.SampleClip(clip, clip.Length * 0.35f, skel, p4, scratch);
                    GaspPoseUtil.ComposeComponent(p4, skel, l4, c4, true);
                    float pelvisY0 = c4[skel.Pelvis].TranslationVector.Y;
                    float footY0 = c4[skel.FootL].TranslationVector.Y;
                    GaspLegSolver.OffsetPelvis(c4, skel, -10f);
                    float pelvisY1 = c4[skel.Pelvis].TranslationVector.Y;
                    float footY1 = c4[skel.FootL].TranslationVector.Y;
                    Check("legik.pelvisOffset", Math.Abs(pelvisY1 - (pelvisY0 - 10f)) < 0.1f && Math.Abs(footY1 - (footY0 - 10f)) < 0.1f,
                        $"pelvis {pelvisY0:F1}->{pelvisY1:F1} foot {footY0:F1}->{footY1:F1}");
                    float drop = GaspLegSolver.ComputePelvisDrop(ank0, ank0 + new Vector3(0, -15, 0), ank0, ank0, 25f);
                    Check("legik.pelvisDrop", Math.Abs(drop + 15f) < 0.01f, $"drop={drop:F1}");
                }
            }

            // 5.5) 方向性查询：前/后/左/右应命中对应方向族；BlendStack 淡入淡出/非循环钳制。
            {
                int dirWalkClip = FindClip(db, "ALS_N_Walk_F");
                if (dirWalkClip >= 0)
                {
                    var init = new GaspPoseBuffer(skel.Count);
                    var sc = new float[Math.Max(db.MaxStride, 1)];
                    GaspPoseUtil.SampleClip(db.Clips[dirWalkClip], 0f, skel, init, sc);
                    var il = new Matrix[skel.Count];
                    var ic = new Matrix[skel.Count];
                    GaspPoseUtil.ComposeComponent(init, skel, il, ic, true);
                    var qb = new GaspQueryBuilder(skel, ic, mdb.ForwardYaw);
                    var query = new float[GaspFeatureSchema.Dimension];
                    float dirWalkSpeed = mdb.WalkSpeed;

                    GaspSearchResult DirQuery(Vector3 modelDir)
                    {
                        // 模型空间前向为 -Z；模型→世界是绕 Y 旋转 π，所以世界速度 = -modelDir。
                        var worldDir = new Vector3(-modelDir.X, 0, -modelDir.Z);
                        var input = new GaspMotionInput
                        {
                            Velocity = worldDir * dirWalkSpeed,
                            DesiredVelocityLocal = modelDir * dirWalkSpeed,
                            HasInput = true,
                            Grounded = true,
                        };
                        qb.Build(input, 1f / 60f, query);
                        mdb.Search(query, -1, 0f, 0f, 0f, recent, -10f, 1, out var best, out _);
                        return best;
                    }

                    var fwd = DirQuery(new Vector3(0, 0, -1));
                    var back = DirQuery(new Vector3(0, 0, 1));
                    var left = DirQuery(new Vector3(1, 0, 0));
                    var right = DirQuery(new Vector3(-1, 0, 0));
                    Check("query.dirF", fwd.Valid && db.Clips[fwd.Clip].Name.Contains("_F"), fwd.Valid ? db.Clips[fwd.Clip].Name : "<none>");
                    Check("query.dirB", back.Valid && (db.Clips[back.Clip].Name.Contains("_B") || db.Clips[back.Clip].Name.Contains("LB") || db.Clips[back.Clip].Name.Contains("RB")),
                        back.Valid ? db.Clips[back.Clip].Name : "<none>");
                    Check("query.dirL", left.Valid && db.Clips[left.Clip].Name.Contains("L"), left.Valid ? db.Clips[left.Clip].Name : "<none>");
                    Check("query.dirR", right.Valid && db.Clips[right.Clip].Name.Contains("R"), right.Valid ? db.Clips[right.Clip].Name : "<none>");

                    // 调试绘制数据源：查询轨迹世界采样（过去=输入位置回退、未来=沿世界速度方向且保持平面）。
                    {
                        var origin = new Vector3(120f, 7f, -90f);
                        var tInput = new GaspMotionInput
                        {
                            Position = origin,
                            Yaw = 0f,
                            Velocity = new Vector3(0f, 0f, dirWalkSpeed),
                            DesiredVelocityLocal = new Vector3(0f, 0f, -dirWalkSpeed),
                            HasInput = true,
                            Grounded = true,
                        };
                        qb.Build(tInput, 1f / 60f, query);
                        var wt = qb.LastWorldTrajectory;
                        var tt = GaspFeatureSchema.TrajectoryTimes;
                        int pastCount = 0, futureCount = 0, pastBad = 0;
                        bool forwardOk = true, planarOk = true;
                        float prevZ = origin.Z;
                        for (int i = 0; i < wt.Length && i < tt.Length; i++)
                        {
                            if (tt[i] < 0f)
                            {
                                pastCount++;
                                if (Math.Abs(wt[i].X - origin.X) > 0.01f || Math.Abs(wt[i].Z - origin.Z) > 0.01f)
                                    pastBad++;
                            }
                            else
                            {
                                futureCount++;
                                if (Math.Abs(wt[i].X - origin.X) > 1f || wt[i].Z <= prevZ + 0.5f)
                                    forwardOk = false;
                                if (Math.Abs(wt[i].Y - origin.Y) > 1f)
                                    planarOk = false;
                                prevZ = wt[i].Z;
                            }
                        }
                        Check("traj.pastUsesPosition", pastCount == 3 && pastBad == 0, $"n={pastCount} bad={pastBad}");
                        Check("traj.futureForward", futureCount == 8 && forwardOk,
                            $"n={futureCount} endZ={wt[wt.Length - 1].Z:F1} originZ={origin.Z:F1}");
                        Check("traj.futurePlanar", planarOk, $"yEnd={wt[wt.Length - 1].Y:F1} originY={origin.Y:F1}");
                    }

                    // BlendStack：播放→双播放器淡入→旧播放器移除；非循环剪辑时间钳制。
                    var bs = new GaspBlendStack();
                    bs.Init(skel, Math.Max(db.MaxStride, 1));
                    int blendIdleClip = FindClip(db, "ALS_N_SecondaryMotion");
                    int walk2 = FindClip(db, "ALS_N_Walk_F");
                    int mantle = FindClip(db, "ALS_N_Mantle_1m_LH");
                    if (blendIdleClip >= 0 && walk2 >= 0)
                    {
                        bs.Play(blendIdleClip, 0f, true, 0.1f, 0.1f);
                        bs.Update(0.2f, db);
                        var baseLocal = new Matrix[skel.Count];
                        var outPose = new GaspPoseBuffer(skel.Count);
                        bs.Evaluate(db, baseLocal, outPose);
                        Check("blend.single", bs.PlayerCount == 1 && outPose.Covered[skel.FootL], $"players={bs.PlayerCount}");

                        bs.Play(walk2, 0f, true, 0.1f, 0.1f);
                        bs.Update(0.05f, db);
                        bs.Evaluate(db, baseLocal, outPose);
                        Check("blend.crossfade", bs.PlayerCount == 2, $"players={bs.PlayerCount}");
                        bs.Update(0.5f, db);
                        bs.Evaluate(db, baseLocal, outPose);
                        Check("blend.settle", bs.PlayerCount == 1, $"players={bs.PlayerCount}");

                        if (mantle >= 0)
                        {
                            bs.Play(mantle, 0f, false, 0.02f, 0.02f);
                            bs.Update(db.Clips[mantle].Length + 1f, db);
                            Check("blend.nonLoopClamp", Mathf.Abs(bs.MainTime - db.Clips[mantle].Length) < 0.01f,
                                $"t={bs.MainTime:F2} len={db.Clips[mantle].Length:F2}");
                        }
                    }
                }
            }

            // 5.4b) 转向扭曲纯几何：绕骨盆竖轴刚体旋转、枢轴不动、角度/钳制/逼近正确。
            {
                int walkW = FindClip(db, "ALS_N_Walk_F");
                if (walkW >= 0)
                {
                    var wp = new GaspPoseBuffer(skel.Count);
                    var wsc = new float[Math.Max(db.MaxStride, 1)];
                    var wl = new Matrix[skel.Count];
                    var wc = new Matrix[skel.Count];
                    GaspPoseUtil.SampleClip(db.Clips[walkW], 0.3f, skel, wp, wsc);
                    GaspPoseUtil.ComposeComponent(wp, skel, wl, wc, true);

                    int pv = skel.Pelvis, hd = skel.Head, ft = skel.FootL;
                    var pivot0 = wc[pv].TranslationVector;
                    var head0 = wc[hd].TranslationVector;
                    var foot0 = wc[ft].TranslationVector;
                    float headDist0 = (head0 - pivot0).Length;
                    float footDist0 = (foot0 - pivot0).Length;

                    GaspPoseUtil.SampleClip(db.Clips[walkW], 0.3f, skel, wp, wsc);
                    GaspPoseUtil.ComposeComponent(wp, skel, wl, wc, true);
                    float applied = GaspPoseWarp.RotateYAbout(wc, pv, 30f * Mathf.DegreesToRadians);
                    var pivot1 = wc[pv].TranslationVector;
                    var head1 = wc[hd].TranslationVector;
                    var foot1 = wc[ft].TranslationVector;

                    Check("warp.pivotFixed", (pivot1 - pivot0).Length < 1e-3f,
                        $"d={(pivot1 - pivot0).Length:F4}");
                    Check("warp.rigidDistances",
                        Math.Abs((head1 - pivot1).Length - headDist0) < 1e-2f
                        && Math.Abs((foot1 - pivot1).Length - footDist0) < 1e-2f,
                        $"head {headDist0:F2}->{(head1 - pivot1).Length:F2} foot {footDist0:F2}->{(foot1 - pivot1).Length:F2}");

                    // 有符号水平角变化 ≈ 施加角（用 atan2 比较方向）。
                    float a0 = Mathf.Atan2(head0.Z - pivot0.Z, head0.X - pivot0.X);
                    float a1 = Mathf.Atan2(head1.Z - pivot1.Z, head1.X - pivot1.X);
                    float dAng = Mathf.Abs(GaspPoseWarp.WrapAngle(a1 - a0));
                    Check("warp.angleApplied",
                        Math.Abs(dAng - 30f * Mathf.DegreesToRadians) < 1f * Mathf.DegreesToRadians
                        && Math.Abs(applied - 30f * Mathf.DegreesToRadians) < 1e-4f,
                        $"dAng={dAng * Mathf.RadiansToDegrees:F2}deg applied={applied * Mathf.RadiansToDegrees:F2}deg");
                }

                // 钳制与速度缩放：60° 差、满速 → 30°；半速 → 15°；静止 → 0。
                float walkSp = Math.Max(1f, mdb.WalkSpeed);
                float sFull = GaspPoseWarp.ComputeSteer(60f * Mathf.DegreesToRadians, 0f, walkSp, walkSp, 30f);
                float sHalf = GaspPoseWarp.ComputeSteer(60f * Mathf.DegreesToRadians, 0f, walkSp * 0.5f, walkSp, 30f);
                float sNone = GaspPoseWarp.ComputeSteer(60f * Mathf.DegreesToRadians, 0f, 0f, walkSp, 30f);
                Check("warp.steerClampScale",
                    Math.Abs(sFull * Mathf.RadiansToDegrees - 30f) < 0.5f
                    && Math.Abs(sHalf * Mathf.RadiansToDegrees - 15f) < 0.5f
                    && Math.Abs(sNone) < 1e-6f,
                    $"full={sFull * Mathf.RadiansToDegrees:F1} half={sHalf * Mathf.RadiansToDegrees:F1} none={sNone:F3}");

                float ap = GaspPoseWarp.Approach(0f, 30f * Mathf.DegreesToRadians, 90f, 0.1f);
                Check("warp.approachRate", Math.Abs(ap * Mathf.RadiansToDegrees - 9f) < 0.2f,
                    $"after90degps_0.1s={ap * Mathf.RadiansToDegrees:F2}deg");
            }

            // 5.5b) 骨骼掩码分层 + 剖面通知：上半身叠加层不影响下半身；淡入到位/移除各发事件。
            {
                int walk3 = FindClip(db, "ALS_N_Walk_F");
                int mantle3 = FindClip(db, "ALS_N_Mantle_1m_LH");
                var maskUp = GaspBoneMask.Subtree(skel, "spine_01");
                Check("mask.built", maskUp != null && GaspBoneMask.Count(maskUp) > 20,
                    $"count={(maskUp != null ? GaspBoneMask.Count(maskUp) : -1)}/{skel.Count}");
                if (maskUp != null)
                {
                    int pelvisN = skel.Pelvis, headN = skel.Head, handN = skel.HandL, footN = skel.FootL;
                    Check("mask.upperOnly",
                        !maskUp[pelvisN] && !maskUp[footN] && maskUp[headN] && maskUp[handN],
                        $"pelvis={maskUp[pelvisN]} foot={maskUp[footN]} head={maskUp[headN]} hand={maskUp[handN]}");
                }
                if (walk3 >= 0 && mantle3 >= 0 && maskUp != null)
                {
                    var events = new List<string>();
                    var stack = new GaspBlendStack();
                    stack.Init(skel, Math.Max(db.MaxStride, 1));
                    stack.BlendStarted += p => events.Add("start:" + p);
                    stack.BlendCompleted += p => events.Add("done:" + p);
                    stack.BlendFinished += p => events.Add("end:" + p);

                    stack.Play(walk3, 0.2f, true, 0.02f, 0.02f);
                    stack.Update(0.1f, db);
                    var basePose = new GaspPoseBuffer(skel.Count);
                    stack.Evaluate(db, null, basePose);

                    // walk 基础层会随时间推进：参考姿态必须按同一时刻直接采样，而不是比较更早的 basePose。
                    var walkScratch = new float[Math.Max(db.MaxStride, 1)];
                    var refPose = new GaspPoseBuffer(skel.Count);
                    float walkTimeBase = 0.2f + 0.1f;   // Play 起点 + 首次 Update

                    stack.PlayOverlay(mantle3, 0.2f, false, 0.05f, 0.05f, maskUp, "upper-test");
                    stack.Update(0.2f, db);
                    var mixed = new GaspPoseBuffer(skel.Count);
                    stack.Evaluate(db, null, mixed);
                    float tMixed = walkTimeBase + 0.2f;
                    if (db.Clips[walk3].Loop)
                        tMixed %= db.Clips[walk3].Length;
                    GaspPoseUtil.SampleClip(db.Clips[walk3], tMixed, skel, refPose, walkScratch);

                    int pelvisN = skel.Pelvis, headN = skel.Head, footN = skel.FootL;
                    Check("mask.lowerUntouched",
                        SameTransform(refPose.Local[pelvisN], mixed.Local[pelvisN])
                        && SameTransform(refPose.Local[footN], mixed.Local[footN]),
                        $"pelvisSame={SameTransform(refPose.Local[pelvisN], mixed.Local[pelvisN])}"
                        + $" footSame={SameTransform(refPose.Local[footN], mixed.Local[footN])}");
                    Check("mask.upperApplied", !SameTransform(refPose.Local[headN], mixed.Local[headN]),
                        $"headSame={SameTransform(refPose.Local[headN], mixed.Local[headN])}");
                    Check("mask.profileEvents",
                        events.Contains("start:upper-test") && events.Contains("done:upper-test"),
                        string.Join(",", events));

                    stack.StopProfile("upper-test", 0.05f);
                    stack.Update(0.2f, db);
                    var after = new GaspPoseBuffer(skel.Count);
                    stack.Evaluate(db, null, after);
                    float tAfter = tMixed + 0.2f;
                    if (db.Clips[walk3].Loop)
                        tAfter %= db.Clips[walk3].Length;
                    GaspPoseUtil.SampleClip(db.Clips[walk3], tAfter, skel, refPose, walkScratch);
                    Check("mask.profileFinished", events.Contains("end:upper-test"), string.Join(",", events));
                    Check("mask.layerRemoved", SameTransform(refPose.Local[headN], after.Local[headN]),
                        $"headSame={SameTransform(refPose.Local[headN], after.Local[headN])}");
                    stack.StopAll(0.05f);
                }
            }

            // 5.6b) 全剪辑绑定 + 根位移剥离：Mantle 必须真的在动，且根骨不得把整具骨架搬离 Actor。
            //       实机踩到：只有 Ground 剪辑被绑定，Mantle 通道全部未命中 → 回落 bind 姿态（躺姿、0 骨骼在动）。
            {
                int rootNode = skel.Find("root");
                Check("clip.rootNodeResolved", rootNode >= 0, $"root={rootNode}");

                // 全库不变量：每个剪辑都真的有骨骼在动（此前 Air/Traversal 因未绑定是静态 bind 姿态）。
                int staticClips = 0; string firstStatic = "";
                var pA = new GaspPoseBuffer(skel.Count);
                var pB = new GaspPoseBuffer(skel.Count);
                var scAll = new float[Math.Max(db.MaxStride, 1)];
                foreach (var cAll in db.Clips)
                {
                    GaspPoseUtil.SampleClip(cAll, 0f, skel, pA, scAll);
                    GaspPoseUtil.SampleClip(cAll, cAll.Length * 0.5f, skel, pB, scAll);
                    int moved = 0;
                    for (int bi = 0; bi < skel.Count; bi++)
                    {
                        float dt2 = (pA.Local[bi].Translation - pB.Local[bi].Translation).Length;
                        float dq = Math.Abs(Quaternion.Dot(pA.Local[bi].Orientation, pB.Local[bi].Orientation));
                        float dqAng = 2f * (float)Math.Acos(Mathf.Clamp(dq, -1f, 1f)) * Mathf.RadiansToDegrees;
                        if (Math.Max(dt2, dqAng) > 0.1f) moved++;
                    }
                    if (moved == 0) { staticClips++; if (firstStatic.Length == 0) firstStatic = cAll.Name; }
                }
                Check("clip.allClipsAnimate", staticClips == 0, $"static={staticClips} first='{firstStatic}'");

                int mantleIdx = FindClip(db, "ALS_N_Mantle_1m_LH");
                Check("clip.mantleExists", mantleIdx >= 0);
                if (mantleIdx >= 0)
                {
                    var cc = db.Clips[mantleIdx];
                    // ik_* 只在源绑定里存在（Ada 骨架没有这些 IK 助手骨），未命中是预期的；
                    // 其余任何未命中都说明"按名绑定失效"，会让整段剪辑静默回落到 bind 姿态。
                    int unbound = 0, unboundNonIk = 0;
                    string firstUnbound = "", firstNonIk = "";
                    for (int i = 0; i < cc.Channels.Length; i++)
                    {
                        if (cc.Channels[i].NodeIndex < 0)
                        {
                            unbound++;
                            if (firstUnbound.Length == 0)
                                firstUnbound = cc.Channels[i].Name;
                            if (!cc.Channels[i].Name.StartsWith("ik_", StringComparison.OrdinalIgnoreCase))
                            {
                                unboundNonIk++;
                                if (firstNonIk.Length == 0)
                                    firstNonIk = cc.Channels[i].Name;
                            }
                        }
                    }
                    Check("clip.mantleChannelsBound", unboundNonIk == 0,
                        $"channels={cc.Channels.Length} unbound={unbound} nonIk={unboundNonIk} first='{firstUnbound}' firstNonIk='{firstNonIk}'");

                    var poseA = new GaspPoseBuffer(skel.Count);
                    var poseB = new GaspPoseBuffer(skel.Count);
                    var sc2 = new float[Math.Max(db.MaxStride, 1)];
                    GaspPoseUtil.SampleClip(cc, 0f, skel, poseA, sc2);
                    GaspPoseUtil.SampleClip(cc, cc.Length * 0.5f, skel, poseB, sc2);
                    float motion = 0f;
                    int movedBones = 0;
                    for (int bi = 0; bi < skel.Count; bi++)
                    {
                        float dt2 = (poseA.Local[bi].Translation - poseB.Local[bi].Translation).Length;
                        float dq = Math.Abs(Quaternion.Dot(poseA.Local[bi].Orientation, poseB.Local[bi].Orientation));
                        float dqAng = 2f * (float)Math.Acos(Mathf.Clamp(dq, -1f, 1f)) * Mathf.RadiansToDegrees;
                        float m2 = Math.Max(dt2, dqAng);
                        if (m2 > 0.1f) movedBones++;
                        if (m2 > motion) motion = m2;
                    }
                    Check("clip.mantleAnimates", movedBones >= 10,
                        $"movedBones={movedBones}/{skel.Count} maxDelta={motion:F1}(cm|deg)");

                    // 根骨平移必须被剥离（位移权威在 ECS），否则会与翻越计划叠加成双倍位移。
                    var bindRootT = skel.Bind[rootNode].Translation;
                    float maxRootDev = 0f;
                    var pose2 = new GaspPoseBuffer(skel.Count);
                    var loc2 = new Matrix[skel.Count];
                    var cmp2 = new Matrix[skel.Count];
                    string pelvisTraj = "";
                    for (int phase = 0; phase <= 4; phase++)
                    {
                        float tt = cc.Length * phase / 4f;
                        GaspPoseUtil.SampleClip(cc, tt, skel, pose2, sc2);
                        maxRootDev = Math.Max(maxRootDev, (pose2.Local[rootNode].Translation - bindRootT).Length);
                        GaspPoseUtil.ComposeComponent(pose2, skel, loc2, cmp2, true);
                        var pv = cmp2[skel.Pelvis].TranslationVector;
                        pelvisTraj += $" t={tt:F1}:({pv.X:F0},{pv.Y:F0},{pv.Z:F0})";
                        if (phase == 0 || phase == 4)
                            Check($"clip.mantlePelvisUpright{phase}", pv.Y > 70f, $"pelvisY={pv.Y:F0}");
                    }
                    Check("clip.rootTranslationStripped", maxRootDev < 0.01f, $"maxRootDev={maxRootDev:F3}cm");
                    Check("clip.mantlePelvisDiag", true, pelvisTraj);
                }
            }

            // 5.6a) 待机候选：SecondaryMotion 是否全身直立、首尾是否闭合（可循环）。
            {
                int si = FindClip(db, "ALS_N_SecondaryMotion");
                Check("idle.secondaryExists", si >= 0);
                if (si >= 0)
                {
                    var ic = db.Clips[si];
                    var ip = new GaspPoseBuffer(skel.Count);
                    var isc = new float[Math.Max(db.MaxStride, 1)];
                    var il = new Matrix[skel.Count];
                    var iw = new Matrix[skel.Count];
                    GaspPoseUtil.SampleClip(ic, 0f, skel, ip, isc);
                    GaspPoseUtil.ComposeComponent(ip, skel, il, iw, true);
                    var p0 = iw[skel.Pelvis].TranslationVector;
                    var f0 = iw[skel.FootL].TranslationVector;
                    GaspPoseUtil.SampleClip(ic, ic.Length, skel, ip, isc);
                    GaspPoseUtil.ComposeComponent(ip, skel, il, iw, true);
                    var p1 = iw[skel.Pelvis].TranslationVector;
                    float loopGap = (p1 - p0).Length;
                    Check("idle.secondaryUpright", p0.Y > 80f && f0.Y < 20f,
                        $"pelvisY={p0.Y:F0} footLY={f0.Y:F0}");
                    Check("idle.secondaryLoopable", ic.Loop && loopGap < 2f,
                        $"loop={ic.Loop} pelvisGap={loopGap:F1}cm");
                    Check("idle.secondaryDiag", true,
                        $"len={ic.Length:F2} pelvis0=({p0.X:F0},{p0.Y:F0},{p0.Z:F0}) pelvisEnd=({p1.X:F0},{p1.Y:F0},{p1.Z:F0})");
                }
            }

            // 5.6) 全剪辑姿态健全性：局部平移/缩放有限、四元数归一、组件空间点到骨盆的距离有界。
            //      实机里 Mantle 后角色呈"炸开"状，先用离线数据回答"是剪辑本身坏了还是运行期接线/蒙皮"。
            {
                var poseBuf = new GaspPoseBuffer(skel.Count);
                var scratch = new float[Math.Max(db.MaxStride, 1)];
                var localM = new Matrix[skel.Count];
                var compM = new Matrix[skel.Count];
                float worst = 0f, worstScale = 0f, worstQuat = 0f, worstExtent = 0f;
                string worstClip = "", worstQuatClip = "";
                foreach (var clip in db.Clips)
                {
                    for (int phase = 0; phase < 3; phase++)
                    {
                        float t = clip.Length * phase / 3f;
                        GaspPoseUtil.SampleClip(clip, t, skel, poseBuf, scratch);
                        for (int i = 0; i < skel.Count; i++)
                        {
                            var tr = poseBuf.Local[i];
                            float len = (float)Math.Sqrt(
                                tr.Translation.X * tr.Translation.X
                                + tr.Translation.Y * tr.Translation.Y
                                + tr.Translation.Z * tr.Translation.Z);
                            if (len > worst) { worst = len; worstClip = clip.Name; }
                            float scaleErr = Math.Max(
                                Math.Abs(tr.Scale.X - 1f),
                                Math.Max(Math.Abs(tr.Scale.Y - 1f), Math.Abs(tr.Scale.Z - 1f)));
                            if (scaleErr > worstScale) worstScale = scaleErr;
                            float qlen = (float)Math.Sqrt(
                                tr.Orientation.X * tr.Orientation.X + tr.Orientation.Y * tr.Orientation.Y
                                + tr.Orientation.Z * tr.Orientation.Z + tr.Orientation.W * tr.Orientation.W);
                            float qerr = Math.Abs(qlen - 1f);
                            if (qerr > worstQuat) { worstQuat = qerr; worstQuatClip = clip.Name; }
                        }

                        GaspPoseUtil.ComposeComponent(poseBuf, skel, localM, compM, true);
                        var pelvis = compM[skel.Pelvis].TranslationVector;
                        for (int i = 0; i < skel.Count; i++)
                        {
                            var p = compM[i].TranslationVector;
                            float d = (p - pelvis).Length;
                            if (!IsFinite(p) || d > worstExtent) worstExtent = d;
                        }
                    }
                }
                Check("pose.localTranslationBounded", worst <= 300f,
                    $"worst={worst:F1}cm clip={worstClip}");
                Check("pose.scaleBounded", worstScale <= 0.15f, $"worstScaleErr={worstScale:F3}");
                Check("pose.quaternionNormalized", worstQuat <= 0.01f,
                    $"worstQuatErr={worstQuat:F4} clip={worstQuatClip}");
                Check("pose.extentBounded", worstExtent <= 400f, $"worstExtent={worstExtent:F1}cm");
            }

            // 5.7) 脚掌对齐地面：脚底法线（Ada 脚骨局部 X 轴）对齐地面、平移保持、最大倾角钳制。
            {
                int ci = FindClip(db, "ALS_N_Walk_F");
                if (ci >= 0 && skel.FootL >= 0)
                {
                    var clip = db.Clips[ci];
                    var pose = new GaspPoseBuffer(skel.Count);
                    var sc = new float[Math.Max(db.MaxStride, 1)];
                    var local = new Matrix[skel.Count];
                    var comp = new Matrix[skel.Count];
                    GaspPoseUtil.SampleClip(clip, clip.Length * 0.2f, skel, pose, sc);
                    GaspPoseUtil.ComposeComponent(pose, skel, local, comp, true);

                    int foot = skel.FootL;
                    int ball = skel.BallL;
                    Vector3 footPos0 = comp[foot].TranslationVector;
                    float footBall0 = ball >= 0 ? (comp[ball].TranslationVector - footPos0).Length : 0f;
                    Matrix rot0 = comp[foot]; rot0.M41 = rot0.M42 = rot0.M43 = 0f;
                    Vector3 up0 = Vector3.Transform(Vector3.Right, rot0).Normalized;

                    // 平地：脚底法线应摆到正上方（超过上限时按上限留残差）。
                    float flatDeg = Mathf.Acos(Mathf.Clamp(Vector3.Dot(up0, Vector3.Up), -1f, 1f)) * Mathf.RadiansToDegrees;
                    float flatApplied = GaspLegSolver.AlignFootToGround(comp, skel, foot, Vector3.Up, 30f);
                    Matrix rot1 = comp[foot]; rot1.M41 = rot1.M42 = rot1.M43 = 0f;
                    Vector3 up1 = Vector3.Transform(Vector3.Right, rot1).Normalized;
                    float flatResidual = Mathf.Max(0f, flatDeg - 30f);
                    Check("footalign.flat",
                        Mathf.Abs(flatApplied - Mathf.Min(flatDeg, 30f)) < 0.5f
                        && Vector3.Dot(up1, Vector3.Up) > Mathf.Cos((flatResidual + 0.5f) * Mathf.DegreesToRadians),
                        $"before={flatDeg:F1}deg applied={flatApplied:F1} dot={Vector3.Dot(up1, Vector3.Up):F4}");

                    // 目标法线 = 当前脚底法线绕水平轴倾斜 20°：应精确走到目标。
                    Vector3 tiltAxis = Vector3.Cross(up0, Vector3.Forward);
                    if (tiltAxis.LengthSquared < 1e-4f)
                        tiltAxis = Vector3.Cross(up0, Vector3.Right);
                    tiltAxis.Normalize();
                    Vector3 normal20 = Vector3.Transform(up0, Quaternion.RotationAxis(tiltAxis, 20f * Mathf.DegreesToRadians)).Normalized;
                    var comp2 = new Matrix[skel.Count];
                    var local2 = new Matrix[skel.Count];
                    GaspPoseUtil.SampleClip(clip, clip.Length * 0.2f, skel, pose, sc);
                    GaspPoseUtil.ComposeComponent(pose, skel, local2, comp2, true);
                    float applied = GaspLegSolver.AlignFootToGround(comp2, skel, foot, normal20, 30f);
                    Vector3 footPos2 = comp2[foot].TranslationVector;
                    Matrix rot2 = comp2[foot]; rot2.M41 = rot2.M42 = rot2.M43 = 0f;
                    Vector3 up2 = Vector3.Transform(Vector3.Right, rot2).Normalized;
                    float footBall2 = ball >= 0 ? (comp2[ball].TranslationVector - footPos2).Length : 0f;
                    Check("footalign.align", Mathf.Abs(applied - 20f) < 1f && Vector3.Dot(up2, normal20) > 0.999f,
                        $"applied={applied:F1} dot={Vector3.Dot(up2, normal20):F3}");
                    Check("footalign.translation", (footPos2 - footPos0).Length < 0.05f,
                        $"d={(footPos2 - footPos0).Length:F3}");
                    Check("footalign.childFollow", ball < 0 || Mathf.Abs(footBall2 - footBall0) < 0.2f,
                        $"footBall {footBall0:F2}->{footBall2:F2}");

                    Vector3 normal80 = Vector3.Transform(up0, Quaternion.RotationAxis(tiltAxis, 80f * Mathf.DegreesToRadians)).Normalized;
                    var comp3 = new Matrix[skel.Count];
                    var local3 = new Matrix[skel.Count];
                    GaspPoseUtil.SampleClip(clip, clip.Length * 0.2f, skel, pose, sc);
                    GaspPoseUtil.ComposeComponent(pose, skel, local3, comp3, true);
                    float appliedClamp = GaspLegSolver.AlignFootToGround(comp3, skel, foot, normal80, 30f);
                    Matrix rot3 = comp3[foot]; rot3.M41 = rot3.M42 = rot3.M43 = 0f;
                    Vector3 up3 = Vector3.Transform(Vector3.Right, rot3).Normalized;
                    float tilt = Mathf.Acos(Mathf.Clamp(Vector3.Dot(up0, up3), -1f, 1f)) * Mathf.RadiansToDegrees;
                    Check("footalign.clamp", Mathf.Abs(appliedClamp - 30f) < 1f && Mathf.Abs(tilt - 30f) < 2f,
                        $"applied={appliedClamp:F1} tilt={tilt:F1}");
                }
            }

            // 6) Traversal 核心：选库、运动扭曲、状态机/冷却。
            {
                var tc = new GaspTraversalController { Database = db };
                var ledge = new GaspLedgeInfo
                {
                    Found = true,
                    WallPoint = new Vector3(0, 80, 300),
                    TopPoint = new Vector3(0, 100, 300),
                    WallNormal = new Vector3(0, 0, -1),
                    Height = 100f,
                    Side = 0,
                };
                int clipL = GaspTraversalSelector.Select(db, ledge, out string reasonL);
                Check("traversal.select1mL", clipL >= 0 && db.Clips[clipL].Name.Contains("Mantle_1m_LH"),
                    reasonL ?? (clipL >= 0 ? db.Clips[clipL].Name : "<none>"));
                var ledgeR = ledge; ledgeR.Side = 1;
                int clipR = GaspTraversalSelector.Select(db, ledgeR, out _);
                Check("traversal.select1mR", clipR >= 0 && db.Clips[clipR].Name.Contains("Mantle_1m_RH"),
                    clipR >= 0 ? db.Clips[clipR].Name : "<none>");
                var ledge2 = ledge; ledge2.Height = 180f;
                int clip2 = GaspTraversalSelector.Select(db, ledge2, out _);
                Check("traversal.select2m", clip2 >= 0 && db.Clips[clip2].Name.Contains("Mantle_2m"),
                    clip2 >= 0 ? db.Clips[clip2].Name : "<none>");
                var ledgeLow = ledge; ledgeLow.Height = 30f;
                var ledgeHigh = ledge; ledgeHigh.Height = 250f;
                var tcRange = new GaspTraversalController { Database = db };
                bool lowRejected = !tcRange.TryStart(ledgeLow, Vector3.Zero, Quaternion.Identity, 0f);
                bool highRejected = !tcRange.TryStart(ledgeHigh, Vector3.Zero, Quaternion.Identity, 0f);
                Check("traversal.rejectHeights", lowRejected && highRejected,
                    $"low={tcRange.LastReason}");

                bool started = tc.TryStart(ledge, Vector3.Zero, Quaternion.Identity, 0f);
                Check("traversal.start", started && tc.Active && tc.Plan != null,
                    tc.LastReason);
                float half = tc.Plan != null ? tc.Plan.Duration * 0.5f : 0.5f;
                tc.Tick(half);
                Vector3 mid = tc.CurrentPosition;
                float lineY = Mathf.Lerp(0f, 100f, 0.5f);
                Check("traversal.arc", tc.Active && mid.Y > lineY + 1f && mid.Z > 0f && mid.Z < 345f,
                    $"mid=({mid.X:F0},{mid.Y:F0},{mid.Z:F0})");
                tc.Tick(half + 0.1f);
                Check("traversal.finish", !tc.Active && tc.FinishCount == 1 && tc.Plan != null);
                Vector3 end = tc.Plan != null ? tc.Plan.EndPosition : Vector3.Zero;
                Check("traversal.end", Mathf.Abs(end.Y - 100f) < 0.1f && Mathf.Abs(end.Z - 345f) < 0.5f,
                    $"end=({end.X:F0},{end.Y:F0},{end.Z:F0})");
                bool restartBlocked = !tc.TryStart(ledge, Vector3.Zero, Quaternion.Identity, 1f);
                Check("traversal.cooldown", restartBlocked);
                tc.Tick(0.5f);
                bool restartOk = tc.TryStart(ledge, Vector3.Zero, Quaternion.Identity, 2f);
                Check("traversal.restart", restartOk, tc.LastReason);

                // 取消后冷却必须能自然衰减：Driver 曾在非 Active 时直接 return，
                // 导致 Tick 不跑、cooldown 永远停在 0.4s —— 一次取消把翻越永久锁死（实机 30 次重试全拒）。
                tc.Cancel();
                bool afterCancelBlocked = !tc.TryStart(ledge, Vector3.Zero, Quaternion.Identity, 3f);
                tc.Tick(0.2f);
                bool afterHalfBlocked = !tc.TryStart(ledge, Vector3.Zero, Quaternion.Identity, 3.2f);
                tc.Tick(0.3f);
                bool afterDecayOk = tc.TryStart(ledge, Vector3.Zero, Quaternion.Identity, 3.5f);
                Check("traversal.cooldownDecay",
                    afterCancelBlocked && afterHalfBlocked && afterDecayOk,
                    $"blocked={afterCancelBlocked} half={afterHalfBlocked} ok={afterDecayOk} reason={tc.LastReason}");
                tc.Cancel();
            }

            Console.WriteLine($"summary pass={_pass} fail={_fail}");
            return _fail == 0 ? 0 : 2;
        }

        private static void SetBind(string[] names, Transform[] bind, string name, Vector3 pos)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    bind[i] = new Transform(pos);
                    return;
                }
            }
        }

        private static int FindClip(GaspAnimationDatabase db, string token)
        {
            for (int i = 0; i < db.Clips.Count; i++)
            {
                if (db.Clips[i].Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
            return -1;
        }

        private static string Family(string name)
        {
            if (name.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0) return "walk";
            if (name.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0) return "run";
            if (name.IndexOf("Sprint", StringComparison.OrdinalIgnoreCase) >= 0) return "sprint";
            if (name.IndexOf("Turn", StringComparison.OrdinalIgnoreCase) >= 0) return "turn";
            if (name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0) return "stop";
            if (name.IndexOf("Transition", StringComparison.OrdinalIgnoreCase) >= 0) return "transition";
            if (name.IndexOf("Secondary", StringComparison.OrdinalIgnoreCase) >= 0) return "idle";
            return "other";
        }
    }
}
