// =============================================================================
// DebugLog
// =============================================================================
// Purpose:
//   Singleton autoload Node that captures all Godot log output (stdout,
//   warnings, errors, script/shader errors) into a thread-safe ring buffer
//   of up to 500 entries. Installs a CanvasLayer overlay with a gear button
//   that opens an in-game settings modal, and exposes Snapshot() so other
//   UI can inspect recent log activity. Also owns the DebugModal and
//   SettingsModal instances used by the overlay.
//
// Interactions:
//   - DebugModal: instantiated and added to the overlay; opened on demand
//     (e.g. from SettingsModal callback) to show the captured log snapshot.
//   - SettingsModal: instantiated with a callback that opens DebugModal;
//     opened when the user taps the gear button.
//   - CapturingLogger (nested): registered with OS.AddLogger to forward
//     engine log messages and errors back into the entry buffer.
// =============================================================================

#nullable enable
using Godot;
using System;
using System.Collections.Generic;

namespace HexGame;

public partial class DebugLog : Node
{
    public static DebugLog? Instance { get; private set; }

    public static event Action<bool>? GameplayActiveChanged;
    private static int _modalDepth;

    public static void PushModal()
    {
        if (++_modalDepth == 1) GameplayActiveChanged?.Invoke(false);
    }

    public static void PopModal()
    {
        if (_modalDepth == 0) return;
        if (--_modalDepth == 0) GameplayActiveChanged?.Invoke(true);
    }

    public static bool IsAnyModalOpen => _modalDepth > 0;

    private const int MaxEntries = 500;
    private const int OverlayLayer = 128;

    private readonly LinkedList<string> _entries = new();
    private readonly object _lock = new();
    private CapturingLogger? _logger;
    private DebugModal? _modal;
    private SettingsModal? _settings;
    private Tween? _gearFlashTween;

    public override void _Ready()
    {
        Instance = this;
        _logger = new CapturingLogger(this);
        OS.AddLogger(_logger);
#if DEBUG
        GD.Print("[DebugLog] ready");
#endif
        CallDeferred(MethodName.AttachOverlay);
    }

    public override void _ExitTree()
    {
        if (_logger != null)
        {
            OS.RemoveLogger(_logger);
            _logger = null;
        }
        if (Instance == this) Instance = null;
    }

    public void Append(string kind, string message)
    {
        lock (_lock)
        {
            string ts = Time.GetTimeStringFromSystem();
            _entries.AddLast($"[{ts}] [{kind}] {message}");
            while (_entries.Count > MaxEntries) _entries.RemoveFirst();
        }
    }

    public string Snapshot()
    {
        lock (_lock) return string.Join("\n", _entries);
    }

    public int EntryCount
    {
        get { lock (_lock) return _entries.Count; }
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    private void AttachOverlay()
    {
        var layer = new CanvasLayer { Layer = OverlayLayer };
        AddChild(layer);

        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(root);

        Texture2D? tex = ResourceLoader.Exists("res://textures/ui/gear.png")
            ? GD.Load<Texture2D>("res://textures/ui/gear.png")
            : null;

        Control button;
        if (tex != null)
        {
            var b = new TextureButton();
            b.TextureNormal = tex;
            b.IgnoreTextureSize = true;
            b.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
            b.Modulate = new Color(0.85f, 0.88f, 0.94f, 0.9f);
            button = b;
            ((TextureButton)button).Pressed += OpenSettings;
        }
        else
        {
            var b = new Button();
            b.Text = "⚙";
            b.AddThemeFontSizeOverride("font_size", 44);
            button = b;
            ((Button)button).Pressed += OpenSettings;
        }
        button.CustomMinimumSize = new Vector2(72, 72);
        button.AnchorLeft = 1.0f; button.AnchorRight = 1.0f;
        button.AnchorTop = 0.0f; button.AnchorBottom = 0.0f;
        button.OffsetLeft = -80; button.OffsetTop = 8;
        button.OffsetRight = -8; button.OffsetBottom = 80;
        button.TooltipText = "Settings";
        Color originalModulate = button.Modulate;
        ((BaseButton)button).ButtonDown += () => FlashGear(button, originalModulate);
        root.AddChild(button);

        _modal = new DebugModal();
        root.AddChild(_modal);

        _settings = new SettingsModal(() => _modal?.Open());
        root.AddChild(_settings);
    }

    private void OpenSettings()
    {
#if DEBUG
        GD.Print("[DebugLog] gear Pressed — opening settings");
#endif
        _settings?.Open();
    }

    private void FlashGear(Control button, Color originalModulate)
    {
#if DEBUG
        GD.Print("[DebugLog] gear ButtonDown received");
#endif
        if (_gearFlashTween != null && _gearFlashTween.IsValid()) _gearFlashTween.Kill();
        button.Modulate = new Color(1.0f, 0.9f, 0.2f, 1.0f);
        _gearFlashTween = CreateTween();
        _gearFlashTween.TweenProperty(button, "modulate", originalModulate, 0.35);
    }

    private partial class CapturingLogger : Logger
    {
        private readonly DebugLog _owner;

        public CapturingLogger(DebugLog owner) { _owner = owner; }

        public override void _LogMessage(string message, bool error)
        {
            _owner.Append(error ? "stderr" : "log", message.TrimEnd());
        }

        public override void _LogError(string function, string file, int line,
                                       string code, string rationale,
                                       bool editorNotify, int errorType,
                                       Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            string kind = errorType switch
            {
                0 => "err",
                1 => "warn",
                2 => "script",
                3 => "shader",
                _ => "err"
            };
            string where = string.IsNullOrEmpty(file) ? function : $"{file}:{line} {function}";
            string body = string.IsNullOrEmpty(rationale) ? code : $"{rationale} ({code})";
            _owner.Append(kind, $"{where} — {body}");
        }
    }
}
