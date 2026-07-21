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

using System;
using Godot;

namespace HexGame;

// Compile-time-safe cue keys: a mistyped string used to no-op silently.
// Each cue maps to res://audio/<lowercase-name>.wav.
public enum SfxCue { Select, Move, Capture, Coin, Crack, Collapse, Win, Lose, Boss }

public partial class Sfx : Node
{
    private const int PoolSize = 6;
    private const string SettingsPath = "user://settings.cfg";   // NOT hex.cfg — GameSession
    private const string Section = "audio";                      // rewrites that file whole

    private static Sfx _instance;

    private readonly AudioStream[] _streams = new AudioStream[CueCount];
    private readonly AudioStreamPlayer[] _pool = new AudioStreamPlayer[PoolSize];
    private AudioStreamPlayer _ambient;
    private AudioStreamPlayer _threat;
    private bool _threatOn;
    private int _next;

    public static bool Enabled { get; private set; } = true;
    public static float Volume { get; private set; } = 1f;   // master level (0..1)

    private static readonly int CueCount = Enum.GetValues<SfxCue>().Length;

    // The soundscape mix, indexed by SfxCue — tune the balance here, not at
    // call sites. (A couple of sites intentionally override, e.g. the quieter
    // enemy-reach select tick.)
    private static readonly float[] CueVolumeDb =
    {
        -12f,  // Select
        -7f,   // Move
        -5f,   // Capture
        -9f,   // Coin
        -5f,   // Crack
        -3f,   // Collapse
        -5f,   // Win
        -4f,   // Lose
        -4f,   // Boss
    };

    public static void Play(SfxCue cue) => Play(cue, CueVolumeDb[(int)cue]);

    public override void _Ready()
    {
        _instance = this;
        for (int i = 0; i < PoolSize; i++)
        {
            _pool[i] = new AudioStreamPlayer();
            AddChild(_pool[i]);
        }
        foreach (var cue in Enum.GetValues<SfxCue>())
        {
            var path = $"res://audio/{cue.ToString().ToLowerInvariant()}.wav";
            if (ResourceLoader.Exists(path))
                _streams[(int)cue] = GD.Load<AudioStream>(path);
        }

        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) == Error.Ok)
        {
            Enabled = (bool)cfg.GetValue(Section, "sound_enabled", true);
            Volume = Mathf.Clamp((float)(double)cfg.GetValue(Section, "volume", 1.0), 0f, 1f);
        }
        ApplyVolume();

        // Quiet seamless room pad under everything (loop is sample-exact).
        _ambient = MakeLoopingPlayer("res://audio/ambient.wav", -16f);
        if (_ambient != null && Enabled) _ambient.Play();

        // Low pulse bed that runs only while the board is cracking.
        _threat = MakeLoopingPlayer("res://audio/threat.wav", -13f);
    }

    private AudioStreamPlayer MakeLoopingPlayer(string path, float volumeDb)
    {
        if (!ResourceLoader.Exists(path) || GD.Load<AudioStream>(path) is not AudioStreamWav wav)
            return null;
        wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        wav.LoopBegin = 0;
        wav.LoopEnd = wav.Data.Length / 2;   // 16-bit mono: 2 bytes per frame
        var p = new AudioStreamPlayer { Stream = wav, VolumeDb = volumeDb };
        AddChild(p);
        return p;
    }

    // Runs while the board is cracking; obeys the sound toggle.
    public static void SetThreatBed(bool on)
    {
        var inst = _instance;
        if (inst == null || inst._threat == null) return;
        inst._threatOn = on;
        if (on && Enabled && !inst._threat.Playing) inst._threat.Play();
        else if (!on) inst._threat.Stop();
    }

    // Master level (0..1) applied via the Master bus, so it scales every cue and
    // the ambient/threat loops uniformly. Independent of the on/off toggle.
    public static void SetVolume(float volume)
    {
        Volume = Mathf.Clamp(volume, 0f, 1f);
        ApplyVolume();
        SaveSettings();
    }

    private static void ApplyVolume()
    {
        // Master bus (index 0); near-zero maps to effectively silent.
        AudioServer.SetBusVolumeDb(0, Volume <= 0.001f ? -60f : Mathf.LinearToDb(Volume));
    }

    // Persist BOTH audio settings together — writing one key on a fresh ConfigFile
    // would drop the other (settings.cfg is rewritten whole).
    private static void SaveSettings()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "sound_enabled", Enabled);
        cfg.SetValue(Section, "volume", Volume);
        cfg.Save(SettingsPath);
    }

    public static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        SaveSettings();

        var inst = _instance;
        if (inst == null) return;
        if (inst._ambient != null)
        {
            if (enabled && !inst._ambient.Playing) inst._ambient.Play();
            else if (!enabled) inst._ambient.Stop();
        }
        if (inst._threat != null)
        {
            if (enabled && inst._threatOn && !inst._threat.Playing) inst._threat.Play();
            else if (!enabled) inst._threat.Stop();
        }
    }

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
    }

    public static void Play(SfxCue cue, float volumeDb = 0f)
    {
        var inst = _instance;
        if (inst == null || !Enabled) return;
        var stream = inst._streams[(int)cue];
        if (stream == null) return;

        var p = inst._pool[inst._next];
        inst._next = (inst._next + 1) % PoolSize;
        p.Stop();
        p.Stream = stream;
        p.VolumeDb = volumeDb;
        p.Play();
    }
}
