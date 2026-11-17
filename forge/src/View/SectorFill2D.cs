// Chosen core: original code (UDB's 2D view modes: the sectors filled with their floor or ceiling, or brightness).
using System;
using System.Collections.Generic;
using System.Linq;
using Chosen.Forge.Data;
using Chosen.Forge.Document;
using Godot;

namespace Chosen.Forge.View;

/// <summary>The 2D view's modes, UDB's: lines only, sectors filled with their brightness, or with their floor or
/// ceiling picture.</summary>
public enum ViewMode { Wireframe, Brightness, Floors, Ceilings }

/// <summary>
/// The sectors filled in the 2D view (UDB's view modes): each sector's floor or ceiling picture mapped as Build maps
/// it (PreviewMesher.FlatMap: a texel 16 Build units, relative to the first wall, swapped, flipped, panned), through
/// its palette, darkened by its shade; or its shade alone (brightness). One mesh a sector in map units, made again
/// when the map changes and drawn with the view's transform, the largest first: a room in a room or over a room is
/// drawn over it.
/// </summary>
public sealed class SectorFill2D
{
    readonly List<(int sector, ArrayMesh mesh, Texture2D tex)> _parts = new();
    bool _dirty = true;
    ViewMode _built;
    bool _builtBright;
    readonly HashSet<int> _moved = new();

    /// <summary>The map (or the pictures) changed: made again when next drawn.</summary>
    public void Invalidate() => _dirty = true;

    /// <summary>Only these sectors moved (Play): theirs made again when next drawn.</summary>
    public void Invalidate(IEnumerable<int> sectors) => _moved.UnionWith(sectors);

    public void Draw(MapView2D v, MapDocument d, ViewMode mode)
    {
        if (mode == ViewMode.Wireframe || d == null)
            return;
        if (_dirty || mode != _built || v.FullBright != _builtBright)
            Build(d, mode, v.FullBright);
        else if (_moved.Count > 0)
        {
            for (int i = 0; i < _parts.Count; i++)
                if (_moved.Contains(_parts[i].sector) && Part(d, _parts[i].sector, mode, _builtBright) is { } p)
                    _parts[i] = p;
            _moved.Clear();
        }
        // map units to the view's pixels (MapView2D.MapToScreen)
        v.DrawSetTransformMatrix(new Transform2D(new Vector2(v.Zoom, 0), new Vector2(0, v.Zoom), v.Size * 0.5f - v.Centre * v.Zoom));
        foreach (var (_, mesh, tex) in _parts)
            v.DrawMesh(mesh, tex);
        v.DrawSetTransformMatrix(Transform2D.Identity);
    }

    void Build(MapDocument d, ViewMode mode, bool fullBright)
    {
        _dirty = false;
        _built = mode;
        _builtBright = fullBright;
        _parts.Clear();
        _moved.Clear();
        // (another layer's under the current one's)
        var order = Enumerable.Range(0, d.Sectors.Count).Where(s => s < d.Areas.Length)
            .OrderBy(s => d.SectorEditable(s) ? 1 : 0).ThenByDescending(s => d.Areas[s]);
        foreach (int s in order)
            if (Part(d, s, mode, fullBright) is { } p)
                _parts.Add(p);
    }

    /// <summary>A sector's filled mesh and picture; null when it isn't drawn. fullBright: a picture by no shade
    /// (View > Full Brightness; Brightness Levels still shows the shades).</summary>
    static (int, ArrayMesh, Texture2D)? Part(MapDocument d, int s, ViewMode mode, bool fullBright)
    {
        bool ceiling = mode == ViewMode.Ceilings;
        {
            var sec = d.Sectors[s];
            // (a hidden layer's isn't drawn, another shown layer's dimmed)
            if (sec.WallNum < 3 || sec.WallPtr < 0 || sec.WallPtr + sec.WallNum > d.Walls.Count || !d.SectorShown(s))
                return null;
            var tris = PreviewMesher.Fill(d, s);
            if (tris.Count == 0)
                return null;
            int tile = ceiling ? sec.CeilingPicnum : sec.FloorPicnum;
            int pal = ceiling ? sec.CeilingPal : sec.FloorPal;
            var color = fullBright && mode != ViewMode.Brightness ? Colors.White : PreviewMesher.Shade(ceiling ? sec.CeilingShade : sec.FloorShade);
            Texture2D tex = mode == ViewMode.Brightness ? null : TileImages.Get(tile, pal);
            if (mode != ViewMode.Brightness && tex == null)
                color = color * new Color(0.45f, 0.45f, 0.45f);
            if (!d.SectorEditable(s))
                color = color * new Color(0.35f, 0.35f, 0.35f);
            var flat = new PreviewMesher.FlatMap(d, s, ceiling, PreviewMesher.TileSize(tile));
            var v = new Vector2[tris.Count];
            var uv = new Vector2[tris.Count];
            var c = new Color[tris.Count];
            for (int i = 0; i < tris.Count; i++)
            {
                var (x, y) = tris[i];
                v[i] = new Vector2((float)x, (float)y);
                uv[i] = flat.Uv(x, y);
                c[i] = color;
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = v;
            arrays[(int)Mesh.ArrayType.TexUV] = uv;
            arrays[(int)Mesh.ArrayType.Color] = c;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return (s, mesh, tex);
        }
    }
}
