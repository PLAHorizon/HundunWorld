using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// <see cref="ICharacterStateBridge"/> 的默认实现。<br/>
/// 通过 <see cref="IGrainFactory"/> 调用 <see cref="ICharacterGrain"/>，
/// 为 <see cref="ZoneShardGrain"/> 提供与 RPG 权威（CharacterGrain）的通信能力。
/// </summary>
/// <remarks>
/// 本服务通过 DI 注入 ZoneShardGrain（Singleton 生命周期），
/// 内部每次调用通过 GrainFactory 获取目标 CharacterGrain 引用。
/// </remarks>
public sealed class CharacterStateBridge : ICharacterStateBridge
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<CharacterStateBridge> _logger;

    public CharacterStateBridge(IGrainFactory grainFactory, ILogger<CharacterStateBridge> logger)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task OnEnterZoneAsync(long characterId, long zoneShardId, int initialHp)
    {
        try
        {
            var characterGrain = _grainFactory.GetGrain<ICharacterGrain>(characterId);
            await characterGrain.OnEnterZoneAsync(zoneShardId);

            _logger.LogDebug(
                "CharacterStateBridge: 角色 {CharacterId} 进入空间 ZoneShard={ZoneShardId}，已通知 CharacterGrain。",
                characterId, zoneShardId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CharacterStateBridge: 通知角色 {CharacterId} 进入空间失败（ZoneShard={ZoneShardId}）。",
                characterId, zoneShardId);
        }
    }

    /// <inheritdoc />
    public async Task OnLeaveZoneAsync(long characterId, long zoneShardId, int finalHp, ZoneLeaveReason reason)
    {
        try
        {
            var characterGrain = _grainFactory.GetGrain<ICharacterGrain>(characterId);
            await characterGrain.OnLeaveZoneAsync(zoneShardId, finalHp, reason);

            _logger.LogDebug(
                "CharacterStateBridge: 角色 {CharacterId} 离开空间 ZoneShard={ZoneShardId}（原因={Reason}，最终HP={FinalHp}），已通知 CharacterGrain。",
                characterId, zoneShardId, reason, finalHp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CharacterStateBridge: 通知角色 {CharacterId} 离开空间失败（ZoneShard={ZoneShardId}，原因={Reason}）。",
                characterId, zoneShardId, reason);
        }
    }

    /// <inheritdoc />
    public async Task<HpChangeResult> RequestHpChangeAsync(long characterId, int hpDelta, ulong sourceId, DamageType damageType)
    {
        try
        {
            var characterGrain = _grainFactory.GetGrain<ICharacterGrain>(characterId);
            var result = await characterGrain.RequestHpChangeAsync(hpDelta, sourceId, damageType);

            _logger.LogDebug(
                "CharacterStateBridge: 角色 {CharacterId} HP 变更请求（Delta={HpDelta}，来源={SourceId}，类型={DamageType}）→ 实际={ActualDelta}，当前HP={CurrentHp}，死亡={IsDead}。",
                characterId, hpDelta, sourceId, damageType, result.ActualDelta, result.CurrentHp, result.IsDead);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CharacterStateBridge: 角色 {CharacterId} HP 变更请求失败（Delta={HpDelta}，来源={SourceId}）。",
                characterId, hpDelta, sourceId);

            // 失败时返回保守结果：不造成伤害
            return new HpChangeResult(0, 0, 0, false, true);
        }
    }
}
