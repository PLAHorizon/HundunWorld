using Horizon.WebAdmin.Core;
using Horizon.WebAdmin.Modules.Game.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Horizon.WebAdmin.Modules.Game;

public class GameModule : AdminModuleBase
{
    public override string ModuleId => "game";
    public override string ModuleName => "游戏管理";
    public override string Icon => "gamepad";
    public override string RoutePrefix => "/game";

    public override List<ModuleMenuItem> MenuItems { get; } =
    [
        new()
        {
            Name = "游戏总览", Icon = "dashboard",
            Children =
            [
                new() { Name = "游戏仪表盘", Route = "/game/dashboard" }
            ]
        },
        new()
        {
            Name = "游戏配置", Icon = "setting",
            Children =
            [
                new() { Name = "游戏列表", Route = "/game/games" },
                new() { Name = "服务器管理", Route = "/game/servers" }
            ]
        },
        new()
        {
            Name = "用户与角色", Icon = "team",
            Children =
            [
                new() { Name = "游戏用户", Route = "/game/users" },
                new() { Name = "角色管理", Route = "/game/characters" }
            ]
        },
        new()
        {
            Name = "游戏内容", Icon = "appstore",
            Children =
            [
                new() { Name = "物品模板", Route = "/game/item-templates" },
                new() { Name = "技能模板", Route = "/game/skill-templates" },
                new() { Name = "公会管理", Route = "/game/guilds" }
            ]
        },
        new()
        {
            Name = "运营管理", Icon = "line-chart",
            Children =
            [
                new() { Name = "聊天管理", Route = "/game/chat" },
                new() { Name = "交易日志", Route = "/game/trade-logs" }
            ]
        },
        new()
        {
            Name = "图文配套", Icon = "file-text",
            Children =
            [
                new() { Name = "游戏图文", Route = "/game/guides" }
            ]
        }
    ];

    public override void RegisterServices(IServiceCollection services)
    {
        var serviceTypes = new[]
        {
            typeof(GameConfigService),
            typeof(GameUserService),
            typeof(GameCharacterService),
            typeof(GameItemService),
            typeof(GameSkillService),
            typeof(GameGuildService),
            typeof(GameChatService),
            typeof(GameTradeService),
            typeof(GameGuideService)
        };

        foreach (var type in serviceTypes)
        {
            services.AddScoped(type, sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("WebApi");
                var configuration = sp.GetRequiredService<IConfiguration>();
                return ActivatorUtilities.CreateInstance(sp, type, httpClient, configuration);
            });
        }
    }
}
