using Flax.Build;

public class QuestEditorEditorTarget : GameProjectEditorTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("QuestEditor");
        Modules.Add("QuestEditorEditor");
    }
}
