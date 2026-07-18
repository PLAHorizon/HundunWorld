// 占位类型说明：
// Weapons/WeaponVisual.cs 引用了 StaticMesh 类型（UE5 UStaticMesh 的移植占位），
// 但该类型尚未实现，且 Flax 中对应类型为 FlaxEngine.Model。
// 此文件为最小占位，仅用于满足编译；实际应后续统一替换为 FlaxEngine.Model 或实现完整适配。
// 本占位与 SaveSystem 模块移植无关，仅为打通整体编译而创建。

namespace NarrativePro.Weapons
{
    /// <summary>
    /// 静态网格占位类型。对应 UE5 UStaticMesh。
    /// 实际项目应替换为 FlaxEngine.Model 或实现完整适配层。
    /// </summary>
    public class StaticMesh
    {
        /// <summary>资源路径占位。</summary>
        public string AssetPath = string.Empty;
    }
}
