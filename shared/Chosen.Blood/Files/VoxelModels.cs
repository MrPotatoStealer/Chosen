// Chosen Blood module (GPL v2): Blood's voxels (KVX, as NBlood's loadkvx decodes them) as MD3 models for the game
// (as pipeline/voxels.py writes them for the DarkPlaces version, but with merged faces: Md3).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Chosen.Shared.Blood.Files;

/// <summary>Mip 0 of a KVX: size, pivot (voxel units) and the solid and colour grid (a slab whose cull flags don't show
/// its top face is solid from the previous slab's end down to it).</summary>
public sealed class Kvx
{
    public readonly int Xs, Ys, Zs;
    public readonly double Px, Py, Pz;
    readonly byte[] _solid, _color;

    public Kvx(byte[] d)
    {
        Xs = BitConverter.ToInt32(d, 4);
        Ys = BitConverter.ToInt32(d, 8);
        Zs = BitConverter.ToInt32(d, 12);
        Px = BitConverter.ToInt32(d, 16) / 256.0;
        Py = BitConverter.ToInt32(d, 20) / 256.0;
        Pz = BitConverter.ToInt32(d, 24) / 256.0;
        int xyofsAt = 28 + (Xs + 1) * 4;
        int p = xyofsAt + Xs * (Ys + 1) * 2;
        _solid = new byte[Xs * Ys * Zs];
        _color = new byte[Xs * Ys * Zs];
        for (int x = 0; x < Xs; x++)
            for (int y = 0; y < Ys; y++)
            {
                int at = xyofsAt + (x * (Ys + 1) + y) * 2;
                int n = BitConverter.ToUInt16(d, at + 2) - BitConverter.ToUInt16(d, at);
                int b = (x * Ys + y) * Zs;
                int z1 = 0;
                while (n > 0)
                {
                    int z0 = d[p], k = d[p + 1], cull = d[p + 2];
                    p += 3;
                    if ((cull & 16) == 0)
                        for (int z = z1; z < Math.Min(z0, Zs); z++)
                            _solid[b + z] = 1;
                    z1 = z0 + k;
                    for (int z = z0; z < z1; z++)
                    {
                        if (z < Zs)
                        {
                            _solid[b + z] = 1;
                            _color[b + z] = d[p];
                        }
                        p++;
                    }
                    n -= k + 3;
                }
            }
    }

    bool Solid(int x, int y, int z) =>
        x >= 0 && y >= 0 && z >= 0 && x < Xs && y < Ys && z < Zs && _solid[(x * Ys + y) * Zs + z] != 0;

    // face directions in voxel space, z down
    internal static readonly (int dx, int dy, int dz)[] Dirs = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    /// <summary>Every voxel face open to the air: (x, y, z, direction, colour).</summary>
    public IEnumerable<(int x, int y, int z, int d, int c)> Faces()
    {
        for (int x = 0; x < Xs; x++)
            for (int y = 0; y < Ys; y++)
            {
                int b = (x * Ys + y) * Zs;
                for (int z = 0; z < Zs; z++)
                {
                    if (_solid[b + z] == 0)
                        continue;
                    for (int d = 0; d < 6; d++)
                    {
                        var (dx, dy, dz) = Dirs[d];
                        if (!Solid(x + dx, y + dy, z + dz))
                            yield return (x, y, z, d, _color[b + z]);
                    }
                }
            }
    }
}

public static class VoxelModels
{
    const double XyAspect = 256.0 / 320.0;
    const string Surface = "voxel";
    public const int Extra = 1, Rotate = 2, RandomAngle = 4;     // data/voxels.txt flags
    /// <summary>What an add-on's own voxel models are named with (mod_v48: its KVX 48, not the base game's v48).</summary>
    public const string OwnPrefix = "mod_";

    static int Md3Normal(double nx, double ny, double nz)
    {
        int pitch = (int)Math.Round(Math.Acos(Math.Max(-1.0, Math.Min(1.0, nz))) * 128 / Math.PI) & 255;
        int yaw = (int)Math.Round(Math.Atan2(ny, nx) * 128 / Math.PI) & 255;
        return pitch | yaw << 8;
    }

