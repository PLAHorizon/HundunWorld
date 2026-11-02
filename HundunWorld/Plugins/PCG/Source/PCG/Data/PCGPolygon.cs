// PCG 插件 - 多边形（2D，XZ 平面）纯核
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.42。

using System;
using System.Collections.Generic;
using System.Globalization;
using FlaxEngine;

namespace PCG.Data
{
    /// <summary>
    /// XZ 平面上的一个**简单多边形**（一个环、顶点按顺序首尾相连），以及"点在不在这块地里"的判定。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 对标 UE5 PCG 的 **Polygon2D** 数据类型：插件此前只有矩形区域（<c>AreaFilter</c>），于是"这片地"必须是
    /// 轴对齐的。可写描述的人心里的形状往往不是矩形——弯的河谷、<b>L 形的院子</b>、斜着的一条地界。
    /// 这个类型把"任意的地块轮廓"变成一等数据，判定就是纯算术，所以它整条都进离屏门。
    /// </para>
    /// <para>
    /// <b>只看 X 与 Z</b>（与 <c>AreaFilter</c>、流式窗口同一个口径）：俯视图是作者表达区域的方式，
    /// 高度归地形与高度筛选器管。把 Y 混进来只会把"一块地"变成一个柱体，而描述里没人这么想。
    /// </para>
    /// <para>
    /// <b>判定用射线交叉（奇偶规则）而不是凸性/绕数</b>：奇偶规则对简单多边形永远正确、且与顶点绕向无关
    /// （顺时针、逆时针给同一个答案）。凸性判定（"所有叉积同号"）在凹多边形上会**把凹进去那部分判成外面**，
    /// 而凹多边形恰恰是这里唯一值得做的形状——矩形已经覆盖了凸的情形。绕数只在自交形状上与奇偶不同，
    /// 而自交形状本就没有"里面"的直观含义（本类型不禁止自交，但自交时的答案是奇偶规则的答案，是确定的）。
    /// </para>
    /// <para>
    /// <b>边界算"里面"</b>：正好落在边或顶点上的点算命中。这不是随手定的——"沿地块边界摆一圈东西"是很自然的
    /// 用法，而纯粹靠射线交叉的话，边界上的点会随浮点误差在里/外之间翻面，同一个多边形两次跑出不同的点集。
    /// 容差按边长缩放（叉积的量纲是面积），所以大多边形上的"贴边"与小块地一样能被认出。
    /// </para>
    /// </remarks>
    public sealed class PCGPolygon
    {
        /// <summary>顶点（<c>X</c> 是世界的 X，<c>Y</c> 是世界的 Z）。按给出的顺序排列，隐式首尾相连。</summary>
        private readonly Vector2[] _vertices;

        /// <summary>顶点的轴对齐范围，构造时算一次——判定要在每个点上问它，不能每个点都重扫顶点表。</summary>
        private readonly Vector2 _min;

        private readonly Vector2 _max;

        /// <summary>包围盒排除用的容差，与坐标量级相称（只决定"要不要继续逐边判"）。</summary>
        private readonly float _boxTolerance;

        /// <summary>顶点的只读视图。</summary>
        public IReadOnlyList<Vector2> Vertices => _vertices;

        /// <summary>顶点个数。少于 3 个就围不出一块面积。</summary>
        public int Count => _vertices?.Length ?? 0;

        /// <summary>这个环是否围出了一块面积（顶点至少 3 个）。</summary>
        public bool IsUsable => Count >= 3;

        private PCGPolygon(Vector2[] vertices)
        {
            _vertices = vertices ?? Array.Empty<Vector2>();

            if (_vertices.Length == 0)
            {
                _min = Vector2.Zero;
                _max = Vector2.Zero;
            }
            else
            {
                var min = _vertices[0];
                var max = _vertices[0];

                for (int i = 1; i < _vertices.Length; i++)
                {
                    var v = _vertices[i];

                    if (v.X < min.X) min.X = v.X;
                    if (v.Y < min.Y) min.Y = v.Y;
                    if (v.X > max.X) max.X = v.X;
                    if (v.Y > max.Y) max.Y = v.Y;
                }

                _min = min;
                _max = max;
            }

            // 关键：这个容差只用在"要不要逐边判"这一步，所以取坐标量级的千分之一——它比任何一条边的
            // 容差都宽松，因此不会把"真在边上"的点提前排除掉，而真正算在不在里的判定仍然逐边做。
            _boxTolerance = 1e-4f * Math.Max(1.0f, Math.Max(Magnitude(_min), Magnitude(_max)));
        }

        private static float Magnitude(Vector2 v) => Math.Max(Math.Abs(v.X), Math.Abs(v.Y));

        /// <summary>用一串顶点造一个多边形。</summary>
        public static PCGPolygon FromVertices(IReadOnlyList<Vector2> vertices)
        {
            if (vertices == null || vertices.Count == 0)
                return new PCGPolygon(Array.Empty<Vector2>());

            var copy = new Vector2[vertices.Count];

            for (int i = 0; i < vertices.Count; i++)
                copy[i] = vertices[i];

            return new PCGPolygon(copy);
        }

        /// <summary>
        /// 解析形如 <c>"0 0; 100 0; 100 100; 0 100"</c> 的顶点串（分号分隔顶点、顶点内是 X Z 两个数）。
        /// </summary>
        /// <param name="text">顶点串；允许换行与多余空白。</param>
        /// <param name="polygon">解析结果（失败时为<b>空多边形</b>，而不是 null——调用方少一处判空）。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <remarks>
        /// 错误信息带**第几个顶点**：一块地写错一个数，报"解析失败"等于让人把整串重读一遍。
        /// 顶点数少于 3 在这里**不**算错误——"还没写完"与"写错了"是两件事，判定那一侧按"围不出面积"处理
        /// （恒不命中），作者看到的是"什么都没长出来"，比一句来路不明的报错好查。
        /// </remarks>
        public static bool TryParse(string text, out PCGPolygon polygon, out string error)
        {
            polygon = new PCGPolygon(Array.Empty<Vector2>());
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "顶点串是空的；至少要写三个顶点，例如 \"0 0; 100 0; 100 100\"";
                return false;
            }

