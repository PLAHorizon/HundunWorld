using Microsoft.Extensions.Hosting;
using Horizon.IM.Gateway.Network;

namespace Horizon.IM.Gateway.Services;

public class IMGatewayHostedService : IHostedService
{
    private readonly IMNetworkServer _networkServer;
    private readonly IMGatewayPushService _pushService;

    public IMGatewayHostedService(IMNetworkServer networkServer, IMGatewayPushService pushService)
    {
        _networkServer = networkServer;
        _pushService = pushService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _pushService.StartAsync(cancellationToken).ConfigureAwait(false);
        await _networkServer.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _networkServer.StopAsync(cancellationToken).ConfigureAwait(false);
        await _pushService.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}