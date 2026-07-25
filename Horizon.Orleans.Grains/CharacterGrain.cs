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
        private readonly ICharacterFingerprintService _fingerprintService;

        private readonly IDataContext<GameEntityContext, UserEntity, long> _gameUserContext;
        private readonly IDataContext<GameEntityContext, CharacterEntity, long> _gameCharacterContext;
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
            ICharacterFingerprintService fingerprintService,

            IDataContext<GameEntityContext, UserEntity, long> gameUserContext,
            IDataContext<GameEntityContext, CharacterEntity, long> gameCharacterContext,
            IMapper mapper)
        {
            _logger = logger;
            _characterState = characterState;
            _presenceStore = presenceStore;
            _fingerprintService = fingerprintService;

            _mapper = mapper;
            _gameUserContext = gameUserContext;
            _gameCharacterContext = gameCharacterContext;
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

        public Task<MoveResponse> MoveAsync(MoveRequest request)
        {
            if (!_isOnline)
            {
                return Task.FromResult(new MoveResponse { Success = false });
            }

            // Update position in state

            _characterState.State.CharacterInfo.Position = new  Position
            {
                X = request.TargetX,
                Y = request.TargetY,
                Z = request.TargetZ
            };

            // In a real game, you would not write state for every move.
            // This would be broadcast to nearby players and persisted periodically or on zone change.
            // await _characterState.WriteStateAsync();

            return Task.FromResult(new MoveResponse
            {
                Success = true,
                CharacterId = _characterState.State.CharacterInfo.CharacterId,
                CurrentX = request.TargetX,
                CurrentY = request.TargetY,
                CurrentZ = request.TargetZ
            });
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

        public Task<bool> UpdateAttributesAsync(Dictionary<string, object> attributes)
        {
            _logger.LogInformation("正在更新角色 {CharacterId} 的属性。", CharacterId);
            // Placeholder: Implement actual attribute update logic
            return Task.FromResult(true);
        }

        public Task<List<EquipmentInfoMessage>> GetEquipmentsAsync()
        {
            _logger.LogInformation("正在获取角色 {CharacterId} 的装备。", CharacterId);
            // Placeholder: Implement logic to retrieve equipment from DB or state
            return Task.FromResult(new List<EquipmentInfoMessage>());
        }

        public Task<bool> EquipItemAsync(long itemId, int slot)
        {
            _logger.LogInformation("为角色 {CharacterId} 在槽位 {Slot} 装备物品 {ItemId}。", itemId, slot, CharacterId);
            // Placeholder: Implement item equipping logic
            return Task.FromResult(true);
        }

        public Task<bool> UnequipItemAsync(int slot)
        {
            _logger.LogInformation("为角色 {CharacterId} 从槽位 {Slot} 卸下装备。", slot, CharacterId);
            // Placeholder: Implement item unequipping logic
            return Task.FromResult(true);
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

        // 新增的方法实现

        public Task<DamageMessage> AttackAsync(AttackMessage request)
        {
            _logger.LogInformation("角色 {AttackerId} 正在攻击目标 {TargetId}。", request.AttackerId, request.TargetId);
            // Placeholder: Implement actual attack logic
            var response = new DamageMessage
            {
                VictimId = request.TargetId,
                AttackerId = request.AttackerId,
                Damage = request.Damage,
                IsCritical = request.IsCritical,
                RemainingHealth = 100, // Placeholder value
                IsDodged = false,
                IsBlocked = false
            };
            return Task.FromResult(response);
        }

        public Task<SkillCastMessage> CastSkillAsync(SkillCastMessage request)
        {
            _logger.LogInformation("角色 {CasterId} 正在释放技能 {SkillId}。", request.CasterId, request.SkillId);
            // Placeholder: Implement actual skill casting logic
            return Task.FromResult(request);
        }

        public Task<QingGongMessage> UseQingGongAsync(QingGongMessage request)
        {
            _logger.LogInformation("角色 {CharacterId} 正在使用轻功技能 {SkillId}。", request.CharacterId, request.QingGongSkillId);
            // Placeholder: Implement actual qinggong logic
            return Task.FromResult(request);
        }

        public Task<NeiGongMessage> UseNeiGongAsync(NeiGongMessage request)
        {
            _logger.LogInformation("角色 {CharacterId} 正在使用内功技能 {SkillId}。", request.CharacterId, request.NeiGongSkillId);
            // Placeholder: Implement actual neigong logic
            return Task.FromResult(request);
        }

        public Task<ComboAttackMessage> ComboAttackAsync(ComboAttackMessage request)
        {
            _logger.LogInformation("角色 {AttackerId} 正在发动连击。", request.AttackerId);
            // Placeholder: Implement actual combo attack logic
            return Task.FromResult(request);
        }

        public Task<DefenseMessage> DefendAsync(DefenseMessage request)
        {
            _logger.LogInformation("角色 {DefenderId} 正在防御 {AttackerId} 的攻击。", request.DefenderId, request.AttackerId);
            // Placeholder: Implement actual defense logic
            return Task.FromResult(request);
        }

        public Task<JoinSectResponse> JoinSectAsync(JoinSectRequest request)
        {
            _logger.LogInformation("角色 {CharacterId} 正在加入门派 {SectId}。", request.CharacterId, request.SectId);
            // Placeholder: Implement actual sect joining logic
            var response = new JoinSectResponse
            {
                Success = true,
                Message = "成功加入门派",
                SectId = request.SectId,
                Position = "弟子"
            };
            return Task.FromResult(response);
        }

        public Task<ReputationUpdateMessage> UpdateReputationAsync(ReputationUpdateMessage request)
        {
            _logger.LogInformation("正在更新角色 {CharacterId} 的声望。", request.CharacterId);
            // Placeholder: Implement actual reputation update logic
            return Task.FromResult(request);
        }

        public Task<ChivalryPointUpdateMessage> UpdateChivalryPointAsync(ChivalryPointUpdateMessage request)
        {
            _logger.LogInformation("正在更新角色 {CharacterId} 的侠义值。", request.CharacterId);
            // Placeholder: Implement actual chivalry point update logic
            return Task.FromResult(request);
        }

        public Task<DuelResponse> HandleDuelAsync(DuelRequest request)
        {
            _logger.LogInformation("角色 {ChallengerId} 正在向 {OpponentId} 发起决斗。", request.ChallengerId, request.OpponentId);
            // Placeholder: Implement actual duel handling logic
            var response = new DuelResponse
            {
                Accepted = true,
                Message = "比武切磋请求已接受",
                DuelId = (ulong)new Random().Next(100000, 999999)
            };
            return Task.FromResult(response);
        }

        public Task<SwornBrotherResponse> HandleSwornBrotherAsync(SwornBrotherRequest request)
        {
            _logger.LogInformation("角色 {InitiatorId} 正在发起结拜请求。", request.InitiatorId);
            // Placeholder: Implement actual sworn brother handling logic
            var response = new SwornBrotherResponse
            {
                Agreed = true,
                Message = "结拜请求已接受",
                BrotherhoodId = (ulong)new Random().Next(100000, 999999)
            };
            return Task.FromResult(response);
        }

        public Task<MasterApprenticeResponse> HandleMasterApprenticeAsync(MasterApprenticeRequest request)
        {
            _logger.LogInformation("师徒关系请求：师傅 {MasterId} 与徒弟 {ApprenticeId}。", request.MasterId, request.ApprenticeId);
            // Placeholder: Implement actual master-apprentice handling logic
            var response = new MasterApprenticeResponse
            {
                Agreed = true,
                Message = "师徒关系请求已接受",
                RelationshipId = (ulong)new Random().Next(100000, 999999),
                RelationshipLevel = 1
            };
            return Task.FromResult(response);
        }

        public Task<InventoryUpdateMessage> UpdateInventoryAsync(InventoryUpdateMessage request)
        {
            _logger.LogInformation("正在更新角色 {CharacterId} 的背包。", request.CharacterId);
            // Placeholder: Implement actual inventory update logic
            return Task.FromResult(request);
        }

        public Task<WeaponSwitchMessage> SwitchWeaponAsync(WeaponSwitchMessage request)
        {
            _logger.LogInformation("角色 {CharacterId} 正在将武器从槽位 {CurrentSlot} 切换到 {TargetSlot}。", request.CharacterId, request.CurrentWeaponSlot, request.TargetWeaponSlot);
            // Placeholder: Implement actual weapon switching logic
            return Task.FromResult(request);
        }

        public Task<UseItemResponse> UseItemAsync(UseItemRequest request)
        {
            _logger.LogInformation("角色 {CharacterId} 正在使用物品 {ItemId}。", request.CharacterId, request.ItemId);
            // Placeholder: Implement actual item usage logic
            var response = new UseItemResponse
            {
                Success = true,
                Message = "物品使用成功",
                Effects = new List<ItemEffect>(),
                RemainingCount = 1
            };
            return Task.FromResult(response);
        }

        public Task<EquipmentEnhanceResponse> EnhanceEquipmentAsync(EquipmentEnhanceRequest request)
        {
            _logger.LogInformation("正在为角色 {CharacterId} 强化装备 {EquipmentId}。", request.EquipmentId, request.CharacterId);
            // Placeholder: Implement actual equipment enhancement logic
            var response = new EquipmentEnhanceResponse
            {
                Success = true,
                Message = "装备强化成功",
                NewEnhanceLevel = 1,
                ConsumedMaterials = new List<long>(),
                ConsumedGold = 100
            };
            return Task.FromResult(response);
        }

        public Task<EquipmentRefineResponse> RefineEquipmentAsync(EquipmentRefineRequest request)
        {
            _logger.LogInformation("正在为角色 {CharacterId} 精炼装备 {EquipmentId}。", request.EquipmentId, request.CharacterId);
            // Placeholder: Implement actual equipment refinement logic
            var response = new EquipmentRefineResponse
            {
                Success = true,
                Message = "装备精炼成功",
                NewRefineLevel = 1,
                ConsumedMaterials = new List<long>(),
                ConsumedRefineStone = 1,
                ConsumedGold = 100
            };
            return Task.FromResult(response);
        }

        public Task<CraftingResponse> CraftItemAsync(CraftingRequest request)
        {
            _logger.LogInformation("正在为角色 {CharacterId} 使用配方 {RecipeId} 合成物品。", request.RecipeId, request.CharacterId);
            // Placeholder: Implement actual crafting logic
            var response = new CraftingResponse
            {
                Success = true,
                Message = "合成成功",
                CraftedItems = new List<ItemInfo>(),
                ConsumedMaterials = new List<long>(),
                ConsumedGold = 100
            };
            return Task.FromResult(response);
        }

        public Task<AttributeInheritanceResponse> InheritAttributesAsync(AttributeInheritanceRequest request)
        {
            _logger.LogInformation("正在将属性从装备 {SourceEquipmentId} 继承到 {TargetEquipmentId}。", request.SourceEquipmentId, request.TargetEquipmentId);
            // Placeholder: Implement actual attribute inheritance logic
            var response = new AttributeInheritanceResponse
            {
                Success = true,
                Message = "属性继承成功",
                InheritedAttributes = new Dictionary<string, object>(),
                ConsumedGold = 100,
                ConsumedMaterials = new List<long>()
            };
            return Task.FromResult(response);
        }

        public Task<WuXingCraftingResponse> WuXingCraftAsync(WuXingCraftingRequest request)
        {
            _logger.LogInformation("正在为角色 {CharacterId} 执行五行铸造。", request.CharacterId);
            // Placeholder: Implement actual WuXing crafting logic
            var response = new WuXingCraftingResponse
            {
                Success = true,
                Message = "五行合成成功",
                CraftedItem = new ItemInfo(),
                ConsumedMaterials = new Dictionary<string, List<long>>(),
                ConsumedGold = 100
            };
            return Task.FromResult(response);
        }

        public Task<LearnSkillResponse> LearnSkillAsync(LearnSkillRequest request)
        {
            _logger.LogInformation("角色 {CharacterId} 正在学习技能 {SkillId}。", request.CharacterId, request.SkillId);
            // Placeholder: Implement actual skill learning logic
            var response = new LearnSkillResponse
            {
                Success = true,
                Message = "技能学习成功",
                LearnedSkill = new SkillInfo { SkillId = request.SkillId, Level = 1 },
                ConsumedGold = 100,
                ConsumedItems = new List<long>()
            };
            return Task.FromResult(response);
        }

        public Task<SkillCooldownQueryResponse> QuerySkillCooldownAsync(SkillCooldownQueryRequest request)
        {
            _logger.LogInformation("正在查询角色 {CharacterId} 的技能冷却时间。", request.CharacterId);
            // Placeholder: Implement actual skill cooldown query logic
            var response = new SkillCooldownQueryResponse
            {
                CharacterId = request.CharacterId,
                SkillCooldowns = new Dictionary<int, long>()
            };
            return Task.FromResult(response);
        }

        public Task<SkillProficiencyQueryResponse> QuerySkillProficiencyAsync(SkillProficiencyQueryRequest request)
        {
            _logger.LogInformation("正在查询角色 {CharacterId} 的技能熟练度。", request.CharacterId);
            // Placeholder: Implement actual skill proficiency query logic
            var response = new SkillProficiencyQueryResponse
            {
                CharacterId = request.CharacterId,
                SkillProficiencies = new Dictionary<int, int>()
            };
            return Task.FromResult(response);
        }

        public Task<UpgradeSkillResponse> UpgradeSkillAsync(UpgradeSkillRequest request)
        {
            _logger.LogInformation("正在为角色 {CharacterId} 升级技能 {SkillId}。", request.SkillId, request.CharacterId);
            // Placeholder: Implement actual skill upgrade logic
            var response = new UpgradeSkillResponse
            {
                Success = true,
                Message = "技能升级成功",
                UpgradedSkill = new SkillInfo { SkillId = request.SkillId, Level = 2 },
                ConsumedGold = 100,
                ConsumedItems = new List<long>(),
                ConsumedExperience = 1000
            };
            return Task.FromResult(response);
        }

        public Task<ChatMessage> SendChatAsync(ChatMessage request)
        {
            // 安全考虑：不在日志中记录聊天内容，避免敏感信息泄露
            _logger.LogInformation("角色 {SenderId} 正在发送聊天消息。", request.SenderId);
            // Placeholder: Implement actual chat message handling logic
            return Task.FromResult(request);
        }

        public Task<AddFriendResponse> AddFriendAsync(AddFriendRequest request)
        {
            _logger.LogInformation("角色 {RequesterId} 正在请求添加 {TargetId} 为好友。", request.RequesterId, request.TargetId);
            // Placeholder: Implement actual friend adding logic
            var response = new AddFriendResponse
            {
                Success = true,
                Message = "好友请求已发送",
                FriendInfo = new FriendInfo { FriendId = request.TargetId }
            };
            return Task.FromResult(response);
        }

        public Task<CreateTeamResponse> CreateTeamAsync(CreateTeamRequest request)
        {
            _logger.LogInformation("角色 {LeaderId} 正在创建队伍 {TeamName}。", request.LeaderId, request.TeamName);
            // Placeholder: Implement actual team creation logic
            var response = new CreateTeamResponse
            {
                Success = true,
                Message = "队伍创建成功",
                TeamInfo = new TeamInfo { TeamId = (ulong)new Random().Next(100000, 999999), LeaderId = request.LeaderId }
            };
            return Task.FromResult(response);
        }

        public Task<JoinTeamResponse> JoinTeamAsync(JoinTeamRequest request)
        {
            _logger.LogInformation("角色 {RequesterId} 正在申请加入队伍 {TeamId}。", request.RequesterId, request.TeamId);
            // Placeholder: Implement actual team joining logic
            var response = new JoinTeamResponse
            {
                Success = true,
                Message = "加入队伍请求已接受",
                TeamInfo = new TeamInfo { TeamId = request.TeamId }
            };
            return Task.FromResult(response);
        }

        public Task<CreateGuildResponse> CreateGuildAsync(CreateGuildRequest request)
        {
            _logger.LogInformation("角色 {CreatorId} 正在创建公会 {GuildName}。", request.CreatorId, request.GuildName);
            // Placeholder: Implement actual guild creation logic
            var response = new CreateGuildResponse
            {
                Success = true,
                Message = "帮派创建成功",
                GuildInfo = new GuildInfo { GuildId = new Random().Next(100000, 999999), LeaderId = request.CreatorId }
            };
            return Task.FromResult(response);
        }

        public Task<JoinGuildResponse> JoinGuildAsync(JoinGuildRequest request)
        {
            _logger.LogInformation("角色 {RequesterId} 正在申请加入公会 {GuildId}。", request.RequesterId, request.GuildId);
            // Placeholder: Implement actual guild joining logic
            var response = new JoinGuildResponse
            {
                Success = true,
                Message = "加入帮派请求已接受",
                GuildInfo = new GuildInfo { GuildId = request.GuildId }
            };
            return Task.FromResult(response);
        }

        public Task<QuestUpdateMessage> UpdateQuestAsync(QuestUpdateMessage request)
        {
            _logger.LogInformation("正在更新角色 {CharacterId} 的任务 {QuestId}。", request.QuestId, request.CharacterId);
            // Placeholder: Implement actual quest update logic
            return Task.FromResult(request);
        }

        public Task<AcceptQuestResponse> AcceptQuestAsync(AcceptQuestRequest request)
        {
            _logger.LogInformation("角色 {CharacterId} 正在接受任务 {QuestId}。", request.CharacterId, request.QuestId);
            // Placeholder: Implement actual quest acceptance logic
            var response = new AcceptQuestResponse
            {
                Success = true,
                Message = "任务接受成功",
                AcceptedQuest = new QuestInfo { QuestId = request.QuestId }
            };
            return Task.FromResult(response);
        }

        public Task<CompleteQuestResponse> CompleteQuestAsync(CompleteQuestRequest request)
        {
            _logger.LogInformation("角色 {CharacterId} 正在完成任务 {QuestId}。", request.CharacterId, request.QuestId);
            // Placeholder: Implement actual quest completion logic
            var response = new CompleteQuestResponse
            {
                Success = true,
                Message = "任务完成",
                Rewards = new Dictionary<string, int>(),
                CompletedQuestId = request.QuestId
            };
            return Task.FromResult(response);
        }

        public async Task<DamageMessage> TakeDamageAsync(DamageMessage request)
        {
            if (_characterState.State.CharacterInfo == null)
            {
                _logger.LogWarning("尝试对未加载的角色造成伤害: {CharacterId}", CharacterId);
                return request;
            }

            _logger.LogInformation("角色 {CharacterName} 受到伤害: {Damage}", _characterState.State.CharacterInfo.CharacterName, request.Damage);

            // 更新角色血量
            _characterState.State.CharacterInfo.CurrentHealth = Math.Max(0, 
                _characterState.State.CharacterInfo.CurrentHealth - request.Damage);

            // 检查是否死亡
            if (_characterState.State.CharacterInfo.CurrentHealth <= 0)
            {
                _characterState.State.CharacterInfo.IsAlive = false;
                
                // 触发死亡事件
                await HandleDeathAsync(new DeathMessage
                {
                    DeceasedId = _characterState.State.CharacterInfo.CharacterId,
                    KillerId = request.AttackerId,
                    Cause = "战斗死亡",
                    DeathPosition = new Position { X = request.ImpactPosition.X, Y = request.ImpactPosition.Y, Z = request.ImpactPosition.Z }
                });
            }

            // 更新最后受伤时间
            _characterState.State.CharacterInfo.LastDamageTime = DateTime.Now;

            // 保存状态
            await _characterState.WriteStateAsync();

            // 返回更新后的伤害消息
            var response = new DamageMessage
            {
                AttackerId = request.AttackerId,
                VictimId = request.VictimId,
                Damage = request.Damage,
                RemainingHealth = (int)_characterState.State.CharacterInfo.CurrentHealth,
                IsCritical = request.IsCritical,
                IsDodged = request.IsDodged,
                IsBlocked = request.IsBlocked,
                ImpactPosition = request.ImpactPosition,
                ElementType = request.ElementType
            };

            _logger.LogInformation("角色 {CharacterName} 剩余血量: {CurrentHealth}", _characterState.State.CharacterInfo.CharacterName, _characterState.State.CharacterInfo.CurrentHealth);

            return response;
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
        /// 丰富角色信息（添加额外的运时数据）
        /// </summary>
        private async Task EnrichCharacterInfo(CharacterInfo characterInfo)
        {
            try
            {
                // 这里可以添加额外的角色信息填充，比如：
                // - 当前装备信息
                // - 在线状态
                // - 等级排名
                // - 等等
                
                // 暂时不做具体实现，留作后续扩展
                await Task.CompletedTask;
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
                        stateBits: info.IsAlive ? 0u : 1u  // bit0 = 死亡标志
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

            // 清除空间标记。
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

            // TODO（Phase 2）：在此处插入防御/减伤/Buff 计算逻辑。
            // 当前为最简实现：直接应用原始伤害。
            var actualDelta = hpDelta;
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

        /// <summary>位置数据过期阈值（分钟）。超过此时间的位置数据视为无效，回退到握手坐标。</summary>
        private const double PositionStaleMinutes = 5.0;

        /// <inheritdoc />
        public async Task UpdateLastPositionAsync(float x, float y, float z, float yaw)
        {
            var state = _characterState.State;
            if (state == null) return;

            state.LastPositionX = x;
            state.LastPositionY = y;
            state.LastPositionZ = z;
            state.LastYaw = yaw;
            state.LastPositionUpdateUtc = DateTime.UtcNow;

            await _characterState.WriteStateAsync();
        }

        /// <inheritdoc />
        public Task<(float X, float Y, float Z, float Yaw)?> GetLastPositionAsync()
        {
            var state = _characterState.State;
            if (state == null || state.LastPositionUpdateUtc == default)
            {
                return Task.FromResult<(float, float, float, float)?>(null);
            }

            // 过期检查：超过 5 分钟的位置数据视为无效
            if ((DateTime.UtcNow - state.LastPositionUpdateUtc).TotalMinutes > PositionStaleMinutes)
            {
                return Task.FromResult<(float, float, float, float)?>(null);
            }

            return Task.FromResult<(float, float, float, float)?>(
                (state.LastPositionX, state.LastPositionY, state.LastPositionZ, state.LastYaw));
        }

        #endregion
    }
}