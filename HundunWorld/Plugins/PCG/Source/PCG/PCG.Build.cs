using Flax.Build;
using Flax.Build.NativeCpp;

public class PCG : GameModule
{
    public override void Init()
    {
        base.Init();

        BuildNativeCode = false;
    }

    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        BuildNativeCode = false;

        options.ScriptingAPI.IgnoreMissingDocumentationWarnings = true;

        options.ScriptingAPI.SystemReferences.Add("System.Collections");
        options.ScriptingAPI.SystemReferences.Add("System.Linq");
        options.ScriptingAPI.SystemReferences.Add("System.Runtime");
        options.ScriptingAPI.SystemReferences.Add("System.Threading");
        options.ScriptingAPI.SystemReferences.Add("System.Threading.Tasks");
        // PCGGraphJson 的图编解码用 System.Text.Json；Flax 脚本默认不引用该程序集
        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");
    }
}
