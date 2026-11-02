using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.CommonUI;
using NarrativePro.CommonUI.Widgets;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Nodes;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Tales.Dialogue
{
    /// <summary>
    /// 运行时对话对象。对应 UE5 <c>UDialogue</c>（Dialogue.h / Dialogue.cpp）。
    /// <para>
    /// 关键端口点：
    /// ① 行时长（Dialogue.cpp:1831 + DialogueSM.cpp:92-125）—— 音频/序列结束的行走真实时长，
    /// 而不是旧实现里的 0；
    /// ② 权威侧生成 chunk、按节点 ID 复制给非权威端（Dialogue.cpp:537-577 / 1464-1548）；
    /// ③ 回复链条件校验（DialogueSM.cpp:377-440）。
    /// </para>
    /// </summary>
    public class Dialogue
    {
        /// <summary>UE5 Dialogue.cpp:47 —— 播完某 NPC 节点后完成的数据任务名。</summary>
        public const string PlayDialogueNodeTask = "PlayDialogueNode";

        /// <summary>UE5 Dialogue.cpp:44 —— 玩家说话者固定 ID。</summary>
        public const string PlayerSpeakerID = "Player";

        public string DialogueId { get; set; } = "";
        public List<SpeakerInfo> Speakers { get; set; } = new List<SpeakerInfo>();
        public PlayerSpeakerInfo PlayerSpeakerInfo { get; set; } = new PlayerSpeakerInfo();
        public DialogueNode_NPC RootDialogue { get; set; }
        public List<DialogueNode_NPC> NPCReplies { get; set; } = new List<DialogueNode_NPC>();
        public List<DialogueNode_Player> PlayerReplies { get; set; } = new List<DialogueNode_Player>();

        /// <summary>队伍对话时每个玩家的说话者信息。对应 UE5 <c>PartySpeakerInfo</c>。</summary>
        public List<PlayerSpeakerInfo> PartySpeakerInfo { get; set; } = new List<PlayerSpeakerInfo>();

        public DialogueNode CurrentNode { get; protected set; }
        public SpeakerInfo CurrentSpeaker { get; protected set; }
        public DialogueLine CurrentLine { get; protected set; }

        /// <summary>剩余待播的 NPC 回复队列（对应 UE5 的 NPCReplyChain：头部出队）。</summary>
        public List<DialogueNode_NPC> NPCReplyChain { get; protected set; } = new List<DialogueNode_NPC>();

        /// <summary>当前可展示的玩家回复。对应 UE5 <c>AvailableResponses</c>。</summary>
        public List<DialogueNode_Player> AvailableResponses { get; protected set; } = new List<DialogueNode_Player>();

        /// <summary>&gt;0 时，玩家离任说话者超过该距离就自动结束对话。对应 UE5 <c>EndDialogueDist</c>。</summary>
        public float EndDialogueDist { get; set; } = -1f;
        public bool bShowCinematicBars { get; set; } = false;
        public bool bUnskippable { get; set; } = false;

        /// <summary>UE5 构造函数默认 bFreeMovement = false（旧 C# 端口写成 true，会误放开移动）。</summary>
        public bool bFreeMovement { get; set; } = false;

        /// <summary>是否允许玩家中途退出。对应 UE5 <c>bCanBeExited</c>（默认 true）。</summary>
        public bool bCanBeExited { get; set; } = true;

        public bool bAutoRotateSpeakers { get; set; } = false;

        /// <summary>UE5 构造函数默认 bAutoStopMovement = true。</summary>
        public bool bAutoStopMovement { get; set; } = true;

        public int Priority { get; set; } = 0;
        public string DefaultHeadBoneName { get; set; } = "head";
        public float DialogueBlendOutTime { get; set; } = 0f;

        /// <summary>本对话的默认镜头（行/说话者都没设置时使用）。对应 UE5 <c>DefaultDialogueShot</c>。</summary>
        public NarrativeDialogueSequence DefaultDialogueShot;

        public TalesComponent OwningComp { get; protected set; }
        public object OwningPawn { get; protected set; }
        public object OwningController { get; protected set; }

        public bool bDeinitialized { get; protected set; } = false;
        public bool bBeganPlaying { get; protected set; } = false;

        public DialoguePlayParams PlayParams { get; protected set; } = new DialoguePlayParams();

        /// <summary>当前行剩余时间（秒）。由 <see cref="TickDialogue"/> 递减。</summary>
        public float CurrentLineRemainingTime { get; protected set; } = 0f;

        /// <summary>当前行播放的语音。对应 UE5 <c>DialogueAudio</c>。</summary>
        [NonSerialized] protected AudioSource DialogueAudio;

        /// <summary>当前播放的镜头序列。对应 UE5 <c>CurrentDialogueSequence</c>。</summary>
        [NonSerialized] public NarrativeDialogueSequence CurrentDialogueSequence;

        /// <summary>说话者 ID → 出演 Actor。对应 UE5 <c>SpeakerAvatars</c>。</summary>
        public Dictionary<string, Actor> SpeakerAvatars { get; protected set; } = new Dictionary<string, Actor>();

        private bool _waitingForPlayerInput = false;

        public event Action<Dialogue> OnBeginDialogue;
        public event Action<Dialogue> OnEndDialogue;
        public event Action<Dialogue, DialogueNode_NPC, DialogueLine, SpeakerInfo> OnNPCDialogueLineStarted;
        public event Action<Dialogue, DialogueNode_NPC, DialogueLine, SpeakerInfo> OnNPCDialogueLineFinished;
        public event Action<Dialogue, DialogueNode_Player, DialogueLine> OnPlayerDialogueLineStarted;
        public event Action<Dialogue, DialogueNode_Player, DialogueLine> OnPlayerDialogueLineFinished;
        public event Action<Dialogue, List<DialogueNode_Player>> OnDialogueRepliesAvailable;

        /// <summary>
        /// 初始化对话。对应 UE5 <c>UDialogue::Initialize</c>（Dialogue.cpp:85-191）：
        /// StartFromID 解析不到节点要直接失败（旧端口只 warn 后回退根节点，会播错内容）。
        /// </summary>
        /// <returns>是否初始化成功（含是否生成了有效 chunk）。</returns>
        public bool Initialize(TalesComponent comp, DialoguePlayParams playParams)
        {
            if (comp == null)
            {
                return false;
            }

            if (NPCReplies == null || NPCReplies.Count == 0 || RootDialogue == null)
            {
                NarrativeLog.LogWarning($"Dialogue '{DialogueId}' 没有根节点或没有任何 NPC 回复，初始化失败");
                return false;
            }

            var startDialogue = RootDialogue;
            if (playParams != null && !string.IsNullOrEmpty(playParams.StartFromID))
            {
                startDialogue = GetNPCReplyByID(playParams.StartFromID);
                if (startDialogue == null)
                {
                    NarrativeLog.LogWarning($"Dialogue '{DialogueId}' 找不到 StartFromID '{playParams.StartFromID}'，初始化失败");
                    return false;
                }
            }

            PlayParams = playParams ?? new DialoguePlayParams();

            if (PlayParams.Priority > -1)
            {
                Priority = PlayParams.Priority;
            }

            OwningComp = comp;
            OwningController = comp.GetOwningController();
            OwningPawn = comp.GetOwningPawn();

            if (RootDialogue != null)
            {
                RootDialogue.OwningDialogue = this;
            }

            foreach (var reply in NPCReplies)
            {
                if (reply == null) continue;
                reply.OwningDialogue = this;
                reply.OwningComponent = OwningComp;
            }

            foreach (var reply in PlayerReplies)
            {
                if (reply == null) continue;
                reply.OwningDialogue = this;
                reply.OwningComponent = OwningComp;
            }

            if (PlayParams.bOverrideUnskippable)
            {
                bUnskippable = PlayParams.bUnskippable;
            }

            if (PlayParams.bOverrideFreeMovement)
            {
                bFreeMovement = PlayParams.bFreeMovement;
            }

            if (PlayParams.bOverrideStopMovement)
            {
                bAutoStopMovement = PlayParams.bStopMovement;
            }

            // Dialogue.cpp:169-172 —— bCanBeExited 也要能被播放参数覆盖
            if (PlayParams.bOverrideCanBeExited)
            {
                bCanBeExited = PlayParams.bCanBeExited;
            }

            InitSpeakerAvatars();

            bDeinitialized = false;

            // 只有权威端生成 chunk；非权威端等着服务端把节点 ID 发过来（Dialogue.cpp:174-184）
            if (OwningComp.HasAuthority())
            {
                if (!GenerateDialogueChunk(startDialogue))
                {
                    return false;
                }
            }

            NarrativeLog.Log($"Dialogue '{DialogueId}' initialized");
            return true;
        }

        /// <summary>兼容旧签名（<c>object comp</c>）的初始化入口。</summary>
        public bool Initialize(object comp, DialoguePlayParams playParams)
        {
            return Initialize(comp as TalesComponent, playParams);
        }

        /// <summary>反初始化。对应 UE5 <c>UDialogue::Deinitialize</c>（Dialogue.cpp:193-241）。</summary>
        public void Deinitialize()
        {
            if (bDeinitialized) return;

            bDeinitialized = true;

            StopDialogueSequence();
            StopDialogueAudio();
            CleanUpSpeakerAvatars();

            CurrentNode = null;
            CurrentSpeaker = null;
            CurrentLine = null;
            NPCReplyChain.Clear();
            AvailableResponses.Clear();
            _waitingForPlayerInput = false;
            CurrentLineRemainingTime = 0f;

            NarrativeLog.Log($"Dialogue '{DialogueId}' deinitialized");
        }

        /// <summary>
        /// 开始播放。对应 UE5 <c>UDialogue::Play</c>（Dialogue.cpp:579-596）。
        /// </summary>
        public void Play()
        {
            if (bDeinitialized)
            {
                NarrativeLog.LogWarning($"Cannot play deinitialized dialogue '{DialogueId}'");
                return;
            }

            if (!bBeganPlaying)
            {
                bBeganPlaying = true;
                OnBeginDialogue?.Invoke(this);
            }

            // NPC 还有话要说就继续播，否则进入「等玩家选择」阶段
            if (NPCReplyChain.Count > 0)
            {
                PlayNextNPCReply();
            }
            else
            {
                NPCFinishedTalking();
            }
        }

        /// <summary>结束当前行。对应 UE5 <c>UDialogue::EndCurrentLine</c>（Dialogue.cpp:426-462）。</summary>
        public void EndCurrentLine()
        {
            if (CurrentNode == null) return;

            // 解绑「行结束」监听，下一行会重新绑定（Dialogue.cpp:431-442）
            StopDialogueAudio();
            StopDialogueCutscene();
            UnbindSequenceFinished();
            CurrentLineRemainingTime = 0f;

            if (CurrentNode is DialogueNode_NPC)
            {
                FinishNPCDialogue();
            }
            else
            {
                FinishPlayerDialogue();
            }
        }

        /// <summary>
        /// 跳过当前行。对应 UE5 <c>UDialogue::SkipCurrentLine</c>（Dialogue.cpp:375-411）：
        /// 跳过 NPC 行时必须把服务端剩余的 chunk 重发给各客户端，保证大家看到的内容一致。
        /// </summary>
        public bool SkipCurrentLine()
        {
            if (!CanSkipCurrentLine()) return false;
            if (OwningComp == null || !OwningComp.HasAuthority()) return false;

            bool bSkippingPlayerReply = CurrentNode is DialogueNode_Player;

            if (!bSkippingPlayerReply)
            {
                SendDialogueChunkToClients();
            }

            EndCurrentLine();
            return true;
        }

        /// <summary>对应 UE5 <c>CanSkipCurrentLine</c>（Dialogue.cpp:413-424）：只看当前节点是否可跳过。</summary>
        public bool CanSkipCurrentLine()
        {
            if (OwningComp == null) return false;
            return CurrentNode != null && CurrentNode.bIsSkippable;
        }

        /// <summary>对话整体是否允许被跳过。对应 UE5 <c>CanSkipDialogue</c>。</summary>
        public bool CanSkipDialogue()
        {
            return !bUnskippable;
        }

        /// <summary>
        /// 选择玩家回复。对应 UE5 <c>UDialogue::SelectDialogueOption</c>（Dialogue.cpp:469-485）。
        /// <c>OnDialogueOptionSelected</c> 由拥有组件在玩家行开始时广播（TalesComponent 已订阅）。
        /// </summary>
        public bool SelectDialogueOption(DialogueNode_Player option)
        {
            if (!CanSelectDialogueOption(option)) return false;

            PlayPlayerDialogueNode(option);

            return true;
        }

        /// <summary>
        /// 对应 UE5 <c>CanSelectDialogueOption</c>（Dialogue.cpp:464-467）：
        /// 只校验「是不是当前可用回复之一」。条件已在生成 chunk 时校验过，
        /// 旧端口额外要求 <c>_waitingForPlayerInput</c>，会让自动选中的路径全部失效。
        /// </summary>
        public bool CanSelectDialogueOption(DialogueNode_Player playerNode)
        {
            return playerNode != null && AvailableResponses != null && AvailableResponses.Contains(playerNode);
        }

        /// <summary>当前是否有有效 chunk。对应 UE5 <c>HasValidChunk</c>（Dialogue.cpp:487-509）。</summary>
        public bool HasValidChunk()
        {
            bool bReplyChainHasContent = false;

            foreach (var reply in NPCReplyChain)
            {
                if (reply != null && !reply.IsRoutingNode())
                {
                    bReplyChainHasContent = true;
                    break;
                }
            }

            return AvailableResponses.Count > 0 || bReplyChainHasContent;
        }

        /// <summary>
        /// 生成一段 chunk（NPC 回复链 + 玩家可用回复）。
        /// 对应 UE5 <c>GenerateDialogueChunk</c>（Dialogue.cpp:511-535）。
        /// </summary>
        public bool GenerateDialogueChunk(DialogueNode_NPC npcNode)
        {
            if (npcNode == null || OwningComp == null || !OwningComp.HasAuthority())
            {
                return false;
            }

            NPCReplyChain = npcNode.GetReplyChain(OwningController, OwningPawn, OwningComp);
            AvailableResponses.Clear();

            DialogueNode_NPC lastNPCNode = NPCReplyChain.Count > 0 ? NPCReplyChain[NPCReplyChain.Count - 1] : null;
            if (lastNPCNode != null)
            {
                AvailableResponses = lastNPCNode.GetPlayerReplies(OwningController, OwningPawn, OwningComp);
            }

            return HasValidChunk();
        }

        /// <summary>
        /// 非权威端接收服务端算好的 chunk。
        /// 对应 UE5 <c>ClientReceiveDialogueChunk</c>（Dialogue.cpp:537-577）。
        /// </summary>
        public void ClientReceiveDialogueChunk(List<string> npcReplyIDs, List<string> playerReplyIDs)
        {
            if (OwningComp == null || OwningComp.HasAuthority()) return;

            // UE5:545-567 —— 不能直接 EndCurrentLine（它会跳到下一行甚至结束对话），
            // 这里只手工收尾当前行，然后把链换成服务端给的。
            if (CurrentNode != null)
            {
                StopDialogueAudio();
                UnbindSequenceFinished();
                FinishDialogueNode(CurrentNode, CurrentLine, CurrentSpeaker);
            }

            NPCReplyChain = GetNPCRepliesByIDs(npcReplyIDs);
            AvailableResponses = GetPlayerRepliesByIDs(playerReplyIDs);

            CurrentNode = null;
            CurrentLine = null;
            CurrentLineRemainingTime = 0f;
            _waitingForPlayerInput = false;

            Play();
        }

        // ===== 节点 <-> ID 解析（网络侧用 ID 复制节点）=====

        /// <summary>对应 UE5 <c>GetNPCReplyByID</c>（Dialogue.cpp:1464-1474）。</summary>
        public DialogueNode_NPC GetNPCReplyByID(string id)
        {
            if (string.IsNullOrEmpty(id) || NPCReplies == null) return null;
            foreach (var npcReply in NPCReplies)
            {
                if (npcReply != null && npcReply.GetID() == id) return npcReply;
            }
            return null;
        }

        /// <summary>对应 UE5 <c>GetPlayerReplyByID</c>（Dialogue.cpp:1476-1486）。</summary>
        public DialogueNode_Player GetPlayerReplyByID(string id)
        {
            if (string.IsNullOrEmpty(id) || PlayerReplies == null) return null;
            foreach (var playerReply in PlayerReplies)
            {
                if (playerReply != null && playerReply.GetID() == id) return playerReply;
            }
            return null;
        }

        /// <summary>对应 UE5 <c>GetNPCRepliesByIDs</c>（Dialogue.cpp:1488-1505）。</summary>
        public List<DialogueNode_NPC> GetNPCRepliesByIDs(List<string> ids)
        {
            var replies = new List<DialogueNode_NPC>();
            if (ids == null) return replies;

            foreach (var id in ids)
            {
                if (NPCReplies == null) break;
                foreach (var reply in NPCReplies)
                {
                    if (reply != null && reply.GetID() == id)
                    {
                        replies.Add(reply);
                        break;
                    }
                }
            }

            return replies;
        }

        /// <summary>对应 UE5 <c>GetPlayerRepliesByIDs</c>（Dialogue.cpp:1507-1524）。</summary>
        public List<DialogueNode_Player> GetPlayerRepliesByIDs(List<string> ids)
        {
            var replies = new List<DialogueNode_Player>();
            if (ids == null) return replies;

            foreach (var id in ids)
            {
                if (PlayerReplies == null) break;
                foreach (var reply in PlayerReplies)
                {
                    if (reply != null && reply.GetID() == id)
                    {
                        replies.Add(reply);
                        break;
                    }
                }
            }

            return replies;
        }

        /// <summary>对应 UE5 <c>MakeIDsFromNPCNodes</c>（Dialogue.cpp:1526-1536）。</summary>
        public List<string> MakeIDsFromNPCNodes(List<DialogueNode_NPC> nodes)
        {
            var ids = new List<string>();
            if (nodes == null) return ids;
            foreach (var node in nodes)
            {
                ids.Add(node != null ? node.GetID() : "");
            }
            return ids;
        }

        /// <summary>对应 UE5 <c>MakeIDsFromPlayerNodes</c>（Dialogue.cpp:1538-1548）。</summary>
        public List<string> MakeIDsFromPlayerNodes(List<DialogueNode_Player> nodes)
        {
            var ids = new List<string>();
            if (nodes == null) return ids;
            foreach (var node in nodes)
            {
                ids.Add(node != null ? node.GetID() : "");
            }
            return ids;
        }

        // ===== 播放 =====

        /// <summary>对应 UE5 <c>PlayNextNPCReply</c>（Dialogue.cpp:1349-1362）：链首出队播放。</summary>
        public void PlayNextNPCReply()
        {
            if (NPCReplyChain.Count > 0 && IsInitialized())
            {
                var npcNode = NPCReplyChain[0];
                NPCReplyChain.RemoveAt(0);
                PlayNPCDialogueNode(npcNode);
            }
            else
            {
                NPCFinishedTalking();
            }
        }

        /// <summary>对应 UE5 <c>PlayNPCDialogueNode</c>（Dialogue.cpp:1123-1185）。</summary>
        public void PlayNPCDialogueNode(DialogueNode_NPC npcReply)
        {
            if (npcReply == null)
            {
                PlayNextNPCReply();
                return;
            }

            CurrentNode = npcReply;

            // 服务端拿不到音频/序列结束事件，必须用固定时长（Dialogue.cpp:1133）
            CurrentLine = npcReply.GetRandomLine(IsStandalone());
            CurrentLine.Text = ReplaceStringVariables(npcReply, CurrentLine, CurrentLine.Text);
            npcReply.PlayedLine = CurrentLine;

            CurrentSpeaker = GetSpeaker(npcReply.GetSpeakerID());

            ProcessNodeEvents(npcReply, true);

            // ProcessNodeEvents 里的事件可能直接结束对话，把 OwningComp 清空
            if (OwningComp == null) return;

            // 没有可播内容的路由节点：直接收尾并跑 End 事件
            if (npcReply.IsRoutingNode())
            {
                FinishNPCDialogue();
                return;
            }

            PlayDialogueNode(npcReply, CurrentLine, CurrentSpeaker);

            OnNPCDialogueLineStarted?.Invoke(this, npcReply, CurrentLine, CurrentSpeaker);

            StartLineTiming(npcReply, CurrentLine);
        }

        /// <summary>对应 UE5 <c>PlayPlayerDialogueNode</c>（Dialogue.cpp:1187-1244）。</summary>
        public void PlayPlayerDialogueNode(DialogueNode_Player playerReply)
        {
            if (OwningComp == null || playerReply == null) return;

            _waitingForPlayerInput = false;

            // 玩家开口就清掉可选项（Dialogue.cpp:1196）
            AvailableResponses.Clear();

            CurrentNode = playerReply;

            ProcessNodeEvents(playerReply, true);

            if (OwningComp == null) return;

            if (playerReply.IsRoutingNode())
            {
                FinishPlayerDialogue();
                return;
            }

            CurrentLine = playerReply.GetRandomLine(IsStandalone());
            CurrentLine.Text = ReplaceStringVariables(playerReply, CurrentLine, CurrentLine.Text);
            playerReply.PlayedLine = CurrentLine;

            OnPlayerDialogueLineStarted?.Invoke(this, playerReply, CurrentLine);

            PlayDialogueNode(playerReply, CurrentLine, PlayerSpeakerInfo);

            CurrentSpeaker = PlayerSpeakerInfo;

            // 电影对话选项（UE5 FNarrativeCineDialogueLine 的端口消费点）：
            // 玩家说这句台词的同时播放目的地序列，台词结束/退出对话时混合输出收尾。
            BeginCinematicDialogueOption(playerReply);

            StartLineTiming(playerReply, CurrentLine);
        }

        // ===== 电影对话选项（对应 UE5 FNarrativeCineDialogueLine + AsyncAction_Play/BlendOutNarrativeSequence） =====

        /// <summary>当前正在演出的电影对话选项（null 表示无）。</summary>
        [NonSerialized] private NarrativePro.Cinematics.NarrativeCineDialogueLine _activeCineOption;

        /// <summary>承载目的地序列的 Actor（本端口即时生成，结束时销毁）。对应 UE5 蓝图节点入参 SequenceActor。</summary>
        [NonSerialized] private FlaxEngine.Actor _cineOptionHost;
        [NonSerialized] private NarrativePro.Cinematics.NarrativeLevelSequenceActor _cineOptionActor;
        [NonSerialized] private NarrativePro.Cinematics.AsyncAction_PlayNarrativeSequence _cinePlayAction;

        /// <summary>
        /// 开始演出玩家节点附带的电影对话选项：条件通过且有目的地序列时，
        /// 生成序列宿主 Actor 并经 <c>AsyncAction_PlayNarrativeSequence</c> 播放
        /// （对应 UE5 蓝图里 PlayNarrativeSequence 异步节点的用法）。
        /// bKeepExistingSequenceSettings=false 时改用选项自带的目的地播放设置；
        /// bBlendAnimsBetweenSequences=false 时清掉绑定走位，让角色瞬时落位（硬切），
        /// 对应 UE5「跳目的地序列默认动画硬切，可开混合」的注释语义（NarrativeLevelSequenceActor.h:146-152）。
        /// </summary>
        private void BeginCinematicDialogueOption(Nodes.DialogueNode_Player playerNode)
        {
            // 上一选项的演出必须先收尾，避免叠加（新台词覆盖旧演出，与 UE5 单序列语境一致）
            EndCinematicDialogueOption();

            var option = playerNode != null ? playerNode.CinematicOption : null;
            if (option == null) return;

            if (!string.IsNullOrEmpty(option.DestinationMarker) && string.IsNullOrEmpty(option.DestinationSequencePath))
            {
                // Flax-不兼容（已核实）：FlaxEngine.CSharp.xml 无 Sequencer/命名 Marker 轨道，
                // 仅有 Marker 无目的地序列时 UE5 的「跳到 Marker」在端口无从挂接，记录后按普通台词继续。
                NarrativeLog.Log($"电影对话选项仅有 DestinationMarker={option.DestinationMarker}，Flax 无命名 Marker 轨道，跳过跳转。");
                return;
            }

            if (string.IsNullOrEmpty(option.DestinationSequencePath)) return;
            if (!option.CanSelect(OwningPawn, OwningController, OwningComp)) return;

            FlaxEngine.Actor host = null;
            NarrativePro.Cinematics.NarrativeLevelSequenceActor actor = null;
            try
            {
                // FlaxEngine.Actor 抽象不可实例化：用 AudioSource 作序列脚本宿主（无任何音频剪辑即静默）。
                host = new FlaxEngine.AudioSource();
                host.Name = "CineDialogueOptionSequence";
                // 序列宿主须显式生成进玩法场景；Flax 约定 SpawnActor 返回 true 表示失败
                if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(host, host.Name))
                {
                    FlaxEngine.Object.Destroy(host);
                    return;
                }
                actor = host.AddScript<NarrativePro.Cinematics.NarrativeLevelSequenceActor>();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("BeginCinematicDialogueOption：生成序列宿主失败：" + ex.Message);
                if (host != null) FlaxEngine.Object.Destroy(host);
                return;
            }
            if (actor == null)
            {
                FlaxEngine.Object.Destroy(host);
                return;
            }

            var settings = option.bKeepExistingSequenceSettings
                ? actor.NarrativeSequenceParams
                : option.DestinationPlaybackSettings;
            if (settings == null) settings = new NarrativePro.Cinematics.NarrativeSequencePlaybackSettings();
            if (!option.bBlendAnimsBetweenSequences) settings.bWalkBindingsToStartTransform = false;

            _activeCineOption = option;
            _cineOptionHost = host;
            _cineOptionActor = actor;

            _cinePlayAction = NarrativePro.Cinematics.AsyncAction_PlayNarrativeSequence.PlayNarrativeSequence(
                actor, option.DestinationSequencePath, settings);

            if (_cinePlayAction == null)
            {
                // 启动失败（Actor/路径校验未过）：回收本次演出，台词照常播放。
                FlaxEngine.Object.Destroy(host);
                _cineOptionHost = null;
                _cineOptionActor = null;
                _activeCineOption = null;
                return;
            }

            NarrativeLog.Log($"演出电影对话选项：{option.OptionText} → {option.DestinationSequencePath}");
        }

        /// <summary>
        /// 收尾电影对话选项：经 <c>AsyncAction_BlendOutNarrativeSequence</c>（对应 UE5 蓝图
        /// BlendOut 异步节点）把目的地序列混合输出，完成信号送达后销毁承载 Actor。
        /// 幂等，无演出时直接返回。
        /// </summary>
        private void EndCinematicDialogueOption()
        {
            if (_cineOptionActor == null && _cinePlayAction == null) return;

            var actor = _cineOptionActor;
            var host = _cineOptionHost;
            var play = _cinePlayAction;

            _activeCineOption = null;
            _cineOptionActor = null;
            _cineOptionHost = null;
            _cinePlayAction = null;

            if (actor != null)
            {
                // UE5：BlendOutAndStop 开启 blend-out 窗口，窗口结束才由播放器广播 OnFinished，
                // 收尾（解绑、交还输入、销毁承载 Actor）都挂在那个事件上。
                // 缺陷4：端口此前在这里「立即 EndTask()」，等于把混合输出节点当场判完成 —— 收尾形同虚设。
                // 现在 NarrativeLevelSequenceActor.BlendOutAndStop 跑真实窗口，这里改为等事件回调再收口：
                // 混合期内标签仍在（角色仍被锁）、黑边仍在淡出，事件回来后依次结束两个异步动作并销毁宿主。
                var blend = NarrativePro.Cinematics.AsyncAction_BlendOutNarrativeSequence.BlendOutNarrativeSequence(actor);
                if (blend != null)
                {
                    blend.OnFinished += () =>
                    {
                        blend.EndTask();
                        play?.EndTask();
                        if (host != null) FlaxEngine.Object.Destroy(host);
                    };
                    return;
                }
            }

            // 没有可收尾的 Actor（或动作创建失败）：保持旧行为，立即回收，绝不泄漏承载 Actor。
            play?.EndTask();

            if (host != null)
            {
                FlaxEngine.Object.Destroy(host);
            }
        }

        /// <summary>
        /// NPC 说完了整段。对应 UE5 <c>NPCFinishedTalking</c>（Dialogue.cpp:1029-1121）：
        /// 权威端处理自动选中（含 <c>bAutoSelectSingleResponse</c>），否则广播可选项给 UI。
        /// </summary>
        public void NPCFinishedTalking()
        {
            if (IsInitialized() && AvailableResponses.Count > 0 && OwningComp != null && OwningComp.CurrentDialogue == this)
            {
                if (OwningComp.HasAuthority())
                {
                    bool bWantsAutoSelect = false;

                    var dialogueSettings = NarrativeDialogueSettings.Instance;
                    if (dialogueSettings != null)
                    {
                        if (dialogueSettings.bAutoSelectSingleResponse
                            || (AvailableResponses.Count == 1 && AvailableResponses[0] != null && AvailableResponses[0].IsAutoSelectIfOnlyReply()))
                        {
                            bWantsAutoSelect = true;
                        }
                    }

                    foreach (var availableResponse in AvailableResponses)
                    {
                        if (availableResponse == null) continue;
                        if (availableResponse.IsAutoSelect() || bWantsAutoSelect)
                        {
                            OwningComp.TrySelectDialogueOption(availableResponse);
                            return;
                        }
                    }
                }

                // 玩家要开口了：播「选择回复」镜头（优先级：节点 SelectingReplyShot → 玩家说话者 → 对话默认）
                var lastNode = CurrentNode as DialogueNode_NPC;
                var playerAvatar = GetPlayerAvatar();
                Actor listeningActor = null;

                if (CurrentSpeaker != null && SpeakerAvatars.TryGetValue(CurrentSpeaker.GetSpeakerID(), out var listenerAvatar))
                {
                    listeningActor = listenerAvatar;
                }

                // 玩家要开口了：播「选择回复」镜头。UE5 里节点/说话者的 SelectingReplyShot 是
                // Instanced 对象；端口先取实例、再按名走镜头注册表解析（JSON 资产今天普遍只写名字）。
                var selectingShot = lastNode != null
                    ? lastNode.SelectingReplyShot ?? NarrativePro.Cinematics.NarrativeDialogueShotRegistry.Resolve(lastNode.SelectingReplyShotName)
                    : null;
                if (selectingShot == null && PlayerSpeakerInfo != null)
                {
                    selectingShot = PlayerSpeakerInfo.ResolveSelectingReplyShot();
                }

                if (selectingShot != null)
                {
                    PlayDialogueSequence(selectingShot, playerAvatar, listeningActor);
                }
                else if (DefaultDialogueShot != null)
                {
                    PlayDialogueSequence(DefaultDialogueShot, playerAvatar, listeningActor);
                }

                OnDialogueRepliesAvailable?.Invoke(this, AvailableResponses);

                // 现在把控制权交给玩家，行计时器停摆
                _waitingForPlayerInput = true;
            }
            else
            {
                // 玩家没有可回复的内容，对话结束
                ExitDialogue(EExitDialogueReason.NoLines);
            }
        }

        /// <summary>对应 UE5 <c>FinishNPCDialogue</c>（Dialogue.cpp:1364-1390）。</summary>
        public void FinishNPCDialogue()
        {
            var npcNode = CurrentNode as DialogueNode_NPC;
            if (npcNode == null)
            {
                PlayNextNPCReply();
                return;
            }

            FinishDialogueNode(npcNode, CurrentLine, CurrentSpeaker);

            if (OwningComp != null)
            {
                if (OwningComp.HasAuthority())
                {
                    OwningComp.CompleteNarrativeDataTask(PlayDialogueNodeTask, npcNode.GetID());
                }

                ProcessNodeEvents(npcNode, false);

                // ProcessEvents 可能已结束对话
                if (OwningComp != null)
                {
                    OnNPCDialogueLineFinished?.Invoke(this, npcNode, CurrentLine, CurrentSpeaker);
                    PlayNextNPCReply();
                }
            }
        }

        /// <summary>对应 UE5 <c>FinishPlayerDialogue</c>（Dialogue.cpp:1392-1462）。</summary>
        public void FinishPlayerDialogue()
        {
            var playerNode = CurrentNode as DialogueNode_Player;
            if (playerNode == null || OwningComp == null)
            {
                NarrativeLog.Log("FinishPlayerDialogue 被调用但 OwningComp 或 PlayerNode 为空。");
                return;
            }

            FinishDialogueNode(playerNode, CurrentLine, CurrentSpeaker);

            OnPlayerDialogueLineFinished?.Invoke(this, playerNode, CurrentLine);

            // 玩家台词说完：目的地序列混合输出并销毁承载 Actor（BlendOut 异步节点的真实调用点）。
            EndCinematicDialogueOption();

            ProcessNodeEvents(playerNode, false);

            if (OwningComp == null) return;

            if (OwningComp.HasAuthority())
            {
                OwningComp.CompleteNarrativeDataTask(PlayDialogueNodeTask, playerNode.GetID());

                if (playerNode.NPCReplies == null || playerNode.NPCReplies.Count <= 0)
                {
                    ExitDialogue(EExitDialogueReason.NoLines);
                    return;
                }

                var nextReply = playerNode.GetFirstValidNPCReply(OwningController, OwningPawn, OwningComp);

                if (GenerateDialogueChunk(nextReply))
                {
                    // 服务端把新 chunk 复制给各客户端（队伍则发给每个队员的 solo 组件）
                    SendDialogueChunkToClients();
                    Play();
                }
                else
                {
                    NarrativeLog.LogWarning("选中的回复之后没有生成新的 chunk，结束对话。");
                    ExitDialogue(EExitDialogueReason.NoLines);
                }
            }
        }

        /// <summary>退出对话。对应 UE5 <c>UDialogue::ExitDialogue</c>（Dialogue.cpp:598-604）。</summary>
        public void ExitDialogue(EExitDialogueReason reason)
        {
            if (bDeinitialized) return;

            NarrativeLog.Log($"Dialogue '{DialogueId}' exiting: {reason}");

            // 中途退出也要收尾电影演出，避免序列宿主 Actor 与异步动作泄漏。
            EndCinematicDialogueOption();

            if (CurrentNode != null)
            {
                ProcessNodeEvents(CurrentNode, false);
            }

            OnEndDialogue?.Invoke(this);
            Deinitialize();
        }

        /// <summary>
        /// 每帧推进。对应 UE5 <c>UDialogue::TickDialogue_Implementation</c>（Dialogue.cpp:606-630）
        /// 加上 Flax 侧的行计时（替代 UE5 的 World Timer）。
        /// </summary>
        public void TickDialogue(float deltaTime)
        {
            if (bDeinitialized) return;

            if (CurrentDialogueSequence != null)
            {
                CurrentDialogueSequence.Tick(deltaTime);
            }

            // 离说话者太远自动结束（Dialogue.cpp:613-628）
            if (EndDialogueDist > 0f && OwningPawn is Actor pawnActor)
            {
                foreach (var kvp in SpeakerAvatars)
                {
                    var speakerAvatar = kvp.Value;
                    if (speakerAvatar == null) continue;

                    if (Vector3.Distance(pawnActor.Position, speakerAvatar.Position) > EndDialogueDist)
                    {
                        OwningComp?.ExitDialogue(EExitDialogueReason.TooFarAway);
                        return;
                    }
                }
            }

            TickLineTiming(deltaTime);
        }

        /// <summary>是否正在播放。</summary>
        public bool IsPlaying()
        {
            return bBeganPlaying && !bDeinitialized;
        }

        /// <summary>是否已初始化（未反初始化）。</summary>
        public bool IsInitialized()
        {
            return !bDeinitialized;
        }

        /// <summary>对应 UE5 <c>GetNodes</c>（Dialogue.cpp:1550-1565）。</summary>
        public List<DialogueNode> GetNodes()
        {
            var nodes = new List<DialogueNode>();
            if (NPCReplies != null)
            {
                foreach (var npcReply in NPCReplies)
                {
                    if (npcReply != null) nodes.Add(npcReply);
                }
            }
            if (PlayerReplies != null)
            {
                foreach (var playerReply in PlayerReplies)
                {
                    if (playerReply != null) nodes.Add(playerReply);
                }
            }
            return nodes;
        }

        /// <summary>
        /// 取说话者信息。对应 UE5 <c>GetSpeaker</c>（Dialogue.cpp:356-373）：
        /// 找不到时回退到第一个说话者（老数据没写 SpeakerID）。
        /// </summary>
        public SpeakerInfo GetSpeaker(string speakerID)
        {
            if (PlayerSpeakerInfo != null && PlayerSpeakerInfo.GetSpeakerID() == speakerID)
            {
                return PlayerSpeakerInfo;
            }

            if (Speakers != null)
            {
                foreach (var speaker in Speakers)
                {
                    if (speaker != null && speaker.GetSpeakerID() == speakerID)
                    {
                        return speaker;
                    }
                }

                if (Speakers.Count > 0 && Speakers[0] != null)
                {
                    return Speakers[0];
                }
            }

            return PlayerSpeakerInfo;
        }

        /// <summary>
        /// 字符串变量替换。对应 UE5 <c>ReplaceStringVariables</c>（Dialogue.cpp:1246-1277）：
        /// 先做输入变量替换，再把 <c>{Name}</c> 占位交给 <see cref="GetStringVariable"/>。
        /// </summary>
        public string ReplaceStringVariables(DialogueNode node, DialogueLine line, string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string result = NarrativeFunctionLibrary.ReplaceInputVariables(OwningController, text);

            int iterations = 0;
            while (iterations < 50)
            {
                int openBrace = result.IndexOf('{');
                int closeBrace = result.IndexOf('}');

                if (openBrace < 0 || closeBrace < 0 || openBrace >= closeBrace) break;

                string variableName = result.Substring(openBrace + 1, closeBrace - openBrace - 1);
                string variableValue = GetStringVariable(node, line, variableName);

                result = result.Remove(openBrace, closeBrace - openBrace + 1);
                result = result.Insert(openBrace, variableValue ?? string.Empty);

                iterations++;
            }

            return result;
        }

        /// <summary>
        /// 单个变量的取值。对应 UE5 <c>GetStringVariable_Implementation</c>（Dialogue.cpp:1865-1873）。
        /// </summary>
        public virtual string GetStringVariable(DialogueNode node, DialogueLine line, string variableName)
        {
            if (variableName == "Time")
            {
                return ArsenalStatics.GetTimeOfDayAsString(OwningComp);
            }

            if (variableName == "PlayerName")
            {
                return PlayerSpeakerInfo != null ? PlayerSpeakerInfo.GetDisplayName() : PlayerSpeakerID;
            }

            if (variableName == "SpeakerName")
            {
                return CurrentSpeaker != null ? CurrentSpeaker.GetDisplayName() : "";
            }

            return variableName;
        }

        /// <summary>
        /// 求一行的真实播放时长（秒）。对应 UE5 <c>GetLineDuration_Implementation</c>（Dialogue.cpp:1831-1863）。
        /// <para>
        /// UE5 只对 AfterReadingTime / AfterDuration 返回固定秒数，其余靠 Audio/Sequence 的结束委托；
        /// Flax 端口在这里把音频资产长度、序列长度真实解析出来，
        /// 因此 <c>WhenAudioEnds</c> / <c>WhenSequenceEnds</c> 的行不再永远播不完。
        /// 返回 <see cref="float.MaxValue"/> 表示 <see cref="ELineDuration.Never"/>（只能靠跳过结束）。
        /// </para>
        /// </summary>
        public virtual float GetLineDuration(DialogueNode node, DialogueLine line)
        {
            if (line == null) return 0f;

            // 等 CG 的行把计时交给视频：播完（或玩家跳过）才收行，避免字幕先于画面结束。
            // 只有视频真的开播了才挂起计时——视频层不可用时挂起会让整段对话永远停在这一行。
            if (line.WaitForCutscene && !string.IsNullOrEmpty(line.CutscenePath) && _cutsceneWaiting) return float.MaxValue;

            float lettersPerSecond = 25f;
            float minDisplayTime = 2f;

            var settings = NarrativeDialogueSettings.Instance;
            if (settings != null)
            {
                lettersPerSecond = settings.LettersPerSecondLineDuration;
                minDisplayTime = settings.MinDialogueTextDisplayTime;
            }
            else
            {
                var narrativeSettings = NarrativeProPlugin.Instance?.NarrativeSettings;
                if (narrativeSettings != null && narrativeSettings.LettersPerSecond > 0f)
                {
                    lettersPerSecond = narrativeSettings.LettersPerSecond;
                }
            }

            switch (line.Duration)
            {
                case ELineDuration.AfterReadingTime:
                case ELineDuration.Default:
                {
                    if (string.IsNullOrWhiteSpace(line.Text)) return 0f;
                    return Math.Max(line.Text.Length / lettersPerSecond, minDisplayTime);
                }

                case ELineDuration.AfterDuration:
                {
                    // 对话行已经带了确切秒数
                    if (line.DurationSecondsOverride > 0f) return line.DurationSecondsOverride;

                    // 兜底：非单机路径下 WhenAudioEnds/WhenSequenceEnds 会被解析成 AfterDuration，
                    // 但音频加载失败时 DurationSecondsOverride 可能为 0，这里再取一次真实长度。
                    float audioDuration = ResolveAudioDuration(line);
                    return audioDuration > 0f ? audioDuration : 0f;
                }

                case ELineDuration.WhenAudioEnds:
                {
                    float audioDuration = ResolveAudioDuration(line);
                    if (audioDuration > 0f) return audioDuration;

                    // 没有可用音频：退化成阅读时长，避免行永远结束不了
                    if (string.IsNullOrWhiteSpace(line.Text)) return 0f;
                    return Math.Max(line.Text.Length / lettersPerSecond, minDisplayTime);
                }

                case ELineDuration.WhenSequenceEnds:
                {
                    float sequenceDuration = ResolveSequenceDuration(line);
                    if (sequenceDuration > 0f) return sequenceDuration;

                    float audioDuration = ResolveAudioDuration(line);
                    if (audioDuration > 0f) return audioDuration;

                    if (string.IsNullOrWhiteSpace(line.Text)) return 0f;
                    return Math.Max(line.Text.Length / lettersPerSecond, minDisplayTime);
                }

                case ELineDuration.Never:
                    return float.MaxValue;

                default:
                    return 0f;
            }
        }

        /// <summary>取语音资产真实长度（秒），失败返回 0。</summary>
        private static float ResolveAudioDuration(DialogueLine line)
        {
            if (line == null || string.IsNullOrEmpty(line.SoundPath)) return 0f;
            return DialogueNode.ResolveAudioDuration(line.SoundPath, 0f);
        }

        /// <summary>取镜头序列长度（秒），失败返回 0。</summary>
        private float ResolveSequenceDuration(DialogueLine line)
        {
            var sequence = ResolveShotForLine(line);
            if (sequence == null) return 0f;

            float duration = sequence.GetSequenceDurationSeconds();
            return duration > 0f ? duration / Math.Max(sequence.PlaybackSpeed, 0.01f) : 0f;
        }

        /// <summary>
        /// 按 UE5 的镜头优先级解析本行实际生效的镜头：
        /// 行上的 Shot → 说话者 DefaultSpeakerShot → 对话默认镜头（Dialogue.cpp:1764-1775）。
        /// </summary>
        private NarrativeDialogueSequence ResolveShotForLine(DialogueLine line)
        {
            if (line != null)
            {
                var shot = line.ResolveShot();
                if (shot != null) return shot;
            }

            if (CurrentSpeaker != null)
            {
                var speakerShot = CurrentSpeaker.ResolveDefaultSpeakerShot();
                if (speakerShot != null) return speakerShot;
            }

            return DefaultDialogueShot;
        }

        // ===== 行计时（Flax 替代 UE5 World Timer）=====

        /// <summary>开始一行的倒计时；Never 行不做自动结束。</summary>
        private void StartLineTiming(DialogueNode node, DialogueLine line)
        {
            float duration = GetLineDuration(node, line);

            if (duration >= float.MaxValue)
            {
                // 只能靠跳过结束
                CurrentLineRemainingTime = 0f;
                return;
            }

            CurrentLineRemainingTime = duration;

            if (duration <= 0.01f)
            {
                EndCurrentLine();
            }
        }

        /// <summary>推进当前行的计时。</summary>
        private void TickLineTiming(float deltaTime)
        {
            if (CurrentNode == null || CurrentLine == null) return;
            if (_waitingForPlayerInput) return;
            if (CurrentLine.Duration == ELineDuration.Never) return;
            if (CurrentLineRemainingTime <= 0f) return;

            CurrentLineRemainingTime -= deltaTime;

            // 音频提前结束（例如被停止）时也立刻收尾，对应 UE5 的 OnAudioFinished 绑定
            if (DialogueAudio != null && !DialogueAudio.IsActuallyPlaying && CurrentLineRemainingTime > 0.2f)
            {
                CurrentLineRemainingTime = 0f;
            }

            if (CurrentLineRemainingTime <= 0f)
            {
                CurrentLineRemainingTime = 0f;
                EndCurrentLine();
            }
        }

        // ===== 播放表现：语音 / 镜头 =====

        /// <summary>
        /// 播放一行（语音、镜头、旋转）。对应 UE5 <c>PlayDialogueNode_Implementation</c>（Dialogue.cpp:1734-1782）。
        /// </summary>
        protected virtual void PlayDialogueNode(DialogueNode node, DialogueLine line, SpeakerInfo speaker)
        {
            Actor speakerActor = GetAvatar(speaker != null ? speaker.GetSpeakerID() : "");
            Actor listenerActor = GetAvatar(node != null ? node.DirectedAtSpeakerID : "");

            if (listenerActor == null)
            {
                listenerActor = GetPlayerAvatar();
            }

            if (node is DialogueNode_NPC && listenerActor == null)
            {
                listenerActor = GetPlayerAvatar();
            }

            PlayDialogueSound(line, speakerActor, listenerActor);
            PlayDialogueCutscene(line);

            var shot = ResolveShotForLine(line);
            if (shot != null)
            {
                PlayDialogueSequence(shot, speakerActor, listenerActor);

                // 对应 UE5:1959-1962 —— LD_WhenSequenceEnds 时绑定序列结束事件来结束本行
                if (line != null && line.Duration == ELineDuration.WhenSequenceEnds)
                {
                    shot.OnSequenceFinished += OnPlayedSequenceFinished;
                }
            }
            else
            {
                StopDialogueSequence();
            }
        }

        // ===== 行内 CG / 过场视频 =====

        private NarrativeCommonVideoPlayer _cutscenePlayer;
        private bool _cutsceneWaiting;

        /// <summary>
        /// 播放本行挂的 CG。等播完的行（<see cref="DialogueLine.WaitForCutscene"/>）由视频结束事件收行；
        /// 不等播完的行只是叠一层视频，行计时照常。
        /// </summary>
        protected virtual void PlayDialogueCutscene(DialogueLine line)
        {
            StopDialogueCutscene();
            if (line == null || string.IsNullOrEmpty(line.CutscenePath)) return;

            try
            {
                var player = NarrativeRuntimeBootstrap.PlayVideo(line.CutscenePath);
                if (player == null)
                {
                    // 视频层不可用时不能挂起行计时，否则对话永远停在这一句
                    NarrativeLog.LogWarning($"对话 CG 未能播放（视频层不可用，本行按普通时长收行）：{line.CutscenePath} [NP_CG_NO_VIDEO_LAYER]");
                    return;
                }

                _cutscenePlayer = player;
                if (line.WaitForCutscene)
                {
                    _cutsceneWaiting = true;
                    _cutscenePlayer.OnPlaybackFinished += OnCutsceneFinished;
                }
            }
            catch (Exception ex)
            {
                _cutsceneWaiting = false;
                NarrativeLog.LogWarning($"对话 CG 播放失败：{line.CutscenePath}（{ex.Message}）[NP_CG_FAILED]");
            }
        }

        private void OnCutsceneFinished(NarrativeCommonVideoPlayer player)
        {
            if (player == null || player != _cutscenePlayer) return;

            _cutscenePlayer.OnPlaybackFinished -= OnCutsceneFinished;
            _cutscenePlayer = null;
            _cutsceneWaiting = false;

            if (CurrentLine != null && CurrentLine.WaitForCutscene) EndCurrentLine();
        }

        /// <summary>停掉本行 CG（切行/收行/跳过时调用）。</summary>
        public virtual void StopDialogueCutscene()
        {
            _cutsceneWaiting = false;
            if (_cutscenePlayer == null) return;

            _cutscenePlayer.OnPlaybackFinished -= OnCutsceneFinished;
            _cutscenePlayer = null;
            try { NarrativeRuntimeBootstrap.StopVideo(); }
            catch (Exception ex) { NarrativeLog.LogWarning("停止对话 CG 失败：" + ex.Message); }
        }

        /// <summary>序列播放结束事件驱动的收行。对应 UE5 <c>SP->OnFinished → EndCurrentLine</c>。</summary>
        private void OnPlayedSequenceFinished()
        {
            if (CurrentLine != null && CurrentLine.Duration == ELineDuration.WhenSequenceEnds)
            {
                EndCurrentLine();
            }
        }

        private void UnbindSequenceFinished()
        {
            if (CurrentDialogueSequence != null)
            {
                CurrentDialogueSequence.OnSequenceFinished -= OnPlayedSequenceFinished;
            }
        }

        /// <summary>
        /// 播放行语音。对应 UE5 <c>PlayDialogueSound_Implementation</c>（Dialogue.cpp:1704-1732）：
        /// 找到说话者就在其位置 3D 播放，找不到则 2D 播放。
        /// </summary>
        protected virtual void PlayDialogueSound(DialogueLine line, Actor speaker, Actor listener)
        {
            StopDialogueAudio();

            if (line == null || string.IsNullOrEmpty(line.SoundPath)) return;

            var clip = Content.Load<AudioClip>(line.SoundPath);
            if (clip == null)
            {
                NarrativeLog.LogWarning($"对话语音资产加载失败：{line.SoundPath}");
                return;
            }

            var source = new AudioSource
            {
                Name = "NarrativeDialogueAudio",
                Clip = clip,
                Volume = 1f,
                IsLooping = false,
                PlayOnStart = true,
                // Flax 的 AudioSource.Is3D 只读，2D/3D 由可写的 AllowSpatialization 决定：
                // 关掉空间化即等价于 UE5 的 2D 播放（PlayAtLocation 为 nullptr 分支）。
                AllowSpatialization = speaker != null
            };

            // 对白音源须显式生成进玩法场景，不靠"无父级默认落 RootScene"
            if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(source, source.Name))
            {
                FlaxEngine.Object.Destroy(source);
                return;
            }

            if (speaker != null)
            {
                source.Position = speaker.Position;
            }

            DialogueAudio = source;
        }

        /// <summary>停止并销毁当前对话语音。</summary>
        protected virtual void StopDialogueAudio()
        {
            if (DialogueAudio == null) return;

            DialogueAudio.Stop();
            FlaxEngine.Object.Destroy(DialogueAudio);
            DialogueAudio = null;
        }

        /// <summary>
        /// 播放镜头序列。对应 UE5 <c>PlayDialogueSequence</c>（Dialogue.cpp:1924-1972）。
        /// </summary>
        public virtual void PlayDialogueSequence(NarrativeDialogueSequence sequence, Actor speaker, Actor listener)
        {
            // 缺陷④兜底：本项目没有任何对话资产携带 Shot（镜头序列资产），UE5 里黑边/隐藏 HUD
            // 由 Level Sequence 的相机节点驱动，Flax 无 Level Sequence——呈现逻辑住在
            // NarrativeDialogueSequence.ApplyPresentation（它同时消费镜头级 bShowCinematicBars
            // 与玩家身上的 WantsCinematicBars/WantsHideHUD 标签）。因此当行上没有镜头资产、
            // 但电影呈现确被请求时，合成一个「只有呈现、没有绑定轨」的序列（等价于 UE5 里
            // 手搭一条仅含 Bars/HideHUD 相机节点的 Sequence），让呈现层对本项目唯一存在的
            // 「无镜头对话」也可达；无呈现请求时仍保持原语义直接返回。
            if (sequence == null)
            {
                sequence = CreateShotlessPresentationSequence();
                if (sequence == null) return;
            }

            if (CurrentDialogueSequence == sequence && !sequence.bShouldRestart)
            {
                // 同一镜头已在播，不重启（Dialogue.cpp 由 bShouldRestart 决定）
                return;
            }

            if (CurrentDialogueSequence != null)
            {
                UnbindSequenceFinished();
                CurrentDialogueSequence.EndSequence();
            }

            sequence.BeginPlaySequence(GetSequenceActor(), this, speaker, listener);
            CurrentDialogueSequence = sequence;
        }

        /// <summary>停止当前镜头序列。对应 UE5 <c>StopDialogueSequence</c>（Dialogue.cpp:1973-1998）。</summary>
        public virtual void StopDialogueSequence()
        {
            if (CurrentDialogueSequence == null) return;

            UnbindSequenceFinished();
            CurrentDialogueSequence.EndSequence();
            CurrentDialogueSequence = null;
        }

        /// <summary>承载镜头摄像机的 Actor（Flax 中直接挂在拥有者 Actor 下）。</summary>
        private Actor GetSequenceActor()
        {
            return OwningComp != null ? OwningComp.Actor : null;
        }

        /// <summary>
        /// 无镜头数据时的电影呈现兜底：仅当本对话配置了 <see cref="bShowCinematicBars"/>
        /// （DialogueFactory 由对话资产配置灌入）或本地玩家带 WantsCinematicBars / WantsHideHUD
        /// 对话标签时，构造一个空的 <see cref="NarrativeDialogueSequence"/> 承载呈现。
        /// 隐藏 HUD 的请求由序列自身的 ApplyPresentation 对账玩家标签（bHideHUD || 标签），
        /// 因此这里只需把对话级黑边意图传递进去；两者都未请求时返回 null（不改变普通对话路径）。
        /// </summary>
        private NarrativeDialogueSequence CreateShotlessPresentationSequence()
        {
            bool wantsBars = bShowCinematicBars;
            bool wantsHideHUD = false;

            var controller = Level.FindScript<NarrativePro.UnrealFramework.NarrativePlayerController>();
            if (controller != null)
            {
                wantsBars = wantsBars
                            || controller.HasMatchingGameplayTag(NarrativePro.Core.NarrativeGameplayTags.State_Player_WantsCinematicBars);
                wantsHideHUD = controller.HasMatchingGameplayTag(NarrativePro.Core.NarrativeGameplayTags.State_Player_WantsHideHUD);
            }

            if (!wantsBars && !wantsHideHUD) return null;

            var sequence = new NarrativeDialogueSequence();
            sequence.bShowCinematicBars = bShowCinematicBars;
            return sequence;
        }

        /// <summary>
        /// 收尾一行播放。对应 UE5 <c>FinishDialogueNode_Implementation</c>（Dialogue.cpp:1784-1795）。
        /// </summary>
        protected virtual void FinishDialogueNode(DialogueNode node, DialogueLine line, SpeakerInfo speaker)
        {
            StopDialogueAudio();
            StopDialogueSequence();
            SetPartyCurrentSpeaker(null);
        }

        // ===== 说话者替身 =====

        /// <summary>
        /// 为每个说话者解析出演 Actor。对应 UE5 <c>InitSpeakerAvatars</c>（Dialogue.cpp:850-895）。
        /// </summary>
        protected virtual void InitSpeakerAvatars()
        {
            SpeakerAvatars.Clear();

            if (Speakers == null) return;

            for (int i = 0; i < Speakers.Count; i++)
            {
                var speaker = Speakers[i];
                if (speaker == null || string.IsNullOrEmpty(speaker.GetSpeakerID())) continue;

                Actor avatar = null;

                // 播放参数直接指定的说话者优先（对应 UE5 FDialoguePlayParams::Speakers）
                if (PlayParams != null && PlayParams.Speakers != null && i < PlayParams.Speakers.Count)
                {
                    var explicitSpeaker = PlayParams.Speakers[i];
                    if (explicitSpeaker != null) avatar = explicitSpeaker.Actor;
                }

                avatar = avatar ?? LinkSpeakerAvatar(speaker, i);

                if (avatar != null)
                {
                    speaker.SpeakerAvatar = avatar;
                    SpeakerAvatars[speaker.GetSpeakerID()] = avatar;
                }
            }

            // 玩家侧替身就是拥有者 Pawn
            if (PlayerSpeakerInfo != null && OwningPawn is Actor pawnActor)
            {
                SpeakerAvatars[PlayerSpeakerInfo.GetSpeakerID()] = pawnActor;
                PlayerSpeakerInfo.SpeakerAvatar = pawnActor;
            }
        }

        /// <summary>
        /// 解析单个说话者的替身。对应 UE5 <c>LinkSpeakerAvatar_Implementation</c>（Dialogue.cpp:1567-1589）：
        /// 有 NPC 定义就用角色子系统查，否则按说话者 ID 查已生成的 NPC。
        /// </summary>
        public virtual Actor LinkSpeakerAvatar(SpeakerInfo info, int idx)
        {
            if (info == null) return null;

            var subsystem = NarrativeCharacterSubsystem.Instance;
            if (subsystem == null) return null;

            if (info.NPCDataAsset != null)
            {
                var byDefinition = subsystem.FindCharacter(info.NPCDataAsset);
                if (byDefinition != null) return byDefinition;
            }

            return subsystem.FindNPCByID(info.GetSpeakerID());
        }

        /// <summary>清理说话者替身。对应 UE5 <c>CleanUpSpeakerAvatars</c>（Dialogue.cpp:896-921）。</summary>
        protected virtual void CleanUpSpeakerAvatars()
        {
            if (Speakers != null)
            {
                foreach (var speaker in Speakers)
                {
                    if (speaker == null) continue;
                    DestroySpeakerAvatar(speaker, speaker.SpeakerAvatar);
                    speaker.SpeakerAvatar = null;
                }
            }

            SpeakerAvatars.Clear();
        }

        /// <summary>对应 UE5 <c>DestroySpeakerAvatar_Implementation</c>：替身若是真实生成物需要销毁。</summary>
        public virtual void DestroySpeakerAvatar(SpeakerInfo info, Actor speakerAvatar)
        {
            // 端口中的替身都是场景里已生成的角色（由 NPC 子系统/活动生成器管理），
            // 不属于对话所有，因此这里只断开引用，不销毁 Actor —— 销毁会误删关卡里的 NPC。
        }

        /// <summary>对应 UE5 <c>GetPlayerAvatar</c>（Dialogue.cpp:1279-1301）：玩家指定了 PlayerOverride 时按override 查。</summary>
        public virtual Actor GetPlayerAvatar()
        {
            var playerNode = CurrentNode as DialogueNode_Player;
            if (playerNode != null && playerNode.PlayerOverride != null)
            {
                var subsystem = NarrativeCharacterSubsystem.Instance;
                var overridden = subsystem != null ? subsystem.FindCharacter(playerNode.PlayerOverride) : null;
                if (overridden != null) return overridden;
            }

            return OwningPawn as Actor;
        }

        /// <summary>对应 UE5 <c>GetAvatar</c>（Dialogue.cpp:1303-1311）。</summary>
        public virtual Actor GetAvatar(string speakerID)
        {
            if (string.IsNullOrEmpty(speakerID)) return null;
            return SpeakerAvatars.TryGetValue(speakerID, out var avatar) ? avatar : null;
        }

        /// <summary>队伍对话下设置当前发言的玩家。对应 UE5 <c>SetPartyCurrentSpeaker</c>。</summary>
        public virtual void SetPartyCurrentSpeaker(object playerState)
        {
            var party = OwningComp as NarrativePartyComponent;
            party?.SetPartyCurrentSpeaker(playerState);
        }

        /// <summary>是否队伍对话。对应 UE5 <c>IsPartyDialogue</c>。</summary>
        public bool IsPartyDialogue()
        {
            return OwningComp is NarrativePartyComponent;
        }

        /// <summary>是否单机（没有连网络的本地会话）。对应 UE5 <c>GetNetMode() == NM_Standalone</c>。</summary>
        public bool IsStandalone()
        {
            return OwningComp == null || OwningComp.IsStandalone();
        }

        /// <summary>
        /// 把当前 chunk（剩余 NPC 链 + 可用回复）复制给客户端。
        /// 对应 UE5 中队伍分支与 solo 分支的 <c>ClientRecieveDialogueChunk</c> 调用
        /// （Dialogue.cpp:387-401 / 1435-1449）。
        /// </summary>
        private void SendDialogueChunkToClients()
        {
            if (OwningComp == null || !OwningComp.HasAuthority() || OwningComp.IsStandalone()) return;

            var npcIds = MakeIDsFromNPCNodes(NPCReplyChain);
            var playerIds = MakeIDsFromPlayerNodes(AvailableResponses);

            var party = OwningComp as NarrativePartyComponent;
            if (party != null)
            {
                foreach (var member in party.GetPartyMembers())
                {
                    member?.ClientReceiveDialogueChunk(npcIds, playerIds);
                }
            }
            else
            {
                OwningComp.ClientReceiveDialogueChunk(npcIds, playerIds);
            }
        }

        /// <summary>OnPlayNodeFuncName 派发。对应 UE5 蓝图 FindFunction/ProcessEvent 等价物。</summary>
        public void DispatchNodePlayed(DialogueNode node, bool bPlaying)
        {
            OnNodePlayedFunc?.Invoke(node, bPlaying);
        }

        /// <summary>节点播放函数（OnPlayNodeFuncName）派发回调。</summary>
        public event Action<DialogueNode, bool> OnNodePlayedFunc;

        /// <summary>节点事件处理。对应 UE5 <c>ProcessNodeEvents</c>（Dialogue.cpp:957-979）。</summary>
        protected void ProcessNodeEvents(DialogueNode node, bool bStartEvents)
        {
            if (node == null) return;

            if (!string.IsNullOrEmpty(node.OnPlayNodeFuncName))
            {
                DispatchNodePlayed(node, bStartEvents);
            }

            var runtime = bStartEvents ? EEventRuntime.Start : EEventRuntime.End;
            node.ProcessEvents(OwningPawn, OwningController, OwningComp, runtime);
        }

        /// <summary>标记进入「等待玩家输入」状态。</summary>
        internal void SetWaitingForPlayerInput(bool waiting)
        {
            _waitingForPlayerInput = waiting;
        }

        /// <summary>是否正在等待玩家输入。</summary>
        public bool IsWaitingForPlayerInput()
        {
            return _waitingForPlayerInput;
        }
    }
}
