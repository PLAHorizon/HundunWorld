using Flax.Build;

public class ItemEditorEditorTarget : GameProjectEditorTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("ItemEditor");
        Modules.Add("ItemEditorEditor");
    }
}
