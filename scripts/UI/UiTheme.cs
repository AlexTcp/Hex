// =============================================================================
// UiTheme
// =============================================================================
// Purpose:
//   The single Premium Slate visual language for all 2D UI: the colour palette,
//   a coded Godot Theme (default Label/font styling + Primary/Secondary/Ghost
//   button type-variations), and small factory helpers (buttons, labels, chips,
//   panels, stylebox builder) so each screen stays short and on-brand. Assign
//   Build() to the UI root once; children inherit it. No font assets are used —
//   the project default font is styled via sizes/colours only.
//
// Interactions:
//   - ScreenManager assigns Build() to UI/Root and uses the factories.
//   - TitleScreen / NewRunScreen / ShopScreen / Hud / PauseOverlay / GameOverScreen /
//     TutorialOverlay build their controls from these helpers.
// =============================================================================

using Godot;

namespace HexGame.UI;

public static class UiTheme
{
    // ----- Palette (sRGB) -------------------------------------------------
    public static readonly Color Bg = new(0.043f, 0.055f, 0.078f);
    public static readonly Color Panel = new(0.086f, 0.106f, 0.149f);
    public static readonly Color PanelBorder = new(0.165f, 0.196f, 0.259f);
    public static readonly Color PanelRaised = new(0.118f, 0.145f, 0.200f);
    public static readonly Color Border = new(0.227f, 0.267f, 0.337f);
    public static readonly Color Text = new(0.902f, 0.918f, 0.949f);
    public static readonly Color TextMuted = new(0.541f, 0.576f, 0.651f);
    public static readonly Color Accent = new(0.910f, 0.698f, 0.227f);       // gold
    public static readonly Color AccentBright = new(0.965f, 0.788f, 0.353f);
    public static readonly Color AccentDim = new(0.612f, 0.467f, 0.141f);
    public static readonly Color AccentOnDark = new(0.102f, 0.071f, 0.020f); // text ON gold
    public static readonly Color Danger = new(0.847f, 0.271f, 0.231f);
    public static readonly Color DangerDim = new(0.431f, 0.118f, 0.102f);
    public static readonly Color Success = new(0.310f, 0.698f, 0.525f);

    // ----- Type sizes (authored @ 1280x720; canvas stretch scales them) ---
    public const int TitleSize = 96;
    public const int HeadingSize = 40;
    public const int SectionSize = 22;
    public const int BodySize = 24;
    public const int BodySmallSize = 20;
    public const int ButtonSize = 28;
    public const int ChipSize = 22;
    public const int HudPrimarySize = 34;
    public const int HudSecondarySize = 24;
    public const int ComboSize = 56;

