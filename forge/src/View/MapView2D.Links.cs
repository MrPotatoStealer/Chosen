// Chosen core: original code (the 2D view's link lines, UDB's event lines: what an element is tied to).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Shared.Engine.Geometry;
using Godot;

namespace Chosen.Forge.View;

/// <summary>What ties two elements: a channel sent and received, a room-over-room link's two ends, a sector's
/// marker.</summary>
public enum LinkKind { Channel, Link, Marker }

/// <summary>A tie from one element to another (a channel: sender to receiver), its label (the channel, the link,
/// the marker's use); SectorA, SectorB: sectors that also show it when highlighted or selected (a link's two rooms),
/// -1 none.</summary>
public readonly record struct MapLink(Element From, Element To, string Label, LinkKind Kind, int SectorA = -1, int SectorB = -1);

public partial class MapView2D
{
    /// <summary>The map's links (the game module's; cached by the window until the map changes).</summary>
    public Func<List<MapLink>> Links;
    /// <summary>The elements whose links show (the highlighted one, the selection).</summary>
    public Func<IEnumerable<Element>> LinkFocus;
    /// <summary>Every link drawn (faint), not only the focus's.</summary>
    public bool AllLinks;
    /// <summary>The focus's channels followed on (View > Follow Link Chains): what its receivers send in turn, to the
    /// end (a sector entered, the switch it toggles, the sounds the switch plays), and back to what sends to it.</summary>
    public bool LinkChains = true;

    /// <summary>The links shown bright and how far along a chain each is (Blood.MapLinks.Focus).</summary>
    Dictionary<MapLink, int> FocusLinks(List<MapLink> all, HashSet<Element> focus) => Blood.MapLinks.Focus(all, focus, LinkChains);

    /// <summary>The links of the highlighted and selected elements, over everything: a line from sender to receiver
    /// with an arrow at the receiver (a link's two ends: an arrow at each), its label at the middle; with AllLinks
    /// every other link faint under them. Ends in a layer not shown: not drawn; both ends locked: dimmed.</summary>
    void DrawLinks()
    {
        if (Links?.Invoke() is not { Count: > 0 } all)
            return;
        var focus = LinkFocus?.Invoke()?.Where(e => !e.IsNone).ToHashSet() ?? new HashSet<Element>();
        if (focus.Count == 0 && !AllLinks)
            return;
        var lit = FocusLinks(all, focus);
        bool On(MapLink l) => lit.ContainsKey(l);
        var screen = new Rect2(Vector2.Zero, Size).Grow(16);
        var font = ThemeDB.FallbackFont;
        const int size = 10;
        // the faint ones first, the focus's over them
        foreach (bool bright in new[] { false, true })
            foreach (var l in all)
            {
                bool on = On(l);
                if (on != bright || (!on && !AllLinks) || !Shown(l.From) || !Shown(l.To))
                    continue;
                var a = MapToScreen(At(l.From));
                var b = MapToScreen(At(l.To));
                if (!screen.Intersects(new Rect2(a, Vector2.Zero).Expand(b)))
                    continue;
                var c = ColorSettings.Links;
                if (!Editable(l.From) && !Editable(l.To))
                    c = Dim(c);
                if (!on)
                    c.A = 0.4f;
                // (further down a chain: a little lighter and thinner than the focus's own)
                bool far = on && lit[l] > 1;
                if (far)
                    c.A *= 0.75f;
                var d = b - a;
                float len = d.Length();
                if (len < 1)
                    continue;
                var u = d / len;
                DrawLine(a, b, c, on && !far ? 1.5f : 1f, true);
                // (the arrow short of the element's own mark)
                if (len > 20)
                {
                    Arrow(b - u * 6, u, c, on ? 7 : 5);
                    if (l.Kind == LinkKind.Link)
                        Arrow(a + u * 6, -u, c, on ? 7 : 5);
                }
                if (on && len > 48 && l.Label.Length > 0)
                {
                    var mid = (a + b) * 0.5f;
                    var w = font.GetStringSize(l.Label, HorizontalAlignment.Left, -1, size);
                    var at = mid + new Vector2(-u.Y, u.X) * 8 - new Vector2(w.X * 0.5f, -size * 0.35f);
                    DrawString(font, at + Vector2.One, l.Label, HorizontalAlignment.Left, -1, size, new Color(0, 0, 0, c.A));
                    DrawString(font, at, l.Label, HorizontalAlignment.Left, -1, size, c);
                }
            }
    }

    void Arrow(Vector2 tip, Vector2 dir, Color c, float s)
    {
        var n = new Vector2(-dir.Y, dir.X);
        DrawColoredPolygon(new[] { tip, tip - dir * s + n * s * 0.5f, tip - dir * s - n * s * 0.5f }, c);
    }

    /// <summary>Where a link's end is drawn: a sector's label, a line's middle, a sprite.</summary>
    Vector2 At(Element e)
    {
        switch (e.Kind)
        {
            case ElementKind.Sector when e.Index < Doc.Labels.Length:
                return Doc.Labels[e.Index];
            case ElementKind.Line when e.Index < Doc.Lines.Count:
            {
                var (ax, ay, bx, by) = BuildGeom.WallEnds(Doc, Doc.Lines[e.Index].Front);
                return new Vector2((float)(ax + bx) * 0.5f, (float)(ay + by) * 0.5f);
            }
            case ElementKind.Sprite when e.Index < Doc.Sprites.Count:
                return new Vector2(Doc.Sprites[e.Index].X, Doc.Sprites[e.Index].Y);
            case ElementKind.Point when e.Index < Doc.Points.Count:
                return new Vector2(Doc.Points[e.Index].X, Doc.Points[e.Index].Y);
        }
        return Vector2.Zero;
    }

    bool Shown(Element e) => e.Kind switch
    {
        ElementKind.Sector => e.Index < Doc.Sectors.Count && Doc.SectorShown(e.Index),
        ElementKind.Line => e.Index < Doc.Lines.Count && Doc.LineShown(Doc.Lines[e.Index]),
        ElementKind.Sprite => e.Index < Doc.Sprites.Count && Doc.SpriteShown(e.Index),
        _ => false,
    };

    bool Editable(Element e) => e.Kind switch
    {
        ElementKind.Sector => Doc.SectorEditable(e.Index),
        ElementKind.Line => Doc.LineEditable(Doc.Lines[e.Index]),
        ElementKind.Sprite => Doc.SpriteEditable(e.Index),
        _ => false,
    };
}
