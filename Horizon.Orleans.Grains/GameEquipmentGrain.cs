using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.Games;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 游戏装备 Grain：roleequipments 端点契约实现。
    /// <para>
    /// 权威数据在 <see cref="IInventoryGrain"/>（角色背包 grain 的 EquippedItems/EquippedItemDetails）。
    /// 此前本 grain 恒返回空列表，导致"服务端已记录穿了什么"这件事对外完全不可见。
    /// </para>
    /// </summary>
    public class GameEquipmentGrain : Grain, IGameEquipmentGrain
    {
        private readonly ILogger<GameEquipmentGrain> _logger;

        public GameEquipmentGrain(ILogger<GameEquipmentGrain> logger)
        {
            _logger = logger;
        }

        public async Task<List<EquipmentInfoDto>> GetRoleEquipmentInfo(RoleEquipmentInfoQueryDto queryDto)
        {
            var characterId = queryDto?.UserRoleId ?? 0;
            if (characterId <= 0)
            {
                _logger.LogWarning("GameEquipment 查询缺少角色 ID，返回空列表。");
                return new List<EquipmentInfoDto>();
            }

            try
            {
                // 背包 grain 用 Guid 键；换算集中在 GrainKeyMapper，勿另写一套。
                var inventoryKey = Horizon.Game.Core.Sim.Server.GrainKeyMapper.ToInventoryKey((ulong)characterId);
                var equipped = await GrainFactory.GetGrain<IInventoryGrain>(inventoryKey).GetEquippedItemInfosAsync();

                var result = new List<EquipmentInfoDto>(equipped.Count);
                foreach (var item in equipped)
                {
                    result.Add(new EquipmentInfoDto
                    {
                        Id = item.ItemId,
                        Name = item.Name,
                        Description = item.Description,
                        // 物品类型/品质与装备枚举同源编号（均为 int 语义），越界值按 0 号成员兜底。
                        EquipmentType = Enum.IsDefined(typeof(Horizon.Share.Enums.Game.EquipmentType), item.ItemType)
                            ? (Horizon.Share.Enums.Game.EquipmentType)item.ItemType
                            : Horizon.Share.Enums.Game.EquipmentType.Head,
                        Quality = Enum.IsDefined(typeof(Horizon.Share.Enums.Game.EquipmentQuality), item.Quality)
                            ? (Horizon.Share.Enums.Game.EquipmentQuality)item.Quality
                            : Horizon.Share.Enums.Game.EquipmentQuality.White,
                    });
                }

                _logger.LogInformation(
                    "GameEquipment 查询完成。UserRoleId={UserRoleId} 已装备 {Count} 件。",
                    characterId, result.Count);
                return result;
            }
            catch (Exception ex)
            {
                // 读失败退化为空列表：登录闭环不依赖装备数据，不该因它把玩家挡在游戏外。
                _logger.LogError(ex,
                    "GameEquipment 查询失败，返回空列表。UserRoleId={UserRoleId}", characterId);
                return new List<EquipmentInfoDto>();
            }
        }
    }
}
