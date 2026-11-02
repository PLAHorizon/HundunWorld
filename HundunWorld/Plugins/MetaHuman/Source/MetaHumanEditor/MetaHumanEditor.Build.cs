using Flax.Build;
using Flax.Build.NativeCpp;

public class MetaHumanEditor : GameEditorModule
{
    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        options.PublicDependencies.Add("MetaHuman");

        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");

        BuildNativeCode = false;
    }
}
