using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>
/// 商户 API DTO（HTTP 响应用）。
/// <para>
/// 与 grain 状态类 <c>MerchantState</c> 字段对应，纯 POCO 无序列化属性，使 HTTP 契约与持久化解耦。
/// 枚举字段（MerchantType）暂以 int 承载（对应 <c>Horizon.Game.Message.Network.MerchantType</c> 数值），
/// 避免 Share 反向依赖 Game.Message；后续枚举收敛到 Core.Abstract 后可改为强类型。
/// </para>
/// </summary>
public class MerchantDto
{
    public long MerchantId { get; set; }
    public Guid UserId { get; set; }
    public string Passport { get; set; } = "";
    /// <summary>商户类型（int，对应 MerchantType 枚举数值）。</summary>
    public int MerchantType { get; set; }
    public string ShopName { get; set; } = "";
    public string ShopDescription { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public string BusinessLicense { get; set; } = "";
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public int AuditStatus { get; set; }
}
