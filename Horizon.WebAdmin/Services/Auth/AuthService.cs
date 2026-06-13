using System.Security.Claims;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Share.Dtos.User;
using Horizon.Share.VMs;

namespace Horizon.WebAdmin.Services.Auth;

public class AuthService
{
    private readonly TokenStorage _tokenStorage;
    private readonly HttpClient _httpClient;
    private readonly IHostEnvironment _environment;

    public AuthService(TokenStorage tokenStorage, HttpClient httpClient, IHostEnvironment environment, IConfiguration configuration)
    {
        _tokenStorage = tokenStorage;
        _httpClient = httpClient;
        _environment = environment;
    }

    public async Task<LoginResult> LoginAsync(string passportId, string password)
    {
        try
        {
            var loginDto = new LoginDto
            {
                PassportId = passportId,
                Password = password,
                AppId = 0,
                AppType = AppType.Basic,
                PassportType = PassportType.System,
                VerifyCode = "",
                Phone = "",
                Email = "",
                MachineId = Guid.NewGuid().ToString()
            };

            var response = await _httpClient.PostAsJsonAsync("Account/signin", loginDto);
            var responseContent = await response.Content.ReadAsStringAsync();

            var result = System.Text.Json.JsonSerializer.Deserialize<ResultVM<LoginResultDto>>(responseContent,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null || !result.IsSuccess || result.Data == null)
            {
                return new LoginResult(false, result?.ErrorMessage ?? "登录失败");
            }

            var accessToken = result.Data.AccessToken;
            var refreshToken = result.Data.RefreshToken;

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new LoginResult(false, "未获取到访问令牌");
            }

            await _tokenStorage.SetAccessTokenAsync(accessToken);
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                await _tokenStorage.SetRefreshTokenAsync(refreshToken);
            }

            var claims = await _tokenStorage.GetClaimsPrincipalAsync();
            if (claims == null)
            {
                await _tokenStorage.ClearAsync();
                return new LoginResult(false, "令牌解析失败");
            }

            var passportTypeClaim = claims.FindFirst("PassportType");
            if (passportTypeClaim == null || passportTypeClaim.Value != $"{(int)PassportType.System}")
            {
                await _tokenStorage.ClearAsync();
                return new LoginResult(false, "只有系统级账号才能登录管理后台");
            }

            return new LoginResult(true, null, claims);
        }
        catch (HttpRequestException ex)
        {
            return new LoginResult(false, $"无法连接到认证服务器: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new LoginResult(false, $"登录异常: {ex.Message}");
        }
    }

    public async Task LogoutAsync()
    {
        await _tokenStorage.ClearAsync();
    }

    public async Task<ClaimsPrincipal?> GetCurrentUserAsync()
    {
        return await _tokenStorage.GetClaimsPrincipalAsync();
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(string oldPassword, string newPassword)
    {
        try
        {
            var token = await _tokenStorage.GetAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
                return (false, "未登录");

            var request = new HttpRequestMessage(HttpMethod.Post, "Account/changepassword");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { OldPassword = oldPassword, NewPassword = newPassword });

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            var result = System.Text.Json.JsonSerializer.Deserialize<ResultVM<bool>>(content,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.IsSuccess == true)
                return (true, null);

            return (false, result?.ErrorMessage ?? "修改密码失败");
        }
        catch (Exception ex)
        {
            return (false, $"修改密码异常: {ex.Message}");
        }
    }
}

public record LoginResult(bool Success, string? ErrorMessage, ClaimsPrincipal? User = null);
