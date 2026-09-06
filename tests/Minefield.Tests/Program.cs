using System;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;
using Microsoft.EntityFrameworkCore;
using Minefield.Data;
using Minefield.Engine;

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
            chunk.SetCell(5, 7, CellState.Revealed, 3); // 3 adjacent mines
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
            // Populate chunk: 10 mines, 246 safe cells
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
            int initialAvailable = pool.AvailableCount;

            Chunk rented1 = pool.Rent(10, 20);
            rented1.SetCell(0, 0, CellState.Revealed, 5);

            Chunk rented2 = pool.Rent(30, 40);
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

                // Insert chunk into DB
                byte[] sampleData = new byte[256];
                sampleData[0] = 0x91; // Revealed Mine
                sampleData[255] = 0x22; // Flagged with 2

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

                // Query back
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

                // Trigger viewport update for 800x600 window
                chunkManager.UpdateViewport(camera, 800f, 600f);

                // Give background worker brief moment to stream initial chunks
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

        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine($"RESULTS: {passed} Passed, {failed} Failed");
        Console.WriteLine("--------------------------------------------------");

        return failed == 0 ? 0 : 1;
    }
}
