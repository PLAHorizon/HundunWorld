using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航子系统。缓存场景中的 POI 和所有导航组件实例。
    /// 适配 UE5 UNavigationSubsystem（WorldSubsystem），Flax 无 WorldSubsystem 等价物，改用场景 Script + 静态单例。
    /// </summary>
    public class NavigationSubsystem : Script
    {
        private static NavigationSubsystem _instance;

        private readonly Dictionary<GameplayTag, POIData> _poiLookupMap = new Dictionary<GameplayTag, POIData>();
        private readonly List<NarrativeNavigationComponent> _navComponents = new List<NarrativeNavigationComponent>();
        private readonly object _lock = new object();

        /// <summary>
        /// 当前场景实例。若静态引用尚未建立（脚本启用顺序不确定），回退到场景查找。
        /// </summary>
        public static NavigationSubsystem Instance
        {
            get
            {
                var instance = _instance;
                if (instance != null) return instance;

                // 场景查找只能在主线程执行；非主线程调用时静默返回 null，由调用方降级
                try
                {
                    _instance = Level.FindScript<NavigationSubsystem>();
                }
                catch
                {
                    _instance = null;
                }

                return _instance;
            }
        }

        /// <summary>POI 查找表</summary>
        public Dictionary<GameplayTag, POIData> POILookupMap
        {
            get
            {
                lock (_lock) return _poiLookupMap;
            }
        }

        /// <summary>地图瓦片边界</summary>
        public MapTileBoundsActor MapTileBounds { get; private set; }

        /// <summary>设置地图瓦片边界，并把其中的所有 POI 并入注册表。</summary>
        /// <remarks>
        /// 缺陷修复：这里原本先 <c>_poiLookupMap.Clear()</c> 再从 <c>bounds.POIs</c> 重建，
        /// 而 <c>bounds.POIs</c> 在全工程无任何写入方（UE5 是在世界构建期填充，见
        /// MapTileBounds.h:140-142 与 NavigationSubsystem.cpp:19-27），于是后启用的 MapTileBoundsActor
        /// 会把已经注册好的运行时 POI 全部清空（AI/AITask_LongMove 与小地图桥接因此查不到 POI）。
        /// 现在：① 合并而不是清空；② 按 UE5 世界构建语义，设置边界时枚举场景 POIActor 补齐 bounds.POIs；
        /// ③ 边界卸载（OnDisable 传 null）只清引用、不清注册表 —— POI 由各自的 OnDisable 注销。
        /// </remarks>
        public void SetMapTileBounds(MapTileBoundsActor bounds)
        {
            if (bounds != null)
            {
                // 与 UE5 世界构建等价：把场景里存在的 POI 补进边界的 POI 列表（幂等，按标签去重）
                PopulateBoundsPOIsFromScene(bounds);
            }

            lock (_lock)
            {
                MapTileBounds = bounds;

                // 1) 先并入边界自带的（可能是资产里序列化下来的）POI
                if (bounds != null && bounds.POIs != null)
                {
                    foreach (var poi in bounds.POIs)
                    {
                        if (poi != null && poi.POITag.IsValid())
                        {
                            _poiLookupMap[poi.POITag] = poi;
                        }
                    }
                }
            }

            // 2) 再并入场景实时 POI：运行期对象的位置/快速旅行点才是当前真相，覆盖同名条目。
            //    边界为空（尚未有 MapTileBoundsActor 或它正在卸载）时同样要并入 —— 注册表不能因为边界缺失而变空。
            MergeScenePOIsIntoLookup();
        }

        /// <summary>
        /// 把场景中已启用的 POI 并入注册表（幂等、不清空既有项）。
        /// 供 <see cref="NarrativePro.Navigation.MapTileBoundsActor"/> 与本子系统在启用时补票使用。
        /// </summary>
        public void RegisterScenePOIs()
        {
            MergeScenePOIsIntoLookup();
        }

        private void MergeScenePOIsIntoLookup()
        {
            foreach (var poi in CollectScenePOIs())
            {
                if (poi == null || !poi.POITag.IsValid()) continue;

                lock (_lock)
                {
                    _poiLookupMap[poi.POITag] = poi;
                }
            }
        }

        /// <summary>把场景里已启用但边界尚未收录的 POI 补进 <paramref name="bounds"/> 的 POI 列表。</summary>
        private static void PopulateBoundsPOIsFromScene(MapTileBoundsActor bounds)
        {
            if (bounds == null) return;
            if (bounds.POIs == null) bounds.POIs = new List<POIData>();

            foreach (var poi in CollectScenePOIs())
            {
                bool present = false;
                for (int i = 0; i < bounds.POIs.Count; i++)
                {
                    var existing = bounds.POIs[i];
                    if (existing != null && existing.POITag == poi.POITag)
                    {
                        present = true;
                        break;
                    }
                }

                if (!present) bounds.POIs.Add(poi);
            }
        }

        /// <summary>
        /// 枚举场景中已启用的 <see cref="POIActor"/> 并取其 POI 数据。
        /// 这是“POI 先启用、导航子系统后启用”这条顺序的兜底：导航子系统自己启用时把已存在的 POI 补票进来。
        /// </summary>
        private static List<POIData> CollectScenePOIs()
        {
            var result = new List<POIData>();

            try
            {
                var actors = Level.GetActors<Actor>(true);
                if (actors == null) return result;

                for (int i = 0; i < actors.Length; i++)
                {
                    var actor = actors[i];
                    if (actor == null) continue;

                    var poiActor = actor.GetScript<POIActor>();
                    if (poiActor == null) continue;

                    var data = poiActor.POIDataCache;
                    if (data == null || !data.POITag.IsValid()) continue;

                    result.Add(data);
                }
            }
            catch (System.Exception ex)
            {
                Core.NarrativeLog.LogWarning("[Navigation] 场景 POI 枚举失败：" + ex.Message);
            }

            return result;
        }

        /// <summary>注册一个 POI（由 <see cref="POIActor"/> 调用）。</summary>
        public void RegisterPOI(POIData poi)
        {
            if (poi == null || !poi.POITag.IsValid()) return;

            lock (_lock)
            {
                _poiLookupMap[poi.POITag] = poi;
            }
        }

        /// <summary>取消注册一个 POI。</summary>
        public void UnregisterPOI(GameplayTag poiTag)
        {
            if (!poiTag.IsValid()) return;

            lock (_lock)
            {
                _poiLookupMap.Remove(poiTag);
            }
        }

        /// <summary>获取所有已缓存 POI 的快照。</summary>
        public List<POIData> GetAllPOIs()
        {
            lock (_lock)
            {
                return new List<POIData>(_poiLookupMap.Values);
            }
        }

        /// <summary>获取已缓存的 POI。</summary>
        public bool GetPointOfInterest(out POIData outPointOfInterest, GameplayTag poiTag)
        {
            lock (_lock)
            {
                return _poiLookupMap.TryGetValue(poiTag, out outPointOfInterest);
            }
        }

        /// <summary>获取所有导航组件实例。</summary>
        public List<NarrativeNavigationComponent> GetAllNavigationComponents()
        {
            lock (_lock)
            {
                return new List<NarrativeNavigationComponent>(_navComponents);
            }
        }

        /// <summary>注册一个导航组件。</summary>
        public void RegisterNavigationComponent(NarrativeNavigationComponent navComp)
        {
            if (navComp == null) return;

            lock (_lock)
            {
                if (!_navComponents.Contains(navComp))
                {
                    _navComponents.Add(navComp);
                }
            }
        }

        /// <summary>取消注册一个导航组件。</summary>
        public void UnregisterNavigationComponent(NarrativeNavigationComponent navComp)
        {
            if (navComp == null) return;

            lock (_lock)
            {
                _navComponents.Remove(navComp);
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            _instance = this;

            // 反向补票：MapTileBoundsActor / POIActor 可能早于本子系统启用（那一刻 Instance 仍为 null，
            // 它们的注册调用被静默丢弃）。两种启用顺序都要收敛到完整注册表，所以这里主动收回场景里已有的边界与 POI。
            MapTileBoundsActor bounds = null;
            try
            {
                bounds = Level.FindScript<MapTileBoundsActor>();
            }
            catch
            {
                bounds = null;
            }

            SetMapTileBounds(bounds);
        }

        /// <summary>
        /// 首帧之后再并一次：场景加载时 POIActor 可能在本子系统 OnEnable 之后才启用，
        /// 它们的自注册此时已经能命中 Instance，这里只是把漏网的补上（幂等）。
        /// </summary>
        public override void OnStart()
        {
            base.OnStart();
            RegisterScenePOIs();
        }

        public override void OnDisable()
        {
            lock (_lock)
            {
                _navComponents.Clear();
                _poiLookupMap.Clear();
                MapTileBounds = null;
            }

            if (_instance == this) _instance = null;
            base.OnDisable();
        }
    }
}
