using System;
using Horizon.Game.Message.Enums;

namespace Horizon.Game.Message;

/// <summary>
/// 声明式网络消息注册（A2 源生成器驱动）。
/// <para>
/// 在消息类型上标注本特性后，<c>Horizon.Game.Message.Generator</c> 会在编译期把它注册进
/// <see cref="MessageUnion"/>（生成 <see cref="MemoryPackUnionAttribute"/> 特性），
/// 并产出 <c>UnionKinds</c> 常量表。tag 冲突/越界由生成器以编译错误（HZN001）拦截。
/// </para>
/// <para>
/// 迁移期纪律：既有手写 <see cref="MessageUnion"/> 注册在双轨对拍测试通过前不得删除；
/// 新消息一律使用本特性，禁止手写追加 <c>[MemoryPackUnion(...)]</c>。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NetworkMessageAttribute : Attribute
{
    /// <param name="tag">MemoryPack union tag（全解决方案唯一，0..65534——header 是压缩 ushort，0xffff 留作 null）。</param>
    /// <param name="service">消息所属服务域（用于生成常量表与文档）。</param>
    public NetworkMessageAttribute(int tag, ServiceType service)
    {
        Tag = tag;
        Service = service;
    }

    /// <summary>MemoryPack union tag（全解决方案唯一）。</summary>
    public int Tag { get; }

    /// <summary>消息所属服务域。</summary>
    public ServiceType Service { get; }
}