using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface;

/// <summary>
/// NPC 货架库存 Grain（按 NPC 编号分键）。
/// <para>
/// <b>为什么库存要单独有个权威</b>：限量商品是"先到先得"的共享状态——两个玩家同时买最后一件，
/// 只能成一个人。放在无状态的处理器里算，两人都会成功。键按 NPC 分，同一商人自然串行。
/// </para>
/// <para>
/// <b>存的是"已买数 + 补货锚点"而不是"剩余件数"</b>（见 <c>ShopStockEntry</c>）：
/// 剩余量是时间的函数，存它就必须有人定时刷新，而"没人刷新的库存"与"卖光了"在玩家眼里同形。
/// </para>
/// <para>
/// <b>货架参数由调用方传入</b>（stockMax/restockSeconds），Grain 不查内容表：
/// 商人配置属于已装配内容，Grain 只负责记账；这样"改了配置"不需要清库存也能立刻生效。
/// </para>
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface INpcShopGrain : IGrainWithIntegerKey
{
    /// <summary>查剩余（不改状态）。</summary>
    Task<ShopStockView> GetStockAsync(int itemId, int stockMax, int restockSeconds);

    /// <summary>记一笔卖出（原子：不够就一件不卖，状态不动）。</summary>
    Task<ShopStockView> TakeAsync(int itemId, int count, int stockMax, int restockSeconds);

    /// <summary>
    /// 冲正一笔卖出（补偿用：钱没扣成 / 物品没进背包时把库存还回去）。
    /// <para><b>为什么要专门的冲正而不是"再卖一次负数"</b>：负数件数在入口会被当坏请求拒，
    /// 补偿路径就会被自己的校验挡死。这里显式把 <c>Purchased</c> 往下减（下限 0）。</para>
    /// </summary>
    Task<ShopStockView> ReturnAsync(int itemId, int count, int stockMax, int restockSeconds);
}

/// <summary>库存回执。</summary>
[GenerateSerializer]
public sealed class ShopStockView
{
    [Id(0)] public bool Ok { get; set; }

    /// <summary><c>Horizon.Game.Sim.Npcs.ShopStockReject</c> 的数值（契约层不引用 Sim 枚举）。</summary>
    [Id(1)] public int Reject { get; set; }

    /// <summary>当前/成交后的剩余件数；不限期无意义（约定 −1）。</summary>
    [Id(2)] public int Remaining { get; set; }

    [Id(3)] public bool Unlimited { get; set; }

    /// <summary>自锚点以来的已卖件数（审计用）。</summary>
    [Id(4)] public int Purchased { get; set; }

    public override string ToString() =>
        $"shop_stock_ok={(Ok ? 1 : 0)} left={Remaining} unlimited={(Unlimited ? 1 : 0)} purchased={Purchased} reject={Reject}";
}
