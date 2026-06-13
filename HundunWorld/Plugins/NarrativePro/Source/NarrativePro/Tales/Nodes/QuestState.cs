using System.Collections.Generic;
using NarrativePro.Core;

namespace NarrativePro.Tales.Nodes
{
    public class QuestState : QuestNode
    {
        public List<QuestBranch> Branches { get; set; } = new List<QuestBranch>();
        public EStateNodeType StateNodeType { get; set; } = EStateNodeType.Regular;

        public virtual void Activate() { }
        public virtual void Deactivate() { }
    }
}
