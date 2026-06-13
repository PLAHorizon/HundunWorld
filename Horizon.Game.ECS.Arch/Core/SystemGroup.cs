namespace Horizon.Game.ECS.Arch.Core;

/// <summary>
/// 系统分组：定义在 <see cref="ArchWorldHost"/> 单帧 Tick 中各阶段的执行顺序。
/// 顺序与枚举值一致：先收消息、再做 Fixed/Update 模拟、最后回写视图与发包。
/// </summary>
public enum SystemGroup
{
    /// <summary>从网络/IO 队列拉取数据，写入 ECS 组件。</summary>
    NetworkReceive = 0,

    /// <summary>固定时间步模拟（物理、移动预测、回滚）。</summary>
    FixedUpdate = 1,

    /// <summary>逐帧逻辑（AI、技能、状态机等）。</summary>
    Update = 2,

    /// <summary>渲染前同步（把 ECS 数据写到 UE Actor / UI）。</summary>
    Render = 3,

    /// <summary>把本地输入/状态打包发送到服务器。</summary>
    NetworkSend = 4,
}
