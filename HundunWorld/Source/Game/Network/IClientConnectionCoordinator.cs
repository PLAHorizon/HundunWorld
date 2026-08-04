using System.Threading.Tasks;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 客户端建连请求类型（连接精简治理，spec 5.1）。
    /// 区分三条业务建连意图，用于首包契约与互斥编排的类型化标识。
    /// </summary>
    public enum ClientConnectionRequestKind
    {
        /// <summary>登录建连：用户点击登录时发起，首包为登录认证请求。</summary>
        Login,

        /// <summary>进游戏按需建连：选择角色进入游戏时发起，首包为 EnterGameRequest。</summary>
        EnterGame,

        /// <summary>断线重连：检测到断线后由重连管理器发起，首包为 ReconnectResumePacket。</summary>
        Reconnect,
    }

    /// <summary>
    /// 客户端单连接编排协调器（连接精简治理，spec 5.1.1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 契约（调用方必须先经 <see cref="RequestConnectAsync"/> 再调用 <see cref="NetworkManager.ConnectAsync"/>）：
    /// </para>
    /// <list type="bullet">
    ///   <item>返回 <c>true</c>：本请求实际执行 TCP 建连（唯一夺锁路径），建连成功后按 <see cref="ClientConnectionRequestKind"/> 立即发送对应首包。</item>
    ///   <item>返回 <c>false</c>：连接已在线被复用，或另一路径正在建连（互斥夺锁失败），调用方复用已有连接或等待，<b>不得再发起第二次 TCP 建连</b>。</item>
    /// </list>
    /// <para>
    /// 互斥语义：任意客户端进程任一时刻至多一条建连路径在途，从源头消除"幽灵连接 + 重复连接 + 闲置连接"三大来源。
    /// </para>
    /// </remarks>
    public interface IClientConnectionCoordinator
    {
        /// <summary>
        /// 请求建连（互斥编排入口）。
        /// </summary>
        /// <param name="kind">建连请求类型（登录/进游戏/重连），决定首包类型。</param>
        /// <returns>true = 本请求实际执行 TCP 建连；false = 已有连接在线被复用或另有路径在建连（不得重复建连）。</returns>
        Task<bool> RequestConnectAsync(ClientConnectionRequestKind kind);

        /// <summary>"TCP 连接建立 → 首包发出"时延（毫秒），供可观测性与首包时延 ≤ 1 秒验收。</summary>
        int LastFirstPacketLatencyMs { get; }

        /// <summary>是否有建连流程在途（互斥状态，供调用方判断是否等待）。</summary>
        bool IsConnectingInProgress { get; }
    }
}