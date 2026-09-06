using System;
using SkiaSharp;
using Minefield.Engine;

namespace Minefield.Rendering;

/// <summary>
/// High-performance SkiaSharp immediate-mode renderer for the infinite grid,
/// chunk sector boundaries, tile graphics, and locked sector effects at 60+ FPS.
/// </summary>
public class InfiniteGridRenderer : IDisposable
{
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _minorGridPaint;
    private readonly SKPaint _majorGridPaint;
    private readonly SKPaint _chunkBorderGlowPaint;
    private readonly SKPaint _axisPaint;
    private readonly SKPaint _chunkLabelPaint;
    private readonly SKPaint _chunkLabelBgPaint;
    private readonly SKPaint _hoverCellPaint;
    private readonly SKPaint _originCrosshairPaint;

    // Tile Styling Paints
    private readonly SKPaint _tileHiddenPaint;
    private readonly SKPaint _tileHiddenBorderPaint;
    private readonly SKPaint _tileRevealedPaint;
    private readonly SKPaint _tileFlagPaint;
    private readonly SKPaint _tileMinePaint;
    private readonly SKPaint _tileDetonatedBgPaint;
    private readonly SKPaint _lockedSectorOverlayPaint;
    private readonly SKPaint _lockedSectorBorderPaint;
    private readonly SKPaint _lockedBadgePaint;

    // Pre-cached number paints for 1..8 with distinctive neon sci-fi colors
    private readonly SKPaint[] _numberPaints = new SKPaint[9];

