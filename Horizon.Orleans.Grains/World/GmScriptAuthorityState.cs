using System;
using System.Collections.Generic;
using MemoryPack;
using Orleans;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// 脚本化世界生成的授权热层状态（Orleans 持久化 → Redis 存储提供程序）。
/// <para>
/// 分层职责：<b>本状态是热层</b>，裁决读路径只碰它；<c>gm_script_grant</c> 表是权威层，
/// 授予/回收先落表再刷这里，Grain 激活时若热层为空则从表重建（设计方案 §4.4）。
/// </para>
/// </summary>
/// <remarks>
/// 项目规范：Grain 状态类需同时标注 <c>MemoryPackable</c> 与 <c>MemoryPackOrder</c>。
/// </remarks>
[MemoryPackable]
[GenerateSerializer]
public partial class GmScriptAuthorityState
{
    /// <summary>操作者角色 ID → 授权与预算状态。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public Dictionary<long, Horizon.Orleans.Interface.World.GmScriptGrantState> Operators { get; set; } = new();

    /// <summary>
    /// GM / 脚本操作审计（**有界环形**，最新在末尾）。
    /// <para>
    /// 为什么必须进持久层而不是只放 Grain 字段：Orleans 会按需**去激活**空闲 Grain，
    /// 进程内列表随之消失——于是"授予了什么、谁在什么时候造了怪"这类问题在重启或去激活后
    /// 一律查无记录，而 <c>/audit</c> 会如实返回空表，看起来像"没人做过"。
    /// 授权面要求可核查，因此审计与授权同层存 Redis。
    /// </para>
    /// <para>
    /// 只保留最近若干条：这是**运营留痕**不是合规归档，长期留存依赖 Silo 日志（每次授予/拒绝都有
    /// Warning/Information 行）。条数上限见 <c>GmCommandGrain.MaxDurableAuditEntries</c>。
    /// </para>
    /// </summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public List<Horizon.Orleans.Interface.World.GmAuditEntry> AuditLog { get; set; } = new();
}
