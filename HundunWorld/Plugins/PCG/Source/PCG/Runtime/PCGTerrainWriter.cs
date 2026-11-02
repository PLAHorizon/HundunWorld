// PCG 插件 - 地形合成的写入端（薄壳：按配方把高度写进 PCG 自己的那块 Terrain）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §9 第 6 条。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;
using PCG.Graph;

namespace PCG.Runtime
{
    /// <summary>
    /// 把 <see cref="PCGTerrainRecipe"/> 落地：找到 PCG 自己的那块 <see cref="Terrain"/>，
    /// 逐 patch 写高度场，并把写下去的那组数交给下游当"高度来源"。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么必须有一块现成的地形（引擎的硬约束，逐行核过源码）。</b>
    /// 托管侧对地形的写入能力**只有** <c>Terrain.SetupPatchHeightMap</c> 一个入口。而它内部第一步是
    /// <c>GetPatch(coord)</c>：**没有那一格 patch 就直接返回 true（失败）而什么都不写**。
    /// 更靠前的问题是：运行期新建一个 <c>Terrain</c> 时 <c>chunkSize == 0</c>（见 <c>Terrain::Terrain</c> 的
    /// 初始化列表），于是 <c>PatchSize == 0</c>；而给它建 patch 的 <c>Setup(lodCount, chunkSize)</c> /
    /// <c>AddPatches(int2)</c> 在 C++ 侧没有 <c>API_FUNCTION</c>、**没有托管绑定**（对照
    /// <c>Engine/Terrain/Terrain.cs</c> 只有 <c>SetupPatchHeightMap</c> 与几个常量）。
    /// 结论：**脚本没法造出"可写的地形"**，只能写进一块已经有 patch 的地形。
    /// </para>
    /// <para>
    /// <b>写谁的地形（2026-10-10 用户拍板）。</b>优先认领名字以 <see cref="TerrainActorPrefix"/> 开头的
    /// 地形；场景里没有就写**场景里那张 Terrain**（工程现状：Main 那张）。写之前把每一格的**原始高度场
    /// 备份成文件**（<see cref="BackupFolder"/>，只在没有备份时写，绝不覆盖旧备份），
    /// <see cref="TryRestore"/> 用它们把地形还原回去。为什么不备份在内存里：备份要活过一次重启。
    /// </para>
    /// <para>
    /// <b>为什么要落资产（<c>forceUseVirtualStorage: false</c>）。</b>引擎源码里那一行是
    /// <c>useVirtualStorage = Editor::IsPlayMode || forceUseVirtualStorage</c>：编辑器里传 true 就只写内存，
    /// **资产不动**——于是 PIE / 重开场景会回到原地形，而编辑器里烘焙出来的物件是按新地形摆的，
    /// 在游戏里就会浮空或埋进去。要"编辑器看到的就是游戏里跑的"，高度必须落进资产。
    /// 代价是这一步**会改你现有的地形**，所以备份与还原是它的一部分，不是可选项。
    /// </para>
    /// <para>
    /// <b>为什么不读回高度。</b>托管侧没有高度 getter，而且"写下去的 raw 值 → 世界 Y"这条换算
    /// 没有标定（附录 B.2）。所以这里把**刚写下去的那份世界单位数组**留在内存里，并把它当作
    /// <see cref="PCGWorldContext.HeightSource"/>——紧随其后的 biome / water / scatter / 洞穴入口
    /// 因此读到的是新地形，而不是旧高度（很多场景里那还是零平面）。这是决定 D 的第一条约束。
    /// </para>
    /// <para>
    /// <b>分辨率。</b>引擎的 <c>UnitsPerVertex</c> 是 100、<c>ChunksCountEdge</c> 是 4，
    /// 所以 patch 的世界边长 = <c>100 * 4 * ChunkSize</c>，**顶点间距恒为 100 世界单位**。
    /// 也就是说：合成出来的丘陵/盆地是按 100 单位一格成型的（一座半径 600 的土丘只有十来个顶点），
    /// 这是引擎地形的粒度，不是这个写入端能调的。
    /// </para>
    /// </remarks>
    public static class PCGTerrainWriter
    {
        /// <summary>PCG 自己那块地形 actor 的名字前缀。</summary>
        public const string TerrainActorPrefix = "PCG_Terrain";

