using Flax.Build;

public class PetEditorEditorTarget : GameProjectEditorTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("PetEditor");
        Modules.Add("PetEditorEditor");
    }
}
