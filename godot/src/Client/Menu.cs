using System;
using System.Collections.Generic;
using Chosen.Core;
using Chosen.Data;
using Godot;

namespace Chosen.Client;

/// <summary>
/// Blood's menus, drawn like Blood's (NBlood's CGameMenu): in Blood's 320x200 space with its tile fonts (font 1 the
/// menu items, 2 the titles, 3 small text), the title bar (tile 2038), the focused item's shade pulsing, over the
/// title screen (tile 2046) before a game starts or over the dimmed game. Escape opens it in the game (which pauses)
/// and goes back a page; up/down/enter or the mouse choose, left/right change a setting.
/// </summary>
public partial class Menu : Control
{
    public static readonly Cvar WeaponsV10x = Cvars.Register("b3d_weaponsv10x", "1",
        "NBlood's v1.0x weapons balance: the voodoo doll's alt fire hits every target at full strength for 20 charges, the life leech heals, flares also hit", saved: true);
    public static readonly Cvar FixBlood = Cvars.Register("b3d_fixblood", "1",
        "Blood's own bugs fixed: a floor moving away from under you while you stand still (E2M1's iceberg) lets you fall at once, not at your first step", saved: true);
    public static readonly Cvar Skill = Cvars.Register("b3d_skill", "2", "difficulty: 0 Still Kicking .. 4 Extra Crispy (from the next level)", saved: true);
    /// <summary>NBlood's cl_autoaim (gAutoAim): weapons aim at a target near the crosshair.</summary>
    public static readonly Cvar AutoAim = Cvars.Register("cl_autoaim", "3", "auto aim: 0 never, 1 always, 2 hitscan weapons only, 3 pitchfork only (the voodoo doll and life leech always aim)", saved: true);
    static readonly Cvar Fullscreen = Cvars.Register("vid_fullscreen", "1", "1: fullscreen (borderless, at the desktop's resolution)", saved: true);
    static readonly Cvar VSync = Cvars.Register("vid_vsync", "1", "1: wait for the display's refresh", saved: true);

    static readonly string[] SkillNames = { "STILL KICKING", "PINK ON THE INSIDE", "LIGHTLY BROILED", "WELL DONE", "EXTRA CRISPY" };

    /// <summary>A menu entry: an action, or a setting (a cvar shown with its value).</summary>
    sealed class Item
    {
        public string Label;
        public Func<string> Live;                // a label that changes while it is shown
        public string Text => Live?.Invoke() ?? Label;
        public Action Run;                       // enter / click
        public string VarName;                   // a setting: its cvar (looked up when used: some register late)
        public Cvar Var => VarName == null ? null : Cvars.Find(VarName);
        public float Min, Max, Step;             // a numeric setting's range (Step 0: on/off)
        public string[] Choices;                 // a setting with named values (0..n-1)
        public Func<bool> Enabled = () => true;
    }

    sealed class Page
    {
        public string Title;
        public bool Small;                       // settings: font 3 rows with values
        public readonly List<Item> Items = new();
        public int Focus;
        public Action Draw;                      // a page drawn its own way (help)
        public float Top = 45, Step = 20;        // the first item's top and the row step (320x200, NBlood's layouts)
        public bool Drip = true;                 // the blood drip QAV (itemBloodQAV) over the page
        public bool Bare;                        // no title bar, a black screen (the credits)
    }

    readonly Stack<Page> _pages = new();
    Page _main;
    int _episode;

    /// <summary>The title screen: no game picked yet (the menu shows over Blood's title art, with its music).</summary>
    public bool Title { get; private set; }
    public bool IsOpen => _pages.Count > 0;

    /// <summary>New game: (first map of the episode, skill).</summary>
    public event Action<string> NewGame;
    public event Action Closed;
    public event Action Quit;

    public override void _Ready()
    {
        Render.HdrOutput.Ui(this);
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        ApplyVideo();
        Fullscreen.Changed += ApplyVideo;
        VSync.Changed += ApplyVideo;
        BuildPages();
    }

