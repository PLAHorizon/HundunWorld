using Flax.Build;

public class HundunAgentEditorTarget : GameProjectEditorTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("HundunAgent");
        Modules.Add("HundunAgentEditor");
    }
}
