using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;

namespace Horizon.WebApi.Identity.Users
{
    /// <summary>
    /// 认证通行证基本信息
    /// </summary>
    public class PassportCurrentUser : IPassportCurrentUser
    {
        private static readonly Claim[] EmptyClaimsArray = new Claim[0];
        public readonly ClaimsPrincipal _principal;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public PassportCurrentUser(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
            _principal = httpContextAccessor.HttpContext.User;
        }
        /// <summary>
        /// 是否认证
        /// </summary>
        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(PassportId);

        /// <summary>
        /// 通行证号
        /// </summary>
        public virtual string PassportId => FindClaim(PassportClaimTypes.PassportId)?.Value;
        /// <summary>
        /// 姓名
        /// </summary>
        public virtual string Name => FindClaim(PassportClaimTypes.Name)?.Value;
        /// <summary>
        /// 应用Id
        /// </summary>
        public long AppId => long.Parse(FindClaim(PassportClaimTypes.AppId)?.Value ?? "0");
        /// <summary>
        /// 应用类型
        /// </summary>
        public AppType AppType => (AppType)int.Parse(FindClaim(PassportClaimTypes.AppType)?.Value ?? "0");
        /// <summary>
        /// 当前通行证类型
        /// </summary>
        public PassportType PassportType => (PassportType)int.Parse(FindClaim(PassportClaimTypes.PassportType)?.Value ?? "0");

        /// <summary>
        /// 组织结构id
        /// </summary>
        public long OrganizationId => long.Parse(FindClaim(PassportClaimTypes.OrganizationId)?.Value ?? "0");
        /// <summary>
        /// 头像
        /// </summary>
        public string Avatar => FindClaim(PassportClaimTypes.Avatar)?.Value;
        /// <summary>
        /// 邮箱
        /// </summary>
        public string Email => FindClaim(PassportClaimTypes.Email)?.Value;
        /// <summary>
        /// 手机
        /// </summary>
        public string Phone => FindClaim(PassportClaimTypes.Phone)?.Value;

        /// <summary>
        /// 用户Id（来自JWT AccessToken中的PUId/Guid声明）
        /// </summary>
        public string UserId => FindClaim(PassportClaimTypes.UserId)?.Value;

        public Claim FindClaim(string claimType)
        {
            return _principal?.Claims.FirstOrDefault(c => c.Type == claimType);
        }

        public Claim[] FindClaims(string claimType)
        {
            return _principal?.Claims.Where(c => c.Type == claimType).ToArray() ?? EmptyClaimsArray;
        }

        public Claim[] GetAllClaims()
        {
            return _principal?.Claims.ToArray() ?? EmptyClaimsArray;
        }
    }
}