            var parts = text.Split(';');
            var vertices = new List<Vector2>(parts.Length);

            for (int i = 0; i < parts.Length; i++)
            {
                var piece = parts[i].Trim();

                // 末尾那个分号（或连续分号）留下的空段直接跳过：它们不是"一个顶点"，报错指不到东西。
                if (piece.Length == 0)
                    continue;

                var numbers = piece.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (numbers.Length != 2)
                {
                    error = $"第 {vertices.Count + 1} 个顶点写了 {numbers.Length} 个数（要正好两个：X 与 Z）";
                    return false;
                }

                if (!TryNumber(numbers[0], out float x) || !TryNumber(numbers[1], out float z))
                {
                    error = $"第 {vertices.Count + 1} 个顶点的 \"{piece}\" 里有不是数的东西";
                    return false;
                }

                vertices.Add(new Vector2(x, z));
            }

            polygon = new PCGPolygon(vertices.ToArray());
            return true;
        }

        /// <summary>把顶点串成一句人能读的话（给节点的 "参数描述" 用）。</summary>
        public string Describe()
        {
            if (!IsUsable)
                return $"{Count} vertex/vertices (needs at least 3)";

            return $"{Count} vertices, bounds x=[{_min.X:F1},{_max.X:F1}] " +
                   $"z=[{_min.Y:F1},{_max.Y:F1}], area {Area:F1}";
        }

        /// <summary>顶点在 X 方向的最小/最大值，以及 Z（存在 <c>Y</c> 里）的最小/最大值。构造时算好，取值不重扫。</summary>
        public (Vector2 Min, Vector2 Max) Bounds => (_min, _max);

        /// <summary>包的轴对齐范围（只看 XZ，Y 留 0）——用来快速排除掉整块地之外的点。</summary>
        public BoundingBox BoundsBox
        {
            get
            {
                var (min, max) = Bounds;
                return new BoundingBox(new Vector3(min.X, 0.0f, min.Y), new Vector3(max.X, 0.0f, max.Y));
            }
        }

        /// <summary>面积（绝对值，按鞋带公式算）。</summary>
        public float Area => Math.Abs(SignedArea);

        /// <summary>
        /// 有向面积：正负号就是顶点绕向（与坐标轴手性有关，所以只把它当"绕向的符号"用，不承诺顺/逆时针的具体含义）。
        /// </summary>
        public float SignedArea
        {
            get
            {
                if (Count < 3)
                    return 0.0f;

                float sum = 0.0f;

                for (int i = 0, j = _vertices.Length - 1; i < _vertices.Length; j = i++)
                    sum += (_vertices[j].X * _vertices[i].Y) - (_vertices[i].X * _vertices[j].Y);

                return sum * 0.5f;
            }
        }

        /// <summary>
        /// 点 <c>(x, z)</c> 在不在这块地里。边界（边与顶点）算在里面。
        /// </summary>
        /// <remarks>
        /// 少于 3 个顶点时恒 false：围不出面积的东西不命中任何点，而不是"全都命中"——
        /// 后者会让一块写坏的地把整层点放过去，从视觉上完全看不出写错了。
        /// </remarks>
        public bool Contains(float x, float z)
        {
            if (!IsUsable)
                return false;

            // 先在包围盒上排除：绝大多数点落在这一步，而它比逐边判定便宜得多。
            if (x < _min.X - _boxTolerance || x > _max.X + _boxTolerance ||
                z < _min.Y - _boxTolerance || z > _max.Y + _boxTolerance)
            {
                return false;
            }

            bool inside = false;

            for (int i = 0, j = _vertices.Length - 1; i < _vertices.Length; j = i++)
            {
                float xi = _vertices[i].X, zi = _vertices[i].Y;
                float xj = _vertices[j].X, zj = _vertices[j].Y;

                // 边界先判：射线交叉在边界上会随浮点误差翻面，而"贴边"是很自然的落点。
                if (IsOnSegment(x, z, xi, zi, xj, zj))
                    return true;

                // 奇偶规则：这条边跨过点所在的水平线时，数一次它与"向右射线"的交点。
                // 半开区间（zi > z）!= (zj > z) 让顶点恰好落在水平线上的情形只被数一次。
                if ((zi > z) != (zj > z))
                {
                    float crossX = (xj - xi) * (z - zi) / (zj - zi) + xi;

                    if (x < crossX)
                        inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>点是否落在某条线段上（含端点），带按线段长度缩放的容差。</summary>
        private static bool IsOnSegment(float x, float z, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax;
            float dz = bz - az;

            // 叉积的量纲是面积，所以容差要跟着长度走：固定阈值在大块地上会把"离边几十米"也当成贴边。
            float length = MathF.Sqrt(dx * dx + dz * dz);
            float tolerance = 1e-4f * Math.Max(1.0f, length);

            float cross = dx * (z - az) - dz * (x - ax);

            if (Math.Abs(cross) > tolerance)
                return false;

            // 叉积为零只说明共线，还要落在两端之间才算"在边上"。
            float dot = (x - ax) * dx + (z - az) * dz;

            if (dot < -tolerance || dot > length * length + tolerance)
                return false;

            return true;
        }

        /// <summary>按不变文化解析一个 float：小数点与负号不能随机器区域设置变。</summary>
        private static bool TryNumber(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
