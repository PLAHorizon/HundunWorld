using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class RegisterView : UserControl
    {
        public RegisterView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            var anim = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(600),
                Delay = TimeSpan.FromMilliseconds(300),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0d),
                        Setters = { new Setter(OpacityProperty, 0d) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1d),
                        Setters = { new Setter(OpacityProperty, 1d) }
                    }
                }
            };
            anim.RunAsync(RegisterSubtitleText);
        }
    }
}