    // ---- merged faces. Each layer's open faces of one colour, facing one way, merged into rectangles (greedy: along u,
    // then down v); a rectangle takes every corner of the model lying on its outline as a vertex of its own (the corner of
    // a face beside it or meeting it at an edge: without, a T-junction, a crack of single pixels along the edge), and is
    // fanned from a corner whose two edges have none (else from its centre), so every piece of its outline is a triangle's
    // edge the face beyond shares. Blood's voxels came to a quarter of their triangles (Eviction's street: 2.3 million of
    // the view's 2.8 million primitives were voxel models' faces, one quad each).

    // a face direction's plane and in-plane axes: d 0/1 x (u y, v z), 2/3 y (u x, v z), 4/5 z (u x, v y)
    static (int x, int y, int z) At(int d, int layer, int u, int v) => d < 2 ? (layer, u, v) : d < 4 ? (u, layer, v) : (u, v, layer);

    // the rectangle's corners in the order Corners gives a single face's (counter-clockwise in voxel space)
    static (int u, int v)[] RectCorners(int d, int u0, int v0, int u1, int v1) => d is 0 or 3 or 4
        ? new[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) }
        : new[] { (u0, v0), (u0, v1), (u1, v1), (u1, v0) };

    /// <summary>The open faces merged into rectangles: (direction, layer, u0, v0, u1, v1 exclusive, colour).</summary>
    static List<(int d, int layer, int u0, int v0, int u1, int v1, int c)> Rectangles(Kvx kvx)
    {
        var grids = new Dictionary<(int d, int layer), int[]>();
        int Us(int d) => d < 2 ? kvx.Ys : kvx.Xs;
        int Vs(int d) => d < 4 ? kvx.Zs : kvx.Ys;
        foreach (var (x, y, z, d, c) in kvx.Faces())
        {
            int layer = d switch { 0 => x + 1, 1 => x, 2 => y + 1, 3 => y, 4 => z + 1, _ => z };
            int u = d < 2 ? y : x, v = d < 4 ? z : y;
            if (!grids.TryGetValue((d, layer), out var g))
            {
                g = new int[Us(d) * Vs(d)];
                Array.Fill(g, -1);
                grids[(d, layer)] = g;
            }
            g[v * Us(d) + u] = c;
        }
        var rects = new List<(int, int, int, int, int, int, int)>();
        foreach (var ((d, layer), g) in grids.OrderBy(k => k.Key.d).ThenBy(k => k.Key.layer))
        {
            int us = Us(d), vs = Vs(d);
            for (int v = 0; v < vs; v++)
                for (int u = 0; u < us; u++)
                {
                    int c = g[v * us + u];
                    if (c < 0)
                        continue;
                    int u1 = u + 1;
                    while (u1 < us && g[v * us + u1] == c)
                        u1++;
                    int v1 = v + 1;
                    for (; v1 < vs; v1++)
                    {
                        bool row = true;
                        for (int k = u; k < u1 && row; k++)
                            row = g[v1 * us + k] == c;
                        if (!row)
                            break;
                    }
                    for (int vv = v; vv < v1; vv++)
                        for (int k = u; k < u1; k++)
                            g[vv * us + k] = -1;
                    rects.Add((d, layer, u, v, u1, v1, c));
                }
        }
        return rects;
    }

    /// <summary>The voxel's MD3 (null when it has no faces): model space x forward, y left, z up, origin at Build's voxel
    /// z origin (zsize / 2 + zpivot), or at the pivot (Refreshed Supply's voxels).</summary>
    public static byte[] Md3(Kvx kvx, string fileName, bool atPivot)
    {
        double zoff = atPivot ? kvx.Pz : kvx.Zs * 0.5 + kvx.Pz;
        var verts = new List<(double x, double y, double z)>();
        var st = new List<(double u, double v)>();
        var normals = new List<(double, double, double)>();
        var tris = new List<(int, int, int)>();
        // (points twice their voxel coordinates: a rectangle's centre falls on halves)
        var index = new Dictionary<((int, int, int) at2, int d, int c), int>();
        var rects = Rectangles(kvx);
        // every corner by the lines through it along each axis: the points on a rectangle's edges
        var lines = new Dictionary<(int axis, int a, int b), List<int>>();
        void OnLine(int axis, int a, int b, int p)
        {
            if (!lines.TryGetValue((axis, a, b), out var l))
                lines[(axis, a, b)] = l = new List<int>();
            l.Add(p);
        }
        var seen = new HashSet<(int, int, int)>();
        foreach (var (d, layer, u0, v0, u1, v1, _) in rects)
            foreach (var (u, v) in RectCorners(d, u0, v0, u1, v1))
            {
                var p = At(d, layer, u, v);
                if (!seen.Add(p))
                    continue;
                OnLine(0, p.y, p.z, p.x);
                OnLine(1, p.x, p.z, p.y);
                OnLine(2, p.x, p.y, p.z);
            }
        foreach (var l in lines.Values)
            l.Sort();
        // the corners strictly between p and q (one axis apart), from p's end
        List<(int, int, int)> Between((int x, int y, int z) p, (int x, int y, int z) q)
        {
            var r = new List<(int, int, int)>();
            int axis = p.x != q.x ? 0 : p.y != q.y ? 1 : 2;
            var key = axis == 0 ? (0, p.y, p.z) : axis == 1 ? (1, p.x, p.z) : (2, p.x, p.y);
            if (!lines.TryGetValue(key, out var l))
                return r;
            int a = axis == 0 ? p.x : axis == 1 ? p.y : p.z, b = axis == 0 ? q.x : axis == 1 ? q.y : q.z;
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            foreach (int t in l)
                if (t > lo && t < hi)
                    r.Add(axis == 0 ? (t, p.y, p.z) : axis == 1 ? (p.x, t, p.z) : (p.x, p.y, t));
            if (a > b)
                r.Reverse();
            return r;
        }
        foreach (var (d, layer, u0, v0, u1, v1, c) in rects)
        {
            var (dx, dy, dz) = Kvx.Dirs[d];
            var n = ((double)dy, (double)dx, (double)-dz);
            var uv = ((c % 16 + 0.5) / 16.0, (c / 16 + 0.5) / 16.0);
            int Vertex((int x, int y, int z) at2)
            {
                var key = (at2, d, c);
                if (!index.TryGetValue(key, out int i))
                {
                    i = verts.Count;
                    index[key] = i;
                    double vx = at2.x / 2.0, vy = at2.y / 2.0, vz = at2.z / 2.0;
                    verts.Add(((vy - kvx.Py) * XyAspect, (vx - kvx.Px) * XyAspect, zoff - vz));
                    st.Add(uv);
                    normals.Add(n);
                }
                return i;
            }
            var corners = RectCorners(d, u0, v0, u1, v1);
            var ring = new List<int>();          // the outline, counter-clockwise, corners and the points on its edges
            var cornerAt = new int[4];
            var extras = new bool[4];            // edge k (corner k to k + 1) has points on it
            for (int k = 0; k < 4; k++)
            {
                var p = At(d, layer, corners[k].u, corners[k].v);
                var q = At(d, layer, corners[(k + 1) & 3].u, corners[(k + 1) & 3].v);
                cornerAt[k] = ring.Count;
                ring.Add(Vertex((p.x * 2, p.y * 2, p.z * 2)));
                var mid = Between(p, q);
                extras[k] = mid.Count > 0;
                foreach (var m in mid)
                    ring.Add(Vertex((m.Item1 * 2, m.Item2 * 2, m.Item3 * 2)));
            }
            // reversed (clockwise seen from outside, the front face) as the single faces' were
            int apex = -1;
            for (int k = 0; k < 4 && apex < 0; k++)
                if (!extras[k] && !extras[(k + 3) & 3])
                    apex = cornerAt[k];
            if (apex >= 0)
            {
                int cnt = ring.Count;
                for (int i = 1; i < cnt - 1; i++)
                    tris.Add((ring[apex], ring[(apex + i + 1) % cnt], ring[(apex + i) % cnt]));
            }
            else
            {
                var a = At(d, layer, u0, v0);
                var b = At(d, layer, u1, v1);
                int centre = Vertex((a.x + b.x, a.y + b.y, a.z + b.z));
                for (int i = 0; i < ring.Count; i++)
                    tris.Add((centre, ring[(i + 1) % ring.Count], ring[i]));
            }
        }
        if (tris.Count == 0)
            return null;
        int nv = verts.Count, nt = tris.Count;
        const int ofsShaders = 108, ofsTris = ofsShaders + 68;
        int ofsSt = ofsTris + nt * 12, ofsXyz = ofsSt + nv * 8, ofsEnd = ofsXyz + nv * 8;
        using var surf = new MemoryStream();
        var s = new BinaryWriter(surf);
        s.Write("IDP3"u8);
        s.Write(Pad(Surface, 64));
        foreach (int v in new[] { 0, 1, 1, nv, nt, ofsTris, ofsShaders, ofsSt, ofsXyz, ofsEnd })
            s.Write(v);
        s.Write(Pad("models/voxels/pal_0", 64));
        s.Write(0);
        foreach (var (a, b, c) in tris)
        {
            s.Write(a); s.Write(b); s.Write(c);
        }
        foreach (var (u, v) in st)
        {
            s.Write((float)u); s.Write((float)v);
        }
        for (int i = 0; i < nv; i++)
        {
            var (x, y, z) = verts[i];
            var (nx, ny, nz) = normals[i];
            s.Write((short)(int)Math.Round(x * 64)); s.Write((short)(int)Math.Round(y * 64)); s.Write((short)(int)Math.Round(z * 64));
            s.Write((ushort)Md3Normal(nx, ny, nz));
        }
        s.Flush();
        double mnx = verts.Min(v => v.x), mny = verts.Min(v => v.y), mnz = verts.Min(v => v.z);
        double mxx = verts.Max(v => v.x), mxy = verts.Max(v => v.y), mxz = verts.Max(v => v.z);
        double radius = verts.Max(v => Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z));
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        const int ofsFrames = 108, frameSize = 56;
        int ofsSurfs = ofsFrames + frameSize, eof = ofsSurfs + (int)surf.Length;
        w.Write("IDP3"u8);
        w.Write(15);
        w.Write(Pad(fileName, 64));
        foreach (int v in new[] { 0, 1, 0, 1, 0, ofsFrames, ofsSurfs, ofsSurfs, eof })
            w.Write(v);
        foreach (double v in new[] { mnx, mny, mnz, mxx, mxy, mxz, 0, 0, 0, radius })
            w.Write((float)v);
        w.Write(Pad("frame", 16));
        w.Write(surf.ToArray());
        w.Flush();
        return ms.ToArray();
    }

    static byte[] Pad(string s, int n)
    {
        var b = Encoding.ASCII.GetBytes(s);
        if (b.Length >= n)
            return b;
        var p = new byte[n];
        b.CopyTo(p, 0);
        return p;
    }

    /// <summary>Refreshed Supply's extra voxels (BloodEX.kpf: voxels/*.kvx named by defs/extraVoxels.txt): tile -&gt;
    /// (model name x_&lt;file&gt;, KVX bytes, flags).</summary>
    public static Dictionary<int, (string name, byte[] kvx, int flags)> Extras(Kpf kpf)
    {
        var d = new Dictionary<int, (string, byte[], int)>();
        var text = kpf?.ReadText("defs/extraVoxels.txt");
        if (text == null)
            return d;
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        foreach (Match m in Regex.Matches(text, @"Voxel\s+(\d+)\s*\{([^}]*)\}"))
        {
            string body = m.Groups[2].Value;
            var fn = Regex.Match(body, "filename\\s+\"([^\"]+)\"");
            if (!fn.Success || kpf.Read(fn.Groups[1].Value) is not { } data)
                continue;
            int flags = Extra;
            if (Regex.IsMatch(body, @"\brotate\s+TRUE\b", RegexOptions.IgnoreCase))
                flags |= Rotate;
            if (Regex.IsMatch(body, @"\bbRandomizedAngle\s+TRUE\b", RegexOptions.IgnoreCase))
                flags |= RandomAngle;
            string stem = Path.GetFileNameWithoutExtension(fn.Groups[1].Value).ToLowerInvariant();
            d[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = ("x_" + stem, data, flags);
        }
        return d;
    }

    /// <summary>
    /// The voxel models of an install: Blood's own (a tile of view 6 or 7 with a voxel in BLOOD.RFF by VOXEL.DAT) and
    /// Refreshed Supply's extras (they win for their tiles). write(model name, md3 bytes) gets each model; the result is
    /// data/voxels.txt. own: an add-on's own KVX by id (null: none), taken before BLOOD.RFF's and kept over an extra.
    /// </summary>
    public static string Build(Rff blood, ArtSet art, int[] voxelDat, Kpf kpf, Action<string, byte[]> write, Func<int, byte[]> own = null)
    {
        var used = new SortedDictionary<int, (string name, byte[] data, int flags, RffEntry entry)>();
        foreach (int num in art.Numbers.OrderBy(n => n))
        {
            var t = art[num];
            if ((t.View != 6 && t.View != 7) || voxelDat == null || num >= voxelDat.Length || voxelDat[num] < 0)
                continue;
            if (own?.Invoke(voxelDat[num]) is { } mine)
                used[num] = (OwnPrefix + "v" + voxelDat[num].ToString(CultureInfo.InvariantCulture), mine, 0, null);
            else if (blood.Find(voxelDat[num], "KVX") is { } e)
                used[num] = ("v" + voxelDat[num].ToString(CultureInfo.InvariantCulture), null, 0, e);
        }
        foreach (var (num, (name, data, flags)) in Extras(kpf))
            if (!used.TryGetValue(num, out var had) || !had.name.StartsWith(OwnPrefix))
                used[num] = (name, data, flags, null);
        var built = new Dictionary<string, (double pz, int zs)?>();
        foreach (var (num, (name, data, flags, entry)) in used)
        {
            if (built.ContainsKey(name))
                continue;
            var kvx = new Kvx(entry == null ? data : blood.Read(entry));
            var md3 = Md3(kvx, name + ".md3", (flags & Extra) != 0);
            built[name] = md3 == null ? null : (kvx.Pz, kvx.Zs);
            if (md3 != null)
                write(name, md3);
        }
        var sb = new StringBuilder("// <tile> <model> <zpivot> <flags> <zsize>: models/voxels/<model>.md3, z pivot and height in voxels;\n" +
            "// flags 1 Refreshed Supply voxel (any sprite with the tile), 2 spins, 4 random angle per sprite\n");
        foreach (var (num, (name, _, flags, _)) in used)
            if (built[name] is { } b)
                sb.Append($"{num.ToString(CultureInfo.InvariantCulture)} {name} {G(b.pz)} {flags.ToString(CultureInfo.InvariantCulture)} {b.zs.ToString(CultureInfo.InvariantCulture)}\n");
        return sb.ToString();
    }

    /// <summary>Python's "%g": 6 significant digits, rounded half to even on the exact value, trailing zeros dropped.</summary>
    public static string G(double v)
    {
        if (v == 0)
            return "0";
        decimal d = (decimal)v;
        if (d != (decimal)(double)d || Math.Abs(v) >= 1e6 || Math.Abs(v) < 1e-4)
            return v.ToString("G6", CultureInfo.InvariantCulture);
        int exp = (int)Math.Floor(Math.Log10(Math.Abs(v)));
        decimal r = Math.Round(d, Math.Max(0, 5 - exp), MidpointRounding.ToEven);
        string s = r.ToString("0.##########", CultureInfo.InvariantCulture);
        return s;
    }
}
