using System;
using System.Collections.Generic;
using NarrativePro.Core;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Nodes;

namespace NarrativePro.Tales.Dialogue
{
    public class Dialogue
    {
        public string DialogueId { get; set; } = "";
        public List<SpeakerInfo> Speakers { get; set; } = new List<SpeakerInfo>();
        public PlayerSpeakerInfo PlayerSpeakerInfo { get; set; } = new PlayerSpeakerInfo();
        public DialogueNode_NPC RootDialogue { get; set; }
        public List<DialogueNode_NPC> NPCReplies { get; set; } = new List<DialogueNode_NPC>();
        public List<DialogueNode_Player> PlayerReplies { get; set; } = new List<DialogueNode_Player>();

        public DialogueNode CurrentNode { get; protected set; }
        public SpeakerInfo CurrentSpeaker { get; protected set; }
        public DialogueLine CurrentLine { get; protected set; }
        public List<DialogueNode_NPC> NPCReplyChain { get; protected set; } = new List<DialogueNode_NPC>();
        public List<DialogueNode_Player> AvailableResponses { get; protected set; } = new List<DialogueNode_Player>();

        public float EndDialogueDist { get; set; } = 0f;
        public bool bShowCinematicBars { get; set; } = false;
        public bool bUnskippable { get; set; } = false;
        public bool bFreeMovement { get; set; } = true;
        public bool bCanBeExited { get; set; } = true;
        public bool bAutoRotateSpeakers { get; set; } = false;
        public bool bAutoStopMovement { get; set; } = false;
        public int Priority { get; set; } = 0;
        public string DefaultHeadBoneName { get; set; } = "";
        public float DialogueBlendOutTime { get; set; } = 0.5f;

        public object OwningComp { get; protected set; }
        public object OwningPawn { get; protected set; }
        public object OwningController { get; protected set; }

        public bool bDeinitialized { get; protected set; } = false;
        public bool bBeganPlaying { get; protected set; } = false;

        public DialoguePlayParams PlayParams { get; protected set; } = new DialoguePlayParams();

        public float CurrentLineRemainingTime { get; protected set; } = 0f;
        protected int _currentNPCReplyIndex = -1;
        protected bool _waitingForPlayerInput = false;

        public event Action<Dialogue> OnBeginDialogue;
        public event Action<Dialogue> OnEndDialogue;
        public event Action<Dialogue, DialogueNode_NPC, DialogueLine, SpeakerInfo> OnNPCDialogueLineStarted;
        public event Action<Dialogue, DialogueNode_NPC, DialogueLine, SpeakerInfo> OnNPCDialogueLineFinished;
        public event Action<Dialogue, DialogueNode_Player, DialogueLine> OnPlayerDialogueLineStarted;
        public event Action<Dialogue, DialogueNode_Player, DialogueLine> OnPlayerDialogueLineFinished;
        public event Action<Dialogue, List<DialogueNode_Player>> OnDialogueRepliesAvailable;

        public void Initialize(object comp, DialoguePlayParams playParams)
        {
            OwningComp = comp;
            PlayParams = playParams ?? new DialoguePlayParams();

            if (PlayParams.bOverrideFreeMovement)
                bFreeMovement = PlayParams.bFreeMovement;
            if (PlayParams.bOverrideStopMovement)
                bAutoStopMovement = PlayParams.bStopMovement;
            if (PlayParams.bOverrideUnskippable)
                bUnskippable = PlayParams.bUnskippable;
            if (PlayParams.Priority >= 0)
                Priority = PlayParams.Priority;

            if (!string.IsNullOrEmpty(PlayParams.StartFromID))
            {
                var startNode = NPCReplies.Find(n => n.ID == PlayParams.StartFromID);
                if (startNode != null)
                    RootDialogue = startNode;
                else
                    NarrativeLog.LogWarning($"StartFromID '{PlayParams.StartFromID}' not found, using default root");
            }

            foreach (var node in NPCReplies)
            {
                node.OwningDialogue = this;
                node.OwningComponent = OwningComp;
            }
            foreach (var node in PlayerReplies)
            {
                node.OwningDialogue = this;
                node.OwningComponent = OwningComp;
            }

            bDeinitialized = false;
            NarrativeLog.Log($"Dialogue '{DialogueId}' initialized");
        }

        public void Deinitialize()
        {
            if (bDeinitialized) return;

            CurrentNode = null;
            CurrentSpeaker = null;
            CurrentLine = null;
            NPCReplyChain.Clear();
            AvailableResponses.Clear();
            _currentNPCReplyIndex = -1;
            _waitingForPlayerInput = false;
            CurrentLineRemainingTime = 0f;

            bDeinitialized = true;
            NarrativeLog.Log($"Dialogue '{DialogueId}' deinitialized");
        }

