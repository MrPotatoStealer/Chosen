"""Export Blood assets into the DarkPlaces game directory.

usage: python -I export.py [blood_dir] [game_dir]

Produces:
  textures/blood/NNNN.png          every ART tile, opaque (world textures: walls/floors)
  textures/blood_masked/NNNN.png   every ART tile with palette index 255 transparent
                                   (masked walls, sprites and HUD pics drawn by CSQC)
  scripts/blood_masked.shader      alpha-tested shaders for the masked set
  scripts/blood_tools.shader       clip/nodraw/caulk/trigger/hint/water/sky tool shaders
  sound/blood/<raw>.wav            every RAW sample (8-bit mono); looped samples also as
                                   <raw>_loop.wav carrying the loop point as a cue chunk
  sound/cdtracks/trackNNN.ogg      Refreshed Supply CD music
  sound/midi/NAME.mid, sc55/       the MIDI songs and FM timbres (SOUNDS.RFF), Refreshed Supply's SC-55 recordings
  data/blood.ini                   the level info: episodes, maps, CD tracks, menu music
  data/rfire.clu                   the procedural fire's colour lookup (tile 2342)
  gfx/palette.lmp                  Blood palette
  gfx/normal.plu                   Blood's shade table (the classic palette mode)
  movie/                           intro logos and cutscenes: the DOS .smk/.wav, remastered/ Refreshed Supply's .ogv
  data/tiles.txt                   tile metadata (size, offsets, anim, view type, surface)
  data/seq.txt                     SEQ animations
  data/qav.txt                     QAV weapon/HUD animations
  data/sfx.txt                     SFX sound definitions
"""
import os
import shutil
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blood_assets as ba  # noqa: E402
import fire  # noqa: E402
from rff import RFF  # noqa: E402

from PIL import Image  # noqa: E402

TRANSPARENT = bytes(4)


def tile_image(tile, pal, masked):
    rgba = bytearray()
    for i in tile.rows():
        if masked and i == 255:
            rgba += TRANSPARENT
        else:
            r, g, b = pal[i]
            rgba += bytes((r, g, b, 255))
    return Image.frombytes('RGBA', (tile.w, tile.h), bytes(rgba))


def export_textures(tiles, pal, game, rff=None):
    """Opaque world textures in textures/blood, alpha-masked copies in textures/blood_masked."""
    solid_dir = os.path.join(game, 'textures', 'blood')
    mask_dir = os.path.join(game, 'textures', 'blood_masked')
    os.makedirs(solid_dir, exist_ok=True)
    os.makedirs(mask_dir, exist_ok=True)
    for num, t in sorted(tiles.items()):
        if t.w <= 0 or t.h <= 0:
            continue
        name = '%04d.png' % num
        sp = os.path.join(solid_dir, name)
        if not os.path.exists(sp):
            tile_image(t, pal, False).convert('RGB').save(sp)
        mp = os.path.join(mask_dir, name)
        if not os.path.exists(mp):
            tile_image(t, pal, True).save(mp)
    if rff is not None:
        print('fire frames:', fire.write_frames(rff, pal, game))
        # the fire's colour lookup (RFIRE.CLU): the Godot game runs Blood's fire live (Render/ProceduralFire.cs)
        e = rff.find('RFIRE', 'CLU')
        if e:
            os.makedirs(os.path.join(game, 'data'), exist_ok=True)
            with open(os.path.join(game, 'data', 'rfire.clu'), 'wb') as f:
                f.write(rff.read(e))
    export_shaders(tiles, game)
    print('textures:', len(tiles))


def anim_frames(tiles, num):
    """Build tile animation (picanm) as an explicit frame list + frequency, or None.
    type 1 oscillates 0..n..1, type 2 runs forward, type 3 backward; one step every 2^speed ticks (120 Hz)."""
    t = tiles[num]
    n = t.anim
    if n <= 0 or t.animtype == 0:
        return None
    if any((num + i) not in tiles or tiles[num + i].w <= 0 for i in range(n + 1)):
        return None
    if t.animtype == 1:
        seq = list(range(0, n + 1)) + list(range(n - 1, 0, -1))
    elif t.animtype == 2:
        seq = list(range(0, n + 1))
    else:
        seq = [0] + list(range(n, 0, -1))
    return [num + i for i in seq], 120.0 / (1 << t.speed)


