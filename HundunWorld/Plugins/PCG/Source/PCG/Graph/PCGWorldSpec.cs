// PCG 插件 - 设定描述（一行一条指令的文本规格）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.39。

using System;
using System.Collections.Generic;
using System.Globalization;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Graph
{
    /// <summary>区域：生成范围的水平尺寸与高度。这是一条**场景侧**的设置（由宿主设到体积上），不是图里的节点。</summary>
    /// <remarks>
    /// 是类而不是结构体：解析时按字段名反射赋值，值类型会被装箱成副本，写进去的尺寸就丢了。
    /// </remarks>
    public sealed class PCGSpecRegion
    {
        /// <summary>X 向尺寸。</summary>
        public float SizeX;

        /// <summary>Z 向尺寸。</summary>
        public float SizeZ;

        /// <summary>Y 向高度。</summary>
        public float Height;
    }

    /// <summary>一条散布层：一片林子、一堆石头或一层草。</summary>
    /// <remarks>
    /// 所有可选项都是可空的：**没写的就不写进槽位**，节点自己的默认值照旧生效。这样规格里的一行就真的
    /// 只表达它写了的东西，不必在规格里把节点默认值再抄一遍（抄一遍就会有第二个真相，改一处忘一处）。
    /// </remarks>
    public sealed class PCGSpecScatter
    {
        /// <summary>这一层的名字，只用于日志与生成出来的节点注释。</summary>
        public string Name = "scatter";

        /// <summary>点个数；为空表示按面积密度算。</summary>
        public int? Points;

        /// <summary>每块的面积密度；为空且没写 Points 时按 1.0 处理。</summary>
        public float? PointsPerBlock;

        /// <summary>点的密度，同时作为生成器的抽样概率（0.6 表示约六成的候选点会长出东西）。</summary>
        public float? Density;

        /// <summary>点尺寸倍率。</summary>
        public float? Scale;

        /// <summary>只保留坡度落在 [MinSlope, MaxSlope] 之间的点；为空表示不过滤。</summary>
        public float? MinSlope;

        /// <summary>坡度上界。</summary>
        public float? MaxSlope;

        /// <summary>
        /// 只保留地貌落在这一串里的点，形如 <c>hill,mountain</c>；为空表示不过滤。
        /// </summary>
        /// <remarks>
        /// 存原文而不在这里查表：名字表在分类器那边（`PCGBiomeKind.NameOf`），而这一层是纯解析层。
        /// 解析器只记下"写了什么"和"写在第几行"，由编译器一次性查表并报错——这样名字只有一个真相，
        /// 报错又仍能带行号。
        /// </remarks>
        public string BiomeNames;

        /// <summary>这一层写在描述的第几行（报错与提醒都指向它）。</summary>
        public int Line;

        /// <summary><see cref="BiomeNames"/> 写在第几行，供编译器报错时引用。</summary>
        public int BiomeLine;

        /// <summary>
        /// `polygon "x z; x z; …"` 的顶点串：只在这块**任意形状**的地里撒点。空表示没用这个子句。
        /// </summary>
        /// <remarks>
        /// 与 <c>within</c>/<c>outside</c>（矩形）二选一：两者都是"这片区域在哪"，同时写等于让编译器猜谁说了算。
        /// </remarks>
        public string PolygonText;

        /// <summary>
        /// `geometry &lt;形状&gt;`：这一层长的是**程序化几何**（`box` / `grid` / `cylinder` / `stairs`），
        /// 而不是一份模型资产。空表示用 `model`。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 补的是"描述只能挂资产"这一格：几何节点早就建得出程序化网格（`Geometry Primitives`），但一句描述够不着它，
        /// 于是"造出来的几何"只能靠手搓图。有了这条，描述里也能说"这里立一排石柱"，而那不需要任何模型资产。
        /// </para>
        /// <para>
        /// 与 <c>model</c> 互斥：两者都在回答"长什么"，同时写等于让编译器猜。落地形态是
        /// `… → Geometry Primitives → Spawn Geometry`（**不是**静态模型生成器——那条链只认资产）。
        /// </para>
        /// </remarks>
        public string GeometryShape;

        /// <summary>`geometry-size &lt;x&gt; &lt;y&gt; &lt;z&gt;`：尺寸，**世界单位**。写了几何就必须给。</summary>
        /// <remarks>
        /// 必填是因为节点自己的默认尺寸是"格子级"的（1000×200×1000）——一段描述漏了尺寸，得到的是一个
        /// 一公里大的盒子而不是漏写，那比报错难查得多。
        /// </remarks>
        public Vector3? GeometrySize;

        /// <summary>`geometry-segments &lt;n&gt;`：圆柱的圆周分段 / 格子的细分（按形状解释）；没写按节点默认。</summary>
        public int? GeometrySegments;

        /// <summary>`geometry-steps &lt;n&gt;`：台阶数（只有 `stairs` 用）。</summary>
        public int? GeometrySteps;

        /// <summary>
        /// `cut &lt;nx&gt; &lt;ny&gt; &lt;nz&gt; &lt;offset&gt;`：把这一层的每个形状**按平面削一刀**（只对程序化几何有意义）。
        /// </summary>
        /// <remarks>
        /// 刀口在**形状自己的坐标系**里、沿法线推 <c>offset</c> 这么远（形状都是建在自己原点的，所以
        /// <c>0 1 0 0</c> 就是"从中间横切"）。**默认切掉法线指向的那一侧**，于是"削平顶"是 <c>0 1 0 5</c>、
        /// "留下上半"是 <c>0 -1 0 0</c>（把法线反过来即可，不需要单独一个取反开关）。
        /// 切口由节点自动封盖，所以结果仍是封闭实体。
        /// </remarks>
        public Vector3? GeometryCutNormal;

        /// <summary><see cref="GeometryCutNormal"/> 那条刀口沿法线推多远。</summary>
        public float? GeometryCutOffset;

        /// <summary><see cref="GeometryCutNormal"/> 写在第几行。</summary>
        public int GeometryCutLine;

        /// <summary>`clip` 的凸区域形状（`box` / `cylinder`）；空表示没裁。</summary>
        public string ClipShape;

        /// <summary>盒：三条边长；圆柱：X 是直径、Y 是高。</summary>
        public Vector3? ClipSize;

        /// <summary>圆柱一圈多少边（棱柱近似）。</summary>
        public int? ClipSegments;

        /// <summary>`clip` 写在第几行。</summary>
        public int ClipLine;

        /// <summary>
        /// `output &lt;actors|instanced|merged&gt;`：这一层的东西**怎么写进场景**。空表示 `actors`。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 三种形态的差别只在规模与代价：`actors`（默认）一个点一个 Actor；`instanced` 写进**一个 Foliage
        /// 宿主**（实例不是 Actor，"一万根草"不会变成一万个场景对象）；`merged` 把整层**合并成一锅三角面**
        /// 只建一个 Actor（摆放烘进顶点，于是每块不能再单独变换、材质槽只剩一个）。
        /// </para>
        /// <para>
        /// 在加这个词之前，描述层**永远**是 `actors`——插件里早就有 `Spawn Foliage`（密集实例化）与
        /// `Spawn Geometry` 的合并模式，但一句描述都够不着它们，于是"一片林子/一片草地"只能是几百个 Actor。
        /// `merged` 只对**程序化几何**有意义（模型资产那半没有"合并成一锅"这回事，写了带行号报错）。
        /// </para>
        /// </remarks>
        public string OutputForm;

        /// <summary>
        /// 每个点在水平面上随机抖动的半径（世界单位）；为空表示按来源定默认值。
        /// </summary>
        /// <remarks>
        /// 按地貌撒点时点源是**地貌底图的格心**，格子是等距的——不抖的话林子会长成整齐的果园。
        /// 所以这条为空时，编译器给按地貌过滤的层补一个"半个格子"的默认抖动；写 0 表示明确不要抖动
        /// （要的就是格阵时，比如摆路灯）。
        /// </remarks>
        public float? Jitter;

        /// <summary>模型资产路径；由宿主解析成资产 id。</summary>
        public string ModelPath;

        /// <summary>实例尺寸倍率。</summary>
        public float? ModelScale;

        /// <summary>是否让每个实例随机绕自身向上轴转。</summary>
        public bool? RandomYaw;

        /// <summary>
        /// `near &lt;特征&gt; &lt;半径&gt;`：只保留距该特征 <b>半径以内</b> 的点；没写时为 null。
        /// </summary>
        /// <remarks>
        /// 这是"湖边的芦苇、路两侧的树、村口的小摊"这类说法的表达方式：在那之前，每一层都只能按**自己的**属性
        /// 筛选（坡度、密度、地貌），说不出"要挨着谁"。落地形式是取另一条链已经产出的点当参照，逐点量出最近
        /// 距离写进一列，再按那一列筛——所以参照的那条链必须**写在前面**（描述从上往下编译，一层只能用已经
        /// 建好的东西当参照），引用不到时编译期带行号报错。
        /// </remarks>
        public string NearSource;

        /// <summary><see cref="NearSource"/> 指向的半径；为空表示没写这条。</summary>
        public float? NearRadius;

        /// <summary><see cref="NearSource"/> 写在第几行，供编译器报错时引用。</summary>
        public int NearLine;

        /// <summary>`away &lt;特征&gt; &lt;半径&gt;`：只保留距该特征 <b>半径以外</b> 的点；没写时为 null。</summary>
        public string AwaySource;

        /// <summary><see cref="AwaySource"/> 指向的半径；为空表示没写这条。</summary>
        public float? AwayRadius;

        /// <summary><see cref="AwaySource"/> 写在第几行，供编译器报错时引用。</summary>
        public int AwayLine;

        /// <summary>
        /// `fade &lt;特征&gt; &lt;半径&gt;`：密度按到该特征的距离<b>线性衰减</b>（贴着它是满密度，到半径处为零）。
        /// </summary>
        /// <remarks>
        /// 与 `near` 的差别是"一刀切"与"渐变"：`near R` 把半径外的点全丢掉，`fade R` 是<b>越远越稀</b>——路两侧
        /// 的林带边缘、湖岸的芦苇、村口的散落人家说的都是后者。落地形式是量出距离后把它重映射成密度，交给
        /// 生成器按密度抽点（`density` 一直是那个全局保留概率，两者叠起来就是既按写的概率抽、又越远越稀）。
        /// </remarks>
        public string FadeSource;

        /// <summary><see cref="FadeSource"/> 指向的半径；为空表示没写这条。</summary>
        public float? FadeRadius;

        /// <summary><see cref="FadeSource"/> 写在第几行，供编译器报错时引用。</summary>
        public int FadeLine;

        /// <summary>
        /// `within &lt;xMin&gt; &lt;zMin&gt; &lt;xMax&gt; &lt;zMax&gt;`：只保留落在这片矩形里的点；没写时为 null。
        /// </summary>
        /// <remarks>
        /// 在这之前，描述只能决定"长什么"（散布层的密度、模型、坡度、地貌），决定不了"长在哪"——每一层都会
        /// 铺满整个生成范围。而"北部是针叶林、南边是草原、西南角那片湖周围种芦苇"里，**位置**恰恰是最常出现
        /// 的信息。四个数按"最小角 → 最大角"写（x 起、z 起、x 止、z 止），落地成 `Area Filter`。
        /// </remarks>
        public float? AreaMinX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 X 上界。</summary>
        public float? AreaMaxX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 下界。</summary>
        public float? AreaMinZ;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 上界。</summary>
        public float? AreaMaxZ;

        /// <summary>true 表示写的是 `outside`：只留区域**以外**的点。</summary>
        public bool AreaInvert;

        /// <summary>区域写在第几行，供编译器报错时引用。</summary>
        public int AreaLine;

        /// <summary>
        /// `except &lt;xMin&gt; &lt;zMin&gt; &lt;xMax&gt; &lt;zMax&gt;`：从这一层已经圈定的范围里**再挖掉**这块矩形。
        /// </summary>
        /// <remarks>
        /// 它和 <see cref="AreaMinX"/> 回答的不是同一个问题，所以两者同写不冲突：within 说"长在哪"，
        /// except 说"从中去掉哪"，两条筛串起来就是 A − B——"湖周围这一圈种芦苇，但码头那块地不许长"。
        /// 单独写 except 也成立，等于"整片生成范围挖掉这一块"。没写时为 null。
        /// </remarks>
        public float? ExceptMinX;

        /// <summary><see cref="ExceptMinX"/> 那块要挖掉的矩形的 X 上界。</summary>
        public float? ExceptMaxX;

        /// <summary><see cref="ExceptMinX"/> 那块要挖掉的矩形的 Z 下界。</summary>
        public float? ExceptMinZ;

        /// <summary><see cref="ExceptMinX"/> 那块要挖掉的矩形的 Z 上界。</summary>
        public float? ExceptMaxZ;

        /// <summary>`except-polygon "x z, ..."`：挖掉的形状任意，判据换成多边形，其余与矩形挖洞一致。</summary>
        public string ExceptPolygonText;

        /// <summary>挖洞写在第几行，供编译器报错时引用。</summary>
        public int ExceptLine;
    }

    /// <summary>水域：湖面与河面。参数直接对应 `Hydrology` 的水文判据。</summary>
    /// <summary>
    /// `terrain` 指令：把地形**造**出来，而不是只判地貌。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 形状表写成一条迷你语法：`shapes "dome 0 0 600 120; basin 900 400 500 80"`，每一项是
    /// `形状 X Z 半径 幅度 [边缘过渡]`。盆地与台地看边缘过渡，土丘自带平滑；叠加是**求和**
    /// （"台地上挖个盆地"是一正一负相加），与 <see cref="PCGTerrainSynthesizer"/> 的口径一致。
    /// </para>
    /// <para>
    /// 形状名用的是 `plateau` / `basin` / `dome`（台地 / 盆地 / 土丘）。"丘陵"不是第四种形状，
    /// 它是几座土丘叠在一起——多写几项即可，这正是决定 A 里"形状库尽量小、组合靠叠加"的意思。
    /// </para>
    /// </remarks>
    public sealed class PCGSpecTerrain
    {
        /// <summary>噪声幅度（世界单位）；0 表示只要形状、不要起伏。</summary>
        public float? NoiseAmplitude;

        /// <summary>噪声空间尺度（越小越平缓）。</summary>
        public float? NoiseScale;

        /// <summary>形状表原文；解析结果在 <see cref="Features"/>。</summary>
        public string Shapes;

        /// <summary>洞掩码的形状表原文（语法与 <see cref="Shapes"/> 同一条）；解析结果在 <see cref="HoleFeatures"/>。</summary>
        public string Holes;

        /// <summary>洞掩码的解析结果：落在这些形状半径内的顶点算"没有地面"。</summary>
        public List<PCGTerrainFeature> HoleFeatures = new List<PCGTerrainFeature>();

        /// <summary>形状表解析出来的特征（解析在描述层完成，写错时带行号报出来）。</summary>
        public List<PCGTerrainFeature> Features = new List<PCGTerrainFeature>();
    }

    public sealed class PCGSpecWater
    {
        /// <summary>规划格边长。</summary>
        public float? CellSize;

        /// <summary>汇流阈值：一个格子要汇集多少上游格子才算河。</summary>
        public int? RiverThreshold;

        /// <summary>小于这么多格的洼地不算湖。</summary>
        public int? MinLakeCells;

        /// <summary>
        /// 区域约束：水系只支持 <c>within</c>（"河只准从这块地里发源、湖只准在这块地里成心"）。
        /// </summary>
        /// <remarks>
        /// 与散布/聚落/城市那三层的同名选项**不是一条链**：那三层筛的是互相独立的实例点，而河与湖是连续体，
        /// 事后删一格就断成两截。所以这里把它做成**产生器的输入**（见 <c>PCGHydrology.allowOrigin</c>）。
        /// <c>outside</c> 也接：约束筛的是**发源格**，"这块地不许发源"与"只准在这块发源"是同一判据的取反。
        /// （早先这里写着"outside 表达不了"，那是把"不能从中间截断已成形的河"错当成"不能排除一块地"。）
        /// <c>except</c> 仍不接：它需要第二块矩形，这里只有一块。
        /// </remarks>
        public float? AreaMinX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 X 上界。</summary>
        public float? AreaMaxX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 下界。</summary>
        public float? AreaMinZ;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 上界。</summary>
        public float? AreaMaxZ;

        /// <summary>true 表示写的是 <c>outside</c>：这块矩形不许发源（河与湖都从别处起）。</summary>
        public bool AreaInvert;

        /// <summary>水位偏移。</summary>
        public float? WaterOffset;

        /// <summary>河湖水面用的模型路径。</summary>
        public string ModelPath;

        /// <summary>实例尺寸倍率。</summary>
        public float? ModelScale;
    }

    /// <summary>聚居地：用切分语法把地块切成房子。</summary>
    public sealed class PCGSpecSettlement
    {
        /// <summary>
        /// `output merged` / `output instanced`：这个聚居地的产出不再一栋一个 Actor。
        /// 与城市同一条判据：只对程序化几何成立，挂模型时编译期报错。
        /// </summary>
        public string OutputForm;

        /// <summary>
        /// 区域约束：与散布层同一条含义、同一套筛子——<c>within</c>/<c>outside</c> 说"聚落只准在这块地里
        /// （或之外）"，<c>except</c>/<c>except-polygon</c> 再从那块地里**挖掉**一片（不许占的湖面、耕地、林地）。
        /// </summary>
        /// <remarks>
        /// 落地点之前只有 <see cref="AtX"/> 一个自由度：村/城会照样把地块铺过湖泊与河面，
        /// 而"城建在湖上"是程序化聚落最经典的一种坏相。这里没有新机制——就是散布层那条链搬到聚落上。
        /// </remarks>
        public float? AreaMinX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 X 上界。</summary>
        public float? AreaMaxX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 下界。</summary>
        public float? AreaMinZ;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 上界。</summary>
        public float? AreaMaxZ;

        /// <summary>true 表示写的是 `outside`：只留区域**以外**的地块。</summary>
        public bool AreaInvert;

        /// <summary>区域写在第几行，供编译器报错时引用。</summary>
        public int AreaLine;

        /// <summary>`polygon "x z; ..."`：圈地的形状任意（弯的湖岸、斜的山脚），与矩形同一条链。</summary>
        public string PolygonText;

        /// <summary>`except "xMin zMin xMax zMax"`：从已圈定的范围里再挖掉这块矩形（A − B）。</summary>
        public float? ExceptMinX;

        /// <summary><see cref="ExceptMinX"/> 那块要挖掉的矩形的 X 上界。</summary>
        public float? ExceptMaxX;

        /// <summary><see cref="ExceptMinX"/> 那块要挖掉的矩形的 Z 下界。</summary>
        public float? ExceptMinZ;

        /// <summary><see cref="ExceptMinX"/> 那块要挖掉的矩形的 Z 上界。</summary>
        public float? ExceptMaxZ;

        /// <summary>`except-polygon "x z; ..."`：挖掉的是任意形状。</summary>
        public string ExceptPolygonText;

        /// <summary>挖洞写在第几行，供编译器报错时引用。</summary>
        public int ExceptLine;
        /// <summary>地块个数（由上游的点数决定，这是上限提示）。</summary>
        public int? Lots;

        /// <summary>切分规则表。</summary>
        public string Rules;

        /// <summary>最大切分深度。</summary>
        public int? MaxDepth;

        /// <summary>一个格子最小多小就不再切。</summary>
        public float? MinCellSize;

        /// <summary>起始地块尺寸（三个数：X Y Z）。</summary>
        public Vector3? CellSize;

        /// <summary>房屋模型路径。</summary>
        public string ModelPath;

        /// <summary>程序化几何的形状名（与 <c>model</c> 二选一；两个都写时几何赢，并被点名一句）。</summary>
        public string GeometryShape;

        /// <summary><c>geometry-size</c>：世界单位的 开间 × 总高（含屋顶） × 进深。</summary>
        public Vector3? GeometrySize;

        /// <summary><c>geometry-segments</c>：绕数/分段（只对圆柱那一类形状有意义）。</summary>
        public int? GeometrySegments;

        /// <summary>实例尺寸倍率。</summary>
        public float? ModelScale;

        /// <summary>是否让每栋房子随机转向。</summary>
        public bool? RandomYaw;

        /// <summary>`at &lt;x&gt; &lt;z&gt;`：把这个村子摆在写下的位置（Y 取 0，与 `road` 两端点同一约定）；没写时为 null。</summary>
        public float? AtX;

        /// <summary><see cref="AtX"/> 的 Z。</summary>
        public float? AtZ;

        /// <summary><see cref="AtX"/> 写在第几行，供编译器报错时引用。</summary>
        public int AtLine;

        /// <summary>`rotate &lt;度&gt;`：整块地绕种子点转这个角度（度）；没写时为 null。</summary>
        public float? RotateDegrees;

        /// <summary><see cref="RotateDegrees"/> 写在第几行，供编译器报错时引用。</summary>
        public int RotateLine;
    }

    /// <summary>一条路：从哪到哪（途经点可选），以及走向的约束与路面模型。</summary>
    /// <remarks>
    /// 两端点是**写死的坐标**（`from` / `to` 各两个数：X 与 Z，Y 取 0），因为"从这里修到那里"正是这条路的意义；
    /// 高度由路径规划器按地形决定，写 Y 没有意义。`via` 可以重复，用来给出**必须经过**的中间点——山路绕开一块
    /// 岩体、小道拐过一片林子，都靠它表达。落地形式是 `Point List → Path → Spawn Static Model`：途经点就是字面点
    /// 节点的那几个槽位，规划器再把它们连成一条低代价的路。
    /// </remarks>
    public sealed class PCGSpecRoad
    {
        /// <summary>起点的 X（必填）。</summary>
        public float? FromX;

        /// <summary>起点的 Z（必填）。</summary>
        public float? FromZ;

        /// <summary>终点的 X（必填）。</summary>
        public float? ToX;

        /// <summary>终点的 Z（必填）。</summary>
        public float? ToZ;

        /// <summary>
        /// 区域约束：路支持 <c>within</c>（只准走这块地）与 <c>outside</c>（绕开这块地），两者互斥
        /// ——它们写的是同一块矩形，同写等于让编译器猜谁说了算（解析器本来就拦）。
        /// </summary>
        /// <remarks>
        /// 路能表达"绕开"，而洞穴/水系不能：路是**走法**，代价搜索天然会绕不可通行的格子；
        /// 洞道与河是截面序列，删一格就断，所以那边只能截断。同一句区域在两侧是两种实现。
        /// 只有一块矩形：要 A − B（只在 A 里但不许经过 B）得再加一组槽位，未做。
        /// </remarks>
        public float? AreaMinX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 X 上界。</summary>
        public float? AreaMaxX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 下界。</summary>
        public float? AreaMinZ;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 上界。</summary>
        public float? AreaMaxZ;

        /// <summary>true 表示写的是 <c>outside</c>：这块矩形不许经过，路绕开它。</summary>
        public bool AreaInvert;

        /// <summary>中途必须经过的点，按写法顺序；X 与 Z 各一个数。</summary>
        public readonly List<Vector2> Vias = new List<Vector2>();

        /// <summary>途经点个数上限：字面点节点一共八个槽位，两端各占一个，中间最多六个。</summary>
        public const int MaxVias = 6;

        /// <summary>
        /// 按"起点 → 各途经点 → 终点"的顺序给出全部途经点。
        /// </summary>
        /// <remarks>
        /// 起点或终点缺失时给出一份**不含它**的清单而不是抛异常：这条方法会被编译器和断言两边调用，而"缺端点"
        /// 的报错由解析器负责，那里有行号可以说。
        /// </remarks>
        public List<Vector2> Waypoints()
        {
            var points = new List<Vector2>(Vias.Count + 2);

            if (FromX.HasValue && FromZ.HasValue)
                points.Add(new Vector2(FromX.Value, FromZ.Value));

            points.AddRange(Vias);

            if (ToX.HasValue && ToZ.HasValue)
                points.Add(new Vector2(ToX.Value, ToZ.Value));

            return points;
        }

        /// <summary>规划格边长。</summary>
        public float? CellSize;

        /// <summary>可通行的最大坡度（度）。</summary>
        public float? SlopeLimit;

        /// <summary>最多偏离直线多远。</summary>
        public float? MaxDetour;

        /// <summary>沿路点的间距。</summary>
        public float? SampleSpacing;

        /// <summary>路面模型路径。</summary>
        public string ModelPath;

        /// <summary>实例尺寸倍率。</summary>
        public float? ModelScale;

        /// <summary>是否让每个实例随机转向。</summary>
        public bool? RandomYaw;
        /// <summary>`geometry <形状>`：路面用**描述自己建出来的水平网格**，而不是借引擎的 Plane 图元。</summary>
        /// <remarks>
        /// 存在的理由是一条实测：Flax 的 `Plane` 是**竖直面片**，而路链只按行进方向给 yaw，
        /// 于是 `road … model "Plane"` 在视口里是一堵 100 单位高的墙（见 memory 里的竖墙记录）。
        /// 路面本来就该由描述建成 XZ 铺开的实体（`grid` 那一档），换图元也不该再赌方向。
        /// </remarks>
        public string GeometryShape;

        /// <summary>路面板块的尺寸（世界单位）：`geometry-size 宽 高 长`。</summary>
        public Vector3? GeometrySize;

        /// <summary>`grid` 那一档每边切几段。</summary>
        public int? GeometrySegments;

    }

    /// <summary>
    /// 一座城市：用**符号与产生式**逐层把一个地块改写成街区、再改写成房子。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="PCGSpecSettlement"/> 的分工是"平铺"与"层级"：聚居地是一张**按深度索引的切分规则表**
    /// （一次切成地块，地块之间没有种类之别），而城市是一条**产生式表**——`城区 = 街区,街区`、`街区 = 房子,房子,房子`——
    /// 于是不同的地块可以长成不同的东西，层数也不受规则条数限制。落地形式是 `Grammar → Spawn Static Model`。
    /// </remarks>
    public sealed class PCGSpecCity
    {
        /// <summary>
        /// 区域约束：字段与 <see cref="PCGSpecSettlement"/> 同名，解析与生成共用同一段代码
        /// （`within` / `outside` / `except` / `polygon` / `except-polygon`，含义也与散布层那一条一致）。
        /// </summary>
        public string OutputForm;

        public float? AreaMinX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 X 上界。</summary>
        public float? AreaMaxX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 下界。</summary>
        public float? AreaMinZ;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 上界。</summary>
        public float? AreaMaxZ;

        /// <summary>true 表示写的是 `outside`：只留区域**以外**的街区。</summary>
        public bool AreaInvert;

        /// <summary>区域写在第几行，供编译器报错时引用。</summary>
        public int AreaLine;

        /// <summary>`polygon "x z; ..."`：圈地的形状任意。</summary>
        public string PolygonText;

        /// <summary>`except "xMin zMin xMax zMax"`：从已圈定的范围里再挖掉这块矩形（A − B）。</summary>
        public float? ExceptMinX;

        /// <summary><see cref="ExceptMinX"/> 那块要挖掉的矩形的 X 上界。</summary>
        public float? ExceptMaxX;

        /// <summary><see cref="ExceptMinX"/> 那块要挖掉的矩形的 Z 下界。</summary>
        public float? ExceptMinZ;

        /// <summary><see cref="ExceptMinX"/> 那块要挖掉的矩形的 Z 上界。</summary>
        public float? ExceptMaxZ;

        /// <summary>`except-polygon "x z; ..."`：挖掉的是任意形状。</summary>
        public string ExceptPolygonText;

        /// <summary>挖洞写在第几行，供编译器报错时引用。</summary>
        public int ExceptLine;
        /// <summary>产生式表，格式与 `Grammar` 节点一致：`A = B,C: X:0.5,0.5; …`（必填）。</summary>
        public string Productions;

        /// <summary>每个种子起始的符号；为空时用节点自己的默认根。</summary>
        public string Root;

        /// <summary>最多改写几层。</summary>
        public int? MaxDepth;

        /// <summary>一块格子小于这个尺寸就不再改写；0 表示不设这道检查。</summary>
        public float? MinCellSize;

        /// <summary>起始地块尺寸（三个数：X Y Z）。</summary>
        public Vector3? CellSize;

        /// <summary>房屋模型路径。</summary>
        public string ModelPath;

        /// <summary>程序化几何的形状名（与 <c>model</c> 二选一；两个都写时几何赢，并被点名一句）。</summary>
        public string GeometryShape;

        /// <summary><c>geometry-size</c>：世界单位的 开间 × 总高（含屋顶） × 进深。</summary>
        public Vector3? GeometrySize;

        /// <summary><c>geometry-segments</c>：绕数/分段（只对圆柱那一类形状有意义）。</summary>
        public int? GeometrySegments;

        /// <summary>实例尺寸倍率。</summary>
        public float? ModelScale;

        /// <summary>是否让每个实例随机转向。</summary>
        public bool? RandomYaw;

        /// <summary>
        /// `geometry-map "符号=形状 宽 高 深; …"`：**按叶子符号分派形状**——一层里不同种类的叶子
        /// 可以长成不同的实体（房子是房、空地是草坪），于是一整座城市不必再借同一个图元。
        /// </summary>
        public string SymbolShapes;

        /// <summary>`at &lt;x&gt; &lt;z&gt;`：把这座城市摆在写下的位置（Y 取 0）；没写时为 null。</summary>
        public float? AtX;

        /// <summary><see cref="AtX"/> 的 Z。</summary>
        public float? AtZ;

        /// <summary><see cref="AtX"/> 写在第几行，供编译器报错时引用。</summary>
        public int AtLine;

        /// <summary>`rotate &lt;度&gt;`：整座城绕种子点转这个角度（度）；没写时为 null。</summary>
        public float? RotateDegrees;

        /// <summary><see cref="RotateDegrees"/> 写在第几行，供编译器报错时引用。</summary>
        public int RotateLine;
    }

    /// <summary>洞穴：从入口往下挖的巷道网络。</summary>
    public sealed class PCGSpecCave
    {
        /// <summary>
        /// 区域约束：洞穴只支持 <c>within</c>（"这片洞只准在这块地里挖"）。
        /// </summary>
        /// <remarks>
        /// 与散布/聚落/城市那三层的同名选项**不是一条链**：那三层筛的是互相独立的实例点，而洞穴的点是
        /// 洞道的截面序列——事后删点等于在管壁上开洞（瑞士奶酪）。所以这里把它做成**产生器的输入**：
        /// 挖到区域边界前把步长截短，落点与方向都不搬（见 <c>PCGCavePlanner.Region</c>）。
        /// <c>outside</c> / <c>except</c> 因此**故意不接**：那需要"绕开一块地"的走法，不是截断能表达的，
        /// 与其给一个静默不生效的开关，不如让解析器按"不认识选项"拒掉。
        /// </remarks>
        public float? AreaMinX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 X 上界。</summary>
        public float? AreaMaxX;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 下界。</summary>
        public float? AreaMinZ;

        /// <summary><see cref="AreaMinX"/> 那片区域的 Z 上界。</summary>
        public float? AreaMaxZ;

        /// <summary>最深钻到入口以下多少。</summary>
        public float? Depth;

        /// <summary>一条隧道最多几段。</summary>
        public int? Segments;

        /// <summary>洞道半径。</summary>
        public float? Radius;

        /// <summary>整个洞穴最多几条隧道。</summary>
        public int? MaxTunnels;

        /// <summary>分叉概率。</summary>
        public float? BranchChance;

        /// <summary>单步最大转角。</summary>
        public float? MaxTurn;

        /// <summary>管道一圈几边。</summary>
        public int? TubeSides;

        /// <summary>`at &lt;x&gt; &lt;z&gt;`：把洞口摆在写下的位置（Y 取 0）；没写时为 null。</summary>
        public float? AtX;

        /// <summary><see cref="AtX"/> 的 Z。</summary>
        public float? AtZ;

        /// <summary><see cref="AtX"/> 写在第几行，供编译器报错时引用。</summary>
        public int AtLine;
    }

    /// <summary>
    /// 一段设定描述：区域尺寸、地貌判据、散布的植被与石头、水、聚居地、洞穴。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这里是"按设定描述生成"的**正面回答**：一段人能读的文字进去，一张能跑的图出来。它刻意只做到这一步——
    /// 描述 → 图。区域尺寸这类场景侧设置留在 <see cref="Region"/> 里由宿主应用，因为图里没有"生成范围"这个东西
    /// （生成范围来自体积）。
    /// </para>
    /// <para>
    /// 每条指令都映射到**已有的**节点，不发明新词汇：林子/石头/草是 `Volume Sampler → [Slope Filter] →
    /// Spawn Static Model`，水是 `Hydrology → Spawn Static Model`，聚居地是 `Split Grammar → Spawn Static Model`，
    /// 洞穴是 `Cave → Spawn Geometry`，地貌底图就是 `Biome`。
    /// </para>
    /// </remarks>
    public sealed class PCGWorldSpec
    {
        /// <summary>图名。</summary>
        public string Name = "PCG World";

        /// <summary>图的默认种子。</summary>
        public int Seed;

        /// <summary>区域尺寸；没写过 `region` 时为 null。</summary>
        public PCGSpecRegion Region;

        /// <summary>有没有写过 `biome`。</summary>
        public bool HasBiome;

        /// <summary>地貌底图的判据参数。</summary>
        public readonly PCGSpecBiome Biome = new PCGSpecBiome();

        /// <summary>散布层，按写法顺序。</summary>
        public readonly List<PCGSpecScatter> Scatters = new List<PCGSpecScatter>();

        /// <summary>有没有写过 `water`。</summary>
        public bool HasWater;

        /// <summary>水域参数。</summary>
        public PCGSpecWater Water;

        /// <summary>聚居地，按写法顺序；可以写多个（大世界里本就不止一个村子）。</summary>
        public readonly List<PCGSpecSettlement> Settlements = new List<PCGSpecSettlement>();

        /// <summary>城市，按写法顺序；可以写多个。</summary>
        public readonly List<PCGSpecCity> Cities = new List<PCGSpecCity>();

        /// <summary>有没有写过 `cave`。</summary>
        public bool HasCave;

        /// <summary>洞穴参数。</summary>
        public PCGSpecCave Cave;

        /// <summary>写了 `terrain` 指令时为 true（决定 B：写高度是破坏性的，不写就不造）。</summary>
        public bool HasTerrain;

        /// <summary>要造出来的地形；没写 `terrain` 时为 null。</summary>
        public PCGSpecTerrain Terrain;

        /// <summary>路，按写法顺序。</summary>
        public readonly List<PCGSpecRoad> Roads = new List<PCGSpecRoad>();

        /// <inheritdoc />
        public override string ToString() =>
            $"{Name}: seed {Seed}, {Scatters.Count} scatter layer(s)" +
            (HasBiome ? ", biome" : string.Empty) +
            (HasWater ? ", water" : string.Empty) +
            (Settlements.Count > 0 ? $", {Settlements.Count} settlement(s)" : string.Empty) +
            (Cities.Count > 0 ? $", {Cities.Count} city/cities" : string.Empty) +
            (HasCave ? ", cave" : string.Empty) +
            (Roads.Count > 0 ? $", {Roads.Count} road(s)" : string.Empty);
    }

    /// <summary>地貌底图的判据参数，对应 `Biome` 节点。</summary>
    public sealed class PCGSpecBiome
    {
        /// <summary>底图格边长。</summary>
        public float? CellSize;

        /// <summary>水位。</summary>
        public float? WaterLevel;

        /// <summary>水位余量。</summary>
        public float? WaterMargin;

        /// <summary>起伏的测量半径。</summary>
        public float? ReliefRadius;

        /// <summary>周边平均的测量半径。</summary>
        public float? RegionRadius;

        /// <summary>每个半径上取几个方向。</summary>
        public int? Samples;

        /// <summary>判定为山地的坡度下界。</summary>
        public float? MountainSlope;

        /// <summary>判定为山地的起伏下界。</summary>
        public float? MountainRelief;

        /// <summary>判定为丘陵的坡度下界。</summary>
        public float? HillSlope;

        /// <summary>判定为丘陵的起伏下界。</summary>
        public float? HillRelief;

        /// <summary>判定为盆地的相对低洼下界。</summary>
        public float? BasinDepth;

        /// <summary>盆地底面允许的起伏。</summary>
        public float? BasinRelief;
    }

    /// <summary>
    /// 把一段设定描述解析成 <see cref="PCGWorldSpec"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 格式是**一行一条指令**，`#` 起注释，双引号里的内容原样保留（模型路径里有空格也能写）。指令与选项都不区分
    /// 大小写。所有错误都带行号，因为"第 7 行不认识 'densityy'"能直接定位，而"解析失败"不能。
    /// </para>
    /// <para>
    /// 这个类型是纯的：不碰引擎、不查资产、不认识任何节点类型。资产路径到资产 id 的解析发生在
    /// <see cref="PCGSpecCompiler"/> 那一侧，由宿主注入。
    /// </para>
    /// </remarks>
    public static class PCGSpecParser
    {
        /// <summary>认识的指令，按错误信息里列出的顺序。</summary>
        public static readonly string[] Directives =
        {
            "world", "seed", "region", "biome", "terrain", "scatter", "water", "village", "city", "cave", "road",
        };

        /// <summary>名字与值的个数，以及值该落到规格类的哪个字段上。</summary>
        public readonly struct Option
        {
            /// <summary>这个选项后面跟几个值。</summary>
            public readonly int Arity;

            /// <summary>目标字段名。</summary>
            public readonly string Field;

            /// <summary>声明一个选项。</summary>
            public Option(int arity, string field)
            {
                Arity = arity;
                Field = field;
            }
        }

        /// <summary>
        /// 某条指令认识的选项表；不认识的指令返回 null。
        /// </summary>
        /// <remarks>
        /// 暴露出来是为了让断言能**遍历**它：给每条指令的每个选项都造一个值跑一遍解析，于是"选项声明了但字段
        /// 拼错/类型不支持/取值个数不对"这类问题会在离线门里当场炸掉，而不是等谁手写到那一行才发现。
        /// </remarks>
        public static IReadOnlyDictionary<string, Option> OptionsFor(string directive)
        {
            switch (directive)
            {
            case "region": return RegionOptions;
            case "biome": return BiomeOptions;
            case "terrain": return TerrainOptions;
            case "scatter": return ScatterOptions;
            case "water": return WaterOptions;
            case "village": return SettlementOptions;
            case "cave": return CaveOptions;
            case "city": return CityOptions;
            case "road": return RoadOptions;
            default: return null;
            }
        }

        /// <summary>解析失败时抛出的内部异常：只在 <see cref="TryParse"/> 内部流转，不会逃出去。</summary>
        private sealed class SpecError : Exception
        {
            public SpecError(string message) : base(message)
            {
            }
        }

        /// <summary>解析一段设定描述。</summary>
        /// <param name="text">描述文本。</param>
        /// <param name="spec">解析结果；失败时为 null。</param>
        /// <param name="error">失败原因，带行号；成功时为 null。</param>
        /// <returns>成功为 true。</returns>
        public static bool TryParse(string text, out PCGWorldSpec spec, out string error)
        {
            spec = null;
            error = null;

            try
            {
                spec = Parse(text);
                return true;
            }
            catch (SpecError failure)
            {
                error = failure.Message;
                spec = null;
                return false;
            }
        }

        private static PCGWorldSpec Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new SpecError("设定描述是空的；至少要有一行，例如：world 温带山谷");

            var spec = new PCGWorldSpec();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool any = false;

            for (int i = 0; i < lines.Length; i++)
            {
                int number = i + 1;
                string body = StripComment(lines[i]).Trim();

                if (body.Length == 0)
                    continue;

                any = true;

                var tokens = Tokenize(body, number);
                string directive = tokens[0].ToLowerInvariant();

                switch (directive)
                {
                case "world":
                    spec.Name = Join(tokens, 1, number, "'world' 后面要跟一个名字，例如：world 温带山谷");
                    break;

                case "seed":
                    Once(seen, directive, number);
                    spec.Seed = Int(Value(tokens, 1, number, "'seed' 后面要跟一个整数种子"), "seed", number);
                    break;

                case "region":
                    Once(seen, directive, number);
                    spec.Region = new PCGSpecRegion();
                    spec.Region.SizeX = Float(Value(tokens, 1, number, "'region' 后面要跟区域尺寸，例如：region 2000 2000"),
                        "region", number);
                    spec.Region.SizeZ = Float(Value(tokens, 2, number, "'region' 需要两个尺寸：region <X> <Z>"),
                        "region", number);
                    Expect(tokens, 3, number);
                    Options(tokens, 3, number, directive, RegionOptions, spec.Region);
                    break;

                case "biome":
                    Once(seen, directive, number);
                    spec.HasBiome = true;
                    Options(tokens, 1, number, directive, BiomeOptions, spec.Biome);
                    break;

                case "scatter":
                    spec.Scatters.Add(ParseScatter(tokens, number));
                    break;

                case "water":
                    Once(seen, directive, number);
                    spec.HasWater = true;
                    spec.Water = new PCGSpecWater();
                    Options(tokens, 1, number, directive, WaterOptions, spec.Water);
                    break;

                case "village":
                    // 可以写多次：一个世界里的村子不止一个，而把它限成一次会让"两个村庄"说不出来。
                    var settlement = new PCGSpecSettlement();
                    Options(tokens, 1, number, directive, SettlementOptions, settlement);
                    spec.Settlements.Add(settlement);
                    break;

                case "city":
                    var city = new PCGSpecCity();
                    Options(tokens, 1, number, directive, CityOptions, city);

                    // 没有产生式的"城市"什么都长不出来：与其让它在运行期报错，不如在这里带行号说清楚。
                    if (string.IsNullOrWhiteSpace(city.Productions))
                    {
                        throw new SpecError($"第 {number} 行：'city' 少了产生式表；写 " +
                                            "productions \"城区 = 街区,街区: X:0.5,0.5; …\" 指定它");
                    }

                    spec.Cities.Add(city);
                    break;

                case "cave":
                    Once(seen, directive, number);
                    spec.HasCave = true;
                    spec.Cave = new PCGSpecCave();
                    Options(tokens, 1, number, directive, CaveOptions, spec.Cave);
                    break;

                case "terrain":
                    // 造地形是**破坏性**的（决定 B），所以只在写了这条指令时才发生：没写就保持"只判不造"，
                    // 与既有的 biome/water 完全一样地跑在别人铺好的地面上。
                    Once(seen, directive, number);
                    spec.HasTerrain = true;
                    spec.Terrain = new PCGSpecTerrain();
                    Options(tokens, 1, number, directive, TerrainOptions, spec.Terrain);

                    // 形状表在这里解析、错了带行号：整条链路要到"写高度"那一刻才会碰这条字符串，
                    // 而那时候既没有行号也没有描述文件名，说不清是哪一行写坏了。
                    spec.Terrain.Features = ParseTerrainShapes(spec.Terrain.Shapes, number);

                    // 洞掩码用的是**同一条**形状语法与同一个解析器（错误一样带行号）。
                    if (!string.IsNullOrWhiteSpace(spec.Terrain.Holes))
                        spec.Terrain.HoleFeatures = ParseTerrainShapes(spec.Terrain.Holes, number);
                    break;

                case "road":
                    spec.Roads.Add(ParseRoad(tokens, number));
                    break;

                default:
                    throw new SpecError($"第 {number} 行：不认识的指令 '{tokens[0]}'；能用的有 " +
                                       $"（{string.Join(" / ", Directives)}）");
                }
            }

            if (!any)
                throw new SpecError("描述里没有任何指令；至少要写一行，例如：world 温带山谷");

            if (spec.Name.Length == 0)
                throw new SpecError("描述里没有名字；至少要写一行：world <名字>");

            return spec;
        }

        // ---- 指令 --------------------------------------------------------------------------------------

        private static PCGSpecScatter ParseScatter(List<string> tokens, int number)
        {
            var layer = new PCGSpecScatter();
            layer.Line = number;

            if (tokens.Count < 2)
                throw new SpecError($"第 {number} 行：'scatter' 后面要跟这一层的名字，例如：scatter forest");

            layer.Name = tokens[1];
            int at = 2;

            while (at < tokens.Count)
            {
                string key = tokens[at++].ToLowerInvariant();

                if (!ScatterOptions.TryGetValue(key, out var option))
                    throw new SpecError($"第 {number} 行：'scatter' 不认识选项 '{key}'；能用的有 " +
                                       $"（{string.Join(" / ", ScatterOptions.Keys)}）");

                if (at + option.Arity > tokens.Count)
                    throw new SpecError($"第 {number} 行：选项 '{key}' 需要 {option.Arity} 个值，后面没有了");

                if (key == "slope")
                {
                    // 唯一一个"两个值落进两个字段"的选项，反射那套表达不了；其余全按表里的字段名走。
                    layer.MinSlope = Float(tokens[at], key, number);
                    layer.MaxSlope = Float(tokens[at + 1], key, number);
                    at += option.Arity;
                    continue;
                }

                if (key == "clip")
                {
                    // 形状词 + 三个数落进**四个字段**，而三个数的含义**随形状变**：盒是三条边长，
                    // 圆柱是"直径、高、边数"（第三个数不是厚度）。反射那套表达不了，所以走显式分支。
                    string shape = (tokens[at] ?? string.Empty).ToLowerInvariant();

                    if (!IsKnownClipShape(shape))
                        throw new SpecError($"第 {number} 行：不认识的裁切形状 '{tokens[at]}'；" +
                                           $"能用的是（{string.Join(" / ", ClipShapes)}）");

                    float x = Float(tokens[at + 1], key, number);
                    float y = Float(tokens[at + 2], key, number);
                    float z = Float(tokens[at + 3], key, number);

                    layer.ClipShape = shape;
                    layer.ClipSize = shape == "cylinder" ? new Vector3(x, y, 0.0f) : new Vector3(x, y, z);
                    layer.ClipSegments = shape == "cylinder" ? (int?)Math.Round(z) : null;
                    layer.ClipLine = number;
                    at += option.Arity;
                    continue;
                }

                if (key == "cut")
                {
                    // 四个值落进**两个字段**（法线 + 沿法线推多远），反射那套同样表达不了，所以走显式分支。
                    layer.GeometryCutNormal = new Vector3(Float(tokens[at], key, number),
                                                          Float(tokens[at + 1], key, number),
                                                          Float(tokens[at + 2], key, number));
                    layer.GeometryCutOffset = Float(tokens[at + 3], key, number);
                    layer.GeometryCutLine = number;
                    at += option.Arity;
                    continue;
                }

                if (key == "within" || key == "outside")
                {
                    // 判据只有一份（`ApplyRegionOption`）。这里曾经自己抄了一份，措辞与共享版不同；
                    // directive 传 `scatter <层名>` 是为了让报错仍然点名是哪一层，不退回成光秃秃的 'scatter'。
                    at += ApplyRegionOption(layer, key, tokens, at, number, $"scatter {layer.Name}");
                    continue;
                }

                if (key == "except")
                {
                    // 与 within/outside 同样的四个数、同样的"先最小角再最大角"，但职责相反：
                    // 那两条圈定这一层在哪，这一条从已圈定的范围里去掉一块，所以同写是 A − B 而不是冲突。
                    float minX = Float(tokens[at], key, number);
                    float minZ = Float(tokens[at + 1], key, number);
                    float maxX = Float(tokens[at + 2], key, number);
                    float maxZ = Float(tokens[at + 3], key, number);

                    if (maxX <= minX || maxZ <= minZ)
                        throw new SpecError($"第 {number} 行：'except' 的区域写反了或退化成了一条线" +
                                           $"（x {minX}..{maxX}，z {minZ}..{maxZ}）；先写最小角再写最大角");

                    if (layer.ExceptMinX.HasValue)
                        throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 写了不止一次 except；" +
                                            "一层只挖一处，要挖多处就拆成两层（各自 except 自己那一块）");

                    layer.ExceptMinX = minX;
                    layer.ExceptMinZ = minZ;
                    layer.ExceptMaxX = maxX;
                    layer.ExceptMaxZ = maxZ;
                    layer.ExceptLine = number;

                    at += option.Arity;
                    continue;
                }

                if (key == "near" || key == "away" || key == "fade")
                {
                    // 三个都是"两个值、两个字段"（名字 + 半径），反射那套同样表达不了，所以和 slope 一样走显式分支。
                    // 两个值一个是名字一个是数，按字段名走只会把名字塞进 float? 上然后报一个看不懂的类型错。
                    string source = tokens[at];
                    float radius = Float(tokens[at + 1], key, number);

                    if (radius <= 0.0f)
                        throw new SpecError($"第 {number} 行：'{key} {source} {tokens[at + 1]}' 的半径必须是正数；" +
                                           "填 0 等于要求「什么都不留」或「全留」，两头都没有写它的意义");

                    switch (key)
                    {
                    case "near":
                        layer.NearSource = source;
                        layer.NearRadius = radius;
                        layer.NearLine = number;
                        break;

                    case "away":
                        layer.AwaySource = source;
                        layer.AwayRadius = radius;
                        layer.AwayLine = number;
                        break;

                    default:
                        layer.FadeSource = source;
                        layer.FadeRadius = radius;
                        layer.FadeLine = number;
                        break;
                    }

                    at += option.Arity;
                    continue;
                }

                // 走**表里的字段名**，而不是再写一条 switch：表和 switch 是两份清单，多一个选项就多一次
                // "声明了但没落地"的机会——本轮 `jitter` 正是这样被静默丢掉的（解析不报错、值没写进去，
                // 而当时的选项覆盖断言只验了"能解析"，验不出"值到了"）。
                var field = typeof(PCGSpecScatter).GetField(option.Field);

                if (field == null)
                {
                    throw new SpecError($"第 {number} 行：'scatter' 的选项 '{key}' 指向字段 {option.Field}，" +
                                       "而规格类里没有这个字段（这是插件的缺陷，不是描述写错了）");
                }

                field.SetValue(layer, ConvertValue(field.FieldType, tokens, at, key, number));

                if (key == "biome")
                    layer.BiomeLine = number;

                at += option.Arity;
            }

            if (layer.Points.HasValue && layer.PointsPerBlock.HasValue)
                throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 同时写了 points 与 per-block；" +
                                   "一个是固定点数、一个是按面积算，只能留一个");

            // 区域也只能有一个：矩形（within/outside）与多边形（polygon）都在回答"这片地在哪里"，
            // 同时写等于让编译器挑一个执行，而作者看到的"点只出现在半个区域里"根本指不回原因。
            if (!string.IsNullOrWhiteSpace(layer.PolygonText) && layer.AreaMinX.HasValue)
                throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 同时写了 polygon 与 within/outside；" +
                                   "两者都是「这片区域在哪」，只能留一个（矩形用 within/outside，任意形状用 polygon）");

            // "长什么"也只能有一个：模型资产与程序化几何是两条不同的链（后者走 Spawn Geometry）。
            if (!string.IsNullOrWhiteSpace(layer.GeometryShape) && !string.IsNullOrWhiteSpace(layer.ModelPath))
                throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 同时写了 geometry 与 model；" +
                                   "一个是程序化几何、一个是模型资产，只能留一个");

            if (!string.IsNullOrWhiteSpace(layer.GeometryShape))
            {
                if (!IsKnownGeometryShape(layer.GeometryShape))
                    throw new SpecError($"第 {number} 行：不认识的几何形状 '{layer.GeometryShape}'；" +
                                       "能用的是 box（盒）/ grid（格）/ cylinder（柱）/ stairs（台阶）");

                // 尺寸必填：节点自己的默认是"格子级"的 1000×200×1000，漏写会得到一个一公里大的盒子，
                // 而"图上看着对、世界里大得离谱"比一句报错难查得多。
                if (!layer.GeometrySize.HasValue)
                    throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 写了 geometry 但没给尺寸；" +
                                       "写 geometry-size <x> <y> <z>（世界单位，例如柱子 3 12 3）");
            }

            // 刀口只对程序化几何有意义（资产模型那半的网格数据不归我们造），法线也不许是零长度。
            if (layer.GeometryCutNormal.HasValue)
            {
                if (string.IsNullOrWhiteSpace(layer.GeometryShape))
                    throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 写了 cut 但这一层不是程序化几何；" +
                                       "`cut` 只对 geometry 那种写法有意义");

                if (layer.GeometryCutNormal.Value.LengthSquared <= 1e-12f)
                    throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 的 cut 法线是零长度，" +
                                       "没有方向的刀口切不出东西；写 cut 0 1 0 0 就是「从中间横切」");
            }

            // 裁切（`clip`）同样只对程序化几何有意义，而且形状与尺寸都要立得住。
            if (!string.IsNullOrWhiteSpace(layer.ClipShape))
            {
                if (string.IsNullOrWhiteSpace(layer.GeometryShape))
                    throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 写了 clip 但这一层不是程序化几何；" +
                                       "`clip` 只对 geometry 那种写法有意义");

                var clipSize = layer.ClipSize ?? Vector3.Zero;

                if (clipSize.X <= 0.0f || clipSize.Y <= 0.0f ||
                    (layer.ClipShape != "cylinder" && clipSize.Z <= 0.0f))
                    throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 的 clip 尺寸要写正数：" +
                                       "盒写 clip box <x> <y> <z>、圆柱写 clip cylinder <直径> <高> <边数>");

                if (layer.ClipSegments.HasValue && (layer.ClipSegments.Value < 3 || layer.ClipSegments.Value > 128))
                    throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 的 clip cylinder 边数要在 3..128 之间");
            }

            // 输出形态：只认三个词；`merged` 只对程序化几何有意义（模型资产那半没有"合并成一锅"这回事，
            // 而"写了没生效"是最难发现的一类）。
            if (!string.IsNullOrWhiteSpace(layer.OutputForm))            {
                if (!IsKnownOutputForm(layer.OutputForm))
                    throw new SpecError($"第 {number} 行：不认识的输出形态 '{layer.OutputForm}'；" +
                                       "能用的是 actors（一个点一个）/ instanced（实例化到一个宿主）/ " +
                                       "merged（合并成一个 Actor）");

                if (NormalizeOutputForm(layer.OutputForm) == "merged" &&
                    string.IsNullOrWhiteSpace(layer.GeometryShape))
                {
                    throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 写了 output merged，但这一层不是" +
                                       "程序化几何；`merged` 只对 geometry 那种写法有意义");
                }
            }

            if (layer.MinSlope.HasValue && layer.MaxSlope.HasValue && layer.MaxSlope.Value < layer.MinSlope.Value)
                throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 的坡度过区间写反了" +
                                   $"（{layer.MinSlope}..{layer.MaxSlope}）");

            // 一层只能有一个位置关系：同时写两个，「保留」与「丢掉」/「砍掉」与「渐变」互相抵消，结果既不是这个
            // 也不是那个。与其让编译器挑一个悄悄执行，不如在解析期带行号问清楚。
            int relations = (layer.NearSource != null ? 1 : 0) + (layer.AwaySource != null ? 1 : 0) +
                            (layer.FadeSource != null ? 1 : 0);

            if (relations > 1)
                throw new SpecError($"第 {number} 行：'scatter {layer.Name}' 写了不止一个位置关系" +
                                   "（near / away / fade 只能挑一个）；要「附近又要避开」或「先砍远再渐变」" +
                                   "就拆成两层");

            return layer;
        }

        /// <summary>
        /// 解析一条路：`from` / `via` / `to` 各两个数（前两者必填，`via` 可重复），其余按通用选项表。
        /// </summary>
        /// <remarks>
        /// 这三个键走显式分支而不是反射：它们各要两个数落进**两个不同字段**（`via` 还要往列表里追加），反射那套
        /// 「一个键一个字段」表达不了。显式分支的好处是字段名写错会当场编译不过，比反射的运行时查表还硬。
        /// </remarks>
        private static PCGSpecRoad ParseRoad(List<string> tokens, int number)
        {
            var road = new PCGSpecRoad();
            int at = 1;

            while (at < tokens.Count)
            {
                string key = tokens[at++].ToLowerInvariant();

                if (key == "from" || key == "to" || key == "via")
                {
                    if (at + 2 > tokens.Count)
                        throw new SpecError($"第 {number} 行：'{key}' 要两个数（X 与 Z），后面没有了");

                    float x = Float(tokens[at], key, number);
                    float z = Float(tokens[at + 1], key, number);

                    if (key == "from")
                    {
                        road.FromX = x;
                        road.FromZ = z;
                    }
                    else if (key == "to")
                    {
                        road.ToX = x;
                        road.ToZ = z;
                    }
                    else
                    {
                        // 上限在这里就报，而不是等编译器发现槽位不够——描述里能数出来的错，行号是最好的报错位置。
                        if (road.Vias.Count >= PCGSpecRoad.MaxVias)
                        {
                            throw new SpecError($"第 {number} 行：'road' 的途经点最多 {PCGSpecRoad.MaxVias} 个" +
                                                "（加上起终点一共八个，正好是字面点节点的槽位数）；要更多就把几条路分着写");
                        }

                        road.Vias.Add(new Vector2(x, z));
                    }

                    at += 2;
                    continue;
                }

                if (key == "within" || key == "outside")
                {
                    // 判据只有一份（`ApplyRegionOption`）。这里曾经抄过一份：`road` 有自己的选项循环
                    // （要处理 from/via/to），进不来通用分支，于是抄的那份只写进 AreaMinX 一个字段，
                    // `outside -100 -120 100 120` 的另外三个是 null、invert 恒 false（实测）。
                    // 现在改成同一个调用——两边不可能再分家。
                    at += ApplyRegionOption(road, key, tokens, at, number, "road");
                    continue;
                }

                if (!RoadOptions.TryGetValue(key, out var option))
                    throw new SpecError($"第 {number} 行：'road' 不认识选项 '{key}'；它能用 " +
                                       "（from / via / to / " + string.Join(" / ", RoadOptions.Keys) + "）");

                if (at + option.Arity > tokens.Count)
                    throw new SpecError($"第 {number} 行：选项 '{key}' 需要 {option.Arity} 个值，后面没有了");

                var field = typeof(PCGSpecRoad).GetField(option.Field);

                if (field == null)
                    throw new SpecError($"第 {number} 行：'road' 的选项 '{key}' 指向字段 {option.Field}，" +
                                       "而规格类里没有这个字段（这是插件的缺陷，不是描述写错了）");

                field.SetValue(road, ConvertValue(field.FieldType, tokens, at, key, number));
                at += option.Arity;
            }

            // 没有两端点的"路"没有意义：它连"从哪到哪"都说不出来，与其猜一个默认值，不如报错。
            if (!road.FromX.HasValue || !road.FromZ.HasValue)
                throw new SpecError($"第 {number} 行：'road' 少了起点；写 from <X> <Z> 指定它");

            if (!road.ToX.HasValue || !road.ToZ.HasValue)
                throw new SpecError($"第 {number} 行：'road' 少了终点；写 to <X> <Z> 指定它");

            return road;
        }

        /// <summary>
        /// 区域判据的**唯一一份**实现：`within` / `outside` / `except` 三条都是"四个数 → 四个同名字段"，
        /// 前缀按角色换（<c>Area*</c> 说"长在哪"，<c>Except*</c> 说"从中去掉哪"），`outside` 只多置一个 invert。
        /// </summary>
        /// <remarks>
        /// 存在的理由：`road` 这条指令有自己的选项循环（要处理 from/via/to），进不来通用分支，
        /// 于是这段判据一度被抄成两份。抄两份迟早分家——只在报错措辞上分家还算小事，
        /// 判据本身分家就是"同一句话在两层里意思不同"。现在两边都调这里。
        /// </remarks>
        /// <returns>这一档吃掉了几个 token（恒为 4：两个对角）。</returns>
        private static int ApplyRegionOption(object target, string key, List<string> tokens, int at,
            int number, string directive)
        {
            bool hole = key == "except";
            string prefix = hole ? "Except" : "Area";
            var type = target.GetType();

            var minXField = type.GetField(prefix + "MinX");
            var minZField = type.GetField(prefix + "MinZ");
            var maxXField = type.GetField(prefix + "MaxX");
            var maxZField = type.GetField(prefix + "MaxZ");

            if (minXField == null || minZField == null || maxXField == null || maxZField == null)
            {
                throw new SpecError($"第 {number} 行：'{directive}' 支持 {key}，但规格类里没有 " +
                                   $"{prefix}MinX/{prefix}MinZ/{prefix}MaxX/{prefix}MaxZ 字段" +
                                   "（这是插件的缺陷，不是描述写错了）");
            }

            // 通用分支在进这里之前已经数过参数个数；路那条循环没有，所以这道检查留在这里兜住两边。
            if (at + 3 >= tokens.Count)
            {
                throw new SpecError($"第 {number} 行：选项 '{key}' 需要 4 个值（先最小角再最大角），后面没有了");
            }

            float minX = Float(tokens[at], key, number);
            float minZ = Float(tokens[at + 1], key, number);
            float maxX = Float(tokens[at + 2], key, number);
            float maxZ = Float(tokens[at + 3], key, number);

            if (maxX <= minX || maxZ <= minZ)
            {
                throw new SpecError($"第 {number} 行：'{key}' 的区域写反了或退化成了一条线" +
                                   $"（x {minX}..{maxX}，z {minZ}..{maxZ}）；先写最小角再写最大角");
            }

            if (minXField.GetValue(target) != null)
            {
                throw new SpecError($"第 {number} 行：'{directive}' 写了不止一次 {key}；" +
                                    "一处只圈一块，要更复杂的形状就拆成两层");
            }

            minXField.SetValue(target, minX);
            minZField.SetValue(target, minZ);
            maxXField.SetValue(target, maxX);
            maxZField.SetValue(target, maxZ);

            if (!hole)
                type.GetField("AreaInvert")?.SetValue(target, key == "outside");

            type.GetField(prefix + "Line")?.SetValue(target, number);

            return 4;
        }

        /// <summary>逐对读 `key value...`，把值写进 <paramref name="target"/> 上 <see cref="Option.Field"/> 指到的字段。</summary>
        /// <remarks>
        /// 键名与字段名分开写：描述里要的是好读的短词（`water`、`per-block`），而字段名要的是自解释的长名
        /// （`WaterLevel`、`PointsPerBlock`），两者硬凑成一个规则只会得到一堆谁都猜不到的字段名。字段不存在时
        /// 直接报错，而不是静默跳过一个选项——那会变成"描述写了、图里没有"。
        /// </remarks>
        /// <summary>
        /// 解析 `terrain` 的形状表：分号分隔，每项 `形状 X Z 半径 幅度 [边缘过渡]`。
        /// </summary>
        /// <remarks>
        /// 表放在**描述层**解析、而不是留给运行期：写错形状名的代价是"整片地形静默地不长"，
        /// 而这里能带行号说清是第几行、哪一项，运行期只能报一句"形状表有问题"。
        /// </remarks>
        private static List<PCGTerrainFeature> ParseTerrainShapes(string shapes, int number)
        {
            if (string.IsNullOrWhiteSpace(shapes))
            {
                throw new SpecError($"第 {number} 行：'terrain' 少了形状表；写 " +
                                    "shapes \"dome 0 0 600 120\" 指定要造的地貌" +
                                    "（盆地 basin / 台地 plateau / 土丘 dome，半径外恒为 0）");
            }

            var features = new List<PCGTerrainFeature>();

            foreach (string entry in shapes.Split(';'))
            {
                string trimmed = entry.Trim();

                if (trimmed.Length == 0)
                    continue;

                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 5)
                {
                    throw new SpecError($"第 {number} 行：形状 '{trimmed}' 要写五段：形状 X Z 半径 幅度" +
                                        "（可以再加一个边缘过渡 0..1，例如 basin 0 0 600 120 0.4）");
                }

                PCGTerrainShape shape;

                switch (parts[0].ToLowerInvariant())
                {
                case "plateau": shape = PCGTerrainShape.Plateau; break;
                case "basin": shape = PCGTerrainShape.Basin; break;
                case "dome": shape = PCGTerrainShape.Dome; break;
                default:
                    throw new SpecError($"第 {number} 行：不认识的形状 '{parts[0]}'；能用的是 " +
                                        "plateau（台地）/ basin（盆地）/ dome（土丘，几座叠起来就是丘陵）");
                }

                float centerX = Float(parts[1], "shapes", number);
                float centerZ = Float(parts[2], "shapes", number);
                float radius = Float(parts[3], "shapes", number);
                float height = Float(parts[4], "shapes", number);
                float falloff = parts.Length > 5 ? Float(parts[5], "shapes", number) : 0.0f;

                // 半径为零的形状对高度场没有任何影响，而"描述写了、地上什么都没变"是最难查的一类：
                // 与其静默地接受，不如当场说清楚。
                if (radius <= 0.0f)
                    throw new SpecError($"第 {number} 行：形状 '{trimmed}' 的半径要大于零（半径外恒为 0）");

                features.Add(new PCGTerrainFeature(shape, new Vector2(centerX, centerZ), radius, height, falloff));
            }

            if (features.Count == 0)
                throw new SpecError($"第 {number} 行：'terrain' 的形状表里一个形状都没有");

            return features;
        }

        private static void Options(List<string> tokens, int from, int number, string directive,
            Dictionary<string, Option> schema, object target)
        {
            int at = from;

            while (at < tokens.Count)
            {
                string key = tokens[at++].ToLowerInvariant();

                if (!schema.TryGetValue(key, out var option))
                    throw new SpecError($"第 {number} 行：'{directive}' 不认识选项 '{key}'；能用的有 " +
                                       $"（{string.Join(" / ", schema.Keys)}）");

                if (at + option.Arity > tokens.Count)
                    throw new SpecError($"第 {number} 行：选项 '{key}' 需要 {option.Arity} 个值，后面没有了");

                if (key == "at")
                {
                    // 两个值落进两个字段（X 与 Z），和 `slope` 一样是反射那套表达不了的；三个用它的指令
                    // （village / city / cave）字段同名，所以这一段共用。
                    var xField = target.GetType().GetField("AtX");
                    var zField = target.GetType().GetField("AtZ");

                    if (xField == null || zField == null)
                        throw new SpecError($"第 {number} 行：'{directive}' 支持 at，但规格类里没有 AtX/AtZ 字段" +
                                           "（这是插件的缺陷，不是描述写错了）");

                    xField.SetValue(target, Float(tokens[at], key, number));
                    zField.SetValue(target, Float(tokens[at + 1], key, number));
                    target.GetType().GetField("AtLine")?.SetValue(target, number);
                    at += option.Arity;
                    continue;
                }

                if (key == "within" || key == "outside" || key == "except")
                {
                    at += ApplyRegionOption(target, key, tokens, at, number, directive);
                    continue;
                }

                if (key == "rotate")
                {
                    // 一个值一个字段，但还要记下它写在第几行（"rotate 没有 at"这个错必须在编译期带行号报出来），
                    // 所以走显式分支而不是表里的通用路径。
                    var rotateField = target.GetType().GetField("RotateDegrees");

                    if (rotateField == null)
                        throw new SpecError($"第 {number} 行：'{directive}' 支持 rotate，但规格类里没有 RotateDegrees 字段" +
                                           "（这是插件的缺陷，不是描述写错了）");

                    rotateField.SetValue(target, Float(tokens[at], key, number));
                    target.GetType().GetField("RotateLine")?.SetValue(target, number);
                    at += option.Arity;
                    continue;
                }

                var field = target.GetType().GetField(option.Field);
                if (field == null)
                    throw new SpecError($"第 {number} 行：'{directive}' 的选项 '{key}' 指向字段 {option.Field}，" +
                                       "而规格类里没有这个字段（这是插件的缺陷，不是描述写错了）");

                field.SetValue(target, ConvertValue(field.FieldType, tokens, at, key, number));
                at += option.Arity;
            }
        }

        private static object ConvertValue(Type type, List<string> tokens, int at, string key, int number)
        {
            if (type == typeof(string))
                return tokens[at];

            // 可空与不可空都认：可空表示"没写就不写进槽位"，不可空表示这一项必填（区域尺寸就是）。
            if (type == typeof(bool) || type == typeof(bool?))
                return Bool(tokens[at], key, number);

            if (type == typeof(int) || type == typeof(int?))
                return Int(tokens[at], key, number);

            if (type == typeof(float) || type == typeof(float?))
                return Float(tokens[at], key, number);

            if (type == typeof(Vector3) || type == typeof(Vector3?))
            {
                if (at + 2 >= tokens.Count)
                    throw new SpecError($"第 {number} 行：选项 '{key}' 要三个数（X Y Z）");

                return new Vector3(Float(tokens[at], key, number), Float(tokens[at + 1], key, number),
                    Float(tokens[at + 2], key, number));
            }

            throw new SpecError($"第 {number} 行：选项 '{key}' 的字段类型 {type.Name} 还不支持（插件缺陷）");
        }

        private static string Value(List<string> tokens, int index, int number, string what)
        {
            if (index >= tokens.Count)
                throw new SpecError($"第 {number} 行：{what}");

            return tokens[index];
        }

        private static void Expect(List<string> tokens, int index, int number)
        {
            if (index >= tokens.Count)
                throw new SpecError($"第 {number} 行：这条指令少写了参数");
        }

        /// <summary>取从 <paramref name="from"/> 起的剩余 token 拼成一个名字（允许名字里有空格）。</summary>
        private static string Join(List<string> tokens, int from, int number, string what)
        {
            if (from >= tokens.Count)
                throw new SpecError($"第 {number} 行：{what}");

            return string.Join(" ", tokens.GetRange(from, tokens.Count - from));
        }

        private static void Once(HashSet<string> seen, string directive, int number)
        {
            if (!seen.Add(directive))
                throw new SpecError($"第 {number} 行：'{directive}' 只能写一次");
        }

        private static float Float(string text, string key, int number)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new SpecError($"第 {number} 行：选项 '{key}' 的值 '{text}' 不是数字");
            }

            return value;
        }

        private static int Int(string text, string key, int number)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new SpecError($"第 {number} 行：选项 '{key}' 的值 '{text}' 不是整数");

            return value;
        }

        private static bool Bool(string text, string key, int number)
        {
            switch (text.ToLowerInvariant())
            {
            case "on":
            case "true":
            case "yes":
            case "1":
                return true;
            case "off":
            case "false":
            case "no":
            case "0":
                return false;
            default:
                throw new SpecError($"第 {number} 行：选项 '{key}' 的值 '{text}' 不是开关" +
                                   "（写 on/off、true/false、yes/no 或 1/0）");
            }
        }

        /// <summary>切词：空白分隔，双引号里的内容原样保留。</summary>
        private static List<string> Tokenize(string body, int number)
        {
            var tokens = new List<string>(8);
            var current = new System.Text.StringBuilder(32);
            bool quoted = false;
            bool open = false;

            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];

                if (c == '"')
                {
                    quoted = !quoted;
                    open = true;
                    continue;
                }

                if (!quoted && char.IsWhiteSpace(c))
                {
                    if (current.Length > 0 || open)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        open = false;
                    }

                    continue;
                }

                current.Append(c);
            }

            if (quoted)
                throw new SpecError($"第 {number} 行：引号没有成对（少一个 \"）");

            if (current.Length > 0 || open)
                tokens.Add(current.ToString());

            if (tokens.Count == 0)
                throw new SpecError($"第 {number} 行：这行是空的");

            return tokens;
        }

        private static string StripComment(string line)
        {
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                    quoted = !quoted;
                else if (line[i] == '#' && !quoted)
                    return line.Substring(0, i);
            }

            return line;
        }

        // ---- 各指令认识的选项与它们的取值个数 --------------------------------------------------------------

        private static readonly Dictionary<string, Option> RegionOptions = new Dictionary<string, Option>(StringComparer.Ordinal)
        {
            { "height", new Option(1, "Height") },
        };

        private static readonly Dictionary<string, Option> TerrainOptions = new Dictionary<string, Option>(StringComparer.Ordinal)
        {
            { "noise", new Option(1, "NoiseAmplitude") },
            { "noise-scale", new Option(1, "NoiseScale") },
            { "shapes", new Option(1, "Shapes") },
            { "holes", new Option(1, "Holes") },
        };

        private static readonly Dictionary<string, Option> BiomeOptions = new Dictionary<string, Option>(StringComparer.Ordinal)
        {
            { "cell", new Option(1, "CellSize") },
            { "water", new Option(1, "WaterLevel") },
            { "margin", new Option(1, "WaterMargin") },
            { "relief", new Option(1, "ReliefRadius") },
            { "region", new Option(1, "RegionRadius") },
            { "samples", new Option(1, "Samples") },
            { "mountain-slope", new Option(1, "MountainSlope") },
            { "mountain-relief", new Option(1, "MountainRelief") },
            { "hill-slope", new Option(1, "HillSlope") },
            { "hill-relief", new Option(1, "HillRelief") },
            { "basin-depth", new Option(1, "BasinDepth") },
            { "basin-relief", new Option(1, "BasinRelief") },
        };

        private static readonly Dictionary<string, Option> ScatterOptions = new Dictionary<string, Option>(StringComparer.Ordinal)
        {
            { "points", new Option(1, "Points") },
            { "per-block", new Option(1, "PointsPerBlock") },
            { "density", new Option(1, "Density") },
            { "scale", new Option(1, "Scale") },
            { "slope", new Option(2, "MinSlope") },
            { "biome", new Option(1, "BiomeNames") },
            { "jitter", new Option(1, "Jitter") },
            { "near", new Option(2, "NearSource") },
            { "away", new Option(2, "AwaySource") },
            { "fade", new Option(2, "FadeSource") },
            { "within", new Option(4, "AreaMinX") },
            { "outside", new Option(4, "AreaMinX") },
            { "except", new Option(4, "ExceptMinX") },
            { "except-polygon", new Option(1, "ExceptPolygonText") },
            { "polygon", new Option(1, "PolygonText") },
            { "geometry", new Option(1, "GeometryShape") },
            { "geometry-size", new Option(3, "GeometrySize") },
            { "geometry-segments", new Option(1, "GeometrySegments") },
            { "geometry-steps", new Option(1, "GeometrySteps") },
            { "clip", new Option(4, "ClipShape") },
            { "cut", new Option(4, "GeometryCutNormal") },
            { "output", new Option(1, "OutputForm") },
            { "model", new Option(1, "ModelPath") },
            { "model-scale", new Option(1, "ModelScale") },
            { "yaw", new Option(1, "RandomYaw") },
        };

        /// <summary>
        /// `geometry` 能写的形状名；**这个顺序就是 <c>PCGGeometryNode.Shape*</c> 的下标**。
        /// </summary>
        /// <remarks>
        /// 名字表只有这一份，节点的下标也只在这里被翻译一次。两者一旦漂了，"说柱子长出盒子"会静默发生，
        /// 所以离屏门里有一条断言把这张表与节点的常量逐个钉在一起（见 `GeometryTests`）。
        /// </remarks>
        public static readonly string[] GeometryShapes =
        { "box", "grid", "cylinder", "stairs", "wedge", "house", "tree" };

        /// <summary>形状名是不是认识的一个（大小写不敏感）。</summary>
        public static bool IsKnownGeometryShape(string name) =>
            !string.IsNullOrWhiteSpace(name) && Array.IndexOf(GeometryShapes, name.ToLowerInvariant()) >= 0;

        /// <summary>形状名 → 下标；不认识时给 0（调用方先用 <see cref="IsKnownGeometryShape"/> 拦过）。</summary>
        public static int GeometryShapeIndex(string name)
        {
            int index = Array.IndexOf(GeometryShapes, name?.ToLowerInvariant());
            return index >= 0 ? index : 0;
        }

        /// <summary>`clip` 能写的两种凸区域。</summary>
        public static readonly string[] ClipShapes = { "box", "cylinder" };

        /// <summary>裁切形状名是不是认识的一个（大小写不敏感）。</summary>
        public static bool IsKnownClipShape(string name) =>
            !string.IsNullOrWhiteSpace(name) && Array.IndexOf(ClipShapes, name.ToLowerInvariant()) >= 0;

        /// <summary>`output` 能写的三种形态；**空表示第一个（`actors`）**。</summary>
        public static readonly string[] OutputForms = { "actors", "instanced", "merged" };

        /// <summary>输出形态名是不是认识的一个（大小写不敏感）。</summary>
        public static bool IsKnownOutputForm(string name) =>
            !string.IsNullOrWhiteSpace(name) && Array.IndexOf(OutputForms, name.ToLowerInvariant()) >= 0;

        /// <summary>输出形态归一化；没写或不认识时回落 `actors`（不认识的那条由解析器先拦住）。</summary>
        public static string NormalizeOutputForm(string name) =>
            IsKnownOutputForm(name) ? name.ToLowerInvariant() : OutputForms[0];

        private static readonly Dictionary<string, Option> WaterOptions = new Dictionary<string, Option>(StringComparer.Ordinal)
        {
            { "cell", new Option(1, "CellSize") },
            { "threshold", new Option(1, "RiverThreshold") },
            { "min-lake-cells", new Option(1, "MinLakeCells") },
            { "water", new Option(1, "WaterOffset") },
            { "model", new Option(1, "ModelPath") },
            { "model-scale", new Option(1, "ModelScale") },
            // 水系这一档两条都接：约束筛的是**发源格**（河从哪起、湖在哪成心），
            // 所以"只准在这里发源"与"这块地不许发源"都是同一判据的取反，都表达得了。
            // 之前这里写着"outside 表达不了"是**错的**——不能表达的是"从中间截断已成形的河"，
            // 那才是连续体被删点的瑞士奶酪；发源筛选不是那回事。
            // `except` 仍不列：它需要第二块矩形，这里只有一块。
            { "within", new Option(4, "AreaMinX") },
            { "outside", new Option(4, "AreaMinX") },
        };

        private static readonly Dictionary<string, Option> SettlementOptions = new Dictionary<string, Option>(StringComparer.Ordinal)
        {
            { "lots", new Option(1, "Lots") },
            { "rules", new Option(1, "Rules") },
            { "max-depth", new Option(1, "MaxDepth") },
            { "min-cell", new Option(1, "MinCellSize") },
            { "cell", new Option(3, "CellSize") },
            { "model", new Option(1, "ModelPath") },
            { "model-scale", new Option(1, "ModelScale") },
            { "yaw", new Option(1, "RandomYaw") },
            { "geometry", new Option(1, "GeometryShape") },
            { "geometry-size", new Option(3, "GeometrySize") },
            { "geometry-segments", new Option(1, "GeometrySegments") },
            { "at", new Option(2, "AtX") },
            { "rotate", new Option(1, "RotateDegrees") },
            { "within", new Option(4, "AreaMinX") },
            { "outside", new Option(4, "AreaMinX") },
            { "except", new Option(4, "ExceptMinX") },
            { "polygon", new Option(1, "PolygonText") },
            { "except-polygon", new Option(1, "ExceptPolygonText") },
            { "output", new Option(1, "OutputForm") },
        };

        private static readonly Dictionary<string, Option> CaveOptions = new Dictionary<string, Option>(StringComparer.Ordinal)
        {
            { "depth", new Option(1, "Depth") },
            { "segments", new Option(1, "Segments") },
            { "radius", new Option(1, "Radius") },
            { "tunnels", new Option(1, "MaxTunnels") },
            { "branch", new Option(1, "BranchChance") },
            { "turn", new Option(1, "MaxTurn") },
            { "sides", new Option(1, "TubeSides") },
            { "at", new Option(2, "AtX") },
            // 洞穴只接 within：它是"在哪个平面范围内挖"的输入，由产生器在挖到边界时截短步长实现。
            // outside / except 故意不列——那要的是"绕开一块地"的走法，截断表达不了，
            // 列上去只会得到一个静默不生效的开关（本仓反复点名的那一族）。
            { "within", new Option(4, "AreaMinX") },
        };

        /// <summary>城市的选项表。`productions` 必填，缺了由指令分支负责报错并带行号。</summary>
        private static readonly Dictionary<string, Option> CityOptions = new Dictionary<string, Option>(StringComparer.Ordinal)
        {
            { "productions", new Option(1, "Productions") },
            { "root", new Option(1, "Root") },
            { "max-depth", new Option(1, "MaxDepth") },
            { "min-cell", new Option(1, "MinCellSize") },
            { "cell", new Option(3, "CellSize") },
            { "model", new Option(1, "ModelPath") },
            { "model-scale", new Option(1, "ModelScale") },
            { "yaw", new Option(1, "RandomYaw") },
            { "geometry", new Option(1, "GeometryShape") },
            { "geometry-map", new Option(1, "SymbolShapes") },
            { "geometry-size", new Option(3, "GeometrySize") },
            { "geometry-segments", new Option(1, "GeometrySegments") },
            { "at", new Option(2, "AtX") },
            { "rotate", new Option(1, "RotateDegrees") },
            { "within", new Option(4, "AreaMinX") },
            { "outside", new Option(4, "AreaMinX") },
            { "except", new Option(4, "ExceptMinX") },
            { "polygon", new Option(1, "PolygonText") },
            { "except-polygon", new Option(1, "ExceptPolygonText") },
            { "output", new Option(1, "OutputForm") },
            // 这五条与聚居地那五条走同一段解析（`Options` 里按字段名填）与同一段生成
            // （`AddRegionFilters`，城市两条产出消费者都接在筛完的那一端）。
        };

        /// <summary>路的选项表；`from`/`to` 不在表里，它们各要两个数、走显式分支。</summary>
        private static readonly Dictionary<string, Option> RoadOptions = new Dictionary<string, Option>(StringComparer.Ordinal)
        {
            { "cell", new Option(1, "CellSize") },
            { "slope", new Option(1, "SlopeLimit") },
            { "detour", new Option(1, "MaxDetour") },
            { "spacing", new Option(1, "SampleSpacing") },
            { "model", new Option(1, "ModelPath") },
            { "model-scale", new Option(1, "ModelScale") },
            { "yaw", new Option(1, "RandomYaw") },
            { "geometry", new Option(1, "GeometryShape") },
            { "geometry-size", new Option(3, "GeometrySize") },
            { "geometry-segments", new Option(1, "GeometrySegments") },
            // 这两个键由上面那段专门的分支处理（四个数 → 四个字段 + invert），**不走**表里的单字段写入。
            // 列在这里是为了让"它能用"那份提示完整；`except` 仍不列：那需要两块矩形，路这里只有一块。
            { "within", new Option(4, "AreaMinX") },
            { "outside", new Option(4, "AreaMinX") },
        };
    }
}
