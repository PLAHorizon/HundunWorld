// PCG Plugin - Node registration
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §4.3 and §6.2.

using System;
using FlaxEngine;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

namespace PCG.Runtime
{
    /// <summary>Node categories used to group entries in the graph editor's add-node menu.</summary>
    public static class PCGNodeCategories
    {
        /// <summary>Nodes that create points from the world.</summary>
        public const string Sampler = "采样器";

        /// <summary>Nodes that remove points.</summary>
        public const string Filter = "筛选器";

        /// <summary>Nodes that change points in place.</summary>
        public const string Modifier = "修饰";

        /// <summary>Nodes that combine or split point sets.</summary>
        public const string Combinator = "组合";

        /// <summary>Boolean and reordering operations between point sets, UE5's "Point Ops".</summary>
        public const string PointOps = "点操作";

        /// <summary>Nodes that choose which branch of a graph is used, UE5's "Control Flow".</summary>
        public const string ControlFlow = "控制流";

        /// <summary>Nodes that materialise points as scene content.</summary>
        public const string Spawner = "生成器";

        /// <summary>Nodes that generate arrangement rather than placement, UE5's structured generation.</summary>
        public const string Structure = "结构";

        /// <summary>
        /// 分组的**稳定键**：分组的数值 id 由它算出，而不是由上面那些显示名算出。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这一条是补一个真缺陷。分组 id 原本按分组名哈希，而画布载荷里存的恰恰是"分组 id : 类型 id"这一对，
        /// 于是"把分组名改成简体中文"这个**纯显示**动作换掉了每一个分组的 id——旧存档里的节点随即解析不到，
        /// 引擎只留下一行 <c>Failed to create Visject Surface node with id: 7:29</c>，设计者看到的则是一张
        /// 少了节点的图（这正是 <see cref="PCGNodeStableIds"/> 的文档警告过的那类失败，只是来源换成了翻译）。
        /// </para>
        /// <para>
        /// 键与显示名分开之后，翻译分组名再也不会动到 id。键取的是**原来的英文名**，所以翻译**之前**存下的图
        /// 也能照旧解析——修的是回归，而不是把旧存档判死刑。
        /// </para>
        /// </remarks>
        public static string KeyOf(string categoryName)
        {
            switch (categoryName)
            {
            case Sampler: return "Sampler";
            case Filter: return "Filter";
            case Modifier: return "Modifier";
            case Combinator: return "Combinator";
            case PointOps: return "Point Ops";
            case ControlFlow: return "Control Flow";
            case Spawner: return "Spawner";
            case Structure: return "Structure";

            // 自建分类（不在上面八个里）没有更稳的名字可用，就用它自己的名字——译者翻的是内置那几个。
            default: return categoryName;
            }
        }
    }

    /// <summary>
    /// Registers every built-in node type with <see cref="PCGNodeRegistry"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept in one place so the registry can be torn down and rebuilt on a scripts reload, and so the editor
    /// and the runtime cannot drift apart over which nodes exist - or over what each of their parameter slots
    /// means.
    /// </para>
    /// <para>
    /// The tables below are positional and must match each node class's own <c>P*</c> index constants. Each
    /// <c>Default</c> must equal the fallback that node passes to its parameter reader: the value is what the
    /// editor pre-fills a freshly placed node with, so if it disagreed with the node, the canvas would show
    /// one number while the graph ran another.
    /// </para>
    /// </remarks>
    public static class PCGNodeRegistration
    {
        private static bool _registered;

        /// <summary>True once <see cref="RegisterAll"/> has run without a subsequent clear.</summary>
        public static bool IsRegistered => _registered;

