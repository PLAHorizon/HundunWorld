using Horizon.Game.Core.Sim.Server;

namespace Horizon.NpcCli;

/// <summary>
/// <c>validate</c> 的回执口径：把"问题"与"会被摘掉的服务位"分成两种信号。
/// <para>
/// <b>为什么摘位要单独说</b>：装配期会把"开了商人/训练/配方位但内容为空"的位摘掉
/// （<c>NpcServiceCore.PruneDataEmpty</c>），这是内容清洗而不是错误。可作者在编辑器里勾着位、
/// 保存也能过，只有发射到世界时才发现入口没了。把它写进校验回执，作者在保存前就能看见。
/// </para>
/// </summary>
public static class NpcValidateReport
{
    /// <summary>校验结论的一句话（编辑器状态栏与 CLI message 共用）。</summary>
    public static string Message(NpcContentLoadReceipt receipt)
    {
        if (receipt.ProblemCount > 0) return $"发现 {receipt.ProblemCount} 个问题";
        if (receipt.PrunedServiceBits > 0)
            return $"校验通过（{receipt.PrunedServiceBits} 个服务位因内容为空将被摘除）";
        return "校验通过";
    }

    /// <summary>逐条摘位明细（空表 = 没有被摘的位）。</summary>
    public static string[] PrunedServices(NpcContentLoadReceipt receipt)
        => receipt.PrunedServices.ToArray();
}
