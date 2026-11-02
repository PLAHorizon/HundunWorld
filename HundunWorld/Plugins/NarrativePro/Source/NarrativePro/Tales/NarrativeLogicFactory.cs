using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using NarrativePro.Core;
using NarrativePro.Tales.Events;
using NarrativePro.Tales.Events.Builtin;
using NarrativePro.Tales.Events.Builtin.Conditions;
using NarrativePro.Tales.Tasks;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 叙事逻辑类型（任务 / 事件 / 条件）的名字→类型构造注册表。
    /// <para>
    /// UE5 里这三类对象是资产内联的实例：<c>UQuestBranch::QuestTasks</c>（QuestSM.h）是
    /// <c>TArray&lt;UNarrativeTask*&gt;</c> 的 Instanced 成员，节点上的 <c>Events</c> / <c>Conditions</c>
    /// 同理（NarrativeNodeBase.h）——编辑器把 UClass 引用（<c>TSubclassOf</c> / Instanced Object）连同类
    /// 一起序列化进 .uasset，运行时直接拿到实例，不需要任何查找表。
    /// </para>
    /// <para>
    /// Flax 侧资产是 JSON（见 <see cref="Quest.QuestFactory"/> 与 <see cref="Dialogue.DialogueFactory"/>），
    /// <c>System.Text.Json</c> 无法多态实例化抽象基类元素（已确认 <c>JsonSerializerOptions.Converters</c>
    /// 路线不可用），因此照抄 <see cref="Items.ItemFactory"/> 的类型/片段注册表做法：
    /// 按 JSON 里的类型标识（任务与事件/条件都用 <c>"type"</c> 判别字段）解析出 <see cref="Type"/>，
    /// <c>Activator.CreateInstance</c> 构造后再把 JSON 字段逐一反射赋值到实例上。
    /// </para>
    /// 注册表在静态构造里登记全部内置类型，并对游戏代码开放
    /// <see cref="RegisterTaskType"/> / <see cref="RegisterEventType"/> / <see cref="RegisterConditionType"/> /
    /// <see cref="AutoRegisterTypes"/> 四个注入口。未命中口径与 <see cref="NarrativeTaskManager"/> 一致
    /// （UE5 <c>UNarrativeTaskManager::GetTask</c> 的 "Has it been renamed?"）；
    /// 任务未注册时回退 <see cref="GenericTask"/>，保持 QuestFactory 既有 JSON 任务不失效。
    /// </summary>
    public static class NarrativeLogicFactory
    {
        /// <summary>JSON 里的类型判别字段候选名（大小写不敏感，按优先级匹配）。</summary>
        private static readonly string[] TypeKeys =
        {
            "type", "$type", "taskType", "eventType", "conditionType", "logicType", "class", "name"
        };

        private static readonly JsonSerializerOptions _fallbackOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>一类逻辑（任务/事件/条件）的名字→类型映射与后缀归一规则。</summary>
        private sealed class LogicRegistry
        {
            public readonly Dictionary<string, Type> Map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            public readonly string Label;
            public readonly string Suffix;
            public readonly Type Base;

            public LogicRegistry(string label, string suffix, Type @base)
            {
                Label = label;
                Suffix = suffix;
                Base = @base;
            }

            public void Register(string typeId, Type type, string[] aliases)
            {
                if (type == null || !Base.IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                {
                    NarrativeLog.LogError($"无法注册{Label}类型 '{typeId}': {type?.Name} 不是 {Base.Name} 的具体子类");
                    return;
                }

                AddUnique(typeId, type);
                AddUnique(type.Name, type);

                if (aliases != null)
                {
                    foreach (var alias in aliases)
                        AddUnique(alias, type);
                }
            }

            private void AddUnique(string key, Type type)
            {
                if (string.IsNullOrWhiteSpace(key)) return;
                string normalized = Normalize(key);
                if (normalized.Length == 0) return;

                Type existing;
                if (Map.TryGetValue(normalized, out existing) && existing != type)
                {
                    // 与 NarrativeTaskManager 处理重名资产的口径一致：保留首个并告警。
                    NarrativeLog.LogWarning($"叙事{Label}注册表中 '{normalized}' 已被 {existing.Name} 占用，忽略 {type.Name}。请确保类型标识唯一。");
                    return;
                }
                Map[normalized] = type;
            }

            /// <summary>
            /// 按名字解析类型。除精确/忽略大小写匹配外还做后缀归一：作者既可写 "CollectItem"
            /// 也可写 "CollectItemTask"（UE5 资产里是 U 前缀的类名，端口按 C# 类名登记，
            /// 这里把带/不带基类后缀两种写法视作同一个键）。
            /// </summary>
            public bool TryResolve(string name, out Type type)
            {
                type = null;
                if (string.IsNullOrWhiteSpace(name)) return false;

                string key = Normalize(name);
                if (Map.TryGetValue(key, out type)) return true;

                if (key.Length > Suffix.Length && key.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                {
                    string bare = key.Substring(0, key.Length - Suffix.Length);
                    if (bare.Length > 0 && Map.TryGetValue(bare, out type)) return true;
                }
                else if (Map.TryGetValue(key + Suffix, out type))
                {
                    return true;
                }

                type = null;
                return false;
            }

            /// <summary>未命中时的诊断口径，对应 UE5 <c>UNarrativeTaskManager::GetTask</c> 的错误日志。</summary>
            public void LogMissing(string name)
            {
                NarrativeLog.LogError($"Tried getting narrative {Label} {name} but couldn't find it. (Has it been renamed?) " +
                                      $"当前已注册的{Label}类型：{string.Join(", ", Map.Keys)}");
            }

            private static string Normalize(string value)
            {
                return value.Trim().Replace(" ", "");
            }
        }

        private static readonly LogicRegistry TaskRegistry = new LogicRegistry("任务", "Task", typeof(NarrativeTask));
        private static readonly LogicRegistry EventRegistry = new LogicRegistry("事件", "Event", typeof(NarrativeEvent));
        private static readonly LogicRegistry ConditionRegistry = new LogicRegistry("条件", "Condition", typeof(NarrativeCondition));

        // Actor/Transform 提供者注册表（Core/NarrativeActorProvider.cs 的具体子类）。
        // UE5 资产把 Instanced 提供者对象内联序列化（QuestTask.h 的 MarkerAttachActorOverride），
        // Flax 资产改用类型名字符串引用，运行期由此表按名构造 —— 与 ItemFactory/本类其余注册表同口径。
        private static readonly LogicRegistry ProviderRegistry = new LogicRegistry("提供者", "Provider", typeof(NarrativeProviderBase));

        // 与 NarrativeTaskManager 的 bCached 同款惰性口径：首次构造时扫一次，内置类型已在静态构造登记完毕，
        // 扫描只做增量补充（重名保留已有映射并告警），因此插件初始化不接线也能工作。
        private static bool _bAssembliesScanned = false;

        static NarrativeLogicFactory()
        {
            RegisterDefaults();
        }

        private static void EnsureAssembliesScanned()
        {
            if (_bAssembliesScanned) return;
            _bAssembliesScanned = true;
            AutoRegisterLoadedAssemblies();
        }

        /// <summary>登记全部内置任务/事件/条件（对应 UE5 的 UNarrativeEvent_* / UNarrativeCondition_* 内置类）。</summary>
        private static void RegisterDefaults()
        {
            // 内置任务（Tales/Tasks/BuiltinTasks）
            TaskRegistry.Register("GenericTask", typeof(GenericTask), new[] { "Generic" });
            TaskRegistry.Register("CollectItemTask", typeof(CollectItemTask), new[] { "CollectItem", "NarrativeTask_CollectItem" });
            TaskRegistry.Register("GoToLocationTask", typeof(GoToLocationTask), new[] { "GoToLocation", "NarrativeTask_GoToLocation" });
            TaskRegistry.Register("WaitTask", typeof(WaitTask), new[] { "Wait", "NarrativeTask_Wait" });

            // 内置事件（Tales/Events/Builtin）
            EventRegistry.Register("GiveItemEvent", typeof(GiveItemEvent), new[] { "GiveItem", "NarrativeEvent_GiveItem" });
            EventRegistry.Register("SetTagEvent", typeof(SetTagEvent), new[] { "SetTag", "NarrativeEvent_SetTag" });
            EventRegistry.Register("PlaySoundEvent", typeof(PlaySoundEvent), new[] { "PlaySound", "NarrativeEvent_PlaySound" });
            EventRegistry.Register("StartQuestEvent", typeof(StartQuestEvent), new[] { "StartQuest", "NarrativeEvent_StartQuest" });
            EventRegistry.Register("CompleteQuestEvent", typeof(CompleteQuestEvent), new[] { "CompleteQuest", "NarrativeEvent_CompleteQuest" });
            EventRegistry.Register("TeleportEvent", typeof(TeleportEvent), new[] { "Teleport", "NarrativeEvent_Teleport" });

            // 内置条件（Tales/Events/Builtin/Conditions）
            ConditionRegistry.Register("HasTagCondition", typeof(HasTagCondition), new[] { "HasTag", "NarrativeCondition_HasTag" });
            ConditionRegistry.Register("HasItemCondition", typeof(HasItemCondition), new[] { "HasItem", "NarrativeCondition_HasItem" });
            ConditionRegistry.Register("HasCompletedTaskCondition", typeof(HasCompletedTaskCondition), new[] { "HasCompletedTask", "NarrativeCondition_HasCompletedTask" });
            ConditionRegistry.Register("QuestStateCondition", typeof(QuestStateCondition), new[] { "QuestState", "NarrativeCondition_QuestState" });

            // 内置 Actor/Transform 提供者（对应 UE5 资产内联的 UNarrativeActorProvider_* 子类）。
            // 消费方：TaskNavigationMarker.MarkerAttachActorOverride → NarrativeTask.SpawnDefaultNavigationMarker
            //（QuestTask.cpp:207-250 的端口，NarrativeTask.cs:366/416）。
            ProviderRegistry.Register("NarrativeActorProvider_NPC", typeof(NarrativeActorProvider_NPC), new[] { "ActorProvider_NPC", "NPCProvider" });
            ProviderRegistry.Register("NarrativeActorProvider_GUIDLookup", typeof(NarrativeActorProvider_GUIDLookup), new[] { "ActorProvider_GUIDLookup", "GuidLookupProvider" });
            ProviderRegistry.Register("NarrativeActorProvider_LevelReference", typeof(NarrativeActorProvider_LevelReference), new[] { "ActorProvider_LevelReference", "LevelReferenceProvider" });
            ProviderRegistry.Register("NarrativeActorProvider_ActorOfClass", typeof(NarrativeActorProvider_ActorOfClass), new[] { "ActorProvider_ActorOfClass", "ActorOfClassProvider" });
            ProviderRegistry.Register("NarrativeTransformProvider_POI", typeof(NarrativeTransformProvider_POI), new[] { "TransformProvider_POI", "POIProvider" });
            ProviderRegistry.Register("NarrativeTransformProvider_SpecifiedTransform", typeof(NarrativeTransformProvider_SpecifiedTransform), new[] { "TransformProvider_SpecifiedTransform", "SpecifiedTransformProvider" });
        }

        #region 注册入口（对游戏代码开放）

        /// <summary>注册任务类型；JSON 里的 <c>"type"</c> 用它给的标识查找。用法同 ItemFactory.RegisterItemType。</summary>
        public static void RegisterTaskType(string typeId, Type type, params string[] aliases)
        {
            TaskRegistry.Register(typeId, type, aliases);
        }

        /// <summary>注册事件类型。</summary>
        public static void RegisterEventType(string typeId, Type type, params string[] aliases)
        {
            EventRegistry.Register(typeId, type, aliases);
        }

        /// <summary>注册条件类型。</summary>
        public static void RegisterConditionType(string typeId, Type type, params string[] aliases)
        {
            ConditionRegistry.Register(typeId, type, aliases);
        }

        /// <summary>注册 Actor/Transform 提供者类型（供任务 JSON 按名引用，口径同 <see cref="RegisterEventType"/>）。</summary>
        public static void RegisterProviderType(string typeId, Type type, params string[] aliases)
        {
            ProviderRegistry.Register(typeId, type, aliases);
        }

        /// <summary>
        /// 按 JSON 对象节点构造提供者：对象里的类型判别字段（<c>type</c>/<c>$type</c>/<c>name</c>…）
        /// 经 <see cref="ProviderRegistry"/> 解析为具体 <see cref="NarrativeProviderBase"/> 子类，
        /// 其余字段反射赋值。对应 UE5 资产内联的 Instanced 提供者对象。
        /// </summary>
        public static NarrativeProviderBase CreateProvider(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            EnsureAssembliesScanned();

            string typeId;
            if (!TryReadString(element, TypeKeys, out typeId))
            {
                ProviderRegistry.LogMissing("<缺少类型字段>");
                return null;
            }

            Type type;
            if (!ProviderRegistry.TryResolve(typeId, out type))
            {
                ProviderRegistry.LogMissing(typeId);
                return null;
            }

            var provider = Instantiate<NarrativeProviderBase>(type, ProviderRegistry, typeId);
            if (provider != null)
            {
                Populate(provider, EnumerateFields(element));
            }
            return provider;
        }

        /// <summary>
        /// 扫描程序集，把 <see cref="NarrativeTask"/> / <see cref="NarrativeEvent"/> /
        /// <see cref="NarrativeCondition"/> 的具体子类按类名自动登记。
        /// 对应 UE5 中由 UClass 体系自动可见的内置逻辑类：游戏代码新增逻辑类后调一次即可，
        /// 不必逐个手写 Register 调用。
        /// </summary>
        public static void AutoRegisterTypes(Assembly assembly)
        {
            if (assembly == null) return;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"NarrativeLogicFactory: 扫描程序集 '{assembly.GetName()}' 失败: {ex.Message}");
                return;
            }

            foreach (var type in types)
            {
                if (type == null || !type.IsClass || type.IsAbstract || type.IsInterface) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                if (typeof(NarrativeTask).IsAssignableFrom(type)) TaskRegistry.Register(type.Name, type, null);
                else if (typeof(NarrativeEvent).IsAssignableFrom(type)) EventRegistry.Register(type.Name, type, null);
                else if (typeof(NarrativeCondition).IsAssignableFrom(type)) ConditionRegistry.Register(type.Name, type, null);
            }
        }

        /// <summary>扫描当前已加载的程序集（插件脚本 + 游戏脚本）做自动登记。</summary>
        public static void AutoRegisterLoadedAssemblies()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    AutoRegisterTypes(assembly);
                }
                catch
                {
                    // 动态/资源程序集取不到类型时跳过，不影响已登记的内置类型
                }
            }
        }

        /// <summary>是否已注册（供资产校验与编辑器工具使用）。</summary>
        public static bool IsTaskRegistered(string typeId)
        {
            Type ignored;
            return TaskRegistry.TryResolve(typeId, out ignored);
        }

        /// <summary>是否已注册事件类型。</summary>
        public static bool IsEventRegistered(string typeId)
        {
            Type ignored;
            return EventRegistry.TryResolve(typeId, out ignored);
        }

        /// <summary>是否已注册条件类型。</summary>
        public static bool IsConditionRegistered(string typeId)
        {
            Type ignored;
            return ConditionRegistry.TryResolve(typeId, out ignored);
        }

        /// <summary>已注册类型标识快照（诊断用）。</summary>
        public static IEnumerable<string> RegisteredTaskTypes => TaskRegistry.Map.Keys;
        public static IEnumerable<string> RegisteredEventTypes => EventRegistry.Map.Keys;
        public static IEnumerable<string> RegisteredConditionTypes => ConditionRegistry.Map.Keys;

        #endregion

        #region 构造

        /// <summary>
        /// 按类型标识构造任务实例。<paramref name="fields"/> 为该任务节点的 JSON 字段集（可为 null）。
        /// 未注册时回退 <see cref="GenericTask"/> 并告警——这是 QuestFactory 的既有行为，
        /// 保证 <c>"type": "KillNPC"</c> 这类纯数据任务名不会因为没注册而丢掉任务。
        /// </summary>
        public static NarrativeTask CreateTask(string typeId, IDictionary<string, JsonElement> fields = null)
        {
            EnsureAssembliesScanned();

            Type type;
            NarrativeTask task;

            if (TaskRegistry.TryResolve(typeId, out type))
            {
                task = Instantiate<NarrativeTask>(type, TaskRegistry, typeId);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(typeId))
                    NarrativeLog.LogWarning($"Tried getting narrative task {typeId} but couldn't find it. (Has it been renamed?) 回退到 {nameof(GenericTask)}。");

                task = new GenericTask();
            }

            if (task != null)
                Populate(task, fields);

            return task;
        }

        /// <summary>按类型标识构造事件实例；未注册返回 null（<see cref="NarrativeEvent"/> 是抽象类，无法回退）。</summary>
        public static NarrativeEvent CreateEvent(string typeId, JsonElement data)
        {
            EnsureAssembliesScanned();

            Type type;
            if (!EventRegistry.TryResolve(typeId, out type))
            {
                EventRegistry.LogMissing(typeId);
                return null;
            }

            var evt = Instantiate<NarrativeEvent>(type, EventRegistry, typeId);
            if (evt != null) Populate(evt, data);
            return evt;
        }

        /// <summary>按类型标识构造条件实例；未注册返回 null。</summary>
        public static NarrativeCondition CreateCondition(string typeId, JsonElement data)
        {
            EnsureAssembliesScanned();

            Type type;
            if (!ConditionRegistry.TryResolve(typeId, out type))
            {
                ConditionRegistry.LogMissing(typeId);
                return null;
            }

            var cond = Instantiate<NarrativeCondition>(type, ConditionRegistry, typeId);
            if (cond != null) Populate(cond, data);
            return cond;
        }

        /// <summary>把 JSON 数组（节点/分支/状态上的 <c>"events"</c>）构造成事件实例列表，跳过无法构造的项。</summary>
        public static List<NarrativeEvent> CreateEvents(IEnumerable<JsonElement> elements)
        {
            var result = new List<NarrativeEvent>();
            if (elements == null) return result;

            foreach (var el in elements)
            {
                string typeId;
                if (!TryReadString(el, TypeKeys, out typeId))
                {
                    NarrativeLog.LogError($"叙事事件 JSON 缺少类型标识字段（\"type\"），已忽略：{SafeRaw(el)}");
                    continue;
                }

                var evt = CreateEvent(typeId, el);
                if (evt != null) result.Add(evt);
            }

            return result;
        }

        /// <summary>把 JSON 数组（节点/对话行上的 <c>"conditions"</c>）构造成条件实例列表。</summary>
        public static List<NarrativeCondition> CreateConditions(IEnumerable<JsonElement> elements)
        {
            var result = new List<NarrativeCondition>();
            if (elements == null) return result;

            foreach (var el in elements)
            {
                string typeId;
                if (!TryReadString(el, TypeKeys, out typeId))
                {
                    NarrativeLog.LogError($"叙事条件 JSON 缺少类型标识字段（\"type\"），已忽略：{SafeRaw(el)}");
                    continue;
                }

                var cond = CreateCondition(typeId, el);
                if (cond != null) result.Add(cond);
            }

            return result;
        }

        private static T Instantiate<T>(Type type, LogicRegistry registry, string requested) where T : class
        {
            try
            {
                var created = Activator.CreateInstance(type) as T;
                if (created == null)
                {
                    NarrativeLog.LogError($"创建叙事{registry.Label}实例失败 '{requested}'：{type.Name} 构造返回 null");
                    return null;
                }
                return created;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"创建叙事{registry.Label}实例失败 '{requested}'（{type.Name}）: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region JSON 反射赋值

        /// <summary>把 JSON 对象元素的字段写进实例的可写公共属性（基类属性也算：bNot / eventRuntime / requiredQuantity 等）。</summary>
        private static void Populate(object instance, JsonElement el)
        {
            if (instance == null || el.ValueKind != JsonValueKind.Object) return;
            Populate(instance, EnumerateFields(el));
        }

        private static void Populate(object instance, IEnumerable<KeyValuePair<string, JsonElement>> fields)
        {
            if (instance == null || fields == null) return;

            var type = instance.GetType();
            foreach (var field in fields)
            {
                if (string.IsNullOrEmpty(field.Key)) continue;
                if (IsTypeKey(field.Key)) continue;

                MemberInfo member;
                try
                {
                    member = FindAssignableMember(type, field.Key);
                }
                catch (AmbiguousMatchException)
                {
                    NarrativeLog.LogWarning($"{type.Name} 上存在仅大小写不同的成员 '{field.Key}'，已跳过");
                    continue;
                }
                if (member == null) continue; // 未建模的字段（编辑器注释、位置等）静默忽略

                Type declaredType = GetMemberType(member);
                if (declaredType == null) continue;

                try
                {
                    object value = ConvertValue(field.Value, declaredType, type.Name, field.Key);
                    if (value == null) continue;
                    SetMember(member, instance, value);
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"{type.Name}.{field.Key} 赋值失败: {ex.Message}");
                }
            }
        }

        private static List<KeyValuePair<string, JsonElement>> EnumerateFields(JsonElement el)
        {
            var fields = new List<KeyValuePair<string, JsonElement>>();
            if (el.ValueKind != JsonValueKind.Object) return fields;
            foreach (var prop in el.EnumerateObject())
                fields.Add(new KeyValuePair<string, JsonElement>(prop.Name, prop.Value));
            return fields;
        }

        private static bool IsTypeKey(string name)
        {
            foreach (var key in TypeKeys)
            {
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// 找同名可写公共属性（含继承来的），其次同名公共字段。
        /// 只读/非公共 setter 的属性（如 <c>NarrativeTask.CurrentProgress</c>）一律不允许被资产改写，
        /// 否则会绕过 UE5 QuestTask.cpp:137-185 的权威/读档门。
        /// </summary>
        private static MemberInfo FindAssignableMember(Type type, string name)
        {
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
                return prop.GetSetMethod(false) != null && prop.GetIndexParameters().Length == 0 ? prop : null;

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return field != null && !field.IsInitOnly ? field : null;
        }

        private static void SetMember(MemberInfo member, object instance, object value)
        {
            var prop = member as PropertyInfo;
            if (prop != null)
            {
                prop.SetValue(instance, value, null);
                return;
            }

            var field = member as FieldInfo;
            if (field != null) field.SetValue(instance, value);
        }

        /// <summary>成员声明类型（属性取 PropertyType，字段取 FieldType）。</summary>
        private static Type GetMemberType(MemberInfo member)
        {
            var prop = member as PropertyInfo;
            if (prop != null) return prop.PropertyType;

            var field = member as FieldInfo;
            return field != null ? field.FieldType : null;
        }

        private static object ConvertValue(JsonElement value, Type declaredType, string ownerName, string fieldName)
        {
            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined) return null;

            Type target = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

            // ① 抽象逻辑容器必须走注册表：事件自带的 conditions、分支的 tasks 都是抽象基类列表，
            //    System.Text.Json 无法实例化（转换器路线不可用的根因）。
            if (value.ValueKind == JsonValueKind.Array && target.IsGenericType &&
                target.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type element = target.GetGenericArguments()[0];
                if (typeof(NarrativeCondition).IsAssignableFrom(element))
                    return FillList(target, CreateConditions(value.EnumerateArray()));
                if (typeof(NarrativeEvent).IsAssignableFrom(element))
                    return FillList(target, CreateEvents(value.EnumerateArray()));
                if (typeof(NarrativeTask).IsAssignableFrom(element))
                {
                    var tasks = new List<NarrativeTask>();
                    foreach (var el in value.EnumerateArray())
                    {
                        string taskType;
                        if (!TryReadString(el, TypeKeys, out taskType)) continue;
                        var created = CreateTask(taskType, ToFieldMap(el));
                        if (created != null) tasks.Add(created);
                    }
                    return FillList(target, tasks);
                }
            }

            // ② 引擎数学类型：Vector3 的成员是 public 字段而非属性，需要专门分支。
            if (target == typeof(FlaxEngine.Vector3))
                return ReadVector3(value);

            // ③ 枚举：资产既可以写数值也可以写名字。
            if (target.IsEnum)
                return ReadEnum(value, target, ownerName, fieldName);

            if (target == typeof(string))
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();

            if (target == typeof(bool))
                return ReadBool(value);

            if (target == typeof(int) || target == typeof(long) || target == typeof(short) ||
                target == typeof(byte) || target == typeof(double) || target == typeof(float) ||
                target == typeof(decimal))
            {
                object numeric;
                long l;
                double d;
                if (value.ValueKind == JsonValueKind.Number)
                {
                    numeric = value.TryGetInt64(out l) ? (object)l : value.GetDouble();
                }
                else if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out d))
                {
                    numeric = d;
                }
                else
                {
                    return null;
                }

                return System.Convert.ChangeType(numeric, target);
            }

            // ④ 抽象提供者字段（TaskNavigationMarker.MarkerAttachActorOverride 等，
            //    NarrativeTask.SpawnDefaultNavigationMarker 消费）：必须走名字注册表，
            //    System.Text.Json 无法实例化抽象基类（同 ① 根因）。
            if (typeof(NarrativeProviderBase).IsAssignableFrom(target))
            {
                return value.ValueKind == JsonValueKind.Object ? CreateProvider(value) : null;
            }

            // ⑤ 其余对象节点（TaskNavigationMarker、目标定义等）：System.Text.Json 默认忽略公共字段
            //   （TaskNavigationMarker 成员全是字段，此前 MarkerSettings 反序列化后恒为默认值 —— 无声空转），
            //    改走「无参构造 + Populate 反射赋值」，与 ① 口径一致；失败退回 System.Text.Json。
            if (value.ValueKind == JsonValueKind.Object && !target.IsPrimitive && target != typeof(string) && target != typeof(object))
            {
                try
                {
                    var complex = Activator.CreateInstance(target);
                    if (complex != null)
                    {
                        Populate(complex, value);
                        return complex;
                    }
                }
                catch (MissingMethodException)
                {
                    // 无无参构造的类型：退回 System.Text.Json
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"{ownerName}.{fieldName} 反射填充 {target.Name} 失败，退回 JSON 反序列化: {ex.Message}");
                }
            }

            // ⑥ 其余（数组、无法反射构造的类型等）交给 System.Text.Json。
            try
            {
                return JsonSerializer.Deserialize(value.GetRawText(), target, _fallbackOptions);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"{ownerName}.{fieldName} 解析为 {target.Name} 失败: {ex.Message}");
                return null;
            }
        }

        private static object FillList(Type listType, IEnumerable items)
        {
            var list = (IList)Activator.CreateInstance(listType);
            foreach (var item in items)
            {
                if (item != null) list.Add(item);
            }
            return list;
        }

        private static object ReadBool(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number: return value.GetDouble() != 0d;
                case JsonValueKind.String:
                {
                    string text = value.GetString();
                    bool parsed;
                    if (bool.TryParse(text, out parsed)) return parsed;

                    int asInt;
                    if (int.TryParse(text, out asInt)) return asInt != 0;

                    return null;
                }
                default:
                    return null;
            }
        }

        private static object ReadEnum(JsonElement value, Type enumType, string ownerName, string fieldName)
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                long raw;
                return value.TryGetInt64(out raw) ? (object)Enum.ToObject(enumType, raw) : null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                string text = value.GetString();
                if (string.IsNullOrEmpty(text)) return null;

                try
                {
                    return Enum.Parse(enumType, text, true);
                }
                catch
                {
                    NarrativeLog.LogWarning($"{ownerName}.{fieldName} 的枚举值 '{text}' 无法识别（{enumType.Name}），已忽略");
                }
            }

            return null;
        }

        private static object ReadVector3(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                float x = 0f, y = 0f, z = 0f;
                foreach (var prop in value.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Number) continue;
                    float v = (float)prop.Value.GetDouble();
                    if (string.Equals(prop.Name, "x", StringComparison.OrdinalIgnoreCase)) x = v;
                    else if (string.Equals(prop.Name, "y", StringComparison.OrdinalIgnoreCase)) y = v;
                    else if (string.Equals(prop.Name, "z", StringComparison.OrdinalIgnoreCase)) z = v;
                }
                return new FlaxEngine.Vector3(x, y, z);
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                var parts = new float[3];
                int i = 0;
                foreach (var el in value.EnumerateArray())
                {
                    if (i >= 3) break;
                    if (el.ValueKind == JsonValueKind.Number) parts[i] = (float)el.GetDouble();
                    i++;
                }
                return new FlaxEngine.Vector3(parts[0], parts[1], parts[2]);
            }

            return null;
        }

        private static Dictionary<string, JsonElement> ToFieldMap(JsonElement el)
        {
            var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (el.ValueKind != JsonValueKind.Object) return map;
            foreach (var prop in el.EnumerateObject())
                map[prop.Name] = prop.Value;
            return map;
        }

        private static bool TryReadString(JsonElement el, string[] candidateKeys, out string value)
        {
            value = null;
            if (el.ValueKind != JsonValueKind.Object) return false;

            foreach (var key in candidateKeys)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase)) continue;
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;

                    string candidate = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        value = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static string SafeRaw(JsonElement el)
        {
            try
            {
                return el.GetRawText();
            }
            catch
            {
                return "<unavailable>";
            }
        }

        #endregion
    }
}
