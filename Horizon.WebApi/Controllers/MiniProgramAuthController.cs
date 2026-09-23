using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using Horizon.WebApi.Configs;
using Orleans;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("api/miniprogram/[controller]")]
    public class MiniProgramAuthController : ControllerBase
    {
        private readonly ILogger<MiniProgramAuthController> _logger;
        private readonly IClusterClient _clusterClient;
        private readonly IConfiguration _configuration;

        public MiniProgramAuthController(
            ILogger<MiniProgramAuthController> logger,
            IClusterClient clusterClient,
            IConfiguration configuration)
        {
            _logger = logger;
            _clusterClient = clusterClient;
            _configuration = configuration;
        }

        // 修复（安全）：微信登录目前是 mock（伪造 wx_mock_/sk_/mp_token_，无真实 jscode2session），
        // 任何人可借此获得"已登录"身份。改为默认关闭，仅在显式配置 WechatMock:Enabled=true
        // （开发/测试环境）时开放；生产必须接入真实微信 jscode2session 后再启用。
        private bool IsWechatMockEnabled()
            => _configuration.GetValue<bool>("WechatMock:Enabled", false);

        private static string MockDisabledMessage
            => "微信小程序登录尚未接入真实 jscode2session；mock 仅在配置 WechatMock:Enabled=true 时可用。";

        [HttpPost("login")]
        public async Task<ResultVM<MiniProgramLoginResult>> WxLoginAsync([FromBody] WxLoginRequest request)
        {
            var result = new ResultVM<MiniProgramLoginResult>();
            try
            {
                if (!IsWechatMockEnabled())
                {
                    result.ErrorMessage = MockDisabledMessage;
                    return result;
                }

                var jsCode = request.Code;
                if (string.IsNullOrEmpty(jsCode))
                {
                    result.ErrorMessage = "缺少微信登录code";
                    return result;
                }

                var wxOpenId = $"wx_mock_{Guid.NewGuid():N}";
                var sessionKey = $"sk_{Guid.NewGuid():N}";

                var passportGrain = _clusterClient.GetGrain<IPassportGrain>(Guid.NewGuid());
                var loginResult = await passportGrain.AuthenticationAsync(new Horizon.Share.Dtos.User.LoginDto
                {
                    PassportId = wxOpenId,
                    Password = sessionKey
                });

                result.Data = new MiniProgramLoginResult
                {
                    OpenId = wxOpenId,
                    SessionKey = sessionKey,
                    Token = $"mp_token_{Guid.NewGuid():N}",
                    ExpiresIn = 7200
                };
                result.IsSuccess = true;

                _logger.LogInformation("小程序登录: OpenId={OpenId}", wxOpenId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "小程序登录失败");
                result.ErrorMessage = "小程序登录失败";
            }
            return result;
        }

        [HttpPost("phone")]
        public async Task<ResultVM<string>> GetPhoneNumberAsync([FromBody] WxPhoneRequest request)
        {
            var result = new ResultVM<string>();
            try
            {
                if (!IsWechatMockEnabled())
                {
                    result.ErrorMessage = MockDisabledMessage;
                    return result;
                }

                result.Data = "138****8888";
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取手机号失败");
                result.ErrorMessage = "获取手机号失败";
            }
            return result;
        }
    }

    public class WxLoginRequest
    {
        public string Code { get; set; }
        public string AppId { get; set; }
    }

    public class WxPhoneRequest
    {
        public string Code { get; set; }
        public string OpenId { get; set; }
    }

    public class MiniProgramLoginResult
    {
        public string OpenId { get; set; }
        public string SessionKey { get; set; }
        public string Token { get; set; }
        public int ExpiresIn { get; set; }
    }
}
