using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    public struct GaspDatabasePose
    {
        public int Clip;
        public int Frame;
        public float Time;
        public float[] Features;
    }

    public struct GaspSearchResult
    {
        public int PoseIndex;
        public int Clip;
        public int Frame;
        public float Time;
        public float Cost;
        public bool Valid => PoseIndex >= 0;
    }

    /// <summary>
    /// 把所有地面剪辑逐帧转成特征向量，并做暴力最近邻检索。
    /// 数据结构与参考实现 mm.h 的 Database 同构，后续可替换为 PCA-KD 树。
    /// </summary>
    public sealed class GaspMotionDatabase
    {
        public GaspAnimationDatabase Anim;
        public GaspSkeleton Skeleton;
        public readonly List<GaspDatabasePose> Poses = new List<GaspDatabasePose>();

        /// <summary>特征空间 KD 树：把全库检索收敛到 top-K 候选（上层再按步态/续播/近期惩罚重排）。</summary>
        public GaspFeatureKdTree KdTree { get; private set; }

        /// <summary>逐剪辑“净转向”（终点朝向 − 起点朝向，弧度，绕竖轴）：静止查询据此避开转身剪辑。</summary>
        public float[] ClipYawDelta = Array.Empty<float>();

        /// <summary>逐剪辑落脚时刻（秒，升序）与左右脚（0=L, 1=R）——Foley/脚步事件的数据源。</summary>
        public readonly List<float[]> ClipPlantTimes = new List<float[]>();
        public readonly List<byte[]> ClipPlantSides = new List<byte[]>();

        /// <summary>最近一次 Search 实际打分的候选数（LOD 证据；暴力模式等于 Poses.Count）。</summary>
        public int LastCandidatesScored { get; private set; }

        private float[] _queryRef;
        private int[] _candIdx;
        private float[] _candDist;
        private int[] _allIdx;
        public readonly List<int> GroundClips = new List<int>();
        public int[] ClipFirstPose;
        public int[] ClipPoseCount;
        /// <summary>每个剪辑从“支撑脚速度”推断出的标称移动速度（模型空间，厘米/秒）。</summary>
        public Vector3[] ClipNominalVelocity;
        public float[] ClipNominalSpeed;
        /// <summary>模型空间里的“前进方向”偏航角（弧度）。Ada+ALS 实测为 π（模型前向是 -Z）。</summary>
        public float ForwardYaw { get; private set; }
        /// <summary>各族标称速度（厘米/秒），用于把游戏速度换算成查询速度。</summary>
        public float WalkSpeed { get; private set; }
        public float RunSpeed { get; private set; }
        public float SprintSpeed { get; private set; }
        public bool IsBuilt { get; private set; }
        public string Error { get; private set; }
        public float BuildMilliseconds { get; private set; }

        private GaspPoseBuffer _pose;
        private GaspPoseBuffer _tmpPose;
        private float[] _scratch;
        private float[] _tmpScratch;
        private Matrix[] _local;
        private Matrix[] _world;
        private Matrix[] _chain;
        private Matrix[] _tmpLocal;
        private Matrix[] _tmpWorld;

        public bool Build(GaspAnimationDatabase anim, GaspSkeleton skeleton)
        {
            Anim = anim;
            Skeleton = skeleton;
            Error = null;
            IsBuilt = false;
            if (anim == null || !anim.IsLoaded)
            {
                Error = "动画库未加载";
                return false;
            }
            if (skeleton == null || skeleton.Pelvis < 0)
            {
                Error = "骨架缺少 pelvis";
                return false;
            }
            if (skeleton.FootL < 0 || skeleton.FootR < 0)
            {
                Error = "骨架缺少 foot_l/foot_r";
                return false;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int clips = anim.Clips.Count;
            ClipFirstPose = new int[clips];
            ClipPoseCount = new int[clips];
            ClipNominalVelocity = new Vector3[clips];
            ClipNominalSpeed = new float[clips];
            for (int i = 0; i < clips; i++)
                ClipFirstPose[i] = -1;

            _pose = new GaspPoseBuffer(skeleton.Count);
            _tmpPose = new GaspPoseBuffer(skeleton.Count);
            _scratch = new float[Mathf.Max(anim.MaxStride, 1)];
            _tmpScratch = new float[Mathf.Max(anim.MaxStride, 1)];
            _local = new Matrix[skeleton.Count];
            _world = new Matrix[skeleton.Count];
            _tmpLocal = new Matrix[skeleton.Count];
            _tmpWorld = new Matrix[skeleton.Count];
            _chain = new Matrix[64];

            // 全剪辑绑定：此前只给 Ground 剪辑绑通道，Air/Traversal 剪辑播放时通道全部未命中、
            // 回落成 bind 姿态（实机 Mantle 呈"躺姿"且 0/343 骨骼在动）。绑定只是按名解析，一次性成本。
            for (int ci0 = 0; ci0 < clips; ci0++)
                skeleton.BindClipChannels(anim.Clips[ci0]);

            // 第一遍：原地剪辑没有骨盆位移，用“支撑脚”速度推断标称移动速度。
            for (int ci = 0; ci < clips; ci++)
            {
                var clip = anim.Clips[ci];
                if (clip.Tag != GaspClipTag.Ground)
                    continue;
                ClipNominalVelocity[ci] = ComputeNominalVelocity(clip);
                ClipNominalSpeed[ci] = ClipNominalVelocity[ci].Length;
            }

            // 用“正向”走/跑/冲刺剪辑的标称速度方向标定模型前向（Ada 实测约 180°）。
            float sinSum = 0f, cosSum = 0f;
            int forwardSamples = 0;
            for (int ci = 0; ci < clips; ci++)
            {
                var c = anim.Clips[ci];
                if (c.Tag != GaspClipTag.Ground || ClipNominalSpeed[ci] < 20f)
                    continue;
                if (c.Name.EndsWith("_F", StringComparison.OrdinalIgnoreCase) &&
                    (c.Name.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     c.Name.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     c.Name.IndexOf("Sprint", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var v = ClipNominalVelocity[ci];
                    float a = Mathf.Atan2(v.X, v.Z);
                    sinSum += Mathf.Sin(a);
                    cosSum += Mathf.Cos(a);
                    forwardSamples++;
                }
            }
            ForwardYaw = forwardSamples > 0 ? Mathf.Atan2(sinSum, cosSum) : 0f;

            WalkSpeed = FamilyMedianSpeed("Walk");
            RunSpeed = FamilyMedianSpeed("Run");
            SprintSpeed = FamilyMedianSpeed("Sprint");

            // 原地剪辑的支撑脚方向对后退/斜向不可靠；方向直接取自剪辑名，速度取族中位数。
            for (int ci = 0; ci < clips; ci++)
            {
                var c = anim.Clips[ci];
                if (c.Tag != GaspClipTag.Ground)
                    continue;
                float familySpeed = c.Name.IndexOf("Sprint", StringComparison.OrdinalIgnoreCase) >= 0 ? SprintSpeed
                    : c.Name.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0 ? RunSpeed
                    : c.Name.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0 ? WalkSpeed
                    : 0f;
                if (familySpeed > 20f && TryNamedDirection(c.Name, out Vector3 dir))
                {
                    ClipNominalVelocity[ci] = dir * familySpeed;
                    ClipNominalSpeed[ci] = familySpeed;
                }
            }

            for (int ci = 0; ci < clips; ci++)
            {
                var clip = anim.Clips[ci];
                if (clip.Tag != GaspClipTag.Ground)
                    continue;
                ClipFirstPose[ci] = Poses.Count;
                GroundClips.Add(ci);
                for (int f = 0; f < clip.FrameCount; f++)
                {
                    float t = f / (float)clip.SampleRate;
                    var features = new float[GaspFeatureSchema.Dimension];
                    BuildFeatures(clip, ClipNominalVelocity[ci], t, features);
                    Poses.Add(new GaspDatabasePose
                    {
                        Clip = ci,
                        Frame = f,
                        Time = t,
                        Features = features,
                    });
                    ClipPoseCount[ci]++;
                }
            }

            sw.Stop();
            BuildMilliseconds = (float)sw.Elapsed.TotalMilliseconds;
            IsBuilt = Poses.Count > 0;
            if (!IsBuilt)
            {
                Error = "没有可用的地面剪辑";
                return false;
            }

            // 净转向：起点 → 终点骨盆朝向差（用于静止查询的转身剪辑惩罚）。
            ClipYawDelta = new float[clips];
            for (int ci = 0; ci < clips; ci++)
            {
                var c = anim.Clips[ci];
                if (Skeleton.Pelvis >= 0)
                {
                    SamplePelvisYaw(c, 0f, out float y0);
                    SamplePelvisYaw(c, c.Length, out float y1);
                    ClipYawDelta[ci] = GaspSkeleton.WrapAngle(y1 - y0);
                }
            }

            // 落脚点（Foley）：对每个剪辑按脚高+脚速检测"支撑脚落地"上升沿。
            ClipPlantTimes.Clear();
            ClipPlantSides.Clear();
            for (int ci = 0; ci < clips; ci++)
                BuildFootPlants(anim.Clips[ci]);

            if (_allIdx == null || _allIdx.Length != Poses.Count)
                _allIdx = new int[Poses.Count];
            for (int i = 0; i < _allIdx.Length; i++)
                _allIdx[i] = i;

            KdTree = new GaspFeatureKdTree();
            KdTree.Build(Poses.ConvertAll(p => p.Features));
            return true;
        }

        /// <summary>
        /// 原地剪辑的“标称移动速度”：取每帧更低的那只脚（支撑脚）的速度，做分量中位数后取反。
        /// 摆动脚速度会正负抵消，支撑脚在整段里持续向后 ⇒ 取反即身体前进方向与速度。
        /// </summary>
        private float FamilyMedianSpeed(string token)
        {
            var list = new List<float>();
            for (int i = 0; i < Anim.Clips.Count; i++)
            {
                var c = Anim.Clips[i];
                if (c.Tag != GaspClipTag.Ground)
                    continue;
                if (c.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (ClipNominalSpeed[i] < 20f)
                    continue;
                list.Add(ClipNominalSpeed[i]);
            }
            if (list.Count == 0)
                return 0f;
            list.Sort();
            return list[list.Count / 2];
        }

        private static bool TryNamedDirection(string clipName, out Vector3 dir)
        {
            // 模型空间：前方 = -Z（Ada+ALS 实测），左 = +X。
            dir = Vector3.Zero;
            if (clipName.EndsWith("_LF", StringComparison.OrdinalIgnoreCase)) { dir = new Vector3(1, 0, -1); }
            else if (clipName.EndsWith("_RF", StringComparison.OrdinalIgnoreCase)) { dir = new Vector3(-1, 0, -1); }
            else if (clipName.EndsWith("_LB", StringComparison.OrdinalIgnoreCase)) { dir = new Vector3(1, 0, 1); }
            else if (clipName.EndsWith("_RB", StringComparison.OrdinalIgnoreCase)) { dir = new Vector3(-1, 0, 1); }
            else if (clipName.EndsWith("_F", StringComparison.OrdinalIgnoreCase)) { dir = new Vector3(0, 0, -1); }
            else if (clipName.EndsWith("_B", StringComparison.OrdinalIgnoreCase)) { dir = new Vector3(0, 0, 1); }
            else if (clipName.EndsWith("_L", StringComparison.OrdinalIgnoreCase)) { dir = new Vector3(1, 0, 0); }
            else if (clipName.EndsWith("_R", StringComparison.OrdinalIgnoreCase)) { dir = new Vector3(-1, 0, 0); }
            else return false;
            dir.Normalize();
            return true;
        }

        private Vector3 ComputeNominalVelocity(GaspClip clip)
        {
            float dt = clip.TimePerFrame;
            // 先找整段里最低的脚高，只采“贴地”帧的速度（支撑相）；摆动相不应参与平均。
            float minY = float.MaxValue;
            for (int f = 0; f < clip.FrameCount; f++)
            {
                float t = f / (float)clip.SampleRate;
                GaspPoseUtil.SampleClip(clip, t, Skeleton, _pose, _scratch);
                GaspPoseUtil.BuildWorld(_pose, Skeleton, _local, _world);
                if (Skeleton.FootL >= 0)
                    minY = Mathf.Min(minY, _world[Skeleton.FootL].TranslationVector.Y);
                if (Skeleton.FootR >= 0)
                    minY = Mathf.Min(minY, _world[Skeleton.FootR].TranslationVector.Y);
            }
            if (minY == float.MaxValue)
                return Vector3.Zero;

            var vx = new List<float>(clip.FrameCount * 2);
            var vz = new List<float>(clip.FrameCount * 2);
            for (int f = 0; f < clip.FrameCount; f++)
            {
                float t = f / (float)clip.SampleRate;
                GaspPoseUtil.SampleClip(clip, t, Skeleton, _pose, _scratch);
                GaspPoseUtil.BuildWorld(_pose, Skeleton, _local, _world);
                for (int k = 0; k < 2; k++)
                {
                    int foot = k == 0 ? Skeleton.FootL : Skeleton.FootR;
                    if (foot < 0)
                        continue;
                    if (_world[foot].TranslationVector.Y > minY + 5f)
                        continue;
                    GetNodeAt(clip, t + dt, foot, out Vector3 next);
                    GetNodeAt(clip, t - dt, foot, out Vector3 prev);
                    vx.Add(-(next.X - prev.X) * clip.SampleRate * 0.5f);
                    vz.Add(-(next.Z - prev.Z) * clip.SampleRate * 0.5f);
                }
            }
            if (vx.Count == 0)
                return Vector3.Zero;
            vx.Sort();
            vz.Sort();
            var v = new Vector3(vx[vx.Count / 2], 0, vz[vz.Count / 2]);
            if (v.Length < 20f)
                return Vector3.Zero;
            if (v.Length > 2000f)
                v = v.Normalized * 2000f;
            return v;
        }

        private void BuildFeatures(GaspClip clip, Vector3 nominalVelocity, float time, float[] outValues)
        {
            GaspPoseUtil.SampleClip(clip, time, Skeleton, _pose, _scratch);
            GaspPoseUtil.BuildWorld(_pose, Skeleton, _local, _world);
            Vector3 root = _world[Skeleton.Pelvis].TranslationVector;
            float yaw = GaspSkeleton.YawFromMatrix(_world[Skeleton.Pelvis]);

            int o = 0;
            for (int i = 0; i < GaspFeatureSchema.TrajectoryTimes.Length; i++)
            {
                float t = GaspFeatureSchema.TrajectoryTimes[i];
                // 原地剪辑：用标称速度合成轨迹；t 为负即“来路”。
                outValues[o++] = nominalVelocity.X * t;
                outValues[o++] = nominalVelocity.Z * t;
                if (MatchesFacingTime(t))
                {
                    GetRootAt(clip, time + t, out _, out float y);
                    float rel = GaspSkeleton.WrapAngle(y - yaw);
                    int f = GaspFeatureSchema.TrajectoryDim + FacingSlot(t) * 2;
                    outValues[f] = Mathf.Sin(rel);
                    outValues[f + 1] = Mathf.Cos(rel);
                }
            }

            o = GaspFeatureSchema.PoseOffset;
            outValues[o++] = nominalVelocity.X;
            outValues[o++] = nominalVelocity.Z;

            WriteFoot(clip, time, Skeleton.FootL, root, ref o, outValues);
            WriteFoot(clip, time, Skeleton.FootR, root, ref o, outValues);

            WriteRelative(_world, Skeleton.Head, root, ref o, outValues);
            WriteRelative(_world, Skeleton.HandL, root, ref o, outValues);
            WriteRelative(_world, Skeleton.HandR, root, ref o, outValues);

            GaspFeatureSchema.ApplyWeights(outValues);
        }

        private void WriteFoot(GaspClip clip, float time, int node, Vector3 root, ref int o, float[] values)
        {
            if (node < 0)
            {
                o += 5;
                return;
            }
            Vector3 p = _world[node].TranslationVector - root;
            values[o++] = p.X;
            values[o++] = p.Y;
            values[o++] = p.Z;
            float dt = clip.TimePerFrame;
            GetNodeAt(clip, time + dt, node, out Vector3 next);
            GetNodeAt(clip, time - dt, node, out Vector3 prev);
            values[o++] = (next.X - prev.X) * clip.SampleRate * 0.5f;
            values[o++] = (next.Z - prev.Z) * clip.SampleRate * 0.5f;
        }

        private static void WriteRelative(Matrix[] world, int node, Vector3 root, ref int o, float[] values)
        {
            if (node < 0)
            {
                o += 3;
                return;
            }
            Vector3 p = world[node].TranslationVector - root;
            values[o++] = p.X;
            values[o++] = p.Y;
            values[o++] = p.Z;
        }

        private void GetRootAt(GaspClip clip, float time, out Vector3 position, out float yaw)
        {
            GaspPoseUtil.SampleClip(clip, time, Skeleton, _tmpPose, _tmpScratch);
            var m = GaspPoseUtil.GetNodeWorld(_tmpPose, Skeleton, Skeleton.Pelvis, _chain);
            position = m.TranslationVector;
            yaw = GaspSkeleton.YawFromMatrix(m);
        }

        private void GetNodeAt(GaspClip clip, float time, int node, out Vector3 position)
        {
            GaspPoseUtil.SampleClip(clip, time, Skeleton, _tmpPose, _tmpScratch);
            var m = GaspPoseUtil.GetNodeWorld(_tmpPose, Skeleton, node, _chain);
            position = m.TranslationVector;
        }

        private static bool MatchesFacingTime(float time)
        {
            for (int i = 0; i < GaspFeatureSchema.FacingTimes.Length; i++)
            {
                if (Mathf.Abs(GaspFeatureSchema.FacingTimes[i] - time) < 1e-4f)
                    return true;
            }
            return false;
        }

        private static int FacingSlot(float time)
        {
            for (int i = 0; i < GaspFeatureSchema.FacingTimes.Length; i++)
            {
                if (Mathf.Abs(GaspFeatureSchema.FacingTimes[i] - time) < 1e-4f)
                    return i;
            }
            return 0;
        }

        /// <summary>
        /// 检索：暴力全库（candidateLimit &lt;= 0）或 KD 树 top-K 候选 + 同一套重排。
        /// 同一剪辑内接近“续播时间”的候选给负偏置；连续成本另给出，供上层做迟滞。
        /// </summary>
        /// <param name="candidateLimit">候选上限；&lt;= 0 表示全库暴力（与旧行为逐位一致）。</param>
        public bool Search(float[] query, int currentClip, float currentTime, float deltaTime,
            float continuationBias, Dictionary<long, float> recent, float now, byte desiredGait,
            out GaspSearchResult best, out float continuingCost, int candidateLimit = 0, float desiredYawRate = 0f)
        {
            best = new GaspSearchResult { PoseIndex = -1, Cost = float.MaxValue };
            continuingCost = float.MaxValue;
            _queryRef = query;
            if (!IsBuilt)
                return false;

            float predictedTime = currentTime + deltaTime;
            int continueFrame = -1;
            int contIndex = -1;
            if (currentClip >= 0 && currentClip < ClipFirstPose.Length && ClipPoseCount[currentClip] > 0)
            {
                var cc = Anim.Clips[currentClip];
                continueFrame = cc.FrameAt(predictedTime);
                int ci0 = ClipFirstPose[currentClip] + continueFrame;
                if (continueFrame >= 0 && ci0 >= 0 && ci0 < Poses.Count && Poses[ci0].Clip == currentClip)
                    contIndex = ci0;
            }

            int[] candidates;
            int candidateCount;
            bool useTree = candidateLimit > 0 && KdTree != null && KdTree.IsBuilt;
            if (useTree)
            {
                int k = Math.Min(candidateLimit, Poses.Count);
                if (_candIdx == null || _candIdx.Length < k)
                {
                    _candIdx = new int[k];
                    _candDist = new float[k];
                }
                candidateCount = KdTree.QueryKnn(query, k, _candIdx, _candDist);
                candidates = _candIdx;
            }
            else
            {
                candidates = _allIdx;
                candidateCount = _allIdx != null ? _allIdx.Length : 0;
            }

            bool sawContinuation = false;
            for (int ci = 0; ci < candidateCount; ci++)
            {
                int i = candidates[ci];
                var p = Poses[i];
                if (p.Clip == currentClip && p.Frame == continueFrame)
                    sawContinuation = true;
                ScorePose(i, ref best, ref continuingCost, predictedTime, continueFrame,
                    currentClip, continuationBias, recent, now, desiredGait, desiredYawRate);
            }

            // KD 候选可能不含"续播帧"：补评一次，保证迟滞用的 continuingCost 始终可用。
            if (contIndex >= 0 && !sawContinuation)
                ScorePose(contIndex, ref best, ref continuingCost, predictedTime, continueFrame,
                    currentClip, continuationBias, recent, now, desiredGait, desiredYawRate);

            LastCandidatesScored = candidateCount + (contIndex >= 0 && !sawContinuation ? 1 : 0);
            return best.Valid;
        }

        private void ScorePose(int i, ref GaspSearchResult best, ref float continuingCost,
            float predictedTime, int continueFrame, int currentClip, float continuationBias,
            Dictionary<long, float> recent, float now, byte desiredGait, float desiredYawRate)
        {
            var p = Poses[i];
            var clip = Anim.Clips[p.Clip];
            if (!clip.Loop && p.Time > clip.Length - 0.2f)
                return;

            float cost = GaspFeatureSchema.Distance(_queryRef, p.Features);
            if (desiredGait > 0 && clip.Gait > 0 && clip.Gait != desiredGait)
                cost += 2000f;

            // 静止/慢转时避开大转身剪辑（TurnIP 的姿势与站立很像，但播放起来会原地转 90°/180°）。
            if (Mathf.Abs(desiredYawRate) < 0.5f && p.Clip >= 0 && p.Clip < ClipYawDelta.Length)
            {
                float netYaw = Mathf.Abs(ClipYawDelta[p.Clip]);
                if (netYaw > 0.1f)
                    cost += 200f * Mathf.Min(netYaw / Mathf.Pi, 1f);
            }

            if (p.Clip == currentClip && continueFrame >= 0 && p.Frame == continueFrame)
                continuingCost = cost;

            if (p.Clip == currentClip)
            {
                float delta = TimeDelta(p.Time, predictedTime, clip);
                if (Mathf.Abs(delta) < 0.35f)
                    cost -= continuationBias * (1f - Mathf.Abs(delta) / 0.35f);
            }

            long key = ((long)p.Clip << 32) | (uint)p.Frame;
            if (p.Clip != currentClip && recent != null && recent.TryGetValue(key, out float stamp) && now - stamp < 0.3f)
                cost += 150f;

            if (cost < best.Cost)
            {
                best.Cost = cost;
                best.PoseIndex = i;
                best.Clip = p.Clip;
                best.Frame = p.Frame;
                best.Time = p.Time;
            }
        }

        private void SamplePelvisYaw(GaspClip clip, float t, out float yaw)
        {
            GaspPoseUtil.SampleClip(clip, t, Skeleton, _tmpPose, _tmpScratch);
            GaspPoseUtil.ComposeComponent(_tmpPose, Skeleton, _tmpLocal, _tmpWorld, true);
            yaw = GaspSkeleton.YawFromMatrix(_tmpWorld[Skeleton.Pelvis]);
        }

        /// <summary>
        /// 单剪辑落脚检测：脚踝高度接近本剪辑最低点且水平速度显著低于峰值 = 支撑（contact）；
        /// 由摆动→支撑的上升沿记一次落脚。左右脚分别检测后按时间合并。
        /// </summary>
        private void BuildFootPlants(GaspClip clip)
        {
            var times = new List<float>();
            var sides = new List<byte>();
            int n = clip.FrameCount;
            if (n >= 3 && Skeleton.FootL >= 0 && Skeleton.FootR >= 0)
            {
                var pl = new Vector3[n];
                var pr = new Vector3[n];
                for (int f = 0; f < n; f++)
                {
                    float t = f / (float)clip.SampleRate;
                    GaspPoseUtil.SampleClip(clip, t, Skeleton, _tmpPose, _tmpScratch);
                    GaspPoseUtil.ComposeComponent(_tmpPose, Skeleton, _tmpLocal, _tmpWorld, true);
                    pl[f] = _tmpWorld[Skeleton.FootL].TranslationVector;
                    pr[f] = _tmpWorld[Skeleton.FootR].TranslationVector;
                }
                CollectFootPlants(pl, clip.Loop, clip.SampleRate, 0, times, sides);
                CollectFootPlants(pr, clip.Loop, clip.SampleRate, 1, times, sides);

                // 按时间排序并合并过近的同侧抖动（< 0.12s）。
                var order = new List<int>();
                for (int i = 0; i < times.Count; i++) order.Add(i);
                order.Sort((a2, b2) => times[a2].CompareTo(times[b2]));
                var t2 = new List<float>();
                var s2 = new List<byte>();
                for (int i = 0; i < order.Count; i++)
                {
                    float tt = times[order[i]];
                    byte ss = sides[order[i]];
                    if (t2.Count > 0 && s2[s2.Count - 1] == ss && tt - t2[t2.Count - 1] < 0.12f)
                        continue;
                    t2.Add(tt);
                    s2.Add(ss);
                }
                times = t2;
                sides = s2;
            }
            ClipPlantTimes.Add(times.ToArray());
            ClipPlantSides.Add(sides.ToArray());
        }

        private static void CollectFootPlants(Vector3[] pos, bool loop, float rate, byte side,
            List<float> times, List<byte> sides)
        {
            int n = pos.Length;
            var ySpeed = new float[n];
            float maxYSpeed = 1f;
            float minY = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                int next = (i + 1) % n;
                bool edges = !loop && (i == 0 || i == n - 1);
                ySpeed[i] = edges ? 0f : Mathf.Abs(pos[next].Y - pos[prev].Y) * rate * 0.5f;
                if (ySpeed[i] > maxYSpeed) maxYSpeed = ySpeed[i];
                if (pos[i].Y < minY) minY = pos[i].Y;
            }

            // 支撑相判据：脚踝贴近本剪辑最低点，且**竖直**速度小。
            // 水平速度在跑步支撑期本来就很大（身体越过支撑脚），不能拿来判接触。
            float speedGate = Mathf.Max(maxYSpeed * 0.2f, 20f);
            // 循环剪辑：用最后一帧的支撑状态做初值，否则"全程站立"的剪辑会在 t=0 误报一次落脚。
            bool prevContact = false;
            if (loop)
                prevContact = pos[n - 1].Y <= minY + 4f && ySpeed[n - 1] <= speedGate;
            for (int i = 0; i < n; i++)
            {
                bool contact = pos[i].Y <= minY + 4f && ySpeed[i] <= speedGate;
                if (contact && !prevContact)
                {
                    times.Add(i / rate);
                    sides.Add(side);
                }
                prevContact = contact;
            }
        }

        public static float TimeDelta(float a, float b, GaspClip clip)
        {
            float d = a - b;
            if (clip.Loop)
            {
                float len = clip.Length;
                if (d > len * 0.5f) d -= len;
                else if (d < -len * 0.5f) d += len;
            }
            return d;
        }

        public GaspClip GetClip(GaspSearchResult r) => Anim.Clips[r.Clip];
    }
}
