using System.Collections.Generic;
using FlaxEngine;
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

        /// <summary>
        /// 取武器/战斗追踪使用的层掩码。对应 UE5 ArsenalSettings->WeaponTraceChannel。
        /// UE5 用碰撞通道（ECollisionChannel），Flax 用 Actor.Layer 位掩码：
        /// WeaponTraceChannel 视为 1 基的层号（0 表示未配置，回退为全部层）。
        /// </summary>
        public static uint GetWeaponTraceMask()
        {
            var settings = NarrativePro.Core.ArsenalSettings.Instance;
            int channel = settings != null ? settings.WeaponTraceChannel : 0;
            if (channel <= 0) return uint.MaxValue;
            int bit = channel - 1;
            if (bit > 31) bit = 31;
            return 1u << bit;
        }

        /// <summary>
        /// 正态分布随机锥（Lyra/Narrative 的 FMath::VRandCone 变体）。
        /// 对应 UE5 NarrativeCombatAbility.cpp 的 VRandConeNormalDistribution。
        /// </summary>
        /// <param name="dir">锥轴方向。</param>
        /// <param name="coneHalfAngleRad">半锥角（弧度）。</param>
        /// <param name="exponent">聚拢指数，越大越靠近轴线。</param>
        /// <param name="random">随机数发生器（用于按角色种子复现）。</param>
        public static Vector3 VRandConeNormalDistribution(Vector3 dir, float coneHalfAngleRad, float exponent, System.Random random)
        {
            if (coneHalfAngleRad <= 0f)
            {
                return dir.LengthSquared > 0f ? dir.Normalized : Vector3.Zero;
            }

            if (random == null) random = _fallbackRandom;

            float coneHalfAngleDegrees = coneHalfAngleRad * Mathf.RadiansToDegrees;

            // 把锥看成两次旋转的串联：一次"偏离中心线"，一次"绕圈"
            // 对偏离中心的旋转施加指数，指数越大点越聚集在轴线附近
            float fromCenter = (float)System.Math.Pow(random.NextDouble(), exponent);
            float angleFromCenter = fromCenter * coneHalfAngleDegrees;
            float angleAround = (float)random.NextDouble() * 360f;

            Quaternion dirQuat = Quaternion.LookRotation(dir.LengthSquared > 0f ? dir.Normalized : Vector3.Forward, Vector3.Up);
            Quaternion fromCenterQuat = Quaternion.Euler(0f, angleFromCenter, 0f);
            Quaternion aroundQuat = Quaternion.Euler(0f, 0f, angleAround);

            Quaternion finalQuat = Quaternion.Multiply(Quaternion.Multiply(dirQuat, aroundQuat), fromCenterQuat);
            finalQuat.Normalize();

            // UE5 用 ForwardVector 作为被旋转向量（Flax 约定：vector * quaternion 旋转该向量）
            return Vector3.Forward * finalQuat;
        }

        private static readonly System.Random _fallbackRandom = new System.Random(1234);
    }
}