        /// <summary>
        /// 按配方写地形。
        /// </summary>
        /// <param name="recipe">配方；null 表示这张图不造地形，直接成功返回。</param>
        /// <param name="owner">哪个体积在写（用来定位场景）。</param>
        /// <param name="worldBox">要覆盖的世界范围（体积的世界包围盒）。</param>
        /// <param name="seed">噪声种子。</param>
        /// <param name="heightSource">写下去的那组数构成的"世界 Y 查询"；失败或没配方时为 null。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>成功为 true。失败时调用方**不要**当作整次生成失败：图的其余部分照样能跑，
        /// 只是它看到的高度是旧地形——所以调用方那边是"记一条警告"。</returns>
        public static bool TryApply(PCGTerrainRecipe recipe, Actor owner, BoundingBox worldBox, int seed,
            out Func<float, float, float?> heightSource, out string error)
        {
            error = null;
            heightSource = null;

            if (recipe == null)
                return true;

            if (owner == null)
            {
                error = "没有宿主体积，无法定位场景。";
                return false;
            }

            var scene = owner.Scene;

            if (scene == null)
            {
                error = "体积不在任何场景里，地形没有可写的地方。";
                return false;
            }

            var terrain = FindTerrain(scene);

            if (terrain == null)
            {
                error = $"场景里没有 PCG 自己的地形：新建一个 Terrain actor，把名字改成以 " +
                        $"'{TerrainActorPrefix}' 开头（例如 {TerrainActorPrefix}_Demo），在它的属性面板里 " +
                        $"Setup（LOD 与 chunk 边长）并 Add patches——托管侧没有建 patch 的 API，这一步必须由编辑器做。";
                return false;
            }

            // chunk 边长只能读（`Terrain.ChunkSize` 在托管侧是只读的），而高度场的边长必须等于
            // 引擎那句 `(chunkSize*4+1)`。所以以地形自己报出来的为准。
            int chunkSize = terrain.ChunkSize;

            if (chunkSize <= 0)
            {
                error = $"'{terrain.Name}' 报出的 chunk 边长是 {chunkSize}；这块地形没有 Setup 过，先按 " +
                        "上面的步骤给它 LOD 与 chunk 边长。";
                return false;
            }

            float patchSize = terrain.PatchSize;

            if (patchSize <= 0.0f)
            {
                error = $"'{terrain.Name}' 报出的 patch 边长是 {patchSize}，写不了高度场。";
                return false;
            }

            var terrainPosition = terrain.Position;
            var features = recipe.Features ?? Array.Empty<PCGTerrainFeature>();

            // 覆盖世界范围需要的 patch 下标闭区间。地形没法被脚本摆布，所以用**它自己的**位置与
            // patch 边长来算：一块 patch 覆盖 100*4*chunk 那么长，范围大时可能跨好几格。
            int minPatchX = (int)Mathf.Floor((worldBox.Minimum.X - terrainPosition.X) / patchSize);
            int maxPatchX = (int)Mathf.Floor((worldBox.Maximum.X - terrainPosition.X) / patchSize);
            int minPatchZ = (int)Mathf.Floor((worldBox.Minimum.Z - terrainPosition.Z) / patchSize);
            int maxPatchZ = (int)Mathf.Floor((worldBox.Maximum.Z - terrainPosition.Z) / patchSize);

            int samples = PCGTerrainSynthesizer.SamplesPerPatch(chunkSize);
            var written = new Dictionary<long, float[]>();

            for (int patchZ = minPatchZ; patchZ <= maxPatchZ; patchZ++)
            {
                for (int patchX = minPatchX; patchX <= maxPatchX; patchX++)
                {
                    var coord = new Int2(patchX, patchZ);

                    // 先问一句有没有这一格：`SetupPatchHeightMap` 在缺 patch 时**静默返回失败**，
                    // 直接把那个"true"报出去会让人以为是高度场写坏了，而真因是这块地形没铺到那儿。
                    if (!terrain.HasPatch(ref coord))
                    {
                        error = $"'{terrain.Name}' 上没有第 ({patchX},{patchZ}) 格 patch；把这块地形 " +
                                $"Add patches 到能盖住生成范围（每格 {patchSize:F0} 世界单位）。";
                        return false;
                    }

                    // 覆盖之前先把**原始**高度场备份到文件：只在没有备份时写，绝不覆盖旧备份——
                    // 第二次生成时那一格已经是 PCG 的地形，拿它当"原始"会把回退点越推越远。
                    if (!TryBackup(terrain, coord, out string backupError))
                    {
                        error = backupError;
                        return false;
                    }

                    var heights = new float[samples * samples];
                    var origin = new Vector2(
                        terrainPosition.X + patchX * patchSize,
                        terrainPosition.Z + patchZ * patchSize);

                    if (!PCGTerrainSynthesizer.TrySynthesize(chunkSize, origin, patchSize, features,
                            recipe.NoiseAmplitude, recipe.NoiseScale, seed, heights, out string synthesizeError))
                    {
                        error = $"第 ({patchX},{patchZ}) 个 patch 合成失败：{synthesizeError}";
                        return false;
                    }

                    // **洞掩码**：写了洞形状才造。0 = 洞、255 = 有地面（引擎源码里就是这个约定，
                    // 见 `PCGTerrainSynthesizer.TryBuildHolesMask` 的注解）。不写洞就传 null ⇒ 引擎保持
                    // 它原有的洞不动，不会被"没提洞"这件事清掉。
                    byte[] holes = null;

                    if (recipe.Holes != null && recipe.Holes.Length > 0)
                    {
                        holes = new byte[samples * samples];

                        if (!PCGTerrainSynthesizer.TryBuildHolesMask(chunkSize, origin, patchSize, recipe.Holes,
                                holes, out string holesError))
                        {
                            error = $"第 ({patchX},{patchZ}) 个 patch 的洞掩码造失败：{holesError}";
                            return false;
                        }
                    }

                    // 反直觉约定（本仓踩过三次的那一类）：**true 表示失败**。
                    // forceUseVirtualStorage 传 **false**：高度要落进资产，否则编辑器里烘焙的内容
                    // 在 PIE / 重开场景之后会悬在（或埋进）原地形里——见类型注释。
                    if (terrain.SetupPatchHeightMap(ref coord, heights, holes, false))
                    {
                        error = $"第 ({patchX},{patchZ}) 个 patch 的 SetupPatchHeightMap 报失败。";
                        return false;
                    }

                    written[Key(patchX, patchZ)] = heights;
                }
            }

            heightSource = (x, z) => SampleWritten(written, terrainPosition, patchSize, samples, x, z);
            return true;
        }

