// PCG 插件 - 点排序（点运算）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.4。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 按点集里的某个值重排，让“最重要的道具”排在前面。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 顺序在本插件里重要，有两个原因：整图的点上限是**边跑边施加**的，所以
    /// 被截断留下来的就是排在**前面**的那些；而各生成器**按顺序**摆点，
    /// 这决定了实例重叠处的绘制顺序。按密度（或高度、或任何采样器写下的
    /// 属性）排序，就是设计师表达“预算不够时哪些道具重要”的方式。
    /// </para>
    /// <para>
    /// 这套排序**天然稳定**——同值时按点的原始下标断——因为不稳定的排序会让同一张图
    /// 在不同运行里造出不同的世界，而那正是本插件花最多力气守住的那一条
    /// 性质。
    /// </para>
    /// </remarks>
    public sealed class PCGSortPointsNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "SortPoints";

        // 参数下标。顺序是已存图格式的一部分；只许追加。
        private const int PKey = 0;
        private const int PAttributeName = 1;
        private const int PDescending = 2;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 3;

        /// <summary>本节点认识的排序键，按枚举暴露的顺序排列。</summary>
        public const string KeyNames = "Density,Height (Y),Horizontal (X),Depth (Z),Attribute";

        /// <summary>点按什么排序。</summary>
        public enum SortKey
        {
            Density = 0,
            Height = 1,
            Horizontal = 2,
            Depth = 3,
            Attribute = 4,
        }

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>按哪一个值排序。</summary>
        public SortKey Key = SortKey.Density;

        /// <summary><see cref="Key"/> 取 <see cref="SortKey.Attribute"/> 时读哪一列。</summary>
        public string AttributeName = string.Empty;

        /// <summary>打开时最大的值排在前面。</summary>
        public bool Descending = true;

        /// <summary>用反序列化出来的参数建节点。</summary>
        public PCGSortPointsNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            int key = _params.Get(PKey, (int)SortKey.Density);
            Key = (SortKey)Math.Min((int)SortKey.Attribute, Math.Max(0, key));
            AttributeName = _params.Get(PAttributeName, string.Empty) ?? string.Empty;
            Descending = _params.Get(PDescending, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Sort Points";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true, tooltip: "Points to reorder."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "The same points, in the chosen order."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"key={Key}{(Key == SortKey.Attribute ? $" '{AttributeName}'" : string.Empty)}, " +
            $"descending={Descending}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count <= 1)
                return input ?? PCGData.Empty;

            var points = input.Points;
            var order = new int[points.Length];
            var keys = new float[points.Length];

            bool attribute = Key == SortKey.Attribute && !string.IsNullOrEmpty(AttributeName);

            if (Key == SortKey.Attribute && !attribute)
            {
                // 走 LastError 报告（执行器会把它收进这次运行的告警），而不是
                // 走 Debug.LogWarning：Flax 的 Debug 要调进原生引擎，所以会打日志的节点根本
                // 没法被离线夹具执行——它会带着 DllNotFoundException: FlaxEngine 直接死。
                // 两种写法消息都能到设计师那儿，而这一种能让这条路径继续被测试覆盖。
                LastError = "Sort by attribute needs an attribute name; the input order is unchanged.";
                return input.Clone();
            }

            for (int i = 0; i < points.Length; i++)
            {
                order[i] = i;

                if (!attribute)
                {
                    keys[i] = NativeKey(points[i]);
                    continue;
                }

                // 先查再读：缺一列属性说明这张图还没把采样器接上，
                // 而不是在生成途中抛的理由。数据集自己的共享属性集也要查，
                // 因为两种约定在各采样器里都在用。
                var attributes = points[i].Attributes ?? input.SharedAttributes;
                keys[i] = attributes != null && attributes.Contains(AttributeName)
                    ? attributes.GetFloat(AttributeName, i)
                    : 0.0f;
            }

            // 用下标断同值，正是它稳定的原因：键相等时保持各层产出的
            // 原顺序，所以同一张图不会在两次运行之间把世界重排一遍。
            Array.Sort(order, (a, b) =>
            {
                int byKey = Descending ? keys[b].CompareTo(keys[a]) : keys[a].CompareTo(keys[b]);
                return byKey != 0 ? byKey : a.CompareTo(b);
            });

            bool unchanged = true;
            for (int i = 0; i < order.Length && unchanged; i++)
                unchanged = order[i] == i;

            return unchanged ? input.Clone() : PCGData.Gather(input, order, order.Length);
        }

        private float NativeKey(in PCGPoint point) => Key switch
        {
            SortKey.Height => point.Position.Y,
            SortKey.Horizontal => point.Position.X,
            SortKey.Depth => point.Position.Z,
            _ => point.Density,
        };
    }
}
