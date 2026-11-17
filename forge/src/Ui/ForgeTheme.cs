// Chosen core: original code.
using Godot;

namespace Chosen.Forge.Ui;

/// <summary>
/// Forge's look: a compact dark theme for every control and dialog (set on the root window): 13 px text, tight
/// spacing, buttons that show hover, press and the toggled state, flat inputs, tabs that read as tabs.
/// </summary>
public static class ForgeTheme
{
    public static readonly Color Back = new(0.13f, 0.13f, 0.15f);
    public static readonly Color Panel = new(0.17f, 0.17f, 0.19f);
    public static readonly Color Raised = new(0.23f, 0.23f, 0.26f);
    public static readonly Color Hover = new(0.30f, 0.30f, 0.34f);
    public static readonly Color Accent = new(0.85f, 0.45f, 0.15f);
    public static readonly Color Text = new(0.88f, 0.88f, 0.88f);
    public static readonly Color Dim = new(0.62f, 0.62f, 0.66f);
    public const int FontSize = 13;

    static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 3, int padX = 6, int padY = 3)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius, ContentMarginLeft = padX, ContentMarginRight = padX, ContentMarginTop = padY,
            ContentMarginBottom = padY,
        };
        if (border is { } b)
        {
            s.BorderColor = b;
            s.BorderWidthLeft = s.BorderWidthRight = s.BorderWidthTop = s.BorderWidthBottom = 1;
        }
        return s;
    }

    public static Theme Make()
    {
        var t = new Theme { DefaultFontSize = FontSize };
        foreach (var type in new[] { "Button", "CheckBox", "CheckButton", "OptionButton", "MenuButton", "ColorPickerButton" })
        {
            // a check box shows its state by its tick: only a hover tint behind it
            bool check = type == "CheckBox" || type == "CheckButton";
            t.SetStylebox("normal", type, check ? Box(new Color(0, 0, 0, 0), null, 3, 2, 1) : Box(Raised));
            t.SetStylebox("hover", type, check ? Box(Hover with { A = 0.6f }, null, 3, 2, 1) : Box(Hover));
            t.SetStylebox("pressed", type, check ? Box(new Color(0, 0, 0, 0), null, 3, 2, 1) : Box(Accent.Darkened(0.35f), Accent));
            t.SetStylebox("hover_pressed", type, check ? Box(Hover with { A = 0.6f }, null, 3, 2, 1) : Box(Accent.Darkened(0.2f), Accent));
            t.SetStylebox("disabled", type, Box(Panel));
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Colors.White);
            t.SetColor("font_hover_pressed_color", type, Colors.White);
            t.SetColor("font_disabled_color", type, Dim.Darkened(0.3f));
            t.SetConstant("h_separation", type, 4);
        }
        // flat toolbar buttons still show hover and press
        t.SetTypeVariation("FlatButton", "Button");
        t.SetStylebox("normal", "FlatButton", Box(new Color(0, 0, 0, 0)));
        foreach (var type in new[] { "LineEdit", "SpinBox", "TextEdit" })
        {
            t.SetStylebox("normal", type, Box(Back, Raised, 2, 5, 2));
            t.SetStylebox("focus", type, Box(Back, Accent, 2, 5, 2));
            t.SetStylebox("read_only", type, Box(Panel, Raised, 2, 5, 2));
            t.SetColor("font_color", type, Text);
        }
        t.SetStylebox("panel", "Panel", Box(Panel, null, 0, 0, 0));
        t.SetStylebox("panel", "PanelContainer", Box(Panel, null, 0, 6, 4));
        t.SetStylebox("panel", "TabContainer", Box(Panel, null, 0, 6, 4));
        t.SetStylebox("tab_selected", "TabContainer", Box(Panel, null, 3, 10, 4));
        t.SetStylebox("tab_unselected", "TabContainer", Box(Back, null, 3, 10, 4));
        t.SetStylebox("tab_hovered", "TabContainer", Box(Hover, null, 3, 10, 4));
        t.SetColor("font_selected_color", "TabContainer", Colors.White);
        t.SetColor("font_unselected_color", "TabContainer", Dim);
        t.SetColor("font_color", "Label", Text);
        t.SetStylebox("panel", "PopupMenu", Box(Panel, Raised, 2, 4, 4));
        t.SetStylebox("hover", "PopupMenu", Box(Accent.Darkened(0.3f), null, 2));
        t.SetConstant("v_separation", "PopupMenu", 4);
        t.SetStylebox("panel", "ItemList", Box(Back, null, 0, 2, 2));
        t.SetStylebox("panel", "Tree", Box(Back, null, 0, 2, 2));
        t.SetStylebox("selected", "Tree", Box(Accent.Darkened(0.4f)));
        t.SetStylebox("selected_focus", "Tree", Box(Accent.Darkened(0.3f)));
        t.SetStylebox("focus", "Tree", new StyleBoxEmpty());
        t.SetConstant("v_separation", "Tree", 1);
        t.SetConstant("item_margin", "Tree", 12);
        t.SetStylebox("selected", "ItemList", Box(Accent.Darkened(0.4f)));
        t.SetStylebox("selected_focus", "ItemList", Box(Accent.Darkened(0.3f)));
        t.SetConstant("v_separation", "ItemList", 1);
        t.SetStylebox("panel", "AcceptDialog", Box(Panel, null, 0, 10, 8));
        t.SetStylebox("embedded_border", "Window", Box(Panel, Raised, 0, 0, 0));
        // tight spacing in the forms and boxes
        t.SetConstant("separation", "VBoxContainer", 3);
        t.SetConstant("separation", "HBoxContainer", 4);
        t.SetConstant("h_separation", "GridContainer", 8);
        t.SetConstant("v_separation", "GridContainer", 2);
        t.SetConstant("separation", "HSplitContainer", 6);
        t.SetConstant("separation", "VSplitContainer", 6);
        t.SetStylebox("grabber", "ProgressBar", Box(Accent));
        return t;
    }
}