        /// <summary>
        /// Registers every built-in node type. Safe to call repeatedly; re-registration replaces entries in
        /// place rather than duplicating them.
        /// </summary>
        /// <param name="log">When true, writes the resulting node list to the log.</param>
        /// <returns>The number of registered node types.</returns>
        public static int RegisterAll(bool log = false)
        {
            // Samplers.
            PCGNodeRegistry.Register(
                PCGLandscapeSamplerNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGLandscapeSamplerNode(data),
                "地形采样器",
                "在生成范围内沿地形表面撒点。",
                new[]
                {
                    Slot("Point Count", 1000, "要撒多少个点"),
                    Slot("Jitter", 0.5f, "抖动偏移，取格子尺寸的比例"),
                    Slot("Height Offset", 0.0f, "把点抬离地表"),
                    Slot("Align To Normal", true, "让每个点对齐地表法线"),
                    EnumSlot("Sample Mode", 0, "高度查询走哪条路", "Ray Cast", "Heightfield Interpolated"),
                    Slot("Fallback To Ray Cast", true, "高度场那条路走不通时改用射线投射"),
                    Slot("Scale", 1.0f, "产出的点统一缩放多少倍"),
                    Slot("Max Slope Degrees", 90.0f, "更陡的表面上的点会被跳过；90 表示不检查"),
                    Slot("Seed Offset", 0, "把同一张图里的两个采样器区分开"),
                    Slot("Points Per Block", 0.0f,
                        "按面积撒点（每 1000x1000 单位几个点）而不是每次固定个数；0 表示用 Point Count"),
                });

            PCGNodeRegistry.Register(
                PCGSplineSamplerNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGSplineSamplerNode(data),
                "样条采样器",
                "沿样条 Actor 撒点，按弧长均分而不是按控制点参数。",
                new[]
                {
                    Slot("Spline Actor", Guid.Empty, "要采样的那条样条"),
                    Slot("Spline Name", (string)null, "Actor id 解析不出来时按这个名字找"),
                    EnumSlot("Sample Mode", 0, "点沿曲线怎么分布", "By Spacing", "By Count"),
                    Slot("Spacing", 50.0f, "相邻两点之间多少世界单位"),
                    Slot("Point Count", 100, "按个数那种模式下用这个数"),
                    Slot("Arc Jitter", 0.0f, "偏移，取步长的比例"),
                    Slot("Align To Tangent", true, "让每个点朝向曲线切线"),
                    Slot("Side Offset Min", 0.0f, "横向偏移的下界"),
                    Slot("Side Offset Max", 0.0f, "横向偏移的上界"),
                    Slot("Height Offset", 0.0f, "沿世界上方向的偏移"),
                    Slot("Scale", 1.0f, "产出的点统一缩放多少倍"),
                    Slot("Seed Offset", 0, "把同一张图里的两个采样器区分开"),
                });

            PCGNodeRegistry.Register(
                PCGNoiseSamplerNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGNoiseSamplerNode(data),
                "噪声采样器",
                "在种子噪声超过阈值的地方产出点；用的是插件自己的种子噪声，所以同一个种子永远复现同一片场。",
                new[]
                {
                    EnumSlot("Noise Kind", 1, "采哪一种种子噪声",
                        "Value 2D", "Perlin 2D", "Worley 2D", "Fbm 2D", "Value 3D", "Worley 3D", "Fbm 3D"),
                    Slot("Frequency", 0.002f, "每世界单位几个周期；越大越细碎"),
                    Slot("Threshold", 0.5f, "低于这个场值的格子会被跳过"),
                    Slot("Octaves", 1, "分形层数；1 表示只有一层"),
                    Slot("Cell Size", 0.0f, "采样格步长（世界单位）；0 表示按频率推出来"),
                    Slot("Jitter", 0.5f, "抖动偏移，取格子尺寸的比例"),
                    Slot("Height", 0.0f, "点放在哪个世界 Y 上"),
                    Slot("Seed Offset", 0, "把同一张图里的两个噪声采样器区分开"),
                    Slot("Scale", 1.0f, "产出的点统一缩放多少倍"),
                });

            PCGNodeRegistry.Register(
                PCGVolumeSamplerNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGVolumeSamplerNode(data),
                "体积采样器",
                "在生成体积里均匀撒下固定个数的点。",
                new[]
                {
                    Slot("Point Count", 1000, "要撒多少个点"),
                    Slot("Seed Offset", 0, "把同一张图里的两个采样器区分开"),
                    Slot("Scale", 1.0f, "产出的点统一缩放多少倍"),
                    Slot("Random Rotation", true, "给每个点一个随机朝向"),
                    Slot("Density", 1.0f, "写进每个点的密度；生成器把它当保留概率用"),
                    Slot("Points Per Block", 0.0f,
                        "按面积撒点（每 1000x1000 单位几个点）而不是每次固定个数；0 表示用 Point Count"),
                });

            // Filters.
            PCGNodeRegistry.Register(
                PCGDensityFilterNode.Id,
                PCGNodeCategories.Filter,
                data => new PCGDensityFilterNode(data),
                "密度筛选",
                "保留密度落在区间内的点。",
                new[]
                {
                    Slot("Min Density", 0.0f, "闭区间的下界"),
                    Slot("Max Density", 1.0f, "闭区间的上界"),
                    Slot("Invert", false, "改成保留剩下的那部分"),
                });

            PCGNodeRegistry.Register(
                PCGHeightFilterNode.Id,
                PCGNodeCategories.Filter,
                data => new PCGHeightFilterNode(data),
                "高度筛选",
                "保留世界高度落在区间内的点。",
                new[]
                {
                    Slot("Min Height", float.NegativeInfinity, "闭区间的下界（世界 Y）"),
                    Slot("Max Height", float.PositiveInfinity, "闭区间的上界（世界 Y）"),
                    Slot("Invert", false, "改成保留剩下的那部分"),
                });

            PCGNodeRegistry.Register(
                PCGSlopeFilterNode.Id,
                PCGNodeCategories.Filter,
                data => new PCGSlopeFilterNode(data),
                "坡度筛选",
                "保留表面陡峭程度落在区间内的点；复用采样器缓存下来的 $Slope，而不是再查一次地形。",
                new[]
                {
                    Slot("Min Slope Degrees", 0.0f, "0 是平地"),
                    Slot("Max Slope Degrees", 90.0f, "90 是垂直的墙"),
                    Slot("Invert", false, "改成保留剩下的那部分"),
                });

            PCGNodeRegistry.Register(
                PCGSelfPruningFilterNode.Id,
                PCGNodeCategories.Filter,
                data => new PCGSelfPruningFilterNode(data),
                "自剪枝筛选",
                "把过挤的点丢掉，每一对冲突里留下更密的那个。",
                new[]
                {
                    Slot("Min Distance", 100.0f, "要保留的中心间距；0 表示改用包围盒相交判定"),
                    Slot("Use Bounds Overlap", false, "改成比较轴对齐包围盒，而不是中心距离"),
                    Slot("Plane Only", true, "忽略竖直轴，地面上撒点要的正是这个"),
                });

            PCGNodeRegistry.Register(
                PCGDistanceFilterNode.Id,
                PCGNodeCategories.Filter,
                data => new PCGDistanceFilterNode(data),
                "距离筛选",
                "按点到某个参照位置的距离筛点，默认在 XZ 平面上量。",
                new[]
                {
                    Slot("Reference", new Vector3(), "从哪个世界位置开始量"),
                    Slot("Min Distance", 0.0f, "最近距离的下界（闭区间）"),
                    Slot("Max Distance", float.PositiveInfinity, "最远距离的上界（闭区间）"),
                    Slot("Plane Only", true, "量距离时忽略竖直轴"),
                    Slot("Invert", false, "改成保留剩下的那部分"),
                });

            PCGNodeRegistry.Register(
                PCGAreaFilterNode.Id,
                PCGNodeCategories.Filter,
                data => new PCGAreaFilterNode(data),
                "区域筛选",
                "只保留 XZ 平面上落在一片矩形里的点——唯一回答「长在哪」的筛选器。",
                new[]
                {
                    Slot("Min X", float.NegativeInfinity, "闭区间的西边界"),
                    Slot("Max X", float.PositiveInfinity, "闭区间的东边界"),
                    Slot("Min Z", float.NegativeInfinity, "闭区间的南边界"),
                    Slot("Max Z", float.PositiveInfinity, "闭区间的北边界"),
                    Slot("Invert", false, "改成保留矩形以外的点"),
                });

            PCGNodeRegistry.Register(
                PCGPolygonFilterNode.Id,
                PCGNodeCategories.Filter,                data => new PCGPolygonFilterNode(data),
                "多边形筛选",
                "只保留 XZ 平面上落在一个任意多边形里的点——地块不是轴对齐矩形时用它（顶点串 \"x z; x z; …\"）。",
                new[]
                {
                    Slot("Vertices", PCGPolygonFilterNode.DefaultVertices,
                        "多边形顶点，按顺序首尾相连；分号分顶点，每个顶点写 X Z 两个数"),
                    Slot("Invert", false, "改成保留多边形以外的点"),
                });

            // Modifiers.
            PCGNodeRegistry.Register(
                PCGTransformPointsNode.Id,
                PCGNodeCategories.Modifier,
                data => new PCGTransformPointsNode(data),
                "变换点",
                "把每个点整体平移、旋转、缩放一个固定量；可以在世界坐标系里，也可以在每个点自己的坐标系里。",
                new[]
                {
                    Slot("Translation", new Vector3(), "位置增量"),
                    Slot("Rotation Degrees", new Vector3(), "旋转增量（度）"),
                    Slot("Scale Multiplier", Vector3.One, "逐分量缩放"),
                    Slot("Local Space", false, "把增量解释在每个点自己的坐标系里"),
                });

            PCGNodeRegistry.Register(
                PCGRandomOffsetNode.Id,
                PCGNodeCategories.Modifier,
                data => new PCGRandomOffsetNode(data),
                "随机偏移",
                "按点序号取随机流，给每个点的位置、旋转与缩放各加一点抖动。",
                new[]
                {
                    Slot("Min Offset", new Vector3(-20f, 0f, -20f), "位置偏移的下界"),
                    Slot("Max Offset", new Vector3(20f, 0f, 20f), "位置偏移的上界"),
                    Slot("Rotation Jitter Degrees", new Vector3(0f, 180f, 0f), "每个轴上最大转角（绝对值）"),
                    Slot("Scale Min", 1.0f, "缩放倍率的下界"),
                    Slot("Scale Max", 1.0f, "缩放倍率的上界"),
                    Slot("Seed Offset", 0, "把两个偏移节点区分开"),
                });

            PCGNodeRegistry.Register(
                PCGCutNode.Id,
                PCGNodeCategories.Modifier,
                data => new PCGCutNode(data),
                "平面切割",
                "把每个网格按平面切一刀并封上切口（布尔差里能证明的那一块）——削平顶、切斜角。",
                new[]
                {
                    Slot("Plane Point", Vector3.Zero, "刀口经过的一点（形状自己的坐标系）"),
                    Slot("Plane Normal", Vector3.UnitY, "刀口法线；默认切掉它指向的那一侧"),
                    Slot("Invert", false, "改成保留法线那一侧"),
                });

            PCGNodeRegistry.Register(
                PCGClipToConvexNode.Id,
                PCGNodeCategories.Modifier,
                data => new PCGClipToConvexNode(data),
                "裁进凸体",
                "只保留落在凸区域（盒或圆柱）里的那部分几何（A ∩ B）；不是挖孔——挖孔需要面在里/外的分类，仍未做。",
                new[]
                {
                    Slot("Shape", PCGClipToConvexNode.BoxShape, "0 = 盒、1 = 圆柱（棱柱近似）"),
                    Slot("Center", Vector3.Zero, "区域中心（形状自己的坐标系）"),
                    Slot("Size", new Vector3(100.0f, 100.0f, 100.0f), "盒：三条边长；圆柱：X 是直径、Y 是高"),
                    Slot("Segments", 16, "圆柱一圈多少边"),
                });

            // Combinators.
            PCGNodeRegistry.Register(
                PCGMergePointsNode.Id,
                PCGNodeCategories.Combinator,
                data => new PCGMergePointsNode(data),
                "合并点集",
                "把所有接进来的点集首尾相接，同名的属性列合并到一起。");

            PCGNodeRegistry.Register(
                PCGSubgraphNode.Id,
                PCGNodeCategories.Combinator,
                data => new PCGSubgraphNode(data),
                "子图",
                "跑另一张图文件并把它的点传下去，于是一套撒点规则只写一次、需要它的每张地貌图都能引用。",
                new[]
                {
                    Slot("Child Graph", (string)null, "要跑的那张图文件的名字"),
                    Slot("Merge Sinks", false, "把子图的每个输出都合起来，而不是只取第一个"),
                    Slot("Seed Offset", 0, "让共用同一个子图的两个子图节点不撞在一起"),
                });

            PCGNodeRegistry.Register(
                PCGMeshSamplerNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGMeshSamplerNode(data),
                "网格采样器",
                "在一个模型的三角形上撒点；宿主可以是场景里的 Actor，也可以是放在区域中心的资产。",
                new[]
                {
                    Slot("Actor", Guid.Empty, "要采样的场景 Actor（采它的表面）"),
                    Slot("Actor Name", string.Empty, "没填 Actor id 时按名字找"),
                    Slot("Point Count", 500, "放在表面上的点数"),
                    Slot("Seed Offset", 0, "让同一个网格的两个采样器刻意错开"),
                    Slot("Density", 1.0f, "写进每个点的密度"),
                    Slot("Lod Index", 0, "采哪一级 LOD 的三角形"),
                    Slot("Model", Guid.Empty, "没填 Actor 时用的模型资产"),
                });

            PCGNodeRegistry.Register(
                PCGSampleGeometryNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGSampleGeometryNode(data),
                "几何采样器",
                "把**接进来的几何**采成点（每块按面积权重撒点）——与网格采样器的区别是几何来自图里，不是资产。",
                new[]
                {
                    Slot("Points Per Mesh", PCGSampleGeometryNode.DefaultPointsPerMesh, "每块网格上撒几个点"),
                    Slot("Density", 1.0f, "写进每个点的密度"),
                    Slot("Seed Offset", 0, "让两个几何采样器刻意错开"),
                });

            // Spawners.
            PCGNodeRegistry.Register(
                PCGSpawnStaticModelNode.Id,
                PCGNodeCategories.Spawner,
                data => new PCGSpawnStaticModelNode(data),
                "生成静态模型",
                "在每个输入点上摆一个静态网格。",
                new[]
                {
                    Slot("Model", Guid.Empty, "要实例化的模型资产"),
                    Slot("Density Threshold", 0.0f, "密度当生成概率用；0 表示每个点都生成"),
                    Slot("Scale Multiplier", 1.0f, "缩放每个实例"),
                    Slot("Random Yaw", true, "让每个实例绕自己的上轴随机转向"),
                    Slot("Max Actors", PCGSpawnStaticModelNode.DefaultMaxActors, "一次运行的上限"),
                });

            PCGNodeRegistry.Register(
                PCGSpawnFoliageNode.Id,
                PCGNodeCategories.Spawner,
                data => new PCGSpawnFoliageNode(data),
                "生成植被",
                "把点当成实例放进一个 Foliage Actor；密集撒点时走这条便宜的路。",
                new[]
                {
                    Slot("Model", Guid.Empty, "要实例化的模型资产"),
                    Slot("Density Threshold", 0.0f, "密度当放置概率用；0 表示每个点都放"),
                    Slot("Scale Multiplier", 1.0f, "缩放每个实例"),
                    Slot("Random Yaw", true, "让每个实例绕自己的上轴随机转向"),
                    Slot("Max Instances", PCGSpawnFoliageNode.DefaultMaxInstances, "一次运行的上限"),
                    Slot("Cull Distance", 0.0f, "0 表示沿用这种植被的引擎默认值"),
                });

            PCGNodeRegistry.Register(
                PCGSpawnActorNode.Id,
                PCGNodeCategories.Spawner,
                data => new PCGSpawnActorNode(data),
                "生成 Actor",
                "在每个输入点上生成一个 Prefab 的 Actor 层级，适合要挂行为的物件。",
                new[]
                {
                    Slot("Prefab", Guid.Empty, "要实例化的 Prefab 资产"),
                    Slot("Density Threshold", 0.0f, "密度当生成概率用；0 表示每个点都生成"),
                    Slot("Scale Multiplier", 1.0f, "缩放生成的每一份层级"),
                    Slot("Random Yaw", true, "让每个实例绕自己的上轴随机转向"),
                    Slot("Max Actors", PCGSpawnActorNode.DefaultMaxActors, "一次运行的上限"),
                });

            PCGNodeRegistry.Register(
                PCGSpawnDecalNode.Id,
                PCGNodeCategories.Spawner,
                data => new PCGSpawnDecalNode(data),
                "生成贴花",
                "在每个输入点投一张贴花；这类地面细节不该用几何体做。",
                new[]
                {
                    Slot("Material", Guid.Empty, "要投的贴花域材质"),
                    Slot("Density Threshold", 0.0f, "密度当放置概率用；0 表示每个点都放"),
                    Slot("Size", new FlaxEngine.Vector3(100.0f, 100.0f, 100.0f), "贴花在各轴上的尺寸；Y 是投影深度"),
                    Slot("Surface Offset", 5.0f, "沿表面法线抬起一点，免得投影器和地面打架"),
                    Slot("Sort Order", 0, "贴花重叠时这个数大的压在上面"),
                    Slot("Max Decals", PCGSpawnDecalNode.DefaultMaxDecals, "一次运行的上限；贴花没有实例化通路"),
                    Slot("Random Yaw", false, "让投出来的贴图绕表面法线转"),
                });

            PCGNodeRegistry.Register(
                PCGDifferencePointsNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGDifferencePointsNode(data),
                "差集",
                "把站在另一层包围盒里的点去掉——从一片散布里切出一条路的走廊。",
                new[]
                {
                    Slot("Mode", 0, "0 = 按另一层的点包围盒切，1 = 按到它各中心点的距离切"),
                    Slot("Max Distance", 200.0f, "按距离那种模式下的作用范围"),
                    Slot("Padding", 0.0f, "把切除体积涨大；0 表示严丝合缝"),
                    Slot("Plane Only", true, "判断包含关系时忽略高度"),
                });

            PCGNodeRegistry.Register(
                PCGIntersectionPointsNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGIntersectionPointsNode(data),
                "交集",
                "只保留站在另一层包围盒里的点——正是 Difference 的反面。",
                new[]
                {
                    Slot("Mode", 0, "0 = 按另一层的点包围盒保留，1 = 按到它各中心点的距离保留"),
                    Slot("Max Distance", 200.0f, "按距离那种模式下的作用范围"),
                    Slot("Padding", 0.0f, "把这区域涨大，点要站进去才留下"),
                    Slot("Plane Only", true, "判断包含关系时忽略高度"),
                });

            PCGNodeRegistry.Register(
                PCGCopyPointsNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGCopyPointsNode(data),
                "复制点",
                "把每个点复制成一小簇——一丛草、一片石头、拖出一条贴花。",
                new[]
                {
                    Slot("Count", PCGCopyPointsNode.DefaultCount, "每个点复制几份；第一份就是它自己"),
                    Slot("Offset", new FlaxEngine.Vector3(0.0f, 0.0f, 0.0f), "相邻两份之间的偏移"),
                    Slot("Local Space", true, "把偏移放在每个点自己的坐标系里，这样叠起来会贴着表面"),
                    Slot("Rotation Spread", new FlaxEngine.Vector3(0.0f, 0.0f, 0.0f), "每份随机旋转的半幅（度）"),
                    Slot("Scale Min", 1.0f, "复制体可能拿到的最小缩放倍率"),
                    Slot("Scale Max", 1.0f, "复制体可能拿到的最大缩放倍率"),
                    Slot("Seed Offset", 0, "把同一张图里的两个复制节点区分开"),
                });

            PCGNodeRegistry.Register(
                PCGSortPointsNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGSortPointsNode(data),
                "排序",
                "按密度、高度、位置或某一列重排点集——点数封顶时决定谁留下。",
                new[]
                {
                    EnumSlot("Key", 0, "按什么排序", "Density", "Height (Y)", "Horizontal (X)", "Depth (Z)", "Attribute"),
                    Slot("Attribute Name", string.Empty, "Key 选 Attribute 时读这一列"),
                    Slot("Descending", true, "大的排在前面"),
                });

            PCGNodeRegistry.Register(
                PCGBoundsModifierNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGBoundsModifierNode(data),
                "包围盒",
                "设置、放大或缩放每个点的包围盒——剪枝按它算间距，生成器按它落位。",
                new[]
                {
                    EnumSlot("Mode", 0, "这些尺寸怎么用", "Set", "Add", "Scale"),
                    Slot("Extents", new FlaxEngine.Vector3(100.0f, 100.0f, 100.0f), "Set 与 Add 模式下的半尺寸"),
                    Slot("Scale", 1.0f, "Scale 模式下的倍数"),
                });

            PCGNodeRegistry.Register(
                PCGSetAttributeNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGSetAttributeNode(data),
                "写属性",
                "把一个数写进每个点——把算出来的值留下来，而不是每次重算。",
                new[]
                {
                    Slot("Attribute", string.Empty, "要写入的列名"),
                    EnumSlot("Source", 0, "这个值从哪来", "Constant", "Density",
                        "Position X", "Position Y", "Position Z", "Attribute"),
                    Slot("Constant", 1.0f, "Constant 那种来源用的值"),
                    Slot("Scale", 1.0f, "先乘的倍数"),
                    Slot("Bias", 0.0f, "后加的偏移"),
                    Slot("Source Attribute", string.Empty, "Source 选 Attribute 时读这一列"),
                });

            PCGNodeRegistry.Register(
                PCGRemapAttributeNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGRemapAttributeNode(data),
                "重映射",
                "把一段区间映射到另一段——坡度列映射成密度，噪声映射成生长通道。",
                new[]
                {
                    Slot("Source Attribute", string.Empty, "要读的列；留空表示读 $Density"),
                    Slot("Target Attribute", string.Empty, "要写的列；留空表示写 $Density"),
                    Slot("In Min", 0.0f, "输入区间的下界"),
                    Slot("In Max", 1.0f, "输入区间的上界"),
                    Slot("Out Min", 0.0f, "下界映射到的值"),
                    Slot("Out Max", 1.0f, "上界映射到的值"),
                    Slot("Clamp", true, "把结果钳在输出区间里"),
                });

            PCGNodeRegistry.Register(
                PCGSelectPointsNode.Id,
                PCGNodeCategories.ControlFlow,
                data => new PCGSelectPointsNode(data),
                "择一",
                "从两层里放行一层——一个开关、较大的那层、或平均更高的那层。",
                new[]
                {
                    EnumSlot("Rule", 0, "两个输入怎么比较", "Switch", "Average Attribute", "Point Count"),
                    Slot("Use First", true, "开关取哪一个输入"),
                    Slot("Compare Attribute", "growth", "Rule 选 Average Attribute 时对它求平均"),
                    Slot("Prefer Higher", true, "大的赢；关掉就是小的赢"),
                });

            PCGNodeRegistry.Register(
                PCGPointNeighborhoodNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGPointNeighborhoodNode(data),
                "点邻域",
                "数出每个点在半径内的邻居个数，还能顺带在邻居上求某一列的平均。",
                new[]
                {
                    Slot("Radius", 200.0f, "每个点周围的搜索半径"),
                    Slot("Count Attribute", PCGPointNeighborhoodNode.DefaultCountAttribute, "邻居个数写到哪一列"),
                    Slot("Average Source", string.Empty, "在邻居上求平均的列；留空表示不算平均"),
                    Slot("Average Result", "neighborAverage", "邻居平均值写到哪一列"),
                    Slot("Include Self", false, "把点自己也算作自己的邻居"),
                    Slot("Plane Only", true, "搜索时忽略高度"),
                });

            PCGNodeRegistry.Register(
                PCGRelaxPointsNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGRelaxPointsNode(data),
                "松弛点",
                "迭代几次把过挤的点推开，让一片散布看起来是散开的而不是抱团的。",
                new[]
                {
                    Slot("Iterations", 4, $"松弛迭代次数（上限 {PCGRelaxPointsNode.MaxIterations}）"),
                    Slot("Radius", 100.0f, "比这个距离更近的邻居互相推开"),
                    Slot("Strength", 0.5f, "每次算出的推力实际施加的比例"),
                    Slot("Max Step", 25.0f, "一次迭代里点最多移动多远"),
                    Slot("Plane Only", true, "保留高度，地面上的散布才会留在它那个面上"),
                });

            PCGNodeRegistry.Register(
                PCGAttributeMathNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGAttributeMathNode(data),
                "属性运算",
                "把两列合成一列——坡度列乘噪声列得到生成概率。",
                new[]
                {
                    EnumSlot("Operation", 2, "两个数怎么合成",
                        "Add", "Subtract", "Multiply", "Divide", "Min", "Max"),
                    Slot("Left Attribute", string.Empty, "左边的列；留空表示 $Density"),
                    Slot("Right Attribute", string.Empty, "右边的列；留空表示用那个常数"),
                    Slot("Right Constant", 1.0f, "没指定右边的列时用这个值"),
                    Slot("Target Attribute", string.Empty, "要写的列；留空表示 $Density"),
                    Slot("Clamp", true, "把结果钳在 0..1，概率要的正是这个"),
                });

            PCGNodeRegistry.Register(
                PCGAttributeFilterNode.Id,
                PCGNodeCategories.Filter,
                data => new PCGAttributeFilterNode(data),
                "属性筛选",
                "保留指定列落在区间内的点——于是任何算出来的列都能拿来筛。",
                new[]
                {
                    Slot("Attribute", string.Empty, "要判断的列；留空表示 $Density"),
                    Slot("Min", 0.0f, "闭区间的下界"),
                    Slot("Max", 1.0f, "闭区间的上界"),
                    Slot("Invert", false, "改成把落在区间内的点去掉"),
                });

            PCGNodeRegistry.Register(
                PCGSmoothAttributeNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGSmoothAttributeNode(data),
                "平滑属性",
                "在邻域上把一列抹匀，逐点跳变的值就变成渐变的。",
                new[]
                {
                    Slot("Source Attribute", string.Empty, "要抹匀的列；留空表示抹 $Density"),
                    Slot("Target Attribute", string.Empty, "要写的列；留空表示就地写回"),
                    Slot("Radius", 150.0f, "这个距离内的邻居参与"),
                    Slot("Passes", 1, $"平滑迭代次数（上限 {PCGSmoothAttributeNode.MaxIterations}）"),
                    Slot("Plane Only", true, "收集邻居时忽略高度"),
                    Slot("Include Self", true, "让点自己的值也参与它的平均"),
                });

            PCGNodeRegistry.Register(
                PCGCreateSplineNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGCreateSplineNode(data),
                "建样条",
                "把一堆点折成一条曲线——由途经点连成路，由立柱连成栅栏。",
                new[]
                {
                    EnumSlot("Order", 0, "顶点按什么顺序排", "Input Order", "Nearest Neighbour"),
                    Slot("Closed", false, "把路径闭合回第一个顶点"),
                    Slot("Keep Points", false, "顺带把输入的点也传下去"),
                    Slot("Curve Subdivisions", 1, "每段插几个点把路径磨圆；1 表示保持折线"),
                });

            PCGNodeRegistry.Register(
                PCGSampleSplineNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGSampleSplineNode(data),
                "沿样条采样",
                "沿数据集自带的曲线撒点——与 Actor 采样器对应的图内那一个。",
                new[]
                {
                    EnumSlot("Mode", 0, "点数由什么决定", "By Spacing", "By Count"),
                    Slot("Spacing", 200.0f, "按间距那种模式下的弧长间隔"),
                    Slot("Count", 10, "按个数那种模式下每条曲线几个点"),
                    Slot("Arc Jitter", 0.0f, "沿曲线的随机位移（世界单位）"),
                    Slot("Align To Tangent", true, "让每个点朝向曲线切线"),
                    Slot("Height Offset", 0.0f, "把点抬离曲线"),
                    Slot("Scale", 1.0f, "产出的点统一缩放多少倍"),
                    Slot("Seed Offset", 0, "把同一张图里的两个样条采样器区分开"),
                });

            PCGNodeRegistry.Register(
                PCGNormalToDensityNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGNormalToDensityNode(data),
                "朝向转密度",
                "把点的朝向变成一个密度——平地上容易长，悬崖面上不长。",
                new[]
                {
                    Slot("Direction", new FlaxEngine.Vector3(0.0f, 1.0f, 0.0f), "拿点的朝向与哪个方向比"),
                    Slot("Target Attribute", string.Empty, "要写的列；留空表示写 $Density"),
                    Slot("Invert", false, "给背对这个方向的点打分"),
                });

            PCGNodeRegistry.Register(
                PCGSpawnSplineMeshNode.Id,
                PCGNodeCategories.Spawner,
                data => new PCGSpawnSplineMeshNode(data),
                "生成样条网格",
                "用网格拼出一条曲线——栅栏、管道、路缘，一段一块。",
                new[]
                {
                    Slot("Model Path", PCGSpawnSplineMeshNode.DefaultModelPath, "每一段用的模型资产"),
                    Slot("Segment Length", 400.0f, "最长的一段；在它之内各段等长"),
                    Slot("Thickness", 50.0f, "横跨曲线的缩放"),
                    Slot("Max Actors", PCGSpawnSplineMeshNode.DefaultMaxActors, "一次运行的上限"),
                });

            PCGNodeRegistry.Register(
                PCGAttributeTransferNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGAttributeTransferNode(data),
                "属性搬运",
                "按最近位置把另一层的某一列搬过来——借用别人已经算好的值。",
                new[]
                {
                    Slot("Attribute", string.Empty, "要搬的列；留空表示搬 $Density"),
                    Slot("Max Distance", PCGAttributeTransferNode.DefaultMaxDistance, "源点离多远之内还算数"),
                    Slot("Plane Only", true, "找最近源点时忽略高度"),
                    EnumSlot("Match", 0, "采用最近那个源点的多少摆放",
                        "None", "Position", "Transform"),
                });

            PCGNodeRegistry.Register(
                PCGDistanceToPointsNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGDistanceToPointsNode(data),
                "到点的距离",
                "把每个点到另一层最近点的距离写成一列——一个可以再重映射的衰减量。",
                new[]
                {
                    Slot("Attribute", PCGDistanceToPointsNode.DefaultAttribute, "距离写到哪一列；留空表示写 $Density"),
                    Slot("Max Distance", PCGDistanceToPointsNode.DefaultMaxDistance, "作用范围；超过它的距离会被钳到它"),
                    Slot("Plane Only", true, "量距离时忽略高度"),
                });

            PCGNodeRegistry.Register(
                PCGNoiseToAttributeNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGNoiseToAttributeNode(data),
                "噪声写属性",
                "把每个点处的种子噪声值写进一列——不换掉这一层也能有变化。",
                new[]
                {
                    Slot("Attribute", PCGNoiseToAttributeNode.DefaultAttribute, "要写的列；留空表示写 $Density"),
                    EnumSlot("Noise Kind", 1, "采哪一种种子噪声",
                        "Value 2D", "Perlin 2D", "Worley 2D", "Fbm 2D", "Value 3D", "Worley 3D", "Fbm 3D"),
                    Slot("Frequency", 0.01f, "每世界单位几个周期；越大越细碎"),
                    Slot("Octaves", 1, "分形层数；1 表示只有一层"),
                    Slot("Seed Offset", 0, "把同一张图里的两个噪声节点区分开"),
                    Slot("Plane Only", true, "只按 XZ 采样，于是高度不会改变这个值"),
                });

            PCGNodeRegistry.Register(
                PCGChannelToAttributeNode.Id,
                PCGNodeCategories.PointOps,
                data => new PCGChannelToAttributeNode(data),
                "通道拆分",
                "把点自己的缩放、包围盒或朝向读成一个数写进列——于是大小与朝向也能像别的值一样被筛、被重映射。",
                new[]
                {
                    Slot("Attribute", PCGChannelToAttributeNode.DefaultAttribute, "要写的列；留空表示写 $Density"),
                    EnumSlot("Channel", 0, "读点自己的哪一路向量",
                        "Scale", "Bounds", "Forward", "Up", "Right"),
                    EnumSlot("Component", 3, "写这一路的哪一部分",
                        "X", "Y", "Z", "Length"),
                });

            PCGNodeRegistry.Register(
                PCGBranchNode.Id,
                PCGNodeCategories.ControlFlow,
                data => new PCGBranchNode(data),
                "分支",
                "用一个布尔值放行或拦住一层——一个 Volume 能覆盖的暴露参数。",
                new[]
                {
                    Slot("Condition", true, "勾上就把输入放行，取消就什么都不产出"),
                });

            PCGNodeRegistry.Register(
                PCGTextureSamplerNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGTextureSamplerNode(data),
                "纹理采样器",
                "在每个点的位置上读一张纹理写进属性——一张画出来的遮罩驱动撒点。",
                new[]
                {
                    Slot("Texture Path", string.Empty, "纹理的内容路径；留空表示不采样"),
                    Slot("Attribute", PCGTextureSamplerNode.DefaultAttribute, "要写的列；留空表示写 $Density"),
                    EnumSlot("Channel", 4, "读哪一个通道",
                        "Red", "Green", "Blue", "Alpha", "Luminance"),
                    EnumSlot("Address", 0, "超出图像范围时怎么办", "Clamp", "Repeat", "Mirror"),
                    EnumSlot("Filter", 1, "落在纹素之间的采样怎么取值", "Nearest", "Bilinear"),
                    Slot("Tile Size", 1000.0f, "图像铺一份占多少世界单位"),
                    Slot("Invert", false, "把值翻过来，于是白色表示不要"),
                });

            PCGNodeRegistry.Register(
                PCGProjectPointsNode.Id,
                PCGNodeCategories.Modifier,
                data => new PCGProjectPointsNode(data),
                "投影点",
                "把已经存在的点落到地形表面上，点数、顺序与各列都不变。",
                new[]
                {
                    EnumSlot("Sample Mode", 0, "投影走哪条地形查询",
                        "Ray Cast", "Heightfield Interpolated"),
                    Slot("Fallback To Ray Cast", true, "高度场没有标定时改用射线投射"),
                    Slot("Height Offset", 0.0f, "点落地之后再加的抬升"),
                    Slot("Align To Normal", false, "把每个点倾到贴合表面"),
                    Slot("Drop Misses", false, "点下面没有地形的就删掉，而不是留在原处"),
                });

            PCGNodeRegistry.Register(
                PCGScaleByAttributeNode.Id,
                PCGNodeCategories.Modifier,
                data => new PCGScaleByAttributeNode(data),
                "按列缩放",
                "按一列给每个点定大小，于是一片散布不再像同一个模子里倒出来的。",
                new[]
                {
                    Slot("Attribute", string.Empty, "大小取自哪一列；留空表示读 $Density"),
                    Slot("Scale Min", 0.5f, "列为 0 处的倍率"),
                    Slot("Scale Max", 2.0f, "列为 1 处的倍率"),
                    Slot("Invert", false, "列值小反而点大"),
                    Slot("Scale Bounds", true, "把点的包围盒一起放大，剪枝才看得到它"),
                });

            PCGNodeRegistry.Register(
                PCGOffsetByAttributeNode.Id,
                PCGNodeCategories.Modifier,
                data => new PCGOffsetByAttributeNode(data),
                "按列位移",
                "按一列取出的距离，把每个点沿某个轴挪一挪。",
                new[]
                {
                    Slot("Attribute", string.Empty, "距离取自哪一列；留空表示读 $Density"),
                    EnumSlot("Axis", 1, "位移发生在哪个世界轴上", "X", "Y", "Z"),
                    Slot("Offset Min", 0.0f, "列为 0 处的距离（世界单位）"),
                    Slot("Offset Max", 100.0f, "列为 1 处的距离（世界单位）"),
                    Slot("Invert", false, "列值小反而挪得多"),
                });

            PCGNodeRegistry.Register(
                PCGRotateByAttributeNode.Id,
                PCGNodeCategories.Modifier,
                data => new PCGRotateByAttributeNode(data),
                "按列转向",
                "按一列取出的角度，让每个点绕自己的上轴转。",
                new[]
                {
                    Slot("Attribute", string.Empty, "角度取自哪一列；留空表示读 $Density"),
                    Slot("Yaw Min", 0.0f, "列为 0 处的转角（度）"),
                    Slot("Yaw Max", 360.0f, "列为 1 处的转角（度）"),
                    Slot("Invert", false, "列值小反而转得多"),
                });

            PCGNodeRegistry.Register(
                PCGSplitGrammarNode.Id,
                PCGNodeCategories.Structure,
                data => new PCGSplitGrammarNode(data),
                "切分语法",
                "按一张规则表把每颗种子格子切下去，于是一行文字能长出地块、楼层与房间。",
                new[]
                {
                    Slot("Rules", "X:0.5,0.5; Z:0.6,0.4",
                        "一层一条规则：轴:比例,比例；只写一个比例表示切到这一层就停"),
                    Slot("Max Depth", 2, "一个格子最多切几刀，切到数就成叶子"),
                    Slot("Min Cell Size", 0.0f, "比这个更薄的格子就不再切；0 表示不检查"),
                    Slot("Cell Size", new Vector3(1000.0f, 200.0f, 1000.0f),
                        "种子没有可用尺寸时用的格子大小，以及不接输入时用的那个"),
                    Slot("Use Point Scale", false, "改成用每颗种子自己的缩放当格子尺寸"),
                    Slot("Write Depth", true, "把每片叶子所在的深度写进 Depth 列"),
                });

            PCGNodeRegistry.Register(
                PCGWorldRayProjectNode.Id,
                PCGNodeCategories.Modifier,
                data => new PCGWorldRayProjectNode(data),
                "射线投影",
                "把每个点落到场景物理在它下面找到的东西上——网格与物件，不只是地形。",
                new[]
                {
                    Slot("Layer Mask", -1, "射线可以打中的碰撞层；-1 表示所有层"),
                    Slot("Hit Triggers", false, "把触发体也算成命中"),
                    Slot("Direction", new Vector3(0.0f, -1.0f, 0.0f),
                        "射线走的方向；零向量表示竖直向下"),
                    Slot("Max Distance", 10000.0f, "射线走多远还没打中就算落空"),
                    Slot("Height Offset", 0.0f, "沿命中法线抬起一点，让点从表面上方开始"),
                    Slot("Align To Normal", true, "把每个点倾到自己的上方向贴合表面"),
                    Slot("Drop Misses", false, "射线什么都没打中的点就删掉，而不是留在原处"),
                });

            PCGNodeRegistry.Register(
                PCGGrammarNode.Id,
                PCGNodeCategories.Structure,
                data => new PCGGrammarNode(data),
                "符号语法",
                "按产生式把一颗种子符号改写成子符号，于是一块地长出铺面、一个铺面长出房间。",
                new[]
                {
                    Slot("Productions",
                        "Lot = Shop,Shop: X:0.5,0.5; Shop = Room,Room,Back: Z:0.35,0.35,0.3",
                        "符号 = 子符号,子符号: 轴:比例,比例；没有产生式的符号就是叶子"),
                    Slot("Root", "Lot", "每颗种子起始的符号"),
                    Slot("Max Depth", 4, "一个格子最多改写几次，到数就成叶子"),
                    Slot("Min Cell Size", 0.0f, "比这个更薄的格子就不再改写；0 表示不检查"),
                    Slot("Cell Size", new Vector3(1000.0f, 200.0f, 1000.0f),
                        "种子没有可用尺寸时用的格子大小，以及不接输入时用的那个"),
                    Slot("Use Point Scale", false, "改成用每颗种子自己的缩放当格子尺寸"),
                    Slot("Write Names", true, "把每片叶子的符号名写进 SymbolName 列"),
                });

            PCGNodeRegistry.Register(
                PCGActorSamplerNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGActorSamplerNode(data),
                "Actor 采样器",
                "把场景里的 Actor 变成点，并把它各自的名字、标签与层带成列。",
                new[]
                {
                    Slot("Name Contains", string.Empty, "只要名字里含这段文字的 Actor；留空表示全要"),
                    Slot("Tag", string.Empty, "只要标签完全等于这个的 Actor；留空表示不限"),
                    Slot("Layer Name", string.Empty, "只要在这个命名层上的 Actor；留空表示不限"),
                    Slot("Skip Generated", true, "跳过本插件生成的 Actor，免得一次运行把自己喂给自己"),
                    Slot("Max Actors", 0, "匹配到这个数就停；0 表示全要"),
                    Slot("Seed Offset", 0, "把同一张图里的两个 Actor 采样器区分开"),
                    Slot("Write Metadata", true, "把名字、标签与层这三列写出来"),
                });

            PCGNodeRegistry.Register(
                PCGGeometryNode.Id,
                PCGNodeCategories.Structure,
                data => new PCGGeometryNode(data),
                "程序化几何",
                "给每个点一个程序化建出来的形状——方盒、格栅、圆柱或台阶——尺寸取自这个点。",
                new[]
                {
                    EnumSlot("Shape", 0, "每个点建哪种实体",
                        "Box", "Grid", "Cylinder", "Stairs"),
                    Slot("Segments", 8, "圆柱的边数，或格栅每轴的格数"),
                    Slot("Steps", 4, "一段台阶有几级"),
                    Slot("Cell Size", new Vector3(1000.0f, 200.0f, 1000.0f),
                        "不接输入时用的尺寸，以及缩放描述不出形状的点用的那个"),
                    Slot("Use Point Scale", true, "用每个点自己的缩放当形状尺寸"),
                });

            PCGNodeRegistry.Register(
                PCGSpawnGeometryNode.Id,
                PCGNodeCategories.Spawner,
                data => new PCGSpawnGeometryNode(data),
                "几何生成器",
                "把几何节点建出来的网格实体化：默认一个点一个 Actor、同形状共用一份模型；也可合并成一个 Actor。",
                new[]
                {
                    Slot("Max Actors", 1000, "一次运行最多建多少个 Actor（合并模式下是合并多少块）"),
                    Slot("Density Threshold", 0.0f, "密度低于每点一抽的结果就跳过；0 表示全都生成"),
                    Slot("Seed Offset", 0, "把同一张图里的两个几何生成器区分开"),
                    Slot("Merge Into One Actor", false, "把这一层合并成一锅三角面、只建一个 Actor"),
                    Slot("To Foliage Host", false, "改成实例化到一个 Foliage 宿主（同形状共享一个类型，实例不是 Actor）"),
                    Slot("Label", string.Empty, "写进 Actor 名字的层标签"),
                });

            PCGNodeRegistry.Register(
                PCGPathNode.Id,
                PCGNodeCategories.Structure,
                data => new PCGPathNode(data),
                "路径",
                "用地形上的最低代价走法把途经点连起来——是一条路，不是一条直线。",
                new[]
                {
                    Slot("Cell Size", 50.0f, "规划格的边长"),
                    Slot("Slope Limit", 25.0f, "路面不能跨过比这个更陡的地面（度）"),
                    Slot("Slope Cost", 3.0f, "每多一度坡度给格子加多少代价"),
                    Slot("Water Level", -1000000.0f, "低于这个高度的格子不可通行；取得很低就等于没有水"),
                    Slot("Max Detour", 500.0f, "路线最多偏离直线多远"),
                    Slot("Smoothing", 3, "对原始路线做的切角次数"),
                    Slot("Sample Spacing", 100.0f, "沿线产出点的间距"),
                    Slot("Write Slope", true, "把每个点所在处的坡度写进 $Slope"),
                    Slot("Region Min", Vector3.Zero, "路只准走这块矩形（或绕开它）的一个角；与上限写平即不设区域"),
                    Slot("Region Max", Vector3.Zero, "区域矩形的对角"),
                    Slot("Region Invert", false, "勾上表示这块矩形不许经过（路绕开它）；不勾表示只准在矩形里走"),
                });

            PCGNodeRegistry.Register(
                PCGHydrologyNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGHydrologyNode(data),
                "水文",
                "从地形推水：积水的地方成湖，汇流的地方成河。",
                new[]
                {
                    Slot("Cell Size", 50.0f, "规划格的边长"),
                    Slot("River Threshold", 200, "一个格子要汇集多少上游格子才算河"),
                    Slot("Min Lake Cells", 12, "比这个更小的洼地不算湖"),
                    Slot("Min Lake Depth", 0.5f, "比这个更浅的水不算湖"),
                    Slot("Water Offset", 0.0f, "把产出的水面抬高或压低"),
                    Slot("Max Rivers", 64, "最多产出几条河"),
                    Slot("Emit Rivers", true, "产出河的点与它们的中心线"),
                    Slot("Emit Lakes", true, "每个湖产出一个点，带它的水位与面积"),
                    Slot("Write Flow", true, "把每个河点的汇流量写进 Flow 列"),
                    Slot("Region Min", Vector3.Zero, "河只准从这块地里发源、湖只准在这块地里成心；与上限写平即不设区域"),
                    Slot("Region Max", Vector3.Zero, "区域的对角；必须大于下限才生效"),
                    Slot("Region Invert", false, "勾上表示这块矩形不许发源（河与湖都从别处起）"),
                });

            PCGNodeRegistry.Register(
                PCGBiomeNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGBiomeNode(data),
                "地貌",
                "把地形分成平原、丘陵、山地、盆地与水面，作为图能拿来驱动的列。",
                new[]
                {
                    Slot("Cell Size", 50.0f, "不接输入时建的那张图的格子边长"),
                    Slot("Water Level", 0.0f, "高度不高于这个的算水面"),
                    Slot("Water Margin", 0.5f, "水位以上这么多以内的高度仍算水面"),
                    Slot("Relief Radius", 200.0f, "测局部高差的半径"),
                    Slot("Region Radius", 1000.0f, "测周边平均高度的半径"),
                    Slot("Samples", 4, "每个半径上取几个方向：4 或 8"),
                    Slot("Mountain Slope", 25.0f, "坡度不低于这个的算山地（度）"),
                    Slot("Mountain Relief", 120.0f, "高差不低于这个的算山地"),
                    Slot("Hill Slope", 8.0f, "坡度不低于这个的算丘陵（度）"),
                    Slot("Hill Relief", 20.0f, "高差不低于这个的算丘陵"),
                    Slot("Basin Depth", 30.0f, "平坦处比周边平均低多少才算盆地"),
                    Slot("Basin Relief", 15.0f, "盆地底面允许有多大的起伏"),
                    Slot("Write Names", true, "在数值列之外再写一列 BiomeName 文本"),
                    Slot("Write Relief", true, "把量到的局部起伏写进 Relief 列"),
                });

            PCGNodeRegistry.Register(
                PCGCaveNode.Id,
                PCGNodeCategories.Structure,
                data => new PCGCaveNode(data),
                "洞穴",
                "从每个入口往下钻出一片分叉的隧道网，产出中心线的点与朝内的管道网格。",
                new[]
                {
                    Slot("Segment Length", 120.0f, "一段隧道多长"),
                    Slot("Segment Jitter", 0.4f, "段长的变化幅度，取比例"),
                    Slot("Branch Chance", 0.15f, "每一段起一条分叉的概率"),
                    Slot("Max Turn", 35.0f, "每段最多转多少度"),
                    Slot("Max Depth", 600.0f, "隧道最多钻到入口以下多深"),
                    Slot("Segments", 24, "一条隧道最多几段"),
                    Slot("Radius", 60.0f, "隧道的孔径半径"),
                    Slot("Radius Jitter", 0.3f, "半径的变化幅度，取比例"),
                    Slot("Max Tunnels", 12, "整个洞穴最多几条隧道"),
                    Slot("Tube Sides", 8, "产出的管道网格一圈几边"),
                    Slot("Make Meshes", true, "每条隧道产出一份朝内的管道网格"),
                    Slot("Write Depth", true, "把每个点在入口以下的深度写出来"),
                    Slot("Region Min", Vector3.Zero, "洞道只准在这块平面范围内的一个角；与上限相等时不设区域"),
                    Slot("Region Max", Vector3.Zero, "洞道只准在这块平面范围内的对角；必须大于下限才生效"),
                });

            PCGNodeRegistry.Register(
                PCGPointListNode.Id,
                PCGNodeCategories.Sampler,
                data => new PCGPointListNode(data),
                "字面点",
                "在它给定的槽位位置上写点——一个字面点源，不是撒点。",
                new[]
                {
                    Slot("Count", 2, "八个槽位里用几个"),
                    Slot("Point 1", new Vector3(0.0f, 0.0f, 0.0f), "第一个点的位置"),
                    Slot("Point 2", new Vector3(500.0f, 0.0f, 0.0f), "第二个点的位置"),
                    Slot("Point 3", new Vector3(1000.0f, 0.0f, 0.0f), "第三个点的位置"),
                    Slot("Point 4", new Vector3(1500.0f, 0.0f, 0.0f), "第四个点的位置"),
                    Slot("Point 5", new Vector3(2000.0f, 0.0f, 0.0f), "第五个点的位置"),
                    Slot("Point 6", new Vector3(2500.0f, 0.0f, 0.0f), "第六个点的位置"),
                    Slot("Point 7", new Vector3(3000.0f, 0.0f, 0.0f), "第七个点的位置"),
                    Slot("Point 8", new Vector3(3500.0f, 0.0f, 0.0f), "第八个点的位置"),
                    Slot("Scale", 1.0f, "写进每个产出点的缩放"),
                    Slot("Density", 1.0f, "写进每个点的密度；生成器把它当生成概率用"),
                    Slot("Seed Offset", 0, "把同一张图里的两个字面点节点区分开"),
                });

            _registered = true;

            if (log)
                PCGNodeRegistry.LogRegisteredTypes();

            return PCGNodeRegistry.Count;
        }

