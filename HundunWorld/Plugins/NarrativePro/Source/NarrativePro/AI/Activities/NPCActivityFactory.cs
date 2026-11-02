using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using NarrativePro.Core;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// 活动系统的序列化工具。对应 UE5 的
    /// <c>FMemoryWriter/FMemoryReader + FObjectAndNameAsStringProxyArchive + ArIsSaveGame = true</c>
    /// （<c>NPCActivityComponent.cpp:794-918</c>）。
    /// <para>
    /// Flax 无反射式 UObject 归档，因此用 <see cref="JsonSerializer"/> 达成同一可观测语义：
    /// 运行时引用与事件通过 <see cref="JsonIgnoreAttribute"/> 排除，其余公开字段即 SaveGame 数据。
    /// </para>
    /// </summary>
    public static class NarrativeActivitySerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            // 活动/目标都是字段承载数据的 POCO，需与 UE5 UPROPERTY 一样按字段序列化
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            WriteIndented = false
        };

        /// <summary>把对象序列化成字节（对应 UE5 写入 <c>TArray<uint8> Data</c>）。</summary>
        public static byte[] ToBytes(object value)
        {
            if (value == null) return Array.Empty<byte>();
            try
            {
                return JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), Options);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"活动系统序列化失败（{value.GetType().Name}）：{ex.Message}");
                return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// 用字节覆盖已有实例的字段（对应 UE5 在既有对象上 <c>Serialize(Ar)</c> 回灌）。
        /// </summary>
        public static void OverwriteFrom(byte[] data, object target)
        {
            if (data == null || data.Length == 0 || target == null) return;
            try
            {
                var type = target.GetType();
                // JsonSerializer 默认会新建对象；这里先解析再逐字段拷回，保住运行时身份与已建立的引用
                using (var document = JsonDocument.Parse(data))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object) return;
                    ApplyProperties(document.RootElement, target, type);
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"活动系统反序列化失败（{target.GetType().Name}）：{ex.Message}");
            }
        }

        private static void ApplyProperties(JsonElement root, object target, Type type)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanWrite) continue;
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                if (!TryReadValue(root, property.Name, out var element)) continue;

                try
                {
                    var value = element.Deserialize(property.PropertyType, Options);
                    property.SetValue(target, value);
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"跳过无法还原的属性 {type.Name}.{property.Name}：{ex.Message}");
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                if (field.IsInitOnly) continue;
                if (!TryReadValue(root, field.Name, out var element)) continue;

                try
                {
                    var value = element.Deserialize(field.FieldType, Options);
                    field.SetValue(target, value);
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"跳过无法还原的字段 {type.Name}.{field.Name}：{ex.Message}");
                }
            }
        }

        private static bool TryReadValue(JsonElement root, string name, out JsonElement element)
        {
            // 序列化时字段/属性名按原样写入；大小写不敏感以提高跨版本兼容性
            if (root.TryGetProperty(name, out element)) return true;
            foreach (var candidate in root.EnumerateObject())
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    element = candidate.Value;
                    return true;
                }
            }
            element = default;
            return false;
        }
    }

    /// <summary>
    /// 活动 / 目标生成器 / 目标项的类型工厂。对应 UE5 的 <c>TSubclassOf&lt;...&gt;</c>：
    /// 配置资产里存类路径，运行时 <c>NewObject(this, Class)</c> 实例化。
    /// <para>
    /// Flax 无 UClass，这里用「类型路径 → Type」的登记表 + 本程序集反射解析，
    /// 再 <see cref="Activator"/> 创建实例（与 <c>Items/ItemFactory.cs</c> 同一套路）。
    /// </para>
    /// </summary>
    public static class NPCActivityFactory
    {
        private static readonly Dictionary<string, Type> _activityTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> _generatorTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> _goalTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> _scheduleBehaviorTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        private static bool _bScanned;

        /// <summary>注册活动类型（可用短名或全名）。</summary>
        public static void RegisterActivity(string typeId, Type type)
        {
            Register(_activityTypes, typeId, type, typeof(NPCActivity), "活动");
        }

        /// <summary>注册目标生成器类型。</summary>
        public static void RegisterGoalGenerator(string typeId, Type type)
        {
            Register(_generatorTypes, typeId, type, typeof(NPCGoalGenerator), "目标生成器");
        }

        /// <summary>注册目标项类型。</summary>
        public static void RegisterGoal(string typeId, Type type)
        {
            Register(_goalTypes, typeId, type, typeof(NPCGoalItem), "目标项");
        }

        /// <summary>注册调度行为类型。</summary>
        public static void RegisterScheduleBehavior(string typeId, Type type)
        {
            Register(_scheduleBehaviorTypes, typeId, type, typeof(ScheduledBehavior_NPC), "调度行为");
        }

        private static void Register(Dictionary<string, Type> map, string typeId, Type type, Type requiredBase, string label)
        {
            if (string.IsNullOrEmpty(typeId) || type == null) return;

            if (!requiredBase.IsAssignableFrom(type) || type.IsAbstract)
            {
                NarrativeLog.LogError($"无法注册{label}类型 '{typeId}'：{type.Name} 不是 {requiredBase.Name} 的具体子类");
                return;
            }

            map[typeId] = type;
            map[type.FullName] = type;
            map[type.Name] = type;
        }

        /// <summary>按类型路径解析活动类型。</summary>
        public static Type ResolveActivityType(string classPath) => Resolve(_activityTypes, classPath, typeof(NPCActivity));

        /// <summary>按类型路径解析目标生成器类型。</summary>
        public static Type ResolveGoalGeneratorType(string classPath) => Resolve(_generatorTypes, classPath, typeof(NPCGoalGenerator));

        /// <summary>按类型路径解析目标项类型。</summary>
        public static Type ResolveGoalType(string classPath) => Resolve(_goalTypes, classPath, typeof(NPCGoalItem));

        /// <summary>按类型路径解析调度行为类型。</summary>
        public static Type ResolveScheduleBehaviorType(string classPath) => Resolve(_scheduleBehaviorTypes, classPath, typeof(ScheduledBehavior_NPC));

        /// <summary>创建调度行为实例。对应 UE5 调度资产里的 TSubclassOf&lt;UScheduledBehavior_NPC&gt; NewObject。</summary>
        public static ScheduledBehavior_NPC CreateScheduleBehavior(string classPath)
        {
            var type = ResolveScheduleBehaviorType(classPath);
            return type != null ? CreateInstance<ScheduledBehavior_NPC>(type, "调度行为") : null;
        }

        private static Type Resolve(Dictionary<string, Type> map, string classPath, Type requiredBase)
        {
            if (string.IsNullOrEmpty(classPath)) return null;

            EnsureScanned();

            if (map.TryGetValue(classPath, out var type)) return type;

            // 回退：直接按 CLR 类型名解析（含程序集限定名与不带程序集两种写法）
            var resolved = Type.GetType(classPath, false, true);
            if (resolved != null && requiredBase.IsAssignableFrom(resolved))
            {
                map[classPath] = resolved;
                return resolved;
            }

            return null;
        }

        /// <summary>创建活动实例。对应 UE5 <c>NewObject&lt;UNPCActivity&gt;(this, ActivityClass)</c>。</summary>
        public static NPCActivity CreateActivity(string classPath)
        {
            var type = ResolveActivityType(classPath);
            return type != null ? CreateInstance<NPCActivity>(type, "活动") : null;
        }

        /// <summary>创建目标生成器实例。对应 UE5 <c>NewObject&lt;UNPCGoalGenerator&gt;</c>。</summary>
        public static NPCGoalGenerator CreateGoalGenerator(string classPath)
        {
            var type = ResolveGoalGeneratorType(classPath);
            return type != null ? CreateInstance<NPCGoalGenerator>(type, "目标生成器") : null;
        }

        /// <summary>创建目标项实例。对应 UE5 <c>NewObject&lt;UNPCGoalItem&gt;</c>。</summary>
        public static NPCGoalItem CreateGoal(string classPath)
        {
            var type = ResolveGoalType(classPath);
            if (type == null)
            {
                // 存档里的目标类型可能来自尚未注册的模块，显式告警而不是静默丢弃
                NarrativeLog.LogWarning($"未能解析目标类型 '{classPath}'，该存档目标将被跳过");
                return null;
            }
            return CreateInstance<NPCGoalItem>(type, "目标项");
        }

        private static T CreateInstance<T>(Type type, string label) where T : class
        {
            try
            {
                // 对应 UE5 以外层对象为 Outer 的 NewObject：这里用无参构造 + 由调用方补 Owner
                return (T)Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"创建{label}实例失败（{type.FullName}）：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 首次使用时扫描本程序集，把所有具体子类按名字与全名登记。
        /// 这样配置资产只需写类名，不必依赖注册顺序。
        /// </summary>
        private static void EnsureScanned()
        {
            if (_bScanned) return;
            _bScanned = true;

            try
            {
                var assembly = typeof(NPCActivity).Assembly;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface) continue;

                    if (typeof(NPCActivity).IsAssignableFrom(type)) Register(_activityTypes, type.FullName, type, typeof(NPCActivity), "活动");
                    else if (typeof(NPCGoalGenerator).IsAssignableFrom(type)) Register(_generatorTypes, type.FullName, type, typeof(NPCGoalGenerator), "目标生成器");
                    else if (typeof(NPCGoalItem).IsAssignableFrom(type)) Register(_goalTypes, type.FullName, type, typeof(NPCGoalItem), "目标项");
                    else if (typeof(ScheduledBehavior_NPC).IsAssignableFrom(type)) Register(_scheduleBehaviorTypes, type.FullName, type, typeof(ScheduledBehavior_NPC), "调度行为");
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"扫描活动类型失败：{ex.Message}");
            }
        }

        /// <summary>强制重新扫描（热重载后调用）。</summary>
        public static void InvalidateScan()
        {
            _bScanned = false;
            _activityTypes.Clear();
            _generatorTypes.Clear();
            _goalTypes.Clear();
            _scheduleBehaviorTypes.Clear();
        }
    }
}
