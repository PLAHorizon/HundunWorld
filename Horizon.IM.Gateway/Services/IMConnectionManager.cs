using System.Collections.Concurrent;
using System.Net.Sockets;

using Microsoft.Extensions.Logging;

namespace Horizon.IM.Gateway.Services;

public class IMConnectionManager : IIMConnectionManager
{
    private readonly ConcurrentDictionary<string, IMConnection> _connections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ulong, string> _userBindings = new();
    private readonly ILogger<IMConnectionManager> _logger;

    public IMConnectionManager(ILogger<IMConnectionManager> logger)
    {
        _logger = logger;
    }

    public Task<bool> AddConnectionAsync(IMConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return Task.FromResult(_connections.TryAdd(connection.Id, connection));
    }

    public Task RemoveConnectionAsync(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return Task.CompletedTask;
        }

        if (_connections.TryRemove(connectionId, out var connection) && connection.UserId > 0)
        {
            _userBindings.TryRemove(connection.UserId, out _);
        }

        return Task.CompletedTask;
    }

    public IMConnection? GetConnection(string connectionId)
    {
        return string.IsNullOrWhiteSpace(connectionId)
            ? null
            : _connections.TryGetValue(connectionId, out var connection) ? connection : null;
    }

    public Task BindUserAsync(ulong userId, string connectionId)
    {
        if (userId == 0 || string.IsNullOrWhiteSpace(connectionId))
        {
            return Task.CompletedTask;
        }

        if (_connections.TryGetValue(connectionId, out var connection))
        {
            if (_userBindings.TryGetValue(userId, out var previousConnectionId)
                && !string.Equals(previousConnectionId, connectionId, StringComparison.Ordinal)
                && _connections.TryGetValue(previousConnectionId, out var previousConnection))
            {
                previousConnection.UserId = 0;
            }

            connection.UserId = userId;
            _userBindings[userId] = connectionId;
        }

        return Task.CompletedTask;
    }

    public IMConnection? GetConnectionByUser(ulong userId)
    {
        return _userBindings.TryGetValue(userId, out var connectionId)
            ? GetConnection(connectionId)
            : null;
    }

    public async Task<bool> SendToUserAsync(ulong userId, byte[] message)
    {
        var connection = GetConnectionByUser(userId);
        if (connection == null)
        {
            return false;
        }

        try
        {
            await connection.SendAsync(message).ConfigureAwait(false);
            return true;
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "客户端已断开连接，无法发送消息: UserId={UserId}, ConnectionId={ConnectionId}", userId, connection.Id);
            await RemoveConnectionAsync(connection.Id).ConfigureAwait(false);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "发送消息失败: UserId={UserId}, ConnectionId={ConnectionId}", userId, connection.Id);
            await RemoveConnectionAsync(connection.Id).ConfigureAwait(false);
            return false;
        }
    }
}