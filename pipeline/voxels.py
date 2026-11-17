"""Convert Blood's voxels into MD3 models for DarkPlaces.

usage: python -I voxels.py [blood_dir] [game_dir]

Two sets:
  * Blood's own (KVX in BLOOD.RFF): a tile whose view type is 6 (voxel) or 7 (spinning voxel) is drawn as the
    voxel VOXEL.DAT gives it (viewProcessSprites, with voxels on): models/voxels/vNN.md3 (NN = voxel id).
  * Refreshed Supply's extra voxels (BloodEX.kpf: voxels/*.kvx, mapped to tiles by defs/extraVoxels.txt):
    switches, signs, candles, chandeliers, tools... They replace their tile wherever a sprite shows it:
    models/voxels/x_<file name>.md3.
Each voxel model is written as:
  <model>.md3                         the voxel's visible faces, one quad per voxel face (no merged faces,
                                      so no T-junctions and no cracks)
  <model>.md3_P.skin                  skin P = palookup P (the sprite's pal) for every Blood PLU
  models/voxels/pal_P.png             16x16 palette swatch per palookup: a face's texture coordinate is the
                                      centre of its colour's texel
  scripts/blood_voxels.shader         the swatch shaders (no mipmaps, no picmip: texels must not blend)
  data/voxels.txt                     tile -> model, z pivot and flags, read by the client

Placement follows Build's voxel drawing (engine.cpp drawvox and the sprite path that calls it): voxel x runs
to the sprite's left, y along its facing, z down; a voxel is xrepeat * 0.8 / 64 units wide and deep (Build's
256/320 voxel aspect) and yrepeat / 64 units tall. Without cstat 128 the voxel z coordinate zsiz / 2 + zpivot
sits at the sprite's z, so the model's origin is there and the model is stored in voxel units with x and y
already multiplied by 0.8: the client scales it by yrepeat / 64 (and xrepeat / 64 across). A Refreshed Supply
voxel's origin is its pivot, which the client puts where the sprite's picture is centred: the pivots are set so
that the animation frames of a tile (candle flames, of slightly different heights) line up.
"""
import math
import os
import re
import struct
import sys
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blood_assets as ba  # noqa: E402
from export_pals import PLU_NAMES  # noqa: E402
from rff import RFF  # noqa: E402

from PIL import Image  # noqa: E402

XY_ASPECT = 256.0 / 320.0
SURFACE = 'voxel'

# face directions in voxel space (dx, dy, dz) with z down
DIRS = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]


class Kvx:
    """Mip 0 of a KVX: size, pivot (voxel units) and the solid/colour grid, decoded like NBlood's loadkvx:
    a slab whose cull flags don't show its top face (16) is solid from the previous slab's end down to it."""

    def __init__(self, data):
        numbytes, self.xs, self.ys, self.zs, xp, yp, zp = struct.unpack_from('<7i', data, 0)
        self.px, self.py, self.pz = xp / 256.0, yp / 256.0, zp / 256.0
        xs, ys, zs = self.xs, self.ys, self.zs
        xyofs_at = 28 + (xs + 1) * 4
        xyofs = struct.unpack_from('<%dH' % (xs * (ys + 1)), data, xyofs_at)
        p = xyofs_at + xs * (ys + 1) * 2
        self.solid = bytearray(xs * ys * zs)
        self.color = bytearray(xs * ys * zs)
        for x in range(xs):
            for y in range(ys):
                n = xyofs[x * (ys + 1) + y + 1] - xyofs[x * (ys + 1) + y]
                base = (x * ys + y) * zs
                z1 = 0
                while n > 0:
                    z0, k, cull = data[p], data[p + 1], data[p + 2]
                    p += 3
                    if not (cull & 16):
                        for z in range(z1, min(z0, zs)):
                            self.solid[base + z] = 1
                    z1 = z0 + k
                    for z in range(z0, z1):
                        if z < zs:
                            self.solid[base + z] = 1
                            self.color[base + z] = data[p]
                        p += 1
                    n -= k + 3

    def is_solid(self, x, y, z):
        if x < 0 or y < 0 or z < 0 or x >= self.xs or y >= self.ys or z >= self.zs:
            return False
        return self.solid[(x * self.ys + y) * self.zs + z] != 0

    def faces(self):
        """(x, y, z, dir index, colour) of every voxel face open to the air"""
        out = []
        xs, ys, zs = self.xs, self.ys, self.zs
        for x in range(xs):
            for y in range(ys):
                base = (x * ys + y) * zs
                for z in range(zs):
                    if not self.solid[base + z]:
                        continue
                    for d, (dx, dy, dz) in enumerate(DIRS):
                        if not self.is_solid(x + dx, y + dy, z + dz):
                            out.append((x, y, z, d, self.color[base + z]))
        return out


