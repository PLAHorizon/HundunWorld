using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using MemoryPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 花卉数据采集后台服务。
    /// 使用 TouchSocket TCP 监听器接收 IoT 传感器数据和市场数据，
    /// 反序列化后转发到对应的 Orleans Grain。
    /// </summary>
    public class FlowerDataCollectionService : BackgroundService
    {
        private readonly ILogger<FlowerDataCollectionService> _logger;
        private readonly IClusterClient _clusterClient;
        private readonly IConfiguration _configuration;

        private TcpService? _tcpService;

        public FlowerDataCollectionService(
            ILogger<FlowerDataCollectionService> logger,
            IClusterClient clusterClient,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var port = _configuration.GetValue<int>("FlowerDataCollection:Port", 7790);
            var ipAddress = _configuration.GetValue<string>("FlowerDataCollection:IpAddress", "0.0.0.0");

            var enableTcpCollector = _configuration.GetValue<bool>("FlowerIoT:EnableTcpCollector", false);
            if (!enableTcpCollector)
            {
                _logger.LogWarning("TCP 数据采集模式已弃用，建议迁移到 MQTT。如需启用，请设置 FlowerIoT:EnableTcpCollector=true");
                await Task.Delay(Timeout.Infinite, stoppingToken);
                return;
            }

            try
            {
                _logger.LogInformation("花卉数据采集服务正在启动...");

                _tcpService = new TcpService();
                _tcpService.Connected = OnClientConnected;
                _tcpService.Closed = OnClientDisconnected;
                _tcpService.Received = OnDataReceived;

                var config = new TouchSocketConfig()
                    .SetListenIPHosts(ipAddress, port)
                    .SetTcpDataHandlingAdapter(() => new FlowerDataMessageAdapter())
                    .ConfigureContainer(container =>
                    {
                        container.AddLogger(ConsoleLogger.Default);
                    });

                await _tcpService.SetupAsync(config);
                await _tcpService.StartAsync();

                _logger.LogInformation("花卉数据采集服务启动成功，监听 {IpAddress}:{Port}", ipAddress, port);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("花卉数据采集服务收到停止信号");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "花卉数据采集服务运行时发生错误，服务将停止但不影响网关主服务");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在停止花卉数据采集服务");

            try
            {
                if (_tcpService != null)
                {
                    await _tcpService.StopAsync();
                    _tcpService.Dispose();
                    _tcpService = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止花卉数据采集服务时发生错误");
            }

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("花卉数据采集服务已停止");
        }

        private Task OnClientConnected(ITcpSessionClient client, ConnectedEventArgs e)
        {
            _logger.LogInformation("花卉数据采集客户端已连接: {Id}", client.Id);
            return Task.CompletedTask;
        }

        private Task OnClientDisconnected(ITcpSessionClient client, ClosedEventArgs e)
        {
            _logger.LogInformation("花卉数据采集客户端已断开: {Id}, 原因: {Message}", client.Id, e.Message);
            return Task.CompletedTask;
        }

        private async Task OnDataReceived(ITcpSessionClient client, ReceivedDataEventArgs e)
        {
            try
            {
                if (e.RequestInfo is not FlowerDataMessageInfo messageInfo || messageInfo.Payload == null)
                {
                    _logger.LogWarning("收到无法解析的花卉数据帧: {Id}", client.Id);
                    return;
                }

                switch (messageInfo.MessageType)
                {
                    case FlowerDataMessageType.SensorData:
                        await HandleSensorDataAsync(messageInfo.Payload);
                        break;
                    case FlowerDataMessageType.MarketData:
                        await HandleMarketDataAsync(messageInfo.Payload);
                        break;
                    default:
                        _logger.LogWarning("收到未知的花卉数据消息类型: {MessageType}", messageInfo.MessageType);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理花卉数据采集消息时发生错误: {Id}", client.Id);
            }
        }

        private async Task HandleSensorDataAsync(byte[] payload)
        {
            try
            {
                var reading = MemoryPackSerializer.Deserialize<SensorReading>(payload);
                if (reading == null || string.IsNullOrEmpty(reading.DeviceId))
                {
                    _logger.LogWarning("传感器数据反序列化失败或DeviceId为空");
                    return;
                }

                var deviceGrain = _clusterClient.GetGrain<IIoTDeviceGrain>(reading.DeviceId);
                await deviceGrain.UpdateReadingAsync(reading);

                _logger.LogDebug("传感器数据已转发: DeviceId={DeviceId}", reading.DeviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理传感器数据失败");
            }
        }

        private async Task HandleMarketDataAsync(byte[] payload)
        {
            try
            {
                var snapshot = MemoryPackSerializer.Deserialize<FlowerPriceSnapshot>(payload);
                if (snapshot == null)
                {
                    _logger.LogWarning("市场数据反序列化失败");
                    return;
                }

                var marketGrain = _clusterClient.GetGrain<IFlowerMarketGrain>(snapshot.MarketId);
                await marketGrain.UpdateSnapshotAsync(snapshot);

                _logger.LogDebug("市场数据已转发: MarketId={MarketId}, SpeciesId={SpeciesId}", snapshot.MarketId, snapshot.SpeciesId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理市场数据失败");
            }
        }
    }

    internal enum FlowerDataMessageType : byte
    {
        SensorData = 0x01,
        MarketData = 0x02
    }

    internal class FlowerDataMessageAdapter : CustomFixedHeaderDataHandlingAdapter<FlowerDataMessageInfo>
    {
        public override int HeaderLength => 5;

        protected override FlowerDataMessageInfo GetInstance() => new();
    }

    internal class FlowerDataMessageInfo : IFixedHeaderRequestInfo
    {
        public FlowerDataMessageType MessageType { get; private set; }
        public byte[]? Payload { get; private set; }
        public int BodyLength { get; set; }
        public int MaxLength => 1024 * 1024;

        public bool OnParsingHeader(ReadOnlySpan<byte> header)
        {
            if (header.Length < 5)
                return false;

            MessageType = (FlowerDataMessageType)header[0];
            BodyLength = BitConverter.ToInt32(header.Slice(1));
            return BodyLength > 0 && BodyLength <= MaxLength;
        }

        public bool OnParsingBody(ReadOnlySpan<byte> body)
        {
            if (body.Length != BodyLength)
                return false;

            Payload = body.ToArray();
            return true;
        }

        public void Build<TByteBlock>(ref TByteBlock byteBlock) where TByteBlock : IByteBlock
        {
            if (Payload == null)
                return;

            var header = new byte[5];
            header[0] = (byte)MessageType;
            BitConverter.TryWriteBytes(header.AsSpan(1), Payload.Length);
            byteBlock.Write(header);
            byteBlock.Write(Payload);
        }

        public bool TryBuild(ReadOnlySequence<byte> buffer, int length, out IRequestInfo requestInfo)
        {
            requestInfo = default!;
            if (buffer.Length < 5)
                return false;

            var header = buffer.Slice(0, 5).ToArray();
            var msgType = (FlowerDataMessageType)header[0];
            var bodyLen = BitConverter.ToInt32(header, 1);

            if (bodyLen <= 0 || bodyLen > MaxLength)
                return false;
            if (buffer.Length < 5 + bodyLen)
                return false;

            var payload = buffer.Slice(5, bodyLen).ToArray();

            requestInfo = new FlowerDataMessageInfo
            {
                MessageType = msgType,
                Payload = payload,
                BodyLength = bodyLen
            };
            return true;
        }

        public bool TryBuild(ReadOnlySequence<byte> buffer, out IRequestInfo requestInfo)
            => TryBuild(buffer, (int)buffer.Length, out requestInfo);
    }
}
