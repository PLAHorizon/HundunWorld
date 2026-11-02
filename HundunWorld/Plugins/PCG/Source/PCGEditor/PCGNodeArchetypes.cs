// PCG 插件 - 图编辑器的节点原型
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §6.1。

using System;
using System.Collections.Generic;
using FlaxEditor.Scripting;
using FlaxEditor.Surface;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Graph;

namespace PCGEditor
{
    /// <summary>
    /// 从运行期节点注册表建出 Visject 的 <see cref="NodeArchetype"/> 表。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 注册表是唯一的事实来源：运行期存在的节点类型必须同样出现在
    /// 画布上，名字、分类、引脚、参数槽都一样；这里没有第二份清单
    /// 会过期。引脚取自注册表建的原型实例，参数
    /// 标签与默认值取自声明的模式——编译器拿它校验
    /// 作者写的图，是同一张表。
    /// </para>
    /// <para>
    /// Visject 用一对 <c>ushort</c>（分组 id、类型 id）标识节点类型，而存下来的图按名字
    /// 存类型。所以这个映射按注册表顺序分配、只在编辑器会话里活着——
    /// 资产里从不存那些数字，所以重排注册表不会破坏已存的
    /// 图。注册顺序是确定的（<c>PCGNodeRegistration</c> 显式列出各类型），
    /// 这才让同一会话里的 id 对撤销缓冲与剪贴板载荷是可复现的。
    /// </para>
    /// </remarks>
    internal static class PCGNodeArchetypes
    {
        /// <summary>画布上节点框的宽度，单位是画布单位。</summary>
        private const float BoxWidth = 12.0f;

        /// <summary>引脚之间的竖直间距。</summary>
        private const float BoxStep = 20.0f;

        /// <summary>不算引脚时节点主体的高度。</summary>
        private const float BaseHeight = 32.0f;

        private static readonly Dictionary<ushort, string> NameByTypeID = new Dictionary<ushort, string>();
        private static readonly Dictionary<string, ushort> TypeIDByName = new Dictionary<string, ushort>(StringComparer.Ordinal);
        private static readonly Dictionary<ushort, ushort> GroupByTypeID = new Dictionary<ushort, ushort>();

        private static List<GroupArchetype> _groups;

        /// <summary>每个 PCG 引脚承载的值类型：一组点。</summary>
        /// <remarks>
        /// ScriptType 是结构体，所以这里是普通的只读字段而不是惰性缓存；这个
        /// 类型令牌在本程序集每次静态初始化时只需存在一次。
        /// </remarks>
        private static readonly ScriptType DataPinType = new ScriptType(typeof(PCGData));

        /// <summary>
        /// 按当前注册表内容建（或复用）原型表。
        /// </summary>
        public static List<GroupArchetype> Build()
        {
            if (PCGNodeRegistry.Count == 0)
                PCG.Runtime.PCGNodeRegistration.RegisterAll();

            _groups = new List<GroupArchetype>();
            NameByTypeID.Clear();
            TypeIDByName.Clear();
            GroupByTypeID.Clear();

            // id 来自名字、不来自注册表里的位置：画布存下的载荷里存的是
            // 打包好的那一对，所以任何依赖顺序的做法都会让昨天的图今天解析不出来。
            // 那次代价与“为什么修法用哈希而不是计数器”见 PCGNodeStableIds。
            var nodes = new List<PCGNodeRegistry.PCGNodeTypeInfo>();
            var nodeNames = new List<string>();
            var categoryNames = new List<string>();

            foreach (var info in PCGNodeRegistry.All)
            {
                if (info == null || string.IsNullOrEmpty(info.TypeId))
                    continue;

                nodes.Add(info);
                nodeNames.Add(info.TypeId);

                string category = info.Category ?? "Misc";

                if (!categoryNames.Contains(category))
                    categoryNames.Add(category);
            }

            var typeIds = PCGNodeStableIds.Assign(nodeNames);

            // 分组 id 按**稳定键**算，不按显示名算：载荷里存的是"分组 id : 类型 id"，而显示名是可以翻译、可以改字的，
            // 一改就把每个分组的 id 全换掉、旧存档里的节点随即解析不到（引擎只留一行 "Failed to create … id: 7:29"）。
            var groupKeys = new List<string>(categoryNames.Count);

            foreach (var name in categoryNames)
                groupKeys.Add(PCG.Runtime.PCGNodeCategories.KeyOf(name));

            var groupIds = PCGNodeStableIds.Assign(groupKeys);
            var byCategory = new Dictionary<string, GroupArchetype>(StringComparer.Ordinal);

            for (int n = 0; n < nodes.Count; n++)
            {
                var info = nodes[n];
                string category = info.Category ?? "Misc";

                if (!byCategory.TryGetValue(category, out var group))
                {
                    group = new GroupArchetype
                    {
                        GroupID = groupIds[categoryNames.IndexOf(category)],
                        Name = category,
                        Color = CategoryColor(info.Category),
                        Archetypes = new List<NodeArchetype>(),
                    };
                    byCategory[category] = group;
                    _groups.Add(group);
                }

                var typeId = typeIds[n];
                NameByTypeID[typeId] = info.TypeId;
                TypeIDByName[info.TypeId] = typeId;
                GroupByTypeID[typeId] = group.GroupID;

                var archetype = new NodeArchetype
                {
                    TypeID = typeId,
                    Title = string.IsNullOrEmpty(info.DisplayName) ? info.TypeId : info.DisplayName,
                    SubTitle = info.Category,
                    Description = info.Description,
                    Signature = info.TypeId,
                    Flags = NodeFlags.None,
                    Size = MeasureNode(info),
                    DefaultValues = DefaultsOf(info.Parameters),
                };

                // 委托收到它该实例化的那些原型，所以不需要捕获。
                archetype.Create = (nodeID, context, spawnedArchetype, groupArchetype) =>
                    SpawnNode(nodeID, context, groupArchetype, spawnedArchetype, info);

                ((List<NodeArchetype>)group.Archetypes).Add(archetype);
            }

            return _groups;
        }

