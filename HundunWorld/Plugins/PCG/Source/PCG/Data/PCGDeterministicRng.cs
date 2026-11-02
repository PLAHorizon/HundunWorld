// // PCG 插件 - 确定性随机数生成
// // 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §3.4。

using System;
using FlaxEngine;

namespace PCG
{
    /// <summary>
    /// /// 由（全局种子、节点 id、点下标）三元组导出的 PCG 专用确定性随机数发生器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么不用顺序流。</b>增量执行只重跑“某个参数变化处的下游节点”。
    /// 而用顺序发生器时，重算一部分点得到的值与整跑**不同**——表现就是“改了一个参数，
    /// 不相干的点也跟着跳”。改成从**每个点稳定的三元组**导出流，
    /// 单个点重算的值就与整跑里**逐位相同**，
    /// 这正是增量执行能成立的原因。
    /// </para>
    /// <para>
    /// 这也是本设计把“增量执行”与“确定性随机数”绑在一起的原因：
    /// 两者不能各自独立地选。
    /// </para>
    /// <para>
    /// **刻意不**用 <c>FlaxEngine.RandomStream</c> 做逐点流：它的序列
    /// 不跨引擎版本承诺稳定，引擎一升级结果就不再可复现。
    /// 引擎那个类型只在“整图取一个起始种子”这种便利场合才用。
    /// </para>
    /// </remarks>
    [Serializable]
    public struct PCGDeterministicRng
    {
        private ulong _state;
        private int _spareIndex;
        private bool _hasSpare;
        private float _spare;

        /// <summary>为某个节点的一个点造一个发生器。</summary>
        /// <param name="globalSeed">图级种子（<see cref="PCGExecutionContext.Seed"/>）。</param>
        /// <param name="nodeId">编译后图里那个稳定的节点标识。</param>
        /// <param name="pointIndex">正在生成的点的下标。</param>
        public PCGDeterministicRng(int globalSeed, int nodeId, int pointIndex)
        {
            _state = Mix3(globalSeed, nodeId, pointIndex);
            _spareIndex = 0;
            _hasSpare = false;
            _spare = 0.0f;
        }

        /// <summary>
        /// 用两个值造一个发生器，给“没有自然点下标”的场合用
        /// （例如按节点挑一个生成变体）。
        /// </summary>
        public PCGDeterministicRng(int globalSeed, int nodeId)
        {
            _state = Mix3(globalSeed, nodeId, 0x5F3759DF);
            _spareIndex = 0;
            _hasSpare = false;
            _spare = 0.0f;
        }

        private static ulong Mix3(int globalSeed, int nodeId, int pointIndex)
        {
            // // SplitMix64 式的雪崩，好让相邻的（种子、节点、下标）三元组在流里落得很开。
            // // 少了它，相邻的点下标会得到相关的头几个输出。
            ulong x = unchecked((ulong)(uint)globalSeed);
            x = x * 0x9E3779B97F4A7C15UL + unchecked((ulong)(uint)nodeId);
            x ^= x >> 30;
            x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27;
            x = x * 0x94D049BB133111EBUL + unchecked((ulong)(uint)pointIndex);
            x ^= x >> 31;
            return x == 0 ? 0x2545F4914F6CDD1DUL : x;
        }

        private uint NextUInt()
        {
            // // 用标准乘子的 xorshift64*；周期 2^64-1，用于程序化摆位足够。
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return (uint)((_state * 0x2545F4914F6CDD1DUL) >> 32);
        }

        /// <summary>取下一个 [0,1) 里的值。</summary>
        public float NextFloat()
        {
            // // 只取 24 位，每个结果都能被 float **精确表示**，所以结果稳定。
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        /// <summary>取下一个 [min,max) 里的值。</summary>
        public float NextRange(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        /// <summary>取下一个 [min,max) 里的整数。</summary>
        public int NextRange(int min, int max)
        {
            if (max <= min)
                return min;

            return min + (int)(NextUInt() % (uint)(max - min));
        }

        /// <summary>取下一个布尔值，为真的概率由参数给出。</summary>
        public bool NextBool(float probability = 0.5f)
        {
            return NextFloat() < probability;
        }

        /// <summary>取一个均匀分布的单位方向。</summary>
        public Vector3 NextUnitVector()
        {
            // // Marsaglia 法：在单位圆盘里取一点，再提到球面上。
            float x, y, s;
            int guard = 0;
            do
            {
                x = NextRange(-1.0f, 1.0f);
                y = NextRange(-1.0f, 1.0f);
                s = x * x + y * y;
                guard++;
            }
            while (s >= 1.0f && guard < 32);

            if (s >= 1.0f)
            {
                // // 退化时的兜底：让函数保持全定义，又不影响正常路径的分布。
                x = 0.0f;
                y = 0.0f;
                s = 0.0f;
            }

            float f = 2.0f * Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - s));
            return new Vector3(2.0f * x * f * 0.5f, 2.0f * y * f * 0.5f, 1.0f - 2.0f * s);
        }

        /// <summary>取一个均匀分布的随机旋转。</summary>
        public Quaternion NextRotation()
        {
            // // Shoemake 的均匀随机四元数。
            float u1 = NextFloat();
            float u2 = NextFloat();
            float u3 = NextFloat();

            float r1 = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - u1));
            float r2 = Mathf.Sqrt(u1);
            float t1 = Mathf.TwoPi * u2;
            float t2 = Mathf.TwoPi * u3;

            return new Quaternion(r1 * Mathf.Sin(t1), r1 * Mathf.Cos(t1), r2 * Mathf.Sin(t2), r2 * Mathf.Cos(t2));
        }

        /// <summary>在给定盒子**自己的坐标系**里取一个随机点。</summary>
        public Vector3 NextPointInBox(BoundingBox box)
        {
            var min = box.Minimum;
            var max = box.Maximum;
            return new Vector3(NextRange(min.X, max.X), NextRange(min.Y, max.Y), NextRange(min.Z, max.Z));
        }

        /// <summary>取下一个标准正态样本（Box-Muller），把第二个值缓存起来。</summary>
        public float NextGaussian()
        {
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare;
            }

            float u, v, s;
            do
            {
                u = NextRange(-1.0f, 1.0f);
                v = NextRange(-1.0f, 1.0f);
                s = u * u + v * v;
            }
            while (s >= 1.0f || s == 0.0f);

            float mul = Mathf.Sqrt(-2.0f * Mathf.Log(s) / s);
            _spare = v * mul;
            _spareIndex++;
            _hasSpare = true;
            return u * mul;
        }

        /// <summary>已经抽了多少个值。测试与诊断用。</summary>
        public int ConsumedCount => _spareIndex;

        /// <summary>
        /// 造一个图级种子。只用引擎的随机数挑一个起始值；真正的
        /// 逐点流由本类型产生，结果因此可复现。
        /// </summary>
        public static int MakeGraphSeed(bool randomize)
        {
            if (!randomize)
                return 0;

            var stream = new RandomStream();
            stream.GenerateNewSeed();
            return stream.GetCurrentSeed();
        }
    }
}
