using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Horizon.WebAdmin.Services.Auth;

public class AuthStateProvider : AuthenticationStateProvider
{
    private readonly TokenStorage _tokenStorage;

    public AuthStateProvider(TokenStorage tokenStorage)
    {
        _tokenStorage = tokenStorage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = await _tokenStorage.GetClaimsPrincipalAsync();
        return new AuthenticationState(user ?? new ClaimsPrincipal());
    }

    public async Task NotifyStateChangedAsync()
    {
        var user = await _tokenStorage.GetClaimsPrincipalAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user ?? new ClaimsPrincipal())));
    }
}
