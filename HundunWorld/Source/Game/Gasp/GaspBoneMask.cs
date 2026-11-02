using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// 骨骼权重掩码：按骨架层级截取子树（如 spine_01 以下 = 上半身），供 BlendStack 做分层混合。
    /// 掩码按节点索引存 bool，构建一次后可复用；null 表示全骨骼。
    /// </summary>
    public static class GaspBoneMask
    {
        /// <summary>全骨骼掩码。</summary>
        public static bool[] All(GaspSkeleton skel)
        {
            var mask = new bool[skel.Count];
            for (int i = 0; i < mask.Length; i++)
                mask[i] = true;
            return mask;
        }

        /// <summary>以 <paramref name="rootBone"/>（含自身）为根的整棵子树。</summary>
        public static bool[] Subtree(GaspSkeleton skel, string rootBone)
        {
            int root = skel.Find(rootBone);
            if (root < 0)
                return null;

            var mask = new bool[skel.Count];
            var stack = new Stack<int>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                int n = stack.Pop();
                if (n < 0 || n >= skel.Count || mask[n])
                    continue;
                mask[n] = true;
                for (int i = 0; i < skel.Count; i++)
                {
                    if (skel.Parents[i] == n)
                        stack.Push(i);
                }
            }
            return mask;
        }

        /// <summary>掩码覆盖的节点数（诊断）。</summary>
        public static int Count(bool[] mask)
        {
            if (mask == null)
                return -1;
            int n = 0;
            for (int i = 0; i < mask.Length; i++)
                if (mask[i]) n++;
            return n;
        }
    }
}
