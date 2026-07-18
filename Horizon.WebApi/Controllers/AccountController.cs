using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Horizon.Core.Options;
using Orleans;
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
using Horizon.Core.Security;

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
        private readonly UserAuthTokenProvider _authTokenProvider;

        public AccountController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<AccountController> logger,
                                IPassportCurrentUser passportCurrent,
                                IClusterClient clusterClient,
                                UserAuthTokenProvider authTokenProvider)
                                : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
            _authTokenProvider = authTokenProvider;
        }

        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="discoverCache"></param>
        /// <param name="httpClientFactory"></param>
        /// <param name="dto"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("signin")]
        public async Task<ResultVM<LoginResultDto>> LoginAsync([FromServices] IDiscoveryCache discoverCache, [FromServices] IHttpClientFactory httpClientFactory, [FromBody] LoginDto dto)
        {
            ResultVM<LoginResultDto> result = new ResultVM<LoginResultDto>();

            if (string.IsNullOrWhiteSpace(dto.PassportId))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "通行证号不能为空";
                return result;
            }

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
                // 使用客户端上传的机器ID生成鉴权令牌（机器ID比IP更稳定，适用于NAT/动态IP场景）
                var machineId = dto.MachineId ?? string.Empty;
                var imAuthToken = TryGenerateImAuthToken(dto.PassportId, machineId);

                // 获取用户ID（用于后续角色列表等操作）
                ulong userId = 0;
                try
                {
                    var orleansClient = await OrleansConnectClient();
                    var passportGrain = orleansClient.GetGrain<IPassportGrain>(Guid.NewGuid());
                    var passportInfo = await passportGrain.AuthenticationAsync(new LoginDto
                    {
                        PassportId = dto.PassportId,
                        Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(dto.Password ?? string.Empty)),
                        AppId = dto.AppId,
                        AppType = dto.AppType,
                        PassportType = dto.PassportType,
                        VerifyCode = dto.VerifyCode ?? string.Empty,
                        Phone = dto.Phone ?? string.Empty,
                        Email = dto.Email ?? string.Empty,
                    });
                    if (passportInfo != null)
                    {
                        userId = (ulong)passportInfo.UserId;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "登录时获取UserId失败: PassportId={PassportId}", dto.PassportId);
                }

                result.Data = new LoginResultDto
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresIn = tokenResponse.ExpiresIn,
                    ExpiresTime = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn),
                    ImAuthToken = imAuthToken,
                    UserId = userId,
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
        /// <param name="expiredImAuthToken">客户端已过期的ImAuthToken（可选），用于提取machineId以签发新令牌</param>
        /// <returns></returns>
        [HttpPost]
        public async Task<ResultVM<LoginResultDto>> GetRefreshTokenAsync([FromServices] IDiscoveryCache discoverCache, [FromServices] IHttpClientFactory httpClientFactory, [FromBody] string refreshToken, [FromQuery] string expiredImAuthToken = null)
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
            {
                result.Data = new LoginResultDto
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresIn = tokenResponse.ExpiresIn,
                    ExpiresTime = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn)
                };

                // 如果客户端提供了过期的ImAuthToken，解密提取machineId后签发新的ImAuthToken
                if (_authTokenProvider != null && !string.IsNullOrWhiteSpace(expiredImAuthToken))
                {
                    try
                    {
                        var validation = _authTokenProvider.ValidateTokenWithoutExpiryCheck(expiredImAuthToken);
                        if (validation.IsValid && validation.TokenData != null)
                        {
                            var newImAuthToken = _authTokenProvider.GenerateToken(
                                validation.TokenData.PassportId,
                                validation.TokenData.MachineId,
                                validation.TokenData.CharacterId);
                            result.Data.ImAuthToken = newImAuthToken;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "刷新令牌时重新生成ImAuthToken失败，客户端需要重新登录");
                    }
                }
            }
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

            if (passportInfoDto == null)
            {
                result.ErrorMessage = "注册失败，请稍后重试";
                return result;
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
            {
                // 使用客户端上传的机器ID生成鉴权令牌（与Login对齐）
                var regMachineId = registerDto.MachineId ?? string.Empty;
                var regImAuthToken = TryGenerateImAuthToken(passportInfoDto.PassportId, regMachineId);

                result.Data = new LoginResultDto
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresIn = tokenResponse.ExpiresIn,
                    ExpiresTime = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn),
                    ImAuthToken = regImAuthToken,
                    UserId = (ulong)passportInfoDto.UserId
                };
                result.IsSuccess = true;
            }
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
        [Authorize]
        [HttpPost("user")]
        public async Task<RemoteUserEnvelope> GetUserIdAsync()
        {

            Guid.TryParse(_passportCurrent.UserId, out Guid id);
            return await Task.FromResult( new RemoteUserEnvelope
            {
                PassportId = _passportCurrent.PassportId,
                UserId = id,
                Name = _passportCurrent.Name,
                NickName = _passportCurrent.Name,
                Avatar = _passportCurrent.Avatar,
                Email = _passportCurrent.Email
            });
        }
        public sealed class RemoteUserEnvelope
        {
            public string PassportId { get; set; }
            public Guid UserId { get; set; }
            public string Name { get; set; }
            public string NickName { get; set; }
            public string Avatar { get; set; }
            public string Email { get; set; }
        }
        private string TryGenerateImAuthToken(string passportId, string machineId)
        {
            if (_authTokenProvider == null || string.IsNullOrWhiteSpace(passportId))
            {
                return string.Empty;
            }

            try
            {
                return _authTokenProvider.GenerateToken(passportId, machineId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成IM鉴权令牌失败: PassportId={PassportId}", passportId);
                return string.Empty;
            }
        }
    }
}
