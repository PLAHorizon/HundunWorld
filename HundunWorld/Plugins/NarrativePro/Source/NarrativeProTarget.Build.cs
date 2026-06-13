using Flax.Build;

public class NarrativeProTarget : GameProjectTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("NarrativePro");
    }
}
