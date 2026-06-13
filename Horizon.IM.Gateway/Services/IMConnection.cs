using TouchSocket.Sockets;

namespace Horizon.IM.Gateway.Services;

public class IMConnection
{
    public IMConnection(ITcpSessionClient client)
    {
        Client = client;
        Id = client.Id;
        CreatedTime = DateTime.UtcNow;
        LastActiveTime = DateTime.UtcNow;
    }

    public string Id { get; }

    public ITcpSessionClient Client { get; }

    public ulong UserId { get; set; }

    public DateTime CreatedTime { get; }

    public DateTime LastActiveTime { get; set; }

    public Task SendAsync(byte[] message)
    {
        LastActiveTime = DateTime.UtcNow;
        return Client.SendAsync(message);
    }
}