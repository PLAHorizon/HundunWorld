using System;

namespace Horizon.Game.GengDi.Core.Controls
{
    public enum ToastType
    {
        Success,
        Warning,
        Error,
        Info
    }

    public class ToastMessage
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
        public string Content { get; set; } = "";
        public ToastType Type { get; set; } = ToastType.Info;
        public int DurationMs { get; set; } = 3000;
    }
}
