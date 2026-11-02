using Flax.Build;

public class NpcEditorTarget : GameProjectTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("NpcEditor");
    }
}
