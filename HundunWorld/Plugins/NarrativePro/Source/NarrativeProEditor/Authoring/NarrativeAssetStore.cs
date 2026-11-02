using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NarrativePro.Core;
using NarrativePro.Data;
using NarrativePro.Tales;
using NarrativePro.Tales.Dialogue;
using NarrativePro.Tales.Quest;

namespace NarrativePro.EditorTools
{
    /// <summary>逻辑条目种类（与 <see cref="NarrativeLogicFactory"/> 的三张注册表一一对应）。</summary>
    public enum NarrativeLogicKind
    {
        Task,
        Event,
        Condition
    }

    /// <summary>对话节点之间的连线种类（对应 DialogueSM 的 NPCReplies / PlayerReplies 邻接表）。</summary>
    public enum DialogueLinkKind
    {
        NpcToNpc,
        NpcToPlayer,
        PlayerToNpc
    }

    /// <summary>
    /// 叙事资产（Quest / Dialogue JSON）的编辑期读写与校验层。
    /// <para>
    /// 这里的写入用的就是运行时 <see cref="QuestFactory"/> / <see cref="DialogueFactory"/>
    /// 反序列化的同一组 DTO（<see cref="QuestData"/> / <see cref="DialogueData"/>），
    /// 目录取自 <c>NarrativeProPlugin.Instance.NarrativeSettings</c>（与运行时 TalesComponent 同一口径），
    /// 相对路径经 <see cref="NarrativeDataPaths"/> 归一到工程根，落点即 <c>Content/NarrativePro/{Quests,Dialogues}/</c>。
    /// 因此编辑端写出的文件运行时一定读得懂——校验函数直接调用运行时加载器本体。
    /// </para>
    /// <para>
    /// 所有增删改返回 <c>string</c>：null 表示成功，否则是可直接显示给作者的中文错误。
    /// 事件/条件/任务的类型名一律先过 <see cref="NarrativeLogicFactory"/> 注册表，
    /// 未注册即拒绝写入，作者无法打出一个运行时查不到的 key。
    /// </para>
    /// </summary>
    public static class NarrativeAssetStore
    {
        public const string QuestsRelative = "Content/NarrativePro/Quests";
        public const string DialoguesRelative = "Content/NarrativePro/Dialogues";

        // 读取用与运行时两个 Factory 完全相同的选项目：大小写不敏感、允许注释与尾逗号，
        // 这样手改过的既有资产在编辑器里打开后不会丢字段。
        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        // 写出用默认枚举数值序列化：运行时的 QuestData/DialogueData 绑定没有挂 JsonStringEnumConverter，
        // 写 "stateType": "Success" 会让运行时装载抛异常，写 1 才安全（与现有 Q001 资产口径一致）。
        // null 一律不落盘：TaskData.RequiredQuantity 缺键时运行时才不会被 null 覆盖掉任务类自己的默认值。
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>与运行时同口径的任务目录（绝对路径）。</summary>
        public static string QuestsDirectory => NarrativeDataPaths.Resolve(QuestsRelativePath);

        /// <summary>与运行时同口径的对话目录（绝对路径）。</summary>
        public static string DialoguesDirectory => NarrativeDataPaths.Resolve(DialoguesRelativePath);

        private static string QuestsRelativePath
        {
            get
            {
                string dir = NarrativePro.NarrativeProPlugin.Instance?.NarrativeSettings?.DefaultQuestDirectory;
                return string.IsNullOrEmpty(dir) ? QuestsRelative : dir;
            }
        }

        private static string DialoguesRelativePath
        {
            get
            {
                string dir = NarrativePro.NarrativeProPlugin.Instance?.NarrativeSettings?.DefaultDialogueDirectory;
                return string.IsNullOrEmpty(dir) ? DialoguesRelative : dir;
            }
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(QuestsDirectory);
            Directory.CreateDirectory(DialoguesDirectory);
        }