        /// <summary>
        /// Declares one parameter slot, taking its kind from the value handed over.
        /// </summary>
        /// <remarks>
        /// Deriving the kind from the default rather than naming it twice means the two cannot disagree:
        /// a slot documented as a float but defaulted to a string would otherwise reach the editor as a
        /// widget that writes the wrong type into the graph.
        /// </remarks>
        private static PCGNodeParameterInfo Slot(string name, object defaultValue, string description)
        {
            var info = new PCGNodeParameterInfo(name, KindOf(defaultValue), defaultValue, null, description);

            if (!info.IsCompatible(defaultValue))
                throw new InvalidOperationException($"Parameter '{name}' defaults to {defaultValue} " +
                                                    $"({defaultValue?.GetType().Name}), which is not compatible with kind {info.Kind}.");

            return info;
        }

        /// <summary>Declares an enum slot, whose stored shape is the option index.</summary>
        private static PCGNodeParameterInfo EnumSlot(string name, int defaultIndex, string description, params string[] options)
        {
            if (options == null || options.Length == 0)
                throw new ArgumentException($"Enum parameter '{name}' needs at least one option.", nameof(options));

            if (defaultIndex < 0 || defaultIndex >= options.Length)
                throw new InvalidOperationException($"Enum parameter '{name}' defaults to index {defaultIndex}, " +
                                                    $"outside its {options.Length} option(s).");

            return new PCGNodeParameterInfo(name, PCGParameterType.Enum, defaultIndex, options, description);
        }

        private static PCGParameterType KindOf(object value)
        {
            switch (value)
            {
            case null: return PCGParameterType.String;
            case float _:
            case double _: return PCGParameterType.Float;
            case int _:
            case long _: return PCGParameterType.Int;
            case bool _: return PCGParameterType.Bool;
            case Vector3 _:
            case Float3 _: return PCGParameterType.Vector;
            case Color _: return PCGParameterType.Color;
            case Guid _: return PCGParameterType.Asset;
            case string _: return PCGParameterType.String;
            case Enum _: return PCGParameterType.Enum;
            default: return PCGParameterType.String;
            }
        }

        /// <summary>
        /// Clears the registrations. Must run on a scripts reload, because the stored factories reference
        /// types in the assembly that is being unloaded.
        /// </summary>
        public static void UnregisterAll()
        {
            PCGNodeRegistry.Clear();
            _registered = false;
        }
    }
}
