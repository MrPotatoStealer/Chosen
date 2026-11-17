"""Blood .MAP reader (format per NBlood db.cpp dbLoadMap): sectors, walls, sprites and their x-structures.

usage as a script: python -I blood_map.py <blood_dir> <MAPNAME>   prints a summary of the map
"""
import os
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rff import RFF  # noqa: E402

kMapHeaderNew = 0x7474614D


def dbcrypt(data, key):
    out = bytearray(data)
    for i in range(len(out)):
        out[i] ^= key & 0xFF
        key += 1
    return bytes(out)


class BitReader:
    def __init__(self, data):
        self.data = data
        self.pos = 0

    def u(self, n):
        v = 0
        for i in range(n):
            byte = self.data[self.pos >> 3]
            v |= ((byte >> (self.pos & 7)) & 1) << i
            self.pos += 1
        return v

    def s(self, n):
        v = self.u(n)
        if n < 32 and v & (1 << (n - 1)):
            v -= 1 << n
        elif n == 32 and v & 0x80000000:
            v -= 1 << 32
        return v


class Obj:
    def __init__(self, **kw):
        self.__dict__.update(kw)

    def __repr__(self):
        return repr(self.__dict__)


# sectortype: wallptr, wallnum, ceilingz, floorz, ceilingstat, floorstat, ceilingpicnum, ceilingheinum,
# ceilingshade, ceilingpal, ceilingxpanning, ceilingypanning, floorpicnum, floorheinum, floorshade, floorpal,
# floorxpanning, floorypanning, visibility, fogpal, type, hitag, extra
SECTOR_FMT = '<hhiiHHhhbBBBhhbBBBBBhhh'
SECTOR_KEYS = ('wallptr wallnum ceilingz floorz ceilingstat floorstat ceilingpicnum ceilingheinum ceilingshade '
               'ceilingpal ceilingxpanning ceilingypanning floorpicnum floorheinum floorshade floorpal floorxpanning '
               'floorypanning visibility fogpal type hitag extra').split()
WALL_FMT = '<iihhhHhhbBBBBBhhh'
WALL_KEYS = ('x y point2 nextwall nextsector cstat picnum overpicnum shade pal xrepeat yrepeat xpanning ypanning '
             'type hitag extra').split()
SPRITE_FMT = '<iiiHhbBBBBBbbhhhhhhhhhh'
SPRITE_KEYS = ('x y z cstat picnum shade pal clipdist blend xrepeat yrepeat xoffset yoffset sectnum statnum ang '
               'owner index yvel inittype type flags extra').split()

XSECTOR_FIELDS = [
    ('reference', 's14'), ('state', 1), ('busy', 17), ('data', 16), ('txID', 10), ('busyWaveA', 3),
    ('busyWaveB', 3), ('rxID', 10), ('command', 8), ('triggerOn', 1), ('triggerOff', 1), ('busyTimeA', 12),
    ('waitTimeA', 12), ('restState', 1), ('interruptable', 1), ('amplitude', 's8'), ('freq', 8),
    ('reTriggerA', 1), ('reTriggerB', 1), ('phase', 8), ('wave', 4), ('shadeAlways', 1), ('shadeFloor', 1),
    ('shadeCeiling', 1), ('shadeWalls', 1), ('shade', 's8'), ('panAlways', 1), ('panFloor', 1),
    ('panCeiling', 1), ('Drag', 1), ('Underwater', 1), ('Depth', 3), ('panVel', 8), ('panAngle', 11),
    ('unused1', 1), ('decoupled', 1), ('triggerOnce', 1), ('isTriggered', 1), ('Key', 3), ('Push', 1),
    ('Vector', 1), ('Reserved', 1), ('Enter', 1), ('Exit', 1), ('Wallpush', 1), ('color', 1), ('unused2', 1),
    ('busyTimeB', 12), ('waitTimeB', 12), ('stopOn', 1), ('stopOff', 1), ('ceilpal', 4), ('offCeilZ', 's32'),
    ('onCeilZ', 's32'), ('offFloorZ', 's32'), ('onFloorZ', 's32'), ('marker0', 16), ('marker1', 16),
    ('Crush', 1), ('ceilXPanFrac', 8), ('ceilYPanFrac', 8), ('floorXPanFrac', 8), ('damageType', 3),
    ('floorpal', 4), ('floorYPanFrac', 8), ('locked', 1), ('windVel', 10), ('windAng', 11), ('windAlways', 1),
    ('dudeLockout', 1), ('bobTheta', 11), ('bobZRange', 5), ('bobSpeed', 's12'), ('bobAlways', 1),
    ('bobFloor', 1), ('bobCeiling', 1), ('bobRotate', 1)]

