using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Minefield.Data;
using SkiaSharp;

namespace Minefield.Engine;

/// <summary>
/// Manages streaming, SQLite persistence, object pooling, and viewport culling
/// for infinite 16x16 chunk sectors.
/// </summary>
public class ChunkManager : IAsyncDisposable
{
    private readonly string? _dbPath;
    private readonly ChunkPool _pool;
    private readonly ConcurrentDictionary<(int X, int Y), Chunk> _activeChunks = new();
    private readonly ConcurrentDictionary<(int X, int Y), byte> _queuedCoords = new();

    private readonly Channel<(int X, int Y)> _loadChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    private readonly BoardGenerator _boardGenerator = new();

    public ChunkPool Pool => _pool;
    public IReadOnlyDictionary<(int X, int Y), Chunk> ActiveChunks => _activeChunks;

    public ChunkManager(string? dbPath = null, int poolPrewarm = 64)
    {
        _dbPath = dbPath;
        _pool = new ChunkPool(poolPrewarm);

        // Ensure database schema is present
        MinefieldDbContext.InitializeDatabase(_dbPath);

        // Create unbounded queue for chunk streaming
        _loadChannel = Channel.CreateUnbounded<(int X, int Y)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _workerTask = Task.Run(ProcessChunkQueueAsync);
    }

    public BoardGenerator Generator => _boardGenerator;

    /// <summary>
    /// Resets all active chunks and wipes the persistent SQLite chunk table for a brand new expedition.
    /// </summary>
    public async Task ResetAllChunksAsync(int newWorldSeed, int newMineDensityPercent)
    {
        _boardGenerator.WorldSeed = newWorldSeed;
        _boardGenerator.MineDensityPercent = newMineDensityPercent;

        // 1. Recycle all in-memory active chunks
        foreach (var kvp in _activeChunks)
        {
            _pool.Return(kvp.Value);
        }
        _activeChunks.Clear();
        _queuedCoords.Clear();

        // 2. Wipe SQLite chunk records
        await _dbLock.WaitAsync();
        try
        {
            using var db = new MinefieldDbContext(_dbPath);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Chunks");
        }
        catch { }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Evaluates the current camera viewport and schedules required chunks for streaming
    /// while culling distant sectors.
    /// </summary>
    public void UpdateViewport(Camera camera, float viewportWidth, float viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        SKRect worldBounds = camera.GetVisibleWorldBounds(viewportWidth, viewportHeight);

        // Include 1 chunk buffer margin around the viewport for seamless panning
        int minChunkX = (int)Math.Floor(worldBounds.Left / Camera.ChunkSize) - 1;
        int maxChunkX = (int)Math.Ceiling(worldBounds.Right / Camera.ChunkSize) + 1;
        int minChunkY = (int)Math.Floor(worldBounds.Top / Camera.ChunkSize) - 1;
        int maxChunkY = (int)Math.Ceiling(worldBounds.Bottom / Camera.ChunkSize) + 1;

        // 1. Enqueue chunks entering viewport
        for (int cx = minChunkX; cx <= maxChunkX; cx++)
        {
            for (int cy = minChunkY; cy <= maxChunkY; cy++)
            {
                var key = (cx, cy);
                if (!_activeChunks.ContainsKey(key) && _queuedCoords.TryAdd(key, 1))
                {
                    _loadChannel.Writer.TryWrite(key);
                }
            }
        }

        // 2. Cull distant chunks (unload margin: 4 chunks away from bounds)
        int cullDistance = 4;
        List<(int X, int Y)> toUnload = new();

        foreach (var kvp in _activeChunks)
        {
            var (cx, cy) = kvp.Key;
            if (cx < minChunkX - cullDistance || cx > maxChunkX + cullDistance ||
                cy < minChunkY - cullDistance || cy > maxChunkY + cullDistance)
            {
                toUnload.Add(kvp.Key);
            }
        }

        if (toUnload.Count > 0)
        {
            // Run unload in background to avoid hitching the UI thread
            Task.Run(() => UnloadChunks(toUnload));
        }
    }

    private async Task ProcessChunkQueueAsync()
    {
        var reader = _loadChannel.Reader;

        while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
        {
            while (reader.TryRead(out var coord))
            {
                if (_cts.IsCancellationRequested) break;

                _queuedCoords.TryRemove(coord, out _);

                if (_activeChunks.ContainsKey(coord)) continue;

                await LoadOrCreateChunkAsync(coord.X, coord.Y).ConfigureAwait(false);
            }
        }
    }

    private async Task LoadOrCreateChunkAsync(int cx, int cy)
    {
        ChunkEntity? entity = null;

        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var db = new MinefieldDbContext(_dbPath);
            entity = await db.Chunks.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ChunkX == cx && c.ChunkY == cy)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbLock.Release();
        }

        Chunk chunk = _pool.Rent(cx, cy);