def anim_maps(tiles, num, folder):
    """animMap image paths in textures/<folder> + frequency for an animated tile (picanm, or Blood's procedural
    fire), or None"""
    proc = fire.anim_maps(num, folder)
    if proc:
        return proc
    anim = anim_frames(tiles, num)
    if not anim:
        return None
    frames, freq = anim
    return ['textures/%s/%04d.png' % (folder, x) for x in frames], freq


def export_shaders(tiles, game):
    """Masked shaders for every tile; animated tiles (lava, water, fans, screens) get animMap shaders."""
    os.makedirs(os.path.join(game, 'scripts'), exist_ok=True)
    nanim = 0
    with open(os.path.join(game, 'scripts', 'blood_masked.shader'), 'w') as f:
        for num, t in sorted(tiles.items()):
            if t.w <= 0 or t.h <= 0:
                continue
            anim = anim_maps(tiles, num, 'blood_masked')
            if anim:
                frames, freq = anim
                maps = ' '.join(frames)
                f.write(MASKED_ANIM_SHADER.replace('NNNN', '%04d' % num).replace('FREQ', '%g' % freq).replace('MAPS', maps))
            else:
                f.write(MASKED_SHADER.replace('NNNN', '%04d' % num))
    with open(os.path.join(game, 'scripts', 'blood_anim.shader'), 'w') as f:
        for num, t in sorted(tiles.items()):
            if t.w <= 0 or t.h <= 0:
                continue
            anim = anim_maps(tiles, num, 'blood')
            if not anim:
                continue
            frames, freq = anim
            maps = ' '.join(frames)
            f.write(SOLID_ANIM_SHADER.replace('NNNN', '%04d' % num).replace('FREQ', '%g' % freq).replace('MAPS', maps))
            nanim += 1
    # liquids: every tile Blood marks as a water (5) or goo (13) surface, as a swimmable volume texture
    surf = ba.load_dat('surface.dat', 8192, 'b') or []
    nliq = 0
    with open(os.path.join(game, 'scripts', 'blood_liquid.shader'), 'w') as f:
        for num, t in sorted(tiles.items()):
            if t.w <= 0 or t.h <= 0 or num >= len(surf) or surf[num] not in (5, 13):
                continue
            if 255 in t.pixels:
                continue    # masked sprite art (whirlpools, splashes), not a liquid surface
            anim = anim_frames(tiles, num)
            if anim:
                frames, freq = anim
                stage = 'animMap %g %s' % (freq, ' '.join('textures/blood/%04d.png' % x for x in frames))
            else:
                stage = 'map textures/blood/%04d.png' % num
            f.write(LIQUID_SHADER.replace('NNNN', '%04d' % num).replace('STAGE', stage)
                    .replace('CONTENTS', 'water' if surf[num] == 5 else 'slime'))
            nliq += 1
    # skies: Blood's parallax sky is a 360 degree panorama (16 strips of 64x400 starting at tile 2500);
    # Refreshed Supply adds 1024x400 panoramas. Each becomes a skybox (env/blood_sky_N_*) + shader.
    skies = export_skyboxes(tiles, game)
    write_sky_shaders(game, skies)
    print('skies:', skies)
    with open(os.path.join(game, 'scripts', 'shaderlist.txt'), 'w') as f:
        f.write('blood_anim\nblood_masked\nblood_liquid\nblood_sky\nblood_tools\n')
    print('animated textures:', nanim, 'liquids:', nliq)


SKY_SHADER = '''textures/blood_sky/NNNN
{
\tqer_editorimage env/blood_sky_NNNN_px.png
\tsurfaceparm sky
\tsurfaceparm noimpact
\tsurfaceparm nolightmap
\tq3map_surfacelight 30
\tskyparms env/blood_sky_NNNN - -
}
'''


def sky_panorama(tiles, pal, start):
    """Panorama image: a single wide tile, or 16 consecutive 64-wide strips (Build parallax sky)."""
    t = tiles.get(start)
    if not t:
        return None
    if t.w >= 512:
        return tile_image(t, pal, False).convert('RGB')
    strips = [tiles.get(start + i) for i in range(16)]
    if any(s is None or s.w != t.w or s.h != t.h for s in strips):
        return None
    pano = Image.new('RGB', (t.w * 16, t.h))
    for i, s in enumerate(strips):
        pano.paste(tile_image(s, pal, False).convert('RGB'), (i * t.w, 0))
    return pano


SKY_TILES = (2500, 3678)   # parallax sky panoramas used by the levels (16 strips each)
SKY_FACE = 1024            # cube face size: each sky texel stays a crisp, near-even block (DarkPlaces samples
                           # skyboxes without mipmaps)


