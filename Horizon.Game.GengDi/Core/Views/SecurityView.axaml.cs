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
            new DeviceRow("Windows PC（当前）", "广东 深圳", "2026-07-26 09:18", "在线", DeviceType.Laptop, true),
            new DeviceRow("iPhone 15 Pro", "广东 深圳", "2026-07-25 22:40", "已记住", DeviceType.Smartphone, false),
            new DeviceRow("iPad Air", "北京 朝阳", "2026-07-20 14:05", "已记住", DeviceType.Tablet, false),
        };

        public SecurityView()
        {
            InitializeComponent();
        }
    }

    /// <summary>
    /// 设备类型枚举，用于选择对应 Lucide 图标。
    /// </summary>
    public enum DeviceType
    {
        Laptop,
        Smartphone,
        Tablet,
    }

    /// <summary>
    /// 登录设备表格行数据。
    /// </summary>
    public sealed record DeviceRow(
        string Device,
        string Location,
        string LastLogin,
        string Status,
        DeviceType DeviceType,
        bool IsCurrentDevice)
    {
        /// <summary>是否为笔记本设备（用于显示 Laptop 图标）。</summary>
        public bool IsLaptop => DeviceType == DeviceType.Laptop;

        /// <summary>是否为手机设备（用于显示 Smartphone 图标）。</summary>
        public bool IsSmartphone => DeviceType == DeviceType.Smartphone;

        /// <summary>是否为平板设备（用于显示 Tablet 图标）。</summary>
        public bool IsTablet => DeviceType == DeviceType.Tablet;

        /// <summary>状态是否为"在线"（用于 success 徽章）。</summary>
        public bool IsOnline => Status == "在线";

        /// <summary>状态是否为"已记住"（用于 info 徽章）。</summary>
        public bool IsRemembered => Status == "已记住";
    }
}
