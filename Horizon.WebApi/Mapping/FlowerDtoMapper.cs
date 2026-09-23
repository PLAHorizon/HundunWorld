using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Message.Network;
using Horizon.Share.Dtos.Flower;

namespace Horizon.WebApi.Mapping;

/// <summary>
/// Flower 域 grain 状态 → API DTO 显式映射。
/// <para>
/// 使 HTTP 契约与 grain/线格式状态解耦（DTO 为纯 POCO，见 <see cref="ProductDto"/>）。
/// 后续 DTO 迁移按此模式逐类型扩展。
/// </para>
/// </summary>
public static class FlowerDtoMapper
{
    public static ProductDto ToDto(ProductState s) => new()
    {
        ProductId = s.ProductId,
        MerchantId = s.MerchantId,
        SpeciesId = s.SpeciesId,
        ProductName = s.ProductName,
        Description = s.Description,
        Price = s.Price,
        Stock = s.Stock,
        Unit = s.Unit,
        Images = s.Images,
        IsActive = s.IsActive,
        Version = s.Version,
        CategoryId = s.CategoryId,
        TypeId = s.TypeId,
        BrandId = s.BrandId,
        FreightTemplateId = s.FreightTemplateId,
        Weight = s.Weight,
        Volume = s.Volume,
        MaxBuyCount = s.MaxBuyCount,
        IsOpenLadder = s.IsOpenLadder,
        ProductType = s.ProductType,
        MarketPrice = s.MarketPrice,
        MinSalePrice = s.MinSalePrice,
        AuditStatus = s.AuditStatus,
        IsPresale = s.IsPresale,
        PresaleDeliveryDate = s.PresaleDeliveryDate,
        RelatedBatchId = s.RelatedBatchId,
    };

    public static MerchantDto ToDto(MerchantState s) => new()
    {
        MerchantId = s.MerchantId,
        UserId = s.UserId,
        Passport = s.Passport,
        MerchantType = (int)s.MerchantType,
        ShopName = s.ShopName,
        ShopDescription = s.ShopDescription,
        ContactPhone = s.ContactPhone,
        BusinessLicense = s.BusinessLicense,
        IsVerified = s.IsVerified,
        VerifiedAt = s.VerifiedAt,
        AuditStatus = s.AuditStatus,
    };

    public static OrderItemDto ToDto(OrderItemState s) => new()
    {
        ProductId = s.ProductId,
        SpeciesId = s.SpeciesId,
        ProductName = s.ProductName,
        Price = s.Price,
        Quantity = s.Quantity,
        Subtotal = s.Subtotal,
    };

    public static OrderDto ToDto(OrderState s) => new()
    {
        OrderId = s.OrderId,
        OrderNo = s.OrderNo,
        BuyerId = s.BuyerId,
        MerchantId = s.MerchantId,
        TotalAmount = s.TotalAmount,
        Status = (int)s.Status,
        PaymentMethod = s.PaymentMethod,
        PaymentTime = s.PaymentTime,
        ShippingAddress = s.ShippingAddress,
        IsPresale = s.IsPresale,
        PresaleDeliveryDate = s.PresaleDeliveryDate,
        Items = s.Items?.Select(i => ToDto(i)).ToList() ?? new(),
        ShipTo = s.ShipTo,
        CellPhone = s.CellPhone,
        ExpressCompanyName = s.ExpressCompanyName,
        ShipOrderNumber = s.ShipOrderNumber,
        Freight = s.Freight,
        OrderTotalAmount = s.OrderTotalAmount,
        RefundStatus = s.RefundStatus,
        SellerRemark = s.SellerRemark,
        DiscountAmount = s.DiscountAmount,
        FullDiscount = s.FullDiscount,
        Address = s.Address,
        Platform = s.Platform,
        ProductTotalAmount = s.ProductTotalAmount,
        RelatedBatchId = s.RelatedBatchId,
        PresaleReadyNotifiedAt = s.PresaleReadyNotifiedAt,
        DeliveredAt = s.DeliveredAt,
        SenderName = s.SenderName,
        SenderPhone = s.SenderPhone,
        SenderAddress = s.SenderAddress,
        CreatedAt = s.CreatedAt,
    };

    public static ProductSKUDto ToDto(ProductSKUState s) => new()
    {
        Id = s.Id,
        ProductId = s.ProductId,
        SkuCode = s.SkuCode,
        Color = s.Color,
        Size = s.Size,
        Version = s.Version,
        SalePrice = s.SalePrice,
        CostPrice = s.CostPrice,
        Stock = s.Stock,
        SafeStock = s.SafeStock,
        ShowPic = s.ShowPic,
    };

