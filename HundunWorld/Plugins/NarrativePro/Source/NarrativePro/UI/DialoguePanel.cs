using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Tales;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Nodes;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;
using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.UI
{
    public class DialoguePanel : Script
    {
        public TalesComponent TalesComponentRef;
        public float TypewriterSpeed = 30f;
        public bool bIsVisible;

        public event Action<int> ReplySelected;
        public event Action SkipRequested;

        private string _currentSpeakerName = "";
        private string _currentDialogueText = "";
        private string _displayedText = "";
        private int _typewriterIndex;
        private List<DialogueNode_Player> _availableReplies = new List<DialogueNode_Player>();
        private bool _isTyping;

        public override void OnEnable()
        {
            Bind(TalesComponentRef);
        }

        public override void OnDisable()
        {
            Unbind();
        }

        /// <summary>
        /// 绑定（或切换）要监听的叙事组件。运行期由 <c>NarrativeRuntimeBootstrap</c> 在
        /// TalesComponent 就绪后调用 —— 面板可以先于叙事组件启用，因此不能只在 OnEnable 里订阅。
        /// 幂等：重复绑定同一组件不会重复订阅。
        /// </summary>
        public void Bind(TalesComponent tales)
        {
            if (ReferenceEquals(_boundTales, tales) && tales != null) return;

            Unbind();
            TalesComponentRef = tales;
            _boundTales = tales;
            if (tales == null) return;

            tales.OnNPCDialogueLineStarted += OnNPCDialogueLineStarted;
            tales.OnNPCDialogueLineFinished += OnNPCDialogueLineFinished;
            tales.OnDialogueRepliesAvailable += OnDialogueRepliesAvailable;
            tales.OnDialogueFinished += OnDialogueFinished;
        }

        /// <summary>解除当前叙事组件订阅。</summary>
        public void Unbind()
        {
            var tales = _boundTales;
            if (tales != null)
            {
                tales.OnNPCDialogueLineStarted -= OnNPCDialogueLineStarted;
                tales.OnNPCDialogueLineFinished -= OnNPCDialogueLineFinished;
                tales.OnDialogueRepliesAvailable -= OnDialogueRepliesAvailable;
                tales.OnDialogueFinished -= OnDialogueFinished;
            }
            _boundTales = null;
        }

        private TalesComponent _boundTales;

        public void Show()
        {
            bIsVisible = true;
        }

        public void Hide()
        {
            bIsVisible = false;
            _currentSpeakerName = "";
            _currentDialogueText = "";
            _displayedText = "";
            _typewriterIndex = 0;
            _availableReplies.Clear();
            _isTyping = false;
        }

        public void SetNPCLine(string speakerName, string text)
        {
            _currentSpeakerName = speakerName;
            _currentDialogueText = text;
            _displayedText = "";
            _typewriterIndex = 0;
            _isTyping = true;
            _availableReplies.Clear();
        }

        public void SetPlayerReplies(List<DialogueNode_Player> replies)
        {
            _availableReplies.Clear();
            if (replies != null)
            {
                _availableReplies.AddRange(replies);
            }
        }

        public void SkipTypewriter()
        {
            if (!_isTyping) return;
            _typewriterIndex = _currentDialogueText.Length;
            _displayedText = _currentDialogueText;
            _isTyping = false;
            SkipRequested?.Invoke();
        }

        public void OnReplySelected(int index)
        {
            if (index < 0 || index >= _availableReplies.Count) return;
            var selectedReply = _availableReplies[index];
            TalesComponentRef?.TrySelectDialogueOption(selectedReply);
            ReplySelected?.Invoke(index);
            _availableReplies.Clear();
        }

        public override void OnUpdate()
        {
            if (!_isTyping || string.IsNullOrEmpty(_currentDialogueText)) return;

            _typewriterIndex += (int)(TypewriterSpeed * Time.DeltaTime);
            if (_typewriterIndex >= _currentDialogueText.Length)
            {
                _typewriterIndex = _currentDialogueText.Length;
                _displayedText = _currentDialogueText;
                _isTyping = false;
            }
            else
            {
                _displayedText = _currentDialogueText.Substring(0, _typewriterIndex);
            }
        }

        private void OnNPCDialogueLineStarted(TalesComponent tales, DialogueClass dialogue, DialogueNode_NPC node, DialogueLine line, SpeakerInfo speaker)
        {
            Show();
            string speakerName = speaker != null ? speaker.DisplayName : "";
            string text = line != null ? line.Text : "";
            SetNPCLine(speakerName, text);
        }

        private void OnNPCDialogueLineFinished(TalesComponent tales, DialogueClass dialogue, DialogueNode_NPC node, DialogueLine line, SpeakerInfo speaker)
        {
            if (_isTyping)
            {
                SkipTypewriter();
            }
        }

        private void OnDialogueRepliesAvailable(TalesComponent tales, DialogueClass dialogue, List<DialogueNode_Player> replies)
        {
            SetPlayerReplies(replies);
        }

        private void OnDialogueFinished(TalesComponent tales, DialogueClass dialogue, EExitDialogueReason reason)
        {
            Hide();
        }
    }
}
