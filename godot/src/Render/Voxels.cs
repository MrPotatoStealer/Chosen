using System;
using System.Collections.Generic;
using System.IO;
using Chosen.Data;
using Chosen.Sim;
using Godot;

namespace Chosen.Render;

/// <summary>
/// Blood's voxel models as the DarkPlaces version made them (pipeline/voxels.py: models/voxels/&lt;name&gt;.md3, one
/// surface whose texture coordinates pick from a palette strip pal_&lt;palookup&gt;.png) and data/voxels.txt:
/// "&lt;tile&gt; &lt;model&gt; &lt;zpivot&gt; &lt;flags&gt; &lt;zsize&gt;". Flags: 1 a Refreshed Supply voxel (it replaces
/// the tile any sprite shows), 2 spins, 4 a random angle per sprite.
/// </summary>
public static class Voxels
{
    public const int Extra = 1, Rotate = 2, RandomAngle = 4;

    public readonly struct Info
    {
        public readonly string Model;
        public readonly float ZPivot, ZSize;
        public readonly int Flags;

        public Info(string model, float zPivot, int flags, float zSize)
        {
            Model = model;
            ZPivot = zPivot;
            Flags = flags;
            ZSize = zSize;
        }
    }

    static Dictionary<int, Info> _tiles;
    static readonly Dictionary<string, ArrayMesh> _meshes = new();
    static readonly Dictionary<int, Texture2D> _palettes = new();

    /// <summary>Read again when next asked (an add-on activated or left: its own models and tiles).</summary>
    public static void Reload()
    {
        _tiles = null;
        _meshes.Clear();
        _palettes.Clear();
    }

    public static bool TryGet(int tile, out Info info)
    {
        if (_tiles == null)
            Load();
        return _tiles.TryGetValue(tile, out info);
    }

    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    static void Load()
    {
        _tiles = new Dictionary<int, Info>();
        var path = BloodData.DataFile("voxels.txt");
        if (!File.Exists(path))
            return;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("//"))
                continue;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 5 || !int.TryParse(p[0], out int t))
                continue;
            _tiles[t] = new Info(p[1], F(p[2]), int.Parse(p[3]), F(p[4]));
        }
    }

    /// <summary>A model's mesh (Godot axes and units), loaded the first time; null if it can't be read.</summary>
    public static ArrayMesh Mesh(string model)
    {
        if (_meshes.TryGetValue(model, out var m))
            return m;
        m = null;
        var path = BloodData.Find("models/voxels/" + model + ".md3");
        if (File.Exists(path))
        {
            try
            {
                m = LoadMd3(File.ReadAllBytes(path));
                Core.Perf.VoxelLoads++;
            }
            catch (Exception e)
            {
                GD.PushWarning($"voxel {model}: {e.Message}");
            }
        }
        _meshes[model] = m;
        return m;
    }

    /// <summary>The palette strip for a palookup (the model's skin: TileArt.Swatch; the palette itself for a pal without
    /// a lookup).</summary>
    public static Texture2D Palette(int pal)
    {
        if (_palettes.TryGetValue(pal, out var t))
            return t;
        return _palettes[pal] = ImageTexture.CreateFromImage(TileArt.Swatch(pal));
    }

    /// <summary>Frame 0 of an MD3 file's surfaces (vertices are 1/64 unit, Quake axes) with their normals (the dynamic
    /// lights' shading).</summary>
    static ArrayMesh LoadMd3(byte[] d)
    {
        int I(int o) => BitConverter.ToInt32(d, o);
        if (d.Length < 108 || d[0] != 'I' || d[1] != 'D' || d[2] != 'P' || d[3] != '3')
            throw new InvalidDataException("not an MD3");
        int numSurfaces = I(84), ofsSurfaces = I(100);
        var mesh = new ArrayMesh();
        int o = ofsSurfaces;
        for (int si = 0; si < numSurfaces; si++)
        {
            // surface header: ident, name[64], flags, numFrames, numShaders, numVerts, numTriangles, ofsTriangles,
            // ofsShaders, ofsSt, ofsXyzNormal, ofsEnd
            int numVerts = I(o + 80), numTris = I(o + 84);
            int ofsTris = I(o + 88), ofsSt = I(o + 96), ofsXyz = I(o + 100), ofsEnd = I(o + 104);
            var verts = new Vector3[numVerts];
            var normals = new Vector3[numVerts];
            var uvs = new Vector2[numVerts];
            for (int v = 0; v < numVerts; v++)
            {
                int xo = o + ofsXyz + v * 8;
                var q = new Vector3(BitConverter.ToInt16(d, xo), BitConverter.ToInt16(d, xo + 2), BitConverter.ToInt16(d, xo + 4)) / 64f;
                verts[v] = Coords.QuakeToGodot(q);
                // the normal: low byte the angle from +z, high byte the angle around it (256 steps a turn); a voxel
                // face's own axis (VoxelModels.Md3 gives every face direction its own vertices: flat)
                float zen = d[xo + 6] * Mathf.Pi / 128f, azi = d[xo + 7] * Mathf.Pi / 128f;
                var qn = new Vector3(Mathf.Sin(zen) * Mathf.Cos(azi), Mathf.Sin(zen) * Mathf.Sin(azi), Mathf.Cos(zen));
                normals[v] = new Vector3(qn.Y, qn.Z, qn.X).Normalized();     // Quake axes to Godot's, as QuakeToGodot
                uvs[v] = new Vector2(BitConverter.ToSingle(d, o + ofsSt + v * 8), BitConverter.ToSingle(d, o + ofsSt + v * 8 + 4));
            }
            var idx = new int[numTris * 3];
            for (int t = 0; t < numTris * 3; t++)
                idx[t] = I(o + ofsTris + t * 4);
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Godot.Mesh.ArrayType.Normal] = normals;
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Godot.Mesh.ArrayType.Index] = idx;
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            o += ofsEnd;
        }
        return mesh;
    }
}
