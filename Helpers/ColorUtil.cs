using System.Globalization;
using System.Windows.Media;

namespace CoTab.Helpers;

public static class ColorUtil
{
    public static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var text = value.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        try
        {
            return (Color)ColorConverter.ConvertFromString(text);
        }
        catch
        {
            return fallback;
        }
    }

    public static Color Parse(string? value) => Parse(value, Colors.Transparent);

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static string ToHexArgb(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color WithAlpha(Color color, double alpha)
    {
        var a = (byte)Math.Clamp(Math.Round(alpha * 255), 0, 255);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }

    public static SolidColorBrush Brush(Color color) => new(color);

    public static SolidColorBrush Brush(string hex, Color fallback) => new(Parse(hex, fallback));

    public static double Luminance(Color color)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    public static double ContrastRatio(Color a, Color b)
    {
        var l1 = Luminance(a);
        var l2 = Luminance(b);
        if (l1 < l2) (l1, l2) = (l2, l1);
        return (l1 + 0.05) / (l2 + 0.05);
    }

    public static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(from.A + (to.A - from.A) * amount),
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    public static Color MixOver(Color foreground, Color background)
    {
        var a = foreground.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round(foreground.R * a + background.R * (1 - a)),
            (byte)Math.Round(foreground.G * a + background.G * (1 - a)),
            (byte)Math.Round(foreground.B * a + background.B * (1 - a)));
    }

    /// <summary>Pushes a color toward white or black until it is readable on the given background.</summary>
    public static Color EnsureContrast(Color color, Color background, double minimumContrast = 4.5)
    {
        if (ContrastRatio(color, background) >= minimumContrast) return color;
        var prefersDark = Luminance(background) > 0.5;
        var target = prefersDark ? Colors.Black : Colors.White;
        for (var i = 1; i <= 20; i++)
        {
            var candidate = Blend(color, target, i / 20.0);
            if (ContrastRatio(candidate, background) >= minimumContrast) return candidate;
        }
        return target;
    }

    public static Color DeriveMuted(Color color, double amount = 0.45)
    {
        var target = Luminance(color) > 0.5 ? Colors.Black : Colors.White;
        return Blend(color, target, amount);
    }

    public static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue = 0;
        if (delta > 0.00001)
        {
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
        }
        if (hue < 0) hue += 360;

        var saturation = max <= 0.00001 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    public static Color FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = value - c;
        double r = 0, g = 0, b = 0;

        if (hue < 60) (r, g, b) = (c, x, 0);
        else if (hue < 120) (r, g, b) = (x, c, 0);
        else if (hue < 180) (r, g, b) = (0, c, x);
        else if (hue < 240) (r, g, b) = (0, x, c);
        else if (hue < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    public static bool TryParseHex(string? text, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        if (t.Length is not (4 or 7 or 9)) return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(t);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ToRgbText(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"{color.R}, {color.G}, {color.B}");
}
