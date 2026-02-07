using Horizon.Core;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Core.Options;
using Horizon.Orleans.Interface;
using Horizon.Share.Commones;
using Horizon.Share.Dtos.User;
using Horizon.WebApi.Identity.Users;
using IdentityServer4.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Horizon.WebApi.Identity
{
    public class ResourceOwnerPasswordValidator : OrleansControllerBase, IResourceOwnerPasswordValidator
    {
        private readonly ILogger<ResourceOwnerPasswordValidator> _logger;
        private readonly PassportSecurityOptions _security;
        public ResourceOwnerPasswordValidator(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                IOptions<PassportSecurityOptions> security,
                                ILogger<ResourceOwnerPasswordValidator> logger)
                                : base(options, clusterOptions, logger)
        {
            _logger = logger;
            _security = security.Value;
        }

        public async Task ValidateAsync(ResourceOwnerPasswordValidationContext context)
        {
            PassportInfoDto user = default(PassportInfoDto);
            var client = await OrleansConnectClient();
            try
            {

                IPassportGrain passport = client.GetGrain<IPassportGrain>(Guid.NewGuid());
                user = await passport.AuthenticationAsync(new LoginDto
                {
                    PassportId = context.UserName,
                    Password = PassportHelper.SetPasportPassword(context.UserName, context.Password),
                    AppId = long.Parse(context.Request.Raw.Get(0)),
                    AppType = (AppType)int.Parse(context.Request.Raw.Get(1)),
                    PassportType = (PassportType)int.Parse(context.Request.Raw.Get(2)),
                    VerifyCode = context.Request.Raw.Get(3),
                    Phone = context.Request.Raw.Get(4),
                    Email = context.Request.Raw.Get(5),
                });

            }
            catch (Exception ex)
            {

            }

            if (user != null)
                context.Result = new GrantValidationResult(
                                         subject: user.PassportId,
                                         authenticationMethod: "custom",
                                         claims: GetClaims(user));
            else
                context.Result = new CustomGrantValidationResult(ErrorCodes.INVALID_USER, "用户名或密码错误");

        }

        private IEnumerable<Claim> GetClaims(PassportInfoDto dto)
        {
            return new[]
            {
                new Claim(PassportClaimTypes.PassportId, dto.PassportId),
                new Claim(PassportClaimTypes.Name, dto.Name??"-"),
                new Claim(PassportClaimTypes.Avatar, dto.Avatar??"-"),
                new Claim(PassportClaimTypes.AppId, dto.AppId.ToString()),
                new Claim(PassportClaimTypes.AppType, ((int)dto.AppType).ToString()),
                new Claim(PassportClaimTypes.Phone, dto.Phone??"-"),
                new Claim(PassportClaimTypes.Email, dto.Email??"-"),
                new Claim(PassportClaimTypes.PassportType, ((int)dto.PassportType).ToString()),
                new Claim(PassportClaimTypes.OrganizationId, dto.OrganizationId.ToString())
            };
        }

        public class CustomGrantValidationResult : GrantValidationResult
        {
            public CustomGrantValidationResult(string code, string message)
            {
                Error = code;
                ErrorDescription = message;
                IsError = true;
            }
        }
    }
}
