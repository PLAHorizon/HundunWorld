using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Data;

namespace NarrativePro.World
{
    /// <summary>
    /// 一份可攀爬物内容配置里的单条记录。字段与 <see cref="ClimbableActor"/> 的可编辑字段一一对应，
    /// 外加生成用的位置/尺寸（Flax 侧不需要场景摆放，由 <see cref="NarrativeClimbableSpawner"/> 代码生成）。
    /// <para>
    /// 字段用 public field 而非属性：读取端 <see cref="NarrativeClimbableSpawner"/> 的
    /// <see cref="JsonSerializerOptions"/> 开了 <c>IncludeFields</c>，与本仓
    /// <c>Data/NarrativeCrowdConfig.cs</c> / <c>Data/NarrativeNPCDatabase.cs</c> 的绑定模式一致
    /// （<c>PropertyNameCaseInsensitive</c> 为 true，因此 JSON 里按 C# 的 PascalCase 书写即可）。
    /// </para>
    /// </summary>
    [Serializable]
    public class ClimbableEntryJson
    {
        /// <summary>条目名（日志与 Actor 命名用）。</summary>
        public string Name = "";

        /// <summary>是否启用（false 时跳过该条）。</summary>
        public bool bEnabled = true;

        /// <summary>攀爬面原点（世界坐标，单位 cm）。约定与 ClimbableActor 一致：局部 Forward 为背离墙面的外法线。</summary>
        public Vector3 Position = Vector3.Zero;

        /// <summary>偏航角（度）。0 = 面朝 +Z（攀爬面外法线指向 +Z，即墙在 -Z 侧）。</summary>
        public float YawDegrees = 0f;

        /// <summary>碰撞盒尺寸（cm）。给一个真实的 BoxCollider，扫描就能直接打中（不依赖邻近回退）。</summary>
        public Vector3 Size = new Vector3(200f, 300f, 20f);

        /// <summary>底部起始抓握高度（局部 Z）。</summary>
        public float EntryHeight = 0f;

        /// <summary>顶部翻越高度（局部 Z）。</summary>
        public float TopHeight = 300f;

        /// <summary>攀爬面横向半宽（局部 X）。</summary>
        public float HalfWidth = 120f;

        /// <summary>身体中心相对攀爬面的嵌入深度。</summary>
        public float GrabDepth = 30f;

        /// <summary>顶部站立再升的高度。</summary>
        public float StandUpStep = 80f;

        /// <summary>顶部站立时的外向深度。</summary>
        public float ExitDepth = 10f;

        /// <summary>竖直攀爬速度（单位/秒）。</summary>
        public float ClimbVerticalSpeed = 100f;

        /// <summary>横移速度（单位/秒）。</summary>
        public float ClimbStrafeSpeed = 60f;

        /// <summary>
        /// 期望爬完整段的目标时长（秒）。给出时按 <c>(TopHeight-EntryHeight)/DurationSeconds</c>
        /// 反算 <see cref="ClimbVerticalSpeed"/>（时长优先于显式速度，方便内容侧按「几秒爬完」调参）。
        /// &lt;=0 时沿用显式 <see cref="ClimbVerticalSpeed"/>。
        /// </summary>
        public float DurationSeconds = 0f;

        /// <summary>允许入抓的最大水平距离。</summary>
        public float MaxClimbDistance = 140f;

        /// <summary>低于底部多少之内仍可入抓。</summary>
        public float BottomSlack = 250f;

        /// <summary>高于顶部多少之外不再触发。</summary>
        public float TopSlack = 100f;
    }

    /// <summary>一份可攀爬内容配置（对应磁盘上一个 json 文件）。</summary>
    [Serializable]
    public class ClimbablesConfigJson
    {
        /// <summary>配置名（日志用）。</summary>
        public string Name = "";

        /// <summary>整份配置是否启用。</summary>
        public bool bEnabled = true;

        /// <summary>攀爬物条目。</summary>
        public List<ClimbableEntryJson> Climbables = new List<ClimbableEntryJson>();
    }

    /// <summary>
    /// <c>Content/NarrativePro/Climbables/*.json</c> 的读取器。
    /// 与 <see cref="NarrativeCrowdConfig"/> 同套路：目录不存在就安静返回空表（不抛、不每帧重试）。
    /// </summary>
    public static class NarrativeClimbableConfig
    {
        /// <summary>可攀爬内容目录（工程相对路径）。</summary>
        public const string DefaultClimbableDirectory = "Content/NarrativePro/Climbables";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private static readonly List<ClimbablesConfigJson> _configs = new List<ClimbablesConfigJson>();
        private static bool _bLoaded;

