// 模块关系说明：
// 本文件属于 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
//
// UE5 中“可存档 Actor”是 AActor 子类并实现 INarrativeStableActor / INarrativeSavableActor 接口，
// 存档子系统直接对 Actor 调用 Execute_GetActorGUID / Execute_PrepareForSave 等（见 NarrativeSaveSubsystem.cpp:678-747）。
// Flax 的 Actor 是密封类，玩法状态实际挂在 Actor 的 Script 组件上
// （如 Spawners.SpawnerBase、Interaction.ItemPickup、Vehicles.NarrativeVehicleBase 都提供了
// GetActorGUID/SetActorGUID/PrepareForSave/Load/ShouldRespawn 这些同名方法，但没有实现插件接口）。
// 本文件提供桥接层 NarrativeSavableActorBinding：优先使用插件接口，接口缺失时按 UE5 同名方法签名
// 反射适配，使这些既有的“影子实现”立刻能被存档子系统真实驱动，而不需要修改其它模块的源码。

using System;
using System.Collections.Generic;
using System.Reflection;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.SaveSystem.Subsystems;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 可存档 Actor 绑定。把“Actor + 提供 GUID 的宿主对象（Actor 自身或其挂载 Script）”
    /// 统一包装成 INarrativeSavableActor，接口优先、反射兜底。
    /// </summary>
    public sealed class NarrativeSavableActorBinding : INarrativeSavableActor
    {
        /// <summary>被存档的场景 Actor。</summary>
        public Actor Actor { get; private set; }

        /// <summary>提供稳定 GUID 与状态的对象（Actor 自身或其挂载的 Script）。</summary>
        public System.Object Target { get; private set; }

        /// <summary>Target 是否实现了插件接口（false 表示反射适配的“影子实现”）。</summary>
        public bool UsesInterface { get; private set; }

        /// <summary>Target 是否实现了 INarrativeSaveStateProvider（自绘状态字节）。</summary>
        public bool HasStateProvider => Target is INarrativeSaveStateProvider;

        /// <summary>构造绑定。一般由 NarrativeSavableActorResolver 创建。</summary>
        internal NarrativeSavableActorBinding(Actor actor, System.Object target, bool usesInterface)
        {
            Actor = actor;
            Target = target;
            UsesInterface = usesInterface;
        }

        /// <summary>归档键（宿主对象类型全名）。</summary>
        public string StateKey => NarrativeSaveStateArchive.GetKeyForObject(Target);

        /// <summary>
        /// 获取稳定 GUID。对应 UE5 INarrativeStableActor::GetActorGUID。
        /// 取值次序（真·持久化稳定 id 优先于反射方法名与字段名启发式）收敛在纯层
        /// <see cref="NarrativeSaveKeyPlanner.DiscoverStableGuid"/>，实时侧与 harness 走同一份实现。
        /// </summary>
        public Guid GetActorGUID()
        {
            // 绑定自身不能当作被询问的宿主：本类也实现 INarrativeStableActor，
            // 交给纯层探测会回调回本方法形成递归（UE5 语义下 Target 永不是绑定自身）。
            if (ReferenceEquals(Target, this)) return Guid.Empty;

            // 承载字段/属性可能不存在：读不到即视为“无稳定 GUID”（Guid.Empty），由调用方走动态分支
            return NarrativeSaveKeyPlanner.DiscoverStableGuid(Target, out _);
        }

        /// <summary>设置稳定 GUID。对应 UE5 INarrativeSavableActor::SetActorGUID。</summary>
        public void SetActorGUID(Guid savedGUID)
        {
            if (Target is INarrativeSavableActor savable && !ReferenceEquals(savable, this))
            {
                savable.SetActorGUID(savedGUID);
                return;
            }

            if (NarrativeReflect.TryInvokeVoid(Target, "SetActorGUID", savedGUID)) return;

            if (NarrativeReflect.WriteGuidMember(Target, GuidMemberNames, savedGUID)) return;

            // 无承载成员时记录告警，便于排查“读档后 GUID 丢失”的问题
            NarrativeLog.LogWarning($"[SaveSystem] {Target.GetType().Name} 缺少 SetActorGUID/GUID 承载成员，无法写回存档 GUID。");
        }

        /// <summary>通知宿主对象即将保存。对应 UE5 INarrativeSavableActor::PrepareForSave。</summary>
        public void PrepareForSave()
        {
            if (Target is INarrativeSavableActor savable && !ReferenceEquals(savable, this))
            {
                savable.PrepareForSave();
                return;
            }
            NarrativeReflect.TryInvokeVoid(Target, "PrepareForSave");
        }

        /// <summary>通知宿主对象已从存档加载。对应 UE5 INarrativeSavableActor::Load。</summary>
        public void Load()
        {
            if (Target is INarrativeSavableActor savable && !ReferenceEquals(savable, this))
            {
                savable.Load();
                return;
            }
            NarrativeReflect.TryInvokeVoid(Target, "Load");
        }

        /// <summary>是否需要存档系统重生成动态 Actor。对应 UE5 ShouldRespawn（UE5 默认为 true）。</summary>
        public bool ShouldRespawn()
        {
            if (Target is INarrativeSavableActor savable && !ReferenceEquals(savable, this))
            {
                return savable.ShouldRespawn();
            }

            var value = NarrativeReflect.InvokeBool(Target, "ShouldRespawn");
            return value ?? true;
        }

        /// <summary>是否需要永久记录。对应 UE5 INarrativeSavableActor::NeedsPermanentRecord。</summary>
        public bool NeedsPermanentRecord()
        {
            var value = NarrativeReflect.InvokeBool(Target, "NeedsPermanentRecord");
            return value ?? false;
        }

        // GUID 承载字段的常见命名（UE5 端口里的等价字段）
        internal static readonly string[] GuidMemberNames =
        {
            "ActorGUID", "SaveGUID", "ActorSaveGUID", "SpawnerSaveGUID", "PickupSaveGUID", "VehicleSaveGUID", "StableActorGUID",
        };
    }

    /// <summary>
    /// 可存档 Actor 解析器。把 Flax 的 Actor/Script 组合解析为 NarrativeSavableActorBinding。
    /// </summary>
    public static class NarrativeSavableActorResolver
    {
        /// <summary>
        /// 解析一个 Actor 的可存档绑定。
        /// 优先：Actor 自身实现 INarrativeStableActor；
        /// 其次：Actor 上某个 Script 实现接口；
        /// 最后：按 UE5 方法名（GetActorGUID 等）反射适配“影子实现”。
        /// 非可存档 Actor 返回 null。
        /// </summary>
        public static NarrativeSavableActorBinding Resolve(Actor actor)
        {
            if (actor == null) return null;

            if (actor is INarrativeStableActor actorStable)
            {
                return new NarrativeSavableActorBinding(actor, actor, true);
            }

            var scripts = GetScripts(actor);

            // 接口优先
            foreach (var script in scripts)
            {
                if (script is INarrativeStableActor)
                {
                    return new NarrativeSavableActorBinding(actor, script, true);
                }
            }

            // 影子实现：具备 GetActorGUID() 方法或 GUID 承载字段
            foreach (var script in scripts)
            {
                if (NarrativeReflect.HasGuidAccessor(script))
                {
                    return new NarrativeSavableActorBinding(actor, script, false);
                }
            }

            if (NarrativeReflect.HasGuidAccessor(actor))
            {
                return new NarrativeSavableActorBinding(actor, actor, false);
            }

            return null;
        }

        /// <summary>获取 Actor 上的全部 Script（缓存安全，异常时返回空集合）。</summary>
        public static List<Script> GetScripts(Actor actor)
        {
            var result = new List<Script>();
            if (actor == null) return result;
            try
            {
                var scripts = actor.GetScripts<Script>();
                if (scripts != null)
                {
                    foreach (var script in scripts)
                    {
                        if (script != null) result.Add(script);
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] 枚举 Actor 脚本失败 {actor.Name}: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// 收集 Actor 作用域内需要归档状态的对象（宿主对象 + 其余脚本 + Actor 自身）。
        /// </summary>
        /// <param name="actor">目标 Actor。</param>
        /// <param name="targets">输出列表，按稳定键顺序追加。</param>
        /// <param name="includeActor">是否同时归档 Actor 自身类型上的可存档成员。</param>
        /// <param name="exclude">需要排除的对象（例如正在被询问的 provider 组件自身）。</param>
        public static void AddStateTargets(Actor actor, List<NarrativeStateTarget> targets, bool includeActor, System.Object exclude)
        {
            if (actor == null || targets == null) return;

            var binding = Resolve(actor);

            // Actor 自身一段（不参与同类内序号计数，与读档侧一致）。
            if (includeActor && !ReferenceEquals(actor, exclude) && NarrativeSaveStateArchive.HasArchivableState(actor))
            {
                targets.Add(new NarrativeStateTarget(NarrativeSaveStateArchive.GetKeyForObject(actor), actor));
            }

            // 脚本段的键与取舍全在纯层 NarrativeSaveKeyPlanner（与 harness 同一份实现）。
            var views = ToViews(GetScripts(actor), binding, exclude);
            var planned = NarrativeSaveKeyPlanner.PlanStateTargets(views, out _);
            for (int i = 0; i < planned.Count; i++)
            {
                targets.Add(planned[i]);
            }
        }

        /// <summary>
        /// 把脚本列表折成纯层候选视图（只做类型判定与基键计算，裁决交给纯层）。
        /// null 条目在进入候选前就被丢弃，因此它们不占用同类内序号 ——
        /// 这与 <c>NarrativeSaveStateArchive.ResolveKeyInActor</c> 读档侧的计数口径一致。
        /// </summary>
        internal static List<SaveCandidateView> ToViews(
            List<Script> scripts,
            NarrativeSavableActorBinding binding,
            System.Object exclude)
        {
            var views = new List<SaveCandidateView>(scripts?.Count ?? 0);
            if (scripts == null) return views;

            foreach (var script in scripts)
            {
                if (script == null) continue;

                views.Add(new SaveCandidateView
                {
                    ObjectKey = script,
                    BaseKey = NarrativeSaveStateArchive.GetKeyForObject(script),
                    bIsProvider = script is INarrativeSaveStateProvider,
                    bIsSavableComponent = script is INarrativeSavableComponent,
                    bHasExplicitMarkers = NarrativeSaveStateArchive.HasExplicitSaveMarkers(script),
                    bHasArchivableState = NarrativeSaveStateArchive.HasArchivableState(script),
                    bIsBindingTarget = binding != null && ReferenceEquals(script, binding.Target),
                    bIsArchiveChannel = script is NarrativeSaveStateProviderComponent,
                    bExcluded = exclude != null && ReferenceEquals(script, exclude),
                });
            }

            return views;
        }

        /// <summary>
        /// 收集 Actor 的“可存档组件”对象（对应 UE5 Actor->GetComponents() 中实现 INarrativeSavableComponent 的组件）。
        /// 返回 (键, 对象) 列表；键用于 NarrativeSaveComponent.ComponentName。
        /// </summary>
        /// <param name="actor">目标 Actor。</param>
        /// <param name="binding">该 Actor 的可存档绑定（其宿主对象的状态写在记录 ByteData 中，不重复入组件）。</param>
        /// <param name="excludeOut">输出：已被 Actor 状态段捕获的对象，供调用方避免重复归档。</param>
        /// <param name="includeAllScripts">为真时把所有具备可归档状态的脚本都当作组件（对应设置 bArchiveAllSavableScripts）。</param>
        public static List<KeyValuePair<string, System.Object>> CollectSavableComponents(
            Actor actor,
            NarrativeSavableActorBinding binding,
            ICollection<System.Object> excludeOut,
            bool includeAllScripts)
        {
            var result = new List<KeyValuePair<string, System.Object>>();
            if (actor == null) return result;

            // 脚本段的键与取舍全在纯层 NarrativeSaveKeyPlanner（与 AddStateTargets、与 harness 同一份实现）。
            var views = ToViews(GetScripts(actor), binding, null);
            var planned = NarrativeSaveKeyPlanner.PlanComponents(views, includeAllScripts);
            for (int i = 0; i < planned.Count; i++)
            {
                result.Add(planned[i]);
                excludeOut?.Add(planned[i].Value);
            }

            // Flax 中子 Actor 可视为组件（对应 UE5 挂在 Actor 下的 SceneComponent）
            foreach (var child in actor.GetChildren<Actor>())
            {
                if (child == null) continue;
                bool childIsComponent = child is INarrativeSavableComponent;
                if (!childIsComponent)
                {
                    foreach (var childScript in GetScripts(child))
                    {
                        if (childScript is INarrativeSavableComponent)
                        {
                            childIsComponent = true;
                            break;
                        }
                    }
                }
                if (!childIsComponent) continue;

                result.Add(new KeyValuePair<string, System.Object>("Actor:" + child.Name, child));
            }

            return result;
        }
    }

    /// <summary>
    /// 反射适配工具：按 UE5 接口方法名查找并调用“影子实现”，结果按 (类型, 名称) 缓存。
    /// </summary>
    public static class NarrativeReflect
    {
        private static readonly Dictionary<Type, MethodInfo> _methodCache = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MemberInfo[]> _guidMemberCache = new Dictionary<Type, MemberInfo[]>();
        private static readonly object _cacheLock = new object();

        private static MethodInfo FindMethod(Type type, string name, Type returnType, Type[] parameterTypes)
        {
            var flags = BindingFlags.Public | BindingFlags.Instance;
            MethodInfo method = null;
            try
            {
                method = parameterTypes == null
                    ? type.GetMethod(name, flags)
                    : type.GetMethod(name, flags, null, parameterTypes, null);
            }
            catch
            {
                method = null;
            }

            if (method == null) return null;
            if (returnType != null && !returnType.IsAssignableFrom(method.ReturnType)) return null;
            return method;
        }

        private static MethodInfo GetCachedMethod(Type type, string name, Type returnType, Type[] parameterTypes)
        {
            string cacheKey = name + "|" + (returnType?.Name ?? "void") + "|" + (parameterTypes?.Length ?? 0);
            lock (_cacheLock)
            {
                // 简化：以 (type, name) 组合缓存最近一次查找结果
                if (_methodCache.TryGetValue(type, out var cached) && cached != null
                    && string.Equals(cached.Name, name, StringComparison.Ordinal))
                {
                    return cached;
                }
            }

            var method = FindMethod(type, name, returnType, parameterTypes);
            lock (_cacheLock)
            {
                if (method != null) _methodCache[type] = method;
            }
            return method;
        }

        /// <summary>调用无参 Guid 方法（如 GetActorGUID / NeedsPermanentRecord 之外的 GUID 取值）。</summary>
        public static Guid? InvokeGuid(System.Object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var method = GetCachedMethod(type, name, typeof(Guid), Type.EmptyTypes);
            if (method == null) return null;
            try
            {
                return (Guid)method.Invoke(target, null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>调用无参 bool 方法。</summary>
        public static bool? InvokeBool(System.Object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var method = GetCachedMethod(type, name, typeof(bool), Type.EmptyTypes);
            if (method == null) return null;
            try
            {
                return (bool)method.Invoke(target, null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>调用无参（或单 Guid 参数）void 方法。</summary>
        public static bool TryInvokeVoid(System.Object target, string name, params System.Object[] args)
        {
            if (target == null) return false;
            var type = target.GetType();
            var parameterTypes = new Type[args?.Length ?? 0];
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                parameterTypes[i] = args[i]?.GetType() ?? typeof(System.Object);
            }

            var method = FindMethod(type, name, typeof(void), parameterTypes);
            if (method == null) return false;

            try
            {
                method.Invoke(target, args);
                return true;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] 调用 {type.Name}.{name} 失败: {ex.InnerException?.Message ?? ex.Message}");
                return false;
            }
        }

        /// <summary>目标是否具备 GUID 访问能力（GetActorGUID() 方法或常见 GUID 承载字段/属性）。</summary>
        public static bool HasGuidAccessor(System.Object target)
        {
            if (target == null) return false;
            var type = target.GetType();
            if (FindMethod(type, "GetActorGUID", typeof(Guid), Type.EmptyTypes) != null) return true;
            return FindGuidMember(type) != null;
        }

        private static MemberInfo[] FindGuidMember(Type type)
        {
            lock (_cacheLock)
            {
                if (_guidMemberCache.TryGetValue(type, out var cached)) return cached;
            }

            MemberInfo found = null;
            foreach (var name in NarrativeSavableActorBinding.GuidMemberNames)
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(Guid) && !field.IsInitOnly)
                {
                    found = field;
                    break;
                }

                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(Guid) && prop.CanWrite && prop.GetSetMethod(false) != null)
                {
                    found = prop;
                    break;
                }
            }

            var result = found == null ? Array.Empty<MemberInfo>() : new[] { found };
            lock (_cacheLock)
            {
                _guidMemberCache[type] = result;
            }
            return result;
        }

        /// <summary>读取 GUID 承载字段/属性。</summary>
        public static Guid? ReadGuidMember(System.Object target, string[] candidateNames)
        {
            if (target == null) return null;
            var member = FindGuidMember(target.GetType());
            if (member == null || member.Length == 0) return null;
            try
            {
                if (member[0] is FieldInfo f) return (Guid)f.GetValue(target);
                if (member[0] is PropertyInfo p) return (Guid)p.GetValue(target, null);
            }
            catch
            {
                return null;
            }
            return null;
        }

        /// <summary>写入 GUID 承载字段/属性。</summary>
        public static bool WriteGuidMember(System.Object target, string[] candidateNames, Guid value)
        {
            if (target == null) return false;
            var member = FindGuidMember(target.GetType());
            if (member == null || member.Length == 0) return false;
            try
            {
                if (member[0] is FieldInfo f)
                {
                    f.SetValue(target, value);
                    return true;
                }
                if (member[0] is PropertyInfo p)
                {
                    p.SetValue(target, value, null);
                    return true;
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] 写入 GUID 成员失败 {target.GetType().Name}: {ex.Message}");
            }
            return false;
        }
    }
}
