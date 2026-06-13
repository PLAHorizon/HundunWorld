using System.Threading.Tasks;

using Horizon.IM.Message;

using Orleans;

namespace Horizon.Orleans.Interface;

[global::Orleans.CodeGeneration.Version(1)]
public interface IIMGatewayObserver : IGrainObserver
{
    Task OnMessageAsync(ulong userId, IMMessageUnion message);
}