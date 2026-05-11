// =============================================================================
// DebugModal
// =============================================================================
// Purpose:
//   Full-screen Control overlay that displays the captured log entries from
//   DebugLog inside a styled panel. Builds its UI procedurally (dim
//   backdrop, scrollable RichTextLabel, Refresh/Copy/Clear/Close buttons),
//   shows the current entry count, supports clipboard copy with a flash
//   toast, and closes when the dim backdrop is clicked.
//
// Interactions:
//   - DebugLog: queried via DebugLog.Instance.Snapshot(), EntryCount, and
//     Clear() to populate, count, and clear the displayed log buffer.
// =============================================================================

#nullable enable
using Godot;

public partial class DebugModal : Control
{
    public static DebugModal? Instance { get; private set; }

    private ColorRect? _dim;
    private RichTextLabel? _logView;
    private Button? _copyButton;
    private Label? _entryCountLabel;
    private Tween? _copyToast;

    public override void _Ready()
    {
        Instance = this;
        AnchorRight = 1.0f;
        AnchorBottom = 1.0f;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        Build();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private void Build()
    {
        _dim = new ColorRect();
        _dim.Color = new Color(0, 0, 0, 0.55f);
        _dim.AnchorRight = 1.0f;
        _dim.AnchorBottom = 1.0f;
        _dim.MouseFilter = MouseFilterEnum.Stop;
        _dim.GuiInput += OnDimInput;
        AddChild(_dim);

        var center = new CenterContainer();
        center.AnchorRight = 1.0f;
        center.AnchorBottom = 1.0f;
        center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(center);

        var panel = new PanelContainer();
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.12f, 0.14f, 0.18f);
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight =
        style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 14;
        style.ContentMarginLeft = style.ContentMarginRight = 24;
        style.ContentMarginTop = style.ContentMarginBottom = 20;
        style.BorderWidthLeft = style.BorderWidthRight =
        style.BorderWidthTop = style.BorderWidthBottom = 1;
        style.BorderColor = new Color(0.30f, 0.35f, 0.42f);
        panel.AddThemeStyleboxOverride("panel", style);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        vbox.CustomMinimumSize = new Vector2(820, 520);
        panel.AddChild(vbox);

        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(headerRow);

        var title = new Label();
        title.Text = "Debug Log";
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color(0.96f, 0.96f, 0.98f));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerRow.AddChild(title);

        _entryCountLabel = new Label();
        _entryCountLabel.AddThemeFontSizeOverride("font_size", 14);
        _entryCountLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.70f, 0.78f));
        _entryCountLabel.VerticalAlignment = VerticalAlignment.Center;
        headerRow.AddChild(_entryCountLabel);

        _logView = new RichTextLabel();
        _logView.BbcodeEnabled = false;
        _logView.SelectionEnabled = true;
        _logView.ScrollFollowing = true;
        _logView.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _logView.SizeFlagsVertical = SizeFlags.ExpandFill;
        _logView.CustomMinimumSize = new Vector2(800, 420);
        _logView.AddThemeFontSizeOverride("normal_font_size", 13);
        _logView.AddThemeColorOverride("default_color", new Color(0.86f, 0.90f, 0.96f));
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.06f, 0.07f, 0.10f);
        bgStyle.CornerRadiusTopLeft = bgStyle.CornerRadiusTopRight =
        bgStyle.CornerRadiusBottomLeft = bgStyle.CornerRadiusBottomRight = 6;
        bgStyle.ContentMarginLeft = bgStyle.ContentMarginRight = 10;
        bgStyle.ContentMarginTop = bgStyle.ContentMarginBottom = 8;
        _logView.AddThemeStyleboxOverride("normal", bgStyle);
        _logView.AddThemeStyleboxOverride("focus", bgStyle);
        vbox.AddChild(_logView);

        var buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(buttonRow);

        var refreshBtn = MakeButton("Refresh");
        refreshBtn.Pressed += Refresh;
        buttonRow.AddChild(refreshBtn);

        _copyButton = MakeButton("Copy");
        _copyButton.Pressed += OnCopyPressed;
        buttonRow.AddChild(_copyButton);

        var clearBtn = MakeButton("Clear");
        clearBtn.Pressed += OnClearPressed;
        buttonRow.AddChild(clearBtn);

        var spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttonRow.AddChild(spacer);

        var closeBtn = MakeButton("Close");
        closeBtn.Pressed += Close;
        buttonRow.AddChild(closeBtn);
    }

    private static Button MakeButton(string text)
    {
        var b = new Button();
        b.Text = text;
        b.CustomMinimumSize = new Vector2(110, 38);
        b.AddThemeFontSizeOverride("font_size", 16);
        return b;
    }

    public void Open()
    {
        if (Visible) return;
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        DebugLog.PushModal();
        Refresh();
    }

    private void Close()
    {
        if (!Visible) return;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        DebugLog.PopModal();
    }

    private void Refresh()
    {
        if (_logView == null || _entryCountLabel == null) return;
        if (DebugLog.Instance == null)
        {
            _logView.Text = "(DebugLog autoload not registered)";
            _entryCountLabel.Text = "";
            return;
        }
        string snapshot = DebugLog.Instance.Snapshot();
        _logView.Text = string.IsNullOrEmpty(snapshot) ? "(no entries yet)" : snapshot;
        _entryCountLabel.Text = $"{DebugLog.Instance.EntryCount} entries";
    }

    private void OnCopyPressed()
    {
        string text = DebugLog.Instance?.Snapshot() ?? "";
        DisplayServer.ClipboardSet(text);
        FlashCopyButton();
    }

    private void OnClearPressed()
    {
        DebugLog.Instance?.Clear();
        Refresh();
    }

    private void FlashCopyButton()
    {
        if (_copyButton == null) return;
        if (_copyToast != null && _copyToast.IsValid()) _copyToast.Kill();
        _copyButton.Text = "Copied!";
        _copyToast = CreateTween();
        _copyToast.TweenInterval(1.0);
        _copyToast.TweenCallback(Callable.From(() => { if (_copyButton != null) _copyButton.Text = "Copy"; }));
    }

    private void OnDimInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            Close();
            AcceptEvent();
        }
    }
}
