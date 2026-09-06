using System;
using System.Collections.Generic;
using Minefield.Rendering;

namespace Minefield.Engine;

/// <summary>
/// Controls interactive gameplay mechanics: cell reveals, flagging, chording,
/// abilities (Recon Drone, Blast Shield), particle effects, and audio synthesis.
/// </summary>
public class GameSession
{
    private readonly ChunkManager _chunkManager;
    private readonly PlayerProfile _profile;
    private readonly ParticleSystem? _particles;
    private readonly SynthesizedAudio? _audio;

    public PlayerProfile Profile => _profile;
    public bool IsTargetingDrone { get; set; } = false;

    public event Action<int, int>? OnCellRevealed;       // (worldCellX, worldCellY)
    public event Action<int, int>? OnMineDetonated;      // (worldCellX, worldCellY)
    public event Action<int, int>? OnSectorLocked;       // (chunkX, chunkY)
    public event Action<int, int>? OnFlagToggled;        // (worldCellX, worldCellY)

    public GameSession(ChunkManager chunkManager, PlayerProfile? profile = null, ParticleSystem? particles = null, SynthesizedAudio? audio = null)
    {
        _chunkManager = chunkManager;
        _profile = profile ?? new PlayerProfile();
        _particles = particles;
        _audio = audio;
    }

    /// <summary>
    /// Reveals the cell at the given world coordinate. If empty (0), performs cross-chunk flood fill.
    /// </summary>
    public void RevealCell(int worldCellX, int worldCellY)
    {
        if (IsTargetingDrone)
        {
            ExecuteReconDrone(worldCellX, worldCellY);
            IsTargetingDrone = false;
            return;
        }

        if (!_chunkManager.TryGetCell(worldCellX, worldCellY, out Chunk? chunk, out int lx, out int ly) || chunk == null)
            return;

        if (chunk.IsLocked) return;

        CellState state = chunk.GetState(lx, ly);
        if (state != CellState.Hidden) return; // Cannot reveal flagged or already revealed

        byte content = chunk.GetContent(lx, ly);
        float cellWorldX = (worldCellX + 0.5f) * Camera.CellSize;
        float cellWorldY = (worldCellY + 0.5f) * Camera.CellSize;

        if (content == CellContent.Mine)
        {
            _profile.DeductMineEnergy(out bool shieldAbsorbed);

            if (shieldAbsorbed)
            {
                // Deflected! Flag the mine safely instead of exploding
                chunk.SetState(lx, ly, CellState.Flagged);
                _audio?.PlayShieldDeflect();
                _particles?.EmitRevealSparkles(cellWorldX, cellWorldY);
            }
            else
            {
                chunk.SetState(lx, ly, CellState.Detonated);
                _audio?.PlayDetonation();
                _particles?.EmitExplosion(cellWorldX, cellWorldY);
                OnMineDetonated?.Invoke(worldCellX, worldCellY);
            }
            return;
        }

        // Reveal safe cell
        chunk.SetState(lx, ly, CellState.Revealed);
        _profile.AddSafeCellXP();
        _audio?.PlayClick();
        _particles?.EmitRevealSparkles(cellWorldX, cellWorldY);
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
                                    _profile.AddSafeCellXP();
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
                    HandleSectorLocked(achunk);
                }
            }
        }
        else
        {
            if (chunk.CheckAndLock())
            {
                HandleSectorLocked(chunk);
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
            _audio?.PlayFlag();
            OnFlagToggled?.Invoke(worldCellX, worldCellY);
        }
        else if (state == CellState.Flagged)
        {
            chunk.SetState(lx, ly, CellState.Hidden);
            _audio?.PlayFlag();
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

        if (flaggedCount == clue && hiddenNeighbors.Count > 0)
        {
            foreach (var (hx, hy) in hiddenNeighbors)
            {
                RevealCell(hx, hy);
            }
        }
    }

    /// <summary>
    /// Executes Recon Drone scan on a 3x3 region: reveals safe cells and flags mines without risk.
    /// </summary>
    public void ExecuteReconDrone(int centerWorldCellX, int centerWorldCellY)
    {
        if (_profile.ReconDronesAvailable <= 0) return;

        _profile.ReconDronesAvailable--;
        float scanCenterX = (centerWorldCellX + 0.5f) * Camera.CellSize;
        float scanCenterY = (centerWorldCellY + 0.5f) * Camera.CellSize;

        _audio?.PlayDroneScan();
        _particles?.EmitDroneScan(scanCenterX, scanCenterY);

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int wx = centerWorldCellX + dx;
                int wy = centerWorldCellY + dy;

                if (_chunkManager.TryGetCell(wx, wy, out Chunk? chunk, out int lx, out int ly) && chunk != null)
                {
                    if (chunk.IsLocked) continue;

                    CellState state = chunk.GetState(lx, ly);
                    if (state == CellState.Hidden)
                    {
                        if (chunk.IsMine(lx, ly))
                        {
                            chunk.SetState(lx, ly, CellState.Flagged);
                            OnFlagToggled?.Invoke(wx, wy);
                        }
                        else
                        {
                            RevealCell(wx, wy);
                        }
                    }
                }
            }
        }
    }

    private void HandleSectorLocked(Chunk chunk)
    {
        _profile.AddSectorLockXP();
        _audio?.PlaySectorLock();

        float chunkLeft = chunk.ChunkX * Camera.ChunkSize;
        float chunkTop = chunk.ChunkY * Camera.ChunkSize;
        _particles?.EmitSectorLockBurst(chunkLeft, chunkTop, Camera.ChunkSize);

        OnSectorLocked?.Invoke(chunk.ChunkX, chunk.ChunkY);
    }
}
