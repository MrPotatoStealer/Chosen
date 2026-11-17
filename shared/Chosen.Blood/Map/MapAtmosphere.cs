// Chosen Blood module (GPL v2): a map's atmospheres (the .MAPX's ATMO chunk) and the game's guesses for a map without.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Chosen.Shared.Blood.Map;

/// <summary>
/// One of a map's atmospheres (ATMO): the distance fog, the ground mist, the weather and the wind of the sectors given
/// it. The first is the map's own, the sectors given none have it. Build units throughout: distances along the floor
/// as the walls', heights as the sectors' (16 times finer).
/// </summary>
public sealed class MapAtmosphere
{
    public string Name = "Level";
    /// <summary>Where the distance fog is complete (Build units); 0: the map's (from its visibility, as the game's
    /// fog always was).</summary>
    public int FogDistance;
    /// <summary>The fog's own colour (sRGB), when FogOwnColour; else the level's (dark, white in a misty level).</summary>
    public bool FogOwnColour;
    public byte FogR, FogG, FogB;
    /// <summary>The ground mist's height over the floor (Build heights; 0: none), its density (1: the game's), its own
    /// colour (sRGB, when MistOwnColour; else the level's: a blue grey, white in a misty level).</summary>
    public int MistHeight;
    public float MistDensity = 1;
    public bool MistOwnColour;
    public byte MistR, MistG, MistB;
    /// <summary>The weather where the sky is open: how much rain, snow and leaves (0: none, 1: the game's own amount).</summary>
    public float Rain, Snow, Leaves;
    /// <summary>The wind: the way it blows (a Build angle, 0 east, 512 south), its speed (Build units a second), how
    /// much it gusts (0 steady, 1 the game's).</summary>
    public int WindAngle;
    public float WindSpeed;
    public float Gusts = 1;
    /// <summary>The seconds the camera's fog takes to become this one's as it comes in.</summary>
    public float Fade = 1.5f;

    public bool HasWeather => Rain > 0 || Snow > 0 || Leaves > 0;

    public MapAtmosphere Clone() => (MapAtmosphere)MemberwiseClone();

    /// <summary>The same settings (the name too).</summary>
    public bool SameAs(MapAtmosphere o) => o != null && Name == o.Name && FogDistance == o.FogDistance && FogOwnColour == o.FogOwnColour
        && FogR == o.FogR && FogG == o.FogG && FogB == o.FogB && MistHeight == o.MistHeight && MistDensity == o.MistDensity
        && MistOwnColour == o.MistOwnColour && MistR == o.MistR && MistG == o.MistG && MistB == o.MistB && Rain == o.Rain
        && Snow == o.Snow && Leaves == o.Leaves && WindAngle == o.WindAngle && WindSpeed == o.WindSpeed && Gusts == o.Gusts && Fade == o.Fade;
}

/// <summary>
/// A map's atmospheres and each sector's (ATMO; MapXExtras.Atmospheres, SectorAtmospheres), and the game's guesses for
/// a map without (Guess: the weather by episode, the ground mist over the open ground; the game's World.NativeWeather
/// and NativeMistGroups were these).
/// </summary>
public static class AtmosphereRules
{
    /// <summary>The ground mist's height the game always gave (24 Quake units: 6144 in Build's heights).</summary>
    public const int GameMistHeight = 24 * 256;

    /// <summary>
    /// ATMO: i32 count, each atmosphere a u16 size and its fields (the name: u8 length, UTF-8; i32 fog distance; u8 flags,
    /// 1 the fog's own colour, 2 the mist's; u8 fog r g b; i32 mist height; f32 mist density; u8 mist r g b; f32 rain,
    /// snow, leaves; i32 wind angle; f32 wind speed, gusts, fade): a reader takes the fields its size holds, and skips
    /// what it doesn't know; then i32 sectors and a u8 a sector, its atmosphere (0: the first).
    /// </summary>
    public static void Write(BinaryWriter w, IReadOnlyList<MapAtmosphere> list, IReadOnlyList<int> sectors, int sectorCount)
    {
        w.Write(list.Count);
        foreach (var a in list)
        {
            using var ms = new MemoryStream();
            using (var r = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                var name = Encoding.UTF8.GetBytes(a.Name ?? "");
                int nl = Math.Min(name.Length, 255);
                r.Write((byte)nl);
                r.Write(name, 0, nl);
                r.Write(a.FogDistance);
                r.Write((byte)((a.FogOwnColour ? 1 : 0) | (a.MistOwnColour ? 2 : 0)));
                r.Write(a.FogR); r.Write(a.FogG); r.Write(a.FogB);
                r.Write(a.MistHeight);
                r.Write(a.MistDensity);
                r.Write(a.MistR); r.Write(a.MistG); r.Write(a.MistB);
                r.Write(a.Rain); r.Write(a.Snow); r.Write(a.Leaves);
                r.Write(a.WindAngle);
                r.Write(a.WindSpeed); r.Write(a.Gusts); r.Write(a.Fade);
            }
            w.Write((ushort)ms.Length);
            w.Write(ms.GetBuffer(), 0, (int)ms.Length);
        }
        w.Write(sectorCount);
        for (int s = 0; s < sectorCount; s++)
            w.Write((byte)(s < sectors.Count && sectors[s] > 0 && sectors[s] < list.Count ? sectors[s] : 0));
    }

