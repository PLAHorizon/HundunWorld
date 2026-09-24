#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEngine;
using NarrativePro.Items;
using NarrativePro.Navigation;

namespace NarrativePro.Editor.Navigation
{
    /// <summary>瓦片烘焙阶段</summary>
    public enum MapTileBakePhase
    {
        Idle,
        Setup,
        Rendering,
        Importing,
        Applying,
        Completed,
        Failed,
        Cancelled,
    }

    /// <summary>
    /// 地图瓦片烘焙任务：用**正交相机 + <see cref="SceneRenderTask"/>** 逐块渲染世界，
    /// 经 <see cref="Screenshot.Capture(SceneRenderTask, string)"/> 落盘 PNG，
    /// 再通过 <c>ContentImporting</c> 导入为 Texture 资产并回填到 <see cref="MapTileSet"/> 的指定层级。
    /// <para>
    /// 对应 UE5 蓝图 <c>BP_MapTileActor</c> 的职责（Flax 无蓝图节点，故以 C# + 编辑器插件承载）。
    /// 由编辑器插件按帧驱动 <see cref="Tick"/>，单次调用不阻塞编辑器。
    /// </para>
    /// <para>
    /// <b>注意</b>：<c>GPUTexture.Init</c> 与 <c>Level.SpawnActor</c> 在本引擎中都是
    /// 「返回 true 表示失败」的反向约定，本类已按该约定处理。
    /// </para>
    /// </summary>
    public class MapTileBakeJob
    {
        /// <summary>每块瓦片之间等待的渲染帧数（正交相机移动后必须至少等一帧再截图）</summary>
        public int RenderFramesPerTile = 3;

        /// <summary>等待截图文件落盘的超时（秒）</summary>
        public float FileWaitTimeoutSeconds = 30f;

        /// <summary>
        /// 正交相机世界尺寸倍率。
        /// <b>Flax 的 <c>Camera.OrthographicSize</c> 语义（半高还是全高）文档未明确，需实测标定</b>（见设计文档 R1）：
        /// 烘焙单块后比对世界标志物在贴图中的像素跨度，反推该倍率。
        /// </summary>
        public float OrthographicSizeMultiplier = 1f;

        /// <summary>相机高度（世界单位），需高于地形最高点</summary>
        public float CameraHeight = 20000f;

        /// <summary>相机俯视俯仰角（度）</summary>
        public float CameraPitchDegrees = -90f;

        /// <summary>瓦片贴图分辨率（像素，正方形）</summary>
        public int TilePixelSize = 1024;

        /// <summary>输出目录（相对工程根，须位于 Content 下才能导入为资产）</summary>
        public string OutputFolderRelative = "Content/Maps/BakedTiles";

        /// <summary>是否把 PNG 导入为 Content 资产</summary>
        public bool bImportToContent = true;

        /// <summary>是否把结果回填到 <see cref="MapTileSet"/></summary>
        public bool bApplyToTileSet = true;

        /// <summary>当前阶段</summary>
        public MapTileBakePhase Phase { get; private set; } = MapTileBakePhase.Idle;

        /// <summary>最近一条状态/错误信息</summary>
        public string Message { get; private set; } = "";

        /// <summary>已完成瓦片数</summary>
        public int CompletedTiles { get; private set; }

        /// <summary>总瓦片数</summary>
        public int TotalTiles { get; private set; }

        /// <summary>进度 0~1</summary>
        public float Progress => TotalTiles <= 0 ? 0f : (float)CompletedTiles / TotalTiles;

        /// <summary>是否已结束（成功/失败/取消）</summary>
        public bool IsFinished =>
            Phase == MapTileBakePhase.Completed || Phase == MapTileBakePhase.Failed || Phase == MapTileBakePhase.Cancelled;

        private readonly MapTileBoundsActor _bounds;
        private readonly GameplayTag _layer;

        private EmptyActor _cameraActor;
        // 注意：本工程存在 NarrativePro.Camera 命名空间，Camera 必须全限定，否则报 CS0118
        private FlaxEngine.Camera _camera;
        private GPUTexture _renderTexture;
        private SceneRenderTask _renderTask;

        private int _tileIndex;
        private int _framesToWait;
        private bool _captureStarted;
        private float _fileWaitElapsed;
        private bool _importStarted;
        private readonly List<string> _pngPaths = new List<string>();

        public MapTileBakeJob(MapTileBoundsActor bounds, GameplayTag layer)
        {
            _bounds = bounds;
            _layer = layer.IsValid() ? layer : NavigatorGameplayTags.MapLayer_Default;
        }

        /// <summary>当前瓦片的目标 PNG 绝对路径。</summary>
        private string CurrentPngPath =>
            _tileIndex >= 0 && _tileIndex < _pngPaths.Count ? _pngPaths[_tileIndex] : null;