        /// <summary>已加载的启用配置。</summary>
        public static IReadOnlyList<ClimbablesConfigJson> Configs => _configs;

        /// <summary>是否存在任何可攀爬内容（不触发磁盘读取）。</summary>
        public static bool HasAny()
        {
            if (!_bLoaded) LoadAll();
            return _configs.Count > 0;
        }

        /// <summary>幂等加载目录内全部配置。</summary>
        public static List<ClimbablesConfigJson> LoadAll(bool force = false)
        {
            if (_bLoaded && !force) return _configs;
            _bLoaded = true;
            if (force) _configs.Clear();

            string resolved = NarrativeDataPaths.ResolveDirectory(DefaultClimbableDirectory);
            if (string.IsNullOrEmpty(resolved) || !Directory.Exists(resolved))
            {
                NarrativeLog.Log(
                    $"[Data] 可攀爬内容目录不存在: '{resolved}'（配置值: {DefaultClimbableDirectory}）——不生成攀爬物。");
                return _configs;
            }

            int entries = 0;
            foreach (var file in Directory.GetFiles(resolved, "*.json"))
            {
                ClimbablesConfigJson config = null;
                try
                {
                    string text = File.ReadAllText(NarrativeDataPaths.Resolve(file));
                    config = JsonSerializer.Deserialize<ClimbablesConfigJson>(text, Options);
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"[Data] 可攀爬配置解析失败 '{file}'：{ex.Message}");
                    continue;
                }

                if (config == null) continue;
                if (!config.bEnabled)
                {
                    NarrativeLog.Log($"[Data] 可攀爬配置 '{file}' 已标记 bEnabled=false，跳过。");
                    continue;
                }
                if (config.Climbables == null || config.Climbables.Count == 0)
                {
                    NarrativeLog.LogWarning($"[Data] 可攀爬配置 '{file}' 没有任何条目，跳过。");
                    continue;
                }

                if (string.IsNullOrEmpty(config.Name)) config.Name = Path.GetFileNameWithoutExtension(file);
                _configs.Add(config);
                entries += config.Climbables.Count;
            }

