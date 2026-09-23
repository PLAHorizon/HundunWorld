namespace Horizon.Game.Sim;

/// <summary>
/// 确定性公式版本协商策略（阶段 0.5 握手协议扩展）。
/// <para>
/// 双端共用此策略判断客户端上报的 <c>FormulaVersion</c> 是否与当前服务端公式兼容。
/// 决策逻辑是确定性的（纯函数），可同时在 Orleans 服务端与 UE5 DS 中无差别执行。
/// </para>
/// <para>
/// 当前策略：仅接受与 <see cref="MovementFormula.FormulaVersion"/> 精确相等的版本。
/// 未来可扩展为范围/版本号区间兼容（如 v2→v3 的前向兼容期）。
/// </para>
/// </summary>
public static class FormulaVersionPolicy
{
    /// <summary>当前服务端使用的公式版本（简写，与 <see cref="MovementFormula.FormulaVersion"/> 一致）。</summary>
    public static int Current => MovementFormula.FormulaVersion;

    /// <summary>
    /// 客户端上报的公式版本是否可接受。
    /// <paramref name="clientFormulaVersion"/> 为 0 时（默认值/未设置字段）返回 <c>false</c>。
    /// </summary>
    public static bool IsCompatible(int clientFormulaVersion)
        => clientFormulaVersion == MovementFormula.FormulaVersion;

    /// <summary>
    /// 当 <see cref="IsCompatible"/> 返回 <c>false</c> 时，给出面向用户的拒绝原因。
    /// </summary>
    public static string GetRejectionReason(int clientFormulaVersion)
    {
        var server = MovementFormula.FormulaVersion;
        return clientFormulaVersion == 0
            ? $"公式版本未设置（值为 0），服务器要求 FormulaVersion={server}；请更新客户端。"
            : $"公式版本不匹配（客户端 FormulaVersion={clientFormulaVersion}，服务器 FormulaVersion={server}），请更新客户端。";
    }
}