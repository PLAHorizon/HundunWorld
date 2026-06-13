using Horizon.Game.Message.Network;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.Payment
{
    public interface IPaymentChannel
    {
        PaymentChannel ChannelType { get; }

        Task<PrepayResult> CreatePrepayAsync(long orderId, string orderNo, decimal amount, string description, PaymentScene scene = PaymentScene.Native);

        Task<PaymentCallbackResult> HandleCallbackAsync(string callbackData);

        Task<PaymentQueryResult> QueryPaymentStatusAsync(string transactionNo);

        Task<RefundResult> RefundAsync(string transactionNo, decimal refundAmount, string reason);

        Task<bool> CloseTransactionAsync(string transactionNo);
    }

    public class PrepayResult
    {
        public bool Success { get; set; }
        public string PrepayId { get; set; } = "";
        public string PayUrl { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    public class PaymentCallbackResult
    {
        public bool Success { get; set; }
        public string TransactionNo { get; set; } = "";
        public string ChannelTransactionNo { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime? PaidAt { get; set; }
        public string RawData { get; set; } = "";
        public string NotifyId { get; set; } = "";
    }

    public class PaymentQueryResult
    {
        public bool Success { get; set; }
        public int Status { get; set; }
        public string ChannelTransactionNo { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime? PaidAt { get; set; }
    }

    public class RefundResult
    {
        public bool Success { get; set; }
        public string ChannelRefundNo { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }
}
