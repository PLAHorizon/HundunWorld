using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Horizon.Core.Options;
using Orleans.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading;
using Horizon.Share.Dtos;
using Horizon.Share.Dtos.User;
using System.Net.Http;
using IdentityModel.Client;
using Horizon.WebApi.Identity;
using Microsoft.AspNetCore.Authorization;
using Horizon.Share.VMs;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Core;
using Horizon.WebApi.Configs;
using Horizon.Orleans.Interface;

namespace Horizon.WebApi.Controllers
{
    /// <summary>
    /// 通行证
    /// </summary>
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    public class AccountController : OrleansControllerBase
    {
        private readonly ILogger<AccountController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;
        public AccountController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<AccountController> logger,
                                IPassportCurrentUser passportCurrent)
                                : base(options, clusterOptions, logger)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="discoverCache"></param>
        /// <param name="httpClientFactory"></param>
        /// <param name="dto"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("sigin")]
        public async Task<ResultVM<LoginResultDto>> LoginAsync([FromServices] IDiscoveryCache discoverCache, [FromServices] IHttpClientFactory httpClientFactory, [FromBody] LoginDto dto)
        {
            ResultVM<LoginResultDto> result = new ResultVM<LoginResultDto>();

            var httpClient = httpClientFactory.CreateClient();

            var disco = await discoverCache.GetAsync();

            var tokenResponse = await httpClient.RequestPasswordTokenAsync(new PasswordTokenRequest
            {
                Address = disco.TokenEndpoint,
                ClientId = Config.ClientId,
                ClientSecret = Config.ClientSecret,
                UserName = dto.PassportId,
                Password = dto.Password,
                Parameters = new Dictionary<string, string> {
                    { "appId", $"{dto.AppId}" },
                    { "appType", $"{(int)dto.AppType}" },
                    { "passportType", $"{(int)dto.PassportType}" },
                    { "verifyCode", $"{dto.VerifyCode}" },
                    { "phone", $"{dto.Phone}" },
                    { "email", $"{dto.Email}" }, },
                Scope = $"{Config.OfflineAccess} {Config.Scope}"
            });

            //有错误
            if (tokenResponse.IsError)
            {
                result.ErrorMessage = tokenResponse.ErrorDescription;
                result.IsSuccess = (tokenResponse.Exception == null && tokenResponse.ErrorType != ResponseErrorType.Exception);
            }
            else
            {
                result.Data = new LoginResultDto
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresIn = tokenResponse.ExpiresIn,
                    ExpiresTime = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn)
                };
                result.IsSuccess = true;
            }
            return result;
        }


        /// <summary>
        /// 通过refresh-token获取新的令牌
        /// </summary>
        /// <param name="discoverCache"></param>
        /// <param name="httpClientFactory"></param>
        /// <param name="refreshToken"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<ResultVM<LoginResultDto>> GetRefreshTokenAsync([FromServices] IDiscoveryCache discoverCache, [FromServices] IHttpClientFactory httpClientFactory, [FromBody] string refreshToken)
        {
            ResultVM<LoginResultDto> result = new ResultVM<LoginResultDto>();

            var httpClient = httpClientFactory.CreateClient();

            var disco = await discoverCache.GetAsync();
            var tokenResponse = await httpClient.RequestRefreshTokenAsync(new RefreshTokenRequest
            {
                Address = disco.TokenEndpoint,
                ClientId = Config.ClientId,
                ClientSecret = Config.ClientSecret,
                RefreshToken = refreshToken
            });

            //有错误
            if (tokenResponse.IsError)
            {
                result.ErrorMessage = tokenResponse.ErrorDescription;
                result.IsSuccess = (tokenResponse.Exception == null && tokenResponse.ErrorType != ResponseErrorType.Exception);
            }
            else
                result.Data = new LoginResultDto
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresIn = tokenResponse.ExpiresIn,
                    ExpiresTime = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn)
                };
            return result;
        }
        /// <summary>
        /// 注册
        /// </summary>
        /// <param name="discoverCache"></param>
        /// <param name="httpClientFactory"></param>
        /// <param name="registerDto"></param>
        /// <returns></returns>
        [HttpPost("register")]
        public async Task<ResultVM<LoginResultDto>> RegisterAsync([FromServices] IDiscoveryCache discoverCache, [FromServices] IHttpClientFactory httpClientFactory, [FromBody] RegisterDto registerDto)
        {
            if (registerDto == null) throw new ArgumentNullException(nameof(registerDto));
            if (string.IsNullOrWhiteSpace(registerDto.Email) &&
                string.IsNullOrWhiteSpace(registerDto.Phone))
                throw new ArgumentNullException($"{nameof(registerDto.Phone)}或{nameof(registerDto.Email)}");
            ResultVM<LoginResultDto> result = new ResultVM<LoginResultDto>();
            PassportInfoDto passportInfoDto = null;
            var client = await OrleansConnectClient();
            {
                IPassportGrain passport = client.GetGrain<IPassportGrain>(Guid.NewGuid());
                passportInfoDto = await passport.RegisterAsync(registerDto);

            }

            var httpClient = httpClientFactory.CreateClient();

            var disco = await discoverCache.GetAsync();

            var tokenResponse = await httpClient.RequestPasswordTokenAsync(new PasswordTokenRequest
            {
                Address = disco.TokenEndpoint,
                ClientId = Config.ClientId,
                ClientSecret = Config.ClientSecret,
                UserName = passportInfoDto.PassportId,
                Password = registerDto.Password,
                Parameters = new Dictionary<string, string> {
                    { "appId", $"{registerDto.AppId}" },
                    { "appType", $"{(int)registerDto.AppType}" },
                    { "passportType", $"{(int)registerDto.PassportType}" },
                    { "verifyCode", $"-" },
                    { "phone", $"{registerDto.Phone}" },
                    { "email", $"{registerDto.Email}" }, },
                Scope = $"{Config.OfflineAccess} {Config.Scope}"
            });

            //有错误
            if (tokenResponse.IsError)
            {
                result.ErrorMessage = tokenResponse.ErrorDescription;
                result.IsSuccess = (tokenResponse.Exception == null && tokenResponse.ErrorType != ResponseErrorType.Exception);
            }
            else
                result.Data = new LoginResultDto
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresIn = tokenResponse.ExpiresIn,
                    ExpiresTime = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn)
                };
            return result;
        }

        /// <summary>
        /// 退出登录
        /// </summary>
        /// <returns></returns>
        [Authorize]
        [HttpPost("signout")]
        public async Task<ResultVM<bool>> SignOutAsync()
        {
            bool resutl = false;
            LoginDto dto = new LoginDto
            {
                AppId = _passportCurrent.AppId,
                AppType = _passportCurrent.AppType,
                PassportType = _passportCurrent.PassportType,
                PassportId = _passportCurrent.PassportId,
            };
            var client = await OrleansConnectClient();
            {
                IPassportGrain passport = client.GetGrain<IPassportGrain>(Guid.NewGuid());
                resutl = await passport.SignOutAsync(dto);

            }
            return new ResultVM<bool> { Data = resutl, ErrorMessage = null };
        }

        /// <summary>
        /// 修改密码
        /// </summary>
        ///<param name="cdto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("changepassword")]
        public async Task<ResultVM<bool>> ChangePasswordAsync([FromBody] ChangePasswordDto cdto)
        {
            bool resutl = false;
            ChangePasswordDto dto = new ChangePasswordDto
            {
                AppId = _passportCurrent.AppId,
                AppType = _passportCurrent.AppType,
                PassportType = _passportCurrent.PassportType,
                PassportId = _passportCurrent.PassportId,
                OldPassword = PassportHelper.SetPasportPassword(_passportCurrent.PassportId, cdto.OldPassword),
                NewPassword = PassportHelper.SetPasportPassword(_passportCurrent.PassportId, cdto.NewPassword),
            };
            var client = await OrleansConnectClient();
            {
                IPassportGrain passport = client.GetGrain<IPassportGrain>(Guid.NewGuid());
                resutl = await passport.ChangePasswordAsync(dto);

            }
            return new ResultVM<bool> { Data = resutl, ErrorMessage = null };
        }

        /// <summary>
        /// 生成通行证
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("creating")]
        public async Task<ResultVM<bool>> CreatingAsync([FromBody] int count = 1000)
        {
            if (_passportCurrent.PassportType == PassportType.System)
            {
                var client = await OrleansConnectClient();
                {
                    IPassportGrain passport = client.GetGrain<IPassportGrain>(Guid.NewGuid());
                    await passport.CreatePassportIdAsync(count);

                }
                return new ResultVM<bool> { Data = true, ErrorMessage = string.Empty };
            }
            return new ResultVM<bool> { Data = false, ErrorMessage = "无权执行此操作！" };
        }
        /// <summary>
        /// 取消生成
        /// </summary>
        /// <returns></returns>
        [Authorize]
        [HttpPost("cancelcreating")]
        public async Task<ResultVM<bool>> CancelCreatingAsync()
        {
            if (_passportCurrent.PassportType == PassportType.System)
            {
                var client = await OrleansConnectClient();
                {
                    IPassportGrain passport = client.GetGrain<IPassportGrain>(Guid.NewGuid());
                    await passport.CancelCreatePassportIdAsync();

                }
                return new ResultVM<bool> { Data = true, ErrorMessage = string.Empty };
            }
            return new ResultVM<bool> { Data = false, ErrorMessage = "无权执行此操作！" };
        }

        /// <summary>
        /// 注销通行证
        /// </summary>
        /// <returns></returns>
        [Authorize]
        [HttpPost("cancel")]
        public async Task<ResultVM<bool>> CancelPassportAsync()
        {
            bool result = false;
            var client = await OrleansConnectClient();
            {
                IPassportGrain passport = client.GetGrain<IPassportGrain>(Guid.NewGuid());
                result = await passport.CancelPassportAsync(_passportCurrent.PassportId);

            }
            return new ResultVM<bool> { Data = result, ErrorMessage = result ? "注销完成" : "注销失败！" };

        }
    }
}
