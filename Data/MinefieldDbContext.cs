using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace Minefield.Data;

/// <summary>
/// EF Core SQLite Database Context for persistent chunks and player profile.
/// </summary>
public class MinefieldDbContext : DbContext
{
    public DbSet<ChunkEntity> Chunks => Set<ChunkEntity>();
    public DbSet<PlayerProfileEntity> PlayerProfiles => Set<PlayerProfileEntity>();

    private readonly string _dbPath;

    public MinefieldDbContext(string? customDbPath = null)
    {
        if (!string.IsNullOrEmpty(customDbPath))
        {
            _dbPath = customDbPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "Minefield");
            Directory.CreateDirectory(folder);
            _dbPath = Path.Combine(folder, "minefield.db");
        }
    }

    public MinefieldDbContext(DbContextOptions<MinefieldDbContext> options)
        : base(options)
    {
        _dbPath = "";
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Composite Primary Key for Chunks (ChunkX, ChunkY)
        modelBuilder.Entity<ChunkEntity>()
            .HasKey(c => new { c.ChunkX, c.ChunkY });

        // Index on LastModified for LRU or cleanup queries
        modelBuilder.Entity<ChunkEntity>()
            .HasIndex(c => c.LastModified);
    }

    /// <summary>
    /// Ensures database schema is created and initialized.
    /// </summary>
    public static void InitializeDatabase(string? customDbPath = null)
    {
        using var context = new MinefieldDbContext(customDbPath);
        context.Database.EnsureCreated();

        // Ensure default player profile exists
        if (!context.PlayerProfiles.Any(p => p.Id == 1))
        {
            context.PlayerProfiles.Add(new PlayerProfileEntity
            {
                Id = 1,
                Level = 1,
                CurrentXP = 0,
                MaxEnergy = 100,
                CurrentEnergy = 100,
                UnlockedAbilitiesJson = "[\"ReconDrone\",\"BlastShield\"]",
                LastPlayed = DateTime.UtcNow
            });
            context.SaveChanges();
        }
    }
}