def face_corners(x, y, z, d):
    """the four voxel-space corners of a face, counter-clockwise seen from outside"""
    if d == 0:
        return [(x + 1, y, z), (x + 1, y + 1, z), (x + 1, y + 1, z + 1), (x + 1, y, z + 1)]
    if d == 1:
        return [(x, y, z), (x, y, z + 1), (x, y + 1, z + 1), (x, y + 1, z)]
    if d == 2:
        return [(x, y + 1, z), (x, y + 1, z + 1), (x + 1, y + 1, z + 1), (x + 1, y + 1, z)]
    if d == 3:
        return [(x, y, z), (x + 1, y, z), (x + 1, y, z + 1), (x, y, z + 1)]
    if d == 4:
        return [(x, y, z + 1), (x + 1, y, z + 1), (x + 1, y + 1, z + 1), (x, y + 1, z + 1)]
    return [(x, y, z), (x, y + 1, z), (x + 1, y + 1, z), (x + 1, y, z)]


def md3_normal(n):
    """md3 normal bytes (pitch = zenith, yaw = azimuth, 256 steps a turn) as DarkPlaces decodes them"""
    pitch = int(round(math.acos(max(-1.0, min(1.0, n[2]))) * 128 / math.pi)) & 255
    yaw = int(round(math.atan2(n[1], n[0]) * 128 / math.pi)) & 255
    return pitch | yaw << 8


def build_mesh(kvx, at_pivot=False):
    """model-space vertices (x forward, y left, z up), texcoords, normals and clockwise triangles (the Quake
    engines' front face); the origin is at Build's voxel z origin (zsiz / 2 + zpivot), or at the pivot"""
    zoff = kvx.pz if at_pivot else kvx.zs * 0.5 + kvx.pz

    def to_model(v):
        vx, vy, vz = v
        return ((vy - kvx.py) * XY_ASPECT, (vx - kvx.px) * XY_ASPECT, zoff - vz)

    verts, st, normals, tris = [], [], [], []
    index = {}
    for x, y, z, d, c in kvx.faces():
        dx, dy, dz = DIRS[d]
        n = (dy, dx, -dz)
        uv = ((c % 16 + 0.5) / 16.0, (c // 16 + 0.5) / 16.0)
        ids = []
        for corner in face_corners(x, y, z, d):
            key = (corner, d, c)
            i = index.get(key)
            if i is None:
                i = len(verts)
                index[key] = i
                verts.append(to_model(corner))
                st.append(uv)
                normals.append(n)
            ids.append(i)
        # the corners run counter-clockwise in voxel space, a right-handed frame mapped to model space without
        # a mirror, so reversing them gives clockwise triangles seen from outside
        tris.append((ids[0], ids[2], ids[1]))
        tris.append((ids[0], ids[3], ids[2]))
    return verts, st, normals, tris


def write_md3(path, shader, verts, st, normals, tris):
    nv, nt = len(verts), len(tris)
    ofs_shaders = 108
    ofs_tris = ofs_shaders + 68
    ofs_st = ofs_tris + nt * 12
    ofs_xyz = ofs_st + nv * 8
    ofs_end = ofs_xyz + nv * 8
    surf = bytearray()
    surf += b'IDP3' + SURFACE.encode().ljust(64, b'\0')
    surf += struct.pack('<10i', 0, 1, 1, nv, nt, ofs_tris, ofs_shaders, ofs_st, ofs_xyz, ofs_end)
    surf += shader.encode().ljust(64, b'\0') + struct.pack('<i', 0)
    surf += b''.join(struct.pack('<3i', *t) for t in tris)
    surf += b''.join(struct.pack('<2f', *s) for s in st)
    surf += b''.join(struct.pack('<3hH', int(round(v[0] * 64)), int(round(v[1] * 64)), int(round(v[2] * 64)),
                                 md3_normal(n)) for v, n in zip(verts, normals))
    mn = tuple(min(v[i] for v in verts) for i in range(3))
    mx = tuple(max(v[i] for v in verts) for i in range(3))
    radius = max(math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]) for v in verts)
    frame = struct.pack('<3f3f3ff', *mn, *mx, 0, 0, 0, radius) + b'frame'.ljust(16, b'\0')
    ofs_frames = 108
    ofs_surfs = ofs_frames + len(frame)
    eof = ofs_surfs + len(surf)
    hdr = b'IDP3' + struct.pack('<i', 15) + os.path.basename(path).encode().ljust(64, b'\0')
    hdr += struct.pack('<9i', 0, 1, 0, 1, 0, ofs_frames, ofs_surfs, ofs_surfs, eof)
    with open(path, 'wb') as f:
        f.write(hdr + frame + surf)


