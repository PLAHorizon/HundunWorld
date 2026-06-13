using System;
using System.Collections.Generic;
using NarrativePro.Tales.Data;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales.Nodes
{
    public class DialogueNode : NarrativeNodeBase
    {
        public DialogueLine Line { get; set; } = new DialogueLine();
        public List<DialogueLine> AlternativeLines { get; set; } = new List<DialogueLine>();
        public string OnPlayNodeFuncName { get; set; } = "";
        public string DirectedAtSpeakerID { get; set; } = "";
        public bool bIsSkippable { get; set; } = true;
        public DialogueLine PlayedLine { get; set; }

        public List<DialogueNode_NPC> NPCReplies { get; set; } = new List<DialogueNode_NPC>();
        public List<DialogueNode_Player> PlayerReplies { get; set; } = new List<DialogueNode_Player>();

        public DialogueClass OwningDialogue { get; set; }
        public object OwningComponent { get; set; }

        public virtual DialogueLine GetRandomLine(bool standalone = true)
        {
            if (AlternativeLines == null || AlternativeLines.Count == 0)
            {
                return Line;
            }

            var allLines = new List<DialogueLine> { Line };
            allLines.AddRange(AlternativeLines);

            var validLines = allLines.FindAll(l => l != null);
            if (validLines.Count == 0) return Line;

            var random = new Random();
            return validLines[random.Next(validLines.Count)];
        }

        public bool IsRoutingNode()
        {
            return string.IsNullOrEmpty(Line?.Text) && (AlternativeLines == null || AlternativeLines.Count == 0);
        }
    }
}