XWALL_FIELDS = [
    ('reference', 's14'), ('state', 1), ('busy', 17), ('data', 's16'), ('txID', 10), ('unused1', 6),
    ('rxID', 10), ('command', 8), ('triggerOn', 1), ('triggerOff', 1), ('busyTime', 12), ('waitTime', 12),
    ('restState', 1), ('interruptable', 1), ('panAlways', 1), ('panXVel', 's8'), ('panYVel', 's8'),
    ('decoupled', 1), ('triggerOnce', 1), ('isTriggered', 1), ('key', 3), ('triggerPush', 1),
    ('triggerVector', 1), ('triggerTouch', 1), ('unused2', 2), ('xpanFrac', 8), ('ypanFrac', 8), ('locked', 1),
    ('dudeLockout', 1), ('unused3', 4), ('unused4', 32)]

XSPRITE_FIELDS = [
    ('reference', 's14'), ('state', 1), ('busy', 17), ('txID', 10), ('rxID', 10), ('command', 8),
    ('triggerOn', 1), ('triggerOff', 1), ('wave', 2), ('busyTime', 12), ('waitTime', 12), ('restState', 1),
    ('Interrutable', 1), ('unused1', 2), ('respawnPending', 2), ('unused2', 1), ('lT', 1), ('dropMsg', 8),
    ('Decoupled', 1), ('triggerOnce', 1), ('isTriggered', 1), ('key', 3), ('Push', 1), ('Vector', 1),
    ('Impact', 1), ('Pickup', 1), ('Touch', 1), ('Sight', 1), ('Proximity', 1), ('unused3', 2), ('lSkill', 5),
    ('lS', 1), ('lB', 1), ('lC', 1), ('DudeLockout', 1), ('data1', 's16'), ('data2', 's16'), ('data3', 's16'),
    ('goalAng', 11), ('dodgeDir', 's2'), ('locked', 1), ('medium', 2), ('respawn', 2), ('data4', 16),
    ('unused4', 6), ('lockMsg', 8), ('health', 12), ('dudeDeaf', 1), ('dudeAmbush', 1), ('dudeGuard', 1),
    ('dudeFlag4', 1), ('target', 's16'), ('targetX', 's32'), ('targetY', 's32'), ('targetZ', 's32'),
    ('burnTime', 16), ('burnSource', 's16'), ('height', 16), ('stateTimer', 16)]


def read_bits(data, fields):
    br = BitReader(data)
    o = {}
    for name, spec in fields:
        if isinstance(spec, str):
            o[name] = br.s(int(spec[1:]))
        else:
            o[name] = br.u(spec)
    return Obj(**o)