    public static OrderRefundDto ToDto(OrderRefundState s) => new()
    {
        Id = s.Id,
        OrderId = s.OrderId,
        OrderItemId = s.OrderItemId,
        RefundNo = s.RefundNo,
        RefundAmount = s.RefundAmount,
        Reason = s.Reason,
        Status = s.Status,
        RefundMode = s.RefundMode,
        SellerAuditRemark = s.SellerAuditRemark,
        SellerAuditTime = s.SellerAuditTime,
        PlatformRemark = s.PlatformRemark,
        PlatformAuditTime = s.PlatformAuditTime,
        BuyerId = s.BuyerId,
        MerchantId = s.MerchantId,
        EnabledRefundAmount = s.EnabledRefundAmount,
        ReturnQuantity = s.ReturnQuantity,
    };

    public static ShippingAddressDto ToDto(ShippingAddressState s) => new()
    {
        Id = s.Id,
        UserId = s.UserId,
        ShipTo = s.ShipTo,
        Phone = s.Phone,
        ProvinceId = s.ProvinceId,
        ProvinceName = s.ProvinceName,
        CityId = s.CityId,
        CityName = s.CityName,
        DistrictId = s.DistrictId,
        DistrictName = s.DistrictName,
        Address = s.Address,
        IsDefault = s.IsDefault,
        Latitude = s.Latitude,
        Longitude = s.Longitude,
        Passport = s.Passport,
    };

    public static BrandDto ToDto(BrandState s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Logo = s.Logo,
        Description = s.Description,
        DisplaySequence = s.DisplaySequence,
        IsRecommend = s.IsRecommend,
        AuditStatus = s.AuditStatus,
    };

    public static ProductCategoryDto ToDto(ProductCategoryState s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Depth = s.Depth,
        Path = s.Path,
        ParentCategoryId = s.ParentCategoryId,
        DisplaySequence = s.DisplaySequence,
        Icon = s.Icon,
        Image = s.Image,
    };

    public static PaymentDto ToDto(PaymentState s) => new()
    {
        TransactionId = s.TransactionId,
        OrderId = s.OrderId,
        TransactionNo = s.TransactionNo,
        Channel = (int)s.Channel,
        Amount = s.Amount,
        Status = s.Status,
        PrepayId = s.PrepayId,
        ChannelTransactionNo = s.ChannelTransactionNo,
        PaidAt = s.PaidAt,
        ExpiredAt = s.ExpiredAt,
        IdempotencyKey = s.IdempotencyKey,
        BuyerId = s.BuyerId,
        CallbackLockKey = s.CallbackLockKey,
        CallbackLockedAt = s.CallbackLockedAt,
        NeedsOrderSync = s.NeedsOrderSync,
        TotalRefundedAmount = s.TotalRefundedAmount,
        PayParams = s.PayParams
    };

    public static CouponDto ToDto(CouponState s) => new()
    {
        Id = s.Id,
        ShopId = s.ShopId,
        CouponName = s.CouponName,
        CouponType = s.CouponType,
        Denomination = s.Denomination,
        UseCondition = s.UseCondition,
        StartDate = s.StartDate,
        EndDate = s.EndDate,
        TotalCount = s.TotalCount,
        ReceivedCount = s.ReceivedCount,
        UsedCount = s.UsedCount,
        IsActive = s.IsActive,
    };

    public static CouponRecordDto ToDto(CouponRecordState s) => new()
    {
        Id = s.Id,
        CouponId = s.CouponId,
        UserId = s.UserId,
        Status = s.Status,
        UsedOrderId = s.UsedOrderId,
        ReceivedAt = s.ReceivedAt,
        UsedAt = s.UsedAt,
    };

    public static FreightTemplateDto ToDto(FreightTemplateState s) => new()
    {
        Id = s.Id,
        MerchantId = s.MerchantId,
        Name = s.Name,
        ValuationMethod = s.ValuationMethod,
        IsFree = s.IsFree,
        FirstUnit = s.FirstUnit,
        FirstPrice = s.FirstPrice,
        ContinueUnit = s.ContinueUnit,
        ContinuePrice = s.ContinuePrice,
        FreeConditionAmount = s.FreeConditionAmount,
        AreaRules = s.AreaRules,
    };

    public static ShopGradeDto ToDto(ShopGradeState s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        ProductLimit = s.ProductLimit,
        ImageLimit = s.ImageLimit,
        TemplateLimit = s.TemplateLimit,
        ChargeStandard = s.ChargeStandard,
        Remark = s.Remark,
    };

    public static FullDiscountRuleDto ToDto(FullDiscountRuleState s) => new()
    {
        Id = s.Id,
        ShopId = s.ShopId,
        RuleName = s.RuleName,
        StartDate = s.StartDate,
        EndDate = s.EndDate,
        LimitValue = s.LimitValue,
        DiscountValue = s.DiscountValue,
        IsActive = s.IsActive,
    };

