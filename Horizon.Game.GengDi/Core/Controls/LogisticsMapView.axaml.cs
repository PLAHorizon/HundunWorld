using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.Services;
using WebViewControl;

namespace Horizon.Game.GengDi.Core.Controls
{
    /// <summary>
    /// 物流地图视图：左侧地图区（WebView 高德地图 / 静态 SVG fallback）
    /// + 右上角图层切换按钮组 + 左下角图例 + 右侧 280px 物流时间线。
    /// 按设计稿 chrome-fixedbar.html 物流地图（LogisticsMapView）100% 还原。
    /// </summary>
    public partial class LogisticsMapView : UserControl
    {
        private static bool _webViewUnavailable;
        private ContentControl _mapHost;
        private WebView _webView;

        public static readonly DirectProperty<LogisticsMapView, LogisticsMapDataInfo> MapDataProperty =
            AvaloniaProperty.RegisterDirect<LogisticsMapView, LogisticsMapDataInfo>(
                nameof(MapData), o => o.MapData, (o, v) => o.MapData = v);

        private LogisticsMapDataInfo _mapData;
        public LogisticsMapDataInfo MapData
        {
            get => _mapData;
            set
            {
                SetAndRaise(MapDataProperty, ref _mapData, value);
                UpdateMap();
                RebuildTimeline();
            }
        }

        /// <summary>
        /// 物流时间线展示节点，供右侧时间线 ItemsControl 绑定。
        /// </summary>
        public ObservableCollection<DisplayLogisticsNode> TimelineNodes { get; } = new();

