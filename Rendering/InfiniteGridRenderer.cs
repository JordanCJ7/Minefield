using System;
using SkiaSharp;
using Minefield.Engine;

namespace Minefield.Rendering;

/// <summary>
/// High-performance SkiaSharp immediate-mode renderer for the infinite grid,
/// chunk sector boundaries, coordinate markers, and hover states at 60+ FPS.
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

    public InfiniteGridRenderer()
    {
        _backgroundPaint = new SKPaint
        {
            Color = new SKColor(7, 11, 20), // #070B14
            Style = SKPaintStyle.Fill
        };

        _minorGridPaint = new SKPaint
        {
            Color = new SKColor(18, 30, 52, 160), // #121E34
            StrokeWidth = 1.0f,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke
        };

        _majorGridPaint = new SKPaint
        {
            Color = new SKColor(0, 229, 255, 180), // Neon Cyan #00E5FF
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
            Color = new SKColor(179, 136, 255, 200), // Neon Purple #B388FF
            StrokeWidth = 2.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        _originCrosshairPaint = new SKPaint
        {
            Color = new SKColor(0, 230, 118, 240), // Neon Emerald #00E676
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
    }

    /// <summary>
    /// Renders the infinite grid, chunk boundaries, and coordinate markers for the current frame.
    /// </summary>
    public void Render(SKCanvas canvas, float width, float height, Camera camera, SKPoint? mouseScreenPos)
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

        // Calculate Cell and Chunk ranges
        int startCellX = (int)Math.Floor(worldBounds.Left / Camera.CellSize) - 1;
        int endCellX = (int)Math.Ceiling(worldBounds.Right / Camera.CellSize) + 1;
        int startCellY = (int)Math.Floor(worldBounds.Top / Camera.CellSize) - 1;
        int endCellY = (int)Math.Ceiling(worldBounds.Bottom / Camera.CellSize) + 1;

        int startChunkX = (int)Math.Floor(worldBounds.Left / Camera.ChunkSize);
        int endChunkX = (int)Math.Ceiling(worldBounds.Right / Camera.ChunkSize);
        int startChunkY = (int)Math.Floor(worldBounds.Top / Camera.ChunkSize);
        int endChunkY = (int)Math.Ceiling(worldBounds.Bottom / Camera.ChunkSize);

        // 4. Draw Minor Cell Grid (with distance fade when zoomed out)
        if (camera.Zoom >= 0.35f)
        {
            float alphaFactor = Math.Clamp((camera.Zoom - 0.35f) / 0.45f, 0.0f, 1.0f);
            byte cellAlpha = (byte)(160 * alphaFactor);
            _minorGridPaint.Color = new SKColor(18, 30, 52, cellAlpha);

            // Vertical Cell Lines
            for (int cx = startCellX; cx <= endCellX; cx++)
            {
                if (cx % Camera.ChunkDimension == 0) continue; // Major chunk line will cover this
                float x = cx * Camera.CellSize;
                canvas.DrawLine(x, worldBounds.Top, x, worldBounds.Bottom, _minorGridPaint);
            }

            // Horizontal Cell Lines
            for (int cy = startCellY; cy <= endCellY; cy++)
            {
                if (cy % Camera.ChunkDimension == 0) continue;
                float y = cy * Camera.CellSize;
                canvas.DrawLine(worldBounds.Left, y, worldBounds.Right, y, _minorGridPaint);
            }
        }

        // 5. Highlight Hovered Cell (Mouse to World to Cell mapping verification)
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

        // 6. Draw Major Chunk Boundaries (16x16 Sectors)
        for (int chx = startChunkX; chx <= endChunkX; chx++)
        {
            float x = chx * Camera.ChunkSize;
            canvas.DrawLine(x, worldBounds.Top, x, worldBounds.Bottom, _chunkBorderGlowPaint);
            canvas.DrawLine(x, worldBounds.Top, x, worldBounds.Bottom, _majorGridPaint);
        }

        for (int chy = startChunkY; chy <= endChunkY; chy++)
        {
            float y = chy * Camera.ChunkSize;
            canvas.DrawLine(worldBounds.Left, y, worldBounds.Right, y, _chunkBorderGlowPaint);
            canvas.DrawLine(worldBounds.Left, y, worldBounds.Right, y, _majorGridPaint);
        }

        // 7. World Origin Crosshair & Coordinate Axes (0, 0)
        if (worldBounds.Left <= 0 && worldBounds.Right >= 0)
        {
            canvas.DrawLine(0, worldBounds.Top, 0, worldBounds.Bottom, _axisPaint);
        }
        if (worldBounds.Top <= 0 && worldBounds.Bottom >= 0)
        {
            canvas.DrawLine(worldBounds.Left, 0, worldBounds.Right, 0, _axisPaint);
        }

        // Origin Marker
        float crosshairSize = 16.0f / camera.Zoom;
        canvas.DrawLine(-crosshairSize, 0, crosshairSize, 0, _originCrosshairPaint);
        canvas.DrawLine(0, -crosshairSize, 0, crosshairSize, _originCrosshairPaint);
        canvas.DrawCircle(0, 0, 5.0f / camera.Zoom, _originCrosshairPaint);

        // 8. Sector Chunk Labels & Corner Brackets
        for (int chx = startChunkX; chx <= endChunkX; chx++)
        {
            for (int chy = startChunkY; chy <= endChunkY; chy++)
            {
                float chunkLeft = chx * Camera.ChunkSize;
                float chunkTop = chy * Camera.ChunkSize;

                // Corner decorative brackets
                float bracketLen = Math.Min(32.0f, Camera.CellSize);
                canvas.DrawLine(chunkLeft, chunkTop, chunkLeft + bracketLen, chunkTop, _majorGridPaint);
                canvas.DrawLine(chunkLeft, chunkTop, chunkLeft, chunkTop + bracketLen, _majorGridPaint);

                // Sector Label
                string sectorLabel = $"SECTOR [{chx:+#;-#;0}, {chy:+#;-#;0}]";
                float textPadding = 4.0f / camera.Zoom;
                float labelX = chunkLeft + 8.0f / camera.Zoom;
                float labelY = chunkTop + 18.0f / camera.Zoom;

                // Scale font size proportionally if zoomed far out, but keep readable
                float fontSize = Math.Clamp(12.0f / camera.Zoom, 8.0f, 22.0f);
                _chunkLabelPaint.TextSize = fontSize;

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

        canvas.Restore();

        // 9. Screen-Space Ambient Vignette & Crosshairs (optional overlay)
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
    }
}
