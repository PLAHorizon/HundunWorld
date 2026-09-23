using Orleans;
using System;
using System.Threading.Tasks;
using Horizon.Game.Message;
using System.Collections.Generic;
using Horizon.Share.Dtos.Games;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 角色Grain接口 - 负责角色数据管理和游戏逻辑
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface ICharacterGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 创建新角色
        /// </summary>
        /// <param name="request">创建角色请求</param>
        /// <returns>创建角色响应</returns>
        Task<CreateCharacterResponse> CreateCharacterAsync(CreateCharacterRequest request);

        /// <summary>
        /// 获取角色信息
        /// </summary>
        /// <param name="characterId">角色Id</param>
        /// <returns>角色信息</returns>
        Task<CharacterInfo> GetCharacterInfoAsync(GameQueryDto gameQueryDto);
        /// <summary>
        /// 获取用户所有角色
        /// </summary>
        /// <param name="gameUserId"></param>
        /// <returns></returns>
        Task<List<CharacterInfo>> GetAllCharactersAsync(GameQueryDto gameQueryDto);
        /// <summary>
        /// 角色进入游戏
        /// </summary>
        /// <param name="request">进入游戏请求</param>
        /// <returns>进入游戏响应</returns>
        Task<EnterGameResponse> EnterGameAsync(EnterGameRequest request);

        /// <summary>
        /// 角色离线
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> GoOfflineAsync();

        /// <summary>
        /// 检查角色是否在线
        /// </summary>
        /// <returns>是否在线</returns>
        Task<bool> IsOnlineAsync();

        /// <summary>
        /// 处理死亡
        /// </summary>
        /// <param name="request">死亡消息</param>
        /// <returns>处理结果</returns>
        Task<DeathMessage> HandleDeathAsync(DeathMessage request);

        /// <summary>
        /// 复活
        /// </summary>
        /// <param name="request">复活请求</param>
        /// <returns>复活响应</returns>
        Task<ResurrectMessage> ResurrectAsync(ResurrectMessage request);

        // ===== P1.1 统一角色状态模型：空间生命周期钩子 =====

        /// <summary>
        /// 角色进入空间（ZoneShard Spawn）时由 <see cref="IZoneShardGrain"/> 调用。<br/>
        /// CharacterGrain 据此：(1) 记录当前所在 ZoneShard；(2) 将权威 RPG 属性推送到 ZoneShard 广播缓存。
        /// </summary>
        /// <param name="zoneShardId">所在 ZoneShard 的 Grain Key。</param>
        Task OnEnterZoneAsync(long zoneShardId);

        /// <summary>
        /// 角色离开空间（ZoneShard Despawn）时由 <see cref="IZoneShardGrain"/> 调用。<br/>
        /// CharacterGrain 据此：(1) 持久化最终状态；(2) 清除所在空间标记。
        /// </summary>
        /// <param name="zoneShardId">离开的 ZoneShard 的 Grain Key。</param>
        /// <param name="finalHp">离开时的最终 HP。</param>
        /// <param name="reason">离开原因。</param>
        Task OnLeaveZoneAsync(long zoneShardId, int finalHp, ZoneLeaveReason reason);

        /// <summary>
        /// 请求 HP 变更（战斗伤害/治疗/环境伤害）。<br/>
        /// CharacterGrain 作为 RPG 权威裁决实际伤害（含防御/减伤/Buff），
        /// 并将新 HP 推送到 ZoneShard 广播缓存。
        /// </summary>
        /// <param name="hpDelta">HP 变化量（负数为伤害，正数为治疗）。</param>
        /// <param name="sourceId">伤害/治疗来源实体 ID（0 表示环境）。</param>
        /// <param name="damageType">伤害类型。</param>
        /// <returns>实际 HP 变化量及是否导致死亡。</returns>
        Task<HpChangeResult> RequestHpChangeAsync(int hpDelta, ulong sourceId, DamageType damageType);

        // ===== 角色位置缓存（用于 ZoneShardGrain 重激活后恢复实体位置） =====

        /// <summary>
        /// 更新角色最后已知位置（由 ZoneShardGrain 定期 fire-and-forget 调用）。
        /// 坐标为 ECS Z-up（X=左右, Y=前后, Z=上下）。
        /// </summary>
        Task UpdateLastPositionAsync(float x, float y, float z, float yaw);

        /// <summary>
        /// 获取角色最后已知位置。
        /// 返回 null 表示无有效位置数据（首次创建角色或数据过期）。
        /// </summary>
        Task<(float X, float Y, float Z, float Yaw)?> GetLastPositionAsync();
    }

    
}