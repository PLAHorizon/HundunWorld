#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using FlaxEditor;
using FlaxEngine;
using NarrativePro.Navigation;

namespace NarrativePro.Editor.Navigation
{
    /// <summary>
    /// NarrativePro 导航工具编辑器插件。
    /// <para>
    /// 提供菜单命令（Tools 菜单）：
    /// <list type="bullet">
    /// <item>重新生成地图瓦片网格 —— 由 MapWidth / NumTiles 生成瓦片集与边界盒；</item>
    /// <item>烘焙地图瓦片贴图 —— 正交相机逐块离屏渲染世界、落盘 PNG、导入为 Texture 资产并回填瓦片集；</item>
    /// <item>创建/校正导航体积 —— 按地图边界摆一个 <c>NavMeshBoundsVolume</c>，可先在视口里调范围；</item>
    /// <item>烘焙导航网格（NavMesh）—— 触发引擎的 Recast 烘焙并保存，之后 FindPath/TestPath 才会返回真实路径。</item>
    /// </list>
    /// 烘焙任务按帧推进（挂接 <c>Editor.EditorUpdate</c>），不会阻塞编辑器。
    /// </para>
    /// </summary>
    public class NarrativeProNavigationEditorPlugin : EditorPlugin
    {
        private readonly List<MapTileBakeJob> _activeJobs = new List<MapTileBakeJob>();
        private readonly List<NavMeshBakeJob> _navJobs = new List<NavMeshBakeJob>();
        private bool _menuAdded;

        public NarrativeProNavigationEditorPlugin()
        {
            _description = new PluginDescription
            {
                Name = "NarrativePro Navigation Tools",
                Category = "Tools",
                Author = "成阳",
                Description = "导航工具：地图瓦片网格生成与离屏烘焙",
                Version = new Version(1, 0),
                IsAlpha = false,
                IsBeta = false,
            };
        }

        public override void InitializeEditor()
        {
            base.InitializeEditor();

            TryAddMenuButtons();
            if (!_menuAdded && Editor != null)
            {
                Editor.EditorUpdate += OnEditorUpdate;
            }
        }

        public override void DeinitializeEditor()
        {
            try
            {
                if (Editor != null)
                {
                    Editor.EditorUpdate -= OnEditorUpdate;
                }

                foreach (var job in _activeJobs)
                {
                    job.Cancel();
                }
                _activeJobs.Clear();

                foreach (var job in _navJobs)
                {
                    job.Cancel();
                }
                _navJobs.Clear();
            }
            catch { }

            base.DeinitializeEditor();
        }

        private void OnEditorUpdate()
        {
            if (!_menuAdded)
            {
                TryAddMenuButtons();
            }

            for (int i = _activeJobs.Count - 1; i >= 0; i--)
            {
                var job = _activeJobs[i];
                job.Tick();

                if (job.IsFinished)
                {
                    _activeJobs.RemoveAt(i);
                }
            }

            for (int i = _navJobs.Count - 1; i >= 0; i--)
            {
                var job = _navJobs[i];
                job.Tick();

                if (job.IsFinished)
                {
                    _navJobs.RemoveAt(i);
                }
            }
        }

        private void TryAddMenuButtons()
        {
            if (_menuAdded) return;

            try
            {
                if (Editor?.UI == null) return;

                Editor.UI.AddMenuButton("Tools", "NarrativePro: 重新生成地图瓦片网格", RegenerateTiles);
                Editor.UI.AddMenuButton("Tools", "NarrativePro: 烘焙地图瓦片贴图", StartBake);
                Editor.UI.AddMenuButton("Tools", "NarrativePro: 创建/校正导航体积", PrepareNavVolume);
                Editor.UI.AddMenuButton("Tools", "NarrativePro: 烘焙导航网格（NavMesh）", StartNavMeshBake);

                _menuAdded = true;
                Debug.Log("[NarrativePro] 导航工具菜单已添加：Tools → NarrativePro: ...");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NarrativePro] 导航工具菜单添加失败，稍后重试: " + ex.Message);
            }
        }

