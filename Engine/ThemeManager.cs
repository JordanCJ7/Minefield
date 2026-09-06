using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Minefield.Engine;

/// <summary>
/// Manages the catalog of visual themes, provides active theme state,
/// and applies theme colors dynamically to WPF Application resources.
/// </summary>
public class ThemeManager
{
    private static readonly List<ThemeDefinition> _themes = new();

    public static IReadOnlyList<ThemeDefinition> Themes => _themes;

    public static ThemeDefinition CurrentTheme { get; private set; }

    public static event Action<ThemeDefinition>? OnThemeChanged;

    static ThemeManager()
    {
        // 1. Cyberpunk Cyan (Default)
        _themes.Add(new ThemeDefinition
        {
            Id = "cyberpunk",
            Name = "CYBERPUNK CYAN",
            Tagline = "Electric neon cyan with deep obsidian glass and ultraviolet accents.",
            AccentHex = "#00E5FF",
            SecondaryHex = "#B388FF",
            EmeraldHex = "#00E676",
            CrimsonHex = "#FF1744",
            BgDarkHex = "#070B14",
            BgGlassHex = "#D90D1526",
            BgGlassLighterHex = "#BF142036",
            BorderGlassHex = "#3300E5FF",
            BorderActiveHex = "#8800E5FF",
            TextPrimaryHex = "#F1F5F9",
            TextMutedHex = "#94A3B8",
            NumberColorsHex = new[] { "#00E5FF", "#00E676", "#FF5252", "#B388FF", "#FFD600", "#FF4081", "#E040FB", "#FFFFFF" }
        });

        // 2. Matrix Terminal
        _themes.Add(new ThemeDefinition
        {
            Id = "matrix",
            Name = "MATRIX TERMINAL",
            Tagline = "Phosphor green digital rain terminal with pitch black glass.",
            AccentHex = "#00FF66",
            SecondaryHex = "#76FF03",
            EmeraldHex = "#00E676",
            CrimsonHex = "#FF3D00",
            BgDarkHex = "#040D06",
            BgGlassHex = "#D9071A0B",
            BgGlassLighterHex = "#BF0E2E15",
            BorderGlassHex = "#3300FF66",
            BorderActiveHex = "#8800FF66",
            TextPrimaryHex = "#E8F5E9",
            TextMutedHex = "#81C784",
            NumberColorsHex = new[] { "#00FF66", "#76FF03", "#FFFF00", "#69F0AE", "#B2FF59", "#FF6E40", "#FFAB40", "#FFFFFF" }
        });

        // 3. Synthwave Neon
        _themes.Add(new ThemeDefinition
        {
            Id = "synthwave",
            Name = "SYNTHWAVE NEON",
            Tagline = "Retro-futuristic laser magenta, cyan gridlines, and twilight abyss.",
            AccentHex = "#FF007F",
            SecondaryHex = "#00F0FF",
            EmeraldHex = "#00E676",
            CrimsonHex = "#FF1744",
            BgDarkHex = "#0E0517",
            BgGlassHex = "#D91A0B2E",
            BgGlassLighterHex = "#BF2D134D",
            BorderGlassHex = "#33FF007F",
            BorderActiveHex = "#88FF007F",
            TextPrimaryHex = "#FCE4EC",
            TextMutedHex = "#CE93D8",
            NumberColorsHex = new[] { "#FF007F", "#00F0FF", "#FFD600", "#E040FB", "#76FF03", "#FF4081", "#B388FF", "#FFFFFF" }
        });

        // 4. Solaris Amber
        _themes.Add(new ThemeDefinition
        {
            Id = "solaris",
            Name = "SOLARIS AMBER",
            Tagline = "High-contrast golden industrial telemetry with carbon black glass.",
            AccentHex = "#FFB300",
            SecondaryHex = "#FF6D00",
            EmeraldHex = "#64DD17",
            CrimsonHex = "#FF3D00",
            BgDarkHex = "#120B03",
            BgGlassHex = "#D9241505",
            BgGlassLighterHex = "#BF3B2208",
            BorderGlassHex = "#33FFB300",
            BorderActiveHex = "#88FFB300",
            TextPrimaryHex = "#FFF8E1",
            TextMutedHex = "#FFCA28",
            NumberColorsHex = new[] { "#FFB300", "#FF6D00", "#FFD600", "#FFA000", "#FFAB00", "#FF5722", "#E65100", "#FFFFFF" }
        });

        // 5. Crimson Protocol
        _themes.Add(new ThemeDefinition
        {
            Id = "crimson",
            Name = "CRIMSON PROTOCOL",
            Tagline = "Emergency hazard red and stealth obsidian for high-stakes operatives.",
            AccentHex = "#FF1744",
            SecondaryHex = "#FF5252",
            EmeraldHex = "#00E676",
            CrimsonHex = "#D50000",
            BgDarkHex = "#0F0507",
            BgGlassHex = "#D924080D",
            BgGlassLighterHex = "#BF3E0E16",
            BorderGlassHex = "#33FF1744",
            BorderActiveHex = "#88FF1744",
            TextPrimaryHex = "#FFEBEE",
            TextMutedHex = "#EF9A9A",
            NumberColorsHex = new[] { "#FF1744", "#FF5252", "#FF9100", "#FFD600", "#00E5FF", "#B388FF", "#FF4081", "#FFFFFF" }
        });

        CurrentTheme = _themes[0];
    }

