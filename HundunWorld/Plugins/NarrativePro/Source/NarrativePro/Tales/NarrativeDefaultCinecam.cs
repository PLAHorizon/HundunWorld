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
            }
            else if (bSetAsMainCameraOnEnable)
            {
                // Flax-不兼容: UE5 的 Camera.MainCamera 写入在 Flax 无对应物，保留占位。原文 TODO: Flax 中 Camera.MainCamera 是只读属性，切换主相机需通过其他方式（如禁用其他 Camera）
                NarrativeLog.Log("NarrativeDefaultCinecam: bSetAsMainCameraOnEnable 已设置但 Flax 暂不支持直接切换主相机");
            }
            NarrativeLog.Log("NarrativeDefaultCinecam enabled");
        }

        public override void OnDisable()
        {
            NarrativeLog.Log("NarrativeDefaultCinecam disabled");
            base.OnDisable();
        }
    }
}
