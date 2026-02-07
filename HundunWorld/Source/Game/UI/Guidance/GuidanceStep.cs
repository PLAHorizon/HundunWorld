using System;
using FlaxEngine;

namespace HundunWorld.Game.UI.Guidance
{
    /// <summary>
    /// 引导步骤信息
    /// </summary>
    public class GuidanceStep
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string TargetElementId { get; set; }
        public Float2 Position { get; set; }
        public bool ShowHighlight { get; set; }
        public Action OnComplete { get; set; }

        public GuidanceStep(string id, string title, string description, string targetElementId = "", bool showHighlight = true)
        {
            Id = id;
            Title = title;
            Description = description;
            TargetElementId = targetElementId;
            ShowHighlight = showHighlight;
        }
    }
}