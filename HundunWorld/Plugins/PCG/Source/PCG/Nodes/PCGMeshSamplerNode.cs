// PCG 插件 - 网格采样节点
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.1。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Data;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 在一份模型的三角形表面上铺开地点生成。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>它是干什么的。</b>长在某个东西<em>上面</em>的那些细节与生长物：摆好的废墟上的
    /// 青苔与常春藤、码头桩柱上的藤壶、塌墙上的碎屑。地形采样器产出不了这些，
    /// 因为那表面不是地面；而手工撒点又没法跟着缩放，因为宿主会移动。
    /// </para>
    /// <para>
    /// <b>找到表面的两条路。</b>给它一个场景 actor（先按 id，再按名字，与样条采样器
    /// 用的是同一套解析顺序），它就在 actor 所在之处采样它的模型，于是挪动
    /// 宿主再重新生成，长出来的东西仍然附着在上面。只给它一个模型资产，它就把那份网格
    /// 采样在生成区域的中心，这一版完全不需要任何场景内容。
    /// </para>
    /// <para>
    /// <b>几何每次运行只下载一次。</b><c>DownloadVertexBuffer</c>/<c>DownloadIndexBuffer</c> 把
    /// 网格的 CPU 侧副本交给脚本；那份副本才是贵的部分，所以它是按节点运行读一次
    /// 而不是每个点读一次，而采样本身留在 <see cref="PCGMeshSurfaceSampling"/> 里，
    /// 在那里不需要资产也能断言。
    /// </para>
    /// </remarks>
    public sealed class PCGMeshSamplerNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "MeshSampler";

        private const int PActorId = 0;
        private const int PActorName = 1;
        private const int PPointCount = 2;
        private const int PSeedOffset = 3;
        private const int PDensity = 4;
        private const int PLodIndex = 5;
        private const int PModelId = 6;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 7;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>要采样其模型的场景 actor；优先于名字。</summary>
        public Guid ActorId;

        /// <summary>没有设 id 时要解析的 actor 名字。</summary>
        public string ActorName;

        /// <summary>没有设 actor 时要采样的模型资产，摆在区域中心。</summary>
        public Guid ModelId;

        /// <summary>要在表面上放多少个点。</summary>
        public int PointCount = 500;

        /// <summary>叠加到运行 seed 上，让同一份网格的两个采样器不至于给出同一批点。</summary>
        public int SeedOffset;

        /// <summary>写进每个点的密度，好让下游的过滤器与生成器能用它。</summary>
        public float Density = 1.0f;

        /// <summary>采样哪一档 LOD；撒点用的表面细节通常要 LOD 0。</summary>
        public int LodIndex;

        /// <summary>由序列化的参数创建节点。</summary>
        public PCGMeshSamplerNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            ActorId = _params.GetAssetId(PActorId);
            ActorName = _params.Get(PActorName, string.Empty);
            PointCount = Mathf.Max(0, _params.Get(PPointCount, 500));
            SeedOffset = _params.Get(PSeedOffset, 0);
            Density = Mathf.Clamp(_params.Get(PDensity, 1.0f), 0.0f, 1.0f);
            LodIndex = Mathf.Max(0, _params.Get(PLodIndex, 0));
            ModelId = _params.GetAssetId(PModelId);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Mesh Sampler";

        /// <inheritdoc />
        public override bool IsThreadSafe => false;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "Points on the model's surface, in world space."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"actor={(string.IsNullOrEmpty(ActorName) ? (ActorId == Guid.Empty ? "none" : ActorId.ToString()) : ActorName)}, " +
            $"count={PointCount}, lod={LodIndex}, density={Density:F2}, seedOffset={SeedOffset}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            LastError = null;

            if (PointCount <= 0)
            {
                LastError = "Point count is zero, so nothing is sampled.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return PCGData.Empty;
            }

            if (!ResolveSurface(context, out var model, out var toWorld, out string resolveError))
            {
                LastError = resolveError;
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return PCGData.Empty;
            }

            if (!TryDownloadGeometry(model, LodIndex, out var positions, out var normals, out var triangles,
                    out string geometryError))
            {
                LastError = geometryError;
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return PCGData.Empty;
            }

            int seed = context.Seed + SeedOffset;
            var data = PCGMeshSurfaceSampling.Generate(
                positions, normals, triangles, toWorld, PointCount, seed, context.CurrentNodeId, Density,
                out string sampleError);

            if (sampleError != null)
            {
                LastError = sampleError;
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return PCGData.Empty;
            }

            // 落到格子外面的点属于相邻格子那一次运行；这条规则和其他节点
            // 用的是同一条，于是一次分区的撒点表现得像一片连续的表面。
            if (context.ClipCell.HasValue && data.Count > 0)
            {
                var points = data.Points;
                var keep = new int[points.Length];
                int count = 0;

                for (int i = 0; i < points.Length; i++)
                {
                    if (!context.IsInsideClip(points[i].Position))
                        continue;

                    keep[count++] = i;
                }

                if (count == 0)
                    return PCGData.Empty;

                if (count < points.Length)
                    data = PCGData.Gather(data, keep, count);
            }

            return data;
        }

        /// <summary>
        /// 找到要采样的模型，以及把它的表面放进世界的那个坐标系。
        /// </summary>
        private bool ResolveSurface(
            PCGExecutionContext context, out Model model, out Transform toWorld, out string error)
        {
            model = null;
            toWorld = Transform.Identity;
            error = null;

            var world = context.World ?? new PCGWorldContext();

            Actor target = null;
            if (ActorId != Guid.Empty)
                target = world.ResolveActor(ActorId) as Actor;

            if (target == null && !string.IsNullOrEmpty(ActorName))
                target = world.ResolveActor(ActorName) as Actor;

            if (target != null)
            {
                model = FindModelOn(target);
                if (model == null)
                {
                    error = $"'{target.Name}' carries no model to sample; " +
                            "point the sampler at a StaticModel, a SkinnedModel, or an actor that has one.";
                    return false;
                }

                toWorld = target.Transform;
                return true;
            }

            if (ModelId == Guid.Empty)
            {
                error = string.IsNullOrEmpty(ActorName)
                    ? "No target actor and no model asset: the mesh sampler has nothing to sample."
                    : $"Actor '{ActorName}' was not found in the scene and no model asset is set as a fallback.";
                return false;
            }

            model = PCGAssetLoading.Resolve<Model>(ModelId);
            if (model == null)
            {
                error = $"Model asset {ModelId} is not loaded or is not a Model.";
                return false;
            }

            // 没有要跟的宿主，于是网格坐在生成区域的正中：采一块岩石模型
            // 来撒碎石，不该要求那块岩石真的存在于场景里。
            //
            // 锚点必须来自体积，而不是来自这次运行的范围。一次分区的运行会
            // 每格执行一遍图，而每格的范围是不同的方块——在那里把模型居中，会让
            // 每个格子都围着它自己的格心长出完整的一片，从而把道具数按格子数成倍放大，
            // 并摆成玩家看得见的方阵。不分区时，这次运行的范围本来就*是*体积的世界盒子，
            // 所以除了分区那一路，这里什么都不改变。
            var bounds = context.SourceBounds ?? context.GenerationBounds;

            var centre = (bounds.Minimum + bounds.Maximum) * 0.5f;
            toWorld = new Transform(centre, Quaternion.Identity, Vector3.One);
            return true;
        }

        private static Model FindModelOn(Actor actor)
        {
            if (actor is StaticModel staticModel && staticModel.Model != null)
                return staticModel.Model;

            // 有父级挂着的宿主是常见情形：一个 prefab 根，网格在某个子节点上。
            var children = actor.GetChildren<StaticModel>();
            if (children != null)
            {
                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i]?.Model != null)
                        return children[i].Model;
                }
            }

            return null;
        }

        /// <summary>
        /// 把某一档 LOD 的三角形拷进普通数组，好让采样不再沾资产类型。
        /// </summary>
        private static bool TryDownloadGeometry(
            Model model, int lodIndex, out Vector3[] positions, out Vector3[] normals, out uint[] triangles,
            out string error)
        {
            positions = Array.Empty<Vector3>();
            normals = Array.Empty<Vector3>();
            triangles = Array.Empty<uint>();
            error = null;

            var lods = model.LODs;
            if (lods == null || lodIndex >= lods.Length)
            {
                error = $"Model has no LOD {lodIndex}.";
                return false;
            }

            var meshes = lods[lodIndex].Meshes;
            if (meshes == null || meshes.Length == 0)
            {
                error = "Model's LOD has no meshes to sample.";
                return false;
            }

            var positionList = new List<Vector3>();
            var normalList = new List<Vector3>();
            var indexList = new List<uint>();

            for (int m = 0; m < meshes.Length; m++)
            {
                var mesh = meshes[m];
                if (mesh == null)
                    continue;

                var vertices = mesh.DownloadVertexBuffer(false);
                var indices = mesh.DownloadIndexBuffer(false);

                if (vertices == null || vertices.Length == 0 || indices == null || indices.Length < 3)
                {
                    // 下载交回的是 CPU 副本；一份从未为读取而上传过的网格
                    // 在这里什么都不给，而静静地跳过它会让整个分布朝
                    // 那些下载成功的东西偏过去。
                    error = $"Mesh {m} of LOD {lodIndex} has no downloadable geometry.";
                    return false;
                }

                int baseVertex = positionList.Count;

                for (int v = 0; v < vertices.Length; v++)
                {
                    var vertex = vertices[v];
                    positionList.Add(new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z));

                    var normal = vertex.Normal;
                    normalList.Add(new Vector3(normal.X, normal.Y, normal.Z));
                }

                for (int i = 0; i < indices.Length; i++)
                    indexList.Add((uint)(baseVertex + (int)indices[i]));
            }

            if (positionList.Count == 0 || indexList.Count < 3)
            {
                error = "The model's geometry is empty.";
                return false;
            }

            positions = positionList.ToArray();
            normals = normalList.ToArray();
            triangles = indexList.ToArray();
            return true;
        }
    }
}
