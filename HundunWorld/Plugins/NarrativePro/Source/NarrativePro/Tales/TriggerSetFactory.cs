using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.Tales.Events;

namespace NarrativePro.Tales
{
    /// <summary>
    /// TriggerSet / TaggedDialogueSet / TriggerSchedule 的 JSON 工厂。
    /// <para>
    /// 与 <see cref="NarrativeLogicFactory"/> 同因：System.Text.Json 无法多态实例化抽象基类
    /// <see cref="NarrativeTrigger"/> 的元素，因此按 JSON 里的 <c>"type"</c> 判别字段查注册表构造，
    /// 再大小写不敏感地把标量字段反射赋值（本项目已两次踩 camelCase JSON vs 默认绑定失败的坑，
    /// 这里全部手工按忽略大小写取键，不用 <c>JsonSerializer.Deserialize&lt;TriggerSet&gt;</c>）。
    /// 事件列表复用 <see cref="NarrativeLogicFactory.CreateEvents"/>（其 ConvertValue 支持
    /// 字符串枚举名，故 <c>"eventRuntime": "End"</c> 可绑定）。
    /// </para>
    /// </summary>
    public static class TriggerSetFactory
    {
        private static readonly Dictionary<string, Func<NarrativeTrigger>> TriggerTypes =
            new Dictionary<string, Func<NarrativeTrigger>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>JSON 里的触发器类型判别字段候选名（与 NarrativeLogicFactory.TypeKeys 口径一致）。</summary>
        private static readonly string[] TypeKeys =
        {
            "type", "$type", "triggerType", "logicType", "class", "name"
        };

        private const string TriggerSuffix = "narrativetrigger";

        static TriggerSetFactory()
        {
            RegisterTriggerType("TimeOfDay", () => new TimeOfDayNarrativeTrigger());
        }

        /// <summary>登记一种可进 TriggerSet JSON 的具体触发器类型（对游戏代码开放扩展口）。</summary>
        public static void RegisterTriggerType(string typeId, Func<NarrativeTrigger> factory)
        {
            if (string.IsNullOrWhiteSpace(typeId) || factory == null) return;
            if (!TriggerTypes.ContainsKey(typeId)) TriggerTypes.Add(typeId, factory);
            string normalized = Normalize(typeId);
            if (!TriggerTypes.ContainsKey(normalized)) TriggerTypes.Add(normalized, factory);
        }

        // ===================================================================
        // JSON 形态判别
        // ===================================================================

        public static bool IsTriggerSetJson(JsonElement root) =>
            root.ValueKind == JsonValueKind.Object && HasProp(root, "triggers");

        public static bool IsTaggedDialogueSetJson(JsonElement root) =>
            root.ValueKind == JsonValueKind.Object && HasProp(root, "taggedDialogues");

        public static bool IsTriggerScheduleJson(JsonElement root) =>
            root.ValueKind == JsonValueKind.Object && HasProp(root, "triggerSetPaths");

        // ===================================================================
        // 解析入口
        // ===================================================================

        /// <summary>从磁盘 JSON 文件解析 TriggerSet（path 须为已解析的绝对路径）。</summary>
        public static TriggerSet LoadTriggerSetFile(string resolvedPath)
        {
            using (var doc = ParseFile(resolvedPath))
            {
                return CreateTriggerSet(doc.RootElement, resolvedPath);
            }
        }

        /// <summary>从磁盘 JSON 文件解析 TaggedDialogueSet（path 须为已解析的绝对路径）。</summary>
        public static TaggedDialogueSet LoadTaggedDialogueSetFile(string resolvedPath)
        {
            using (var doc = ParseFile(resolvedPath))
            {
                return CreateTaggedDialogueSet(doc.RootElement, resolvedPath);
            }
        }

        private static JsonDocument ParseFile(string resolvedPath)
        {
            string json = File.ReadAllText(resolvedPath);
            return JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }

        /// <summary>由 JSON 根对象构造 TriggerSet（含多态触发器与其 TriggerEvents）。</summary>
        public static TriggerSet CreateTriggerSet(JsonElement root, string source)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;

            var set = new TriggerSet();
            set.Name = ReadString(root, "name");
            set.AppliesToNpcIds = ReadStringList(root, "appliestoNpcids");

