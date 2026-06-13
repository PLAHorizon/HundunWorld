using Flax.Build;

public class NarrativeTarget : GameProjectTarget
{
    /// <inheritdoc />
    public override void Init()
    {
        base.Init();

        // Reference the modules for game
        Modules.Add("Narrative");
    }
}