        /// <summary>
        /// 找要写的地形：优先名字以 <see cref="TerrainActorPrefix"/> 开头的，否则用场景里第一张 Terrain。
        /// </summary>
        /// <remarks>
        /// 名字前缀优先，是为了让"工程里有自己那块 PCG 地形"时不去动美术那张；没有时才落到场景现有的
        /// Terrain 上（用户 2026-10-10 的选择：写 Main 那张，写前备份、附带还原）。
        /// </remarks>
        private static Terrain FindTerrain(Scene scene)
        {
            var roots = scene.GetChildren<Actor>();

            if (roots == null)
                return null;

            Terrain first = null;

            for (int i = 0; i < roots.Length; i++)
            {
                if (!(roots[i] is Terrain candidate))
                    continue;

                if (candidate.Name != null &&
                    candidate.Name.StartsWith(TerrainActorPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (first == null)
                    first = candidate;
            }

            return first;
        }

        /// <summary>
        /// 在一个 patch 的顶点格坐标上做双线性插值；纯函数，进离屏门。
        /// </summary>
        /// <param name="heights">那个 patch 的高度场（与 <see cref="PCGTerrainSynthesizer"/> 同口径）。</param>
        /// <param name="samples">边长顶点数（<c>chunkSize*4+1</c>）。</param>
        /// <param name="u">X 方向的顶点格坐标，取值 [0, samples-1]。</param>
        /// <param name="v">Z 方向的顶点格坐标，取值 [0, samples-1]。</param>
        /// <remarks>
        /// 下标是**行优先（先 Z 后 X）**，即 <c>z * samples + x</c>——与合成器写下去的那一份完全一致。
        /// 这条缝对得上，写入端"把刚写下去的数喂给采样端"才有意义；对不上就是每个点都偏一点点，
        /// 而那是最难看出来的错。所以它被单独拎出来，好让离屏断言能直接量它（引擎里读不回来）。
        /// </remarks>
        public static float SamplePatch(float[] heights, int samples, float u, float v)
        {
            if (heights == null || samples < 2)
                return 0.0f;

            float max = samples - 1;

            if (u < 0.0f) u = 0.0f; else if (u > max) u = max;
            if (v < 0.0f) v = 0.0f; else if (v > max) v = max;

            int x0 = (int)u;
            int z0 = (int)v;
            int x1 = x0 + 1 >= samples ? samples - 1 : x0 + 1;
            int z1 = z0 + 1 >= samples ? samples - 1 : z0 + 1;

            float fx = u - x0;
            float fz = v - z0;

            float h00 = heights[z0 * samples + x0];
            float h10 = heights[z0 * samples + x1];
            float h01 = heights[z1 * samples + x0];
            float h11 = heights[z1 * samples + x1];

            float a = h00 + (h10 - h00) * fx;
            float b = h01 + (h11 - h01) * fx;

            return a + (b - a) * fz;
        }

        /// <summary>
        /// 从写下去的那些数组里读世界 Y：先定位 patch，再在 patch 内做双线性插值。
        /// </summary>
        /// <remarks>
        /// 范围外给 null（"这里没有地面"），让采样端照常按"没命中"处理。
        /// </remarks>
        private static float? SampleWritten(Dictionary<long, float[]> written, Vector3 terrainPosition,
            float patchSize, int samples, float x, float z)
        {
            if (written.Count == 0)
                return null;

            int patchX = (int)Mathf.Floor((x - terrainPosition.X) / patchSize);
            int patchZ = (int)Mathf.Floor((z - terrainPosition.Z) / patchSize);

            if (!written.TryGetValue(Key(patchX, patchZ), out var heights) || heights == null)
                return null;

            float originX = terrainPosition.X + patchX * patchSize;
            float originZ = terrainPosition.Z + patchZ * patchSize;

            // patch 局部坐标，单位是"顶点格"：[0, samples-1]。
            float u = (x - originX) / patchSize * (samples - 1);
            float v = (z - originZ) / patchSize * (samples - 1);

            return SamplePatch(heights, samples, u, v);
        }

        /// <summary>patch 下标打包成字典键（<c>Int2</c> 的哈希不在这里依赖）。</summary>
        private static long Key(int patchX, int patchZ) => ((long)patchX << 32) ^ (uint)patchZ;

        // ==================== 原始高度场的备份与还原 ====================

        /// <summary>原始高度场的备份目录（工程内，跟着版本库走）。</summary>
        public static string BackupFolder =>
            System.IO.Path.Combine(Globals.ProjectFolder, "Logs", "PCG", "TerrainBackup");

        /// <summary>一格 patch 的备份文件名：原地形 id + 格坐标，于是多张地形也不打架。</summary>
        private static string BackupPath(Terrain terrain, Int2 coord) =>
            System.IO.Path.Combine(BackupFolder, $"{terrain.ID:N}_{coord.X}_{coord.Y}.bin");

        /// <summary>
        /// 把一格 patch 的**原始**高度场备份到文件；已有备份就什么都不做。
        /// </summary>
        /// <remarks>
        /// 读的是 raw 值（<c>GetHeightmapData</c>），所以还原是**逐位**的——与"raw → 世界 Y"那条
        /// 未标定的换算无关（附录 B.2）。写文件比只留内存多一步，但内存里的备份活不过重启，
        /// 而"把用户的地形改坏了"这种事必须能跨重启回退。
        /// </remarks>
        private static unsafe bool TryBackup(Terrain terrain, Int2 coord, out string error)
        {
            error = null;

            try
            {
                string path = BackupPath(terrain, coord);

                if (System.IO.File.Exists(path))
                    return true;

                var patch = terrain.GetPatch(ref coord);

                if (patch == null)
                {
                    error = $"'{terrain.Name}' 上第 ({coord.X},{coord.Y}) 格 patch 取不到，备份不了。";
                    return false;
                }

                int size = terrain.HeightmapSize;
                var data = patch.GetHeightmapData();

                if (data == null || size < 2)
                {
                    error = $"'{terrain.Name}' 第 ({coord.X},{coord.Y}) 格没有可读的高度场（HeightmapSize={size}）。";
                    return false;
                }

                var original = new float[size * size];

                for (int i = 0; i < original.Length; i++)
                    original[i] = data[i];

                System.IO.Directory.CreateDirectory(BackupFolder);

                // 先写长度再写数据：还原时不依赖任何外部配置就知道该读多少个 float。
                using (var stream = System.IO.File.Create(path))
                using (var writer = new System.IO.BinaryWriter(stream))
                {
                    writer.Write(original.Length);

                    for (int i = 0; i < original.Length; i++)
                        writer.Write(original[i]);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"备份第 ({coord.X},{coord.Y}) 格高度场失败：{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 用备份文件把地形还原成 PCG 没碰过的样子。
        /// </summary>
        /// <param name="owner">任意一个场景里的 actor（用来定位场景）。</param>
        /// <param name="restored">还原了几格；没有备份可还原时为 0。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>成功为 true。</returns>
        public static bool TryRestore(Actor owner, out int restored, out string error)
        {
            restored = 0;
            error = null;

            var scene = owner?.Scene;

            if (scene == null)
            {
                error = "没有场景，找不到要还原的地形。";
                return false;
            }

            try
            {
                if (!System.IO.Directory.Exists(BackupFolder))
                    return true;

                var roots = scene.GetChildren<Actor>();

                if (roots == null)
                    return true;

                for (int i = 0; i < roots.Length; i++)
                {
                    if (!(roots[i] is Terrain terrain) || terrain.ID == Guid.Empty)
                        continue;

                    var files = System.IO.Directory.GetFiles(BackupFolder, terrain.ID.ToString("N") + "_*.bin");

                    for (int f = 0; f < files.Length; f++)
                    {
                        if (!TryParseBackupName(files[f], out var coord) || !TryReadBackup(files[f], out var heights))
                            continue;

                        if (!terrain.HasPatch(ref coord))
                            continue;

                        if (terrain.SetupPatchHeightMap(ref coord, heights, null, false))
                        {
                            error = $"还原第 ({coord.X},{coord.Y}) 格时 SetupPatchHeightMap 报失败。";
                            return false;
                        }

                        restored++;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"还原地形失败：{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static bool TryParseBackupName(string path, out Int2 coord)
        {
            coord = new Int2();

            var parts = System.IO.Path.GetFileNameWithoutExtension(path).Split('_');

            if (parts.Length != 3 || !int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int z))
                return false;

            coord = new Int2(x, z);
            return true;
        }

        private static bool TryReadBackup(string path, out float[] heights)
        {
            heights = null;

            using (var stream = System.IO.File.OpenRead(path))
            using (var reader = new System.IO.BinaryReader(stream))
            {
                if (stream.Length < 4)
                    return false;

                int count = reader.ReadInt32();

                // 上限只是防一个被截断/被改坏的文件把内存吃光；正常一格是 (chunk*4+1)²。
                if (count <= 0 || count > 64 * 1024 * 1024 || stream.Length < 4 + count * 4L)
                    return false;

                heights = new float[count];

                for (int i = 0; i < count; i++)
                    heights[i] = reader.ReadSingle();
            }

            return true;
        }
    }
}