    /// <summary>Godot's --windowed, or a scripted run (screenshots, replays, reports): the saved fullscreen setting is
    /// ignored, so test runs open as windows.</summary>
    static readonly bool ForceWindowed = System.Array.IndexOf(OS.GetCmdlineArgs(), "--windowed") >= 0 ||
        System.Array.Exists(OS.GetCmdlineUserArgs(), a => a.StartsWith("--shot") || a.StartsWith("--play") || a.StartsWith("--report") || a.StartsWith("--views") || a.StartsWith("--bugs"));

    static bool _focused;

    static void ApplyVideo()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        var mode = Fullscreen.Bool && !ForceWindowed ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode)
            DisplayServer.WindowSetMode(mode);
        // the window opens without focus (project.godot's no_focus: a test run never takes the keyboard, not even
        // while it opens): a test run (pipeline's launchers: --position, or --noinput) keeps it so and goes off the
        // desktop; a played game takes the focus once
        if (Mouse.NoInput)
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
            // (Godot keeps --position on a screen, at its corner: moved off it now)
            if (System.Array.IndexOf(OS.GetCmdlineArgs(), "--position") >= 0)
                DisplayServer.WindowSetPosition(new Vector2I(-30000, -30000));
        }
        else if (!_focused)
        {
            _focused = true;
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, false);
            DisplayServer.WindowMoveToForeground();
        }
        DisplayServer.WindowSetVsyncMode(VSync.Bool ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }

    // ---------------------------------------------------------------- pages
    static Item Action(string label, Action run) => new() { Label = label, Run = run };
    static Item Toggle(string label, string cvar) => new() { Label = label, VarName = cvar };
    static Item Slider(string label, string cvar, float min, float max, float step) =>
        new() { Label = label, VarName = cvar, Min = min, Max = max, Step = step };
    static Item Choice(string label, string cvar, params string[] choices) => new() { Label = label, VarName = cvar, Choices = choices };

    void BuildPages()
    {
        _main = new Page { Title = "BLOOD", Top = 45 };
        _main.Items.Add(Action("NEW GAME", () => { Addons.Activate(null); Push(EpisodePage()); }));
        // Refreshed Supply's add-ons (Death Wish, Cryptic Passage, MARROW) that have been converted (Data.Addons)
        if (Addons.All.Count > 0)
            _main.Items.Add(Action("ADD-ONS", () => Push(AddonsPage())));
        _main.Items.Add(Action("OPTIONS", () => Push(OptionsPage())));
        _main.Items.Add(Action("HELP", () => Push(HelpPage())));
        _main.Items.Add(Action("CREDITS", () => Push(CreditsPage())));
        _main.Items.Add(Action("QUIT", () => Quit?.Invoke()));
    }

    /// <summary>Refreshed Supply's add-on menu: one picked, its episodes (its .ini) are the game's.</summary>
    Page AddonsPage()
    {
        var p = new Page { Title = "ADD-ONS", Top = 55 };
        p.Step = _2d.FontHeight(1) + 8;
        foreach (var a in Addons.All)
        {
            var addon = a;
            p.Items.Add(Action(a.Title.ToUpperInvariant(), () => { Addons.Activate(addon); Push(EpisodePage()); }));
        }
        return p;
    }

    Page EpisodePage()
    {
        var p = new Page { Title = "EPISODES" };
        var eps = LevelInfo.AllEpisodes;
        // SetupEpisodeMenu: 100 less 10 per episode shown, kept within 35..55, rows the font's height plus 8 apart
        int shown = 0;
        foreach (var e in eps)
            if (!e.BloodBathOnly && e.Maps.Count > 0)
                shown++;
        p.Top = Mathf.Clamp(100 - 10 * shown, 35, 55);
        p.Step = _2d.FontHeight(1) + 8;
        for (int i = 0; i < eps.Count; i++)
        {
            var ep = eps[i];
            if (ep.BloodBathOnly || ep.Maps.Count == 0)
                continue;
            int index = i;
            p.Items.Add(new Item
            {
                Label = ep.Title,
                Run = () => { _episode = index; Push(SkillPage()); },
                // only episodes whose first level's .MAP is there (pipeline/export_maps.py)
                Enabled = () => Data.Addons.BuildMapPath(ep.Maps[0]) != null,
            });
        }
        return p;
    }

    Page SkillPage()
    {
        var p = new Page { Title = "DIFFICULTY", Focus = Mathf.Clamp(Skill.Int, 0, 4), Top = 50 };
        for (int i = 0; i < SkillNames.Length; i++)
        {
            int s = i;
            p.Items.Add(Action(SkillNames[i], () =>
            {
                Skill.Set(s.ToString());
                var map = LevelInfo.AllEpisodes[_episode].Maps[0];
                Close();
                NewGame?.Invoke(map);
            }));
        }
        return p;
    }

    Page OptionsPage()
    {
        var p = new Page { Title = "OPTIONS" };
        p.Items.Add(Action("GAME", () => Push(Settings("GAME",
            Choice("HUD", "b3d_hudsize", "NONE", "MINIMAL", "SMALL", "STATUS BAR"),
            Toggle("CROSSHAIR", "b3d_crosshair"),
            Choice("AUTO AIM", "cl_autoaim", "NEVER", "ALWAYS", "HITSCAN ONLY", "PITCHFORK ONLY"),
            Toggle("LEVEL STATS", "b3d_levelstats"),
            Toggle("ALWAYS RUN", "cl_alwaysrun"),
            Toggle("VIEW BOB", "b3d_vbob"),
            Toggle("WEAPON SWAY", "b3d_hbob"),
            Toggle("CAMERA FOLLOWS SLOPES", "b3d_slopelook"),
            Toggle("V1.0X WEAPONS BALANCE", "b3d_weaponsv10x"),
            Toggle("FIX BLOOD'S BUGS", "b3d_fixblood"),
            Choice("CUTSCENES", "b3d_cutscenes", "ORIGINAL", "REMASTERED"),
            Toggle("SUBTITLES", "b3d_subtitles"),
            Toggle("SKIP INTRO", "b3d_skipintro")))));
        p.Items.Add(Action("VIDEO", () => Push(Settings("VIDEO",
            Toggle("FULLSCREEN", "vid_fullscreen"),
            Toggle("VSYNC", "vid_vsync"),
            Slider("BRIGHTNESS", "brightness", 0.5f, 2f, 0.05f),
            Action("HDR", () => Push(Settings("HDR",
                Toggle("HDR OUTPUT", "r_hdr"),
                Slider("WHITE LEVEL", "r_hdrwhite", 80f, 600f, 20f),
                Slider("PEAK BRIGHTNESS", "r_hdrpeak", 200f, Render.HdrOutput.PeakLimit, 20f),
                Slider("HIGHLIGHTS", "r_hdrhighlight", 1f, 12f, 0.5f),
                Slider("UI LEVEL", "r_hdrui", 80f, 600f, 20f),
                new Item { Live = () => Render.HdrOutput.Status, Run = () => { } }))),
            Action("RENDERER", () => Push(Settings("RENDERER",
                Choice("GRAPHICS API", "r_driver", "DIRECT3D 12", "VULKAN"),
                new Item { Live = () => Render.RenderDriver.Status, Run = Render.RenderDriver.Restart }))),
            Toggle("SPRITE SHADOWS", "b3d_spriteshadows"),
            Slider("FIELD OF VIEW", "fov", 60, 130, 5),
            Toggle("VOXEL MODELS", "b3d_voxels"),
            Choice("CLASSIC PALETTE", "r_useindexedcolortextures", "OFF", "TEXTURES", "POST PROCESS"),
            Choice("PERFORMANCE", "r_perfoverlay", "OFF", "NUMBERS", "GRAPH")))));
        // not Blood's own: light and atmosphere added on top (Render.Enhanced), all off by default
        p.Items.Add(Action("ENHANCED", () => Push(Settings("ENHANCED",
            Action("LIGHTING", () => Push(Settings("LIGHTING",
                Toggle("DYNAMIC LIGHTS", "r_dynamiclights"),
                Toggle("DYNAMIC SHADOWS", "r_shadows"),
                Slider("SHADOW CASTERS", "r_shadowlights", 0f, 24f, 1f),
                Slider("SHADOW DISTANCE", "r_shadowdist", 250f, 3000f, 250f),
                Slider("LIGHT RANGE", "r_lightrange", 0.5f, 3f, 0.25f),
                Slider("LIGHT FLICKER", "r_lightflicker", 0f, 2f, 0.25f),
                Choice("AMBIENT OCCLUSION", "r_ao", "OFF", "SSAO", "GTAO"),
                Slider("AO INTENSITY", "r_ao_intensity", 0f, 2f, 0.1f),
                Choice("AO RESOLUTION", "r_ao_half", "FULL", "HALF"),
                Slider("SECTOR CONTRAST", "b3d_shadecontrast", 1f, 2f, 0.1f),
                Slider("MOONLIGHT", "r_moonlight", 0f, 2f, 0.25f),
                Slider("MOONLIT FOG", "r_moonfog", 0f, 2f, 0.25f),
                Slider("GLOWING PIXELS", "r_glow", 0f, 2f, 0.25f)))),
            Action("WATER", () => Push(Settings("WATER",
                Toggle("TRANSPARENT WATER", "r_waterportals"),
                Slider("WATER OPACITY", "r_wateralpha", 0f, 1f, 0.05f),
                Slider("WATER FOG", "r_waterfog", 0f, 4f, 0.25f),
                Toggle("LIGHT UNDER WATER", "r_caustics"),
                Slider("UNDERWATER VIEW", "r_waterwarp", 0f, 2f, 0.25f),
                Toggle("MIST ON WATER", "r_watermist"),
                Toggle("RIPPLES", "r_waterripple"),
                Toggle("SKY ON WATER", "r_watershine"),
                Toggle("REFLECTIONS", "r_waterreflect")))),
            Toggle("FOG", "r_fog"),
            Slider("FOG DENSITY", "r_fogdensity", 0.25f, 4f, 0.25f),
            Slider("FOG LIGHT GLOW", "r_fogglow", 0f, 4f, 0.25f),
            Slider("FOG ON SKY", "r_fogsky", 0f, 2f, 0.25f),
            Toggle("FOG BLUR", "r_fogblur"),
            Toggle("GROUND MIST", "r_groundmist"),
            Slider("MIST DENSITY", "r_groundmistdensity", 0.25f, 3f, 0.25f),
            Action("WEATHER", () => Push(Settings("WEATHER",
                Choice("WEATHER", "r_weather", "OFF", "LEVEL", "RAIN", "SNOW"),
                Slider("WEATHER AMOUNT", "r_weatheramount", 0.1f, 1f, 0.1f),
                Slider("RAIN AMOUNT", "r_rainamount", 0.05f, 1f, 0.05f),
                Slider("SNOW AMOUNT", "r_snowamount", 0.05f, 2f, 0.05f),
                Slider("LEAVES AMOUNT", "r_leavesamount", 0.05f, 2f, 0.05f),
                Slider("WIND", "r_wind", 0f, 3f, 0.25f),
                Choice("LIGHTNING", "r_lightning", "OFF", "RAIN LEVELS", "ALL LEVELS"),
                Toggle("RAIN RIPPLES", "r_rainripples"),
                Choice("FALLING ASH", "r_ash", "OFF", "FIRE LEVELS", "ALL LEVELS")))),
            Action("ATMOSPHERE", () => Push(Settings("ATMOSPHERE",
                Toggle("EMBERS", "r_embers"),
                Toggle("SMOKE", "r_smoke"),
                Toggle("DUST", "r_dust"),
                Slider("AREA COLOR", "r_areacolor", 0f, 2f, 0.25f),
                Toggle("EXPLOSION AFTERMATH", "r_aftermath")))),
            Action("ALL EFFECTS ON", () => Render.Atmosphere.SetAll(true)),
            Action("ALL EFFECTS OFF", () => Render.Atmosphere.SetAll(false))))));
        p.Items.Add(Action("SOUND", () => Push(Settings("SOUND",
            Slider("SOUND VOLUME", "volume", 0, 1, 0.05f),
            // (snd_smooth: crisp holds each sample as Blood's audiolib mixes, smooth interpolates)
            Choice("SOUND QUALITY", "snd_smooth", "CRISP", "SMOOTH"),
            Slider("MUSIC VOLUME", "bgmvolume", 0, 1, 0.05f),
            Toggle("CD MUSIC", "b3d_cdmusic"),
            Choice("MIDI DEVICE", "b3d_mididevice", "SC-55", "OPL3 FM")))));
        p.Items.Add(Action("MOUSE", () => Push(Settings("MOUSE",
            Slider("SENSITIVITY", "sensitivity", 0.5f, 20, 0.5f),
            Toggle("INVERT", "m_invert")))));
        return p;
    }

    static Page Settings(string title, params Item[] items)
    {
        var p = new Page { Title = title, Small = true, Top = 45, Step = 12 };
        foreach (var i in items)
        {
            if (i.Run == null)
                i.Enabled = () => i.Var != null;       // (a submenu is always there)
            p.Items.Add(i);
        }
        // (no scrolling: a long page's rows closer together, all on the 200 lines)
        p.Step = Mathf.Min(p.Step, Mathf.Floor((198f - p.Top) / Mathf.Max(items.Length, 1)));
        return p;
    }

    Page HelpPage()
    {
        var p = new Page { Title = "HELP", Drip = false };
        p.Draw = () =>
        {
            string[] lines =
            {
                "MOVE  W A S D     RUN  SHIFT", "JUMP  SPACE     CROUCH  C OR CTRL", "OPEN / USE  E",
                "FIRE  MOUSE 1     ALT FIRE  MOUSE 2", "WEAPONS  1-0     NEXT / PREVIOUS  WHEEL",
                "LAST WEAPON  Q     HOLSTER  X", "USE ITEM  ENTER     SELECT ITEM  [ ]",
                "MEDKIT  M   CRYSTAL BALL  K", "BEAST VISION  B   JUMP BOOTS  J", "TURN AROUND  BACKSPACE",
                "VIEW SIZE  - AND =     CONSOLE  ` OR F1", "ENHANCED OPTIONS OFF / BACK  F10", "ESCAPE: BACK",
            };
            for (int i = 0; i < lines.Length; i++)
                _2d.Text(3, lines[i], 160, 44 + i * 11, 0, 0, 1);
        };
        return p;
    }

    /// <summary>menuCredits (itemCreditsQAV): QAV CREDITS looping once, centred on a black screen (not tiled: the item's
    /// bWideScreen is off); Escape goes back.</summary>
    Page CreditsPage()
    {
        var p = new Page { Title = "", Drip = false, Bare = true };
        ulong start = Time.GetTicksMsec();
        p.Draw = () =>
        {
            QavData.Load();
            var q = QavData.Get(257);
            if (q == null || q.Duration <= 0)
                return;
            int ticks = (int)((Time.GetTicksMsec() - start) * 120 / 1000 % (ulong)q.Duration);
            _2d.Qav(q, ticks, q.X, q.Y, 10);
        };
        return p;
    }

    // ---------------------------------------------------------------- opening and closing
    /// <summary>Opens the main menu; title: over Blood's title screen (no game yet).</summary>
    public void Open(bool title = false)
    {
        Title = title;
        _pages.Clear();
        _main.Focus = 0;
        Push(_main);
        Mouse.Set(Input.MouseModeEnum.Visible);
    }

    /// <summary>Opens a page by name over the main menu (debugging and screenshots: the menu_page command).</summary>
    public void OpenPage(string name, bool title = false)
    {
        Open(title);
        var p = name switch { "episodes" => EpisodePage(), "addons" => AddonsPage(), "difficulty" => SkillPage(), "options" => OptionsPage(), "help" => HelpPage(),
            "credits" => CreditsPage(), "game" => null, _ => null };
        if (name == "game")
        {
            Push(OptionsPage());
            _pages.Peek().Items[0].Run();
            return;
        }
        if (p != null)
            Push(p);
        if (name is "video" or "renderer" or "hdr" or "enhanced" or "lighting" or "water" or "weather" or "atmosphere")
        {
            Push(OptionsPage());
            _pages.Peek().Items[name is "enhanced" or "lighting" or "water" or "weather" or "atmosphere" ? 2 : 1].Run();
            if (name is not ("video" or "enhanced"))
                _pages.Peek().Items.Find(i => i.Label == name.ToUpperInvariant()).Run();
        }
    }

    void Push(Page p)
    {
        _pages.Push(p);
        QueueRedraw();
    }

    void Back()
    {
        if (Title && _pages.Count == 1)
            return;     // the title screen's main menu stays (no level behind it yet)
        _pages.Pop();
        if (_pages.Count == 0)
            Close();
        QueueRedraw();
    }

    public void Close()
    {
        _pages.Clear();
        Title = false;
        Mouse.Capture();
        QueueRedraw();
        Closed?.Invoke();
    }

    // ---------------------------------------------------------------- input
    public override void _Input(InputEvent e)
    {
        if (!IsOpen || GameConsole.IsOpen)
            return;
        var p = _pages.Peek();
        if (e is InputEventKey k && k.Pressed)
        {
            switch (k.Keycode)
            {
                case Key.Escape:
                case Key.Backspace:
                    Back();
                    break;
                case Key.Up:
                case Key.W:
                    Move(p, -1);
                    break;
                case Key.Down:
                case Key.S:
                    Move(p, 1);
                    break;
                case Key.Left:
                case Key.A:
                    Change(p, -1);
                    break;
                case Key.Right:
                case Key.D:
                    Change(p, 1);
                    break;
                case Key.Enter:
                case Key.KpEnter:
                case Key.Space:
                    Activate(p);
                    break;
                default:
                    return;
            }
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseMotion mm)
        {
            int i = ItemAt(p, mm.Position);
            if (i >= 0 && i != p.Focus)
            {
                p.Focus = i;
                QueueRedraw();
            }
        }
        else if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
                Move(p, -1);
            else if (mb.ButtonIndex == MouseButton.WheelDown)
                Move(p, 1);
            else if (mb.ButtonIndex == MouseButton.Left && ItemAt(p, mb.Position) is int i && i >= 0)
            {
                p.Focus = i;
                Activate(p);
            }
            else if (mb.ButtonIndex == MouseButton.Right)
                Back();
            GetViewport().SetInputAsHandled();
        }
    }

    void Move(Page p, int d)
    {
        if (p.Items.Count == 0)
            return;
        for (int n = 0; n < p.Items.Count; n++)
        {
            p.Focus = (p.Focus + d + p.Items.Count) % p.Items.Count;
            if (p.Items[p.Focus].Enabled())
                break;
        }
        QueueRedraw();
    }

    void Activate(Page p)
    {
        if (p.Items.Count == 0)
            return;
        var it = p.Items[p.Focus];
        if (!it.Enabled())
            return;
        if (it.Run != null)
            it.Run();
        else
            Change(p, 1, wrap: true);
    }

    void Change(Page p, int d, bool wrap = false)
    {
        if (p.Items.Count == 0)
            return;
        var it = p.Items[p.Focus];
        if (it.Var == null)
            return;
        if (it.Choices != null)
        {
            int n = it.Choices.Length, v = it.Var.Int + d;
            v = wrap ? (v + n) % n : Mathf.Clamp(v, 0, n - 1);
            it.Var.Set(v.ToString());
        }
        else if (it.Step == 0)
            it.Var.Set(it.Var.Bool ? "0" : "1");
        else
        {
            float v = Mathf.Clamp(Mathf.Snapped(it.Var.Float + d * it.Step, it.Step), it.Min, it.Max);
            it.Var.Set(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        QueueRedraw();
    }

    // ---------------------------------------------------------------- drawing
    readonly Render.Blood2D _2d = new();

    static float ItemTop(Page p, int i) => p.Top + i * p.Step;

    int ItemAt(Page p, Vector2 screen)
    {
        if (p.Draw != null)
            return -1;
        _2d.Begin(this);
        var q = _2d.FromScreen(screen);
        for (int i = 0; i < p.Items.Count; i++)
        {
            float y = ItemTop(p, i), h = p.Step;
            if (q.Y >= y - 2 && q.Y < y + h - 2 && q.X > 20 && q.X < 300 && p.Items[i].Enabled())
                return i;
        }
        return -1;
    }

    public override void _Process(double delta)
    {
        if (IsOpen)
            QueueRedraw();       // the focused item pulses
    }

    public override void _Draw()
    {
        if (!IsOpen)
            return;
        _2d.Begin(this);
        var size = GetViewportRect().Size;
        var top = _pages.Peek();
        if (top.Bare)
        {
            DrawRect(new Rect2(Vector2.Zero, size), Colors.Black);
            top.Draw?.Invoke();
            return;
        }
        if (Title)
        {
            // the title screen (gMenuPicnum), darkened while the menu is up as NBlood draws it
            DrawRect(new Rect2(Vector2.Zero, size), Colors.Black);
            _2d.Tile(2046, 160, 100, 40, 0, center: true);
            // the version in the corner, and what to do about a data folder an older extraction made
            _2d.Text(3, "V" + Shared.Engine.ChosenVersion.Full.ToUpperInvariant(), 318, 192, 32, 0, 2);
            if (BloodData.Outdated != "")
                _2d.Text(3, BloodData.Outdated, 160, 182, 0, 2, 1);
        }
        else
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.5f));
        var p = _pages.Peek();
        // the title bar and the page's title (CGameMenuItemTitle: font 1 at shade -128, centred on y 20)
        _2d.Tile(2038, 160, 20, -128, 0, center: true);
        _2d.Text(1, p.Title, 160, 20 - (int)_2d.FontHeight(1) / 2, -128, 0, 1);
        if (p.Draw != null)
            p.Draw();
        else
            DrawItems(p);
        if (p.Drip)
            DrawDrip();
    }

    /// <summary>itemBloodQAV: Blood's dripping blood (QAV BDRIP) looping on the 120 Hz clock, tiled across a wide
    /// screen from its left edge (bWideScreen, kQavOrientationLeft).</summary>
    void DrawDrip()
    {
        QavData.Load();
        var q = QavData.Get(256);
        if (q == null || q.Duration <= 0)
            return;
        int ticks = (int)(Time.GetTicksMsec() * 120 / 1000 % (ulong)q.Duration);
        for (int i = 0; i < _2d.WideCopies; i++)
            _2d.Qav(q, ticks, q.X + i * 320, q.Y, 10 | 256);
    }

    void DrawItems(Page p)
    {
        int pulse = 32 - ((int)(Time.GetTicksMsec() * 120 / 1000) & 63);
        for (int i = 0; i < p.Items.Count; i++)
        {
            var it = p.Items[i];
            int shade = !it.Enabled() ? 48 : i == p.Focus ? pulse : 32;
            float y = ItemTop(p, i);
            if (!p.Small)
            {
                _2d.Text(1, it.Text, 160, y, shade, 0, 1);
                continue;
            }
            _2d.Text(3, it.Text, 40, y, shade, 0, 0);
            _2d.Text(3, Value(it), 280, y, shade, 0, 2);
        }
    }

    static string Value(Item it)
    {
        if (it.Var == null)
            return "";
        if (it.Choices != null)
            return it.Choices[Mathf.Clamp(it.Var.Int, 0, it.Choices.Length - 1)];
        if (it.Step == 0)
            return it.Var.Bool ? "ON" : "OFF";
        if (it.Max <= 1)
            return $"{Mathf.RoundToInt(it.Var.Float * 100)}%";
        return it.Var.Float.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }
}
