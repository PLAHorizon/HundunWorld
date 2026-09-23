using Flax.Build;
using Flax.Build.NativeCpp;

public class PCGEditor : GameEditorModule
{
    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        options.PublicDependencies.Add("PCG");

        BuildNativeCode = false;
    }
}
