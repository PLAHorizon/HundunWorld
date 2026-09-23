using Flax.Build;

/// <summary>
/// 无头 Dedicated Server 构建目标（DS）。
/// <para>
/// 与 <see cref="GameTarget"/>（客户端）分离：DS 不加载渲染/输入/相机相关的 Game 模块，
/// 只加载 <c>ServerGame</c>——一个纯托管、无场景依赖的模拟宿主模块。
/// </para>
/// <para>
/// 构建命令：<c>FlaxEngine.exe -build -project HundunWorld.flaxproj -target ServerTarget -platform Windows -arch x64</c>
/// 运行命令：<c>HundunWorld.exe -headless -null -mute</c>
/// </para>
/// </summary>
public class ServerTarget : GameProjectTarget
{
    /// <inheritdoc />
    public override void Init()
    {
        base.Init();

        // DS 只引用 ServerGame 模块。刻意不引用 "Game"：
        // Game 模块饱含 PlayerController / Camera / Input / ActorSync 等客户端专属逻辑，
        // 在无输入、无渲染的 DS 上加载它们会引入无意义的初始化与潜在 NRE。
        Modules.Add("ServerGame");
    }
}