    public static (List<MapAtmosphere> list, List<int> sectors) Read(byte[] d, int pos, int len)
    {
        var list = new List<MapAtmosphere>();
        var sectors = new List<int>();
        int end = pos + len, p = pos;
        if (len < 4)
            return (list, sectors);
        int n = BitConverter.ToInt32(d, p);
        p += 4;
        for (int i = 0; i < n && p + 2 <= end; i++)
        {
            int size = BitConverter.ToUInt16(d, p);
            p += 2;
            int rec = p, recEnd = Math.Min(p + size, end);
            p = recEnd;
            var a = new MapAtmosphere();
            list.Add(a);
            int q = rec;
            bool Has(int bytes) => q + bytes <= recEnd;
            if (!Has(1))
                continue;
            int nl = d[q++];
            if (!Has(nl))
                continue;
            a.Name = Encoding.UTF8.GetString(d, q, nl);
            q += nl;
            if (Has(4)) { a.FogDistance = BitConverter.ToInt32(d, q); q += 4; }
            if (Has(1)) { int f = d[q++]; a.FogOwnColour = (f & 1) != 0; a.MistOwnColour = (f & 2) != 0; }
            if (Has(3)) { a.FogR = d[q]; a.FogG = d[q + 1]; a.FogB = d[q + 2]; q += 3; }
            if (Has(4)) { a.MistHeight = BitConverter.ToInt32(d, q); q += 4; }
            if (Has(4)) { a.MistDensity = BitConverter.ToSingle(d, q); q += 4; }
            if (Has(3)) { a.MistR = d[q]; a.MistG = d[q + 1]; a.MistB = d[q + 2]; q += 3; }
            if (Has(4)) { a.Rain = BitConverter.ToSingle(d, q); q += 4; }
            if (Has(4)) { a.Snow = BitConverter.ToSingle(d, q); q += 4; }
            if (Has(4)) { a.Leaves = BitConverter.ToSingle(d, q); q += 4; }
            if (Has(4)) { a.WindAngle = BitConverter.ToInt32(d, q); q += 4; }
            if (Has(4)) { a.WindSpeed = BitConverter.ToSingle(d, q); q += 4; }
            if (Has(4)) { a.Gusts = BitConverter.ToSingle(d, q); q += 4; }
            if (Has(4)) { a.Fade = BitConverter.ToSingle(d, q); q += 4; }
        }
        if (p + 4 <= end)
        {
            int ns = BitConverter.ToInt32(d, p);
            p += 4;
            for (int s = 0; s < ns && p < end; s++)
                sectors.Add(d[p++]);
        }
        if (list.Count == 0)
            list.Add(new MapAtmosphere());
        for (int s = 0; s < sectors.Count; s++)
            if (sectors[s] >= list.Count)
                sectors[s] = 0;
        return (list, sectors);
    }

    // ---------------------------------------------------------------- the game's guesses

    /// <summary>A Quake wind (degrees counter-clockwise from east, units a second) as a Build angle and speed.</summary>
    public static (int angle, float speed) WindFromQuake(float degrees, float speed) =>
        ((int)Math.Round(-degrees * 2048 / 360.0) & 2047, speed * 16);

    /// <summary>A Build wind as Quake's (degrees counter-clockwise from east, units a second).</summary>
    public static (float degrees, float speed) WindToQuake(int angle, float speed)
    {
        float deg = -(angle & 2047) * 360f / 2048f;
        if (deg <= -180)
            deg += 360;
        return (deg, speed / 16f);
    }

    /// <summary>The level's weather as the game guessed it (the old converter's): Blood's second episode, its winter,
    /// snow; the first's autumn graveyards rain and leaves; the rest rain; none for a level with no open sky. Its wind
    /// (Quake degrees and speed): 30 60 for snow, else 20 90.</summary>
    public static (bool rain, bool snow, bool leaves, float windDegrees, float windSpeed) GuessWeather(string mapName, bool anySky)
    {
        string t = (mapName ?? "").ToUpperInvariant();
        if (!anySky)
            return (false, false, false, 20, 90);
        if (t.StartsWith("E2M"))
            return (false, true, false, 30, 60);
        return (true, false, t.StartsWith("E1M"), 20, 90);
    }

