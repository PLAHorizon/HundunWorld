// PCG 插件 - 网格平面切割（布尔"差"的第一个可用子集）纯核
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.44。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// 切一刀的**分档计数**（诊断用）：三条路各走了多少个三角形，以及收下多少条线段。
    /// </summary>
    /// <remarks>
    /// 为什么要它：这一层剩下的几个问题（多刀裁完切口绕序会翻、棱柱侧面与网格相切时整次裁剪被拒、§7.5 内向盖朝向）
    /// 都怀疑同一个根——"刀口擦过一条边/顶点时，跨平面的三角形落进了两边都不算的那一档"。怀疑是靠不住的，
    /// 这一组数字才让人看见**每一档实际收了多少**，而不是靠读代码推。诊断不参与任何判定。
    /// </remarks>
    public struct PCGCutStats
    {
        /// <summary>整块在保留侧、原样搬过去的三角形数。</summary>
        public int Kept;

        /// <summary>整块在去掉侧、被丢掉的三角形数。</summary>
        public int Dropped;

        /// <summary>跨过刀口、走裁剪那条路的三角形数。</summary>
        public int Crossed;

        /// <summary>收下来当切口线段的条数（`Crossed` 里"恰好贡献两个**不重合**交点"的那些）。</summary>
        public int Segments;

        /// <summary>
        /// 这一刀的盖子是**耳切**出来的次数（扇形翻面才走耳切）。它是"耳切这条路真跑过"的唯一可见证据：
        /// 没有它，耳切只能靠"失败点往后挪了"来间接推断，而那种推断算不上断言。
        /// </summary>
        public int EarCaps;

        /// <inheritdoc />
        public override string ToString() =>
            $"kept {Kept} / dropped {Dropped} / crossed {Crossed} / segments {Segments} / ear caps {EarCaps}";
    }

    /// <summary>
    /// 用一个平面把网格切开、只留一侧，并把切口**盖上**（于是结果仍是封闭的）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是"布尔差"里能**证明**的那一小块：真正的 <c>A − B</c>（挖方孔）要判"B 的哪些面在 A 里/外"，
    /// 而那一步最容易产出一个"看着对、其实自交"的网格——本插件宁可先不做它。平面切割没有那个分类问题：
    /// 每个三角形只被一个半空间裁一次，切口是一条条线段，**且对于封闭输入这些线段必然首尾相接成环**，
    /// 环一封上就是一个合法的封闭实体。于是它能被四条判据钉住（见下），而"判不出来"的情形一律报错。
    /// </para>
    /// <para>
    /// <b>判据（这一层的"对"就是这四条）</b>：
    /// ① **水密**——结果里每条边恰好被两个三角形共享（按位置配对，所以顶点必须焊接）；
    /// ② **体积守恒**——对解析可算的形状（立方体切一半 = 一半体积）逐点对上；
    /// ③ **未触碰的三角形原样保留**——没跨平面的三角形一个顶点都不重算；
    /// ④ **判不出来就报错**——截面接不成**单个**闭合环（开网格、多处截断、非凸环）时拒绝产出，
    ///    因为那种情况下继续做出来的东西正是"看起来对但其实自交"。
    /// </para>
    /// <para>
    /// <b>顶点焊接是必须的</b>：切口上的点在相邻两个三角形里各算一次，而两条边在各自三角形里的端点顺序
    /// 相反，浮点结果可能差最后一位；不焊接的话"按位置配对边的水密性"当场就不过。所以结果一律经由
    /// 一个位置去重的累加器装配（量化到 1e-4 相对尺度）。
    /// </para>
    /// <para>
    /// 输入应当是**封闭**网格（`Box` / `Cylinder(capped)` / `Tube(capped)` 都是；`Grid` 不是，
    /// 台阶按设计带内部面）。不封闭的输入会在封盖那一步被拒——那是刻意的。
    /// </para>
    /// </remarks>
    public static class PCGMeshCut
    {
        /// <summary>
        /// 按平面切一刀。
        /// </summary>
        /// <param name="mesh">输入网格（越封闭越好；不封闭时会被拒）。</param>
        /// <param name="pointOnPlane">平面上的一点（世界或局部，取决于调用方把平面放在哪个空间）。</param>
        /// <param name="planeNormal">平面法线（不必归一，零长度会被拒）。</param>
        /// <param name="keepPositiveSide">true 保留法线**正**侧，false 保留负侧。</param>
        /// <param name="result">切完的网格（失败时是空网格）。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否切成功。</returns>
        public static bool TryCutByPlane(in PCGMesh mesh, Vector3 pointOnPlane, Vector3 planeNormal,
            bool keepPositiveSide, out PCGMesh result, out string error) =>
            TryCutByPlane(mesh, pointOnPlane, planeNormal, keepPositiveSide, out result, out error, out _);

        /// <summary>
        /// 同上一个重载，另外交回**分档计数**（诊断用；"刀口擦过边/顶点"那一档就是靠它才看得见的）。
        /// </summary>
        /// <param name="stats">三条路各走了多少个三角形、收下多少条线段。</param>
        public static bool TryCutByPlane(in PCGMesh mesh, Vector3 pointOnPlane, Vector3 planeNormal,
            bool keepPositiveSide, out PCGMesh result, out string error, out PCGCutStats stats)
        {
            result = PCGMesh.Empty;
            error = null;
            stats = default;

            if (mesh == null || mesh.VertexCount == 0 || mesh.TriangleCount == 0)
            {
                error = "The mesh has no triangles to cut.";
                return false;
            }

            float length = planeNormal.Length;

            if (length <= 1e-6f)
            {
                error = "The cutting plane has no direction (zero-length normal).";
                return false;
            }

            // 保留侧统一成"到平面的有符号距离 ≥ 0"，于是后面的裁剪只有一套分支。
            var keepNormal = keepPositiveSide ? planeNormal / length : -planeNormal / length;

            float scale = Math.Max(1.0f, PCGMeshBuilder.Bounds(mesh).Size.Length);
            float epsilon = scale * 1e-4f;

            var vertices = mesh.Vertices;
            var normals = mesh.Normals;
            var uvs = mesh.UVs;
            var triangles = mesh.Triangles;

            var accumulator = new Accumulator();
            var cutPoints = new List<Vector3>(8);

            bool anyKept = false;

            var polygon = new List<CutVertex>(4);

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int ia = triangles[t];
                int ib = triangles[t + 1];
                int ic = triangles[t + 2];

                if (ia < 0 || ib < 0 || ic < 0 ||
                    ia >= mesh.VertexCount || ib >= mesh.VertexCount || ic >= mesh.VertexCount)
                {
                    continue;
                }

                float da = Signed(vertices[ia], pointOnPlane, keepNormal);
                float db = Signed(vertices[ib], pointOnPlane, keepNormal);
                float dc = Signed(vertices[ic], pointOnPlane, keepNormal);

                // 分类用**严格**比较，让 ±ε 那条带一律走"跨平面"这条路去裁剪 + 收线段。
                //
                // 原来是"全部 ≥ -ε ⇒ 整块保留"与"全部 ≤ +ε ⇒ 整块丢掉"，两条判据在 ±ε 带里**重叠**：d≈0
                // （恰好落在刀口上）的顶点两边都满足，于是相邻上下两排三角形里，一排整块保留（一个交点都不收）、
                // 另一排收下两个**重合**的点（零长线段）——切口那一圈因此接不起来（实测：10 边的管子横切留 10 条
                // 破口边；同一根平移 3.7 则 0 条）。严格比较之后，"带内"不再算"整块保留"，而是走裁剪与收线段那条路。
                if (da > epsilon && db > epsilon && dc > epsilon)
                {
                    // 整个三角形都在保留侧：原样搬过去（判据③：一个顶点都不重算）。
                    anyKept = true;
                    stats.Kept++;
                    accumulator.Triangle(
                        accumulator.Add(vertices[ia], NormalOf(normals, ia), UvOf(uvs, ia)),
                        accumulator.Add(vertices[ib], NormalOf(normals, ib), UvOf(uvs, ib)),
                        accumulator.Add(vertices[ic], NormalOf(normals, ic), UvOf(uvs, ic)));
                    continue;
                }

                if (da < -epsilon && db < -epsilon && dc < -epsilon)
                {
                    stats.Dropped++;
                    continue;
                }

                // 跨平面：对"距离 ≥ 0"这个凸约束裁剪三角形，得到 3 或 4 个顶点的一条折线。
                // 交点就地收下来（它们就在平面上），而每块跨平面的三角形**恰好**贡献两个——那两个点
                // 就是切口线段的两端。靠"事后按距离数一遍"去认它们既啰嗦又容易在容差上出错。
                stats.Crossed++;
                var onPlane = new List<Vector3>(2);

                polygon.Clear();
                ClipEdge(polygon, onPlane, ia, ib, da, db, vertices, normals, uvs, pointOnPlane, keepNormal, epsilon);
                ClipEdge(polygon, onPlane, ib, ic, db, dc, vertices, normals, uvs, pointOnPlane, keepNormal, epsilon);
                ClipEdge(polygon, onPlane, ic, ia, dc, da, vertices, normals, uvs, pointOnPlane, keepNormal, epsilon);

                // 交点数**不止两个**是常态、不是异常：刀口正好穿过网格的一个顶点时，那个三角形的三个顶点是
                // （带内、保留侧、去掉侧）——**三条边都会"跨"**，于是收下**三个**交点，其中两个是同一个点
                // （那个顶点），第三个才是真正的穿越点。曲线在这一段是"顶点 → 穿越点"的一条**实线段**。
                // 之前只认 `onPlane.Count == 2`，这一类被整段丢掉——**实测**：棱柱裁立方体时缺的 4~14 条线段
                // 全出现在"上一刀造出来的那个顶点正好压在当前刀口上"的地方（夹具有一个顶点 d=1.19e-08、
                // 参与 6 个三角形）。所以这里改成"**去重之后恰好剩下两点就收**"——两个交点、三个交点都走这一条，
                // 零长（去重后只剩一个点）与四个以上（压住一条边那类）都不收。
                if (onPlane.Count >= 2)
                {
                    var distinct = new List<Vector3>(onPlane.Count);

                    for (int i = 0; i < onPlane.Count; i++)
                    {
                        bool seen = false;

                        // **去重的判据只能是"浮点噪声级"**——既不是分类带 `epsilon`、也不是焊接半径。这一问是
                        // "这个三角形在刀口上的相交是不是**退化成一个点**"，它的尺度应该是**坐标精度**，
                        // 而不是任何一层几何容差：
                        //  - 用 `epsilon`（`scale×1e-4`，大网格上到 0.05）会把"相距 2e-4~5e-4 的一根**发丝**"
                        //    判成同一个点而把那段线段丢掉 ⇒ 环上留下两个度 1 的端点（实测 prism-16 一对相距
                        //    **2.197e-4**、prism-32 的一对相距 **1.466e-4**，都在"某点只连着 1 条线段"上被拒）。
                        //  - 用 1e-4 也不行：prism-32/64 的发丝是**一串**（单段 <1e-4、串起来更长），逐段丢掉照样开口。
                        // 取 `scale × 1e-6`：比坐标的浮点噪声（`scale×1e-7` 量级）高一个量级、比分类带低两个量级
                        // ⇒ 只滤"真退化"，发丝一律**保留**。保留是安全的：cap 上多几条 1e-4 量级的短边，
                        // 而水密按位置配对、体积是解析对照、没跨平面的三角形一个顶点都没重算。
                        // 保留带进来的"重复边 / 悬垂短枝"由接环那一步的清洗处理（见 `TryBuildCap`）。
                        float samePoint = epsilon * 0.01f;

                        for (int k = 0; k < distinct.Count && !seen; k++)
                            if ((distinct[k] - onPlane[i]).LengthSquared <= samePoint * samePoint)
                                seen = true;

                        if (!seen)
                            distinct.Add(onPlane[i]);
                    }

                    if (distinct.Count == 2)
                    {
                        cutPoints.Add(distinct[0]);
                        cutPoints.Add(distinct[1]);
                        stats.Segments++;
                    }
                }

                if (polygon.Count < 3)
                    continue;

                anyKept = true;

                // 扇形三角化：裁剪出来的折线是凸的（半空间∩三角形），所以从首点扇开一定合法。
                int first = accumulator.Add(polygon[0].Position, polygon[0].Normal, polygon[0].Uv);

                for (int i = 1; i + 1 < polygon.Count; i++)
                {
                    accumulator.Triangle(
                        first,
                        accumulator.Add(polygon[i].Position, polygon[i].Normal, polygon[i].Uv),
                        accumulator.Add(polygon[i + 1].Position, polygon[i + 1].Normal, polygon[i + 1].Uv));
                }
            }

            if (cutPoints.Count == 0)
            {
                // 一刀没切到（全留或全丢）：没有切口要盖，直接交结果。
                result = accumulator.Build();

                if (result.TriangleCount == 0)
                    error = null;

                return true;
            }

            if (!anyKept)
            {
                result = PCGMesh.Empty;
                return true;
            }

            // 盖的朝向跟**输入自己的约定**走：有向体积为正 ⇒ 法线朝外（Box / Cylinder / 朝外的 Tube 都实测为正），
            // 为负 ⇒ 朝内（洞壁那种 `inward` Tube 实测 -11.31）。不跟的话，内向网格的盖会整片反着走，产物就是
            // **真正的绕序不自洽**（实测：被切过的洞壁"有向边未两两相反"——与"硬边共享一个法线"那种**表示**问题
            // 不是一回事）。符号是可靠信号：四个建造器逐个量过（`MeshCutTests` 里有诊断断言）。
            float orientation = SignedVolume(mesh) < 0.0f ? -1.0f : 1.0f;

            // `TryBuildCap` 的规则是"把环反过来，使环的**几何**法线 = 所传向量的相反数"，所以想让它交出 capNormal
            // 就得传 `-capNormal`；而盖的**顶点**法线要的就是 capNormal。两处都按这条写，避免符号再错一次。
            var capNormal = -keepNormal * orientation;

            // 封盖：把切口上的线段接成**单个**闭合环（判据④：接不成或不止一个环就拒绝）。
            if (!TryBuildCap(cutPoints, -capNormal, epsilon, out var cap, out string capError))
            {
                error = capError;
                result = PCGMesh.Empty;
                return false;
            }

            // 环的首尾相接：按位置取回**已焊接**的下标再连面——切口上的点是相邻三角形各算一次的，
            // 直接用新顶点会把"逐边水密"破掉。
            var loop = new int[cap.Count];

            for (int i = 0; i < cap.Count; i++)
                loop[i] = accumulator.Add(cap[i], capNormal, Vector2.Zero);

            // **盖的每一片扇形三角形都必须真的朝 capNormal**：`TryBuildCap` 的绕向判据用的是整圈的
            // **有向面积**（`Σ cross(a-centroid, b-centroid)`），而那个量对**非简单环**不成立——实测
            // （弯曲洞壁横切）：环上的点在 1e-4 焊接里被合掉之后环会**自交**，扇形三角化于是产出**方向相反**
            // 的三角形（8 片、面积 394、几何法线与它自己的顶点法线余弦 **−0.98**），而整圈的有向面积仍然
            // "看着对"，所以那一步判不出来。在这里**逐片验**：不一致就整刀拒绝——
            // **宁可不动，也不交出一个盖反了的网格**（盖反了在背面剔除下就是墙上一个洞）。
            // 封盖的三角化分两步，**第一步能成就绝不进第二步**，所以今天能过的每一档走的还是同一条路：
            // ① 扇形（从 cap[0] 连出去）——快，对凸的/星形的环一直是对的；
            // ② 扇形在某一片上翻面 ⇒ 环很可能是**非凸但简单**的，这时改走**耳切**（不要求凸）。
            // 两条都不成才拒绝。朝向判据按**相对**尺度：退化片的几何法线是浮点噪声，绝对阈值（哪怕 `epsilon²`）
            // 挡不住"比最大片小几个数量级"的碎片——实测演示里那 6 块石料（8×12×8 裁进 12 边圆柱）
            // 就是被这种碎片误拒的（面积 0.00、余弦 −0.852），而那是**引擎侧一次重烘焙**才抓出来的。
            int reversedAt = FindReversedSector(cap, capNormal);
            List<int[]> cover;

            if (reversedAt < 0)
            {
                cover = FanTriangles(cap.Count);
            }
            else if (TryEarClip(cap, capNormal, out var ears))
            {
                cover = ears;
                stats.EarCaps++;      // 计数只为让"耳切这条路跑过"变成可断言的事实，不参与任何判定
            }
            else
            {
                error = $"切开之后的盖里有**反着**的扇形三角形（第 {reversedAt} 片），耳切也没能盖住这个环——" +
                        "环多半在一点穿过两次（自交），而整圈的有向面积看不出这件事。宁可拒绝，" +
                        "不交出一个盖反了的网格。";

                result = PCGMesh.Empty;
                return false;
            }

            for (int t = 0; t < cover.Count; t++)
            {
                int[] tri = cover[t];

                // 环上有两个点**焊接成同一个位置**时，扇形与耳切都会交出一个"两个顶点重合"的退化三角形：
                // 它带一条零长边（只被 1 个三角形用），同时把隔壁那条真边顶成被 4 个三角形用——
                // 于是产物同时开口又超重，出口判据①把它拒了（实测 64 边棱柱第 56 面：
                // 零长边 (0.672,1.016,-0.742)↔同点，配对边 4 用）。这样的三角形不盖任何面积，
                // 丢掉它既补上水密也不改变覆盖；真正的面积由其余片负责。
                if (tri[0] == tri[1] || tri[1] == tri[2] || tri[0] == tri[2])
                    continue;

                accumulator.Triangle(loop[tri[0]], loop[tri[1]], loop[tri[2]]);
            }

            result = accumulator.Build();

            // **出口判据①**：这一刀成功与否不只看"环接上了"，还要看**产物真的水密**。
            // 加这一道是因为实测出现过"环在拓扑上闭合、产物却有未配对边"的情形（64 边棱柱：清洗之后环的点数
            // 对了，`EdgeReport` 却报破口）——那种产物比**拒绝**更坏：判据① 是这一层的契约，
            // 交出一个不水密的网格，正是本文件反复拒绝的那种"看着对、其实不成立"。
            // 这一道对既有的每一档都是**中性**的（那些档本来就都断言过"成功即水密"），只在新的边界上生效。
            int openEdges = CountOpenEdges(result, pointOnPlane, planeNormal, epsilon, out string openEdgeDetail);

            if (openEdges > 0)
            {
                error = $"The cut closed its boundary on the plane, but the result is not watertight " +
                        $"({openEdges} edge(s) are not shared by exactly two triangles); refusing to return it." +
                        (string.IsNullOrEmpty(openEdgeDetail) ? string.Empty : $"\n  未配对的边：{openEdgeDetail}");

                result = PCGMesh.Empty;
                return false;
            }

            return true;
        }

        /// <summary>到平面的有符号距离（以"保留侧"为正）。</summary>
        /// <summary>扇形里第一片"真反了"的下标；一片都不反返回 -1。退化片按相对尺度跳过（噪声法线判不出朝向）。</summary>
        private static int FindReversedSector(List<Vector3> cap, in Vector3 capNormal)
        {
            float fanMax = 0.0f;

            for (int i = 1; i + 1 < cap.Count; i++)
            {
                var probe = Vector3.Cross(cap[i] - cap[0], cap[i + 1] - cap[0]);

                if (probe.Length > fanMax)
                    fanMax = probe.Length;
            }

            for (int i = 1; i + 1 < cap.Count; i++)
            {
                var geometric = Vector3.Cross(cap[i] - cap[0], cap[i + 1] - cap[0]);
                float area = geometric.Length;

                if (area <= fanMax * 1e-3f)
                    continue;   // 相对最大片小三个数量级 ⇒ 碎片的法线是噪声，没有朝向可言

                if (Vector3.Dot(geometric, capNormal) / area <= -0.5f)
                    return i;
            }

            return -1;
        }

        /// <summary>扇形的三角表（下标指回环上的点）。</summary>
        private static List<int[]> FanTriangles(int count)
        {
            var list = new List<int[]>(System.Math.Max(0, count - 2));

            for (int i = 1; i + 1 < count; i++)
                list.Add(new[] { 0, i, i + 1 });

            return list;
        }

        /// <summary>
        /// 耳切封盖：把环当**简单多边形**（允许非凸）来三角化。找不到耳朵就返回 false——那才是
        /// "环在一点穿过两次"的自交情形，这时候拒绝仍然比交出一个翻面的盖好。
        /// </summary>
        /// <remarks>
        /// 只有扇形翻面之后才走这条路，所以今天能过的切口一个三角形都没变。投影到与 `capNormal` 垂直的平面基上：
        /// `u` 取任一与法线垂直的单位向量、`v = n × u`，于是 (u, v, n) 右手 ⇒ 在该基下逆时针的三角形，
        /// 几何法线沿 +n，与扇形的绕序约定一致。<paramref name="cap"/> 的点数最多几百，最坏 O(n³) 也只发生在
        /// 以前**直接拒掉**的那些切口上。
        /// </remarks>
        private static bool TryEarClip(List<Vector3> cap, in Vector3 capNormal, out List<int[]> tris)
        {
            tris = null;

            int n = cap.Count;

            if (n < 4)
                return false;      // 三个点以下扇形已经判过，走到这里没有意义

            var u = Vector3.Cross(capNormal, System.Math.Abs(capNormal.X) < 0.9f
                ? new Vector3(1.0f, 0.0f, 0.0f)
                : new Vector3(0.0f, 1.0f, 0.0f));

            float uLength = u.Length;

            if (uLength <= 1e-8f)
                return false;

            u = u / uLength;

            var v = Vector3.Cross(capNormal, u);
            var xs = new float[n];
            var ys = new float[n];

            for (int i = 0; i < n; i++)
            {
                xs[i] = Vector3.Dot(cap[i], u);
                ys[i] = Vector3.Dot(cap[i], v);
            }

            float twiceArea = 0.0f;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                twiceArea += xs[i] * ys[j] - xs[j] * ys[i];
            }

            if (System.Math.Abs(twiceArea) <= 1e-8f)
                return false;      // 整圈退化（多半是被焊接合光了）

            var order = new List<int>(n);

            for (int i = 0; i < n; i++)
                order.Add(i);

            if (twiceArea < 0.0f)
                order.Reverse();

            // 两个容差都要跟着**多边形自己的尺度**：公共顶点既不能被当成"落在别的三角形内部"（那就谁也切不动），
            // 也不能让一条几乎共线的凹角被当成凸耳朵。
            float scale = System.Math.Abs(twiceArea) / (float)n;
            float insideTolerance = scale * 1e-6f;
            float convexTolerance = scale * 1e-8f;

            tris = new List<int[]>(n - 2);

            for (int step = 0; order.Count > 3; step++)
            {
                if (step > n * n)
                {
                    tris = null;
                    return false;      // 兜不住就拒绝，而不是把时间耗下去
                }

                int m = order.Count;
                bool cut = false;

                for (int k = 0; k < m && !cut; k++)
                {
                    int ia = order[(k - 1 + m) % m];
                    int ib = order[k];
                    int ic = order[(k + 1) % m];

                    // 凸顶角才可能是耳朵（这条逆时针序下，内部在每条边的左手边）。
                    if (Edge2(xs[ia], ys[ia], xs[ib], ys[ib], xs[ic], ys[ic]) <= convexTolerance)
                        continue;

                    bool clear = true;

                    for (int t = 0; t < m && clear; t++)
                    {
                        int p = order[t];

                        if (p == ia || p == ib || p == ic)
                            continue;

                        if (InsideTriangle(xs[p], ys[p], xs[ia], ys[ia], xs[ib], ys[ib], xs[ic], ys[ic], insideTolerance))
                            clear = false;
                    }

                    if (!clear)
                        continue;

                    tris.Add(new[] { ia, ib, ic });
                    order.RemoveAt(k);
                    cut = true;
                }

                if (!cut)
                {
                    tris = null;
                    return false;      // 没有耳朵 ⇒ 环自交
                }
            }

            tris.Add(new[] { order[0], order[1], order[2] });

            if (tris.Count != n - 2)
            {
                tris = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 折线 a→b→c 的有向二倍面积（叉积的 z 分量）。同一个式子既是耳朵的凸凹判据，也是
        /// "点在边的哪一侧"的判据：`Edge2(P, A, B) = cross(B - A, P - A)`。
        /// </summary>
        private static float Edge2(float ax, float ay, float bx, float by, float cx, float cy) =>
            (bx - ax) * (cy - by) - (cx - bx) * (by - ay);

        /// <summary>点是否落在三角形内（含边界，按 `tolerance` 松一手）。</summary>
        private static bool InsideTriangle(float px, float py, float ax, float ay, float bx, float by,
                                           float cx, float cy, float tolerance)
        {
            float d1 = Edge2(px, py, ax, ay, bx, by);
            float d2 = Edge2(px, py, bx, by, cx, cy);
            float d3 = Edge2(px, py, cx, cy, ax, ay);

            bool hasNegative = d1 < -tolerance || d2 < -tolerance || d3 < -tolerance;
            bool hasPositive = d1 > tolerance || d2 > tolerance || d3 > tolerance;

            return !(hasNegative && hasPositive);
        }

        private static float Signed(in Vector3 position, in Vector3 pointOnPlane, in Vector3 keepNormal) =>
            Vector3.Dot(position - pointOnPlane, keepNormal);

        /// <summary>散度定理算有向体积；**符号**用来判输入网格的约定是法线朝外（正）还是朝内（负）。</summary>
        private static float SignedVolume(in PCGMesh mesh)
        {
            if (mesh.Triangles == null)
                return 0.0f;

            double sum = 0.0;

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                var a = mesh.Vertices[mesh.Triangles[t * 3]];
                var b = mesh.Vertices[mesh.Triangles[t * 3 + 1]];
                var c = mesh.Vertices[mesh.Triangles[t * 3 + 2]];
                sum += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }

            return (float)sum;
        }

        private static Vector3 NormalOf(Vector3[] normals, int index) =>
            normals != null && index < normals.Length ? normals[index] : Vector3.Up;

        private static Vector2 UvOf(Vector2[] uvs, int index) =>
            uvs != null && index < uvs.Length ? uvs[index] : Vector2.Zero;

        /// <summary>把一条边裁进"距离 ≥ 0"，得到的顶点按行走方向追加到折线里；与平面的交点另收一份。</summary>
        private static void ClipEdge(List<CutVertex> polygon, List<Vector3> onPlane, int from, int to,
            float dFrom, float dTo, Vector3[] vertices, Vector3[] normals, Vector2[] uvs,
            in Vector3 pointOnPlane, in Vector3 keepNormal, float epsilon)
        {
            // "在保留侧"的判据必须与整块那两条**完全一致**（严格大过 +ε）。差一点点就会出实测过的那类缺陷：
            // 整块判定说"这是跨平面、该去裁剪"，而这里的 `>= -ε` 却把刀口上的顶点算成"在保留侧"，
            // 于是裁剪出来的折线整个等于原三角形、一个交点都不收——切口那一圈就永远封不上。
            bool fromInside = dFrom > epsilon;
            bool toInside = dTo > epsilon;

            if (fromInside)
                polygon.Add(new CutVertex(vertices[from], NormalOf(normals, from), UvOf(uvs, from)));

            if (fromInside == toInside)
                return;

            // 与平面的交点：参数按距离线性插值，法线与 UV 一起插（颜色等其它流这里没有）。
            float span = dFrom - dTo;
            float t = Math.Abs(span) > 1e-9f ? dFrom / span : 0.0f;
            t = Math.Clamp(t, 0.0f, 1.0f);

            var position = Vector3.Lerp(vertices[from], vertices[to], t);
            var normal = Vector3.Lerp(NormalOf(normals, from), NormalOf(normals, to), t);
            var uv = Vector2.Lerp(UvOf(uvs, from), UvOf(uvs, to), t);

            polygon.Add(new CutVertex(position, normal, uv));
            onPlane.Add(position);
        }

        /// <summary>把切口线段接成单个闭合环，并按"切口朝外"的绕向排出顶点序。</summary>
        private static bool TryBuildCap(List<Vector3> segments, in Vector3 keepNormal, float epsilon,
            out List<Vector3> cap, out string error)
        {
            cap = null;
            error = null;

            int count = segments.Count / 2;

            if (count < 3)
            {
                error = $"The cut produced {count} segment(s) on the plane; a cap needs at least three. " +
                        "The mesh is probably not closed.";
                return false;
            }

            // 端点先**按距离焊接**（邻域查找，见 `WeldEndpoints`），再接环。
            //
            // 为什么不能拿"量化桶"当键：同一个交点会被相邻两个三角形各算一次，浮点末位可能不同——落在桶边界
            // 两侧就成了两个键，于是一个物理点被拆成好几个节点。**实测**：棱柱裁立方体时同一个点被拆成
            // **4 个度 1 的"断口"**（度数分布 `度1:4、度2:24`，四个断口打印出来的坐标一模一样），
            // 切口曲线因此断在那里、整刀被拒（8/16/32/64 边都会在第 4~15 面失败，正是"哪个面先撞上近似并列"）。
            // 测试侧的校验器早就吃过同一课（"报数随尺子变 ⇒ 毛病在尺子"），当时没把这条教训传到这里。
            var positions = new List<Vector3>(count * 2);
            var ids = new int[count * 2];

            // 焊接半径取**与累加器同一把尺子**（`Quantize` 的 1e-4，绝对）：要变的只是"按距离、不按桶"，
            // 不是把半径放大。用 `epsilon`（scale × 1e-4）会把**本来不同**的两个交点也并起来——**实测（2026-10-11
            // 复跑）**：那样洞穴那批被切过的洞壁当场破 2 块（"有向边未两两相反" + "开口 3 条"），
            // 因为大网格上 epsilon 会大到 0.05 量级、那已经不是一个"同一个点"的尺度了。
            // ⇒ 两把尺子确实要对齐，但要对齐到**小的那把**（去重的容差改成焊接半径），不是把焊接放大（见下面收线段那段）。
            WeldEndpoints(segments, count, WeldRadius, positions, ids);

            // 接环用**无向**邻接：每个点恰好两个邻居就够，**不要求线段方向连成有向环**。
            //
            // 为什么不能要求有向环：线段的方向来自"各三角形自己的走序"，而刀口穿过的那些面朝向各不相同，
            // 方向在拐角处**可能不一致**——实测（法线 ±X 切立方体）：角点 (0,-1,-1) 上两条线段都朝外，
            // 有向环因此接不起来、整刀被拒，而那个环其实完好（只是方向不齐）。环的走向由末尾的叉积判据定，
            // 所以"无向接环 + 最后定向"与"有向环"是等价的，且不会因为方向不齐而误拒。
            var neighbours = new Dictionary<int, List<int>>();

            for (int i = 0; i < count; i++)
            {
                var a = ids[i * 2];
                var b = ids[i * 2 + 1];
                Link(neighbours, a, b);
                Link(neighbours, b, a);
            }

            // **清洗一步（2026-10-11）**：接环要的是一张"每个点恰好两条边"的简单环，而逐三角形收线段会带进两种
            // 与几何无关的痕迹——**重复边**（同一段曲线被相邻两个三角形各收一次：实测 prism-64 上同一个邻居
            // 在列表里出现两次，把那个点撑成度 4）与**悬垂短枝**（一段被收重了的小边挂在环上：实测那是个
            // 只连着度 4 那个点的孤立点，长 7.39e-4）。**probe 的读数**（`Tools/_prism_pinch.py`）：把度 4 那个点
            // 拿掉之后图分成 `[280, 1]` 两块——"1 块"就是那根短枝。所以这不是"环在一点穿过两次"的**拓扑捏合**
            // （旧注释以为要拆环），而是**收重**；去重 + 剪叶子就能还原成简单环，不需要猜任何配对规则。
            // **旧的"按容差接合断口"那条负结果仍然有效**：那是拿两条**不同的**开链去焊，实测断口相距 3.6。
            var cleaned = new Dictionary<int, List<int>>();

            foreach (var pair in neighbours)
            {
                var links = new List<int>(pair.Value.Count);

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    int other = pair.Value[i];

                    if (other == pair.Key)          // 自环：退化线段，不是环上的一条边
                        continue;

                    if (!links.Contains(other))     // 重复边只算一条
                        links.Add(other);
                }

                if (links.Count > 0)
                    cleaned[pair.Key] = links;
            }

            // 剪叶子（度 ≤ 1 的点连同它的边一起丢），反复到不动点：悬垂短枝会被整条吃掉，
            // 而**真正的开链**（开网格）也会被吃光 —— 那种情况下面 `cleaned.Count == 0` 会明确拒绝。
            var pruned = 0;

            while (true)
            {
                var doomed = new List<int>();

                foreach (var pair in cleaned)
                    if (pair.Value.Count <= 1)
                        doomed.Add(pair.Key);

                if (doomed.Count == 0)
                    break;

                foreach (int dead in doomed)
                {
                    if (cleaned.TryGetValue(dead, out var links))
                    {
                        for (int i = 0; i < links.Count; i++)
                            if (cleaned.TryGetValue(links[i], out var others))
                                others.Remove(dead);
                    }

                    cleaned.Remove(dead);
                    pruned++;
                }
            }

            if (cleaned.Count == 0)
            {
                error = "The cut boundary is an open chain, not a ring (every end pruned away). " +
                        "The mesh is probably not closed; refusing to cap it.";
                return false;
            }

            // 度数直方图：切口曲线上"每个点连着几条线段"各有多少个点。**路径**（两个度1的点）与
            // "一圈加一条尾巴"（度3的点）在数字上完全不同形——把这张表写进错误信息，下一次就不用靠猜
            // （前三次尝试都是猜的：挪收线段的位置、按墙的走序定走向、把"两端都在平面上的边"当线段收 ——
            // 三条都被实测证伪，见文档 §7.6）。
            var histogram = new SortedDictionary<int, int>();

            foreach (var pair in cleaned)
            {
                histogram.TryGetValue(pair.Value.Count, out int seen);
                histogram[pair.Value.Count] = seen + 1;
            }

            foreach (var pair in cleaned)
            {
                if (pair.Value.Count != 2)
                {
                    error = "The cut boundary does not close (a point on the plane is shared by " +
                            $"{pair.Value.Count} segment(s), not 2). The mesh is probably not closed." +
                            DescribePoint(pair.Key, positions) + "；度数分布 " + DescribeHistogram(histogram) +
                            "；断口 " + DescribeOpenEnds(cleaned, positions);
                    return false;
                }
            }

            var ring = cleaned.Count;
            var start = 0;

            foreach (var key in cleaned.Keys)
            {
                start = key;
                break;
            }

            var loop = new List<Vector3>(ring);
            var current = start;
            var previous = start;   // 起点没有"上一个"，用自己当哨兵：两条邻居里随便挑一条开始走。

            for (int i = 0; i < ring; i++)
            {
                loop.Add(positions[current]);

                var list = cleaned[current];
                var following = list[0] == previous ? list[1] : list[0];

                if (following == current)
                    break;   // 自环（退化线段）——已经在上面丢掉了，这里只是防线

                previous = current;
                current = following;

                if (current == start)
                    break;
            }

            if (loop.Count != ring)
            {
                error = $"The cut boundary closed after {loop.Count} of {ring} node(s); refusing to cap it." +
                        $"（收下 {count} 条线段、去重与剪枝后剩 {ring} 个点、丢掉 {pruned} 个）";
                return false;
            }

            // 绕向：切口的外法线是 -keepNormal。扇形三角化的法线由叉积决定，所以按它的符号翻一次序。
            //
            // 试过改成"用墙自己的走序判"（每条线段都按某个三角形的走序收下来，于是"环必须与墙反向走同一条边"
            // 应该是个本地判据、不用猜全局约定）——**实测把水密性整个打破了**（立方体切一半、圆柱横切、多刀每一步
            // 全报 watertight False），而目标那条（内向洞壁的绕序）也没修好。说明 `cutPoints` 里成对线段的
            // **先后次序并不等于墙的走序**（`ClipEdge` 追加交点的次序受多边形行走与退化分支影响）。
            // 所以回到叉积判据；下次要动这里，先把"某条有向边在墙里是怎么走的"**量出来**再改。
            var centroid = Vector3.Zero;

            for (int i = 0; i < loop.Count; i++)
                centroid += loop[i];

            centroid /= loop.Count;

            var accumulated = Vector3.Zero;

            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i] - centroid;
                var b = loop[(i + 1) % loop.Count] - centroid;
                accumulated += Vector3.Cross(a, b);
            }

            if (Vector3.Dot(accumulated, keepNormal) > 0.0f)
                loop.Reverse();

            cap = loop;
            return true;
        }

        /// <summary>无向邻接表里加一条边（键是按位置量化过的）。</summary>
        /// <summary>接环时"两个端点算同一个点"的半径——与累加器焊接顶点那把尺子一致（`Quantize` 的 1e-4，绝对）。</summary>
        private const float WeldRadius = 1e-4f;

        /// <summary>邻域查表的格子边长（比焊接半径大一档，保证半径内的点必在 3×3×3 邻域里）。</summary>
        private const float WeldCell = WeldRadius * 2.0f;

        /// <summary>
        /// 把每条线段的两个端点按**距离**焊接成节点下标（3×3×3 邻域里找半径内的已有点）。
        /// </summary>
        /// <remarks>
        /// 与 <c>Accumulator.Add</c>（按量化桶焊接顶点）是两把尺子，这里是刻意的：接环要判"两条线段是否共享
        /// 一个端点"，而"共享"是**距离**意义上的——同一个交点由不同三角形算出来时浮点末位会不同，桶边界会把
        /// 一个物理点劈成两个键（实测棱柱裁立方体时劈成 4 个，切口曲线断在那里）。桶式合并只适合"我们只想要
        /// 一个近似代表"的场合，不适合"判拓扑相邻"。
        /// </remarks>
        private static void WeldEndpoints(List<Vector3> segments, int count, float epsilon,
            List<Vector3> positions, int[] ids)
        {
            var buckets = new Dictionary<(long, long, long), List<int>>();
            float cell = Math.Max(epsilon * 2.0f, 1e-6f);
            float squared = epsilon * epsilon;

            for (int i = 0; i < count * 2; i++)
            {
                var p = segments[i];
                var here = CellOf(p, cell);
                int found = -1;

                for (long dx = -1; dx <= 1 && found < 0; dx++)
                    for (long dy = -1; dy <= 1 && found < 0; dy++)
                        for (long dz = -1; dz <= 1 && found < 0; dz++)
                        {
                            if (!buckets.TryGetValue((here.Item1 + dx, here.Item2 + dy, here.Item3 + dz), out var list))
                                continue;

                            for (int k = 0; k < list.Count; k++)
                            {
                                if ((positions[list[k]] - p).LengthSquared <= squared)
                                {
                                    found = list[k];
                                    break;
                                }
                            }
                        }

                if (found < 0)
                {
                    found = positions.Count;
                    positions.Add(p);

                    if (!buckets.TryGetValue(here, out var list))
                    {
                        list = new List<int>(4);
                        buckets.Add(here, list);
                    }

                    list.Add(found);
                }

                ids[i] = found;
            }
        }

        /// <summary>
        /// 按**位置**数一遍产物里"没有被恰好两个三角形共享"的边（判据①的出口检查；0 表示水密）。
        /// </summary>
        /// <remarks>
        /// 必须按**位置**配对、不能按顶点下标：产物的顶点在硬边上被刻意拆成了两份（各自带自己的法线），
        /// 按下标数会把每条硬边都误报成未配对。焊接半径与接环那把尺子一致（<c>WeldRadius</c>）。
        /// </remarks>
        private static int CountOpenEdges(in PCGMesh mesh, in Vector3 pointOnPlane, in Vector3 planeNormal,
            float planeEpsilon, out string message)
        {
            message = null;

            if (mesh?.Triangles == null || mesh.Vertices == null || mesh.Triangles.Length == 0)
                return 0;

            var vertices = mesh.Vertices;
            var ids = new int[vertices.Length];
            var positions = new List<Vector3>(vertices.Length);

            var buckets = new Dictionary<(long, long, long), List<int>>();
            float cell = Math.Max(WeldRadius * 2.0f, 1e-6f);
            float squared = WeldRadius * WeldRadius;

            for (int i = 0; i < vertices.Length; i++)
            {
                var p = vertices[i];
                var here = CellOf(p, cell);
                int found = -1;

                for (long dx = -1; dx <= 1 && found < 0; dx++)
                    for (long dy = -1; dy <= 1 && found < 0; dy++)
                        for (long dz = -1; dz <= 1 && found < 0; dz++)
                        {
                            if (!buckets.TryGetValue((here.Item1 + dx, here.Item2 + dy, here.Item3 + dz), out var list))
                                continue;

                            for (int k = 0; k < list.Count; k++)
                            {
                                if ((positions[list[k]] - p).LengthSquared <= squared)
                                {
                                    found = list[k];
                                    break;
                                }
                            }
                        }

                if (found < 0)
                {
                    found = positions.Count;
                    positions.Add(p);

                    if (!buckets.TryGetValue(here, out var bucket))
                    {
                        bucket = new List<int>(4);
                        buckets.Add(here, bucket);
                    }

                    bucket.Add(found);
                }

                ids[i] = found;
            }

            // 分侧记账：一块三角形三个顶点**都**落在刀口平面上 ⇒ 它是**盖**上的一块，否则是**墙**上的。
            // 同一条未配对的边坏在盖上与坏在墙上，修法完全不同（2026-10-11 之前这里只报"被 N 个三角形用"，
            // 于是只能靠猜——而已经为猜错过好几轮）。
            var unit = Vector3.Normalize(planeNormal);
            var shared = new Dictionary<(int, int), (int Total, int Cap)>();

            // 同一步里顺手数两件事，因为它们决定"未配对边"该怎么修：**退化**三角形（三个顶点焊完之后有两点
            // 重合）与**重复**三角形（三个顶点集合与前面某块一模一样，绕序不算）。
            // 2026-10-11 那一步把 64 边那档定位到"坏在墙上"之后，下一步就是分清这两种重复的来源。
            var seenTriangles = new Dictionary<(int, int, int), int>();
            int degenerate = 0;
            int duplicated = 0;

            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                int i0 = mesh.Triangles[t];
                int i1 = mesh.Triangles[t + 1];
                int i2 = mesh.Triangles[t + 2];

                bool isCap = OnCutPlane(mesh.Vertices[i0], pointOnPlane, unit, planeEpsilon) &&
                             OnCutPlane(mesh.Vertices[i1], pointOnPlane, unit, planeEpsilon) &&
                             OnCutPlane(mesh.Vertices[i2], pointOnPlane, unit, planeEpsilon);

                BumpEdge(shared, ids[i0], ids[i1], isCap);
                BumpEdge(shared, ids[i1], ids[i2], isCap);
                BumpEdge(shared, ids[i2], ids[i0], isCap);

                if (ids[i0] == ids[i1] || ids[i1] == ids[i2] || ids[i0] == ids[i2])
                {
                    degenerate++;
                    continue;
                }

                // 与绕序无关的顶点集合：同一块面被交两遍时两次可能方向相同也可能相反。
                int a = ids[i0], b = ids[i1], c = ids[i2];

                if (b < a)
                {
                    int swap = a;
                    a = b;
                    b = swap;
                }

                if (c < b)
                {
                    int swap = b;
                    b = c;
                    c = swap;

                    if (b < a)
                    {
                        swap = a;
                        a = b;
                        b = swap;
                    }
                }

                var triple = (a, b, c);
                seenTriangles.TryGetValue(triple, out int times);
                seenTriangles[triple] = times + 1;

                if (times > 0)
                    duplicated++;
            }

            int open = 0;
            var detail = new List<string>();

            foreach (var pair in shared)
            {
                if (pair.Value.Total == 2)
                    continue;

                open++;

                // 把未配对边的**几何与归属**一起交出去。
                if (detail.Count < 4)
                {
                    var a = positions[pair.Key.Item1];
                    var b = positions[pair.Key.Item2];

                    detail.Add($"({a.X:F3},{a.Y:F3},{a.Z:F3})-({b.X:F3},{b.Y:F3},{b.Z:F3}) " +
                               $"长 {(b - a).Length:F5} 被 {pair.Value.Total} 个三角形用" +
                               $"（盖 {pair.Value.Cap} / 壁 {pair.Value.Total - pair.Value.Cap}）");
                }
            }

            var summary = $"退化三角形 {degenerate} 块、与前面重合的三角形 {duplicated} 块";

            message = detail.Count == 0 ? summary : string.Join("；", detail) + "｜" + summary;

            return open;
        }

        /// <summary>把一条无向边的共享计数加一（盖上/墙上各记一份）。</summary>
        private static void BumpEdge(Dictionary<(int, int), (int Total, int Cap)> shared, int a, int b, bool isCap)
        {
            var key = a < b ? (a, b) : (b, a);

            shared.TryGetValue(key, out var seen);
            shared[key] = (seen.Total + 1, seen.Cap + (isCap ? 1 : 0));
        }

        /// <summary>这个顶点离刀口平面近到算"在平面上"了吗（按分类带同一档距离）。</summary>
        private static bool OnCutPlane(in Vector3 position, in Vector3 pointOnPlane, in Vector3 unitNormal,
            float planeEpsilon)
        {
            return System.Math.Abs(Vector3.Dot(position - pointOnPlane, unitNormal)) <= planeEpsilon;
        }

        private static (long, long, long) CellOf(Vector3 position, float cell) =>
            ((long)Math.Floor(position.X / cell), (long)Math.Floor(position.Y / cell),
             (long)Math.Floor(position.Z / cell));

        private static void Link(Dictionary<int, List<int>> neighbours, int from, int to)
        {
            if (!neighbours.TryGetValue(from, out var list))
            {
                list = new List<int>(2);
                neighbours.Add(from, list);
            }

            list.Add(to);
        }

        /// <summary>把接环用的下标还原成可读坐标，写进错误信息（"哪个点不对"比"有个点不对"有用得多）。</summary>
        private static string DescribePoint(int index, List<Vector3> positions) =>
            index >= 0 && index < positions.Count ? $" point {positions[index]}:" : string.Empty;

        /// <summary>度数分布的紧凑写法（度 → 点数），写进错误信息。</summary>
        private static string DescribeHistogram(SortedDictionary<int, int> histogram)
        {
            var parts = new List<string>(histogram.Count);

            foreach (var pair in histogram)
                parts.Add($"度{pair.Key}:{pair.Value}");

            return string.Join("、", parts);
        }

        /// <summary>
        /// 把**断口**（度 1、也就是曲线的端点）的坐标列出来（最多四个）——曲线断在哪里，比"有几个端点"更有用。
        /// </summary>
        private static string DescribeOpenEnds(Dictionary<int, List<int>> neighbours, List<Vector3> positions)
        {
            var parts = new List<string>(4);

            foreach (var pair in neighbours)
            {
                if (pair.Value.Count != 1 || parts.Count >= 4)
                    continue;

                parts.Add(pair.Key >= 0 && pair.Key < positions.Count ? positions[pair.Key].ToString() : "?");
            }

            return parts.Count == 0 ? "无" : string.Join(" / ", parts);
        }

        /// <summary>把位置量化成接环用的键（与顶点焊接同一把尺子）。</summary>
        private static (long, long, long) Quantize(Vector3 value) =>
            ((long)MathF.Round(value.X * 10000.0f),
             (long)MathF.Round(value.Y * 10000.0f),
             (long)MathF.Round(value.Z * 10000.0f));

        /// <summary>裁剪折线上的一个顶点（位置 + 插值后的法线与 UV）。</summary>
        private struct CutVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 Uv;

            public CutVertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                Position = position;
                Normal = normal;
                Uv = uv;
            }
        }

        /// <summary>按位置去重地装配顶点与三角面——切口上的点必须焊接，否则"逐边水密"不会成立。</summary>
        private sealed class Accumulator
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int> _triangles = new List<int>();
            private readonly Dictionary<(long, long, long), List<int>> _buckets =
                new Dictionary<(long, long, long), List<int>>();

            /// <summary>
            /// 按**距离**焊接顶点（3×3×3 邻域 + 半径），不按量化桶。
            /// </summary>
            /// <remarks>
            /// 桶边界会把同一个物理点劈成两个顶点：两个三角形各算一次交点，浮点末位不同就可能落在桶的两侧。
            /// 实测（棱柱裁立方体）：某处聚了 **4 个相距 1e-4~1e-3 的近重合顶点**，刀口穿过时切口曲线在那里
            /// 断成四条小开链、整刀被拒（`度1:4、度2:24`，四个断口坐标打印出来一模一样）。
            /// 测试侧的校验器早就改成按距离焊了（"报数随尺子变 ⇒ 毛病在尺子"），这条教训此前没落到产出侧。
            /// </remarks>
            public int Add(Vector3 position, Vector3 normal, Vector2 uv)
            {
                var here = CellOf(position, WeldCell);
                int found = -1;

                for (long dx = -1; dx <= 1 && found < 0; dx++)
                    for (long dy = -1; dy <= 1 && found < 0; dy++)
                        for (long dz = -1; dz <= 1 && found < 0; dz++)
                        {
                            if (!_buckets.TryGetValue((here.Item1 + dx, here.Item2 + dy, here.Item3 + dz),
                                    out var list))
                                continue;

                            for (int k = 0; k < list.Count; k++)
                            {
                                if ((_vertices[list[k]] - position).LengthSquared > WeldRadius * WeldRadius)
                                    continue;

                                // **还要法线同向**：只按位置焊会把"墙"与"切口盖"在那条硬棱上并成一个顶点，
                                // 于是整条棱只带一个法线——硬边上的三角形必然与"三顶点法线均值"不同向
                                // （实测最差那块面积 1.04、余弦 0.00：几何法线与那个法线**垂直**）。
                                // 加上这一条之后硬边各自拆点、各带各的法线，而**同一面上的相邻三角形**照旧合并
                                // （它们的法线本来就同向），所以"逐边按位置配对"的水密性一点没变。
                                if (Vector3.Dot(_normals[list[k]], normal) < 0.5f)
                                    continue;

                                found = list[k];
                                break;
                            }
                        }

                if (found >= 0)
                    return found;

                int index = _vertices.Count;
                _vertices.Add(position);
                _normals.Add(normal);
                _uvs.Add(uv);

                if (!_buckets.TryGetValue(here, out var bucket))
                {
                    bucket = new List<int>(4);
                    _buckets.Add(here, bucket);
                }

                bucket.Add(index);
                return index;
            }

            public void Triangle(int a, int b, int c)
            {
                if (a == b || b == c || a == c)
                    return;

                _triangles.Add(a);
                _triangles.Add(b);
                _triangles.Add(c);
            }

            public PCGMesh Build() => new PCGMesh
            {
                Vertices = _vertices.ToArray(),
                Normals = _normals.ToArray(),
                UVs = _uvs.ToArray(),
                Triangles = _triangles.ToArray(),
            };
        }
    }
}
