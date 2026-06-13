using Flax.Build;

public class TraeBridgeEditorTarget : GameProjectEditorTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("TraeBridge");
    }
}