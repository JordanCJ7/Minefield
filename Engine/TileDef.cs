using System;

namespace Minefield.Engine;

/// <summary>
/// Cell visibility and interaction states.
/// </summary>
public enum CellState : byte
{
    Hidden = 0,
    Revealed = 1,
    Flagged = 2,
    Detonated = 3
}

/// <summary>
/// Cell content constants.
/// 0 = Empty (0 adjacent mines)
/// 1..8 = Clue number (adjacent mine count)
/// 9 = Mine
/// 10 = Uninitialized
/// </summary>
public static class CellContent
{
    public const byte Empty = 0;
    public const byte Mine = 9;
    public const byte Uninitialized = 10;
}