def sky_name(start, ypan=0):
    """skybox name of a sky tile drawn with a ceiling y-panning (each panning moves the horizon)"""
    return '%04d' % start + ('_y%d' % ypan if ypan else '')


def sky_horizon(h, ypan):
    """the panorama row Build draws at the horizon: Polymost's sky v at eye level is tilesizy / 2 + yoffs
    (Blood leaves the sky's yoffs 0), plus the sector's ypanning in 1/256 of the tile height rounded up to a
    power of two (doubled when that isn't the height itself, r_parallaxskypanning). The texture wraps over that
    height, so a panning past half of it moves the other way (E3M2's 255: four rows up, not a whole tile down)"""
    i = 1 << max(0, (h - 1).bit_length())
    if i != h:
        i += i
    return h / 2.0 + (ypan - 256 if ypan >= 128 else ypan) * i / 256.0


# DarkPlaces' skybox (r_sky.c): per face (suffix _px _nx _py _ny _pz _nz, loaded without flips), its corner
# directions at texture coordinates (0,0) (1,0) (0,1) (1,1); image x runs along s, rows along t
SKY_FACES = {
    'px': ((16, 16, 16), (16, 16, -16), (16, -16, 16), (16, -16, -16)),
    'nx': ((-16, 16, -16), (-16, 16, 16), (-16, -16, -16), (-16, -16, 16)),
    'py': ((-16, 16, -16), (16, 16, -16), (-16, 16, 16), (16, 16, 16)),
    'ny': ((-16, -16, 16), (16, -16, 16), (-16, -16, -16), (16, -16, -16)),
    'pz': ((-16, 16, 16), (16, 16, 16), (-16, -16, 16), (16, -16, 16)),
    'nz': ((16, 16, -16), (-16, 16, -16), (16, -16, -16), (-16, -16, -16)),
}


def make_skybox(pano, out, name, ypan=0):
    """Project a cylindrical Build sky onto DarkPlaces' cube faces. Like Polymost the panorama wraps once around
    the view (its width is 360 degrees, Build angle 0 = +x turning towards -y) and its rows are spaced like its
    columns at the horizon, linear in the tangent of the elevation; rows beyond the panorama repeat its top and
    bottom rows."""
    import math
    import numpy as np
    src = np.asarray(pano)
    ph, pw = src.shape[:2]
    horizon = sky_horizon(ph, ypan)
    scale = pw / (2 * math.pi)     # pixels per radian at the horizon
    size = SKY_FACE
    c = (np.arange(size) + 0.5) / size
    s, t = np.meshgrid(c, c)
    for face, (p00, p10, p01, p11) in SKY_FACES.items():
        d = [(1 - s) * (1 - t) * p00[k] + s * (1 - t) * p10[k] + (1 - s) * t * p01[k] + s * t * p11[k]
             for k in range(3)]
        yaw = np.arctan2(d[1], d[0])
        hyp = np.hypot(d[0], d[1])
        sx = np.floor(-yaw / (2 * math.pi) * pw).astype(np.int64) % pw
        tan = np.where(hyp > 1e-6, d[2] / np.maximum(hyp, 1e-6), np.sign(d[2]) * 1e6)
        row = np.floor(horizon - tan * scale)
        sy = np.clip(row, 0, ph - 1).astype(np.int64)
        img = src[sy, sx].astype(np.float32)
        # beyond the panorama the repeated edge row would converge into streaks at the zenith and nadir (Build
        # never looks that far up): fade it to the edge row's average colour over the first 12 degrees
        elev = np.arctan2(d[2], hyp)
        for edge, lim in ((0, math.atan(horizon / scale)), (ph - 1, -math.atan((ph - 1 - horizon) / scale))):
            past = (row < 0) if edge == 0 else (row > ph - 1)
            w = np.clip((np.abs(elev) - abs(lim)) / math.radians(12), 0, 1) * past
            img = img * (1 - w[..., None]) + src[edge].mean(axis=0) * w[..., None]
        Image.fromarray(np.round(img).astype(np.uint8)).save(os.path.join(out, 'blood_sky_%s_%s.png' % (name, face)))


def sky_variants(game):
    """(tile, ypan) skyboxes converted maps asked for (data/skyvariants.txt)"""
    out = set()
    path = os.path.join(game, 'data', 'skyvariants.txt')
    if os.path.exists(path):
        for line in open(path):
            v = line.split()
            if len(v) >= 2:
                out.add((int(v[0]), int(v[1])))
    return out


