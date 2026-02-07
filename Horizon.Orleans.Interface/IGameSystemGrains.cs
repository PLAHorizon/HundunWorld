using Orleans;
using System;
using System.Threading.Tasks;
using Horizon.Game.Message;
using System.Collections.Generic;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 物品系统Grain接口 - 负责物品管理、背包操作
    /// </summary>
    public interface IInventoryGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 获取背包信息
        /// </summary>
        /// <returns>背包信息</returns>
        Task<InventoryInfo> GetInventoryAsync();

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
    }

    /// <summary>
    /// 技能系统Grain接口 - 负责技能学习、释放、冷却管理
    /// </summary>
    public interface ISkillGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 释放技能
        /// </summary>
        /// <param name="request">技能释放请求</param>
        /// <returns>技能释放响应</returns>
        Task<SkillCastMessage> CastSkillAsync(SkillCastMessage request);

        /// <summary>
        /// 学习技能
        /// </summary>
        /// <param name="skillId">技能ID</param>
        /// <returns>是否成功学习</returns>
        Task<bool> LearnSkillAsync(int skillId);

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
    }

    /// <summary>
    /// 材料合成系统Grain接口
    /// </summary>
    public interface ICraftingGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 材料合成
        /// </summary>
        /// <param name="request">材料合成请求</param>
        /// <returns>材料合成响应</returns>
     //   Task<MaterialSynthesisResponse> SynthesizeAsync(MaterialSynthesisRequest request);

        /// <summary>
        /// 获取合成配方列表
        /// </summary>
        /// <returns>配方列表</returns>
        Task<List<CraftingRecipe>> GetRecipesAsync();

        /// <summary>
        /// 学习合成配方
        /// </summary>
        /// <param name="recipeId">配方ID</param>
        /// <returns>是否成功学习</returns>
        Task<bool> LearnRecipeAsync(int recipeId);

        /// <summary>
        /// 检查合成材料是否足够
        /// </summary>
        /// <param name="recipeId">配方ID</param>
        /// <returns>是否材料足够</returns>
        Task<bool> CheckMaterialsAsync(int recipeId);
    }

   
}
