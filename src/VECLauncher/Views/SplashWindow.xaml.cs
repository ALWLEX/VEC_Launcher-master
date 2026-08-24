using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class SplashWindow : Window
{
    private sealed class SlimeParticle
    {
        public FrameworkElement Element { get; set; } = null!;
        public double X { get; set; }
        public double Y { get; set; }
        public double Vx { get; set; }
        public double Vy { get; set; }
        public double Life { get; set; }
        public double MaxLife { get; set; }
        public double Size { get; set; }
    }

    private DispatcherTimer? _splashSlimeTimer;
    private double _slimeTime = 0;
    private bool _isExitingSplash = false;
    private double _exitProgress = 0;
    private bool _wasInAir = false;
    private double _impactWobbleTime = 999;
    private readonly Random _rand = new();
    private readonly List<SlimeParticle> _particles = new();

    public SplashWindow()
    {
        InitializeComponent();
        Loaded += SplashWindow_Loaded;
    }

    private void SplashWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InitSplashSlimeAnimation();
        _ = RunLoadingSequenceAsync();
    }

    private void InitSplashSlimeAnimation()
    {
        try
        {
            var slimeModel = Slime3DBuilder.BuildSlimeModel();
            SplashSlimeVisual.Content = slimeModel;

            _splashSlimeTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            _splashSlimeTimer.Tick += (s, e) =>
            {
                UpdateParticles();

                if (_isExitingSplash)
                {
                    _exitProgress += 0.035;
                    double jumpY = _exitProgress * 6.5;
                    double jumpX = _exitProgress * 5.8;
                    double jumpZ = -_exitProgress * 4.0;

                    SlimeTranslate.OffsetY = jumpY;
                    SlimeTranslate.OffsetX = jumpX;
                    SlimeTranslate.OffsetZ = jumpZ;

                    SlimeRotateY.Angle += 8;
                    SlimeRotateX.Angle += 4;

                    double shrink = Math.Max(0, 1.0 - _exitProgress * 0.7);
                    SlimeScaleTransform.ScaleX = shrink;
                    SlimeScaleTransform.ScaleY = shrink * 1.25;
                    SlimeScaleTransform.ScaleZ = shrink;

                    SlimeShadowScale.ScaleX = Math.Max(0, 1.0 - _exitProgress * 1.5);
                    SlimeShadowScale.ScaleY = Math.Max(0, 1.0 - _exitProgress * 1.5);
                    SlimeGroundShadow.Opacity = Math.Max(0, 0.5 * (1.0 - _exitProgress * 1.5));
                    return;
                }

                _slimeTime += 0.055;

                double cycle = _slimeTime % (Math.PI * 1.45);

                if (cycle < Math.PI)
                {
                    _wasInAir = true;

                    double progress = cycle / Math.PI;
                    double height = Math.Sin(progress) * 1.15;
                    SlimeTranslate.OffsetY = height;
                    SlimeTranslate.OffsetX = 0;

                    double stretchY = 1.0 + Math.Sin(progress) * 0.20;
                    double stretchXZ = 1.0 / Math.Sqrt(stretchY);
                    SlimeScaleTransform.ScaleX = stretchXZ;
                    SlimeScaleTransform.ScaleY = stretchY;
                    SlimeScaleTransform.ScaleZ = stretchXZ;

                    double shadowFactor = Math.Max(0.1, 1.0 - (height / 1.15) * 0.5);
                    SlimeShadowScale.ScaleX = shadowFactor;
                    SlimeShadowScale.ScaleY = shadowFactor;
                    SlimeGroundShadow.Opacity = 0.5 * shadowFactor;
                }
                else
                {
                    if (_wasInAir)
                    {
                        _wasInAir = false;
                        _impactWobbleTime = 0;
                        SpawnLandingParticles();
                    }

                    _impactWobbleTime += 0.055;

                    double decay = Math.Exp(-_impactWobbleTime * 4.5);
                    double wobble = Math.Sin(_impactWobbleTime * 14.0) * decay;

                    SlimeTranslate.OffsetY = -Math.Max(0, wobble * 0.24);

                    double squashY = 1.0 - wobble * 0.40;
                    double squashXZ = 1.0 + wobble * 0.32;

                    SlimeScaleTransform.ScaleX = squashXZ;
                    SlimeScaleTransform.ScaleY = squashY;
                    SlimeScaleTransform.ScaleZ = squashXZ;

                    double shadowFactor = 1.0 + wobble * 0.4;
                    SlimeShadowScale.ScaleX = shadowFactor;
                    SlimeShadowScale.ScaleY = shadowFactor;
                    SlimeGroundShadow.Opacity = Math.Clamp(0.5 + wobble * 0.3, 0.2, 0.8);
                }

                SlimeRotateY.Angle = 22 + Math.Sin(_slimeTime * 0.8) * 8;
                SlimeRotateX.Angle = 8 + Math.Cos(_slimeTime * 1.1) * 3;
            };

            _splashSlimeTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Warn($"SplashWindow: failed to initialize 3D slime: {ex.Message}");
        }
    }

    private void SpawnLandingParticles()
    {
        double originX = 180.0;
        double originY = 175.0;

        Color[] slimeColors = new[]
        {
            Color.FromRgb(0x4A, 0xDE, 0x80),
            Color.FromRgb(0x22, 0xC5, 0x5E),
            Color.FromRgb(0x86, 0xEF, 0xAC),
            Color.FromRgb(0x16, 0xA3, 0x4A)
        };

        int count = _rand.Next(12, 18);
        for (int i = 0; i < count; i++)
        {
            double angle = _rand.NextDouble() * Math.PI + Math.PI;
            double speed = _rand.NextDouble() * 4.5 + 2.0;
            double size = _rand.NextDouble() * 4.0 + 3.0;

            var color = slimeColors[_rand.Next(slimeColors.Length)];
            var rect = new Rectangle
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(color),
                RadiusX = 1.5,
                RadiusY = 1.5
            };

            Canvas.SetLeft(rect, originX);
            Canvas.SetTop(rect, originY);
            ParticleCanvas.Children.Add(rect);

            _particles.Add(new SlimeParticle
            {
                Element = rect,
                X = originX,
                Y = originY,
                Vx = Math.Cos(angle) * speed * 1.4,
                Vy = Math.Sin(angle) * speed * 0.85,
                Life = 0,
                MaxLife = _rand.Next(18, 30),
                Size = size
            });
        }
    }

    private void UpdateParticles()
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life++;

            p.X += p.Vx;
            p.Y += p.Vy;
            p.Vy += 0.28;

            double progress = p.Life / p.MaxLife;
            p.Element.Opacity = Math.Max(0, 1.0 - progress);

            Canvas.SetLeft(p.Element, p.X);
            Canvas.SetTop(p.Element, p.Y);

            if (p.Life >= p.MaxLife)
            {
                ParticleCanvas.Children.Remove(p.Element);
                _particles.RemoveAt(i);
            }
        }
    }

    private void SetSplashProgress(double percent, string status, string tip)
    {
        TxtSplashStatus.Text = status;
        TxtSplashTip.Text = tip;
        TxtSplashPercent.Text = $"{(int)percent}%";

        double barWidth = 360.0 * Math.Clamp(percent / 100.0, 0, 1.0);
        SplashProgressBar.Width = barWidth;
    }

    private async Task RunLoadingSequenceAsync()
    {
        SetSplashProgress(8, "Инициализация VEC Engine...", "Загрузка конфигурации и темы...");
        await Task.Delay(1400);

        SetSplashProgress(25, "Сканирование Java окружения...", "Обнаружение установленных JVM...");
        await Task.Delay(1800);

        SetSplashProgress(45, "Синхронизация профилей...", "Загрузка сохранённых аккаунтов и скинов...");
        await Task.Delay(1800);

        SetSplashProgress(68, "Проверка версий Minecraft...", "Синхронизация манифеста версий...");
        await Task.Delay(1800);

        SetSplashProgress(85, "Загрузка сборок и модов...", "Подготовка установленных модпаков...");
        await Task.Delay(1600);

        SetSplashProgress(98, "Всё готово!", "Запуск VEC Platform...");
        await Task.Delay(1200);
        SetSplashProgress(100, "Добро пожаловать!", "Открытие лаунчера...");

        _isExitingSplash = true;
        await Task.Delay(420);

        var mainWindow = App.Services.GetRequiredService<MainWindow>();
        mainWindow.Opacity = 0;
        mainWindow.Show();

        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(450),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        var fadeIn = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        fadeOut.Completed += (_, _) =>
        {
            _splashSlimeTimer?.Stop();
            _splashSlimeTimer = null;
            SplashSlimeVisual.Content = null;
            Close();
        };

        mainWindow.BeginAnimation(OpacityProperty, fadeIn);
        BeginAnimation(OpacityProperty, fadeOut);
    }
}