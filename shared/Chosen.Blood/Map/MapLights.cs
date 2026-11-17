// Chosen Blood module (GPL v2): the dynamic lights a .MAPX places (its LITE chunk), and the rules that give a Blood
// level's sprites their lights (the game's for a .MAP; the editor's Generate Lights: the same table).
using System;
using System.Collections.Generic;
using System.IO;

namespace Chosen.Shared.Blood.Map;

/// <summary>What a map light is: a point of its own, or the light a sprite gives (it follows the sprite and goes out
/// with it, as the game's rules have it: a torch or candle while lit, a light fixture while it shows its tile, a
/// decoration Blood draws fullbright).</summary>
public enum LightKind { Point = 0, Torch = 1, Fixture = 2, Glow = 3 }

/// <summary>
/// A dynamic light a .MAPX places. A point light (Sprite -1) shines from (X, Y, Z), Build units (z down). One attached
/// to a sprite shines from the spot its kind has on the sprite (LightRules.Spot: a torch's flame, a lamp's glass, a
/// glowing decoration's middle) moved by (X, Y, Z), so it follows the sprite and goes out with it. Radius in Build
/// units (16 a game unit); Energy and Flicker as the game's lights have them (1: a lamp's, 0: steady).
/// </summary>
public sealed class MapLight
{
    public LightKind Kind;
    public int Sprite = -1;
    public int X, Y, Z;
    public byte R = 255, G = 200, B = 140;
    public int Radius = 3200;
    public float Energy = 1, Flicker;
    /// <summary>A fixture's: where on its picture (-1 bottom .. 1 top of a face sprite), and the tile it shines while
    /// showing (shot: its broken frames; a switch's off frame: dark).</summary>
    public float Up;
    public int Tile = -1;
    /// <summary>Casts shadows (the game's nearest few do).</summary>
    public bool Shadows = true;
    /// <summary>As lit as its sector's light effect (dark with the room's lights switched off, flickering with a
    /// flickering room): a fixture Blood draws in its sector's light has it.</summary>
    public bool SectorLight;
    /// <summary>A point light's room (its Build sector, Forge's choice where rooms share its place: the layer edited as
    /// it was placed); -1: the sector holding its place. An attached light's is its sprite's.</summary>
    public int Sector = -1;

    public bool Attached => Sprite >= 0;

    public MapLight Clone() => (MapLight)MemberwiseClone();

    public string Describe() => (Attached ? $"{Kind.ToString().ToLowerInvariant()} light on sprite {Sprite}" : "point light")
        + $", reach {Radius}, energy {Energy:0.##}";
}

/// <summary>The game's rules for the lights a level's sprites give (a .MAP's, made as it loads: Render.DynamicLights;
/// the editor's Generate Lights makes the same ones a .MAPX keeps): a torch or candle (types 30, 32), a light fixture by
/// the tile drawn, a decoration Blood draws fullbright (shade -64 or brighter).</summary>
public static class LightRules
{
    public const int TorchType = 30, CandleType = 32;

    /// <summary>The fixtures by tile: colour, reach (game units), energy, where on the picture, flicker (the old
    /// converter's FIXTURE_LIGHTS).</summary>
    public static readonly IReadOnlyDictionary<int, (byte r, byte g, byte b, float radius, float energy, float up, float flicker)> Fixtures =
        new Dictionary<int, (byte, byte, byte, float, float, float, float)>
        {
            [229] = (255, 150, 70, 180, 1.0f, 0, 0),         // round wall lamp, orange glass
            [580] = (255, 170, 80, 170, 1.0f, 0.8f, 0.2f),   // candelabra
            [584] = (255, 170, 80, 150, 0.9f, 0.6f, 0.2f),   // wall candelabra
            [640] = (255, 200, 120, 240, 1.2f, 0.75f, 0.06f), // lamp post lantern
            [676] = (230, 240, 255, 220, 1.0f, 0, 0),        // round white lamp
            [795] = (240, 240, 255, 200, 1.0f, 0, 0),        // small round ceiling lamp
            [796] = (210, 230, 255, 220, 1.1f, 0, 0),        // fluorescent tube
            [944] = (255, 180, 100, 160, 0.9f, 0, 0),        // small hanging lamp
            [2101] = (255, 140, 50, 200, 1.3f, 0, 0.45f),    // fire
            [2288] = (210, 230, 255, 220, 1.0f, 0, 0),       // fluorescent ceiling panel
            [2290] = (210, 230, 255, 220, 1.0f, 0, 0),       // fluorescent ceiling panel (also a light switch)
        };

