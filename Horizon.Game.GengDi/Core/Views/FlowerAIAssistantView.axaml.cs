using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerAIAssistantView : UserControl
    {
        public FlowerAIAssistantView()
        {
            InitializeComponent();
            DataContext = new FlowerAIAssistantViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is FlowerAIAssistantViewModel vm && vm.CanSend)
            {
                _ = vm.SendMessageAsync();
                e.Handled = true;
            }
        }

        private void OnSuggestionTapped(object? sender, TappedEventArgs e)
        {
            if (sender is Border border && border.Child is TextBlock textBlock && DataContext is FlowerAIAssistantViewModel vm)
            {
                vm.InputText = textBlock.Text;
                _ = vm.SendMessageAsync();
            }
        }
    }
}
