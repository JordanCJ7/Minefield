using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Minefield.Data;

/// <summary>
/// Persistent database entity for a 16x16 sector chunk.
/// </summary>
[Table("Chunks")]
public class ChunkEntity
{
    public int ChunkX { get; set; }

    public int ChunkY { get; set; }

    public bool IsLocked { get; set; }

    /// <summary>
    /// Serialized 256-byte array of the 16x16 tiles.
    /// </summary>
    public byte[] Data { get; set; } = new byte[256];

    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
