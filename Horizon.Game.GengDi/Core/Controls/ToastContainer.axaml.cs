using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Horizon.Game.GengDi.Core.Controls
{
    public partial class ToastContainer : UserControl
    {
        public ObservableCollection<ToastMessage> Toasts => ToastService.Instance.ActiveToasts;

        public ToastContainer()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = this;
            Toasts.CollectionChanged += OnToastsChanged;
        }

        private void OnToastsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null && e.NewItems.Count > 0)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(80);
                    var itemsControl = this.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault();
                    if (itemsControl == null) return;
                    foreach (var container in itemsControl.GetRealizedContainers())
                    {
                        var border = container.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "ToastBorder");
                        if (border != null && border.Opacity < 1)
                            border.Opacity = 1;
                    }
                });
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ToastMessage toast)
            {
                ToastService.Instance.Dismiss(toast);
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            Toasts.CollectionChanged -= OnToastsChanged;
        }
    }
}