    // the torch's and candle's light (colour 255 160 70), a glowing decoration's (255 190 110): reach in game units
    public const float TorchRadius = 220, TorchEnergy = 1.4f, TorchFlicker = 0.4f;
    public const float CandleRadius = 110, CandleEnergy = 0.55f, CandleFlicker = 0.25f;
    public const float GlowRadius = 160, GlowEnergy = 1, GlowFlicker = 0.05f;

    /// <summary>Is a sprite of this type a decoration in the game (statnum 0 once initialised: not a monster, thing,
    /// item, trap, marker or ambient sound; World.InitByType)?</summary>
    public static bool IsDecoration(int t) =>
        !(t >= 200 && t < 255) && !(t >= 400 && t < 436) && t != 413
        && !(t >= 40 && t < 51) && !(t >= 60 && t < 81) && !(t >= 100 && t < 151)
        && t is not (452 or 454 or 456 or 459) && !(t >= 1 && t <= 19) && t != 710;

    /// <summary>The light a sprite of the map gives by the game's rules, attached to it (no offset), or null.</summary>
    public static MapLight ForSprite(BSprite s)
    {
        if (s.StatNum >= 1024 || s.Type is 1 or 2)
            return null;
        if (s.Type is TorchType or CandleType)
        {
            bool torch = s.Type == TorchType;
            return new MapLight
            {
                Kind = LightKind.Torch, Sprite = s.Index, R = 255, G = 160, B = 70,
                Radius = (int)((torch ? TorchRadius : CandleRadius) * 16), Energy = torch ? TorchEnergy : CandleEnergy,
                Flicker = torch ? TorchFlicker : CandleFlicker,
            };
        }
        if (Fixtures.TryGetValue(s.Picnum, out var f))
            return new MapLight
            {
                Kind = LightKind.Fixture, Sprite = s.Index, Tile = s.Picnum, R = f.r, G = f.g, B = f.b, Radius = (int)(f.radius * 16),
                Energy = f.energy, Up = f.up, Flicker = f.flicker, SectorLight = true,
            };
        // (a generator or sound of 700..711 the game makes invisible for good: it never shines; one flagged invisible in
        // the map keeps its light, dark until it shows, as the game's)
        if (IsDecoration(s.Type) && s.Shade <= -64 && s.Picnum > 0 && s.Type is < 700 or > 711)
            return new MapLight
            {
                Kind = LightKind.Glow, Sprite = s.Index, R = 255, G = 190, B = 110, Radius = (int)(GlowRadius * 16),
                Energy = GlowEnergy, Flicker = GlowFlicker,
            };
        return null;
    }

    /// <summary>Does an attached light shine as the level starts (the game's checks each frame, on the map as stored): its
    /// sprite showing (not flagged invisible), a flame lit (no x-sprite, or its state on), a fixture showing its tile. A
    /// point light of its own always does.</summary>
    public static bool ShinesAtStart(MapLight l, BSprite s)
    {
        if (!l.Attached)
            return true;
        if (s == null || (s.Cstat & 32768) != 0 || s.Type is >= 700 and <= 711)
            return false;
        if (l.Kind == LightKind.Torch)
            return s.Xsp == null || s.Xsp.State != 0;
        if (l.Kind == LightKind.Fixture)
            return l.Tile < 0 || s.Picnum == l.Tile;
        return true;
    }

    /// <summary>Every light the map's sprites give by the game's rules (in sprite order).</summary>
    public static List<MapLight> Generate(BloodMapFile m)
    {
        var r = new List<MapLight>();
        for (int i = 0; i < m.Sprites.Count; i++)
        {
            m.Sprites[i].Index = i;
            if (ForSprite(m.Sprites[i]) is { } l)
                r.Add(l);
        }
        return r;
    }

    /// <summary>A tile's size and offset (pixels) as the game's spot rules need them; zeros for none.</summary>
    public delegate (int w, int h, int yofs) TileSize(int tile);