        /// <summary>目录下的 *.json（绝对路径，按文件名排序）。目录不存在时返回空表。</summary>
        public static List<string> ListJsonFiles(string directory)
        {
            var result = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return result;
                result.AddRange(Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly));
                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[NarrativePro] 列举叙事资产失败 {directory}: {ex.Message}");
            }
            return result;
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Untitled";
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.Trim())
            {
                bool invalid = char.IsWhiteSpace(c) || Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0;
                sb.Append(invalid ? '_' : c);
            }
            string sanitized = sb.ToString().Trim('_');
            return sanitized.Length == 0 ? "Untitled" : sanitized;
        }

        #region 模板与 IO

        /// <summary>新建一份结构上保证能通过 <see cref="QuestFactory.ValidateQuest"/> 的任务模板并落盘。</summary>
        public static QuestData CreateQuestTemplate(string questId, out string path)
        {
            EnsureDirectories();
            string id = SanitizeFileName(questId);

            var data = new QuestData
            {
                QuestId = id,
                QuestName = "新任务",
                QuestDescription = "",
                QuestDialogueClass = "",
                StartStateId = "start"
            };
            data.States.Add(new QuestStateData
            {
                Id = "start",
                Description = "接受委托",
                StateType = EStateNodeType.Regular,
                Position = new NodePositionData { X = 0f, Y = 0f }
            });
            data.States.Add(new QuestStateData
            {
                Id = "success",
                Description = "任务完成",
                StateType = EStateNodeType.Success,
                Position = new NodePositionData { X = 400f, Y = -120f }
            });
            data.Branches.Add(new QuestBranchData
            {
                Id = "branch_main",
                Description = "完成委托",
                FromStateId = "start",
                ToStateId = "success",
                Hidden = false,
                Position = new NodePositionData { X = 200f, Y = 0f },
                Tasks = new List<TaskData>()
            });

            path = Path.Combine(QuestsDirectory, id + ".json");
            WriteQuest(path, data);
            return data;
        }

        /// <summary>新建一份带根节点（<see cref="DialogueFactory.ValidateDialogue"/> 的硬要求）的对话模板并落盘。</summary>
        public static DialogueData CreateDialogueTemplate(string dialogueId, out string path)
        {
            EnsureDirectories();
            string id = SanitizeFileName(dialogueId);

            var data = new DialogueData
            {
                DialogueId = id,
                Speakers = new List<SpeakerData>
                {
                    new SpeakerData { SpeakerId = "npc", DisplayName = "NPC", IsPlayer = false, Tags = new List<string>() },
                    new SpeakerData { SpeakerId = "player", DisplayName = "玩家", IsPlayer = true, Tags = new List<string>() }
                },
                Config = new DialogueConfigData
                {
                    EndDialogueDist = 1200f,
                    ShowCinematicBars = false,
                    Unskippable = false,
                    FreeMovement = true,
                    CanBeExited = true,
                    Priority = 0
                },
                NpcReplies = new List<NpcReplyData>
                {
                    new NpcReplyData
                    {
                        Id = "root",
                        SpeakerId = "npc",
                        IsRoot = true,
                        IsSkippable = true,
                        Line = new DialogueLineData { Text = "……", Duration = ELineDuration.Default },
                        NpcReplies = new List<string>(),
                        PlayerReplies = new List<string>()
                    }
                },
                PlayerReplies = new List<PlayerReplyData>()
            };

            path = Path.Combine(DialoguesDirectory, id + ".json");
            WriteDialogue(path, data);
            return data;
        }

        public static QuestData LoadQuestData(string path)
        {
            string resolved = NarrativeDataPaths.Resolve(path);
            return JsonSerializer.Deserialize<QuestData>(File.ReadAllText(resolved), ReadOptions);
        }

        public static DialogueData LoadDialogueData(string path)
        {
            string resolved = NarrativeDataPaths.Resolve(path);
            return JsonSerializer.Deserialize<DialogueData>(File.ReadAllText(resolved), ReadOptions);
        }

        public static void WriteQuest(string path, QuestData data)
        {
            string resolved = NarrativeDataPaths.Resolve(path);
            string directory = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(resolved, JsonSerializer.Serialize(data, WriteOptions));
        }

        public static void WriteDialogue(string path, DialogueData data)
        {
            string resolved = NarrativeDataPaths.Resolve(path);
            string directory = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(resolved, JsonSerializer.Serialize(data, WriteOptions));
        }

        public static string QuestAssetName(string path) => Path.GetFileNameWithoutExtension(path);
        public static string DialogueAssetName(string path) => Path.GetFileNameWithoutExtension(path);

        /// <summary>把任务 id 对应的文件路径解析出来（运行时按文件名索引资产，见 NarrativeDataLoader.LoadQuest 的缓存键）。</summary>
        public static string QuestPathFor(string assetName) => Path.Combine(QuestsDirectory, SanitizeFileName(assetName) + ".json");
        public static string DialoguePathFor(string assetName) => Path.Combine(DialoguesDirectory, SanitizeFileName(assetName) + ".json");

        // --- 资产文件操作（重命名 / 复制 / 删除）---

        /// <summary>
        /// 重命名任务资产：文件名与内部 QuestId 一起改（运行时按 id 索引，两边不同步迟早出事）。
        /// 先写新文件、后删旧文件，中途失败不会把资产弄丢。
        /// </summary>
        public static string RenameQuestAsset(string path, string newName, out string newPath)
        {
            newPath = null;
            string error = CheckRenameTarget(path, newName, out newPath);
            if (error != null) return error;

            var data = LoadQuestData(path);
            if (data == null) return "读取任务资产失败：" + path;
            data.QuestId = Path.GetFileNameWithoutExtension(newPath);
            WriteQuest(newPath, data);
            File.Delete(NarrativeDataPaths.Resolve(path));
            return null;
        }

        /// <summary>重命名对话资产（口径同上，改 DialogueId）。</summary>
        public static string RenameDialogueAsset(string path, string newName, out string newPath)
        {
            newPath = null;
            string error = CheckRenameTarget(path, newName, out newPath);
            if (error != null) return error;

            var data = LoadDialogueData(path);
            if (data == null) return "读取对话资产失败：" + path;
            data.DialogueId = Path.GetFileNameWithoutExtension(newPath);
            WriteDialogue(newPath, data);
            File.Delete(NarrativeDataPaths.Resolve(path));
            return null;
        }

        /// <summary>复制任务资产：内容整份拷走，QuestId 改成新名。</summary>
        public static string DuplicateQuestAsset(string path, string newName, out string newPath)
        {
            newPath = null;
            string error = CheckRenameTarget(path, newName, out newPath);
            if (error != null) return error;

            var data = LoadQuestData(path);
            if (data == null) return "读取任务资产失败：" + path;
            data.QuestId = Path.GetFileNameWithoutExtension(newPath);
            WriteQuest(newPath, data);
            return null;
        }

        /// <summary>复制对话资产（口径同上，改 DialogueId）。</summary>
        public static string DuplicateDialogueAsset(string path, string newName, out string newPath)
        {
            newPath = null;
            string error = CheckRenameTarget(path, newName, out newPath);
            if (error != null) return error;

            var data = LoadDialogueData(path);
            if (data == null) return "读取对话资产失败：" + path;
            data.DialogueId = Path.GetFileNameWithoutExtension(newPath);
            WriteDialogue(newPath, data);
            return null;
        }

        /// <summary>删除资产文件。调用方负责让用户二次确认。</summary>
        public static string DeleteAssetFile(string path)
        {
            string resolved = NarrativeDataPaths.Resolve(path);
            if (!File.Exists(resolved)) return "资产文件不存在：" + resolved;
            File.Delete(resolved);
            return null;
        }

        // --- 对话镜头（Content/NarrativePro/Shots 里的模板，按名引用）---

        /// <summary>镜头模板目录（工程相对，经 NarrativeDataPaths 归一）。</summary>
        public static string ShotDirectory => NarrativeDataPaths.Resolve("Content/NarrativePro/Shots");

        /// <summary>
        /// 读出所有可用镜头：显示名 + 真名（写进资产的 shotName）。目录只在编辑器里用，缺失时返回空表。
        /// 显示名优先 friendlyShotName，和真名不同时并排显示成 "Medium (Shot_Medium)"。
        /// </summary>
        public static List<(string Display, string Value)> ShotChoices(string directory = null)
        {
            var result = new List<(string Display, string Value)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in ListJsonFiles(directory ?? ShotDirectory))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;

                    string shotName = root.TryGetProperty("shotName", out var nameProperty) ? (nameProperty.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(shotName)) shotName = Path.GetFileNameWithoutExtension(file);
                    if (!seen.Add(shotName)) continue;

                    string friendly = root.TryGetProperty("friendlyShotName", out var friendlyProperty) ? (friendlyProperty.GetString() ?? "") : "";
                    string display = string.IsNullOrWhiteSpace(friendly) || friendly == shotName
                        ? shotName
                        : friendly + " (" + shotName + ")";
                    result.Add((display, shotName));
                }
                catch
                {
                    // 坏镜头 JSON 由导演/引擎侧单独告警，这里跳过
                }
            }

            result.Sort((left, right) => string.Compare(left.Display, right.Display, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>镜头真名清单（含 aliases），审计"这个镜头名认得吗"用。</summary>
        public static HashSet<string> KnownShotNames(string directory = null)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in ListJsonFiles(directory ?? ShotDirectory))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;

                    if (root.TryGetProperty("shotName", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String)
                        names.Add(nameProperty.GetString() ?? "");
                    names.Add(Path.GetFileNameWithoutExtension(file));

                    if (root.TryGetProperty("aliases", out var aliasProperty) && aliasProperty.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var alias in aliasProperty.EnumerateArray())
                        {
                            if (alias.ValueKind == JsonValueKind.String) names.Add(alias.GetString() ?? "");
                        }
                    }
                }
                catch
                {
                    // 同上
                }
            }

            names.Remove("");
            return names;
        }

        /// <summary>给一条对话行设置镜头（空串＝清除，回落说话者默认镜头）。</summary>
        public static string SetDialogueLineShot(DialogueData data, string nodeId, string shotName)
        {
            if (data == null) return "当前没有打开的对话";

            var npc = FindNpcReply(data, nodeId);
            var player = npc == null ? FindPlayerReply(data, nodeId) : null;
            if (npc == null && player == null) return $"找不到对话节点 '{nodeId}'";

            string trimmed = (shotName ?? "").Trim();
            if (npc != null)
            {
                npc.Line ??= new DialogueLineData();
                npc.Line.ShotName = trimmed;
            }
            else
            {
                player.Line ??= new DialogueLineData();
                player.Line.ShotName = trimmed;
            }
            return null;
        }

        /// <summary>设置说话者的默认镜头与（玩家说话者）选择回复镜头；空串＝清除。</summary>
        public static string SetDialogueSpeakerShots(DialogueData data, string speakerId, string defaultShot, string selectingShot)
        {
            if (data == null) return "当前没有打开的对话";

            var speaker = FindSpeaker(data, speakerId);
            if (speaker == null) return $"找不到说话者 '{speakerId}'";

            speaker.DefaultShotName = (defaultShot ?? "").Trim();
            speaker.SelectingReplyShotName = (selectingShot ?? "").Trim();
            return null;
        }

        // --- 对话行内 CG（过场视频）---

        private static List<string> _movieCache;
        private static DateTime _movieCacheTime;

        /// <summary>
        /// 工程 Content 下的视频清单（mp4/webm/ogv/mov），返回工程相对路径（/ 分隔，运行时 PlayVideo 认这个）。
        /// 目录树可能很大，缓存 5 分钟；传 directory 时按该目录现扫（自检用）。
        /// </summary>
        public static List<string> MovieChoices(string directory = null)
        {
            if (string.IsNullOrEmpty(directory) && _movieCache != null &&
                (DateTime.UtcNow - _movieCacheTime).TotalMinutes < 5)
            {
                return _movieCache;
            }

            var result = new List<string>();
            try
            {
                string root = directory ?? NarrativeDataPaths.Resolve("Content");
                if (!Directory.Exists(root)) return result;

                foreach (var file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension != ".mp4" && extension != ".webm" && extension != ".ogv" && extension != ".mov") continue;

                    if (string.IsNullOrEmpty(directory))
                    {
                        string relative = Path.GetRelativePath(NarrativeDataPaths.ProjectDirectory, file);
                        result.Add(relative.Replace('\\', '/'));
                    }
                    else
                    {
                        result.Add(file);
                    }
                }
            }
            catch
            {
                // 目录读不到就给空表，界面显示"（不播）"
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(directory))
            {
                _movieCache = result;
                _movieCacheTime = DateTime.UtcNow;
            }
            return result;
        }

        /// <summary>给一条对话行挂 CG（空串＝不播）。waitForCutscene＝等播完再收行。</summary>
        public static string SetDialogueLineCutscene(DialogueData data, string nodeId, string cutscenePath, bool waitForCutscene)
        {
            if (data == null) return "当前没有打开的对话";

            var npc = FindNpcReply(data, nodeId);
            var player = npc == null ? FindPlayerReply(data, nodeId) : null;
            if (npc == null && player == null) return $"找不到对话节点 '{nodeId}'";

            string trimmed = (cutscenePath ?? "").Trim();
            if (npc != null)
            {
                npc.Line ??= new DialogueLineData();
                npc.Line.CutscenePath = trimmed;
                npc.Line.WaitForCutscene = trimmed.Length > 0 && waitForCutscene;
            }
            else
            {
                player.Line ??= new DialogueLineData();
                player.Line.CutscenePath = trimmed;
                player.Line.WaitForCutscene = trimmed.Length > 0 && waitForCutscene;
            }
            return null;
        }

        // --- 全文检索与可读大纲 ---

        /// <summary>一次全文命中：落在哪个文件、哪类节点、命中片段。KindTag 用字符串（state/branch/npc/player/speaker），避免公开类型暴露内部枚举。</summary>
        public sealed class NarrativeContentHit
        {
            public string Path;
            public bool IsQuest;
            public string KindTag;
            public string NodeId;
            public string Snippet;
        }

        /// <summary>
        /// 在单个资产里全文搜（id / 描述 / 台词 / 选项文本 / 说话者名），大小写不敏感。
        /// 查询少于 2 个字符直接返回空：单字符会把几乎所有资产捞出来，搜索框每敲一下都要扫盘。
        /// </summary>
        public static List<NarrativeContentHit> SearchContent(string path, bool isQuest, string query, int limit)
        {
            var hits = new List<NarrativeContentHit>();
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2 || limit <= 0) return hits;
            string q = query.Trim();

            try
            {
                if (isQuest)
                {
                    var data = LoadQuestData(path);
                    if (data == null) return hits;

                    foreach (var state in data.States ?? new List<QuestStateData>())
                    {
                        TryAddHit(hits, path, true, "state", state?.Id, state?.Id, q, limit);
                        TryAddHit(hits, path, true, "state", state?.Id, state?.Description, q, limit);
                    }
                    foreach (var branch in data.Branches ?? new List<QuestBranchData>())
                    {
                        TryAddHit(hits, path, true, "branch", branch?.Id, branch?.Id, q, limit);
                        TryAddHit(hits, path, true, "branch", branch?.Id, branch?.Description, q, limit);
                    }
                }
                else
                {
                    var data = LoadDialogueData(path);
                    if (data == null) return hits;

                    foreach (var speaker in data.Speakers ?? new List<SpeakerData>())
                    {
                        TryAddHit(hits, path, false, "speaker", speaker?.SpeakerId, speaker?.DisplayName, q, limit);
                    }
                    foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
                    {
                        TryAddHit(hits, path, false, "npc", npc?.Id, npc?.Id, q, limit);
                        TryAddHit(hits, path, false, "npc", npc?.Id, npc?.Line?.Text, q, limit);
                    }
                    foreach (var player in data.PlayerReplies ?? new List<PlayerReplyData>())
                    {
                        TryAddHit(hits, path, false, "player", player?.Id, player?.Id, q, limit);
                        TryAddHit(hits, path, false, "player", player?.Id, player?.OptionText, q, limit);
                        TryAddHit(hits, path, false, "player", player?.Id, player?.Line?.Text, q, limit);
                    }
                }
            }
            catch
            {
                // 坏资产不该把搜索框弄崩；校验面板会另行报它的问题
            }

            return hits;
        }

        /// <summary>跨所有任务/对话资产的全文搜（浏览器搜索框在名字没匹配上时用它兜底）。</summary>
        public static List<NarrativeContentHit> SearchAllContent(string query, int perFileLimit)
        {
            var hits = new List<NarrativeContentHit>();
            foreach (var file in ListJsonFiles(QuestsDirectory)) hits.AddRange(SearchContent(file, true, query, perFileLimit));
            foreach (var file in ListJsonFiles(DialoguesDirectory)) hits.AddRange(SearchContent(file, false, query, perFileLimit));
            return hits;
        }

        private static void TryAddHit(List<NarrativeContentHit> hits, string path, bool isQuest, string kindTag, string nodeId, string text, string query, int limit)
        {
            if (hits.Count >= limit || string.IsNullOrEmpty(text) || string.IsNullOrEmpty(nodeId)) return;

            int at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return;

            hits.Add(new NarrativeContentHit
            {
                Path = path,
                IsQuest = isQuest,
                KindTag = kindTag,
                NodeId = nodeId,
                Snippet = SnippetAround(text, at, query.Length)
            });
        }

        private static string SnippetAround(string text, int at, int length)
        {
            int start = Math.Max(0, at - 12);
            int end = Math.Min(text.Length, at + length + 28);
            string piece = text.Substring(start, end - start).Replace('\n', ' ').Replace('\r', ' ');
            return (start > 0 ? "…" : "") + piece + (end < text.Length ? "…" : "");
        }

        /// <summary>任务的可读大纲：从起始状态沿分支 BFS，缩进表示层级；不可达状态排在最后单列。</summary>
        public static string DescribeQuestOutline(QuestData data)
        {
            if (data == null) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("任务 " + (data.QuestId ?? "") + " · 起始 " + (string.IsNullOrEmpty(data.StartStateId) ? "（未设置）" : data.StartStateId));

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<QuestStateData>();
            var start = FindState(data, data.StartStateId);
            if (start != null)
            {
                queue.Enqueue(start);
                visited.Add(start.Id);
            }

            int step = 0;
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                step++;
                sb.AppendLine(step + ". " + state.Id + "（" + state.StateType + "）" + (state.Description ?? ""));

                foreach (var branch in data.Branches ?? new List<QuestBranchData>())
                {
                    if (!string.Equals(branch.FromStateId, state.Id, StringComparison.OrdinalIgnoreCase)) continue;
                    sb.AppendLine("    - " + branch.Id + " → " + branch.ToStateId +
                                  (branch.Hidden ? "（隐藏）" : "") + DescribeTasksBrief(branch));

                    var next = FindState(data, branch.ToStateId);
                    if (next != null && visited.Add(next.Id)) queue.Enqueue(next);
                }
            }

            foreach (var state in data.States ?? new List<QuestStateData>())
            {
                if (state?.Id != null && !visited.Contains(state.Id)) sb.AppendLine("（不可达）" + state.Id);
            }

            return sb.ToString();
        }

        /// <summary>对话的可读大纲：从根节点沿连线 BFS；玩家选项与后继节点缩进列出。</summary>
        public static string DescribeDialogueOutline(DialogueData data)
        {
            if (data == null) return "";

            string rootId = "";
            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
            {
                if (npc != null && npc.IsRoot) { rootId = npc.Id; break; }
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("对话 " + (data.DialogueId ?? "") + " · 根 " + (string.IsNullOrEmpty(rootId) ? "（未设置）" : rootId));

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            if (!string.IsNullOrEmpty(rootId))
            {
                queue.Enqueue(rootId);
                visited.Add(rootId);
            }

            int step = 0;
            while (queue.Count > 0)
            {
                var npc = FindNpcReply(data, queue.Dequeue());
                if (npc == null) continue;

                step++;
                var speaker = FindSpeaker(data, npc.SpeakerId);
                sb.AppendLine(step + ". [" + (speaker?.DisplayName ?? npc.SpeakerId ?? "?") + "] " + (npc.Line?.Text ?? ""));

                foreach (var playerId in npc.PlayerReplies ?? new List<string>())
                {
                    var player = FindPlayerReply(data, playerId);
                    if (player == null) continue;
                    sb.AppendLine("    - 玩家：" + (player.OptionText ?? playerId));
                    foreach (var nextId in player.NpcReplies ?? new List<string>())
                    {
                        sb.AppendLine("        → " + nextId);
                        if (visited.Add(nextId)) queue.Enqueue(nextId);
                    }
                }

                foreach (var nextId in npc.NpcReplies ?? new List<string>())
                {
                    sb.AppendLine("    → " + nextId);
                    if (visited.Add(nextId)) queue.Enqueue(nextId);
                }
            }

            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
            {
                if (npc?.Id != null && !visited.Contains(npc.Id)) sb.AppendLine("（不可达）" + npc.Id);
            }

            return sb.ToString();
        }

        private static string DescribeTasksBrief(QuestBranchData branch)
        {
            if (branch?.Tasks == null || branch.Tasks.Count == 0) return "";

            var parts = new List<string>();
            foreach (var task in branch.Tasks)
            {
                parts.Add(task.Type + (task.RequiredQuantity.HasValue ? " x" + task.RequiredQuantity.Value : ""));
            }
            return " · 任务: " + string.Join(", ", parts);
        }

        // --- 引用完整性审计（运行时装载器不管的那些"作者错误"）---

        /// <summary>一条审计发现。IsError=false 的是警告（流程能跑，但大概率是作者漏了）。</summary>
        public sealed class NarrativeAuditIssue
        {
            public string NodeId = "";
            public bool IsError = true;
            public string Message = "";
        }

        /// <summary>
        /// 任务侧审计：起始状态、分支端点、关联对话文件是否存在（错误级）；
        /// 不可达状态、没有出口的常规状态（警告级）。运行时装载器只保证"读得懂"，这些它不管。
        /// </summary>
        public static List<NarrativeAuditIssue> AuditQuest(QuestData data)
        {
            var issues = new List<NarrativeAuditIssue>();
            if (data == null) return issues;

            if (string.IsNullOrWhiteSpace(data.StartStateId))
            {
                issues.Add(new NarrativeAuditIssue { Message = "任务没有起始状态，运行时不知道从哪开始。" });
            }
            else if (FindState(data, data.StartStateId) == null)
            {
                issues.Add(new NarrativeAuditIssue { Message = $"起始状态 '{data.StartStateId}' 不存在。" });
            }

            foreach (var branch in data.Branches ?? new List<QuestBranchData>())
            {
                if (branch == null) continue;
                if (FindState(data, branch.FromStateId) == null)
                    issues.Add(new NarrativeAuditIssue { NodeId = branch.Id, Message = $"分支 '{branch.Id}' 的起点状态 '{branch.FromStateId}' 不存在。" });
                if (FindState(data, branch.ToStateId) == null)
                    issues.Add(new NarrativeAuditIssue { NodeId = branch.Id, Message = $"分支 '{branch.Id}' 的终点状态 '{branch.ToStateId}' 不存在。" });
            }

            if (!string.IsNullOrWhiteSpace(data.QuestDialogueClass))
            {
                string dialoguePath = DialoguePathFor(data.QuestDialogueClass);
                if (!File.Exists(dialoguePath))
                    issues.Add(new NarrativeAuditIssue { Message = $"关联对话 '{data.QuestDialogueClass}' 没有对应文件（{Path.GetFileName(dialoguePath)}）。" });
            }

            // 状态/分支上的任务与对话事件：目标资产不存在时运行时会静默跳过，必须在保存前说清楚
            foreach (var state in data.States ?? new List<QuestStateData>())
            {
                if (state?.Id == null) continue;
                AuditLogicEvents(state.Events, state.Id, $"状态 '{state.Id}'", issues);
            }
            foreach (var branch in data.Branches ?? new List<QuestBranchData>())
            {
                if (branch?.Id == null) continue;
                AuditLogicEvents(branch.Events, branch.Id, $"分支 '{branch.Id}'", issues);
            }

            var reachable = ReachableQuestStates(data);
            foreach (var state in data.States ?? new List<QuestStateData>())
            {
                if (state?.Id == null) continue;
                if (reachable.Count > 0 && !reachable.Contains(state.Id))
                {
                    issues.Add(new NarrativeAuditIssue
                    {
                        NodeId = state.Id,
                        IsError = false,
                        Message = $"状态 '{state.Id}' 从起始状态走不到（没有任何分支指向它）。"
                    });
                }

                bool terminal = state.StateType == EStateNodeType.Success || state.StateType == EStateNodeType.Failure;
                bool hasOutgoing = (data.Branches ?? new List<QuestBranchData>())
                    .Exists(b => string.Equals(b?.FromStateId, state.Id, StringComparison.OrdinalIgnoreCase));
                if (!terminal && !hasOutgoing)
                {
                    issues.Add(new NarrativeAuditIssue
                    {
                        NodeId = state.Id,
                        IsError = false,
                        Message = $"常规状态 '{state.Id}' 没有任何出口分支，流程走到这里会卡住。"
                    });
                }
            }

            return issues;
        }

        /// <summary>从起始状态沿分支能走到的状态集合；起始状态缺失时返回空集合（调用方据此跳过可达性警告）。</summary>
        private static HashSet<string> ReachableQuestStates(QuestData data)
        {
            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var start = FindState(data, data.StartStateId);
            if (start == null) return reachable;

            var queue = new Queue<QuestStateData>();
            queue.Enqueue(start);
            reachable.Add(start.Id);
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                foreach (var branch in data.Branches ?? new List<QuestBranchData>())
                {
                    if (!string.Equals(branch?.FromStateId, state.Id, StringComparison.OrdinalIgnoreCase)) continue;
                    var next = FindState(data, branch.ToStateId);
                    if (next != null && reachable.Add(next.Id)) queue.Enqueue(next);
                }
            }
            return reachable;
        }

        /// <summary>
        /// 对话侧审计：根节点、说话者引用、连线目标是否存在（错误级）；
        /// 没被引用的说话者、走不到的节点（警告级）。
        /// </summary>
        public static List<NarrativeAuditIssue> AuditDialogue(DialogueData data)
        {
            var issues = new List<NarrativeAuditIssue>();
            if (data == null) return issues;

            string rootId = "";
            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
            {
                if (npc != null && npc.IsRoot) { rootId = npc.Id; break; }
            }
            if (string.IsNullOrEmpty(rootId))
            {
                issues.Add(new NarrativeAuditIssue { Message = "对话没有根节点（isRoot），运行时不知道从哪句开始。" });
            }
            else if (FindNpcReply(data, rootId) == null)
            {
                issues.Add(new NarrativeAuditIssue { Message = $"根节点 '{rootId}' 不存在。" });
            }

            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
            {
                if (npc == null) continue;
                if (string.IsNullOrWhiteSpace(npc.SpeakerId) || FindSpeaker(data, npc.SpeakerId) == null)
                    issues.Add(new NarrativeAuditIssue { NodeId = npc.Id, Message = $"NPC 节点 '{npc.Id}' 的说话者 '{npc.SpeakerId}' 不在 speakers 列表里。" });
                foreach (var target in npc.PlayerReplies ?? new List<string>())
                {
                    if (FindPlayerReply(data, target) == null)
                        issues.Add(new NarrativeAuditIssue { NodeId = npc.Id, Message = $"NPC 节点 '{npc.Id}' 连向不存在的玩家选项 '{target}'。" });
                }
                foreach (var target in npc.NpcReplies ?? new List<string>())
                {
                    if (FindNpcReply(data, target) == null)
                        issues.Add(new NarrativeAuditIssue { NodeId = npc.Id, Message = $"NPC 节点 '{npc.Id}' 连向不存在的 NPC 节点 '{target}'。" });
                }
            }

            foreach (var player in data.PlayerReplies ?? new List<PlayerReplyData>())
            {
                if (player == null) continue;
                foreach (var target in player.NpcReplies ?? new List<string>())
                {
                    if (FindNpcReply(data, target) == null)
                        issues.Add(new NarrativeAuditIssue { NodeId = player.Id, Message = $"玩家选项 '{player.Id}' 连向不存在的 NPC 节点 '{target}'。" });
                }
            }

            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
            {
                if (!string.IsNullOrEmpty(npc?.SpeakerId)) referenced.Add(npc.SpeakerId);
            }
            foreach (var speaker in data.Speakers ?? new List<SpeakerData>())
            {
                if (speaker?.SpeakerId != null && !referenced.Contains(speaker.SpeakerId))
                {
                    // 玩家说话者按约定不会被 NPC 节点引用（玩家台词挂在玩家选项上），报"没人用"就是把约定当缺陷
                    if (speaker.IsPlayer) continue;

                    issues.Add(new NarrativeAuditIssue
                    {
                        NodeId = speaker.SpeakerId,
                        IsError = false,
                        Message = $"说话者 '{speaker.SpeakerId}' 没有被任何 NPC 节点使用。"
                    });
                }
            }

            // 行内 CG：路径必须指向真实文件，否则运行时 PlayVideo 会静默跳过
            void CheckCutscene(string cutscenePath, string nodeId)
            {
                if (string.IsNullOrWhiteSpace(cutscenePath)) return;
                string resolved = NarrativeDataPaths.Resolve(cutscenePath.Trim());
                if (File.Exists(resolved)) return;
                issues.Add(new NarrativeAuditIssue
                {
                    NodeId = nodeId ?? "",
                    Message = "节点 '" + (nodeId ?? "?") + "' 的 CG '" + cutscenePath + "' 文件不存在（播放时会被跳过）。"
                });
            }

            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>()) CheckCutscene(npc?.Line?.CutscenePath, npc?.Id);
            foreach (var player in data.PlayerReplies ?? new List<PlayerReplyData>()) CheckCutscene(player?.Line?.CutscenePath, player?.Id);

            // 对话节点上的任务/对话事件同样要查目标资产（叙事里"穿插任务"最常写在这里）
            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
            {
                if (npc?.Id != null) AuditLogicEvents(npc.Events, npc.Id, $"NPC 节点 '{npc.Id}'", issues);
            }
            foreach (var player in data.PlayerReplies ?? new List<PlayerReplyData>())
            {
                if (player?.Id != null) AuditLogicEvents(player.Events, player.Id, $"玩家选项 '{player.Id}'", issues);
            }

            // 镜头名必须能在 Shots 目录里解析到：解析不到时运行时会静默回落默认镜头，作者看不出来
            var knownShots = KnownShotNames();
            if (knownShots.Count > 0)
            {
                void CheckShot(string shotName, string nodeId, string where)
                {
                    if (string.IsNullOrWhiteSpace(shotName) || knownShots.Contains(shotName.Trim())) return;
                    issues.Add(new NarrativeAuditIssue
                    {
                        NodeId = nodeId ?? "",
                        Message = where + " 的镜头 '" + shotName + "' 在 Shots 目录里不存在（运行时会回落默认镜头）。"
                    });
                }

                foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
                    CheckShot(npc?.Line?.ShotName, npc?.Id, "NPC 节点 '" + (npc?.Id ?? "?") + "'");
                foreach (var player in data.PlayerReplies ?? new List<PlayerReplyData>())
                    CheckShot(player?.Line?.ShotName, player?.Id, "玩家选项 '" + (player?.Id ?? "?") + "'");
                foreach (var speaker in data.Speakers ?? new List<SpeakerData>())
                {
                    CheckShot(speaker?.DefaultShotName, speaker?.SpeakerId, "说话者 '" + (speaker?.SpeakerId ?? "?") + "'");
                    CheckShot(speaker?.SelectingReplyShotName, speaker?.SpeakerId, "说话者 '" + (speaker?.SpeakerId ?? "?") + "'");
                }
                CheckShot(data.Config?.DefaultShotName, "", "对话默认镜头");
            }

            var reachable = ReachableDialogueNodes(data, rootId);
            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
            {
                if (npc?.Id != null && reachable.Count > 0 && !reachable.Contains(npc.Id))
                    issues.Add(new NarrativeAuditIssue { NodeId = npc.Id, IsError = false, Message = $"NPC 节点 '{npc.Id}' 从根节点走不到。" });
            }
            foreach (var player in data.PlayerReplies ?? new List<PlayerReplyData>())
            {
                if (player?.Id != null && reachable.Count > 0 && !reachable.Contains(player.Id))
                    issues.Add(new NarrativeAuditIssue { NodeId = player.Id, IsError = false, Message = $"玩家选项 '{player.Id}' 从根节点走不到。" });
            }

            return issues;
        }

        /// <summary>
        /// 事件引用审计：开始/重开/结算任务、播放对话的目标资产必须存在。
        /// 运行时对空目标只打一条警告就跳过，作者在编辑器里看不出来。
        /// </summary>
        private static void AuditLogicEvents(List<JsonElement> events, string nodeId, string owner, List<NarrativeAuditIssue> issues)
        {
            if (events == null) return;
            foreach (var element in events)
            {
                if (element.ValueKind != JsonValueKind.Object) continue;

                string type = ReadLogicString(element, "type");
                if (string.IsNullOrWhiteSpace(type)) continue;

                string normalized = type.Trim();
                if (normalized.EndsWith("Event", StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring(0, normalized.Length - 5);

                bool isQuestEvent = string.Equals(normalized, "StartQuest", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(normalized, "RestartQuest", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(normalized, "CompleteQuest", StringComparison.OrdinalIgnoreCase);
                if (isQuestEvent)
                {
                    string quest = ReadLogicString(element, "questClassId");
                    if (string.IsNullOrWhiteSpace(quest))
                    {
                        issues.Add(new NarrativeAuditIssue { NodeId = nodeId, Message = $"{owner} 的 {type} 没有填任务类（questClassId），运行时会跳过。" });
                    }
                    else if (!File.Exists(QuestPathFor(quest)))
                    {
                        issues.Add(new NarrativeAuditIssue
                        {
                            NodeId = nodeId,
                            IsError = false,
                            Message = $"{owner} 的 {type} 引用任务 '{quest}' 没有对应文件（{Path.GetFileName(QuestPathFor(quest))}），运行时会跳过。"
                        });
                    }
                }
                else if (string.Equals(normalized, "BeginDialogue", StringComparison.OrdinalIgnoreCase))
                {
                    string dialogue = ReadLogicString(element, "dialogueClassId");
                    if (string.IsNullOrWhiteSpace(dialogue))
                    {
                        issues.Add(new NarrativeAuditIssue { NodeId = nodeId, Message = $"{owner} 的 {type} 没有填对话类（dialogueClassId），运行时会跳过。" });
                    }
                    else if (!File.Exists(DialoguePathFor(dialogue)))
                    {
                        issues.Add(new NarrativeAuditIssue
                        {
                            NodeId = nodeId,
                            IsError = false,
                            Message = $"{owner} 的 {type} 引用对话 '{dialogue}' 没有对应文件（{Path.GetFileName(DialoguePathFor(dialogue))}），运行时会跳过。"
                        });
                    }
                }
            }
        }

        private static string ReadLogicString(JsonElement element, string name)
        {
            try
            {
                if (element.ValueKind != JsonValueKind.Object) return "";
                foreach (var prop in element.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    return prop.Value.ValueKind == JsonValueKind.String ? (prop.Value.GetString() ?? "") : prop.Value.ToString();
                }
            }
            catch { }
            return "";
        }

        private static HashSet<string> ReachableDialogueNodes(DialogueData data, string rootId)
        {
            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(rootId) || FindNpcReply(data, rootId) == null) return reachable;

            var queue = new Queue<string>();
            queue.Enqueue(rootId);
            reachable.Add(rootId);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                var npc = FindNpcReply(data, current);
                if (npc != null)
                {
                    foreach (var target in Concat(npc.PlayerReplies, npc.NpcReplies))
                    {
                        if (reachable.Add(target)) queue.Enqueue(target);
                    }
                    continue;
                }

                var player = FindPlayerReply(data, current);
                if (player != null)
                {
                    foreach (var target in player.NpcReplies ?? new List<string>())
                    {
                        if (reachable.Add(target)) queue.Enqueue(target);
                    }
                }
            }
            return reachable;
        }

        private static IEnumerable<string> Concat(List<string> a, List<string> b)
        {
            foreach (var item in a ?? new List<string>()) yield return item;
            foreach (var item in b ?? new List<string>()) yield return item;
        }

        // --- 任务模板链接（叙事资产 ↔ 任务编辑器里的 DB 模板）---

        /// <summary>设置/清空"这条叙事图实现哪个任务模板"。空串＝清空；非数字直接拒绝。</summary>
        public static string SetQuestTemplateId(QuestData data, string text)
        {
            if (data == null) return "当前没有打开的任务";

            string trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0)
            {
                data.QuestTemplateId = null;
                return null;
            }

            if (!int.TryParse(trimmed, out int id)) return $"模板 id 必须是数字：'{trimmed}'";
            if (id <= 0) return "模板 id 必须大于 0";
            data.QuestTemplateId = id;
            return null;
        }

        /// <summary>找出所有声明了该模板 id 的叙事资产（directory 只给自检用，生产走默认目录）。</summary>
        public static List<string> QuestAssetsForTemplate(int templateId, string directory = null)
        {
            var paths = new List<string>();
            if (templateId <= 0) return paths;

            foreach (var file in ListJsonFiles(directory ?? QuestsDirectory))
            {
                try
                {
                    var data = LoadQuestData(file);
                    if (data?.QuestTemplateId == templateId) paths.Add(file);
                }
                catch
                {
                    // 坏资产由校验面板单独报，这里跳过
                }
            }
            return paths;
        }

        /// <summary>
        /// 给任务模板一键生成叙事图骨架：开始(常规) --b_main--> 完成(成功)，并把 questTemplateId 填好。
        /// 骨架本身结构完整（过审计），作者接着加中间状态即可。
        /// </summary>
        public static string CreateQuestAssetWithTemplate(int templateId, string assetName, out string path, out string assetId, string directory = null)
        {
            path = null;
            assetId = null;
            if (templateId <= 0) return "模板 id 必须大于 0";

            string name = SanitizeFileName(assetName);
            path = Path.Combine(directory ?? QuestsDirectory, name + ".json");
            if (File.Exists(path)) return $"已存在同名叙事资产：{name}";

            var data = new QuestData
            {
                QuestId = name,
                QuestName = name,
                QuestTemplateId = templateId,
                StartStateId = "start",
                States = new List<QuestStateData>
                {
                    new QuestStateData
                    {
                        Id = "start", Description = "开始", StateType = EStateNodeType.Regular,
                        Position = new NodePositionData { X = 0f, Y = 0f }
                    },
                    new QuestStateData
                    {
                        Id = "done", Description = "完成", StateType = EStateNodeType.Success,
                        Position = new NodePositionData { X = 320f, Y = 0f }
                    }
                },
                Branches = new List<QuestBranchData>
                {
                    new QuestBranchData
                    {
                        Id = "b_main", Description = "", FromStateId = "start", ToStateId = "done",
                        Position = new NodePositionData { X = 160f, Y = 96f },
                        Tasks = new List<TaskData>()
                    }
                }
            };

            WriteQuest(path, data);
            assetId = name;
            return null;
        }

        /// <summary>模板 id 冲突审计：同一条 DB 模板被多条叙事图声明时给警告（定位到另一条资产的文件名）。</summary>
        public static List<NarrativeAuditIssue> AuditTemplateLink(QuestData data, string selfPath, string directory = null)
        {
            var issues = new List<NarrativeAuditIssue>();
            int? templateId = data?.QuestTemplateId;
            if (templateId == null || templateId <= 0) return issues;

            string self = NarrativeDataPaths.Resolve(selfPath);
            foreach (var other in QuestAssetsForTemplate(templateId.Value, directory))
            {
                if (string.Equals(NarrativeDataPaths.Resolve(other), self, StringComparison.OrdinalIgnoreCase)) continue;
                issues.Add(new NarrativeAuditIssue
                {
                    IsError = false,
                    Message = $"模板 {templateId} 也被 {Path.GetFileNameWithoutExtension(other)} 声明；一条 DB 任务模板通常只该有一条叙事图。"
                });
            }
            return issues;
        }

        /// <summary>
        /// 模板链接的一句话状态。catalogAvailable=false 表示这次没查（CLI 不可用），必须如实说出来；
        /// publishState 口径与任务编辑器一致：1=已发布，其它=草稿/下线。
        /// </summary>
        public static string DescribeTemplateLinkStatus(int templateId, bool catalogAvailable, bool exists, bool valid, int publishState)
        {
            if (!catalogAvailable) return "目录未查";
            if (!exists) return "模板不存在";
            if (!valid) return "模板已作废";
            return publishState == 1 ? "已发布" : "未发布（草稿/下线）";
        }

        /// <summary>模板 id → 声明它的叙事资产名（扫描一次目录；同模板多条时取第一条，冲突由审计单独报）。</summary>
        public static Dictionary<int, string> TemplateAssetMap(string directory = null)
        {
            var map = new Dictionary<int, string>();
            foreach (var file in ListJsonFiles(directory ?? QuestsDirectory))
            {
                try
                {
                    var data = LoadQuestData(file);
                    int? templateId = data?.QuestTemplateId;
                    if (templateId == null || templateId <= 0 || map.ContainsKey(templateId.Value)) continue;
                    map[templateId.Value] = Path.GetFileNameWithoutExtension(file);
                }
                catch
                {
                    // 坏资产由校验面板单独报
                }
            }
            return map;
        }

        /// <summary>资产头里给作者看的一行："模板 10001"，没挂模板返回空串。</summary>
        public static string QuestLinkLabel(QuestData data)
        {
            int? templateId = data?.QuestTemplateId;
            return templateId == null || templateId <= 0 ? "" : "模板 " + templateId.Value;
        }

        private static string CheckRenameTarget(string path, string newName, out string newPath)
        {
            newPath = null;
            if (string.IsNullOrWhiteSpace(newName)) return "新文件名不能为空";

            string resolved = NarrativeDataPaths.Resolve(path);
            if (!File.Exists(resolved)) return "资产文件不存在：" + resolved;

            string fileName = SanitizeFileName(newName);
            string directory = Path.GetDirectoryName(resolved) ?? "";
            newPath = Path.Combine(directory, fileName + ".json");
            if (string.Equals(resolved, newPath, StringComparison.OrdinalIgnoreCase)) return "新旧文件名相同";
            if (File.Exists(newPath)) return "已存在同名资产：" + fileName;
            return null;
        }

        #endregion

        #region 校验（与运行时同路）

        /// <summary>
        /// 校验任务文件：①用运行时同一份加载器 <see cref="QuestFactory.LoadQuest"/> 真装一遍
        /// （起始状态 / Success|Failure 终止状态 / 分支目标状态全都被它检查）；
        /// ②把文件里每个事件/条件/任务的 <c>"type"</c> 拿去 <see cref="NarrativeLogicFactory"/> 注册表核对。
        /// </summary>
        public static List<string> ValidateQuestFile(string path)
        {
            var problems = new List<string>();
            string resolved = NarrativeDataPaths.Resolve(path);

            if (!File.Exists(resolved))
            {
                problems.Add($"文件不存在：{resolved}");
                return problems;
            }

            try
            {
                QuestFactory.LoadQuest(resolved);
            }
            catch (Exception ex)
            {
                problems.Add($"运行时装载失败：{ex.Message}");
            }

            JsonElement root;
            if (!TryParseRoot(resolved, out root))
            {
                problems.Add("JSON 解析失败（文件不是合法 JSON 对象）");
                return problems;
            }

            WalkElementCollection(root, "states", "events", NarrativeLogicKind.Event, problems);
            WalkElementCollection(root, "states", "conditions", NarrativeLogicKind.Condition, problems);
            WalkElementCollection(root, "branches", "events", NarrativeLogicKind.Event, problems);
            WalkElementCollection(root, "branches", "conditions", NarrativeLogicKind.Condition, problems);

            // 运行时装载不做枚举范围检查（stateType=9 会被静默当成 Regular），编辑器把这条补上。
            ForEach(root, "states", state =>
            {
                if (!state.TryGetProperty("stateType", out var typeProperty)) return;
                if (typeProperty.ValueKind != JsonValueKind.Number || !typeProperty.TryGetInt32(out int value) ||
                    !Enum.IsDefined(typeof(EStateNodeType), value))
                {
                    problems.Add($"状态 '{GetName(state)}' 的 stateType 不是 0..2（Regular/Success/Failure），运行时只会按 Regular 处理。");
                }
            });

            ForEach(root, "branches", branch =>
            {
                ForEach(branch, "tasks", task =>
                {
                    string type;
                    if (!TryGetLogicType(task, out type))
                    {
                        problems.Add("任务条目缺少 \"type\" 字段，运行时会直接忽略它。");
                        return;
                    }

                    if (!NarrativeLogicFactory.IsTaskRegistered(type))
                    {
                        problems.Add($"未注册的任务类型 '{type}'：运行时会回退成 GenericTask（Has it been renamed?）。注册表可用：{Join(NarrativeLogicFactory.RegisteredTaskTypes)}");
                    }
                });
            });

            return problems;
        }

        /// <summary>校验对话文件，口径同上（根节点要求来自 <see cref="DialogueFactory.ValidateDialogue"/>）。</summary>
        public static List<string> ValidateDialogueFile(string path)
        {
            var problems = new List<string>();
            string resolved = NarrativeDataPaths.Resolve(path);

            if (!File.Exists(resolved))
            {
                problems.Add($"文件不存在：{resolved}");
                return problems;
            }

            try
            {
                DialogueFactory.LoadDialogue(resolved);
            }
            catch (Exception ex)
            {
                problems.Add($"运行时装载失败：{ex.Message}");
            }

            JsonElement root;
            if (!TryParseRoot(resolved, out root))
            {
                problems.Add("JSON 解析失败（文件不是合法 JSON 对象）");
                return problems;
            }

            foreach (var collection in new[] { "npcReplies", "playerReplies" })
            {
                WalkElementCollection(root, collection, "events", NarrativeLogicKind.Event, problems);
                WalkElementCollection(root, collection, "conditions", NarrativeLogicKind.Condition, problems);

                ForEach(root, collection, node =>
                {
                    JsonElement line;
                    if (node.TryGetProperty("line", out line) && line.ValueKind == JsonValueKind.Object)
                    {
                        CheckLogicArray(line, "conditions", NarrativeLogicKind.Condition, $"对话行 {GetName(node)}", problems);
                    }

                    ForEach(node, "alternativeLines", alt =>
                        CheckLogicArray(alt, "conditions", NarrativeLogicKind.Condition, $"备选行 {GetName(node)}", problems));
                });
            }

            return problems;
        }

        public static bool ValidateAsset(string path, out List<string> problems)
        {
            problems = IsQuestPath(path) ? ValidateQuestFile(path) : ValidateDialogueFile(path);
            return problems.Count == 0;
        }

        private static bool IsQuestPath(string path)
        {
            string dir = Path.GetDirectoryName(NarrativeDataPaths.Resolve(path)) ?? "";
            return string.Equals(dir, QuestsDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static void WalkElementCollection(JsonElement root, string containerProp, string itemProp, NarrativeLogicKind kind, List<string> problems)
        {
            ForEach(root, containerProp, container =>
                CheckLogicArray(container, itemProp, kind, GetName(container), problems));
        }

        private static void CheckLogicArray(JsonElement owner, string property, NarrativeLogicKind kind, string ownerId, List<string> problems)
        {
            JsonElement array;
            if (!owner.TryGetProperty(property, out array) || array.ValueKind != JsonValueKind.Array) return;

            foreach (var item in array.EnumerateArray())
            {
                string type;
                if (!TryGetLogicType(item, out type))
                {
                    problems.Add($"{ownerId} 的 {property} 里有条目缺少 \"type\" 字段，运行时会忽略它。");
                    continue;
                }

                if (!IsRegistered(kind, type))
                {
                    problems.Add($"{ownerId} 的未知{Label(kind)}类型 '{type}'（Has it been renamed?）。注册表可用：{Join(Names(kind))}");
                }
            }
        }

        private static void ForEach(JsonElement root, string property, Action<JsonElement> body)
        {
            JsonElement array;
            if (!root.TryGetProperty(property, out array) || array.ValueKind != JsonValueKind.Array) return;
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object) body(item);
            }
        }

        private static string GetName(JsonElement obj)
        {
            JsonElement id;
            if (obj.TryGetProperty("id", out id) && id.ValueKind == JsonValueKind.String) return $"节点 '{id.GetString()}'";
            return "未命名节点";
        }

        private static bool TryParseRoot(string path, out JsonElement root)
        {
            root = default;
            try
            {
                using (var doc = JsonDocument.Parse(File.ReadAllText(path),
                           new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                    root = doc.RootElement.Clone();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>类型判别字段候选：与 NarrativeLogicFactory.TypeKeys 保持一致（运行时用哪几个键，这里就查哪几个）。</summary>
        private static readonly string[] TypeKeys =
        {
            "type", "$type", "taskType", "eventType", "conditionType", "logicType", "class", "name"
        };

        private static bool TryGetLogicType(JsonElement item, out string type)
        {
            type = null;
            if (item.ValueKind != JsonValueKind.Object) return false;

            foreach (var key in TypeKeys)
            {
                foreach (var prop in item.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase)) continue;
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;
                    if (string.IsNullOrWhiteSpace(prop.Value.GetString())) continue;

                    type = prop.Value.GetString();
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region 注册表

        /// <summary>
        /// 预热注册表：内置类型在 <see cref="NarrativeLogicFactory"/> 静态构造里已登记，
        /// 这里再扫一次已加载程序集，把游戏代码自定义的任务/事件/条件也带进来，
        /// 使编辑器里可见的名单与运行时装载时的名单一致。
        /// </summary>
        public static void WarmLogicRegistry()
        {
            try
            {
                NarrativeLogicFactory.AutoRegisterLoadedAssemblies();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[NarrativePro] 预热叙事逻辑注册表失败：{ex.Message}");
            }
        }

        public static List<string> RegisteredTaskNames()
        {
            WarmLogicRegistry();
            return Sorted(NarrativeLogicFactory.RegisteredTaskTypes);
        }

        public static List<string> RegisteredEventNames()
        {
            WarmLogicRegistry();
            return Sorted(NarrativeLogicFactory.RegisteredEventTypes);
        }

        public static List<string> RegisteredConditionNames()
        {
            WarmLogicRegistry();
            return Sorted(NarrativeLogicFactory.RegisteredConditionTypes);
        }

        public static List<string> Names(NarrativeLogicKind kind)
        {
            switch (kind)
            {
                case NarrativeLogicKind.Task: return RegisteredTaskNames();
                case NarrativeLogicKind.Event: return RegisteredEventNames();
                default: return RegisteredConditionNames();
            }
        }

        public static string Label(NarrativeLogicKind kind)
        {
            switch (kind)
            {
                case NarrativeLogicKind.Task: return "任务";
                case NarrativeLogicKind.Event: return "事件";
                default: return "条件";
            }
        }

        private static bool IsRegistered(NarrativeLogicKind kind, string typeId)
        {
            switch (kind)
            {
                case NarrativeLogicKind.Task: return NarrativeLogicFactory.IsTaskRegistered(typeId);
                case NarrativeLogicKind.Event: return NarrativeLogicFactory.IsEventRegistered(typeId);
                default: return NarrativeLogicFactory.IsConditionRegistered(typeId);
            }
        }

        private static string CheckRegistration(NarrativeLogicKind kind, string typeId)
        {
            if (string.IsNullOrWhiteSpace(typeId)) return $"必须给出{Label(kind)}类型名";
            if (IsRegistered(kind, typeId)) return null;

            return $"拒绝写入：{Label(kind)}类型 '{typeId}' 不在运行时注册表里（Has it been renamed?）。可用：{Join(Names(kind))}";
        }

        private static List<string> Sorted(IEnumerable<string> names)
        {
            var list = new List<string>();
            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static string Join(IEnumerable<string> names)
        {
            return string.Join(", ", Sorted(names));
        }

        #endregion

        #region 任务编辑

        public static string AddQuestState(QuestData data, string stateId, string stateType, string description)
        {
            if (data == null) return "当前没有打开的任务";
            if (string.IsNullOrWhiteSpace(stateId)) return "状态 id 不能为空";
            if (FindState(data, stateId) != null) return $"已存在 id 为 '{stateId}' 的状态";

            EStateNodeType type;
            string error = TryParseStateType(stateType, out type);
            if (error != null) return error;

            data.States.Add(new QuestStateData
            {
                Id = stateId.Trim(),
                Description = description ?? "",
                StateType = type,
                Position = new NodePositionData { X = data.States.Count * 400f, Y = 0f }
            });
            return null;
        }

        public static string RemoveQuestState(QuestData data, string stateId)
        {
            if (data == null) return "当前没有打开的任务";
            var state = FindState(data, stateId);
            if (state == null) return $"找不到状态 '{stateId}'";

            data.States.Remove(state);
            data.Branches.RemoveAll(b => string.Equals(b.FromStateId, stateId, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(b.ToStateId, stateId, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(data.StartStateId, stateId, StringComparison.OrdinalIgnoreCase)) data.StartStateId = "";
            return null;
        }

        public static string SetQuestStartState(QuestData data, string stateId)
        {
            if (data == null) return "当前没有打开的任务";
            if (FindState(data, stateId) == null) return $"找不到状态 '{stateId}'，不能作为起始状态";
            data.StartStateId = stateId.Trim();
            return null;
        }

        /// <summary>
        /// 图上"长出一个新节点"：在源状态右侧加一个常规状态，并连一条 源→新 的分支。
        /// id 自动取 s_next / b_next（撞名就加序号），位置排在源状态右边一列、分支落在两者中间偏下。
        /// </summary>
        public static string AddQuestStateConnected(QuestData data, string fromStateId, out string newStateId, out string newBranchId)
        {
            newStateId = null;
            newBranchId = null;
            if (data == null) return "当前没有打开的任务";

            var from = FindState(data, fromStateId);
            if (from == null) return $"找不到起始状态 '{fromStateId}'";

            string stateId = UniqueId(id => FindState(data, id) != null, "s_next");
            string error = AddQuestState(data, stateId, ((int)EStateNodeType.Regular).ToString(), "新状态");
            if (error != null) return error;

            var added = FindState(data, stateId);
            var source = from.Position ?? new NodePositionData();
            added.Position = new NodePositionData { X = source.X + 320f, Y = source.Y };
            newStateId = added.Id;

            string branchId = UniqueId(id => FindBranch(data, id) != null, "b_next");
            error = AddQuestBranch(data, branchId, from.Id, newStateId, "");
            if (error != null) return error;

            FindBranch(data, branchId).Position = new NodePositionData { X = source.X + 160f, Y = source.Y + 96f };
            newBranchId = branchId;
            return null;
        }

        /// <summary>反转分支方向（from ↔ to）。端点有失效时拒绝，免得把坏引用挪到另一头。</summary>
        public static string FlipQuestBranch(QuestData data, string branchId)
        {
            var branch = FindBranch(data, branchId);
            if (branch == null) return $"找不到分支 '{branchId}'";
            if (FindState(data, branch.FromStateId) == null || FindState(data, branch.ToStateId) == null)
                return "分支端点已失效，先修好端点再反转";

            string from = branch.FromStateId;
            branch.FromStateId = branch.ToStateId;
            branch.ToStateId = from;
            return null;
        }

        /// <summary>拖拽连线的任务侧落地：自动取分支 id（b_next，撞名加序号），位置落在两端中点偏下。</summary>
        public static string AddQuestBranchConnected(QuestData data, string fromStateId, string toStateId, out string newBranchId)
        {
            newBranchId = null;
            if (data == null) return "当前没有打开的任务";
            if (string.Equals(fromStateId, toStateId, StringComparison.OrdinalIgnoreCase)) return "不能连到自己";

            var from = FindState(data, fromStateId);
            var to = FindState(data, toStateId);
            if (from == null) return $"找不到起始状态 '{fromStateId}'";
            if (to == null) return $"找不到目标状态 '{toStateId}'";

            string branchId = UniqueId(id => FindBranch(data, id) != null, "b_next");
            string error = AddQuestBranch(data, branchId, from.Id, to.Id, "");
            if (error != null) return error;

            var branch = FindBranch(data, branchId);
            var fp = from.Position ?? new NodePositionData();
            var tp = to.Position ?? new NodePositionData();
            branch.Position = new NodePositionData { X = (fp.X + tp.X) * 0.5f + 60f, Y = (fp.Y + tp.Y) * 0.5f + 96f };
            newBranchId = branchId;
            return null;
        }

        public static string AddQuestBranch(QuestData data, string branchId, string fromStateId, string toStateId, string description)
        {
            if (data == null) return "当前没有打开的任务";
            if (string.IsNullOrWhiteSpace(branchId)) return "分支 id 不能为空";
            if (FindBranch(data, branchId) != null) return $"已存在 id 为 '{branchId}' 的分支";
            if (FindState(data, fromStateId) == null) return $"找不到起始状态 '{fromStateId}'";
            if (FindState(data, toStateId) == null) return $"找不到目标状态 '{toStateId}'";

            data.Branches.Add(new QuestBranchData
            {
                Id = branchId.Trim(),
                Description = description ?? "",
                FromStateId = fromStateId.Trim(),
                ToStateId = toStateId.Trim(),
                Position = new NodePositionData { X = 200f, Y = data.Branches.Count * 120f },
                Tasks = new List<TaskData>()
            });
            return null;
        }

        public static string RemoveQuestBranch(QuestData data, string branchId)
        {
            if (data == null) return "当前没有打开的任务";
            var branch = FindBranch(data, branchId);
            if (branch == null) return $"找不到分支 '{branchId}'";
            data.Branches.Remove(branch);
            return null;
        }

        /// <summary>给分支加一条任务。<c>paramsJson</c> 里的字段会被原样写进任务对象，运行时由注册表反射赋值。</summary>
        public static string AddQuestTask(QuestData data, string branchId, string typeId, string targetId,
                                          string requiredQuantity, string description, string paramsJson)
        {
            if (data == null) return "当前没有打开的任务";
            var branch = FindBranch(data, branchId);
            if (branch == null) return $"找不到分支 '{branchId}'";

            string registration = CheckRegistration(NarrativeLogicKind.Task, typeId);
            if (registration != null) return registration;

            var task = new TaskData { Type = typeId.Trim(), TargetId = targetId ?? "", Description = description ?? "" };
            int quantity;
            if (!string.IsNullOrWhiteSpace(requiredQuantity))
            {
                if (!int.TryParse(requiredQuantity.Trim(), out quantity)) return "需求数量必须是整数";
                task.RequiredQuantity = quantity;
            }

            if (!string.IsNullOrWhiteSpace(paramsJson))
            {
                Dictionary<string, JsonElement> fields;
                string error = ParseParams(paramsJson, out fields);
                if (error != null) return error;

                foreach (var field in fields)
                {
                    // 与 TaskData 已建模的字段同名时以 DTO 为准，否则序列化会写出重复键。
                    if (IsReservedTaskField(field.Key)) continue;
                    task.ExtraFields[field.Key] = field.Value;
                }
            }

            if (branch.Tasks == null) branch.Tasks = new List<TaskData>();
            branch.Tasks.Add(task);
            return null;
        }

        public static string RemoveQuestTask(QuestData data, string branchId, string taskIndex)
        {
            if (data == null) return "当前没有打开的任务";
            var branch = FindBranch(data, branchId);
            if (branch == null) return $"找不到分支 '{branchId}'";

            int index;
            if (!int.TryParse(taskIndex, out index)) return "任务序号必须是整数";
            if (branch.Tasks == null || index < 0 || index >= branch.Tasks.Count)
                return $"任务序号 {index} 越界（该分支共 {branch.Tasks?.Count ?? 0} 条）";

            branch.Tasks.RemoveAt(index);
            return null;
        }

        /// <summary>给状态或分支加事件/条件；类型名未注册则拒绝写入。</summary>
        public static string AddQuestLogic(QuestData data, string targetId, NarrativeLogicKind kind, string typeId, string paramsJson)
        {
            if (data == null) return "当前没有打开的任务";
            if (kind == NarrativeLogicKind.Task) return "任务只能加在分支上，请用「加分支任务」";

            string registration = CheckRegistration(kind, typeId);
            if (registration != null) return registration;

            Dictionary<string, JsonElement> fields;
            string parseError = ParseParams(paramsJson, out fields);
            if (parseError != null) return parseError;

            JsonElement element = BuildLogicElement(typeId.Trim(), fields);

            var state = FindState(data, targetId);
            if (state != null)
            {
                if (kind == NarrativeLogicKind.Event)
                {
                    state.Events = state.Events ?? new List<JsonElement>();
                    state.Events.Add(element);
                }
                else
                {
                    state.Conditions = state.Conditions ?? new List<JsonElement>();
                    state.Conditions.Add(element);
                }
                return null;
            }

            var branch = FindBranch(data, targetId);
            if (branch != null)
            {
                if (kind == NarrativeLogicKind.Event)
                {
                    branch.Events = branch.Events ?? new List<JsonElement>();
                    branch.Events.Add(element);
                }
                else
                {
                    branch.Conditions = branch.Conditions ?? new List<JsonElement>();
                    branch.Conditions.Add(element);
                }
                return null;
            }

            return $"找不到状态或分支 '{targetId}'";
        }

        public static string RemoveQuestLogic(QuestData data, string targetId, NarrativeLogicKind kind, string indexText)
        {
            if (data == null) return "当前没有打开的任务";
            int index;
            if (!int.TryParse(indexText, out index)) return "条目序号必须是整数";

            List<JsonElement> list = null;
            var state = FindState(data, targetId);
            if (state != null) list = kind == NarrativeLogicKind.Event ? state.Events : state.Conditions;
            else
            {
                var branch = FindBranch(data, targetId);
                if (branch != null) list = kind == NarrativeLogicKind.Event ? branch.Events : branch.Conditions;
            }

            if (list == null) return $"找不到状态或分支 '{targetId}'";
            if (index < 0 || index >= list.Count) return $"序号 {index} 越界（共 {list.Count} 条）";

            list.RemoveAt(index);
            return null;
        }

        private static QuestStateData FindState(QuestData data, string stateId)
        {
            if (data?.States == null || string.IsNullOrWhiteSpace(stateId)) return null;
            foreach (var state in data.States)
            {
                if (string.Equals(state.Id, stateId.Trim(), StringComparison.OrdinalIgnoreCase)) return state;
            }
            return null;
        }

        private static QuestBranchData FindBranch(QuestData data, string branchId)
        {
            if (data?.Branches == null || string.IsNullOrWhiteSpace(branchId)) return null;
            foreach (var branch in data.Branches)
            {
                if (string.Equals(branch.Id, branchId.Trim(), StringComparison.OrdinalIgnoreCase)) return branch;
            }
            return null;
        }

        private static string TryParseStateType(string text, out EStateNodeType type)
        {
            type = EStateNodeType.Regular;
            if (string.IsNullOrWhiteSpace(text)) return "状态类型不能为空（0=Regular / 1=Success / 2=Failure）";

            int numeric;
            if (int.TryParse(text.Trim(), out numeric))
            {
                if (!Enum.IsDefined(typeof(EStateNodeType), numeric)) return $"状态类型数值 {numeric} 不在 0..2 之间";
                type = (EStateNodeType)numeric;
                return null;
            }

            EStateNodeType parsed;
            if (Enum.TryParse(text.Trim(), true, out parsed))
            {
                type = parsed;
                return null;
            }
            return $"无法识别的状态类型 '{text}'（可用 Regular/Success/Failure 或 0/1/2）";
        }

        private static bool IsReservedTaskField(string key)
        {
            return string.Equals(key, "type", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, "targetId", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, "requiredQuantity", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, "description", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region 对话编辑

        public static string AddDialogueSpeaker(DialogueData data, string speakerId, string displayName, string isPlayer)
        {
            if (data == null) return "当前没有打开的对话";
            if (string.IsNullOrWhiteSpace(speakerId)) return "说话者 id 不能为空";
            if (FindSpeaker(data, speakerId) != null) return $"已存在说话者 '{speakerId}'";

            bool player = ParseBool(isPlayer, false);
            if (data.Speakers == null) data.Speakers = new List<SpeakerData>();
            data.Speakers.Add(new SpeakerData
            {
                SpeakerId = speakerId.Trim(),
                DisplayName = displayName ?? speakerId.Trim(),
                IsPlayer = player,
                Tags = new List<string>()
            });
            return null;
        }

        public static string RemoveDialogueSpeaker(DialogueData data, string speakerId)
        {
            if (data == null) return "当前没有打开的对话";
            var speaker = FindSpeaker(data, speakerId);
            if (speaker == null) return $"找不到说话者 '{speakerId}'";
            data.Speakers.Remove(speaker);
            return null;
        }

        /// <summary>
        /// 对话图上从某个 NPC 节点长出一个新 NPC 节点：沿用源节点的说话者，排在它右下方并连好线。
        /// </summary>
        public static string AddDialogueNpcReplyConnected(DialogueData data, string fromNodeId, out string newNodeId)
        {
            newNodeId = null;
            if (data == null) return "当前没有打开的对话";

            var from = FindNpcReply(data, fromNodeId);
            if (from == null) return $"找不到节点 '{fromNodeId}'";
            if (string.IsNullOrEmpty(from.SpeakerId) || FindSpeaker(data, from.SpeakerId) == null)
                return "源节点没有可用的说话者，先给它选一个说话者再往下接";

            string replyId = UniqueId(id => FindNpcReply(data, id) != null, "npc_next");
            string error = AddDialogueNpcReply(data, replyId, from.SpeakerId, "新台词", "0");
            if (error != null) return error;

            var added = FindNpcReply(data, replyId);
            added.NodePosX = from.NodePosX + 340f;
            added.NodePosY = from.NodePosY + 160f;
            newNodeId = added.Id;

            return LinkDialogue(data, from.Id, newNodeId, DialogueLinkKind.NpcToNpc);
        }

        public static string AddDialogueNpcReply(DialogueData data, string replyId, string speakerId, string text, string isRoot)
        {
            if (data == null) return "当前没有打开的对话";
            if (string.IsNullOrWhiteSpace(replyId)) return "节点 id 不能为空";
            if (FindNpcReply(data, replyId) != null) return $"已存在 NPC 节点 '{replyId}'";
            if (FindSpeaker(data, speakerId) == null) return $"说话者 '{speakerId}' 不在 speakers 列表里，运行时会找不到发言人";

            bool root = ParseBool(isRoot, false);
            if (data.NpcReplies == null) data.NpcReplies = new List<NpcReplyData>();
            data.NpcReplies.Add(new NpcReplyData
            {
                Id = replyId.Trim(),
                SpeakerId = speakerId.Trim(),
                IsRoot = root,
                IsSkippable = true,
                Line = new DialogueLineData { Text = text ?? "", Duration = ELineDuration.Default },
                NpcReplies = new List<string>(),
                PlayerReplies = new List<string>(),
                NodePosX = data.NpcReplies.Count * 300f,
                NodePosY = 0f
            });
            return null;
        }

        public static string AddDialoguePlayerReply(DialogueData data, string replyId, string optionText, string text)
        {
            if (data == null) return "当前没有打开的对话";
            if (string.IsNullOrWhiteSpace(replyId)) return "节点 id 不能为空";
            if (FindPlayerReply(data, replyId) != null) return $"已存在玩家节点 '{replyId}'";

            if (data.PlayerReplies == null) data.PlayerReplies = new List<PlayerReplyData>();
            data.PlayerReplies.Add(new PlayerReplyData
            {
                Id = replyId.Trim(),
                OptionText = optionText ?? "",
                AutoSelectIfOnlyReply = true,
                Line = new DialogueLineData { Text = text ?? "", Duration = ELineDuration.Default },
                NpcReplies = new List<string>(),
                PlayerReplies = new List<string>(),
                NodePosX = data.PlayerReplies.Count * 300f,
                NodePosY = 200f
            });
            return null;
        }

        public static string RemoveDialogueNode(DialogueData data, string nodeId)
        {
            if (data == null) return "当前没有打开的对话";
            var npc = FindNpcReply(data, nodeId);
            var player = FindPlayerReply(data, nodeId);
            if (npc == null && player == null) return $"找不到节点 '{nodeId}'";

            if (npc != null) data.NpcReplies.Remove(npc);
            if (player != null) data.PlayerReplies.Remove(player);
            Unlink(data, nodeId);
            return null;
        }

        public static string LinkDialogue(DialogueData data, string fromId, string toId, DialogueLinkKind kind)
        {
            if (data == null) return "当前没有打开的对话";
            if (string.Equals(fromId, toId, StringComparison.OrdinalIgnoreCase)) return "不能连到自己";

            switch (kind)
            {
                case DialogueLinkKind.NpcToNpc:
                {
                    var from = FindNpcReply(data, fromId);
                    if (from == null) return $"找不到 NPC 节点 '{fromId}'";
                    if (FindNpcReply(data, toId) == null) return $"找不到 NPC 节点 '{toId}'";
                    if (from.NpcReplies == null) from.NpcReplies = new List<string>();
                    if (Contains(from.NpcReplies, toId)) return "该连线已存在";
                    from.NpcReplies.Add(toId.Trim());
                    return null;
                }
                case DialogueLinkKind.NpcToPlayer:
                {
                    var from = FindNpcReply(data, fromId);
                    if (from == null) return $"找不到 NPC 节点 '{fromId}'";
                    if (FindPlayerReply(data, toId) == null) return $"找不到玩家节点 '{toId}'";
                    if (from.PlayerReplies == null) from.PlayerReplies = new List<string>();
                    if (Contains(from.PlayerReplies, toId)) return "该连线已存在";
                    from.PlayerReplies.Add(toId.Trim());
                    return null;
                }
                default:
                {
                    var from = FindPlayerReply(data, fromId);
                    if (from == null) return $"找不到玩家节点 '{fromId}'";
                    if (FindNpcReply(data, toId) == null) return $"找不到 NPC 节点 '{toId}'";
                    if (from.NpcReplies == null) from.NpcReplies = new List<string>();
                    if (Contains(from.NpcReplies, toId)) return "该连线已存在";
                    from.NpcReplies.Add(toId.Trim());
                    return null;
                }
            }
        }

        public static string UnlinkDialogue(DialogueData data, string toId)
        {
            if (data == null) return "当前没有打开的对话";
            if (string.IsNullOrWhiteSpace(toId)) return "要断开的目标节点 id 不能为空";
            Unlink(data, toId.Trim());
            return null;
        }

        public static string AddDialogueLogic(DialogueData data, string nodeId, NarrativeLogicKind kind, string typeId, string paramsJson)
        {
            if (data == null) return "当前没有打开的对话";
            if (kind == NarrativeLogicKind.Task) return "对话节点上没有任务槽，任务挂在任务分支上";

            string registration = CheckRegistration(kind, typeId);
            if (registration != null) return registration;

            Dictionary<string, JsonElement> fields;
            string parseError = ParseParams(paramsJson, out fields);
            if (parseError != null) return parseError;

            JsonElement element = BuildLogicElement(typeId.Trim(), fields);

            var npc = FindNpcReply(data, nodeId);
            if (npc != null)
            {
                if (kind == NarrativeLogicKind.Event)
                {
                    npc.Events = npc.Events ?? new List<JsonElement>();
                    npc.Events.Add(element);
                }
                else
                {
                    npc.Conditions = npc.Conditions ?? new List<JsonElement>();
                    npc.Conditions.Add(element);
                }
                return null;
            }

            var player = FindPlayerReply(data, nodeId);
            if (player != null)
            {
                if (kind == NarrativeLogicKind.Event)
                {
                    player.Events = player.Events ?? new List<JsonElement>();
                    player.Events.Add(element);
                }
                else
                {
                    player.Conditions = player.Conditions ?? new List<JsonElement>();
                    player.Conditions.Add(element);
                }
                return null;
            }

            return $"找不到节点 '{nodeId}'";
        }

        public static string RemoveDialogueLogic(DialogueData data, string nodeId, NarrativeLogicKind kind, string indexText)
        {
            if (data == null) return "当前没有打开的对话";
            if (kind == NarrativeLogicKind.Task) return "对话节点上没有任务槽，任务挂在任务分支上";

            int index;
            if (!int.TryParse(indexText, out index)) return "条目序号必须是整数";

            List<JsonElement> list = null;
            var npc = FindNpcReply(data, nodeId);
            if (npc != null) list = kind == NarrativeLogicKind.Event ? npc.Events : npc.Conditions;
            else
            {
                var player = FindPlayerReply(data, nodeId);
                if (player != null) list = kind == NarrativeLogicKind.Event ? player.Events : player.Conditions;
            }

            if (list == null) return $"找不到节点 '{nodeId}'";
            if (index < 0 || index >= list.Count) return $"序号 {index} 越界（共 {list.Count} 条）";

            list.RemoveAt(index);
            return null;
        }

        public static string SetDialogueRootNode(DialogueData data, string nodeId)
        {
            if (data == null) return "当前没有打开的对话";
            var node = FindNpcReply(data, nodeId);
            if (node == null) return $"找不到 NPC 节点 '{nodeId}'（根节点必须是 NPC 回复）";

            if (data.NpcReplies != null)
            {
                foreach (var reply in data.NpcReplies) reply.IsRoot = false;
            }
            node.IsRoot = true;
            return null;
        }

        private static void Unlink(DialogueData data, string toId)
        {
            if (data?.NpcReplies != null)
            {
                foreach (var npc in data.NpcReplies)
                {
                    npc.NpcReplies?.RemoveAll(id => string.Equals(id, toId, StringComparison.OrdinalIgnoreCase));
                    npc.PlayerReplies?.RemoveAll(id => string.Equals(id, toId, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (data?.PlayerReplies != null)
            {
                foreach (var player in data.PlayerReplies)
                {
                    player.NpcReplies?.RemoveAll(id => string.Equals(id, toId, StringComparison.OrdinalIgnoreCase));
                    player.PlayerReplies?.RemoveAll(id => string.Equals(id, toId, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        private static bool Contains(List<string> list, string value)
        {
            if (list == null || string.IsNullOrWhiteSpace(value)) return false;
            foreach (var item in list)
            {
                if (string.Equals(item, value.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static SpeakerData FindSpeaker(DialogueData data, string speakerId)
        {
            if (data?.Speakers == null || string.IsNullOrWhiteSpace(speakerId)) return null;
            foreach (var speaker in data.Speakers)
            {
                if (string.Equals(speaker.SpeakerId, speakerId.Trim(), StringComparison.OrdinalIgnoreCase)) return speaker;
            }
            return null;
        }

        private static NpcReplyData FindNpcReply(DialogueData data, string replyId)
        {
            if (data?.NpcReplies == null || string.IsNullOrWhiteSpace(replyId)) return null;
            foreach (var reply in data.NpcReplies)
            {
                if (string.Equals(reply.Id, replyId.Trim(), StringComparison.OrdinalIgnoreCase)) return reply;
            }
            return null;
        }

        private static PlayerReplyData FindPlayerReply(DialogueData data, string replyId)
        {
            if (data?.PlayerReplies == null || string.IsNullOrWhiteSpace(replyId)) return null;
            foreach (var reply in data.PlayerReplies)
            {
                if (string.Equals(reply.Id, replyId.Trim(), StringComparison.OrdinalIgnoreCase)) return reply;
            }
            return null;
        }

        private static bool ParseBool(string text, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            text = text.Trim();
            if (text == "1") return true;
            if (text == "0") return false;

            bool parsed;
            return bool.TryParse(text, out parsed) ? parsed : fallback;
        }

        #endregion

        #region 参数 JSON

        /// <summary>给作者看的参数样例（字段名即运行时逻辑类上的公共属性名，反射赋值时大小写不敏感）。</summary>
        public const string ParamsExample = "{ \"tag\": \"Quest.Started\", \"bAdd\": true }";

        private static string ParseParams(string paramsJson, out Dictionary<string, JsonElement> fields)
        {
            fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(paramsJson)) return null;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(paramsJson.Trim(),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            }
            catch (Exception ex)
            {
                return "参数不是合法 JSON：" + ex.Message + "（示例：" + ParamsExample + "）";
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return "参数必须是 JSON 对象，例如 { \"tag\": \"Quest.Started\" }";
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "type", StringComparison.OrdinalIgnoreCase)) continue;
                    fields[prop.Name] = prop.Value.Clone();
                }
            }
            return null;
        }

        /// <summary>拼出运行时要读的条目 JSON：判别字段 "type" 由注册表选中的名字写死，作者无法改写它。</summary>
        private static JsonElement BuildLogicElement(string typeId, Dictionary<string, JsonElement> fields)
        {
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["type"] = typeId };
            if (fields != null)
            {
                foreach (var field in fields) map[field.Key] = field.Value;
            }
            return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(map));
        }

        #endregion

        #region 内存快照（撤销 / 重做 / 脏标记对照）

        /// <summary>把内存里的任务序列化成快照串；撤销栈与"是否已保存"的对照都用它，绝不落盘。</summary>
        public static string SnapshotQuest(QuestData data)
        {
            return data == null ? null : JsonSerializer.Serialize(data, WriteOptions);
        }

        /// <summary>把内存里的对话序列化成快照串（口径与写盘一致）。</summary>
        public static string SnapshotDialogue(DialogueData data)
        {
            return data == null ? null : JsonSerializer.Serialize(data, WriteOptions);
        }

        /// <summary>从快照串还原任务；空串还原成 null，坏串直接抛（调用方是编辑器自己，不该出现坏串）。</summary>
        public static QuestData RestoreQuest(string snapshot)
        {
            return string.IsNullOrEmpty(snapshot) ? null : JsonSerializer.Deserialize<QuestData>(snapshot, ReadOptions);
        }

        /// <summary>从快照串还原对话。</summary>
        public static DialogueData RestoreDialogue(string snapshot)
        {
            return string.IsNullOrEmpty(snapshot) ? null : JsonSerializer.Deserialize<DialogueData>(snapshot, ReadOptions);
        }

        #endregion

        #region 编辑器视图支持（查找 / 改值 / 自动布局 / 内存校验）

        public static QuestStateData FindQuestState(QuestData data, string stateId) => FindState(data, stateId);

        public static QuestBranchData FindQuestBranch(QuestData data, string branchId) => FindBranch(data, branchId);

        public static NpcReplyData FindNpcNode(DialogueData data, string nodeId) => FindNpcReply(data, nodeId);

        public static PlayerReplyData FindPlayerNode(DialogueData data, string nodeId) => FindPlayerReply(data, nodeId);

        public static SpeakerData FindDialogueSpeaker(DialogueData data, string speakerId) => FindSpeaker(data, speakerId);

        /// <summary>任务里全部状态 id（按资产顺序）。分支端点用下拉选它，省掉手打 id 拼错。</summary>
        public static List<string> QuestStateIds(QuestData data)
        {
            var ids = new List<string>();
            foreach (var state in data?.States ?? new List<QuestStateData>())
            {
                if (!string.IsNullOrEmpty(state?.Id)) ids.Add(state.Id);
            }
            return ids;
        }

        /// <summary>对话里全部节点 id（NPC 在前、玩家选项在后）。连线目标下拉用它。</summary>
        public static List<string> DialogueNodeIds(DialogueData data)
        {
            var ids = new List<string>();
            foreach (var npc in data?.NpcReplies ?? new List<NpcReplyData>())
            {
                if (!string.IsNullOrEmpty(npc?.Id)) ids.Add(npc.Id);
            }
            foreach (var player in data?.PlayerReplies ?? new List<PlayerReplyData>())
            {
                if (!string.IsNullOrEmpty(player?.Id)) ids.Add(player.Id);
            }
            return ids;
        }

        /// <summary>磁盘上所有对话资产名——就是任务头 questDialogueClass 能填的那些值。</summary>
        public static List<string> DialogueAssetNames()
        {
            var names = new List<string>();
            foreach (var file in ListJsonFiles(DialoguesDirectory))
            {
                names.Add(Path.GetFileNameWithoutExtension(file));
            }
            return names;
        }

        /// <summary>磁盘上所有任务资产名——叙事里"穿插任务"事件（开始/重开/结算）能引用的任务类。</summary>
        public static List<string> QuestAssetNames()
        {
            var names = new List<string>();
            foreach (var file in ListJsonFiles(QuestsDirectory))
            {
                names.Add(Path.GetFileNameWithoutExtension(file));
            }
            return names;
        }

        /// <summary>指定任务资产里的状态 id（给事件向导的"起始状态"下拉用）。资产不存在时返回空表。</summary>
        public static List<string> QuestStateIdsFor(string questAssetName)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(questAssetName)) return ids;

            string path = QuestPathFor(questAssetName);
            // 文件不存在是下拉框的正常状态（还没建资产），不查也不喊
            if (!File.Exists(path)) return ids;

            try
            {
                var data = LoadQuestData(path);
                foreach (var state in data?.States ?? new List<QuestStateData>())
                {
                    if (!string.IsNullOrEmpty(state?.Id)) ids.Add(state.Id);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[NarrativePro] 读取任务 '{questAssetName}' 的状态失败: {ex.Message}");
            }
            return ids;
        }

        /// <summary>对话资产里所有节点 id（给 BeginDialogueEvent 的"起始节点"下拉用）。</summary>
        public static List<string> DialogueNodeIdsFor(string dialogueAssetName)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(dialogueAssetName)) return ids;

            string path = DialoguePathFor(dialogueAssetName);
            if (!File.Exists(path)) return ids;

            try
            {
                var data = LoadDialogueData(path);
                ids.AddRange(DialogueNodeIds(data));
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[NarrativePro] 读取对话 '{dialogueAssetName}' 的节点失败: {ex.Message}");
            }
            return ids;
        }

        /// <summary>
        /// 改对话级设置（电影黑边 / 禁止跳过 / 自由移动 / 可退出 / 优先级 / 默认镜头 / 自动结束距离）。
        /// 这些字段运行时由 <c>DialogueFactory</c> 读进 <c>Dialogue</c>，是"沉浸式过场"最直接的一组开关。
        /// </summary>
        public static string SetDialogueConfig(DialogueData data, bool showCinematicBars, bool unskippable,
            bool freeMovement, bool canBeExited, int priority, string defaultShot, float endDistance)
        {
            if (data == null) return "当前没有打开的对话";

            data.Config = data.Config ?? new DialogueConfigData();
            data.Config.ShowCinematicBars = showCinematicBars;
            data.Config.Unskippable = unskippable;
            data.Config.FreeMovement = freeMovement;
            data.Config.CanBeExited = canBeExited;
            data.Config.Priority = priority;
            data.Config.DefaultShotName = defaultShot ?? "";
            data.Config.EndDialogueDist = endDistance;
            return null;
        }

        /// <summary>改任务头部字段。名称/描述允许为空，运行时不校验它们。</summary>
        public static string SetQuestHeader(QuestData data, string name, string description, string dialogueClass)
        {
            if (data == null) return "当前没有打开的任务";
            data.QuestName = name ?? "";
            data.QuestDescription = description ?? "";
            data.QuestDialogueClass = dialogueClass ?? "";
            return null;
        }

        /// <summary>改状态的描述与类型（id 是图的主键，不允许在这里改）。</summary>
        public static string SetQuestState(QuestData data, string stateId, string description, string stateType)
        {
            var state = FindState(data, stateId);
            if (state == null) return $"找不到状态 '{stateId}'";

            EStateNodeType type;
            string error = TryParseStateType(stateType, out type);
            if (error != null) return error;

            state.Description = description ?? "";
            state.StateType = type;
            return null;
        }

        /// <summary>改分支的描述、端点与隐藏标记。</summary>
        public static string SetQuestBranch(QuestData data, string branchId, string description, string fromStateId, string toStateId, string hidden)
        {
            var branch = FindBranch(data, branchId);
            if (branch == null) return $"找不到分支 '{branchId}'";
            if (FindState(data, fromStateId) == null) return $"找不到起始状态 '{fromStateId}'";
            if (FindState(data, toStateId) == null) return $"找不到目标状态 '{toStateId}'";

            branch.Description = description ?? "";
            branch.FromStateId = fromStateId.Trim();
            branch.ToStateId = toStateId.Trim();
            branch.Hidden = ParseBool(hidden, branch.Hidden);
            return null;
        }

        /// <summary>改 NPC 节点。isRoot 只有填 1 才会抢根标记，填 0 不会把根摘掉（运行时要求恰好一个根）。</summary>
        public static string SetDialogueNpc(DialogueData data, string nodeId, string speakerId, string text, string isSkippable, string isRoot)
        {
            var node = FindNpcReply(data, nodeId);
            if (node == null) return $"找不到 NPC 节点 '{nodeId}'";
            if (FindSpeaker(data, speakerId) == null) return $"说话者 '{speakerId}' 不在 speakers 列表里，运行时会找不到发言人";

            node.SpeakerId = speakerId.Trim();
            node.IsSkippable = ParseBool(isSkippable, node.IsSkippable);
            if (node.Line == null) node.Line = new DialogueLineData { Duration = ELineDuration.Default };
            node.Line.Text = text ?? "";

            if (ParseBool(isRoot, false)) return SetDialogueRootNode(data, nodeId);
            return null;
        }

        /// <summary>改玩家选项节点。</summary>
        public static string SetDialoguePlayer(DialogueData data, string nodeId, string optionText, string text,
                                               string autoSelect, string autoSelectIfOnlyReply)
        {
            var node = FindPlayerReply(data, nodeId);
            if (node == null) return $"找不到玩家节点 '{nodeId}'";

            node.OptionText = optionText ?? "";
            node.AutoSelect = ParseBool(autoSelect, node.AutoSelect);
            node.AutoSelectIfOnlyReply = ParseBool(autoSelectIfOnlyReply, node.AutoSelectIfOnlyReply);
            if (node.Line == null) node.Line = new DialogueLineData { Duration = ELineDuration.Default };
            node.Line.Text = text ?? "";
            return null;
        }

        public static string SetDialogueSpeaker(DialogueData data, string speakerId, string displayName, string isPlayer)
        {
            var speaker = FindSpeaker(data, speakerId);
            if (speaker == null) return $"找不到说话者 '{speakerId}'";
            speaker.DisplayName = string.IsNullOrWhiteSpace(displayName) ? speaker.SpeakerId : displayName;
            speaker.IsPlayer = ParseBool(isPlayer, speaker.IsPlayer);
            return null;
        }

        /// <summary>图里拖动节点后写回位置。任务的状态与分支共用一个 id 空间，先找状态再找分支。</summary>
        public static string SetQuestNodePosition(QuestData data, string nodeId, float x, float y)
        {
            var state = FindState(data, nodeId);
            if (state != null)
            {
                state.Position = new NodePositionData { X = x, Y = y };
                return null;
            }

            var branch = FindBranch(data, nodeId);
            if (branch != null)
            {
                branch.Position = new NodePositionData { X = x, Y = y };
                return null;
            }

            return $"找不到节点 '{nodeId}'";
        }

        public static string SetDialogueNodePosition(DialogueData data, string nodeId, float x, float y)
        {
            var npc = FindNpcReply(data, nodeId);
            if (npc != null)
            {
                npc.NodePosX = x;
                npc.NodePosY = y;
                return null;
            }

            var player = FindPlayerReply(data, nodeId);
            if (player != null)
            {
                player.NodePosX = x;
                player.NodePosY = y;
                return null;
            }

            return $"找不到节点 '{nodeId}'";
        }

        /// <summary>断一条具体连线（与 <see cref="UnlinkDialogue"/> 的"断开所有指向"不同）。</summary>
        public static string RemoveDialogueLink(DialogueData data, string fromId, string toId)
        {
            if (data == null) return "当前没有打开的对话";
            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId)) return "连线两端都不能为空";

            var npcFrom = FindNpcReply(data, fromId);
            if (npcFrom != null)
            {
                npcFrom.NpcReplies?.RemoveAll(id => string.Equals(id, toId.Trim(), StringComparison.OrdinalIgnoreCase));
                npcFrom.PlayerReplies?.RemoveAll(id => string.Equals(id, toId.Trim(), StringComparison.OrdinalIgnoreCase));
                return null;
            }

            var playerFrom = FindPlayerReply(data, fromId);
            if (playerFrom != null)
            {
                playerFrom.NpcReplies?.RemoveAll(id => string.Equals(id, toId.Trim(), StringComparison.OrdinalIgnoreCase));
                return null;
            }

            return $"找不到节点 '{fromId}'";
        }

        /// <summary>按目标节点类型自动选连线种类（NPC→NPC / NPC→玩家 / 玩家→NPC）。</summary>
        public static string LinkDialogueAuto(DialogueData data, string fromId, string toId)
        {
            if (FindPlayerReply(data, toId) != null)
                return LinkDialogue(data, fromId, toId, DialogueLinkKind.NpcToPlayer);

            if (FindNpcReply(data, toId) != null)
            {
                var kind = FindPlayerReply(data, fromId) != null ? DialogueLinkKind.PlayerToNpc : DialogueLinkKind.NpcToNpc;
                return LinkDialogue(data, fromId, toId, kind);
            }

            return $"找不到目标节点 '{toId}'";
        }

        /// <summary>
        /// 内存校验：把当前内存里的 DTO 落一份临时文件，再走与运行时同一条装载/注册表检查。
        /// 临时文件写在系统临时目录，不污染 Content；校验完即删。
        /// </summary>
        public static List<string> ValidateQuestData(QuestData data) => ValidateSerialized(data, true);

        public static List<string> ValidateDialogueData(DialogueData data) => ValidateSerialized(data, false);

        private static List<string> ValidateSerialized(object data, bool isQuest)
        {
            if (data == null) return new List<string> { "没有可校验的资产" };

            string temp = Path.Combine(Path.GetTempPath(), "narrative_validate_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(data, WriteOptions));
                return isQuest ? ValidateQuestFile(temp) : ValidateDialogueFile(temp);
            }
            catch (Exception ex)
            {
                return new List<string> { "临时校验文件写入失败：" + ex.Message };
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch { }
            }
        }

        /// <summary>新资产的节点位置全挤在原点时（手写/模板生成的旧文件常见），打开后先自动排一遍。</summary>
        public static bool IsQuestLayoutDegenerate(QuestData data)
        {
            if (data?.States == null) return false;
            int count = data.States.Count + (data.Branches?.Count ?? 0);
            if (count < 2) return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in data.States) seen.Add(CellOf(state.Position));
            if (data.Branches != null)
            {
                foreach (var branch in data.Branches) seen.Add(CellOf(branch.Position));
            }
            return seen.Count <= 1;
        }

        public static bool IsDialogueLayoutDegenerate(DialogueData data)
        {
            if (data == null) return false;
            int count = (data.NpcReplies?.Count ?? 0) + (data.PlayerReplies?.Count ?? 0);
            if (count < 2) return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (data.NpcReplies != null)
            {
                foreach (var node in data.NpcReplies) seen.Add(CellOf(node.NodePosX, node.NodePosY));
            }
            if (data.PlayerReplies != null)
            {
                foreach (var node in data.PlayerReplies) seen.Add(CellOf(node.NodePosX, node.NodePosY));
            }
            return seen.Count <= 1;
        }

        private static string CellOf(NodePositionData position) =>
            position == null ? "0:0" : CellOf(position.X, position.Y);

        private static string CellOf(float x, float y) =>
            (int)Math.Round(x / 20f) + ":" + (int)Math.Round(y / 20f);

        /// <summary>
        /// 是否存在两个节点挤在同一个格子里（手写资产常常只写了状态位置、分支留在 0,0）。
        /// 这种情况打开时也要自动排一遍，否则卡片会叠在一起看不出图。
        /// </summary>
        public static bool HasStackedQuestNodes(QuestData data)
        {
            if (data?.States == null) return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in data.States)
            {
                if (!seen.Add(CellOf(state.Position))) return true;
            }
            foreach (var branch in data.Branches ?? new List<QuestBranchData>())
            {
                if (!seen.Add(CellOf(branch.Position))) return true;
            }
            return false;
        }

        public static bool HasStackedDialogueNodes(DialogueData data)
        {
            if (data == null) return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
            {
                if (!seen.Add(CellOf(npc.NodePosX, npc.NodePosY))) return true;
            }
            foreach (var player in data.PlayerReplies ?? new List<PlayerReplyData>())
            {
                if (!seen.Add(CellOf(player.NodePosX, player.NodePosY))) return true;
            }
            return false;
        }

        /// <summary>
        /// 任务图分层自动布局：从起始状态按分支做 BFS，层号即列号；不可达状态放到最后一列。
        /// 分支节点落在两端状态的中点并向下偏移，避免压住状态卡片。
        /// </summary>
        public static void LayoutQuest(QuestData data)
        {
            if (data?.States == null || data.States.Count == 0) return;

            var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var outgoing = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var branch in data.Branches ?? new List<QuestBranchData>())
            {
                string from = branch.FromStateId ?? "";
                if (!outgoing.TryGetValue(from, out var list))
                {
                    list = new List<string>();
                    outgoing[from] = list;
                }
                list.Add(branch.ToStateId ?? "");
            }

            string start = data.StartStateId;
            if (string.IsNullOrWhiteSpace(start) || FindState(data, start) == null) start = data.States[0].Id ?? "";

            var queue = new Queue<string>();
            if (!string.IsNullOrEmpty(start))
            {
                depth[start] = 0;
                queue.Enqueue(start);
            }

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!outgoing.TryGetValue(current, out var next)) continue;
                int nextDepth = depth[current] + 1;
                foreach (var id in next)
                {
                    if (string.IsNullOrWhiteSpace(id) || depth.ContainsKey(id)) continue;
                    depth[id] = nextDepth;
                    queue.Enqueue(id);
                }
            }

            int maxDepth = 0;
            foreach (var value in depth.Values) maxDepth = Math.Max(maxDepth, value);
            foreach (var state in data.States)
            {
                if (!depth.ContainsKey(state.Id ?? "")) depth[state.Id ?? ""] = maxDepth + 1;
            }

            var countPerDepth = new Dictionary<int, int>();
            foreach (var state in data.States)
            {
                int column = depth[state.Id ?? ""];
                countPerDepth.TryGetValue(column, out int rows);
                countPerDepth[column] = rows + 1;
            }

            var placed = new Dictionary<int, int>();
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in data.States)
            {
                int column = depth[state.Id ?? ""];
                placed.TryGetValue(column, out int row);
                placed[column] = row + 1;

                float center = (countPerDepth[column] - 1) * 0.5f;
                state.Position = new NodePositionData
                {
                    X = column * 320f,
                    Y = (row - center) * 140f
                };
                occupied.Add(CellOf(state.Position));
            }

            foreach (var branch in data.Branches ?? new List<QuestBranchData>())
            {
                var from = FindState(data, branch.FromStateId);
                var to = FindState(data, branch.ToStateId);
                float x = 60f;
                float y = 40f;
                if (from?.Position != null && to?.Position != null)
                {
                    x = (from.Position.X + to.Position.X) * 0.5f + 60f;
                    y = (from.Position.Y + to.Position.Y) * 0.5f + 52f;
                }

                // 同一对状态之间的并行分支会算到同一个中点，往下错开，避免叠在一起。
                int guard = 0;
                while (occupied.Contains(CellOf(x, y)) && guard++ < 60) y += 72f;

                branch.Position = new NodePositionData { X = x, Y = y };
                occupied.Add(CellOf(x, y));
            }
        }

        /// <summary>
        /// 对话图分层布局：NPC 链按 BFS 层号分列；玩家选项挂在父 NPC 右侧一列；不可达节点排到最下方。
        /// </summary>
        public static void LayoutDialogue(DialogueData data)
        {
            if (data == null) return;

            var npcById = new Dictionary<string, NpcReplyData>(StringComparer.OrdinalIgnoreCase);
            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
            {
                if (!string.IsNullOrEmpty(npc.Id)) npcById[npc.Id] = npc;
            }

            var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();

            NpcReplyData root = null;
            foreach (var npc in npcById.Values)
            {
                if (npc.IsRoot) { root = npc; break; }
            }
            if (root == null && (data.NpcReplies?.Count ?? 0) > 0) root = data.NpcReplies[0];

            if (root != null && !string.IsNullOrEmpty(root.Id))
            {
                depth[root.Id] = 0;
                queue.Enqueue(root.Id);
            }

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!npcById.TryGetValue(current, out var node) || node.NpcReplies == null) continue;
                int nextDepth = depth[current] + 1;
                foreach (var id in node.NpcReplies)
                {
                    if (string.IsNullOrWhiteSpace(id) || depth.ContainsKey(id) || !npcById.ContainsKey(id)) continue;
                    depth[id] = nextDepth;
                    queue.Enqueue(id);
                }
            }

            int maxDepth = 0;
            foreach (var value in depth.Values) maxDepth = Math.Max(maxDepth, value);
            foreach (var npc in npcById.Values)
            {
                if (!depth.ContainsKey(npc.Id ?? "")) depth[npc.Id ?? ""] = maxDepth + 1;
            }

            var countPerDepth = new Dictionary<int, int>();
            foreach (var npc in npcById.Values)
            {
                int column = depth[npc.Id ?? ""];
                countPerDepth.TryGetValue(column, out int rows);
                countPerDepth[column] = rows + 1;
            }

            var placed = new Dictionary<int, int>();
            foreach (var npc in npcById.Values)
            {
                int column = depth[npc.Id ?? ""];
                placed.TryGetValue(column, out int row);
                placed[column] = row + 1;

                float center = (countPerDepth[column] - 1) * 0.5f;
                npc.NodePosX = column * 340f;
                npc.NodePosY = (row - center) * 160f;
            }

            // 玩家选项挂在“第一个指向它的 NPC”右侧，按父节点纵向排开。
            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var npc in npcById.Values)
            {
                if (npc.PlayerReplies == null) continue;
                int index = 0;
                foreach (var playerId in npc.PlayerReplies)
                {
                    var player = FindPlayerReply(data, playerId);
                    if (player == null || !assigned.Add(player.Id ?? "")) continue;

                    player.NodePosX = npc.NodePosX + 210f;
                    player.NodePosY = npc.NodePosY + 92f + index * 92f;
                    index++;
                }
            }

            // 没被任何 NPC 指到的玩家节点（孤立/手写）放到最下面一排。
            int orphan = 0;
            foreach (var player in data.PlayerReplies ?? new List<PlayerReplyData>())
            {
                if (!assigned.Add(player.Id ?? "")) continue;
                player.NodePosX = orphan * 250f;
                player.NodePosY = 520f;
                orphan++;
            }
        }

        /// <summary>复制状态（含事件/条件），位置向右下偏移一点；返回 null 表示成功。</summary>
        public static string DuplicateQuestState(QuestData data, string sourceId, out string newId)
        {
            newId = "";
            var source = FindState(data, sourceId);
            if (source == null) return $"找不到状态 '{sourceId}'";

            newId = UniqueId(id => FindState(data, id) != null, (sourceId ?? "state").Trim() + "_copy");
            var position = source.Position ?? new NodePositionData();
            data.States.Add(new QuestStateData
            {
                Id = newId,
                Description = source.Description,
                StateType = source.StateType,
                Position = new NodePositionData { X = position.X + 40f, Y = position.Y + 90f },
                Events = source.Events == null ? null : new List<JsonElement>(source.Events),
                Conditions = source.Conditions == null ? null : new List<JsonElement>(source.Conditions)
            });
            return null;
        }

        /// <summary>复制分支（含任务与事件/条件）。from/to 端点保持原样。</summary>
        public static string DuplicateQuestBranch(QuestData data, string sourceId, out string newId)
        {
            newId = "";
            var source = FindBranch(data, sourceId);
            if (source == null) return $"找不到分支 '{sourceId}'";

            newId = UniqueId(id => FindBranch(data, id) != null, (sourceId ?? "branch").Trim() + "_copy");
            var position = source.Position ?? new NodePositionData();
            var tasks = new List<TaskData>();
            foreach (var task in source.Tasks ?? new List<TaskData>())
            {
                var copy = new TaskData
                {
                    Type = task.Type,
                    TargetId = task.TargetId,
                    RequiredQuantity = task.RequiredQuantity,
                    Description = task.Description,
                    ExtraFields = new Dictionary<string, JsonElement>(task.ExtraFields ?? new Dictionary<string, JsonElement>(),
                        StringComparer.OrdinalIgnoreCase)
                };
                tasks.Add(copy);
            }

            data.Branches.Add(new QuestBranchData
            {
                Id = newId,
                Description = source.Description,
                FromStateId = source.FromStateId,
                ToStateId = source.ToStateId,
                Hidden = source.Hidden,
                Position = new NodePositionData { X = position.X + 30f, Y = position.Y + 80f },
                Tasks = tasks,
                Events = source.Events == null ? null : new List<JsonElement>(source.Events),
                Conditions = source.Conditions == null ? null : new List<JsonElement>(source.Conditions)
            });
            return null;
        }

        /// <summary>复制 NPC 节点（含台词、事件/条件；连线不复制，避免出现预期外的分支）。</summary>
        public static string DuplicateDialogueNpc(DialogueData data, string sourceId, out string newId)
        {
            newId = "";
            var source = FindNpcReply(data, sourceId);
            if (source == null) return $"找不到 NPC 节点 '{sourceId}'";

            newId = UniqueId(id => FindNpcReply(data, id) != null || FindPlayerReply(data, id) != null,
                (sourceId ?? "npc").Trim() + "_copy");
            if (data.NpcReplies == null) data.NpcReplies = new List<NpcReplyData>();
            data.NpcReplies.Add(new NpcReplyData
            {
                Id = newId,
                SpeakerId = source.SpeakerId,
                IsRoot = false, // 根节点只能有一个，复制体一律不是根
                IsSkippable = source.IsSkippable,
                Line = CopyLine(source.Line),
                NpcReplies = new List<string>(),
                PlayerReplies = new List<string>(),
                NodePosX = source.NodePosX + 40f,
                NodePosY = source.NodePosY + 110f,
                Events = source.Events == null ? null : new List<JsonElement>(source.Events),
                Conditions = source.Conditions == null ? null : new List<JsonElement>(source.Conditions)
            });
            return null;
        }

        /// <summary>复制玩家选项节点（含台词、事件/条件；不复制连线）。</summary>
        public static string DuplicateDialoguePlayer(DialogueData data, string sourceId, out string newId)
        {
            newId = "";
            var source = FindPlayerReply(data, sourceId);
            if (source == null) return $"找不到玩家节点 '{sourceId}'";

            newId = UniqueId(id => FindNpcReply(data, id) != null || FindPlayerReply(data, id) != null,
                (sourceId ?? "player").Trim() + "_copy");
            if (data.PlayerReplies == null) data.PlayerReplies = new List<PlayerReplyData>();
            data.PlayerReplies.Add(new PlayerReplyData
            {
                Id = newId,
                OptionText = source.OptionText,
                HintText = source.HintText,
                AutoSelect = source.AutoSelect,
                AutoSelectIfOnlyReply = source.AutoSelectIfOnlyReply,
                Line = CopyLine(source.Line),
                NpcReplies = new List<string>(),
                PlayerReplies = new List<string>(),
                NodePosX = source.NodePosX + 40f,
                NodePosY = source.NodePosY + 110f,
                Events = source.Events == null ? null : new List<JsonElement>(source.Events),
                Conditions = source.Conditions == null ? null : new List<JsonElement>(source.Conditions)
            });
            return null;
        }

        private static DialogueLineData CopyLine(DialogueLineData line)
        {
            if (line == null) return new DialogueLineData { Duration = ELineDuration.Default };
            return new DialogueLineData
            {
                Text = line.Text,
                Duration = line.Duration,
                DurationSecondsOverride = line.DurationSecondsOverride,
                SoundPath = line.SoundPath,
                BodyAnimationName = line.BodyAnimationName,
                FacialAnimationName = line.FacialAnimationName,
                ShotName = line.ShotName,
                Shot = line.Shot,
                Conditions = line.Conditions == null ? null : new List<JsonElement>(line.Conditions)
            };
        }

        private static string UniqueId(Func<string, bool> exists, string baseId)
        {
            string candidate = string.IsNullOrWhiteSpace(baseId) ? "copy" : baseId.Trim();
            if (!exists(candidate)) return candidate;

            for (int i = 2; i < 1000; i++)
            {
                string numbered = candidate + "_" + i;
                if (!exists(numbered)) return numbered;
            }
            return candidate + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>重命名状态：同步改写起始状态与所有分支端点（id 是图的主键）。</summary>
        public static string RenameQuestState(QuestData data, string oldId, string newId)
        {
            var state = FindState(data, oldId);
            if (state == null) return $"找不到状态 '{oldId}'";
            if (string.IsNullOrWhiteSpace(newId)) return "新 id 不能为空";

            string trimmed = newId.Trim();
            if (string.Equals(state.Id, trimmed, StringComparison.Ordinal)) return null;
            if (FindState(data, trimmed) != null || FindBranch(data, trimmed) != null)
                return $"id '{trimmed}' 已被状态或分支占用";

            string previous = state.Id;
            state.Id = trimmed;

            foreach (var branch in data.Branches ?? new List<QuestBranchData>())
            {
                if (string.Equals(branch.FromStateId, previous, StringComparison.OrdinalIgnoreCase)) branch.FromStateId = trimmed;
                if (string.Equals(branch.ToStateId, previous, StringComparison.OrdinalIgnoreCase)) branch.ToStateId = trimmed;
            }
            if (string.Equals(data.StartStateId, previous, StringComparison.OrdinalIgnoreCase)) data.StartStateId = trimmed;
            return null;
        }

        public static string RenameQuestBranch(QuestData data, string oldId, string newId)
        {
            var branch = FindBranch(data, oldId);
            if (branch == null) return $"找不到分支 '{oldId}'";
            if (string.IsNullOrWhiteSpace(newId)) return "新 id 不能为空";

            string trimmed = newId.Trim();
            if (string.Equals(branch.Id, trimmed, StringComparison.Ordinal)) return null;
            if (FindBranch(data, trimmed) != null || FindState(data, trimmed) != null)
                return $"id '{trimmed}' 已被状态或分支占用";

            branch.Id = trimmed;
            return null;
        }

        /// <summary>重命名对话节点：同步改写所有 NPC/玩家邻接表里的引用。</summary>
        public static string RenameDialogueNode(DialogueData data, string oldId, string newId)
        {
            if (data == null) return "当前没有打开的对话";
            var npc = FindNpcReply(data, oldId);
            var player = npc == null ? FindPlayerReply(data, oldId) : null;
            if (npc == null && player == null) return $"找不到节点 '{oldId}'";
            if (string.IsNullOrWhiteSpace(newId)) return "新 id 不能为空";

            string trimmed = newId.Trim();
            string previous = npc != null ? npc.Id : player.Id;
            if (string.Equals(previous, trimmed, StringComparison.Ordinal)) return null;
            if (FindNpcReply(data, trimmed) != null || FindPlayerReply(data, trimmed) != null)
                return $"节点 id '{trimmed}' 已被占用";

            if (npc != null) npc.Id = trimmed;
            else player.Id = trimmed;

            ReplaceDialogueReference(data, previous, trimmed);
            return null;
        }

        /// <summary>重命名说话者：同步改写所有 NPC 节点上的 speakerId。</summary>
        public static string RenameDialogueSpeaker(DialogueData data, string oldId, string newId)
        {
            var speaker = FindSpeaker(data, oldId);
            if (speaker == null) return $"找不到说话者 '{oldId}'";
            if (string.IsNullOrWhiteSpace(newId)) return "新 id 不能为空";

            string trimmed = newId.Trim();
            if (string.Equals(speaker.SpeakerId, trimmed, StringComparison.Ordinal)) return null;
            if (FindSpeaker(data, trimmed) != null) return $"说话者 id '{trimmed}' 已被占用";

            string previous = speaker.SpeakerId;
            speaker.SpeakerId = trimmed;
            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
            {
                if (string.Equals(npc.SpeakerId, previous, StringComparison.OrdinalIgnoreCase)) npc.SpeakerId = trimmed;
            }
            return null;
        }

        private static void ReplaceDialogueReference(DialogueData data, string oldId, string newId)
        {
            foreach (var npc in data.NpcReplies ?? new List<NpcReplyData>())
            {
                ReplaceInList(npc.NpcReplies, oldId, newId);
                ReplaceInList(npc.PlayerReplies, oldId, newId);
            }
            foreach (var player in data.PlayerReplies ?? new List<PlayerReplyData>())
            {
                ReplaceInList(player.NpcReplies, oldId, newId);
                ReplaceInList(player.PlayerReplies, oldId, newId);
            }
        }

        private static void ReplaceInList(List<string> list, string oldId, string newId)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], oldId, StringComparison.OrdinalIgnoreCase)) list[i] = newId;
            }
        }

        #endregion

        /// <summary>把注册表名单与资产清单导成一行文本，供菜单命令写日志。</summary>
        public static string DescribeRegistry()
        {
            return $"任务：{Join(NarrativeLogicFactory.RegisteredTaskTypes)}\n" +
                   $"事件：{Join(NarrativeLogicFactory.RegisteredEventTypes)}\n" +
                   $"条件：{Join(NarrativeLogicFactory.RegisteredConditionTypes)}";
        }
    }
}
