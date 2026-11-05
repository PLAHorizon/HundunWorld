using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// <see cref="PlanetRegionSnapshotResponse.FromSnapshot"/> 的<em>逐字段搬运</em>判据。
/// <para>为什么要反射而不是手写十个断言：手写的那十个只会覆盖"今天有的那十个"。
/// 库里给 <see cref="PlanetRegionSnapshot"/> 加第十一个字段、而忘了在摊平处赋值时，
/// 线上收到的就是该类型的默认值——<c>bool</c> 变成 false、数组变成空、<c>Code</c> 变成 None。
/// 那不会被任何编译错误拦住，只会表现为"客户端拿到一份看起来合法的快照"。
/// 反射这条判据自动覆盖加进来的字段：库里加一个、线上没有同名属性就红，
/// 线上有但没搬值也红（送进去的都是非默认的哨兵，默认值一出现就说明没搬）。</para>
/// </summary>
public class PlanetRegionSnapshotMirrorTests
{
    /// <summary><c>MessageUnion</c> 的那两项是路由用的，不参与搬运。</summary>
    private static readonly string[] Infrastructure = { "Type", "ServiceType", "MessageId", "Timestamp" };

    private static PropertyInfo[] Payload(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && !Infrastructure.Contains(p.Name)
                                   && p.DeclaringType != typeof(MessageUnion) && p.Name != "Item")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>按类型造一个<em>非默认</em>的哨兵值；seed 让每个字段的哨兵互不相同。</summary>
    private static object Sentinel(Type type, int seed)
    {
        if (type == typeof(bool)) return true;
        if (type == typeof(string)) return "mirror-sentinel-" + seed;
        if (type == typeof(ulong)) return 0x1122334400UL + (ulong)seed;
        if (type == typeof(long)) return 1234567890123L + seed;
        if (type == typeof(int)) return 700000 + seed;
        if (type == typeof(uint)) return (uint)(700000 + seed);
        if (type == typeof(short)) return (short)(300 + seed);
        if (type == typeof(ushort)) return (ushort)(300 + seed);
        if (type == typeof(byte)) return (byte)(17 + seed);
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue(values.Length - 1)!;      // 取最后一个：一定不是默认值
        }
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var array = Array.CreateInstance(elementType, 2);
            array.SetValue(Sentinel(elementType, seed * 10 + 1), 0);
            array.SetValue(Sentinel(elementType, seed * 10 + 2), 1);
            return array;
        }
        if (Type.GetTypeCode(type) == TypeCode.Object || (type.IsValueType && !type.IsPrimitive))
        {
            // 复合类型（PlanetCellKey 那种，结构体或类都算）：造一个实例，把可写的字段/属性逐个填哨兵
            var instance = Activator.CreateInstance(type)!;
            int inner = 0;
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!field.IsInitOnly) field.SetValue(instance, Sentinel(field.FieldType, seed * 31 + inner++));
            }
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.CanWrite) prop.SetValue(instance, Sentinel(prop.PropertyType, seed * 31 + inner++));
            }
            return instance;
        }
        throw new NotSupportedException("哨兵没覆盖的类型：" + type.FullName);
    }

    private static bool Same(object expected, object actual) =>
        expected is null && actual is null
        || expected is not null && expected.Equals(actual)
        || expected is IEnumerable ea && actual is IEnumerable ab && SequenceEqual(ea, ab);

    private static bool SequenceEqual(IEnumerable left, IEnumerable right)
    {
        var a = left.Cast<object?>().ToList();
        var b = right.Cast<object?>().ToList();
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!Same(a[i]!, b[i]!)) return false;
        return true;
    }

    [Fact]
    public void 摊平必须搬运每一个字段_少搬一个就红而不是安静地给默认值()
    {
        var snapshotType = typeof(PlanetRegionSnapshot);
        var responseType = typeof(PlanetRegionSnapshotResponse);

        var snapshotProps = Payload(snapshotType);
        Assert.True(snapshotProps.Length >= 8,
                    $"只认出 {snapshotProps.Length} 个可搬运字段，反射筛选本身错了（今天应有 10 个）");

        var snapshot = new PlanetRegionSnapshot();
        var sentinels = new Dictionary<string, object>();
        int seed = 1;
        foreach (var prop in snapshotProps)
        {
            object value = Sentinel(prop.PropertyType, seed++);
            prop.SetValue(snapshot, value);
            sentinels[prop.Name] = value;
        }

        var response = PlanetRegionSnapshotResponse.FromSnapshot(snapshot);

        // 正向：线上形状的每一个字段都必须等于库里那个同名字段送进去的哨兵
        foreach (var prop in Payload(responseType))
        {
            var source = snapshotType.GetProperty(prop.Name);
            Assert.True(source is not null,
                        $"线上形状有 {prop.Name}，库里没有同名字段：FromSnapshot 无从搬运，收到的一律是默认值");
            object actual = prop.GetValue(response)!;
            object expected = sentinels[prop.Name];
            Assert.True(Same(expected, actual),
                        $"{prop.Name} 没被搬运：期望哨兵 {Describe(expected)}，实得 {Describe(actual)}"
                        + "（没赋值时这里会是该类型的默认值，而那在客户端读起来像服务端真的这么判了）");
        }

        // 反向：库里加的字段线上必须有，否则它压根不上线
        foreach (var prop in snapshotProps)
            Assert.True(responseType.GetProperty(prop.Name) is not null,
                        $"库里新增了 {prop.Name}，线上形状没有它：这条字段永远到不了客户端");
    }

    [Fact]
    public void 哨兵必须是非默认的_否则上一条判据是在比两个默认值()
    {
        // 这条是上一条的防空转：把"全零"送进去时，"没搬运"和"搬运了"长得一模一样。
        var boolSentinel = Sentinel(typeof(bool), 1);
        var longSentinel = Sentinel(typeof(long), 2);
        var arraySentinel = Sentinel(typeof(long[]), 3);
        var codeSentinel = Sentinel(typeof(PlanetRegionRejectCode), 4);
        Assert.True((bool)boolSentinel);
        Assert.NotEqual(0L, (long)longSentinel);
        Assert.Equal(2, ((Array)arraySentinel).Length);
        Assert.NotEqual((PlanetRegionRejectCode)0, (PlanetRegionRejectCode)codeSentinel);
        Assert.NotEqual("", Sentinel(typeof(string), 5)!);
    }

    private static string Describe(object value) => value switch
    {
        null => "<null>",
        IEnumerable e and not string => "[" + string.Join(",", e.Cast<object?>().Take(4)) + "…]",
        _ => value.ToString() ?? "<无文本>"
    };
}
