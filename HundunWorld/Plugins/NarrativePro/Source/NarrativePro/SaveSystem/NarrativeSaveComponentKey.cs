// 模块关系说明：
// 本文件属于 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
//
// 为什么需要它（缺陷修复）：
// UE5 用组件的稳定 ComponentName 作为归档键（NarrativeSave.h:18、NarrativeSaveSubsystem.cpp:723-728,790-803），
// 而 Flax 端口早期用 Actor.GetScripts() 的*枚举顺序*生成 "类型全名" / "类型全名#1" 位置化键。
// 本插件的运行期宿主会条件式补挂脚本并在重生时重挂（NarrativeRuntimeBootstrap.AttachHostComponents），
// 只要脚本数量或顺序发生变化，位置化键就会把字节应用到*错误的*组件上（跨会话静默串档）。
//
// 本文件提供两层稳定身份：
//   1) [NarrativeSaveKey("...")] 类型特性 / SaveComponentKey 实例成员 —— 显式声明的归档键，与顺序完全无关；
//   2) 确定性 slot id —— 就是该对象的归档键本身（"类型全名#同类内序号" 或显式键），随记录持久化
//      （NarrativeSaveComponent.ComponentId 与状态段每条目标的 "i" 字段）。
// 为什么放弃 GUID id（上一版的缺陷）：GUID↔对象 映射只活在当前进程静态内存里，全新启动时表为空，
// Resolve(id) 必 miss，保存/读取两侧都只能退回位置化名字键 —— id 机制跨重启等于空转。
// 确定性 id 不需要任何缓存：写读两侧对同一对象都按“扫描 actor 脚本、同基键计数出序号”算出同值
// （写侧 NarrativeSavableActorResolver.GetScripts / CollectSavableComponents，读侧 ResolveKeyInActor
// 用同一个 Actor.GetScripts 枚举与同一计数规则），因此天然跨进程重启存活。
// 残余风险 —— 同类实例数量中途变化让序号挪位：由载荷指纹闸门兜底（"f" 字段，见
// NarrativeSaveStateArchive.ComputeFingerprintForObject），指纹不符则拒绝应用字节并告警，
// 行为从“静默串档”收敛为“保留默认值 + 一条告警”。
// 会话内序号位被另一个活对象占用时退化为仅本会话有效的不透明 id（保证 id↔对象 单射），
// 该不透明 id 跨会话解析不到时同样回到“确定性键 + 指纹闸门”通路。
// 读档优先按 id/键匹配并用指纹校验；没有指纹的老档按原名通路还原，下次保存补写 id 与指纹。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 为脚本/组件类型声明稳定的归档键，等价 UE5 用稳定 ComponentName 归档。
    /// 加了此特性的组件，其归档键不再依赖 <c>Actor.GetScripts()</c> 的枚举顺序。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
    public sealed class NarrativeSaveKeyAttribute : Attribute
    {
        /// <summary>稳定归档键（同一 Actor 作用域内必须唯一）。</summary>
        public readonly string Key;

        /// <summary>声明稳定归档键。</summary>
        public NarrativeSaveKeyAttribute(string key)
        {
            Key = key ?? string.Empty;
        }
    }

    /// <summary>
    /// 组件归档键与 slot id 的运行期注册表（仅本会话的绑定加速）。
    /// 只做“身份绑定”，不改动归档内容本身；引用相等语义（同一个 Script 对象 = 同一个组件）。
    /// 注意：本表不是身份的唯一真相源 —— id 是确定性键，跨重启可由写读两侧同规则重算；
    /// 本表只让会话内（脚本中途增删/重排后）仍能按首次固定的 id 找回原对象。
    /// </summary>
    public static class NarrativeComponentKeyRegistry
    {
        private static readonly Dictionary<System.Object, string> _idByObject =
            new Dictionary<System.Object, string>(ReferenceKeyComparer.Instance);

        private static readonly Dictionary<string, WeakReference<System.Object>> _objectById =
            new Dictionary<string, WeakReference<System.Object>>(StringComparer.Ordinal);

        private static readonly object _lock = new object();

        /// <summary>
        /// 读取对象的显式归档键：优先 <see cref="NarrativeSaveKeyAttribute"/>，
        /// 其次实例上的 <c>public string SaveComponentKey</c> 字段/属性（非空时生效）。
        /// 都没有时返回 null（调用方回退类型全名的旧方案）。
        /// </summary>
        public static string GetExplicitKey(System.Object target)
        {
            if (target == null) return null;

            var type = target.GetType();
            try
            {
                var attr = (NarrativeSaveKeyAttribute[])type.GetCustomAttributes(
                    typeof(NarrativeSaveKeyAttribute), true);
                if (attr != null)
                {
                    for (int i = 0; i < attr.Length; i++)
                    {
                        if (attr[i] != null && !string.IsNullOrEmpty(attr[i].Key)) return attr[i].Key;
                    }
                }
            }
            catch
            {
                // 反射特性读取失败时按“无显式键”处理
            }

            return ReadMemberKey(target, type);
        }

        private static string ReadMemberKey(System.Object target, Type type)
        {
            const string MemberName = "SaveComponentKey";
            try
            {
                var field = type.GetField(MemberName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(string))
                {
                    return field.GetValue(target) as string;
                }

                var prop = type.GetProperty(MemberName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(string) && prop.CanRead
                    && prop.GetIndexParameters().Length == 0)
                {
                    return prop.GetValue(target, null) as string;
                }
            }
            catch
            {
                // 成员存在但取值异常（对象已销毁等）：按“无显式键”处理
            }
            return null;
        }

        /// <summary>取该对象已绑定的 slot id（未绑定返回 null）。</summary>
        public static string GetSlotId(System.Object target)
        {
            if (target == null) return null;
            lock (_lock)
            {
                return _idByObject.TryGetValue(target, out var id) ? id : null;
            }
        }

        /// <summary>
        /// 取该对象的 slot id；没有就分配一个（确定性：与归档键同形，写读两侧同值，可跨重启重算）。
        /// 本会话内已绑定过对象不重复取号，保持“首次写入即固定”的语义。
        /// 序号位已被另一个活对象占用时（会话内同类实例数中途变化）退化为不透明 id，保证 id↔对象单射。
        /// </summary>
        public static string GetOrCreateSlotId(System.Object target, string stableKey)
        {
            if (target == null) return string.Empty;

            lock (_lock)
            {
                if (_idByObject.TryGetValue(target, out var existing) && !string.IsNullOrEmpty(existing))
                {
                    return existing;
                }

                string id = MakeId(stableKey);
                if (_objectById.TryGetValue(id, out var taken)
                    && taken != null && taken.TryGetTarget(out var holder) && holder != null
                    && !ReferenceEquals(holder, target))
                {
                    // 同一键位已被别的活对象绑定：追加会话内后缀避免双写覆盖。
                    // 该不透明 id 跨重启解析不到，读档会回到确定性键定位 + 指纹闸门，仍然安全。
                    id = id + "@dup:" + Guid.NewGuid().ToString("N");
                }
                BindUnsafe(target, id);
                return id;
            }
        }

        /// <summary>把记录里读回的 slot id 绑定到运行期对象（老档首次加载时的“补票”）。</summary>
        public static void Bind(System.Object target, string slotId)
        {
            if (target == null || string.IsNullOrEmpty(slotId)) return;
            lock (_lock)
            {
                // 已经绑定到别的 id 的对象不覆盖：以对象当前绑定为准，避免同一对象两条身份
                if (_idByObject.TryGetValue(target, out var current) && !string.IsNullOrEmpty(current)
                    && !string.Equals(current, slotId, StringComparison.Ordinal))
                {
                    return;
                }
                // id 已被别的活对象持有不抢占：确定性 id 在不同 Actor 的同型脚本间可能同形，
                // 抢占只会制造错命中；真正的归属由“按键定位 + 指纹闸门”裁决。
                if (_objectById.TryGetValue(slotId, out var taken)
                    && taken != null && taken.TryGetTarget(out var holder) && holder != null
                    && !ReferenceEquals(holder, target))
                {
                    return;
                }
                BindUnsafe(target, slotId);
            }
        }

        private static void BindUnsafe(System.Object target, string slotId)
        {
            _idByObject[target] = slotId;
            _objectById[slotId] = new WeakReference<System.Object>(target);
        }

        /// <summary>按 slot id 找回运行期对象（已销毁/未绑定返回 null）。</summary>
        public static System.Object Resolve(string slotId)
        {
            if (string.IsNullOrEmpty(slotId)) return null;
            lock (_lock)
            {
                if (!_objectById.TryGetValue(slotId, out var weak)) return null;
                if (weak != null && weak.TryGetTarget(out var target) && target != null) return target;
                _objectById.Remove(slotId);
                return null;
            }
        }

        /// <summary>对象销毁时清掉绑定，避免 id 表无限增长。</summary>
        public static void Unbind(System.Object target)
        {
            if (target == null) return;
            lock (_lock)
            {
                if (!_idByObject.TryGetValue(target, out var id)) return;
                _idByObject.Remove(target);
                _objectById.Remove(id);
            }
        }

        /// <summary>场景/宿主卸载时清空全部绑定（跨场景不保留身份）。</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _idByObject.Clear();
                _objectById.Clear();
            }
        }

        private static string MakeId(string stableKey)
        {
            // 确定性 id：id 直接采用“显式键 / 类型全名#同类内序号”。写侧（CaptureActorState、
            // CollectSavableComponents）与读侧（ResolveKeyInActor）用同一枚举同一计数，对同一对象
            // 恒得同值，因此无需进程内注册表即可跨重启还原身份 —— 这正是取代旧 GUID 方案的原因。
            return string.IsNullOrEmpty(stableKey) ? "c" : stableKey;
        }

        /// <summary>引用相等比较器（用 RuntimeHelpers.GetHashCode，不触发用户 Equals/GetHashCode）。</summary>
        private sealed class ReferenceKeyComparer : IEqualityComparer<System.Object>
        {
            public static readonly ReferenceKeyComparer Instance = new ReferenceKeyComparer();

            public bool Equals(System.Object a, System.Object b) => ReferenceEquals(a, b);

            public int GetHashCode(System.Object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
