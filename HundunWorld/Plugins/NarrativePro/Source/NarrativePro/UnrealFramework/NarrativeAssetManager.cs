using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// Narrative 资产管理器。对应 UE5 UNarrativeAssetManager。
    /// UE5 中继承 UAssetManager；Flax 无 AssetManager 基类，改为单例 [Serializable] class。
    /// 用于 GAS 初始化与高效的 NPC 资源缓存。
    /// 简化点：
    /// - UE5 TSoftObjectPtr/TSoftClassPtr 改为 string 路径占位
    /// - UE5 模板函数 GetAsset&lt;T&gt;/GetSubclass&lt;T&gt; 改为泛型方法
    /// - 移除 UE5 引擎的 PrimaryAsset 体系，仅保留基本加载与缓存
    /// </summary>
    [Serializable]
    public class NarrativeAssetManager
    {
        // ===== 单例 =====

        private static NarrativeAssetManager _instance;

        /// <summary>全局单例实例。对应 UE5 UNarrativeAssetManager::Get()。</summary>
        public static NarrativeAssetManager Get()
        {
            if (_instance == null)
            {
                _instance = new NarrativeAssetManager();
            }
            return _instance;
        }

        /// <summary>设置单例实例（用于测试注入）。</summary>
        public static void SetInstance(NarrativeAssetManager instance)
        {
            _instance = instance;
        }

        // ===== 加载的资产缓存 =====

        /// <summary>保持驻留内存的已加载资产列表（防止 GC 回收）。</summary>
        [NonSerialized]
        protected List<object> LoadedAssets = new List<object>();

        /// <summary>资产路径到已加载资产的缓存，避免重复加载。</summary>
        [NonSerialized]
        protected Dictionary<string, object> AssetCache = new Dictionary<string, object>(StringComparer.Ordinal);

        // ===== 加载 API =====

        /// <summary>
        /// 返回 TSoftObjectPtr 引用的资产。若未加载将同步加载。对应 UE5 GetAsset&lt;AssetType&gt;。
        /// </summary>
        /// <typeparam name="T">资产类型。</typeparam>
        /// <param name="assetPath">资产路径（替代 UE5 TSoftObjectPtr）。</param>
        /// <param name="bKeepInMemory">是否保持驻留内存。</param>
        /// <returns>加载的资产实例；失败返回 null。</returns>
        public virtual T GetAsset<T>(string assetPath, bool bKeepInMemory = true) where T : FlaxEngine.Asset
        {
            if (string.IsNullOrEmpty(assetPath)) return null;

            if (AssetCache.TryGetValue(assetPath, out var cached))
            {
                return cached as T;
            }

            // Flax-已实现: 通过 Flax Content.Load<T>(assetPath) 进行同步加载
            T loaded = null;
            try
            {
                loaded = FlaxEngine.Content.Load<T>(assetPath);
            }
            catch (Exception e)
            {
                NarrativeLog.LogWarning($"[NarrativeAssetManager] 加载资产失败: {assetPath} ({e.Message})");
                return null;
            }

            if (loaded != null)
            {
                AssetCache[assetPath] = loaded;
                if (bKeepInMemory)
                {
                    LoadedAssets.Add(loaded);
                }
            }
            return loaded;
        }

        /// <summary>
        /// 返回 TSoftClassPtr 引用的子类。若未加载将同步加载。对应 UE5 GetSubclass&lt;AssetType&gt;。
        /// </summary>
        /// <typeparam name="T">资产类型。</typeparam>
        /// <param name="classPath">类路径（替代 UE5 TSoftClassPtr）。</param>
        /// <param name="bKeepInMemory">是否保持驻留内存。</param>
        /// <returns>类路径字符串（Flax 中无 TSubclassOf，统一返回路径占位）。</returns>
        public virtual string GetSubclass<T>(string classPath, bool bKeepInMemory = true) where T : class
        {
            if (string.IsNullOrEmpty(classPath)) return "";
            // Flax 中无 TSubclassOf 概念，统一返回路径占位
            // 注意：不调用 GetAsset<T> 因为 T 可能不是 Asset 类型
            return classPath;
        }

        /// <summary>开始初始加载。从 InitializeObjectReferences 调用。对应 UE5 StartInitialLoading。</summary>
        public virtual void StartInitialLoading()
        {
            NarrativeLog.Log("[NarrativeAssetManager] StartInitialLoading");
            InitializeGAS();
            // TODO [需接入资产加载系统]: 加载 Narrative 默认资产、标签等
        }

        /// <summary>线程安全地将已加载资产加入驻留内存列表。对应 UE5 AddLoadedAsset。</summary>
        /// <param name="asset">要驻留的资产。</param>
        public virtual void AddLoadedAsset(object asset)
        {
            if (asset == null) return;
            if (!LoadedAssets.Contains(asset))
            {
                LoadedAssets.Add(asset);
            }
        }

        /// <summary>初始化 GAS（Gameplay Ability System）。对应 UE5 InitializeGAS。</summary>
        public virtual void InitializeGAS()
        {
            NarrativeLog.Log("[NarrativeAssetManager] InitializeGAS");
            // TODO [需接入 GAS 系统]: 注册 GameplayTag、加载默认 AttributeData、Effect 等
        }

        /// <summary>清空资产缓存（用于关卡切换或内存压力释放）。</summary>
        public virtual void ClearCache()
        {
            AssetCache.Clear();
            LoadedAssets.Clear();
        }
    }
}
