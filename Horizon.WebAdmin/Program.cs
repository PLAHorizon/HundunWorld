using Horizon.WebAdmin.Core;
using Horizon.WebAdmin.Modules.Flower;
using Horizon.WebAdmin.Modules.Game;
using Horizon.WebAdmin.Services.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
});
builder.Services.AddAntDesign();

var baseUrl = builder.Configuration["WebApiBaseUrl"] ?? "https://localhost:5101";

builder.Services.AddScoped<TokenStorage>();

builder.Services.AddScoped<AuthService>(sp =>
    new AuthService(
        sp.GetRequiredService<TokenStorage>(),
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("WebApi"),
        sp.GetRequiredService<IHostEnvironment>(),
        builder.Configuration));

builder.Services.AddScoped<AuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthStateProvider>());

builder.Services.AddTransient<AuthDelegatingHandler>();

builder.Services.AddHttpClient("WebApi", client =>
{
    client.BaseAddress = new Uri(baseUrl);
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();
    if (builder.Environment.IsDevelopment())
    {
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true;
    }
    return handler;
})
.AddHttpMessageHandler<AuthDelegatingHandler>();

builder.Services.AddAdminModule<FlowerModule>();
builder.Services.AddAdminModule<GameModule>();
builder.Services.AddSingleton<ModuleRegistry>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

public class AuthDelegatingHandler : DelegatingHandler
{
    private readonly TokenStorage _tokenStorage;

    public AuthDelegatingHandler(TokenStorage tokenStorage)
    {
        _tokenStorage = tokenStorage;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenStorage.GetAccessTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