def write_sky_shaders(game, names):
    with open(os.path.join(game, 'scripts', 'blood_sky.shader'), 'w') as f:
        for name in sorted(names):
            f.write(SKY_SHADER.replace('NNNN', name))


def export_skyboxes(tiles, game, pal=None, starts=SKY_TILES):
    """skyboxes of the level skies (unpanned) and of the panned variants converted maps use"""
    if pal is None:
        rff = RFF(os.path.join(ba.BLOOD_DIR, 'BLOOD.RFF'))
        pal = ba.load_palette(rff)
    out = os.path.join(game, 'env')
    os.makedirs(out, exist_ok=True)
    made = []
    for start, ypan in sorted(set((s, 0) for s in starts) | sky_variants(game)):
        pano = sky_panorama(tiles, pal, start)
        if pano is None:
            continue
        make_skybox(pano, out, sky_name(start, ypan), ypan)
        made.append(sky_name(start, ypan))
    return made


def ensure_sky_variant(blood_dir, game, start, ypan):
    """the skybox of a sky tile at a ceiling y-panning, for a converted map (listed for export_skyboxes)"""
    if not ypan:
        return sky_name(start)
    path = os.path.join(game, 'data', 'skyvariants.txt')
    if (start, ypan) not in sky_variants(game):
        with open(path, 'a') as f:
            f.write('%d %d\n' % (start, ypan))
    name = sky_name(start, ypan)
    out = os.path.join(game, 'env')
    if not os.path.exists(os.path.join(out, 'blood_sky_%s_px.png' % name)):
        rff = RFF(os.path.join(blood_dir, 'BLOOD.RFF'))
        pano = sky_panorama(ba.load_tiles(blood_dir), ba.load_palette(rff), start)
        if pano is None:
            return sky_name(start)
        os.makedirs(out, exist_ok=True)
        make_skybox(pano, out, name, ypan)
    have = set()
    shader = os.path.join(game, 'scripts', 'blood_sky.shader')
    if os.path.exists(shader):
        for line in open(shader):
            if line.startswith('textures/blood_sky/'):
                have.add(line.strip()[len('textures/blood_sky/'):])
    if name not in have:
        write_sky_shaders(game, have | {name})
    return name

LIQUID_SHADER = '''textures/blood_liquid/NNNN
{
\tqer_editorimage textures/blood/NNNN.png
\tqer_trans 0.6
\tsurfaceparm CONTENTS
\tsurfaceparm nonsolid
\tsurfaceparm trans
\tsurfaceparm nolightmap
\tsurfaceparm nomarks
\tcull none
\t{
\t\tSTAGE
\t\trgbGen identityLighting
\t}
}
'''


SOLID_ANIM_SHADER = '''textures/blood/NNNN
{
\tqer_editorimage textures/blood/NNNN.png
\t{
\t\tanimMap FREQ MAPS
\t\trgbGen identity
\t}
\t{
\t\tmap $lightmap
\t\tblendFunc GL_DST_COLOR GL_ZERO
\t\trgbGen identity
\t}
}
'''

MASKED_ANIM_SHADER = '''textures/blood_masked/NNNN
{
\tqer_editorimage textures/blood_masked/NNNN.png
\tqer_alphafunc greater 0.5
\tsurfaceparm trans
\tsurfaceparm alphashadow
\tcull none
\t{
\t\tanimMap FREQ MAPS
\t\talphaFunc GE128
\t\tdepthWrite
\t\trgbGen identity
\t}
\t{
\t\tmap $lightmap
\t\tblendFunc GL_DST_COLOR GL_ZERO
\t\tdepthFunc equal
\t\trgbGen identity
\t}
}
'''


MASKED_SHADER = '''textures/blood_masked/NNNN
{
\tqer_editorimage textures/blood_masked/NNNN.png
\tqer_alphafunc greater 0.5
\tsurfaceparm trans
\tsurfaceparm alphashadow
\tcull none
\t{
\t\tmap textures/blood_masked/NNNN.png
\t\talphaFunc GE128
\t\tdepthWrite
\t\trgbGen identity
\t}
\t{
\t\tmap $lightmap
\t\tblendFunc GL_DST_COLOR GL_ZERO
\t\tdepthFunc equal
\t\trgbGen identity
\t}
}
'''

