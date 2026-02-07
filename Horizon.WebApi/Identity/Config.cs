using IdentityServer4.Models;
using System.Collections.Generic;

namespace Horizon.WebApi.Identity
{
    public class Config
    {
        public const string ClientId = "api_client";
        public const string ClientSecret = "api_secret";
        public const string Scope = "api_scope";
        public const string SilentScope = "silent_scope";
        public const string AutoScope = "auto_scope";
        public const string OfflineAccess = "offline_access";

        public static IEnumerable<IdentityResource> GetIdentityResources()
        {
            return new IdentityResource[]
            {
            };
        }

        public static IEnumerable<ApiResource> GetApis()
        {
            return new List<ApiResource>
            {
                new ApiResource("iHuaxiaX")
                {
                    Scopes = { Scope, SilentScope, AutoScope }
                }
            };
        }

        public static IEnumerable<ApiScope> GetScopes()
        {
            return new List<ApiScope>
            {
                new ApiScope(Scope),
                new ApiScope(SilentScope),
                new ApiScope(AutoScope)
            };
        }

        public static IEnumerable<Client> GetClients()
        {
            return new List<Client>
            {
                new Client
                {
                    ClientId = ClientId,
                    ClientName = ClientId,
                    AllowedGrantTypes = GrantTypes.ResourceOwnerPassword,
                    AccessTokenLifetime = 3600 * 24 * 7, //7天
                    AllowOfflineAccess = true,
                    RefreshTokenExpiration = TokenExpiration.Sliding,
                    SlidingRefreshTokenLifetime = 3600 * 24 * 5, //5天
                    ClientSecrets = {new Secret(ClientSecret.Sha256())},
                    AllowedScopes = {
                        Scope,
                        SilentScope,
                        AutoScope,
                        OfflineAccess
                    }
                }
            };
        }
    }
}
