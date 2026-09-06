using System;
using System.Text.Json;
using Minefield.Data;

namespace Minefield.Engine;

/// <summary>
/// Manages player progression, level thresholds, energy stamina,
/// combo multipliers, lifetime telemetry stats, and achievements.
/// </summary>
public class PlayerProfile
{
    public int Level { get; private set; } = 1;
    public long CurrentXP { get; private set; } = 0;
    public int MaxEnergy { get; private set; } = 100;
    public int CurrentEnergy { get; private set; } = 100;
    public float ComboMultiplier { get; private set; } = 1.0f;
    public int Streak { get; private set; } = 0;

    // Ability Charges
    public int ReconDronesAvailable { get; set; } = 2;
    public int BlastShieldCharges { get; set; } = 1;

    // Customization & Stats
    public string ActiveThemeId { get; set; } = "cyberpunk";
    public string Codename { get; set; } = "OPERATIVE #0042";
    public LifetimeStats Stats { get; } = new();
    public AchievementManager Achievements { get; } = new();
    public DifficultyLevel CurrentDifficulty { get; set; } = DifficultyLevel.Standard;

    // Run-Specific Metrics for Game Over / Expeditions
    public int RunCellsCleared { get; set; } = 0;
    public int RunSectorsLocked { get; set; } = 0;
    public long RunXpEarned { get; set; } = 0;
    public float RunHighestCombo { get; set; } = 1.0f;
    public int RunDetonations { get; set; } = 0;

    public string RankTitle => Level switch
    {
        < 3 => "RECON SCOUT",
        < 6 => "TACTICAL SAPPER",
        < 10 => "FIELD SPECIALIST",
        < 15 => "SECTOR VANGUARD",
        _ => "GRAND ARCHITECT"
    };

    public event Action? OnProfileChanged;
    public event Action<int>? OnLevelUp;
    public event Action<string>? OnStatusMessage;
    public event Action? OnComboShattered;
    public event Action? OnEnergyDepleted;

    public long XPForNextLevel => Level * 1000L;
    public float XPProgressFraction => Math.Clamp((float)CurrentXP / XPForNextLevel, 0.0f, 1.0f);
    public float EnergyFraction => Math.Clamp((float)CurrentEnergy / MaxEnergy, 0.0f, 1.0f);

    public PlayerProfile()
    {
        Achievements.OnAchievementUnlocked += ach =>
        {
            AddXP(ach.XpReward);
            OnStatusMessage?.Invoke($"🏆 ACHIEVEMENT UNLOCKED: {ach.Title} (+{ach.XpReward} XP)");
            OnProfileChanged?.Invoke();
        };
    }

    public void AddSafeCellXP()
    {
        Streak++;
        ComboMultiplier = Math.Clamp(1.0f + (Streak / 10.0f) * 0.5f, 1.0f, 5.0f);

        long xpGained = (long)(10 * ComboMultiplier);
        AddXP(xpGained);

        RunCellsCleared++;
        RunXpEarned += xpGained;
        if (ComboMultiplier > RunHighestCombo)
            RunHighestCombo = ComboMultiplier;

        Stats.RecordSafeReveal(Streak, ComboMultiplier, xpGained);

        // Slow energy regeneration on safe streaks
        if (Streak % 5 == 0 && CurrentEnergy < MaxEnergy)
        {
            CurrentEnergy = Math.Min(MaxEnergy, CurrentEnergy + 2);
        }

        Achievements.Evaluate(this, Stats);
        OnProfileChanged?.Invoke();
    }

    public void AddSectorLockXP()
    {
        long xpGained = (long)(500 * ComboMultiplier);
        AddXP(xpGained);

        RunSectorsLocked++;
        RunXpEarned += xpGained;

        // Replenish energy and grant drone charge
        CurrentEnergy = Math.Min(MaxEnergy, CurrentEnergy + 30);
        ReconDronesAvailable++;
        BlastShieldCharges = Math.Min(3, BlastShieldCharges + 1);

        Stats.RecordSectorLocked(xpGained);
        Achievements.Evaluate(this, Stats);

        OnStatusMessage?.Invoke($"SECTOR SECURED! +{xpGained} XP | +30 ENERGY | +1 RECON DRONE");
        OnProfileChanged?.Invoke();
    }

