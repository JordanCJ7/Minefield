using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Minefield.Engine;
using Minefield.Rendering;

namespace Minefield;

/// <summary>
/// Main Game Window hosting the SkiaSharp immediate-mode rendering engine
/// and the high-tech WPF glassmorphism HUD overlays.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Camera _camera = new();
    private readonly InfiniteGridRenderer _gridRenderer = new();
    private readonly ChunkManager _chunkManager = new();
    private readonly GameSession _gameSession;

    // Mouse Navigation & Gameplay State
    private bool _isPanning;
    private Point _lastMousePosition;
    private Point _mouseDownPos;
    private bool _hasDragged;
    private SKPoint? _lastMouseScreenPixel;

    // Real-Time FPS Tracking
    private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();
    private int _frameCount;
    private double _lastFpsUpdate;
    private double _currentFps = 60.0;

    public MainWindow()
    {
        InitializeComponent();

        _gameSession = new GameSession(_chunkManager);
        _gameSession.OnSectorLocked += GameSession_OnSectorLocked;
        _gameSession.OnMineDetonated += GameSession_OnMineDetonated;

        // Subscribe to CompositionTarget.Rendering for smooth continuous rendering
        CompositionTarget.Rendering += OnCompositionRendering;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void GameSession_OnSectorLocked(int cx, int cy)
    {
        // Visual notification in status strip
        Dispatcher.Invoke(() =>
        {
            TxtSectorStatus.Text = $"SECTOR [{cx}, {cy}] SECURED! +500 XP";
        });
    }

    private void GameSession_OnMineDetonated(int wx, int wy)
    {
        // Detonation notification
        Dispatcher.Invoke(() =>
        {
            TxtSectorStatus.Text = $"⚠ MINE DETONATION AT [{wx}, {wy}]!";
        });
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateTelemetry(0, 0);
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnCompositionRendering;
        await _chunkManager.DisposeAsync();
        _gridRenderer.Dispose();
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        // Update chunk streaming with current viewport dimensions
        var (dpiX, dpiY) = GetDpiScaling();
        float viewportWidth = (float)(SkiaCanvas.ActualWidth * dpiX);
        float viewportHeight = (float)(SkiaCanvas.ActualHeight * dpiY);

        if (viewportWidth > 0 && viewportHeight > 0)
        {
            _chunkManager.UpdateViewport(_camera, viewportWidth, viewportHeight);
        }

        // Triggers Skia canvas redraw synchronized with the display refresh rate
        SkiaCanvas.InvalidateVisual();
    }

    #region Window Controls & Dragging

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                DragMove();
            }
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void BtnResetCamera_Click(object sender, RoutedEventArgs e)
    {
        _camera.Reset();
        UpdateTelemetry(0, 0);
        SkiaCanvas.InvalidateVisual();
    }

    #endregion

    #region SkiaSharp Canvas & Input Handling

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        // 1. Calculate FPS
        _frameCount++;
        double elapsedSeconds = _fpsStopwatch.Elapsed.TotalSeconds;
        if (elapsedSeconds - _lastFpsUpdate >= 0.35)
        {
            _currentFps = _frameCount / (elapsedSeconds - _lastFpsUpdate);
            _frameCount = 0;
            _lastFpsUpdate = elapsedSeconds;
            TxtFps.Text = $"{_currentFps:F1} FPS";
        }

        // 2. Render Infinite Grid and Viewport
        int pixelWidth = e.Info.Width;
        int pixelHeight = e.Info.Height;
        SKCanvas canvas = e.Surface.Canvas;

        _gridRenderer.Render(canvas, pixelWidth, pixelHeight, _camera, _lastMouseScreenPixel, _chunkManager);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Point pos = e.GetPosition(SkiaCanvas);
        _mouseDownPos = pos;
        _hasDragged = false;

        // Middle-mouse always starts panning
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _lastMousePosition = pos;
            SkiaCanvas.CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            _isPanning = true;
            _lastMousePosition = pos;
            SkiaCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        Point currentPos = e.GetPosition(SkiaCanvas);
        var (dpiX, dpiY) = GetDpiScaling();

        // Convert WPF DIPs to Skia physical pixels
        float screenPixelX = (float)(currentPos.X * dpiX);
        float screenPixelY = (float)(currentPos.Y * dpiY);
        _lastMouseScreenPixel = new SKPoint(screenPixelX, screenPixelY);

        if (_isPanning)
        {
            double deltaDipX = currentPos.X - _lastMousePosition.X;
            double deltaDipY = currentPos.Y - _lastMousePosition.Y;

            if (Math.Abs(currentPos.X - _mouseDownPos.X) > 4 || Math.Abs(currentPos.Y - _mouseDownPos.Y) > 4)
            {
                _hasDragged = true;
                Cursor = Cursors.SizeAll;
            }

            // Pan in pixel coordinates
            _camera.Pan((float)(deltaDipX * dpiX), (float)(deltaDipY * dpiY));
            _lastMousePosition = currentPos;
        }

        // Update live cursor coordinates
        float viewportWidth = (float)(SkiaCanvas.ActualWidth * dpiX);
        float viewportHeight = (float)(SkiaCanvas.ActualHeight * dpiY);
        if (viewportWidth > 0 && viewportHeight > 0)
        {
            SKPoint worldPoint = _camera.ScreenToWorld(_lastMouseScreenPixel.Value, viewportWidth, viewportHeight);
            UpdateTelemetry(worldPoint.X, worldPoint.Y);
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Point currentPos = e.GetPosition(SkiaCanvas);
        var (dpiX, dpiY) = GetDpiScaling();

        float viewportWidth = (float)(SkiaCanvas.ActualWidth * dpiX);
        float viewportHeight = (float)(SkiaCanvas.ActualHeight * dpiY);

        SKPoint screenPixel = new SKPoint((float)(currentPos.X * dpiX), (float)(currentPos.Y * dpiY));
        SKPoint worldPoint = _camera.ScreenToWorld(screenPixel, viewportWidth, viewportHeight);
        var (cellX, cellY) = Camera.WorldToCell(worldPoint.X, worldPoint.Y);

        if (_isPanning)
        {
            _isPanning = false;
            SkiaCanvas.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;

            // If right button was released without dragging, treat as Toggle Flag!
            if (e.ChangedButton == MouseButton.Right && !_hasDragged)
            {
                _gameSession.ToggleFlag(cellX, cellY);
                SkiaCanvas.InvalidateVisual();
                e.Handled = true;
                return;
            }

            // If middle button was released without dragging on a revealed number, treat as Chord!
            if (e.ChangedButton == MouseButton.Middle && !_hasDragged)
            {
                _gameSession.ChordCell(cellX, cellY);
                SkiaCanvas.InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        // Left Click: Reveal or Chord
        if (e.ChangedButton == MouseButton.Left)
        {
            if (!_hasDragged)
            {
                if (e.ClickCount == 2)
                {
                    // Double-click chord
                    _gameSession.ChordCell(cellX, cellY);
                }
                else
                {
                    _gameSession.RevealCell(cellX, cellY);
                }

                SkiaCanvas.InvalidateVisual();
            }

            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point mousePos = e.GetPosition(SkiaCanvas);
        var (dpiX, dpiY) = GetDpiScaling();

        float screenPixelX = (float)(mousePos.X * dpiX);
        float screenPixelY = (float)(mousePos.Y * dpiY);
        float viewportWidth = (float)(SkiaCanvas.ActualWidth * dpiX);
        float viewportHeight = (float)(SkiaCanvas.ActualHeight * dpiY);

        // Smooth zoom step centered around mouse cursor
        float zoomFactor = e.Delta > 0 ? 1.15f : (1.0f / 1.15f);
        _camera.ZoomAt(new SKPoint(screenPixelX, screenPixelY), zoomFactor, viewportWidth, viewportHeight);

        // Update telemetry
        SKPoint worldPoint = _camera.ScreenToWorld(new SKPoint(screenPixelX, screenPixelY), viewportWidth, viewportHeight);
        UpdateTelemetry(worldPoint.X, worldPoint.Y);
    }

    private void SkiaCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            _lastMouseScreenPixel = null;
        }
    }

    private (double DpiX, double DpiY) GetDpiScaling()
    {
        PresentationSource source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            return (source.CompositionTarget.TransformToDevice.M11,
                    source.CompositionTarget.TransformToDevice.M22);
        }
        return (1.0, 1.0);
    }

    private void UpdateTelemetry(float worldX, float worldY)
    {
        TxtWorldPos.Text = $"X: {worldX:+0.0;-0.0;0.0}  Y: {worldY:+0.0;-0.0;0.0}";

        var (cellX, cellY) = Camera.WorldToCell(worldX, worldY);
        TxtCellPos.Text = $"[{cellX}, {cellY}]";

        var (chunkX, chunkY) = Camera.WorldToChunk(worldX, worldY);
        TxtChunkPos.Text = $"[{chunkX}, {chunkY}]";

        TxtZoom.Text = $"{(_camera.Zoom * 100.0f):F0}%";
        TxtChunkCount.Text = $"{_chunkManager.ActiveChunks.Count} SECTORS ACTIVE (SQLite)";
    }

    #endregion
}
