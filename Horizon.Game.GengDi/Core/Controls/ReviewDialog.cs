using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Controls
{
    public class ReviewDialog : UserControl
    {
        private readonly ReviewOrderViewModel _viewModel;
        private TaskCompletionSource<bool> _tcs;

        public ReviewDialog(ReviewOrderViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;

            var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24) };

            var title = new TextBlock { Text = "评价订单", FontSize = 18, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(title);

            var descLabel = new TextBlock { Text = "描述相符", FontSize = 13 };
            panel.Children.Add(descLabel);
            var descSlider = new Slider { Minimum = 1, Maximum = 5, Value = 5, TickFrequency = 1, IsSnapToTickEnabled = true };
            descSlider.Bind(Slider.ValueProperty, new Avalonia.Data.Binding("DescriptionScore"));
            panel.Children.Add(descSlider);

            var serviceLabel = new TextBlock { Text = "服务态度", FontSize = 13 };
            panel.Children.Add(serviceLabel);
            var serviceSlider = new Slider { Minimum = 1, Maximum = 5, Value = 5, TickFrequency = 1, IsSnapToTickEnabled = true };
            serviceSlider.Bind(Slider.ValueProperty, new Avalonia.Data.Binding("ServiceScore"));
            panel.Children.Add(serviceSlider);

            var logisticsLabel = new TextBlock { Text = "物流速度", FontSize = 13 };
            panel.Children.Add(logisticsLabel);
            var logisticsSlider = new Slider { Minimum = 1, Maximum = 5, Value = 5, TickFrequency = 1, IsSnapToTickEnabled = true };
            logisticsSlider.Bind(Slider.ValueProperty, new Avalonia.Data.Binding("LogisticsScore"));
            panel.Children.Add(logisticsSlider);

            var contentBox = new TextBox
            {
                Watermark = "请输入评价内容",
                AcceptsReturn = true,
                Height = 80,
                TextWrapping = TextWrapping.Wrap
            };
            contentBox.Bind(TextBox.TextProperty, new Avalonia.Data.Binding("Content"));
            panel.Children.Add(contentBox);

            var anonCheck = new CheckBox { Content = "匿名评价" };
            anonCheck.Bind(CheckBox.IsCheckedProperty, new Avalonia.Data.Binding("IsAnonymous"));
            panel.Children.Add(anonCheck);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
            var cancelBtn = new Button { Content = "取消", Padding = new Thickness(24, 10), CornerRadius = new CornerRadius(10) };
            cancelClick(cancelBtn);
            btnPanel.Children.Add(cancelBtn);

            var submitBtn = new Button { Content = "提交评价", Padding = new Thickness(24, 10), CornerRadius = new CornerRadius(10) };
            submitBtn.Click += OnSubmit;
            btnPanel.Children.Add(submitBtn);
            panel.Children.Add(btnPanel);

            Content = panel;
        }

        private void cancelClick(Button cancelBtn)
        {
            cancelBtn.Click += OnCancel;
        }

        public Task<bool> ShowDialog()
        {
            _tcs = new TaskCompletionSource<bool>();
            return _tcs.Task;
        }

        private async void OnSubmit(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) btn.IsEnabled = false;
            var result = await _viewModel.SubmitReviewAsync();
            _tcs?.SetResult(result);
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _tcs?.SetResult(false);
        }
    }
}
