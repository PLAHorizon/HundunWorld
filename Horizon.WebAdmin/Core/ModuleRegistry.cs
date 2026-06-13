#nullable enable
namespace Horizon.WebAdmin.Core;

public class ModuleRegistry
{
    public List<IAdminModule> Modules { get; } = new();

    public IAdminModule? ActiveModule { get; private set; }

    public ModuleRegistry(IEnumerable<IAdminModule> modules)
    {
        foreach (var module in modules)
        {
            Modules.Add(module);
        }

        if (Modules.Count > 0)
        {
            ActiveModule = Modules[0];
        }
    }

    public void SetActiveModule(string moduleId)
    {
        var module = Modules.FirstOrDefault(m => m.ModuleId == moduleId);
        if (module is not null)
        {
            ActiveModule = module;
        }
    }
}
