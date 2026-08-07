using Flax.Build;

public class HundunAgentTarget : GameProjectTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("HundunAgent");
    }
}
