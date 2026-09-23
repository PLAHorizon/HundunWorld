using System;

namespace Horizon.Orleans.Interface;

/// <summary>
/// 标记 grain 接口方法为<b>幂等</b>：Silo 的 <c>RetryFilter</c> 仅对标注了本特性的方法
/// 在失败时安全重试（幂等操作重复执行不产生重复副作用）。
/// <para>
/// 非幂等变更（创建/自增/扣减/写状态等）<b>严禁</b>标注本特性，以确保它们被精确执行一次；
/// 默认（未标注）即不重试——这是对"全局 3× 重试会重放非幂等副作用"缺陷的修复。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class IdempotentAttribute : Attribute
{
}