            JsonElement triggers;
            if (!TryGetProp(root, "triggers", out triggers) || triggers.ValueKind != JsonValueKind.Array)
            {
                NarrativeLog.LogError($"TriggerSet JSON 缺少数组字段 \"triggers\"（{source}）");
                return null;
            }

            foreach (var el in triggers.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                string typeId = ReadFirstString(el, TypeKeys);
                Func<NarrativeTrigger> factory;
                if (string.IsNullOrEmpty(typeId) || !TryResolve(typeId, out factory))
                {
                    NarrativeLog.LogError(
                        $"TriggerSet 构造失败：未注册的触发器类型 '{typeId}'（Has it been renamed?）({source})");
                    continue;
                }

                var trigger = factory();
                if (trigger == null) continue;

                PopulateTriggerMembers(trigger, el);

                JsonElement events;
                if (TryGetProp(el, "triggerEvents", out events) && events.ValueKind == JsonValueKind.Array)
                {
                    trigger.TriggerEvents = NarrativeLogicFactory.CreateEvents(events.EnumerateArray());
                }

                set.Triggers.Add(trigger);
            }

            return set;
        }

        /// <summary>由 JSON 根对象构造 TaggedDialogueSet（对应 UE5 UTaggedDialogueSet 资产）。</summary>
        public static TaggedDialogueSet CreateTaggedDialogueSet(JsonElement root, string source)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;

            var set = new TaggedDialogueSet();
            set.AppliesToNpcIds = ReadStringList(root, "appliesToNpcIds") ?? new List<string>();
            JsonElement entries;
            if (!TryGetProp(root, "taggedDialogues", out entries) || entries.ValueKind != JsonValueKind.Array)
            {
                NarrativeLog.LogError($"TaggedDialogueSet JSON 缺少数组字段 \"taggedDialogues\"（{source}）");
                return null;
            }

            foreach (var el in entries.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                // UE5 FTaggedDialogue 构造函数默认 Cooldown=30 / MaxDistance=5000，
                // 与 Tales/TaggedDialogueSet.cs 的 C# 构造函数默认一致；JSON 缺省时保留默认。
                var td = new TaggedDialogue();

                string tag = ReadString(el, "tag");
                td.Tag = new GameplayTag(tag ?? "");
                if (!td.Tag.IsValid())
                {
                    NarrativeLog.LogWarning($"TaggedDialogueSet 条目缺少 \"tag\"，已忽略（{source}）");
                    continue;
                }

                td.DialoguePath = ReadString(el, "dialoguePath") ?? "";
                JsonElement num;
                if (TryGetProp(el, "cooldown", out num) && num.ValueKind == JsonValueKind.Number)
                    td.Cooldown = (float)num.GetDouble();
                if (TryGetProp(el, "maxDistance", out num) && num.ValueKind == JsonValueKind.Number)
                    td.MaxDistance = (float)num.GetDouble();

                var required = ReadStringList(el, "requiredTags");
                if (required != null && required.Count > 0) td.RequiredTags = new GameplayTagContainer(required);
                var blocked = ReadStringList(el, "blockedTags");
                if (blocked != null && blocked.Count > 0) td.BlockedTags = new GameplayTagContainer(blocked);

                set.TaggedDialogues.Add(td);
            }

