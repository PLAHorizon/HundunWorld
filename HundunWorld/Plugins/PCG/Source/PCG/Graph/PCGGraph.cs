// PCG 插件 - 图表数据模型

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Graph
{
    /// <summary>图级暴露参数的种类。</summary>
    public enum PCGParameterType
    {
        /// <summary>浮点值。</summary>
        Float = 0,
        /// <summary>整数值。</summary>
        Int = 1,
        /// <summary>布尔值。</summary>
        Bool = 2,
        /// <summary>向量值。</summary>
        Vector = 3,
        /// <summary>颜色值。</summary>
        Color = 4,
        /// <summary>模型资产引用。存在 <see cref="PCGParameterValue.AssetId"/> 里。</summary>
        Asset = 5,
        /// <summary>自由文本。</summary>
        String = 6,
        /// <summary>
        /// 在一组固定标签里选一个。存成枚举的整数值放在 <see cref="PCGParameterValue.Number"/> 里，
        /// 因为这个值结构体没地方再放一个带类型的引用。
        /// </summary>
        Enum = 7,
    }

    /// <summary>可序列化的参数值，覆盖节点可能暴露的每一种取值类型。</summary>
    [Serializable]
    public struct PCGParameterValue
    {
        /// <summary>数值载荷。Float、Int（原样往返）与 Bool（0 或 1）都用它。</summary>
        public float Number;

        /// <summary>向量载荷。</summary>
        public Vector3 Vector;

        /// <summary>颜色载荷。</summary>
        public Color Color;

        /// <summary>资产载荷，形式是资产 id。</summary>
        public Guid AssetId;

        /// <summary>文本载荷。</summary>
        public string Text;

        /// <summary>创建一个浮点值。</summary>
        public static PCGParameterValue FromFloat(float v) => new PCGParameterValue { Number = v };

        /// <summary>创建一个整数值。</summary>
        public static PCGParameterValue FromInt(int v) => new PCGParameterValue { Number = v };

        /// <summary>创建一个布尔值。</summary>
        public static PCGParameterValue FromBool(bool v) => new PCGParameterValue { Number = v ? 1.0f : 0.0f };

        /// <summary>创建一个 Vector3 值。</summary>
        public static PCGParameterValue FromVector(Vector3 v) => new PCGParameterValue { Vector = v };

        /// <summary>创建一个颜色值。</summary>
        public static PCGParameterValue FromColor(Color v) => new PCGParameterValue { Color = v };

        /// <summary>创建一个资产引用值。</summary>
        public static PCGParameterValue FromAsset(Guid id) => new PCGParameterValue { AssetId = id };

        /// <summary>创建一个文本值。</summary>
        public static PCGParameterValue FromString(string v) => new PCGParameterValue { Text = v };

        /// <summary>
        /// 按 <paramref name="type"/> 描述的形状把这个值读出来。
        /// </summary>
        public object ToObject(PCGParameterType type)
        {
            switch (type)
            {
            case PCGParameterType.Float: return Number;
            case PCGParameterType.Int: return (int)Number;
            case PCGParameterType.Bool: return Number != 0.0f;
            case PCGParameterType.Vector: return Vector;
            case PCGParameterType.Color: return Color;
            case PCGParameterType.Asset: return AssetId;
            case PCGParameterType.String: return Text;
            case PCGParameterType.Enum: return (int)Number;
            default: return Number;
            }
        }

        /// <summary>按 <paramref name="type"/> 把 <paramref name="value"/> 写进这个结构体。</summary>
        /// <returns>值兼容时为 true。</returns>
        public bool TrySet(PCGParameterType type, object value)
        {
            if (value == null)
                return false;

            try
            {
                switch (type)
                {
                case PCGParameterType.Float:
                    Number = Convert.ToSingle(value);
                    return true;
                case PCGParameterType.Int:
                    Number = Convert.ToInt32(value);
                    return true;
                case PCGParameterType.Bool:
                    Number = Convert.ToBoolean(value) ? 1.0f : 0.0f;
                    return true;
                case PCGParameterType.Vector:
                    Vector = (Vector3)value;
                    return true;
                case PCGParameterType.Color:
                    Color = (Color)value;
                    return true;
                case PCGParameterType.Asset:
                    AssetId = (Guid)value;
                    return true;
                case PCGParameterType.String:
                    Text = (string)value;
                    return true;
                case PCGParameterType.Enum:
                    // 枚举控件带的是它的整数值；节点在读的时候再换回去。
                    Number = Convert.ToInt32(value);
                    return true;
                default:
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 声明节点暴露给图的一个参数，于是每个 <c>PCGVolume</c> 都能按实例覆盖它，而不必重编这张图。
    /// </summary>
    /// <remarks>
    /// 覆盖值**有意不进**结构哈希。否则同一张图配不同值的两个体积各自都会触发一次全量重编，复用机制也就白费了。
    /// </remarks>
    [Serializable]
    public sealed class PCGParameterDefinition
    {
        /// <summary>稳定的参数名，在同一张图内唯一。</summary>
        public string Name;

        /// <summary>编辑器里显示的名字。</summary>
        public string DisplayName;

        /// <summary>取值类型。</summary>
        public PCGParameterType Type;

        /// <summary>默认值，没有覆盖时用它。</summary>
        public PCGParameterValue DefaultValue;

        /// <summary>这个参数属于哪个节点。</summary>
        public int NodeIndex;

        /// <summary>在该节点 <see cref="PCGNode.Values"/> 数组里的下标。</summary>
        public int ValueIndex;

        /// <inheritdoc />
        public override string ToString() => $"{Name} ({Type}) -> node {NodeIndex}[{ValueIndex}]";
    }

    /// <summary>图中的某个节点，序列化形态。</summary>
    [Serializable]
    public sealed class PCGNode
    {
        /// <summary>节点类型标识，与某个 <c>PCGNodeBase.TypeId</c> 一致。</summary>
        public string TypeId;

        /// <summary>在图编辑器里的位置。</summary>
        public Vector2 EditorPosition;

        /// <summary>
        /// 节点参数值，按该节点自己的参数顺序索引。
        /// </summary>
        /// <remarks>
        /// 保持朴素 <c>object[]</c> 而不是按名字键控的字典，是因为规划中的图编辑器建在 <c>VisjectSurface</c> 上，
        /// 而它的 <c>SurfaceNode.Values</c> 就是位置数组。现在就照着那个形状走，编辑器落地时就不必做数据迁移。
        /// </remarks>
        public object[] Values = Array.Empty<object>();

        /// <summary>可选的人写注释。</summary>
        public string Comment;

        /// <summary>节点被静音、应当透传或不产出任何东西时为 true。</summary>
        public bool Disabled;

        /// <inheritdoc />
        public override string ToString() => $"{TypeId} @ {EditorPosition}";
    }

    /// <summary>图中的一条边：一个节点的输出引脚下标接到另一个节点的输入引脚下标。</summary>
    [Serializable]
    public sealed class PCGEdge
    {
        /// <summary>上游节点下标。</summary>
        public int OutputNodeIndex;

        /// <summary>上游引脚下标，索引 <c>PCGNodeBase.OutputPins</c>。</summary>
        public int OutputPinIndex;

        /// <summary>下游节点下标。</summary>
        public int InputNodeIndex;

        /// <summary>下游引脚下标，索引 <c>PCGNodeBase.InputPins</c>。</summary>
        public int InputPinIndex;

        /// <inheritdoc />
        public override string ToString() =>
            $"{OutputNodeIndex}[{OutputPinIndex}] -> {InputNodeIndex}[{InputPinIndex}]";
    }

    /// <summary>
    /// 一份地形合成配方：把描述里的 `terrain` 指令搬到图上，由体积在**跑图之前**落地。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么放在图的设置里、而不是做成一个节点：节点是**拉取式**执行——接不进任何汇的节点根本不会被求值，
    /// 而"地形要写在所有采样之前"这一点恰恰不取决于图里的边，取决于是"先造地、再采样"这个顺序。
    /// 放进设置里，体积在跑图前统一落地，顺序天然正确，也不必为了一个副作用在图里找一条接得上汇的边。
    /// </para>
    /// <para>
    /// 高度用**世界单位**，与 <see cref="PCGTerrainSynthesizer"/> 的口径一致：形状的幅度就是地面抬高/压低多少，
    /// 噪声幅度同单位。标定（raw 高度场 → 世界 Y）是写入端那一侧的事。
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class PCGTerrainRecipe
    {
        /// <summary>叠在形状上的噪声幅度（世界单位）；0 表示只要形状。</summary>
        public float NoiseAmplitude = 25.0f;

        /// <summary>噪声空间尺度（越小越平缓）。</summary>
        public float NoiseScale = 900.0f;

        /// <summary>要叠加的形状；空数组表示只要噪声（"一片起伏的平原"）。</summary>
        public PCGTerrainFeature[] Features = Array.Empty<PCGTerrainFeature>();

        /// <summary>
        /// **挖洞**用的形状：落在这些形状半径内的顶点被标成"没有地面"（洞掩码 0）。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="Features"/> 是**两套**形状：`Features` 改高度，`Holes` 删地面。
        /// 同一条描述里可以既有"这里堆一座丘"又有"那里开一个洞"。空数组 = 不挖洞（传 null 给
        /// `SetupPatchHeightMap`，引擎保持它原有的洞不动）。
        /// </remarks>
        public PCGTerrainFeature[] Holes = Array.Empty<PCGTerrainFeature>();
    }

    /// <summary>影响执行的图级设置。</summary>
    [Serializable]
    public sealed class PCGGraphSettings
    {
        /// <summary>体积没有提供种子时用的种子。</summary>
        public int DefaultSeed;

        /// <summary>在编辑器里预览时画调试输出。</summary>
        public bool EnableDebugDraw = true;

        /// <summary>单次图运行产出多少点之后中止。</summary>
        /// <remarks>
        /// 否则一张跑飞的图（例如采样器被误设成一个巨大的点数）会在任何人反应过来之前把内存吃光。
        /// 执行器撞到这条预算就带一句明确的话中止。
        /// </remarks>
        public int MaxPointBudget = 2000000;

        /// <summary>展开后的图最多允许多少个节点，用来兜住递归子图。</summary>
        public int MaxExpandedNodeCount = 4096;

        /// <summary>
        /// 地形合成；null 表示这张图**不造地形**（决定 B：写高度是破坏性的，不做默认行为）。
        /// </summary>
        public PCGTerrainRecipe Terrain;
    }

    /// <summary>
    /// 一张 PCG 图：节点、连线、暴露参数与设置。
    /// </summary>
    /// <remarks>
    /// 这个类型就是一份普通的可序列化状态。"把它作为 Flax 资产持久化"是另一件事；见
    /// docs/PCG_PLUGIN_DESIGN.md §3.3 注 4 里的资产接入说明。
    /// </remarks>
    [Serializable]
    public sealed class PCGGraph
    {
        /// <summary>图名，供日志与生成物件命名使用。</summary>
        public string Name = "PCG Graph";

        /// <summary>图里的节点。连线与参数都是按节点下标引用的。</summary>
        public PCGNode[] Nodes = Array.Empty<PCGNode>();

        /// <summary>图里的连线。</summary>
        public PCGEdge[] Edges = Array.Empty<PCGEdge>();

        /// <summary>暴露给体积的参数。</summary>
        public PCGParameterDefinition[] ExposedParameters = Array.Empty<PCGParameterDefinition>();

        /// <summary>执行设置。</summary>
        public PCGGraphSettings Settings = new PCGGraphSettings();

        /// <summary>
        /// 留给图编辑器画布序列化的不透明载荷。
        /// </summary>
        /// <remarks>
        /// 从一开始就留好，于是编辑器以后要持久化自己的布局状态时不必改文件格式。里面是 Visject 产出的字节的
        /// base64；见 <c>PCGGraphJson.EncodeEditorData</c>。
        /// </remarks>
        public string EditorData;

        /// <summary>
        /// 读取方为了让 <see cref="EditorData"/> 可用而不得不做的事，或 null。
        /// </summary>
        /// <remarks>
        /// **不持久化**——它描述的是"这份实例是怎么读出来的"，而不是"这张图是什么"。它存在的唯一场合是 v1 文件
        /// 里被旧的 UTF-8 存储毁掉的载荷：画布只能按节点数据重建，而设计师需要被告知发生了什么。
        /// </remarks>
        public string EditorDataNote;

        /// <summary>创建一张空图。</summary>
        public PCGGraph()
        {
        }

        /// <summary>在给定的拓扑上创建一张图，并把 null 集合归一化。</summary>
        public PCGGraph(PCGNode[] nodes, PCGEdge[] edges)
        {
            Nodes = nodes ?? Array.Empty<PCGNode>();
            Edges = edges ?? Array.Empty<PCGEdge>();
            Settings = new PCGGraphSettings();
        }

        /// <summary>取节点个数。</summary>
        public int NodeCount => Nodes?.Length ?? 0;

        /// <summary>取连线条数。</summary>
        public int EdgeCount => Edges?.Length ?? 0;
    }
}
