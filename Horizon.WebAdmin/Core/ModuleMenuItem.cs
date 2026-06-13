namespace Horizon.WebAdmin.Core;

public class ModuleMenuItem
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Route { get; set; } = "";
    public List<ModuleMenuItem> Children { get; set; } = new();
}
