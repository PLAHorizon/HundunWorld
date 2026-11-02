using Flax.Build;

public class ItemEditorTarget : GameProjectTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("ItemEditor");
    }
}
