using Flax.Build;

public class NpcEditorEditorTarget : GameProjectEditorTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("NpcEditor");
        Modules.Add("NpcEditorEditor");
    }
}
