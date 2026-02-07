using System.Security.Claims;

namespace Horizon.WebApi.Identity.Users
{
    public class ActionHttpContextUser
    {
        public static ClaimsPrincipal Principal { get; set; }
    }
}
