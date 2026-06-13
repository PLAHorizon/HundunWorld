using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Controls
{
    public class WeatherAnimationControl : Control
    {
        public static readonly StyledProperty<WeatherType> WeatherTypeProperty =
            AvaloniaProperty.Register<WeatherAnimationControl, WeatherType>(
                nameof(WeatherType), WeatherType.Sunny);

        public WeatherType WeatherType
        {
            get => GetValue(WeatherTypeProperty);
            set => SetValue(WeatherTypeProperty, value);
        }

        private readonly DispatcherTimer _animationTimer;
        private readonly List<Particle> _particles = new();
        private readonly Random _random = new();

        public WeatherAnimationControl()
        {
            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(32)
            };
            _animationTimer.Tick += OnAnimationTick;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            InitializeParticles();
            _animationTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _animationTimer.Stop();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == WeatherTypeProperty)
            {
                InitializeParticles();
            }
        }

        private void InitializeParticles()
        {
            _particles.Clear();
            
            var weatherType = WeatherType;
            int particleCount = weatherType switch
            {
                WeatherType.Rain => 80,
                WeatherType.Snow => 50,
                WeatherType.Sunny => 20,
                WeatherType.Thunder => 100,
                WeatherType.Fog => 30,
                _ => 20
            };

            var width = Bounds.Width > 0 ? Bounds.Width : 800;
            var height = Bounds.Height > 0 ? Bounds.Height : 200;

            for (int i = 0; i < particleCount; i++)
            {
                _particles.Add(CreateParticle(weatherType, width, height));
            }
        }

        private Particle CreateParticle(WeatherType type, double width, double height)
        {
            return type switch
            {
                WeatherType.Rain => new Particle
                {
                    X = _random.NextDouble() * width,
                    Y = _random.NextDouble() * height,
                    SpeedX = -1,
                    SpeedY = 10 + _random.NextDouble() * 8,
                    Size = 1 + _random.NextDouble() * 2,
                    Opacity = 0.3 + _random.NextDouble() * 0.5,
                    Type = ParticleType.Rain
                },
                WeatherType.Snow => new Particle
                {
                    X = _random.NextDouble() * width,
                    Y = _random.NextDouble() * height,
                    SpeedX = -1 + _random.NextDouble() * 2,
                    SpeedY = 1 + _random.NextDouble() * 2,
                    Size = 2 + _random.NextDouble() * 4,
                    Opacity = 0.4 + _random.NextDouble() * 0.6,
                    Type = ParticleType.Snow,
                    Phase = _random.NextDouble() * Math.PI * 2
                },
                WeatherType.Sunny => new Particle
                {
                    X = width * 0.85,
                    Y = height * 0.15,
                    SpeedX = 0,
                    SpeedY = 0,
                    Size = 40 + _random.NextDouble() * 10,
                    Opacity = 0.15 + _random.NextDouble() * 0.2,
                    Type = ParticleType.Sun,
                    Phase = _random.NextDouble() * Math.PI * 2
                },
                WeatherType.Thunder => new Particle
                {
                    X = _random.NextDouble() * width,
                    Y = _random.NextDouble() * height,
                    SpeedX = -2,
                    SpeedY = 12 + _random.NextDouble() * 10,
                    Size = 1 + _random.NextDouble() * 3,
                    Opacity = 0.2 + _random.NextDouble() * 0.6,
                    Type = _random.NextDouble() > 0.95 ? ParticleType.Lightning : ParticleType.Rain,
                    Phase = _random.NextDouble() * 100
                },
                WeatherType.Fog => new Particle
                {
                    X = _random.NextDouble() * width,
                    Y = _random.NextDouble() * height,
                    SpeedX = 0.5 + _random.NextDouble() * 0.5,
                    SpeedY = 0,
                    Size = 30 + _random.NextDouble() * 40,
                    Opacity = 0.05 + _random.NextDouble() * 0.15,
                    Type = ParticleType.Fog,
                    Phase = _random.NextDouble() * Math.PI * 2
                },
                _ => new Particle()
            };
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            var width = Bounds.Width;
            var height = Bounds.Height;
            
            if (width <= 0 || height <= 0) return;

            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var width = Bounds.Width;
            var height = Bounds.Height;

            if (width <= 0 || height <= 0) return;

            var weatherType = WeatherType;

            foreach (var particle in _particles)
            {
                UpdateParticle(particle, width, height, weatherType);
                DrawParticle(context, particle, weatherType);
            }
        }

        private void UpdateParticle(Particle particle, double width, double height, WeatherType type)
        {
            particle.X += particle.SpeedX;
            particle.Y += particle.SpeedY;

            if (type == WeatherType.Snow)
            {
                particle.Phase += 0.02;
                particle.X += Math.Sin(particle.Phase) * 0.5;
            }

            if (type == WeatherType.Sunny)
            {
                particle.Phase += 0.01;
                particle.Opacity = 0.15 + Math.Sin(particle.Phase) * 0.1;
            }

            if (type == WeatherType.Fog)
            {
                particle.Phase += 0.005;
                particle.Opacity = 0.1 + Math.Sin(particle.Phase) * 0.05;
            }

            if (particle.X < -50) particle.X = width + 50;
            if (particle.X > width + 50) particle.X = -50;
            if (particle.Y < -50) particle.Y = height + 50;
            if (particle.Y > height + 50) particle.Y = -50;
        }

        private void DrawParticle(DrawingContext context, Particle particle, WeatherType weatherType)
        {
            switch (particle.Type)
            {
                case ParticleType.Rain:
                    var rainBrush = new SolidColorBrush(
                        Color.FromArgb((byte)(particle.Opacity * 255), 100, 180, 255));
                    context.DrawLine(
                        new Pen(rainBrush, particle.Size),
                        new Point(particle.X, particle.Y),
                        new Point(particle.X + particle.SpeedX * 2, particle.Y + particle.SpeedY * 2));
                    break;

                case ParticleType.Snow:
                    var snowBrush = new SolidColorBrush(
                        Color.FromArgb((byte)(particle.Opacity * 255), 255, 255, 255));
                    context.DrawEllipse(
                        snowBrush,
                        null,
                        new Point(particle.X, particle.Y),
                        particle.Size / 2,
                        particle.Size / 2);
                    break;

                case ParticleType.Sun:
                    var sunCoreBrush = new SolidColorBrush(
                        Color.FromArgb((byte)(particle.Opacity * 255), 255, 220, 100));
                    context.DrawEllipse(
                        sunCoreBrush,
                        null,
                        new Point(particle.X, particle.Y),
                        particle.Size / 2,
                        particle.Size / 2);

                    for (int i = 0; i < 12; i++)
                    {
                        var angle = (i * Math.PI / 6) + DateTime.Now.Millisecond * 0.0005;
                        var rayLength = particle.Size * 1.8;
                        var rayBrush = new SolidColorBrush(
                            Color.FromArgb((byte)(particle.Opacity * 150), 255, 200, 50));
                        
                        context.DrawLine(
                            new Pen(rayBrush, 2),
                            new Point(particle.X + Math.Cos(angle) * particle.Size * 0.6,
                                     particle.Y + Math.Sin(angle) * particle.Size * 0.6),
                            new Point(
                                particle.X + Math.Cos(angle) * rayLength,
                                particle.Y + Math.Sin(angle) * rayLength));
                    }
                    break;

                case ParticleType.Fog:
                    var fogBrush = new SolidColorBrush(
                        Color.FromArgb((byte)(particle.Opacity * 255), 200, 200, 200));
                    context.DrawEllipse(
                        fogBrush,
                        null,
                        new Point(particle.X, particle.Y),
                        particle.Size,
                        particle.Size * 0.5);
                    break;

                case ParticleType.Lightning:
                    particle.Phase -= 1;
                    if (particle.Phase > 0)
                    {
                        var lightningBrush = new SolidColorBrush(
                            Color.FromArgb(200, 255, 255, 150));
                        DrawLightning(context, particle.X, 0, particle.Y, lightningBrush);
                    }
                    break;
            }
        }

        private void DrawLightning(DrawingContext context, double x, double y1, double y2, IBrush brush)
        {
            var points = new List<Point> { new Point(x, y1) };
            var currentX = x;
            var segments = (int)((y2 - y1) / 15);
            
            for (int i = 0; i < segments; i++)
            {
                currentX += (_random.NextDouble() - 0.5) * 20;
                points.Add(new Point(currentX, y1 + (i + 1) * 15));
            }

            var pen = new Pen(brush, 3);
            for (int i = 0; i < points.Count - 1; i++)
            {
                context.DrawLine(pen, points[i], points[i + 1]);
            }
        }

        private class Particle
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double SpeedX { get; set; }
            public double SpeedY { get; set; }
            public double Size { get; set; }
            public double Opacity { get; set; }
            public ParticleType Type { get; set; }
            public double Phase { get; set; }
        }

        private enum ParticleType
        {
            Rain,
            Snow,
            Sun,
            Fog,
            Lightning
        }
    }
}
