#if FLAX_EDITOR
using FlaxEngine;
using NarrativePro.Navigation;

namespace NarrativePro.Editor.Navigation
{
    /// <summary>导航网格烘焙阶段。</summary>
    public enum NavMeshBakePhase
    {
        Idle,
        Building,
        Completed,
        Failed,
        Cancelled,
    }

    /// <summary>
    /// NavMesh 烘焙：准备 <see cref="NavMeshBoundsVolume"/> → 触发引擎烘焙 → 按帧轮询完成 → 保存场景与资产。
    /// <para>
    /// 走引擎自己的烘焙入口（<c>FlaxEditor.Editor.BuildNavMesh()</c>，等价 Tools → Build Nav Mesh）而不是自调
    /// <c>Navigation.BuildNavMesh</c>：体素尺寸与 agent 参数都取自
    /// <c>Content/Settings/Navigation Settings.json</c>，且由引擎负责为用到的场景补建 <see cref="NavMesh"/>
    /// Actor、把瓦片写进随场景序列化的二进制资产——打包后的客户端只读这份资产，
    /// 烘焙器本身在 Game 目标里通常根本没编译进来，所以必须在编辑器里烘好并保存。
    /// </para>
    /// <para>
    /// <b>为什么完成即保存</b>：瓦片只落在 NavMesh Actor 的 DataAsset 上，不保存就只存在于这次编辑器会话，
    /// 重启 PIE 又回到"无导航网格→直线兜底"。可用 <see cref="bSaveAfterBake"/> 关掉。
    /// </para>
    /// <para>静态 <see cref="PrepareVolume"/> 供"只校正体积不烘焙"的菜单项复用；按帧驱动由编辑器插件调 <see cref="Tick"/>。</para>
    /// </summary>
    public class NavMeshBakeJob
    {
        /// <summary>
        /// 体积厚度（世界单位，Flax 为厘米，上下各一半）。需覆盖地形起伏：
        /// 太薄会把坡地上的体素整片丢掉，表现为"斜面上没有导航网格"。
        /// </summary>
        public float VerticalExtent = 20000f;

        /// <summary>
        /// XZ 相对 <see cref="MapTileBoundsActor.MapWidth"/> 的外扩倍率。
        /// 地图边界只圈住"画得出来的地方"，角色能走到的范围通常更大。
        /// </summary>
        public float AreaPaddingScale = 1.25f;

        /// <summary>场景里没有地图边界 Actor 时的兜底边长（世界单位）。&lt;=0 表示不兜底、直接失败。</summary>
        public float FallbackXZExtent = 40000f;

        /// <summary>请求入队后允许判定"没在烘焙即已完成"的最少帧数。</summary>
        public int GraceFrames = 5;

        /// <summary>烘焙超时（帧，60fps 下 60000 帧≈1000 秒）。</summary>
        public int TimeoutFrames = 60000;

        /// <summary>成功后是否保存场景与资产。</summary>
        public bool bSaveAfterBake = true;

        /// <summary>当前阶段。</summary>
        public NavMeshBakePhase Phase { get; private set; } = NavMeshBakePhase.Idle;

        /// <summary>最近一条状态/错误信息。</summary>
        public string Message { get; private set; } = "";

        /// <summary>是否已结束。</summary>
        public bool IsFinished =>
            Phase == NavMeshBakePhase.Completed
            || Phase == NavMeshBakePhase.Failed
            || Phase == NavMeshBakePhase.Cancelled;

        private int _frames;
        private bool _seenBuilding;
        private NavMeshBoundsVolume _volume;

        /// <summary>体积的水平边长（PrepareVolume 成功后有效）。</summary>
        public float VolumeSide { get; private set; }

        /// <summary>
        /// 创建或校正导航体积（不触发烘焙），供"只准备体积"的菜单项与 <see cref="Start"/> 共用。
        /// </summary>
        /// <returns>是否成功</returns>
        public static bool PrepareVolume(float verticalExtent, float paddingScale, float fallbackXZExtent,
            out NavMeshBoundsVolume volume, out string failure)
        {
            volume = null;
            failure = null;

            if (!ComputeSize(verticalExtent, paddingScale, fallbackXZExtent, out var size))
            {
                failure = "无法确定导航体积范围：场景既没有 MapTileBoundsActor，也未配置兜底尺寸";
                return false;
            }

            var center = ResolveCenter();
            var existing = Level.GetActors<NavMeshBoundsVolume>(false);

            if (existing != null && existing.Length > 0)
            {
                volume = existing[0];
                volume.Position = center;
                volume.Size = size;

                if (existing.Length > 1)
                {
                    Debug.LogWarning($"[NarrativePro] 场景里有 {existing.Length} 个导航体积，" +
                                     $"本次只校正 {volume.Name}，其余保持不变（引擎会烘焙所有体积的并集）。");
                }

                return true;
            }

            var created = new NavMeshBoundsVolume { Name = "NarrativePro_NavMeshBounds" };
            created.Position = center;

            // Level.SpawnActor 返回 true 表示失败（本引擎的反向约定）
            if (Level.SpawnActor(created))
            {
                failure = "创建 NavMeshBoundsVolume 失败（Level.SpawnActor 返回失败）";
                return false;
            }

            // 先定位再定尺寸：体积的 bounds 依当前 Transform 重算
            created.Size = size;
            volume = created;
            return true;
        }

