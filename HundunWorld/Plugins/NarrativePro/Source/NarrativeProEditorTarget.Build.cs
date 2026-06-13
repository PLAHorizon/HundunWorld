using Flax.Build;

public class NarrativeProEditorTarget : GameProjectEditorTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("NarrativePro");
        Modules.Add("NarrativeProEditor");
    }
}
