using System;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 阶段 6.3 P2 — 自动回滚门 override 协调器接口。
/// <para>
/// 数据面（<see cref="DsSyncDataPlane.IsEnabled"/>）与判定宿主（<see cref="RollbackGateHostedService"/>）
/// 通过本接口共享「是否处于自动回切 override」状态：override 生效期间数据面强制不走 DS（即使
/// UseDS=true），冷却到期后由判定宿主评估健康窗口并调用 <see cref="Clear"/> 自愈恢复。
/// </para>
/// </summary>
public interface IRollbackCoordinator
{
    /// <summary>
    /// 数据面实时读取：是否强制回切（= 当前 UseDS=true 且 override 处于生效期）。
    /// 冷却到期后返回 false，允许判定宿主评估恢复窗口。
    /// </summary>
    bool IsRollbackActive { get; }

    /// <summary>override 是否处于生效期（诊断/测试；与 <see cref="IsRollbackActive"/> 的差异仅在于 UseDS 开关）。</summary>
    bool IsOverrideArmed { get; }

    /// <summary>
    /// 触发回滚：设置 override 并启动冷却。仅当当前配置 UseDS=true 时生效
    /// （开关本已关闭时回滚无意义，且会阻碍后续重新开启）。重复调用刷新冷却。
    /// </summary>
    void Arm();

    /// <summary>自愈恢复：清除 override（由判定宿主在冷却到期且连续健康窗口达标时调用）。</summary>
    void Clear();
}