    public static CashDepositDto ToDto(CashDepositState s) => new()
    {
        Id = s.Id,
        ShopId = s.ShopId,
        CategoryId = s.CategoryId,
        Amount = s.Amount,
        Status = s.Status,
        PaidAt = s.PaidAt,
        DeductedAt = s.DeductedAt,
        NoReasonReturn = s.NoReasonReturn,
    };

    public static BusinessCategoryDto ToDto(BusinessCategoryState s) => new()
    {
        Id = s.Id,
        ShopId = s.ShopId,
        CategoryId = s.CategoryId,
        CommissionRate = s.CommissionRate,
        AuditStatus = s.AuditStatus,
        AuditRemark = s.AuditRemark,
    };

    public static ProductCommentDto ToDto(ProductCommentState s) => new()
    {
        Id = s.Id,
        ProductId = s.ProductId,
        OrderId = s.OrderId,
        UserId = s.UserId,
        Rank = s.Rank,
        Content = s.Content,
        Images = s.Images,
        ReplyContent = s.ReplyContent,
        ReplyTime = s.ReplyTime,
        IsAnonymous = s.IsAnonymous,
    };

    public static OrderComplaintDto ToDto(OrderComplaintState s) => new()
    {
        Id = s.Id,
        OrderId = s.OrderId,
        UserId = s.UserId,
        ShopId = s.ShopId,
        ComplaintReason = s.ComplaintReason,
        ComplaintContent = s.ComplaintContent,
        Status = s.Status,
        ReplyContent = s.ReplyContent,
        CreatedAt = s.CreatedAt,
        ResolvedAt = s.ResolvedAt,
    };

    public static SettlementDto ToDto(SettlementState s) => new()
    {
        Id = s.Id,
        MerchantId = s.MerchantId,
        PeriodStart = s.PeriodStart,
        PeriodEnd = s.PeriodEnd,
        TotalAmount = s.TotalAmount,
        PlatformFee = s.PlatformFee,
        SettledAmount = s.SettledAmount,
        Status = s.Status,
        SettledAt = s.SettledAt,
    };

    public static SettledConfigDto ToDto(SettledConfigState s) => new()
    {
        Id = s.Id,
        BusinessType = s.BusinessType,
        SettlementAccountType = s.SettlementAccountType,
        TrialDays = s.TrialDays,
        IsCity = s.IsCity,
        IsPeopleNumber = s.IsPeopleNumber,
        IsAddress = s.IsAddress,
        IsBusinessLicenseCode = s.IsBusinessLicenseCode,
        IsBusinessScope = s.IsBusinessScope,
        IsBusinessLicense = s.IsBusinessLicense,
    };

    public static TradeCommentDto ToDto(TradeCommentState s) => new()
    {
        Id = s.Id,
        OrderId = s.OrderId,
        UserId = s.UserId,
        ShopId = s.ShopId,
        DescriptionScore = s.DescriptionScore,
        ServiceScore = s.ServiceScore,
        LogisticsScore = s.LogisticsScore,
        Content = s.Content,
        IsAnonymous = s.IsAnonymous,
    };

    public static ShopShipperDto ToDto(ShopShipperState s) => new()
    {
        Id = s.Id,
        ShopId = s.ShopId,
        ShipperTag = s.ShipperTag,
        ShipperName = s.ShipperName,
        RegionId = s.RegionId,
        Address = s.Address,
        TelPhone = s.TelPhone,
        IsDefaultSendGoods = s.IsDefaultSendGoods,
        Longitude = s.Longitude,
        Latitude = s.Latitude,
    };

    public static ShopBrandApplyDto ToDto(ShopBrandApplyState s) => new()
    {
        Id = s.Id,
        ShopId = s.ShopId,
        BrandName = s.BrandName,
        ProofMaterial = s.ProofMaterial,
        AuditStatus = s.AuditStatus,
        AuditRemark = s.AuditRemark,
    };

    public static RepurchaseDto ToDto(RepurchaseState s) => new()
    {
        Id = s.Id,
        BuyerId = s.BuyerId,
        OriginalOrderId = s.OriginalOrderId,
        NewOrderId = s.NewOrderId,
        RepurchaseTime = s.RepurchaseTime,
    };

    public static PendingSettlementDto ToDto(PendingSettlementState s) => new()
    {
        Id = s.Id,
        OrderId = s.OrderId,
        ShopId = s.ShopId,
        OrderAmount = s.OrderAmount,
        PlatformCommission = s.PlatformCommission,
        RefundAmount = s.RefundAmount,
        SettleableAmount = s.SettleableAmount,
        Status = s.Status,
        SettlementId = s.SettlementId,
        CreatedAt = s.CreatedAt,
        SettledAt = s.SettledAt,
    };

