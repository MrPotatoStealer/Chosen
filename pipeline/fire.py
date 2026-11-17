"""Blood's procedural fire (fire.cpp): tile 2342 is not art but a cellular fire Blood redraws while it is on
screen (FireProcess, every 3 ticks of the 120 Hz clock). The simulation is run here and recorded as a loop of
frames (textures/blood[_masked]/2342_fireNN.png) that the shaders play with animMap.
"""
import os
import random

from PIL import Image

FIRE_TILE = 2342
FRAMES = 64
FREQ = 40.0          # FireProcess: lastUpdate + 2 < totalclock -> one frame every 3 ticks of 120 Hz
SIZE = 128
ROWS = 132           # CellularFrame(FrameBuffer, 128, 132); FrameBuffer holds 135 rows (3 seed rows)
DAMPING = 6          # gDamping


def frame_name(i):
    return '%04d_fire%02d' % (FIRE_TILE, i)


def simulate(clu, count, seed=2342):
    """count frames of the fire after FireInit's 100 warm-up frames, as lists of 128x128 palette indices"""
    rnd = random.Random(seed)
    cool = [max(int((i - DAMPING) / 4), 0) for i in range(1024)]                     # BuildCoolTable
    seeds = []
    for _ in range(16):                                                               # InitSeedBuffers
        row = []
        for _ in range(0, SIZE, 2):
            v = rnd.randrange(256)
            row += [v, v]
        seeds.append(row)
    buf = [0] * (SIZE * (ROWS + 3))

    def step():                                                                       # DoFireFrame
        s = seeds[rnd.randrange(16)]
        for i in range(3):
            buf[16896 + i * SIZE:16896 + (i + 1) * SIZE] = s
        for p1 in range(SIZE * ROWS):                                                 # CellularFrame
            p2 = p1 + SIZE
            sm = buf[p2 - 1] + buf[p2] + buf[p2 + 1] + buf[p2 + SIZE]
            if buf[p2 + SIZE] > 96:
                p2 += SIZE
                sm += buf[p2 - 1] + buf[p2] + buf[p2 + 1] + buf[p2 + SIZE]
                sm >>= 1
            buf[p1] = cool[sm]
        # the tile is column-major: source byte x * 128 + y goes to column y, row x, so the image is the
        # buffer's first 128 rows as they are (the seeds at the bottom, flames rising)
        return [clu[v] for v in buf[:SIZE * SIZE]]

    for _ in range(100):                                                              # FireInit
        step()
    return [step() for _ in range(count)]


def write_frames(rff, pal, game):
    """the fire frames as opaque (textures/blood) and masked (textures/blood_masked) images; kept if present"""
    solid = os.path.join(game, 'textures', 'blood')
    masked = os.path.join(game, 'textures', 'blood_masked')
    if all(os.path.exists(os.path.join(d, frame_name(i) + '.png')) for d in (solid, masked) for i in range(FRAMES)):
        return FRAMES
    e = rff.find('RFIRE', 'CLU')
    if not e:
        return 0
    clu = rff.read(e)
    for i, px in enumerate(simulate(clu, FRAMES)):
        rgb = bytes(c for v in px for c in pal[v])
        im = Image.frombytes('RGB', (SIZE, SIZE), rgb)
        im.save(os.path.join(solid, frame_name(i) + '.png'))
        im.convert('RGBA').save(os.path.join(masked, frame_name(i) + '.png'))
    return FRAMES


def anim_maps(num, folder):
    """animMap frames and frequency of a procedural tile in textures/<folder>, or None"""
    if num != FIRE_TILE:
        return None
    return ['textures/%s/%s.png' % (folder, frame_name(i)) for i in range(FRAMES)], FREQ
