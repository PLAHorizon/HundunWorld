using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// Ada 骨架的只读视图：绑定姿态、层级、常用骨骼索引。
    /// 离线剪辑的局部变换以“绑定姿态 + 通道覆盖”的方式落到这具骨架上。
    /// </summary>
    public sealed class GaspSkeleton
    {
        public int Count;
        public int[] Parents;
        public string[] Names;
        public Transform[] Bind;
        private readonly Dictionary<string, int> _byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public int Pelvis = -1;
        public int Root = -1;
        public int FootL = -1;
        public int FootR = -1;
        public int Head = -1;
        public int HandL = -1;
        public int HandR = -1;
        public int ThighL = -1;
        public int ThighR = -1;
        public int CalfL = -1;
        public int CalfR = -1;
        public int BallL = -1;
        public int BallR = -1;

        public static GaspSkeleton FromModel(SkinnedModel model)
        {
            if (model == null)
                return null;
            var nodes = model.Nodes;
            if (nodes == null || nodes.Length == 0)
                return null;
            var names = new string[nodes.Length];
            var parents = new int[nodes.Length];
            var bind = new Transform[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                names[i] = nodes[i].Name;
                parents[i] = nodes[i].ParentIndex;
                bind[i] = nodes[i].LocalTransform;
            }
            return Create(names, parents, bind);
        }

        /// <summary>用于离线测试/工具：直接按名字、父索引与绑定姿态构造骨架视图。</summary>
        public static GaspSkeleton Create(string[] names, int[] parents, Transform[] bind)
        {
            if (names == null || parents == null || bind == null || names.Length != parents.Length || names.Length != bind.Length)
                return null;
            var skel = new GaspSkeleton
            {
                Count = names.Length,
                Names = names,
                Parents = parents,
                Bind = bind,
            };
            for (int i = 0; i < names.Length; i++)
            {
                if (!string.IsNullOrEmpty(names[i]) && !skel._byName.ContainsKey(names[i]))
                    skel._byName.Add(names[i], i);
            }
            skel.Pelvis = skel.Find("pelvis");
            skel.Root = skel.Find("root");
            skel.FootL = skel.Find("foot_l");
            skel.FootR = skel.Find("foot_r");
            skel.Head = skel.Find("head");
            skel.HandL = skel.Find("hand_l");
            skel.HandR = skel.Find("hand_r");
            skel.ThighL = skel.Find("thigh_l");
            skel.ThighR = skel.Find("thigh_r");
            skel.CalfL = skel.Find("calf_l");
            skel.CalfR = skel.Find("calf_r");
            skel.BallL = skel.Find("ball_l");
            skel.BallR = skel.Find("ball_r");
            return skel;
        }

        public int Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return -1;
            return _byName.TryGetValue(name, out int index) ? index : -1;
        }

        /// <summary>把剪辑通道按名绑定到节点索引（只写 NodeIndex，未命中的保持 -1）。</summary>
        public void BindClipChannels(GaspClip clip)
        {
            for (int i = 0; i < clip.Channels.Length; i++)
                clip.Channels[i].NodeIndex = Find(clip.Channels[i].Name);
        }

        /// <summary>
        /// 剪辑局部姿态 → 绑定为底 + 通道覆盖。covered 表示该节点被剪辑覆盖（需要 GASP 接管）。
        /// </summary>
        public void BuildBindPose(Transform[] pose, bool[] covered)
        {
            Array.Copy(Bind, pose, Count);
            if (covered != null)
                Array.Clear(covered, 0, Count);
        }

        /// <summary>父节点在前的层级；节点顺序由引擎保证父索引小于子索引。</summary>
        public void BuildWorld(Matrix[] local, Matrix[] world)
        {
            for (int i = 0; i < Count; i++)
            {
                int parent = Parents[i];
                world[i] = parent >= 0 ? local[i] * world[parent] : local[i];
            }
        }

        public static readonly Matrix ToModel = Matrix.RotationX(-Mathf.PiOverTwo);
        public static readonly Matrix ToClip = Matrix.RotationX(Mathf.PiOverTwo);

        /// <summary>
        /// 与 MetaHumanClipFrameFix 完全相同的 Z-up→Y-up 逐节点局部矩阵修正。
        /// </summary>
        public static Matrix ConjugatedLocal(in Transform t)
        {
            var m = Matrix.Transformation(t.Scale, t.Orientation, t.Translation);
            return ToModel * m * ToClip;
        }

        /// <summary>模型空间前向（+Z）偏航角，单位弧度。</summary>
        public static float YawFromMatrix(Matrix m)
        {
            var q = Quaternion.RotationMatrix(m);
            var fwd = Vector3.Transform(Vector3.Forward, q);
            return Mathf.Atan2(fwd.X, fwd.Z);
        }

        public static float WrapAngle(float radians)
        {
            while (radians > Mathf.Pi)
                radians -= Mathf.TwoPi;
            while (radians < -Mathf.Pi)
                radians += Mathf.TwoPi;
            return radians;
        }
    }
}
