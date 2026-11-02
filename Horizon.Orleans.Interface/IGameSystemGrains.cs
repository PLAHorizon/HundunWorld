using Orleans;
using System;
using System.Threading.Tasks;
using Horizon.Game.Message;
using System.Collections.Generic;
using Horizon.Game.Message.Network;
using MemoryPack;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 物品系统Grain接口 - 负责物品管理、背包操作
    /// <para>
    /// Grain Primary Key = 由 <c>GrainKeyMapper.ToInventoryKey(characterId)</c> 换算出的 <see cref="Guid"/>
    /// （ulong 角色 ID → Guid 的换算集中在该映射器，勿各处各写一套）。
    /// </para>
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IInventoryGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 获取背包信息
        /// </summary>
        /// <returns>背包信息</returns>
        Task<InventoryInfo> GetInventoryAsync();

        /// <summary>
        /// 装配 §14 动态习得门槛的判定输入（材料数量 / 分域好感 / 基础属性）。
        /// 只能由背包这一方提供：SkillGrain 反过来问会凑成 Grain 环。
        /// </summary>
        Task<LearnGateContext> BuildLearnGateContextAsync(ulong characterId);

        /// <summary>
        /// 按模板 id 跨堆叠扣减材料。数量不足时一票否决、一格都不扣。
        /// </summary>
        Task<bool> ConsumeByTemplateAsync(ulong characterId, int templateId, int quantity);

        /// <summary>
        /// 物品操作
        /// </summary>
        /// <param name="request">物品操作请求</param>
        /// <returns>物品操作响应</returns>
        Task<ItemChangeInfo> ItemOperationAsync(ItemChangeInfo request);

        /// <summary>
        /// 添加物品到背包
        /// </summary>
        /// <param name="templateId">物品模板ID</param>
        /// <param name="quantity">数量</param>
        /// <param name="quality">品质</param>
        /// <returns>是否成功添加</returns>
        Task<bool> AddItemAsync(int templateId, int quantity, int quality = 0);

        /// <summary>
        /// 移除背包物品
        /// </summary>
        /// <param name="itemId">物品实例ID</param>
        /// <param name="quantity">移除数量</param>
        /// <returns>是否成功移除</returns>
        Task<bool> RemoveItemAsync(long itemId, int quantity);

        /// <summary>
        /// 使用物品
        /// </summary>
        /// <param name="itemId">物品实例ID</param>
        /// <param name="quantity">使用数量</param>
        /// <returns>是否成功使用</returns>
        Task<bool> UseItemAsync(long itemId, int quantity = 1);

        /// <summary>
        /// 使用物品（带真实结算与原因）。秘籍段（模板 id &gt;= 800000）会转投 SkillGrain 研读，
        /// 并按"是否真耗尽"决定扣不扣数量。
        /// <para>
        /// 为什么要一个新方法而不是往 <c>UseItemAsync</c> 里塞：两个 handler 都声明了
        /// <c>MessageType.UseItem</c>，逻辑只有落在 Grain 这个唯一咽喉上，才不会因为谁先注册而表现不同。
        /// </para>
        /// </summary>
        Task<UseItemResponse> UseItemWithResultAsync(ulong characterId, long itemId, int count);

        /// <summary>
        /// 整理背包
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> SortInventoryAsync();

        /// <summary>
        /// 扩展背包容量
        /// </summary>
        /// <param name="slots">扩展槽位数</param>
        /// <returns>是否成功</returns>
        Task<bool> ExpandInventoryAsync(int slots);

        /// <summary>
        /// 装备物品到指定槽位
        /// </summary>
        Task<bool> EquipItemAsync(long itemId, int slot);

        /// <summary>
        /// 按模板 ID（客户端装备定义 ID）装备到指定槽位；服务端负责解析成背包实例 ID。
        /// </summary>
        Task<bool> EquipByTemplateAsync(int templateId, int slot);

        /// <summary>
        /// 从指定槽位卸下装备
        /// </summary>
        Task<bool> UnequipItemAsync(int slot);

        /// <summary>
        /// 获取所有已装备物品（槽位 → 物品ID）。
        /// </summary>
        Task<Dictionary<int, long>> GetEquippedItemsAsync();

        /// <summary>
        /// 获取所有已装备物品的完整信息（含 Count/TemplateId/Name 等）。
        /// 装备外观/属性结算需要完整条目，只有物品ID不够。
        /// </summary>
        Task<List<ItemInfo>> GetEquippedItemInfosAsync();

        /// <summary>
        /// 槽位 → 完整物品信息。客户端按槽位渲染换装，扁平列表会丢"哪件在哪个槽"。
        /// </summary>
        Task<Dictionary<int, ItemInfo>> GetEquippedSlotsAsync();

        /// <summary>
        /// 宝石镶嵌/卸下（服务端权威）：从背包扣一颗 <paramref name="gemTemplateId"/> 宝石镶入
        /// 装备实例指定槽位；槽内已有宝石则换下并放回背包（可再利用）。
        /// <paramref name="gemTemplateId"/> 传 0 表示单纯卸下该槽宝石放回背包。
        /// 返回操作后该装备的完整宝石数组（-1=空槽）。
        /// </summary>
        Task<(bool Success, string Message, List<int> Gems)> SocketGemAsync(long equipmentItemId, int gemSlot, int gemTemplateId);
    }

    /// <summary>
    /// 分域好感度（方案 §14）：角色对五行各域的心意累计。
    /// <para>
    /// 这个 Grain 只负责"记账"与"给快照"，判定不在这里：需要条件的一方（SkillGrain）
    /// 拿到的是调用方装配好的快照，而不是自己来问 —— 反过来问会和"秘籍研读"那条边凑成 Grain 环。
    /// </para>
    /// </summary>
    public interface IFavorabilityGrain : IGrainWithGuidKey
    {
        /// <summary>按五行域（1金 2木 3水 4火 5土）取当前好感，供装配判定输入用。</summary>
        Task<Dictionary<int, int>> GetByDomainAsync();

        /// <summary>
        /// 累积好感。
        /// </summary>
        /// <param name="domain">1..5 五行域；越界不记账</param>
        /// <param name="delta">正数增加、负数扣减（下限 0）</param>
        /// <param name="targetKey">来源标识，形如 <c>npc:10086</c>；同一来源累计到上限后不再增加，
        /// 防"逮着一个 NPC 反复对话刷满一域"</param>
        /// <param name="perTargetCap">覆盖默认的来源上限，0 = 用默认值</param>
        /// <returns>记账后该域的总好感</returns>
        Task<int> AddAsync(int domain, int delta, string? targetKey = null, int perTargetCap = 0);
    }

    /// <summary>
    /// 技能系统Grain接口 - 负责技能学习、释放、冷却管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface ISkillGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 释放技能
        /// </summary>
        /// <param name="request">技能释放请求</param>
        /// <returns>技能释放响应</returns>
        Task<SkillCastMessage> CastSkillAsync(SkillCastMessage request);

        /// <summary>
        /// 直接习得（剧情授予/无秘籍的行）。<b>不</b>由客户端决定通道：这一式只要在秘籍表里登记过书，
        /// 这里就会被拒并告知该读哪一本（§14.5）。动态条件（好感/材料/属性）由调用方在服务端
        /// 装配 <paramref name="dynamicContext"/> 传进来 —— 客户端上报的那份一律不信。
        /// </summary>
        /// <param name="characterId">角色ID（等级与亲和都在 CharacterGrain 那边，门槛要问权威侧）</param>
        /// <param name="skillId">技能ID</param>
        Task<Horizon.Game.Message.Network.LearnSkillResponse> LearnSkillAsync(
            long characterId, int skillId, Horizon.Game.Message.Network.LearnGateContext? dynamicContext = null);

        /// <summary>
        /// 研读秘籍习得技能：在 <see cref="LearnSkillAsync"/> 的门槛之上，额外要求 §6.2 的
        /// "演化源式练满 + 周天圆满次数 + 道阶系圆满对数"与 §14 的动态门槛（好感/材料/属性），
        /// 并按秘籍表 learn_success_rate 掷签（失败即本书耗尽）。
        /// 道阶演化式与普通式走同一个入口——它们只差这几条门槛，不需要第二个方法。
        /// </summary>
        /// <param name="dynamicContext">服务端装配的动态条件输入；传 null 时带动态门槛的式一律不放行</param>
        Task<LearnSkillResponse> LearnFromBookAsync(
            long characterId, int skillId, Horizon.Game.Message.Network.LearnGateContext? dynamicContext = null);

        /// <summary>
        /// 散功：忘掉一式。删掉演化式会把它封存的源技自动放回可用状态。
        /// </summary>
        Task<bool> UnlearnAsync(long characterId, int skillId);

        /// <summary>
        /// 破势：夺走本角色一层节数。护体行按 §5.3 判定——本角色指定的镇岳行优先，
        /// 未指定时才用调用方从防御向量算出的最高行。克制不成立或无层可夺都返回 false。
        /// </summary>
        /// <param name="attackElement">攻方这一式所履五行</param>
        /// <param name="dominantDefenseRow">守方防御向量最高行（镇岳未设时的兜底）</param>
        Task<bool> BreakCycleAsync(int attackElement, int dominantDefenseRow);

        /// <summary>夺元：本角色的循环节 +1（不要求顺生，这是被夺来的战果）。</summary>
        Task<bool> GrantCycleAsync();

        /// <summary>
        /// 镇岳（§5.3）：指定护体五行行（1..5；0 = 取消指定，回到自动取防御最高行）。
        /// 每 10s 才能换一次，切换不耗气——这是 PVP 里"换攻防假设"的那一步棋，成本要低但不为零。
        /// </summary>
        Task<Horizon.Game.Message.Network.GuardElementResult> SetGuardElementAsync(int element);

        /// <summary>
        /// 升级技能
        /// </summary>
        /// <param name="skillId">技能ID</param>
        /// <returns>是否成功升级</returns>
        Task<bool> UpgradeSkillAsync(int skillId);

        /// <summary>
        /// 获取角色所有技能
        /// </summary>
        /// <returns>技能列表</returns>
        Task<List<SkillInfo>> GetSkillsAsync();

        /// <summary>
        /// 技能模板目录 + 本角色已学技能，一次下行给客户端。
        /// 模板表读不到时返回 <c>Success=false</c>（客户端据此保留本地兜底表），不抛异常。
        /// </summary>
        Task<Horizon.Game.Message.Network.SkillCatalogSyncMessage> GetSkillCatalogAsync();

        /// <summary>
        /// 检查技能冷却
        /// </summary>
        /// <param name="skillId">技能ID</param>
        /// <returns>剩余冷却时间(秒)</returns>
        Task<float> GetSkillCooldownAsync(int skillId);

        /// <summary>
        /// 重置技能冷却
        /// </summary>
        /// <param name="skillId">技能ID</param>
        /// <returns>是否成功</returns>
        Task<bool> ResetSkillCooldownAsync(int skillId);

        /// <summary>
        /// 重置所有已学习技能并返还技能点
        /// </summary>
        Task<bool> ResetAllSkillsAsync();

        /// <summary>
        /// 设置技能前置依赖
        /// </summary>
        Task<bool> SetSkillDependencyAsync(int skillId, List<int> prerequisites);

        /// <summary>
        /// 获取可用技能点
        /// </summary>
        Task<int> GetSkillPointsAsync();

        /// <summary>
        /// 添加技能点
        /// </summary>
        Task<bool> AddSkillPointsAsync(int points);

        /// <summary>
        /// 打断施法
        /// </summary>
        Task InterruptCastAsync();
    }

    /// <summary>
    /// 材料合成系统Grain接口
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface ICraftingGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 执行合成（<paramref name="held"/> 是调用方从背包取的持有量快照）。
        /// <para>
        /// <b>为什么持有量要由调用方传</b>：Grain 没有背包访问权。以前这个方法不看背包就算成功率、
        /// 还伪造一个实例 id，等于"无中生有"。现在它只做：已学？材料够？掷签？记历史；
        /// <b>扣料与发货由调用方执行</b>（与商店成交同一条分工）。
        /// </para>
        /// </summary>
        /// <param name="recipeId">配方ID</param>
        /// <param name="held">物品模板 id → 持有件数。</param>
        Task<CraftingResult> CraftItemAsync(int recipeId, Dictionary<long, int> held);

        /// <summary>
        /// 获取合成配方列表
        /// </summary>
        /// <returns>配方列表</returns>
        Task<List<CraftingRecipe>> GetRecipesAsync();

        /// <summary>
        /// 学习合成配方（定义由调用方从已装配内容取，Grain 不自己编一份）。
        /// </summary>
        /// <param name="definition">配方定义（材料/工费/产物/等级门槛）。</param>
        /// <returns>是否成功学习</returns>
        Task<bool> LearnRecipeAsync(CraftingRecipe definition);

        /// <summary>
        /// 检查合成材料是否足够（逐件缺料回执，不再是一个看不出理由的 bool）。
        /// </summary>
        /// <param name="recipeId">配方ID</param>
        /// <param name="held">物品模板 id → 持有件数。</param>
        Task<CraftMaterialCheck> CheckMaterialsAsync(int recipeId, Dictionary<long, int> held);

        /// <summary>
        /// 获取合成历史记录
        /// </summary>
        Task<List<CraftingHistoryEntry>> GetCraftingHistoryAsync();
    }

    /// <summary>
    /// 材料校验回执（逐件缺料，客户端据此把"缺哪样、缺几件"直接写在制作面板上）。
    /// </summary>
    [GenerateSerializer]
    public sealed class CraftMaterialCheck
    {
        /// <summary>材料是否齐。</summary>
        [Id(0)] public bool Ok { get; set; }

        /// <summary><c>Horizon.Game.Sim.Npcs.NpcCraftReject</c> 的数值（契约层不引用 Sim 枚举）。</summary>
        [Id(1)] public int Reject { get; set; }

        /// <summary>缺几种。</summary>
        [Id(2)] public int MissingKinds { get; set; }

        /// <summary>总共缺几件。</summary>
        [Id(3)] public int MissingUnits { get; set; }

        /// <summary>逐件明细（ASCII 行，便于日志与面板直接显示）。</summary>
        [Id(4)] public string[] Lines { get; set; } = System.Array.Empty<string>();
    }

    /// <summary>
    /// 五行炼丹系统Grain接口
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IWuxingAlchemyGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 学习炼丹配方
        /// </summary>
        /// <param name="recipeId">配方ID</param>
        /// <returns>是否成功学习</returns>
        Task<bool> LearnAlchemyRecipeAsync(int recipeId);

        /// <summary>
        /// 执行炼丹
        /// </summary>
        /// <param name="recipeId">配方ID</param>
        /// <param name="primaryElement">主要五行元素</param>
        /// <param name="secondaryElement">辅助五行元素</param>
        /// <returns>炼丹结果</returns>
        Task<AlchemyResult> PerformAlchemyAsync(int recipeId, int primaryElement, int secondaryElement);

        /// <summary>
        /// 获取炼丹配方列表
        /// </summary>
        /// <returns>配方列表</returns>
        Task<List<AlchemyRecipe>> GetAlchemyRecipesAsync();

        /// <summary>
        /// 获取炼丹历史记录
        /// </summary>
        /// <returns>历史记录列表</returns>
        Task<List<AlchemyHistoryEntry>> GetAlchemyHistoryAsync();

        /// <summary>
        /// 获取炼丹熟练度
        /// </summary>
        /// <returns>熟练度</returns>
        Task<float> GetAlchemyProficiencyAsync();
    }

}
