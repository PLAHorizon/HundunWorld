using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services
{
    public class OrderDisplay
    {
        public long OrderId { get; set; }
        public string OrderNo { get; set; } = "";
        public int Status { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal OrderTotalAmount { get; set; }
        public decimal Freight { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal FullDiscount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string PaymentMethod { get; set; } = "";
        public string ShippingAddress { get; set; } = "";
        public string ShipTo { get; set; } = "";
        public string CellPhone { get; set; } = "";
        public string ExpressCompanyName { get; set; } = "";
        public string ShipOrderNumber { get; set; } = "";
        public List<OrderItemDisplay> Items { get; set; } = new();
        public bool CanShip => Status == 1;
        public bool CanViewLogistics => Status == 2;
    }

    public class OrderItemDisplay
    {
        public long ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Subtotal { get; set; }
    }

    public class RefundInfo
    {
        public long Id { get; set; }
        public long OrderId { get; set; }
        public string RefundNo { get; set; } = "";
        public decimal RefundAmount { get; set; }
        public string Reason { get; set; } = "";
        public int Status { get; set; }
        public int RefundMode { get; set; }
        public string SellerAuditRemark { get; set; } = "";
        public DateTime? SellerAuditTime { get; set; }
        public long MerchantId { get; set; }
    }

    public class CommentInfo
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public int Rank { get; set; }
        public string Content { get; set; } = "";
        public string Images { get; set; } = "";
        public string ReplyContent { get; set; } = "";
        public DateTime? ReplyTime { get; set; }
        public bool IsAnonymous { get; set; }
    }

    public class OrderRefundState
    {
        public long Id { get; set; }
        public long OrderId { get; set; }
        public string RefundNo { get; set; } = "";
        public decimal RefundAmount { get; set; }
        public string Reason { get; set; } = "";
        public int Status { get; set; }
        public int RefundMode { get; set; }
        public string SellerAuditRemark { get; set; } = "";
        public DateTime? SellerAuditTime { get; set; }
        public long MerchantId { get; set; }
    }

    public class ProductCommentState
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public int Rank { get; set; }
        public string Content { get; set; } = "";
        public string Images { get; set; } = "";
        public string ReplyContent { get; set; } = "";
        public DateTime? ReplyTime { get; set; }
        public bool IsAnonymous { get; set; }
    }

    public class OrderStateResponse
    {
        public long OrderId { get; set; }
        public string OrderNo { get; set; } = "";
        public int Status { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string PaymentMethod { get; set; } = "";
        public List<OrderItemState>? Items { get; set; }
    }

    public class OrderItemState
    {
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Subtotal { get; set; }
    }

    public class BatchShipRequest
    {
        public List<BatchShipItem> Items { get; set; } = new();
        public long ShipperId { get; set; }
    }

    public class BatchShipItem
    {
        public long OrderId { get; set; }
        public string ExpressCompanyName { get; set; } = "";
        public string ShipOrderNumber { get; set; } = "";
    }

    public class LogisticsTrackInfo
    {
        public long OrderId { get; set; }
        public string ExpressCompanyName { get; set; } = "";
        public string ShipOrderNumber { get; set; } = "";
        public List<LogisticsTrackNode> Tracks { get; set; } = new();
    }

    public class LogisticsTrackNode
    {
        public DateTime Time { get; set; }
        public string Description { get; set; } = "";
        public string Location { get; set; } = "";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class LogisticsMapDataInfo
    {
        public long OrderId { get; set; }
        public string ExpressCompanyName { get; set; } = "";
        public string ShipOrderNumber { get; set; } = "";
        public string OriginCity { get; set; } = "";
        public string DestinationCity { get; set; } = "";
        public int LogisticsStatus { get; set; }
        public List<LogisticsMapNodeInfo> Nodes { get; set; } = new();
    }

    public class LogisticsMapNodeInfo
    {
        public DateTime Time { get; set; }
        public string Description { get; set; } = "";
        public string Location { get; set; } = "";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class SettlementDetailInfo
    {
        public long Id { get; set; }
        public long SettlementBillId { get; set; }
        public long OrderId { get; set; }
        public string OrderNo { get; set; } = "";
        public decimal OrderAmount { get; set; }
        public decimal PlatformCommission { get; set; }
        public decimal SettleableAmount { get; set; }
        public DateTime OrderCreatedAt { get; set; }
    }

    public class SettlementAccountSummaryInfo
    {
        public long MerchantId { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalSettled { get; set; }
        public decimal TotalPending { get; set; }
        public decimal AvailableBalance { get; set; }
        public int PendingBillCount { get; set; }
    }

    public class FrequentProductInfo
    {
        public long ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal Price { get; set; }
        public int PurchaseCount { get; set; }
        public string ImageUrl { get; set; } = "";
        public long MerchantId { get; set; }
    }

    public class PaymentStatusResult
    {
        public bool Success { get; set; }
        public int Status { get; set; }
    }

    public class PaymentInitResult
    {
        public bool Success { get; set; }
        public string PayUrl { get; set; } = "";
        public long TransactionId { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    public class FlowerOrderService
    {
        private static readonly Dictionary<long, (LogisticsMapDataInfo Data, DateTime CachedAt)> _logisticsCache = new();
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        private sealed class OrderCreateResult
        {
            public long OrderId { get; set; }
            public string OrderNo { get; set; } = "";
        }

        public async Task<long?> CreateOrderAsync(Guid buyerId, List<CartItem> items, ShippingAddressInfo? address,
            decimal freight, decimal discountAmount, decimal fullDiscountAmount)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var orderItems = items.Select(i => new
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    Price = i.Price,
                    Quantity = i.Quantity,
                    Subtotal = i.Price * i.Quantity
                }).ToList();
                var firstMerchantId = items.FirstOrDefault()?.MerchantId ?? 0;
                var body = JsonSerializer.Serialize(new
                {
                    BuyerId = buyerId,
                    MerchantId = firstMerchantId,
                    Items = orderItems,
                    ShippingAddress = address?.FullAddress ?? "",
                    ShipTo = address?.ShipTo ?? "",
                    CellPhone = address?.Phone ?? "",
                    Freight = freight,
                    DiscountAmount = discountAmount,
                    FullDiscount = fullDiscountAmount,
                    Platform = "1"
                }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrder", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<OrderCreateResult>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true && result.Data != null ? result.Data.OrderId : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerOrderService] {nameof(CreateOrderAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<OrderDisplay>?> GetMyOrdersAsync(Guid buyerId, int page = 1, int pageSize = 20)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerOrder/my-orders?buyerId={buyerId}&page={page}&pageSize={pageSize}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<OrderDisplay>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerOrderService] {nameof(GetMyOrdersAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<OrderDisplay>?> GetMerchantOrdersAsync(long merchantId, int page = 1, int pageSize = 20)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerOrder/merchant-orders?merchantId={merchantId}&page={page}&pageSize={pageSize}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<OrderDisplay>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerOrderService] {nameof(GetMerchantOrdersAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<OrderDisplay>?> GetMerchantOrdersByStatusAsync(long merchantId, int? status, int page = 1, int pageSize = 20)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var url = $"{baseUri}FlowerOrder/merchant-orders?merchantId={merchantId}&page={page}&pageSize={pageSize}";
                if (status.HasValue) url += $"&status={status.Value}";
                var response = await FlowerHttpConfig.HttpClient.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<OrderStateResponse>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data?.Select(MapOrderToDisplay).ToList();
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(GetMerchantOrdersByStatusAsync)}: {ex.Message}"); return null; }
        }

        public async Task<PaymentInitResult?> PayOrderAsync(long orderId, int channel, decimal amount)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { OrderId = orderId, Channel = channel, Amount = amount }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerPayment/initiate", content).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return new PaymentInitResult { Success = false, ErrorMessage = $"HTTP {(int)response.StatusCode}" };

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var isSuccess = root.TryGetProperty("IsSuccess", out var successEl) && successEl.GetBoolean();
                var errorMsg = root.TryGetProperty("ErrorMessage", out var msgEl) ? msgEl.GetString() ?? "" : "";

                if (!isSuccess)
                    return new PaymentInitResult { Success = false, ErrorMessage = errorMsg };

                long txId = 0;
                string payUrl = "";
                if (root.TryGetProperty("Data", out var dataEl))
                {
                    if (dataEl.TryGetProperty("TransactionId", out var txIdEl)) txId = txIdEl.GetInt64();
                    if (dataEl.TryGetProperty("PayUrl", out var payUrlEl)) payUrl = payUrlEl.GetString() ?? "";
                }

                return new PaymentInitResult { Success = true, TransactionId = txId, PayUrl = payUrl };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerOrderService] {nameof(PayOrderAsync)}: {ex.Message}");
                return new PaymentInitResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<PaymentStatusResult?> QueryPaymentStatusAsync(long transactionId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerPayment/{transactionId}/status").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return new PaymentStatusResult { Success = false };

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var isSuccess = root.TryGetProperty("IsSuccess", out var successEl) && successEl.GetBoolean();
                if (!isSuccess) return new PaymentStatusResult { Success = false };

                int status = 0;
                if (root.TryGetProperty("Data", out var dataEl))
                {
                    if (dataEl.TryGetProperty("Status", out var statusEl)) status = statusEl.GetInt32();
                }

                return new PaymentStatusResult { Success = true, Status = status };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerOrderService] {nameof(QueryPaymentStatusAsync)}: {ex.Message}");
                return new PaymentStatusResult { Success = false };
            }
        }

        public async Task<bool> ShipOrderAsync(long orderId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrder/{orderId}/deliver", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerOrderService] {nameof(ShipOrderAsync)}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ShipOrderAsync(long orderId, string expressCompany, string shipOrderNumber, long shipperId = 0)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ExpressCompany = expressCompany, ShipOrderNumber = shipOrderNumber, ShipperId = shipperId }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrder/{orderId}/ship", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(ShipOrderAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> ConfirmDeliveryAsync(long orderId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrder/{orderId}/confirm-delivery", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerOrderService] {nameof(ConfirmDeliveryAsync)}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CompleteOrderAsync(long orderId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrder/{orderId}/complete", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerOrderService] {nameof(CompleteOrderAsync)}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CancelOrderAsync(long orderId, string reason = "买家取消订单")
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrder/{orderId}/cancel?reason={Uri.EscapeDataString(reason)}", null).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<bool>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerOrderService] {nameof(CancelOrderAsync)}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RequestRefundAsync(long orderId, string reason)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrder/{orderId}/refund?reason={Uri.EscapeDataString(reason ?? "")}", null).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<bool>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerOrderService] {nameof(RequestRefundAsync)}: {ex.Message}");
                return false;
            }
        }

        public async Task<RefundInfo?> RequestRefundAsync(long orderId, long orderItemId, decimal amount, string reason, int refundMode, Guid buyerId, long merchantId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { OrderId = orderId, OrderItemId = orderItemId, RefundAmount = amount, Reason = reason, RefundMode = refundMode, BuyerId = buyerId, MerchantId = merchantId }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrderRefund/request", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<OrderRefundState>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data != null ? MapRefundToInfo(result.Data) : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(RequestRefundAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<RefundInfo>?> GetMerchantRefundsAsync(long merchantId, int? status = null)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var url = $"{baseUri}FlowerOrderRefund/merchant/{merchantId}";
                if (status.HasValue) url += $"?status={status.Value}";
                var response = await FlowerHttpConfig.HttpClient.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<OrderRefundState>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data?.Select(MapRefundToInfo).ToList();
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(GetMerchantRefundsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> AuditRefundAsync(long refundId, bool approved, string remark)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { Approved = approved, Remark = remark }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrderRefund/{refundId}/seller-audit", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(AuditRefundAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<CommentInfo>?> GetProductCommentsAsync(long productId, int page = 1, int pageSize = 20)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerProductComment/product/{productId}?page={page}&pageSize={pageSize}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<ProductCommentState>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data?.Select(MapCommentToInfo).ToList();
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(GetProductCommentsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> ReplyCommentAsync(long commentId, string replyContent)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ReplyContent = replyContent }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerProductComment/{commentId}/reply", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(ReplyCommentAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> SubmitComplaintAsync(long orderId, Guid userId, long shopId, string reason, string content)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { OrderId = orderId, UserId = userId, ShopId = shopId, ComplaintReason = reason, ComplaintContent = content }, FlowerHttpConfig.JsonOptions);
                var content2 = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrderComplaint", content2).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(SubmitComplaintAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> SubmitTradeCommentAsync(long orderId, Guid userId, long shopId, int descScore, int serviceScore, int logisticsScore, string content, bool isAnonymous)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { OrderId = orderId, UserId = userId, ShopId = shopId, DescriptionScore = descScore, ServiceScore = serviceScore, LogisticsScore = logisticsScore, Content = content, IsAnonymous = isAnonymous }, FlowerHttpConfig.JsonOptions);
                var content2 = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerTradeComment", content2).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(SubmitTradeCommentAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> SubmitReturnShipmentAsync(long refundId, string expressCompanyName, string shipOrderNumber)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ExpressCompanyName = expressCompanyName, ShipOrderNumber = shipOrderNumber }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrderRefund/{refundId}/return-shipment", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(SubmitReturnShipmentAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> ConfirmReturnReceivedAsync(long refundId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrderRefund/{refundId}/confirm-received", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(ConfirmReturnReceivedAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> RepurchaseAsync(long orderId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrder/{orderId}/repurchase", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(RepurchaseAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> BatchShipOrdersAsync(BatchShipRequest request)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(request, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerOrder/batch-ship", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(BatchShipOrdersAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<FrequentProductInfo>?> GetFrequentProductsAsync(Guid buyerId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerOrder/frequent-products/{buyerId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<FrequentProductInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(GetFrequentProductsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<LogisticsTrackInfo?> GetLogisticsTrackAsync(long orderId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerLogistics/{orderId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<LogisticsTrackInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(GetLogisticsTrackAsync)}: {ex.Message}"); return null; }
        }

        public async Task<LogisticsTrackInfo?> GetReturnLogisticsTrackAsync(long refundId, string expressCompanyName, string shipOrderNumber)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerLogistics/return/{refundId}?expressCompanyName={Uri.EscapeDataString(expressCompanyName)}&shipOrderNumber={Uri.EscapeDataString(shipOrderNumber)}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<LogisticsTrackInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(GetReturnLogisticsTrackAsync)}: {ex.Message}"); return null; }
        }

        public async Task<LogisticsMapDataInfo?> GetLogisticsMapDataAsync(long orderId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerLogistics/{orderId}/map").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<LogisticsMapDataInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(GetLogisticsMapDataAsync)}: {ex.Message}"); return null; }
        }

        public async Task<LogisticsMapDataInfo?> GetLogisticsMapDataCachedAsync(long orderId)
        {
            if (_logisticsCache.TryGetValue(orderId, out var cached) &&
                (DateTime.Now - cached.CachedAt) < _cacheDuration)
            {
                return cached.Data;
            }

            var data = await GetLogisticsMapDataAsync(orderId);
            if (data != null)
            {
                _logisticsCache[orderId] = (data, DateTime.Now);
            }
            return data;
        }

        public void InvalidateLogisticsCache(long orderId)
        {
            _logisticsCache.Remove(orderId);
        }

        public async Task<List<SettlementDetailInfo>?> GetSettlementDetailsAsync(long settlementBillId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSettlement/{settlementBillId}/details").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<SettlementDetailInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(GetSettlementDetailsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<SettlementAccountSummaryInfo?> GetSettlementAccountSummaryAsync(long merchantId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSettlement/account/{merchantId}/summary").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<SettlementAccountSummaryInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerOrderService] {nameof(GetSettlementAccountSummaryAsync)}: {ex.Message}"); return null; }
        }

        private static RefundInfo MapRefundToInfo(OrderRefundState r)
        {
            return new RefundInfo
            {
                Id = r.Id,
                OrderId = r.OrderId,
                RefundNo = r.RefundNo ?? "",
                RefundAmount = r.RefundAmount,
                Reason = r.Reason ?? "",
                Status = r.Status,
                RefundMode = r.RefundMode,
                SellerAuditRemark = r.SellerAuditRemark ?? "",
                SellerAuditTime = r.SellerAuditTime,
                MerchantId = r.MerchantId
            };
        }

        private static CommentInfo MapCommentToInfo(ProductCommentState c)
        {
            return new CommentInfo
            {
                Id = c.Id,
                ProductId = c.ProductId,
                Rank = c.Rank,
                Content = c.Content ?? "",
                Images = c.Images ?? "",
                ReplyContent = c.ReplyContent ?? "",
                ReplyTime = c.ReplyTime,
                IsAnonymous = c.IsAnonymous
            };
        }

        private static OrderDisplay MapOrderToDisplay(OrderStateResponse o)
        {
            return new OrderDisplay
            {
                OrderId = o.OrderId,
                OrderNo = o.OrderNo ?? "",
                Status = o.Status,
                TotalAmount = o.TotalAmount,
                CreatedAt = o.CreatedAt,
                PaymentMethod = o.PaymentMethod ?? "",
                Items = o.Items?.Select(i => new OrderItemDisplay
                {
                    ProductName = i.ProductName ?? "",
                    Quantity = i.Quantity,
                    Subtotal = i.Subtotal
                }).ToList() ?? new()
            };
        }
    }
}