            NarrativeLog.Log($"[Data] NarrativeClimbableConfig: 可攀爬配置 {_configs.Count} 份、条目 {entries} 条（目录 {resolved}）。");
            return _configs;
        }
    }

    /// <summary>
    /// 内容驱动的攀爬物生成器（无场景摆放也能有攀爬物）。
    /// <para>
    /// 为什么需要它：UE5 的攀爬物由关卡摆放的 AClimbableActor 提供；本游戏的 Flax 场景资产
    /// （Content/Maps/*.scene）从不实例化插件 Actor，于是 <see cref="ClimbableActor"/> 的静态登记表
    /// 恒空 —— NPC 的路径跟随在墙前发起攀爬时（<c>AI/NarrativePathFollowingComp</c> 的
    /// <c>TryAttachWarp</c> 重试环）既扫不到 IClimbable、邻近回退也拿不到候选，攀爬通路整条不可达。
    /// </para>
    /// <para>
    /// 通路复用 <see cref="NarrativePro.Spawners.NPCCrowdSpawner"/> 的既有做法：读
    /// <c>Content/NarrativePro/Climbables/*.json</c> → 代码生成 Actor（这里是带真实
    /// <see cref="BoxCollider"/> 的 Actor，扫描直接打得中）→ 挂 <see cref="ClimbableActor"/> 并写入字段。
    /// 触发时机是首次攀爬发现查询（主线程 OnUpdate 内，由 <see cref="ClimbableActor.FindNearestClimbable"/>
    /// 调用），<b>一次性幂等</b>：无论成功失败都不再重试，避免每帧磁盘扫描。
    /// </para>
    /// </summary>
    public static class NarrativeClimbableSpawner
    {
        private static readonly List<Actor> _spawned = new List<Actor>();
        private static bool _bSpawned;
        private static int _spawnedCount;

        /// <summary>已生成的攀爬物 Actor 数。</summary>
        public static int SpawnedCount => _spawnedCount;

        /// <summary>是否已经跑过一轮内容生成。</summary>
        public static bool HasRun => _bSpawned;

        /// <summary>已生成 Actor 快照（供诊断/存档侧使用）。</summary>
        public static IReadOnlyList<Actor> Spawned => _spawned;

        /// <summary>
        /// 幂等地按内容生成攀爬物。必须在主线程调用（本仓唯一调用点在移动组件的攀爬发现里，属 OnUpdate 阶段）。
        /// </summary>
        public static void EnsureSpawnedFromContent()
        {
            if (_bSpawned) return;
            _bSpawned = true;   // 先立旗：任何异常/空目录都不再重复扫盘

            try
            {
                var configs = NarrativeClimbableConfig.LoadAll();
                if (configs.Count == 0) return;

                foreach (var config in configs)
                {
                    if (config?.Climbables == null) continue;
                    foreach (var entry in config.Climbables)
                    {
                        if (entry == null || !entry.bEnabled) continue;
                        SpawnOne(config, entry);
                    }
                }

                NarrativeLog.Log($"[World] 内容驱动攀爬物生成完成：{_spawnedCount} 个（配置 {configs.Count} 份）。");
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("[World] 攀爬物内容生成失败：" + ex.Message);
            }
        }

        private static void SpawnOne(ClimbablesConfigJson config, ClimbableEntryJson entry)
        {
            // 用 BoxCollider 当宿主：Flax 的碰撞体本身是 Actor，既能被 SphereCastAll 打中
            //（不必依赖邻近回退），也真的挡路 —— 这样「NPC 被墙挡住 → 爬过去」在运行期可复现。
            var collider = new BoxCollider
            {
                Name = string.IsNullOrEmpty(entry.Name) ? $"Climbable_{config.Name}" : entry.Name,
            };

            // 世界对象必须显式生成进玩法场景：无父级版 SpawnActor 固定落 Scenes[0]（RootScene）
            if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(collider, collider.Name))
            {
                // Flax 约定：SpawnActor 返回 true 表示失败。
                NarrativeLog.LogWarning($"[World] 攀爬物生成失败（玩法场景未就绪或 SpawnActor 失败）：'{entry.Name}' @ {entry.Position}");
                FlaxEngine.Object.Destroy(collider);
                return;
            }

            collider.Position = entry.Position;
            collider.Orientation = Quaternion.RotationY(entry.YawDegrees * Mathf.DegreesToRadians);
            collider.Size = entry.Size;
            collider.IsTrigger = false;   // 必须可阻挡：攀爬面就是被扫描的几何

            var climbable = collider.AddScript<ClimbableActor>();
            if (climbable == null)
            {
                NarrativeLog.LogWarning($"[World] 攀爬物脚本挂载失败：'{entry.Name}'");
                FlaxEngine.Object.Destroy(collider);
                return;
            }

            climbable.EntryHeight = entry.EntryHeight;
            climbable.TopHeight = entry.TopHeight;
            climbable.HalfWidth = entry.HalfWidth;
            climbable.GrabDepth = entry.GrabDepth;
            climbable.StandUpStep = entry.StandUpStep;
            climbable.ExitDepth = entry.ExitDepth;
            climbable.ClimbStrafeSpeed = entry.ClimbStrafeSpeed;
            climbable.MaxClimbDistance = entry.MaxClimbDistance;
            climbable.BottomSlack = entry.BottomSlack;
            climbable.TopSlack = entry.TopSlack;

            // 目标时长优先：内容侧写「几秒爬完」，速度按段长反算（避免两处口径打架）。
            float span = entry.TopHeight - entry.EntryHeight;
            if (entry.DurationSeconds > 0f && span > 0f)
                climbable.ClimbVerticalSpeed = span / entry.DurationSeconds;
            else
                climbable.ClimbVerticalSpeed = entry.ClimbVerticalSpeed;

            _spawned.Add(collider);
            _spawnedCount++;
        }

        /// <summary>销毁内容生成的攀爬物（关卡切换/测试收尾用；场景摆放的不受影响）。</summary>
        public static void DespawnAll()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                var actor = _spawned[i];
                if (actor != null) FlaxEngine.Object.Destroy(actor);
            }
            _spawned.Clear();
            _spawnedCount = 0;
            _bSpawned = false;
            NarrativeClimbableConfig.LoadAll(force: true);
        }
    }
}
