using Flax.Build;

public class MetaHumanEditorTarget : GameProjectEditorTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("MetaHuman");
        Modules.Add("MetaHumanEditor");
    }
}
