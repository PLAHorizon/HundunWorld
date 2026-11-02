using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using HundunWorld.Game.UI.Ink.Pages.CharacterCreation;
using MetaHuman;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HundunWorld.Game.Services
{
    /// <summary>
    /// 数字人外观的服务端同步：把本地捏出来的差量存档上传，落 CharacterGrain 状态。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么要这一层：捏脸结果原本只写在本机 <c>.mhl</c> 文件里（<see cref="MetaHumanLookStore"/>），
    /// 换设备或清了目录就回到默认脸。服务端把 blob 当不透明数据存，
    /// 但三道闸门在服务端：已发布拒改、模板一致、校验和由服务端另算一遍——
    /// 客户端自述的校验和只能证明它想让我存什么，不能证明数据完整。
    /// </para>
    /// <para>
    /// 回执要么按成功处理、要么带原因回来：不允许「发了就当存上了」。
    /// 因此 <see cref="LastErrorCode"/> 与事件都会把失败显式暴露给 UI，而不是只写一行日志。
    /// </para>
    /// </remarks>
    public class MetaHumanAppearanceService
    {
        public static MetaHumanAppearanceService Instance { get; } = new MetaHumanAppearanceService();

        /// <summary>最近一次提交的失败原因（成功时为空）。UI 用它决定是提示「已同步」还是「未同步」。</summary>
        public string LastErrorCode { get; private set; } = "";

        /// <summary>本会话已经补传过本地快照的角色（列表刷新会重复调用采纳，避免重复上传推高修订号）。</summary>
        private readonly HashSet<long> _healSent = new HashSet<long>();

        /// <summary>远端玩家外观：按角色 Id 缓存服务端下发的解析结果；请求去重避免每帧重复拉。</summary>
        private readonly Dictionary<ulong, MetaHuman.MetaHumanLook> _remoteLooks =
            new Dictionary<ulong, MetaHuman.MetaHumanLook>();
        private readonly HashSet<ulong> _remoteRequested = new HashSet<ulong>();

        /// <summary>远端头发的离散发色下标（随外观读取回执一起下发）。</summary>
        private readonly Dictionary<ulong, int> _remoteHairColor = new Dictionary<ulong, int>();

        /// <summary>网络未就绪/发送失败时挂起的补传；由 <see cref="TickPendingHeals"/> 每帧重试。</summary>
        private readonly HashSet<long> _pendingHeal = new HashSet<long>();

        /// <summary>下一次允许重试的时间（2 秒节流，避免发送一直失败时刷爆轨迹文件）。</summary>
        private DateTime _nextHealAttemptUtc = DateTime.MinValue;

        /// <summary>是否见过断线：网络恢复后要清掉「本会话已补传」标记，否则服务器重启后永远不再补传。</summary>
        private bool _networkDownSeen;

        /// <summary>
        /// 外观同步的落盘轨迹：编辑器磁盘日志落后且中文会被 NUL 填充，排障时只能靠这份 ASCII 文件。
        /// 只追加、不抛错（诊断不能把主流程弄挂）。
        /// </summary>
        public static void Trace(string message)
        {
            try
            {
                string dir = Path.Combine(Globals.ProjectFolder, "Saved", "MetaHuman");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "appearance_sync.log"),
                    DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch
            {
                // 诊断失败不影响同步
            }
        }

        /// <summary>服务端确认存下的校验和；0 表示还没成功存过任何一份。</summary>
        public uint StoredChecksum { get; private set; }

        public int StoredRevision { get; private set; }

        /// <summary>同步结果回调（主线程）。参数：是否成功、原因、服务端 revision。</summary>
        public event Action<bool, string, int> OnSyncResult;

        /// <summary>
        /// 服务端判定「这份存档已发布、后续一律拒收」时触发（主线程）。
        /// 必须有人消费：否则玩家会一路编辑、每次提交都被拒却不知道为什么。
        /// 判定用的是回执里的状态位，不是 Error 的文案 —— 文案给人看，改一次措辞就会把这条判反。
        /// </summary>
        public event Action OnServerSealed;

        private MetaHumanAppearanceService()
        {
        }

        /// <summary>
        /// 上传一份外观。返回 false 表示「没发出去」（网络未连接/构造失败），
        /// 与「服务端拒绝」是两件事，后者走 <see cref="OnSyncResult"/>。
        /// </summary>
        public bool TrySendLook(long characterId, MetaHumanLook look, string templateId, string templateVersion,
                                 int templateParamCount, int entryCount)
        {
            LastErrorCode = "";

            if (look == null || string.IsNullOrEmpty(templateId))
            {
                LastErrorCode = "没有可上传的外观或模板标识";
                Raise(false, LastErrorCode, 0);
                return false;
            }

            byte[] blob = look.ToBytes();
            if (blob == null || blob.Length == 0)
            {
                Trace("drop char=" + characterId + " reason=empty-blob");
                LastErrorCode = "外观导出为空";
                Raise(false, LastErrorCode, 0);
                return false;
            }

            var payload = new MetaHumanLookPayload
            {
                TemplateId = templateId,
                TemplateVersion = templateVersion,
                TemplateParamCount = templateParamCount,
                EntryCount = entryCount,
                Blob = blob,
                // 传输层校验和：服务端会用同一个算法另算一遍再比，不信任这里的值本身
                BlobChecksum = MetaHumanLookBlobHash.Compute(blob),
                Published = look.Published,
                SavedAtUtcTicks = DateTime.UtcNow.Ticks,
            };

            if (HundunWorldGame.Instance?.NetworkManager == null)
            {
                Trace("drop char=" + characterId + " reason=no-network");
                LastErrorCode = "网络未连接";
                Raise(false, LastErrorCode, 0);
                return false;
            }

            Trace("queued char=" + characterId + " bytes=" + blob.Length + " tpl=" + templateId
                  + " ver=" + templateVersion + " published=" + look.Published + " rev=" + look.Revision);
            _ = SendAsync(characterId, payload);
            return true;
        }

        private async Task SendAsync(long characterId, MetaHumanLookPayload payload)
        {
            try
            {
                bool sent = await HundunWorldGame.Instance.NetworkManager.SendAsync(new UpdateCharacterAppearanceRequest
                {
                    CharacterId = (ulong)characterId,
                    Look = payload,
                });

                if (!sent)
                {
                    Trace("send-failed char=" + characterId);
                    QueueHeal(characterId);
                    LastErrorCode = "外观提交未发出";
                    Raise(false, LastErrorCode, StoredRevision);
                }
                else
                {
                    Trace("sent char=" + characterId);
                }
            }
            catch (Exception ex)
            {
                Trace("send-ex char=" + characterId + " " + ex.GetType().Name + " " + ex.Message);
                QueueHeal(characterId);
                LastErrorCode = "外观提交异常：" + ex.Message;
                Debug.LogException(ex);
                Raise(false, LastErrorCode, StoredRevision);
            }
        }

        /// <summary>发送失败（或异常）后重新挂起，等网络就绪再试。</summary>
        public void QueueHeal(long characterId)
        {
            if (characterId <= 0L)
            {
                return;
            }
            _healSent.Remove(characterId);
            if (_pendingHeal.Add(characterId))
            {
                Trace("heal-pending char=" + characterId + " reason=send-failed");
            }
        }

        /// <summary>每帧调用：网络恢复后把挂起的补传再发一次。</summary>
        public void TickPendingHeals()
        {
            var net = HundunWorldGame.Instance?.NetworkManager;
            if (net == null)
            {
                return;
            }

            if (!net.CanSendMessage())
            {
                _networkDownSeen = true;
                return;
            }

            // 服务器重启/断线重连后，服务端可能已经没有这份外观；不清标记就永远不再补传。
            if (_networkDownSeen)
            {
                _networkDownSeen = false;
                if (_healSent.Count > 0)
                {
                    Trace("heal-reset reason=network-reconnected count=" + _healSent.Count);
                    _healSent.Clear();
                }
            }

            if (_pendingHeal.Count == 0)
            {
                return;
            }

            if (DateTime.UtcNow < _nextHealAttemptUtc)
            {
                return;
            }
            _nextHealAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            long[] ids = new long[_pendingHeal.Count];
            _pendingHeal.CopyTo(ids);
            foreach (long id in ids)
            {
                _pendingHeal.Remove(id);
                HealServerFromLocal(id);
            }
        }

        /// <summary>
        /// 服务端没有该角色外观、本机有快照时补传一次（每会话每角色一次）。
        /// 为什么单独暴露：创角时的提交可能发生在网络/时序不稳的时刻，而"采纳服务端"这条旧通道
        /// 在当前 Ink 登录流程里并不一定被调用（2026-10-22 实测：登录进世界后 SQL 仍 NULL、且无采纳轨迹）。
        /// </summary>
        public void HealServerFromLocal(long characterId)
        {
            if (characterId <= 0L)
            {
                return;
            }

            // 创角/进场可能发生在网关握手完成之前：没连上就先挂起，别把"这次没发"记成"已经发过"。
            if (HundunWorldGame.Instance?.NetworkManager == null
                || !HundunWorldGame.Instance.NetworkManager.CanSendMessage())
            {
                if (_healSent.Contains(characterId))
                {
                    _healSent.Remove(characterId);
                }
                if (_pendingHeal.Add(characterId))
                {
                    Trace("heal-pending char=" + characterId + " reason=network-not-ready");
                }
                return;
            }

            if (!_healSent.Add(characterId))
            {
                return;
            }

            // 服务端已经存过外观就不用再补传：否则每次登录都把修订号 +1，仲裁永远判不出"一致"。
            if (ServerHasLook(characterId))
            {
                Trace("heal-skip char=" + characterId + " reason=server-has-look");
                return;
            }

            MetaHuman.MetaHumanLook local = MetaHuman.Runtime.MetaHumanLookStore.LoadLatest(characterId.ToString());
            Trace("heal-local char=" + characterId + " local=" + (local != null) +
                  " tpl=" + (local?.TemplateId ?? "<null>") + " rev=" + (local?.Revision ?? -1));
            if (local == null || string.IsNullOrEmpty(local.TemplateId))
            {
                return;
            }

            bool queued = TrySendLook(characterId, local, local.TemplateId,
                local.TemplateVersion ?? string.Empty, local.TemplateParamCount,
                local.Values?.Count ?? 0);
            Debug.Log(queued
                ? $"[MetaHumanAppearance] 服务端无外观，已补传本地快照：角色 {characterId} rev={local.Revision}"
                : $"[MetaHumanAppearance] 本地快照补传未发出：角色 {characterId} 原因={LastErrorCode}");
        }

        /// <summary>
        /// 远端角色进场后拉一次外观。玩家实体 Id 就是角色 Id（DS 建档时 EntityId=CharacterId）。
        /// 每个角色每会话只请求一次；失败时清掉标记，下次生成可重试。
        /// </summary>
        /// <summary>只读查询已缓存的外观（选角预览这类非世界场景用）。</summary>
        public bool TryGetCachedLook(ulong characterId, out MetaHuman.MetaHumanLook look)
        {
            return _remoteLooks.TryGetValue(characterId, out look);
        }

        public void RequestRemoteAppearance(ulong characterId)
        {
            if (characterId == 0UL || !_remoteRequested.Add(characterId))
            {
                return;
            }

            if (HundunWorldGame.Instance?.NetworkManager == null
                || !HundunWorldGame.Instance.NetworkManager.CanSendMessage())
            {
                _remoteRequested.Remove(characterId);
                Trace("remote-fetch-skip char=" + characterId + " reason=network-not-ready");
                return;
            }

            _ = SendRemoteFetchAsync(characterId);
        }

        private async Task SendRemoteFetchAsync(ulong characterId)
        {
            try
            {
                bool sent = await HundunWorldGame.Instance.NetworkManager.SendAsync(new GetCharacterAppearanceRequest
                {
                    CharacterId = characterId,
                });
                Trace((sent ? "remote-fetch-sent char=" : "remote-fetch-drop char=") + characterId);
                if (!sent)
                {
                    _remoteRequested.Remove(characterId);
                }
            }
            catch (Exception ex)
            {
                _remoteRequested.Remove(characterId);
                Trace("remote-fetch-ex char=" + characterId + " " + ex.GetType().Name + " " + ex.Message);
            }
        }

        /// <summary>由回执处理器在主线程调用：校验并缓存远端外观，命中 Actor 就立刻套上。</summary>
        public void OnRemoteAppearanceResponse(GetCharacterAppearanceResponse response)
        {
            if (response == null || response.CharacterId == 0UL)
            {
                return;
            }

            if (!response.Success)
            {
                _remoteRequested.Remove(response.CharacterId);
                Trace("remote-fetch-fail char=" + response.CharacterId + " err=" + response.Error);
                return;
            }

            MetaHumanLookPayload payload = response.Look;
            if (payload == null || payload.Blob == null || payload.Blob.Length == 0)
            {
                Trace("remote-fetch-empty char=" + response.CharacterId);
                return;
            }

            uint recomputed = MetaHumanLookBlobHash.Compute(payload.Blob);
            if (recomputed != payload.BlobChecksum)
            {
                Trace("remote-fetch-badsum char=" + response.CharacterId
                      + " got=" + recomputed.ToString("X8") + " claimed=" + payload.BlobChecksum.ToString("X8"));
                return;
            }

            MetaHuman.MetaHumanLook look;
            try
            {
                look = MetaHuman.MetaHumanLook.FromBytes(payload.Blob);
            }
            catch (Exception ex)
            {
                Trace("remote-fetch-badblob char=" + response.CharacterId + " " + ex.GetType().Name);
                return;
            }

            // 服务端也会主动推同一形状的回执（对方保存/发布外观时）。乱序或迟到副本不能让远端"退回旧脸"，
            // 因此按 Revision 单调：收到的比缓存旧的直接丢弃，同修订号则允许重放（幂等）。
            if (_remoteLooks.TryGetValue(response.CharacterId, out MetaHuman.MetaHumanLook cached)
                && MetaHuman.MetaHumanLookRevisionPolicy.IsStale(cached.Revision, look.Revision))
            {
                Trace("remote-stale char=" + response.CharacterId
                      + " got=" + look.Revision + " have=" + cached.Revision);
                return;
            }

            _remoteLooks[response.CharacterId] = look;
            // 发色的权威值是 look 里的 hair.color：外观副本只有建角那一刻写过的 HairColor，
            // 之后玩家在游戏里改发色/发布不会回写那一列（2026-10-23 核对）。look 里没有这一项时
            // 说明玩家没动过发色 ⇒ 用服务端那份（建角时写入的下标）。
            int serverHairColor = response.HairColor;
            int hairColor = serverHairColor;
            float hairColor01;
            if (look.Values != null && look.Values.TryGetValue("hair.color", out hairColor01))
            {
                hairColor = QuantizeHairColorIndex(hairColor01);
            }

            _remoteHairColor[response.CharacterId] = hairColor;
            Trace("remote-fetch-ok char=" + response.CharacterId + " bytes=" + payload.Blob.Length
                  + " entries=" + (look.Values?.Count ?? 0) + " published=" + look.Published
                  + " hair=" + serverHairColor + "/" + hairColor);

            TryApplyRemoteLook(response.CharacterId);
        }

        /// <summary>hair.color（0..1）→ 发色板下标；与捏脸页 QuantizeIndex 同一口径。</summary>
        private static int QuantizeHairColorIndex(float t)
        {
            int last = Mathf.Max(1, CcFacePresets.HairColors.Length - 1);
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(t, 0f, 1f) * last), 0, last);
        }

        /// <summary>把已缓存的外观套到同名远端 Actor 上；Actor 还没生成时等它生成后再调一次。</summary>
        public void TryApplyRemoteLook(ulong characterId)
        {
            if (!_remoteLooks.TryGetValue(characterId, out MetaHuman.MetaHumanLook look))
            {
                return;
            }

            var actor = FlaxEngine.Level.FindActor("RemotePlayer_" + characterId);
            var remote = actor?.GetScript<RemotePlayerActor>();
            if (remote == null)
            {
                return;
            }

            int hairColor = 0;
            _remoteHairColor.TryGetValue(characterId, out hairColor);
            bool applied = remote.ApplyMetaHumanLook(look, characterId, hairColor);
            Trace("remote-apply char=" + characterId + " actor=" + (actor != null) + " ok=" + applied);
        }

        /// <summary>
        /// 服务端随角色列表下发的副本里是否已带外观。只认缓存里那份 payload；
        /// 缓存缺失时按"没有"处理（宁可多传一次，也不要因为读不到而永远不传）。
        /// </summary>
        private static bool ServerHasLook(long characterId)
        {
            try
            {
                var info = HundunWorld.Game.Services.CharacterService.Instance?.GetCharacterFromCache((ulong)characterId);
                if (info == null)
                {
                    var manager = global::Game.UI.Character.CharacterManager.Instance;
                    info = manager?.CharacterList?.FirstOrDefault(c => c.CharacterId == (ulong)characterId)
                           ?? manager?.SelectedCharacter;
                }
                return info?.MetaHumanLook?.Blob != null && info.MetaHumanLook.Blob.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 采纳服务端随角色信息下发的外观：落到本地仓库，之后的读档路径（本地优先）就能拿到它。
        /// 冲突处理取「本地已发布则不动」：本地那份可能是这次会话刚发布、服务端列表还没刷新的结果，
        /// 用它覆盖服务端下发等于把玩家刚发布的外观退回上一版。真发生冲突时两条路径都要留痕。
        /// </summary>
        public void AdoptFromServer(long characterId, CharacterInfo info)
        {
            if (characterId <= 0L || info == null)
            {
                return;
            }

            MetaHumanLookPayload look = info.MetaHumanLook;
            if (look == null || look.Blob == null || look.Blob.Length == 0)
            {
                // 服务端没有存档、本机有：创角时那次提交可能没发出去（网络未就绪/时序），这里补传一次。
                Trace("adopt-empty char=" + characterId);
                HealServerFromLocal(characterId);
                return;
            }

            string id = characterId.ToString();
            if (MetaHuman.Runtime.MetaHumanLookStore.HasPublished(id))
            {
                // 用户拍板口径：比修订号（当前用 blob 自带的保存时刻作为单调序）。
                uint serverSum = look.BlobChecksum;
                MetaHuman.MetaHumanLook local = MetaHuman.Runtime.MetaHumanLookStore.LoadLatest(id);
                uint localSum = local == null ? 0u : local.Checksum;
                long localRev = local == null ? 0L : local.SavedAtUtcTicks;
                long serverRev = look.SavedAtUtcTicks;

                // 修订号放在外观 blob 里（v2 追加，v1 读成 0），服务端当不透明数据存，
                // 因此这里解码随包 blob 就能拿到服务端那份的修订号，不需要动传输契约。
                long serverRevision = 0L;
                try
                {
                    serverRevision = MetaHumanLook.FromBytes(look.Blob).Revision;
                }
                catch
                {
                    serverRevision = 0L;
                }

                string revBasis;
                MetaHuman.Core.AppearanceArbitration decision =
                    MetaHuman.Core.MetaHumanAppearanceArbitration.ChooseLooks(
                        local != null ? local.Revision : 0L, localRev, localSum,
                        serverRevision, serverRev, serverSum,
                        out revBasis);

                if (revBasis == "修订号")
                {
                    localRev = local != null ? local.Revision : 0L;
                    serverRev = serverRevision;
                }

                if (decision == MetaHuman.Core.AppearanceArbitration.Identical)
                {
                    LastErrorCode = "本机与服务端已发布存档一致（无冲突）";
                    return;
                }

                if (decision == MetaHuman.Core.AppearanceArbitration.UseLocal)
                {
                    LastErrorCode = "本机与服务端各有一份已发布外观：本机" + revBasis + " " + localRev
                        + " ≥ 服务端 " + serverRev + "，保留本机（本机自述 " + localSum.ToString("X8")
                        + "，服务端随包 " + serverSum.ToString("X8") + "）";
                    Debug.LogWarning($"[MetaHuman] 角色 {id} 两份已发布外观，保留本机：{LastErrorCode}");
                    return;
                }

                Debug.Log($"[MetaHuman] 角色 {id} 服务端外观更新（{revBasis} {serverRev} > {localRev}），准备采纳");
            }

            // 先验传输完整性：服务端下发的 blob 与它自述的校验和必须自洽，
            // 否则就是把一次截断的下载固化成本地存档。
            uint recomputed = MetaHumanLookBlobHash.Compute(look.Blob);
            if (recomputed != look.BlobChecksum)
            {
                Debug.LogError($"[MetaHuman] 服务端下发的外观校验和不符（角色 {id}）："
                               + $"重算 {recomputed:X8} 随包 {look.BlobChecksum:X8}，已丢弃");
                return;
            }

            string error;
            string saved = MetaHuman.Runtime.MetaHumanLookStore.SaveRawBytes(id, look.Blob, out error);
            Trace(saved != null
                ? "adopt-saved char=" + id + " bytes=" + look.Blob.Length + " tpl=" + look.TemplateId
                : "adopt-failed char=" + id + " error=" + error);
            Debug.Log(saved != null
                ? $"[MetaHuman] 已采纳服务端外观并落到本地（角色 {id}，{look.Blob.Length} 字节，模板 {look.TemplateId}）"
                : $"[MetaHuman] 服务端外观落本地失败（角色 {id}）：{error}");
        }

        /// <summary>由 <see cref="MetaHumanAppearanceResponseHandler"/> 在主线程调用。</summary>
        public void OnApplyResponse(UpdateCharacterAppearanceResponse response)
        {
            if (response == null)
            {
                LastErrorCode = "回执为空";
                Raise(false, LastErrorCode, StoredRevision);
                return;
            }

            if (!response.Success)
            {
                LastErrorCode = string.IsNullOrEmpty(response.Error) ? "服务端拒绝" : response.Error;

                // 已发布这一格要走得比"报个错"更远：把库里到底是哪一份记下来，并让本机一起定格。
                if (response.AppearanceSealed)
                {
                    StoredChecksum = response.StoredChecksum;
                    StoredRevision = response.StoredRevision;
                    Debug.LogWarning("[MetaHuman] 服务端已封存该角色外观（revision=" + response.StoredRevision
                                     + " checksum=" + response.StoredChecksum + "），本机不再接受再编辑");
                    Raise(false, LastErrorCode, response.StoredRevision);
                    Action handler = OnServerSealed;
                    if (handler != null)
                    {
                        handler();
                    }
                    return;
                }

                Trace("reject char-response err=" + LastErrorCode + " sealed=" + response.AppearanceSealed
                      + " storedRev=" + response.StoredRevision);
                Debug.LogError("[MetaHuman] 外观未存进服务端：" + LastErrorCode);
                Raise(false, LastErrorCode, response.StoredRevision);
                return;
            }

            StoredChecksum = response.StoredChecksum;
            StoredRevision = response.StoredRevision;
            Trace("ok revision=" + response.StoredRevision + " checksum=" + response.StoredChecksum);
            Debug.Log("[MetaHuman] 外观已存进服务端：revision=" + response.StoredRevision
                      + " checksum=" + response.StoredChecksum);
            Raise(true, "", response.StoredRevision);
        }

        private void Raise(bool ok, string error, int revision)
        {
            Action<bool, string, int> handler = OnSyncResult;
            if (handler != null)
            {
                handler(ok, error, revision);
            }
        }
    }

    /// <summary>外观回执消费方：回执链会写服务状态并驱动 UI 提示，必须回主线程执行。</summary>
    public class MetaHumanAppearanceResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.UpdateCharacterAppearanceResult };

        public override ServiceType ServiceType => ServiceType.Game;

        public MetaHumanAppearanceResponseHandler() : base(MessageType.UpdateCharacterAppearanceResult)
        {
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is UpdateCharacterAppearanceResponse response)
                Scripting.InvokeOnUpdate(() => MetaHumanAppearanceService.Instance.OnApplyResponse(response));

            return Task.CompletedTask;
        }
    }

    /// <summary>远端外观读取回执：解析、校验、缓存并套到远端 Actor，必须回主线程做。</summary>
    public class RemoteAppearanceResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.GetCharacterAppearanceResult };

        public override ServiceType ServiceType => ServiceType.Game;

        public RemoteAppearanceResponseHandler() : base(MessageType.GetCharacterAppearanceResult)
        {
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is GetCharacterAppearanceResponse response)
                Scripting.InvokeOnUpdate(() => MetaHumanAppearanceService.Instance.OnRemoteAppearanceResponse(response));

            return Task.CompletedTask;
        }
    }
}
