// =============================================================================
// Sfx
// =============================================================================
// Purpose:
//   Autoload sound-effect service: preloads the small synthesized WAV set
//   (audio/*.wav — quiet tournament-room felt thuds, ticks and chimes) and
//   plays them through a small round-robin AudioStreamPlayer pool so
//   overlapping events (a capture during a crack) never cut each other off.
//   Static Play() mirrors the Haptics pattern; safe no-op headless or if a
//   stream is missing.
//
// Interactions:
//   - HexBoard: select/move/capture/coin/crack/collapse/win/lose cues.
//   - project.godot [autoload]: Sfx="*res://scripts/Sfx.cs".
// =============================================================================

using System.Collections.Generic;
using Godot;

namespace HexGame;

public partial class Sfx : Node
{
    private const int PoolSize = 6;
    private const string SettingsPath = "user://settings.cfg";   // NOT hex.cfg — GameSession
    private const string Section = "audio";                      // rewrites that file whole

    private static Sfx _instance;

    private readonly Dictionary<string, AudioStream> _streams = new();
    private readonly AudioStreamPlayer[] _pool = new AudioStreamPlayer[PoolSize];
    private int _next;

    public static bool Enabled { get; private set; } = true;

    private static readonly string[] Names =
    {
        "select", "move", "capture", "coin", "crack", "collapse", "win", "lose",
    };

    public override void _Ready()
    {
        _instance = this;
        for (int i = 0; i < PoolSize; i++)
        {
            _pool[i] = new AudioStreamPlayer();
            AddChild(_pool[i]);
        }
        foreach (var name in Names)
        {
            var path = $"res://audio/{name}.wav";
            if (ResourceLoader.Exists(path))
                _streams[name] = GD.Load<AudioStream>(path);
        }

        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) == Error.Ok)
            Enabled = (bool)cfg.GetValue(Section, "sound_enabled", true);
    }

    public static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "sound_enabled", enabled);
        cfg.Save(SettingsPath);
    }

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
    }

    public static void Play(string name, float volumeDb = 0f)
    {
        var inst = _instance;
        if (inst == null || !Enabled || !inst._streams.TryGetValue(name, out var stream)) return;

        var p = inst._pool[inst._next];
        inst._next = (inst._next + 1) % PoolSize;
        p.Stop();
        p.Stream = stream;
        p.VolumeDb = volumeDb;
        p.Play();
    }
}
