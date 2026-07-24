namespace Horizon.Game.Core.Interfaces;

/// <summary>
/// 会话绑定校验器：验证指定连接是否绑定了指定角色 ID。<br/>
/// 由 Gateway 层实现（基于 ConnectionManager 的 characterId↔connectionId 映射），
/// 用于防止身份伪造（恶意客户端在包中填入他人 characterId）。<br/>
/// 灰度开关 <c>GatewayOptions.EnableSessionBindingValidation</c> 控制是否启用，
/// 默认关闭；验证通过后开启。
/// </summary>
public interface ISessionBindingValidator
{
    /// <summary>
    /// 校验功能是否已启用（由配置灰度开关控制）。
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 验证指定连接是否绑定了指定角色 ID。
    /// </summary>
    /// <param name="connectionId">TCP 连接 ID。</param>
    /// <param name="characterId">待验证的角色 ID。</param>
    /// <returns>true 表示该连接确实绑定了该角色；false 表示未绑定或连接不存在。</returns>
    bool IsCharacterBoundToConnection(string connectionId, long characterId);
}