    /// <summary>Where an attached light's kind puts it on its sprite, Build units (z down), as the game places it
    /// (Render.DynamicLights FlameSpot, FixtureSpot, the glow's middle): a torch's flame (the top of its lit frame), a
    /// fixture's glass (its picture's middle moved by Up half heights; a few units off a wall or floor sprite on the side
    /// it faces), a glowing decoration's middle. Its offset (X, Y, Z) not added.</summary>
    public static (double x, double y, double z) Spot(MapLight l, BSprite s, TileSize tile)
    {
        double x = s.X, y = s.Y, z = s.Z;
        switch (l.Kind)
        {
            case LightKind.Torch:
            {
                bool torch = s.Type == TorchType;
                int pic = torch && tile(s.Picnum + 1).h > 0 ? s.Picnum + 1 : s.Picnum;
                var t = tile(pic);
                if (pic <= 0 || t.h <= 0)
                    return (x, y, z - (torch ? 24 : 8) * 256.0);
                return (x, y, z - (t.h / 2.0 + t.yofs + s.YOffset) * s.YRepeat * 4 - (torch ? 4 * 256 : 0));
            }
            case LightKind.Fixture:
            {
                const double Off = 8 * 16, OffZ = 8 * 256;
                int align = s.Cstat & 48;
                bool oneSided = (s.Cstat & 64) != 0;
                if (align == 32)
                {
                    bool above = !oneSided || (s.Cstat & 8) == 0;
                    return (x, y, z + (above ? -OffZ : OffZ));
                }
                var t = tile(l.Tile >= 0 ? l.Tile : s.Picnum);
                z -= (t.yofs + s.YOffset) * s.YRepeat * 4.0 + l.Up * t.h * s.YRepeat * 2.0;
                if (align == 16)
                {
                    double a = s.Ang * Math.PI / 1024;
                    x += Math.Cos(a) * Off;
                    y += Math.Sin(a) * Off;
                }
                return (x, y, z);
            }
            case LightKind.Glow:
                return (x, y, z - tile(s.Picnum).h * s.YRepeat * 2.0);
            default:
                return (x, y, z);
        }
    }

    /// <summary>Where a light shines from, Build units: a point light's place, an attached one's spot plus its offset
    /// (null: its sprite isn't in the map).</summary>
    public static (double x, double y, double z)? Place(MapLight l, IReadOnlyList<BSprite> sprites, TileSize tile)
    {
        if (!l.Attached)
            return (l.X, l.Y, l.Z);
        if (l.Sprite >= sprites.Count)
            return null;
        var (x, y, z) = Spot(l, sprites[l.Sprite], tile);
        return (x + l.X, y + l.Y, z + l.Z);
    }

    // ---------------------------------------------------------------- rooms sharing space

    /// <summary>
    /// Rooms sharing a place in plan (Build's impossible space, rooms stacked in height, Forge's layers drawn over each
    /// other) coloured so that no two sharing a place have the same colour: a light in one then neither lights nor is
    /// shadowed by the others there (its render layers: RoomLayer, RoomMask). Each colour spreads to the neighbours
    /// through the two-sided walls where it can (a room's layer one colour), from 1 to max; 0: a sector sharing no
    /// place (lit by every light), or one no colour was left for.
    /// </summary>
    public static int[] RoomColours(int n, Func<int, IReadOnlyList<int>> overlaps, Func<int, IReadOnlyList<int>> neighbours, int max)
    {
        var c = new int[n];
        var queue = new Queue<int>();
        for (int s0 = 0; s0 < n; s0++)
        {
            if (c[s0] != 0 || overlaps(s0).Count == 0)
                continue;
            queue.Enqueue(s0);
            var queued = new HashSet<int> { s0 };
            while (queue.Count > 0)
            {
                int s = queue.Dequeue();
                var taken = new HashSet<int>();
                foreach (int o in overlaps(s))
                    if (o >= 0 && o < n && c[o] != 0)
                        taken.Add(c[o]);
                int pick = 0;
                foreach (int nb in neighbours(s))
                    if (nb >= 0 && nb < n && c[nb] != 0 && !taken.Contains(c[nb]))
                    {
                        pick = c[nb];
                        break;
                    }
                for (int k = 1; pick == 0 && k <= max; k++)
                    if (!taken.Contains(k))
                        pick = k;
                c[s] = pick;
                foreach (int nb in neighbours(s))
                    if (nb >= 0 && nb < n && c[nb] == 0 && overlaps(nb).Count > 0 && queued.Add(nb))
                        queue.Enqueue(nb);
            }
        }
        return c;
    }

    /// <summary>The render layer of a sector of colour c (RoomColours): the first (every light's) for 0, else its own
    /// from firstBit on.</summary>
    public static uint RoomLayer(int c, int firstBit) => c <= 0 ? 1u : 1u << (firstBit + c - 1);

    /// <summary>The render layers a light in sector s reaches (its cull and shadow masks): all but those of the rooms
    /// sharing s's place in another colour.</summary>
    public static uint RoomMask(int s, int[] colours, Func<int, IReadOnlyList<int>> overlaps, int firstBit)
    {
        uint m = 0xFFFFFFFFu;
        if (colours == null || s < 0 || s >= colours.Length)
            return m;
        foreach (int o in overlaps(s))
            if (o >= 0 && o < colours.Length && colours[o] > 0 && colours[o] != colours[s])
                m &= ~RoomLayer(colours[o], firstBit);
        return m;
    }

