using Flax.Build;
using Flax.Build.NativeCpp;

public class NarrativeProEditor : GameEditorModule
{
    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        options.PublicDependencies.Add("NarrativePro");

        BuildNativeCode = false;
    }
}
