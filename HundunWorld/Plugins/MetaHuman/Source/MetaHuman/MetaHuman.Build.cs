using Flax.Build;
using Flax.Build.NativeCpp;

public class MetaHuman : GameModule
{
    public override void Init()
    {
        base.Init();

        BuildNativeCode = false;
    }

    public override void Setup(BuildOptions options)
    {
        base.Setup(options);

        options.ScriptingAPI.IgnoreMissingDocumentationWarnings = true;

        options.ScriptingAPI.SystemReferences.Add("System.Collections");
        options.ScriptingAPI.SystemReferences.Add("System.Linq");
        options.ScriptingAPI.SystemReferences.Add("System.Runtime");
        options.ScriptingAPI.SystemReferences.Add("System.Threading");
        options.ScriptingAPI.SystemReferences.Add("System.Threading.Tasks");
        // 模板/预设/外观快照的编解码用 System.Text.Json；Flax 脚本默认不引用该程序集
        options.ScriptingAPI.SystemReferences.Add("System.Text.Json");
        // Encoding.UTF8 在这个程序集里（Flax 的裁剪引用集不含它时，MetaHuman.csproj 报 CS1069 类型被转出）
        options.ScriptingAPI.SystemReferences.Add("System.Text.Encoding.Extensions");
        // MetaHumanFaceTrackingReceiver 的 UDP 监听（UdpClient/IPEndPoint）
        options.ScriptingAPI.SystemReferences.Add("System.Net.Sockets");
    }
}
