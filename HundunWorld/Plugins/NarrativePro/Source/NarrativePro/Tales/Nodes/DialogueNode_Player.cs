namespace NarrativePro.Tales.Nodes
{
    public class DialogueNode_Player : DialogueNode
    {
        public string OptionText { get; set; } = "";
        public string HintText { get; set; } = "";
        public bool bAutoSelect { get; set; } = false;
        public bool bAutoSelectIfOnlyReply { get; set; } = true;

        public bool IsAutoSelect()
        {
            return bAutoSelect || IsRoutingNode();
        }

        public bool IsAutoSelectIfOnlyReply()
        {
            return bAutoSelectIfOnlyReply || IsRoutingNode();
        }

        public string GetOptionText()
        {
            return !string.IsNullOrEmpty(OptionText) ? OptionText : (Line?.Text ?? "");
        }

        public string GetHintText()
        {
            return HintText ?? "";
        }
    }
}
