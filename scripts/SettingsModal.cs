// =============================================================================
// SettingsModal
// =============================================================================
// Purpose:
//   Sliding right-edge drawer Control built procedurally for in-game
//   settings. Displays a dimmed scrim plus a panel with "Resume" and
//   "Logs" buttons; animates open/close with a Tween. Tapping the scrim or
//   Resume closes the drawer; tapping Logs invokes the supplied callback
//   (used by DebugLog to open the DebugModal).
//
// Interactions:
//   - DebugLog: indirectly — DebugLog constructs SettingsModal and supplies
//     the onLogsRequested callback, which routes back to opening DebugModal.
// =============================================================================

#nullable enable
using Godot;
using System;

namespace HexGame;

public partial class SettingsModal : Control
{
    private const int DrawerWidth = 320;
    private const double SlideDuration = 0.22;

    private readonly Action _onLogsRequested;

    private ColorRect? _scrim;
    private Panel? _drawer;
    private Tween? _slide;
    private bool _open;

    public SettingsModal(Action onLogsRequested)
    {
        _onLogsRequested = onLogsRequested;
    }

    public override void _Ready()
    {
        AnchorRight = 1.0f;
        AnchorBottom = 1.0f;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        Build();
    }

    private void Build()
    {
        _scrim = new ColorRect();
        _scrim.Color = new Color(0, 0, 0, 0f);
        _scrim.AnchorRight = 1.0f;
        _scrim.AnchorBottom = 1.0f;
        _scrim.MouseFilter = MouseFilterEnum.Stop;
        _scrim.GuiInput += OnScrimInput;
        AddChild(_scrim);

        _drawer = new Panel();
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.12f, 0.14f, 0.18f);
        style.BorderWidthLeft = 1;
        style.BorderColor = new Color(0.30f, 0.35f, 0.42f);
        _drawer.AddThemeStyleboxOverride("panel", style);

        _drawer.AnchorLeft = 1.0f;
        _drawer.AnchorRight = 1.0f;
        _drawer.AnchorTop = 0.0f;
        _drawer.AnchorBottom = 1.0f;
        _drawer.OffsetLeft = 0;
        _drawer.OffsetRight = DrawerWidth;
        _drawer.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_drawer);

        var content = new VBoxContainer();
        content.AnchorRight = 1.0f;
        content.AnchorBottom = 1.0f;
        content.OffsetLeft = 20;
        content.OffsetTop = 24;
        content.OffsetRight = -20;
        content.OffsetBottom = -24;
        content.AddThemeConstantOverride("separation", 14);
        _drawer.AddChild(content);

        var title = new Label();
        title.Text = "Settings";
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color(0.96f, 0.96f, 0.98f));
        content.AddChild(title);

        content.AddChild(new HSeparator());

        var resumeBtn = MakeDrawerButton("Resume");
        resumeBtn.Pressed += Close;
        content.AddChild(resumeBtn);

        var soundBtn = MakeDrawerButton(SoundLabel());
        soundBtn.Pressed += () =>
        {
            Sfx.SetEnabled(!Sfx.Enabled);
            soundBtn.Text = SoundLabel();
            Sfx.Play(SfxCue.Select);   // audible confirmation when turning ON
        };
        content.AddChild(soundBtn);

        var volLabel = new Label { Text = "Volume" };
        volLabel.AddThemeFontSizeOverride("font_size", 16);
        volLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.82f, 0.88f));
        content.AddChild(volLabel);

        var volSlider = new HSlider
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            Step = 0.05,
            Value = Sfx.Volume,
            CustomMinimumSize = new Vector2(0, 28),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        volSlider.ValueChanged += v => Sfx.SetVolume((float)v);
        content.AddChild(volSlider);

        var logsBtn = MakeDrawerButton("Logs");
        logsBtn.Pressed += OnLogsPressed;
        content.AddChild(logsBtn);
    }

    private static string SoundLabel() => Sfx.Enabled ? "Sound: On" : "Sound: Off";

    private static Button MakeDrawerButton(string text)
    {
        var b = new Button();
        b.Text = text;
        b.CustomMinimumSize = new Vector2(0, 44);
        b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        b.AddThemeFontSizeOverride("font_size", 18);
        return b;
    }

    public void Open()
    {
        if (_open || _drawer == null || _scrim == null) return;
        _open = true;
        DebugLog.PushModal();
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;

        _scrim.Color = new Color(0, 0, 0, 0f);
        _drawer.OffsetLeft = 0;
        _drawer.OffsetRight = DrawerWidth;

        if (_slide != null && _slide.IsValid()) _slide.Kill();
        _slide = CreateTween().SetParallel(true);
        _slide.TweenProperty(_drawer, "offset_left", -(double)DrawerWidth, SlideDuration)
              .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _slide.TweenProperty(_drawer, "offset_right", 0.0, SlideDuration)
              .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _slide.TweenProperty(_scrim, "color", new Color(0, 0, 0, 0.45f), SlideDuration)
              .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    private void Close()
    {
        if (!_open || _drawer == null || _scrim == null) return;
        _open = false;
        DebugLog.PopModal();

        if (_slide != null && _slide.IsValid()) _slide.Kill();
        _slide = CreateTween().SetParallel(true);
        _slide.TweenProperty(_drawer, "offset_left", 0.0, SlideDuration)
              .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _slide.TweenProperty(_drawer, "offset_right", (double)DrawerWidth, SlideDuration)
              .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _slide.TweenProperty(_scrim, "color", new Color(0, 0, 0, 0f), SlideDuration)
              .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _slide.Chain().TweenCallback(Callable.From(OnCloseFinished));
    }

    private void OnCloseFinished()
    {
        if (_open) return;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private void OnLogsPressed()
    {
        // Close INSTANTLY (not the 0.22s slide) before opening the log viewer:
        // an animated close would leave this drawer's scrim fading over the
        // freshly-opened DebugModal, eating its input and flashing a dim overlay.
        CloseInstant();
        _onLogsRequested?.Invoke();
    }

    private void CloseInstant()
    {
        if (!_open) return;
        _open = false;
        DebugLog.PopModal();
        if (_slide != null && _slide.IsValid()) _slide.Kill();
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private void OnScrimInput(InputEvent ev)
    {
        if (InputUtil.IsPrimaryPress(ev)) { Close(); AcceptEvent(); }
    }
}
