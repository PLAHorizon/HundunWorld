namespace Horizon.WebAdmin.Core;

public abstract class AdminModuleBase : IAdminModule
{
    public abstract string ModuleId { get; }
    public abstract string ModuleName { get; }
    public abstract string Icon { get; }
    public abstract string RoutePrefix { get; }

    public virtual List<ModuleMenuItem> MenuItems { get; } = new();

    public virtual void RegisterServices(IServiceCollection services)
    {
    }
}
