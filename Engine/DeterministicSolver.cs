using System;
using System.Collections.Generic;
using System.Linq;

namespace Minefield.Engine;

/// <summary>
/// 3-Tier Logical Minesweeper Deduction Engine:
/// Tier 1: Direct Basic Flags & Direct Clears
/// Tier 2: Subset Deduction (1-2 and Overlap Logic)
/// Tier 3: Proof by Contradiction / Backtracking Deduction
/// Guarantees 100% deterministic solvability with zero 50/50 guesses,
/// accounting for cross-sector boundary mines.
/// </summary>
public class DeterministicSolver
{
    private enum SolverCellState : byte
    {
        Unknown = 0,
        SafeRevealed = 1,
        FlaggedMine = 2
    }

    /// <summary>
    /// Evaluates if the chunk can be 100% solved from the provided entry points
    /// without requiring a guess.
    /// </summary>
    /// <param name="chunk">The chunk containing mines and clues.</param>
    /// <param name="startingSafeCells">Known starting safe cells.</param>
    /// <param name="unsolvedCount">Output: number of cells that could not be logically deduced.</param>
    /// <param name="isWorldMine">Optional ground truth predicate to query external boundary mines.</param>
    /// <returns>True if 100% solved with zero ambiguity; false if a forced guess was encountered.</returns>
    public bool TrySolveChunk(Chunk chunk, IEnumerable<(int X, int Y)> startingSafeCells, out int unsolvedCount, Func<int, int, bool>? isWorldMine = null)
    {
        const int W = Chunk.Dimension;
        const int H = Chunk.Dimension;

        var states = new SolverCellState[W, H];
        int totalMines = chunk.CountMines();
        int totalSafe = Chunk.TotalCells - totalMines;

        // Queue of revealed clues that need processing
        var cluesToProcess = new HashSet<(int X, int Y)>();

        // Initialize starting safe cells
        int safeRevealedCount = 0;
        int flaggedMineCount = 0;

        foreach (var (sx, sy) in startingSafeCells)
        {
            if (sx >= 0 && sx < W && sy >= 0 && sy < H)
            {
                if (states[sx, sy] != SolverCellState.SafeRevealed && !chunk.IsMine(sx, sy))
                {
                    states[sx, sy] = SolverCellState.SafeRevealed;
                    safeRevealedCount++;
                    cluesToProcess.Add((sx, sy));

                    // If starting cell is 0 (Empty), flood-reveal adjacent safe cells
                    if (chunk.GetContent(sx, sy) == CellContent.Empty)
                    {
                        Queue<(int X, int Y)> flood = new();
                        flood.Enqueue((sx, sy));

                        while (flood.Count > 0)
                        {
                            var (fx, fy) = flood.Dequeue();
                            foreach (var (nx, ny) in GetNeighbors(fx, fy))
                            {
                                if (states[nx, ny] == SolverCellState.Unknown && !chunk.IsMine(nx, ny))
                                {
                                    states[nx, ny] = SolverCellState.SafeRevealed;
                                    safeRevealedCount++;
                                    cluesToProcess.Add((nx, ny));
                                    if (chunk.GetContent(nx, ny) == CellContent.Empty)
                                    {
                                        flood.Enqueue((nx, ny));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Main Deduction Loop
        bool progress = true;
        while (progress && safeRevealedCount < totalSafe)
        {
            progress = false;

            // -----------------------------------------------------------------
            // TIER 1: Direct Basic Deductions (Flags and Clears)
            // -----------------------------------------------------------------
            bool tier1Progress = true;
            while (tier1Progress)
            {
                tier1Progress = false;
                var cluesSnapshot = cluesToProcess.ToList();

                foreach (var (cx, cy) in cluesSnapshot)
                {
                    byte clue = chunk.GetContent(cx, cy);
                    GetNeighborInfo(chunk, cx, cy, states, isWorldMine, out int flaggedNeighbors, out var unknownNeighbors);

                    if (unknownNeighbors.Count == 0)
                    {
                        cluesToProcess.Remove((cx, cy));
                        continue;
                    }

                    // Direct Flags: remaining unknown cells == needed mines
                    if (clue - flaggedNeighbors == unknownNeighbors.Count && unknownNeighbors.Count > 0)
                    {
                        foreach (var (ux, uy) in unknownNeighbors)
                        {
                            states[ux, uy] = SolverCellState.FlaggedMine;
                            flaggedMineCount++;
                        }
                        tier1Progress = true;
                        progress = true;
                        continue;
                    }

                    // Direct Clears: clue satisfied, remaining unknown cells are all safe
                    if (clue == flaggedNeighbors && unknownNeighbors.Count > 0)
                    {
                        foreach (var (ux, uy) in unknownNeighbors)
                        {
                            states[ux, uy] = SolverCellState.SafeRevealed;
                            safeRevealedCount++;
                            cluesToProcess.Add((ux, uy));
                        }
                        tier1Progress = true;
                        progress = true;
                    }
                }
            }

            if (safeRevealedCount >= totalSafe) break;

            // -----------------------------------------------------------------
            // TIER 2: Subset Overlap Logic (e.g. 1-2 patterns and subset deduction)
            // -----------------------------------------------------------------
            var activeClues = cluesToProcess.ToList();
            for (int i = 0; i < activeClues.Count; i++)
            {
                var (ax, ay) = activeClues[i];
                byte clueA = chunk.GetContent(ax, ay);
                GetNeighborInfo(chunk, ax, ay, states, isWorldMine, out int flaggedA, out var unknownsListA);

                if (unknownsListA.Count == 0) continue;
                int remainingA = clueA - flaggedA;
                var unknownsA = new HashSet<(int X, int Y)>(unknownsListA);

                for (int j = 0; j < activeClues.Count; j++)
                {
                    if (i == j) continue;
                    var (bx, by) = activeClues[j];
                    byte clueB = chunk.GetContent(bx, by);
                    GetNeighborInfo(chunk, bx, by, states, isWorldMine, out int flaggedB, out var unknownsListB);

                    if (unknownsListB.Count == 0) continue;
                    int remainingB = clueB - flaggedB;
                    var unknownsB = new HashSet<(int X, int Y)>(unknownsListB);

                    // Check if unknownsA is a strict subset of unknownsB
                    if (unknownsA.Count < unknownsB.Count && unknownsA.IsSubsetOf(unknownsB))
                    {
                        var diff = unknownsB.Except(unknownsA).ToList();
                        int remainingDiff = remainingB - remainingA;

                        // All cells in difference must be safe
                        if (remainingDiff == 0 && diff.Count > 0)
                        {
                            foreach (var (dx, dy) in diff)
                            {
                                states[dx, dy] = SolverCellState.SafeRevealed;
                                safeRevealedCount++;
                                cluesToProcess.Add((dx, dy));
                            }
                            progress = true;
                        }
                        // All cells in difference must be mines
                        else if (remainingDiff == diff.Count && diff.Count > 0)
                        {
                            foreach (var (dx, dy) in diff)
                            {
                                states[dx, dy] = SolverCellState.FlaggedMine;
                                flaggedMineCount++;
                            }
                            progress = true;
                        }
                    }
                }
            }

            if (progress || safeRevealedCount >= totalSafe) continue;

            // -----------------------------------------------------------------
            // TIER 3: Proof by Contradiction / Backtracking Analysis
            // -----------------------------------------------------------------
            var frontier = new HashSet<(int X, int Y)>();
            foreach (var (cx, cy) in cluesToProcess)
            {
                foreach (var (nx, ny) in GetNeighbors(cx, cy))
                {
                    if (states[nx, ny] == SolverCellState.Unknown)
                        frontier.Add((nx, ny));
                }
            }

            foreach (var (fx, fy) in frontier)
            {
                // Hypothesis 1: Assume (fx, fy) is a Mine
                if (LeadsToContradiction(chunk, states, cluesToProcess, fx, fy, SolverCellState.FlaggedMine, isWorldMine))
                {
                    // Must be Safe!
                    states[fx, fy] = SolverCellState.SafeRevealed;
                    safeRevealedCount++;
                    cluesToProcess.Add((fx, fy));
                    progress = true;
                    break;
                }

                // Hypothesis 2: Assume (fx, fy) is Safe
                if (LeadsToContradiction(chunk, states, cluesToProcess, fx, fy, SolverCellState.SafeRevealed, isWorldMine))
                {
                    // Must be a Mine!
                    states[fx, fy] = SolverCellState.FlaggedMine;
                    flaggedMineCount++;
                    progress = true;
                    break;
                }
            }
        }

        unsolvedCount = totalSafe - safeRevealedCount;
        return unsolvedCount == 0;
    }

    private static void GetNeighborInfo(
        Chunk chunk,
        int cx,
        int cy,
        SolverCellState[,] states,
        Func<int, int, bool>? isWorldMine,
        out int flagged,
        out List<(int X, int Y)> unknowns)
    {
        flagged = 0;
        unknowns = new List<(int X, int Y)>(8);

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx;
                int ny = cy + dy;

                if (nx >= 0 && nx < Chunk.Dimension && ny >= 0 && ny < Chunk.Dimension)
                {
                    if (states[nx, ny] == SolverCellState.FlaggedMine)
                        flagged++;
                    else if (states[nx, ny] == SolverCellState.Unknown)
                        unknowns.Add((nx, ny));
                }
                else if (isWorldMine != null)
                {
                    int wx = chunk.ChunkX * Chunk.Dimension + nx;
                    int wy = chunk.ChunkY * Chunk.Dimension + ny;
                    if (isWorldMine(wx, wy))
                        flagged++;
                }
            }
        }
    }

    private bool LeadsToContradiction(Chunk chunk, SolverCellState[,] currentStates, HashSet<(int X, int Y)> clues, int testX, int testY, SolverCellState testState, Func<int, int, bool>? isWorldMine)
    {
        var simStates = (SolverCellState[,])currentStates.Clone();
        simStates[testX, testY] = testState;

        var simClues = new Queue<(int X, int Y)>(clues);

        int steps = 0;
        while (simClues.Count > 0 && steps < 64)
        {
            steps++;
            var (cx, cy) = simClues.Dequeue();
            byte clue = chunk.GetContent(cx, cy);

            GetNeighborInfo(chunk, cx, cy, simStates, isWorldMine, out int flagged, out var unknowns);

            // Contradiction 1: Too many mines
            if (flagged > clue) return true;

            // Contradiction 2: Not enough cells to satisfy clue
            if (flagged + unknowns.Count < clue) return true;

            if (unknowns.Count == 0) continue;

            if (clue - flagged == unknowns.Count)
            {
                foreach (var (ux, uy) in unknowns)
                    simStates[ux, uy] = SolverCellState.FlaggedMine;
            }
            else if (clue == flagged)
            {
                foreach (var (ux, uy) in unknowns)
                {
                    simStates[ux, uy] = SolverCellState.SafeRevealed;
                    simClues.Enqueue((ux, uy));
                }
            }
        }

        return false;
    }

    public static List<(int X, int Y)> GetNeighbors(int x, int y)
    {
        var list = new List<(int X, int Y)>(8);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < Chunk.Dimension && ny >= 0 && ny < Chunk.Dimension)
                {
                    list.Add((nx, ny));
                }
            }
        }
        return list;
    }
}
