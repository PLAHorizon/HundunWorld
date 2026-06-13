using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace Horizon.Game.GengDi.Core.Controls
{
    public partial class WeatherIconControl : UserControl
    {
        private readonly DispatcherTimer _animationTimer;
        private double _time;
        private double _rotationAngle;
        private double _cloudOffset;
        private double _dropOffset;
        private double _snowAngle;
        private bool _lightningVisible;
        private double _fogBreath;

        public static readonly StyledProperty<string> WeatherConditionProperty =
            AvaloniaProperty.Register<WeatherIconControl, string>(nameof(WeatherCondition), "晴");

        public static readonly StyledProperty<int> WmoCodeProperty =
            AvaloniaProperty.Register<WeatherIconControl, int>(nameof(WmoCode), 0);

        public string WeatherCondition
        {
            get => GetValue(WeatherConditionProperty);
            set => SetValue(WeatherConditionProperty, value);
        }

        public int WmoCode
        {
            get => GetValue(WmoCodeProperty);
            set => SetValue(WmoCodeProperty, value);
        }

        public WeatherIconControl()
        {
            InitializeComponent();
            RenderIcon();

            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _animationTimer.Tick += OnAnimationTick;
            _animationTimer.Start();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == WeatherConditionProperty || change.Property == WmoCodeProperty)
            {
                RenderIcon();
            }
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            _time += 0.033;
            _rotationAngle = (_rotationAngle + 0.6) % 360;
            _cloudOffset = 2.0 * Math.Sin(_time * 1.2);
            _dropOffset = (_dropOffset + 1.2) % 16;
            _snowAngle = (_snowAngle + 0.8) % 360;
            _lightningVisible = Math.Sin(_time * 8) > 0.7;
            _fogBreath = 1.0 + 0.08 * Math.Sin(_time * 1.5);

            var rootCanvas = this.FindControl<Canvas>("IconCanvas");
            if (rootCanvas == null) return;

            foreach (var child in rootCanvas.Children)
            {
                if (child is Canvas weatherPart)
                {
                    AnimateWeatherPart(weatherPart);
                }
            }
        }

        private Matrix CreateScaleMatrix(double scaleX, double scaleY, double centerX, double centerY)
        {
            double tx = centerX * (1 - scaleX);
            double ty = centerY * (1 - scaleY);
            return new Matrix(scaleX, 0, 0, scaleY, tx, ty);
        }

        private void AnimateWeatherPart(Canvas part)
        {
            var tag = part.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;

            switch (tag)
            {
                case "sun_rays":
                    part.RenderTransform = new RotateTransform(_rotationAngle, 32, 32);
                    break;
                case "sun_glow":
                    var glowScale = 1.0 + 0.1 * Math.Sin(_time * 2.0);
                    part.RenderTransform = new MatrixTransform(CreateScaleMatrix(glowScale, glowScale, 32, 32));
                    part.Opacity = 0.25 + 0.1 * Math.Sin(_time * 2.0);
                    break;
                case "cloud_float":
                    part.RenderTransform = new TranslateTransform(_cloudOffset, 0);
                    break;
                case "cloud_float_slow":
                    part.RenderTransform = new TranslateTransform(_cloudOffset * 0.5, 0);
                    break;
                case "rain_drop_1":
                    part.RenderTransform = new TranslateTransform(0, _dropOffset % 14);
                    part.Opacity = 0.85 - (_dropOffset % 14) / 22.0;
                    break;
                case "rain_drop_2":
                    part.RenderTransform = new TranslateTransform(0, (_dropOffset + 5) % 14);
                    part.Opacity = 0.85 - ((_dropOffset + 5) % 14) / 22.0;
                    break;
                case "rain_drop_3":
                    part.RenderTransform = new TranslateTransform(0, (_dropOffset + 10) % 14);
                    part.Opacity = 0.85 - ((_dropOffset + 10) % 14) / 22.0;
                    break;
                case "snow_flake_1":
                    var sx1 = 2.0 * Math.Sin(_time * 1.5);
                    var sy1 = _dropOffset % 12;
                    part.RenderTransform = new MatrixTransform(
                        Matrix.CreateRotation(DegreesToRadians(_snowAngle)) *
                        Matrix.CreateTranslation(sx1, sy1));
                    break;
                case "snow_flake_2":
                    var sx2 = 2.0 * Math.Sin(_time * 1.5 + 2);
                    var sy2 = (_dropOffset + 4) % 12;
                    part.RenderTransform = new MatrixTransform(
                        Matrix.CreateRotation(DegreesToRadians(-_snowAngle)) *
                        Matrix.CreateTranslation(sx2, sy2));
                    break;
                case "snow_flake_3":
                    var sx3 = 2.0 * Math.Sin(_time * 1.5 + 4);
                    var sy3 = (_dropOffset + 8) % 12;
                    part.RenderTransform = new MatrixTransform(
                        Matrix.CreateRotation(DegreesToRadians(_snowAngle * 0.7)) *
                        Matrix.CreateTranslation(sx3, sy3));
                    break;
                case "lightning":
                    part.IsVisible = _lightningVisible;
                    if (_lightningVisible)
                    {
                        var flash = 1.0 + 0.15 * Math.Sin(_time * 20);
                        part.RenderTransform = new MatrixTransform(CreateScaleMatrix(flash, flash, 32, 38));
                    }
                    break;
                case "fog_layer":
                    var breath = _fogBreath;
                    part.RenderTransform = new MatrixTransform(new Matrix(breath, 0, 0, 1, 32 * (1 - breath), 0));
                    part.Opacity = 0.4 + 0.15 * Math.Sin(_time * 1.5);
                    break;
            }
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        private void RenderIcon()
        {
            var rootCanvas = this.FindControl<Canvas>("IconCanvas");
            if (rootCanvas == null) return;

            rootCanvas.Children.Clear();

            int code = WmoCode;
            int weatherType = GetWeatherType(code);

            switch (weatherType)
            {
                case 0: DrawSunny(rootCanvas); break;
                case 1: DrawPartlyCloudy(rootCanvas); break;
                case 2: DrawCloudy(rootCanvas); break;
                case 3: DrawFoggy(rootCanvas); break;
                case 4: DrawRainy(rootCanvas); break;
                case 5: DrawSnowy(rootCanvas); break;
                case 6: DrawThunder(rootCanvas); break;
                default: DrawSunny(rootCanvas); break;
            }
        }

        private int GetWeatherType(int wmoCode)
        {
            return wmoCode switch
            {
                0 => 0,
                1 or 2 => 1,
                3 => 2,
                45 or 48 => 3,
                51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => 4,
                71 or 73 or 75 or 77 or 85 or 86 => 5,
                95 or 96 or 99 => 6,
                _ => 0
            };
        }

        #region Sunny

        private void DrawSunny(Canvas canvas)
        {
            var glowGroup = new Canvas { Tag = "sun_glow" };

            var outerGlow = new Ellipse
            {
                Width = 52,
                Height = 52,
                Fill = new RadialGradientBrush
                {
                    Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop { Offset = 0, Color = Color.Parse("#FFE4B533") },
                        new GradientStop { Offset = 0.5, Color = Color.Parse("#FFCC8018") },
                        new GradientStop { Offset = 1, Color = Color.Parse("#FFB74D00") }
                    }
                }
            };
            Canvas.SetLeft(outerGlow, 6);
            Canvas.SetTop(outerGlow, 6);
            glowGroup.Children.Add(outerGlow);
            canvas.Children.Add(glowGroup);

            var raysGroup = new Canvas { Tag = "sun_rays" };

            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0;
                double rad = DegreesToRadians(angle);
                bool isLong = i % 2 == 0;

                double innerR = isLong ? 22 : 24;
                double outerR = isLong ? 30 : 27;
                double thickness = isLong ? 2.5 : 1.5;

                double x1 = 32 + innerR * Math.Cos(rad);
                double y1 = 32 + innerR * Math.Sin(rad);
                double x2 = 32 + outerR * Math.Cos(rad);
                double y2 = 32 + outerR * Math.Sin(rad);

                raysGroup.Children.Add(new Line
                {
                    StartPoint = new Point(x1, y1),
                    EndPoint = new Point(x2, y2),
                    Stroke = new SolidColorBrush(Color.Parse("#FFCC80")),
                    StrokeThickness = thickness,
                    StrokeLineCap = PenLineCap.Round
                });
            }

            canvas.Children.Add(raysGroup);

            var sunCore = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = new RadialGradientBrush
                {
                    Center = new RelativePoint(0.4, 0.35, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop { Offset = 0, Color = Color.Parse("#FFF8E1") },
                        new GradientStop { Offset = 0.3, Color = Color.Parse("#FFE0B2") },
                        new GradientStop { Offset = 0.7, Color = Color.Parse("#FFCC80") },
                        new GradientStop { Offset = 1, Color = Color.Parse("#FFB74D") }
                    }
                }
            };
            Canvas.SetLeft(sunCore, 22);
            Canvas.SetTop(sunCore, 22);
            canvas.Children.Add(sunCore);

            var highlight = new Ellipse
            {
                Width = 8,
                Height = 6,
                Fill = new RadialGradientBrush
                {
                    Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop { Offset = 0, Color = Color.Parse("#FFFFFF88") },
                        new GradientStop { Offset = 1, Color = Color.Parse("#FFFFFF00") }
                    }
                }
            };
            Canvas.SetLeft(highlight, 25);
            Canvas.SetTop(highlight, 24);
            canvas.Children.Add(highlight);
        }

        #endregion

        #region Cloud Shapes

        private Avalonia.Controls.Shapes.Path CreateFluffyCloud(double x, double y, double scale, bool isDark)
        {
            string topColor = isDark ? "#B0BEC5" : "#ECEFF1";
            string midColor = isDark ? "#90A4AE" : "#CFD8DC";
            string bottomColor = isDark ? "#78909C" : "#B0BEC5";

            var path = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(
                    $"M{x + 4 * scale},{y + 14 * scale} " +
                    $"Q{x + 2 * scale},{y + 8 * scale} {x + 8 * scale},{y + 6 * scale} " +
                    $"Q{x + 10 * scale},{y + 0 * scale} {x + 16 * scale},{y + 2 * scale} " +
                    $"Q{x + 20 * scale},{y - 2 * scale} {x + 26 * scale},{y + 2 * scale} " +
                    $"Q{x + 34 * scale},{y + 0 * scale} {x + 36 * scale},{y + 6 * scale} " +
                    $"Q{x + 42 * scale},{y + 8 * scale} {x + 40 * scale},{y + 14 * scale} " +
                    $"Z"),
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop { Offset = 0, Color = Color.Parse(topColor) },
                        new GradientStop { Offset = 0.6, Color = Color.Parse(midColor) },
                        new GradientStop { Offset = 1, Color = Color.Parse(bottomColor) }
                    }
                }
            };
            Canvas.SetLeft(path, 0);
            Canvas.SetTop(path, 0);
            return path;
        }

        private Avalonia.Controls.Shapes.Path CreateCloudHighlight(double x, double y, double scale)
        {
            var path = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(
                    $"M{x + 10 * scale},{y + 6 * scale} " +
                    $"Q{x + 12 * scale},{y + 3 * scale} {x + 18 * scale},{y + 4 * scale} " +
                    $"Q{x + 22 * scale},{y + 2 * scale} {x + 26 * scale},{y + 4 * scale} " +
                    $"Q{x + 30 * scale},{y + 3 * scale} {x + 32 * scale},{y + 6 * scale} " +
                    $"Z"),
                Fill = new SolidColorBrush(Color.Parse("#FFFFFF20"))
            };
            Canvas.SetLeft(path, 0);
            Canvas.SetTop(path, 0);
            return path;
        }

        #endregion

        #region Partly Cloudy

        private void DrawPartlyCloudy(Canvas canvas)
        {
            var sunGlowGroup = new Canvas { Tag = "sun_glow" };
            var sunGlow = new Ellipse
            {
                Width = 36,
                Height = 36,
                Fill = new RadialGradientBrush
                {
                    Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop { Offset = 0, Color = Color.Parse("#FFE4B522") },
                        new GradientStop { Offset = 1, Color = Color.Parse("#FFB74D00") }
                    }
                }
            };
            Canvas.SetLeft(sunGlow, 2);
            Canvas.SetTop(sunGlow, 2);
            sunGlowGroup.Children.Add(sunGlow);
            canvas.Children.Add(sunGlowGroup);

            var raysGroup = new Canvas { Tag = "sun_rays" };
            for (int i = 0; i < 8; i++)
            {
                double angle = i * 45.0;
                double rad = DegreesToRadians(angle);
                double x1 = 20 + 14 * Math.Cos(rad);
                double y1 = 20 + 14 * Math.Sin(rad);
                double x2 = 20 + 18 * Math.Cos(rad);
                double y2 = 20 + 18 * Math.Sin(rad);

                raysGroup.Children.Add(new Line
                {
                    StartPoint = new Point(x1, y1),
                    EndPoint = new Point(x2, y2),
                    Stroke = new SolidColorBrush(Color.Parse("#FFCC80")),
                    StrokeThickness = 1.5,
                    StrokeLineCap = PenLineCap.Round
                });
            }
            canvas.Children.Add(raysGroup);

            var sunCore = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new RadialGradientBrush
                {
                    Center = new RelativePoint(0.4, 0.35, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop { Offset = 0, Color = Color.Parse("#FFF8E1") },
                        new GradientStop { Offset = 0.5, Color = Color.Parse("#FFE0B2") },
                        new GradientStop { Offset = 1, Color = Color.Parse("#FFB74D") }
                    }
                }
            };
            Canvas.SetLeft(sunCore, 13);
            Canvas.SetTop(sunCore, 13);
            canvas.Children.Add(sunCore);

            var cloudGroup = new Canvas { Tag = "cloud_float" };
            cloudGroup.Children.Add(CreateFluffyCloud(14, 28, 0.85, false));
            cloudGroup.Children.Add(CreateCloudHighlight(14, 28, 0.85));
            canvas.Children.Add(cloudGroup);
        }

        #endregion

        #region Cloudy

        private void DrawCloudy(Canvas canvas)
        {
            var backCloudGroup = new Canvas { Tag = "cloud_float_slow" };
            backCloudGroup.Children.Add(CreateFluffyCloud(4, 18, 0.7, true));
            canvas.Children.Add(backCloudGroup);

            var frontCloudGroup = new Canvas { Tag = "cloud_float" };
            frontCloudGroup.Children.Add(CreateFluffyCloud(10, 24, 0.9, false));
            frontCloudGroup.Children.Add(CreateCloudHighlight(10, 24, 0.9));
            canvas.Children.Add(frontCloudGroup);
        }

        #endregion

        #region Rainy

        private void DrawRainy(Canvas canvas)
        {
            var cloudGroup = new Canvas { Tag = "cloud_float" };
            cloudGroup.Children.Add(CreateFluffyCloud(8, 10, 1.0, true));
            cloudGroup.Children.Add(CreateCloudHighlight(8, 10, 1.0));
            canvas.Children.Add(cloudGroup);

            DrawRainDrops(canvas, 28);
        }

        private void DrawRainDrops(Canvas canvas, double startY)
        {
            var dropPositions = new[] { (16.0, 0.0), (26.0, 2.0), (36.0, -1.0), (21.0, 4.0), (31.0, 3.0) };
            var tags = new[] { "rain_drop_1", "rain_drop_2", "rain_drop_3", "rain_drop_1", "rain_drop_2" };

            for (int i = 0; i < dropPositions.Length; i++)
            {
                var (dx, offsetY) = dropPositions[i];
                var dropGroup = new Canvas { Tag = tags[i] };

                var drop = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse(
                        $"M{dx},{startY + offsetY} " +
                        $"Q{dx - 2},{startY + offsetY + 5} {dx},{startY + offsetY + 8} " +
                        $"Q{dx + 2},{startY + offsetY + 5} {dx},{startY + offsetY} Z"),
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops = new GradientStops
                        {
                            new GradientStop { Offset = 0, Color = Color.Parse("#90CAF9") },
                            new GradientStop { Offset = 1, Color = Color.Parse("#64B5F6") }
                        }
                    }
                };
                Canvas.SetLeft(drop, 0);
                Canvas.SetTop(drop, 0);
                dropGroup.Children.Add(drop);

                var highlight = new Ellipse
                {
                    Width = 1.5,
                    Height = 1.5,
                    Fill = new SolidColorBrush(Color.Parse("#BBDEFB"))
                };
                Canvas.SetLeft(highlight, dx - 0.5);
                Canvas.SetTop(highlight, startY + offsetY + 2);
                dropGroup.Children.Add(highlight);

                canvas.Children.Add(dropGroup);
            }
        }

        #endregion

        #region Snowy

        private void DrawSnowy(Canvas canvas)
        {
            var cloudGroup = new Canvas { Tag = "cloud_float" };
            cloudGroup.Children.Add(CreateFluffyCloud(8, 10, 1.0, false));
            cloudGroup.Children.Add(CreateCloudHighlight(8, 10, 1.0));
            canvas.Children.Add(cloudGroup);

            var flakePositions = new[] { (18.0, 30.0), (30.0, 32.0), (40.0, 28.0), (24.0, 36.0), (34.0, 38.0) };
            var tags = new[] { "snow_flake_1", "snow_flake_2", "snow_flake_3", "snow_flake_1", "snow_flake_2" };
            var sizes = new[] { 5.0, 4.0, 6.0, 3.5, 4.5 };

            for (int i = 0; i < flakePositions.Length; i++)
            {
                var (fx, fy) = flakePositions[i];
                var flakeGroup = new Canvas { Tag = tags[i] };

                DrawSnowflakeShape(flakeGroup, fx, fy, sizes[i]);

                canvas.Children.Add(flakeGroup);
            }
        }

        private void DrawSnowflakeShape(Canvas parent, double cx, double cy, double size)
        {
            var armColor = Color.Parse("#CFD8DC");
            var dotColor = Color.Parse("#B0BEC5");

            for (int i = 0; i < 6; i++)
            {
                double angle = i * 60.0;
                double rad = DegreesToRadians(angle);
                double ex = cx + size * Math.Cos(rad);
                double ey = cy + size * Math.Sin(rad);

                parent.Children.Add(new Line
                {
                    StartPoint = new Point(cx, cy),
                    EndPoint = new Point(ex, ey),
                    Stroke = new SolidColorBrush(armColor),
                    StrokeThickness = 1.2,
                    StrokeLineCap = PenLineCap.Round
                });

                double branchLen = size * 0.4;
                double branchAngle1 = DegreesToRadians(angle + 45);
                double branchAngle2 = DegreesToRadians(angle - 45);
                double midX = cx + size * 0.6 * Math.Cos(rad);
                double midY = cy + size * 0.6 * Math.Sin(rad);

                parent.Children.Add(new Line
                {
                    StartPoint = new Point(midX, midY),
                    EndPoint = new Point(midX + branchLen * Math.Cos(branchAngle1), midY + branchLen * Math.Sin(branchAngle1)),
                    Stroke = new SolidColorBrush(armColor),
                    StrokeThickness = 0.8,
                    StrokeLineCap = PenLineCap.Round
                });

                parent.Children.Add(new Line
                {
                    StartPoint = new Point(midX, midY),
                    EndPoint = new Point(midX + branchLen * Math.Cos(branchAngle2), midY + branchLen * Math.Sin(branchAngle2)),
                    Stroke = new SolidColorBrush(armColor),
                    StrokeThickness = 0.8,
                    StrokeLineCap = PenLineCap.Round
                });

                parent.Children.Add(new Ellipse
                {
                    Width = 1.5,
                    Height = 1.5,
                    Fill = new SolidColorBrush(dotColor)
                });
                Canvas.SetLeft(parent.Children[parent.Children.Count - 1], ex - 0.75);
                Canvas.SetTop(parent.Children[parent.Children.Count - 1], ey - 0.75);
            }

            var centerDot = new Ellipse
            {
                Width = 2.5,
                Height = 2.5,
                Fill = new RadialGradientBrush
                {
                    Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop { Offset = 0, Color = Color.Parse("#ECEFF1") },
                        new GradientStop { Offset = 1, Color = Color.Parse("#CFD8DC") }
                    }
                }
            };
            Canvas.SetLeft(centerDot, cx - 1.25);
            Canvas.SetTop(centerDot, cy - 1.25);
            parent.Children.Add(centerDot);
        }

        #endregion

        #region Thunder

        private void DrawThunder(Canvas canvas)
        {
            var cloudGroup = new Canvas { Tag = "cloud_float" };
            cloudGroup.Children.Add(CreateFluffyCloud(6, 8, 1.05, true));
            canvas.Children.Add(cloudGroup);

            var lightningGroup = new Canvas { Tag = "lightning" };

            var bolt = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(
                    "M30,24 L26,32 L30,32 L25,42 L34,30 L30,30 L35,24 Z"),
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop { Offset = 0, Color = Color.Parse("#FFF8E1") },
                        new GradientStop { Offset = 0.4, Color = Color.Parse("#FFE082") },
                        new GradientStop { Offset = 1, Color = Color.Parse("#FFD54F") }
                    }
                }
            };
            Canvas.SetLeft(bolt, 0);
            Canvas.SetTop(bolt, 0);
            lightningGroup.Children.Add(bolt);

            var boltGlow = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = new RadialGradientBrush
                {
                    Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop { Offset = 0, Color = Color.Parse("#FFE08233") },
                        new GradientStop { Offset = 1, Color = Color.Parse("#FFE08200") }
                    }
                }
            };
            Canvas.SetLeft(boltGlow, 22);
            Canvas.SetTop(boltGlow, 28);
            lightningGroup.Children.Add(boltGlow);

            canvas.Children.Add(lightningGroup);

            DrawRainDrops(canvas, 38);
        }

        #endregion

        #region Foggy

        private void DrawFoggy(Canvas canvas)
        {
            var fogLayers = new[]
            {
                (8, 16, 48, 3.0, 0.55),
                (6, 24, 52, 2.5, 0.40),
                (10, 32, 44, 2.0, 0.45),
                (4, 40, 56, 3.0, 0.30),
                (8, 48, 48, 2.0, 0.22),
            };

            for (int i = 0; i < fogLayers.Length; i++)
            {
                var (x, y, width, thickness, opacity) = fogLayers[i];
                var fogGroup = new Canvas { Tag = "fog_layer" };

                var fogLine = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse(
                        $"M{x},{y} " +
                        $"Q{x + width * 0.25},{y - 2} {x + width * 0.5},{y} " +
                        $"Q{x + width * 0.75},{y + 2} {x + width},{y} " +
                        $"L{x + width},{y + thickness} " +
                        $"Q{x + width * 0.75},{y + thickness + 1} {x + width * 0.5},{y + thickness} " +
                        $"Q{x + width * 0.25},{y + thickness - 1} {x},{y + thickness} Z"),
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops = new GradientStops
                        {
                            new GradientStop { Offset = 0, Color = Color.Parse("#B0BEC500") },
                            new GradientStop { Offset = 0.2, Color = Color.Parse($"#CFD8DC{(byte)(opacity * 255):X2}") },
                            new GradientStop { Offset = 0.5, Color = Color.Parse($"#E0E0E0{(byte)(opacity * 255):X2}") },
                            new GradientStop { Offset = 0.8, Color = Color.Parse($"#CFD8DC{(byte)(opacity * 255):X2}") },
                            new GradientStop { Offset = 1, Color = Color.Parse("#B0BEC500") }
                        }
                    }
                };
                Canvas.SetLeft(fogLine, 0);
                Canvas.SetTop(fogLine, 0);
                fogGroup.Children.Add(fogLine);
                fogGroup.Opacity = opacity;

                canvas.Children.Add(fogGroup);
            }

            var mistCloudGroup = new Canvas { Tag = "cloud_float_slow" };
            mistCloudGroup.Children.Add(CreateFluffyCloud(10, 6, 0.75, false));
            mistCloudGroup.Opacity = 0.4;
            canvas.Children.Add(mistCloudGroup);
        }

        #endregion
    }
}
