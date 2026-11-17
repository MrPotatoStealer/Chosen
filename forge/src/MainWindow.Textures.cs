// Chosen core: original code. The 3D preview's texture editing (UDB's visual mode): panning, repeats, shade and
// flips by key, auto-align, offsets copied and pasted, and the Texture Settings dialog with its live preview.
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Config;
using Chosen.Forge.Tools;
using Chosen.Forge.Ui;
using Chosen.Forge.View;
using Godot;

namespace Chosen.Forge;

public partial class MainWindow
{
    /// <summary>The offsets Shift+C took (panning and repeats), for Shift+V.</summary>
    Dictionary<string, int> _offsetClip;

    static string Codes(IEnumerable<VisualTarget> targets) => string.Join(" ", targets.Select(Preview3D.Code));

    /// <summary>One undo level for a run of the same change on the same targets while it is the newest level (the
    /// wheel's heights, the arrows' panning...).</summary>
    void MergeUndo(string key, string name)
    {
        if (key != _visualKey || Undo.Top == null || Undo.Top != _visualTop)
        {
            Undo.CreateUndo(name);
            _visualKey = key;
            _visualTop = Undo.Top;
        }
    }

    /// <summary>The preview's built-in texture keys: the arrows pan (Shift 8, Ctrl the grid), keypad 6 / 4, 8 / 5 (2)
    /// and 9 / 7 the repeats, keypad + / - the shade (Shift or Ctrl 8). On the selection (as the wheel),
    /// else what the crosshair is on.</summary>
    bool PreviewTextureKey(InputEventKey k)
    {
        if (k.AltPressed)
            return false;
        int pan = k.CtrlPressed ? Math.Clamp(View.GridSize, 1, 128) : k.ShiftPressed ? 8 : 1;
        int r = k.CtrlPressed || k.ShiftPressed ? 8 : 1;
        string cmd = k.Keycode switch
        {
            // (the texture moves the way of the arrow: Build's panning the other way along x, down the picture for y)
            Key.Left => $"pan {pan} 0",
            Key.Right => $"pan {-pan} 0",
            Key.Up => $"pan 0 {pan}",
            Key.Down => $"pan 0 {-pan}",
            Key.Kp6 => $"repeat {r} 0",
            Key.Kp4 => $"repeat {-r} 0",
            Key.Kp8 => $"repeat 0 {r}",
            Key.Kp5 or Key.Kp2 => $"repeat 0 {-r}",
            Key.Kp9 => $"repeat {r} {r}",
            Key.Kp7 => $"repeat {-r} {-r}",
            Key.KpAdd => $"shade {-r}",
            Key.KpSubtract => $"shade {r}",
            _ => null,
        };
        if (cmd == null)
            return false;
        var targets = PreviewTargets(true, true);
        // the arrows move lights as they move sprites (along the map axis nearest to where the camera looks, by the
        // grid; Shift an eighth of it: a run on the same lights one undo step); nothing else of them
        if (k.Keycode is Key.Left or Key.Right or Key.Up or Key.Down && targets.Any(t => t.IsLight))
        {
            int step = k.ShiftPressed ? Math.Max(1, View.GridSize / 8) : View.GridSize;
            var (fwd, right) = _preview.ViewAxes;
            var dir = k.Keycode switch { Key.Up => fwd, Key.Down => -fwd, Key.Right => right, _ => -right };
            UiDo($"lightmove {dir.X * step} {dir.Y * step} 0 {string.Join(",", targets.Where(t => t.IsLight).Select(t => t.Light))} run");
        }
        targets = targets.Where(t => !t.IsLight).ToList();
        // the arrows move sprites (UDB's): along the map axis nearest to where the camera looks, by the grid (Shift an
        // eighth of it); the rest of the selection pans
        if (k.Keycode is Key.Left or Key.Right or Key.Up or Key.Down && targets.Any(t => t.IsSprite))
        {
            int step = k.ShiftPressed ? Math.Max(1, View.GridSize / 8) : View.GridSize;
            var (fwd, right) = _preview.ViewAxes;
            var camRight = _preview.CameraRight;
            var moves = new List<string>();
            foreach (var t in targets.Where(t => t.IsSprite))
            {
                var sp = Doc.Sprites[t.Sprite];
                int dx = 0, dy = 0, dz = 0;
                if ((sp.Cstat & 48) == 16)
                {
                    // a wall sprite along its wall (left and right as seen), up and down it (16 z a unit)
                    if (k.Keycode is Key.Up or Key.Down)
                        dz = (k.Keycode == Key.Up ? -1 : 1) * step * 16;
                    else
                    {
                        double a = sp.Ang * Math.PI / 1024 + Math.PI / 2;
                        double ax = Math.Cos(a), ay = Math.Sin(a);
                        if (ax * camRight.X + ay * camRight.Y < 0)
                            (ax, ay) = (-ax, -ay);
                        int sign = k.Keycode == Key.Right ? 1 : -1;
                        dx = (int)Math.Round(ax * step * sign);
                        dy = (int)Math.Round(ay * step * sign);
                    }
                }
                else
                {
                    var dir = k.Keycode switch { Key.Up => fwd, Key.Down => -fwd, Key.Right => right, _ => -right };
                    (dx, dy) = (dir.X * step, dir.Y * step);
                }
                moves.Add($"{dx},{dy},{dz}:{Preview3D.Code(t)}");
            }
            UiDo("visualmove " + string.Join(" ", moves));
            targets = targets.Where(t => !t.IsSprite).ToList();
        }
        if (targets.Count > 0)
            UiDo($"visualtex {cmd} {Codes(targets)}");
        return true;
    }

