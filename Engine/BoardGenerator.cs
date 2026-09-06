using System;
using System.Collections.Generic;

namespace Minefield.Engine;

/// <summary>
/// Deterministic procedural chunk generator with globally consistent, boundary-seamless
/// mine placement and constraint-checked clues guaranteeing zero cross-sector contradictions.
/// </summary>
public class BoardGenerator
{
    private readonly DeterministicSolver _solver = new();

    public const int DefaultMineCount = 42; // ~16.4% density on 16x16 (256 cells)
    public const int MaxMutations = 30;

    public int WorldSeed { get; set; } = 1337;
    public int MineDensityPercent { get; set; } = 17;

    public static ulong HashCoordinates(int x, int y, int seed)
    {
        ulong h = (ulong)seed ^ ((ulong)x * 0x517cc1b727220a95UL) ^ ((ulong)y * 0x6c62272e07bb0142UL);
        h ^= h >> 33;
        h *= 0xff51afd7ed558ccdUL;
        h ^= h >> 33;
        h *= 0xc4ceb9fe1a85ec53UL;
        h ^= h >> 33;
        return h;
    }

    /// <summary>
    /// Checks if a world coordinate is in a guaranteed safe foothold (world origin or legacy starter zone).
    /// </summary>
    public static bool IsInStarterZone(int wx, int wy)
    {
        // 1. World origin 5x5 safe zone around (0, 0)
        if (Math.Abs(wx) <= 2 && Math.Abs(wy) <= 2)
            return true;

        // 2. Legacy starter zone around center (7..9, 7..9) of chunk (0,0)
        if (wx >= 7 && wx <= 9 && wy >= 7 && wy <= 9)
            return true;

        return false;
    }

    /// <summary>
    /// Evaluates base candidate mine status for any world coordinate.
    /// </summary>
    public bool IsCandidateMine(int wx, int wy)
    {
        if (IsInStarterZone(wx, wy))
            return false;

        // Sector clearing opening (local cells 7,7 and 8,8 in each chunk)
        int lx = wx % Chunk.Dimension;
        if (lx < 0) lx += Chunk.Dimension;
        int ly = wy % Chunk.Dimension;
        if (ly < 0) ly += Chunk.Dimension;
        if ((lx == 7 || lx == 8) && (ly == 7 || ly == 8))
            return false;

        ulong h = HashCoordinates(wx, wy, WorldSeed);
        float val = (h & 0xFFFF) / 65535.0f;
        return val < (MineDensityPercent / 100.0f);
    }

    /// <summary>
    /// Returns whether the cell at (wx, wy) in the infinite world contains a mine.
    /// Applies deterministic local stencil filters to suppress 2x2 clumps and 50/50 ambiguities.
    /// </summary>
    public bool IsMineAt(int wx, int wy)
    {
        if (!IsCandidateMine(wx, wy))
            return false;

        // Stencil 1: Suppress 2x2 solid blocks of mines
        for (int dy = -1; dy <= 0; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                int x0 = wx + dx;
                int y0 = wy + dy;
                if (IsCandidateMine(x0, y0) &&
                    IsCandidateMine(x0 + 1, y0) &&
                    IsCandidateMine(x0, y0 + 1) &&
                    IsCandidateMine(x0 + 1, y0 + 1))
                {
                    ulong hSelf = HashCoordinates(wx, wy, WorldSeed);
                    ulong h00 = HashCoordinates(x0, y0, WorldSeed);
                    ulong h10 = HashCoordinates(x0 + 1, y0, WorldSeed);
                    ulong h01 = HashCoordinates(x0, y0 + 1, WorldSeed);
                    ulong h11 = HashCoordinates(x0 + 1, y0 + 1, WorldSeed);

                    ulong minH = Math.Min(Math.Min(h00, h10), Math.Min(h01, h11));
                    if (hSelf == minH)
                        return false;
                }
            }
        }

        // Stencil 2: Suppress 2x2 diagonal checkerboard ambiguity
        for (int dy = -1; dy <= 0; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                int x0 = wx + dx;
                int y0 = wy + dy;
                bool m00 = IsCandidateMine(x0, y0);
                bool m10 = IsCandidateMine(x0 + 1, y0);
                bool m01 = IsCandidateMine(x0, y0 + 1);
                bool m11 = IsCandidateMine(x0 + 1, y0 + 1);

                if (m00 && m11 && !m10 && !m01)
                {
                    ulong hSelf = HashCoordinates(wx, wy, WorldSeed);
                    ulong h00 = HashCoordinates(x0, y0, WorldSeed);
                    ulong h11 = HashCoordinates(x0 + 1, y0 + 1, WorldSeed);
                    if (hSelf == Math.Min(h00, h11))
                        return false;
                }
                else if (m10 && m01 && !m00 && !m11)
                {
                    ulong hSelf = HashCoordinates(wx, wy, WorldSeed);
                    ulong h10 = HashCoordinates(x0 + 1, y0, WorldSeed);
                    ulong h01 = HashCoordinates(x0, y0 + 1, WorldSeed);
                    if (hSelf == Math.Min(h10, h01))
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Computes the exact clue (number of adjacent mines, 0..8) for any cell at world coordinates (wx, wy).
    /// </summary>
    public byte GetClueAt(int wx, int wy)
    {
        if (IsMineAt(wx, wy))
            return CellContent.Mine;

        byte count = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if (IsMineAt(wx + dx, wy + dy))
                    count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Generates a chunk using globally consistent, boundary-seamless procedural placement.
    /// </summary>
    public void GenerateChunk(Chunk chunk, Func<int, int, Chunk?>? getNeighborChunk = null)
    {
        bool isOrigin = (chunk.ChunkX == 0 && chunk.ChunkY == 0);

        for (int ly = 0; ly < Chunk.Dimension; ly++)
        {
            for (int lx = 0; lx < Chunk.Dimension; lx++)
            {
                int wx = chunk.ChunkX * Chunk.Dimension + lx;
                int wy = chunk.ChunkY * Chunk.Dimension + ly;

                bool isMine = IsMineAt(wx, wy);
                byte content = isMine ? CellContent.Mine : GetClueAt(wx, wy);

                CellState state = CellState.Hidden;
                if (isOrigin && IsInStarterZone(wx, wy))
                {
                    state = CellState.Revealed;
                }

                chunk.SetCell(lx, ly, state, content);
            }
        }

        chunk.IsGenerated = true;
        chunk.IsModified = true;
    }

    /// <summary>
    /// Recalculates clues for all non-mine cells in a chunk.
    /// </summary>
    public void RecalculateClues(Chunk chunk, Func<int, int, Chunk?>? getNeighborChunk = null)
    {
        for (int ly = 0; ly < Chunk.Dimension; ly++)
        {
            for (int lx = 0; lx < Chunk.Dimension; lx++)
            {
                int wx = chunk.ChunkX * Chunk.Dimension + lx;
                int wy = chunk.ChunkY * Chunk.Dimension + ly;
                if (chunk.IsMine(lx, ly)) continue;

                chunk.SetContent(lx, ly, GetClueAt(wx, wy));
            }
        }
    }
}
