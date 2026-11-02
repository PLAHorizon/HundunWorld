using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>每帧喂给 MM 的角色运动状态。</summary>
    public struct GaspMotionInput
    {
        public Vector3 Position;
        public float Yaw;
        public float YawRate;
        public Vector3 Velocity;
        public Vector3 DesiredVelocityLocal;
        public bool HasInput;
        public bool Grounded;
        public float VerticalSpeed;
        /// <summary>期望步态族：0=不限，1=走，2=跑，3=冲刺。</summary>
        public byte Gait;
    }

    /// <summary>
    /// 查询构建：当前动画姿态取上一帧 GASP 输出，轨迹取 Actor 历史 + 速度/输入预测。
    /// 坐标系为“模型空间”（+Z 前向，厘米），因此不受 Actor 世界 yaw 影响。
    /// </summary>
    public sealed class GaspQueryBuilder
    {
        private const int HistorySize = 64;
        private const float HistorySeconds = 1.0f;

        private readonly GaspSkeleton _skel;
        private readonly float[] _times = new float[HistorySize];
        private readonly Vector3[] _positions = new Vector3[HistorySize];
        private int _historyCount;
        private int _historyHead;

        private readonly Matrix[] _world;
        private readonly Matrix[] _prevWorld;
        private readonly float _forwardYaw;
        private bool _hasPrev;
        private Vector3 _prevRoot;

        public GaspQueryBuilder(GaspSkeleton skeleton, Matrix[] initialComponentPose, float forwardYaw)
        {
            _skel = skeleton;
            _forwardYaw = forwardYaw;
            _world = new Matrix[skeleton.Count];
            _prevWorld = new Matrix[skeleton.Count];
            if (initialComponentPose != null)
                Array.Copy(initialComponentPose, _world, skeleton.Count);
            Array.Copy(_world, _prevWorld, _world.Length);
        }

        public Vector3 RootPosition => _world[_skel.Pelvis].TranslationVector;

        /// <summary>最近一次 Build 的轨迹采样（世界坐标，索引对应 <see cref="GaspFeatureSchema.TrajectoryTimes"/>），调试绘制用。</summary>
        public readonly Vector3[] LastWorldTrajectory = new Vector3[GaspFeatureSchema.TrajectoryTimes.Length];

        public void AddHistory(float time, Vector3 position)
        {
            _times[_historyHead] = time;
            _positions[_historyHead] = position;
            _historyHead = (_historyHead + 1) % HistorySize;
            if (_historyCount < HistorySize)
                _historyCount++;
        }

        /// <summary>提交上一帧最终写回骨骼的组件空间矩阵，作为本帧查询姿态。</summary>
        public void SetAppliedComponentPose(Matrix[] componentPose, float dt)
        {
            Array.Copy(_world, _prevWorld, _world.Length);
            _prevRoot = _world[_skel.Pelvis].TranslationVector;
            Array.Copy(componentPose, _world, _skel.Count);
            _hasPrev = dt > 0f;
        }

        public void Build(in GaspMotionInput input, float dt, float[] outQuery)
        {
            Array.Clear(outQuery, 0, GaspFeatureSchema.Dimension);
            Vector3 root = _world[_skel.Pelvis].TranslationVector;
            float yaw = GaspSkeleton.YawFromMatrix(_world[_skel.Pelvis]);

            // ---- 轨迹：过去来自历史，未来来自速度 + 输入预测 ----
            // Actor 世界 yaw 的“前方”是 +Z；模型空间的前方由 _forwardYaw 标定（Ada+ALS 实测为 π）。
            float modelYawOffset = -input.Yaw - _forwardYaw;
            Vector3 velLocal = RotateY(input.Velocity, modelYawOffset);
            Vector3 desired = input.DesiredVelocityLocal;
            if (!input.HasInput && desired.LengthSquared < 1e-4f)
                desired = Vector3.Zero;

            int o = 0;
            for (int i = 0; i < GaspFeatureSchema.TrajectoryTimes.Length; i++)
            {
                float t = GaspFeatureSchema.TrajectoryTimes[i];
                Vector3 local;
                if (t < 0f)
                {
                    Vector3 p = SampleHistory(input.Position, t);
                    LastWorldTrajectory[i] = p;
                    local = RotateY(p - input.Position, modelYawOffset);
                }
                else
                {
                    local = PredictFuture(velLocal, desired, t);
                    LastWorldTrajectory[i] = input.Position + RotateY(local, -modelYawOffset);
                }
                outQuery[o++] = local.X;
                outQuery[o++] = local.Z;
            }

            // 朝向：用轨迹时间里的未来采样计算预测相对偏航
            for (int i = 0; i < GaspFeatureSchema.FacingTimes.Length; i++)
            {
                float t = GaspFeatureSchema.FacingTimes[i];
                float rel = Mathf.Clamp(input.YawRate * t, -1.5f, 1.5f);
                outQuery[GaspFeatureSchema.TrajectoryDim + i * 2] = Mathf.Sin(rel);
                outQuery[GaspFeatureSchema.TrajectoryDim + i * 2 + 1] = Mathf.Cos(rel);
            }

            // ---- 当前姿态特征 ----
            if (!_hasPrev)
                Array.Copy(_world, _prevWorld, _world.Length);
            int po = GaspFeatureSchema.PoseOffset;
            float invDt = dt > 1e-5f ? 1f / dt : 0f;
            Vector3 rootVel = (_hasPrev ? (_world[_skel.Pelvis].TranslationVector - _prevRoot) * invDt : Vector3.Zero);
            outQuery[po++] = rootVel.X;
            outQuery[po++] = rootVel.Z;

            WriteFootPose(_skel.FootL, root, invDt, ref po, outQuery);
            WriteFootPose(_skel.FootR, root, invDt, ref po, outQuery);
            WriteRelativePose(_skel.Head, root, ref po, outQuery);
            WriteRelativePose(_skel.HandL, root, ref po, outQuery);
            WriteRelativePose(_skel.HandR, root, ref po, outQuery);

            GaspFeatureSchema.ApplyWeights(outQuery);
        }

        private void WriteFootPose(int node, Vector3 root, float invDt, ref int o, float[] values)
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
            Vector3 v = ( _world[node].TranslationVector - _prevWorld[node].TranslationVector) * invDt;
            values[o++] = v.X;
            values[o++] = v.Z;
        }

        private void WriteRelativePose(int node, Vector3 root, ref int o, float[] values)
        {
            if (node < 0)
            {
                o += 3;
                return;
            }
            Vector3 p = _world[node].TranslationVector - root;
            values[o++] = p.X;
            values[o++] = p.Y;
            values[o++] = p.Z;
        }

        private Vector3 SampleHistory(Vector3 fallback, float offset)
        {
            if (_historyCount == 0)
                return fallback;
            float target = _times[(_historyHead - 1 + HistorySize) % HistorySize] + offset;
            // 从最新往回找最近的两点做线性插值
            int newest = (_historyHead - 1 + HistorySize) % HistorySize;
            Vector3 prevP = _positions[newest];
            float prevT = _times[newest];
            for (int k = 1; k < _historyCount; k++)
            {
                int idx = (_historyHead - 1 - k + HistorySize * 2) % HistorySize;
                float t = _times[idx];
                Vector3 p = _positions[idx];
                if (t <= target)
                {
                    float span = prevT - t;
                    float alpha = span > 1e-5f ? (target - t) / span : 0f;
                    return Vector3.Lerp(p, prevP, alpha);
                }
                prevT = t;
                prevP = p;
            }
            return prevP;
        }

        private static Vector3 PredictFuture(Vector3 velLocal, Vector3 desired, float t)
        {
            const float step = 1f / 60f;
            Vector3 pos = Vector3.Zero;
            Vector3 vel = velLocal;
            float elapsed = 0f;
            while (elapsed < t)
            {
                float dt = Mathf.Min(step, t - elapsed);
                float k = 1f - Mathf.Exp(-dt / 0.20f);
                vel = Vector3.Lerp(vel, desired, k);
                pos += vel * dt;
                elapsed += dt;
            }
            return pos;
        }

        private static Vector3 RotateY(Vector3 v, float radians)
        {
            float c = Mathf.Cos(radians);
            float s = Mathf.Sin(radians);
            return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }
    }

    /// <summary>
    /// MM 选择器：续播偏置 + 迟滞 + 最近选择惩罚。
    /// </summary>
    public sealed class GaspMotionMatcher
    {
        public GaspMotionDatabase Database;

        /// <summary>
        /// KD 树候选上限（LOD）：&gt;0 时只对 top-K 候选打分；&lt;= 0 为全库暴力。
        /// 近处/本地玩家用较宽候选（128），远处/远端角色用较窄（32）降成本。
        /// </summary>
        public int CandidateLimit;
        public GaspSearchResult Current;
        public float CurrentTime;
        public bool HasCurrent;
        public readonly Dictionary<long, float> RecentSelections = new Dictionary<long, float>();

        public float LastCost;
        public float LastContinuingCost;
        public bool LastSwitched;
        public float LastSwitchTime = -10f;
        public int LastQueryClip = -1;

        public bool Update(float[] query, float dt, float now, float continuationBias, float switchThreshold, byte desiredGait = 0, float desiredYawRate = 0f)
        {
            if (Database == null || !Database.IsBuilt)
                return false;

            float predicted = CurrentTime + dt;
            if (HasCurrent)
            {
                var cc = Database.Anim.Clips[Current.Clip];
                if (cc.Loop)
                {
                    predicted %= cc.Length;
                    if (predicted < 0f) predicted += cc.Length;
                }
                else if (predicted > cc.Length)
                {
                    predicted = cc.Length;
                }
            }

            Database.Search(query, HasCurrent ? Current.Clip : -1, predicted, 0f,
                continuationBias, RecentSelections, now, desiredGait, out var best, out float continuingCost,
                CandidateLimit, desiredYawRate);

            LastCost = best.Cost;
            LastContinuingCost = continuingCost;
            LastSwitched = false;
            if (!best.Valid)
                return false;

            bool shouldSwitch = !HasCurrent;
            if (HasCurrent)
            {
                var curClip = Database.Anim.Clips[Current.Clip];
                float delta = GaspMotionDatabase.TimeDelta(best.Time, predicted, curClip);
                bool sameClipNear = best.Clip == Current.Clip && Mathf.Abs(delta) <= 0.15f;
                bool ended = !curClip.Loop && predicted >= curClip.Length - 0.03f;
                bool hold = continuingCost < float.MaxValue && best.Cost > continuingCost - switchThreshold;
                shouldSwitch = ended || !(sameClipNear || hold);
            }

            if (shouldSwitch)
            {
                Current = best;
                CurrentTime = best.Time;
                HasCurrent = true;
                LastSwitched = true;
                LastSwitchTime = now;
                LastQueryClip = best.Clip;
                RecentSelections[((long)best.Clip << 32) | (uint)best.Frame] = now;
                PruneRecent(now);
            }
            else
            {
                CurrentTime = predicted;
            }
            return shouldSwitch;
        }

        public void ForceSelect(int clip, float time)
        {
            Current = new GaspSearchResult
            {
                Clip = clip,
                Time = time,
                Frame = Database.Anim.Clips[clip].FrameAt(time),
                PoseIndex = Database.ClipFirstPose[clip] + Database.Anim.Clips[clip].FrameAt(time),
                Cost = 0f,
            };
            CurrentTime = time;
            HasCurrent = true;
            LastSwitched = true;
        }

        private void PruneRecent(float now)
        {
            if (RecentSelections.Count < 32)
                return;
            var stale = new List<long>();
            foreach (var kv in RecentSelections)
            {
                if (now - kv.Value > 0.3f)
                    stale.Add(kv.Key);
            }
            for (int i = 0; i < stale.Count; i++)
                RecentSelections.Remove(stale[i]);
        }
    }
}
