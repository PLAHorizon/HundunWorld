using Horizon.Share.Dtos.Games;
using Horizon.Share.Dtos.User;
using Orleans;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 游戏用户 Grain 接口（登录链路游戏用户/角色层契约）。
    /// <para>
    /// 客户端（UE5 ManagedHundunWorld GameLoginClient 与 GengDi 启动器 AccountService）
    /// 登录成功后调用 GameUserRole/* WebApi 端点完成"构建游戏用户 → 拉取角色列表"，
    /// 本 Grain 即该层契约的实现载体（WebApi 为 Orleans Client，只引用 Interface 不引用 Grains）。
    /// </para>
    /// <para>
    /// 数据模型：Basic 库 Basic_Sys_User（PassportId → Guid Id）为通行证主体；
    /// Game 库 Game_HunduShijie_User（user_id 自增 long，GameUserId=Basic User.Id）为游戏用户；
    /// Game 库 Game_HunduShijie_Character（user_id=游戏用户 Id）为角色。
    /// </para>
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IGameUserGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 注册（或复用）游戏用户：按通行证创建 Game_HunduShijie_User 记录。
        /// passportId 由 WebApi 从 JWT 声明解析（register 端点为 Authorize）。
        /// </summary>
        Task<GameUserInfoDto> RegisterPassportAndGameUserAsync(GameRegisterDto registerDto, string passportId);

        /// <summary>按通行证号 + 游戏 Id 查询游戏用户（不存在返回 null）。</summary>
        Task<GameUserInfoDto> GetGameUserAsync(GameUserQeuryDto queryDto);

        /// <summary>拉取游戏用户角色列表（gameuserroles；客户端取首个 UserRoleId 作为 Sync 握手 LocalCharacterId）。</summary>
        Task<GameUserRoleInfoDto[]> GetGameUserRolesAsync(GameUserRoleQueryDto queryDto);

        /// <summary>拉取游戏基础角色模板目录（gameroles；当前返回空目录，登录闭环不依赖）。</summary>
        Task<GameRoleNormalDto[]> GetGameRolesAsync(GameRoleQueryDto queryDto);

        /// <summary>获取角色在游戏世界中的地理信息（出生点/存档位置）。</summary>
        Task<GameRoleWorldInfoDto> GetGameRoleWorldInfoAsync(GameUserRoleQueryDto queryDto);

        /// <summary>保存角色在游戏世界中的地理信息。</summary>
        Task<bool> SetGameRoleWorldInfoAsync(GameRoleWorldInfoDto roleDto);

        /// <summary>创建游戏角色（createroles；角色创建子流程提交）。</summary>
        Task<bool> CreateGameRoleAsync(GameRoleDto roleDto);
    }
};
