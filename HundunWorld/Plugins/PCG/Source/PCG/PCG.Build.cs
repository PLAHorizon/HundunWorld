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
    }
}