        private static MapTileBoundsActor FindMapTileBounds()
        {
            return Level.FindScript<MapTileBoundsActor>();
        }

        /// <summary>由 MapWidth / NumTiles 重新生成瓦片网格。</summary>
        private static void RegenerateTiles()
        {
            var bounds = FindMapTileBounds();
            if (bounds == null)
            {
                Debug.LogError("[NarrativePro] 场景中未找到 MapTileBoundsActor，无法生成瓦片网格");
                return;
            }

            bounds.EnsureCollider();
            bounds.ApplyColliderBounds();
            bounds.RegenerateTiles(true);

            Debug.Log(
                $"[NarrativePro] 已生成 {bounds.TileCount} 块瓦片" +
                $"（每边 {bounds.TilesPerSide}、单块世界边长 {bounds.TileWorldSize:F1}、贴图 {bounds.TileSet.TileImageSize:F0}px）。" +
                "请保存场景以持久化瓦片集。");
        }

        /// <summary>启动瓦片贴图烘焙。</summary>
        private void StartBake()
        {
            var bounds = FindMapTileBounds();
            if (bounds == null)
            {
                Debug.LogError("[NarrativePro] 场景中未找到 MapTileBoundsActor，无法烘焙瓦片");
                return;
            }

            var layer = bounds.TileSet != null && bounds.TileSet.TileLayer.IsValid()
                ? bounds.TileSet.TileLayer
                : NavigatorGameplayTags.MapLayer_Default;

            var job = new MapTileBakeJob(bounds, layer);
            job.Start();

            if (job.IsFinished)
            {
                Debug.LogError($"[NarrativePro] 瓦片烘焙未能启动：{job.Message}");
                return;
            }

            _activeJobs.Add(job);
            Debug.Log($"[NarrativePro] 已启动地图瓦片烘焙（层 {layer}）。渲染与导入将分帧进行，进度见日志。");
        }

        /// <summary>
        /// 只按地图边界创建/校正 <see cref="NavMeshBoundsVolume"/>，不烘焙。
        /// 用于先在视口里把体积范围调到覆盖真正可走的区域，再执行烘焙。
        /// </summary>
        private static void PrepareNavVolume()
        {
            if (!NavMeshBakeJob.PrepareVolume(DefaultVerticalExtent, DefaultAreaPadding,
                    DefaultFallbackExtent, out var volume, out var failure))
            {
                Debug.LogError($"[NarrativePro] {failure}");
                return;
            }

            Debug.Log($"[NarrativePro] 导航体积已就绪：{volume.Name} @ " +
                      $"({volume.Position.X:F0},{volume.Position.Y:F0},{volume.Position.Z:F0}) " +
                      $"尺寸 ({volume.Size.X:F0},{volume.Size.Y:F0},{volume.Size.Z:F0})。" +
                      "确认它罩住全部可行走地面后，再执行 Tools → NarrativePro: 烘焙导航网格（NavMesh）。");
        }

        /// <summary>启动导航网格烘焙（分帧轮询引擎进度，完成后保存场景与资产）。</summary>
        private void StartNavMeshBake()
        {
            var job = new NavMeshBakeJob
            {
                VerticalExtent = DefaultVerticalExtent,
                AreaPaddingScale = DefaultAreaPadding,
                FallbackXZExtent = DefaultFallbackExtent,
            };
            job.Start();

            if (job.Phase == NavMeshBakePhase.Idle)
            {
                Debug.LogError($"[NarrativePro] 导航网格烘焙未能启动：{job.Message}");
                return;
            }

            if (job.IsFinished)
            {
                // 启动即结束（体积缺失/播放模式），已被 job 记过日志
                return;
            }

            _navJobs.Add(job);
        }

        // 导航体积默认参数：MapWidth 由场景里的 MapTileBoundsActor 提供，这里只给外扩与厚度
        private const float DefaultVerticalExtent = 20000f;
        private const float DefaultAreaPadding = 1.25f;
        private const float DefaultFallbackExtent = 40000f;
    }
}
#endif
