using Flax.Build;
using Flax.Build.NativeCpp;

public class HundunAgentEditor : GameEditorModule
{
    public override void Init()
    {
        base.Init();

        BuildNativeCode = false;
    }

    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        options.PublicDependencies.Add("HundunAgent");

        BuildNativeCode = false;

        options.ScriptingAPI.IgnoreMissingDocumentationWarnings = true;

        options.ScriptingAPI.SystemReferences.Add("System.Net.Http");
        options.ScriptingAPI.SystemReferences.Add("System.Net.HttpListener");
        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");
        options.ScriptingAPI.SystemReferences.Add("System.Threading");
        options.ScriptingAPI.SystemReferences.Add("System.Linq");
    }
}
