// PCG 插件 - 曲线网格生成器
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.16。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 用网格拼出一条曲线：栅栏、管道、路缘石——一段一件，每件都转过来顺着那一段。
    /// </summary>
    /// <remarks>
    /// <para>
    /// UE5 是把一份网格沿样条拉伸；本插件的这份 Flax 构建没有可驱动的曲线网格 actor，
    /// 所以这一排改由一件件拼成（<see cref="PCGSplineMeshLayout"/>）。
    /// 对 MMO 真正沿路摆放的那些东西，看上去的结果是一样的，而且它不需要一个未必存在的引擎类型。
    /// </para>
    /// <para>
    /// 曲线来自数据而不是场景：在本节点前面接一个 <see cref="PCGCreateSplineNode"/>，
    /// 这一排就跟着图算出来的东西走——一条路的途经点、一次撒点的高脊线。
    /// 场景里本来就存在的那些曲线，归基于 actor 的曲线采样器管。
    /// </para>
    /// <para>
    /// 归属关系与其他生成器完全一致：每一件都挂在体积之下、按它所在的格子命名，
    /// 所以一次分区的或流式的重生成清掉的恰好是它自己的那几件。
    /// </para>
    /// </remarks>
    public sealed class PCGSpawnSplineMeshNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "SpawnSplineMesh";

        // 参数下标。顺序属于存档图格式，只能追加。
        private const int PModelPath = 0;
        private const int PSegmentLength = 1;
        private const int PThickness = 2;
        private const int PMaxActors = 3;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 4;

        /// <summary>生成件数的默认上限，与其他生成器的取向一致。</summary>
        public const int DefaultMaxActors = 2000;

        /// <summary>
        /// 新节点的槽位默认值。这是一条**路径**（这个槽位历史上的写法），它在工程资产库内可用；
        /// 引擎自带的图元在库外，要写资产 Guid 才取得到（两条都认，见 <see cref="ResolveModel"/>）。
        /// </summary>
        public const string DefaultModelPath = "Engine/Models/Box";

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>每一件所用的 Model 资产路径。</summary>
        public string ModelPath = string.Empty;

        /// <summary>最长件，世界单位；各件会在该上限内做成等长。</summary>
        public float SegmentLength = 400.0f;

        /// <summary>沿曲线横向的缩放，也就是网格自身的 Y 与 Z。</summary>
        public float Thickness = 50.0f;

        /// <summary>单次运行的件数上限，于是一条长曲线不会把场景淹掉。</summary>
        public int MaxActors = DefaultMaxActors;

        /// <summary>上一次运行真正建出的件数，也就是调用方生成后要检查的那个数。</summary>
        public int PlacedCount { get; private set; }

        /// <summary>由序列化的参数创建节点。</summary>
        public PCGSpawnSplineMeshNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            ModelPath = _params.Get(PModelPath, DefaultModelPath) ?? string.Empty;
            SegmentLength = Math.Max(1.0f, _params.Get(PSegmentLength, 400.0f));
            Thickness = Math.Max(0.0f, _params.Get(PThickness, 50.0f));
            MaxActors = Math.Max(0, _params.Get(PMaxActors, DefaultMaxActors));
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Spawn Spline Mesh";

        /// <inheritdoc />
        /// <summary>
        /// 主线程专用：它建 StaticModel Actor，**不是**线程安全的。
        /// </summary>
        /// <remarks>
        /// 与几何生成器同一个理由（见 <see cref="PCGSpawnGeometryNode.IsThreadSafe"/>）：这个标志是"会写场景"
        /// 在节点上的说法，而分区缓存靠它辨认该不该复用格子；标错了会让复用的格子里根本没有物件。
        /// </remarks>
        public override bool IsThreadSafe => false;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Data carrying the curves to build."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same layer, for whatever comes next in the graph."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"model='{ModelPath}', segment={SegmentLength:F0}, thickness={Thickness:F0}, max={MaxActors}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null)
                return PCGData.Empty;
            if (input.Splines == null || input.Splines.Length == 0)
            {
                LastError = "Spawn Spline Mesh found no curves on its input; wire a Create Spline node in front " +
                            "of it.";
                return input;
            }

            if (MaxActors <= 0)
            {
                LastError = "Spawn Spline Mesh has Max Actors at zero, so nothing is placed.";
                return input;
            }

            if (context.DryRun)
                return input;

            if (string.IsNullOrEmpty(ModelPath))
            {
                LastError = "Spawn Spline Mesh needs a model path.";
                return input;
            }

            var model = ResolveModel(ModelPath);
            if (model == null)
            {
                LastError = $"Model '{ModelPath}' is not loaded or is not a Model.";
                return input;
            }

            var pieces = new List<PCGSplineSegment>();
            PCGSplineMeshLayout.Layout(input.Splines, SegmentLength, Thickness, pieces);

            if (pieces.Count == 0)
            {
                LastError = "Spawn Spline Mesh found no curve long enough to place a piece on.";
                return input;
            }

            var parent = context.Source as Actor;
            var label = string.IsNullOrEmpty(model.Path)
                ? "SplineMesh"
                : System.IO.Path.GetFileNameWithoutExtension(model.Path);

            int placed = 0;
            bool capped = false;

            PlacedCount = 0;

            for (int i = 0; i < pieces.Count; i++)
            {
                context.ThrowIfCancelled(i);

                if (placed >= MaxActors)
                {
                    capped = true;
                    break;
                }

                var piece = pieces[i];

                // 分区的运行覆盖的是一块带 padding 的区域：一件站在边界附近的话，
                // 就会被本格和它的邻居各建一遍。
                if (!context.IsInsideClip(piece.Position))
                    continue;

                var actor = new StaticModel
                {
                    Model = model,
                    Name = PCGSpawnNaming.ActorName(label, i, context.LayerIndex,
                        context.ClipCell.HasValue ? context.ClipCell.Value.IndexX : 0,
                        context.ClipCell.HasValue ? context.ClipCell.Value.IndexZ : 0),
                    Transform = new Transform(piece.Position, piece.Rotation, piece.Scale),
                };

                // `TrySpawn` 用**返回值为真**表示成功（与其余生成器、也与自检里
                // `if (!TrySpawn(...)) 失败` 的写法同一个口径）。这里以前写反了：成功的分块被当成失败，
                // 于是一排分块照样建了出来，而 PlacedCount 一直是 0、还报一句"全都落在格子外"——
                // 这条路径此前一直因为模型取不到而被自检跳过，所以直到模型能载进来了才露出来。
                if (PCGActors.TrySpawn(actor, parent))
                {
                    placed++;
                }
                else
                {
                    Debug.LogWarning($"[PCG] {Id}: failed to spawn the piece at {i}.");
                }
            }

            if (capped)
            {
                Debug.LogWarning(
                    $"[PCG] {Id}: stopped at MaxActors={MaxActors} of {pieces.Count} piece(s). " +
                    "Raise Max Actors deliberately, or shorten the curves.");
            }

            if (placed == 0)
            {
                LastError = "Spawn Spline Mesh placed nothing: every piece fell outside the cell being generated.";
                return PCGData.Empty;
            }

            PlacedCount = placed;

            // 曲线原样继续往后传：后面的节点还能采样同一条路径，
            // 而清理是沿体积自己的子节点走的，不动这份数据。
            return input;
        }

        /// <summary>
        /// 解析模型槽位：既认**资产路径**（这个节点历史上的写法），也认**资产 Guid**。
        /// </summary>
        /// <remarks>
        /// 其余生成器存的是 Guid，而 Guid 是唯一对"工程资产库之外"的资产（例如引擎自带的图元）
        /// 也成立的写法——按路径取在那边取不到。所以两种都认，先 Guid 后路径；槽位布局不动，
        /// 已经存进图的路径照样能用。
        /// </remarks>
        private static Model ResolveModel(string pathOrId)
        {
            if (Guid.TryParse(pathOrId, out var modelId))
            {
                var byId = Content.Load<Model>(modelId);
                if (byId != null)
                    return byId;
            }

            return Content.Load<Model>(pathOrId);
        }
    }
}
