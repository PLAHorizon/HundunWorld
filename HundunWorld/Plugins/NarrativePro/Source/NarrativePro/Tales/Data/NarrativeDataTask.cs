using System;

namespace NarrativePro.Tales.Data
{
    [Serializable]
    public class NarrativeDataTask
    {
        public string TaskName { get; set; } = "";
        public string TaskDescription { get; set; } = "";
        public string ArgumentName { get; set; } = "";
        public string TaskCategory { get; set; } = "";
        public string DefaultArgument { get; set; } = "";

        public string MakeTaskString(string argument)
        {
            string str = (TaskName + "_" + argument).ToLower();
            str = str.Replace(" ", "");
            return str;
        }
    }
}
