using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Minefield.Data;

/// <summary>
/// Persistent database entity for player level, energy, XP, and abilities.
/// </summary>
[Table("PlayerProfile")]
public class PlayerProfileEntity
{
    [Key]
    public int Id { get; set; } = 1;

    public int Level { get; set; } = 1;

    public long CurrentXP { get; set; } = 0;

    public int MaxEnergy { get; set; } = 100;

    public int CurrentEnergy { get; set; } = 100;

    public string UnlockedAbilitiesJson { get; set; } = "[]";

    public string ActiveThemeId { get; set; } = "cyberpunk";

    public string StatsJson { get; set; } = "{}";

    public string AchievementsJson { get; set; } = "[]";

    public DateTime LastPlayed { get; set; } = DateTime.UtcNow;
}
