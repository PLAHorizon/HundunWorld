using Orleans;
using System;
using System.Threading.Tasks;
using Horizon.Game.Message;
using System.Collections.Generic;
using Horizon.Share.Dtos.Games;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 角色Grain接口 - 负责角色数据管理和游戏逻辑
    /// </summary>
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
        /// 角色移动
        /// </summary>
        /// <param name="request">移动请求</param>
        /// <returns>移动响应</returns>
        Task<MoveResponse> MoveAsync(MoveRequest request);

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
        /// 更新角色属性
        /// </summary>
        /// <param name="attributes">属性字典</param>
        /// <returns>是否成功</returns>
        Task<bool> UpdateAttributesAsync(Dictionary<string, object> attributes);

        /// <summary>
        /// 获取角色所有装备
        /// </summary>
        /// <returns>装备列表</returns>
        Task<List<EquipmentInfoMessage>> GetEquipmentsAsync();

        /// <summary>
        /// 装备物品
        /// </summary>
        /// <param name="itemId">物品ID</param>
        /// <param name="slot">装备槽位</param>
        /// <returns>是否成功</returns>
        Task<bool> EquipItemAsync(long itemId, int slot);

        /// <summary>
        /// 卸下装备
        /// </summary>
        /// <param name="slot">装备槽位</param>
        /// <returns>是否成功</returns>
        Task<bool> UnequipItemAsync(int slot);

        // 新增的方法以支持新消息类型

        /// <summary>
        /// 攻击
        /// </summary>
        /// <param name="request">攻击请求</param>
        /// <returns>攻击响应</returns>
        Task<DamageMessage> AttackAsync(AttackMessage request);

        /// <summary>
        /// 施放技能
        /// </summary>
        /// <param name="request">技能施放请求</param>
        /// <returns>技能施放响应</returns>
        Task<SkillCastMessage> CastSkillAsync(SkillCastMessage request);

        /// <summary>
        /// 使用轻功
        /// </summary>
        /// <param name="request">轻功请求</param>
        /// <returns>轻功响应</returns>
        Task<QingGongMessage> UseQingGongAsync(QingGongMessage request);

        /// <summary>
        /// 使用内功
        /// </summary>
        /// <param name="request">内功请求</param>
        /// <returns>内功响应</returns>
        Task<NeiGongMessage> UseNeiGongAsync(NeiGongMessage request);

        /// <summary>
        /// 连击攻击
        /// </summary>
        /// <param name="request">连击请求</param>
        /// <returns>连击响应</returns>
        Task<ComboAttackMessage> ComboAttackAsync(ComboAttackMessage request);

        /// <summary>
        /// 防御（格挡/闪避）
        /// </summary>
        /// <param name="request">防御请求</param>
        /// <returns>防御响应</returns>
        Task<DefenseMessage> DefendAsync(DefenseMessage request);

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

        /// <summary>
        /// 加入门派
        /// </summary>
        /// <param name="request">加入门派请求</param>
        /// <returns>加入门派响应</returns>
        Task<JoinSectResponse> JoinSectAsync(JoinSectRequest request);

        /// <summary>
        /// 更新声望
        /// </summary>
        /// <param name="request">声望更新消息</param>
        /// <returns>处理结果</returns>
        Task<ReputationUpdateMessage> UpdateReputationAsync(ReputationUpdateMessage request);

        /// <summary>
        /// 更新侠义值
        /// </summary>
        /// <param name="request">侠义值更新消息</param>
        /// <returns>处理结果</returns>
        Task<ChivalryPointUpdateMessage> UpdateChivalryPointAsync(ChivalryPointUpdateMessage request);

        /// <summary>
        /// 处理比武切磋请求
        /// </summary>
        /// <param name="request">比武请求</param>
        /// <returns>比武响应</returns>
        Task<DuelResponse> HandleDuelAsync(DuelRequest request);

        /// <summary>
        /// 处理结拜请求
        /// </summary>
        /// <param name="request">结拜请求</param>
        /// <returns>结拜响应</returns>
        Task<SwornBrotherResponse> HandleSwornBrotherAsync(SwornBrotherRequest request);

        /// <summary>
        /// 处理师徒关系请求
        /// </summary>
        /// <param name="request">师徒关系请求</param>
        /// <returns>师徒关系响应</returns>
        Task<MasterApprenticeResponse> HandleMasterApprenticeAsync(MasterApprenticeRequest request);

        /// <summary>
        /// 更新背包
        /// </summary>
        /// <param name="request">背包更新消息</param>
        /// <returns>处理结果</returns>
        Task<InventoryUpdateMessage> UpdateInventoryAsync(InventoryUpdateMessage request);

        /// <summary>
        /// 切换武器
        /// </summary>
        /// <param name="request">武器切换请求</param>
        /// <returns>武器切换响应</returns>
        Task<WeaponSwitchMessage> SwitchWeaponAsync(WeaponSwitchMessage request);

        /// <summary>
        /// 使用物品
        /// </summary>
        /// <param name="request">使用物品请求</param>
        /// <returns>使用物品响应</returns>
        Task<UseItemResponse> UseItemAsync(UseItemRequest request);

        /// <summary>
        /// 强化装备
        /// </summary>
        /// <param name="request">装备强化请求</param>
        /// <returns>装备强化响应</returns>
        Task<EquipmentEnhanceResponse> EnhanceEquipmentAsync(EquipmentEnhanceRequest request);

        /// <summary>
        /// 精炼装备
        /// </summary>
        /// <param name="request">装备精炼请求</param>
        /// <returns>装备精炼响应</returns>
        Task<EquipmentRefineResponse> RefineEquipmentAsync(EquipmentRefineRequest request);

        /// <summary>
        /// 合成物品
        /// </summary>
        /// <param name="request">合成请求</param>
        /// <returns>合成响应</returns>
        Task<CraftingResponse> CraftItemAsync(CraftingRequest request);

        /// <summary>
        /// 属性继承
        /// </summary>
        /// <param name="request">属性继承请求</param>
        /// <returns>属性继承响应</returns>
        Task<AttributeInheritanceResponse> InheritAttributesAsync(AttributeInheritanceRequest request);

        /// <summary>
        /// 五行合成
        /// </summary>
        /// <param name="request">五行合成请求</param>
        /// <returns>五行合成响应</returns>
        Task<WuXingCraftingResponse> WuXingCraftAsync(WuXingCraftingRequest request);

        /// <summary>
        /// 学习技能
        /// </summary>
        /// <param name="request">学习技能请求</param>
        /// <returns>学习技能响应</returns>
        Task<LearnSkillResponse> LearnSkillAsync(LearnSkillRequest request);

        /// <summary>
        /// 查询技能冷却
        /// </summary>
        /// <param name="request">技能冷却查询请求</param>
        /// <returns>技能冷却查询响应</returns>
        Task<SkillCooldownQueryResponse> QuerySkillCooldownAsync(SkillCooldownQueryRequest request);

        /// <summary>
        /// 查询技能熟练度
        /// </summary>
        /// <param name="request">技能熟练度查询请求</param>
        /// <returns>技能熟练度查询响应</returns>
        Task<SkillProficiencyQueryResponse> QuerySkillProficiencyAsync(SkillProficiencyQueryRequest request);

        /// <summary>
        /// 升级技能
        /// </summary>
        /// <param name="request">升级技能请求</param>
        /// <returns>升级技能响应</returns>
        Task<UpgradeSkillResponse> UpgradeSkillAsync(UpgradeSkillRequest request);

        /// <summary>
        /// 发送聊天消息
        /// </summary>
        /// <param name="request">聊天消息</param>
        /// <returns>处理结果</returns>
        Task<ChatMessage> SendChatAsync(ChatMessage request);

        /// <summary>
        /// 添加好友
        /// </summary>
        /// <param name="request">添加好友请求</param>
        /// <returns>添加好友响应</returns>
        Task<AddFriendResponse> AddFriendAsync(AddFriendRequest request);

        /// <summary>
        /// 创建队伍
        /// </summary>
        /// <param name="request">创建队伍请求</param>
        /// <returns>创建队伍响应</returns>
        Task<CreateTeamResponse> CreateTeamAsync(CreateTeamRequest request);

        /// <summary>
        /// 加入队伍
        /// </summary>
        /// <param name="request">加入队伍请求</param>
        /// <returns>加入队伍响应</returns>
        Task<JoinTeamResponse> JoinTeamAsync(JoinTeamRequest request);

        /// <summary>
        /// 创建帮派
        /// </summary>
        /// <param name="request">创建帮派请求</param>
        /// <returns>创建帮派响应</returns>
        Task<CreateGuildResponse> CreateGuildAsync(CreateGuildRequest request);

        /// <summary>
        /// 加入帮派
        /// </summary>
        /// <param name="request">加入帮派请求</param>
        /// <returns>加入帮派响应</returns>
        Task<JoinGuildResponse> JoinGuildAsync(JoinGuildRequest request);

        /// <summary>
        /// 更新任务
        /// </summary>
        /// <param name="request">任务更新消息</param>
        /// <returns>处理结果</returns>
        Task<QuestUpdateMessage> UpdateQuestAsync(QuestUpdateMessage request);

        /// <summary>
        /// 接受任务
        /// </summary>
        /// <param name="request">接受任务请求</param>
        /// <returns>接受任务响应</returns>
        Task<AcceptQuestResponse> AcceptQuestAsync(AcceptQuestRequest request);

        /// <summary>
        /// 完成任务
        /// </summary>
        /// <param name="request">完成任务请求</param>
        /// <returns>完成任务响应</returns>
        Task<CompleteQuestResponse> CompleteQuestAsync(CompleteQuestRequest request);
    }

    
}