        if (entity != null)
        {
            chunk.IsLocked = entity.IsLocked;
            chunk.Deserialize(entity.Data);
            chunk.IsModified = false;

            // Self-healing integrity check: verify loaded chunk matches the pure deterministic generator
            bool isCorruptLegacy = false;
            for (int ly = 0; ly < Chunk.Dimension; ly++)
            {
                int wy = chunk.WorldOriginY + ly;
                for (int lx = 0; lx < Chunk.Dimension; lx++)
                {
                    int wx = chunk.WorldOriginX + lx;
                    bool expectedMine = _boardGenerator.IsMineAt(wx, wy);
                    if (chunk.IsMine(lx, ly) != expectedMine)
                    {
                        isCorruptLegacy = true;
                        break;
                    }

                    if (!expectedMine && chunk.GetContent(lx, ly) != _boardGenerator.GetClueAt(wx, wy))
                    {
                        isCorruptLegacy = true;
                        break;
                    }
                }
                if (isCorruptLegacy) break;
            }

            if (isCorruptLegacy)
            {
                // Obsolete chunk detected from older generator version - regenerate with guaranteed exact clues
                _boardGenerator.GenerateChunk(chunk);
                chunk.IsModified = true;

                // Update database
                await _dbLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    using var db = new MinefieldDbContext(_dbPath);
                    var existing = await db.Chunks.FindAsync(cx, cy).ConfigureAwait(false);
                    if (existing != null)
                    {
                        existing.Data = chunk.Serialize();
                        existing.IsLocked = chunk.IsLocked;
                        existing.LastModified = DateTime.UtcNow;
                        await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                }
                catch { }
                finally
                {
                    _dbLock.Release();
                }
            }
        }
        else
        {
            // Generate mines and clues using deterministic solver and mutation loop
            _boardGenerator.GenerateChunk(chunk, (nx, ny) =>
                _activeChunks.TryGetValue((nx, ny), out Chunk? neighbor) ? neighbor : null);

            // Persist new generated chunk in SQLite
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using var db = new MinefieldDbContext(_dbPath);
                db.Chunks.Add(new ChunkEntity
                {
                    ChunkX = cx,
                    ChunkY = cy,
                    IsLocked = chunk.IsLocked,
                    Data = chunk.Serialize(),
                    LastModified = DateTime.UtcNow
                });
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }

            chunk.IsModified = false;
        }

        _activeChunks[(cx, cy)] = chunk;
    }

    private void UnloadChunks(List<(int X, int Y)> toUnload)
    {
        List<ChunkEntity> toSave = new();

        foreach (var key in toUnload)
        {
            if (_activeChunks.TryRemove(key, out Chunk? chunk))
            {
                if (chunk.IsModified)
                {
                    toSave.Add(new ChunkEntity
                    {
                        ChunkX = chunk.ChunkX,
                        ChunkY = chunk.ChunkY,
                        IsLocked = chunk.IsLocked,
                        Data = chunk.Serialize(),
                        LastModified = DateTime.UtcNow
                    });
                }

                _pool.Return(chunk);
            }
        }

        if (toSave.Count > 0)
        {
            _dbLock.Wait();
            try
            {
                using var db = new MinefieldDbContext(_dbPath);
                foreach (var entity in toSave)
                {
                    var existing = db.Chunks.Find(entity.ChunkX, entity.ChunkY);
                    if (existing != null)
                    {
                        existing.IsLocked = entity.IsLocked;
                        existing.Data = entity.Data;
                        existing.LastModified = entity.LastModified;
                    }
                    else
                    {
                        db.Chunks.Add(entity);
                    }
                }
                db.SaveChanges();
            }
            finally
            {
                _dbLock.Release();
            }
        }
    }

    /// <summary>
    /// Persists all currently active chunks that have unsaved modifications to SQLite.
    /// </summary>
    public async Task FlushActiveChunksAsync()
    {
        List<ChunkEntity> modified = new();

        foreach (var chunk in _activeChunks.Values)
        {
            if (chunk.IsModified)
            {
                modified.Add(new ChunkEntity
                {
                    ChunkX = chunk.ChunkX,
                    ChunkY = chunk.ChunkY,
                    IsLocked = chunk.IsLocked,
                    Data = chunk.Serialize(),
                    LastModified = DateTime.UtcNow
                });
                chunk.IsModified = false;
            }
        }

        if (modified.Count > 0)
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using var db = new MinefieldDbContext(_dbPath);
                foreach (var entity in modified)
                {
                    var existing = await db.Chunks.FindAsync(entity.ChunkX, entity.ChunkY).ConfigureAwait(false);
                    if (existing != null)
                    {
                        existing.IsLocked = entity.IsLocked;
                        existing.Data = entity.Data;
                        existing.LastModified = entity.LastModified;
                    }
                    else
                    {
                        db.Chunks.Add(entity);
                    }
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }
        }
    }

    /// <summary>
    /// Retrieves the chunk containing the specified cell coordinates, or null if not loaded.
    /// </summary>
    public bool TryGetCell(int worldCellX, int worldCellY, out Chunk? chunk, out int localX, out int localY)
    {
        var (cx, cy, lx, ly) = Camera.CellToChunkAndLocal(worldCellX, worldCellY);
        localX = lx;
        localY = ly;
        return _activeChunks.TryGetValue((cx, cy), out chunk);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _loadChannel.Writer.Complete();

        try
        {
            await _workerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        await FlushActiveChunksAsync().ConfigureAwait(false);

        _cts.Dispose();
        _dbLock.Dispose();
    }
}