    /// <summary>A level under open sky anywhere (a sky ceiling over a floor that isn't sky)?</summary>
    public static bool AnySky(IReadOnlyList<BSector> sectors)
    {
        foreach (var s in sectors)
            if ((s.CeilingStat & 1) != 0 && (s.FloorStat & 1) == 0)
                return true;
        return false;
    }

    /// <summary>
    /// The ground mist's areas as the game guessed them: groups of adjacent mist sectors at about one height (a step of
    /// 32 Quake units at most: 8192 in Build's heights). A mist sector is open to the sky (openSky: a parallax ceiling, or
    /// a link up to a room open to it; not a parallax floor or a link down), not moving, not under 32 x 32 Quake units
    /// (area in Build units), not raised 32 over all its open neighbours (a roof, a ledge); a group raised 32 over all
    /// the open ground round it (flat roofs side by side) has none. floorZ: Build's (down).
    /// </summary>
    public static List<List<int>> GuessMistGroups(int n, Func<int, bool> openSky, Func<int, double> floorZ, Func<int, double> area,
        Func<int, bool> moving, Func<int, IReadOnlyList<int>> neighbours)
    {
        const double Step = 32 * 256, MinArea = 32.0 * 32 * 256;
        double Up(int s) => -floorZ(s);
        var mist = new SortedSet<int>();
        for (int s = 0; s < n; s++)
        {
            if (!openSky(s) || moving(s) || area(s) < MinArea)
                continue;
            bool anyOpen = false, raised = true;
            foreach (int o in neighbours(s))
                if (openSky(o))
                {
                    anyOpen = true;
                    if (Up(s) < Up(o) + Step)
                        raised = false;
                }
            if (anyOpen && raised)
                continue;
            mist.Add(s);
        }
        var groups = new List<List<int>>();
        var seen = new HashSet<int>();
        foreach (int si in mist)
        {
            if (!seen.Add(si))
                continue;
            var group = new List<int>();
            var todo = new Stack<int>();
            todo.Push(si);
            while (todo.Count > 0)
            {
                int c = todo.Pop();
                group.Add(c);
                foreach (int o in neighbours(c))
                    if (mist.Contains(o) && !seen.Contains(o) && Math.Abs(Up(o) - Up(c)) <= Step)
                    {
                        seen.Add(o);
                        todo.Push(o);
                    }
            }
            var members = new HashSet<int>(group);
            double low = double.MaxValue;
            foreach (int c in group)
                low = Math.Min(low, Up(c));
            bool anyRound = false, allBelow = true;
            foreach (int c in group)
                foreach (int o in neighbours(c))
                    if (!members.Contains(o) && openSky(o))
                    {
                        anyRound = true;
                        if (Up(o) > low - Step)
                            allBelow = false;
                    }
            if (anyRound && allBelow)
                continue;
            group.Sort();
            groups.Add(group);
        }
        return groups;
    }

    /// <summary>
    /// The atmospheres the game gives a map without its own (and Forge's Generate Atmosphere): the level's (its fog from
    /// its visibility, the guessed weather and wind) and, when the ground mist lies anywhere, "Open ground" (the same
    /// with the game's mist) given to the mist's sectors.
    /// </summary>
    public static (List<MapAtmosphere> list, List<int> sectors) Guess(string mapName, bool anySky, int sectorCount, IEnumerable<List<int>> mistGroups)
    {
        var (rain, snow, leaves, deg, speed) = GuessWeather(mapName, anySky);
        var (angle, bspeed) = WindFromQuake(deg, speed);
        var level = new MapAtmosphere
        {
            Name = "Level", Rain = rain ? 1 : 0, Snow = snow ? 1 : 0, Leaves = leaves ? 1 : 0, WindAngle = angle, WindSpeed = bspeed,
        };
        var list = new List<MapAtmosphere> { level };
        var sectors = new List<int>(new int[sectorCount]);
        bool any = false;
        foreach (var g in mistGroups)
            foreach (int s in g)
                if (s >= 0 && s < sectorCount)
                {
                    sectors[s] = 1;
                    any = true;
                }
        if (any)
        {
            var mist = level.Clone();
            mist.Name = "Open ground";
            mist.MistHeight = GameMistHeight;
            list.Add(mist);
        }
        return (list, sectors);
    }
}
