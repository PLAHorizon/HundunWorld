using Flax.Build;
using Flax.Build.NativeCpp;

public class TraeBridgeEditor : GameEditorModule
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

        options.ScriptingAPI.SystemReferences.Add("System.Net.Http");
        options.ScriptingAPI.SystemReferences.Add("System.Net.HttpListener");
        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");
        options.ScriptingAPI.SystemReferences.Add("System.Threading");
    }
}
