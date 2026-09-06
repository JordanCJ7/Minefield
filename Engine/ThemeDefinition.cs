using System;
using SkiaSharp;
using System.Windows.Media;

namespace Minefield.Engine;

/// <summary>
/// Defines a complete cohesive color palette for both the WPF HUD and SkiaSharp grid renderer.
/// </summary>
public class ThemeDefinition
{
    public string Id { get; init; } = "cyberpunk";
    public string Name { get; init; } = "CYBERPUNK CYAN";
    public string Tagline { get; init; } = "High-tech electric neon cyan with deep obsidian glass.";

    // Hex Strings for WPF (#AARRGGBB or #RRGGBB)
    public string AccentHex { get; init; } = "#00E5FF";
    public string SecondaryHex { get; init; } = "#B388FF";
    public string EmeraldHex { get; init; } = "#00E676";
    public string CrimsonHex { get; init; } = "#FF1744";
    public string BgDarkHex { get; init; } = "#070B14";
    public string BgGlassHex { get; init; } = "#D90D1526";
    public string BgGlassLighterHex { get; init; } = "#BF142036";
    public string BorderGlassHex { get; init; } = "#3300E5FF";
    public string BorderActiveHex { get; init; } = "#8800E5FF";
    public string TextPrimaryHex { get; init; } = "#F1F5F9";
    public string TextMutedHex { get; init; } = "#94A3B8";

    // Skia Color Properties
    public SKColor SkiaBackground => ParseSkia(BgDarkHex);
    public SKColor SkiaAccent => ParseSkia(AccentHex);
    public SKColor SkiaSecondary => ParseSkia(SecondaryHex);
    public SKColor SkiaEmerald => ParseSkia(EmeraldHex);
    public SKColor SkiaCrimson => ParseSkia(CrimsonHex);
    public SKColor SkiaMinorGrid => new SKColor(SkiaAccent.Red, SkiaAccent.Green, SkiaAccent.Blue, 35);
    public SKColor SkiaMajorGrid => new SKColor(SkiaAccent.Red, SkiaAccent.Green, SkiaAccent.Blue, 180);
    public SKColor SkiaChunkBorderGlow => new SKColor(SkiaAccent.Red, SkiaAccent.Green, SkiaAccent.Blue, 60);
    public SKColor SkiaAxis => new SKColor(SkiaSecondary.Red, SkiaSecondary.Green, SkiaSecondary.Blue, 200);

    // Number colors 1 through 8
    public string[] NumberColorsHex { get; init; } = new string[8]
    {
        "#00E5FF", // 1: Cyan
        "#00E676", // 2: Emerald Green
        "#FF5252", // 3: Coral Red
        "#B388FF", // 4: Neon Purple
        "#FFD600", // 5: Amber Gold
        "#FF4081", // 6: Deep Pink
        "#E040FB", // 7: Electric Violet
        "#FFFFFF"  // 8: Pure Bright White
    };

    public static SKColor ParseSkia(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return SKColors.White;
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new SKColor(r, g, b, 255);
        }
        if (hex.Length == 8)
        {
            byte a = Convert.ToByte(hex.Substring(0, 2), 16);
            byte r = Convert.ToByte(hex.Substring(2, 2), 16);
            byte g = Convert.ToByte(hex.Substring(4, 2), 16);
            byte b = Convert.ToByte(hex.Substring(6, 2), 16);
            return new SKColor(r, g, b, a);
        }
        return SKColors.White;
    }

    public static System.Windows.Media.Color ParseWpfColor(string hex)
    {
        return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
    }
}
