using Flax.Build;

public class PCGEditorTarget : GameProjectEditorTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("PCG");
        Modules.Add("PCGEditor");
    }
}
