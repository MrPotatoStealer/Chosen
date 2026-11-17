using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace Chosen.Core;

/// <summary>
/// The in-game console: the key left of 1 (backtick, or § on Nordic keyboards) or F1 toggles it. Type a command, or a console variable's name to see it and
/// "name value" to set it. Up/Down walk the history, Tab completes names.
/// </summary>
public partial class GameConsole : CanvasLayer
{
    public sealed class Command
    {
        public string Name, Usage, Help;
        public Action<string[]> Run;
    }

    static GameConsole _instance;
    static readonly Dictionary<string, Command> _commands = new();
    static readonly List<string> _pending = new();

    public static bool IsOpen => _instance != null && _instance.Visible;

    Panel _panel;
    TextureRect _back;
    RichTextLabel _log;
    LineEdit _input;
    readonly List<string> _history = new();
    int _historyPos;

    public static void Register(string name, string usage, string help, Action<string[]> run) =>
        _commands[name] = new Command { Name = name, Usage = usage, Help = help, Run = run };

    /// <summary>Prints to the console (and Godot's output).</summary>
    public static void Print(string text)
    {
        GD.Print(text);
        if (_instance?._log == null)
            _pending.Add(text);
        else
            _instance.Queue(text);
    }

    static string Escape(string s) => s.Replace("[", "[lb]");

