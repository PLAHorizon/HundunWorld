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

        /// <summary>设置地图瓦片边界，并缓存其中的所有 POI。</summary>
        public void SetMapTileBounds(MapTileBoundsActor bounds)
        {
            lock (_lock)
            {
                MapTileBounds = bounds;

                if (bounds?.POIs == null) return;

                _poiLookupMap.Clear();
                foreach (var poi in bounds.POIs)
                {
                    if (poi != null && poi.POITag.IsValid())
                    {
                        _poiLookupMap[poi.POITag] = poi;
                    }
                }
            }
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
