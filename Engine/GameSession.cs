using System;
using System.Collections.Generic;

namespace Minefield.Engine;

/// <summary>
/// Controls interactive gameplay mechanics: cell reveals, flagging, chording,
/// cross-chunk flood-fill algorithms, and sector lock evaluation.
/// </summary>
public class GameSession
{
    private readonly ChunkManager _chunkManager;

    public event Action<int, int>? OnCellRevealed;       // (worldCellX, worldCellY)
    public event Action<int, int>? OnMineDetonated;      // (worldCellX, worldCellY)
    public event Action<int, int>? OnSectorLocked;       // (chunkX, chunkY)
    public event Action<int, int>? OnFlagToggled;        // (worldCellX, worldCellY)

    public GameSession(ChunkManager chunkManager)
    {
        _chunkManager = chunkManager;
    }

    /// <summary>
    /// Reveals the cell at the given world coordinate. If empty (0), performs cross-chunk flood fill.
    /// </summary>
    public void RevealCell(int worldCellX, int worldCellY)
    {
        if (!_chunkManager.TryGetCell(worldCellX, worldCellY, out Chunk? chunk, out int lx, out int ly) || chunk == null)
            return;

        if (chunk.IsLocked) return;

        CellState state = chunk.GetState(lx, ly);
        if (state != CellState.Hidden) return; // Cannot reveal flagged or already revealed

        byte content = chunk.GetContent(lx, ly);

        if (content == CellContent.Mine)
        {
            chunk.SetState(lx, ly, CellState.Detonated);
            OnMineDetonated?.Invoke(worldCellX, worldCellY);
            return;
        }

        // Reveal safe cell
        chunk.SetState(lx, ly, CellState.Revealed);
        OnCellRevealed?.Invoke(worldCellX, worldCellY);

        // Flood fill if empty cell (0 adjacent mines)
        if (content == CellContent.Empty)
        {
            var visited = new HashSet<(int, int)> { (worldCellX, worldCellY) };
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue((worldCellX, worldCellY));

            var affectedChunks = new HashSet<Chunk> { chunk };

            while (queue.Count > 0)
            {
                var (curX, curY) = queue.Dequeue();

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = curX + dx;
                        int ny = curY + dy;

                        if (!visited.Add((nx, ny))) continue;

                        if (_chunkManager.TryGetCell(nx, ny, out Chunk? nChunk, out int nlx, out int nly) && nChunk != null)
                        {
                            if (nChunk.IsLocked) continue;

                            CellState nState = nChunk.GetState(nlx, nly);
                            if (nState == CellState.Hidden)
                            {
                                byte nContent = nChunk.GetContent(nlx, nly);
                                if (nContent != CellContent.Mine)
                                {
                                    nChunk.SetState(nlx, nly, CellState.Revealed);
                                    affectedChunks.Add(nChunk);
                                    OnCellRevealed?.Invoke(nx, ny);

                                    if (nContent == CellContent.Empty)
                                    {
                                        queue.Enqueue((nx, ny));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            foreach (var achunk in affectedChunks)
            {
                if (achunk.CheckAndLock())
                {
                    OnSectorLocked?.Invoke(achunk.ChunkX, achunk.ChunkY);
                }
            }
        }
        else
        {
            if (chunk.CheckAndLock())
            {
                OnSectorLocked?.Invoke(chunk.ChunkX, chunk.ChunkY);
            }
        }
    }

    /// <summary>
    /// Toggles flag on an unrevealed cell.
    /// </summary>
    public void ToggleFlag(int worldCellX, int worldCellY)
    {
        if (!_chunkManager.TryGetCell(worldCellX, worldCellY, out Chunk? chunk, out int lx, out int ly) || chunk == null)
            return;

        if (chunk.IsLocked) return;

        CellState state = chunk.GetState(lx, ly);
        if (state == CellState.Hidden)
        {
            chunk.SetState(lx, ly, CellState.Flagged);
            OnFlagToggled?.Invoke(worldCellX, worldCellY);
        }
        else if (state == CellState.Flagged)
        {
            chunk.SetState(lx, ly, CellState.Hidden);
            OnFlagToggled?.Invoke(worldCellX, worldCellY);
        }
    }

    /// <summary>
    /// Chords a revealed cell: if the number of flagged neighbors equals the cell clue number,
    /// reveals all remaining hidden non-flagged neighbors.
    /// </summary>
    public void ChordCell(int worldCellX, int worldCellY)
    {
        if (!_chunkManager.TryGetCell(worldCellX, worldCellY, out Chunk? chunk, out int lx, out int ly) || chunk == null)
            return;

        if (chunk.GetState(lx, ly) != CellState.Revealed) return;

        byte clue = chunk.GetContent(lx, ly);
        if (clue < 1 || clue > 8) return;

        // Count adjacent flags and collect hidden neighbors across chunks
        int flaggedCount = 0;
        var hiddenNeighbors = new List<(int X, int Y)>();

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = worldCellX + dx;
                int ny = worldCellY + dy;

                if (_chunkManager.TryGetCell(nx, ny, out Chunk? nChunk, out int nlx, out int nly) && nChunk != null)
                {
                    CellState state = nChunk.GetState(nlx, nly);
                    if (state == CellState.Flagged) flaggedCount++;
                    else if (state == CellState.Hidden) hiddenNeighbors.Add((nx, ny));
                }
            }
        }

        // If flags match clue, reveal all hidden neighbors
        if (flaggedCount == clue && hiddenNeighbors.Count > 0)
        {
            foreach (var (hx, hy) in hiddenNeighbors)
            {
                RevealCell(hx, hy);
            }
        }
    }
}
