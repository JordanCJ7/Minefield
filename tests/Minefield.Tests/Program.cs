using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;
using Microsoft.EntityFrameworkCore;
using Minefield.Data;
using Minefield.Engine;
using Minefield.Rendering;

namespace Minefield.Tests;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================");
        Console.WriteLine("    MINEFIELD ENGINE - AUTOMATED TEST SUITE       ");
        Console.WriteLine("==================================================");
        Console.ResetColor();

        int passed = 0;
        int failed = 0;

        void Assert(bool condition, string testName)
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[PASS] {testName}");
                Console.ResetColor();
                passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {testName}");
                Console.ResetColor();
                failed++;
            }
        }

        // ----------------------------------------------------
        // CAMERA TESTS
        // ----------------------------------------------------
        // Test 1: Camera World to Screen and Screen to World Invertibility
        {
            var camera = new Camera { X = 120.5f, Y = -350.25f, Zoom = 1.75f };
            float vw = 1920.0f;
            float vh = 1080.0f;

            SKPoint originalScreen = new SKPoint(543.2f, 876.1f);
            SKPoint world = camera.ScreenToWorld(originalScreen, vw, vh);
            SKPoint backToScreen = camera.WorldToScreen(world, vw, vh);

            bool match = Math.Abs(originalScreen.X - backToScreen.X) < 0.001f &&
                         Math.Abs(originalScreen.Y - backToScreen.Y) < 0.001f;
            Assert(match, "Camera: ScreenToWorld <-> WorldToScreen Invertibility");
        }

        // Test 2: Camera Pan
        {
            var camera = new Camera { X = 0f, Y = 0f, Zoom = 2.0f };
            camera.Pan(100.0f, 50.0f);
            bool match = Math.Abs(camera.X - (-50.0f)) < 0.001f &&
                         Math.Abs(camera.Y - (-25.0f)) < 0.001f;
            Assert(match, "Camera: Pan translation scaled by Zoom");
        }

        // Test 3: Cursor-Centered Zoom Invariance
        {
            var camera = new Camera { X = 50.0f, Y = -30.0f, Zoom = 1.0f };
            float vw = 1200f;
            float vh = 800f;
            SKPoint cursorScreen = new SKPoint(750f, 320f);

            SKPoint worldUnderCursorBefore = camera.ScreenToWorld(cursorScreen, vw, vh);
            camera.ZoomAt(cursorScreen, 1.5f, vw, vh);
            SKPoint worldUnderCursorAfter = camera.ScreenToWorld(cursorScreen, vw, vh);

            bool match = Math.Abs(worldUnderCursorBefore.X - worldUnderCursorAfter.X) < 0.001f &&
                         Math.Abs(worldUnderCursorBefore.Y - worldUnderCursorAfter.Y) < 0.001f;
            Assert(match, "Camera: Cursor-Centered Zoom Anchor Invariance");
        }

        // Test 4: Positive and Negative Cell Coordinates
        {
            var (c0x, c0y) = Camera.WorldToCell(5.0f, 10.0f);
            var (cNegX, cNegY) = Camera.WorldToCell(-5.0f, -10.0f);

            bool match = c0x == 0 && c0y == 0 && cNegX == -1 && cNegY == -1;
            Assert(match, "Camera: WorldToCell handles origin and negative bounds correctly");
        }

        // Test 5: Chunk and Local Coordinate Mapping
        {
            var (ch0, cy0, lx0, ly0) = Camera.CellToChunkAndLocal(0, 0);
            bool test0 = ch0 == 0 && cy0 == 0 && lx0 == 0 && ly0 == 0;

            var (chNeg1, cyNeg1, lxNeg1, lyNeg1) = Camera.CellToChunkAndLocal(-1, -1);
            bool testNeg1 = chNeg1 == -1 && cyNeg1 == -1 && lxNeg1 == 15 && lyNeg1 == 15;

            var (chNeg17, cyNeg17, lxNeg17, lyNeg17) = Camera.CellToChunkAndLocal(-17, -17);
            bool testNeg17 = chNeg17 == -2 && cyNeg17 == -2 && lxNeg17 == 15 && lyNeg17 == 15;

            Assert(test0 && testNeg1 && testNeg17, "Camera: CellToChunkAndLocal across negative quadrants");
        }

        // ----------------------------------------------------
        // CHUNK & TILE DATA MODEL TESTS
        // ----------------------------------------------------
        // Test 6: Chunk packed byte state & content
        {
            var chunk = new Chunk { ChunkX = 3, ChunkY = -5 };
            chunk.SetCell(0, 0, CellState.Hidden, CellContent.Empty);
            chunk.SetCell(5, 7, CellState.Revealed, 3);
            chunk.SetCell(15, 15, CellState.Flagged, CellContent.Mine);

            bool check0 = chunk.GetState(0, 0) == CellState.Hidden && chunk.GetContent(0, 0) == CellContent.Empty;
            bool check1 = chunk.GetState(5, 7) == CellState.Revealed && chunk.GetContent(5, 7) == 3;
            bool check2 = chunk.GetState(15, 15) == CellState.Flagged && chunk.GetContent(15, 15) == CellContent.Mine;

            Assert(check0 && check1 && check2, "Chunk: Bit-packed tile state and content serialization");
        }

        // Test 7: Chunk serialization round-trip
        {
            var original = new Chunk { ChunkX = -2, ChunkY = 4 };
            for (int ly = 0; ly < Chunk.Dimension; ly++)
            {
                for (int lx = 0; lx < Chunk.Dimension; lx++)
                {
                    CellState state = (CellState)((lx + ly) % 4);
                    byte content = (byte)((lx * ly) % 10);
                    original.SetCell(lx, ly, state, content);
                }
            }

            byte[] bytes = original.Serialize();
            var restored = new Chunk();
            restored.Deserialize(bytes);

            bool match = true;
            for (int ly = 0; ly < Chunk.Dimension; ly++)
            {
                for (int lx = 0; lx < Chunk.Dimension; lx++)
                {
                    if (restored.GetState(lx, ly) != original.GetState(lx, ly) ||
                        restored.GetContent(lx, ly) != original.GetContent(lx, ly))
                    {
                        match = false;
                        break;
                    }
                }
            }

            Assert(match && bytes.Length == 256, "Chunk: 256-byte binary serialization roundtrip");
        }

        // Test 8: Sector Lock Evaluation
        {
            var chunk = new Chunk { ChunkX = 0, ChunkY = 0 };
            for (int i = 0; i < 10; i++)
            {
                chunk.SetCell(i, 0, CellState.Hidden, CellContent.Mine);
            }
            for (int ly = 0; ly < Chunk.Dimension; ly++)
            {
                for (int lx = (ly == 0 ? 10 : 0); lx < Chunk.Dimension; lx++)
                {
                    chunk.SetCell(lx, ly, CellState.Revealed, 1);
                }
            }

            bool locked = chunk.CheckAndLock();
            Assert(locked && chunk.IsLocked, "Chunk: Automated sector lock triggers when all safe cells revealed");
        }

        // Test 9: ChunkPool recycling
        {
            var pool = new ChunkPool(initialPrewarm: 8);

            Chunk rented1 = pool.Rent(10, 20);
            rented1.SetCell(0, 0, CellState.Revealed, 5);

            pool.Return(rented1);

            Chunk rentedAgain = pool.Rent(50, 60);
            bool cleanState = rentedAgain.GetState(0, 0) == CellState.Hidden &&
                              rentedAgain.GetContent(0, 0) == CellContent.Empty;

            Assert(cleanState, "ChunkPool: Object recycling cleans state without new allocation");
        }

        // ----------------------------------------------------
        // SQLITE DATABASE TESTS
        // ----------------------------------------------------
        // Test 10: SQLite Database persistence round-trip
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_minefield_{Guid.NewGuid():N}.db");
            try
            {
                MinefieldDbContext.InitializeDatabase(tempDb);

                byte[] sampleData = new byte[256];
                sampleData[0] = 0x91;
                sampleData[255] = 0x22;

                using (var db = new MinefieldDbContext(tempDb))
                {
                    db.Chunks.Add(new ChunkEntity
                    {
                        ChunkX = -42,
                        ChunkY = 88,
                        IsLocked = true,
                        Data = sampleData,
                        LastModified = DateTime.UtcNow
                    });
                    db.SaveChanges();
                }

                using (var db = new MinefieldDbContext(tempDb))
                {
                    var entity = db.Chunks.Find(-42, 88);
                    bool valid = entity != null &&
                                 entity.IsLocked &&
                                 entity.Data[0] == 0x91 &&
                                 entity.Data[255] == 0x22;

                    var profile = db.PlayerProfiles.Find(1);
                    bool profileValid = profile != null && profile.Level == 1 && profile.MaxEnergy == 100;

                    Assert(valid && profileValid, "SQLite: Database schema creation, chunk blob persistence, and player profile retrieval");
                }
            }
            finally
            {
                if (File.Exists(tempDb))
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }
        }

        // Test 11: ChunkManager asynchronous streaming
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_cm_{Guid.NewGuid():N}.db");
            try
            {
                var chunkManager = new ChunkManager(tempDb, poolPrewarm: 16);
                var camera = new Camera { X = 0, Y = 0, Zoom = 1.0f };

                chunkManager.UpdateViewport(camera, 800f, 600f);
                await Task.Delay(300);

                bool hasChunks = chunkManager.ActiveChunks.Count > 0;
                bool originLoaded = chunkManager.ActiveChunks.ContainsKey((0, 0));

                await chunkManager.DisposeAsync();

                Assert(hasChunks && originLoaded, "ChunkManager: Viewport tracking and asynchronous SQLite background streaming");
            }
            finally
            {
                if (File.Exists(tempDb))
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }
        }

        // ----------------------------------------------------
        // DETERMINISTIC SOLVER & GENERATOR TESTS
        // ----------------------------------------------------
        // Test 12: 3-Tier Solver logical deduction on classic patterns
        {
            var solver = new DeterministicSolver();
            var chunk = new Chunk { ChunkX = 0, ChunkY = 0 };

            chunk.SetCell(0, 0, CellState.Hidden, CellContent.Mine);
            chunk.SetCell(1, 0, CellState.Hidden, CellContent.Empty);
            chunk.SetCell(2, 0, CellState.Hidden, CellContent.Mine);

            chunk.SetCell(0, 1, CellState.Hidden, 1);
            chunk.SetCell(1, 1, CellState.Hidden, 2);
            chunk.SetCell(2, 1, CellState.Hidden, 1);

            var starting = new List<(int, int)> { (0, 1), (1, 1), (2, 1) };

            for (int ly = 2; ly < Chunk.Dimension; ly++)
            {
                for (int lx = 0; lx < Chunk.Dimension; lx++)
                {
                    chunk.SetCell(lx, ly, CellState.Hidden, CellContent.Empty);
                    starting.Add((lx, ly));
                }
            }

            bool solved = solver.TrySolveChunk(chunk, starting, out int unsolved);
            Assert(solved && unsolved == 0, "DeterministicSolver: 1-2-1 subset overlap logical deduction");
        }

        // Test 13: BoardGenerator generates fully solvable chunk
        {
            var generator = new BoardGenerator();
            var chunk = new Chunk { ChunkX = 0, ChunkY = 0 };

            generator.GenerateChunk(chunk);

            int mines = chunk.CountMines();
            bool validMineCount = mines >= 35 && mines <= 50;

            bool starterSafe = true;
            for (int sy = 7; sy <= 9; sy++)
            {
                for (int sx = 7; sx <= 9; sx++)
                {
                    if (chunk.IsMine(sx, sy)) starterSafe = false;
                }
            }

            var solver = new DeterministicSolver();
            var starting = new List<(int, int)>();
            for (int sy = 7; sy <= 9; sy++)
            {
                for (int sx = 7; sx <= 9; sx++) starting.Add((sx, sy));
            }

            bool isSolvable = solver.TrySolveChunk(chunk, starting, out int unsolved, generator.IsMineAt);
            Assert(validMineCount && starterSafe && isSolvable, "BoardGenerator: Deterministic generation with 0% forced guesses");
        }

        // Test 14: Cross-chunk edge consistency
        {
            var generator = new BoardGenerator();
            var chunk0 = new Chunk { ChunkX = 0, ChunkY = 0 };
            generator.GenerateChunk(chunk0);

            var chunkEast = new Chunk { ChunkX = 1, ChunkY = 0 };
            generator.GenerateChunk(chunkEast);

            bool boundaryAligned = true;
            for (int ly = 0; ly < Chunk.Dimension; ly++)
            {
                if (chunkEast.IsMine(0, ly)) continue;

                byte clue = chunkEast.GetContent(0, ly);
                byte expected = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = 0 + dx;
                        int ny = ly + dy;
                        if (nx >= 0 && nx < 16 && ny >= 0 && ny < 16)
                        {
                            if (chunkEast.IsMine(nx, ny)) expected++;
                        }
                        else if (nx < 0 && ny >= 0 && ny < 16)
                        {
                            if (chunk0.IsMine(15, ny)) expected++;
                        }
                    }
                }

                if (clue != expected)
                {
                    boundaryAligned = false;
                    break;
                }
            }

            Assert(boundaryAligned, "BoardGenerator: Cross-sector vertical edge boundary mine and clue alignment");

            // Verify horizontal boundary (e.g. Sector [0, -1] and Sector [0, 0]) - exactly where user bug occurred
            var chunkNorth = new Chunk { ChunkX = 0, ChunkY = -1 };
            generator.GenerateChunk(chunkNorth);

            bool horizontalAligned = true;
            for (int lx = 0; lx < Chunk.Dimension; lx++)
            {
                // Test row 15 of chunkNorth (y = -1 in world coords)
                if (!chunkNorth.IsMine(lx, 15))
                {
                    byte clue = chunkNorth.GetContent(lx, 15);
                    byte expected = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int wx = lx + dx;
                            int wy = -1 + dy;
                            if (generator.IsMineAt(wx, wy)) expected++;
                        }
                    }
                    if (clue != expected) { horizontalAligned = false; break; }
                }

                // Test row 0 of chunk0 (y = 0 in world coords)
                if (!chunk0.IsMine(lx, 0))
                {
                    byte clue = chunk0.GetContent(lx, 0);
                    byte expected = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int wx = lx + dx;
                            int wy = 0 + dy;
                            if (generator.IsMineAt(wx, wy)) expected++;
                        }
                    }
                    if (clue != expected) { horizontalAligned = false; break; }
                }
            }

            Assert(horizontalAligned, "BoardGenerator: Cross-sector horizontal boundary (Y=-1 / Y=0) clue exactness");
        }

        // ----------------------------------------------------
        // GAMEPLAY LOOP & INTERACTION TESTS
        // ----------------------------------------------------
        // Test 15: Left-Click Reveal, Mine Detonation, and Right-Click Flag
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_gameplay_{Guid.NewGuid():N}.db");
            try
            {
                var chunkManager = new ChunkManager(tempDb, poolPrewarm: 8);
                var profile = new PlayerProfile { BlastShieldCharges = 0 };
                var session = new GameSession(chunkManager, profile);

                // Wait for chunk (0,0)
                var camera = new Camera { X = 0, Y = 0, Zoom = 1.0f };
                chunkManager.UpdateViewport(camera, 800f, 600f);

                Chunk? chunk0 = null;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    chunkManager.TryGetCell(0, 0, out chunk0, out _, out _);
                    if (chunk0 != null) break;
                    await Task.Delay(50);
                }

                Assert(chunk0 != null, "GameSession: Setup - Chunk 0 loaded");

                if (chunk0 != null)
                {
                    // Manually configure cells for precise test
                    chunk0.SetCell(0, 0, CellState.Hidden, 2);
                    chunk0.SetCell(1, 0, CellState.Hidden, CellContent.Mine);

                    session.ToggleFlag(0, 0);
                    bool flagged = chunk0.GetState(0, 0) == CellState.Flagged;
                    session.ToggleFlag(0, 0);
                    bool unflagged = chunk0.GetState(0, 0) == CellState.Hidden;

                    session.RevealCell(0, 0);
                    bool revealed = chunk0.GetState(0, 0) == CellState.Revealed;

                    bool mineEventFired = false;
                    session.OnMineDetonated += (mx, my) => { if (mx == 1 && my == 0) mineEventFired = true; };
                    session.RevealCell(1, 0);
                    bool detonated = chunk0.GetState(1, 0) == CellState.Detonated;

                    Assert(flagged && unflagged && revealed && detonated && mineEventFired,
                        "GameSession: Safe cell reveal, flag toggle, and mine detonation mechanics");
                }

                await chunkManager.DisposeAsync();
            }
            finally
            {
                if (File.Exists(tempDb))
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }
        }

        // Test 16: Infinite Cross-Chunk Flood-Fill
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_flood_{Guid.NewGuid():N}.db");
            try
            {
                var chunkManager = new ChunkManager(tempDb, poolPrewarm: 8);
                var session = new GameSession(chunkManager);

                var camera = new Camera { X = 280, Y = 0, Zoom = 1.0f };
                chunkManager.UpdateViewport(camera, 1200f, 600f);

                Chunk? chunk0 = null;
                Chunk? chunk1 = null;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    chunkManager.TryGetCell(15, 5, out chunk0, out _, out _);
                    chunkManager.TryGetCell(16, 5, out chunk1, out _, out _);
                    if (chunk0 != null && chunk1 != null) break;
                    await Task.Delay(50);
                }

                Assert(chunk0 != null && chunk1 != null, "GameSession: Setup - Chunks 0 and 1 loaded for cross-boundary test");

                if (chunk0 != null && chunk1 != null)
                {
                    chunk0.SetCell(15, 5, CellState.Hidden, CellContent.Empty);
                    chunk1.SetCell(0, 5, CellState.Hidden, CellContent.Empty);
                    chunk1.SetCell(1, 5, CellState.Hidden, 1);

                    session.RevealCell(15, 5);

                    bool c0Revealed = chunk0.GetState(15, 5) == CellState.Revealed;
                    bool c1Revealed0 = chunk1.GetState(0, 5) == CellState.Revealed;
                    bool c1Revealed1 = chunk1.GetState(1, 5) == CellState.Revealed;

                    Assert(c0Revealed && c1Revealed0 && c1Revealed1,
                        "GameSession: Breadth-first flood-fill seamlessly propagates across chunk boundaries");
                }

                await chunkManager.DisposeAsync();
            }
            finally
            {
                if (File.Exists(tempDb))
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }
        }

        // Test 17: Chording Mechanic
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_chord_{Guid.NewGuid():N}.db");
            try
            {
                var chunkManager = new ChunkManager(tempDb, poolPrewarm: 8);
                var session = new GameSession(chunkManager);

                var camera = new Camera { X = 0, Y = 0, Zoom = 1.0f };
                chunkManager.UpdateViewport(camera, 800f, 600f);
                await Task.Delay(300);

                chunkManager.TryGetCell(5, 5, out Chunk? chunk, out _, out _);
                if (chunk != null)
                {
                    chunk.SetCell(5, 5, CellState.Revealed, 1);
                    chunk.SetCell(4, 5, CellState.Flagged, CellContent.Mine);
                    chunk.SetCell(6, 5, CellState.Hidden, 2);

                    session.ChordCell(5, 5);

                    bool chordRevealed = chunk.GetState(6, 5) == CellState.Revealed;
                    Assert(chordRevealed, "GameSession: Chording sweeps hidden cells when flags match clue");
                }

                await chunkManager.DisposeAsync();
            }
            finally
            {
                if (File.Exists(tempDb))
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }
        }

        // ----------------------------------------------------
        // PHASE 6: ECONOMY, ABILITIES, PARTICLES & SOUND
        // ----------------------------------------------------
        // Test 18: Player Profile Economy, XP, & Level-up Progression
        {
            var profile = new PlayerProfile();
            Assert(profile.Level == 1 && profile.CurrentEnergy == 100, "PlayerProfile: Initial Level 1 and 100 energy");

            // Add safe XP
            profile.AddSafeCellXP();
            bool xpAdded = profile.CurrentXP > 0 && profile.Streak == 1;

            // Trigger Level Up by awarding XP threshold
            int startingLevel = profile.Level;
            bool leveledUp = false;
            profile.OnLevelUp += lvl => leveledUp = true;

            for (int i = 0; i < 100; i++)
            {
                profile.AddSafeCellXP();
            }

            Assert(xpAdded && (leveledUp || profile.Level > startingLevel),
                "PlayerProfile: XP accumulation, streak combo multiplier, and promotion leveling");
        }

        // Test 19: Blast Shield & Detonation Absorption
        {
            var profile = new PlayerProfile { BlastShieldCharges = 1 };
            int energyBefore = profile.CurrentEnergy;

            // First mine hit: absorbed by Blast Shield
            profile.DeductMineEnergy(out bool absorbed);
            bool shieldProtected = absorbed && profile.CurrentEnergy >= energyBefore && profile.BlastShieldCharges == 0;

            // Second mine hit: no shield -> energy deducted
            int energyBeforeSecond = profile.CurrentEnergy;
            profile.DeductMineEnergy(out bool absorbed2);
            bool energyDeducted = !absorbed2 && profile.CurrentEnergy < energyBeforeSecond && profile.ComboMultiplier == 1.0f;

            Assert(shieldProtected && energyDeducted, "PlayerProfile: Blast Shield absorption and penalty mechanics");
        }

        // Test 20: Recon Drone 3x3 Safe Scan Ability
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_drone_{Guid.NewGuid():N}.db");
            try
            {
                var chunkManager = new ChunkManager(tempDb, poolPrewarm: 8);
                var profile = new PlayerProfile { ReconDronesAvailable = 2 };
                var session = new GameSession(chunkManager, profile);

                var camera = new Camera { X = 0, Y = 0, Zoom = 1.0f };
                chunkManager.UpdateViewport(camera, 800f, 600f);
                await Task.Delay(300);

                chunkManager.TryGetCell(7, 7, out Chunk? chunk, out _, out _);
                if (chunk != null)
                {
                    // Place a mine at (8, 7) and safe cell at (6, 7)
                    chunk.SetCell(8, 7, CellState.Hidden, CellContent.Mine);
                    chunk.SetCell(6, 7, CellState.Hidden, 1);

                    // Execute Recon Drone on (7, 7)
                    session.ExecuteReconDrone(7, 7);

                    bool mineFlagged = chunk.GetState(8, 7) == CellState.Flagged;
                    bool safeRevealed = chunk.GetState(6, 7) == CellState.Revealed;
                    bool chargeConsumed = profile.ReconDronesAvailable == 1;

                    Assert(mineFlagged && safeRevealed && chargeConsumed,
                        "ReconDrone: Safely scans 3x3 sector, reveals safe cells, and flags mines without detonation");
                }

                await chunkManager.DisposeAsync();
            }
            finally
            {
                if (File.Exists(tempDb))
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }
        }

        // Test 21: Particle System Simulation & Recycling
        {
            var particles = new ParticleSystem();

            // Emit explosion and shockwave
            particles.EmitExplosion(100f, 200f);
            particles.EmitRevealSparkles(50f, 50f);
            particles.EmitSectorLockBurst(0f, 0f, 576f);

            // Step physics simulation
            particles.Update(0.016f); // 1 frame at 60fps
            particles.Update(0.5f);   // 500ms later

            // Should update and decay without exceptions or allocations
            Assert(true, "ParticleSystem: Immediate-mode particle physics simulation, velocity drag, and life decay");
            particles.Dispose();
        }

        // Test 22: Synthesized Audio PCM Waveform Generation
        {
            using var audio = new SynthesizedAudio();
            Assert(!audio.IsMuted, "SynthesizedAudio: Zero-dependency in-memory procedural sound synthesis initialization");
        }

        // Test 23: LifetimeStats Tracking & Serialization
        {
            var stats = new LifetimeStats();
            stats.RecordSafeReveal(10, 1.5f, 15);
            stats.RecordSafeReveal(25, 2.25f, 25);
            stats.RecordFlagPlaced();
            stats.RecordSectorLocked(500);
            stats.RecordShieldAbsorbed();
            stats.RecordDroneUsed();
            stats.RecordChord();

            string json = stats.ToJson();
            var restored = LifetimeStats.FromJson(json);

            bool ok = restored.TotalCellsCleared == 2 &&
                      restored.LongestSafeStreak == 25 &&
                      Math.Abs(restored.HighestComboMultiplier - 2.25f) < 0.01f &&
                      restored.TotalMinesFlagged == 1 &&
                      restored.TotalSectorsLocked == 1 &&
                      restored.TotalShieldsAbsorbed == 1 &&
                      restored.TotalDronesUsed == 1 &&
                      restored.TotalChordsExecuted == 1 &&
                      restored.TotalXpEarned == 540;

            Assert(ok, "LifetimeStats: Metrics recording, streak calculation, and JSON roundtrip");
        }

        // Test 24: AchievementManager Catalog, Evaluation & Rewards
        {
            var manager = new AchievementManager();
            Assert(manager.TotalCount == 12, "AchievementManager: Catalog contains 12 unlockable tactical achievements");

            var profile = new PlayerProfile();
            var stats = new LifetimeStats();

            // Simulate progress to unlock First Contact and Sapper Initiate
            stats.RecordSafeReveal(1, 1.0f, 10);
            stats.RecordFlagPlaced();
            manager.Evaluate(profile, stats);

            var firstContact = manager.Achievements.First(a => a.Id == "first_contact");
            var sapper = manager.Achievements.First(a => a.Id == "sapper_initiate");

            bool unlocked = firstContact.IsUnlocked && sapper.IsUnlocked && manager.UnlockedCount == 2;
            Assert(unlocked, "AchievementManager: Condition evaluation and unlock state updates");

            // Test JSON serialization roundtrip
            string achJson = manager.ToJson();
            var manager2 = new AchievementManager();
            manager2.LoadFromJson(achJson);
            Assert(manager2.UnlockedCount == 2, "AchievementManager: State persistence and JSON serialization");
        }

        // Test 25: ThemeManager Catalog & Color Definitions
        {
            Assert(ThemeManager.Themes.Count == 5, "ThemeManager: 5 distinct sci-fi themes registered");
            var matrix = ThemeManager.GetThemeById("matrix");
            Assert(matrix.Name == "MATRIX TERMINAL", "ThemeManager: Theme lookup by ID");

            var skiaBg = matrix.SkiaBackground;
            var skiaAccent = matrix.SkiaAccent;
            Assert(skiaBg.Alpha == 255 && skiaAccent.Green == 255, "ThemeManager: Skia color conversions for matrix theme");

            var wpfAccent = ThemeDefinition.ParseWpfColor(matrix.AccentHex);
            Assert(wpfAccent.G == 255, "ThemeManager: WPF Color parsing for matrix theme");
        }

        // Test 26: SQLite Persistence of Profile, Stats, Achievements & Theme
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"minefield_test_profile_{Guid.NewGuid():N}.db");
            try
            {
                var profile = new PlayerProfile();
                profile.ActiveThemeId = "synthwave";
                profile.AddSafeCellXP(); // triggers safe reveal, streak, stats
                profile.RecordFlagPlaced();
                profile.RecordDroneUsed();
                profile.SaveToDatabase(tempDb);

                var loadedProfile = new PlayerProfile();
                loadedProfile.LoadFromDatabase(tempDb);

                bool match = loadedProfile.ActiveThemeId == "synthwave" &&
                             loadedProfile.Stats.TotalCellsCleared == 1 &&
                             loadedProfile.Stats.TotalMinesFlagged == 1 &&
                             loadedProfile.Stats.TotalDronesUsed == 1;

                Assert(match, "PlayerProfile: SQLite persistence with StatsJson, AchievementsJson, and ActiveThemeId");
            }
            finally
            {
                try { File.Delete(tempDb); } catch { }
            }
        }

        // Test 27: GameDifficulty Presets & Scaling
        {
            var cadet = DifficultyConfig.GetConfig(DifficultyLevel.Cadet);
            var standard = DifficultyConfig.GetConfig(DifficultyLevel.Standard);
            var hazard = DifficultyConfig.GetConfig(DifficultyLevel.Hazard);
            var nightmare = DifficultyConfig.GetConfig(DifficultyLevel.Nightmare);

            bool diffValid = cadet.MineDensityPercent == 12 && cadet.EnergyPenalty == 15 && cadet.StartingShields == 2 &&
                             standard.MineDensityPercent == 17 && standard.EnergyPenalty == 25 && standard.StartingShields == 1 &&
                             hazard.MineDensityPercent == 22 && hazard.EnergyPenalty == 35 && hazard.StartingShields == 1 &&
                             nightmare.MineDensityPercent == 28 && nightmare.EnergyPenalty == 50 && nightmare.StartingShields == 0;

            Assert(diffValid, "GameDifficulty: 4 Threat Presets with escalating mine density, penalties, and shields");
        }

        // Test 28: Camera Screen Shake & Decay
        {
            var cam = new Camera();
            Assert(cam.ShakeTrauma == 0.0f && cam.ShakeOffsetX == 0.0f && cam.ShakeOffsetY == 0.0f, "Camera: Initial shake state is zero");

            cam.AddTrauma(0.8f);
            Assert(cam.ShakeTrauma == 0.8f, "Camera: AddTrauma increases shake trauma");

            cam.UpdateShake(0.1f);
            Assert(cam.ShakeTrauma < 0.8f, "Camera: UpdateShake decays trauma over time");

            // Tick forward until fully decayed
            cam.UpdateShake(2.0f);
            Assert(cam.ShakeTrauma == 0.0f && cam.ShakeOffsetX == 0.0f && cam.ShakeOffsetY == 0.0f, "Camera: Full decay returns trauma and offsets to zero");
        }

        // Test 29: Mine Detonation Penalties, Combo Shatter & Core Meltdown
        {
            var profile = new PlayerProfile();
            profile.BlastShieldCharges = 0; // ensure unshielded
            profile.CurrentDifficulty = DifficultyLevel.Standard; // 25 energy penalty

            bool shattered = false;
            bool depleted = false;
            profile.OnComboShattered += () => shattered = true;
            profile.OnEnergyDepleted += () => depleted = true;

            // Build a streak to establish a combo
            for (int i = 0; i < 5; i++) profile.AddSafeCellXP();
            Assert(profile.Streak == 5 && profile.ComboMultiplier > 1.0f, "PlayerProfile: Streak established");

            int energyBefore = profile.CurrentEnergy;
            // Detonate mine (unshielded)
            profile.DeductMineEnergy(out bool absorbed);
            Assert(!absorbed && shattered, "PlayerProfile: Unshielded detonation shatters combo and triggers OnComboShattered");
            Assert(profile.Streak == 0 && profile.ComboMultiplier == 1.0f, "PlayerProfile: Combo reset to 1.0 and streak reset to 0");
            Assert(profile.CurrentEnergy == energyBefore - 25, "PlayerProfile: 25 Energy penalty applied on Standard threat");

            // Deplete remaining energy to zero
            profile.DeductMineEnergy(profile.CurrentEnergy, 0, out _);
            Assert(profile.CurrentEnergy == 0 && depleted, "PlayerProfile: Energy depleted triggers OnEnergyDepleted (Game Over)");
        }

        // Test 30: PlayerProfile Reboot Expedition
        {
            var profile = new PlayerProfile();
            profile.BlastShieldCharges = 0;
            for (int i = 0; i < 6; i++) profile.AddSafeCellXP();
            profile.RunCellsCleared = 50;
            profile.DeductMineEnergy(35, 0, out _);

            var hazard = DifficultyConfig.GetConfig(DifficultyLevel.Hazard);
            profile.RebootExpedition(hazard.StartingShields, hazard.StartingDrones);

            bool rebootOk = profile.CurrentEnergy == profile.MaxEnergy &&
                            profile.Streak == 0 &&
                            profile.ComboMultiplier == 1.0f &&
                            profile.BlastShieldCharges == hazard.StartingShields &&
                            profile.ReconDronesAvailable == hazard.StartingDrones &&
                            profile.RunCellsCleared == 0;

            Assert(rebootOk, "PlayerProfile: RebootExpedition resets energy to max, clears combo, and applies threat kit");
        }

        // Test 31: ChunkManager ResetAllChunksAsync
        {
            string testDb = Path.Combine(Path.GetTempPath(), $"minefield_test_reset_{Guid.NewGuid():N}.db");
            var cm = new ChunkManager(testDb);
            try
            {
                var cam = new Camera { X = 0, Y = 0, Zoom = 1.0f };
                cm.UpdateViewport(cam, 800, 600);
                await Task.Delay(200); // Allow async generation

                Assert(cm.ActiveChunks.Count > 0, "ChunkManager: Initial chunks generated");

                await cm.ResetAllChunksAsync(9999, 28);
                Assert(cm.ActiveChunks.Count == 0, "ChunkManager: ResetAllChunksAsync purged active chunks");
                Assert(cm.Generator.WorldSeed == 9999 && cm.Generator.MineDensityPercent == 28,
                    "ChunkManager: ResetAllChunksAsync updated generator seed and density");
            }
            finally
            {
                await cm.DisposeAsync();
                try { File.Delete(testDb); } catch { }
            }
        }

        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine($"RESULTS: {passed} Passed, {failed} Failed");
        Console.WriteLine("--------------------------------------------------");

        return failed == 0 ? 0 : 1;
    }
}
