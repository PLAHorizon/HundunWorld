using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Message;
using MemoryPack;
using Xunit;

namespace Horizon.Game.Gateway.Tests.Contract;

/// <summary>
/// A2 — MessageUnion 双轨对拍测试（Verification 模式）。
/// <para>
/// 目标：源生成器（<c>Horizon.Game.Message.Generator</c>）产出的 <see cref="UnionKinds"/> 常量表
/// 必须与手写 <see cref="MessageUnion"/> 上的 <see cref="MemoryPackUnionAttribute"/> 注册**完全双射一致**
/// （tag 集合 + 类型全名精确），证明"声明式 <see cref="NetworkMessageAttribute"/> → union 注册"
/// 的生成链路无漂移。
/// </para>
/// <para>
/// 迁移纪律：
/// <list type="number">
///   <item>全量 codemod（225 类）已完成，双射断言即当前基线；</item>
///   <item>切换 Generation 模式删除手写块后：本测试固定为"新消息必须用特性注册"的回归护栏
///       （tag 冲突改由生成器 HZN001 编译期拦截，本文件断言常量表非空且覆盖试点类型）。</item>
/// </list>
/// </para>
/// </summary>
public class UnionRegistrationParityTests
{
    private static Dictionary<int, Type> HandwrittenUnionTags()
    {
        return typeof(MessageUnion)
            .GetCustomAttributes(typeof(MemoryPackUnionAttribute), false)
            .Cast<MemoryPackUnionAttribute>()
            .ToDictionary(a => (int)a.Tag, a => a.Type);
    }

    private static string StripGlobalPrefix(string typeName)
        => typeName.StartsWith("global::", StringComparison.Ordinal)
            ? typeName["global::".Length..]
            : typeName;

    [Fact]
    public void GeneratedUnionKinds_Matches_HandwrittenRegistration_Exactly()
    {
        var handwritten = HandwrittenUnionTags();

        // 全量迁移完成：生成常量表必须与手写注册**双射**一致（tag 集合 + 类型全名精确）。
        Assert.Equal(handwritten.Count, UnionKinds.All.Count);

        foreach (var (tag, type) in handwritten)
        {
            Assert.True(
                UnionKinds.All.TryGetValue(tag, out var entry),
                $"生成常量表缺少手写注册的 tag={tag} ({type.FullName})");

            Assert.Equal(
                type.FullName,
                StripGlobalPrefix(entry.TypeName));
        }
    }

    [Fact]
    public void NetworkMessageTaggedTypes_AreRegisteredInUnionKinds()
    {
        // 展示了"新消息用特性声明"的链路：试点类型必须出现在常量表中。
        Assert.True(UnionKinds.All.ContainsKey(0), "LoginRequest(tag=0) 应在生成常量表中");
        Assert.True(UnionKinds.All.ContainsKey(13), "MoveRequest(tag=13) 应在生成常量表中");
        Assert.True(UnionKinds.All.ContainsKey(27), "HeartbeatMessage(tag=27) 应在生成常量表中");

        Assert.Equal(
            "Horizon.Game.Message.Network.LoginRequest",
            StripGlobalPrefix(UnionKinds.All[0].TypeName));
        Assert.Equal(3 /*ServiceType.Account*/, UnionKinds.All[0].Service);
        Assert.Equal(2 /*ServiceType.Game*/, UnionKinds.All[13].Service);
        Assert.Equal(7 /*ServiceType.System*/, UnionKinds.All[27].Service);
    }

    [Fact]
    public void HandwrittenRegistration_IsNonEmpty()
    {
        // 基线护栏：手写注册当前是事实源（225 项），不允许被误删。
        Assert.True(HandwrittenUnionTags().Count >= 200);
    }
}