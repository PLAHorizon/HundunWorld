using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.JSInterop;

namespace Horizon.WebAdmin.Services.Auth;

public class TokenStorage
{
    private readonly IJSRuntime _jsRuntime;
    private const string TokenKey = "__webadmin_token";
    private const string RefreshTokenKey = "__webadmin_refresh";

    public TokenStorage(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task SetAccessTokenAsync(string token)
    {
        try { await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", TokenKey, token); }
        catch (InvalidOperationException) { }
        catch (JSDisconnectedException) { }
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        try { return await _jsRuntime.InvokeAsync<string?>("sessionStorage.getItem", TokenKey); }
        catch (TaskCanceledException) { return null; }
        catch (JSDisconnectedException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public async Task SetRefreshTokenAsync(string token)
    {
        try { await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", RefreshTokenKey, token); }
        catch (InvalidOperationException) { }
        catch (JSDisconnectedException) { }
    }

    public async Task<string?> GetRefreshTokenAsync()
    {
        try { return await _jsRuntime.InvokeAsync<string?>("sessionStorage.getItem", RefreshTokenKey); }
        catch (TaskCanceledException) { return null; }
        catch (JSDisconnectedException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public async Task ClearAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", TokenKey);
            await _jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", RefreshTokenKey);
        }
        catch (InvalidOperationException) { }
        catch (JSDisconnectedException) { }
    }

    public async Task<ClaimsPrincipal?> GetClaimsPrincipalAsync()
    {
        var token = await GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            var identity = new ClaimsIdentity(jwt.Claims, "Bearer");
            return new ClaimsPrincipal(identity);
        }
        catch
        {
            return null;
        }
    }
}
