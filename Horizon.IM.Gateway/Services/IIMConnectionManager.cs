namespace Horizon.IM.Gateway.Services;

public interface IIMConnectionManager
{
    Task<bool> AddConnectionAsync(IMConnection connection);

    Task RemoveConnectionAsync(string connectionId);

    IMConnection? GetConnection(string connectionId);

    Task BindUserAsync(ulong userId, string connectionId);

    IMConnection? GetConnectionByUser(ulong userId);

    Task<bool> SendToUserAsync(ulong userId, byte[] message);
}