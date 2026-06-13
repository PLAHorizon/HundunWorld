using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Horizon.Game.GengDi.Core.Controls
{
    public sealed class ToastService
    {
        private static readonly Lazy<ToastService> _instance = new(() => new ToastService());
        public static ToastService Instance => _instance.Value;

        public ObservableCollection<ToastMessage> ActiveToasts { get; } = new();

        private ToastService() { }

        public void Show(string content, ToastType type = ToastType.Info, int durationMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            var toast = new ToastMessage
            {
                Content = content,
                Type = type,
                DurationMs = durationMs
            };
            Post(() => ActiveToasts.Add(toast));
            _ = AutoDismissAsync(toast);
        }

        public void Success(string content, int durationMs = 3000) => Show(content, ToastType.Success, durationMs);
        public void Warning(string content, int durationMs = 4000) => Show(content, ToastType.Warning, durationMs);
        public void Error(string content, int durationMs = 5000) => Show(content, ToastType.Error, durationMs);
        public void Info(string content, int durationMs = 3000) => Show(content, ToastType.Info, durationMs);

        public void Dismiss(ToastMessage toast)
        {
            if (toast != null)
                Post(() => { if (ActiveToasts.Contains(toast)) ActiveToasts.Remove(toast); });
        }

        public void DismissAll()
        {
            Post(() => ActiveToasts.Clear());
        }

        private async System.Threading.Tasks.Task AutoDismissAsync(ToastMessage toast)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(toast.DurationMs);
                Dismiss(toast);
            }
            catch { }
        }

        private static void Post(Action action)
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                action();
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(action);
        }
    }
}