class BloodMap:
    def __init__(self, data):
        if data[:4] != b'BLM\x1a':
            raise ValueError('not a Blood map')
        version = struct.unpack_from('<h', data, 4)[0]
        v7 = (version & 0xff00) == 0x700
        pos = 6
        hdr = data[pos:pos + 37]
        pos += 37
        song = struct.unpack_from('<i', hdr, 22)[0]
        if song not in (0, kMapHeaderNew, 0x4D617474):
            hdr = dbcrypt(hdr, kMapHeaderNew)
        (self.startx, self.starty, self.startz, self.startang, self.startsect, self.pskybits, self.visibility,
         self.songid, self.parallaxtype, self.maprev, numsectors, numwalls, numsprites) = struct.unpack(
            '<iiihhhiibihhh', hdr)
        xsprite_size, xwall_size, xsector_size = 56, 24, 60
        if v7:
            h2 = dbcrypt(data[pos:pos + 128], numwalls)
            pos += 128
            xsprite_size, xwall_size, xsector_size = struct.unpack_from('<iii', h2, 64)
        nsky = 1 << self.pskybits
        sky = data[pos:pos + nsky * 2]
        pos += nsky * 2
        if v7:
            sky = dbcrypt(sky, nsky * 2)
        self.skyofs = list(struct.unpack('<%dh' % nsky, sky))
        self.sectors, self.walls, self.sprites = [], [], []
        for i in range(numsectors):
            raw = data[pos:pos + 40]
            pos += 40
            if v7:
                raw = dbcrypt(raw, self.maprev * 40)
            s = Obj(**dict(zip(SECTOR_KEYS, struct.unpack(SECTOR_FMT, raw))))
            s.xs = None
            if s.extra > 0:
                s.xs = read_bits(data[pos:pos + xsector_size], XSECTOR_FIELDS)
                pos += xsector_size
            self.sectors.append(s)
        for i in range(numwalls):
            raw = data[pos:pos + 32]
            pos += 32
            if v7:
                raw = dbcrypt(raw, (self.maprev * 40) | kMapHeaderNew)
            w = Obj(**dict(zip(WALL_KEYS, struct.unpack(WALL_FMT, raw))))
            w.xs = None
            if w.extra > 0:
                w.xs = read_bits(data[pos:pos + xwall_size], XWALL_FIELDS)
                pos += xwall_size
            self.walls.append(w)
        for i in range(numsprites):
            raw = data[pos:pos + 44]
            pos += 44
            if v7:
                raw = dbcrypt(raw, (self.maprev * 44) | kMapHeaderNew)
            sp = Obj(**dict(zip(SPRITE_KEYS, struct.unpack(SPRITE_FMT, raw))))
            sp.index = i
            sp.xs = None
            if sp.extra > 0:
                sp.xs = read_bits(data[pos:pos + xsprite_size], XSPRITE_FIELDS)
                pos += xsprite_size
            self.sprites.append(sp)
        crc = struct.unpack_from('<I', data, pos)[0]
        if zlib.crc32(data[:pos]) != crc:
            raise ValueError('map CRC mismatch')
        for i, s in enumerate(self.sectors):
            s.index = i
        for i, w in enumerate(self.walls):
            w.index = i
            w.sector = -1
        for s in self.sectors:
            for w in range(s.wallptr, s.wallptr + s.wallnum):
                self.walls[w].sector = s.index
        self.propagate_markers()

    def propagate_markers(self):
        """PropagateMarkerReferences (NBlood's dbLoadMap): a sector's markers are the marker sprites it owns, whatever
        its xsector stores (MARROW's MRW2M3: slide 1112's marker0 named sprite 1728, another sector's warp marker,
        one past its own: its walls slid 500 units across the level); a marker sprite owned by no sector with an
        xsector, or of another type, is deleted (statnum 1024)"""
        self.marker_fixes = 0
        for sp in self.sprites:
            if sp.statnum != 10:
                continue
            o = sp.owner
            x = self.sectors[o].xs if 0 <= o < len(self.sectors) else None
            if x is None or sp.type not in (3, 4, 5, 8):
                sp.statnum = 1024
                continue
            key = 'marker1' if sp.type == 4 else 'marker0'
            if getattr(x, key) != sp.index:
                self.marker_fixes += 1
                setattr(x, key, sp.index)


def load(blood_dir, name):
    """Load MAPNAME (without extension) from PATCH01.RFF if patched there, else BLOOD.RFF."""
    for fn in ('PATCH01.RFF', 'BLOOD.RFF'):
        p = os.path.join(blood_dir, fn)
        if not os.path.exists(p):
            continue
        r = RFF(p)
        for e in r.entries:
            if e['type'].upper() == 'MAP' and e['name'].upper() == name.upper():
                return BloodMap(r.read(e))
    raise FileNotFoundError(name + '.MAP')


def main():
    m = load(sys.argv[1], sys.argv[2])
    from collections import Counter
    print('sectors', len(m.sectors), 'walls', len(m.walls), 'sprites', len(m.sprites), 'vis', m.visibility,
          'song', m.songid, 'sky', m.skyofs, 'start', m.startx, m.starty, m.startz, m.startang)
    print('sector types', Counter(s.type for s in m.sectors))
    print('xsectors', sum(1 for s in m.sectors if s.xs))
    print('sloped', sum(1 for s in m.sectors if (s.floorstat | s.ceilingstat) & 2))
    print('parallax ceilings', Counter(s.ceilingpicnum for s in m.sectors if s.ceilingstat & 1))
    print('parallax floors', Counter(s.floorpicnum for s in m.sectors if s.floorstat & 1))
    print('wall types', Counter(w.type for w in m.walls), 'xwalls', sum(1 for w in m.walls if w.xs))
    print('masked walls', sum(1 for w in m.walls if w.cstat & 16), 'one-way', sum(1 for w in m.walls if w.cstat & 32))
    print('sprite types', sorted(Counter(sp.type for sp in m.sprites).items()))
    print('sprite align', Counter(sp.cstat & 0x30 for sp in m.sprites))
    xs = Counter((s.type, s.xs.busyTimeA if s.xs else None) for s in m.sectors if s.type)
    print('sector type/busy', xs)
    zr = [s.floorz for s in m.sectors] + [s.ceilingz for s in m.sectors]
    xr = [w.x for w in m.walls]
    yr = [w.y for w in m.walls]
    print('bounds x', min(xr), max(xr), 'y', min(yr), max(yr), 'z', min(zr), max(zr))


if __name__ == '__main__':
    main()
