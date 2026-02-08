using Horizon.Share.Dtos.User;
using Orleans;
using System;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 通行证 Grain 接口，负责用户账户的认证和管理。
    /// Grain ID 为用户唯一标识符 (Guid)。
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IPassportGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 用户登录认证。
        /// </summary>
        /// <param name="loginDto">包含登录凭据的数据传输对象。</param>
        /// <returns>认证成功则返回通行证信息，否则根据具体实现可能抛出异常或返回特定错误代码的对象。</returns>
        Task<PassportInfoDto> AuthenticationAsync(LoginDto loginDto);

        /// <summary>
        /// 微信用户登录认证。
        /// </summary>
        /// <param name="loginDto">包含微信登录凭据的数据传输对象。</param>
        /// <returns>认证成功则返回通行证信息，否则根据具体实现可能抛出异常或返回特定错误代码的对象。</returns>
        Task<PassportInfoDto> WxUserAuthenticationAsync(WxLoginDto loginDto);

        /// <summary>
        /// 用户退出登录。
        /// </summary>
        /// <param name="loginDto">包含用户标识信息的登录数据对象，用于确认退出哪个账户。</param>
        /// <returns>操作成功返回 true，失败返回 false。</returns>
        Task<bool> SignOutAsync(LoginDto loginDto);

        /// <summary>
        /// 修改用户密码。
        /// </summary>
        /// <param name="changePasswordDto">包含旧密码和新密码的数据传输对象。</param>
        /// <returns>密码修改成功返回 true，失败返回 false。</returns>
        Task<bool> ChangePasswordAsync(ChangePasswordDto changePasswordDto);

        /// <summary>
        /// 用户注册新账户。
        /// </summary>
        /// <param name="loginDto">包含注册信息的数据传输对象。</param>
        /// <returns>注册成功则返回新的通行证信息，否则根据具体实现可能抛出异常或返回特定错误代码的对象。</returns>
        Task<PassportInfoDto> RegisterAsync(RegisterDto loginDto);

        /// <summary>
        /// (管理功能) 创建指定数量的通行证ID。
        /// 此方法可能用于批量生成测试账户或预注册账户。
        /// </summary>
        /// <param name="count">要创建的通行证ID数量。</param>
        /// <returns>表示异步操作的任务。</returns>
        Task CreatePassportIdAsync(int count);

        /// <summary>
        /// (管理功能) 取消正在进行的创建通行证ID的操作。
        /// </summary>
        /// <returns>表示异步操作的任务。</returns>
        Task CancelCreatePassportIdAsync();

        /// <summary>
        /// 注销用户账户。
        /// </summary>
        /// <param name="passportId">要注销的通行证ID。</param>
        /// <returns>操作成功返回 true，失败返回 false。</returns>
        Task<bool> CancelPassportAsync(string passportId);

        /// <summary>
        /// 更新用户会话信息
        /// </summary>
        /// <param name="sessionInfo">会话信息</param>
        /// <returns>操作成功返回 true，失败返回 false。</returns>
        Task<bool> UpdateSessionInfoAsync(SessionInfoMessage sessionInfo);

        /// <summary>
        /// 获取所有角色信息
        /// </summary>
        /// <param name="gameQueryDto">游戏查询数据传输对象</param>
        /// <returns>角色信息列表</returns>
        Task<List<CharacterInfo>> GetAllCharactersAsync(Share.Dtos.Games.GameQueryDto gameQueryDto);
    }
}