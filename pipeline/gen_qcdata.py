"""Generate QuakeC data tables from the exported Blood data files.

usage: python -I gen_qcdata.py [game_dir] [qc_dir]

Writes game/blood3d/data/rt_tiles.txt, rt_seq.txt, rt_sfx.txt, rt_qav.txt (runtime tables) and qc/server/gen_qav.qc.
Packing (all values are exact integers < 2^24 so they survive float storage):
  tile_size[t]  = w + h*1024
  tile_ofs[t]   = (xofs+128) + (yofs+128)*256 + anim*65536
  tile_misc[t]  = view + surface*8 + (shade+128)*256... (shade stored separately below)
  seq_info[s]   = nframes + ticksPerFrame*256 + flags*65536
  seq_first[s]  = index of first frame in frame arrays
  seq_sound[s]  = sound id
  frm_a[f]      = tile + (flags & 1023)*8192
  frm_b[f]      = xrepeat + yrepeat*256 + ((flags>>10)&3)*65536      (xflip=1, yflip=2)
  frm_c[f]      = (shade+128) + pal*256
"""
import os
import sys


def read_lines(path):
    with open(path) as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith('//'):
                yield line.split()


def fmt_array(decl, name, values, per_line=16):
    out = ['%s %s[%d] = {' % (decl, name, len(values))]
    for i in range(0, len(values), per_line):
        out.append('\t' + ', '.join(str(v) for v in values[i:i + per_line]) + ',')
    out[-1] = out[-1].rstrip(',')
    out.append('};')
    return '\n'.join(out) + '\n'


def bsearch_func(name, ids_array, n):
    """float name(float id) -> slot or -1, binary search over sorted ids array."""
    return '''float(float id) %s =
{
\tlocal float lo, hi, mid, v;
\tlo = 0; hi = %d;
\twhile (lo <= hi)
\t{
\t\tmid = floor((lo + hi) / 2);
\t\tv = %s[mid];
\t\tif (v == id) return mid;
\t\tif (v < id) lo = mid + 1; else hi = mid - 1;
\t}
\treturn -1;
};
''' % (name, n - 1, ids_array)


def gen_tiles(game, out):
    rows = list(read_lines(os.path.join(game, 'data', 'tiles.txt')))
    maxt = max(int(r[0]) for r in rows)
    size = [0] * (maxt + 1)
    ofs = [128 + 128 * 256] * (maxt + 1)
    misc = [0] * (maxt + 1)
    for r in rows:
        t, w, h, xo, yo, anim, animtype, speed, view, surf, shade, vox = map(int, r)
        size[t] = w + h * 1024
        ofs[t] = (xo + 128) + (yo + 128) * 256 + anim * 65536
        misc[t] = view + (surf & 31) * 8 + ((shade + 128) & 255) * 256 + (1 if vox >= 0 else 0) * 65536 \
            + (animtype & 3) * 131072 + (speed & 15) * 524288
    with open(os.path.join(game, 'data', 'rt_tiles.txt'), 'w') as f:
        for t in range(maxt + 1):
            if size[t]:
                f.write('%d %d %d %d\n' % (t, size[t], ofs[t], misc[t]))
    print('tiles', maxt + 1)


def gen_seq(game, out):
    seqs = []
    cur = None
    for r in read_lines(os.path.join(game, 'data', 'seq.txt')):
        if r[0] == 'seq':
            cur = dict(id=int(r[1]), n=int(r[2]), ticks=int(r[3]), sound=int(r[4]), flags=int(r[5]), frames=[])
            seqs.append(cur)
        else:
            cur['frames'].append(tuple(map(int, r)))
    seqs.sort(key=lambda s: s['id'])
    ids, info, first, sound = [], [], [], []
    fa, fb, fc = [], [], []
    for s in seqs:
        ids.append(s['id'])
        info.append(len(s['frames']) + (s['ticks'] & 255) * 256 + (s['flags'] & 255) * 65536)
        first.append(len(fa))
        sound.append(s['sound'])
        for tile, flags, xr, yr, shade, pal in s['frames']:
            fa.append(tile + (flags & 1023) * 8192)
            fb.append(xr + yr * 256 + ((flags >> 10) & 3) * 65536)
            fc.append(((shade + 128) & 255) + pal * 256)
    with open(os.path.join(game, 'data', 'rt_seq.txt'), 'w') as f:
        for i in range(len(ids)):
            f.write('S %d %d %d %d\n' % (ids[i], info[i], first[i], sound[i]))
        for i in range(len(fa)):
            f.write('F %d %d %d %d\n' % (i, fa[i], fb[i], fc[i]))
    print('seqs', len(ids), 'frames', len(fa))


