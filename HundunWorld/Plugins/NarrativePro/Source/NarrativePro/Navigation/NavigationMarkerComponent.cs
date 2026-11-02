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
            if (MarkerObject == null)
            {
                // 对齐 UE5：NavigationMarkerComponent::InitializeComponent 会自行 NewObject<UMapMarker>()。
                // 本组件已在 World.scene 中按类型名放置（Content/Maps/World.scene:265），
                // 场景脚本只带开关不带 MarkerObject 时，端口此前静默空转 —— 这里补上默认标记对象，
                // 使「挂组件即出标记」成立。
                MarkerObject = new MapMarker();
            }

            if (Actor != null)
            {
                MarkerObject.ActorOwner = Actor;
                MarkerObject.MarkerTransform = Actor.Transform;
            }
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
