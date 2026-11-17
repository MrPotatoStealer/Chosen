using Chosen.Data;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Drawing in Blood's 320x200 screen space (for the menus): tiles and Blood's tile fonts, mapped onto a centred 4:3
/// area of the screen with Blood's non-square pixels, like the HUD (Hud.Setup2D / RotateSprite).
/// </summary>
public sealed class Blood2D
{
    CanvasItem _ci;
    float _xs, _ys, _w;

    public void Begin(CanvasItem ci)
    {
        _ci = ci;
        var size = ci.GetViewportRect().Size;
        _w = size.X;
        _ys = size.Y / 200f;
        _xs = Mathf.Min(size.Y / 240f, size.X / 320f);
    }

    public Vector2 ToScreen(float x, float y) => new(_w / 2 + (x - 160) * _xs, y * _ys);

    public Vector2 FromScreen(Vector2 s) => new((s.X - _w / 2) / _xs + 160, s.Y / _ys);

    static float Bright(int shade) => shade <= 0 ? 1f : Mathf.Clamp(1 - shade / 64f, 0f, 1f);

    /// <summary>A tile at (x, y): its top left there, or (center) its middle there, as rotatesprite without
    /// RS_TOPLEFT.</summary>
    public void Tile(int pic, float x, float y, int shade, int pal, bool center = false)
    {
        if (!Tiles.Valid(pic))
            return;
        var tex = TileTextures.Get(pic, pal);
        if (tex == null)
            return;
        float w = Tiles.W(pic), h = Tiles.H(pic);
        if (center)
        {
            x -= Tiles.XOfs(pic) + w / 2;
            y -= Tiles.YOfs(pic) + h / 2;
        }
        var o = ToScreen(x, y);
        float br = Bright(shade);
        _ci.DrawTextureRect(tex, new Rect2(o, new Vector2(w * _xs, h * _ys)), false, new Color(br, br, br));
    }

    static int FontBase(int font) => font switch { 0 => 4096, 1 => 4192, 2 => 4288, 3 => 4384, _ => 4480 };
    static int FontSpace(int font) => font == 0 || font == 4 ? 0 : 1;

    public static int TextWidth(int font, string s)
    {
        int wd = -FontSpace(font);
        foreach (char c in s)
        {
            int t = ((c - 32) & 127) + FontBase(font);
            wd += Tiles.Valid(t) ? Tiles.W(t) + FontSpace(font) : 6;
        }
        return wd;
    }

    /// <summary>A font's height as NBlood's FontSet takes it: its tallest glyph.</summary>
    public float FontHeight(int font)
    {
        int h = 0;
        for (int i = 0; i < 96; i++)
            if (Tiles.Valid(FontBase(font) + i))
                h = System.Math.Max(h, Tiles.H(FontBase(font) + i));
        return h > 0 ? h : 8;
    }

    /// <summary>Screen columns of the 4:3 picture's width (NBlood's xdim43 tiling of a QAV on a wide screen).</summary>
    public int WideCopies => Mathf.Max(1, Mathf.CeilToInt(_w / (_xs * 320)));

    /// <summary>
    /// Build's rotatesprite in 320x200 coordinates (z 65536 = 1, a: Build angle): flags 1 translucent (32 the more
    /// see-through level), 4 y-flipped, 16 top-left origin, 256 aligned to the screen's left edge.
    /// </summary>
    public void RotateSprite(float sx, float sy, float z, int a, int pic, int shade, int pal, int stat)
    {
        if (!Tiles.Valid(pic))
            return;
        var tex = TileTextures.Get(pic, pal);
        if (tex == null)
            return;
        float w = Tiles.W(pic), h = Tiles.H(pic), xoff = 0, yoff = 0;
        if ((stat & 16) == 0)
        {
            // rotatesprite's centre: the tile's size >> 1, whole pixels (a half pixel off on an odd-sized tile shows the
            // credits' scrolling text past the edge of the background's window)
            xoff = Tiles.XOfs(pic) + ((int)w >> 1);
            yoff = Tiles.YOfs(pic) + ((int)h >> 1);
        }
        float zoom = z / 65536f, ang = a * Mathf.Pi / 1024, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
        var ax = new Vector2(ca * zoom * _xs, sa * zoom * _ys);
        var ay = new Vector2(-sa * zoom * _xs, ca * zoom * _ys);
        if ((stat & 4) != 0)
            ay = -ay;
        var o = (stat & 256) != 0 ? new Vector2(sx * _xs, sy * _ys) : ToScreen(sx, sy);
        float br = Bright(shade);
        if ((stat & 1) != 0)
            tex = TileTextures.GetFaded(pic, pal, (stat & 32) != 0 ? 2 : 1) ?? tex;     // Blood's blend table: 32 the more opaque
        _ci.DrawSetTransformMatrix(new Transform2D(ax, ay, o));
        _ci.DrawTextureRect(tex, new Rect2(-xoff, -yoff, w, h), false, new Color(br, br, br));
        _ci.DrawSetTransformMatrix(Transform2D.Identity);
    }

    /// <summary>A QAV animation's frame at ticks (QAV::Draw / DrawFrame): its layers at the QAV's position, stat added
    /// to each layer's; a layer facing the other way (256) is turned half round and flipped upright.</summary>
    public void Qav(QavData.Qav q, int ticks, float x, float y, int stat, int shade = 0, int pal = 0)
    {
        if (q == null || q.Ticks <= 0 || q.Frames.Count == 0)
            return;
        int n = Mathf.Clamp(ticks / q.Ticks, 0, q.Frames.Count - 1);
        foreach (var l in q.Frames[n])
        {
            if (l.Picnum <= 0)
                continue;
            int st = stat | l.Stat, angle = l.Angle;
            if ((l.Stat & 256) != 0)
            {
                angle = (angle + 1024) & 2047;
                st &= ~256;
                st ^= 4;
            }
            if ((stat & 256) != 0)
                st |= 256;      // kQavOrientationLeft: the whole QAV is aligned to the left edge
            RotateSprite(x + l.X, y + l.Y, l.Z, angle, l.Picnum, Mathf.Clamp(l.Shade + shade, -128, 127), pal > 0 ? pal : l.Pal, st);
        }
    }

    /// <summary>Text with its top at y; align 0 left, 1 centred, 2 right.</summary>
    public void Text(int font, string s, float x, float y, int shade, int pal, int align)
    {
        if (align != 0)
            x -= align == 1 ? TextWidth(font, s) / 2f : TextWidth(font, s);
        foreach (char c in s)
        {
            int t = ((c - 32) & 127) + FontBase(font);
            if (Tiles.Valid(t))
            {
                Tile(t, x, y, shade, pal);
                x += Tiles.W(t) + FontSpace(font);
            }
            else
                x += 6;
        }
    }
}