        public void Play()
        {
            if (bDeinitialized)
            {
                NarrativeLog.LogWarning($"Cannot play deinitialized dialogue '{DialogueId}'");
                return;
            }

            if (RootDialogue == null)
            {
                NarrativeLog.LogError($"Dialogue '{DialogueId}' has no root node");
                return;
            }

            bBeganPlaying = true;
            OnBeginDialogue?.Invoke(this);

            GenerateDialogueChunk(RootDialogue);
        }

        public void EndCurrentLine()
        {
            if (CurrentNode == null) return;

            if (CurrentNode is DialogueNode_NPC npcNode)
            {
                FinishNPCDialogue();
            }
            else if (CurrentNode is DialogueNode_Player playerNode)
            {
                FinishPlayerDialogue();
            }
        }

        public bool SkipCurrentLine()
        {
            if (!CanSkipCurrentLine()) return false;

            EndCurrentLine();
            return true;
        }

        public bool CanSkipCurrentLine()
        {
            if (CurrentNode == null) return false;
            if (bUnskippable) return false;
            return CurrentNode.bIsSkippable;
        }

        public void SelectDialogueOption(DialogueNode_Player playerNode)
        {
            if (!CanSelectDialogueOption(playerNode)) return;

            _waitingForPlayerInput = false;
            CurrentNode = playerNode;
            CurrentSpeaker = PlayerSpeakerInfo;
            CurrentLine = playerNode.GetRandomLine();
            playerNode.PlayedLine = CurrentLine;

            ProcessNodeEvents(playerNode, true);
            OnPlayerDialogueLineStarted?.Invoke(this, playerNode, CurrentLine);
        }

        public bool CanSelectDialogueOption(DialogueNode_Player playerNode)
        {
            if (playerNode == null) return false;
            if (!_waitingForPlayerInput) return false;
            if (!AvailableResponses.Contains(playerNode)) return false;
            return playerNode.AreConditionsMet(OwningPawn, OwningController, OwningComp);
        }

        public void GenerateDialogueChunk(DialogueNode_NPC npcNode)
        {
            if (npcNode == null)
            {
                ExitDialogue(EExitDialogueReason.NoLines);
                return;
            }

            if (!npcNode.AreConditionsMet(OwningPawn, OwningController, OwningComp))
            {
                ExitDialogue(EExitDialogueReason.NoLines);
                return;
            }

            NPCReplyChain = npcNode.GetReplyChain();
            AvailableResponses.Clear();
            _currentNPCReplyIndex = -1;

            var lastNPCInChain = NPCReplyChain.Count > 0 ? NPCReplyChain[NPCReplyChain.Count - 1] : null;
            if (lastNPCInChain != null && lastNPCInChain.PlayerReplies != null)
            {
                foreach (var playerReply in lastNPCInChain.PlayerReplies)
                {
                    if (playerReply.AreConditionsMet(OwningPawn, OwningController, OwningComp))
                    {
                        AvailableResponses.Add(playerReply);
                    }
                }
            }

            PlayNextNPCReply();
        }

        public bool HasValidChunk()
        {
            return NPCReplyChain != null && NPCReplyChain.Count > 0 && _currentNPCReplyIndex < NPCReplyChain.Count;
        }

        public SpeakerInfo GetSpeaker(string speakerID)
        {
            if (PlayerSpeakerInfo != null && PlayerSpeakerInfo.SpeakerID == speakerID)
                return PlayerSpeakerInfo;

            if (Speakers != null)
            {
                foreach (var speaker in Speakers)
                {
                    if (speaker.SpeakerID == speakerID)
                        return speaker;
                }
            }

            return null;
        }

        public List<DialogueNode> GetNodes()
        {
            var nodes = new List<DialogueNode>();
            nodes.AddRange(NPCReplies);
            nodes.AddRange(PlayerReplies);
            return nodes;
        }

        public string ReplaceStringVariables(DialogueNode node, DialogueLine line, string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string result = text;
            if (CurrentSpeaker != null)
                result = result.Replace("{SpeakerName}", CurrentSpeaker.DisplayName);
            if (PlayerSpeakerInfo != null)
                result = result.Replace("{PlayerName}", PlayerSpeakerInfo.DisplayName);

            return result;
        }

        public float GetLineDuration(DialogueNode node, DialogueLine line)
        {
            if (line == null) return 0f;

            float lettersPerSec = 30f;
            var settings = NarrativeProPlugin.Instance?.NarrativeSettings;
            if (settings != null) lettersPerSec = settings.LettersPerSecond;

            switch (line.Duration)
            {
                case ELineDuration.AfterReadingTime:
                    if (string.IsNullOrEmpty(line.Text)) return 2f;
                    return Math.Max(2f, line.Text.Length / lettersPerSec);

                case ELineDuration.AfterDuration:
                    return line.DurationSecondsOverride > 0 ? line.DurationSecondsOverride : 2f;

                case ELineDuration.WhenAudioEnds:
                case ELineDuration.WhenSequenceEnds:
                    return 0f;

                case ELineDuration.Never:
                    return float.MaxValue;

                case ELineDuration.Default:
                default:
                    if (string.IsNullOrEmpty(line.Text)) return 2f;
                    return Math.Max(2f, line.Text.Length / lettersPerSec);
            }
        }

