using Horizon.Share.Dtos.Games;
using Orleans;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 游戏装备 Grain 接口（roleequipments 端点契约）。
    /// 登录闭环不依赖装备数据，当前实现返回空列表（角色装备查询由后续装备系统迭代补全）。
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IGameEquipmentGrain : IGrainWithGuidKey
    {
        /// <summary>查询指定角色的装备信息。</summary>
        Task<List<EquipmentInfoDto>> GetRoleEquipmentInfo(RoleEquipmentInfoQueryDto queryDto);
    }
}