def gen_sfx(game, out):
    rows = sorted(read_lines(os.path.join(game, 'data', 'sfx.txt')), key=lambda r: int(r[0]))
    ids, names, vol, pitch, prange, loop = [], [], [], [], [], []
    for r in rows:
        sid, raw, relvol, p, pr, fmt, ls = r[0], r[1], r[2], r[3], r[4], r[5], r[6]
        ids.append(int(sid))
        names.append('"blood/%s.wav"' % raw)
        vol.append(int(relvol))
        pitch.append(int(p))
        prange.append(int(pr))
        loop.append(1 if int(ls) >= 0 else 0)
    with open(os.path.join(game, 'data', 'rt_sfx.txt'), 'w') as f:
        for i in range(len(ids)):
            f.write('%d %s %d %d %d %d\n' % (ids[i], names[i].strip('"'), vol[i], pitch[i], prange[i], loop[i]))
    print('sfx', len(ids))


def gen_qav_server(game, out):
    """Server-side QAV timing data: per qav (id-indexed) and per frame callback/sound."""
    qavs = {}
    cur = None
    for r in read_lines(os.path.join(game, 'data', 'qav.txt')):
        if r[0] == 'qav':
            cur = dict(id=int(r[1]), name=r[2], n=int(r[3]), ticks=int(r[4]), dur=int(r[5]), frames=[])
            qavs[cur['id']] = cur
        elif r[0] == 'frame':
            cur['frames'].append((int(r[1]), int(r[2]), int(r[3])))
    maxid = max(qavs)
    ticks = [0] * (maxid + 1)
    dur = [0] * (maxid + 1)
    nfr = [0] * (maxid + 1)
    first = [0] * (maxid + 1)
    fcb, fsnd = [], []
    names = []
    for qid in range(maxid + 1):
        q = qavs.get(qid)
        if not q:
            continue
        ticks[qid] = q['ticks']
        dur[qid] = q['dur']
        nfr[qid] = len(q['frames'])
        first[qid] = len(fcb)
        for cb, snd, prio in q['frames']:
            # Blood calls a frame's callback only when nCallbackId > 0 (-1 = none); store 0 for none
            fcb.append(max(cb, 0) + 256 * (prio & 255))
            fsnd.append(snd)
        names.append((qid, q['name']))
    with open(os.path.join(out, 'gen_qav.qc'), 'w') as f:
        f.write('// generated by pipeline/gen_qcdata.py -- do not edit\n')
        f.write('const float kNumQavIds = %d;\n' % (maxid + 1))
        f.write('float(string name) qavIdByName =\n{\n')
        for qid, name in names:
            f.write('\tif (name == "%s") return %d;\n' % (name.upper(), qid))
        f.write('\treturn -1;\n};\n')
    with open(os.path.join(game, 'data', 'rt_qav.txt'), 'w') as f:
        for qid in range(maxid + 1):
            if qid in qavs:
                f.write('Q %d %d %d %d %d\n' % (qid, ticks[qid], dur[qid], nfr[qid], first[qid]))
        for i in range(len(fcb)):
            f.write('F %d %d %d\n' % (i, fcb[i], fsnd[i]))
    print('qavs', len(qavs), 'frames', len(fcb))


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    game = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, 'game', 'blood3d')
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(root, 'Deprecated', 'darkplaces', 'qc', 'shared')
    os.makedirs(out, exist_ok=True)
    gen_tiles(game, out)
    gen_seq(game, out)
    gen_sfx(game, out)
    gen_qav_server(game, os.path.join(os.path.dirname(out), 'server'))


if __name__ == '__main__':
    main()
