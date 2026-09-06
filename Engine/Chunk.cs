using System;

namespace Minefield.Engine;

/// <summary>
/// In-memory representation of a 16x16 cell sector.
/// Supports high-performance byte-packed tile storage, object pool recycling,
/// and automated sector lock evaluation.
/// </summary>
public class Chunk
{
    public const int Dimension = 16;
    public const int TotalCells = Dimension * Dimension; // 256 cells

    public int ChunkX { get; set; }
    public int ChunkY { get; set; }

    public bool IsLocked { get; set; }
    public bool IsModified { get; set; }
    public bool IsGenerated { get; set; }

    /// <summary>
    /// Packed byte array: Lower 4 bits = CellState (0..3), Upper 4 bits = Content (0..9).
    /// </summary>
    public readonly byte[] RawTiles = new byte[TotalCells];

    public Chunk()
    {
        Reset();
    }

    public static int GetIndex(int localX, int localY)
    {
        return localY * Dimension + localX;
    }

    public CellState GetState(int localX, int localY)
    {
        if ((uint)localX >= Dimension || (uint)localY >= Dimension)
            throw new ArgumentOutOfRangeException($"Coordinates out of range: ({localX}, {localY})");

        int index = localY * Dimension + localX;
        return (CellState)(RawTiles[index] & 0x0F);
    }

    public byte GetContent(int localX, int localY)
    {
        if ((uint)localX >= Dimension || (uint)localY >= Dimension)
            throw new ArgumentOutOfRangeException($"Coordinates out of range: ({localX}, {localY})");

        int index = localY * Dimension + localX;
        return (byte)((RawTiles[index] >> 4) & 0x0F);
    }

    public void SetCell(int localX, int localY, CellState state, byte content)
    {
        if ((uint)localX >= Dimension || (uint)localY >= Dimension)
            throw new ArgumentOutOfRangeException($"Coordinates out of range: ({localX}, {localY})");

        int index = localY * Dimension + localX;
        byte packed = (byte)(((byte)state & 0x0F) | ((content & 0x0F) << 4));
        if (RawTiles[index] != packed)
        {
            RawTiles[index] = packed;
            IsModified = true;
        }
    }

    public void SetState(int localX, int localY, CellState state)
    {
        if ((uint)localX >= Dimension || (uint)localY >= Dimension)
            throw new ArgumentOutOfRangeException($"Coordinates out of range: ({localX}, {localY})");

        int index = localY * Dimension + localX;
        byte content = (byte)((RawTiles[index] >> 4) & 0x0F);
        byte packed = (byte)(((byte)state & 0x0F) | (content << 4));
        if (RawTiles[index] != packed)
        {
            RawTiles[index] = packed;
            IsModified = true;
        }
    }

    public void SetContent(int localX, int localY, byte content)
    {
        if ((uint)localX >= Dimension || (uint)localY >= Dimension)
            throw new ArgumentOutOfRangeException($"Coordinates out of range: ({localX}, {localY})");

        int index = localY * Dimension + localX;
        byte state = (byte)(RawTiles[index] & 0x0F);
        byte packed = (byte)(state | ((content & 0x0F) << 4));
        if (RawTiles[index] != packed)
        {
            RawTiles[index] = packed;
            IsModified = true;
        }
    }

    public bool IsMine(int localX, int localY)
    {
        return GetContent(localX, localY) == CellContent.Mine;
    }

    public bool IsRevealed(int localX, int localY)
    {
        return GetState(localX, localY) == CellState.Revealed;
    }

    public bool IsFlagged(int localX, int localY)
    {
        return GetState(localX, localY) == CellState.Flagged;
    }

    public int CountMines()
    {
        int count = 0;
        for (int i = 0; i < TotalCells; i++)
        {
            if (((RawTiles[i] >> 4) & 0x0F) == CellContent.Mine)
                count++;
        }
        return count;
    }

    public int CountSafeRevealed()
    {
        int count = 0;
        for (int i = 0; i < TotalCells; i++)
        {
            byte state = (byte)(RawTiles[i] & 0x0F);
            byte content = (byte)((RawTiles[i] >> 4) & 0x0F);
            if (state == (byte)CellState.Revealed && content != CellContent.Mine)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Evaluates whether all safe cells in the sector have been revealed.
    /// If so, locks the chunk and returns true.
    /// </summary>
    public bool CheckAndLock()
    {
        if (IsLocked) return false;

        int totalSafe = TotalCells - CountMines();
        int safeRevealed = CountSafeRevealed();

        if (safeRevealed >= totalSafe && totalSafe > 0)
        {
            IsLocked = true;
            IsModified = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resets this chunk for object pool reuse.
    /// </summary>
    public void Reset()
    {
        ChunkX = 0;
        ChunkY = 0;
        IsLocked = false;
        IsModified = false;
        IsGenerated = false;
        Array.Clear(RawTiles, 0, TotalCells);
    }

    public byte[] Serialize()
    {
        byte[] copy = new byte[TotalCells];
        Buffer.BlockCopy(RawTiles, 0, copy, 0, TotalCells);
        return copy;
    }

    public void Deserialize(byte[] data)
    {
        if (data == null || data.Length != TotalCells)
            throw new ArgumentException($"Invalid tile data length. Expected {TotalCells} bytes.", nameof(data));

        Buffer.BlockCopy(data, 0, RawTiles, 0, TotalCells);
        IsGenerated = true;
    }
}