    public static ThemeDefinition GetThemeById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return _themes[0];
        return _themes.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? _themes[0];
    }

    public static void SetTheme(string themeId, ResourceDictionary? appResources = null)
    {
        var theme = GetThemeById(themeId);
        CurrentTheme = theme;

        if (appResources != null)
        {
            ApplyWpfTheme(theme, appResources);
        }

        OnThemeChanged?.Invoke(theme);
    }

    public static void ApplyWpfTheme(ThemeDefinition theme, ResourceDictionary resources)
    {
        try
        {
            // Colors
            resources["NeonCyanColor"] = ThemeDefinition.ParseWpfColor(theme.AccentHex);
            resources["NeonPurpleColor"] = ThemeDefinition.ParseWpfColor(theme.SecondaryHex);
            resources["NeonEmeraldColor"] = ThemeDefinition.ParseWpfColor(theme.EmeraldHex);
            resources["NeonCrimsonColor"] = ThemeDefinition.ParseWpfColor(theme.CrimsonHex);
            resources["BgDarkColor"] = ThemeDefinition.ParseWpfColor(theme.BgDarkHex);
            resources["BgGlassColor"] = ThemeDefinition.ParseWpfColor(theme.BgGlassHex);
            resources["BgGlassLighterColor"] = ThemeDefinition.ParseWpfColor(theme.BgGlassLighterHex);
            resources["BorderGlassColor"] = ThemeDefinition.ParseWpfColor(theme.BorderGlassHex);
            resources["BorderActiveColor"] = ThemeDefinition.ParseWpfColor(theme.BorderActiveHex);
            resources["TextPrimaryColor"] = ThemeDefinition.ParseWpfColor(theme.TextPrimaryHex);
            resources["TextMutedColor"] = ThemeDefinition.ParseWpfColor(theme.TextMutedHex);

            // Brushes
            resources["NeonCyanBrush"] = new SolidColorBrush(ThemeDefinition.ParseWpfColor(theme.AccentHex));
            resources["NeonPurpleBrush"] = new SolidColorBrush(ThemeDefinition.ParseWpfColor(theme.SecondaryHex));
            resources["NeonEmeraldBrush"] = new SolidColorBrush(ThemeDefinition.ParseWpfColor(theme.EmeraldHex));
            resources["NeonCrimsonBrush"] = new SolidColorBrush(ThemeDefinition.ParseWpfColor(theme.CrimsonHex));
            resources["BgDarkBrush"] = new SolidColorBrush(ThemeDefinition.ParseWpfColor(theme.BgDarkHex));
            resources["BgGlassBrush"] = new SolidColorBrush(ThemeDefinition.ParseWpfColor(theme.BgGlassHex));
            resources["BgGlassLighterBrush"] = new SolidColorBrush(ThemeDefinition.ParseWpfColor(theme.BgGlassLighterHex));
            resources["BorderGlassBrush"] = new SolidColorBrush(ThemeDefinition.ParseWpfColor(theme.BorderGlassHex));
            resources["BorderActiveBrush"] = new SolidColorBrush(ThemeDefinition.ParseWpfColor(theme.BorderActiveHex));
            resources["TextPrimaryBrush"] = new SolidColorBrush(ThemeDefinition.ParseWpfColor(theme.TextPrimaryHex));
            resources["TextMutedBrush"] = new SolidColorBrush(ThemeDefinition.ParseWpfColor(theme.TextMutedHex));
        }
        catch { }
    }
}
