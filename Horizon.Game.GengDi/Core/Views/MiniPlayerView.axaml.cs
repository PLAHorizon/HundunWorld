using Avalonia.Controls;
using Avalonia.Input;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class MiniPlayerView : UserControl
    {
        public MiniPlayerView()
        {
            InitializeComponent();
            DataContext = new MiniPlayerViewModel();
        }

        /// <summary>
        /// 阻止控制按钮 / 滑块的 PointerPressed 事件冒泡，
        /// 避免点击控制区时误触底层布局的其它指针处理逻辑。
        /// </summary>
        private void OnControlButtonPressed(object sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
        }
    }
}
