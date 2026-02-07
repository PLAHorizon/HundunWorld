using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.Share.Commones;
using Horizon.Share.Dtos;
using Horizon.Share.Dtos.Articles;
using Horizon.Share.Dtos.User;
using Horizon.Share.VMs;
using Horizon.WebApi.Configs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.WebApi.Controllers.v1.Articles
{
    /// <summary>
    /// 用户
    /// </summary>
    [ApiGroup(ApiGroupName.Account)]
    [ApiController]
    [Authorize]
    [Route("[controller]")]
    public class UserController : OrleansControllerBase
    {
        private readonly ILogger<UserController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;
        public UserController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<UserController> logger,
                                IPassportCurrentUser passportCurrent)
                                : base(options, clusterOptions, logger)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        /// <summary>
        /// 获取用户简要信息
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<ResultVM<UserDto>> GetUserAsync()
        {
            UserQueryDto dto = new UserQueryDto
            {
                AppId = _passportCurrent.AppId,
                AppType = _passportCurrent.AppType,
                OrganizationId = _passportCurrent.OrganizationId,
                PassportId = _passportCurrent.PassportId,
                PassportType = _passportCurrent.PassportType,
            };
            UserDto resutl = null;
          var client = await OrleansConnectClient();
            {
                IUserGrain user = client.GetGrain<IUserGrain>(Guid.NewGuid());
                resutl = await user.GetUserInfoAsync(dto);
               
            }
            return new ResultVM<UserDto> { Data = resutl, ErrorMessage = null };
        }
        /// <summary>
        /// 分页 获取用户列表
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [HttpPost("openusers")]
        public async Task<ResultVM<IPageItems<OpenUserDto>>> PageArticleCategoryAsync([FromBody] UserQueryDto queryDto)
        {
            IPageItems<OpenUserDto> resutl = null;
            var client = await OrleansConnectClient();
            {
                IUserGrain category = client.GetGrain<IUserGrain>(Guid.NewGuid());
                resutl = await category.GetOpenUsersAsync(queryDto);
                
            }
            return new ResultVM<IPageItems<OpenUserDto>> { Data = resutl, ErrorMessage = null };
        }

        /// <summary>
        /// 修改用户信息
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [HttpPut]
        public async Task<ResultVM<bool>> ChangeUserInfoAsync([FromBody] ChangeUserInfo dto)
        {
            bool resutl = false;
          var client = await OrleansConnectClient();
            {
                IUserGrain user = client.GetGrain<IUserGrain>(Guid.NewGuid());
                resutl = await user.ChangeUserInfoAsync(dto);
               
            }
            return new ResultVM<bool> { Data = resutl, ErrorMessage = null };
        }

    }
}
