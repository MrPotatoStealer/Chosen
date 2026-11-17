using System;

namespace Chosen.Sim.Collision;

/// <summary>Contents bits, DarkPlaces' values (the game's trace masks keep their meaning).</summary>
public static class Contents
{
    // sprite bodies: Body blocks moves and hitscans (cstat 1 and 256), Blocker only moves (cstat 1: E1M4's window
    // grates), Corpse only hitscans (cstat 256)
    // masked walls: WallMove blocks moves (cstat 1: CLIPMASK0's walls), WallShot hitscans (cstat 64: CLIPMASK1's);
    // cansee passes both (E1M4's broken wall, cstat 64: the axe zombies behind it see the player through the hole)
    public const int Solid = 1, Water = 2, Slime = 4, Lava = 8, Sky = 16, Body = 32, Corpse = 64, Blocker = 128,
        PlayerClip = 256, MonsterClip = 512, BotClip = 2048, WallMove = 4096, WallShot = 8192;
    public const int Sprites = Body | Corpse | Blocker;
    public const int MaskMove = Solid | Body | Blocker | PlayerClip | MonsterClip | WallMove;   // kMaskMove (801)
    public const int MaskShot = Solid | Body | Corpse | WallShot;           // kMaskShot (97): CLIPMASK1's sprites
    public const int MaskBlock = Solid | Body | Blocker | WallMove;         // CLIPMASK0's sprites (HitScan)
    public const int MaskZRange = Solid | Body | Blocker;                   // kMaskZRange (33): floors, not clip
    public const int Liquid = Water | Slime | Lava;

    /// <summary>
    /// A surface name of one layer of rooms sharing space ends in _g&lt;layer&gt; (StackedRooms.ForSurface). The name
    /// without it, and the layer (-1: none).
    /// </summary>
    public static string StripLayer(string tex, out int layer)
    {
        layer = -1;
        int i = tex.Length - 1;
        while (i >= 0 && char.IsAsciiDigit(tex[i]))
            i--;
        if (i < 1 || i == tex.Length - 1 || tex[i] != 'g' || tex[i - 1] != '_')
            return tex;
        layer = int.Parse(tex.AsSpan(i + 1));
        return tex[..(i - 1)];
    }
}

/// <summary>Result of a box trace (DarkPlaces' trace_* globals).</summary>
public struct TraceResult
{
    public float Fraction;
    public Godot.Vector3 EndPos;
    public Godot.Vector3 Normal;
    public bool StartSolid, AllSolid;
    public int Contents;          // contents of what was hit
    public object Entity;         // null: nothing; a body's owner
    public string Texture;        // texture of the face hit ("" for bodies)
}
