using IdentityModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;

namespace Horizon.WebApi.Identity.Users
{
    public class PassportClaimTypes
    {
        /// <summary>
        /// Default: <see cref="ClaimTypes.Name"/>
        /// </summary>
        public static string UserName { get; set; } = "account";

        public static string Name { get; set; } = "ssoname";

        /// <summary>
        /// Default: <see cref="ClaimTypes.Role"/>
        /// </summary>
        public static string Role { get; set; } = JwtClaimTypes.Role;

        /// <summary>
        /// Default: <see cref="ClaimTypes.Email"/>
        /// </summary>
        public static string Email { get; set; } = ClaimTypes.Email;

        /// <summary>
        /// Default: "email_verified".
        /// </summary>
        public static string EmailVerified { get; set; } = "email_verified";

        /// <summary>
        /// Default: "phone_number".
        /// </summary>
        public static string PhoneNumber { get; set; } = "phone_number";

        /// <summary>
        /// Default: "phone_number_verified".
        /// </summary>
        public static string PhoneNumberVerified { get; set; } = "phone_number_verified";

        /// <summary>
        /// Default: "tenantid".
        /// </summary>
        public static string TenantId { get; set; } = "tenantid";


        /// <summary>
        /// Default: "editionid".
        /// </summary>
        public static string EditionId { get; set; } = "editionid";

        /// <summary>
        /// Default: "client_id".
        /// </summary>
        public static string ClientId { get; set; } = "client_id";

        /// <summary>
        /// 
        /// </summary>
        public static string PassportId { get; set; } = nameof(PassportId);
        public static string Avatar { get; set; } = nameof(Avatar);
        public static string AppId { get; set; } = nameof(AppId);
        public static string AppType { get; set; } = nameof(AppType);
        public static string Phone { get; set; } = nameof(Phone);
        public static string OrganizationId { get; set; } = nameof(OrganizationId);
        public static string PassportType { get; set; } = nameof(PassportType);
    }
}
