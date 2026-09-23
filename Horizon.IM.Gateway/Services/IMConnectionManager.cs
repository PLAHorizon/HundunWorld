using System.Collections.Concurrent;
using System.Net.Sockets;

using Microsoft.Extensions.Logging;

namespace Horizon.IM.Gateway.Services;

public class IMConnectionManager : IIMConnectionManager
{
    private readonly ConcurrentDictionary<string, IMConnection> _connections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<string, bool>> _userBindings = new();
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
            if (_userBindings.TryGetValue(connection.UserId, out var connSet))
            {
                connSet.TryRemove(connectionId, out _);
                if (connSet.IsEmpty)
                {
                    _userBindings.TryRemove(connection.UserId, out _);
                }
            }
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
            connection.UserId = userId;
            var connSet = _userBindings.GetOrAdd(userId, _ => new ConcurrentDictionary<string, bool>(StringComparer.Ordinal));
            connSet[connectionId] = true;
        }

        return Task.CompletedTask;
    }

    public IMConnection? GetConnectionByUser(ulong userId)
    {
        if (!_userBindings.TryGetValue(userId, out var connSet) || connSet.IsEmpty)
        {
            return null;
        }

        foreach (var connId in connSet.Keys)
        {
            if (_connections.TryGetValue(connId, out var connection))
            {
                return connection;
            }
        }

        return null;
    }

    public IReadOnlyList<IMConnection> GetConnectionsByUser(ulong userId)
    {
        if (!_userBindings.TryGetValue(userId, out var connSet) || connSet.IsEmpty)
        {
            return Array.Empty<IMConnection>();
        }

        var list = new List<IMConnection>();
        foreach (var connId in connSet.Keys)
        {
            if (_connections.TryGetValue(connId, out var connection))
            {
                list.Add(connection);
            }
        }

        return list;
    }

    public bool HasConnections(ulong userId)
    {
        return _userBindings.TryGetValue(userId, out var connSet) && !connSet.IsEmpty;
    }

    public async Task<bool> SendToUserAsync(ulong userId, byte[] message)
    {
        var connections = GetConnectionsByUser(userId);
        if (connections.Count == 0)
        {
            return false;
        }

        var success = false;
        foreach (var connection in connections)
        {
            try
            {
                await connection.SendAsync(message).ConfigureAwait(false);
                success = true;
            }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "客户端已断开连接，无法发送消息: UserId={UserId}, ConnectionId={ConnectionId}", userId, connection.Id);
                await RemoveConnectionAsync(connection.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "发送消息失败: UserId={UserId}, ConnectionId={ConnectionId}", userId, connection.Id);
                await RemoveConnectionAsync(connection.Id).ConfigureAwait(false);
            }
        }

        return success;
    }
}
