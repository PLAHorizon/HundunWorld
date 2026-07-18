using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航子系统。缓存场景中的 POI 和所有导航组件实例。
    /// 适配 UE5 UNavigationSubsystem，Flax 无 WorldSubsystem 等价物，使用 Singleton 模式。
    /// </summary>
    public class NavigationSubsystem : Script
    {
        private static NavigationSubsystem _instance;

        private readonly Dictionary<GameplayTag, POIData> _poiLookupMap = new Dictionary<GameplayTag, POIData>();
        private readonly List<NarrativeNavigationComponent> _navComponents = new List<NarrativeNavigationComponent>();
        private readonly object _lock = new object();

        /// <summary>当前场景实例</summary>
        public static NavigationSubsystem Instance => _instance;

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

        /// <summary>设置地图瓦片边界。</summary>
        public void SetMapTileBounds(MapTileBoundsActor bounds)
        {
            lock (_lock)
            {
                MapTileBounds = bounds;
                // 如果有新的瓦片边界，缓存其中所有 POI
                if (bounds != null && bounds.POIs != null)
                {
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
        }

        /// <summary>注册一个 POI。</summary>
        public void RegisterPOI(POIData poi)
        {
            if (poi == null || !poi.POITag.IsValid()) return;
            lock (_lock)
            {
                _poiLookupMap[poi.POITag] = poi;
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
