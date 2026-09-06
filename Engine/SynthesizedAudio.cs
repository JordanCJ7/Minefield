using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace Minefield.Engine;

/// <summary>
/// Zero-dependency procedural sound synthesis engine. Generates in-memory
/// PCM WAV waveforms played through System.Media.SoundPlayer.
/// </summary>
public class SynthesizedAudio : IDisposable
{
    public bool IsMuted { get; set; } = false;

    private readonly SoundPlayer? _clickPlayer;
    private readonly SoundPlayer? _flagPlayer;
    private readonly SoundPlayer? _detonationPlayer;
    private readonly SoundPlayer? _sectorLockPlayer;
    private readonly SoundPlayer? _droneScanPlayer;
    private readonly SoundPlayer? _shieldPlayer;

    public SynthesizedAudio()
    {
        try
        {
            _clickPlayer = CreatePlayer(CreateClickWav());
            _flagPlayer = CreatePlayer(CreateFlagWav());
            _detonationPlayer = CreatePlayer(CreateDetonationWav());
            _sectorLockPlayer = CreatePlayer(CreateSectorLockWav());
            _droneScanPlayer = CreatePlayer(CreateDroneScanWav());
            _shieldPlayer = CreatePlayer(CreateShieldWav());
        }
        catch { }
    }

    public void PlayClick() => PlayAsync(_clickPlayer);
    public void PlayFlag() => PlayAsync(_flagPlayer);
    public void PlayDetonation() => PlayAsync(_detonationPlayer);
    public void PlaySectorLock() => PlayAsync(_sectorLockPlayer);
    public void PlayDroneScan() => PlayAsync(_droneScanPlayer);
    public void PlayShieldDeflect() => PlayAsync(_shieldPlayer);

    private void PlayAsync(SoundPlayer? player)
    {
        if (IsMuted || player == null) return;
        Task.Run(() =>
        {
            try { player.Play(); } catch { }
        });
    }

    private static SoundPlayer CreatePlayer(byte[] wavBytes)
    {
        var ms = new MemoryStream(wavBytes);
        var player = new SoundPlayer(ms);
        player.Load();
        return player;
    }

    #region Waveform Synthesis

    private static byte[] CreateClickWav()
    {
        // 1100 Hz short tech blip (35 ms)
        return Synthesize(0.035, t =>
        {
            double env = 1.0 - (t / 0.035);
            return Math.Sin(2 * Math.PI * 1100 * t) * env * 0.4;
        });
    }

    private static byte[] CreateFlagWav()
    {
        // Sci-fi chirp upward glide 600 Hz -> 1400 Hz (60 ms)
        return Synthesize(0.06, t =>
        {
            double freq = 600 + (t / 0.06) * 800;
            double env = Math.Sin(Math.PI * (t / 0.06));
            return Math.Sin(2 * Math.PI * freq * t) * env * 0.5;
        });
    }

    private static byte[] CreateDetonationWav()
    {
        // Bass rumble + noise explosion (350 ms)
        var rand = new Random(42);
        return Synthesize(0.35, t =>
        {
            double decay = Math.Exp(-8.0 * t);
            double sub = Math.Sin(2 * Math.PI * (70 - t * 80) * t);
            double noise = (rand.NextDouble() * 2.0 - 1.0);
            return (sub * 0.6 + noise * 0.4) * decay * 0.8;
        });
    }

    private static byte[] CreateSectorLockWav()
    {
        // Ascending major chord sweep (C5 -> E5 -> G5 -> C6) (300 ms)
        return Synthesize(0.32, t =>
        {
            double freq = t switch
            {
                < 0.08 => 523.25, // C5
                < 0.16 => 659.25, // E5
                < 0.24 => 783.99, // G5
                _ => 1046.50      // C6
            };
            double env = Math.Sin(Math.PI * (t / 0.32));
            return Math.Sin(2 * Math.PI * freq * t) * env * 0.55;
        });
    }

    private static byte[] CreateDroneScanWav()
    {
        // High-frequency sonar radar pulse (220 ms)
        return Synthesize(0.22, t =>
        {
            double mod = Math.Sin(2 * Math.PI * 30 * t);
            double freq = 1800 + mod * 400;
            double env = Math.Exp(-6.0 * t);
            return Math.Sin(2 * Math.PI * freq * t) * env * 0.45;
        });
    }

    private static byte[] CreateShieldWav()
    {
        // Metallic deflection chime (880 Hz + 1320 Hz) (200 ms)
        return Synthesize(0.20, t =>
        {
            double env = Math.Exp(-10.0 * t);
            double h1 = Math.Sin(2 * Math.PI * 880 * t);
            double h2 = Math.Sin(2 * Math.PI * 1320 * t) * 0.5;
            return (h1 + h2) * env * 0.5;
        });
    }

    private static byte[] Synthesize(double durationSeconds, Func<double, double> sampleGenerator, int sampleRate = 44100)
    {
        int numSamples = (int)(durationSeconds * sampleRate);
        int dataSize = numSamples * 2; // 16-bit mono = 2 bytes per sample

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);

        // Format chunk
        bw.Write("fmt "u8);
        bw.Write(16);                 // Subchunk1Size (16 for PCM)
        bw.Write((short)1);            // AudioFormat (1 = PCM)
        bw.Write((short)1);            // NumChannels (1 = Mono)
        bw.Write(sampleRate);          // SampleRate
        bw.Write(sampleRate * 2);      // ByteRate (SampleRate * NumChannels * BitsPerSample/8)
        bw.Write((short)2);            // BlockAlign (NumChannels * BitsPerSample/8)
        bw.Write((short)16);           // BitsPerSample (16-bit)

        // Data chunk
        bw.Write("data"u8);
        bw.Write(dataSize);

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double sample = Math.Clamp(sampleGenerator(t), -1.0, 1.0);
            short pcm = (short)(sample * short.MaxValue);
            bw.Write(pcm);
        }

        return ms.ToArray();
    }

    #endregion

    public void Dispose()
    {
        _clickPlayer?.Dispose();
        _flagPlayer?.Dispose();
        _detonationPlayer?.Dispose();
        _sectorLockPlayer?.Dispose();
        _droneScanPlayer?.Dispose();
        _shieldPlayer?.Dispose();
    }
}