    public InfiniteGridRenderer()
    {
        _backgroundPaint = new SKPaint
        {
            Color = new SKColor(7, 11, 20), // #070B14
            Style = SKPaintStyle.Fill
        };

        _minorGridPaint = new SKPaint
        {
            Color = new SKColor(18, 30, 52, 160),
            StrokeWidth = 1.0f,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke
        };

        _majorGridPaint = new SKPaint
        {
            Color = new SKColor(0, 229, 255, 180), // Neon Cyan
            StrokeWidth = 2.0f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        _chunkBorderGlowPaint = new SKPaint
        {
            Color = new SKColor(0, 229, 255, 50),
            StrokeWidth = 6.0f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.0f)
        };

        _axisPaint = new SKPaint
        {
            Color = new SKColor(179, 136, 255, 200), // Neon Purple
            StrokeWidth = 2.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        _originCrosshairPaint = new SKPaint
        {
            Color = new SKColor(0, 230, 118, 240), // Neon Emerald
            StrokeWidth = 2.0f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        _chunkLabelBgPaint = new SKPaint
        {
            Color = new SKColor(13, 21, 38, 220),
            Style = SKPaintStyle.Fill
        };

        _chunkLabelPaint = new SKPaint
        {
            Color = new SKColor(0, 229, 255, 240),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            TextSize = 11.0f,
            Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold)
        };

        _hoverCellPaint = new SKPaint
        {
            Color = new SKColor(0, 229, 255, 45),
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        // Tile Paints
        _tileHiddenPaint = new SKPaint
        {
            Color = new SKColor(15, 23, 42), // #0F172A
            Style = SKPaintStyle.Fill
        };

        _tileHiddenBorderPaint = new SKPaint
        {
            Color = new SKColor(30, 41, 59, 180), // #1E293B
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.0f
        };

        _tileRevealedPaint = new SKPaint
        {
            Color = new SKColor(5, 8, 16), // Recessed floor #050810
            Style = SKPaintStyle.Fill
        };

        _tileFlagPaint = new SKPaint
        {
            Color = new SKColor(255, 23, 68), // Neon Crimson Flag
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            TextSize = 18.0f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI Symbol", SKFontStyle.Bold)
        };

        _tileMinePaint = new SKPaint
        {
            Color = new SKColor(255, 23, 68), // Mine Crimson
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            TextSize = 18.0f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI Symbol", SKFontStyle.Bold)
        };

        _tileDetonatedBgPaint = new SKPaint
        {
            Color = new SKColor(183, 28, 28, 180),
            Style = SKPaintStyle.Fill
        };

        _lockedSectorOverlayPaint = new SKPaint
        {
            Color = new SKColor(0, 230, 118, 18), // Subtle energetic emerald wash
            Style = SKPaintStyle.Fill
        };

        _lockedSectorBorderPaint = new SKPaint
        {
            Color = new SKColor(0, 230, 118, 160), // Emerald sector border
            StrokeWidth = 2.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        _lockedBadgePaint = new SKPaint
        {
            Color = new SKColor(0, 230, 118, 240),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            TextSize = 10.0f,
            Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold)
        };

        // Initialize neon colors for numbers 1..8
        SKColor[] numberColors =
        {
            SKColors.Transparent,
            new SKColor(0, 229, 255),    // 1: Cyan
            new SKColor(0, 230, 118),    // 2: Emerald
            new SKColor(255, 23, 68),    // 3: Crimson
            new SKColor(179, 136, 255),  // 4: Purple
            new SKColor(255, 214, 0),    // 5: Amber
            new SKColor(29, 233, 182),   // 6: Teal
            new SKColor(255, 110, 64),   // 7: Coral
            new SKColor(241, 245, 249)   // 8: Pure Ice
        };

        for (int i = 1; i <= 8; i++)
        {
            _numberPaints[i] = new SKPaint
            {
                Color = numberColors[i],
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                TextSize = 16.0f,
                TextAlign = SKTextAlign.Center,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold)
            };
        }
    }

    /// <summary>
    /// Renders the infinite grid, streamed chunks, tiles, and telemetry visuals.
    /// </summary>
    public void Render(SKCanvas canvas, float width, float height, Camera camera, SKPoint? mouseScreenPos, ChunkManager? chunkManager = null)
    {
        // 1. Clear Viewport Background
        canvas.DrawRect(0, 0, width, height, _backgroundPaint);

        // 2. Visible World Bounds Calculation
        SKRect worldBounds = camera.GetVisibleWorldBounds(width, height);

        // 3. Save state and apply Camera World Transformation
        canvas.Save();
        canvas.Translate(width / 2.0f, height / 2.0f);
        canvas.Scale(camera.Zoom);
        canvas.Translate(-camera.X, -camera.Y);

        int startChunkX = (int)Math.Floor(worldBounds.Left / Camera.ChunkSize);
        int endChunkX = (int)Math.Ceiling(worldBounds.Right / Camera.ChunkSize);
        int startChunkY = (int)Math.Floor(worldBounds.Top / Camera.ChunkSize);
        int endChunkY = (int)Math.Ceiling(worldBounds.Bottom / Camera.ChunkSize);

        // 4. Render Active Chunks and Tiles
        if (chunkManager != null)
        {
            float tileInset = 1.0f;
            float textOffsetY = 6.0f; // Center Consolas font vertically in 36px cell

            for (int chx = startChunkX; chx <= endChunkX; chx++)
            {
                for (int chy = startChunkY; chy <= endChunkY; chy++)
                {
                    if (!chunkManager.ActiveChunks.TryGetValue((chx, chy), out Chunk? chunk))
                        continue;

                    float chunkLeft = chx * Camera.ChunkSize;
                    float chunkTop = chy * Camera.ChunkSize;

                    // Locked Sector Background Wash
                    if (chunk.IsLocked)
                    {
                        canvas.DrawRect(chunkLeft, chunkTop, Camera.ChunkSize, Camera.ChunkSize, _lockedSectorOverlayPaint);
                    }

                    // Level-of-Detail: Only render individual cell details if zoom is sufficient
                    if (camera.Zoom >= 0.35f)
                    {
                        for (int ly = 0; ly < Chunk.Dimension; ly++)
                        {
                            for (int lx = 0; lx < Chunk.Dimension; lx++)
                            {
                                float cellLeft = chunkLeft + lx * Camera.CellSize;
                                float cellTop = chunkTop + ly * Camera.CellSize;

                                CellState state = chunk.GetState(lx, ly);
                                byte content = chunk.GetContent(lx, ly);

                                var cellRect = new SKRect(
                                    cellLeft + tileInset,
                                    cellTop + tileInset,
                                    cellLeft + Camera.CellSize - tileInset,
                                    cellTop + Camera.CellSize - tileInset
                                );

                                switch (state)
                                {
                                    case CellState.Hidden:
                                        canvas.DrawRoundRect(cellRect, 3.0f, 3.0f, _tileHiddenPaint);
                                        canvas.DrawRoundRect(cellRect, 3.0f, 3.0f, _tileHiddenBorderPaint);
                                        break;

                                    case CellState.Flagged:
                                        canvas.DrawRoundRect(cellRect, 3.0f, 3.0f, _tileHiddenPaint);
                                        canvas.DrawRoundRect(cellRect, 3.0f, 3.0f, _tileHiddenBorderPaint);
                                        canvas.DrawText("▲", cellLeft + Camera.CellSize / 2.0f, cellTop + Camera.CellSize / 2.0f + textOffsetY, _tileFlagPaint);
                                        break;

                                    case CellState.Detonated:
                                        canvas.DrawRect(cellRect, _tileDetonatedBgPaint);
                                        canvas.DrawText("✹", cellLeft + Camera.CellSize / 2.0f, cellTop + Camera.CellSize / 2.0f + textOffsetY, _tileMinePaint);
                                        break;

                                    case CellState.Revealed:
                                        canvas.DrawRect(cellRect, _tileRevealedPaint);

                                        if (content == CellContent.Mine)
                                        {
                                            canvas.DrawText("✹", cellLeft + Camera.CellSize / 2.0f, cellTop + Camera.CellSize / 2.0f + textOffsetY, _tileMinePaint);
                                        }
                                        else if (content >= 1 && content <= 8)
                                        {
                                            string numStr = content.ToString();
                                            canvas.DrawText(numStr, cellLeft + Camera.CellSize / 2.0f, cellTop + Camera.CellSize / 2.0f + textOffsetY, _numberPaints[content]);
                                        }
                                        break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // 5. Draw Minor Cell Grid (if not covered by tiles or when zoomed out)
        if (chunkManager == null && camera.Zoom >= 0.35f)
        {
            int startCellX = (int)Math.Floor(worldBounds.Left / Camera.CellSize) - 1;
            int endCellX = (int)Math.Ceiling(worldBounds.Right / Camera.CellSize) + 1;
            int startCellY = (int)Math.Floor(worldBounds.Top / Camera.CellSize) - 1;
            int endCellY = (int)Math.Ceiling(worldBounds.Bottom / Camera.CellSize) + 1;

            float alphaFactor = Math.Clamp((camera.Zoom - 0.35f) / 0.45f, 0.0f, 1.0f);
            byte cellAlpha = (byte)(160 * alphaFactor);
            _minorGridPaint.Color = new SKColor(18, 30, 52, cellAlpha);

            for (int cx = startCellX; cx <= endCellX; cx++)
            {
                if (cx % Camera.ChunkDimension == 0) continue;
                float x = cx * Camera.CellSize;
                canvas.DrawLine(x, worldBounds.Top, x, worldBounds.Bottom, _minorGridPaint);
            }

            for (int cy = startCellY; cy <= endCellY; cy++)
            {
                if (cy % Camera.ChunkDimension == 0) continue;
                float y = cy * Camera.CellSize;
                canvas.DrawLine(worldBounds.Left, y, worldBounds.Right, y, _minorGridPaint);
            }
        }

        // 6. Highlight Hovered Cell
        if (mouseScreenPos.HasValue)
        {
            SKPoint worldMouse = camera.ScreenToWorld(mouseScreenPos.Value, width, height);
            var (hoverCellX, hoverCellY) = Camera.WorldToCell(worldMouse.X, worldMouse.Y);
            float hx = hoverCellX * Camera.CellSize;
            float hy = hoverCellY * Camera.CellSize;

            canvas.DrawRect(hx, hy, Camera.CellSize, Camera.CellSize, _hoverCellPaint);

            using var hoverBorder = new SKPaint
            {
                Color = new SKColor(0, 229, 255, 200),
                StrokeWidth = 1.5f / camera.Zoom,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            canvas.DrawRect(hx, hy, Camera.CellSize, Camera.CellSize, hoverBorder);
        }

        // 7. Draw Major Chunk Boundaries & Sector Overlays
        for (int chx = startChunkX; chx <= endChunkX; chx++)
        {
            for (int chy = startChunkY; chy <= endChunkY; chy++)
            {
                float chunkLeft = chx * Camera.ChunkSize;
                float chunkTop = chy * Camera.ChunkSize;

                bool isLocked = chunkManager != null &&
                                chunkManager.ActiveChunks.TryGetValue((chx, chy), out var ch) &&
                                ch.IsLocked;

                SKPaint borderPaint = isLocked ? _lockedSectorBorderPaint : _majorGridPaint;

                // Chunk rectangle outline
                canvas.DrawRect(chunkLeft, chunkTop, Camera.ChunkSize, Camera.ChunkSize, borderPaint);

                // Corner decorative brackets
                float bracketLen = Math.Min(32.0f, Camera.CellSize);
                canvas.DrawLine(chunkLeft, chunkTop, chunkLeft + bracketLen, chunkTop, borderPaint);
                canvas.DrawLine(chunkLeft, chunkTop, chunkLeft, chunkTop + bracketLen, borderPaint);

                // Sector Label
                string sectorLabel = isLocked
                    ? $"SECTOR [{chx:+#;-#;0}, {chy:+#;-#;0}] // SECURE ✓"
                    : $"SECTOR [{chx:+#;-#;0}, {chy:+#;-#;0}]";

                float textPadding = 4.0f / camera.Zoom;
                float labelX = chunkLeft + 8.0f / camera.Zoom;
                float labelY = chunkTop + 18.0f / camera.Zoom;

                float fontSize = Math.Clamp(12.0f / camera.Zoom, 8.0f, 22.0f);
                _chunkLabelPaint.TextSize = fontSize;
                _chunkLabelPaint.Color = isLocked ? new SKColor(0, 230, 118, 240) : new SKColor(0, 229, 255, 240);

                float textWidth = _chunkLabelPaint.MeasureText(sectorLabel);
                var bgRect = new SKRect(
                    labelX - textPadding,
                    labelY - fontSize,
                    labelX + textWidth + textPadding * 2,
                    labelY + textPadding
                );

                canvas.DrawRoundRect(bgRect, 2.0f / camera.Zoom, 2.0f / camera.Zoom, _chunkLabelBgPaint);
                canvas.DrawText(sectorLabel, labelX, labelY, _chunkLabelPaint);
            }
        }

        // 8. World Origin Crosshair (0, 0)
        if (worldBounds.Left <= 0 && worldBounds.Right >= 0)
        {
            canvas.DrawLine(0, worldBounds.Top, 0, worldBounds.Bottom, _axisPaint);
        }
        if (worldBounds.Top <= 0 && worldBounds.Bottom >= 0)
        {
            canvas.DrawLine(worldBounds.Left, 0, worldBounds.Right, 0, _axisPaint);
        }

        float crosshairSize = 16.0f / camera.Zoom;
        canvas.DrawLine(-crosshairSize, 0, crosshairSize, 0, _originCrosshairPaint);
        canvas.DrawLine(0, -crosshairSize, 0, crosshairSize, _originCrosshairPaint);
        canvas.DrawCircle(0, 0, 5.0f / camera.Zoom, _originCrosshairPaint);

        canvas.Restore();

        // 9. Screen-Space Ambient Vignette
        using var vignettePaint = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(width / 2.0f, height / 2.0f),
                Math.Max(width, height) * 0.75f,
                new[] { SKColors.Transparent, new SKColor(5, 8, 16, 120) },
                new[] { 0.0f, 1.0f },
                SKShaderTileMode.Clamp
            )
        };
        canvas.DrawRect(0, 0, width, height, vignettePaint);
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _minorGridPaint.Dispose();
        _majorGridPaint.Dispose();
        _chunkBorderGlowPaint.Dispose();
        _axisPaint.Dispose();
        _chunkLabelPaint.Dispose();
        _chunkLabelBgPaint.Dispose();
        _hoverCellPaint.Dispose();
        _originCrosshairPaint.Dispose();

        _tileHiddenPaint.Dispose();
        _tileHiddenBorderPaint.Dispose();
        _tileRevealedPaint.Dispose();
        _tileFlagPaint.Dispose();
        _tileMinePaint.Dispose();
        _tileDetonatedBgPaint.Dispose();
        _lockedSectorOverlayPaint.Dispose();
        _lockedSectorBorderPaint.Dispose();
        _lockedBadgePaint.Dispose();

        for (int i = 1; i <= 8; i++)
        {
            _numberPaints[i]?.Dispose();
        }
    }
}