SWATCH_SHADER = '''models/voxels/pal_P
{
\tnopicmip
\tnomipmaps
\t{
\t\tmap models/voxels/pal_P.png
\t}
}
'''


def write_palettes(rff, game):
    """pal_P.png for palookup 0 and every PLU Blood has (shade 0 row); returns the skin count"""
    pal = ba.load_palette(rff)
    out = os.path.join(game, 'models', 'voxels')
    remaps = {0: bytes(range(256))}
    for i, name in enumerate(PLU_NAMES):
        d = ba.load_plu(rff, name) if i else None
        if d:
            remaps[i] = d[:256]
    count = max(remaps) + 1
    for p in range(count):
        remap = remaps.get(p, remaps[0])
        img = Image.new('RGB', (16, 16))
        img.putdata([pal[remap[i]] for i in range(256)])
        img.save(os.path.join(out, 'pal_%d.png' % p))
    with open(os.path.join(game, 'scripts', 'blood_voxels.shader'), 'w') as f:
        f.write(''.join(SWATCH_SHADER.replace('_P', '_%d' % p) for p in range(count)))
    lst = os.path.join(game, 'scripts', 'shaderlist.txt')
    listed = open(lst).read().split() if os.path.exists(lst) else []
    if 'blood_voxels' not in listed:
        with open(lst, 'a') as f:
            f.write('blood_voxels\n')
    return count


# data/voxels.txt flags
VF_EXTRA = 1       # Refreshed Supply voxel: replaces its tile on any sprite (after rotation/animation picks the tile)
VF_ROTATE = 2      # spins (extraVoxels "rotate")
VF_RANDOMANG = 4   # drawn at a random angle per sprite (extraVoxels "bRandomizedAngle")


