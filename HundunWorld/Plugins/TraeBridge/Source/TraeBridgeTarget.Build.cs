using Flax.Build;

public class TraeBridgeTarget : GameProjectTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("TraeBridge");
    }
}