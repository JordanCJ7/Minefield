using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Minefield.Engine;

/// <summary>
/// Manages the catalog of in-game achievements, evaluates progress criteria,
/// triggers unlock notifications, and persists state.
/// </summary>
public class AchievementManager
{
    private readonly List<Achievement> _achievements = new();

    public event Action<Achievement>? OnAchievementUnlocked;

    public IReadOnlyList<Achievement> Achievements => _achievements;
    public int UnlockedCount => _achievements.Count(a => a.IsUnlocked);
    public int TotalCount => _achievements.Count;
    public long TotalXpEarnedFromAchievements => _achievements.Where(a => a.IsUnlocked).Sum(a => a.XpReward);

    public AchievementManager()
    {
        InitializeCatalog();
    }

    private void InitializeCatalog()
    {
        _achievements.Clear();

        _achievements.Add(new Achievement
        {
            Id = "first_contact",
            Title = "First Contact",
            Description = "Reveal your first safe sector coordinate without detonation.",
            Category = AchievementCategory.Exploration,
            GlyphIcon = "⚡",
            TargetValue = 1,
            XpReward = 250
        });

        _achievements.Add(new Achievement
        {
            Id = "sapper_initiate",
            Title = "Sapper Initiate",
            Description = "Flag your first confirmed subterranean mine.",
            Category = AchievementCategory.Tactics,
            GlyphIcon = "🚩",
            TargetValue = 1,
            XpReward = 250
        });

        _achievements.Add(new Achievement
        {
            Id = "sector_lockdown",
            Title = "Sector Lockdown",
            Description = "Completely clear and secure a 16x16 sector.",
            Category = AchievementCategory.Exploration,
            GlyphIcon = "⬢",
            TargetValue = 1,
            XpReward = 1000
        });

        _achievements.Add(new Achievement
        {
            Id = "overclocked",
            Title = "Overclocked",
            Description = "Achieve a x3.0 combo multiplier via consecutive safe reveals.",
            Category = AchievementCategory.Tactics,
            GlyphIcon = "🔥",
            TargetValue = 3,
            XpReward = 750
        });

        _achievements.Add(new Achievement
        {
            Id = "max_velocity",
            Title = "Max Velocity",
            Description = "Reach the maximum x5.0 combo multiplier streak.",
            Category = AchievementCategory.Tactics,
            GlyphIcon = "⚡",
            TargetValue = 5,
            XpReward = 2500
        });

        _achievements.Add(new Achievement
        {
            Id = "aerial_recon",
            Title = "Aerial Recon",
            Description = "Deploy a Recon Drone to scan unknown territory with zero risk.",
            Category = AchievementCategory.Tactics,
            GlyphIcon = "🎯",
            TargetValue = 1,
            XpReward = 500
        });

        _achievements.Add(new Achievement
        {
            Id = "kevlar_core",
            Title = "Kevlar Core",
            Description = "Absorb a mine detonation using the Kinetic Blast Shield.",
            Category = AchievementCategory.Survival,
            GlyphIcon = "🛡",
            TargetValue = 1,
            XpReward = 1000
        });

        _achievements.Add(new Achievement
        {
            Id = "sector_vanguard",
            Title = "Sector Vanguard",
            Description = "Completely secure 5 infinite sectors.",
            Category = AchievementCategory.Exploration,
            GlyphIcon = "👑",
            TargetValue = 5,
            XpReward = 3000
        });

        _achievements.Add(new Achievement
        {
            Id = "operative_promoted",
            Title = "Field Specialist",
            Description = "Attain Operative Level 5.",
            Category = AchievementCategory.Progression,
            GlyphIcon = "🎖",
            TargetValue = 5,
            XpReward = 2000
        });

        _achievements.Add(new Achievement
        {
            Id = "grand_architect",
            Title = "Grand Architect",
            Description = "Attain Operative Level 10.",
            Category = AchievementCategory.Progression,
            GlyphIcon = "💎",
            TargetValue = 10,
            XpReward = 5000
        });

        _achievements.Add(new Achievement
        {
            Id = "century_sweeper",
            Title = "Century Sweeper",
            Description = "Clear 100 safe cells across the infinite grid.",
            Category = AchievementCategory.Progression,
            GlyphIcon = "🏆",
            TargetValue = 100,
            XpReward = 1500
        });

        _achievements.Add(new Achievement
        {
            Id = "cartographer",
            Title = "Cartographer",
            Description = "Stream and visit 10 active sectors in SQLite memory.",
            Category = AchievementCategory.Exploration,
            GlyphIcon = "🌐",
            TargetValue = 10,
            XpReward = 1500
        });
    }

    /// <summary>
    /// Evaluates all achievements against current player stats and triggers unlocks.
    /// </summary>
    public void Evaluate(PlayerProfile profile, LifetimeStats stats)
    {
        UpdateProgress("first_contact", stats.TotalCellsCleared);
        UpdateProgress("sapper_initiate", stats.TotalMinesFlagged);
        UpdateProgress("sector_lockdown", stats.TotalSectorsLocked);
        UpdateProgress("overclocked", (long)stats.HighestComboMultiplier);
        UpdateProgress("max_velocity", (long)stats.HighestComboMultiplier);
        UpdateProgress("aerial_recon", stats.TotalDronesUsed);
        UpdateProgress("kevlar_core", stats.TotalShieldsAbsorbed);
        UpdateProgress("sector_vanguard", stats.TotalSectorsLocked);
        UpdateProgress("operative_promoted", profile.Level);
        UpdateProgress("grand_architect", profile.Level);
        UpdateProgress("century_sweeper", stats.TotalCellsCleared);
        UpdateProgress("cartographer", stats.SectorsVisited);
    }

    private void UpdateProgress(string id, long value)
    {
        var achievement = _achievements.FirstOrDefault(a => a.Id == id);
        if (achievement == null) return;

        achievement.CurrentValue = Math.Max(achievement.CurrentValue, value);

        if (!achievement.IsUnlocked && achievement.CurrentValue >= achievement.TargetValue)
        {
            achievement.IsUnlocked = true;
            achievement.UnlockedAt = DateTime.UtcNow;
            OnAchievementUnlocked?.Invoke(achievement);
        }
    }

    public string ToJson()
    {
        var states = _achievements.Select(a => new
        {
            a.Id,
            a.CurrentValue,
            a.IsUnlocked,
            a.UnlockedAt
        });
        return JsonSerializer.Serialize(states);
    }

    public void LoadFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                if (elem.TryGetProperty("Id", out var idProp))
                {
                    string id = idProp.GetString() ?? "";
                    var ach = _achievements.FirstOrDefault(a => a.Id == id);
                    if (ach != null)
                    {
                        if (elem.TryGetProperty("CurrentValue", out var cv))
                            ach.CurrentValue = cv.GetInt64();
                        if (elem.TryGetProperty("IsUnlocked", out var iu))
                            ach.IsUnlocked = iu.GetBoolean();
                        if (elem.TryGetProperty("UnlockedAt", out var ua) && ua.ValueKind == JsonValueKind.String)
                        {
                            if (DateTime.TryParse(ua.GetString(), out var dt))
                                ach.UnlockedAt = dt;
                        }
                    }
                }
            }
        }
        catch { }
    }
}
