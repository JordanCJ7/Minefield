using System;
using SkiaSharp;

namespace Minefield.Engine;

/// <summary>
/// High-performance 2D infinite camera system supporting smooth panning,
/// cursor-centered zooming, and bidirectional screen-to-world coordinate transformations.
/// </summary>
public class Camera
{
    // Default Cell Size (pixels) and Chunk Dimension (16x16 cells)
    public const float CellSize = 36.0f;
    public const int ChunkDimension = 16;
    public const float ChunkSize = CellSize * ChunkDimension; // 576.0f pixels per chunk

    public const float MinZoom = 0.15f;
    public const float MaxZoom = 4.0f;

    /// <summary>
    /// Camera world position at the center of the viewport (in world pixels).
    /// </summary>
    public float X { get; set; } = 0.0f;
    public float Y { get; set; } = 0.0f;

    /// <summary>
    /// Current scale/zoom factor.
    /// </summary>
    public float Zoom { get; set; } = 1.0f;

    /// <summary>
    /// Translates camera in world units based on screen pixel delta.
    /// </summary>
    public void Pan(float deltaScreenX, float deltaScreenY)
    {
        X -= deltaScreenX / Zoom;
        Y -= deltaScreenY / Zoom;
    }

    /// <summary>
    /// Zooms the camera while keeping the world point under the screen cursor static.
    /// </summary>
    public void ZoomAt(SKPoint screenPivot, float zoomFactor, float viewportWidth, float viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        // Determine world coordinate of cursor prior to zoom
        SKPoint worldPivotBefore = ScreenToWorld(screenPivot, viewportWidth, viewportHeight);

        // Apply clamped zoom
        float targetZoom = Math.Clamp(Zoom * zoomFactor, MinZoom, MaxZoom);
        if (Math.Abs(targetZoom - Zoom) < 0.0001f) return;

        Zoom = targetZoom;

        // Reposition camera so worldPivotBefore remains under screenPivot
        X = worldPivotBefore.X - (screenPivot.X - viewportWidth / 2.0f) / Zoom;
        Y = worldPivotBefore.Y - (screenPivot.Y - viewportHeight / 2.0f) / Zoom;
    }

    /// <summary>
    /// Resets camera to world origin (0, 0) at 100% zoom.
    /// </summary>
    public void Reset()
    {
        X = 0.0f;
        Y = 0.0f;
        Zoom = 1.0f;
    }

    /// <summary>
    /// Converts a screen pixel coordinate (relative to SKElement) to infinite world coordinates.
    /// </summary>
    public SKPoint ScreenToWorld(SKPoint screenPoint, float viewportWidth, float viewportHeight)
    {
        float worldX = (screenPoint.X - viewportWidth / 2.0f) / Zoom + X;
        float worldY = (screenPoint.Y - viewportHeight / 2.0f) / Zoom + Y;
        return new SKPoint(worldX, worldY);
    }

    /// <summary>
    /// Converts an infinite world coordinate to screen pixel coordinates.
    /// </summary>
    public SKPoint WorldToScreen(SKPoint worldPoint, float viewportWidth, float viewportHeight)
    {
        float screenX = (worldPoint.X - X) * Zoom + viewportWidth / 2.0f;
        float screenY = (worldPoint.Y - Y) * Zoom + viewportHeight / 2.0f;
        return new SKPoint(screenX, screenY);
    }

    /// <summary>
    /// Calculates the visible world bounding rectangle given the current viewport size.
    /// </summary>
    public SKRect GetVisibleWorldBounds(float viewportWidth, float viewportHeight)
    {
        SKPoint topLeft = ScreenToWorld(new SKPoint(0, 0), viewportWidth, viewportHeight);
        SKPoint bottomRight = ScreenToWorld(new SKPoint(viewportWidth, viewportHeight), viewportWidth, viewportHeight);
        return new SKRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    /// <summary>
    /// Converts a world coordinate to integer cell grid coordinates.
    /// </summary>
    public static (int CellX, int CellY) WorldToCell(float worldX, float worldY)
    {
        int cellX = (int)Math.Floor(worldX / CellSize);
        int cellY = (int)Math.Floor(worldY / CellSize);
        return (cellX, cellY);
    }

    /// <summary>
    /// Converts a world coordinate to integer 16x16 chunk sector coordinates.
    /// </summary>
    public static (int ChunkX, int ChunkY) WorldToChunk(float worldX, float worldY)
    {
        int chunkX = (int)Math.Floor(worldX / ChunkSize);
        int chunkY = (int)Math.Floor(worldY / ChunkSize);
        return (chunkX, chunkY);
    }

    /// <summary>
    /// Converts cell coordinates to sector chunk coordinates and local cell index (0..15).
    /// </summary>
    public static (int ChunkX, int ChunkY, int LocalX, int LocalY) CellToChunkAndLocal(int cellX, int cellY)
    {
        int chunkX = (int)Math.Floor((double)cellX / ChunkDimension);
        int chunkY = (int)Math.Floor((double)cellY / ChunkDimension);

        int localX = cellX % ChunkDimension;
        if (localX < 0) localX += ChunkDimension;

        int localY = cellY % ChunkDimension;
        if (localY < 0) localY += ChunkDimension;

        return (chunkX, chunkY, localX, localY);
    }
}
