using Flax.Build;
using Flax.Build.NativeCpp;

public class QuestEditorEditor : GameEditorModule
{
    public override void Init()
    {
        base.Init();

        BuildNativeCode = false;
    }

    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        options.PublicDependencies.Add("QuestEditor");

        BuildNativeCode = false;

        options.ScriptingAPI.IgnoreMissingDocumentationWarnings = true;

        // 任务数据在 SQL Server 里（权威源），编辑器进程只负责界面：
        // 读写一律起一个短命周期子进程调 Horizon.QuestCli，避免把 SqlClient/EF 拖进 Flax 脚本域，
        // 也避免"编辑器一份校验、服务端一份校验"迟早分叉。
        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");
        options.ScriptingAPI.SystemReferences.Add("System.Diagnostics.Process");
        options.ScriptingAPI.SystemReferences.Add("System.Linq");
        options.ScriptingAPI.SystemReferences.Add("System.Threading");
    }
}