    /// <summary>The preview's texture actions (keys.cfg's visual...; the context menu): each made a command on the
    /// targets as they are now (recorded and replayed as any other).</summary>
    bool PreviewTextureAction(string action)
    {
        var all = PreviewTargets(true, true);
        var targets = all.Where(t => !t.IsLight).ToList();
        switch (action)
        {
            // slope edges on or off (a view setting: not recorded as an edit)
            case "visuallightsmode":
                ToggleLightsMode3D();
                return true;
            case "visualslopeedges":
                _preview.SlopeMode = !_preview.SlopeMode;
                DisplayStatus(_preview.SlopeMode
                    ? "slope mode: aim at a floor or ceiling: its plane, from the first wall (green) to the far edge (cyan); click a half to lock onto it, the wheel moves it (Shift one game unit, Ctrl four steps): the far half tilts the plane about the first wall, the first wall's half moves that side; Shift+click a wall: make it the first wall; clicks select nothing. H again: off"
                    : "slope edges off: the wheel raises and lowers floors and ceilings");
                return true;
            case "visualautoalign":
            case "visualautoalignx":
            case "visualautoaligny":
            {
                // a floor or ceiling aimed at: its picture carried on into its neighbours (the selected ones when
                // several are selected)
                if (action == "visualautoalign" && _preview.Target is { IsPlane: true } pt)
                {
                    var planes = new List<VisualTarget> { pt };
                    if (_preview.Selected.Count > 1)
                        planes.AddRange(_preview.Selected.Where(x => x.Part == pt.Part && x != pt));
                    UiDo($"visualtex alignplanes {Codes(planes)}");
                    return true;
                }
                // from the wall aimed at (else the first selected), along the selection when several are selected
                var start = _preview.Target is { IsWall: true } t ? t : _preview.Selected.FirstOrDefault(x => x.IsWall);
                if (!start.IsWall)
                {
                    DisplayStatus("auto-align: aim at a wall (its picture along the walls next to it) or a floor or ceiling (into the sectors next to it)");
                    return true;
                }
                var list = new List<VisualTarget> { start };
                if (_preview.Selected.Count > 1)
                    list.AddRange(_preview.Selected.Where(x => x.IsWall && x != start));
                string how = action == "visualautoalignx" ? "x" : action == "visualautoaligny" ? "y" : "xy";
                UiDo($"visualtex align {how} {Codes(list)}");
                return true;
            }
            case "visualresettexture":
                if (targets.Count > 0)
                    UiDo($"visualtex reset {Codes(targets)}");
                return true;
            case "visualflip":
                if (targets.Count > 0)
                    UiDo($"visualtex flip {Codes(targets)}");
                return true;
            case "visualalignbottom":
                if (targets.Count > 0)
                    UiDo($"visualtex bottom {Codes(targets)}");
                return true;
            case "visualcopyoffsets":
                if (_preview.Target is { } ct)
                    UiDo($"visualtex copy {Preview3D.Code(ct)}");
                return true;
            case "visualpasteoffsets":
                if (_offsetClip == null)
                    DisplayStatus("paste offsets: none copied yet (Shift+C on a surface)");
                else if (targets.Count > 0)
                    UiDo($"visualtex set {TextureOps.Encode(_offsetClip)} {Codes(targets)}");
                return true;
            case "visualtexturesettings":
                TextureSettings(targets);
                return true;
            case "visualuse":
                PlayUse();
                return true;
            case "visualinsert":
                // (Lights mode: a light)
                if (Editing.Mode is Editing.LightsMode)
                    PlaceLightAtCrosshair();
                else
                    PlaceAtCrosshair();
                return true;
            case "visualdelete":
            {
                var gone = all.Where(t => t.IsSprite || t.IsLight).ToList();
                if (gone.Count == 0)
                    DisplayStatus("delete: select sprites or lights (or aim at one); walls and sectors are deleted in 2D");
                else
                    UiDo("visualdelete " + Codes(gone));
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// "visualtex op [args] targets": pan dx dy, repeat dx dy, shade ds (a run of the same on the same targets one undo
    /// step), flip (none, x, x and y, y in turn), bottom (a wall's align bottom; a floor's or ceiling's relative
    /// alignment), reset, align xy|x|y (from the first target; the others: the walls it may change), alignplanes (a
    /// floor's or ceiling's picture carried on into the sectors next to it; the others: the sectors it may change),
    /// floortowall / ceilingtowall (a wall's sector's floor or ceiling along it: TextureOps.AlignPlaneToWall), copy
    /// (the first's offsets), set name=value,... (Texture Settings, pasted offsets).
    /// </summary>
    void VisualTex(string action)
    {
        var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3)
            return;
        string op = p[1];
        int argc = op switch { "pan" or "repeat" => 2, "shade" or "align" or "set" or "paste" => 1, _ => 0 };
        if (p.Length < 3 + argc)
            return;
        var args = p.Skip(2).Take(argc).ToArray();
        var targets = p.Skip(2 + argc).Select(c => Preview3D.Parse(Doc, c)).Where(t => t != null).Select(t => t.Value).ToList();
        if (targets.Count == 0)
            return;
        if (_play != null)
        {
            DisplayStatus("Play is on: stop it (P) to change textures");
            return;
        }
        int A(int i) => int.TryParse(args[i], out int v) ? v : 0;
        string key = op + " " + Codes(targets);
        string what = targets.Count == 1 ? DescribeTarget(targets[0]) : $"{targets.Count} surfaces";
        var t0 = targets[0];
        switch (op)
        {
            case "copy":
            {
                var f = TextureOps.Fields(Doc, t0);
                _offsetClip = f.Where(kv => kv.Key is "panx" or "pany" or "repx" or "repy").ToDictionary(kv => kv.Key, kv => kv.Value);
                DisplayStatus($"offsets of {what} copied ({TextureOps.Encode(_offsetClip)}): Shift+V pastes them");
                return;
            }
            case "pan":
                MergeUndo(key, "Move texture");
                foreach (var t in targets)
                {
                    if (t.IsSprite)
                        continue;
                    int dx = A(0), dy = A(1);
                    // (an x-flipped picture runs from the wall's end: its panning moves it the other way; a y flip
                    // turns the picture over in place, more ypanning still moves it up)
                    if (t.IsWall && (Doc.Walls[t.Wall].Cstat & 8) != 0)
                        dx = -dx;
                    TextureOps.Pan(Doc, t, dx, dy);
                }
                break;
            case "repeat":
                MergeUndo(key, "Change texture repeats");
                if (!targets.Aggregate(false, (any, t) => TextureOps.Repeat(Doc, t, A(0), A(1)) | any))
                {
                    Undo.WithdrawUndo();
                    _visualKey = null;
                    DisplayStatus("floors and ceilings have no repeats (Texture Settings: double size)");
                    return;
                }
                break;
            case "shade":
                MergeUndo(key, "Change shade");
                foreach (var t in targets)
                    TextureOps.Shade(Doc, t, A(0));
                break;
            case "flip":
                Fresh("Flip texture");
                foreach (var t in targets)
                {
                    bool x = TextureOps.GetFlag(Doc, t, "flipx") == true, y = TextureOps.GetFlag(Doc, t, "flipy") == true;
                    // none, x, x and y, y, none
                    (x, y) = (x, y) switch { (false, false) => (true, false), (true, false) => (true, true), (true, true) => (false, true), _ => (false, false) };
                    TextureOps.SetFlag(Doc, t, "flipx", x);
                    TextureOps.SetFlag(Doc, t, "flipy", y);
                }
                break;
            case "bottom":
                Fresh("Toggle texture alignment");
                foreach (var t in targets)
                {
                    string flag = t.IsPlane ? "relative" : "bottom";
                    if (TextureOps.GetFlag(Doc, t, flag) is { } on)
                        TextureOps.SetFlag(Doc, t, flag, !on);
                }
                break;
            case "reset":
                Fresh("Reset texture");
                foreach (var t in targets)
                    TextureOps.Reset(Doc, t);
                break;
            case "alignplanes":
            {
                Fresh(t0.Part == VisualPart.Ceiling ? "Auto-align ceilings" : "Auto-align floors");
                var limit = targets.Count > 1 ? targets.Select(t => t.Sector).ToHashSet() : null;
                var (n, left) = TextureOps.AlignPlanes(Doc, t0, limit);
                if (n == 0)
                {
                    Undo.WithdrawUndo();
                    DisplayStatus($"auto-align: no sector next to {what} has its picture ({TextureOps.Tile(Doc, t0)})"
                        + (left > 0 ? $", or its first wall runs another way ({left}: the picture runs along the first wall)" : ""));
                    return;
                }
                Doc.NotifyChanged();
                UndoChanged();
                DisplayStatus($"auto-align: {n} {(t0.Part == VisualPart.Ceiling ? "ceiling" : "floor")}(s) carry on {what}'s picture"
                    + (left > 0 ? $"; {left} left (their first wall runs another way than this relative one's)" : ""));
                return;
            }
            case "floortowall":
            case "ceilingtowall":
            {
                if (!t0.IsWall)
                    return;
                bool ceil = op == "ceilingtowall";
                // (it turns the sector's walls round: a change of shape, not while the moving sectors are shown off or on)
                if (Posed)
                {
                    DisplayStatus(ShownLocked);
                    return;
                }
                // (the sector taken first: making the wall first renumbers the walls)
                int sector = Doc.Walls[t0.Wall].Sector;
                Fresh(ceil ? "Align ceiling to wall" : "Align floor to wall");
                if (TextureOps.AlignPlaneToWall(Doc, t0.Wall, ceil) is { } why)
                {
                    Undo.WithdrawUndo();
                    DisplayStatus($"align {(ceil ? "ceiling" : "floor")} to wall {t0.Wall}: {why}");
                    return;
                }
                Doc.NotifyChanged();
                UndoChanged();
                DisplayStatus($"sector {sector}'s {(ceil ? "ceiling" : "floor")} runs along the wall from its start (its first wall now, the picture relative to it)");
                return;
            }
            case "align":
            {
                Fresh("Auto-align textures");
                bool ax = args[0].Contains('x'), ay = args[0].Contains('y');
                var limit = targets.Count > 1 ? targets.Where(t => t.IsWall).Select(t => t.Wall).ToHashSet() : null;
                int n = TextureOps.AutoAlign(Doc, t0, ax, ay, limit);
                if (n == 0)
                {
                    Undo.WithdrawUndo();
                    DisplayStatus($"auto-align: no wall next to {what} has its picture ({TextureOps.Tile(Doc, t0)})");
                    return;
                }
                Doc.NotifyChanged();
                UndoChanged();
                DisplayStatus(TextureOps.IsOpening(Doc, t0)
                    ? $"auto-align ({args[0]}): {n} masked opening(s) aligned to {DescribeTarget(t0)}" + SharedNote(targets.Prepend(t0), true)
                    : $"auto-align ({args[0]}): {n} wall part(s) aligned to {DescribeTarget(t0)}");
                return;
            }
            // Ctrl+V: the picture Ctrl+C copied and its settings (another kind of surface: the picture)
            case "paste":
            {
                Fresh("Paste texture");
                var clip = TextureOps.Decode(args[0]);
                foreach (var t in targets)
                    TextureOps.Paste(Doc, t, clip);
                Doc.NotifyChanged();
                UndoChanged();
                int same = targets.Count(t => (t.IsWall ? 0 : t.IsPlane ? 1 : 2) == clip.GetValueOrDefault("kind", -1));
                DisplayStatus($"pasted tile {clip.GetValueOrDefault("tile")} on {what}"
                    + (same > 0 ? $" with its settings ({same} of the same kind)" : " (another kind: the picture alone)"));
                return;
            }
            case "set":
            {
                Fresh("Texture settings");
                var f = TextureOps.Decode(args[0]);
                foreach (var t in targets)
                    TextureOps.SetFields(Doc, t, f);
                break;
            }
            default:
                return;
        }
        Doc.NotifyChanged();
        UndoChanged();
        var now = TextureOps.Fields(Doc, t0);
        string V(string k) => now.TryGetValue(k, out int v) ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
        DisplayStatus($"{what}: tile {V("tile")}, panning {V("panx")}, {V("pany")}, repeats {V("repx")} x {V("repy")}, shade {V("shade")}"
            + (op == "set" ? "" : SharedNote(targets, false)));
    }

    /// <summary>A masked (or one-way) picture's offsets, repeats, shade, palette and flips are its wall's own (Blood keeps
    /// one set a wall: the engine draws the mask with them): a change to one moves the wall's picture above and below
    /// the opening on that side too. The note saying so, when a target is an opening with such a picture beside it ("":
    /// none); aligned: said of an auto-align (each wall along the row).</summary>
    string SharedNote(IEnumerable<VisualTarget> targets, bool aligned)
    {
        bool beside = targets.Any(t => TextureOps.IsOpening(Doc, t)
            && Preview3D.PartsOf(Doc, t.Wall).Any(p => p.Part is VisualPart.Upper or VisualPart.Lower));
        if (!beside && !aligned)
            return "";
        return aligned
            ? " (each wall's own offsets: its picture above and below the opening on that side moved with it; Blood keeps one set a wall)"
            : " (the wall's own: its picture above and below the opening on this side moved too; Blood keeps one set a wall)";
    }

    /// <summary>Snap to Grid on a tool context (2D): a command with the grid and the items, recorded and replayed.</summary>
    public void SnapToGrid(ToolContext c)
    {
        var (kind, items) = c.Points.Count > 0 ? ("point", c.Points) : c.Lines.Count > 0 ? ("line", c.Lines)
            : c.Sectors.Count > 0 ? ("sector", c.Sectors) : ("sprite", c.Sprites);
        if (items.Count == 0)
        {
            DisplayStatus("snap to grid: select points, walls, sectors or sprites first");
            return;
        }
        UiDo($"snapgrid {View.GridSize} {kind} " + string.Join(" ", items));
    }

    /// <summary>"snapgrid grid point|line|sector|sprite index...": the points of those (a wall's two ends, a sector's
    /// corners) or the sprites moved to the nearest grid crossing (one undo step; the map rules checked after).</summary>
    void SnapGrid(string action)
    {
        var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 4 || !int.TryParse(p[1], out int grid) || grid < 1)
            return;
        var items = p.Skip(3).Select(x => int.TryParse(x, out int i) ? i : -1).Where(i => i >= 0).Distinct().ToList();
        int Snap(int v) => (int)(Math.Round(v / (double)grid, MidpointRounding.AwayFromZero) * grid);
        var points = new HashSet<int>();
        var sprites = new List<int>();
        switch (p[2])
        {
            case "point":
                points.UnionWith(items.Where(i => i < Doc.Points.Count));
                break;
            case "line":
                foreach (int l in items.Where(i => i < Doc.Lines.Count))
                {
                    int f = Doc.Lines[l].Front;
                    points.Add(Doc.PointOf[f]);
                    int n = Doc.Walls[f].Point2;
                    if (n >= 0 && n < Doc.Walls.Count)
                        points.Add(Doc.PointOf[n]);
                }
                break;
            case "sector":
                foreach (int s in items.Where(i => i < Doc.Sectors.Count))
                    for (int w = Doc.Sectors[s].WallPtr; w < Doc.Sectors[s].WallPtr + Doc.Sectors[s].WallNum && w < Doc.Walls.Count; w++)
                        points.Add(Doc.PointOf[w]);
                break;
            case "sprite":
                sprites.AddRange(items.Where(i => i < Doc.Sprites.Count && Doc.SpriteEditable(i)));
                break;
        }
        // (another layer's points stay)
        points.RemoveWhere(pt => pt < 0 || pt >= Doc.Points.Count || Doc.Points[pt].Walls.Any(w => !Doc.WallEditable(w)));
        var moves = points.Select(pt => (pt, x: Snap(Doc.Points[pt].X), y: Snap(Doc.Points[pt].Y)))
            .Where(m => m.x != Doc.Points[m.pt].X || m.y != Doc.Points[m.pt].Y).ToList();
        var spriteMoves = sprites.Select(i => (i, x: Snap(Doc.Sprites[i].X), y: Snap(Doc.Sprites[i].Y)))
            .Where(m => m.x != Doc.Sprites[m.i].X || m.y != Doc.Sprites[m.i].Y).ToList();
        if (moves.Count + spriteMoves.Count == 0)
        {
            DisplayStatus($"snap to grid: already on the grid ({grid})");
            return;
        }
        Operation($"Snap to grid ({grid})", () =>
        {
            foreach (var (pt, x, y) in moves)
                Doc.MovePoint(pt, x, y);
            foreach (var (i, x, y) in spriteMoves)
                Doc.MoveSprite(i, x, y);
            return null;
        });
    }

    /// <summary>"visualmove dx dy s1 s2...": sprites moved across the map (Build units; a run on the same sprites one
    /// undo step), each into the sector now holding it. In Edit only (as the wheel moves them up and down).</summary>
    void VisualMove(string action)
    {
        var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // "dx,dy,dz:s1 ..." each its own (a wall sprite along its wall), or "dx dy s1 s2 ..." all the same
        var each = new Dictionary<int, (int dx, int dy, int dz)>();
        if (p.Length >= 2 && p[1].Contains(':'))
        {
            foreach (var e in p.Skip(1))
            {
                var parts = e.Split(':');
                var n = parts[0].Split(',');
                if (parts.Length == 2 && n.Length == 3 && int.TryParse(n[0], out int ex) && int.TryParse(n[1], out int ey)
                    && int.TryParse(n[2], out int ez) && Preview3D.Parse(Doc, parts[1]) is { IsSprite: true } et)
                    each[et.Sprite] = (ex, ey, ez);
            }
        }
        else if (p.Length >= 4 && int.TryParse(p[1], out int dx0) && int.TryParse(p[2], out int dy0))
            foreach (var c in p.Skip(3))
                if (Preview3D.Parse(Doc, c) is { IsSprite: true } ct)
                    each[ct.Sprite] = (dx0, dy0, 0);
        var sprites = each.Keys.Where(Doc.SpriteEditable).ToList();
        if (sprites.Count == 0)
            return;
        if (Posed)
        {
            DisplayStatus("sprites are moved in Edit");
            return;
        }
        MergeUndo("move " + string.Join(" ", sprites.OrderBy(i => i)), sprites.Count == 1 ? "Move sprite" : $"Move {sprites.Count} sprites");
        foreach (int i in sprites)
        {
            var (dx, dy, dz) = each[i];
            Doc.MoveSprite(i, Doc.Sprites[i].X + dx, Doc.Sprites[i].Y + dy);
            Doc.Sprites[i].Z += dz;
        }
        Doc.NotifyChanged();
        UndoChanged();
        var s0 = Doc.Sprites[sprites[0]];
        DisplayStatus($"sprite {sprites[0]} at {Units.Xy(s0.X)}, {Units.Xy(s0.Y)} in sector {s0.SectNum}"
            + (sprites.Count > 1 ? $" (and {sprites.Count - 1} more)" : ""));
    }

    /// <summary>"visualdelete s1 s2...": sprites deleted (one undo step; the preview's selection cleared: the sprites
    /// after them are numbered down).</summary>
    void VisualDelete(string action)
    {
        var targets = action.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(c => Preview3D.Parse(Doc, c))
            .Where(t => t != null).Select(t => t.Value).ToList();
        var sprites = targets.Where(t => t.IsSprite).Select(t => t.Sprite).Where(Doc.SpriteEditable).Distinct().ToList();
        var lights = targets.Where(t => t.IsLight).Select(t => t.Light).Where(Doc.LightEditable).Distinct().ToList();
        if (sprites.Count == 0 && lights.Count == 0)
            return;
        if (Posed && sprites.Count > 0)
        {
            DisplayStatus("sprites are deleted in Edit");
            return;
        }
        var what = new List<string>();
        if (sprites.Count > 0)
            what.Add(sprites.Count == 1 ? $"sprite {sprites[0]}" : $"{sprites.Count} sprites");
        if (lights.Count > 0)
            what.Add(lights.Count == 1 ? $"light {lights[0]}" : $"{lights.Count} lights");
        Fresh("Delete " + string.Join(" and ", what));
        // (the lights first: deleting sprites renumbers the lights attached to them, takes their own away)
        Doc.DeleteLights(lights);
        if (sprites.Count > 0)
            Doc.DeleteSprites(sprites);
        _preview.ClearSelection();
        foreach (var s in _selection.Values)
            s.Clear();
        if (sprites.Count > 0)
            Doc.NotifyChanged();
        else
            Doc.NotifyLightsChanged();
        UndoChanged();
        SelectionChanged();
        DisplayStatus(string.Join(" and ", what) + " deleted");
    }

    /// <summary>An undo level of its own (no run merged into it).</summary>
    void Fresh(string name)
    {
        Undo.CreateUndo(name);
        _visualKey = null;
    }

    /// <summary>Texture Settings (T, the 3D preview's right click): a tab for each kind of surface among the targets
    /// (walls, floors, ceilings, sprites; no tabs for one kind), each with its pictures (a wall's and, two-sided, its
    /// masked one), panning, repeats, shade, palette, flags and Blood's scrolling, as its first surface has them; a field
    /// the tab's surfaces differ in says "Multiple" and is written only when changed (then onto all of them). What is
    /// changed goes onto that kind only (keys "w.panx": TextureOps.ForKind), shown in the preview as it is changed.</summary>
    public void TextureSettings(List<VisualTarget> targets)
    {
        if (targets.Count == 0)
            return;
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Visible;
        var t0 = targets[0];
        var d = new ToolDialog(this, null, targets.Count == 1 ? $"Texture of {DescribeTarget(t0)}" : $"Texture of {targets.Count} surfaces")
        {
            ApplyOnClose = true,
        };
        var form = new TextureForm();
        var kinds = new[] { ("w", "Walls"), ("f", "Floors"), ("c", "Ceilings"), ("s", "Sprites") }
            .Where(k => targets.Any(t => TextureOps.KindPrefix(t) == k.Item1)).ToList();
        foreach (var (prefix, title) in kinds)
        {
            var group = targets.Where(t => TextureOps.KindPrefix(t) == prefix).ToList();
            if (kinds.Count > 1)
                d.Tab($"{title} ({group.Count})");
            KindFields(d, prefix, group, form);
        }
        d.Change = copy =>
        {
            var f = form.Changed();
            foreach (var t in targets)
                TextureOps.SetFields(copy, t, f);
            return null;
        };
        d.Show(() => form.Changed() is { Count: > 0 } f ? $"visualtex set {TextureOps.Encode(f)} {Codes(targets)}" : null);
    }

    /// <summary>Texture Settings' fields: each key's value now and as it started; a mixed one (the surfaces differ:
    /// "Multiple") counts as changed only once it is edited.</summary>
    sealed class TextureForm
    {
        public readonly Dictionary<string, int> Start = new();
        public readonly Dictionary<string, Func<int>> Now = new();
        public readonly HashSet<string> Mixed = new(), Touched = new();

        public Dictionary<string, int> Changed() => Now
            .Where(kv => Mixed.Contains(kv.Key) ? Touched.Contains(kv.Key) : kv.Value() != Start[kv.Key])
            .ToDictionary(kv => kv.Key, kv => kv.Value());
    }

    const string MultipleTip = "the selected surfaces differ here: a value typed or picked goes onto all of them";

    /// <summary>A number field saying "Multiple" (its value the first surface's, kept until it is edited).</summary>
    static void ShowMultiple(SpinBox s)
    {
        s.TooltipText = MultipleTip;
        void Say()
        {
            if (GodotObject.IsInstanceValid(s))
                s.GetLineEdit().Text = "Multiple";
        }
        Callable.From(Say).CallDeferred();
    }

    /// <summary>One kind's fields in Texture Settings (its tab): its pictures, then its settings, Blood's scrolling
    /// last; each shown as the first surface has it, "Multiple" where the kind's surfaces differ; keys prefixed with
    /// the kind.</summary>
    void KindFields(ToolDialog d, string prefix, List<VisualTarget> group, TextureForm form)
    {
        var t0 = group[0];
        var all = group.Select(t => TextureOps.Fields(Doc, t)).ToList();
        var mine = all[0];
        bool Differ(string key) => all.Select(f => f.TryGetValue(key, out int v) ? v : int.MinValue).Distinct().Count() > 1;
        var pictures = new Dictionary<string, SpinBox>();
        foreach (var (key, label) in TextureOps.PictureKeys)
        {
            var values = group.Select(t => TextureOps.Picture(Doc, t, key)).Where(v => v != null).Select(v => v.Value).ToList();
            if (values.Count == 0)
                continue;
            int first = values[0];
            bool mixed = values.Distinct().Count() > 1;
            string pk = prefix + "." + key;
            form.Start[pk] = first;
            var spin = d.Int(pk, label, first, 0, 32767);
            pictures[key] = spin;
            form.Now[pk] = () => (int)spin.Value;
            var row = new HBoxContainer();
            var thumb = new TextureRect { CustomMinimumSize = new Vector2(48, 48), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Texture = mixed ? null : Data.TileImages.Get(first) };
            var many = new Label { Text = mixed ? $"Multiple ({values.Distinct().Count()} pictures)" : "", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            spin.ValueChanged += v =>
            {
                thumb.Texture = Data.TileImages.Get((int)v);
                many.Text = "";
                form.Touched.Add(pk);
            };
            if (mixed)
            {
                form.Mixed.Add(pk);
                ShowMultiple(spin);
            }
            var pick = new Button { Text = "Pick...", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                TooltipText = $"the {label.ToLowerInvariant()} of the {values.Count} surface(s) that have one" };
            string pickKind = key switch
            {
                "tilewall" => "wall",
                "tileover" => "masked",
                "tilefloor" => "floor",
                "tileceiling" => group.All(t => t.IsPlane && (Doc.Sectors[t.Sector].CeilingStat & 1) != 0) ? "sky" : "ceiling",
                _ => "sprite",
            };
            pick.Pressed += () => TilePickerDialog.Show(this, pick, (int)spin.Value, n =>
            {
                spin.Value = n;
                form.Touched.Add(pk);
            }, kind: pickKind);
            row.AddChild(thumb);
            row.AddChild(pick);
            row.AddChild(many);
            d.Row("", row);
        }
        var tile = pictures.GetValueOrDefault(TextureOps.PictureKey(Doc, t0)) ?? pictures.Values.First();
        // a number: shown as the first surface has it ("Multiple" where they differ), written under the kind's prefix
        SpinBox Num(string key, string label, int min, int max, string tip = null)
        {
            string k = prefix + "." + key;
            form.Start[k] = mine[key];
            var s = d.Int(k, label, mine[key], min, max);
            if (tip != null)
                s.TooltipText = tip;
            form.Now[k] = () => (int)s.Value;
            s.ValueChanged += _ => form.Touched.Add(k);
            if (Differ(key))
            {
                form.Mixed.Add(k);
                ShowMultiple(s);
            }
            return s;
        }
        // a flag: its label says "(multiple)" where the surfaces differ
        void Flag(string key, string caption, string tip = null)
        {
            string k = prefix + "." + key;
            form.Start[k] = mine[key];
            bool mixed = Differ(key);
            var c = d.Check(k, mixed ? caption + " (multiple)" : caption, mine[key] != 0);
            c.TooltipText = mixed ? MultipleTip : tip ?? "";
            form.Now[k] = () => c.ButtonPressed ? 1 : 0;
            c.Toggled += _ => form.Touched.Add(k);
            if (mixed)
                form.Mixed.Add(k);
        }
        bool sprite = t0.IsSprite;
        if (!sprite)
        {
            Num("panx", "X panning", 0, 255);
            Num("pany", "Y panning", 0, 255);
        }
        if (mine.ContainsKey("repx"))
        {
            Num("repx", sprite ? "Width (x repeat)" : "X repeat", 1, 255);
            Num("repy", sprite ? "Height (y repeat)" : "Y repeat", 1, 255);
        }
        Num("shade", "Shade (darker up)", -128, 127);
        var pal = Num("pal", "Palette", 0, 255);
        var palPick = new Button { Text = "Pick...", TooltipText = "Blood's palettes, each on this picture" };
        palPick.Pressed += () => PalettePickerDialog.Show(this, palPick, (int)pal.Value, (int)tile.Value, n =>
        {
            pal.Value = n;
            form.Touched.Add(prefix + ".pal");
        });
        d.Row("", palPick);
        foreach (var (name, caption) in TextureOps.Flags)
            if (mine.ContainsKey(name))
                Flag(name, caption);
        // a two-sided wall's opening (masked walls: TextureOps.MaskKeys), as its first two-sided wall has it; written on
        // the two-sided walls only
        var two = all.Where(f => f.ContainsKey("middle")).ToList();
        if (two.Count > 0)
        {
            d.Note(two.Count == all.Count ? "Masked wall (the picture across the opening: Masked picture above)"
                : $"Masked wall ({two.Count} of the {all.Count} walls are two-sided: these go onto those)");
            void Choice(string key, string label, string[] names, string tip)
            {
                string k = prefix + "." + key;
                int start = two[0][key];
                bool mixed = two.Select(f => f[key]).Distinct().Count() > 1;
                form.Start[k] = start;
                var o = d.Choice(k, label, mixed ? names.Append("Multiple").ToArray() : names, mixed ? names.Length : start);
                o.TooltipText = mixed ? MultipleTip : tip;
                form.Now[k] = () => o.Selected >= names.Length ? start : o.Selected;
                o.ItemSelected += _ => form.Touched.Add(k);
                if (mixed)
                    form.Mixed.Add(k);
            }
            void MaskFlag(string key, string caption, string tip)
            {
                string k = prefix + "." + key;
                bool mixed = two.Select(f => f[key]).Distinct().Count() > 1;
                form.Start[k] = two[0][key];
                var c = d.Check(k, mixed ? caption + " (multiple)" : caption, two[0][key] != 0);
                c.TooltipText = mixed ? MultipleTip : tip;
                form.Now[k] = () => c.ButtonPressed ? 1 : 0;
                c.Toggled += _ => form.Touched.Add(k);
                if (mixed)
                    form.Mixed.Add(k);
            }
            Choice("middle", "Opening", new[] { "Open", "Masked (both sides)", "One-way (this side)" },
                "masked: the masked picture across the opening, seen from both sides (the other side's x-flipped to read the same); "
                + "one-way: a solid picture seen from this side only");
            Choice("seethrough", "See-through", new[] { "Solid", "Translucent", "Translucent, reversed" },
                "a masked picture blended with what is behind it (reversed: the other way round), both sides");
            MaskFlag("block", "Blocks the player", "the player and enemies can't walk through it (both sides)");
            MaskFlag("hitscan", "Stops shots", "bullets and hitscan weapons hit it (both sides)");
        }
        // Blood's scrolling (the x-wall's or x-sector's: made only when a value changes)
        var scrolling = TextureOps.Scrolling(t0);
        if (scrolling.Length > 0)
        {
            d.Note(t0.IsPlane ? "Scrolling (speed and angle shared by the sector's floor and ceiling)" : "Scrolling");
            foreach (var sc in scrolling)
                if (sc.flag)
                    Flag(sc.key, sc.label, sc.tip);
                else
                    Num(sc.key, sc.label, sc.min, sc.max, sc.tip);
        }
    }
}