    public bool DeductMineEnergy(out bool shieldAbsorbed)
    {
        var config = DifficultyConfig.GetConfig(CurrentDifficulty);
        return DeductMineEnergy(config.EnergyPenalty, config.XpPenalty, out shieldAbsorbed);
    }

    public bool DeductMineEnergy(int penaltyEnergy, int xpPenalty, out bool shieldAbsorbed)
    {
        if (BlastShieldCharges > 0)
        {
            BlastShieldCharges--;
            shieldAbsorbed = true;
            Stats.RecordShieldAbsorbed();
            Achievements.Evaluate(this, Stats);

            OnStatusMessage?.Invoke("BLAST SHIELD ABSORBED DETONATION! NO ENERGY LOST.");
            OnProfileChanged?.Invoke();
            return false;
        }

        shieldAbsorbed = false;
        RunDetonations++;
        bool hadCombo = Streak > 0 || ComboMultiplier > 1.0f;

        CurrentEnergy = Math.Max(0, CurrentEnergy - penaltyEnergy);
        Streak = 0;
        ComboMultiplier = 1.0f;

        if (hadCombo)
        {
            OnComboShattered?.Invoke();
        }

        long actualXpLoss = Math.Min(CurrentXP, (long)xpPenalty);
        CurrentXP -= actualXpLoss;

        Stats.RecordDetonation();
        Achievements.Evaluate(this, Stats);

        OnStatusMessage?.Invoke($"⚠ MINE DETONATED! -{penaltyEnergy} ENERGY | -{actualXpLoss} XP | COMBO SHATTERED!");
        OnProfileChanged?.Invoke();

        if (CurrentEnergy == 0)
        {
            OnEnergyDepleted?.Invoke();
            return true;
        }

        return false;
    }

    public void RebootExpedition(int startingShields, int startingDrones)
    {
        CurrentEnergy = MaxEnergy;
        Streak = 0;
        ComboMultiplier = 1.0f;
        BlastShieldCharges = startingShields;
        ReconDronesAvailable = startingDrones;
        RunCellsCleared = 0;
        RunSectorsLocked = 0;
        RunXpEarned = 0;
        RunHighestCombo = 1.0f;
        RunDetonations = 0;
        SaveToDatabase();
        OnProfileChanged?.Invoke();
    }

    public void RecordFlagPlaced()
    {
        Stats.RecordFlagPlaced();
        Achievements.Evaluate(this, Stats);
        OnProfileChanged?.Invoke();
    }

    public void RecordDroneUsed()
    {
        Stats.RecordDroneUsed();
        Achievements.Evaluate(this, Stats);
        OnProfileChanged?.Invoke();
    }

    public void RecordChord()
    {
        Stats.RecordChord();
        Achievements.Evaluate(this, Stats);
        OnProfileChanged?.Invoke();
    }

    public void RecordSectorVisited(int activeSectorCount)
    {
        Stats.RecordSectorVisited(activeSectorCount);
        Achievements.Evaluate(this, Stats);
    }

    public void AddXP(long amount)
    {
        CurrentXP += amount;
        while (CurrentXP >= XPForNextLevel)
        {
            CurrentXP -= XPForNextLevel;
            Level++;
            MaxEnergy += 10;
            CurrentEnergy = MaxEnergy; // Full energy on level up
            OnLevelUp?.Invoke(Level);
            OnStatusMessage?.Invoke($"PROMOTION! LEVEL {Level} REACHED ({RankTitle})! MAX ENERGY INCREASED.");
            Achievements.Evaluate(this, Stats);
        }
    }