TOOL_SHADERS = '''textures/tools/wallclip
{
	qer_trans 0.4
	surfaceparm nodraw
	surfaceparm nolightmap
	surfaceparm nonsolid
	surfaceparm trans
	surfaceparm nomarks
	surfaceparm noimpact
	surfaceparm playerclip
	surfaceparm monsterclip
}

textures/tools/clip
{
\tqer_trans 0.4
\tsurfaceparm nodraw
\tsurfaceparm nolightmap
\tsurfaceparm nonsolid
\tsurfaceparm trans
\tsurfaceparm nomarks
\tsurfaceparm noimpact
\tsurfaceparm playerclip
}

textures/tools/origin
{
\tqer_trans 0.5
\tsurfaceparm nodraw
\tsurfaceparm nolightmap
\tsurfaceparm nonsolid
\tsurfaceparm trans
\tsurfaceparm nomarks
\tsurfaceparm origin
}

textures/tools/nodraw
{
\tsurfaceparm nodraw
\tsurfaceparm nolightmap
\tsurfaceparm nonsolid
\tsurfaceparm trans
\tsurfaceparm nomarks
}

textures/tools/caulk
{
\tsurfaceparm nodraw
\tsurfaceparm nolightmap
\tsurfaceparm nomarks
}

textures/tools/trigger
{
\tqer_trans 0.5
\tsurfaceparm nodraw
\tsurfaceparm nolightmap
\tsurfaceparm nonsolid
\tsurfaceparm trans
\tsurfaceparm nomarks
}

textures/tools/hint
{
\tqer_trans 0.3
\tsurfaceparm nodraw
\tsurfaceparm nonsolid
\tsurfaceparm structural
\tsurfaceparm trans
\tsurfaceparm noimpact
\tsurfaceparm hint
}

textures/tools/water
{
\tqer_trans 0.5
\tsurfaceparm nodraw
\tsurfaceparm nonsolid
\tsurfaceparm trans
\tsurfaceparm water
\tsurfaceparm nolightmap
}

textures/tools/link
{
\tqer_trans 0.4
\tsurfaceparm nodraw
\tsurfaceparm nolightmap
\tsurfaceparm nonsolid
\tsurfaceparm trans
\tsurfaceparm nomarks
\tsurfaceparm botclip
}

textures/tools/portal
{
\tqer_editorimage textures/tools/portal.png
\tsurfaceparm nolightmap
\tsurfaceparm nonsolid
\tsurfaceparm trans
\tsurfaceparm nomarks
\tsurfaceparm botclip
\tdprefract 0 1 1 1
\tdpcamera
\t{
\t\tmap textures/tools/portal.png
\t\tblendFunc GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA
\t}
}

textures/tools/sky
{
\tqer_editorimage textures/tools/sky.png
\tsurfaceparm noimpact
\tsurfaceparm nolightmap
\tsurfaceparm sky
\tq3map_sun 0.6 0.5 0.5 60 220 45
\tq3map_surfacelight 40
\tskyparms - 512 -
}
'''


