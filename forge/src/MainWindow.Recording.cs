// Chosen core: original code.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chosen.Forge.Config;
using Chosen.Forge.Document;
using Chosen.Forge.Editing;
using Chosen.Forge.Recording;
using Godot;

namespace Chosen.Forge;

/// <summary>
/// Session recording and replay (src/Recording/SessionRecorder.cs): every session is recorded; Help > Report a Problem
/// marks the moment and copies the session into the repository's bugs/forge; --replay plays one back through the same
/// code paths and checks the map after every edit; --regress replays every test in forge/tests/replays.
/// </summary>
public partial class MainWindow
{
    /// <summary>The session being recorded (null while replaying, in the headless tests and screenshots).</summary>
    public SessionRecorder Rec { get; private set; }
    /// <summary>A session is being replayed: dialogs are built but not shown (the session's "dlg" lines fill them).</summary>
    public bool Replaying { get; private set; }
    /// <summary>The dialog shown last (the tests fill it).</summary>
    public Window LastDialog { get; private set; }
    /// <summary>The dialog a replay built last, waiting for its "dlg" or "dlgcancel" line.</summary>
    public Window PendingDialog { get; private set; }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The regression replays: forge/tests/replays/&lt;name&gt;/ (events.log, the maps, expected.txt, note.txt).</summary>
    public static string ReplayTests => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "tests", "replays"));

    public override void _ExitTree()
    {
        Rec?.Close();
        Rec = null;
    }

    void StartRecording()
    {
        var args = OS.GetCmdlineUserArgs();
        bool test = args.Contains("--uitest") || args.Any(a => a.StartsWith("--rebuild="));
        Rec = SessionRecorder.Start(this, test ? SessionRecorder.TestRoot : null);
        Rec?.Line("keys", string.Join(";", Keys.Actions.Select(a => $"{a}={Keys.Text(a)}")));
    }

    /// <summary>A command from a menu, the toolbar, a mode button or a docker button: recorded, then done.</summary>
    public void UiDo(string action)
    {
        Rec?.Do(action);
        Do(action);
    }

    /// <summary>A command a mouse or key handler gives (Paste's click, Align Grid's): recorded and done; in a replay
    /// left to the recorded "do" line (the input that gave it is replayed too: done once).</summary>
    public void InputDo(string action)
    {
        if (!Replaying)
            UiDo(action);
    }

    /// <summary>Marks a GUI event handled (a replayed one has no GUI dispatch).</summary>
    void Handled()
    {
        if (!Replaying)
            GetViewport().SetInputAsHandled();
    }

    /// <summary>The settings the modes' input depends on, as a session's state line.</summary>
    public string RecordedState() => string.Join(";",
        $"grid={View.GridSize}", $"snap={(View.Snap ? 1 : 0)}", $"units={(Units.Game ? "game" : "build")}",
        $"hl={ClassicMode.HighlightRange.ToString(Inv)}", $"sprite={SpriteType}", $"spritetile={SpriteTile}", $"editnew={(EditNewSprite ? 1 : 0)}",
        $"hidden={string.Join(",", _hiddenCategories.OrderBy(c => c))}",
        $"gridframe={View.GridOrigin.X.ToString(Inv)},{View.GridOrigin.Y.ToString(Inv)},{View.GridAngle.ToString("R", Inv)}",
        $"circle={DrawCircleMode.Sides}", $"cells={DrawGridMode.Size.cols},{DrawGridMode.Size.rows}",
        $"falloff={ClassicMode.FallCurve},{ClassicMode.FallRadius.ToString("R", Inv)}");

    void ApplyState(string s)
    {
        foreach (var kv in s.Split(';'))
        {
            int eq = kv.IndexOf('=');
            if (eq < 0)
                continue;
            string k = kv[..eq], v = kv[(eq + 1)..];
            switch (k)
            {
                case "grid": View.GridSize = int.Parse(v, Inv); break;
                case "snap": View.Snap = v == "1"; break;
                case "units": Units.Game = v == "game"; break;
                case "hl": ClassicMode.HighlightRange = float.Parse(v, Inv); break;
                case "sprite": SpriteType = int.Parse(v, Inv); break;
                case "spritetile": SpriteTile = int.Parse(v, Inv); break;
                case "editnew": EditNewSprite = v == "1"; break;
                case "gridframe" when v.Split(',') is { Length: 3 } gf:
                    View.GridOrigin = new Vector2(float.Parse(gf[0], Inv), float.Parse(gf[1], Inv));
                    View.GridAngle = float.Parse(gf[2], Inv);
                    View.QueueRedraw();
                    break;
                case "circle": DrawCircleMode.Sides = int.Parse(v, Inv); break;
                case "cells" when v.Split(',') is { Length: 2 } gc: DrawGridMode.Size = (int.Parse(gc[0], Inv), int.Parse(gc[1], Inv)); break;
                case "falloff" when v.Split(',') is { Length: 2 } fo:
                    ClassicMode.FallCurve = int.Parse(fo[0], Inv);
                    ClassicMode.FallRadius = float.Parse(fo[1], Inv);
                    break;
                case "hidden":
                    _hiddenCategories.Clear();
                    foreach (var c in v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        _hiddenCategories.Add(c);
                    break;
            }
        }
        UpdateStatusFields();
    }

    // ---------------------------------------------------------------- Report a Problem

    /// <summary>Help > Report a Problem (PrtScr too): a note, the session's recording, a screenshot of the window as
    /// it was (before the dialog) and where the views were (report.txt: the game's report keys, so pipeline/bugs.py
    /// nblood forge/&lt;id&gt; shows NBlood at the 3D preview's camera, with what Play had set off).</summary>
    void ReportProblem()
    {
        // (the window as it is now: the dialog isn't drawn yet)
        Image shot = DisplayServer.GetName() == "headless" ? null : GetViewport().GetTexture().GetImage();
        var view = ViewReport();
        var d = new ConfirmationDialog { Title = "Report a Problem", OkButtonText = "Report", MinSize = new Vector2I(560, 0) };
        var box = new VBoxContainer();
        d.AddChild(box);
        box.AddChild(new Label { Text = "What went wrong? Everything you did this session is recorded and can be replayed exactly; "
            + "the camera and what Play set off are saved with it.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0) });
        var text = new TextEdit { CustomMinimumSize = new Vector2(520, 90), WrapMode = TextEdit.LineWrappingMode.Boundary };
        box.AddChild(text);
        CheckBox attach = null;
        if (shot != null)
        {
            var thumb = (Image)shot.Duplicate();
            thumb.Resize(520, Math.Max(1, shot.GetHeight() * 520 / Math.Max(1, shot.GetWidth())), Image.Interpolation.Bilinear);
            box.AddChild(new TextureRect { Texture = ImageTexture.CreateFromImage(thumb), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(520, Math.Min(300, thumb.GetHeight())), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize });
            attach = new CheckBox { Text = "Attach this screenshot", ButtonPressed = true };
            box.AddChild(attach);
        }
        d.Confirmed += () =>
        {
            if (Rec == null)
            {
                DisplayStatus("Report a Problem: this session isn't being recorded");
                return;
            }
            string note = text.Text.Trim();
            Rec.Mark(note);
            string where = Rec.Folder;
            // into the repository's bugs/forge when Forge runs from it (the exported Forge: the session folder)
            if (Repo is { } repo)
                where = Path.Combine(repo, "bugs", "forge", Path.GetFileName(Rec.Folder));
            // a second report from the same session: a folder of its own (named for when it was made), the first's
            // note and screenshot kept
            if (File.Exists(Path.Combine(where, "note.txt")))
                where += "-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
            if (where != Rec.Folder)
                CopyFolder(Rec.Folder, where);
            File.WriteAllText(Path.Combine(where, "note.txt"), note + "\n");
            bool shotSaved = shot != null && attach?.ButtonPressed == true && shot.SavePng(Path.Combine(where, "screenshot.png")) == Error.Ok;
            File.WriteAllText(Path.Combine(where, "report.txt"), $"id: {Path.GetFileName(where)}\ndescription: {note.Replace('\n', ' ')}\nversion: {Chosen.Shared.Engine.ChosenVersion.Full}\n"
                + view + (shotSaved ? "screenshot: screenshot.png\n" : ""));
            DisplayStatus($"Problem reported: {where}" + (shotSaved ? " (with a screenshot)" : ""));
        };
        Popup(d);
        text.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Where the views are (report.txt, after id and description): the map, the view, what is shown, the 3D
    /// preview's camera (Quake units as the game's reports: camera, angles; and Build's), the 2D view's middle and
    /// zoom, Play's clock and what the player set off; nblood_send and nblood_seconds: NBlood in the same state.</summary>
    string ViewReport()
    {
        var ci = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.00", ci);
        var lines = new List<string>();
        string map = Doc?.Path ?? "";
        if (Repo is { } repo && map.Length > 0)
        {
            string rel = Path.GetRelativePath(repo, map);
            if (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel))
                map = rel.Replace('\\', '/');
        }
        lines.Add("map: " + map);
        lines.Add("engine: forge");
        lines.Add("date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", ci));
        lines.Add("view: " + (_preview.Visible ? "3D preview" : "2D " + (Editing?.Mode?.Name ?? "")));
        lines.Add("shown: " + (_play != null ? $"Play {F(_play.Sim.Seconds)} s" : _shown.ToString()));
        // the 3D preview's camera: Godot (x, y, z) here = Build (x / 16, -z / 256, y / 16) (PreviewMesher.P); Quake
        // (the game's, NBlood's -b3dview): Build (x / 16, -y / 16, -z / 256)
        var (at, yaw, pitch) = _preview.CameraPose;
        float fx = -Mathf.Sin(yaw), fy = -Mathf.Cos(yaw);
        double qyaw = (Mathf.RadToDeg(Mathf.Atan2(-fy, fx)) + 360) % 360;
        double qpitch = -Mathf.RadToDeg(pitch);
        lines.Add($"camera: {F(at.X)} {F(-at.Z)} {F(at.Y)}");
        lines.Add($"angles: {F(qpitch)} {F(qyaw)} 0.00");
        int bang = (int)Math.Round(Mathf.Atan2(fy, fx) * 1024 / Mathf.Pi + 2048) % 2048;
        lines.Add($"build_camera: {Math.Round(at.X * 16)} {Math.Round(at.Z * 16)} {Math.Round(-at.Y * 256)} angle {bang} sector {_preview.CameraSectorNow}");
        lines.Add($"view2d: {F(View.Centre.X)} {F(View.Centre.Y)} zoom {View.Zoom.ToString("0.0000", ci)}");
        if (_play != null)
        {
            var log = _play.Sim.PlayerLog;
            string Kind(int t) => t switch { Blood.EventSim.TypeWall => "wall", Blood.EventSim.TypeSprite => "sprite", _ => "sector" };
            lines.Add("play_log: " + string.Join(", ", log.Select(l => $"{F(l.seconds)} {l.what} {Kind(l.type)} {l.index}")));
            // NBlood: -b3dsend turns sectors on as the level starts (as walking in): the sectors entered or used, the
            // shot that long after the first of them
            // (a thing broken: its channel toggled, c<n>, as its trigger sends it; the thing itself stays there)
            bool Sends(Blood.EventSim.PlayerEvent l) => (l.type == Blood.EventSim.TypeSector && l.what != "exit")
                || (l.type == Blood.EventSim.TypeSprite && l.what is "use" or "explode" && Doc.Sprites[l.index].Xsp is { TxID: > 0 });
            var sends = log.Where(Sends).Select(l => l.type == Blood.EventSim.TypeSector ? l.index.ToString(ci) : "c" + Doc.Sprites[l.index].Xsp.TxID)
                .Distinct().ToList();
            double from = sends.Count > 0 ? log.First(Sends).seconds : 0;
            if (sends.Count > 0)
                lines.Add("nblood_send: " + string.Join(",", sends));
            lines.Add("nblood_seconds: " + F(Math.Max(0.5, _play.Sim.Seconds - from)));
        }
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>The repository Forge runs from (null: an exported Forge).</summary>
    static string Repo
    {
        get
        {
            string repo = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
            return Directory.Exists(Path.Combine(repo, "bugs")) && Directory.Exists(Path.Combine(repo, "forge")) ? repo : null;
        }
    }

    /// <summary>
    /// A session copied into the repository: a map it opened that is a level of the data folder as it is there
    /// (Blood's, an add-on's: not ours to commit) becomes a "&lt;copy&gt;.ref" naming that file; the mapper's own maps
    /// are copied.
    /// </summary>
    static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        static byte[] Read(string f)
        {
            // the log is still open for writing: read it shared
            using var src = new FileStream(f, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            src.CopyTo(ms);
            return ms.ToArray();
        }
        // copy -> the file it was copied from (the open lines)
        var origin = new Dictionary<string, string>();
        string log = Path.Combine(from, "events.log");
        if (File.Exists(log))
            foreach (var l in System.Text.Encoding.UTF8.GetString(Read(log)).Split('\n'))
                if (l.Split('\t') is { Length: > 4 } f && f[1] == "open")
                {
                    origin[f[2]] = f[4];
                    origin[Path.ChangeExtension(f[2], ".patch")] = Path.ChangeExtension(f[4], ".patch");
                }
        string repo = Repo, data = repo != null ? Path.Combine(repo, "game") + Path.DirectorySeparatorChar : null;
        foreach (var f in Directory.GetFiles(from))
        {
            string name = Path.GetFileName(f);
            var bytes = Read(f);
            // (Blood's and the add-ons' levels are .MAP files with their .patch: a .MAPX is Forge's own, the mapper's map
            // even in the data folder (game/blood3d/maps/test.mapx), which changes when saved: referenced, a report on it
            // or a test made from one no longer replayed once the map was saved again)
            if (data != null && origin.TryGetValue(name, out var o) && Path.GetExtension(o).ToLowerInvariant() is ".map" or ".patch"
                && Path.GetFullPath(o).StartsWith(data, StringComparison.OrdinalIgnoreCase)
                && File.Exists(o) && File.ReadAllBytes(o).AsSpan().SequenceEqual(bytes))
            {
                File.WriteAllText(Path.Combine(to, name + ".ref"), Path.GetRelativePath(repo, o).Replace('\\', '/') + "\n");
                continue;
            }
            File.WriteAllBytes(Path.Combine(to, name), bytes);
        }
    }

    /// <summary>A session's copy of a map: the file, or the data folder's file its .ref names.</summary>
    static string SessionFile(string dir, string name)
    {
        string p = Path.Combine(dir, name);
        if (!File.Exists(p) && File.Exists(p + ".ref") && Repo is { } repo)
            p = Path.Combine(repo, File.ReadAllText(p + ".ref").Trim());
        return p;
    }

    // ---------------------------------------------------------------- replay

    /// <summary>Commands a replay leaves out: file dialogs, other dialogs (their results are recorded), the game.</summary>
    static readonly HashSet<string> NotReplayed = new()
    {
        "newmap", "openmap", "savemap", "savemapas", "savemapx", "exportmap", "preferences", "exit", "testmap",
        "gotocoords", "about", "reportproblem", "opensessions",
    };

    /// <summary>A session folder by name: "last" is the newest recorded session but this one.</summary>
    static string SessionFolder(string arg)
    {
        if (arg != "last")
            return Path.GetFullPath(arg);
        return Directory.GetDirectories(SessionRecorder.Root).Where(d => File.Exists(Path.Combine(d, "events.log")))
            .OrderByDescending(d => d).FirstOrDefault() ?? "";
    }

    /// <summary>
    /// Replays a session (or a regression test): its input through the view and the window as it came, its commands,
    /// dialog results and opened maps; at each recorded check the map's fingerprint is compared with expected.txt
    /// (else the recorded one). accept: writes expected.txt from this replay. until: stops before that line. The
    /// number of checks that differed.
    /// </summary>
    public async Task<int> Replay(string dir, bool accept = false, int until = 0, bool verbose = true)
    {
        string log = Path.Combine(dir, "events.log");
        if (!File.Exists(log))
        {
            GD.PrintErr($"replay: no {log}");
            return 1;
        }
        var lines = File.ReadAllLines(log);
        string expPath = Path.Combine(dir, "expected.txt");
        var expected = !accept && File.Exists(expPath)
            ? File.ReadAllLines(expPath).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t')[0]).ToList()
            : null;
        var got = new List<string>();
        int checks = 0, diverged = 0;
        var rec = Rec;
        var keys = Keys;
        string recordedStatus = "";
        Vector2 centre = View.Centre;
        float zoom = View.Zoom;
        string stateBefore = RecordedState();
        // the tools' own settings as Forge starts (a session from before they were recorded has no state for them)
        ApplyState("gridframe=0,0,0;circle=16;cells=4,4;falloff=0,1024");
        // and in the mode Forge starts in (a replay after another began in the mode that one ended in)
        Editing.Reset();
        Rec = null;
        Replaying = true;
        void SetView()
        {
            View.Zoom = zoom;
            View.CentreOn(centre);
        }
        int at = 0;
        try
        {
            for (int n = 0; n < lines.Length; n++)
            {
                at = n + 1;
                if (until > 0 && n + 1 >= until)
                    break;
                var f = lines[n].Split('\t');
                if (f.Length < 2)
                    continue;
                string Arg(int i) => i < f.Length ? f[i] : "";
                float Num(int i) => float.Parse(Arg(i), Inv);
                switch (f[1])
                {
                    case "keys":
                        Keys = new KeyMap();
                        foreach (var kv in Arg(2).Split(';'))
                            if (kv.IndexOf('=') is var eq and > 0)
                                Keys.Bind(kv[..eq], kv[(eq + 1)..]);
                        break;
                    case "state":
                        ApplyState(Arg(2));
                        break;
                    case "view":
                        centre = new Vector2(Num(2), Num(3));
                        zoom = Num(4);
                        SetView();
                        break;
                    case "move":
                    {
                        SetView();
                        var e = new InputEventMouseMotion
                        {
                            Position = View.MapToScreen(new Vector2(Num(2), Num(3))), ButtonMask = (MouseButtonMask)int.Parse(Arg(5), Inv),
                        };
                        Mods(e, Arg(4));
                        View.HandleInput(e);
                        if (n % 8 == 0)
                            await Frames(1);
                        break;
                    }
                    case "btn":
                    {
                        var b = (MouseButton)int.Parse(Arg(2), Inv);
                        // the view's own zoom and pan: the view lines carry what they did
                        if (b is not (MouseButton.Left or MouseButton.Right))
                            break;
                        SetView();
                        var e = new InputEventMouseButton
                        {
                            ButtonIndex = b, Pressed = Arg(3) == "1", Position = View.MapToScreen(new Vector2(Num(4), Num(5))),
                            ButtonMask = (MouseButtonMask)int.Parse(Arg(7), Inv), DoubleClick = Arg(8) == "1",
                        };
                        Mods(e, Arg(6));
                        View.HandleInput(e);
                        await Frames(1);
                        break;
                    }
                    case "key":
                    {
                        var k = new InputEventKey
                        {
                            Keycode = (Key)long.Parse(Arg(3), Inv), PhysicalKeycode = (Key)long.Parse(Arg(4), Inv), Pressed = Arg(5) == "1",
                            Echo = Arg(6) == "1", Unicode = (char)long.Parse(Arg(8), Inv),
                        };
                        Mods(k, Arg(7));
                        if (k.Keycode == Key.Space)
                            break;
                        SetView();
                        if (Arg(2) == "v")
                            View.HandleInput(k);
                        else
                            UnhandledKey(k);
                        await Frames(1);
                        break;
                    }
                    case "leave":
                        Editing.Mode?.OnMouseLeave();
                        break;
                    case "do":
                        if (!NotReplayed.Contains(Arg(2)))
                        {
                            SetView();
                            Do(Arg(2));
                        }
                        await Frames(1);
                        break;
                    case "goto":
                        if (int.TryParse(Arg(2), out int gi) && gi < _findings.Count)
                            GoTo(_findings[gi]);
                        break;
                    case "new":
                        SetDocument(MapDocument.New());
                        View.FitPending = false;
                        break;
                    case "open":
                    {
                        bool patch = Arg(3) == "1";
                        var od = MapDocument.Open(SessionFile(dir, Arg(2)), _ => patch);
                        if (od.Format == MapFormat.Map)
                            Blood.AutoLayers.Make(od);
                        SetDocument(od);
                        View.FitPending = false;
                        break;
                    }
                    case "doc":
                        if (Arg(3) == "undo")
                            Undo.CreateUndo(Arg(4));
                        Doc.Restore(File.ReadAllBytes(Path.Combine(dir, Arg(2))));
                        UndoChanged();
                        break;
                    case "revert":
                        Undo.Revert();
                        UndoChanged();
                        break;
                    case "dlg":
                    case "dlgcancel":
                    {
                        var d = PendingDialog;
                        PendingDialog = null;
                        if (d == null)
                        {
                            GD.Print($"replay: line {n + 1}: no dialog open for \"{Arg(2)}\"");
                            diverged++;
                            break;
                        }
                        if (f[1] == "dlg")
                        {
                            var fields = SessionRecorder.Fields(d);
                            foreach (var kv in Arg(3).Split(';', StringSplitOptions.RemoveEmptyEntries))
                                if (kv.IndexOf('=') is var eq and > 0 && int.Parse(kv[..eq], Inv) is var fi && fi < fields.Count)
                                    SessionRecorder.SetValue(fields[fi], Uri.UnescapeDataString(kv[(eq + 1)..]));
                            d.EmitSignal(AcceptDialog.SignalName.Confirmed);
                        }
                        else
                            d.EmitSignal(AcceptDialog.SignalName.Canceled);
                        d.QueueFree();
                        break;
                    }
                    case "save":
                        // saving puts a start in no sector into one (the map changes there)
                        Doc.RepairStart();
                        break;
                    case "saved":
                        // what saving does to the map (its revision, the .MAPX's info), without the file
                        Doc.SaveBytes(Enum.Parse<MapFormat>(Arg(2)), true, out _);
                        break;
                    case "status":
                        recordedStatus = Arg(2);
                        break;
                    case "mark":
                        GD.Print($"replay: line {n + 1}: the problem was reported here: \"{Arg(2)}\"");
                        break;
                    case "error":
                        if (verbose)
                            GD.Print($"replay: line {n + 1}: the session had an {Arg(2)} here: {Arg(3)} ({Arg(4)})");
                        break;
                    case "check":
                    {
                        string fp = SessionRecorder.Fingerprint(Doc);
                        string want = expected != null ? (checks < expected.Count ? expected[checks] : "(none)") : Arg(2);
                        got.Add($"{fp}\t{Arg(3)}");
                        if (fp != want)
                        {
                            diverged++;
                            if (verbose && diverged <= 5)
                            {
                                // what the edit said: the status written just after its check
                                string said = recordedStatus;
                                for (int j = n + 1; j < Math.Min(lines.Length, n + 4); j++)
                                    if (lines[j].Split('\t') is { Length: > 2 } sf && sf[1] == "status")
                                    {
                                        said = sf[2];
                                        break;
                                    }
                                GD.Print($"replay: line {n + 1}: after \"{Arg(3)}\" the map differs (check {checks + 1}); "
                                    + $"recorded \"{said}\", now \"{_status.Text}\"");
                            }
                        }
                        checks++;
                        break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"replay {dir}: line {at}: {e}");
            diverged++;
        }
        finally
        {
            PendingDialog?.QueueFree();
            PendingDialog = null;
            // the settings as they were (a replay's never reach the user's)
            _replaySprite = null;
            _replaySpriteTile = null;
            _replayEditNew = null;
            Replaying = false;
            ApplyState(stateBefore);
            Rec = rec;
            Keys = keys;
            ForgeInput.ShiftOverride = ForgeInput.AltOverride = ForgeInput.CtrlOverride = null;
        }
        if (expected != null && got.Count != expected.Count)
        {
            diverged++;
            GD.Print($"replay: {got.Count} checks, expected.txt has {expected.Count}");
        }
        if (accept)
        {
            File.WriteAllLines(expPath, new[] { "# map fingerprints after each edit (forge --replay --accept)" }.Concat(got));
            GD.Print($"replay: accepted {got.Count} checks into {expPath}");
        }
        if (verbose)
            GD.Print($"replay {Path.GetFileName(dir)}: {lines.Length} lines, {checks} checks, " + (diverged == 0 ? "all as expected" : $"{diverged} differ"));
        return accept ? 0 : diverged;
    }

    static void Mods(InputEventWithModifiers e, string m)
    {
        e.ShiftPressed = m.Contains('s');
        e.CtrlPressed = m.Contains('c');
        e.AltPressed = m.Contains('a');
        // the modes ask whether Shift is held (no grid snap; the drawing boxes: Shift from the middle, Alt square, Ctrl free)
        ForgeInput.ShiftOverride = e.ShiftPressed;
        ForgeInput.AltOverride = e.AltPressed;
        ForgeInput.CtrlOverride = e.CtrlPressed;
    }

    /// <summary>--regress: every replay in forge/tests/replays; the number that failed.</summary>
    async Task<int> Regress()
    {
        int failed = 0, n = 0;
        if (!Directory.Exists(ReplayTests))
        {
            GD.Print($"regress: no {ReplayTests}");
            return 0;
        }
        foreach (var dir in Directory.GetDirectories(ReplayTests).OrderBy(d => d))
        {
            n++;
            int d = await Replay(dir, verbose: false);
            string note = File.Exists(Path.Combine(dir, "note.txt")) ? File.ReadAllText(Path.Combine(dir, "note.txt")).Trim() : "";
            GD.Print($"regress: {Path.GetFileName(dir)}: {(d == 0 ? "ok" : $"FAILED ({d} checks differ)")}{(note != "" ? " - " + note : "")}");
            if (d != 0)
                failed++;
        }
        GD.Print(failed == 0 ? $"regress: {n} replays passed" : $"regress: {failed} of {n} replays FAILED");
        return failed;
    }

    /// <summary>--addtest=&lt;session|last&gt; --name=&lt;name&gt; [--note=...]: a session copied into forge/tests/replays and
    /// replayed once, its checks written as expected.txt.</summary>
    async Task<int> AddTest(string session, string name, string note)
    {
        string dir = Path.Combine(ReplayTests, name);
        CopyFolder(session, dir);
        File.Delete(Path.Combine(dir, "expected.txt"));
        if (note != null)
            File.WriteAllText(Path.Combine(dir, "note.txt"), note + "\n");
        // replayed as recorded first: it must reproduce the session, then its checks become the test's
        int d = await Replay(dir);
        if (d != 0)
            GD.Print($"addtest: the replay differs from the recording in {d} checks; expected.txt is taken from the replay");
        await Replay(dir, accept: true);
        GD.Print($"addtest: {dir}");
        return 0;
    }

    /// <summary>The recording and replay options; true when one ran (the window then quits with its result).</summary>
    bool RunRecordingOptions(string[] args)
    {
        string Opt(string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}="))?[(name.Length + 3)..];
        Task<int> job = null;
        if (Opt("replay") is { } r)
        {
            string until = Opt("until");
            job = Replay(SessionFolder(r), args.Contains("--accept"), until != null ? int.Parse(until, Inv) : 0);
            if (Opt("save") is { } save)
                job = job.ContinueWith(t =>
                {
                    // the map where the replay stopped (--until), as a .MAP to dump and compare
                    File.WriteAllBytes(save, Doc.SaveBytes(MapFormat.Map, false, out _));
                    GD.Print($"replay: saved {save}");
                    return t.Result;
                });
            if (Opt("shot") is { } shot)
                job = job.ContinueWith(t =>
                {
                    Callable.From(() => GetViewport().GetTexture().GetImage().SavePng(shot)).CallDeferred();
                    return t.Result;
                });
        }
        else if (Opt("rebuild") is { } rb)
            job = RebuildTest(Path.GetFullPath(rb));
        else if (args.Contains("--regress"))
            job = Regress();
        else if (Opt("addtest") is { } a)
            job = AddTest(SessionFolder(a), Opt("name") ?? Path.GetFileName(SessionFolder(a)), Opt("note"));
        if (job == null)
            return false;
        job.ContinueWith(t =>
        {
            if (t.IsFaulted)
                GD.PrintErr("forge: " + t.Exception?.GetBaseException());
            Callable.From(() => GetTree().Quit(t.IsFaulted ? 1 : t.Result)).CallDeferred();
        });
        return true;
    }
}
