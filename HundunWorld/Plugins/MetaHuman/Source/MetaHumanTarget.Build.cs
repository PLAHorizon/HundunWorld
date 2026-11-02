using Flax.Build;

public class MetaHumanTarget : GameProjectTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("MetaHuman");
    }
}
