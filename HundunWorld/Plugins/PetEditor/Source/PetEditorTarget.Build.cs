using Flax.Build;

public class PetEditorTarget : GameProjectTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("PetEditor");
    }
}
