using System;
using System.Text.Json;

namespace Minefield.Engine;

/// <summary>
/// Tracks lifetime player performance, exploration distance, and combat telemetry across all game sessions.
/// </summary>
public class LifetimeStats
{
    public long TotalCellsCleared { get; set; } = 0;
    public long TotalMinesFlagged { get; set; } = 0;
    public int TotalSectorsLocked { get; set; } = 0;
    public float HighestComboMultiplier { get; set; } = 1.0f;
    public int TotalShieldsAbsorbed { get; set; } = 0;
    public int TotalDetonations { get; set; } = 0;
    public int LongestSafeStreak { get; set; } = 0;
    public int TotalDronesUsed { get; set; } = 0;
    public int TotalChordsExecuted { get; set; } = 0;
    public long TotalXpEarned { get; set; } = 0;
    public int SectorsVisited { get; set; } = 1;

    public void RecordSafeReveal(int streak, float combo, long xpGained)
    {
        TotalCellsCleared++;
        TotalXpEarned += xpGained;
        if (streak > LongestSafeStreak)
            LongestSafeStreak = streak;
        if (combo > HighestComboMultiplier)
            HighestComboMultiplier = combo;
    }

    public void RecordFlagPlaced()
    {
        TotalMinesFlagged++;
    }

    public void RecordSectorLocked(long xpGained)
    {
        TotalSectorsLocked++;
        TotalXpEarned += xpGained;
    }

    public void RecordShieldAbsorbed()
    {
        TotalShieldsAbsorbed++;
    }

    public void RecordDetonation()
    {
        TotalDetonations++;
    }

    public void RecordDroneUsed()
    {
        TotalDronesUsed++;
    }

    public void RecordChord()
    {
        TotalChordsExecuted++;
    }

    public void RecordSectorVisited(int activeSectorCount)
    {
        if (activeSectorCount > SectorsVisited)
            SectorsVisited = activeSectorCount;
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }

    public static LifetimeStats FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new LifetimeStats();

        try
        {
            return JsonSerializer.Deserialize<LifetimeStats>(json) ?? new LifetimeStats();
        }
        catch
        {
            return new LifetimeStats();
        }
    }
}
