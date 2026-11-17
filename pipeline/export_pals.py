"""Export palette-lookup (PLU) variants of sprite tiles.

usage: python -I export_pals.py [blood_dir] [game_dir]

Blood recolors sprites with palookups (pal 3 = TOMMY robes, 11-14 = player team colors, ...).
DarkPlaces cannot remap palettes at draw time, so the (tile, pal) pairs the game can show are
exported as textures/blood_masked/NNNN_pP.png and listed in data/palvariants.txt for the client.

Pairs: every SEQ frame drawn with a non-zero pal (plus its rotations and tile animation frames),
and all player animation tiles with the four team palettes.
"""
import os
import sys

from PIL import Image

import blood_assets as ba
from rff import RFF

PLU_NAMES = ['NORMAL', 'SATURATE', 'BEAST', 'TOMMY', 'SPIDER3', 'GRAY', 'GRAYISH', 'SPIDER1', 'SPIDER2',
             'FLAME', 'COLD', 'P1', 'P2', 'P3', 'P4']
PLAYER_SEQ_FIRST = 4096    # dudeInfo[kDudePlayer1].seqStartID ... +20 animations
PLAYER_SEQ_COUNT = 20


def expand_tile(tiles, t):
    """Base tile -> all tiles drawn for it (view rotations x tile animation frames)."""
    out = set()
    tile = tiles.get(t)
    if not tile:
        return out
    views = {1: 5, 2: 8, 3: 2}.get(tile.view, 1)
    pic = t
    for _ in range(views):
        cur = tiles.get(pic)
        if not cur:
            break
        for a in range(cur.anim + 1):
            if pic + a in tiles:
                out.add(pic + a)
        pic += cur.anim + 1
    return out


def read_seq_frames(game):
    seqs = {}
    cur = None
    with open(os.path.join(game, 'data', 'seq.txt')) as f:
        for line in f:
            if line.startswith('//') or not line.strip():
                continue
            v = line.split()
            if v[0] == 'seq':
                cur = int(v[1])
                seqs[cur] = []
            else:
                seqs[cur].append((int(v[0]), int(v[5])))
    return seqs


def main():
    blood_dir = sys.argv[1] if len(sys.argv) > 1 else ba.BLOOD_DIR
    ba.BLOOD_DIR = blood_dir      # the helpers read the install from there
    game = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'game', 'blood3d')
    rff = RFF(os.path.join(blood_dir, 'BLOOD.RFF'))
    pal = ba.load_palette(rff)
    plus = {}
    for i, name in enumerate(PLU_NAMES):
        d = ba.load_plu(rff, name)
        if d:
            plus[i] = d[:256]       # shade 0 row
    tiles = ba.load_tiles(blood_dir)
    seqs = read_seq_frames(game)
    pairs = set()
    for sid, frames in seqs.items():
        for t, p in frames:
            if p > 0:
                for x in expand_tile(tiles, t):
                    pairs.add((x, p))
    for sid in range(PLAYER_SEQ_FIRST, PLAYER_SEQ_FIRST + PLAYER_SEQ_COUNT):
        for t, _ in seqs.get(sid, []):
            for x in expand_tile(tiles, t):
                for p in (11, 12, 13, 14):
                    pairs.add((x, p))
    outdir = os.path.join(game, 'textures', 'blood_masked')
    n = 0
    with open(os.path.join(game, 'data', 'palvariants.txt'), 'w') as lst:
        for t, p in sorted(pairs):
            if p not in plus:
                continue
            tile = tiles[t]
            if tile.w <= 0 or tile.h <= 0:
                continue
            lut = plus[p]
            rgba = bytearray()
            for i in tile.rows():
                if i == 255:
                    rgba += bytes(4)
                else:
                    r, g, b = pal[lut[i]]
                    rgba += bytes((r, g, b, 255))
            Image.frombytes('RGBA', (tile.w, tile.h), bytes(rgba)).save(os.path.join(outdir, '%04d_p%d.png' % (t, p)))
            lst.write('%d %d\n' % (t, p))
            n += 1
    print('pal variants', n)


if __name__ == '__main__':
    main()
