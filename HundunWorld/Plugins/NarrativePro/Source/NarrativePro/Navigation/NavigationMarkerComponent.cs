using FlaxEngine;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航标记组件。挂到任何需要在导航 UI 上显示的 Actor 上。
    /// 适配 UE5 UNavigationMarkerComponent。
    /// </summary>
    public class NavigationMarkerComponent : Script
    {
        /// <summary>此组件对应的地图标记对象</summary>
        public MapMarker MarkerObject { get; set; }

        public override void OnEnable()
        {
            base.OnEnable();
            RegisterMarker();
        }

        public override void OnDisable()
        {
            RemoveMarker();
            base.OnDisable();
        }

        /// <summary>注册到所有导航组件。</summary>
        public virtual void RegisterMarker()
        {
            if (MarkerObject == null) return;
            MarkerObject.ActorOwner = Actor;
            MarkerObject.RegisterMarker();
        }

        /// <summary>从所有导航组件移除。</summary>
        public virtual void RemoveMarker()
        {
            if (MarkerObject == null) return;
            MarkerObject.RemoveMarker();
        }
    }
}
