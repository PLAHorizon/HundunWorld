using Flax.Build;

public class MonsterEditorTarget : GameProjectTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("MonsterEditor");
    }
}
