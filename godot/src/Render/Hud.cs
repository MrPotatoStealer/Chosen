using System;
using Chosen.Core;
using Chosen.Data;
using Chosen.Sim;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Blood's 2D layer, as Blood3D's client drew it (hud.qc, qav.qc, view.qc): the first-person weapon (its QAV
/// animation), the crosshair, the screen tints, the status bar in its three sizes and the message lines. Everything is
/// placed in Blood's 320x200 space with Build's rotatesprite, mapped onto a centred 4:3 area of the screen like
/// NBlood; edge-aligned parts (RS_ALIGN_L / RS_ALIGN_R) stay inside b3d_hudaspect.
/// </summary>
public partial class Hud : Control
{
    static readonly Cvar HudSize = Cvars.Register("b3d_hudsize", "2", "0 no HUD, 1 minimal, 2 small corner panels, 3 Blood's status bar", saved: true);
    // an explicit static constructor: the cvars above register when the HUD is created, not at its first draw
    static Hud() { }
    static readonly Cvar HudAspect = Cvars.Register("b3d_hudaspect", "1.777778", "on wider screens, edge-aligned HUD parts stay inside this aspect ratio", saved: true);
    static readonly Cvar Crosshair = Cvars.Register("b3d_crosshair", "1", "draw the crosshair", saved: true);
    static readonly Cvar LevelStats = Cvars.Register("b3d_levelstats", "0", "kills and secrets on the HUD", saved: true);

    // rotatesprite flags (Build's dastat, NBlood's RS_*)
    const int RsTrans1 = 1, RsYFlip = 4, RsTopLeft = 16, RsTrans2 = 32, RsAlignL = 256, RsAlignR = 512;

    World _world;
    Player _player;
    double _clock;                 // Blood's 120 Hz clock, interpolated (the client's gClock)
    float _lerp;
    Vector3 _eye;                  // the camera (Quake units): the weapon takes the shade there
    float _xs, _ys, _hudL, _hudR, _w, _h;
    CanvasItem _ci;                // the layer being drawn
    readonly string[] _msgText = new string[4];
    readonly double[] _msgTime = new double[4];
    Layer _weaponLayer, _barLayer;
    ColorRect _tintMul, _tintAdd, _palette, _classic, _warp, _grade;
    BackBufferCopy _paletteCopy, _warpCopy;
    int _paletteId;

    /// <summary>A canvas layer of the HUD: draws with the painter it is given.</summary>
    sealed partial class Layer : Control
    {
        public Action<CanvasItem> Painter;
        public override void _Draw() => Painter?.Invoke(this);
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        Control Full(Control c)
        {
            c.SetAnchorsPreset(LayoutPreset.FullRect);
            c.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(c);
            return c;
        }
        // drawn in this order: weapon and crosshair, all so far taken to the classic palette (ClassicPalette.ScreenPass), the
        // tints over them (a change of the whole palette in Blood), then the status bar and messages, and the screen palette
        // over everything (ViewPalette)
        _grade = (ColorRect)Full(new ColorRect { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mul }, Visible = false });
        _warp = (ColorRect)Full(new ColorRect { Material = new ShaderMaterial { Shader = WaterFx.WarpShader }, Visible = false });
        _weaponLayer = (Layer)Full(new Layer { Painter = DrawWeaponLayer });
        _warpCopy = new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport, Visible = false };
        AddChild(_warpCopy);
        _classic = (ColorRect)Full(new ColorRect { Material = new ShaderMaterial { Shader = ClassicPalette.ScreenShader }, Visible = false });
        _tintMul = (ColorRect)Full(new ColorRect { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mul }, Visible = false });
        _tintAdd = (ColorRect)Full(new ColorRect { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }, Visible = false });
        _barLayer = (Layer)Full(new Layer { Painter = DrawBarLayer });
        HdrOutput.Ui(_barLayer);      // (the weapon and the tints belong to the world's picture, at its white)
        // (a canvas's first pass reading the screen copies it once; the second needs its own copy, or it shows that first one)
        _paletteCopy = new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport, Visible = false };
        AddChild(_paletteCopy);
        _palette = (ColorRect)Full(new ColorRect { Material = new ShaderMaterial { Shader = ViewPalette.Shader }, Visible = false });
    }

    /// <summary>No messages on screen (pipeline/bugs.py regress shots: pickups at the spot would differ in timing).</summary>
    public static bool HideMessages;

    public void Setup(World world)
    {
        System.Array.Clear(_msgText);      // a new level starts without the last one's messages
        if (_world != world)
        {
            if (_world != null)
                _world.MessageShown -= AddMessage;
            _world = world;
            world.MessageShown += AddMessage;
        }
    }

    /// <summary>Each drawn frame: whose view it is (null: no player HUD), the interpolation and the eye.</summary>
    public void Update(Player p, float lerp, Vector3 eye)
    {
        _player = p;
        _lerp = lerp;
        _eye = eye;
        _clock = (_world?.FrameClock ?? 0) + lerp * GameClock.TicsPerFrame;
        UpdateTint();
        _weaponLayer.QueueRedraw();
        _barLayer.QueueRedraw();
    }

    void AddMessage(string text)
    {
        for (int i = _msgText.Length - 1; i > 0; i--)
        {
            _msgText[i] = _msgText[i - 1];
            _msgTime[i] = _msgTime[i - 1];
        }
        _msgText[0] = text;
        _msgTime[0] = Now;
    }

    static double Now => Time.GetTicksMsec() / 1000.0;

    // ---------------------------------------------------------------- Blood's 320x200 space (hudSetup, hudToScreen)
    void Setup2D()
    {
        var size = GetViewportRect().Size;
        _w = size.X;
        _h = size.Y;
        _ys = _h / 200f;
        _xs = _h / 240f;                // Blood's non-square pixels on a 4:3 screen
        float wd = _w, aspect = HudAspect.Float;
        if (aspect > 0 && _h * aspect < _w)
            wd = _h * aspect;
        _hudL = (_w - wd) / 2;
        _hudR = _hudL + wd;
        if (_xs * 320 > wd)
            _xs = wd / 320;
    }

    Vector2 ToScreen(double x, double y, int stat)
    {
        float sx = (stat & RsAlignL) != 0 ? _hudL + (float)x * _xs
            : (stat & RsAlignR) != 0 ? _hudR - (320 - (float)x) * _xs
            : _w / 2 + ((float)x - 160) * _xs;
        return new Vector2(sx, (float)y * _ys);
    }

    static float ShadeBright(int shade) => shade <= 0 ? 1f : Mathf.Clamp(1 - shade / 64f, 0f, 1f);

    /// <summary>Build's tile animation frame offset (animateoffs) at the client clock.</summary>
    int AnimateOffs(int t)
    {
        int num = Tiles.AnimNum(t);
        if (num <= 0)
            return 0;
        int i = (int)Math.Floor(_clock / (1 << Tiles.AnimSpeed(t)));
        switch (Tiles.AnimType(t))
        {
            case 1:
                int k = i % (num * 2);
                return k < num ? k : num * 2 - k;
            case 2:
                return i % (num + 1);
            case 3:
                return -(i % (num + 1));
        }
        return 0;
    }

    /// <summary>rotatesprite(sx, sy, z, a, picnum, shade, pal, stat) in 320x200 coordinates (not 16.16).</summary>
    void RotateSprite(double sx, double sy, double z, double a, int pic, int shade, int pal, int stat)
    {
        if (!Tiles.Valid(pic))
            return;
        pic += AnimateOffs(pic);
        var tex = TileTextures.Get(pic, pal);
        if (tex == null)
            return;
        int w = Tiles.W(pic), h = Tiles.H(pic);
        float xoff = 0, yoff = 0;
        if ((stat & RsTopLeft) == 0)
        {
            xoff = Tiles.XOfs(pic) + w / 2;
            yoff = Tiles.YOfs(pic) + h / 2;
        }
        float zoom = (float)(z / 65536);
        // translucent: the picture with Blood's level baked into its alpha (TileTextures.GetFaded)
        if ((stat & RsTrans1) != 0)
            tex = TileTextures.GetFaded(pic, pal, (stat & RsTrans2) != 0 ? 2 : 1) ?? tex;
        float br = ShadeBright(shade) * (_weaponFire ? HdrOutput.HighlightCanvas : 1f);
        float ang = (float)(a * Math.PI / 1024), ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
        var ax = new Vector2(ca * zoom * _xs, sa * zoom * _ys);
        var ay = new Vector2(-sa * zoom * _xs, ca * zoom * _ys);
        if ((stat & RsYFlip) != 0)
            ay = -ay;                   // mirrored about the origin row
        _ci.DrawSetTransformMatrix(new Transform2D(ax, ay, ToScreen(sx, sy, stat)));
        _ci.DrawTextureRect(tex, new Rect2(-xoff, -yoff, w, h), false, new Color(br, br, br));
        _ci.DrawSetTransformMatrix(Transform2D.Identity);
    }

    void DrawStatSprite(int pic, double x, double y, int shade, int pal, int stat, double zoom = 0) =>
        RotateSprite(x, y, zoom == 0 ? 65536 : zoom, 0, pic, shade, pal, stat | 74);

    void DrawStatMaskedSprite(int pic, double x, double y, int shade, int pal, int stat, double zoom = 0) =>
        RotateSprite(x, y, zoom == 0 ? 65536 : zoom, 0, pic, shade, pal, stat | 10);

    /// <summary>sprintf("%*d") drawn with digit tiles pic..pic+9; spaces advance without drawing.</summary>
    void DrawStatNumber(int width, double n, int pic, double x, double y, int shade, int pal, int stat, double zoom = 0)
    {
        if (zoom == 0)
            zoom = 65536;
        string s = ((long)Math.Floor(n)).ToString().PadLeft(width);
        double adv = (Tiles.W(pic) + 1) * zoom / 65536;
        foreach (char c in s)
        {
            if (c >= '0' && c <= '9')
                RotateSprite(x, y, zoom, 0, pic + c - '0', shade, pal, stat | 10);
            x += adv;
        }
    }

    /// <summary>TileHGauge: the tile with its top left at (x, y), cut to nMult/nDiv of its width.</summary>
    void TileHGauge(int pic, double x, double y, double nMult, double nDiv, int stat)
    {
        if (!Tiles.Valid(pic) || nDiv <= 0)
            return;
        var tex = TileTextures.Get(pic);
        if (tex == null)
            return;
        float w = Tiles.W(pic), h = Tiles.H(pic), frac = Mathf.Clamp((float)(nMult / nDiv), 0f, 1f);
        var o = ToScreen(x, y, stat);
        _ci.DrawTextureRectRegion(tex, new Rect2(o, new Vector2(w * frac * _xs, h * _ys)), new Rect2(0, 0, w * frac, h));
    }

    // ---------------------------------------------------------------- Blood's tile fonts
    static int FontBase(int font) => font switch { 0 => 4096, 1 => 4192, 2 => 4288, 3 => 4384, _ => 4480 };
    static int FontSpace(int font) => font == 0 || font == 4 ? 0 : 1;

    static int TextWidth(int font, string s)
    {
        int wd = -FontSpace(font);
        foreach (char c in s)
        {
            int t = ((c - 32) & 127) + FontBase(font);
            if (Tiles.Valid(t))
                wd += Tiles.W(t) + FontSpace(font);
        }
        return wd;
    }

    /// <summary>viewDrawText: position 0 left, 1 centred, 2 right aligned.</summary>
    void DrawText(int font, string s, double x, double y, int shade, int pal, int position, bool shadow, int stat)
    {
        if (position != 0)
        {
            int wd = TextWidth(font, s);
            x -= position == 1 ? wd / 2 : wd;
        }
        foreach (char c in s)
        {
            int t = ((c - 32) & 127) + FontBase(font);
            if (!Tiles.Valid(t))
                continue;
            if (shadow)
                RotateSprite(x + 1, y + 1, 65536, 0, t, 127, pal, 26 | stat);
            RotateSprite(x, y, 65536, 0, t, shade, pal, 26 | stat);
            x += Tiles.W(t) + FontSpace(font);
        }
    }

    // ---------------------------------------------------------------- weapon (WeaponDraw / QAV::Draw)
    void DrawWeaponLayer(CanvasItem ci)
    {
        _ci = ci;
        Setup2D();
        var p = _player;
        if (p == null || HudSize.Int < 0)
            return;
        if (Crosshair.Bool && p.XHealth > 0)
            RotateSprite(160, 100, 65536, 0, 2319, 0, 0, 2);
        if (p.XHealth > 0)
            DrawPlayerWeapon(p);
        DrawGear(p);
    }

    /// <summary>What the player wears drawn over the view (viewDrawScreen): the diving suit's visor in the corners with
    /// its glass glints, the asbestos armor's hood at the bottom.</summary>
    void DrawGear(Player p)
    {
        if (Sim.World.PackItemActive(p, Player.PackDivingSuit))
        {
            RotateSprite(0, 0, 65536, 0, 2344, 0, 0, RsAlignL | RsTopLeft | 2);
            RotateSprite(320, 0, 65536, 1024, 2344, 0, 0, RsAlignR | RsTopLeft | RsYFlip | 2);
            RotateSprite(0, 200, 65536, 0, 2344, 0, 0, RsAlignL | RsTopLeft | RsYFlip | 2);
            RotateSprite(320, 200, 65536, 1024, 2344, 0, 0, RsAlignR | RsTopLeft | 2);
            RotateSprite(15, 3, 65536, 0, 2346, 32, 0, RsAlignL | RsTopLeft | 2 | RsTrans1);
            RotateSprite(212, 77, 65536, 0, 2347, 32, 0, RsAlignR | RsTopLeft | 2 | RsTrans1);
        }
        if (p.PwUpTime[K.kPwUpAsbestArmor] > 0)
        {
            RotateSprite(0, 200, 65536, 0, 2358, 0, 0, RsAlignL | RsTopLeft | RsYFlip | 2);
            RotateSprite(320, 200, 65536, 1024, 2358, 0, 0, RsAlignR | RsTopLeft | 2);
        }
    }

    void DrawPlayerWeapon(Player p)
    {
        double swayW = Mathf.Lerp((float)p.PrevSwayWidth, (float)p.SwayWidth, _lerp) + Client.PlayerView.ShakeBobX;
        double swayH = Mathf.Lerp((float)p.PrevSwayHeight, (float)p.SwayHeight, _lerp) + Client.PlayerView.ShakeBobY;
        double zDelta = Mathf.Lerp((float)(p.PrevZWeapon - p.PrevZView), (float)(p.ZWeapon - p.ZView), _lerp) - 12 * 256;
        // viewDrawScreen: the weapon in the view sector's floor shade (its light effect in it)
        int ns = _world != null ? _world.PeekSpriteBSector(p, false) : -1;
        int shade = ns >= 0 ? _world.Board.Sectors[ns].FloorShade + (int)_world.SectorShadeFxNow(ns).X : 0;
        shade = Math.Clamp(shade, -128, 127);
        DrawWeapon(p, shade, 160 + swayW / 256, 220 + swayH / 256 + zDelta / 128, 0);
    }

    void DrawWeapon(Player p, int shade, double x, double y, int pal)
    {
        var qav = p.WeaponQav >= 0 ? QavData.Get(p.WeaponQav) : null;
        if (qav == null || qav.Duration <= 0)
            return;
        int dur = qav.Duration;
        double ticks;
        if (p.WeaponTimer == 0)
            ticks = p.WeaponState == -1 || (p.CurWeapon == 3 && p.WeaponState == 7) ? dur - 1 : Math.Floor(_clock) % dur;
        else
            ticks = Math.Clamp(dur - p.WeaponTimer + _lerp * GameClock.TicsPerFrame, 0, dur - 1);
        int flags = 2;
        double cloak = p.PwUpTime[K.kPwUpShadowCloak];
        if (cloak >= 120 * 8 || (cloak != 0 && ((long)_clock & 32) != 0))
        {
            shade = -128;
            flags |= RsTrans1;
        }
        DrawQav(qav, ticks, x, y, flags, shade, pal);
    }

    bool _weaponFire;

    void DrawQav(QavData.Qav qav, double ticks, double x, double y, int stat, int shade, int pal)
    {
        if (qav.Ticks <= 0)
            return;
        int n = (int)Math.Clamp(Math.Floor(ticks / qav.Ticks), 0, qav.FrameCount - 1);
        if (n < 0 || n >= qav.Frames.Count)
            return;
        foreach (var l in qav.Frames[n])
        {
            if (l.Picnum <= 0)
                continue;
            int st = stat | l.Stat, angle = l.Angle;
            if ((st & 256) != 0)
            {
                // the layer faces the other way: turned half round and flipped back upright
                angle = (angle + 1024) & 2047;
                st &= ~256;
                st ^= RsYFlip;
            }
            // a weapon's fullbright layers are its fire (napalm's muzzle, the flare): HDR's highlights, as the fire it shoots
            _weaponFire = l.Shade <= -64;
            RotateSprite(x + l.X, y + l.Y, l.Z, angle, l.Picnum, Math.Clamp(l.Shade + shade, -128, 127), pal > 0 ? pal : l.Pal, st);
            _weaponFire = false;
        }
    }

    // ---------------------------------------------------------------- screen tints (scrSetDac)
    void UpdateTint()
    {
        var p = _player;
        // the view moved under water; the whole screen, as the tints below
        float warp = WaterFx.CameraUnder ? Mathf.Max(WaterFx.Warp.Float, 0f) : 0f;
        _warp.Visible = warp > 0f || Atmosphere.SpotsOn;
        if (_warp.Visible)
        {
            var wm = (ShaderMaterial)_warp.Material;
            wm.SetShaderParameter("warp", warp);
            wm.SetShaderParameter("spots", Atmosphere.Spots);
            _warp.Position = Vector2.Zero;
            _warp.Size = GetViewportRect().Size;
        }
        _grade.Visible = Atmosphere.Grade != Colors.White;
        if (_grade.Visible)
        {
            _grade.Color = Atmosphere.Grade;
            _grade.Position = Vector2.Zero;
            _grade.Size = GetViewportRect().Size;
        }
        _warpCopy.Visible = _warp.Visible && ClassicPalette.ScreenPass;
        if (p == null)
        {
            _tintMul.Visible = _tintAdd.Visible = _palette.Visible = _classic.Visible = _paletteCopy.Visible = false;
            return;
        }
        _classic.Visible = ClassicPalette.ScreenPass;
        // the screen palette (viewDrawScreen): the death mask, reflective shots, else under water by the medium
        int pal = World.PowerupCheck(p, K.kPwUpDeathMask) > 0 ? 4
            : World.PowerupCheck(p, K.kPwUpReflectShots) > 0 ? 1
            : !p.IsUnderwater ? 0
            : p.Medium == Player.MediumWater ? 1 : p.Medium == Player.MediumGoo ? 3 : 2;
        if (pal != _paletteId)
        {
            _paletteId = pal;
            ((ShaderMaterial)_palette.Material).SetShaderParameter("pal", ViewPalette.Matrix(pal));
        }
        _palette.Visible = pal != 0 && ViewPalette.Matrix(pal) != Projection.Identity;
        // (with any pass before it reading the screen: without a copy it shows that pass's, no weapon or status bar)
        _paletteCopy.Visible = _palette.Visible && (_classic.Visible || _warp.Visible);
        double pain = Math.Min(p.PainEffect, 85);
        double r = p.PickupEffect + pain * 2 - p.BlindEffect - Math.Floor(p.ChokeEffect / 64);
        double g = p.PickupEffect - pain * 3 - p.BlindEffect - Math.Floor(p.ChokeEffect / 32);
        double b = -p.PickupEffect - pain * 3 - p.BlindEffect - Math.Floor(p.ChokeEffect / 64);
        float fr = (float)Math.Clamp(r, -255, 255) / 255, fg = (float)Math.Clamp(g, -255, 255) / 255, fb = (float)Math.Clamp(b, -255, 255) / 255;
        var mul = new Color(1 + Math.Min(fr, 0), 1 + Math.Min(fg, 0), 1 + Math.Min(fb, 0));
        var add = new Color(Math.Max(fr, 0), Math.Max(fg, 0), Math.Max(fb, 0));
        // the whole screen, whatever the window became since the HUD was made (switching to fullscreen)
        var screen = GetViewportRect().Size;
        _tintMul.Position = _tintAdd.Position = _palette.Position = _classic.Position = Vector2.Zero;
        _tintMul.Size = _tintAdd.Size = _palette.Size = _classic.Size = screen;
        _tintMul.Color = mul;
        _tintMul.Visible = mul != Colors.White;
        _tintAdd.Color = add;
        _tintAdd.Visible = add.R > 0 || add.G > 0 || add.B > 0;
    }

    // ---------------------------------------------------------------- status bar (UpdateStatusBar) and messages
    void DrawBarLayer(CanvasItem ci)
    {
        _ci = ci;
        Setup2D();
        if (_world != null && _world.LevelEnded)
        {
            DrawEndGame();
            DrawMessages();
            return;
        }
        if (_player != null)
        {
            switch (HudSize.Int)
            {
                case 1: DrawHudMinimal(_player); break;
                case 2: DrawHudSmall(_player); break;
                case >= 3: DrawHudFull(_player); break;
            }
            DrawChoke(_player);
        }
        DrawMessages();
    }

    /// <summary>gChoke.Draw: a hand holding on to the player (QAV 518, looping) over the view and the status bar,
    /// riding with the weapon's height.</summary>
    void DrawChoke(Player p)
    {
        const int ChokeQav = 518;
        if (p.Hand != 1)
            return;
        var qav = QavData.Get(ChokeQav);
        if (qav == null || qav.Duration <= 0)
            return;
        double zDelta = Mathf.Lerp((float)(p.PrevZWeapon - p.PrevZView), (float)(p.ZWeapon - p.ZView), _lerp) - 12 * 256;
        DrawQav(qav, Math.Floor(_clock) % qav.Duration, 160, Math.Floor(zDelta / 128) + 220, 2, 0, 0);
    }

    static int PackIcon(int i) => i switch { 0 => 2569, 1 => 2564, 2 => 2566, 3 => 2568, _ => 2560 };

    int Keys(Player p)
    {
        int keys = 0;
        for (int i = 0; i < 7; i++)
            if (p.HasKey[i + 1] != 0)
                keys |= 1 << i;
        return keys;
    }

    static double AmmoShown(Player p, int i) => i == 6 ? Math.Floor(p.Ammo[i] / 10) : p.Ammo[i];

    bool Blink(int bit) => ((long)Math.Floor(_clock) & bit) != 0;

    void DrawLevelStats(double x, double y)
    {
        if (!LevelStats.Bool || _world == null)
            return;
        DrawText(3, $"K:{_world.Kills}/{_world.TotalKills}", x, y, 20, 0, 0, true, RsAlignL);
        DrawText(3, $"S:{_world.SecretsFound}/{_world.SecretsTotal}", x, y + 8, 20, 0, 0, true, RsAlignL);
    }

    void DrawPowerUps(Player p)
    {
        // ascending by the time left, like Blood
        var list = new (double t, int n, int zoom, int yofs)[]
        {
            (p.PwUpTime[K.kPwUpShadowCloak], 13, 26214, 0), (p.PwUpTime[K.kPwUpReflectShots], 24, 26214, 5),
            (p.PwUpTime[K.kPwUpDeathMask], 14, 19660, 9), (p.PwUpTime[K.kPwUpTwoGuns], 17, 16384, 4),
            (p.PwUpTime[K.kPwUpFeatherFall], 12, 19660, 7), (p.PwUpTime[K.kPwUpGasMask], 19, 26214, 4),
        };
        Array.Sort(list, (a, b) => a.t.CompareTo(b.t));
        double y = 50;
        foreach (var (t, n, zoom, yofs) in list)
        {
            if (t <= 0)
                continue;
            double sec = Math.Floor(t / 100);
            if (sec > 5 || Blink(32))
                DrawStatMaskedSprite(GenTables.tbl_powerup[n * GenTables.tbl_powerup_STRIDE], 15, y + yofs, 0, 0, RsAlignL, zoom);
            DrawStatNumber(1, sec, 2250, 30, y, 0, sec > 5 ? 0 : 2, RsAlignL, 32768);
            y += 20;
        }
    }

    static readonly int[] PackIcon2Tile = { 519, 830, 760, 839, 827 };
    static readonly double[] PackIcon2Scale = { 0.5, 0.3, 0.6, 0.5, 0.4 };
    static readonly int[] PackIcon2YOfs = { 0, 0, 0, -4, 0 };
    static readonly int[] AmmoIconTile = { -1, 816, 619, 817, 801, 589, 618, 548, 820, 525, 811, 810 };
    static readonly double[] AmmoIconScale = { 0, 0.5, 0.8, 0.7, 0.5, 0.7, 0.5, 0.3, 0.3, 0.6, 0.5, 0.45 };
    static readonly int[] AmmoIconYOfs = { 0, 0, 0, 3, -6, 2, 4, -6, -6, -6, 2, 2 };

    /// <summary>b3d_hudsize 1: NBlood's minimal HUD, small icons and numbers along the bottom.</summary>
    void DrawHudMinimal(Player p)
    {
        DrawStatMaskedSprite(2169, 12, 195, 0, 0, RsAlignL, 65536 * 0.56);
        DrawStatNumber(1, Math.Floor(p.XHealth / 16), 2190, 28, 187, 0, 0, RsAlignL);
        if (p.Armor[1] != 0)
        {
            DrawStatMaskedSprite(2578, 70, 186, 0, 0, RsAlignL, 65536 * 0.5);
            DrawStatNumber(3, Math.Floor(p.Armor[1] / 16), 2240, 83, 187, 0, 0, RsAlignL, 65536 * 0.65);
        }
        if (p.Armor[0] != 0)
        {
            DrawStatMaskedSprite(2586, 112, 195, 0, 0, RsAlignL, 65536 * 0.5);
            DrawStatNumber(3, Math.Floor(p.Armor[0] / 16), 2240, 125, 187, 0, 0, RsAlignL, 65536 * 0.65);
        }
        if (p.Armor[2] != 0)
        {
            DrawStatMaskedSprite(2602, 155, 196, 0, 0, RsAlignL, 65536 * 0.5);
            DrawStatNumber(3, Math.Floor(p.Armor[2] / 16), 2240, 170, 187, 0, 0, RsAlignL, 65536 * 0.65);
        }
        int pi = p.PackItemId;
        if (pi >= 0 && pi < 5)
        {
            DrawStatMaskedSprite(PackIcon2Tile[pi], 225, 194 + PackIcon2YOfs[pi], 0, 0, RsAlignR, 65536 * PackIcon2Scale[pi]);
            DrawStatNumber(3, p.PackAmount[pi], 2240, 240, 187, 4, 0, RsAlignR, 65536 * 0.7);
        }
        int wa = p.WeaponAmmo;
        if (p.CurWeapon != 0 && wa >= 0 && wa < 12)
        {
            if (AmmoIconTile[wa] >= 0)
                DrawStatMaskedSprite(AmmoIconTile[wa], 304, 192 + AmmoIconYOfs[wa], 0, 0, RsAlignR, 65536 * AmmoIconScale[wa]);
            DrawStatNumber(3, AmmoShown(p, wa), 2240, 267, 187, 0, 0, RsAlignR);
        }
        int keys = Keys(p);
        if (_world == null || _world.GameType <= 1)
            for (int i = 0; i < 6; i++)
                if ((keys & (1 << i)) != 0)
                    DrawStatMaskedSprite(2552 + i, 260 + 10 * i, 170, 0, 0, RsAlignR, 65536 * 0.25);
        if (p.ThrowPower != 0 && p.XHealth > 0)
            TileHGauge(2260, 124, 165, p.ThrowPower, 65536, 0);
        DrawLevelStats(2, 140);
        DrawPowerUps(p);
    }

    /// <summary>b3d_hudsize 2: Blood's fullscreen HUD, health and ammo bottom left, armor and item bottom right.</summary>
    void DrawHudSmall(Player p)
    {
        if (p.ThrowPower != 0 && p.XHealth > 0)
            TileHGauge(2260, 124, 175, p.ThrowPower, 65536, 0);
        DrawStatSprite(2201, 34, 187, 16, 0, RsAlignL);
        if (p.XHealth >= 16 || Blink(16) || p.XHealth == 0)
            DrawStatNumber(3, Math.Floor(p.XHealth / 16), 2190, 8, 183, 0, 0, RsAlignL);
        if (p.CurWeapon != 0 && p.WeaponAmmo != -1)
            DrawStatNumber(3, AmmoShown(p, p.WeaponAmmo), 2240, 42, 183, 0, 0, RsAlignL);
        DrawStatSprite(2173, 284, 187, 16, 0, RsAlignR);
        DrawArmorGauge(p, 1, 2207, 250, 175, 255, 178, RsAlignR);
        DrawArmorGauge(p, 0, 2209, 250, 183, 255, 186, RsAlignR);
        DrawArmorGauge(p, 2, 2208, 250, 191, 255, 194, RsAlignR);
        if (p.PackItemId >= 0)
        {
            DrawStatSprite(PackIcon(p.PackItemId), 286, 186, 0, 0, RsAlignR);
            DrawStatNumber(3, p.PackAmount[p.PackItemId], 2250, 302, 183, 4, 0, RsAlignR);
        }
        int keys = Keys(p);
        if (_world == null || _world.GameType <= 1)
        {
            for (int i = 0; i < 6; i++)
            {
                if ((keys & (1 << i)) == 0)
                    continue;
                if ((i & 1) != 0)
                    DrawStatSprite(2220 + i, 320 - (78 + i / 2 * 10), 194, 0, 0, RsAlignR);
                else
                    DrawStatSprite(2220 + i, 73 + i / 2 * 10, 194, 0, 0, RsAlignL);
            }
        }
        DrawLevelStats(2, 140);
        DrawPowerUps(p);
    }

    void DrawArmorGauge(Player p, int i, int gauge, double gx, double gy, double nx, double ny, int stat)
    {
        if (p.Armor[i] == 0)
            return;
        TileHGauge(gauge, gx, gy, p.Armor[i], 3200, stat);
        DrawStatNumber(3, Math.Floor(p.Armor[i] / 16), 2230, nx, ny, 0, 0, stat);
    }

    /// <summary>b3d_hudsize 3: Blood's status bar.</summary>
    void DrawHudFull(Player p)
    {
        DrawStatMaskedSprite(2200, 160, 172, 16, 0, 0);
        if (p.PackItemId >= 0)
        {
            DrawStatSprite(PackIcon(p.PackItemId), 265, 186, 0, 0, 0);
            DrawStatNumber(3, p.PackAmount[p.PackItemId], 2250, 260, 172, 4, 0, 0);
        }
        if (p.XHealth >= 16 || Blink(16) || p.XHealth == 0)
            DrawStatNumber(3, Math.Floor(p.XHealth / 16), 2190, 86, 183, 0, 0, 0);
        if (p.CurWeapon != 0 && p.WeaponAmmo != -1)
            DrawStatNumber(3, AmmoShown(p, p.WeaponAmmo), 2240, 216, 183, 0, 0, 0);
        for (int i = 9; i >= 1; i--)
        {
            int x = 135 + (i - 1) / 3 * 23, y = 182 + (i - 1) % 3 * 6;
            DrawStatNumber(3, AmmoShown(p, i), 2230, x, y, i == p.WeaponAmmo ? -128 : 32, 10, 0);
        }
        DrawStatNumber(2, p.Ammo[10], 2230, 291, 194, p.WeaponAmmo == 10 ? -128 : 32, 10, 0);
        DrawStatNumber(2, p.Ammo[11], 2230, 309, 194, p.WeaponAmmo == 11 ? -128 : 32, 10, 0);
        DrawArmorGauge(p, 1, 2207, 44, 174, 50, 177, 0);
        DrawArmorGauge(p, 0, 2209, 44, 182, 50, 185, 0);
        DrawArmorGauge(p, 2, 2208, 44, 190, 50, 193, 0);
        int keys = Keys(p);
        for (int i = 0; i < 6; i++)
        {
            int x = 73 + (i & 1) * 173, y = 171 + i / 2 * 11;
            if ((keys & (1 << i)) != 0)
                DrawStatSprite(2220 + i, x, y, 0, 0, 0);
            else
                DrawStatSprite(2220 + i, x, y, 40, 5, 0);
        }
        if (p.ThrowPower != 0 && p.XHealth > 0)
            TileHGauge(2260, 124, 175, p.ThrowPower, 65536, 0);
        DrawLevelStats(2, 140);
        DrawPowerUps(p);
    }

    /// <summary>The end-of-level stats (CEndGameMgr::Draw).</summary>
    void DrawEndGame()
    {
        _ci.DrawRect(new Rect2(0, 0, _w, _h), new Color(0, 0, 0, 0.7f));
        DrawText(1, "LEVEL STATS", 160, 20, -128, 0, 1, false, 0);
        if (_world.GameType == K.kGameTypeSinglePlayer)
        {
            DrawText(1, "KILLS:", 75, 50, -128, 0, 0, true, 0);
            DrawText(1, _world.Kills.ToString(), 160, 50, -128, 0, 0, true, 0);
            DrawText(1, "OF", 190, 50, -128, 0, 0, true, 0);
            DrawText(1, _world.TotalKills.ToString(), 220, 50, -128, 0, 0, true, 0);
        }
        DrawText(1, "SECRETS:", 75, 70, -128, 0, 0, true, 0);
        DrawText(1, _world.SecretsFound.ToString(), 160, 70, -128, 0, 0, true, 0);
        DrawText(1, "OF", 190, 70, -128, 0, 0, true, 0);
        DrawText(1, Math.Max(_world.SecretsFound, _world.SecretsTotal).ToString(), 220, 70, -128, 0, 0, true, 0);
        if (_world.SuperSecretsFound > 0)
            DrawText(1, "YOU FOUND A SUPER SECRET!", 160, 100, -128, 2, 1, true, 0);
        DrawText(3, "PRESS A KEY TO CONTINUE", 160, 134, -128, 0, 1, true, 0);
    }

    void DrawMessages()
    {
        if (HideMessages)
            return;
        double y = 1, now = Now;
        for (int i = 0; i < _msgText.Length; i++)
        {
            if (string.IsNullOrEmpty(_msgText[i]) || now - _msgTime[i] > 5)
                continue;
            DrawText(0, _msgText[i], 1, y, 0, 0, 0, true, RsAlignL);
            y += 10;
        }
    }
}