    public void ResetProfile(string? dbPath = null)
    {
        Level = 1;
        CurrentXP = 0;
        MaxEnergy = 100;
        CurrentEnergy = 100;
        ComboMultiplier = 1.0f;
        Streak = 0;
        ReconDronesAvailable = 2;
        BlastShieldCharges = 1;
        ActiveThemeId = "cyberpunk";

        Stats.TotalCellsCleared = 0;
        Stats.TotalMinesFlagged = 0;
        Stats.TotalSectorsLocked = 0;
        Stats.HighestComboMultiplier = 1.0f;
        Stats.TotalShieldsAbsorbed = 0;
        Stats.TotalDetonations = 0;
        Stats.LongestSafeStreak = 0;
        Stats.TotalDronesUsed = 0;
        Stats.TotalChordsExecuted = 0;
        Stats.TotalXpEarned = 0;
        Stats.SectorsVisited = 1;

        SaveToDatabase(dbPath);
        OnProfileChanged?.Invoke();
    }

    public void SaveToDatabase(string? dbPath = null)
    {
        try
        {
            using var db = new MinefieldDbContext(dbPath);
            db.Database.EnsureCreated();
            var entity = db.PlayerProfiles.Find(1);
            if (entity == null)
            {
                entity = new PlayerProfileEntity { Id = 1 };
                db.PlayerProfiles.Add(entity);
            }

            entity.Level = Level;
            entity.CurrentXP = CurrentXP;
            entity.MaxEnergy = MaxEnergy;
            entity.CurrentEnergy = CurrentEnergy;
            entity.ActiveThemeId = ActiveThemeId;
            entity.StatsJson = Stats.ToJson();
            entity.AchievementsJson = Achievements.ToJson();
            entity.LastPlayed = DateTime.UtcNow;

            var abilityData = new { ReconDrones = ReconDronesAvailable, BlastShields = BlastShieldCharges };
            entity.UnlockedAbilitiesJson = JsonSerializer.Serialize(abilityData);

            db.SaveChanges();
        }
        catch { }
    }

    public void LoadFromDatabase(string? dbPath = null)
    {
        try
        {
            using var db = new MinefieldDbContext(dbPath);
            var entity = db.PlayerProfiles.Find(1);
            if (entity != null)
            {
                Level = Math.Max(1, entity.Level);
                CurrentXP = entity.CurrentXP;
                MaxEnergy = Math.Max(100, entity.MaxEnergy);
                CurrentEnergy = Math.Clamp(entity.CurrentEnergy, 0, MaxEnergy);

                if (!string.IsNullOrWhiteSpace(entity.ActiveThemeId))
                    ActiveThemeId = entity.ActiveThemeId;

                if (!string.IsNullOrWhiteSpace(entity.StatsJson))
                {
                    var loadedStats = LifetimeStats.FromJson(entity.StatsJson);
                    Stats.TotalCellsCleared = loadedStats.TotalCellsCleared;
                    Stats.TotalMinesFlagged = loadedStats.TotalMinesFlagged;
                    Stats.TotalSectorsLocked = loadedStats.TotalSectorsLocked;
                    Stats.HighestComboMultiplier = loadedStats.HighestComboMultiplier;
                    Stats.TotalShieldsAbsorbed = loadedStats.TotalShieldsAbsorbed;
                    Stats.TotalDetonations = loadedStats.TotalDetonations;
                    Stats.LongestSafeStreak = loadedStats.LongestSafeStreak;
                    Stats.TotalDronesUsed = loadedStats.TotalDronesUsed;
                    Stats.TotalChordsExecuted = loadedStats.TotalChordsExecuted;
                    Stats.TotalXpEarned = loadedStats.TotalXpEarned;
                    Stats.SectorsVisited = Math.Max(1, loadedStats.SectorsVisited);
                }

                if (!string.IsNullOrWhiteSpace(entity.AchievementsJson))
                {
                    Achievements.LoadFromJson(entity.AchievementsJson);
                }

                if (!string.IsNullOrEmpty(entity.UnlockedAbilitiesJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(entity.UnlockedAbilitiesJson);
                        if (doc.RootElement.TryGetProperty("ReconDrones", out var rd))
                            ReconDronesAvailable = rd.GetInt32();
                        if (doc.RootElement.TryGetProperty("BlastShields", out var bs))
                            BlastShieldCharges = bs.GetInt32();
                    }
                    catch { }
                }

                OnProfileChanged?.Invoke();
            }
        }
        catch { }
    }
}
