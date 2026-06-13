using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WebViewControl;

namespace Horizon.Game.GengDi.Core.Controls
{
    public partial class WeatherMapView : UserControl
    {
        private static bool _webViewUnavailable;
        private ContentControl _mapHost;
        private Border _fallbackHost;
        private TextBlock _fallbackCityText;
        private TextBlock _fallbackCoordText;
        private WebView _webView;
        private string _currentLayer = "temperature";

        public static readonly DirectProperty<WeatherMapView, double> LatitudeProperty =
            AvaloniaProperty.RegisterDirect<WeatherMapView, double>(
                nameof(Latitude), o => o.Latitude, (o, v) => o.Latitude = v);

        public static readonly DirectProperty<WeatherMapView, double> LongitudeProperty =
            AvaloniaProperty.RegisterDirect<WeatherMapView, double>(
                nameof(Longitude), o => o.Longitude, (o, v) => o.Longitude = v);

        public static readonly DirectProperty<WeatherMapView, string> CityNameProperty =
            AvaloniaProperty.RegisterDirect<WeatherMapView, string>(
                nameof(CityName), o => o.CityName, (o, v) => o.CityName = v);

        public static readonly DirectProperty<WeatherMapView, string> SelectedLayerProperty =
            AvaloniaProperty.RegisterDirect<WeatherMapView, string>(
                nameof(SelectedLayer), o => o.SelectedLayer, (o, v) => o.SelectedLayer = v);

        private double _latitude = 39.9042;
        public double Latitude
        {
            get => _latitude;
            set
            {
                SetAndRaise(LatitudeProperty, ref _latitude, value);
                UpdateMap();
            }
        }

        private double _longitude = 116.4074;
        public double Longitude
        {
            get => _longitude;
            set
            {
                SetAndRaise(LongitudeProperty, ref _longitude, value);
                UpdateMap();
            }
        }

        private string _cityName = "北京";
        public string CityName
        {
            get => _cityName;
            set
            {
                SetAndRaise(CityNameProperty, ref _cityName, value);
                UpdateMap();
            }
        }

        private string _selectedLayer = "temperature";
        public string SelectedLayer
        {
            get => _selectedLayer;
            set
            {
                var old = _selectedLayer;
                SetAndRaise(SelectedLayerProperty, ref _selectedLayer, value);
                if (old != value)
                    SwitchLayer(value);
            }
        }

        public WeatherMapView()
        {
            InitializeComponent();
            _mapHost = this.FindControl<ContentControl>("MapHost");
            _fallbackHost = this.FindControl<Border>("FallbackHost");
            _fallbackCityText = this.FindControl<TextBlock>("FallbackCityText");
            _fallbackCoordText = this.FindControl<TextBlock>("FallbackCoordText");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void UpdateMap()
        {
            if (_webViewUnavailable)
            {
                ShowFallback();
                return;
            }

            if (_latitude == 0 && _longitude == 0)
                return;

            var html = GenerateMapHtml(_latitude, _longitude, _cityName, _currentLayer);
            var tempPath = Path.Combine(Path.GetTempPath(), "weather_map.html");
            File.WriteAllText(tempPath, html);

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
                ShowFallback();
            }
        }

        private void SwitchLayer(string layer)
        {
            _currentLayer = layer;
            if (_webView != null && !_webViewUnavailable)
            {
                try
                {
                    var layerUrl = layer switch
                    {
                        "temperature" => "temperature",
                        "precipitation" => "precipitation",
                        "wind" => "wind",
                        "humidity" => "relative_humidity",
                        _ => "temperature"
                    };
                    _webView.ExecuteScript($"switchLayer('{layerUrl}')");
                }
                catch { }
            }
        }

        private void ShowFallback()
        {
            _fallbackHost.IsVisible = true;
            _fallbackCityText.Text = _cityName;
            _fallbackCoordText.Text = $"{_latitude:F2}°N, {_longitude:F2}°E";
        }

        private static string GenerateMapHtml(double lat, double lon, string cityName, string layer)
        {
            var escapedCity = cityName.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "\\'");
            var layerUrl = layer switch
            {
                "temperature" => "temperature",
                "precipitation" => "precipitation",
                "wind" => "wind",
                "humidity" => "relative_humidity",
                _ => "temperature"
            };

            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>Weather Map</title>
<link rel=""stylesheet"" href=""https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"" />
<style>
  html, body, #map {{ width: 100%; height: 100%; margin: 0; padding: 0; overflow: hidden; }}
  .leaflet-tile-pane {{ filter: invert(1) hue-rotate(180deg) brightness(0.9) contrast(1.1); }}
  .leaflet-control-zoom {{ display: none; }}
  .leaflet-control-attribution {{ display: none; }}
  .city-marker {{
    width: 20px; height: 20px;
    background: #7AA9FF; border-radius: 50%;
    border: 3px solid #fff;
    box-shadow: 0 0 8px rgba(122,169,255,0.6);
  }}
  .city-popup {{ font-size: 13px; color: #333; }}
  .city-popup b {{ color: #7AA9FF; }}
</style>
</head>
<body>
<div id=""map""></div>
<script src=""https://unpkg.com/leaflet@1.9.4/dist/leaflet.js""></script>
<script>
var map = L.map('map', {{
  center: [{lat}, {lon}],
  zoom: 8,
  zoomControl: false,
  attributionControl: false
}});

var baseLayer = L.tileLayer('https://tile.openstreetmap.org/{{z}}/{{x}}/{{y}}.png', {{
  maxZoom: 18
}}).addTo(map);

var weatherLayer = L.tileLayer('https://tile.open-meteo.com/{layerUrl}/{{z}}/{{x}}/{{y}}.png', {{
  maxZoom: 18,
  opacity: 0.7
}}).addTo(map);

var cityIcon = L.divIcon({{
  className: 'city-marker',
  iconSize: [20, 20],
  iconAnchor: [10, 10]
}});

var marker = L.marker([{lat}, {lon}], {{ icon: cityIcon }}).addTo(map);
marker.bindPopup('<div class=""city-popup""><b>{escapedCity}</b></div>');

window.switchLayer = function(layerName) {{
  if (weatherLayer) map.removeLayer(weatherLayer);
  weatherLayer = L.tileLayer('https://tile.open-meteo.com/' + layerName + '/{{z}}/{{x}}/{{y}}.png', {{
    maxZoom: 18,
    opacity: 0.7
  }}).addTo(map);
}};

window.updateCity = function(lat, lon, name) {{
  map.setView([lat, lon], 8);
  marker.setLatLng([lat, lon]);
  marker.setPopupContent('<div class=""city-popup""><b>' + name + '</b></div>');
}};
</script>
</body>
</html>";
        }
    }
}
