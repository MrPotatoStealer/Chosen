"""Loaders for Blood (1997) data formats: ART tiles, palettes, SEQ, QAV, SFX.

Formats follow NBlood (source/blood/src, source/build/src).
"""
import glob
import os
import struct

import blood_install
from rff import RFF

# the install (BLOOD, else found as ChosenLoader finds it: blood_install.py); an entry point given one sets it
BLOOD_DIR = blood_install.find_or_missing()

# an add-on being exported or converted (pipeline/addons.py activate): its folder in the install, whose tilesNNN.art
# replace the base game's files of the same name whole (NBlood's -game_dir: the folder is searched first), its data
# folder (game/blood3d/addons/<id>: the images of its tiles), and the tiles its art files hold
ADDON_DIR = None
ADDON_GAME = None
ADDON_TILES = set()


def game_for(tile, game):
    """the data folder a tile's images belong in: the add-on's for a tile its art replaces, else the base game's"""
    return ADDON_GAME if ADDON_GAME and tile in ADDON_TILES else game


# ---------------------------------------------------------------- palette
def load_palette(rff):
    raw = rff.read(rff.find('BLOOD', 'PAL'))
    pal = []
    for i in range(256):
        r, g, b = raw[i * 3:i * 3 + 3]
        pal.append((r, g, b))  # Blood stores 8-bit components
    return pal


def load_plu(rff, name):
    """64 shade levels x 256 entries."""
    e = rff.find(name, 'PLU')
    return rff.read(e) if e else None


# ---------------------------------------------------------------- ART
class Tile:
    __slots__ = ('num', 'w', 'h', 'anim', 'xofs', 'yofs', 'animtype', 'speed', 'view', 'pixels')

    def __init__(self, num, w, h, picanm, pixels):
        self.num = num
        self.w = w
        self.h = h
        self.anim = picanm & 63
        self.animtype = (picanm >> 6) & 3
        x = (picanm >> 8) & 255
        y = (picanm >> 16) & 255
        self.xofs = x - 256 if x > 127 else x
        self.yofs = y - 256 if y > 127 else y
        self.speed = (picanm >> 24) & 15
        self.view = (picanm >> 28) & 7  # Blood sprite view type (picanm.extra & 7)
        self.pixels = pixels  # column-major indices, len w*h

    def rows(self):
        """Row-major list of palette indices."""
        w, h, p = self.w, self.h, self.pixels
        return [p[x * h + y] for y in range(h) for x in range(w)]


def art_files(blood_dir):
    """the art files Blood loads: the install's tilesNNN.art, each replaced whole by the active add-on's of that name"""
    files = {os.path.basename(p).lower(): p for p in glob.glob(os.path.join(blood_dir, 'tiles*.art'))}
    if ADDON_DIR:
        files.update({os.path.basename(p).lower(): p for p in glob.glob(os.path.join(ADDON_DIR, 'tiles*.art'))})
    return [files[k] for k in sorted(files)]


def load_tiles(blood_dir=None):
    tiles = {}
    blood_dir = blood_dir or BLOOD_DIR
    for path in art_files(blood_dir):
        with open(path, 'rb') as f:
            d = f.read()
        ver, _, start, end = struct.unpack_from('<iiii', d, 0)
        n = end - start + 1
        sx = struct.unpack_from('<%dh' % n, d, 16)
        sy = struct.unpack_from('<%dh' % n, d, 16 + n * 2)
        pa = struct.unpack_from('<%dI' % n, d, 16 + n * 4)
        off = 16 + n * 8
        for i in range(n):
            w, h = sx[i], sy[i]
            size = w * h if w > 0 and h > 0 else 0
            tiles[start + i] = Tile(start + i, w, h, pa[i], d[off:off + size])
            off += size
    return tiles