    public static ShopWithdrawDto ToDto(ShopWithdrawState s) => new()
    {
        Id = s.Id,
        ShopId = s.ShopId,
        Amount = s.Amount,
        BankName = s.BankName,
        AccountNo = s.AccountNo,
        AccountName = s.AccountName,
        Status = s.Status,
        AuditRemark = s.AuditRemark,
        CreatedAt = s.CreatedAt,
        AuditedAt = s.AuditedAt,
        PaidAt = s.PaidAt,
    };

    public static ShopAccountItemDto ToDto(ShopAccountItemState s) => new()
    {
        Id = s.Id,
        ShopId = s.ShopId,
        AccountType = s.AccountType,
        Amount = s.Amount,
        BalanceAfter = s.BalanceAfter,
        Description = s.Description,
        RelatedId = s.RelatedId,
        CreatedAt = s.CreatedAt,
    };

    public static SettlementAccountDto ToDto(SettlementAccountState s) => new()
    {
        Id = s.Id,
        MerchantId = s.MerchantId,
        BankName = s.BankName,
        AccountNo = s.AccountNo,
        AccountName = s.AccountName,
        IsDefault = s.IsDefault,
    };

    public static LogisticsTrackDto ToDto(LogisticsTrackState s) => new()
    {
        Id = s.Id,
        OrderId = s.OrderId,
        ExpressCompanyName = s.ExpressCompanyName,
        ShipOrderNumber = s.ShipOrderNumber,
        TrackData = s.TrackData,
        LastQueriedAt = s.LastQueriedAt,
        LogisticsStatus = s.LogisticsStatus,
        IsReturn = s.IsReturn,
        RefundId = s.RefundId,
        OriginCity = s.OriginCity,
        DestinationCity = s.DestinationCity,
        CurrentLocation = s.CurrentLocation,
    };

    public static SettlementDetailDto ToDto(SettlementDetailState s) => new()
    {
        Id = s.Id,
        SettlementBillId = s.SettlementBillId,
        OrderId = s.OrderId,
        OrderNo = s.OrderNo,
        OrderAmount = s.OrderAmount,
        PlatformCommission = s.PlatformCommission,
        RefundAmount = s.RefundAmount,
        SettleableAmount = s.SettleableAmount,
    };

    public static ReturnShipmentDto ToDto(ReturnShipmentState s) => new()
    {
        Id = s.Id,
        RefundId = s.RefundId,
        ExpressCompanyName = s.ExpressCompanyName,
        ShipOrderNumber = s.ShipOrderNumber,
        ReturnAddress = s.ReturnAddress,
        ShippedAt = s.ShippedAt,
        ReceivedAt = s.ReceivedAt,
        Status = s.Status,
    };

    public static SettlementAccountSummaryDto ToDto(SettlementAccountSummaryState s) => new()
    {
        MerchantId = s.MerchantId,
        TotalSettled = s.TotalSettled,
        TotalWithdrawn = s.TotalWithdrawn,
        AvailableBalance = s.AvailableBalance,
        PendingSettlement = s.PendingSettlement,
        FrozenAmount = s.FrozenAmount,
    };

    public static ProductLadderPriceDto ToDto(ProductLadderPriceState s) => new()
    {
        Id = s.Id,
        ProductId = s.ProductId,
        MinBatch = s.MinBatch,
        MaxBatch = s.MaxBatch,
        Price = s.Price,
    };

    public static PlantingBatchDto ToDto(PlantingBatchState s) => new()
    {
        Id = s.Id,
        BatchName = s.BatchName,
        SpeciesId = s.SpeciesId,
        SpeciesName = s.SpeciesName,
        GreenhouseId = s.GreenhouseId,
        PlantingDate = s.PlantingDate,
        ExpectedHarvestDate = s.ExpectedHarvestDate,
        ActualHarvestDate = s.ActualHarvestDate,
        Status = s.Status,
        PlantingQuantity = s.PlantingQuantity,
        Remark = s.Remark,
        UserId = s.UserId,
        Passport = s.Passport,
    };

    public static CostRecordDto ToDto(CostRecordState s) => new()
    {
        Id = s.Id,
        BatchId = s.BatchId,
        Category = s.Category,
        Amount = s.Amount,
        CostDate = s.CostDate,
        Remark = s.Remark,
    };

    public static YieldRecordDto ToDto(YieldRecordState s) => new()
    {
        Id = s.Id,
        BatchId = s.BatchId,
        SpeciesId = s.SpeciesId,
        SpeciesName = s.SpeciesName,
        Quantity = s.Quantity,
        Unit = s.Unit,
        Grade = s.Grade,
        HarvestDate = s.HarvestDate,
        Remark = s.Remark,
    };
}
