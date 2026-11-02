// PCG 插件 - 几何生成器（Spawner）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.34。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把几何节点产出的网格实体化，程序化几何才真的出现在场景里。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 几何节点止步于数据：<see cref="PCGData.Meshes"/> 里装着形状，但没有任何东西把它们画出来。这里
    /// 是这条管线的另一端——它把每个点的网格变成虚拟 <c>Model</c>，并在点的位置摆一个
    /// <c>StaticModel</c>。这正是 UE5 几何工作流收尾的那一步，也是决定一张图能产出建筑、而不是只摆放
    /// 美术做好的资产的那一步。
    /// </para>
    /// <para>
    /// <b>网格按形状共享。</b>一千块同样大的楼板是**一个**构建出来的网格和一个
    /// 虚拟模型，不是一千个：点的大小本来就在网格里（构建器把它烘了进去），所以两个
    /// 同样大的点要的是同一份几何，逐点重建等于把这次运行的预算花在重复的
    /// 顶点上。缓存键是形状自己的数字——见 <see cref="KeyOf"/>——于是形状或尺寸不同的点
    /// 各得一份模型，而相等的点共享一份。
    /// </para>
    /// <para>
    /// 生成的 Actor 缩放取 1，因为它的网格已经带了点的大小：再乘一次点的缩放
    /// 就等于乘了两遍，而"楼梯比它本该填满的格子高了一倍"正是
    /// 那种看起来像构建器有 bug 的现象。
    /// </para>
    /// <para>
    /// 网格用 <c>Mesh.UpdateMesh</c> 上传（**不是**手工填 <c>MeshAccessor</c>）：引擎自己的上传会按拿到的流
    /// 构造显式布局，而那个布局恰恰是访问器的逐流 setter 构造不出来的；切线留空由引擎从法线推，
    /// 所以这里不需要凭空造出切线或颜色数据。
    /// </para>
    /// </remarks>
    public sealed class PCGSpawnGeometryNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "SpawnGeometry";

        // 参数下标。顺序是存档格式的一部分；只能往后追加。
        private const int PMaxActors = 0;
        private const int PDensityThreshold = 1;
        private const int PSeedOffset = 2;
        private const int PMergeIntoOneActor = 3;
        private const int PToFoliageHost = 4;
        private const int PLabel = 5;

        /// <summary>这个节点读取的参数个数。</summary>
        public const int ParameterCount = 6;

        /// <summary>一次运行最多构建多少个虚拟模型，无论层里有多少点。</summary>
        public const int MaxModels = 256;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>这个节点一次运行最多创建多少个 Actor。</summary>
        public int MaxActors = 1000;

        /// <summary>密度低于此值的点会被跳过（每个点各抽一次）。零表示全部生成。</summary>
        public float DensityThreshold;

        /// <summary>把同一张图里的两个几何生成器区分开。</summary>
        public int SeedOffset;

        /// <summary>
        /// 把这一层要写的几何**合并成一个 Actor**（默认关：一个点一个 Actor）。
        /// </summary>
        /// <remarks>
        /// 存在的理由是一条引擎事实（用 `Tools/FlaxApiDump` 实测）：`Model.LODs` 与 `ModelLOD.Meshes`
        /// 都是**只读数组**，所以一个虚拟 Model 只有一个可写网格槽——"一个 Actor 画一整片几何"
        /// 只能靠先把它们合并成一锅三角面。代价是每块各自的变换被烘进顶点、材质槽也只剩一个；
        /// 好处是 Actor 数从 N 降到 1（一片石柱群不再撑爆大纲与场景遍历）。
        /// </remarks>
        public bool MergeIntoOneActor;

        /// <summary>
        /// 把这一层的几何**实例化到一个 <c>Foliage</c> 宿主**上（默认关）。
        /// </summary>
        /// <remarks>
        /// 与"合并成一个 Actor"是两种不同的取法，**互斥**：合并把摆放烘进顶点、只留一个网格；实例化保住每块
        /// 自己的变换、同形状共享一个 FoliageType。大片程序化几何（一万根草、一片石林）逐块建 Actor 会把场景
        /// 遍历、大纲与序列化一起拖垮，而**实例不是 Actor**——它们只存在于宿主的缓冲里。
        /// 载体为什么是 Foliage 而不是 `ModelInstanceActor`：后者的 `Entries` 只是逐材质槽的覆盖数组
        /// （见设计文档 §5.1.43 的订正），Flax 里没有"任意网格 N 个实例"那种缓冲。
        /// </remarks>
        public bool ToFoliageHost;

        /// <summary>
        /// 写进 Actor 名字里的**层标签**。存在的理由是可诊断性：程序化几何以前一律叫
        /// `PCG_Gen_Geometry_*`，一座城里上千件与一片林子里几件在大纲里长得一模一样，
        /// 出问题（埋地、错位、某层空转）只能靠猜。带上描述层的名字后，场景本身就是读数。
        /// </summary>
        public string Label = string.Empty;

        /// <summary>上一次运行创建的 Actor 数。</summary>
        public int SpawnedCount { get; private set; }

        /// <summary>上一次运行构建的不同网格数。</summary>
        public int BuiltModelCount { get; private set; }

        /// <summary>上一次运行是否在 <see cref="MaxActors"/> 处停下。</summary>
        public bool LastRunWasCapped { get; private set; }

        /// <summary>
        /// 至今构建的虚拟模型，按形状键控，随节点存活。
        /// </summary>
        /// <remarks>
        /// 持有而不是每次运行重建，且刻意从不释放：这个节点已经摆下的 Actor
        /// 指向这些网格，释放一个就会让上一次运行生成的东西整片消失。流式
        /// 体积会频繁重新生成，那正是逐次重建最伤的时候——也正是
        /// 释放会让世界塞满隐形道具的时候。
        /// </remarks>
        private readonly Dictionary<string, Model> _models = new Dictionary<string, Model>(StringComparer.Ordinal);

        /// <summary>按序列化参数创建节点。</summary>
        public PCGSpawnGeometryNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            MaxActors = Math.Max(0, _params.Get(PMaxActors, 1000));
            DensityThreshold = Mathf.Saturate(_params.Get(PDensityThreshold, 0.0f));
            SeedOffset = _params.Get(PSeedOffset, 0);
            MergeIntoOneActor = _params.Get(PMergeIntoOneActor, false);
            ToFoliageHost = _params.Get(PToFoliageHost, false);
            Label = _params.Get(PLabel, string.Empty) as string ?? string.Empty;
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Spawn Geometry";

        /// <summary>
        /// 主线程专用：它建 Actor（还建虚拟 Model），**不是**线程安全的。
        /// </summary>
        /// <remarks>
        /// 这个标志不只是给并行用的：一件"会写场景"的图必须是可辨认的，否则分区缓存会去复用它的格子——
        /// 而复用意味着那一格的物件这一轮根本没建（实测同一个体积连跑三次的 Actor 数是 120 / 60 / 72）。
        /// 权威判据是 <c>PCGGraphEffects.GraphWritesScene</c>（离屏有 `EverySpawnerIsListed` 盯着漏登记），
        /// 这个标志是同一件事在节点上的说法，两者必须一致。
        /// </remarks>
        public override bool IsThreadSafe => false;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points carrying meshes, from a geometry node."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, so a run can continue past the spawn."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"max {MaxActors} actor(s)" +
            (DensityThreshold > 0.0f ? $", density >= {DensityThreshold:F2}" : string.Empty) +
            (SeedOffset != 0 ? $", seed offset {SeedOffset}" : string.Empty) +
            (MergeIntoOneActor ? ", merged into ONE actor" : string.Empty) +
            (ToFoliageHost ? ", instanced into a Foliage host" : string.Empty);

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            SpawnedCount = 0;
            BuiltModelCount = 0;
            LastRunWasCapped = false;
            LastError = null;

            var input = inputs[0];
            if (input == null)
                return PCGData.Empty;

            bool hasAligned = input.Meshes != null && input.Meshes.Length > 0;
            bool hasLoose = input.Geometry != null && input.Geometry.Length > 0;

            // 这一层没有几何可写：给一个能照着改的错误名（否则看起来就像生成器悄悄什么都不做）。
            // 注意判据是**两种承载都没有**，不是 `Count == 0`——"没有点、只有独立几何"是合法的一种输入，
            // 而且恰恰是这个生成器该写出来的那一种（`Merge` 的可达性最初就是被同一种早退坑掉的）。
            if (!hasAligned && !hasLoose)
            {
                LastError = "This layer carries no geometry. Put a Geometry Primitives node before Spawn Geometry, " +
                            "or use one of the asset spawners instead.";

                // 干跑连日志都不打：`Debug.LogWarning` 也是引擎调用，离屏预览会因此崩掉。诊断不会丢——LastError
                // 由执行器收进本次运行的警告里，那本来就是"该由执行器报出来"的正主。
                if (!context.DryRun)
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");

                return input;
            }

            if (MaxActors <= 0)
            {
                LastError = "MaxActors is zero, so nothing can be spawned.";
                return input;
            }

            // 预览与决策运行把这一层原样交下去：不建模型、不摆 Actor。
            if (context.DryRun)
                return input;

            var parent = ResolveParent(context);

            // 先把"要写出去的几何"收成一张表：每一项都是**自带摆放**的网格，于是"合并"与"逐个写"
            // 后面走的是同一条数据（两种承载在入口处就统一了）。
            var placed = new List<PCGGeometryItem>(Math.Max(4, Math.Min(input.Count, MaxActors)));

            // 三个去向各数一遍 + 落进裁切区那些点的**密度之和**。
            // 抽签是 `Density >= rng.NextFloat()`，所以那个和就是"这一层期望摆出几件"——
            // 报"摆了几件"之前得先说得清"是谁把点吃光的"，否则一句"都落在格外"会把密度问题指错地方。
            int skippedNoMesh = 0;
            int skippedOutsideClip = 0;
            int skippedByDensity = 0;
            float expectedPlacementSum = 0.0f;

            if (hasAligned)
            {
                for (int i = 0; i < input.Count; i++)
                {
                    context.ThrowIfCancelled(i);

                    if (placed.Count >= MaxActors)
                    {
                        LastRunWasCapped = true;
                        break;
                    }

                    var mesh = i < input.Meshes.Length ? input.Meshes[i] : null;

                    if (mesh == null || mesh.VertexCount == 0 || mesh.TriangleCount == 0)
                    {
                        skippedNoMesh++;
                        continue;
                    }

                    var point = input.Points[i];

                    // 分区的运行会在一个加了余量的区域上执行，好让"挤不挤"能跨边界判定；
                    // 把余量里的点也生成出来，就会把它们重复塞进相邻的格子里。
                    if (!context.IsInsideClip(point.Position))
                    {
                        skippedOutsideClip++;
                        continue;
                    }

                    expectedPlacementSum += point.Density;

                    if (DensityThreshold > 0.0f)
                    {
                        var rng = context.GetRng(i + SeedOffset);

                        if (point.Density < rng.NextFloat())
                        {
                            skippedByDensity++;
                            continue;
                        }
                    }

                    // 缩放取 1：网格已经带了点的大小，再乘一次点的缩放就等于乘了两遍。
                    placed.Add(PCGGeometryItem.At(mesh, new Transform(point.Position, point.Rotation, Float3.One)));
                }
            }

            if (hasLoose)
            {
                for (int i = 0; i < input.Geometry.Length; i++)
                {
                    context.ThrowIfCancelled(i);

                    if (placed.Count >= MaxActors)
                    {
                        LastRunWasCapped = true;
                        break;
                    }

                    var geometry = input.Geometry[i];

                    if (!geometry.IsUsable)
                        continue;

                    // 独立几何没有密度可抽（它不属于任何点），但**边界裁剪同样适用**：
                    // 一块摆在邻格里的几何不该被这一格也写一遍。
                    if (!context.IsInsideClip(geometry.Placement.Translation))
                        continue;

                    placed.Add(geometry);
                }
            }

            if (placed.Count == 0)
            {
                // 一句"都落在格外"会把密度问题指到别处去——照着查会白查一轮，
                // 所以这里把三个去向各报一个数，再摆上"这一层本来期望几件"。
                LastError = $"Spawn Geometry placed nothing from {input.Count} item(s): " +
                            $"{skippedOutsideClip} fell outside the region being generated, " +
                            $"{skippedByDensity} were dropped by the density draw (threshold " +
                            $"{DensityThreshold:0.###}), {skippedNoMesh} carried no usable mesh; the " +
                            $"point-aligned pieces together expected {expectedPlacementSum:0.###} prop(s)." +
                            (skippedOutsideClip + skippedByDensity + skippedNoMesh == 0
                                ? " Nothing was skipped here, so the input carried no placeable piece at all."
                                : string.Empty);
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return input;
            }

            // 摆出来了**不等于这一层长出来了**：期望不足一件时这一次抽签站到了作者这边，换个种子整层就没了，
            // 而屏幕上只是"稀"，没人会去查。与静态模型生成器同一条判据、同一种说法。
            if (hasAligned && DensityThreshold > 0.0f && expectedPlacementSum < 1.0f && !LastRunWasCapped)
            {
                LastError = $"Spawn Geometry placed {placed.Count} piece(s) from {input.Count} point(s), but this " +
                            $"layer expected only {expectedPlacementSum:0.###} piece(s) in total, so whether it " +
                            "shows up at all is one coin flip. Raise points / per-block, or raise density.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
            }

            // 两种输出形态互斥：合并把摆放烘进顶点、只留一份网格；实例化保住每块的变换、同形状共享一个类型。
            // 同时置位时不能替作者挑一个（挑错了屏幕上还像"跑通了"），报出来让他自己定。
            if (MergeIntoOneActor && ToFoliageHost)
            {
                LastError = "Spawn Geometry: Merge Into One Actor and To Foliage Host are two different output shapes; " +
                            "turn one of them off.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return input;
            }

            if (ToFoliageHost)
                return SpawnInstanced(input, context, parent, placed);

            if (MergeIntoOneActor)
            {
                // 合并成一锅三角面再建**一个** Actor：`Model.LODs`/`ModelLOD.Meshes` 都是只读数组，
                // 一个虚拟 Model 只有一个可写网格槽，所以"一个 Actor 画一整片"只能这样来。
                // 每块各自的摆放烘进顶点（一个 Actor 只有一个 Transform），材质槽因此也只剩一个。
                var merged = PCGMeshBuilder.MergePlaced(placed);

                if (merged.VertexCount == 0 || merged.TriangleCount == 0)
                {
                    LastError = "Merging the layer's geometry produced an empty mesh.";
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");
                    return input;
                }

                var mergedModel = BuildModel(merged);

                if (mergedModel == null)
                    return input;

                var mergedActor = new StaticModel
                {
                    Model = mergedModel,
                    Name = SpawnName(context, 0),
                    Transform = new Transform(Vector3.Zero, Quaternion.Identity, Float3.One),
                };

                if (!PCGActors.TrySpawn(mergedActor, parent))
                {
                    LastError = "Failed to spawn the merged geometry actor.";
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");
                    return input;
                }

                SpawnedCount = 1;
                BuiltModelCount = _models.Count;
                return input;
            }

            for (int i = 0; i < placed.Count; i++)
            {
                context.ThrowIfCancelled(i);

                var item = placed[i];
                var model = ResolveModel(item.Mesh);

                if (model == null)
                    continue;

                var actor = new StaticModel
                {
                    Model = model,
                    Name = SpawnName(context, i),
                    Transform = item.Placement,
                };

                if (!PCGActors.TrySpawn(actor, parent))
                {
                    Debug.LogWarning($"[PCG] {Id}: failed to spawn actor for piece {i}.");
                    continue;
                }

                SpawnedCount++;
            }

            BuiltModelCount = _models.Count;
            return input;
        }

        /// <summary>这一层写出来的 Actor 名字：带层与格子的记号，于是 <c>Clear</c> 能按格子精确收回。</summary>
        /// <summary>这一层 Actor 的名字前缀：有标签用标签（去掉空格），没标签退回旧的 `Geometry*`。</summary>
        private string NameLabel
        {
            get
            {
                var clean = (Label ?? string.Empty).Replace(" ", string.Empty);

                if (clean.Length > 0)
                    return MergeIntoOneActor ? clean + "Merged" : clean;

                return MergeIntoOneActor ? "GeometryMerged" : "Geometry";
            }
        }

        private string SpawnName(PCGExecutionContext context, int index) =>
            PCGSpawnNaming.ActorName(NameLabel, index,
                context.LayerIndex,
                context.ClipCell.HasValue ? context.ClipCell.Value.IndexX : 0,
                context.ClipCell.HasValue ? context.ClipCell.Value.IndexZ : 0);

        /// <summary>
        /// 实例化输出：一层的几何写进**一个 <c>Foliage</c> 宿主**，同形状共享一个类型、N 个位置就是 N 个实例。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 与"逐块建 Actor"的差别只在规模：实例**不是 Actor**（它们只存在于宿主的缓冲里），所以"一万根草"
        /// 不会变成一万个场景对象。形状不同的块各占一个 FoliageType，于是"两种柱子混在一片"也是对的。
        /// </para>
        /// <para>
        /// 宿主名字沿用植被生成器那一套（`PCG_Foliage_&lt;节点&gt;_l&lt;层&gt;_&lt;格&gt;`）：`PCGVolume.Clear()` 与
        /// 按格清理对它是**同一条规则**，不需要第二套归属机制；节点下标在名字里，所以同一张图里两侧各自的宿主
        /// 不会撞名。
        /// </para>
        /// </remarks>
        private PCGData SpawnInstanced(PCGData input, PCGExecutionContext context, Actor parent,
            List<PCGGeometryItem> placed)
        {
            var foliage = FindOrCreateHost(context, parent);

            if (foliage == null)
                return input;

            // 复用宿主时必须先清掉上一次的实例：否则"重新生成"会变成"再叠一遍"（那是这一层最贵的缺陷）。
            foliage.RemoveAllInstances();

            int instances = 0;

            for (int i = 0; i < placed.Count; i++)
            {
                context.ThrowIfCancelled(i);

                var item = placed[i];
                var model = ResolveModel(item.Mesh);

                if (model == null)
                    continue;

                int type = ResolveTypeIndex(foliage, model);

                if (type < 0)
                    continue;

                // `Random` 是引擎给每个实例的随机输入（着色器里的变化靠它）。这里没有点的密度可借，
                // 就用一条**低差异序列**（黄金比）按块序号摊开：确定性、且不会让一片实例长得像复制粘贴。
                var instance = new FoliageInstance
                {
                    Type = (ushort)type,
                    Transform = item.Placement,
                    Random = (i * 0.6180339887f) % 1.0f,
                };

                foliage.AddInstance(ref instance);
                instances++;
            }

            // 一次，等所有实例都进去之后：逐实例重建会把聚类结构重建实例数那么多次。
            foliage.RebuildClusters();

            SpawnedCount = instances;
            BuiltModelCount = _models.Count;
            return input;
        }

        /// <summary>找（或建）这一层的 <c>Foliage</c> 宿主；名字带节点与格子，所以按名字找得到。</summary>
        private Foliage FindOrCreateHost(PCGExecutionContext context, Actor parent)
        {
            string name = PCGSpawnFoliageNode.ActorNamePrefix + Math.Max(0, context.CurrentNodeId) +
                          $"_l{Math.Max(0, context.LayerIndex)}_" +
                          (context.ClipCell.HasValue
                              ? $"{context.ClipCell.Value.IndexX}x{context.ClipCell.Value.IndexZ}z"
                              : "whole");

            if (parent != null)
            {
                var children = parent.GetChildren<Foliage>();

                if (children != null)
                {
                    for (int i = 0; i < children.Length; i++)
                    {
                        var existing = children[i];

                        if (existing != null && existing.Name == name)
                            return existing;
                    }
                }
            }

            var foliage = new Foliage
            {
                Name = name,

                // 恒等变换是有意的：下面写进去的实例变换是世界坐标，恒等宿主让两者是同一个坐标系。
                Transform = new Transform(Vector3.Zero, Quaternion.Identity, Float3.One),
            };

            if (!PCGActors.TrySpawn(foliage, parent))
            {
                LastError = "Failed to spawn the Foliage host for the geometry.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return null;
            }

            return foliage;
        }

        /// <summary>这一块几何该用哪个 FoliageType：模型相同就复用，不同就加一个。</summary>
        private static int ResolveTypeIndex(Foliage foliage, Model model)
        {
            int count = foliage.FoliageTypesCount;

            for (int i = 0; i < count; i++)
            {
                var type = foliage.GetFoliageType(i);

                if (type?.Model != null && SameAsset(type.Model, model))
                    return i;
            }

            foliage.AddFoliageType(model);

            count = foliage.FoliageTypesCount;
            return count > 0 ? count - 1 : -1;
        }

        /// <summary>两个模型是不是同一个资产（按路径比，不按托管包装对象的引用）。</summary>
        private static bool SameAsset(Asset a, Asset b)
        {
            if (ReferenceEquals(a, b))
                return true;

            return !string.IsNullOrEmpty(a.Path) && string.Equals(a.Path, b.Path, StringComparison.Ordinal);
        }

        /// <summary>
        /// 取（或建）这一块形状对应的虚拟模型：同形状共享一份，键是 <see cref="KeyOf"/>；太多形状时中止并报出。
        /// </summary>
        private Model ResolveModel(in PCGMesh mesh)
        {
            string key = KeyOf(mesh);

            if (_models.TryGetValue(key, out var model))
                return model;

            if (_models.Count >= MaxModels)
            {
                LastError = $"Spawn Geometry stopped at {MaxModels} distinct meshes; the rest were skipped. " +
                            "That many shapes usually means the sizes vary per point.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return null;
            }

            model = BuildModel(mesh);

            if (model == null)
                return null;

            _models.Add(key, model);
            return model;
        }

        /// <summary>
        /// 网格缓存所用的身份：**形状本身**（顶点表的量化内容 + 三角形数与绕序）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 按结构而不是按引用：两个同样大的点带来的是两份相等但不同的网格，它们应当共享一份模型。
        /// 顶点坐标按千分之一（毫米）量化，好让不同代码路径算出的"同一个形状"在最低位上的浮点噪声
        /// 不能把一种形状劈成两种。
        /// </para>
        /// <para>
        /// 实现在纯核 <see cref="PCGMeshBuilder.ShapeKey"/> 里，这里只是它的门面——**只有一份实现**，
        /// 免得将来"合并还是实例化"的判据与缓存的判据各自漂移。旧版本用的是"三角形数 + 包围盒尺寸"，
        /// 那条键对"顶点集合相同而绕序相反"或"包围盒一样、内部安排不同"的形状会撞车，而撞车时
        /// 缓存会把**另一块几何**交给这个点（屏幕上是"某个道具长成了别的样子"）。
        /// </para>
        /// </remarks>
        public static string KeyOf(in PCGMesh mesh) => PCGMeshBuilder.ShapeKey(mesh);

        /// <summary>
        /// 构建一个只装一份网格的虚拟模型。
        /// </summary>
        /// <remarks>
        /// 是虚拟资产而不是存档资产：程序化几何没有文件，而这是引擎自己
        /// 持有生成的网格的方式（它的调试绘制叠加层也这么做）。模型由调用方在
        /// Actor 指向它之后释放，因为已生成的 Actor 会让这份资产保持存活。
        /// </remarks>
        private Model BuildModel(in PCGMesh mesh)
        {
            var model = Content.CreateVirtualAsset<Model>();

            if (model == null)
            {
                LastError = "Could not create a virtual Model for the generated geometry.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return null;
            }

            var lods = model.LODs;

            if (lods == null || lods.Length == 0 || lods[0].Meshes == null || lods[0].Meshes.Length == 0)
            {
                LastError = "The virtual Model came with no LOD to fill.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                FlaxEngine.Object.Destroy(model);
                return null;
            }

            var target = lods[0].Meshes[0];

            if (target == null)
            {
                LastError = "The virtual Model's first mesh is null.";
                FlaxEngine.Object.Destroy(model);
                return null;
            }

            // 用 Mesh.UpdateMesh 而不是手工填 MeshAccessor：引擎自己的上传会为每个顶点
            // 缓冲按它拿到的流构造一个显式布局（Mesh.cpp），而手工构造那个布局正是
            // 访问器的逐流 setter 做不到的事。它的 C++ 侧还会检查
            // 模型是虚拟的——这也是这里为什么走 CreateVirtualAsset——并把缺失的切线
            // 或颜色数组当作"没提供"，转而从法线算出切线基。
            // 公开的那个重载返回 void：失败码在下一层（Internal_UpdateMeshUInt）
            // 被丢掉了，所以被拒绝的上传会留下一份空网格而不是抛异常。
            // 它可能被拒绝的两种情形——非虚拟模型与空缓冲——在这里都
            // 不可能发生，在读到"Actor 生成了但看不见"的报告时，这一点值得知道。
            target.UpdateMesh(
                ToFloat3(mesh.Vertices), mesh.Triangles, ToFloat3(mesh.Normals), null, ToFloat2(mesh.UVs), null);

            return model;
        }

        /// <summary>
        /// 解析出应当拥有这些生成对象的 Actor。
        /// </summary>
        /// <remarks>
        /// 与资产生成器同一条规则：本次运行的来源 Actor 就是提出请求的那个体积，
        /// 挂到它下面，那个体积以后才能找到并清掉恰好属于自己这一次的输出。
        /// </remarks>
        private static Actor ResolveParent(PCGExecutionContext context)
        {
            if (context.Source is Actor actor)
                return actor;

            return null;
        }

        private static Float3[] ToFloat3(Vector3[] values)
        {
            if (values == null || values.Length == 0)
                return null;

            var result = new Float3[values.Length];

            for (int i = 0; i < values.Length; i++)
                result[i] = new Float3(values[i].X, values[i].Y, values[i].Z);

            return result;
        }

        private static Float2[] ToFloat2(Vector2[] values)
        {
            if (values == null || values.Length == 0)
                return null;

            var result = new Float2[values.Length];

            for (int i = 0; i < values.Length; i++)
                result[i] = new Float2(values[i].X, values[i].Y);

            return result;
        }

    }
}