        /// <summary>启动烘焙。之后由外部按帧调用 <see cref="Tick"/>。</summary>
        public void Start()
        {
            if (Phase != NavMeshBakePhase.Idle) return;

            var editor = FlaxEditor.Editor.Instance;
            if (editor == null)
            {
                Fail("编辑器实例不可用（Editor.Instance 为 null）");
                return;
            }

            // IsPlayMode 是静态成员，不能用实例引用访问
            if (FlaxEditor.Editor.IsPlayMode)
            {
                Fail("播放模式下不能烘焙导航网格，请先退出 PIE");
                return;
            }

            if (!PrepareVolume(VerticalExtent, AreaPaddingScale, FallbackXZExtent, out var volume, out var failure))
            {
                Fail(failure);
                return;
            }

            _volume = volume;
            VolumeSide = volume.Size.X;

            editor.BuildNavMesh();

            Phase = NavMeshBakePhase.Building;
            _frames = 0;
            _seenBuilding = false;

            Debug.Log($"[NarrativePro] 已请求烘焙导航网格：体积={_volume.Name} " +
                      $"范围=({volume.Size.X:F0}×{volume.Size.Y:F0}×{volume.Size.Z:F0})，" +
                      $"完成后{(bSaveAfterBake ? "自动保存场景与资产" : "不保存（需手动 Ctrl+S）")}。");
        }

        /// <summary>按帧推进。</summary>
        public void Tick()
        {
            if (IsFinished || Phase != NavMeshBakePhase.Building) return;

            _frames++;

            if (FlaxEngine.Navigation.IsBuildingNavMesh)
            {
                _seenBuilding = true;

                // 只在"确实在烘焙"的时候计时：中途判死会让引擎后台任务变成孤儿，
                // 也让用户以为烘焙失败
                if (_frames >= TimeoutFrames)
                {
                    Fail($"烘焙超时（已进行 {_frames} 帧仍未结束）——场景很大时请调大 TimeoutFrames");
                    return;
                }

                if (_frames % 300 == 0)
                {
                    Debug.Log($"[NarrativePro] 导航网格烘焙中… 进度 {FlaxEngine.Navigation.NavMeshBuildingProgress * 100f:F0}%");
                }

                return;
            }

            // 请求是排到下一次脚本更新才真正开跑的，刚调用 Start 时 IsBuildingNavMesh 仍是 false，
            // 必须跨过这段空窗才能判定"没在烘焙 = 烘完了"
            if (!_seenBuilding && _frames < GraceFrames) return;

            Complete();
        }

        /// <summary>取消任务（不保存）。</summary>
        public void Cancel()
        {
            if (IsFinished) return;

            Phase = NavMeshBakePhase.Cancelled;
            Message = "已取消";
            Debug.LogWarning("[NarrativePro] 导航网格烘焙已取消，未保存。");
        }

        private void Complete()
        {
            var navMeshes = Level.GetActors<NavMesh>(false);
            int count = navMeshes?.Length ?? 0;

            if (count == 0)
            {
                Fail("烘焙流程已结束，但场景里没有 NavMesh Actor——" +
                     "请确认导航体积与地面几何在 XZ 与高度上都有重叠，且地面 Static Model 勾选了 Affects Navigation");
                return;
            }

            Phase = NavMeshBakePhase.Completed;
            Message = $"已生成 {count} 个 NavMesh Actor";

            // 自检：拿体积中心与一条对角线做真实查询，直接证明运行时寻路可用
            var center = _volume != null ? _volume.Position : Vector3.Zero;
            float half = Mathf.Max(100f, VolumeSide * 0.35f);
            bool pathOk = NavigatorStatics.TryFindPath(center,
                center + new Vector3(half, 0f, half), out var path);

            if (bSaveAfterBake)
            {
                FlaxEditor.Editor.Instance.SaveAll();
            }

            Debug.Log($"[NarrativePro] 导航网格烘焙完成：{count} 个 NavMesh Actor，" +
                      $"自检 FindPath {(pathOk ? $"通过（拐点 {path?.Length ?? 0} 个）" : "未通过（该对点可能不连通）")}，" +
                      $"{(bSaveAfterBake ? "已保存场景与资产" : "未保存，请手动 Ctrl+S")}。" +
                      "重启 PIE 后点地寻路即为真实绕障路径，小地图与大地图会同步画出该路径。");
        }

        private void Fail(string message)
        {
            Phase = NavMeshBakePhase.Failed;
            Message = message;
            Debug.LogError("[NarrativePro] 导航网格烘焙失败：" + message);
        }

        private static Vector3 ResolveCenter()
        {
            var bounds = Level.FindScript<MapTileBoundsActor>();
            return bounds?.Actor != null ? bounds.Actor.Position : Vector3.Zero;
        }

        private static bool ComputeSize(float verticalExtent, float paddingScale,
            float fallbackXZExtent, out Vector3 size)
        {
            size = Vector3.Zero;

            float extent = 0f;
            var bounds = Level.FindScript<MapTileBoundsActor>();
            if (bounds != null && bounds.MapWidth > 0f)
            {
                extent = bounds.MapWidth;
            }

            if (extent <= 0f) extent = fallbackXZExtent;
            if (extent <= 0f) return false;

            float side = extent * (paddingScale > 0f ? paddingScale : 1f);
            float height = verticalExtent > 0f ? verticalExtent : side;

            size = new Vector3(side, height, side);
            return true;
        }
    }
}
#endif