        /// <summary>开始烘焙。之后由外部按帧调用 <see cref="Tick"/>。</summary>
        public void Start()
        {
            if (_bounds?.Actor == null)
            {
                Fail("未找到 MapTileBoundsActor（或其绑定的 Actor），无法烘焙");
                return;
            }

            if (_bounds.TileSet?.Tiles == null || _bounds.TileSet.Tiles.Count == 0)
            {
                Fail("瓦片集为空，请先在 MapTileBoundsActor 上生成瓦片网格（RegenerateTiles）");
                return;
            }

            Phase = MapTileBakePhase.Setup;
            Message = "准备中";
        }

        /// <summary>取消烘焙并清理资源。</summary>
        public void Cancel()
        {
            if (IsFinished) return;
            Cleanup();
            Phase = MapTileBakePhase.Cancelled;
            Message = "已取消";
        }

        /// <summary>按帧推进。由编辑器插件在 <c>Editor.EditorUpdate</c> 中调用。</summary>
        public void Tick()
        {
            if (IsFinished || Phase == MapTileBakePhase.Idle) return;

            try
            {
                switch (Phase)
                {
                    case MapTileBakePhase.Setup:
                        BeginSetup();
                        break;

                    case MapTileBakePhase.Rendering:
                        TickRendering();
                        break;

                    case MapTileBakePhase.Importing:
                        TickImporting();
                        break;

                    case MapTileBakePhase.Applying:
                        ApplyToTileSet();
                        break;
                }
            }
            catch (Exception ex)
            {
                Fail($"烘焙异常: {ex.Message}");
            }
        }

        // ===================================================================
        // Setup
        // ===================================================================

        private void BeginSetup()
        {
            var outDir = Path.Combine(Globals.ProjectFolder, OutputFolderRelative);
            Directory.CreateDirectory(outDir);

            BuildPngPathList(outDir);

            if (!CreateCamera())
            {
                Fail("创建离屏正交相机失败");
                return;
            }

            if (!CreateRenderTarget())
            {
                Fail("创建渲染目标失败（GPUTexture.Init）");
                return;
            }

            TotalTiles = _pngPaths.Count;
            CompletedTiles = 0;
            _tileIndex = 0;
            _importStarted = false;

            Phase = MapTileBakePhase.Rendering;
            Message = $"开始渲染 {TotalTiles} 块瓦片（层 {_layer}）";
            PositionCameraForCurrentTile();
        }

