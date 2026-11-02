// PCG 插件 - 平面切割节点（布尔"差"的第一块）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.44。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把这一层的每个网格**按平面切一刀**，并留一侧（切口会自动封盖，所以结果仍是封闭实体）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是"布尔差"里能证明的那一小块：真正的 <c>A − B</c>（挖方孔）要判"B 的哪些面在 A 里/外"，
    /// 那一步最容易产出一个"看着对、其实自交"的网格，所以本插件先不做它；平面切割没有那个分类问题。
    /// 四条判据（水密 / 体积守恒 / 未跨平面的三角形原样保留 / 判不出来就报错）写在
    /// <see cref="PCGMeshCut"/> 上，并且逐条有离屏断言。
    /// </para>
    /// <para>
    /// <b>刀口在形状自己的坐标系里</b>：网格都是建在自己原点的，所以 <c>y=0</c> 就是"把这个形状切一半"，
    /// 而与它被摆在世界的哪儿、转了多少度无关——"所有柱子都削成同一个斜顶"正是这么写的。
    /// （世界坐标系的刀口是另一件事，留给将来。）
    /// </para>
    /// <para>
    /// **默认切掉法线指向的那一侧**（`Plane Normal` 默认朝上 ⇒ 默认是把顶上削平）；`Invert` 反过来保留
    /// 法线那一侧。某个网格切不动时（不封闭、截面接不成单个环）**保留原样**并把原因记进
    /// <see cref="PCGNodeBase.LastError"/>——整层丢掉比留下一个没削的柱子更糟，而只留一句日志又看不见。
    /// </para>
    /// <para>
    /// 它在**预览与判定运行里照样切**：切割是纯数据变换、不碰场景，而"预览显示没削、烘焙显示削过"是
    /// 同一张图两种样子。`DryRun` 挡的是建 Actor 那一类，不是数据节点。
    /// </para>
    /// </remarks>
    public sealed class PCGCutNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "Cut";

        // 参数下标。顺序是存档格式的一部分；只能往后追加。
        private const int PPlanePoint = 0;
        private const int PPlaneNormal = 1;
        private const int PInvert = 2;

        /// <summary>这个节点读取的参数个数。</summary>
        public const int ParameterCount = 3;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>刀口经过的一点（形状自己的坐标系）。</summary>
        public Vector3 PlanePoint = Vector3.Zero;

        /// <summary>刀口法线；**默认切掉它指向的那一侧**。</summary>
        public Vector3 PlaneNormal = Vector3.UnitY;

        /// <summary>置位时反过来：保留法线指向的那一侧。</summary>
        public bool Invert;

        /// <summary>按序列化参数创建节点。</summary>
        public PCGCutNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            PlanePoint = _params.GetVector3(PPlanePoint, Vector3.Zero);
            PlaneNormal = _params.GetVector3(PPlaneNormal, Vector3.UnitY);
            Invert = _params.Get(PInvert, false);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Cut By Plane";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Geometry to cut."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same layer with every mesh cut and capped."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"plane {PlanePoint} normal {PlaneNormal}, keep {(Invert ? "normal side" : "opposite side")}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];

            if (input == null)
                return PCGData.Empty;

            if (PlaneNormal.LengthSquared <= 1e-12f)
            {
                LastError = "Cut By Plane has a zero-length plane normal, so it cannot cut anything.";
                return input;
            }

            bool hasAligned = input.Meshes != null && input.Meshes.Length > 0;
            bool hasLoose = input.Geometry != null && input.Geometry.Length > 0;

            if (!hasAligned && !hasLoose)
            {
                LastError = "Cut By Plane found no geometry on its input; the layer passes through unchanged.";
                return input;
            }

            // 注意这里**没有** DryRun 早退：切割是**纯数据变换**，不是写场景。早退过一次，结果是
            // "预览显示的是没削的柱子、烘焙显示的是削过的"——同一张图两种样子。DryRun 的契约是
            // "不调引擎"，不是"不干活"；真正该被它挡住的是建 Actor 那一类。
            var result = input.Clone(cloneAttributes: false);
            int refused = 0;
            string firstRefusal = null;

            if (hasAligned)
            {
                var meshes = new PCGMesh[input.Meshes.Length];

                for (int i = 0; i < input.Meshes.Length; i++)
                {
                    context.ThrowIfCancelled(i);

                    meshes[i] = CutOrKeep(input.Meshes[i], ref refused, ref firstRefusal, i);
                }

                result.Meshes = meshes;
            }

            if (hasLoose)
            {
                var geometry = new PCGGeometryItem[input.Geometry.Length];

                for (int i = 0; i < input.Geometry.Length; i++)
                {
                    context.ThrowIfCancelled(i);

                    var item = input.Geometry[i];

                    if (item.IsUsable)
                        item.Mesh = CutOrKeep(item.Mesh, ref refused, ref firstRefusal, i);

                    geometry[i] = item;
                }

                result.Geometry = geometry;
            }

            if (refused > 0)
            {
                // 切的动的都切了；切不动的原样留下并把原因摆出来（悄悄留一个没削的柱子是"看着对"那类缺陷）。
                // **只写 LastError、不打日志**：这个节点是纯算术、能离线执行，而 `Debug.*` 在离屏门里会
                // DllNotFound 直接崩（实测），诊断由执行器收进本次运行的警告里。
                LastError = $"{refused} mesh(es) could not be cut and were kept as they were; " +
                            $"first reason: {firstRefusal}";
            }

            return result;
        }

        /// <summary>切一刀；判不出来时原样返回并累计原因。</summary>
        private PCGMesh CutOrKeep(PCGMesh mesh, ref int refused, ref string firstRefusal, int index)
        {
            if (mesh == null || mesh.VertexCount == 0)
                return mesh;

            // 默认切掉法线那一侧 ⇒ 保留的是法线的**反**侧（`keepPositiveSide` 是"保留法线侧"）。
            if (PCGMeshCut.TryCutByPlane(mesh, PlanePoint, PlaneNormal, Invert, out var cut, out string error))
                return cut;

            refused++;

            if (firstRefusal == null)
                firstRefusal = $"mesh {index}: {error}";

            return mesh;
        }
    }
}