def load_dat(name, count, fmt='b', blood_dir=None):
    # the install the entry point was given (ba.BLOOD_DIR, set by its main), not the default path
    p = os.path.join(blood_dir or BLOOD_DIR, name)
    if not os.path.exists(p):
        return None
    with open(p, 'rb') as f:
        d = f.read()
    sz = struct.calcsize(fmt)
    count = min(count, len(d) // sz)
    return list(struct.unpack_from('<%d%s' % (count, fmt), d, 0))


# ---------------------------------------------------------------- SEQ
class SeqFrame:
    def __init__(self, a, b):
        self.tile = (a & 0xFFF) | (((b >> 25) & 0xF) << 12)
        self.transparent = (a >> 12) & 1
        self.transparent2 = (a >> 13) & 1
        self.blockable = (a >> 14) & 1
        self.hittable = (a >> 15) & 1
        self.xrepeat = (a >> 16) & 0xFF
        self.yrepeat = (a >> 24) & 0xFF
        s = b & 0xFF
        self.shade = s - 256 if s > 127 else s
        self.pal = (b >> 8) & 0x1F
        self.trigger = (b >> 13) & 1
        self.smoke = (b >> 14) & 1
        self.autoaim = (b >> 15) & 1
        self.pushable = (b >> 16) & 1
        self.playSound = (b >> 17) & 1
        self.invisible = (b >> 18) & 1
        self.xflip = (b >> 19) & 1
        self.yflip = (b >> 20) & 1


class Seq:
    def __init__(self, sid, data):
        self.id = sid
        sig, self.version, self.nframes, self.ticks, self.sound, self.flags = struct.unpack_from('<4shhhhi', data, 0)
        if sig not in (b'SEQ\x1a',):
            raise ValueError('bad SEQ %d' % sid)
        self.frames = []
        for i in range(self.nframes):
            a, b = struct.unpack_from('<II', data, 16 + i * 8)
            self.frames.append(SeqFrame(a, b))


def load_seqs(rff):
    out = {}
    for e in rff.by_type('SEQ'):
        try:
            out[e['id']] = Seq(e['id'], rff.read(e))
        except (ValueError, struct.error):
            pass
    return out


# ---------------------------------------------------------------- QAV
class QavTile:
    __slots__ = ('picnum', 'x', 'y', 'z', 'stat', 'shade', 'pal', 'angle')


class QavFrame:
    __slots__ = ('callback', 'sound', 'priority', 'tiles')


class Qav:
    def __init__(self, qid, name, data):
        self.id = qid
        self.name = name
        self.nframes, self.ticks, self.duration, self.x, self.y = struct.unpack_from('<iiiii', data, 8)
        self.frames = []
        for i in range(self.nframes):
            base = 0x24 + i * 204
            fr = QavFrame()
            fr.callback, fr.sound, fr.priority = struct.unpack_from('<iiB', data, base)
            fr.tiles = []
            for t in range(8):
                tb = base + 12 + t * 24
                qt = QavTile()
                qt.picnum, qt.x, qt.y, qt.z, qt.stat, qt.shade, qt.pal, qt.angle = struct.unpack_from('<iiiiibBH', data, tb)
                fr.tiles.append(qt)
            self.frames.append(fr)


def load_qavs(rff):
    out = {}
    for e in rff.by_type('QAV'):
        try:
            out[e['id']] = Qav(e['id'], e['name'], rff.read(e))
        except struct.error:
            pass
    return out


# ---------------------------------------------------------------- SFX
class Sfx:
    def __init__(self, sid, name, data):
        self.id = sid
        self.name = name
        self.relvol, self.pitch, self.pitchrange, self.format, self.loopstart = struct.unpack_from('<iiiii', data, 0)
        self.raw = data[20:29].split(b'\0')[0].decode('latin1')


SOUND_RATES = [11025, 11025, 11025, 11025, 11025, 22050, 22050, 22050, 22050, 44100, 44100, 44100, 44100]


def sound_rate(fmt):
    return SOUND_RATES[fmt] if 0 <= fmt < 13 else 11025


def load_sfx(rff):
    out = {}
    for e in rff.by_type('SFX'):
        try:
            out[e['id']] = Sfx(e['id'], e['name'], rff.read(e))
        except struct.error:
            pass
    return out
