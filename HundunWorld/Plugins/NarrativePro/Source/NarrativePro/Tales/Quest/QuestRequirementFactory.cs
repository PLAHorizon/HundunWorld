using System;
using System.Collections.Generic;
using System.Text.Json;
using NarrativePro.Core;

namespace NarrativePro.Tales.Quest
{
    /// <summary>
    /// 任务要求（<see cref="QuestRequirement"/>）的名字→类型注册表与构造入口。
    /// <para>
    /// UE5 依据：<c>UQuest::QuestRequirements</c> 是资产里内联的 Instanced 对象数组
    /// （<c>Public/Tales/Quest.h</c>，`EditInlineNew` 实例化），运行时直接拿到实例，
    /// 所以引擎侧从来没有"按名字构造要求"这一步 —— 那是蓝图编译器的活儿。
    /// </para>
    /// <para>
    /// 端口差异（刻意，与 <see cref="NarrativeLogicFactory"/> / <c>ItemFactory</c> 同口径）：
    /// 任务资产是 JSON，要求条目只剩字符串判别字段，故这里按 <c>Type.Name</c> 建表构造，
    /// 并把该 JSON 节点的其余键反射填进实例（<c>maxUnits</c> / <c>secondsLeeway</c> / 三条提示文案等）。
    /// 没有这张表的话，端口里 <c>Tales/Requirements/*.cs</c> 那三个要求类**没有任何作者入口**，
    /// 只能由 C# 代码 <c>AddQuestRequirement</c> 手工挂，等于把上游"在任务图上勾选要求"的能力丢了。
    /// </para>
    /// </summary>
    public static class QuestRequirementFactory
    {
        private static readonly Dictionary<string, Type> _map =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        private static bool _scanned = false;

        /// <summary>与 <c>NarrativeLogicFactory</c> 一致：JSON 键大小写不敏感，允许注释与尾逗号。</summary>
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>按资产里写的类型名构造一个要求实例；名字未注册或节点不可解析时返回 null。</summary>
        public static QuestRequirement Create(string typeId, JsonElement node)
        {
            if (string.IsNullOrEmpty(typeId)) return null;
            EnsureScanned();

            Type type;
            if (!_map.TryGetValue(typeId, out type))
            {
                NarrativeLog.LogWarning(
                    $"QuestRequirementFactory: 未注册的任务要求类型 '{typeId}'（Has it been renamed?），该条要求被跳过");
                return null;
            }

            try
            {
                // JsonSerializer 需要可写的目标类型；这里给的是具体类型，所以要求类必须保留无参构造。
                var instance = JsonSerializer.Deserialize(node.GetRawText(), type, _options) as QuestRequirement;
                if (instance == null)
                {
                    NarrativeLog.LogWarning($"QuestRequirementFactory: 类型 '{typeId}' 反序列化后为空，该条要求被跳过");
                }
                return instance;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"QuestRequirementFactory: 构造要求 '{typeId}' 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>该要求类型名是否可解析（供资产校验与 harness 清单断言使用）。</summary>
        public static bool IsRegistered(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return false;
            EnsureScanned();
            return _map.ContainsKey(typeId);
        }

        /// <summary>已注册类型名快照（诊断用）。</summary>
        public static IEnumerable<string> RegisteredTypes
        {
            get
            {
                EnsureScanned();
                return _map.Keys;
            }
        }

        private static void EnsureScanned()
        {
            if (_scanned) return;
            _scanned = true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(QuestRequirement).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                    if (_map.ContainsKey(type.Name))
                    {
                        NarrativeLog.LogWarning($"QuestRequirementFactory: 要求类型名 '{type.Name}' 重复，保留先注册者");
                        continue;
                    }
                    _map[type.Name] = type;
                }
            }
        }
    }
}
