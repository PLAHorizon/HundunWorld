// PCG 插件 - Actor 采样器（采样器类）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.32。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把场景里的 actor 变成一层点，并把每个 actor 的名字、tag 与层作为列带上。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 插件里其余每一个采样器都按某条规则凭空造点：体积、表面、曲线、纹理、
    /// 噪声场。这一个从关卡里已有的东西取点，于是设计者说"在这些标记 actor 周围
    /// 放一个村庄"或"把这两点之间的路拓宽"时，不必为每一种新摆法去改图。
    /// UE5 里同一个形状就是 actor 数据源，而它正是"别再写死"这件事里
    /// 参数覆盖不了的那一半——参数带的是图本来就会用的一个数，而
    /// 一个 actor 带的是只有关卡才知道的一个摆法。
    /// </para>
    /// <para>
    /// 元数据才是让它不只是"一堆变换的散点"的东西：名字、tag、层是编辑器里
    /// 能给 actor 贴标签的三样，三样都写成字符串列，好让图按标记的种类
    /// 分别过滤、分支或生成。过滤放在这里而不是只放在下游，
    /// 因为这个节点多半要的是"那两枚我在意的标记"，不是"所有标记"。
    /// </para>
    /// <para>
    /// 生成的对象默认被跳过。这个体积自己的道具也是 actor，把它们喂回去
    /// 会让下一次运行从上一次运行的产出继续长大——一个自喂的循环，
    /// 在有人挪动一件道具、村庄随之翻倍之前，它看上去像个能用的生成器。
    /// </para>
    /// <para>
    /// actor 清单来自 <see cref="PCGWorldContext.ActorSource"/>，由这次运行装上：图节点
    /// 没有场景就没法枚举关卡，而场景恰是离屏测试没有的东西。没有来源的运行
    /// 会把这件事报出来并且什么都不产出——与其他依赖世界的节点
    /// 遵守同一份约定。
    /// </para>
    /// </remarks>
    public sealed class PCGActorSamplerNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "ActorSampler";

        // 参数下标。顺序属于存档图格式，只能追加。
        private const int PNameContains = 0;
        private const int PTag = 1;
        private const int PLayerName = 2;
        private const int PSkipGenerated = 3;
        private const int PMaxActors = 4;
        private const int PSeedOffset = 5;
        private const int PWriteMetadata = 6;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 7;

        /// <summary>存放来源 actor 名字的列。</summary>
        public const string NameColumn = "ActorName";

        /// <summary>存放来源 actor 的 tag 的列。</summary>
        public const string TagColumn = "ActorTag";

        /// <summary>存放来源 actor 的碰撞层名字的列。</summary>
        public const string LayerColumn = "ActorLayer";

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;
        private readonly List<PCGActorInfo> _scratch = new List<PCGActorInfo>();

        /// <summary>只用名字包含这段的 actor。为空表示所有 actor。</summary>
        public string NameContains = string.Empty;

        /// <summary>只用 tag 等于这个的 actor。为空表示不限 tag。</summary>
        public string Tag = string.Empty;

        /// <summary>只用处于这个具名层的 actor。为空表示不限层。</summary>
        public string LayerName = string.Empty;

        /// <summary>设置时，跳过本插件生成的那些 actor。</summary>
        public bool SkipGenerated = true;

        /// <summary>最多用多少个 actor；零表示全用。</summary>
        public int MaxActors;

        /// <summary>把同一张图里的两个 actor 采样器分开。</summary>
        public int SeedOffset;

        /// <summary>设置时，写出名字、tag、层这三列。</summary>
        public bool WriteMetadata = true;

        /// <summary>上一次运行从多少个 actor 产出了点。</summary>
        public int MatchedCount { get; private set; }

        /// <summary>由序列化的参数创建节点。</summary>
        public PCGActorSamplerNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            NameContains = _params.Get(PNameContains, string.Empty) ?? string.Empty;
            Tag = _params.Get(PTag, string.Empty) ?? string.Empty;
            LayerName = _params.Get(PLayerName, string.Empty) ?? string.Empty;
            SkipGenerated = _params.Get(PSkipGenerated, true);
            MaxActors = Math.Max(0, _params.Get(PMaxActors, 0));
            SeedOffset = _params.Get(PSeedOffset, 0);
            WriteMetadata = _params.Get(PWriteMetadata, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Actor Sampler";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "One point per matching actor, carrying its transform and metadata."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            var filters = new List<string>(3);

            if (!string.IsNullOrEmpty(NameContains))
                filters.Add($"name~{NameContains}");

            if (!string.IsNullOrEmpty(Tag))
                filters.Add($"tag={Tag}");

            if (!string.IsNullOrEmpty(LayerName))
                filters.Add($"layer={LayerName}");

            return (filters.Count == 0 ? "every actor" : string.Join(", ", filters)) +
                   (SkipGenerated ? " (skipping generated)" : string.Empty) +
                   (MaxActors > 0 ? $", max {MaxActors}" : string.Empty);
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            MatchedCount = 0;
            LastError = null;

            var world = context.World;

            if (world == null || !world.HasActorSource)
            {
                LastError = "Actor Sampler found no actor source in the execution context. " +
                            "It produces nothing rather than inventing actors.";
                return PCGData.Empty;
            }

            var actors = new List<PCGActorInfo>(_scratch.Capacity);
            PCGActors.FromSource(world.ActorSource, _scratch, actors);

            if (actors.Count == 0)
                return PCGData.Empty;

            var matched = new List<PCGActorInfo>(actors.Count);

            for (int i = 0; i < actors.Count; i++)
            {
                if (!PCGActors.Matches(actors[i], NameContains, Tag, LayerName, SkipGenerated))
                    continue;

                matched.Add(actors[i]);

                // 上限作用在"匹配上的那些"而不是"读到的那些"："场景里头十个 actor"
                // 不是个有用的上限，因为在意的那十枚可能在清单的任何位置。
                if (MaxActors > 0 && matched.Count >= MaxActors)
                    break;
            }

            if (matched.Count == 0)
            {
                // 不算错误："没有标记带那个 tag"时，空结果就是诚实的回答，而这次运行的
                // 日志已经说了要的是什么。用计数报出来，好让调用方看得见。
                MatchedCount = 0;
                return PCGData.Empty;
            }

            var result = PCGData.Create(matched.Count);
            var points = result.Points;

            string[] names = WriteMetadata ? PCGCellSplit.DeclareTextColumn(result, NameColumn) : null;
            string[] tags = WriteMetadata ? PCGCellSplit.DeclareTextColumn(result, TagColumn) : null;
            string[] layers = WriteMetadata ? PCGCellSplit.DeclareTextColumn(result, LayerColumn) : null;

            for (int i = 0; i < points.Length; i++)
            {
                var actor = matched[i];

                points[i] = PCGPoint.Create(actor.Position, 1.0f, unchecked(SeedOffset + i));
                points[i].Rotation = actor.Rotation;
                points[i].Scale = new Float3(actor.Scale.X, actor.Scale.Y, actor.Scale.Z);

                // 盒子就是 actor 自己的占地：标记的缩放是设计者给它设的那个值，而下游
                // 每一条剪枝决策都测这个盒子，所以它要由缩放搭出来，
                // 而不是留成 Create() 开头那个退化点。
                var half = new Vector3(
                    Math.Max(0.001f, actor.Scale.X) * 0.5f,
                    Math.Max(0.001f, actor.Scale.Y) * 0.5f,
                    Math.Max(0.001f, actor.Scale.Z) * 0.5f);

                points[i].Bounds = PCGPointTransform.MakeBox(
                    actor.Position, PCGPointTransform.EnclosingExtents(half, actor.Rotation));

                if (names != null)
                {
                    names[i] = actor.Name ?? string.Empty;
                    tags[i] = actor.Tag ?? string.Empty;
                    layers[i] = actor.LayerName ?? string.Empty;
                }
            }

            MatchedCount = matched.Count;
            return result;
        }
    }
}
