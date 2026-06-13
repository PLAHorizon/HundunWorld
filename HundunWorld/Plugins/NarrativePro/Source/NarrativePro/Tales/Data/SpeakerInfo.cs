using System;
using System.Collections.Generic;

namespace NarrativePro.Tales.Data
{
    [Serializable]
    public class SpeakerInfo
    {
        public string SpeakerID { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();
        public bool IsPlayer { get; set; } = false;
    }

    [Serializable]
    public class PlayerSpeakerInfo : SpeakerInfo
    {
        public string SelectingReplyShotName { get; set; } = "";

        public PlayerSpeakerInfo()
        {
            SpeakerID = "Player";
            IsPlayer = true;
        }
    }
}
