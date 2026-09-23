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
    /// 登录闭环不依赖装备数据，当前返回空列表（角色装备系统迭代时补全）。
    /// </summary>
    public class GameEquipmentGrain : Grain, IGameEquipmentGrain
    {
        private readonly ILogger<GameEquipmentGrain> _logger;

        public GameEquipmentGrain(ILogger<GameEquipmentGrain> logger)
        {
            _logger = logger;
        }

        public Task<List<EquipmentInfoDto>> GetRoleEquipmentInfo(RoleEquipmentInfoQueryDto queryDto)
        {
            _logger.LogInformation("GameEquipment 查询（当前空实现）。UserRoleId={UserRoleId}", queryDto?.UserRoleId ?? 0);
            return Task.FromResult(new List<EquipmentInfoDto>());
        }
    }
}