def tool_textures(game):
    out = os.path.join(game, 'textures', 'tools')
    os.makedirs(out, exist_ok=True)
    cols = {'clip': (160, 40, 160), 'nodraw': (200, 200, 60), 'caulk': (120, 120, 140), 'trigger': (200, 120, 0),
            'hint': (220, 40, 40), 'water': (40, 80, 200), 'sky': (60, 20, 30),
            'origin': (40, 200, 200), 'link': (60, 200, 60), 'portal': (0, 0, 0), 'wallclip': (200, 40, 120),
            'mist': (190, 210, 235)}
    for n, c in cols.items():
        # (the mist's volumes half clear)
        a = 110 if n == 'mist' else 255
        img = Image.new('RGBA', (64, 64), c + (a,))
        for y in range(64):
            for x in range(64):
                if (x // 8 + y // 8) % 2:
                    img.putpixel((x, y), tuple(v // 2 for v in c) + (a,))
        if n == 'portal':
            # DP draws the camera view as a background pass and blends the texture over it: keep it clear
            img = Image.new('RGBA', (64, 64), (0, 0, 0, 0))
        img.save(os.path.join(out, n + '.png'))
    with open(os.path.join(game, 'scripts', 'blood_tools.shader'), 'w') as f:
        f.write(TOOL_SHADERS)


def export_tiledata(tiles, game):
    surf = ba.load_dat('surface.dat', 8192, 'b') or []
    shade = ba.load_dat('SHADE.DAT', 8192, 'b') or []
    vox = ba.load_dat('voxel.dat', 4096, 'h') or []
    path = os.path.join(game, 'data', 'tiles.txt')
    with open(path, 'w') as f:
        f.write('// tile w h xofs yofs anim animtype speed view surface shade voxel\n')
        for num, t in sorted(tiles.items()):
            if t.w <= 0 or t.h <= 0:
                continue
            f.write('%d %d %d %d %d %d %d %d %d %d %d %d\n' % (
                num, t.w, t.h, t.xofs, t.yofs, t.anim, t.animtype, t.speed, t.view,
                surf[num] if num < len(surf) else 0, shade[num] if num < len(shade) else 0,
                vox[num] if num < len(vox) else -1))


def seq_frame_flags(fr):
    return (fr.transparent | fr.transparent2 << 1 | fr.blockable << 2 | fr.hittable << 3 | fr.trigger << 4 |
            fr.smoke << 5 | fr.autoaim << 6 | fr.pushable << 7 | fr.playSound << 8 | fr.invisible << 9 |
            fr.xflip << 10 | fr.yflip << 11)


def export_seqs(seqs, game):
    with open(os.path.join(game, 'data', 'seq.txt'), 'w') as f:
        f.write('// seq <id> <nframes> <ticksPerFrame> <soundId> <flags>\n'
                '// then per frame: <tile> <flags> <xrepeat> <yrepeat> <shade> <pal>\n'
                '// frame flags: 1 trans 2 trans2 4 blockable 8 hittable 16 trigger 32 smoke 64 autoaim\n'
                '//              128 pushable 256 playSound 512 invisible 1024 xflip 2048 yflip\n')
        for sid, s in sorted(seqs.items()):
            f.write('seq %d %d %d %d %d\n' % (sid, s.nframes, s.ticks, s.sound, s.flags))
            for fr in s.frames:
                f.write('%d %d %d %d %d %d\n' % (fr.tile, seq_frame_flags(fr), fr.xrepeat, fr.yrepeat, fr.shade, fr.pal))


def export_qavs(qavs, game):
    with open(os.path.join(game, 'data', 'qav.txt'), 'w') as f:
        f.write('// qav <id> <name> <nframes> <ticksPerFrame> <duration> <x> <y>\n'
                '// frame <callback> <sound> <priority> <nlayers>, then layers: <picnum> <x> <y> <z> <stat> <shade> <pal> <angle>\n')
        for qid, q in sorted(qavs.items()):
            f.write('qav %d %s %d %d %d %d %d\n' % (qid, q.name, q.nframes, q.ticks, q.duration, q.x, q.y))
            for fr in q.frames:
                layers = [t for t in fr.tiles if t.picnum > 0]
                f.write('frame %d %d %d %d\n' % (fr.callback, fr.sound, fr.priority, len(layers)))
                for t in layers:
                    f.write('%d %d %d %d %d %d %d %d\n' % (t.picnum, t.x, t.y, t.z, t.stat, t.shade, t.pal, t.angle))


def wav_bytes(pcm, rate, loopstart):
    fmt = struct.pack('<HHIIHH', 1, 1, rate, rate, 1, 8)
    chunks = b'fmt ' + struct.pack('<I', len(fmt)) + fmt
    pad = bytes([128]) if len(pcm) & 1 else b''
    chunks += b'data' + struct.pack('<I', len(pcm)) + pcm + pad
    if loopstart >= 0:
        cue = struct.pack('<I', 1) + struct.pack('<II4sIII', 1, loopstart, b'data', 0, 0, loopstart)
        chunks += b'cue ' + struct.pack('<I', len(cue)) + cue
    return b'RIFF' + struct.pack('<I', 4 + len(chunks)) + b'WAVE' + chunks


def export_sounds(blood_dir, game):
    srff = RFF(os.path.join(blood_dir, 'sounds.rff'))
    sfx = ba.load_sfx(srff)
    out = os.path.join(game, 'sound', 'blood')
    os.makedirs(out, exist_ok=True)
    written = set()

    def write(fn, data, fmt, loop):
        key = (fn, fmt, loop)
        if key in written:
            return
        with open(os.path.join(out, fn + '.wav'), 'wb') as w:
            w.write(wav_bytes(data, ba.sound_rate(fmt), loop))
        written.add(key)

    with open(os.path.join(game, 'data', 'sfx.txt'), 'w') as f:
        f.write('// <id> <raw> <relvol> <pitch> <pitchrange> <format> <loopstart>\n')
        for sid, s in sorted(sfx.items()):
            e = srff.find(s.raw, 'RAW')
            if not e:
                continue
            name = s.raw.lower()
            data = srff.read(e)
            # one-shot version always exists (Blood plays loops one-shot when no channel is given)
            write(name, data, s.format, -1)
            fn = name
            if s.loopstart >= 0:
                fn = name + '_loop'
                write(fn, data, s.format, s.loopstart)
            f.write('%d %s %d %d %d %d %d\n' % (sid, fn, s.relvol, s.pitch, s.pitchrange, s.format, s.loopstart))
    print('sfx:', len(sfx), 'wavs:', len(written))
    misc = os.path.join(game, 'sound', 'misc')
    os.makedirs(misc, exist_ok=True)
    with open(os.path.join(misc, 'null.wav'), 'wb') as w:
        w.write(wav_bytes(bytes([128]) * 32, 11025, -1))
    # CD tracks from Refreshed Supply
    cd = os.path.join(game, 'sound', 'cdtracks')
    os.makedirs(cd, exist_ok=True)
    for n in range(2, 10):
        src = os.path.join(blood_dir, 'blood%02d.ogg' % n)
        if os.path.exists(src):
            shutil.copyfile(src, os.path.join(cd, 'track%03d.ogg' % n))
    # the level info (episodes, maps, each level's CD track, the menu's music): blood.ini
    ini = os.path.join(blood_dir, 'blood.ini')
    if os.path.exists(ini):
        os.makedirs(os.path.join(game, 'data'), exist_ok=True)
        shutil.copyfile(ini, os.path.join(game, 'data', 'blood.ini'))


def export_strings(blood_dir, game):
    """data/loc_english.txt: Refreshed Supply's English strings (BloodEX.kpf, a zip: $ble1 = "The Way of All Flesh",
    the add-ons' episodes and levels: $dw_ep1, $cp01...), the titles blood.ini and the add-ons' .ini name by code"""
    import zipfile
    kpf = os.path.join(blood_dir, 'BloodEX.kpf')
    if not os.path.exists(kpf):
        return 0
    with zipfile.ZipFile(kpf) as z:
        data = z.read('localization/loc_english.txt')
    os.makedirs(os.path.join(game, 'data'), exist_ok=True)
    with open(os.path.join(game, 'data', 'loc_english.txt'), 'wb') as f:
        f.write(data)
    return len(data)


def export_palette(pal, game):
    os.makedirs(os.path.join(game, 'gfx'), exist_ok=True)
    with open(os.path.join(game, 'gfx', 'palette.lmp'), 'wb') as f:
        for c in pal:
            f.write(bytes(c))


VIEW_PALETTES = ('WATER', 'BEAST', 'SEWER', 'INVULN1')     # Blood's screen palettes 1-4 (screen.cpp PAL[])


def export_view_palettes(rff, game):
    """gfx/water.lmp, beast.lmp, sewer.lmp, invuln1.lmp: the palettes Blood switches the whole screen to (underwater,
    in goo or another liquid, the death mask; Render/ViewPalette maps BLOOD.PAL's colours to them)"""
    os.makedirs(os.path.join(game, 'gfx'), exist_ok=True)
    for name in VIEW_PALETTES:
        e = rff.find(name, 'PAL')
        if e:
            with open(os.path.join(game, 'gfx', name.lower() + '.lmp'), 'wb') as f:
                f.write(rff.read(e)[:768])
    # the shade table (64 shades x 256 palette indices): the classic palette mode (Render/ClassicPalette) shades
    # through it as Build does
    e = rff.find('NORMAL', 'PLU')
    if e:
        with open(os.path.join(game, 'gfx', 'normal.plu'), 'wb') as f:
            f.write(rff.read(e)[:64 * 256])
    export_palookups(rff, game)


def export_palookups(rff, game):
    """gfx/palookup.bin: every palookup Blood loads (scrLoadPLUs: NORMAL, SATURATE, BEAST... P4, palette numbers 0..14),
    64 shades x 256 palette indices each, one after the other (a missing one as NORMAL): the game's world shader
    recolours a surface by its pal through them (Render/BuildPalette), as Build does"""
    from export_pals import PLU_NAMES
    normal = rff.read(rff.find('NORMAL', 'PLU'))[:64 * 256]
    with open(os.path.join(game, 'gfx', 'palookup.bin'), 'wb') as f:
        for name in PLU_NAMES:
            e = rff.find(name, 'PLU')
            d = rff.read(e)[:64 * 256] if e else normal
            f.write(d if len(d) == 64 * 256 else normal)


def export_music(blood_dir, game):
    """sound/midi/: Blood's MIDI songs and its FM timbre bank from SOUNDS.RFF (NAME.mid, gmtimbre.tmb; the game plays
    them on an emulated OPL3 as NBlood does); sound/sc55/: Refreshed Supply's recordings of the same songs on a Roland
    SC-55 (SC-55/NAME.ogg), when the install has them. Client/MusicPlayer picks CD tracks or these."""
    out = os.path.join(game, 'sound', 'midi')
    os.makedirs(out, exist_ok=True)
    snd = os.path.join(blood_dir, 'SOUNDS.RFF')
    if not os.path.exists(snd):
        snd = os.path.join(blood_dir, 'sounds.rff')
    n = 0
    if os.path.exists(snd):
        rff = RFF(snd)
        for e in rff.entries:
            t = e['type'].upper()
            if t in ('MID', 'TMB'):
                with open(os.path.join(out, e['name'].lower() + '.' + t.lower()), 'wb') as f:
                    f.write(rff.read(e))
                n += 1
    sc55 = os.path.join(blood_dir, 'SC-55')
    m = 0
    if os.path.isdir(sc55):
        dst = os.path.join(game, 'sound', 'sc55')
        os.makedirs(dst, exist_ok=True)
        for f in os.listdir(sc55):
            if f.lower().endswith('.ogg'):
                shutil.copyfile(os.path.join(sc55, f), os.path.join(dst, f.lower()))
                m += 1
    print('music: %d MIDI songs and timbres, %d SC-55 recordings' % (n, m))


def export_movies(blood_dir, game):
    """movie/: the intro logos and the episode cutscenes, both versions (Client/MoviePlayer). Original: the DOS
    Smacker videos (LOGO, GTI, CS1-6.SMK) with their sound (CSnn822M.WAV beside them; the logos' sound is
    SOUNDS.RFF's LOGO and GT, resources 300 and 301, as NBlood plays them), from DOS/BLOOD121 or the install's own
    movie folder. Remastered: Refreshed Supply's Theora videos (movie/csN.ogv) and their English subtitles."""
    out = os.path.join(game, 'movie')
    os.makedirs(os.path.join(out, 'remastered'), exist_ok=True)
    found = 0
    for src_dir in (os.path.join(blood_dir, 'DOS', 'BLOOD121'), os.path.join(blood_dir, 'movie'), blood_dir):
        if not os.path.isdir(src_dir):
            continue
        for f in os.listdir(src_dir):
            low = f.lower()
            if low.endswith('.smk') or (low.startswith('cs') and low.endswith('822m.wav')):
                dst = os.path.join(out, low)
                if not os.path.exists(dst):
                    shutil.copyfile(os.path.join(src_dir, f), dst)
                    found += 1
    snd = os.path.join(blood_dir, 'SOUNDS.RFF')
    if not os.path.exists(snd):
        snd = os.path.join(blood_dir, 'sounds.rff')
    if os.path.exists(snd):
        rff = RFF(snd)
        for rid, name in ((300, 'logo.wav'), (301, 'gti.wav')):
            e = rff.find(type='WAV', id=rid)
            if e:
                with open(os.path.join(out, name), 'wb') as f:
                    f.write(rff.read(e))
    remastered = os.path.join(blood_dir, 'movie')
    if os.path.isdir(remastered):
        for f in os.listdir(remastered):
            low = f.lower()
            if low.startswith('cs') and (low.endswith('.ogv') or (low.endswith('.srt') and '_' not in low)):
                dst = os.path.join(out, 'remastered', low)
                if not os.path.exists(dst):
                    shutil.copyfile(os.path.join(remastered, f), dst)
                    found += 1
    print('movies: %d files copied to %s' % (found, out))


def main():
    blood_dir = sys.argv[1] if len(sys.argv) > 1 else ba.BLOOD_DIR
    ba.BLOOD_DIR = blood_dir      # the helpers read the install from there
    game = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'game', 'blood3d')
    os.makedirs(os.path.join(game, 'data'), exist_ok=True)
    os.makedirs(os.path.join(game, 'scripts'), exist_ok=True)
    rff = RFF(os.path.join(blood_dir, 'BLOOD.RFF'))
    pal = ba.load_palette(rff)
    export_palette(pal, game)
    export_view_palettes(rff, game)
    tiles = ba.load_tiles(blood_dir)
    export_tiledata(tiles, game)
    export_seqs(ba.load_seqs(rff), game)
    export_qavs(ba.load_qavs(rff), game)
    export_sounds(blood_dir, game)
    export_strings(blood_dir, game)
    export_music(blood_dir, game)
    export_movies(blood_dir, game)
    tool_textures(game)
    export_textures(tiles, pal, game, rff)


if __name__ == '__main__':
    main()
