using System;

namespace Minefield.Engine;

public enum DifficultyLevel
{
    Cadet,      // 12% density, low penalty, +2 shields, 1.0x XP
    Standard,   // 17% density, standard penalty, +1 shield, 1.5x XP
    Hazard,     // 22% density, heavy penalty, +1 shield, 2.0x XP
    Nightmare   // 28% density, lethal penalty, 0 shields, 3.0x XP
}

/// <summary>
/// Configuration for expedition threat levels and penalty parameters.
/// </summary>
public record DifficultyConfig(
    DifficultyLevel Level,
    string Title,
    string Description,
    int MineDensityPercent,
    int EnergyPenalty,
    int XpPenalty,
    int StartingShields,
    int StartingDrones,
    float XpMultiplier,
    string BadgeColorHex
)
{
    public static DifficultyConfig Cadet => new(
        DifficultyLevel.Cadet,
        "CADET // LOW THREAT",
        "Sparse subterranean mine density. Lower penalty on detonation with supplementary kinetic shielding.",
        12,
        15,
        25,
        2,
        3,
        1.0f,
        "#00E676"
    );

    public static DifficultyConfig Standard => new(
        DifficultyLevel.Standard,
        "STANDARD // OPERATIVE",
        "Balanced procedural minefield requiring solid deductive logic and steady exploration pacing.",
        17,
        25,
        50,
        1,
        2,
        1.5f,
        "#00E5FF"
    );

    public static DifficultyConfig Hazard => new(
        DifficultyLevel.Hazard,
        "HAZARD // HIGH RISK",
        "Dense hazard matrix with punishing detonation energy loss and doubled XP progression yields.",
        22,
        35,
        100,
        1,
        2,
        2.0f,
        "#FFA726"
    );

    public static DifficultyConfig Nightmare => new(
        DifficultyLevel.Nightmare,
        "NIGHTMARE // HARDCORE",
        "Lethal mine saturation. Zero starting shields. Detonations cause catastrophic core damage. 3x XP multiplier.",
        28,
        50,
        200,
        0,
        1,
        3.0f,
        "#FF1744"
    );

    public static DifficultyConfig GetConfig(DifficultyLevel level) => level switch
    {
        DifficultyLevel.Cadet => Cadet,
        DifficultyLevel.Standard => Standard,
        DifficultyLevel.Hazard => Hazard,
        DifficultyLevel.Nightmare => Nightmare,
        _ => Standard
    };
}
