using System.Collections.Concurrent;

namespace Minefield.Engine;

/// <summary>
/// Thread-safe object pool for Chunk instances to eliminate Garbage Collection allocations
/// during continuous infinite panning and chunk streaming.
/// </summary>
public class ChunkPool
{
    private readonly ConcurrentQueue<Chunk> _pool = new();
    private int _createdCount = 0;

    public int AvailableCount => _pool.Count;
    public int TotalCreated => _createdCount;

    public ChunkPool(int initialPrewarm = 32)
    {
        for (int i = 0; i < initialPrewarm; i++)
        {
            _pool.Enqueue(new Chunk());
            _createdCount++;
        }
    }

    /// <summary>
    /// Rents a recycled chunk or instantiates a new one if the pool is empty.
    /// </summary>
    public Chunk Rent(int chunkX, int chunkY)
    {
        if (!_pool.TryDequeue(out Chunk? chunk))
        {
            chunk = new Chunk();
            System.Threading.Interlocked.Increment(ref _createdCount);
        }

        chunk.Reset();
        chunk.ChunkX = chunkX;
        chunk.ChunkY = chunkY;
        return chunk;
    }

    /// <summary>
    /// Returns a chunk to the pool after clearing its state.
    /// </summary>
    public void Return(Chunk chunk)
    {
        if (chunk == null) return;
        chunk.Reset();
        _pool.Enqueue(chunk);
    }
}