    /// <summary>The room colours as near the camera's sector s as they are, nearest first (through the two-sided walls,
    /// s's own first): a sector sharing no place (colour 0) beside a room that does sees into that room's place
    /// (test.mapx: from yard 17 room 0's, where room 15's mist then stood).</summary>
    public static List<int> RoomsByNearness(int s, int[] colours, Func<int, IReadOnlyList<int>> neighbours)
    {
        var order = new List<int>();
        if (colours == null || s < 0 || s >= colours.Length)
            return order;
        var seen = new HashSet<int> { s };
        var queue = new Queue<int>();
        queue.Enqueue(s);
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            if (colours[c] > 0 && !order.Contains(colours[c]))
                order.Add(colours[c]);
            foreach (int nb in neighbours(c))
                if (nb >= 0 && nb < colours.Length && seen.Add(nb))
                    queue.Enqueue(nb);
        }
        return order;
    }

    /// <summary>Whether what belongs to the rooms of colours own (a fog volume, knowing no render layers) is seen from
    /// the camera (RoomsByNearness's order): not when a room sharing its place (others) is nearer than its own.</summary>
    public static bool RoomSeen(List<int> order, HashSet<int> own, HashSet<int> others)
    {
        if (others == null || others.Count == 0)
            return true;
        foreach (int c in order)
        {
            if (own != null && own.Contains(c))
                return true;
            if (others.Contains(c))
                return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- the LITE chunk

    /// <summary>The LITE chunk's record: u8 kind, u8 flags (1 shadows, 2 sector light), i32 sprite, i32 x, y, z, u8 r, g,
    /// b, i32 radius, f32 energy, flicker, up, i32 tile; each record's size is written first (u16), so a newer one's
    /// fields past these are skipped by this reader.</summary>
    public const int RecordSize = 1 + 1 + 4 + 12 + 3 + 4 + 12 + 4 + 4;
    /// <summary>The first records' size (without the sector): still read, the sector -1.</summary>
    public const int FirstRecordSize = RecordSize - 4;

    public static void Write(BinaryWriter w, List<MapLight> lights)
    {
        w.Write(lights.Count);
        w.Write((ushort)RecordSize);
        foreach (var l in lights)
        {
            w.Write((byte)l.Kind);
            w.Write((byte)((l.Shadows ? 1 : 0) | (l.SectorLight ? 2 : 0)));
            w.Write(l.Sprite);
            w.Write(l.X); w.Write(l.Y); w.Write(l.Z);
            w.Write(l.R); w.Write(l.G); w.Write(l.B);
            w.Write(l.Radius);
            w.Write(l.Energy); w.Write(l.Flicker); w.Write(l.Up);
            w.Write(l.Tile);
            w.Write(l.Sector);
        }
    }

    public static List<MapLight> Read(byte[] d, int at, int len)
    {
        var r = new List<MapLight>();
        if (len < 6)
            throw new InvalidDataException(".MAPX: chunk LITE is too short");
        int n = BitConverter.ToInt32(d, at), size = BitConverter.ToUInt16(d, at + 4);
        if (n < 0 || size < FirstRecordSize || 6 + (long)n * size > len)
            throw new InvalidDataException($".MAPX: chunk LITE holds {n} lights of {size} bytes in {len}");
        for (int i = 0, p = at + 6; i < n; i++, p += size)
        {
            int flags = d[p + 1];
            r.Add(new MapLight
            {
                Kind = (LightKind)Math.Clamp((int)d[p], 0, 3), Shadows = (flags & 1) != 0, SectorLight = (flags & 2) != 0,
                Sprite = BitConverter.ToInt32(d, p + 2),
                X = BitConverter.ToInt32(d, p + 6), Y = BitConverter.ToInt32(d, p + 10), Z = BitConverter.ToInt32(d, p + 14),
                R = d[p + 18], G = d[p + 19], B = d[p + 20],
                Radius = BitConverter.ToInt32(d, p + 21),
                Energy = BitConverter.ToSingle(d, p + 25), Flicker = BitConverter.ToSingle(d, p + 29), Up = BitConverter.ToSingle(d, p + 33),
                Tile = BitConverter.ToInt32(d, p + 37),
                Sector = size >= RecordSize ? BitConverter.ToInt32(d, p + 41) : -1,
            });
        }
        return r;
    }
}
