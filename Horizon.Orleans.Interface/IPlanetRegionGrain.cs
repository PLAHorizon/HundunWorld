using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Orleans;
using Orleans.CodeGeneration;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 星球区域权限 grain：一颗星球一个实例（主键 = 星球 id），里面装的是
    /// <see cref="Horizon.Planet.Authority.PlanetRegionAuthority"/> 的那份权威判定。
    /// <para>为什么整颗星球一个 grain 而不是每区一个：迁移判定要同时看 from 与 to 的归属，
    /// 跨 grain 就没有"一次判定原子生效"这件事，得引入两阶段。区域层的状态很小
    /// （每实体一条记录），先用单 grain 把语义钉对，分片留给真有瓶颈时再说。</para>
    /// <para>grain 只做三件事：把线上 DTO 翻成库里的类型、把库里的判定结果翻回线上码、
    /// 以及在归属真的改变时落盘。判定本身一行都不在这儿——那是 <c>Horizon.Planet</c> 的活，
    /// 它的判据不依赖 Orleans。</para>
    /// <para><b>版本 1 → 2 → 3 都是签名变更，不是新增方法</b>：
    /// v2 给 <see cref="BindAsync"/> / <see cref="TeleportAsync"/> 各加了
    /// <c>planetCaliber</c>（§6.2 的握手字节），v3 给 <see cref="SnapshotAsync"/> 加了同一项并让
    /// 返回值自带结论。口径要是做成"可选/可缺"，就留下了"没声明也照样进世界"的那条路，
    /// 而 §6.2 要的恰恰是没有这条路。v1/v2 从未部署过（这条 grain 此前只有测试调用者），
    /// 所以不做多版本共存。</para>
    /// </summary>
    [Version(3)]
    public interface IPlanetRegionGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 初始落位。区域由全局点反查，不接受调用方报的区域；tick 倒退直接返回 false。
        /// <para><paramref name="planetCaliber"/> 不匹配时也返回 false，并且<em>不建归属</em>：
        /// 落位是"进入世界"那一步，口径不符就不该在权威表里留下任何东西。</para>
        /// </summary>
        Task<bool> BindAsync(ulong characterId, long xCm, long yCm, long zCm, long tick, byte[] planetCaliber);

        /// <summary>传送/快速旅行：直接改全局点，不要求共边。实体没落过位、或口径不符都返回 false。</summary>
        Task<bool> TeleportAsync(ulong characterId, long xCm, long yCm, long zCm, long tick, byte[] planetCaliber);

        /// <summary>
        /// 处理一条区域迁移请求。被拒时 <see cref="PlanetRegionTransferResult.Reason"/> 一定非空，
        /// 且 <see cref="PlanetRegionTransferResult.Code"/> 是 <see cref="PlanetRegionRejectCode"/> 里的一条。
        /// </summary>
        Task<PlanetRegionTransferResult> TransferAsync(PlanetRegionTransferRequest request);

        /// <summary>推进服务端时钟（摘除过期重叠窗口）。时钟倒退会抛。</summary>
        Task AdvanceAsync(long tick);

        /// <summary>
        /// 读一个实体的当前归属、订阅集与可见实体——AOI 侧的入口，<b>只读</b>（§13.55 的并轨策略：
        /// 客户端拿它决定加载哪些邻区与边界 cell，服务端权威位置仍归平面世界的 ZoneShard）。
        /// <para>带口径判定：一份按别的层级算出来的订阅集，客户端拿去加载的是另一块地面的实体，
        /// 而且全程不报错。<paramref name="planetCaliber"/> 与迁移请求上的是同一条 35 字节。</para>
        /// <para>返回值自带结论（<c>Accepted/Code/Reason</c>）：<b>空数组有两种意思</b>——
        /// "周围真的没人"与"这条请求不该被处理"。少了这一栏客户端只能靠超时猜。</para>
        /// </summary>
        Task<PlanetRegionSnapshot> SnapshotAsync(ulong characterId, byte[] planetCaliber);

    }
}
