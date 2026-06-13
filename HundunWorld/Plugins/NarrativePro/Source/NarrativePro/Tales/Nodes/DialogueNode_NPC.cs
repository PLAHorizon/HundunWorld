using System.Collections.Generic;

namespace NarrativePro.Tales.Nodes
{
    public class DialogueNode_NPC : DialogueNode
    {
        public string SpeakerID { get; set; } = "";
        public string SelectingReplyShotName { get; set; } = "";

        public List<DialogueNode_NPC> GetReplyChain()
        {
            var chain = new List<DialogueNode_NPC>();
            var current = this;
            while (current != null)
            {
                chain.Add(current);
                if (current.NPCReplies != null && current.NPCReplies.Count > 0)
                {
                    current = current.NPCReplies[0];
                }
                else
                {
                    current = null;
                }
            }
            return chain;
        }
    }
}
