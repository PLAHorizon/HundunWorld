using Horizon.Core.Abstract.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 认证通行证基本信息接口
    /// </summary>
    public interface IPassportCurrentUser
    {
        /// <summary>
        /// 是否已认证
        /// </summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// 通行证
        /// </summary>
        string PassportId { get; }
        /// <summary>
        /// 用户名称
        /// </summary>
        string Name { get; }
        long AppId { get; }
        AppType AppType { get; }
        long OrganizationId { get; }
        string Avatar { get; }
        string Email { get; }
        string Phone { get; }
        string UserId { get; }
        PassportType PassportType { get; }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="claimType"></param>
        /// <returns></returns>
        Claim FindClaim(string claimType);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="claimType"></param>
        /// <returns></returns>        
        Claim[] FindClaims(string claimType);
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        Claim[] GetAllClaims();


    }
}
