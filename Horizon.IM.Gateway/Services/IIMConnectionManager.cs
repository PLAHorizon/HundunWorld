namespace Horizon.IM.Gateway.Services;

public interface IIMConnectionManager
{
    Task<bool> AddConnectionAsync(IMConnection connection);

    Task RemoveConnectionAsync(string connectionId);

    IMConnection? GetConnection(string connectionId);

    Task BindUserAsync(ulong userId, string connectionId);

    /// <summary>
    /// 获取用户的第一个连接（向后兼容）
    /// </summary>
    IMConnection? GetConnectionByUser(ulong userId);

    /// <summary>
    /// 获取用户的所有连接（支持多设备同时在线）
    /// </summary>
    IReadOnlyList<IMConnection> GetConnectionsByUser(ulong userId);

    /// <summary>
    /// 检查用户是否还有活跃连接
    /// </summary>
    bool HasConnections(ulong userId);

    /// <summary>
    /// 发送消息到用户的所有设备
    /// </summary>
    Task<bool> SendToUserAsync(ulong userId, byte[] message);
}
