using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// 特征空间方差分割 KD 树：把全库暴力检索收敛到 top-K 候选，再由上层按
    /// 步态惩罚/续播偏置/近期惩罚重排。
    /// <para>
    /// 参考实现用 PCA KD-tree；这里用"最大方差维度中位数分割"的经典 KD 树——
    /// 在 51 维、约千帧的库上召回质量与 PCA 等价（见 harness 对拍），构建/查询都更简单。
    /// </para>
    /// <para>距离口径与 <see cref="GaspFeatureSchema.Distance"/> 一致：**平方欧氏距离**（不做开方）。</para>
    /// </summary>
    public sealed class GaspFeatureKdTree
    {
        private const int LeafSize = 8;

        private float[][] _points;
        private int _dim;
        private int[] _index;
        private readonly IndexComparer _comparer = new IndexComparer();

        // 节点表（索引即节点 id；-1 表示无）。
        private int[] _axis;
        private float[] _split;
        private int[] _left;
        private int[] _right;
        private int[] _start;
        private int[] _count;
        private int _nodeCount;
        private int _root = -1;

        // 查询复用缓冲（单线程使用）。
        private int[] _stack;
        private int[] _heapIdx;
        private float[] _heapDist;

        public bool IsBuilt { get; private set; }

        /// <summary>最近一次查询访问的点数（诊断/LOD 证据）。</summary>
        public int LastVisited { get; private set; }

        public int PointCount => _points?.Length ?? 0;

        public void Build(IReadOnlyList<float[]> points)
        {
            if (points == null || points.Count == 0)
            {
                IsBuilt = false;
                return;
            }
            _points = new float[points.Count][];
            for (int i = 0; i < points.Count; i++)
                _points[i] = points[i];
            _dim = _points[0].Length;
            _index = new int[_points.Length];
            for (int i = 0; i < _index.Length; i++)
                _index[i] = i;

            int capacity = Math.Max(8, 4 * (_points.Length / LeafSize + 1));
            _axis = new int[capacity];
            _split = new float[capacity];
            _left = new int[capacity];
            _right = new int[capacity];
            _start = new int[capacity];
            _count = new int[capacity];
            _nodeCount = 0;

            _root = BuildNode(0, _index.Length);
            IsBuilt = _root >= 0;
        }

        private int BuildNode(int start, int count)
        {
            if (_nodeCount >= _axis.Length)
            {
                int cap = _axis.Length * 2;
                Array.Resize(ref _axis, cap);
                Array.Resize(ref _split, cap);
                Array.Resize(ref _left, cap);
                Array.Resize(ref _right, cap);
                Array.Resize(ref _start, cap);
                Array.Resize(ref _count, cap);
            }
            int node = _nodeCount++;
            _start[node] = start;
            _count[node] = count;
            _left[node] = -1;
            _right[node] = -1;
            if (count <= LeafSize)
            {
                _axis[node] = -1;
                return node;
            }

            // 选节点内方差最大的维度做分割轴。
            int axis = 0;
            float bestVar = -1f;
            for (int d = 0; d < _dim; d++)
            {
                float sum = 0f, sum2 = 0f;
                for (int i = start; i < start + count; i++)
                {
                    float v = _points[_index[i]][d];
                    sum += v;
                    sum2 += v * v;
                }
                float mean = sum / count;
                float var = sum2 / count - mean * mean;
                if (var > bestVar)
                {
                    bestVar = var;
                    axis = d;
                }
            }

            _comparer.Points = _points;
            _comparer.Dim = axis;
            Array.Sort(_index, start, count, _comparer);

            int mid = count / 2;
            _axis[node] = axis;
            _split[node] = _points[_index[start + mid]][axis];
            _left[node] = BuildNode(start, mid);
            _right[node] = BuildNode(start + mid, count - mid);
            return node;
        }

        private sealed class IndexComparer : IComparer<int>
        {
            public float[][] Points;
            public int Dim;

            public int Compare(int a, int b)
            {
                float va = Points[a][Dim];
                float vb = Points[b][Dim];
                return va < vb ? -1 : (va > vb ? 1 : 0);
            }
        }

        /// <summary>
        /// K 近邻查询（平方欧氏距离）。结果按距离升序写入 <paramref name="outIndices"/>/<paramref name="outDistances"/>。
        /// </summary>
        public int QueryKnn(float[] query, int k, int[] outIndices, float[] outDistances)
        {
            if (!IsBuilt || query == null || k <= 0)
                return 0;
            k = Math.Min(k, _points.Length);
            if (_heapIdx == null || _heapIdx.Length < k)
            {
                _heapIdx = new int[k];
                _heapDist = new float[k];
            }
            if (_stack == null || _stack.Length < 64)
                _stack = new int[64];

            int heapCount = 0;
            int visited = 0;
            int stackCount = 0;
            _stack[stackCount++] = _root;

            while (stackCount > 0)
            {
                int node = _stack[--stackCount];
                if (node < 0)
                    continue;
                int axis = _axis[node];
                if (axis < 0 || _count[node] <= LeafSize)
                {
                    for (int i = _start[node]; i < _start[node] + _count[node]; i++)
                    {
                        int pi = _index[i];
                        visited++;
                        float d2 = GaspFeatureSchema.Distance(query, _points[pi]);
                        HeapPush(ref heapCount, k, pi, d2);
                    }
                    continue;
                }

                float diff = query[axis] - _split[node];
                float planeD2 = diff * diff;
                int near = diff < 0f ? _left[node] : _right[node];
                int far = diff < 0f ? _right[node] : _left[node];

                // 先近端；远端仅在"还在候选内或可能更好"时访问。
                float worst = heapCount < k ? float.MaxValue : _heapDist[0];
                if (planeD2 < worst)
                {
                    if (stackCount + 2 > _stack.Length)
                        Array.Resize(ref _stack, _stack.Length * 2);
                    _stack[stackCount++] = far;
                }
                _stack[stackCount++] = near;
            }

            LastVisited = visited;
            // 堆按最大距离在根：导出为升序数组。
            int n = heapCount;
            for (int i = n - 1; i >= 0; i--)
            {
                outIndices[i] = _heapIdx[0];
                outDistances[i] = _heapDist[0];
                heapCount--;
                if (heapCount > 0)
                {
                    _heapIdx[0] = _heapIdx[heapCount];
                    _heapDist[0] = _heapDist[heapCount];
                    HeapSiftDown(heapCount);
                }
            }
            return n;
        }

        private void HeapPush(ref int count, int k, int pointIndex, float dist)
        {
            if (count < k)
            {
                int i = count++;
                _heapIdx[i] = pointIndex;
                _heapDist[i] = dist;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_heapDist[parent] >= _heapDist[i])
                        break;
                    Swap(i, parent);
                    i = parent;
                }
                return;
            }
            if (dist >= _heapDist[0])
                return;
            _heapIdx[0] = pointIndex;
            _heapDist[0] = dist;
            HeapSiftDown(count);
        }

        private void HeapSiftDown(int count)
        {
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, largest = i;
                if (l < count && _heapDist[l] > _heapDist[largest]) largest = l;
                if (r < count && _heapDist[r] > _heapDist[largest]) largest = r;
                if (largest == i)
                    break;
                Swap(i, largest);
                i = largest;
            }
        }

        private void Swap(int a, int b)
        {
            int ti = _heapIdx[a]; _heapIdx[a] = _heapIdx[b]; _heapIdx[b] = ti;
            float td = _heapDist[a]; _heapDist[a] = _heapDist[b]; _heapDist[b] = td;
        }
    }
}
