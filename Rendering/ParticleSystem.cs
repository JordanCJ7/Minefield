using System;
using SkiaSharp;
using Minefield.Engine;

namespace Minefield.Rendering;

public enum ParticleType
{
    Spark,
    Shockwave,
    FloatGlow
}

public struct Particle
{
    public bool Active;
    public ParticleType Type;
    public float X;
    public float Y;
    public float Vx;
    public float Vy;
    public SKColor Color;
    public float Size;
    public float Life; // 1.0 -> 0.0
    public float Decay;
}

/// <summary>
/// Pre-allocated, zero-allocation particle system rendering explosive detonations,
/// safe reveals, and sector lock vector effects at 60+ FPS in SkiaSharp.
/// </summary>
public class ParticleSystem : IDisposable
{
    private const int MaxParticles = 1024;
    private readonly Particle[] _particles = new Particle[MaxParticles];
    private readonly Random _rng = new();

    private readonly SKPaint _sparkPaint;
    private readonly SKPaint _shockwavePaint;
    private readonly SKPaint _glowPaint;

    public ParticleSystem()
    {
        _sparkPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        _shockwavePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeWidth = 3.0f
        };

        _glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4.0f)
        };
    }

    public void EmitExplosion(float worldX, float worldY)
    {
        // 1. Shockwave Ring
        SpawnParticle(ParticleType.Shockwave, worldX, worldY, 0, 0,
            new SKColor(255, 23, 68), initialSize: 10.0f, decay: 2.2f);

        // 2. High-speed spark fragments
        SKColor[] sparkColors =
        {
            new SKColor(255, 23, 68),   // Crimson
            new SKColor(255, 110, 64),  // Bright Orange
            new SKColor(255, 214, 0),   // Amber
            new SKColor(255, 255, 255)  // White hot
        };

        for (int i = 0; i < 48; i++)
        {
            double angle = _rng.NextDouble() * Math.PI * 2.0;
            float speed = (float)(80.0 + _rng.NextDouble() * 320.0);
            float vx = (float)Math.Cos(angle) * speed;
            float vy = (float)Math.Sin(angle) * speed;
            SKColor color = sparkColors[_rng.Next(sparkColors.Length)];
            float size = (float)(3.0 + _rng.NextDouble() * 4.5);
            float decay = (float)(1.2 + _rng.NextDouble() * 1.6);

            SpawnParticle(ParticleType.Spark, worldX, worldY, vx, vy, color, size, decay);
        }
    }

    public void EmitRevealSparkles(float worldX, float worldY)
    {
        for (int i = 0; i < 6; i++)
        {
            float vx = (float)((_rng.NextDouble() - 0.5) * 35.0);
            float vy = (float)(-20.0 - _rng.NextDouble() * 45.0); // Float upward
            SKColor color = new SKColor(0, 229, 255);
            float size = (float)(2.0 + _rng.NextDouble() * 2.5);
            float decay = (float)(1.5 + _rng.NextDouble() * 1.5);

            SpawnParticle(ParticleType.FloatGlow, worldX, worldY, vx, vy, color, size, decay);
        }
    }

    public void EmitSectorLockBurst(float chunkLeft, float chunkTop, float chunkSize)
    {
        float centerX = chunkLeft + chunkSize / 2.0f;
        float centerY = chunkTop + chunkSize / 2.0f;

        // Giant Emerald Shockwave
        SpawnParticle(ParticleType.Shockwave, centerX, centerY, 0, 0,
            new SKColor(0, 230, 118), initialSize: 20.0f, decay: 1.0f);

        // Perimeter spark shards
        for (int i = 0; i < 64; i++)
        {
            double angle = _rng.NextDouble() * Math.PI * 2.0;
            float speed = (float)(60.0 + _rng.NextDouble() * 240.0);
            float vx = (float)Math.Cos(angle) * speed;
            float vy = (float)Math.Sin(angle) * speed;
            SKColor color = new SKColor(0, 230, 118);
            float size = (float)(3.5 + _rng.NextDouble() * 3.5);
            float decay = (float)(0.8 + _rng.NextDouble() * 1.2);

            SpawnParticle(ParticleType.Spark, centerX, centerY, vx, vy, color, size, decay);
        }
    }

    public void EmitDroneScan(float worldCenterX, float worldCenterY)
    {
        // Radar pulse ring
        SpawnParticle(ParticleType.Shockwave, worldCenterX, worldCenterY, 0, 0,
            new SKColor(0, 229, 255), initialSize: 15.0f, decay: 1.4f);

        for (int i = 0; i < 24; i++)
        {
            double angle = _rng.NextDouble() * Math.PI * 2.0;
            float speed = (float)(30.0 + _rng.NextDouble() * 100.0);
            float vx = (float)Math.Cos(angle) * speed;
            float vy = (float)Math.Sin(angle) * speed;
            SKColor color = new SKColor(0, 229, 255);
            float size = 2.5f;
            float decay = 1.8f;

            SpawnParticle(ParticleType.FloatGlow, worldCenterX, worldCenterY, vx, vy, color, size, decay);
        }
    }

    private void SpawnParticle(ParticleType type, float x, float y, float vx, float vy, SKColor color, float initialSize, float decay)
    {
        for (int i = 0; i < MaxParticles; i++)
        {
            if (!_particles[i].Active)
            {
                _particles[i].Active = true;
                _particles[i].Type = type;
                _particles[i].X = x;
                _particles[i].Y = y;
                _particles[i].Vx = vx;
                _particles[i].Vy = vy;
                _particles[i].Color = color;
                _particles[i].Size = initialSize;
                _particles[i].Life = 1.0f;
                _particles[i].Decay = decay;
                return;
            }
        }
    }

    public void Update(float dt)
    {
        for (int i = 0; i < MaxParticles; i++)
        {
            if (!_particles[i].Active) continue;

            _particles[i].Life -= _particles[i].Decay * dt;
            if (_particles[i].Life <= 0)
            {
                _particles[i].Active = false;
                continue;
            }

            if (_particles[i].Type == ParticleType.Shockwave)
            {
                // Expanding wave
                _particles[i].Size += 380.0f * dt;
            }
            else
            {
                _particles[i].X += _particles[i].Vx * dt;
                _particles[i].Y += _particles[i].Vy * dt;

                // Drag/friction
                _particles[i].Vx *= (float)Math.Pow(0.15, dt);
                _particles[i].Vy *= (float)Math.Pow(0.15, dt);
            }
        }
    }

    public void Render(SKCanvas canvas, Camera camera)
    {
        for (int i = 0; i < MaxParticles; i++)
        {
            if (!_particles[i].Active) continue;

            byte alpha = (byte)(255 * Math.Clamp(_particles[i].Life, 0.0f, 1.0f));
            var color = _particles[i].Color.WithAlpha(alpha);

            switch (_particles[i].Type)
            {
                case ParticleType.Spark:
                    _sparkPaint.Color = color;
                    canvas.DrawCircle(_particles[i].X, _particles[i].Y, _particles[i].Size, _sparkPaint);
                    break;

                case ParticleType.FloatGlow:
                    _glowPaint.Color = color;
                    canvas.DrawCircle(_particles[i].X, _particles[i].Y, _particles[i].Size, _glowPaint);
                    break;

                case ParticleType.Shockwave:
                    _shockwavePaint.Color = color;
                    _shockwavePaint.StrokeWidth = 3.0f * _particles[i].Life;
                    canvas.DrawCircle(_particles[i].X, _particles[i].Y, _particles[i].Size, _shockwavePaint);
                    break;
            }
        }
    }

    public void Dispose()
    {
        _sparkPaint.Dispose();
        _shockwavePaint.Dispose();
        _glowPaint.Dispose();
    }
}
