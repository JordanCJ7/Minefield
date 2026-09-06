using System;
using System.Text.Json;
using Minefield.Data;

namespace Minefield.Engine;

/// <summary>
/// Manages player progression, level thresholds, energy stamina,
/// combo multipliers, and unlocked ability charges.
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

    public event Action? OnProfileChanged;
    public event Action<int>? OnLevelUp;
    public event Action<string>? OnStatusMessage;

    public long XPForNextLevel => Level * 1000L;
    public float XPProgressFraction => Math.Clamp((float)CurrentXP / XPForNextLevel, 0.0f, 1.0f);
    public float EnergyFraction => Math.Clamp((float)CurrentEnergy / MaxEnergy, 0.0f, 1.0f);

    public void AddSafeCellXP()
    {
        Streak++;
        ComboMultiplier = Math.Clamp(1.0f + (Streak / 10.0f) * 0.5f, 1.0f, 5.0f);

        long xpGained = (long)(10 * ComboMultiplier);
        AddXP(xpGained);

        // Slow energy regeneration on safe streaks
        if (Streak % 5 == 0 && CurrentEnergy < MaxEnergy)
        {
            CurrentEnergy = Math.Min(MaxEnergy, CurrentEnergy + 2);
        }

        OnProfileChanged?.Invoke();
    }

    public void AddSectorLockXP()
    {
        long xpGained = (long)(500 * ComboMultiplier);
        AddXP(xpGained);

        // Replenish energy and grant drone charge
        CurrentEnergy = Math.Min(MaxEnergy, CurrentEnergy + 30);
        ReconDronesAvailable++;
        BlastShieldCharges = Math.Min(3, BlastShieldCharges + 1);

        OnStatusMessage?.Invoke($"SECTOR SECURED! +{xpGained} XP | +30 ENERGY | +1 RECON DRONE");
        OnProfileChanged?.Invoke();
    }

    public bool DeductMineEnergy(out bool shieldAbsorbed)
    {
        if (BlastShieldCharges > 0)
        {
            BlastShieldCharges--;
            shieldAbsorbed = true;
            OnStatusMessage?.Invoke("BLAST SHIELD ABSORBED DETONATION! NO ENERGY LOST.");
            OnProfileChanged?.Invoke();
            return false;
        }

        shieldAbsorbed = false;
        CurrentEnergy = Math.Max(0, CurrentEnergy - 25);
        ComboMultiplier = 1.0f;
        Streak = 0;

        OnStatusMessage?.Invoke("MINE DETONATED! -25 ENERGY | COMBO RESET.");
        OnProfileChanged?.Invoke();
        return CurrentEnergy == 0;
    }

    private void AddXP(long amount)
    {
        CurrentXP += amount;
        while (CurrentXP >= XPForNextLevel)
        {
            CurrentXP -= XPForNextLevel;
            Level++;
            MaxEnergy += 10;
            CurrentEnergy = MaxEnergy; // Full energy on level up
            OnLevelUp?.Invoke(Level);
            OnStatusMessage?.Invoke($"PROMOTION! LEVEL {Level} REACHED! MAX ENERGY INCREASED.");
        }
    }

    public void SaveToDatabase(string? dbPath = null)
    {
        try
        {
            using var db = new MinefieldDbContext(dbPath);
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
