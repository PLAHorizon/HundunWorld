using System.Reflection;
using System.Runtime.Loader;
using System.Text;

var engineDir = @"C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Editor\Win64\Development";
var alc = new ApiDumpContext(engineDir);
var asm = alc.LoadFromAssemblyPath(Path.Combine(engineDir, "FlaxEngine.CSharp.dll"));
Type[] types;
try { types = asm.GetTypes(); }
catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
var sb = new StringBuilder();
foreach (var name in new[] { "FlaxEditor.Content.ProjectFolderTreeNode", "FlaxEditor.Content.ContentFolderTreeNode", "FlaxEngine.Engine", "FlaxEngine.GUI.Control", "FlaxEngine.GUI.TextBoxBase", "FlaxEngine.GUI.Button" })
{
    var t = types.FirstOrDefault(x => x?.FullName == name);
    if (t == null) { sb.AppendLine($"### MISSING {name}"); continue; }
    sb.AppendLine($"### {t.FullName} : {t.BaseType?.FullName}");
    foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        sb.AppendLine($"  ctor ({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name + (p.IsOptional ? "?" : "")))})");
    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).OrderBy(x => x.Name))
        sb.AppendLine($"  prop {p.PropertyType.Name} {p.Name} {{ {(p.CanRead ? "get " : "")}{(p.CanWrite ? "set" : "")}}}");
    foreach (var e in t.GetEvents(BindingFlags.Public | BindingFlags.Instance))
        sb.AppendLine($"  event {e.Name} : {e.EventHandlerType?.Name}");
    sb.AppendLine();
}
// Dock-related types
sb.AppendLine("=== Types containing 'Dock' (FlaxEngine.GUI) ===");
foreach (var t in types.Where(x => x.Namespace == "FlaxEngine.GUI" && x.Name.Contains("Dock")))
    sb.AppendLine("  " + t.FullName);
foreach (var t in types.Where(x => x.Name == "DockStyle"))
{
    sb.AppendLine("### " + t.FullName);
    if (t.IsEnum)
        foreach (var v in Enum.GetNames(t)) sb.AppendLine("  val " + v);
}
Console.WriteLine(sb.ToString());

class ApiDumpContext : AssemblyLoadContext
{
    private readonly string _dir;
    public ApiDumpContext(string dir) : base(isCollectible: false) { _dir = dir; }
    protected override Assembly Load(AssemblyName assemblyName)
    {
        var candidate = Path.Combine(_dir, assemblyName.Name + ".dll");
        if (File.Exists(candidate)) return LoadFromAssemblyPath(candidate);
        return null;
    }
}