    public override void _Ready()
    {
        _instance = this;
        Layer = 100;
        Visible = false;
        _panel = new Panel { ClipContents = true };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _panel.CustomMinimumSize = new Vector2(0, 360);
        _panel.SelfModulate = new Color(1, 1, 1, 0.9f);
        AddChild(_panel);
        Render.HdrOutput.Ui(_panel);
        // DarkPlaces' console background (gfx/conback: Blood's gargoyle and logo), stretched over the whole screen
        // with its bottom at the console's bottom edge, at scr_conalpha 0.9
        var back = Data.TileArt.Present ? Data.TileArt.Picture(2046, false) : null;      // Blood's title screen
        if (back != null)
        {
            _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.9f) });
            _back = new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(back),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                Modulate = new Color(1, 1, 1, 0.9f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _panel.AddChild(_back);
        }
        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.OffsetLeft = 8; box.OffsetRight = -8; box.OffsetTop = 6; box.OffsetBottom = -6;
        _panel.AddChild(box);
        _log = new RichTextLabel { ScrollFollowing = true, SelectionEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, BbcodeEnabled = true };
        box.AddChild(_log);
        _input = new LineEdit { PlaceholderText = "command (help lists them)", KeepEditingOnTextSubmit = true };
        _input.TextSubmitted += OnSubmit;
        _input.GuiInput += OnInputKey;
        box.AddChild(_input);
        foreach (var p in _pending)
            Queue(p);
        _pending.Clear();

        Register("help", "help [command]", "list the commands, or show one", a =>
        {
            if (a.Length > 1 && _commands.TryGetValue(a[1], out var c))
                Print($"{c.Usage}: {c.Help}");
            else
                foreach (var c2 in _commands.Values.OrderBy(c => c.Name))
                    Print($"  {c2.Usage,-32} {c2.Help}");
        });
        Register("cvarlist", "cvarlist [prefix]", "list the console variables", a =>
        {
            foreach (var c in Cvars.All.Where(c => a.Length < 2 || c.Name.StartsWith(a[1])).OrderBy(c => c.Name))
                Print($"  {c.Name} \"{c.Value}\"{(c.Saved ? " (saved)" : "")}  {c.Help}");
        });
        Register("echo", "echo <text>", "print text", a => Print(string.Join(' ', a.Skip(1))));
        Register("clear", "clear", "clear the console", _ =>
        {
            _log.Clear();
            _lines.Clear();
            _queued.Clear();
        });
        Register("toggleconsole", "toggleconsole", "open or close the console", _ => Show(!Visible));
    }

    /// <summary>Runs a console line (several commands separated by ';').</summary>
    public static void Execute(string line)
    {
        foreach (var part in line.Split(';'))
        {
            var args = Tokenize(part);
            if (args.Length == 0)
                continue;
            // names are case-insensitive, as in DarkPlaces (MPKFA works)
            if (_commands.TryGetValue(args[0], out var cmd) || _commands.TryGetValue(args[0].ToLowerInvariant(), out cmd))
            {
                try { cmd.Run(args); }
                catch (Exception e) { Print($"{args[0]}: {e.Message}"); }
            }
            else if ((Cvars.Find(args[0]) ?? Cvars.Find(args[0].ToLowerInvariant())) is Cvar cv)
            {
                if (args.Length > 1)
                    cv.Set(string.Join(' ', args.Skip(1)));
                else
                    Print($"\"{cv.Name}\" is \"{cv.Value}\" (default \"{cv.Default}\")  {cv.Help}");
            }
            else
                Print($"unknown command \"{args[0]}\"");
        }
    }

    /// <summary>Splits on spaces; "quoted text" stays one argument.</summary>
    static string[] Tokenize(string s)
    {
        var list = new List<string>();
        var cur = new StringBuilder();
        bool quoted = false, any = false;
        foreach (char ch in s)
        {
            if (ch == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (any) list.Add(cur.ToString());
                cur.Clear();
                any = false;
                continue;
            }
            cur.Append(ch);
            any = true;
        }
        if (any) list.Add(cur.ToString());
        return list.ToArray();
    }

    // The log takes new lines in one batch, and only while the console is open: appending to a RichTextLabel
    // re-lays out its text (about 15 ms with a few hundred lines), and every game message is printed here.
    const int MaxLines = 1000;
    readonly List<string> _lines = new();      // what the log shows (the last MaxLines)
    readonly List<string> _queued = new();     // printed since the log was last brought up to date

    void Queue(string text)
    {
        _queued.Add(text);
        _lines.Add(text);
        if (_lines.Count > MaxLines * 3 / 2)
            _lines.RemoveRange(0, _lines.Count - MaxLines);
    }

    void FlushLog()
    {
        if (_queued.Count == 0)
            return;
        var sb = new StringBuilder();
        if (_log.GetParagraphCount() + _queued.Count > MaxLines * 3 / 2)
        {
            // trimmed: show the kept lines afresh
            _log.Clear();
            foreach (var l in _lines)
                sb.Append(Escape(l)).Append('\n');
        }
        else
            foreach (var l in _queued)
                sb.Append(Escape(l)).Append('\n');
        _queued.Clear();
        _log.AppendText(sb.ToString());
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        FlushLog();
        // half the screen, as DarkPlaces' console; the background keeps the screen's size, bottom-aligned
        var screen = GetViewport().GetVisibleRect().Size;
        float h = Mathf.Max(200, Mathf.Floor(screen.Y / 2));
        if (_panel.CustomMinimumSize.Y != h)
            _panel.CustomMinimumSize = new Vector2(0, h);
        if (_back != null)
        {
            _back.Size = screen;
            _back.Position = new Vector2(0, h - screen.Y);
        }
    }

    /// <summary>Opens or closes the console.</summary>
    public static void Show(bool open)
    {
        var c = _instance;
        if (c == null || c.Visible == open)
            return;
        c.Visible = open;
        // the mouse stays captured (the game is paused and ignores it while the console is open)
        if (open)
        {
            c._input.GrabFocus();
            c._input.Clear();
        }
        else
            c._input.ReleaseFocus();
    }

    // the key left of 1 by position (backtick on US keyboards, § on Nordic ones), backtick by name, or F1
    static bool IsToggleKey(InputEventKey k) =>
        k.PhysicalKeycode == Key.Quoteleft || k.Keycode == Key.Quoteleft || k.Keycode == Key.Section || k.Keycode == Key.F1;

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo && IsToggleKey(k))
        {
            Show(!Visible);
            GetViewport().SetInputAsHandled();
        }
    }

    void OnSubmit(string text)
    {
        _input.Clear();
        if (text.Trim() == "")
            return;
        Print("] " + text);
        _history.Add(text);
        _historyPos = _history.Count;
        Execute(text);
    }

    void OnInputKey(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed)
            return;
        if (k.Keycode == Key.Up && _history.Count > 0)
        {
            _historyPos = Math.Max(0, _historyPos - 1);
            SetInput(_history[_historyPos]);
        }
        else if (k.Keycode == Key.Down && _history.Count > 0)
        {
            _historyPos = Math.Min(_history.Count, _historyPos + 1);
            SetInput(_historyPos < _history.Count ? _history[_historyPos] : "");
        }
        else if (k.Keycode == Key.Tab)
        {
            string t = _input.Text.Trim();
            var names = _commands.Keys.Concat(Cvars.All.Select(c => c.Name)).Where(n => n.StartsWith(t)).OrderBy(n => n).ToList();
            if (names.Count == 1)
                SetInput(names[0] + " ");
            else if (names.Count > 1)
                Print("  " + string.Join("  ", names));
        }
        else
            return;
        _input.AcceptEvent();
    }

    void SetInput(string s)
    {
        _input.Text = s;
        _input.CaretColumn = s.Length;
    }
}
