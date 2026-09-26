using AutoMapper;
using AutoMapper.QueryableExtensions;
using Horizon.Core.Abstract;
using Horizon.Core.Helper;
using Horizon.Entities;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using CharacterState = Horizon.Game.Message.Network.CharacterState;
using Horizon.Share.Dtos.Games;
using MemoryPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TouchSocket.Core;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// Character Grain to manage all character-related logic and data.
    /// </summary>
    public class CharacterGrain : Grain<CharacterState>, ICharacterGrain
    {
        private readonly ILogger<CharacterGrain> _logger;
        private readonly IPersistentState<CharacterState> _characterState;
        private readonly ICharacterPresenceStore _presenceStore;
        private readonly ICharacterPositionStore _positionStore;
        private readonly ICharacterFingerprintService _fingerprintService;

        private readonly IDataContext<GameEntityContext, UserEntity, long> _gameUserContext;
        private readonly IDataContext<GameEntityContext, CharacterEntity, long> _gameCharacterContext;
        private readonly IDataContext<GameEntityContext, TitleEntity, long> _gameTitleContext;

        /// <summary>
        /// 属性加点落库通道（表 <c>Game_HunduShijie_CharacterAttribute</c>）。
        /// Redis grain 状态是热缓存，此表是权威持久层。
        /// </summary>
        private readonly IDataContext<GameEntityContext, CharacterAttributeEntity, long> _gameAttributeContext;

        /// <summary>属性点池：每级发放的自由点数（与客户端展示口径一致，服务端为权威）。</summary>
        private const int AllocationPointsPerLevel = 5;
        private const int AllocationKindCount = 9;
        private readonly IMapper _mapper;

        private ulong CharacterId { get; set; } = 0;

        /// <summary>
        /// 角色是否在线（内存缓存，不持久化）。<br/>
        /// 权威在线状态由 Redis presence key 管理，见 <see cref="ICharacterPresenceStore"/>。
        /// </summary>
        private bool _isOnline;

        /// <summary>
        /// P1.1：当前所在 ZoneShard 的 Grain Key（0 表示不在任何空间中）。<br/>
        /// 由 <see cref="OnEnterZoneAsync"/> 设置，<see cref="OnLeaveZoneAsync"/> 清除。
        /// </summary>
        private long _currentZoneShardId;



        public CharacterGrain(
            ILogger<CharacterGrain> logger,
            [PersistentState("character", "GameStore")] IPersistentState<CharacterState> characterState,
            ICharacterPresenceStore presenceStore,
            ICharacterPositionStore positionStore,
            ICharacterFingerprintService fingerprintService,

            IDataContext<GameEntityContext, UserEntity, long> gameUserContext,
            IDataContext<GameEntityContext, CharacterEntity, long> gameCharacterContext,
            IDataContext<GameEntityContext, TitleEntity, long> gameTitleContext,
            IDataContext<GameEntityContext, CharacterAttributeEntity, long> gameAttributeContext,
            IMapper mapper)
        {
            _logger = logger;
            _characterState = characterState;
            _presenceStore = presenceStore;
            _positionStore = positionStore;
            _fingerprintService = fingerprintService;

            _mapper = mapper;
            _gameUserContext = gameUserContext;
            _gameCharacterContext = gameCharacterContext;
            _gameTitleContext = gameTitleContext;
            _gameAttributeContext = gameAttributeContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            CharacterId = (ulong)this.GetPrimaryKeyLong();
            _logger.LogInformation("CharacterGrain {CharacterId} 正在激活。", CharacterId);

            // 防御性初始化：CustomGrainStorageSerializer 在 Redis 无数据或反序列化失败时会返回 default(T)，
            // 对于引用类型 CharacterState 即为 null，导致后续访问 .CharacterInfo 抛出 NullReferenceException。
            // 这里确保 State 一定为非 null 实例。
            if (_characterState.State == null)
            {
                _logger.LogWarning("CharacterGrain {CharacterId} 激活时 State 为 null（持久化存储无数据或反序列化失败），初始化为默认实例。", CharacterId);
                _characterState.State = new CharacterState();
            }

            // If the state is new, it means this is the first activation or state was cleared.
            // We try to load from the database as a fallback.
            if (_characterState.State.CharacterInfo == null)
            {
                _logger.LogInformation("未找到 {CharacterId} 的状态，尝试从数据库加载。", CharacterId);
                var characterEntity = await _gameCharacterContext.QueryFirstOrDefaultAsync(c => c.Id == (long)CharacterId);
                if (characterEntity != null)
                {
                    _characterState.State.CharacterInfo = _mapper.Map<CharacterInfo>(characterEntity);
                    _isOnline = false; // 内存缓存默认离线，权威状态由 Redis presence 管理
                    await _characterState.WriteStateAsync();
                    _logger.LogInformation("已从数据库成功加载角色 {CharacterName}。", characterEntity.CharacterName);
                }
                else
                {
                    _logger.LogWarning("激活期间未在数据库中找到角色 {CharacterId}。", CharacterId);
                }
            }
            else
            {
                // 双轨制架构：IsOnline 已从 CharacterState 中移除，不再持久化。
                // grain 激活即新实例，角色必然不在线（必须重新调用 EnterGameAsync 才能上线）。
                // 权威在线状态由 Redis presence key（TTL 90 秒）管理，IsOnlineAsync 查询 Redis。
                if (_isOnline)
                {
                    _logger.LogDebug(
                        "CharacterGrain {CharacterId} 激活时发现内存 _isOnline=true，重置为 false（权威状态由 Redis presence 管理）。",
                        CharacterId);
                    _isOnline = false;
                }
            }

            // ===== 属性加点：grain 状态为空时从 SQL 权威层回填（Redis 只是热缓存） =====
            // 典型触发场景：Redis 被清空/换环境、或该角色首次启用加点功能。
            await EnsureAllocationLoadedAsync();

            // ===== 从 Redis 永久存储加载角色位置（双轨制架构） =====
            // 服务器重启后 Grain 重新激活时，从 Redis 恢复角色最后位置到内存缓存。
            // Redis 不可用时降级到 GrainState 中的旧值，加载失败不影响 Grain 激活。
            try
            {
                var savedPosition = await _positionStore.GetPositionAsync((long)CharacterId);
                if (savedPosition.HasValue)
                {
                    _characterState.State.LastPositionX = savedPosition.Value.X;
                    _characterState.State.LastPositionY = savedPosition.Value.Y;
                    _characterState.State.LastPositionZ = savedPosition.Value.Z;
                    _characterState.State.LastYaw = savedPosition.Value.Yaw;
                    _characterState.State.LastPositionUpdateUtc = DateTime.UtcNow;
                    _logger.LogInformation(
                        "CharacterGrain {CharacterId} 从 Redis 恢复位置：X={X}, Y={Y}, Z={Z}, Yaw={Yaw}",
                        CharacterId, savedPosition.Value.X, savedPosition.Value.Y,
                        savedPosition.Value.Z, savedPosition.Value.Yaw);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CharacterGrain {CharacterId} 从 Redis 加载位置失败", CharacterId);
            }

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<CreateCharacterResponse> CreateCharacterAsync(CreateCharacterRequest request)
        {
            try
            {
                _logger.LogInformation("开始创建角色: {CharacterName}, UserId: {UserId}", 
                    request.CharacterName, request.UserId);

                // 1. 棄用现有角色检查（因为这个 Grain 是为单个角色设计的）
                if (_characterState.State.CharacterInfo != null&&CharacterId!=0)
                {
                    _logger.LogWarning("该 Grain 已经有角色了: {ExistingCharacterName}", 
                        _characterState.State.CharacterInfo.CharacterName);
                    return new CreateCharacterResponse 
                    { 
                        IsSuccess = false, 
                        Message = "该 Grain 已经有角色了。" 
                    };
                }

                // 2. 验证用户状态
                var gameuser = await _gameUserContext.QueryFirstOrDefaultAsync(
                    u => u.Id == (long)request.UserId && !u.IsDeleted && u.IsValid);
                    
                if (gameuser == null)
                {
                    _logger.LogWarning("用户不存在或已被禁用: UserId={UserId}", request.UserId);
                    return new CreateCharacterResponse 
                    { 
                        IsSuccess = false, 
                        Message = "用户不存在或已被禁用。" 
                    };
                }
                
                if (gameuser.Status > 0)
                {
                    _logger.LogWarning("用户账号被封禁: UserId={UserId}, Status={Status}", 
                        request.UserId, gameuser.Status);
                    return new CreateCharacterResponse 
                    { 
                        IsSuccess = false, 
                        Message = "账号被封禁，无法创建角色，请联系游戏管理员。" 
                    };
                }

                // 3. 验证角色数量限制
                var existingCharacterCount = await GetCharacterCountForUser((long)request.UserId, request.GameId);
                if (existingCharacterCount >= 5) // 最大角色数量限制
                {
                    _logger.LogWarning("用户角色数量已达上限: UserId={UserId}, Count={Count}", 
                        request.UserId, existingCharacterCount);
                    return new CreateCharacterResponse 
                    { 
                        IsSuccess = false, 
                        Message = "角色数量已达上限（5个），请删除不需要的角色后再创建。" 
                    };
                }

                // 4. 检查角色名是否已存在
                var cleanedName = await ValidateAndCleanCharacterName(request.CharacterName);
                if (string.IsNullOrEmpty(cleanedName))
                {
                    return new CreateCharacterResponse 
                    { 
                        IsSuccess = false, 
                        Message = "角色名包含非法字符或敏感词汇，请重新输入。" 
                    };
                }

                var nameExists = await _gameCharacterContext.QueryFirstOrDefaultAsync(
                    c => c.CharacterName == cleanedName && c.GameId == request.GameId && !c.IsDeleted);
                    
                if (nameExists != null)
                {
                    _logger.LogWarning("角色名已存在: {CharacterName}", cleanedName);
                    return new CreateCharacterResponse 
                    { 
                        IsSuccess = false, 
                        Message = "角色名已存在，请重新创建角色。" 
                    };
                }

                // 5. 创建角色实体
                var characterEntity = _mapper.Map<CharacterEntity>(request);
                characterEntity.CharacterName = cleanedName;
                characterEntity.Level = 1;
                characterEntity.Experience = 0;
                characterEntity.CreateTime = DateTime.Now;
                characterEntity.ServerId = request.ServerId;
                characterEntity.AreaId = request.ZoneId;
                characterEntity.GameId = request.GameId;
                characterEntity.UserId = (long)request.UserId;
                characterEntity.GameUserId = gameuser.Id;
                
                // 设置默认出生位置（根据职业不同可能不同）
                characterEntity = SetDefaultStartingLocation(characterEntity, request.Profession);

                // 6. 保存到数据库
                characterEntity = await _gameCharacterContext.AddAsync(characterEntity);

                // 7. 更新 Grain 状态
                _characterState.State.CharacterInfo = _mapper.Map<CharacterInfo>(characterEntity);
                _isOnline = false;
                await _characterState.WriteStateAsync();
                
                CharacterId = (ulong)characterEntity.Id;

                _logger.LogInformation("角色创建成功: {CharacterName} (ID: {CharacterId}), UserId: {UserId}", 
                    cleanedName, CharacterId, request.UserId);

                return new CreateCharacterResponse
                {
                    IsSuccess = true,
                    Message = "创建角色成功",
                    Character = _characterState.State.CharacterInfo
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建角色时发生异常: {CharacterName}, UserId: {UserId}", 
                    request.CharacterName, request.UserId);
                    
                return new CreateCharacterResponse
                {
                    IsSuccess = false,
                    Message = "创建角色失败，请稍后重试。"
                };
            }
        }

        public Task<CharacterInfo> GetCharacterInfoAsync(GameQueryDto gameQueryDto)
        {
            // The grain key is the characterId (as a Guid). This parameter seems redundant but we'll comply.
            if (_characterState.State.CharacterInfo == null)
            {
                _logger.LogWarning("请求角色 {CharacterId} 信息，但状态为空。", gameQueryDto.CharacterId);
                return Task.FromResult<CharacterInfo>(null);
            }
            return Task.FromResult(_characterState.State.CharacterInfo);
        }

        public async Task<EnterGameResponse> EnterGameAsync(EnterGameRequest request)
        {
            try
            {
                _logger.LogInformation("角色进入游戏: CharacterId={CharacterId}", request.CharacterId);

                // 1. 检查角色数据是否加载
                if (_characterState.State.CharacterInfo == null)
                {
                    _logger.LogWarning("角色数据未加载: CharacterId={CharacterId}", request.CharacterId);
                    return new EnterGameResponse
                    {
                        Success = false,
                        Message = "角色数据未加载。"
                    };
                }

                // 2. 检查角色是否已经在线（查询 Redis 权威源）
                //    注意：检测到残留在线状态时仅记录日志，不执行 ZoneShard 实体清理。
                //    EnterWorldAsync（ZoneShardGrain）已有幂等性检查：若实体已存在会先 UnregisterEntityAsync
                //    再重新注册，无需在 CharacterGrain 业务层提前清理。提前清理会在 EnterGameAsync（阶段1）
                //    与 EnterWorldAsync（阶段2，HandshakePacket 触发）之间产生实体空窗期，
                //    导致其他玩家在时间窗口内看不到该角色，且可能触发 PlayerDespawnScheduler 的
                //    实体丢失误检测（用默认出生点重新注册，AOI 不匹配 → 角色永久不可见）。
                var isOnlineInRedis = await _presenceStore.IsOnlineAsync((long)CharacterId);
                if (isOnlineInRedis || _isOnline)
                {
                    _logger.LogInformation("角色已经在线: {CharacterName} (Redis={Redis}, Mem={Mem})",
                        _characterState.State.CharacterInfo.CharacterName, isOnlineInRedis, _isOnline);
                    // 允许重复进入，但记录日志
                }

                // 3. 更新角色内存状态（IsOnline 不持久化，只保留在内存中）
                _isOnline = true;
                _characterState.State.CharacterInfo.LastLoginTime = DateTime.Now;

                // 4. 优先设置 Redis presence key（权威在线状态，TTL 90 秒）
                //    放在 WriteStateAsync 之前：即使后续业务数据持久化失败，Redis 在线状态已设置，
                //    避免角色"进入游戏但 IsOnlineAsync 返回 false"的不一致状态。
                //    gatewayId/connectionId 此处传空字符串，由 Gateway 侧在连接建立时更新真实值。
                //    如果 Redis 不可用，SetOnlineAsync 内部降级返回 false，不影响进入游戏流程。
                var presenceSet = await _presenceStore.SetOnlineAsync((long)CharacterId, string.Empty, string.Empty);
                if (!presenceSet)
                {
                    _logger.LogWarning(
                        "角色 {CharacterId} Redis presence 设置失败（Redis 可能不可用），仅使用内存状态。",
                        CharacterId);
                }

                // 4.5 同步更新 GameServerGrain 持久化在线角色列表（修复严重 BUG 的核心修复点）。
                //     原代码 GameServerState.OnlinePlayers 虽被 [PersistentState] 自动持久化，
                //     但业务层从未调用 PlayerOnlineAsync 维护此列表，导致角色离线后持久化在线信息
                //     一直未更新、角色永久残留服务端。此处显式调用以建立持久化在线列表的写入路径。
                //     默认服务器 ID = 1；调用失败不应阻断进入游戏主流程，仅记录告警。
                try
                {
                    var gameServerGrain = GrainFactory.GetGrain<IGameServerGrain>(1L);
                    await gameServerGrain.PlayerOnlineAsync((long)CharacterId);
                }
                catch (Exception gameServerEx)
                {
                    _logger.LogWarning(gameServerEx,
                        "更新 GameServerGrain 在线列表失败（不影响进入游戏流程）: CharacterId={CharacterId}",
                        CharacterId);
                }

                // 5. 在数据库中更新最后登录时间
                await UpdateCharacterLastLoginTime(_characterState.State.CharacterInfo.CharacterId);

                // 6. 保存 grain 状态（仅持久化 CharacterInfo 等业务数据，IsOnline 不再持久化）
                await _characterState.WriteStateAsync();

                _logger.LogInformation("角色成功进入游戏: {CharacterName} (ID: {CharacterId})",
                    _characterState.State.CharacterInfo.CharacterName,
                    _characterState.State.CharacterInfo.CharacterId);

                // 7. 返回成功响应。必须设置 CharacterId 顶层字段，网关依赖此字段
                //    注册 characterId → connection 映射，断线 Despawn 正确反查角色 ID。
                //    [修复] 返回前调用 EnrichCharacterInfo 从 Redis 持久化存储读取离线位置，
                //    填充到 CharacterInfo.Position，确保客户端使用服务端持久化的位置初始化角色，
                //    而非数据库中可能过期的 Position 字段或默认 (0,0,0)。
                //    客户端 EnterGameHandler 会用此 Position 创建本地 ECS 实体并发送握手包，
                //    服务端 ZoneShardGrain.EnterWorldAsync 也会优先从 CharacterGrain.GetLastPositionAsync()
                //    读取同一权威源，保证两端初始位置一致。
                await EnrichCharacterInfo(_characterState.State.CharacterInfo);
                // 加点随 CharacterInfo 一并下发：客户端无需再发查询即可重建第三层属性来源。
                await EnsureAllocationLoadedAsync();
                _characterState.State.CharacterInfo.Allocation = CurrentAllocation();
                return new EnterGameResponse
                {
                    Success = true,
                    Message = $"角色{_characterState.State.CharacterInfo.CharacterName}进入游戏",
                    CharacterId = _characterState.State.CharacterInfo.CharacterId,
                    CharacterInfo = _characterState.State.CharacterInfo
                    // 这里还可以加载其他数据，如背包、技能、任务等
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "角色进入游戏时发生异常: CharacterId={CharacterId}",
                    request.CharacterId);

                return new EnterGameResponse
                {
                    Success = false,
                    Message = "进入游戏失败，请稍后重试。"
                };
            }
        }

        public async Task<bool> GoOfflineAsync()
        {
            // 关键修复（严重 BUG 根因）：原实现在 CharacterInfo == null 时直接 return false，
            // 导致 Redis presence key / fingerprint key 不被清理、DeactivateOnIdle 不被调用，
            // 角色离线后仍以"在线"状态残留服务端长达 presence TTL（90 秒）甚至更久。
            //
            // 正确行为：无论 CharacterInfo 是否加载，都必须清理 Redis 权威在线状态，
            // 并标记 grain 停用。CharacterInfo == null 时仅跳过业务数据持久化。
            _isOnline = false;

            var hasCharacterInfo = _characterState.State?.CharacterInfo != null;
            var characterName = _characterState.State?.CharacterInfo?.CharacterName ?? "<未加载>";

            // 1) 清理 Redis presence key（权威在线状态，最高优先级）
            //    即使 Redis 不可用，SetOfflineAsync 内部降级返回 false，不影响后续清理步骤。
            try
            {
                var presenceCleared = await _presenceStore.SetOfflineAsync((long)CharacterId);
                if (!presenceCleared)
                {
                    _logger.LogWarning(
                        "角色 {CharacterId} Redis presence 清理失败（Redis 可能不可用），依赖 Monitor 兜底。",
                        CharacterId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "角色 {CharacterId} Redis presence 清理异常，依赖 Monitor 兜底。",
                    CharacterId);
            }

            // 1.5) 立即更新 GameServerGrain 持久化在线列表（修复严重 BUG 的核心修复点）。
            //      原代码 GameServerState.OnlinePlayers 由 [PersistentState] 自动持久化，
            //      但业务层从未调用 PlayerOfflineAsync，导致角色离线后持久化在线信息从未被更新、
            //      角色永久残留服务端。此处显式调用，确保"角色离线后立即更新持久信息"。
            //      调用失败不应阻断后续清理（fingerprint、WriteState），仅记录告警，
            //      兜底由 PlayerDespawnScheduler.DespawnImmediatelyAsync 与 CharacterPresenceMonitor 完成。
            try
            {
                var gameServerGrain = GrainFactory.GetGrain<IGameServerGrain>(1L);
                await gameServerGrain.PlayerOfflineAsync((long)CharacterId);
            }
            catch (Exception gameServerEx)
            {
                _logger.LogWarning(gameServerEx,
                    "更新 GameServerGrain 在线列表失败（不影响后续离线清理流程）: CharacterId={CharacterId}",
                    CharacterId);
            }

            // 2) 清理 Redis fingerprint key（character:fingerprint:{id}，TTL 5min）。
            //    fingerprint 是网关侧防止同一角色重复登录的锁，离线时必须立即清理，
            //    否则角色离线后 Redis 中仍残留 fingerprint key 长达 5 分钟，
            //    外部观察"角色在线信息未及时更新"，且会导致角色重新上线时被指纹拦截（误判已在线）。
            try
            {
                await _fingerprintService.ReleaseAsync((long)CharacterId);
            }
            catch (Exception fpEx)
            {
                _logger.LogWarning(fpEx,
                    "角色 {CharacterId} Redis fingerprint 清理失败（依赖 TTL 5min 兜底过期）",
                    CharacterId);
            }

            // 3) 持久化 CharacterInfo 等业务数据（不含 IsOnline，IsOnline 已从 CharacterState 移除）。
            //    仅在 CharacterInfo 已加载时执行，避免持久化空状态覆盖已有数据。
            if (hasCharacterInfo)
            {
                try
                {
                    await _characterState.WriteStateAsync();
                }
                catch (Exception stateEx)
                {
                    _logger.LogWarning(stateEx,
                        "角色 {CharacterId} 持久化业务数据失败（不影响在线状态清理）",
                        CharacterId);
                }
            }
            else
            {
                _logger.LogWarning(
                    "角色 {CharacterId} GoOfflineAsync 时 CharacterInfo 为 null，跳过业务数据持久化（仍已清理 Redis 在线状态）",
                    CharacterId);
            }

            _logger.LogInformation("角色 '{CharacterName}'（{CharacterId}）已下线。", characterName, CharacterId);

            // 4) 标记 grain 停用以释放资源（必须执行，否则 grain 保持激活态）
            DeactivateOnIdle();

            return true;
        }

        public async Task<bool> IsOnlineAsync()
        {
            // 双轨制架构：查询 Redis presence 作为权威源。
            // Redis 不可用时返回 false（降级），调用方应结合 ConnectionManager 内存状态兜底判断。
            return await _presenceStore.IsOnlineAsync((long)CharacterId);
        }

        public async Task<List<CharacterInfo>> GetAllCharactersAsync(GameQueryDto gameQueryDto)
        {
            try
            {
                _logger.LogInformation("获取用户角色列表: UserId={UserId}, GameId={GameId}", 
                    gameQueryDto.GameUserId, gameQueryDto.GameId);

                // 查询用户的所有角色
                var charactersQuery = await _gameCharacterContext.QueryAsync(
                    m => m.UserId == gameQueryDto.GameUserId && 
                         m.GameId == gameQueryDto.GameId && 
                         m.IsValid && 
                         !m.IsDeleted);
                
                // 排序
                var characters = charactersQuery
                    .OrderByDescending(c => c.LastLoginTime)
                    .ThenByDescending(c => c.CreateTime)
                    .ToList();

                // 转换为 CharacterInfo 列表
                var characterInfos = _mapper.Map<List<CharacterInfo>>(characters);

                // 为每个角色填充额外信息（如需要）
                foreach (var characterInfo in characterInfos)
                {
                    // 这里可以添加额外的角色信息填充，比如装备、技能等
                    await EnrichCharacterInfo(characterInfo);
                }

                _logger.LogInformation("成功获取到 {Count} 个角色", characterInfos.Count);
                return characterInfos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色列表时发生异常: UserId={UserId}, GameId={GameId}", 
                    gameQueryDto.GameUserId, gameQueryDto.GameId);
                return new List<CharacterInfo>();
            }
        }

        public async Task<DeathMessage> HandleDeathAsync(DeathMessage request)
        {
            if (_characterState.State.CharacterInfo == null)
            {
                _logger.LogWarning("尝试处理未加载角色的死亡: {CharacterId}", CharacterId);
                return request;
            }

            _logger.LogInformation("角色 {CharacterName} 死亡", _characterState.State.CharacterInfo.CharacterName);

            // 更新角色状态
            _characterState.State.CharacterInfo.IsAlive = false;
            _characterState.State.CharacterInfo.CurrentHealth = 0;
            _characterState.State.CharacterInfo.DeathCount++;
            _characterState.State.CharacterInfo.LastDeathTime = DateTime.Now;

            // 保存状态
            await _characterState.WriteStateAsync();

            _logger.LogInformation("角色 {CharacterName} 已标记为死亡", _characterState.State.CharacterInfo.CharacterName);

            return request;
        }

        public async Task<ResurrectMessage> ResurrectAsync(ResurrectMessage request)
        {
            if (_characterState.State.CharacterInfo == null)
            {
                _logger.LogWarning("尝试复活未加载的角色: {CharacterId}", CharacterId);
                return request;
            }

            _logger.LogInformation("角色 {CharacterName} 复活", _characterState.State.CharacterInfo.CharacterName);

            // 根据复活类型恢复血量
            float restoreRatio = request.ResurrectType == 1 ? 1.0f : 0.5f; // 1=完全复活，其他=半血复活
            _characterState.State.CharacterInfo.CurrentHealth = 
                (float)(_characterState.State.CharacterInfo.MaxHealth * restoreRatio);

            _characterState.State.CharacterInfo.IsAlive = true;
            _characterState.State.CharacterInfo.ResurrectionCount++;

            // 更新位置
            if (request.ResurrectPosition != null)
            {
                _characterState.State.CharacterInfo.Position = request.ResurrectPosition;
            }

            // 保存状态
            await _characterState.WriteStateAsync();

            // 创建响应
            var response = new ResurrectMessage
            {
                ResurrectedId = request.ResurrectedId,
                ResurrectPosition = _characterState.State.CharacterInfo.Position,
                ResurrectType = request.ResurrectType,
                RemainingHealth = _characterState.State.CharacterInfo.CurrentHealth,
                MaxHealth = _characterState.State.CharacterInfo.MaxHealth
            };

            _logger.LogInformation("角色 {CharacterName} 已复活，血量恢复至: {CurrentHealth}", _characterState.State.CharacterInfo.CharacterName, _characterState.State.CharacterInfo.CurrentHealth);

            return response;
        }

        #region 私有辅助方法

        /// <summary>
        /// 获取用户在指定游戏中的角色数量
        /// </summary>
        private async Task<int> GetCharacterCountForUser(long userId, int gameId)
        {
            try
            {
                var count = await _gameCharacterContext.CountAsync(
                    c => c.UserId == userId && c.GameId == gameId && c.IsValid && !c.IsDeleted);
                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户角色数量时发生异常: UserId={UserId}, GameId={GameId}", 
                    userId, gameId);
                return 0;
            }
        }

        /// <summary>
        /// 验证并清理角色名
        /// </summary>
        private async Task<string> ValidateAndCleanCharacterName(string characterName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(characterName))
                    return null;

                // 清理空格
                var cleaned = characterName.Trim();
                
                // 验证长度
                if (cleaned.Length < 2 || cleaned.Length > 12)
                    return null;

                // 过滤敏感词
                cleaned = cleaned.FilterSensitiveWords(sensitiveWords: null);
                
                // 验证是否为空（可能全部是敏感词）
                if (string.IsNullOrWhiteSpace(cleaned))
                    return null;

                return cleaned;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证角色名时发生异常: {CharacterName}", characterName);
                return null;
            }
        }

        /// <summary>
        /// 设置默认出生位置
        /// </summary>
        private CharacterEntity SetDefaultStartingLocation(CharacterEntity character, Profession profession)
        {
            // 根据不同职业设置不同的出生位置
            switch (profession)
            {
                case Profession.Shaolin:
                    character.MapId = 1001; // 少林寺
                    character.PositionX = 100.0f;
                    character.PositionY = 50.0f;
                    character.PositionZ = 200.0f;
                    break;
                case Profession.Wudang:
                    character.MapId = 1002; // 武当山
                    character.PositionX = 150.0f;
                    character.PositionY = 80.0f;
                    character.PositionZ = 250.0f;
                    break;
                case Profession.Emei:
                    character.MapId = 1003; // 峨眉山
                    character.PositionX = 120.0f;
                    character.PositionY = 60.0f;
                    character.PositionZ = 220.0f;
                    break;
                default:
                    // 默认新手村
                    character.MapId = 1000;
                    character.PositionX = 0.0f;
                    character.PositionY = 0.0f;
                    character.PositionZ = 0.0f;
                    break;
            }

            return character;
        }

        /// <summary>
        /// 丰富角色信息（添加额外的运时数据）。
        /// 当前实现：从 Redis 永久存储读取角色离线位置，填充到 <see cref="CharacterInfo.Position"/>，
        /// 供客户端在角色选择界面展示角色最后所在位置。
        /// </summary>
        private async Task EnrichCharacterInfo(CharacterInfo characterInfo)
        {
            try
            {
                // ===== 填充离线位置信息 =====
                // ICharacterPositionStore 存储的是 ECS Z-up 坐标（X=左右, Y=前后, Z=上下）。
                // CharacterInfo.Position 使用 Flax Y-up 坐标系（X=左右, Y=上下, Z=前后），
                // 需做 Y/Z 互换：Flax Y = ECS Z, Flax Z = ECS Y。
                try
                {
                    var savedPosition = await _positionStore.GetPositionAsync((long)characterInfo.CharacterId);
                    if (savedPosition.HasValue)
                    {
                        characterInfo.Position = new Position
                        {
                            X = savedPosition.Value.X,   // 左右（不变）
                            Y = savedPosition.Value.Z,   // ECS Z（上下）→ Flax Y
                            Z = savedPosition.Value.Y,   // ECS Y（前后）→ Flax Z
                            Yaw = savedPosition.Value.Yaw,
                        };
                        _logger.LogDebug(
                            "EnrichCharacterInfo: 角色 {CharacterId} 离线位置 ECS({EX:F2},{EY:F2},{EZ:F2},Yaw={Yaw:F2}) → Flax({FX:F2},{FY:F2},{FZ:F2})",
                            characterInfo.CharacterId,
                            savedPosition.Value.X, savedPosition.Value.Y, savedPosition.Value.Z, savedPosition.Value.Yaw,
                            characterInfo.Position.X, characterInfo.Position.Y, characterInfo.Position.Z);
                    }
                    else
                    {
                        // 无离线位置数据（首次创建的角色），Position 保持默认值 (0,0,0)
                        _logger.LogDebug(
                            "EnrichCharacterInfo: 角色 {CharacterId} 无离线位置数据（首次创建或未进入过游戏）",
                            characterInfo.CharacterId);
                    }
                }
                catch (Exception posEx)
                {
                    // Redis 不可用时降级：位置信息缺失不影响角色列表正常返回
                    _logger.LogWarning(posEx,
                        "EnrichCharacterInfo: 读取角色 {CharacterId} 离线位置失败，Position 将使用默认值",
                        characterInfo.CharacterId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "丰富角色信息时发生异常: CharacterId={CharacterId}",
                    characterInfo.CharacterId);
            }
        }

        /// <summary>
        /// 更新角色最后登录时间
        /// </summary>
        private async Task UpdateCharacterLastLoginTime(ulong characterId)
        {
            try
            {
                var character = await _gameCharacterContext.QueryFirstOrDefaultAsync(
                    c => c.Id == (long)characterId);
                    
                if (character != null)
                {
                    character.LastLoginTime = DateTime.Now;
                    await _gameCharacterContext.UpdateAsync(character, character.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新角色最后登录时间时发生异常: CharacterId={CharacterId}", 
                    characterId);
            }
        }

        #endregion

        #region 属性加点（服务端权威：Redis 缓存 + SQL 落库）

        /// <summary>
        /// 属性加点：校验期望终态后同时更新 grain 状态（Redis 持久缓存）与属性表（SQL 权威落库）。
        /// <para>
        /// 采用"提交终态"而非"增量"：客户端是预览式批量确认，终态提交让请求幂等，
        /// 且服务端只需一次总和校验；全零即重置。校验失败不改任何一层，直接把当前权威值回给客户端回滚预览。
        /// </para>
        /// </summary>
        public async Task<AttributeAllocateResponse> AllocateAttributeAsync(ulong characterId, AttributeAllocationData desired)
        {
            var info = _characterState.State.CharacterInfo;
            if (info == null)
                return AllocationResponse(false, "角色数据未加载。", characterId, new AttributeAllocationData(), 0, 0);

            desired ??= new AttributeAllocationData();

            var total = AllocationPointsPerLevel * Math.Max(1, info.Level);
            var values = new[]
            {
                desired.Strength, desired.Agility, desired.Health, desired.Energy,
                desired.Metal, desired.Wood, desired.Water, desired.Fire, desired.Earth
            };
            var spent = values.Sum();

            if (spent < 0 || values.Any(v => v < 0))
                return AllocationResponse(false, "加点数不能为负。", characterId, CurrentAllocation(), total, total - AllocationSpent(CurrentAllocation()));

            if (spent > total)
                return AllocationResponse(false, $"点数超出上限（已用 {spent} / 点池 {total}）。",
                    characterId, CurrentAllocation(), total, total - AllocationSpent(CurrentAllocation()));

            _characterState.State.Allocation = desired;
            await _characterState.WriteStateAsync();

            // 落库失败不回滚缓存：Redis 仍是当前会话的权威显示源，
            // 但必须显式告警——否则玩家下次冷启动会看到点数"缩水"回旧值。
            var persisted = await PersistAllocationToDatabaseAsync(characterId, desired);

            _logger.LogInformation(
                "属性加点: CharacterId={CharacterId}, 已用 {Spent}/{Total}, 落库={Persisted}",
                characterId, spent, total, persisted);

            return AllocationResponse(true, persisted ? "加点成功。" : "加点已生效，但落库失败（重启后可能回退）。",
                characterId, desired, total, total - spent);
        }

        /// <summary>当前权威分配（grain 状态；为空按未分配处理）。</summary>
        private AttributeAllocationData CurrentAllocation()
            => _characterState.State.Allocation ??= new AttributeAllocationData();

        private static int AllocationSpent(AttributeAllocationData a)
            => a.Strength + a.Agility + a.Health + a.Energy + a.Metal + a.Wood + a.Water + a.Fire + a.Earth;

        private static AttributeAllocateResponse AllocationResponse(
            bool success, string message, ulong characterId,
            AttributeAllocationData allocation, int total, int available)
            => new AttributeAllocateResponse
            {
                Success = success,
                Message = message,
                CharacterId = characterId,
                Allocation = allocation,
                TotalPoints = total,
                AvailablePoints = available,
            };

        /// <summary>
        /// grain 状态里没有加点数据时，从属性表回填。
        /// 触发时机：Redis 被清空 / 换环境 / 角色首次启用加点。属性表也无记录时保持全零。
        /// </summary>
        private async Task EnsureAllocationLoadedAsync()
        {
            if (_characterState.State.Allocation != null) return;

            try
            {
                var row = await _gameAttributeContext.QueryFirstOrDefaultAsync(a => a.CharacterId == CharacterId);
                _characterState.State.Allocation = row == null
                    ? new AttributeAllocationData()
                    : new AttributeAllocationData
                    {
                        Strength = row.Strength,
                        Agility = row.Agility,
                        Health = row.Constitution,
                        Energy = row.InternalForce,
                        Metal = row.MetalPoint,
                        Wood = row.WoodPoint,
                        Water = row.WaterPoint,
                        Fire = row.FirePoint,
                        Earth = row.EarthPoint,
                    };
            }
            catch (Exception ex)
            {
                // 属性表可能尚未应用迁移（列不存在）：降级为"未分配"，不阻断角色激活。
                _logger.LogWarning(ex, "加载属性加点失败，按未分配处理: CharacterId={CharacterId}", CharacterId);
                _characterState.State.Allocation = new AttributeAllocationData();
            }
        }

        /// <summary>把分配写回属性表（有行则更新，无行则插入）。返回是否落库成功。</summary>
        private async Task<bool> PersistAllocationToDatabaseAsync(ulong characterId, AttributeAllocationData a)
        {
            try
            {
                var row = await _gameAttributeContext.QueryFirstOrDefaultAsync(x => x.CharacterId == characterId);
                if (row == null)
                {
                    // 只写加点相关列：该表其余属性列由各自的属性系统接入时维护，此处不越界初始化。
                    row = new CharacterAttributeEntity { CharacterId = characterId };
                    ApplyAllocationToRow(row, a);
                    row.UpdateTime = DateTime.Now;
                    await _gameAttributeContext.AddAsync(row);
                }
                else
                {
                    ApplyAllocationToRow(row, a);
                    row.UpdateTime = DateTime.Now;
                    await _gameAttributeContext.UpdateAsync(row, row.Id);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "属性加点落库失败: CharacterId={CharacterId}", characterId);
                return false;
            }
        }

        private static void ApplyAllocationToRow(CharacterAttributeEntity row, AttributeAllocationData a)
        {
            row.Strength = a.Strength;
            row.Agility = a.Agility;
            row.Constitution = a.Health;
            row.InternalForce = a.Energy;
            row.MetalPoint = a.Metal;
            row.WoodPoint = a.Wood;
            row.WaterPoint = a.Water;
            row.FirePoint = a.Fire;
            row.EarthPoint = a.Earth;
        }

        #endregion

        #region P1.1 统一角色状态模型：空间生命周期钩子

        /// <inheritdoc />
        public async Task OnEnterZoneAsync(long zoneShardId)
        {
            _currentZoneShardId = zoneShardId;
            _logger.LogInformation(
                "CharacterGrain {CharacterId}: 进入空间 ZoneShard={ZoneShardId}，推送权威 RPG 属性到广播缓存。",
                CharacterId, zoneShardId);

            // 将权威 RPG 属性推送到 ZoneShard 广播缓存，确保其他玩家看到正确的 HP/Level 等。
            try
            {
                var zoneShard = GrainFactory.GetGrain<IZoneShardGrain>(zoneShardId);
                var info = _characterState.State?.CharacterInfo;
                if (info != null)
                {
                    await zoneShard.UpdateCharacterAttributesAsync(
                        CharacterId,
                        level: info.Level,
                        exp: info.Experience,
                        hp: (int)info.CurrentHealth,
                        maxHp: (int)info.MaxHealth,
                        stateBits: info.IsAlive ? 0u : 1u,  // bit0 = 死亡标志
                        nickname: info.CharacterName,
                        title: await ResolveCurrentTitleNameAsync()
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "CharacterGrain {CharacterId}: 推送 RPG 属性到 ZoneShard={ZoneShardId} 失败。",
                    CharacterId, zoneShardId);
            }
        }

        /// <summary>
        /// 当前称号名的激活期缓存（null = 未查过；空串 = 查过且未佩戴）。
        /// 称号只在角色主动切换时变化，Grain 激活期内的重复查询没有价值。
        /// </summary>
        private string? _titleNameCache;

        /// <summary>
        /// 解析角色当前佩戴的称号名：CharacterEntity.CurrentTitleId → TitleEntity.TitleName。
        /// 未佩戴、称号行被删或查询异常时返回空串（客户端据此回退默认表现），不抛异常。
        /// </summary>
        private async Task<string> ResolveCurrentTitleNameAsync()
        {
            if (_titleNameCache != null) return _titleNameCache;

            var titleName = string.Empty;
            try
            {
                var character = await _gameCharacterContext.QueryFirstOrDefaultAsync(c => c.Id == (long)CharacterId);
                var titleId = character?.CurrentTitleId ?? 0;
                if (titleId > 0)
                {
                    var title = await _gameTitleContext.QueryFirstOrDefaultAsync(t => t.Id == titleId);
                    if (!string.IsNullOrEmpty(title?.TitleName))
                        titleName = title!.TitleName;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "CharacterGrain {CharacterId}: 解析当前称号名失败，按未佩戴处理。", CharacterId);
            }

            _titleNameCache = titleName;
            return titleName;
        }

        /// <inheritdoc />
        public async Task<CharacterSyncProfile> GetSyncProfileAsync()
        {
            var info = _characterState.State?.CharacterInfo;
            if (info == null)
            {
                _logger.LogWarning(
                    "CharacterGrain {CharacterId}: 档案状态为空，返回空昵称/等级 0（DS 侧将回退占位名）。",
                    CharacterId);
                return new CharacterSyncProfile { Nickname = "", Level = 0, Title = "" };
            }

            return new CharacterSyncProfile
            {
                Nickname = info.CharacterName ?? "",
                Level = info.Level,
                Title = await ResolveCurrentTitleNameAsync(),
            };
        }

        /// <inheritdoc />
        public async Task OnLeaveZoneAsync(long zoneShardId, int finalHp, ZoneLeaveReason reason)
        {
            _logger.LogInformation(
                "CharacterGrain {CharacterId}: 离开空间 ZoneShard={ZoneShardId}（原因={Reason}，最终HP={FinalHp}）。",
                CharacterId, zoneShardId, reason, finalHp);

            // 持久化最终 HP（仅当 finalHp > 0 时更新，避免孤儿清理时报告 0 覆盖正常值）。
            if (_characterState.State?.CharacterInfo != null && finalHp > 0)
            {
                _characterState.State.CharacterInfo.CurrentHealth = finalHp;
                await _characterState.WriteStateAsync();
            }

            // 清除空间标记（语义="已不在任何空间"的哨兵值，非路由到 shard 0；多 Shard 下由
            // 重新进入世界时按 ZoneBasedShardRouter 重新解析，详见 docs/SHARD_OPERATION.md）。
            _currentZoneShardId = 0;
        }

        /// <inheritdoc />
        public async Task<HpChangeResult> RequestHpChangeAsync(int hpDelta, ulong sourceId, Horizon.Orleans.Interface.World.DamageType damageType)
        {
            var info = _characterState.State?.CharacterInfo;
            if (info == null)
            {
                _logger.LogWarning(
                    "CharacterGrain {CharacterId}: RequestHpChange 时 CharacterInfo 为 null，拒绝伤害。",
                    CharacterId);
                return new HpChangeResult(0, 0, 0, false, true);
            }

            // Phase 2 裁决：防御/减伤/Buff 计算（HP 变更链路的唯一裁决点）。
            // 设计约束：CombatGrain 只做命中结算（闪避/暴击/格挡/五行克制/基础伤害），
            // 不再应用防御减免——防御/减伤/Buff 统一在此处按 DamageType 裁决，避免双重减免。
            var actualDelta = hpDelta;
            if (actualDelta < 0 && damageType != Horizon.Orleans.Interface.World.DamageType.True)
            {
                // 防御减免：defense / (defense + 100)，与 CombatCalculator 公式一致。
                float defenseReduction = CombatCalculator.CalculateDefenseReduction(info.Defense);
                float mitigated = actualDelta * (1 - defenseReduction);

                // 减伤 Buff 叠加（0~1）。
                if (info.DamageReduction > 0)
                    mitigated *= (1 - info.DamageReduction);

                actualDelta = (int)Math.Round(mitigated);

                // 完全抵抗/免疫：减免后无实际伤害，HP 不变。
                if (actualDelta == 0)
                {
                    _logger.LogInformation(
                        "CharacterGrain {CharacterId}: 伤害被完全抵抗（来源={SourceId}，类型={DamageType}）。",
                        CharacterId, sourceId, damageType);
                    return new HpChangeResult(0, (int)info.CurrentHealth, (int)info.MaxHealth, false, true);
                }
            }

            var currentHp = (int)info.CurrentHealth;
            var maxHp = (int)info.MaxHealth;

            // 治疗不超过上限
            if (actualDelta > 0)
            {
                currentHp = Math.Min(currentHp + actualDelta, maxHp);
                actualDelta = currentHp - (int)info.CurrentHealth;
            }
            else
            {
                currentHp = Math.Max(currentHp + actualDelta, 0);
                actualDelta = currentHp - (int)info.CurrentHealth;
            }

            var isDead = currentHp <= 0;
            info.CurrentHealth = currentHp;
            info.IsAlive = !isDead;

            if (isDead)
            {
                info.DeathCount++;
                _logger.LogInformation(
                    "CharacterGrain {CharacterId}: 角色死亡（来源={SourceId}，类型={DamageType}）。",
                    CharacterId, sourceId, damageType);
            }

            await _characterState.WriteStateAsync();

            // 将新 HP 推送到 ZoneShard 广播缓存。
            if (_currentZoneShardId != 0)
            {
                try
                {
                    var zoneShard = GrainFactory.GetGrain<IZoneShardGrain>(_currentZoneShardId);
                    await zoneShard.UpdateCharacterAttributesAsync(
                        CharacterId,
                        hp: currentHp,
                        maxHp: maxHp,
                        stateBits: isDead ? 1u : 0u
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "CharacterGrain {CharacterId}: HP 变更后推送到 ZoneShard={ZoneShardId} 失败。",
                        CharacterId, _currentZoneShardId);
                }
            }

            return new HpChangeResult(actualDelta, currentHp, maxHp, isDead, false);
        }

        #endregion

        #region 角色位置缓存

        /// <inheritdoc />
        public async Task UpdateLastPositionAsync(float x, float y, float z, float yaw)
        {
            var state = _characterState.State;
            if (state == null) return;

            // 更新内存缓存（CharacterState.LastPosition*，作为 Redis 不可用时的降级兜底）
            state.LastPositionX = x;
            state.LastPositionY = y;
            state.LastPositionZ = z;
            state.LastYaw = yaw;
            state.LastPositionUpdateUtc = DateTime.UtcNow;

            // 写入 Redis 永久存储（双轨制：不再调用 WriteStateAsync 持久化整个 CharacterState）
            // CharacterState 含 CharacterInfo 大对象，每秒一次 WriteState 性能开销大且与业务数据写入争抢锁
            try
            {
                await _positionStore.SavePositionAsync((long)CharacterId, x, y, z, yaw);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CharacterGrain {CharacterId} 位置写入 Redis 失败，降级为仅内存缓存", CharacterId);
            }
        }

        /// <inheritdoc />
        public async Task<(float X, float Y, float Z, float Yaw)?> GetLastPositionAsync()
        {
            // 优先从 Redis 读取（热缓存；24h 无更新会被归档服务清到 SQL）
            try
            {
                var redisPosition = await _positionStore.GetPositionAsync((long)CharacterId);
                if (redisPosition.HasValue)
                {
                    return redisPosition;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CharacterGrain {CharacterId} 从 Redis 读取位置失败，降级到 SQL Server 归档", CharacterId);
            }

            // Redis 未命中：读 SQL Server 归档位置（CharacterPositionArchiveService 落库的 Flax Y-up 列），
            // 转回本方法约定的 ECS Z-up：ecsX=flaxX, ecsY=flaxZ, ecsZ=flaxY。
            // 全零视为"从未有位置数据"（新建角色默认出生点即 (0,0,0)），交给调用方回退握手坐标。
            try
            {
                var entity = await _gameCharacterContext.QueryFirstOrDefaultAsync(c => c.Id == (long)CharacterId);
                if (entity != null &&
                    (entity.PositionX != 0f || entity.PositionY != 0f || entity.PositionZ != 0f))
                {
                    _logger.LogInformation(
                        "CharacterGrain {CharacterId} Redis 未命中，从 SQL Server 归档恢复位置 Flax({PX:F2},{PY:F2},{PZ:F2})。",
                        CharacterId, entity.PositionX, entity.PositionY, entity.PositionZ);
                    return (entity.PositionX, entity.PositionZ, entity.PositionY, entity.Rotation);
                }
            }
            catch (Exception sqlEx)
            {
                _logger.LogWarning(sqlEx, "CharacterGrain {CharacterId} 读取 SQL Server 归档位置失败，降级到内存缓存", CharacterId);
            }

            // 降级：Redis 与 SQL 均无数据时，回退到内存缓存（CharacterState.LastPosition*）
            var state = _characterState.State;
            if (state == null || state.LastPositionUpdateUtc == default)
            {
                return null;
            }

            return (state.LastPositionX, state.LastPositionY, state.LastPositionZ, state.LastYaw);
        }

        #endregion
    }
}