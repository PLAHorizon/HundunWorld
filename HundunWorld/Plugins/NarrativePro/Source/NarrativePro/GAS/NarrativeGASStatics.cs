using System.Collections.Generic;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// GAS 静态工具函数库。对应 UE5 UNarrativeGASStatics。
    /// 提供 GAS 中 Blueprint 不便访问的工具函数。
    /// </summary>
    public static class NarrativeGASStatics
    {
        /// <summary>从效果规格中获取动态授予标签。</summary>
        public static GameplayTagContainer GetDynamicGrantedTagsFromEffectSpec(GameplayEffectSpec spec)
        {
            if (spec?.Effect?.GrantedTags == null) return new GameplayTagContainer();
            var result = new GameplayTagContainer();
            foreach (var tag in spec.Effect.GrantedTags.GetTags())
            {
                result.AddTag(new GameplayTag(tag));
            }
            // 合并 Spec 上的动态标签
            if (spec.DynamicGrantedTags != null)
            {
                foreach (var tag in spec.DynamicGrantedTags.GetTags())
                {
                    result.AddTag(new GameplayTag(tag));
                }
            }
            return result;
        }

        /// <summary>从效果规格中获取动态资产标签。</summary>
        public static GameplayTagContainer GetDynamicAssetTagsFromEffectSpec(GameplayEffectSpec spec)
        {
            if (spec == null) return new GameplayTagContainer();
            var result = new GameplayTagContainer();
            if (spec.DynamicAssetTags != null)
            {
                foreach (var tag in spec.DynamicAssetTags.GetTags())
                {
                    result.AddTag(new GameplayTag(tag));
                }
            }
            return result;
        }

        /// <summary>从效果规格中获取所有资产标签（Effect 定义 + Spec 动态）。</summary>
        public static GameplayTagContainer GetAllAssetTagsFromEffectSpec(GameplayEffectSpec spec)
        {
            var result = new GameplayTagContainer();
            if (spec?.Effect?.AssetTags != null)
            {
                foreach (var tag in spec.Effect.AssetTags.GetTags())
                {
                    result.AddTag(new GameplayTag(tag));
                }
            }
            if (spec?.DynamicAssetTags != null)
            {
                foreach (var tag in spec.DynamicAssetTags.GetTags())
                {
                    result.AddTag(new GameplayTag(tag));
                }
            }
            return result;
        }

        /// <summary>检查激活效果句柄是否有效。</summary>
        public static bool IsEffectHandleValid(ActiveGameplayEffectHandle handle)
        {
            return handle.IsValid;
        }
    }
}
