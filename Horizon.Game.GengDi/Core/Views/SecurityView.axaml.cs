using System.Collections.Generic;
using Avalonia.Controls;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class SecurityView : UserControl
    {
        /// <summary>
        /// 登录设备静态示例数据，供 DataGrid 展示（SecurityViewModel 暂无设备集合属性）。
        /// </summary>
        public static IReadOnlyList<DeviceRow> SampleDevices { get; } = new[]
        {
            new DeviceRow("Windows PC", "北京 · Chrome", "2026-07-25 09:12", "当前设备"),
            new DeviceRow("iPhone 15", "上海 · Safari", "2026-07-24 21:40", "在线"),
            new DeviceRow("iPad Pro", "深圳 · App", "2026-07-20 14:05", "已离线"),
        };

        public SecurityView()
        {
            InitializeComponent();
        }
    }

    /// <summary>
    /// 登录设备表格行数据。
    /// </summary>
    public sealed record DeviceRow(string Device, string Location, string LastLogin, string Status);
}
