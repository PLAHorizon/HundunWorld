using Flax.Build;

public class MonsterEditorEditorTarget : GameProjectEditorTarget
{
    public override void Init()
    {
        base.Init();

        Modules.Add("MonsterEditor");
        Modules.Add("MonsterEditorEditor");
    }
}