        public LogisticsMapView()
        {
            DiagLog.Log($"[LogisticsMapView] ctor START");
            InitializeComponent();
            _mapHost = this.FindControl<ContentControl>("MapHost");
            DiagLog.Log("[LogisticsMapView] ctor END");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// 根据 MapData.Nodes 构建时间线展示节点。
        /// 除最后一个节点外均为已完成（success 圆 + check）；最后一个节点根据签收状态判断。
        /// </summary>
        private void RebuildTimeline()
        {
            TimelineNodes.Clear();
            if (_mapData == null || _mapData.Nodes == null || _mapData.Nodes.Count == 0) return;

            var totalCount = _mapData.Nodes.Count;
            var isSigned = _mapData.LogisticsStatus >= 4;

            for (int i = 0; i < totalCount; i++)
            {
                var node = _mapData.Nodes[i];
                var isLast = i == totalCount - 1;
                TimelineNodes.Add(new DisplayLogisticsNode
                {
                    Status = node.Description,
                    TimeText = node.Time.ToString("MM-dd HH:mm"),
                    Location = node.Location,
                    IsCompleted = !isLast || isSigned,
                    IsLast = isLast,
                });
            }
        }

        private void UpdateMap()
        {
            if (_mapData == null || _mapData.Nodes == null || _mapData.Nodes.Count == 0)
            {
                IsVisible = false;
                return;
            }

            var nodesWithCoords = _mapData.Nodes
                .Where(n => n.Latitude.HasValue && n.Longitude.HasValue)
                .ToList();

            if (nodesWithCoords.Count < 2)
            {
                IsVisible = false;
                return;
            }

            IsVisible = true;

            var html = GenerateMapHtml(nodesWithCoords, _mapData);
            var tempPath = Path.Combine(Path.GetTempPath(), $"logistics_map_{_mapData.OrderId}.html");
            File.WriteAllText(tempPath, html);

            if (_webViewUnavailable)
            {
                return;
            }

            try
            {
                if (_webView == null)
                {
                    _webView = new WebView
                    {
                        Address = $"file:///{tempPath.Replace('\\', '/')}"
                    };
                    _mapHost.Content = _webView;
                }
                else
                {
                    _webView.Address = $"file:///{tempPath.Replace('\\', '/')}";
                }
            }
            catch (InvalidOperationException)
            {
                _webViewUnavailable = true;
                _mapHost.Content = null;
            }
        }

        private static string GenerateMapHtml(List<LogisticsMapNodeInfo> nodes, LogisticsMapDataInfo data)
        {
            var apiKey = AppSettingsService.Instance.CurrentSettings.AmapApiKey;

            var nodesData = nodes.Select(n => new
            {
                lat = n.Latitude!.Value,
                lng = n.Longitude!.Value,
                time = n.Time.ToString("yyyy-MM-dd HH:mm"),
                desc = n.Description,
                location = n.Location
            }).ToList();

            var nodesJson = JsonSerializer.Serialize(nodesData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var originCity = EscapeJs(data.OriginCity);
            var destCity = EscapeJs(data.DestinationCity);

            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>物流地图</title>
<style>
  html, body, #container {{ width: 100%; height: 100%; margin: 0; padding: 0; }}
  .truck-marker {{
    width: 32px; height: 32px;
    background: #42A5F5; border-radius: 50%;
    display: flex; align-items: center; justify-content: center;
    animation: pulse 1.5s ease-in-out infinite;
    box-shadow: 0 0 10px rgba(66,165,245,0.6);
  }}
  .truck-marker span {{ font-size: 18px; }}
  @keyframes pulse {{
    0%, 100% {{ transform: scale(1); box-shadow: 0 0 10px rgba(66,165,245,0.6); }}
    50% {{ transform: scale(1.2); box-shadow: 0 0 20px rgba(66,165,245,0.8); }}
  }}
  .signed-marker {{
    width: 32px; height: 32px;
    background: #66BB6A; border-radius: 50%;
    display: flex; align-items: center; justify-content: center;
    box-shadow: 0 0 10px rgba(102,187,106,0.6);
  }}
  .signed-marker span {{ font-size: 18px; }}
  .node-marker {{
    width: 12px; height: 12px;
    background: #FFA726; border-radius: 50%;
    border: 2px solid #fff;
  }}
  .origin-marker {{
    width: 24px; height: 24px;
    background: #FF7043; border-radius: 50%;
    display: flex; align-items: center; justify-content: center;
    border: 2px solid #fff;
  }}
  .origin-marker span {{ font-size: 12px; }}
  .amap-info-content {{ font-size: 12px; line-height: 1.6; }}
</style>
</head>
<body>
<div id=""container""></div>
<script src=""https://webapi.amap.com/maps?v=2.0&key={apiKey}""></script>
<script>
var nodes = {nodesJson};
var logisticsStatus = {data.LogisticsStatus};
var originCity = ""{originCity}"";
var destCity = ""{destCity}"";

var map = new AMap.Map('container', {{
  zoom: 5,
  center: [nodes[0].lng, nodes[0].lat]
}});

var path = nodes.map(function(n) {{ return [n.lng, n.lat]; }});
var polyline = new AMap.Polyline({{
  path: path,
  strokeColor: '#42A5F5',
  strokeWeight: 4,
  strokeOpacity: 0.8,
  lineJoin: 'round'
}});
map.add(polyline);

nodes.forEach(function(node, index) {{
  var marker;
  if (index === 0) {{
    marker = new AMap.Marker({{
      position: [node.lng, node.lat],
      content: '<div class=""origin-marker""><span>\uD83D\uDCE6</span></div>',
      offset: new AMap.Pixel(-12, -12)
    }});
  }} else if (index === nodes.length - 1 && logisticsStatus === 4) {{
    marker = new AMap.Marker({{
      position: [node.lng, node.lat],
      content: '<div class=""signed-marker""><span>\u2713</span></div>',
      offset: new AMap.Pixel(-16, -16)
    }});
  }} else if (index === nodes.length - 1) {{
    marker = new AMap.Marker({{
      position: [node.lng, node.lat],
      content: '<div class=""truck-marker""><span>\uD83D\uDE9A</span></div>',
      offset: new AMap.Pixel(-16, -16)
    }});
  }} else {{
    marker = new AMap.Marker({{
      position: [node.lng, node.lat],
      content: '<div class=""node-marker""></div>',
      offset: new AMap.Pixel(-6, -6)
    }});
  }}

  var info = new AMap.InfoWindow({{
    content: '<div class=""amap-info-content""><b>' + node.time + '</b><br/>' + node.desc + '<br/>' + node.location + '</div>',
    offset: new AMap.Pixel(0, -20)
  }});

  marker.on('click', function() {{
    info.open(map, marker.getPosition());
  }});

  map.add(marker);
}});

map.setFitView();
</script>
</body>
</html>";
        }

        private static string EscapeJs(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }

    /// <summary>
    /// 物流时间线展示节点，映射 LogisticsMapNodeInfo 并附加展示状态。
    /// </summary>
    public class DisplayLogisticsNode
    {
        /// <summary>状态描述（已揽收/运输中/已到达/待派送等）</summary>
        public string Status { get; set; } = "";
        /// <summary>格式化时间（MM-dd HH:mm）</summary>
        public string TimeText { get; set; } = "";
        /// <summary>地点</summary>
        public string Location { get; set; } = "";
        /// <summary>是否已完成（已完成显示 success 圆+check，否则空圆）</summary>
        public bool IsCompleted { get; set; }
        /// <summary>是否最后一个节点（最后一个无连接竖线）</summary>
        public bool IsLast { get; set; }
    }
}
