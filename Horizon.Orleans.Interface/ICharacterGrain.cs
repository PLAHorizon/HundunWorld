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
        /// 加经验并按曲线升等——<b>服务端唯一的等级写入口</b>。
        /// <para>
        /// 角色表的 <c>experience</c> 列自创建置 0 后全仓再无任何写入方，也没有升等逻辑；
        /// 而技能习得门槛读 <c>learn_level_require</c>。等级不可变等于黄/玄/地/天阶在游戏里永远走不到，
        /// §14 的动态条件只剩白阶可达。曲线与上限收敛在 ProgressionCurve 一处，调用方不要自己算。
        /// </para>
        /// </summary>
        /// <param name="amount">经验增量，必须为正；一次可跨多级。</param>
        Task<AddExpResult> AddExpAsync(long amount);
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

        /// <summary>
        /// 五行权威裁决版 HP 变更。与 <see cref="RequestHpChangeAsync"/> 走同一具裁决体，
        /// 区别只是把攻方所用元素与攻方五行破防带进来。<br/>
        /// 减免口径由 <c>ElementalDamageResolver</c> 单点提供：
        /// 有效防御 = max(0, 通用防御 + 守方命中行五行防御(已高者计) - 攻方破防(已高者计))，
        /// 曲线 100 常数，克制倍率只作用于攻方取行。
        /// </summary>
        /// <param name="hpDelta">HP 变化量（负数为伤害，正数为治疗）。</param>
        /// <param name="sourceId">伤害/治疗来源实体 ID（0 表示环境）。</param>
        /// <param name="damageType">伤害类型。</param>
        /// <param name="attackElement">攻方所用五行（1~5；0 = 无属性，按守方最高防御行处理）。</param>
        /// <param name="attackerPenetration">攻方五行破防（各来源取最高后的最终向量）。</param>
        /// <param name="defenderBuffDefense">
        /// 守方当前生效 Buff 的五行防御（取高侧）。Buff 记录在战斗侧（CombatGrain 的 ActiveEffects），
        /// 而防御裁决在角色档案侧，所以由战斗侧把这份<b>临时增量</b>随请求带来，
        /// 与档案自己的常驻来源一起走同一套"已高者计"聚合——两侧各存一份防御会各自漂移。
        /// </param>
        Task<HpChangeResult> RequestElementalHpChangeAsync(int hpDelta, ulong sourceId, DamageType damageType,
            int attackElement, Horizon.Game.Message.Combat.ElementStatVector attackerPenetration,
            Horizon.Game.Message.Combat.ElementStatVector defenderBuffDefense);

        /// <summary>
        /// 失效并重算角色侧权威战斗画像（通用攻防基值 + 五行三行来源）。
        /// <para>
        /// 穿戴/卸下、镶嵌宝石、加点、技能变更后必须由对应写路径调用：CharacterGrain 按角色 ID 寻址，
        /// 是唯一能被可靠定向失效的那一层，受击减免读的就是这份画像。
        /// 不在缓存里、也未被本方法失效的攻方侧画像由 CombatGrain 的 TTL 收敛。
        /// </para>
        /// </summary>
        Task RefreshCombatProfileAsync();

        /// <summary>
        /// 取角色侧权威战斗画像的下行快照（五行三行 + 通用攻防基值 + 五行属性值）。
        /// <para>
        /// 与受击裁决读的是<b>同一份</b> <c>CombatProfile</c>，所以面板显示的三行就是这一击真正生效的三行；
        /// 客户端自己的"加点 + 装备硬编码表"只在画像取不到时作为镜像兜底。
        /// </para>
        /// </summary>
        Task<Horizon.Game.Message.Network.CharacterAttributeRefreshResponse> GetCombatProfileSnapshotAsync();

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

        /// <summary>
        /// 获取远程玩家头顶名牌所需的**权威档案**（昵称/等级/称号）。
        /// <para>
        /// DS 灰度通道下握手由网关直接转发给 DS，不经过 <see cref="OnEnterZoneAsync"/> 的属性推送，
        /// 因此 DS 侧完全没有档案来源。网关在转发前调用本方法取权威值并写入握手包，
        /// 使远程玩家显示真实昵称与等级，而非 <c>Player_&lt;id&gt;</c> / <c>Lv.1</c> 占位。
        /// </para>
        /// </summary>
        Task<CharacterSyncProfile> GetSyncProfileAsync();

        /// <summary>
        /// 属性加点：提交**期望终态**（九项全量，全零即重置），服务端校验后写 grain 状态（Redis 缓存）
        /// 并 upsert <c>Game_HunduShijie_CharacterAttribute</c>（落库）。
        /// <para>
        /// 校验失败时不落库、不改缓存，响应里回当前权威分配，客户端据此回滚预览，因此无需额外的查询接口。
        /// </para>
        /// </summary>
        Task<AttributeAllocateResponse> AllocateAttributeAsync(ulong characterId, AttributeAllocationData desired);

        /// <summary>
        /// 存一份数字人外观。服务端不解释 blob，但三道闸一道不少：
        /// 已发布拒改、模板不一致拒收、Blob 校验和由服务端另算一遍再比（不信客户端自述）。
        /// </summary>
        Task<Horizon.Game.Message.Network.UpdateCharacterAppearanceResponse> UpdateAppearanceAsync(
            Horizon.Game.Message.Network.MetaHumanLookPayload look);

        /// <summary>读回已存的外观（没有则返回 null）。</summary>
        Task<Horizon.Game.Message.Network.MetaHumanLookPayload> GetAppearanceAsync();

        /// <summary>外观是否已发布（服务端判定，不是客户端 UI 判定；否则改包就能绕过）。</summary>
        Task<bool> IsAppearancePublishedAsync();
    }

    /// <summary>
    /// 远程玩家基础档案（昵称/等级/称号）。称号为空表示未佩戴。
    /// </summary>
    [GenerateSerializer]
    public sealed class CharacterSyncProfile
    {
        /// <summary>角色昵称。</summary>
        [Id(0)] public string Nickname { get; set; } = "";

        /// <summary>角色等级。</summary>
        [Id(1)] public int Level { get; set; }

        /// <summary>当前称号名（空 = 未佩戴称号）。</summary>
        [Id(2)] public string Title { get; set; } = "";

        /// <summary>阵营（0 未入 / 1 正派 / 2 邪派）。PvP 敌对判定的唯一权威输入。</summary>
        [Id(3)] public int Faction { get; set; }
    }


}