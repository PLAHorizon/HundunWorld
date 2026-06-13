using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Gateway.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 驱动 <see cref="GatewaySyncDispatcher"/> 的后台服务（P6-a 运行时连线）。<br/>
    /// 灰度关：<see cref="GatewayOptions.UseSyncPacketDispatch"/>=false 时 <see cref="StartAsync"/>
    /// 直接返回，不启动循环——保持老路径零副作用。
    /// </summary>
    /// <remarks>
    /// 工作循环：不停调用 <see cref="GatewaySyncDispatcher.RunOnceAsync"/>；dispatcher 内部用
    /// <see cref="IZoneShardFanoutSource.TryDequeueAsync"/> 自行阻塞等待，所以这里不需要额外 sleep。
    /// 当取消 token 触发时，dispatcher 返回 0，循环自然退出。
    /// </remarks>
    public sealed class SyncDispatcherHostedService : BackgroundService
    {
        private readonly GatewaySyncDispatcher _dispatcher;
        private readonly IOptionsMonitor<GatewayOptions> _options;
        private readonly ILogger<SyncDispatcherHostedService> _logger;

        public SyncDispatcherHostedService(
            GatewaySyncDispatcher dispatcher,
            IOptionsMonitor<GatewayOptions> options,
            ILogger<SyncDispatcherHostedService> logger)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.CurrentValue.UseSyncPacketDispatch)
            {
                _logger.LogInformation(
                    "SyncDispatcherHostedService：UseSyncPacketDispatch=false，跳过 fanout 循环（保持老广播路径）。");
                return;
            }
            _dispatcher.Enabled = true;
            _logger.LogInformation("SyncDispatcherHostedService：进入 ZoneShard fanout 循环。");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _dispatcher.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SyncDispatcherHostedService：RunOnceAsync 异常；1s 后重试。");
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }

            _dispatcher.Enabled = false;
            _logger.LogInformation(
                "SyncDispatcherHostedService：退出；累计 processed={Processed}, delivered={Delivered}, droppedOffline={Dropped}。",
                _dispatcher.ProcessedEventCount,
                _dispatcher.DeliveredPacketCount,
                _dispatcher.DroppedOfflineCount);
        }
    }
}