def load_extra_voxels(blood_dir):
    """Refreshed Supply's extra voxels: BloodEX.kpf (a zip) holds voxels/*.kvx and defs/extraVoxels.txt,
    'Voxel <picnum> { filename "voxels/NNNN_NAME.kvx" [rotate TRUE] [bRandomizedAngle TRUE] }'.
    Returns {tile: (model name, kvx bytes, flags)}."""
    path = os.path.join(blood_dir, 'BloodEX.kpf')
    if not os.path.exists(path):
        return {}
    z = zipfile.ZipFile(path)
    names = {n.lower(): n for n in z.namelist()}
    if 'defs/extravoxels.txt' not in names:
        return {}
    text = z.read(names['defs/extravoxels.txt']).decode('latin1')
    text = re.sub(r'/\*.*?\*/', '', text, flags=re.S)
    out = {}
    for m in re.finditer(r'Voxel\s+(\d+)\s*\{([^}]*)\}', text):
        body = m.group(2)
        fn = re.search(r'filename\s+"([^"]+)"', body)
        if not fn or fn.group(1).lower() not in names:
            continue
        flags = VF_EXTRA
        if re.search(r'\brotate\s+TRUE\b', body, re.I):
            flags |= VF_ROTATE
        if re.search(r'\bbRandomizedAngle\s+TRUE\b', body, re.I):
            flags |= VF_RANDOMANG
        stem = os.path.splitext(os.path.basename(fn.group(1)))[0].lower()
        out[int(m.group(1))] = ('x_' + stem, z.read(names[fn.group(1).lower()]), flags)
    return out


def main():
    blood_dir = sys.argv[1] if len(sys.argv) > 1 else ba.BLOOD_DIR
    ba.BLOOD_DIR = blood_dir      # the helpers read the install from there
    game = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'game', 'blood3d')
    out = os.path.join(game, 'models', 'voxels')
    os.makedirs(out, exist_ok=True)
    os.makedirs(os.path.join(game, 'data'), exist_ok=True)
    os.makedirs(os.path.join(game, 'scripts'), exist_ok=True)
    rff = RFF(os.path.join(blood_dir, 'BLOOD.RFF'))
    tiles = ba.load_tiles(blood_dir)
    vox = ba.load_dat('voxel.dat', 4096, 'h', blood_dir) or []
    nskins = write_palettes(rff, game)

    # tile -> (model name, kvx bytes, flags). Blood's own: tiles of view type 6/7 with a voxel in BLOOD.RFF
    # (tilePreloadTile turns one without a valid voxel back into a sprite). Refreshed Supply's extra voxels
    # take precedence for the tiles they name.
    used = {}
    for num, t in sorted(tiles.items()):
        if t.view in (6, 7) and num < len(vox) and vox[num] >= 0:
            e = rff.find(type='KVX', id=vox[num])
            if e:
                used[num] = ('v%d' % vox[num], None, 0, e)
    for num, (name, data, flags) in load_extra_voxels(blood_dir).items():
        used[num] = (name, data, flags, None)

    built = {}       # model name -> (z pivot, z size), or None when the voxel has no faces
    total_tris = 0
    for num, (name, data, flags, entry) in sorted(used.items()):
        if name in built:
            continue
        kvx = Kvx(data if entry is None else rff.read(entry))
        verts, st, normals, tris = build_mesh(kvx, at_pivot=bool(flags & VF_EXTRA))
        built[name] = (kvx.pz, kvx.zs) if tris else None
        if not tris:
            continue
        path = 'models/voxels/%s.md3' % name
        write_md3(os.path.join(game, path), 'models/voxels/pal_0', verts, st, normals, tris)
        for p in range(nskins):
            with open(os.path.join(game, '%s_%d.skin' % (path, p)), 'w') as f:
                f.write('%s,models/voxels/pal_%d\n' % (SURFACE, p))
        total_tris += len(tris)
    rows = [(num, name, built[name][0], flags, built[name][1]) for num, (name, _, flags, _) in sorted(used.items())
            if built[name] is not None]
    with open(os.path.join(game, 'data', 'voxels.txt'), 'w') as f:
        f.write('// <tile> <model> <zpivot> <flags> <zsize>: models/voxels/<model>.md3, z pivot and height in voxels;\n'
                '// flags 1 Refreshed Supply voxel (any sprite with the tile), 2 spins, 4 random angle per sprite\n')
        for row in rows:
            f.write('%d %s %g %d %d\n' % row)
    print('voxel models:', sum(1 for v in built.values() if v is not None), 'tiles:', len(rows),
          '(Refreshed Supply: %d)' % sum(1 for r in rows if r[3] & VF_EXTRA), 'skins:', nskins,
          'triangles:', total_tris)


if __name__ == '__main__':
    main()
