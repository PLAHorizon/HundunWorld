using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using UObject = CUE4Parse.UE4.Assets.Exports.UObject;

namespace UE5ToFlaxConverter.Core.Readers;

/// <summary>
/// UE5 资源读取器共享的反射辅助方法。
/// 提供字段/属性查询与缓存，避免在热路径（顶点循环）上重复反射查找。
/// </summary>
internal static class ReflectionHelper
{
    /// <summary>
    /// 成员查找缓存键：(Type, MemberName) → MemberInfo。
    /// 使用 ConditionalWeakTable 保证 Type 卸载时缓存自动随之回收，避免内存泄漏。
    /// </summary>
    private static readonly ConditionalWeakTable<Type, ConcurrentDictionary<string, MemberInfo?>> _cache = new();

    /// <summary>
    /// 按名称读取对象字段或属性值（公有与非公有实例成员均可）。
    /// 找不到时返回 null；成员为 null 引用时也返回 null。
    /// </summary>
    /// <param name="obj">目标对象，若为 null 直接返回 null。</param>
    /// <param name="name">字段或属性名（区分大小写）。</param>
    public static object? GetMember(object? obj, string name)
    {
        if (obj is null) return null;
        var member = ResolveMember(obj.GetType(), name);
        return member switch
        {
            FieldInfo f => f.GetValue(obj),
            PropertyInfo p => p.CanRead ? p.GetValue(obj) : null,
            _ => null
        };
    }

    /// <summary>
    /// 强类型读取：读取成员并尝试转换为 <typeparamref name="T"/>。
    /// 转换失败或成员不存在返回 <paramref name="defaultValue"/>。
    /// </summary>
    public static T? GetMember<T>(object? obj, string name, T? defaultValue = default)
    {
        var value = GetMember(obj, name);
        if (value is null) return defaultValue;
        if (value is T typed) return typed;
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// 读取可枚举成员，无法解析为可枚举时返回 null。
    /// </summary>
    public static System.Collections.IEnumerable? GetEnumerableMember(object? obj, string name)
        => GetMember(obj, name) as System.Collections.IEnumerable;

    /// <summary>
    /// 获取 UObject 的类名。
    /// CUE4Parse 中 Class 字段为 ResolvedObject，其 ToString() 返回类名。
    /// 失败时返回空字符串而非抛出异常。
    /// </summary>
    public static string GetClassName(UObject obj)
    {
        try
        {
            var classField = obj.GetType().GetField("Class", BindingFlags.Public | BindingFlags.Instance);
            var classValue = classField?.GetValue(obj);
            return classValue?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 安全地将浮点成员读取为 float。
    /// 支持 float/double/int/byte 等数值类型，并裁剪到 0..1 之间的浮点字段（如颜色分量）可由 caller 自行处理。
    /// </summary>
    public static float GetSingle(object? obj, string name, float defaultValue = 0f)
    {
        var v = GetMember(obj, name);
        return v switch
        {
            null => defaultValue,
            float f => f,
            double d => (float)d,
            int i => i,
            byte b => b,
            _ => float.TryParse(v.ToString(), out var parsed) ? parsed : defaultValue
        };
    }

    /// <summary>
    /// 安全地将对象直接转换为 float（无需读取成员，用于已取出的数组元素）。
    /// 支持 float/double/int/byte 等数值类型。
    /// </summary>
    public static float GetSingle(object? obj, float defaultValue = 0f)
    {
        return obj switch
        {
            null => defaultValue,
            float f => f,
            double d => (float)d,
            int i => i,
            byte b => b,
            _ => float.TryParse(obj.ToString(), out var parsed) ? parsed : defaultValue
        };
    }

    /// <summary>
    /// 安全地读取 int 成员。
    /// </summary>
    public static int GetInt32(object? obj, string name, int defaultValue = 0)
    {
        var v = GetMember(obj, name);
        return v switch
        {
            null => defaultValue,
            int i => i,
            uint u => (int)u,
            long l => (int)l,
            byte b => b,
            _ => int.TryParse(v.ToString(), out var parsed) ? parsed : defaultValue
        };
    }

    private static MemberInfo? ResolveMember(Type type, string name)
    {
        var perType = _cache.GetOrCreateValue(type);
        return perType.GetOrAdd(name, static (n, t) =>
        {
            var field = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field is not null) return field;
            return (MemberInfo?)t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }, type);
    }
}
