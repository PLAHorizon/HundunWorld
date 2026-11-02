using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 脚本化生成（scripted spawn）的**进程级环境变量**串行集合。
/// <para>
/// 症状不是绑定器变了，而是**几个测试类同时改同一批进程环境变量**：
/// 那条用例要的是"<c>ScriptedSpawn__Enabled=1</c> 会让 .NET 的绑定器抛 <c>InvalidOperationException</c>"，
/// 而 <c>ScriptedSpawnDiagnosticsTests</c> 会把同一个变量写成 <c>"true"</c>——两边交错执行时，
/// 本类清完变量还没等自己赋值，对面已经把 <c>"true"</c> 写进去了，于是绑定成功、异常没抛。
/// 反过来更糟：本类的 <c>finally</c> 会把**别人**刚设的变量清掉。
/// </para>
/// <para>
/// <c>DsHostScriptedLiveTests</c> 看着像同类，其实不是：它写的是 <c>ProcessStartInfo.Environment</c>
/// （只影响它自己拉起的子进程），所以**不**挂在本集合里——串行集合该收的只是"真的共用同一份可变态"的类，
/// 多挂只会把套件拖慢，还会让这道规矩看起来比实际更宽。
/// </para>
/// <para>
/// 环境变量是进程级的，没有"每个测试一份"这回事；要么把值显式喂进配置源（部分用例已经这样做了），
/// 要么让这些类彼此串行。这里选串行，并把规矩变成判据
/// （见 <c>会写脚本生成进程环境变量的测试类都必须挂在串行集合上</c>）。
/// </para>
/// </summary>
[CollectionDefinition(ScriptedSpawnEnvironmentCollection.Name, DisableParallelization = true)]
public class ScriptedSpawnEnvironmentCollection
{
    public const string Name = "scripted-spawn-env";
}
