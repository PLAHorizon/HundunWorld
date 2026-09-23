using Flax.Build;

public class PCGTarget : GameProjectTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("PCG");
    }
}
