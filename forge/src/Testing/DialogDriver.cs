// Chosen core: original code.
using System;
using System.Linq;
using Chosen.Forge.Recording;
using Godot;

namespace Chosen.Forge.Testing;

/// <summary>
/// Fills a dialog as a user does (the UI test, the rebuild test): a field found by its tab and its label, set as if
/// typed (its signals fire); a check box by its text; the dialog's own OK button pressed.
/// </summary>
public static class DialogDriver
{
    /// <summary>The tab of that name (or the dialog itself when it has no tabs).</summary>
    static Node Tab(Window d, string tab)
    {
        if (tab == null)
            return d;
        var tabs = d.FindChildren("*", nameof(TabContainer), true, false).OfType<TabContainer>().FirstOrDefault();
        return tabs?.GetChildren().FirstOrDefault(c => c.Name == tab) ?? throw new ArgumentException($"no tab {tab} in {d.Title}");
    }

    /// <summary>The field after the label of that text in a grid (a text box, a spin box, the first text box of a row).</summary>
    public static Control Field(Window d, string tab, string label)
    {
        foreach (var l in Tab(d, tab).FindChildren("*", nameof(Label), true, false).OfType<Label>())
        {
            if (l.Text != label || l.GetParent() is not GridContainer g)
                continue;
            var next = g.GetChild(l.GetIndex() + 1);
            if (next is LineEdit or SpinBox)
                return (Control)next;
            if (next.FindChildren("*", nameof(LineEdit), true, false).FirstOrDefault() is LineEdit e)
                return e;
        }
        throw new ArgumentException($"no field {label} in {d.Title}/{tab}");
    }

    public static bool Has(Window d, string tab, string label)
    {
        try
        {
            Field(d, tab, label);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>A field's value typed (when it differs: an untouched field stays as the dialog showed it).</summary>
    public static void Set(Window d, string tab, string label, string value)
    {
        var f = Field(d, tab, label);
        if (SessionRecorder.ValueOf(f) != value)
            SessionRecorder.SetValue(f, value);
    }

    /// <summary>A check box (by its text, "(mixed)" aside) ticked or not.</summary>
    public static bool Check(Window d, string tab, string text, bool on)
    {
        foreach (var c in Tab(d, tab).FindChildren("*", nameof(CheckBox), true, false).OfType<CheckBox>())
            if (c.Text == text || c.Text == text + " (mixed)")
            {
                if (c.ButtonPressed != on)
                    c.ButtonPressed = on;
                return true;
            }
        return false;
    }

    /// <summary>OK (Apply) pressed.</summary>
    public static void Ok(Window d) => ((AcceptDialog)d).GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
}