            return set;
        }

        /// <summary>由 JSON 根对象构造 TriggerSchedule 并加载其引用的全部 TriggerSet。</summary>
        public static TriggerSchedule CreateTriggerSchedule(JsonElement root, string source)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;

            var schedule = new TriggerSchedule();
            schedule.TriggerSetPaths = ReadStringList(root, "triggerSetPaths") ?? new List<string>();
            schedule.LoadAll();
            return schedule;
        }

        // ===================================================================
        // 内部工具
        // ===================================================================

        private static bool TryResolve(string typeId, out Func<NarrativeTrigger> factory)
        {
            factory = null;
            if (string.IsNullOrWhiteSpace(typeId)) return false;
            if (TriggerTypes.TryGetValue(typeId, out factory)) return true;

            string normalized = Normalize(typeId);
            if (TriggerTypes.TryGetValue(normalized, out factory)) return true;

            // 带/不带 NarrativeTrigger 后缀视作同一键（与 LogicRegistry.TryResolve 口径一致）
            if (normalized.Length > TriggerSuffix.Length &&
                normalized.EndsWith(TriggerSuffix, StringComparison.OrdinalIgnoreCase))
            {
                string bare = normalized.Substring(0, normalized.Length - TriggerSuffix.Length);
                if (bare.Length > 0 && TriggerTypes.TryGetValue(bare, out factory)) return true;
            }
            else if (TriggerTypes.TryGetValue(normalized + TriggerSuffix, out factory))
            {
                return true;
            }
            return false;
        }

        private static string Normalize(string key)
        {
            string trimmed = key.Trim();
            int lastDot = trimmed.LastIndexOf('.');
            return lastDot >= 0 && lastDot < trimmed.Length - 1 ? trimmed.Substring(lastDot + 1) : trimmed;
        }

        /// <summary>把 JSON 对象上的标量成员反射赋给触发器实例（跳过判别字段与 triggerEvents）。</summary>
        private static void PopulateTriggerMembers(NarrativeTrigger trigger, JsonElement el)
        {
            var type = trigger.GetType();
            foreach (var prop in el.EnumerateObject())
            {
                if (IsTypeKey(prop.Name)) continue;
                if (string.Equals(prop.Name, "triggerEvents", StringComparison.OrdinalIgnoreCase)) continue;

                MemberInfo member = FindMember(type, prop.Name);
                if (member == null) continue; // 未建模字段静默忽略（与 NarrativeLogicFactory 口径一致）

                Type memberType = member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;
                object value = ConvertScalar(prop.Value, memberType);
                if (value == null) continue;

                try
                {
                    if (member is FieldInfo fi) fi.SetValue(trigger, value);
                    else ((PropertyInfo)member).SetValue(trigger, value);
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"{type.Name}.{prop.Name} 赋值失败: {ex.Message}");
                }
            }
        }

        private static MemberInfo FindMember(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(flags))
                    if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
                foreach (var p in t.GetProperties(flags))
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        private static object ConvertScalar(JsonElement value, Type target)
        {
            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined) return null;
            target = Nullable.GetUnderlyingType(target) ?? target;

            if (target == typeof(string))
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();

            if (target == typeof(bool))
            {
                switch (value.ValueKind)
                {
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False: return false;
                    case JsonValueKind.Number: return value.GetDouble() != 0d;
                    case JsonValueKind.String:
                        bool parsed;
                        if (bool.TryParse(value.GetString(), out parsed)) return parsed;
                        int asInt;
                        if (int.TryParse(value.GetString(), out asInt)) return asInt != 0;
                        return null;
                }
                return null;
            }

            if (target == typeof(float) || target == typeof(double) || target == typeof(int) ||
                target == typeof(long) || target == typeof(decimal))
            {
                double d;
                if (value.ValueKind == JsonValueKind.Number) d = value.GetDouble();
                else if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out d)) { }
                else return null;
                return System.Convert.ChangeType(d, target);
            }

            if (target.IsEnum)
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    try { return Enum.Parse(target, value.GetString(), true); }
                    catch { return null; }
                }
                if (value.ValueKind == JsonValueKind.Number)
                {
                    try { return Enum.ToObject(target, (int)value.GetDouble()); }
                    catch { return null; }
                }
                return null;
            }

            return null; // 其余复杂类型（OwnerCharacter/TriggerEvents 等）不走标量通道
        }

        private static bool IsTypeKey(string name)
        {
            foreach (var key in TypeKeys)
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>大小写不敏感取 JSON 属性（兼容 camelCase / PascalCase 两种写法）。</summary>
        public static bool TryGetProp(JsonElement root, string name, out JsonElement value)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        private static bool HasProp(JsonElement root, string name)
        {
            JsonElement _;
            return TryGetProp(root, name, out _);
        }

        private static string ReadString(JsonElement root, string name)
        {
            JsonElement el;
            if (!TryGetProp(root, name, out el)) return null;
            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
        }

        private static string ReadFirstString(JsonElement root, string[] keys)
        {
            foreach (var key in keys)
            {
                string s = ReadString(root, key);
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return null;
        }

        private static List<string> ReadStringList(JsonElement root, string name)
        {
            JsonElement el;
            if (!TryGetProp(root, name, out el) || el.ValueKind != JsonValueKind.Array) return null;

            var result = new List<string>();
            foreach (var item in el.EnumerateArray())
            {
                string s = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
                if (!string.IsNullOrWhiteSpace(s)) result.Add(s.Trim());
            }
            return result;
        }
    }
}
