using System;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>逐骨骼局部姿态缓冲（未做 Z-up 修正）。</summary>
    public sealed class GaspPoseBuffer
    {
        public int Count;
        public Transform[] Local;
        public bool[] Covered;

        public GaspPoseBuffer(int count)
        {
            Resize(count);
        }

        public void Resize(int count)
        {
            if (Count == count && Local != null)
                return;
            Count = count;
            Local = new Transform[count];
            Covered = new bool[count];
        }

        public void ResetFromBind(GaspSkeleton skel)
        {
            Array.Copy(skel.Bind, Local, Count);
            Array.Clear(Covered, 0, Count);
        }
    }

    public static class GaspPoseUtil
    {
        /// <summary>绑定姿态 + 剪辑通道覆盖。</summary>
        public static void SampleClip(GaspClip clip, float time, GaspSkeleton skel, GaspPoseBuffer dst, float[] scratch)
        {
            dst.ResetFromBind(skel);
            clip.Sample(time, scratch);
            for (int i = 0; i < clip.Channels.Length; i++)
            {
                var ch = clip.Channels[i];
                int node = ch.NodeIndex;
                if (node < 0)
                    continue;
                var t = dst.Local[node];
                int o = ch.Offset;
                if ((ch.Flags & GaspClip.FlagPosition) != 0)
                {
                    // 根骨平移 = 剪辑级 root motion（实测只有 Mantle 带：1m 剪辑前移 99cm）。
                    // 位移权威在 ECS/服务端（移动/翻越计划），这里再应用一次会让角色视觉上双倍位移、
                    // 并让根节点把整具骨架搬离 Actor；因此根节点保留绑定位置，只取旋转/缩放。
                    if (node != skel.Root)
                        t.Translation = new Vector3(scratch[o], scratch[o + 1], scratch[o + 2]);
                    o += 3;
                }
                if ((ch.Flags & GaspClip.FlagRotation) != 0)
                {
                    t.Orientation = new Quaternion(scratch[o], scratch[o + 1], scratch[o + 2], scratch[o + 3]);
                    o += 4;
                }
                if ((ch.Flags & GaspClip.FlagScale) != 0)
                {
                    t.Scale = new Float3(scratch[o], scratch[o + 1], scratch[o + 2]);
                }
                dst.Local[node] = t;
                dst.Covered[node] = true;
            }
        }

        /// <summary>
        /// 局部姿态 → 引擎组件空间（模型空间）。引擎的 NodesPose 是组件空间矩阵，
        /// Z-up→Y-up 修正（MetaHumanClipFrameFix）作用在组件矩阵上，所以这里也最后逐矩阵修正。
        /// </summary>
        public static void BuildWorld(GaspPoseBuffer pose, GaspSkeleton skel, Matrix[] local, Matrix[] world)
        {
            ComposeComponent(pose, skel, local, world, true);
        }

        /// <summary>
        /// 局部姿态 → 组件空间矩阵。conjugate=true 时逐矩阵做 Z-up→Y-up 修正。
        /// </summary>
        public static void ComposeComponent(GaspPoseBuffer pose, GaspSkeleton skel, Matrix[] local, Matrix[] world, bool conjugate)
        {
            for (int i = 0; i < skel.Count; i++)
            {
                var t = pose.Local[i];
                local[i] = Matrix.Transformation(t.Scale, t.Orientation, t.Translation);
            }
            for (int i = 0; i < skel.Count; i++)
            {
                int p = skel.Parents[i];
                world[i] = p >= 0 ? local[i] * world[p] : local[i];
            }
            if (conjugate)
            {
                for (int i = 0; i < skel.Count; i++)
                    world[i] = GaspSkeleton.ToModel * world[i] * GaspSkeleton.ToClip;
            }
        }

        /// <summary>只算某节点（含祖先链）的组件空间矩阵，用于轨迹采样。</summary>
        public static Matrix GetNodeWorld(GaspPoseBuffer pose, GaspSkeleton skel, int node, Matrix[] chainLocal)
        {
            int depth = 0;
            int cur = node;
            while (cur >= 0 && depth < chainLocal.Length)
            {
                var t = pose.Local[cur];
                chainLocal[depth++] = Matrix.Transformation(t.Scale, t.Orientation, t.Translation);
                cur = skel.Parents[cur];
            }
            if (depth == 0)
                return Matrix.Identity;
            Matrix world = chainLocal[depth - 1];
            for (int i = depth - 2; i >= 0; i--)
                world = chainLocal[i] * world;
            return GaspSkeleton.ToModel * world * GaspSkeleton.ToClip;
        }

        /// <summary>
        /// 多权重混合。players[i] 的 weight 归一化后逐骨混合；被任一玩家覆盖的节点才算 covered。
        /// </summary>
        public static void Blend(GaspPoseBuffer[] poses, float[] weights, int count, GaspSkeleton skel, GaspPoseBuffer dst)
        {
            dst.Resize(skel.Count);
            Array.Copy(skel.Bind, dst.Local, skel.Count);
            Array.Clear(dst.Covered, 0, skel.Count);

            for (int node = 0; node < skel.Count; node++)
            {
                float total = 0f;
                Quaternion rot = Quaternion.Identity;
                bool hasRot = false;
                Vector3 trans = Vector3.Zero;
                Vector3 scale = Vector3.Zero;
                for (int p = 0; p < count; p++)
                {
                    float w = weights[p];
                    if (w <= 0f)
                        continue;
                    var pose = poses[p];
                    if (!pose.Covered[node])
                        continue;
                    var t = pose.Local[node];
                    trans += t.Translation * w;
                    scale += new Vector3(t.Scale.X, t.Scale.Y, t.Scale.Z) * w;
                    if (!hasRot)
                    {
                        rot = t.Orientation;
                        hasRot = true;
                        total = w;
                    }
                    else
                    {
                        total += w;
                        rot = Quaternion.Slerp(rot, t.Orientation, w / Mathf.Max(total, 1e-5f));
                    }
                }
                if (!hasRot || total <= 0f)
                    continue;
                var outT = dst.Local[node];
                outT.Translation = trans / total;
                outT.Orientation = Quaternion.Normalize(rot);
                var s = scale / total;
                outT.Scale = new Float3(s.X, s.Y, s.Z);
                dst.Local[node] = outT;
                dst.Covered[node] = true;
            }
        }
    }
}
