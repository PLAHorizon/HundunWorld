using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Horizon.Game.Gateway.Monitoring
{
    /// <summary>
    /// 混沌世界游戏网关自定义指标定义
    /// 提供连接、消息处理、网络性能等核心指标
    /// </summary>
    public static class GatewayMetrics
    {
        /// <summary>
        /// 指标源名称
        /// </summary>
        public const string MeterName = "HundunWorld.Gateway";

        /// <summary>
        /// 活动源名称（用于分布式追踪）
        /// </summary>
        public const string ActivitySourceName = "HundunWorld.Gateway";

        private static readonly Meter Meter = new(MeterName, "1.0.0");
        private static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

        // ========== 连接指标 ==========

        /// <summary>当前活跃连接数</summary>
        public static readonly UpDownCounter<long> ActiveConnections = Meter.CreateUpDownCounter<long>(
            "hundunworld.gateway.connections.active",
            description: "当前活跃客户端连接数");

        /// <summary>连接建立总数</summary>
        public static readonly Counter<long> ConnectionsEstablishedTotal = Meter.CreateCounter<long>(
            "hundunworld.gateway.connections.established.total",
            description: "连接建立总数");

        /// <summary>连接断开总数</summary>
        public static readonly Counter<long> ConnectionsClosedTotal = Meter.CreateCounter<long>(
            "hundunworld.gateway.connections.closed.total",
            description: "连接断开总数");

        /// <summary>连接错误总数</summary>
        public static readonly Counter<long> ConnectionErrorsTotal = Meter.CreateCounter<long>(
            "hundunworld.gateway.connections.errors.total",
            description: "连接错误总数");

        // ========== 消息处理指标 ==========

        /// <summary>接收消息总数</summary>
        public static readonly Counter<long> MessagesReceivedTotal = Meter.CreateCounter<long>(
            "hundunworld.gateway.messages.received.total",
            description: "接收到的消息总数");

        /// <summary>发送消息总数</summary>
        public static readonly Counter<long> MessagesSentTotal = Meter.CreateCounter<long>(
            "hundunworld.gateway.messages.sent.total",
            description: "发送的消息总数");

        /// <summary>消息处理时长（毫秒）</summary>
        public static readonly Histogram<double> MessageProcessingDuration = Meter.CreateHistogram<double>(
            "hundunworld.gateway.messages.processing_duration.ms",
            unit: "ms",
            description: "消息处理时长");

        /// <summary>消息处理错误总数</summary>
        public static readonly Counter<long> MessageErrorsTotal = Meter.CreateCounter<long>(
            "hundunworld.gateway.messages.errors.total",
            description: "消息处理错误总数");

        // ========== 网络性能指标 ==========

        /// <summary>接收字节总数</summary>
        public static readonly Counter<long> BytesReceivedTotal = Meter.CreateCounter<long>(
            "hundunworld.gateway.network.bytes_received.total",
            unit: "bytes",
            description: "接收字节总数");

        /// <summary>发送字节总数</summary>
        public static readonly Counter<long> BytesSentTotal = Meter.CreateCounter<long>(
            "hundunworld.gateway.network.bytes_sent.total",
            unit: "bytes",
            description: "发送字节总数");

        /// <summary>网络延迟（毫秒）</summary>
        public static readonly Histogram<double> NetworkLatency = Meter.CreateHistogram<double>(
            "hundunworld.gateway.network.latency.ms",
            unit: "ms",
            description: "网络延迟");

        // ========== 负载均衡指标 ==========

        /// <summary>Orleans Grain调用总数</summary>
        public static readonly Counter<long> OrleansCallsTotal = Meter.CreateCounter<long>(
            "hundunworld.gateway.orleans.calls.total",
            description: "Orleans Grain调用总数");

        /// <summary>Orleans Grain调用失败总数</summary>
        public static readonly Counter<long> OrleansCallErrorsTotal = Meter.CreateCounter<long>(
            "hundunworld.gateway.orleans.call_errors.total",
            description: "Orleans Grain调用失败总数");

        // ========== 分布式追踪辅助方法 ==========

        /// <summary>
        /// 创建一个消息处理的活动追踪
        /// </summary>
        public static Activity? StartMessageActivity(string messageType)
        {
            return ActivitySource.StartActivity(
                $"Gateway/Message/{messageType}",
                ActivityKind.Server);
        }
    }
}
