using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.Pets;

/// <summary>
/// <see cref="IPetBonusReader"/> 的 Orleans 实现：按主人 id 找名册 Grain 取账。
/// <para>这一层唯一的责任是"把跨 Grain 调用变成可注入、可失败上报的读数"。
/// 失败一律回<b>空账 + Issues</b>：属性面板不能因为宠物读不到就连攻击都不发，
/// 但也不许把"读不到"洗成"没有宠"——那种情况下 Issues 必须非空（记忆
/// <c>skipped-checks-must-surface-in-results</c>）。</para>
/// </summary>
public sealed class PetBonusReader : IPetBonusReader
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<PetBonusReader> _logger;

    public PetBonusReader(IGrainFactory grainFactory, ILogger<PetBonusReader> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PetRosterSnapshotDto> ReadAsync(long ownerCharacterId, CancellationToken cancellationToken = default)
    {
        if (ownerCharacterId <= 0)
        {
            // 角色 id 为 0 说明角色行还没拿到——不去查（名册 Grain 键为 0 会命中一条无主的空账，
            // 那种"看起来正常"的空账会把"角色没装配"这个真问题洗成"玩家没宠"）。
            return Empty("角色 id 无效（" + ownerCharacterId + "），本次未查询宠物名册");
        }

        try
        {
            var grain = _grainFactory.GetGrain<IPetRosterGrain>(ownerCharacterId);
            return await grain.GetSnapshotAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PetBonusReader: 读角色 {Character} 的宠物名册失败，按空账处理。", ownerCharacterId);
            return Empty("读宠物名册异常：" + ex.Message);
        }
    }

    private static PetRosterSnapshotDto Empty(string issue) => new()
    {
        Issues = issue,
        ReadSideVersion = PetRosterMapper.Version,
    };
}