        /// <summary>丢掉这张表。必须在脚本重载时跑：那些委托指向本程序集。</summary>
        public static void Clear()
        {
            _groups = null;
            NameByTypeID.Clear();
            TypeIDByName.Clear();
            GroupByTypeID.Clear();
        }

        /// <summary>把图里存的节点类型名解析成会话内的原型对。</summary>
        public static bool TryGetArchetype(string pcgTypeId, out GroupArchetype group, out NodeArchetype node)
        {
            group = null;
            node = null;

            if (_groups == null || string.IsNullOrEmpty(pcgTypeId) ||
                !TypeIDByName.TryGetValue(pcgTypeId, out var typeId))
                return false;

            foreach (var candidate in _groups)
            {
                foreach (var archetype in candidate.Archetypes)
                {
                    if (archetype.TypeID == typeId)
                    {
                        group = candidate;
                        node = archetype;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>把原型对解析回图里存的名字。</summary>
        public static bool TryResolve(ushort typeId, out string pcgTypeId)
        {
            return NameByTypeID.TryGetValue(typeId, out pcgTypeId);
        }

        private static SurfaceNode SpawnNode(
            uint nodeID, VisjectSurfaceContext context, GroupArchetype groupArchetype,
            NodeArchetype nodeArchetype, PCGNodeRegistry.PCGNodeTypeInfo info)
        {
            var node = new SurfaceNode(nodeID, context, nodeArchetype, groupArchetype);

            // 引脚取自原型实例，所以画布与执行器在“有几个引脚、哪个能接多条”
            // 上永远不会不一致。
            var prototype = info.Factory(new PCGNodeRegistry.PCGNodeSerializationData(info.TypeId, null, 0));
            if (prototype == null)
                return node;

            int boxID = 0;

            foreach (var pin in prototype.InputPins)
            {
                node.AddBox(false, boxID++, 0, pin.Name, DataPinType, !pin.AllowMultiple, -1);
            }

            foreach (var pin in prototype.OutputPins)
            {
                node.AddBox(true, boxID++, 0, pin.Name, DataPinType, !pin.AllowMultiple, -1);
            }

            return node;
        }

        private static object[] DefaultsOf(PCGNodeParameterInfo[] parameters)
        {
            if (parameters == null || parameters.Length == 0)
                return Array.Empty<object>();

            var values = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                values[i] = parameters[i]?.Default;

            return values;
        }

        private static Float2 MeasureNode(PCGNodeRegistry.PCGNodeTypeInfo info)
        {
            int pins = 1;

            if (info != null)
            {
                // 不带参数建原型很便宜：引脚声明不依赖取值。
                var prototype = info.Factory(new PCGNodeRegistry.PCGNodeSerializationData(info.TypeId, null, 0));
                if (prototype != null)
                    pins = Math.Max(prototype.InputPins?.Length ?? 0, prototype.OutputPins?.Length ?? 0);
            }

            return new Float2(160.0f, BaseHeight + Math.Max(1, pins) * BoxStep);
        }

        private static Color CategoryColor(string category)
        {
            switch (category)
            {
            case PCG.Runtime.PCGNodeCategories.Sampler: return new Color(0.20f, 0.55f, 0.85f);
            case PCG.Runtime.PCGNodeCategories.Filter: return new Color(0.85f, 0.55f, 0.20f);
            case PCG.Runtime.PCGNodeCategories.Modifier: return new Color(0.45f, 0.75f, 0.35f);
            case PCG.Runtime.PCGNodeCategories.Combinator: return new Color(0.70f, 0.40f, 0.80f);
            case PCG.Runtime.PCGNodeCategories.Spawner: return new Color(0.85f, 0.35f, 0.35f);
            case PCG.Runtime.PCGNodeCategories.Structure: return new Color(0.35f, 0.70f, 0.70f);
            default: return new Color(0.5f, 0.5f, 0.5f);
            }
        }
    }
}
