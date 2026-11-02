using Flax.Build;
using Flax.Build.NativeCpp;

public class NpcEditor : GameModule
{
    public override void Init()
    {
        base.Init();

        BuildNativeCode = false;
    }

    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        BuildNativeCode = false;

        options.ScriptingAPI.IgnoreMissingDocumentationWarnings = true;

        // 草稿 JSON 的读写在本模块里用 System.Text.Json 完成（与 CLI 同一份字段名约定）。
        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");
        options.ScriptingAPI.SystemReferences.Add("System.Linq");
    }
}
