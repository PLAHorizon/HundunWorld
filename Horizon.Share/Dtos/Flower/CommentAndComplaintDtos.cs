using System;

namespace Horizon.Share.Dtos.Flower;

/// <summary>商品评价 API DTO（对应 ProductCommentState，纯 POCO）。</summary>
public class ProductCommentDto
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public long OrderId { get; set; }
    public Guid UserId { get; set; }
    public int Rank { get; set; }
    public string Content { get; set; } = "";
    public string Images { get; set; } = "";
    public string ReplyContent { get; set; } = "";
    public DateTime? ReplyTime { get; set; }
    public bool IsAnonymous { get; set; }
}

/// <summary>订单投诉 API DTO（对应 OrderComplaintState，纯 POCO）。</summary>
public class OrderComplaintDto
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public Guid UserId { get; set; }
    public long ShopId { get; set; }
    public string ComplaintReason { get; set; } = "";
    public string ComplaintContent { get; set; } = "";
    public int Status { get; set; }
    public string ReplyContent { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
