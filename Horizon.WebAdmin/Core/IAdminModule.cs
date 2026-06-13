namespace Horizon.WebAdmin.Core;

public interface IAdminModule
{
    string ModuleId { get; }
    string ModuleName { get; }
    string Icon { get; }
    string RoutePrefix { get; }
    List<ModuleMenuItem> MenuItems { get; }
    void RegisterServices(IServiceCollection services);
}
