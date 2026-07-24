using Horizon.Game.Core.Interfaces;
using Horizon.Game.Gateway.Configuration;
using Microsoft.Extensions.Options;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 会话绑定校验器实现：基于 <see cref="IConnectionManager"/> 的 characterId↔connectionId 映射，
/// 验证指定连接是否绑定了指定角色 ID。<br/>
/// 灰度开关 <see cref="GatewayOptions.EnableSessionBindingValidation"/> 控制是否启用。
/// </summary>
public sealed class SessionBindingValidator : ISessionBindingValidator
{
    private readonly IConnectionManager _connectionManager;
    private readonly IOptionsMonitor<GatewayOptions> _options;

    public SessionBindingValidator(
        IConnectionManager connectionManager,
        IOptionsMonitor<GatewayOptions> options)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public bool IsEnabled => _options.CurrentValue.EnableSessionBindingValidation;

    /// <inheritdoc />
    public bool IsCharacterBoundToConnection(string connectionId, long characterId)
    {
        if (string.IsNullOrEmpty(connectionId)) return false;

        var boundCharacters = _connectionManager.GetCharacterIdsByConnection(connectionId);
        return boundCharacters.Contains(characterId);
    }
}
