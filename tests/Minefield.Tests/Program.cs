using System;
using SkiaSharp;
using Minefield.Engine;

namespace Minefield.Tests;

class Program
{
    static int Main(string[] args)
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
            // Cell (0, 0) -> Chunk (0, 0), Local (0, 0)
            var (ch0, cy0, lx0, ly0) = Camera.CellToChunkAndLocal(0, 0);
            bool test0 = ch0 == 0 && cy0 == 0 && lx0 == 0 && ly0 == 0;

            // Cell (15, 15) -> Chunk (0, 0), Local (15, 15)
            var (ch15, cy15, lx15, ly15) = Camera.CellToChunkAndLocal(15, 15);
            bool test15 = ch15 == 0 && cy15 == 0 && lx15 == 15 && ly15 == 15;

            // Cell (16, 16) -> Chunk (1, 1), Local (0, 0)
            var (ch16, cy16, lx16, ly16) = Camera.CellToChunkAndLocal(16, 16);
            bool test16 = ch16 == 1 && cy16 == 1 && lx16 == 0 && ly16 == 0;

            // Cell (-1, -1) -> Chunk (-1, -1), Local (15, 15)
            var (chNeg1, cyNeg1, lxNeg1, lyNeg1) = Camera.CellToChunkAndLocal(-1, -1);
            bool testNeg1 = chNeg1 == -1 && cyNeg1 == -1 && lxNeg1 == 15 && lyNeg1 == 15;

            // Cell (-16, -16) -> Chunk (-1, -1), Local (0, 0)
            var (chNeg16, cyNeg16, lxNeg16, lyNeg16) = Camera.CellToChunkAndLocal(-16, -16);
            bool testNeg16 = chNeg16 == -1 && cyNeg16 == -1 && lxNeg16 == 0 && lyNeg16 == 0;

            // Cell (-17, -17) -> Chunk (-2, -2), Local (15, 15)
            var (chNeg17, cyNeg17, lxNeg17, lyNeg17) = Camera.CellToChunkAndLocal(-17, -17);
            bool testNeg17 = chNeg17 == -2 && cyNeg17 == -2 && lxNeg17 == 15 && lyNeg17 == 15;

            bool allPassed = test0 && test15 && test16 && testNeg1 && testNeg16 && testNeg17;
            Assert(allPassed, "Camera: CellToChunkAndLocal handles positive, boundary, and negative sectors");
        }

        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine($"RESULTS: {passed} Passed, {failed} Failed");
        Console.WriteLine("--------------------------------------------------");

        return failed == 0 ? 0 : 1;
    }
}