        private void BuildPngPathList(string outDir)
        {
            _pngPaths.Clear();

            var tiles = _bounds.TileSet.Tiles;
            for (int i = 0; i < tiles.Count; i++)
            {
                string name = SanitizeFileName($"Tile_{_layer.TagName}_T{i}");
                _pngPaths.Add(Path.Combine(outDir, name + ".png"));
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
            return name;
        }

        private bool CreateCamera()
        {
            _cameraActor = new EmptyActor { Name = "NarrativePro_MapTileBakeCamera" };

            // Level.SpawnActor 返回 true 表示失败；挂到 MapTileBounds Actor 下便于随场景一起清理
            if (Level.SpawnActor(_cameraActor, _bounds.Actor))
            {
                _cameraActor = null;
                return false;
            }

            _camera = _cameraActor.AddChild<FlaxEngine.Camera>();
            _camera.UsePerspective = false;
            _camera.NearPlane = 1f;
            _camera.FarPlane = CameraHeight * 3f + 1000f;
            return true;
        }

        private bool CreateRenderTarget()
        {
            int size = Mathf.Max(64, TilePixelSize);
            var desc = GPUTextureDescription.New2D(size, size, PixelFormat.R8G8B8A8_UNorm);

            _renderTexture = new GPUTexture();
            // GPUTexture.Init 返回 true 表示**无法**创建
            if (_renderTexture.Init(ref desc))
            {
                FlaxEngine.Object.Destroy(_renderTexture);
                _renderTexture = null;
                return false;
            }

            _renderTask = new SceneRenderTask
            {
                Order = -200,
                Output = _renderTexture,
                Camera = _camera,
                Enabled = true,
            };

            return true;
        }

        // ===================================================================
        // Rendering
        // ===================================================================

        private void PositionCameraForCurrentTile()
        {
            var tile = _bounds.GetTileAtIndex(_tileIndex);
            if (tile == null)
            {
                Fail($"瓦片索引 {_tileIndex} 越界或数据为空");
                return;
            }

            float tileWorldSize = _bounds.TileWorldSize;
            _cameraActor.Position = new Vector3(
                tile.TileLocation.X,
                tile.TileLocation.Y + CameraHeight,
                tile.TileLocation.Z);
            _cameraActor.Orientation = Quaternion.Euler(CameraPitchDegrees, 0f, 0f);

            // OrthographicSize 语义需实测标定，故用倍率暴露（见设计文档 R1）
            _camera.OrthographicSize = tileWorldSize * OrthographicSizeMultiplier;

            // 相机 Teleport 后需通知渲染任务重新计算视图
            _renderTask?.CameraCut();

            _framesToWait = Mathf.Max(1, RenderFramesPerTile);
            _captureStarted = false;
            _fileWaitElapsed = 0f;
        }

        private void TickRendering()
        {
            if (_tileIndex >= _pngPaths.Count)
            {
                BeginImportOrApply();
                return;
            }

            if (_framesToWait > 0)
            {
                _framesToWait--;
                return;
            }

            string pngPath = CurrentPngPath;

            if (!_captureStarted)
            {
                // 删除可能残留的旧文件，否则 File.Exists 会立刻为真而误判完成
                if (File.Exists(pngPath)) File.Delete(pngPath);

                Screenshot.Capture(_renderTask, pngPath);
                _captureStarted = true;
                _fileWaitElapsed = 0f;
                return;
            }

            if (File.Exists(pngPath) && new FileInfo(pngPath).Length > 0)
            {
                CompletedTiles++;

                // 若下一块的 PNG 名与当前同名（理论上不会，索引已入名），仍按流程推进
                _tileIndex++;
                if (_tileIndex < _pngPaths.Count)
                {
                    PositionCameraForCurrentTile();
                }
                else
                {
                    BeginImportOrApply();
                }
                return;
            }

            _fileWaitElapsed += Time.DeltaTime;
            if (_fileWaitElapsed > FileWaitTimeoutSeconds)
            {
                Fail($"等待瓦片截图落盘超时（{FileWaitTimeoutSeconds}s）：{pngPath}");
            }
        }

        private void BeginImportOrApply()
        {
            if (bImportToContent)
            {
                Phase = MapTileBakePhase.Importing;
                _importStarted = false;
                Message = "渲染完成，开始导入 Content";
            }
            else
            {
                Phase = MapTileBakePhase.Applying;
                Message = "渲染完成（未导入 Content）";
            }
        }

        // ===================================================================
        // Import
        // ===================================================================

        private void TickImporting()
        {
            var editor = FlaxEditor.Editor.Instance;
            if (editor == null)
            {
                Fail("编辑器实例不可用，无法导入瓦片贴图");
                return;
            }

            if (!_importStarted)
            {
                var fullDir = Path.Combine(Globals.ProjectFolder, OutputFolderRelative);
                var folder = editor.ContentDatabase.Find(fullDir) as ContentFolder;
                if (folder == null)
                {
                    editor.ContentDatabase.Rebuild(true);
                    folder = editor.ContentDatabase.Find(fullDir) as ContentFolder;
                }

                if (folder == null)
                {
                    Fail($"无法定位输出目录（需位于 Content 下）：{OutputFolderRelative}");
                    return;
                }

                foreach (var png in _pngPaths)
                {
                    if (File.Exists(png))
                    {
                        editor.ContentImporting.Import(png, folder, true);
                    }
                }

                _importStarted = true;
                Message = $"已提交 {_pngPaths.Count} 张瓦片贴图的导入请求";
                return;
            }

            if (editor.ContentImporting.IsImporting) return;

            editor.ContentDatabase.Rebuild(true);
            Phase = MapTileBakePhase.Applying;
            Message = "导入完成，开始回填瓦片集";
        }

        // ===================================================================
        // Apply
        // ===================================================================

        private void ApplyToTileSet()
        {
            if (bApplyToTileSet)
            {
                for (int i = 0; i < _pngPaths.Count; i++)
                {
                    var tile = _bounds.GetTileAtIndex(i);
                    if (tile == null) continue;

                    var assetPath = bImportToContent
                        ? $"{OutputFolderRelative}/{Path.GetFileNameWithoutExtension(_pngPaths[i])}.flax"
                        : _pngPaths[i];

                    tile.SetImagePathForLayer(_layer, assetPath);
                }

                _bounds.TileSet.TileLayer = _layer;

                FlaxEngine.Debug.Log(
                    $"[MapTileBaker] 已回填 {_pngPaths.Count} 块瓦片的图层贴图（层 {_layer}）。" +
                    "请保存场景以持久化瓦片集，并刷新地图 UI 以应用新层级。");
            }

            Cleanup();
            Phase = MapTileBakePhase.Completed;
            Message = $"完成：{CompletedTiles}/{TotalTiles} 块瓦片，输出目录 {OutputFolderRelative}";
            FlaxEngine.Debug.Log($"[MapTileBaker] {Message}");
        }

        // ===================================================================
        // 清理
        // ===================================================================

        private void Cleanup()
        {
            if (_renderTask != null)
            {
                _renderTask.Enabled = false;
                _renderTask.Camera = null;
                FlaxEngine.Object.Destroy(_renderTask);
                _renderTask = null;
            }

            if (_renderTexture != null)
            {
                FlaxEngine.Object.Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_cameraActor != null)
            {
                FlaxEngine.Object.Destroy(_cameraActor);
                _cameraActor = null;
            }

            _camera = null;
        }

        private void Fail(string message)
        {
            Message = message;
            Phase = MapTileBakePhase.Failed;
            FlaxEngine.Debug.LogError($"[MapTileBaker] {message}");
            Cleanup();
        }
    }
}
#endif
