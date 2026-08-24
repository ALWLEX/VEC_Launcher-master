using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VECLauncher.Services;

public sealed class ThemePreset
{
    public required string Name { get; init; }
    public required string BgDeep { get; init; }
    public required string Bg { get; init; }
    public required string Panel { get; init; }
    public required string PanelHover { get; init; }
    public required string Border { get; init; }
    public required string Text { get; init; }
    public required string TextMuted { get; init; }
    public bool IsLight { get; init; }
}

public static class ThemeService
{
    public static Color CurrentAccent { get; private set; } =
        (Color)ColorConverter.ConvertFromString("#FACC15");

    private static ThemePreset? _current;
    public static ThemePreset CurrentTheme => _current ??= Presets[0];

    public static readonly ThemePreset[] Presets =
    {
        new()
        {
            Name = "Тёмная", BgDeep = "#050505", Bg = "#0D0D0D", Panel = "#141414",
            PanelHover = "#1F1F1F", Border = "#2A2A2A", Text = "#FFFFFF", TextMuted = "#8A8A8A"
        },
        new()
        {
            Name = "Светлая", BgDeep = "#F4F4F5", Bg = "#FFFFFF", Panel = "#F4F4F5",
            PanelHover = "#E4E4E7", Border = "#D4D4D8", Text = "#09090B", TextMuted = "#71717A",
            IsLight = true
        }
    };

    public static IEnumerable<ThemePreset> AllPresets() => Presets;

    public static void ApplyTheme(string themeName)
    {
        var preset = AllPresets().FirstOrDefault(p =>
                         string.Equals(p.Name, themeName, StringComparison.OrdinalIgnoreCase))
                     ?? Presets[0];

        _current = preset;

        var res = Application.Current?.Resources;
        if (res is null) return;

        SetBrush(res, "BgDeep", preset.BgDeep);
        SetBrush(res, "Bg", preset.Bg);
        SetBrush(res, "Panel", preset.Panel);
        SetBrush(res, "PanelHover", preset.PanelHover);
        SetBrush(res, "BorderBrushDark", preset.Border);
        SetBrush(res, "Fg", preset.Text);
        SetBrush(res, "FgMuted", preset.TextMuted);

        res["BgDeepColor"] = ToColor(preset.BgDeep);
        res["BgColor"] = ToColor(preset.Bg);
        res["PanelColor"] = ToColor(preset.Panel);
        res["TextColor"] = ToColor(preset.Text);

        res["OnAccent"] = Freeze(new SolidColorBrush(
            preset.IsLight ? Colors.White : (Color)ColorConverter.ConvertFromString("#050505")));

        res["ConsoleBg"] = Freeze(new SolidColorBrush(ToColor(preset.IsLight ? "#FFFFFF" : "#0B0D10")));
        res["ConsoleFg"] = Freeze(new SolidColorBrush(ToColor(preset.IsLight ? "#39424F" : "#A8B4C4")));
    }

    public static void ApplyAccent(string hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            if (!hex.StartsWith('#')) hex = "#" + hex;

            var color = (Color)ColorConverter.ConvertFromString(hex);
            CurrentAccent = color;

            var res = Application.Current?.Resources;
            if (res is null) return;

            res["AccentColor"] = color;
            res["Accent"] = Freeze(new SolidColorBrush(color));
            res["AccentDark"] = Freeze(new SolidColorBrush(Darken(color, 0.82)));
            res["AccentLight"] = Freeze(new SolidColorBrush(Lighten(color, 0.22)));
            res["AccentGlow"] = Freeze(new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B)));
        }
        catch (Exception ex)
        {
            Log.Warn($"ThemeService: failed to apply accent color: {ex.Message}");
        }
    }

    public static ImageBrush? BuildWindowBackground(string imagePath, double opacity)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(imagePath);
            bmp.DecodePixelWidth = 1920;
            bmp.EndInit();
            bmp.Freeze();

            var brush = new ImageBrush(bmp)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                Opacity = Math.Clamp(opacity, 0.05, 1.0)
            };
            brush.Freeze();
            return brush;
        }
        catch (Exception ex)
        {
            Log.Warn($"ThemeService: failed to load window background: {ex.Message}");
            return null;
        }
    }

    public static LinearGradientBrush BuildBanner(string style, Color accent)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        var t = CurrentTheme;

        switch (style)
        {
            case "Ночь":
                brush.GradientStops.Add(new GradientStop(ToColor(t.Bg), 0));
                brush.GradientStops.Add(new GradientStop(Lighten(ToColor(t.Panel), 0.05), 1));
                break;

            case "Космос":
                brush.GradientStops.Add(new GradientStop(ToColor("#1B1436"), 0));
                brush.GradientStops.Add(new GradientStop(ToColor("#0E1230"), 0.6));
                brush.GradientStops.Add(new GradientStop(ToColor("#231A3D"), 1));
                break;

            case "Закат":
                brush.GradientStops.Add(new GradientStop(ToColor("#3A1F1A"), 0));
                brush.GradientStops.Add(new GradientStop(ToColor("#1A1620"), 0.6));
                brush.GradientStops.Add(new GradientStop(ToColor("#2A1B2E"), 1));
                break;

            case "Графит":
                brush.GradientStops.Add(new GradientStop(ToColor(t.Panel), 0));
                brush.GradientStops.Add(new GradientStop(ToColor(t.BgDeep), 1));
                break;

            default:
                brush.GradientStops.Add(new GradientStop(
                    t.IsLight ? Lighten(accent, 0.55) : Darken(accent, 0.28), 0));
                brush.GradientStops.Add(new GradientStop(ToColor(t.Bg), 0.55));
                brush.GradientStops.Add(new GradientStop(ToColor(t.Panel), 1));
                break;
        }

        brush.Freeze();
        return brush;
    }

    public static readonly string[] BackgroundStyles = { "Изумруд", "Ночь", "Космос", "Закат", "Графит" };

    private static void SetBrush(ResourceDictionary res, string key, string hex) =>
        res[key] = Freeze(new SolidColorBrush(ToColor(hex)));

    private static Color ToColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    public static Color Darken(Color c, double factor) => Color.FromRgb(
        (byte)Math.Clamp(c.R * factor, 0, 255),
        (byte)Math.Clamp(c.G * factor, 0, 255),
        (byte)Math.Clamp(c.B * factor, 0, 255));

    public static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255),
        (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255),
        (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255));
}