using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NpcEditor
{
    /// <summary>
    /// 插件侧的草稿模型（与 <c>Horizon.NpcCli</c> 的 <c>NpcDraft</c> 同一族字段名）。
    /// <para>
    /// <b>为什么这里要再写一份 DTO 而不引用服务端程序集</b>：Flax 脚本域只解析引擎给的引用集，
    /// 拖进 Horizon.Model/EF 会把"插件能不能加载"绑到数据库驱动版本上；而字段名是跨脚本域的字面量协议，
    /// 由契约测试（<c>NpcEditorContractTests</c>）按 JSON 键逐字对账，不靠共享类型保证。
    /// </para>
    /// <para><b>全部用具名属性而不是公开字段</b>：System.Text.Json 默认不绑公开字段，
    /// 写成字段会得到"序列化成功但全是 null"的静默空草稿。</para>
    /// </summary>
    public sealed class NpcDraftModel
    {
        public NpcTemplateModel Template { get; set; } = new NpcTemplateModel();

        public List<NpcVendorRowModel> VendorRows { get; set; } = new List<NpcVendorRowModel>();
        public List<NpcTrainerRowModel> TrainerRows { get; set; } = new List<NpcTrainerRowModel>();
        public List<NpcGossipRowModel> GossipRows { get; set; } = new List<NpcGossipRowModel>();
        public List<NpcScheduleRowModel> ScheduleRows { get; set; } = new List<NpcScheduleRowModel>();
        public List<NpcQuestLinkModel> QuestLinks { get; set; } = new List<NpcQuestLinkModel>();
        public List<NpcSpawnPointModel> SpawnPoints { get; set; } = new List<NpcSpawnPointModel>();

        /// <summary>
        /// 巡逻/巡街路线。**必须收在草稿里**：编辑器"加载→保存"会把整份草稿序列化回去，
        /// 漏掉这一栏等于保存时把路线删掉（曾经如此：画好的巡街线保存后消失）。
        /// </summary>
        public List<NpcRouteModel> Routes { get; set; } = new List<NpcRouteModel>();

        /// <summary>
        /// 制作配方。与路线同理：**草稿里没有这一栏，编辑器保存一次就把配方内容丢掉**。
        /// 配方本体是全局表（多只 NPC 可以教同一张），这里只携带"本 NPC 相关的那些"。
        /// </summary>
        public List<NpcRecipeModel> Recipes { get; set; } = new List<NpcRecipeModel>();

        /// <summary>技能挂载（<c>Casts</c> 的数据源）；表不存在时为空。</summary>
        public List<NpcSkillMountModel> SkillMounts { get; set; } = new List<NpcSkillMountModel>();

        /// <summary>掉落绑定（编辑器"掉落"页提交的就是这一份）。</summary>
        public List<NpcLootBindingModel> LootBindings { get; set; } = new List<NpcLootBindingModel>();

        /// <summary>内容版本（乐观锁）。加载时从库带回，保存时原样回传。</summary>
        public int ContentVersion { get; set; }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public static NpcDraftModel FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<NpcDraftModel>(json, Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// 用界面上的那一份替换第 <paramref name="index"/> 个刷点，**其余刷点原样保留**；
        /// 越界（含 −1）表示追加。
        /// <para>
        /// <b>为什么不是"整表替换"</b>：一个 NPC 可以有多个刷点，而面板只显示第一条。
        /// 整表替换会让"只改一次刷新时间"顺手删掉另一个营地的刷点——保存成功、库里少了一条，
        /// 玩家看到的是"那边的人没了"，而编辑器里从来没见过那一条。
        /// </para>
        /// </summary>
        public void ReplaceSpawn(int index, NpcSpawnPointModel spawn)
        {
            SpawnPoints ??= new System.Collections.Generic.List<NpcSpawnPointModel>();
            if (index >= 0 && index < SpawnPoints.Count) SpawnPoints[index] = spawn;
            else SpawnPoints.Add(spawn);
        }

        /// <summary>
        /// 删除一条刷点（越界返回 false、不动数据）。
        /// <para><b>为什么只有刷点能删</b>：保存是"按 NPC 整组替换刷点"，删掉的这一行自然会从库里消失；
        /// 路线按 <c>PathId</c> 合并、配方在全局表里共享，删一条会牵动**引用它的其它 NPC**，
        /// 那不是"保存这一只"该顺手做的事（退役有专门的流程）。</para>
        /// </summary>
        public bool RemoveSpawn(int index)
        {
            SpawnPoints ??= new System.Collections.Generic.List<NpcSpawnPointModel>();
            if (index < 0 || index >= SpawnPoints.Count) return false;
            SpawnPoints.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// 按 <see cref="NpcRouteModel.PathId"/> 合并路线：已存在则替换那一条，0/找不到则追加。
        /// 其余路线不动——理由同 <see cref="ReplaceSpawn"/>（路线也是多条的）。
        /// </summary>
        public void UpsertRoute(NpcRouteModel route)
        {
            Routes ??= new System.Collections.Generic.List<NpcRouteModel>();
            if (route == null) return;

            if (route.PathId > 0)
            {
                for (var i = 0; i < Routes.Count; i++)
                {
                    if (Routes[i].PathId != route.PathId) continue;
                    Routes[i] = route;
                    return;
                }
            }

            Routes.Add(route);
        }

        /// <summary>
        /// 按 <see cref="NpcRecipeModel.RecipeId"/> 合并配方：已存在则替换，0/找不到则追加。
        /// 一只 NPC 可以教多张配方，面板只编辑第一条。
        /// </summary>
        public void UpsertRecipe(NpcRecipeModel recipe)
        {
            Recipes ??= new System.Collections.Generic.List<NpcRecipeModel>();
            if (recipe == null) return;

            if (recipe.RecipeId > 0)
            {
                for (var i = 0; i < Recipes.Count; i++)
                {
                    if (Recipes[i].RecipeId != recipe.RecipeId) continue;
                    Recipes[i] = recipe;
                    return;
                }
            }

            Recipes.Add(recipe);
        }

        /// <summary>
        /// 按**面板行号**合并路线：正在编辑的那一行必须就地替换，与它有没有业务号无关。
        /// <para>
        /// 旧走法只认 <see cref="NpcRouteModel.PathId"/>：新建的路线在落库前 PathId 还是 0，
        /// 于是每点一次"保存/预览/校验"（都会提交一次草稿）就多出一条一模一样的路线。
        /// </para>
        /// </summary>
        public void UpsertRouteAt(int index, NpcRouteModel route)
        {
            Routes ??= new System.Collections.Generic.List<NpcRouteModel>();
            if (route == null) return;

            if (index >= 0 && index < Routes.Count)
            {
                Routes[index] = route;
                return;
            }

            if (route.PathId > 0)
            {
                for (var i = 0; i < Routes.Count; i++)
                {
                    if (Routes[i].PathId != route.PathId) continue;
                    Routes[i] = route;
                    return;
                }
            }

            Routes.Add(route);
        }

        /// <summary>按面板行号合并配方；理由同 <see cref="UpsertRouteAt"/>（新配方 RecipeId 落库前为 0）。</summary>
        public void UpsertRecipeAt(int index, NpcRecipeModel recipe)
        {
            Recipes ??= new System.Collections.Generic.List<NpcRecipeModel>();
            if (recipe == null) return;

            if (index >= 0 && index < Recipes.Count)
            {
                Recipes[index] = recipe;
                return;
            }

            if (recipe.RecipeId > 0)
            {
                for (var i = 0; i < Recipes.Count; i++)
                {
                    if (Recipes[i].RecipeId != recipe.RecipeId) continue;
                    Recipes[i] = recipe;
                    return;
                }
            }

            Recipes.Add(recipe);
        }
    }

    public sealed class NpcTemplateModel
    {
        public int NpcId { get; set; }
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string ModelKey { get; set; } = "";
        public string PrefabGuid { get; set; } = "";
        public float Scale { get; set; } = 1f;
        public int Level { get; set; } = 1;
        public int LevelVariance { get; set; }
        public byte Reaction { get; set; }
        public uint ServiceFlags { get; set; }
        public byte AiTier { get; set; }
        public uint AiBitsForceOn { get; set; }
        public uint AiBitsForceOff { get; set; }
        public int AttackableOverride { get; set; } = -1;
        public float SpeedWalk { get; set; } = 1.6f;
        public float SpeedRun { get; set; } = 4f;
        public int AlertRadiusMeters { get; set; } = 20;
        public int GossipGroupId { get; set; }
        public byte Rank { get; set; }
        public int WuxingDomain { get; set; }
        public int KillCreditNpcId { get; set; }
        public int FactionGroup { get; set; }
    }

    public sealed class NpcVendorRowModel
    {
        public int NpcId { get; set; }
        public int Slot { get; set; }
        public int ItemId { get; set; }
        public long BuyPriceOverride { get; set; }
        public long SellPriceOverride { get; set; }
        public int StockMax { get; set; }
        public int RestockSeconds { get; set; }
        public int Category { get; set; }
    }

    public sealed class NpcTrainerRowModel
    {
        public int NpcId { get; set; }
        public int SkillId { get; set; }
        public byte ServiceKind { get; set; } = 1;
        public long CostGold { get; set; }
        public int CostItemId { get; set; }
        public int CostItemCount { get; set; }
        public int Seq { get; set; }
    }

    public sealed class NpcGossipRowModel
    {
        public int NpcId { get; set; }
        public int GroupId { get; set; }
        public int Seq { get; set; }
        public int TextId { get; set; }

        /// <summary>
        /// 台词正文（023 的 <c>text</c> 列；上限 <c>NpcStore.NpcGossipTextMaxLength</c> = 200）。
        /// <para>空串 = 没配，客户端按 <see cref="ActionKind"/> 合成标签。**保存时必须带回去**：
        /// 保存路径是"删光重插"，漏这一格就等于每次保存都把台词清空。</para>
        /// </summary>
        public string Text { get; set; } = "";

        public byte ActionKind { get; set; }
        public int ActionArg { get; set; }
        public int ReqMinLevel { get; set; }
        public int ReqMaxLevel { get; set; }
    }

    public sealed class NpcScheduleRowModel
    {
        public int NpcId { get; set; }
        public int Seq { get; set; }
        public int FromSeconds { get; set; }
        public int ToSeconds { get; set; }
        public int RouteId { get; set; }
        public byte Posture { get; set; }
        public int GossipGroupId { get; set; }
    }

    public sealed class NpcQuestLinkModel
    {
        public int NpcId { get; set; }
        public int QuestId { get; set; }
        public byte Role { get; set; } = 1;
        public int OrderSeq { get; set; }
    }

    public sealed class NpcSpawnPointModel
    {
        public int SpawnPointId { get; set; }
        public int ZoneId { get; set; }
        public long ShardId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Yaw { get; set; }
        public int NpcId { get; set; }
        public int LevelOverride { get; set; }
        public int MaxAlive { get; set; } = 1;
        public float RespawnSeconds { get; set; }
        public int RouteId { get; set; }
        public byte FacingMode { get; set; }
    }

    /// <summary>路线（与 CLI 的 <c>NpcRouteSnapshot</c> 同名字段）。</summary>
    public sealed class NpcRouteModel
    {
        /// <summary>路线编号；0 = 新建（保存时由 CLI 分配）。</summary>
        public int PathId { get; set; }
        public string Name { get; set; } = "";
        /// <summary>0 往返 1 循环 2 随机。</summary>
        public byte Mode { get; set; }
        public float PauseAtPointSeconds { get; set; }
        public bool UseNavMesh { get; set; } = true;
        public List<NpcRoutePointModel> Points { get; set; } = new List<NpcRoutePointModel>();
    }

    /// <summary>路线上的一个点（与 CLI 的 <c>NpcRoutePointSnapshot</c> 同名字段）。</summary>
    public sealed class NpcRoutePointModel
    {
        public int Seq { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float PauseSeconds { get; set; }
    }

    /// <summary>配方（与 CLI 的 <c>NpcRecipeSnapshotDto</c> 同名字段）。</summary>
    /// <summary>
    /// 技能挂载（施法）。字段与 CLI 的 <c>NpcSkillMountDto</c> / 024 表同名字段一一对应；
    /// 面板只编前六列，其余沿用载入值（保存一次就把没显示的列重置掉，是这类编辑器的老坑）。
    /// </summary>
    public sealed class NpcSkillMountModel
    {
        public int NpcId { get; set; }
        public int SkillId { get; set; }
        public int SlotSeq { get; set; }
        public byte SlotType { get; set; }
        public int ChanceBps { get; set; } = 10000;
        public int CastMs { get; set; }
        public int CooldownMs { get; set; } = 5000;
        public float MinRange { get; set; }
        public float MaxRange { get; set; } = 8f;
        public byte TargetSelect { get; set; }
        public int TargetCount { get; set; } = 1;
        public float AoeRadius { get; set; }
        public int SchoolMask { get; set; }
        public byte Interruptible { get; set; } = 1;
        public byte AllowMovement { get; set; }
        public int DamageScaleBps { get; set; } = 10000;
        public int StatusId { get; set; }
        public float StatusDurationSeconds { get; set; }
        public int StartEmote { get; set; }
        public int EmergencyHpPercent { get; set; } = 30;
    }

    /// <summary>
    /// 技能挂载的"提交"合并：面板只显示前 <paramref name="visibleRowCount"/> 行，而保存是**整组替换**
    /// （<c>NpcStore</c> 按 npc_id 先删光再重插）。所以看不见的行必须原样带回——
    /// 否则打开一只挂了 5 条技能的 NPC、只改了个名字再保存，第 5 条就被静默删掉了。
    /// <para>
    /// 反过来，**被清空技能列的行属于"作者要删"**：它的下标落在可见区内，因此不会被当成隐藏行带回。
    /// </para>
    /// </summary>
    public static class NpcSkillMountEditing
    {
        public static List<NpcSkillMountModel> MergeSkillMounts(
            IList<NpcSkillMountModel> existing, IList<NpcSkillMountModel> visible, int visibleRowCount)
        {
            var next = new List<NpcSkillMountModel>();

            if (visible != null)
            {
                foreach (var row in visible)
                {
                    if (row == null || row.SkillId <= 0) continue;
                    // 不去重：同一技能可以挂两个时机（战斗 + 残血），那是两条合法的挂载。
                    next.Add(row);
                }
            }

            if (existing != null)
            {
                // 只认为"面板没显示到的下标"才是隐藏行；可见区里被清空的行是作者主动删除。
                var seq = visibleRowCount;
                for (var i = visibleRowCount; i < existing.Count; i++)
                {
                    var row = existing[i];
                    if (row == null || row.SkillId <= 0) continue;
                    // 尾巴排在可见行之后：SlotSeq 是"平局时先放哪个"的稳定序，不能与可见行撞号。
                    row.SlotSeq = seq++;
                    next.Add(row);
                }
            }

            return next;
        }
    }

    /// <summary>
    /// 掉落的"提交"合并：面板只露前几件，保存却是整组替换（<c>NpcStore</c> 按 npc_id 删光重插）。
    /// 与技能挂载同一个坑：看不见的件必须原样带回，否则一条 5 件的掉落表保存一次就少一件。
    /// </summary>
    public static class NpcLootEditing
    {
        /// <summary>把面板没显示到的条目接在可见条目之后（<c>Seq</c> 顺延，隐藏列保持原值）。</summary>
        public static void AppendHiddenEntries(NpcLootBindingModel edited, NpcLootBindingModel previous, int visibleRowCount)
        {
            if (edited?.Entries == null || previous?.Entries == null) return;

            var seq = visibleRowCount;
            for (var i = visibleRowCount; i < previous.Entries.Count; i++)
            {
                var entry = previous.Entries[i];
                if (entry == null || entry.ItemId <= 0) continue;
                // 不去重：同一物品可以在两个不同掉落组里各掉一次，那是两条合法的条目。
                entry.Seq = seq++;
                edited.Entries.Add(entry);
            }
        }

        /// <summary>
        /// 多份绑定：面板只编第 0 份，其余原样带回（<c>NpcStore</c> 会按 npc_id 把所有绑定删掉重插）。
        /// 第 0 份被清空（无条目也没金币）时才整份消失——那是作者主动清掉。
        /// </summary>
        public static List<NpcLootBindingModel> MergeBindings(
            NpcLootBindingModel edited, IReadOnlyList<NpcLootBindingModel> previous)
        {
            var next = new List<NpcLootBindingModel>();
            if (edited != null && (edited.Entries.Count > 0 || edited.MoneyMax > 0)) next.Add(edited);

            if (previous != null)
            {
                for (var i = 1; i < previous.Count; i++)
                {
                    if (previous[i] != null) next.Add(previous[i]);
                }
            }

            return next;
        }
    }

    /// <summary>
    /// 通用"面板行数 &lt; 库行数"的提交合并：保存是按 npc_id 整组替换，看不见的行必须原样接回尾巴。
    /// <para>
    /// 可见区的行由面板逐格重建；可见区里被清空的行 = 作者主动删除，不会被带回。
    /// <paramref name="key"/> 只用于判"这一行有效"（返回 0 表示空行、丢弃），**不做去重**——
    /// 同一物品/同一技能/同一任务在两条不同规则里各出现一次是合法的（掉落组、施法时机、发布+交付），
    /// 按业务键去重会把它们静默吃掉。<paramref name="setSeq"/> 为空时保留原有序号（商人槽位是稳定键）。
    /// </para>
    /// </summary>
    public static class NpcRowListEditing
    {
        public static void AppendHidden<T>(
            List<T> edited, IList<T> existing, int visibleRowCount,
            Func<T, long> key, Action<T, int> setSeq)
            where T : class
        {
            if (edited == null || existing == null || key == null) return;

            var seq = visibleRowCount;
            for (var i = visibleRowCount; i < existing.Count; i++)
            {
                var row = existing[i];
                if (row == null) continue;
                if (key(row) == 0L) continue;
                setSeq?.Invoke(row, seq++);
                edited.Add(row);
            }
        }
    }

    public sealed class NpcRecipeModel
    {
        public int RecipeId { get; set; }
        public string Name { get; set; } = "";
        public int ProductItemId { get; set; }
        public long CostGold { get; set; }
        public int StationNpcId { get; set; }
        public int SkillReq { get; set; }
        public List<NpcMaterialModel> Materials { get; set; } = new List<NpcMaterialModel>();

        /// <summary>成功档技能差上限，由易到难（橙黄绿灰）。</summary>
        public List<int> QualityGapCaps { get; set; } = new List<int>();

        public List<int> QualityChanceBps { get; set; } = new List<int>();
        public byte LearnSourceKind { get; set; } = 1;
    }

    /// <summary>材料需求（与 CLI 的 <c>NpcMaterialNeedDto</c> 同名字段）。</summary>
    public sealed class NpcMaterialModel
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    /// <summary>掉落绑定（与 CLI 的 <c>MonsterLootBindingSnapshotDto</c> 同名字段）。</summary>
    public sealed class NpcLootBindingModel    {
        public int MonsterTemplateId { get; set; }
        public int LootTableId { get; set; } = 1;
        public int RollChanceBps { get; set; } = 10000;
        public int MinCount { get; set; } = 1;
        public int MaxCount { get; set; } = 1;
        public int ConditionKind { get; set; }
        public int QuestId { get; set; }
        public int MoneyMin { get; set; }
        public int MoneyMax { get; set; }
        public List<NpcLootEntryModel> Entries { get; set; } = new List<NpcLootEntryModel>();
    }

    /// <summary>逐件掉落（基数万分比 = 概率 × 10000）。</summary>
    public sealed class NpcLootEntryModel
    {
        public int ItemId { get; set; }
        public int ChanceBps { get; set; } = 1000;
        public int CountMin { get; set; } = 1;
        public int CountMax { get; set; } = 1;
        public int Seq { get; set; }
        public int GroupId { get; set; }
        public int ConditionKind { get; set; } = -1;
        public int QuestId { get; set; }
    }

    /// <summary>
    /// 行选择（刷点/路线/配方）的索引规则。**count 本身是合法值**——那一档表示"新增一行"，
    /// 钳到 count-1 会让"新建"从界面上消失；负数一律回 0。
    /// </summary>
    public static class NpcRowIndex
    {
        public static int Clamp(int index, int count)
        {
            if (count < 0) count = 0;
            if (index < 0) return 0;
            return index > count ? count : index;
        }

        /// <summary>上/下一行：先夹当前，再挪，再夹。</summary>
        public static int Step(int index, int delta, int count)
            => Clamp(Clamp(index, count) + delta, count);
    }

    /// <summary>
    /// 逐件掉落条件的下拉映射（值域与库列 <c>condition_kind</c> 同号：<b>-1 = 继承绑定</b>，0=无条件，
    /// 1=任务持有者，2=组队，3=稀有度门槛）。索引 0 是"继承"——默认值必须是继承而不是"无条件"，
    /// 否则旧数据里没配过的行会集体变成人人可拿（与实体列默认值 -1 是同一条纪律）。
    /// </summary>
    public static class NpcLootConditionUi
    {
        public static readonly string[] Names =
        {
            "继承（跟绑定）", "无条件", "任务持有者", "组队", "稀有度门槛",
        };

        /// <summary>下拉索引 → 库值。越界一律回继承（不猜"无条件"）。</summary>
        public static int ValueAt(int index) => index switch
        {
            1 => 0,
            2 => 1,
            3 => 2,
            4 => 3,
            _ => -1,
        };

        /// <summary>库值 → 下拉索引。未知值同样回"继承"。</summary>
        public static int IndexOf(int value) => value switch
        {
            0 => 1,
            1 => 2,
            2 => 3,
            3 => 4,
            _ => 0,
        };

        /// <summary>
        /// 绑定级下拉：**没有"继承"这一档**（绑定就是继承链的终点），索引即库值 0..3；
        /// 越界一律回 0（无条件），与实体列默认值同侧。
        /// </summary>
        public static readonly string[] BindingNames = { "无条件", "任务持有者", "组队", "稀有度门槛" };

        public static int BindingValueAt(int index) => index is >= 0 and <= 3 ? index : 0;

        public static int BindingIndexOf(int value) => value is >= 0 and <= 3 ? value : 0;
    }

    /// <summary>服务位的显示名与位值（与 <c>NpcServiceCore</c> 逐位对齐；编辑器只做"勾选 → 加位"）。</summary>
    /// <summary>
    /// AI 能力位（与 <c>NpcAiBits</c> 同值）。位图是"档位 + 强制开/强制关"展开后的结果，
    /// 编辑器里这两列覆盖必须能单独勾——只选档位表达不了"这一只例外"。
    /// </summary>
    public static class NpcAiBitsUi
    {
        public const uint FacePlayer = 1u << 0;
        public const uint SpeaksGossip = 1u << 1;
        public const uint Wanders = 1u << 2;
        public const uint FollowsPath = 1u << 3;
        public const uint DefendsSelf = 1u << 4;
        public const uint Pursues = 1u << 5;
        public const uint CallsReinforcement = 1u << 6;
        public const uint Casts = 1u << 7;
        public const uint FleesAtLowHealth = 1u << 8;

        public static readonly string[] Names =
        {
            "面向玩家", "讲台词", "踱步", "走路线", "会还手", "会追击", "呼叫增援", "会施法", "残血逃跑",
        };

        public static readonly uint[] Values =
        {
            FacePlayer, SpeaksGossip, Wanders, FollowsPath, DefendsSelf, Pursues, CallsReinforcement, Casts, FleesAtLowHealth,
        };
    }

    /// <summary>
    /// 一行 AI 覆盖（强制开 / 强制关）的互斥规则：**同一位只能有一边为"是"**。
    /// <para>
    /// 内核把"同位既开又关"当配置缺陷报 <c>ai_force_conflict</c>（强制关赢、不静默选一个），
    /// 所以编辑器在输入侧就不该让它产生。规则放模型侧是为了能被单测钉住——
    /// 窗口是 Flax 类型，测试工程只链接本文件。
    /// </para>
    /// </summary>
    public static class NpcAiOverrideEditing
    {
        /// <summary>
        /// 某一边被改过之后两列应有的值。<b>只有"改成是"才清另一边</b>；
        /// 改成"否"不动另一列（否则清空一侧会顺手把另一侧点亮）。
        /// </summary>
        /// <param name="onSideChanged">true = 刚动的是"强制开"列。</param>
        public static (bool On, bool Off) Exclusive(bool on, bool off, bool onSideChanged)
        {
            if (onSideChanged) return on ? (true, false) : (false, off);
            return off ? (false, true) : (on, false);
        }
    }

    public static class NpcServiceBitsUi
    {
        public const uint Gossip = 1u << 0;
        public const uint QuestGiver = 1u << 1;
        public const uint QuestTurnIn = 1u << 2;
        public const uint Vendor = 1u << 3;
        public const uint Trainer = 1u << 4;
        public const uint CraftTrainer = 1u << 5;
        public const uint CraftStation = 1u << 6;

        public static readonly string[] Names = { "对话", "发布任务", "交付任务", "商人", "技能训练", "配方教学", "制作设施" };
        public static readonly uint[] Values = { Gossip, QuestGiver, QuestTurnIn, Vendor, Trainer, CraftTrainer, CraftStation };

        public static string Describe(uint flags)
        {
            var parts = new List<string>();
            for (var i = 0; i < Values.Length; i++)
            {
                if ((flags & Values[i]) != 0) parts.Add(Names[i]);
            }

            return parts.Count == 0 ? "（无服务）" : string.Join("、", parts);
        }
    }
}
