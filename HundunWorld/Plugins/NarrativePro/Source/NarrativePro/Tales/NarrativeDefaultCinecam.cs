using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 默认电影摄像机脚本。对应 UE5 ANarrativeDefaultCinecam。
    /// Flax 中 Camera 类是 sealed 不能继承，因此以 Script 形式附加到 Camera Actor 上。
    /// 使用完全限定名 FlaxEngine.Camera 以避免与 NarrativePro.Camera 命名空间冲突。
    /// </summary>
    public class NarrativeDefaultCinecam : Script
    {
        /// <summary>关联的摄像机（自动获取挂载此脚本的 Camera）。</summary>
        public FlaxEngine.Camera Cinecam { get; private set; }

        /// <summary>是否在启用时将此相机设为主相机（调用 Camera.Use）。</summary>
        public bool bSetAsMainCameraOnEnable = false;

        public override void OnEnable()
        {
            base.OnEnable();
            Cinecam = Actor as FlaxEngine.Camera;
            if (Cinecam == null)
            {
                Cinecam = Actor.GetScript<FlaxEngine.Camera>();
            }
            if (Cinecam == null)
            {
                NarrativeLog.LogWarning("NarrativeDefaultCinecam: 挂载的 Actor 上未找到 Camera 组件");
                return;
            }

            if (bSetAsMainCameraOnEnable)
            {
                // Flax 的对等物是 Camera.OverrideMainCamera（Source/Engine/Level/Actors/Camera.cpp:26-33：
                // GetMainCamera 先取它，再取 CutSceneCamera，最后才是相机表第一个）。
                _previousOverride = FlaxEngine.Camera.OverrideMainCamera;
                FlaxEngine.Camera.OverrideMainCamera = Cinecam;
            }
            NarrativeLog.Log("NarrativeDefaultCinecam enabled");
        }

        public override void OnDisable()
        {
            // 只在「自己确实是当前覆盖相机」时还原，避免把别人后来设的相机一起清掉
            if (bSetAsMainCameraOnEnable && Cinecam != null
                && ReferenceEquals(FlaxEngine.Camera.OverrideMainCamera, Cinecam))
            {
                FlaxEngine.Camera.OverrideMainCamera = _previousOverride;
            }
            _previousOverride = null;
            NarrativeLog.Log("NarrativeDefaultCinecam disabled");
            base.OnDisable();
        }

        private FlaxEngine.Camera _previousOverride;
    }
}
