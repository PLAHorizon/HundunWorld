using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Core.Options;
using Horizon.Share.Dtos.Games;
using Horizon.Share.Dtos.User;
using Horizon.Share.VMs;
using Horizon.WebApi.Configs;
using Horizon.Orleans.Interface;
using Horizon.WebApi.Identity.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.WebApi.Controllers.Games
{
    [ApiGroup(ApiGroupName.Games)]
    [ApiController]
    [Route("[controller]")]
    public class GameUserRoleController : OrleansControllerBase
    {
        private readonly ILogger<GameUserRoleController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;

        public GameUserRoleController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<GameUserRoleController> logger,
                                IClusterClient clusterClient,
                                IPassportCurrentUser passportCurrent)
                                : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        /// <summary>
        /// 注册（构建）游戏用户：通行证身份取自 JWT（Bearer），客户端登录后调用此端点创建游戏用户记录。
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("register")]
        public async Task<ResultVM<GameUserInfoDto>> RegisterGameUserAsync(GameRegisterDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var result = await passport.RegisterPassportAndGameUserAsync(queryDto, _passportCurrent.PassportId);

                    return new ResultVM<GameUserInfoDto> { Data = result, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<GameUserInfoDto> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 获取游戏用户信息
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("gameuser")]
        public async Task<ResultVM<GameUserInfoDto>> GetGameUserAsync(GameUserQeuryDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var result = await passport.GetGameUserAsync(queryDto);

                    return new ResultVM<GameUserInfoDto> { Data = result, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<GameUserInfoDto> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 获取游戏内的基础角色目录
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("gameroles")]
        public async Task<ResultVM<GameRoleNormalDto[]>> GetGameRolesAsync(GameRoleQueryDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var result = await passport.GetGameRolesAsync(queryDto);

                    return new ResultVM<GameRoleNormalDto[]> { Data = result, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<GameRoleNormalDto[]> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 获取游戏用户的角色列表
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("gameuserroles")]
        public async Task<ResultVM<GameUserRoleInfoDto[]>> GetGameUserRolesAsync(GameUserRoleQueryDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var result = await passport.GetGameUserRolesAsync(queryDto);

                    return new ResultVM<GameUserRoleInfoDto[]> { Data = result, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<GameUserRoleInfoDto[]> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 获取游戏用户的角色世界信息
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("getgamerole")]
        public async Task<ResultVM<GameRoleWorldInfoDto>> GetGameRoleWorldInfoAsync(GameUserRoleQueryDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var result = await passport.GetGameRoleWorldInfoAsync(queryDto);

                    return new ResultVM<GameRoleWorldInfoDto> { Data = result, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<GameRoleWorldInfoDto> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 设置游戏用户的角色世界信息
        /// </summary>
        /// <param name="roleDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("setgamerole")]
        public async Task<ResultVM<bool>> SetGameRoleWorldInfoAsync(GameRoleWorldInfoDto roleDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var result = await passport.SetGameRoleWorldInfoAsync(roleDto);

                    return new ResultVM<bool> { Data = result, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<bool> { Data = false, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 游戏用户创建角色
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("createroles")]
        public async Task<ResultVM<bool>> CreateGameUserRolesAsync(GameRoleDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var result = await passport.CreateGameRoleAsync(queryDto);

                    return new ResultVM<bool> { Data = result, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<bool> { Data = false, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 获取游戏角色指定穿戴的装备信息
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("roleequipments")]
        public async Task<ResultVM<List<EquipmentInfoDto>>> GetRoleEquipmentInfoAsync(RoleEquipmentInfoQueryDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameEquipmentGrain passport = client.GetGrain<IGameEquipmentGrain>(Guid.NewGuid());
                    var result = await passport.GetRoleEquipmentInfo(queryDto);

                    return new ResultVM<List<EquipmentInfoDto>> { Data = result, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<List<EquipmentInfoDto>> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }
    }
}