        public void TickDialogue(float deltaTime)
        {
            if (!bBeganPlaying || bDeinitialized) return;
            if (CurrentLine == null) return;
            if (CurrentLine.Duration == ELineDuration.Never) return;
            if (CurrentLine.Duration == ELineDuration.WhenAudioEnds) return;
            if (CurrentLine.Duration == ELineDuration.WhenSequenceEnds) return;

            if (CurrentLineRemainingTime > 0f)
            {
                CurrentLineRemainingTime -= deltaTime;
                if (CurrentLineRemainingTime <= 0f)
                {
                    CurrentLineRemainingTime = 0f;
                    EndCurrentLine();
                }
            }
        }

        public void ExitDialogue(EExitDialogueReason reason)
        {
            if (bDeinitialized) return;

            NarrativeLog.Log($"Dialogue '{DialogueId}' exiting: {reason}");

            if (CurrentNode != null)
            {
                ProcessNodeEvents(CurrentNode, false);
            }

            CurrentNode = null;
            CurrentSpeaker = null;
            CurrentLine = null;
            NPCReplyChain.Clear();
            AvailableResponses.Clear();
            _currentNPCReplyIndex = -1;
            _waitingForPlayerInput = false;
            CurrentLineRemainingTime = 0f;

            OnEndDialogue?.Invoke(this);
            Deinitialize();
        }

        public void PlayNextNPCReply()
        {
            _currentNPCReplyIndex++;

            if (_currentNPCReplyIndex >= NPCReplyChain.Count)
            {
                FinishNPCDialogue();
                return;
            }

            var npcNode = NPCReplyChain[_currentNPCReplyIndex];
            CurrentNode = npcNode;
            CurrentSpeaker = GetSpeaker(npcNode.SpeakerID);
            CurrentLine = npcNode.GetRandomLine();
            npcNode.PlayedLine = CurrentLine;

            ProcessNodeEvents(npcNode, true);

            var duration = GetLineDuration(npcNode, CurrentLine);
            CurrentLineRemainingTime = duration;

            OnNPCDialogueLineStarted?.Invoke(this, npcNode, CurrentLine, CurrentSpeaker);
        }

        public void FinishNPCDialogue()
        {
            if (CurrentNode is DialogueNode_NPC npcNode)
            {
                ProcessNodeEvents(npcNode, false);
                OnNPCDialogueLineFinished?.Invoke(this, npcNode, CurrentLine, CurrentSpeaker);
            }

            if (_currentNPCReplyIndex < NPCReplyChain.Count - 1)
            {
                PlayNextNPCReply();
                return;
            }

            CurrentNode = null;
            CurrentLine = null;
            CurrentLineRemainingTime = 0f;

            if (AvailableResponses.Count == 0)
            {
                ExitDialogue(EExitDialogueReason.NoLines);
                return;
            }

            if (AvailableResponses.Count == 1 && AvailableResponses[0].IsAutoSelectIfOnlyReply())
            {
                SelectDialogueOption(AvailableResponses[0]);
                return;
            }

            var autoSelect = AvailableResponses.Find(r => r.IsAutoSelect());
            if (autoSelect != null)
            {
                SelectDialogueOption(autoSelect);
                return;
            }

            _waitingForPlayerInput = true;
            OnDialogueRepliesAvailable?.Invoke(this, AvailableResponses);
        }

        public void FinishPlayerDialogue()
        {
            DialogueNode_Player finishedNode = CurrentNode as DialogueNode_Player;

            if (finishedNode != null)
            {
                ProcessNodeEvents(finishedNode, false);
                OnPlayerDialogueLineFinished?.Invoke(this, finishedNode, CurrentLine);
            }

            CurrentNode = null;
            CurrentLine = null;
            CurrentLineRemainingTime = 0f;

            if (finishedNode != null && finishedNode.NPCReplies != null && finishedNode.NPCReplies.Count > 0)
            {
                GenerateDialogueChunk(finishedNode.NPCReplies[0]);
                return;
            }

            ExitDialogue(EExitDialogueReason.NoLines);
        }

        public bool IsPlaying()
        {
            return bBeganPlaying && !bDeinitialized;
        }

        public bool IsInitialized()
        {
            return !bDeinitialized;
        }

        protected void ProcessNodeEvents(DialogueNode node, bool bStartEvents)
        {
            var runtime = bStartEvents ? EEventRuntime.Start : EEventRuntime.End;
            node.ProcessEvents(OwningPawn, OwningController, OwningComp, runtime);
        }
    }
}
