using System.IO;
using System.Media;
using System.Windows.Media;
using BreakApp.Core.Models;

namespace BreakApp.Services;

/// <summary>Plays the built-in chime (HiSync's two-tone 880 Hz → 1318.5 Hz) or a custom sound file.</summary>
public sealed class SoundService
{
    private readonly Dictionary<int, byte[]> _chimeCache = [];
    private readonly List<MediaPlayer> _players = [];
    private SoundPlayer? _player;

    public Func<SoundSettings> Settings { get; set; } = () => new SoundSettings();

    /// <summary>null = built-in chime, "" = silent, otherwise a file path. volume 0..1 (multiplied by the global volume).</summary>
    public void Play(string? sound, double volumeScale = 1)
    {
        var s = Settings();
        if (s.Mute || sound == "") return;
        var volume = Math.Clamp(s.Volume * volumeScale, 0, 1);
        if (volume <= 0) return;
        try
        {
            if (sound is null) PlayChime(volume);
            else PlayFile(sound, volume);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or UriFormatException)
        {
            App.Log($"sound failed: {e.Message}");
        }
    }

    private void PlayChime(double volume)
    {
        var key = (int)Math.Round(volume * 20);
        if (!_chimeCache.TryGetValue(key, out var wav)) _chimeCache[key] = wav = BuildChime(key / 20.0);
        _player?.Dispose();
        _player = new SoundPlayer(new MemoryStream(wav));
        _player.Play();
    }

    private void PlayFile(string path, double volume)
    {
        if (!File.Exists(path)) { PlayChime(volume); return; }
        var mp = new MediaPlayer { Volume = volume };
        mp.MediaEnded += (_, _) => { mp.Close(); _players.Remove(mp); };
        mp.MediaFailed += (_, _) => { mp.Close(); _players.Remove(mp); };
        mp.Open(new Uri(path, UriKind.Absolute));
        _players.Add(mp);
        mp.Play();
    }

    /// <summary>16-bit mono PCM WAV of the chime: two sine notes, 20 ms attack, exponential decay over 0.6 s.</summary>
    internal static byte[] BuildChime(double volume)
    {
        const int rate = 44100;
        const double length = 0.8;
        var n = (int)(rate * length);
        var samples = new short[n];
        (double f, double at)[] notes = [(880, 0), (1318.5, 0.11)];
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)rate;
            double v = 0;
            foreach (var (f, at) in notes)
            {
                var lt = t - at;
                if (lt < 0 || lt > 0.65) continue;
                var g = lt < 0.02 ? 0.12 * lt / 0.02 : 0.12 * Math.Pow(0.0001 / 0.12, (lt - 0.02) / 0.58);
                v += g * Math.Sin(2 * Math.PI * f * lt);
            }
            samples[i] = (short)Math.Clamp(v * 2.5 * volume * short.MaxValue, short.MinValue, short.MaxValue);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