    // ----- Theme ----------------------------------------------------------
    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = BodySize };
        t.SetColor("font_color", "Label", Text);

        // Default Button reads as the Secondary variation (so unstyled buttons
        // still look right).
        DefineButton(t, "Button",
            normal: Box(PanelRaised, 12, 1, Border, 28, 16),
            hover: Box(new Color(0.153f, 0.188f, 0.259f), 12, 1, Accent, 28, 16),
            pressed: Box(Panel, 12, 1, Border, 28, 16),
            disabled: Box(Panel, 12, 1, Border, 28, 16),
            font: Text, fontHover: Text, fontPressed: TextMuted, fontDisabled: new Color(Text, 0.4f));

        DefineButton(t, "ButtonPrimary",
            normal: Box(Accent, 12, 0, null, 28, 16, new Color(0, 0, 0, 0.35f), 6, new Vector2(0, 3)),
            hover: Box(AccentBright, 12, 0, null, 28, 16, new Color(0, 0, 0, 0.35f), 6, new Vector2(0, 3)),
            pressed: Box(AccentDim, 12, 0, null, 28, 16, new Color(0, 0, 0, 0.35f), 2, new Vector2(0, 1)),
            disabled: Box(new Color(0.353f, 0.306f, 0.180f, 0.5f), 12, 0, null, 28, 16),
            font: AccentOnDark, fontHover: AccentOnDark, fontPressed: AccentOnDark,
            fontDisabled: new Color(AccentOnDark, 0.5f));

        DefineButton(t, "ButtonSecondary",
            normal: Box(PanelRaised, 12, 1, Border, 28, 16),
            hover: Box(new Color(0.153f, 0.188f, 0.259f), 12, 1, Accent, 28, 16),
            pressed: Box(Panel, 12, 1, Border, 28, 16),
            disabled: Box(Panel, 12, 1, Border, 28, 16),
            font: Text, fontHover: Text, fontPressed: TextMuted, fontDisabled: new Color(Text, 0.4f));

        DefineButton(t, "ButtonGhost",
            normal: Box(new Color(0, 0, 0, 0), 12, 1, Border, 28, 16),
            hover: Box(new Color(0, 0, 0, 0), 12, 1, Text, 28, 16),
            pressed: Box(Panel, 12, 1, Border, 28, 16),
            disabled: Box(new Color(0, 0, 0, 0), 12, 1, Border, 28, 16),
            font: TextMuted, fontHover: Text, fontPressed: TextMuted, fontDisabled: new Color(TextMuted, 0.4f));

        DefineButton(t, "ButtonDanger",
            normal: Box(new Color(0, 0, 0, 0), 12, 1, Border, 28, 16),
            hover: Box(DangerDim, 12, 1, Danger, 28, 16),
            pressed: Box(Panel, 12, 1, Danger, 28, 16),
            disabled: Box(new Color(0, 0, 0, 0), 12, 1, Border, 28, 16),
            font: TextMuted, fontHover: Danger, fontPressed: Danger, fontDisabled: new Color(TextMuted, 0.4f));

        return t;
    }

    private static void DefineButton(Theme t, string variation,
        StyleBoxFlat normal, StyleBoxFlat hover, StyleBoxFlat pressed, StyleBoxFlat disabled,
        Color font, Color fontHover, Color fontPressed, Color fontDisabled)
    {
        if (variation != "Button") t.SetTypeVariation(variation, "Button");
        t.SetStylebox("normal", variation, normal);
        t.SetStylebox("hover", variation, hover);
        t.SetStylebox("pressed", variation, pressed);
        t.SetStylebox("disabled", variation, disabled);
        t.SetStylebox("focus", variation, new StyleBoxEmpty());
        t.SetColor("font_color", variation, font);
        t.SetColor("font_hover_color", variation, fontHover);
        t.SetColor("font_pressed_color", variation, fontPressed);
        t.SetColor("font_focus_color", variation, font);
        t.SetColor("font_disabled_color", variation, fontDisabled);
        t.SetFontSize("font_size", variation, ButtonSize);
    }

    // ----- StyleBox builder ----------------------------------------------
    public static StyleBoxFlat Box(Color bg, int radius, int border = 0, Color? borderColor = null,
        int marginH = 0, int marginV = 0, Color? shadow = null, int shadowSize = 0, Vector2 shadowOffset = default)
    {
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.SetCornerRadiusAll(radius);
        if (border > 0)
        {
            sb.SetBorderWidthAll(border);
            sb.BorderColor = borderColor ?? PanelBorder;
        }
        if (marginH > 0) { sb.ContentMarginLeft = marginH; sb.ContentMarginRight = marginH; }
        if (marginV > 0) { sb.ContentMarginTop = marginV; sb.ContentMarginBottom = marginV; }
        if (shadow.HasValue)
        {
            sb.ShadowColor = shadow.Value;
            sb.ShadowSize = shadowSize;
            sb.ShadowOffset = shadowOffset;
        }
        return sb;
    }

    // ----- Control factories ---------------------------------------------
    public static Button PrimaryButton(string text) => MakeButton(text, "ButtonPrimary");
    public static Button SecondaryButton(string text) => MakeButton(text, "ButtonSecondary");
    public static Button GhostButton(string text) => MakeButton(text, "ButtonGhost");
    public static Button DangerButton(string text) => MakeButton(text, "ButtonDanger");

    private static Button MakeButton(string text, string variation)
    {
        var b = new Button { Text = text, ThemeTypeVariation = variation };
        b.CustomMinimumSize = new Vector2(0, 76);
        b.Pressed += () => Sfx.Play("select", -12f);
        return b;
    }

    public static Label MakeLabel(string text, int size, Color color,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    public static Label Heading(string text) =>
        MakeLabel(text, HeadingSize, Text, HorizontalAlignment.Center);

    public static Label Body(string text) =>
        MakeLabel(text, BodySize, Text, HorizontalAlignment.Center);

    public static Label Muted(string text, int size) =>
        MakeLabel(text, size, TextMuted, HorizontalAlignment.Center);

    // A small rounded pill containing one line of text.
    public static PanelContainer Chip(string text, int fontSize, Color textColor)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(PanelRaised, 10, 1, PanelBorder, 16, 8));
        panel.AddChild(MakeLabel(text, fontSize, textColor, HorizontalAlignment.Center));
        return panel;
    }

    public static PanelContainer ModalPanel()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel",
            Box(Panel, 18, 1, PanelBorder, 32, 32, new Color(0, 0, 0, 0.5f), 24, new Vector2(0, 8)));
        return panel;
    }

    // ----- Vignette -------------------------------------------------------
    // Compatibility renderer has no Environment glow/vignette, so the frame
    // darkening is a full-screen canvas_item shader (one draw, zero assets,
    // reliable on low-end Mali/Adreno drivers). Returns a full-rect ColorRect
    // that never eats input.
    private const string VignetteCode =
        "shader_type canvas_item;\n" +
        "uniform vec4 edge_color : source_color = vec4(0.0,0.0,0.0,1.0);\n" +
        "uniform float inner = 0.42;\n" +
        "uniform float outer = 1.0;\n" +
        "uniform float strength = 0.78;\n" +
        "void fragment() {\n" +
        "    vec2 d = UV - vec2(0.5);\n" +
        "    float r = length(d) * 1.41421356;\n" +
        "    float a = smoothstep(inner, outer, r) * strength;\n" +
        "    COLOR = vec4(edge_color.rgb, a);\n" +
        "}\n";

    private static Shader _vignetteShader;

    public static ColorRect Vignette(Color edge, float strength)
    {
        _vignetteShader ??= new Shader { Code = VignetteCode };
        var mat = new ShaderMaterial { Shader = _vignetteShader };
        mat.SetShaderParameter("edge_color", edge);
        mat.SetShaderParameter("strength", strength);
        var rect = new ColorRect { Material = mat, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }
}
