// PCG 插件 - 选择（控制流）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.8。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 从两层里挑一层原样传下去：可以按开关来挑、按点数来挑，或者按哪一层的某个属性
    /// 均值更高来挑。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是**数据**的选择器，不是能省下工作的分支，与 UE5 的差别值得说清楚：
    /// UE5 的图可以跳过它不需要的那个 pin，而本插件的执行器是拉取式的，交给节点的
    /// 输入都是已经算好的。所以两条分支在本节点运行之前就已全部求值。这个节点买到的
    /// 是"图上写明白两个现成的层里用哪一个"——设计者在**一处**拨的 A/B 开关，或是
    /// "哪个采样器产点更多"——而不必把图的下半段
    /// 整个复制两份。
    /// </para>
    /// <para>
    /// 每条规则在"判不出来"时都有定好的答案：属性缺失或恰好打平都走第一个输入，
    /// 并且节点会说明当时是哪一种情形。一个悄悄返回空的选择器，会把图上被关掉的那半
    /// 变成一个没有解释的空世界。
    /// </para>
    /// </remarks>
    public sealed class PCGSelectPointsNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "SelectPoints";

        // 参数下标。顺序是存档图格式的一部分，只能往后追加。
        private const int PMode = 0;
        private const int PUseFirst = 1;
        private const int PCompareAttribute = 2;
        private const int PPreferHigher = 3;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 4;

        /// <summary>两个输入怎么比。</summary>
        public enum SelectionRule
        {
            /// <summary>设计者拨的开关：用第一个输入还是第二个。</summary>
            Switch = 0,

            /// <summary>按某个属性的均值来比，两个采样器之间不必再挂第二个节点。</summary>
            AverageAttribute = 1,

            /// <summary>按每一层各有多少点来比。</summary>
            PointCount = 2,
        }

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>由哪条规则来决定。</summary>
        public SelectionRule Rule = SelectionRule.Switch;

        /// <summary>用到 <see cref="SelectionRule.Switch"/> 时，开关选中哪个输入。</summary>
        public bool UseFirst = true;

        /// <summary>在 <see cref="SelectionRule.AverageAttribute"/> 下被求均值的属性。</summary>
        public string CompareAttribute = "growth";

        /// <summary>置位时两者里大的赢，否则小的赢。</summary>
        public bool PreferHigher = true;

        /// <summary>用序列化参数构造节点。</summary>
        public PCGSelectPointsNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            int rule = _params.Get(PMode, (int)SelectionRule.Switch);
            Rule = (SelectionRule)Math.Min((int)SelectionRule.PointCount, Math.Max(0, rule));
            UseFirst = _params.Get(PUseFirst, true);
            CompareAttribute = _params.Get(PCompareAttribute, "growth") ?? string.Empty;
            PreferHigher = _params.Get(PPreferHigher, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Select";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("A", PCGPinDirection.Input, isRequired: true, tooltip: "First candidate layer."),
            new PCGPinDefinition("B", PCGPinDirection.Input, tooltip: "Second candidate layer."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Whichever layer the rule picked."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() => Rule switch
        {
            SelectionRule.Switch => $"rule=switch, useFirst={UseFirst}",
            SelectionRule.PointCount => $"rule=pointCount, preferHigher={PreferHigher}",
            _ => $"rule=average '{CompareAttribute}', preferHigher={PreferHigher}",
        };

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var first = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            var second = inputs != null && inputs.Length > 1 ? inputs[1] : null;

            var firstEmpty = first == null || first.Count == 0;
            var secondEmpty = second == null || second.Count == 0;

            // 一边是空的不算"决定"：把另一边传下去，而不是造出一个空世界。
            if (firstEmpty && secondEmpty)
                return PCGData.Empty;

            if (firstEmpty)
                return second.Clone();

            if (secondEmpty)
                return first.Clone();

            bool takeFirst = Rule switch
            {
                SelectionRule.PointCount => PreferHigher
                    ? first.Count >= second.Count
                    : first.Count <= second.Count,
                SelectionRule.AverageAttribute => CompareAverages(first, second),
                _ => UseFirst,
            };

            return takeFirst ? first.Clone() : second.Clone();
        }

        private bool CompareAverages(PCGData first, PCGData second)
        {
            if (string.IsNullOrEmpty(CompareAttribute))
            {
                LastError = "Select is set to compare an attribute but no name was given; the first input is used.";
                return true;
            }

            if (!TryAverage(first, CompareAttribute, out float a) || !TryAverage(second, CompareAttribute, out float b))
            {
                LastError = $"Select could not average '{CompareAttribute}' on both inputs; the first input is used.";
                return true;
            }

            // 恰好打平时走第一个输入，于是还没把两条分支分出差别的图
            // 每次跑都产出同一个世界，而不去依赖浮点噪声。
            if (Math.Abs(a - b) < 1e-6f)
                return true;

            return PreferHigher ? a > b : a < b;
        }

        private static bool TryAverage(PCGData data, string name, out float average)
        {
            average = 0.0f;

            var set = data?.SharedAttributes;
            if (set == null || !set.Contains(name) || data.Count == 0)
                return false;

            double total = 0.0;

            for (int i = 0; i < data.Count; i++)
            {
                var pointSet = data.Points[i].Attributes ?? set;
                total += pointSet.Contains(name) ? pointSet.GetFloat(name, i) : 0.0f;
            }

            average = (float)(total / data.Count);
            return true;
        }
    }
}
