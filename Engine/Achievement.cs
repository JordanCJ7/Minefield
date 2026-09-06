using System;

namespace Minefield.Engine;

public enum AchievementCategory
{
    Exploration,
    Tactics,
    Survival,
    Progression
}

/// <summary>
/// Represents an unlockable in-game achievement with progress tracking and XP rewards.
/// </summary>
public class Achievement
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AchievementCategory Category { get; set; } = AchievementCategory.Exploration;
    public string GlyphIcon { get; set; } = "★";
    public long TargetValue { get; set; } = 1;
    public long CurrentValue { get; set; } = 0;
    public bool IsUnlocked { get; set; } = false;
    public DateTime? UnlockedAt { get; set; } = null;
    public long XpReward { get; set; } = 500;

    public float ProgressFraction => TargetValue <= 0 ? 1.0f : Math.Clamp((float)CurrentValue / TargetValue, 0.0f, 1.0f);
    public int ProgressPercent => (int)(ProgressFraction * 100);
}
