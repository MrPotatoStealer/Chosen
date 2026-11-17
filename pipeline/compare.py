"""Compare a level with NBlood, view by view, where Build's tricks are hard to draw.

usage: python compare.py <MAP> [--max N] [--skip-nblood] [--skip-godot] [--only layer,stack,link,mover] [--newviews]

Picks cameras where the level is complicated rather than everywhere: rooms stacked so their heights overlap (from
both rooms, looking at the shared area), room-over-room links (from each side, looking at the linked floor or
ceiling) and moving sectors (doors, lifts, sliding and rotating parts, from the rooms beside them). Each camera stands
inside a sector at eye height with nothing between it and what it looks at in the map's plan. Both engines then draw
every view at 1280x720 in one run each, monsters off and no HUD (NBlood: third_party/nblood patched with -b3dviews,
pipeline/nblood_b3dview.patch; Godot: --views), all at the same game time (VIEW_SECONDS, the level held there), and
each pair gets a difference score: brightness normalised, so
that Godot's dynamic lights or a different shade only count a little and missing or extra geometry counts a lot.

Output in tools/compare/<map>/: views.txt and views.json (the cameras), nb/ and gd/ (the screenshots), pair/NNNN.jpg
(NBlood | Godot | difference) and index.html, all views worst first. The worst ones are printed. The Godot run's
settings (monsters, HUD, crosshair) are restored afterwards. A map compared before keeps its cameras (views.json) and
NBlood's shots of them; --newviews picks the cameras again from the map (and shoots NBlood again).
"""
import json
import math
import os
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image, ImageFilter
from shapely.geometry import LineString, Point, Polygon
from shapely.ops import unary_union

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import addons  # noqa: E402
import blood_map  # noqa: E402
import bugs  # noqa: E402

EYE = 54.0                 # eye height above the floor (Quake units; a standing player's camera)
PREFER = 192.0             # preferred distance from what a view looks at
MOVER_TYPES = (600, 602, 613, 614, 615, 616, 617)
LINK_TYPES = {7: 'up', 9: 'up', 11: 'up', 13: 'up', 6: 'low', 10: 'low', 12: 'low', 14: 'low'}
W, H = 1280, 720
OUT_ROOT = os.path.join(bugs.ROOT, 'tools', 'compare')
GODOT_EXEC = 'b3d_monsters 0; restart; b3d_hudsize 0; b3d_crosshair 0; ' + bugs.SHOT_EXEC
# the game time every view is taken at, in both engines: each runs the level that long and holds it there (NBlood's
# -b3dshot with -b3dviews, Godot's --viewtime), so timed movers (E1M2's train, lifts, doors) stand in the same place
VIEW_SECONDS = 3


class Level:
    """A level's sectors from its Blood .MAP: each one's footprint (Quake x, y), its floor and ceiling heights, the
    sectors stacked over one another (overlapping in plan, one above the other) and those sharing space (overlapping in
    plan and height)."""

    def __init__(self, name):
        self.name = name.lower()
        self.m = addons.load_map(bugs.BLOOD_DIR, name.upper())      # an add-on's too (Death Wish's DWE1M1)
        self.foot = [self.footprint(si) for si in range(len(self.m.sectors))]
        self.stacks, self.layerpairs = self.overlaps()

    def loops(self, si):
        s = self.m.sectors[si]
        seen, loops = set(), []
        for first in range(s.wallptr, s.wallptr + s.wallnum):
            if first in seen:
                continue
            loop, w = [], first
            while w not in seen and s.wallptr <= w < s.wallptr + s.wallnum:
                seen.add(w)
                wl = self.m.walls[w]
                loop.append((wl.x / 16.0, -wl.y / 16.0))
                w = wl.point2
            loops.append(loop)
        return loops

    def footprint(self, si):
        poly = None
        for r in self.loops(si):
            if len(r) < 3:
                continue
            p = Polygon(r).buffer(0)
            poly = p if poly is None else poly.symmetric_difference(p)
        return poly if poly is not None and not poly.is_empty else None

    def z(self, si, ceiling, x, y):
        """Build's getzsofslope at Quake (x, y), in Quake z"""
        s = self.m.sectors[si]
        z = s.ceilingz if ceiling else s.floorz
        stat = s.ceilingstat if ceiling else s.floorstat
        heinum = s.ceilingheinum if ceiling else s.floorheinum
        if stat & 2 and heinum:
            w0 = self.m.walls[s.wallptr]
            w1 = self.m.walls[w0.point2]
            dx, dy = w1.x - w0.x, w1.y - w0.y
            i = math.hypot(dx, dy)
            if i > 0:
                bx, by = x * 16, -y * 16
                z += heinum * ((dx * (by - w0.y) - dy * (bx - w0.x)) / 8.0) / i
        return -z / 256.0

    def eye(self, si, x, y):
        f, c = self.z(si, False, x, y), self.z(si, True, x, y)
        return min(f + EYE, c - 6) if c - f > 12 else (f + c) / 2

    def overlaps(self):
        stacks, layers = [], []
        n = len(self.foot)
        boxes = [f.bounds if f is not None else None for f in self.foot]
        for a in range(n):
            if boxes[a] is None:
                continue
            for b in range(a + 1, n):
                if boxes[b] is None or boxes[a][2] <= boxes[b][0] or boxes[b][2] <= boxes[a][0] \
                        or boxes[a][3] <= boxes[b][1] or boxes[b][3] <= boxes[a][1]:
                    continue
                ov = self.foot[a].intersection(self.foot[b])
                if ov.is_empty or ov.area < 4:
                    continue
                t = ov.representative_point()
                fa, ca = self.z(a, False, t.x, t.y), self.z(a, True, t.x, t.y)
                fb, cb = self.z(b, False, t.x, t.y), self.z(b, True, t.x, t.y)
                if ca <= fb + 0.5:
                    stacks.append((a, b))
                elif cb <= fa + 0.5:
                    stacks.append((b, a))
                else:
                    layers.append((a, b))
        return stacks, layers


def camera(region, target, clear, prefer=PREFER, step=16.0):
    """a point of region (12 units clear of its edges) at about prefer from target, the line to it inside clear"""
    if region is None or region.is_empty:
        return None
    inner = region.buffer(-12)
    if inner.is_empty:
        inner = region.buffer(-4)
        if inner.is_empty:
            return None
    x0, y0, x1, y1 = inner.bounds
    tx, ty = target
    zone = clear.buffer(0.5) if clear is not None else None
    best, best_s = None, None
    x = x0 + step / 2
    while x < x1:
        y = y0 + step / 2
        while y < y1:
            if inner.contains(Point(x, y)):
                d = math.hypot(x - tx, y - ty)
                if 32 <= d <= 640 and (zone is None or zone.contains(LineString([(x, y), (tx, ty)]))):
                    s = abs(d - prefer)
                    if best_s is None or s < best_s:
                        best, best_s = (x, y), s
            y += step
        x += step
    return best


def aim(cam, target):
    dx, dy, dz = target[0] - cam[0], target[1] - cam[1], target[2] - cam[2]
    yaw = math.degrees(math.atan2(dy, dx)) % 360
    pitch = -math.degrees(math.atan2(dz, math.hypot(dx, dy)))      # negative up, as the bug reports have it
    return yaw, max(-25.0, min(25.0, pitch))      # steeper, polymost's turn (gvrcorrection) and Godot's differ


def make_views(lv, only):
    views = []

    def standable(si):
        """a camera sector: not moving (the map stores it in a state that may not be the start's) and open"""
        s = lv.m.sectors[si]
        if s.type in MOVER_TYPES or lv.foot[si] is None:
            return False
        t = lv.foot[si].representative_point()
        return lv.z(si, True, t.x, t.y) - lv.z(si, False, t.x, t.y) >= 24

    def add(kind, desc, cam_xy, cam_sector, target):
        if cam_xy is None or not standable(cam_sector):
            return
        cz = lv.eye(cam_sector, *cam_xy)
        cam = (cam_xy[0], cam_xy[1], cz)
        yaw, pitch = aim(cam, target)
        for v in views:
            if math.hypot(v['cam'][0] - cam[0], v['cam'][1] - cam[1]) < 24 and abs(v['cam'][2] - cz) < 16 and \
                    abs((v['yaw'] - yaw + 180) % 360 - 180) < 25:
                return
        views.append({'kind': kind, 'desc': desc, 'cam': [round(c, 2) for c in cam], 'yaw': round(yaw, 2),
                      'pitch': round(pitch, 2), 'sector': cam_sector, 'target': [round(t, 2) for t in target]})

    if 'stack' in only:
        for lo, hi in lv.stacks:
            fl, fh = lv.foot[lo], lv.foot[hi]
            if fl is None or fh is None:
                continue
            ov = fl.intersection(fh)
            if ov.is_empty or ov.area < 4:
                continue
            t = ov.representative_point()
            band = (lv.z(hi, False, t.x, t.y) + lv.z(lo, True, t.x, t.y)) / 2
            outside = fl.difference(ov)
            add('stack', 'sector %d under %d, from the lower room' % (lo, hi),
                camera(outside if not outside.is_empty else fl, (t.x, t.y), fl), lo, (t.x, t.y, band))
            add('stack', 'sector %d under %d, from the upper room' % (lo, hi),
                camera(fh, (t.x, t.y), fh), hi, (t.x, t.y, lv.z(hi, False, t.x, t.y) + 8))
    if 'layer' in only:
        # rooms sharing space: each seen from itself across the shared area
        for a, b in lv.layerpairs:
            fa, fb = lv.foot[a], lv.foot[b]
            if fa is None or fb is None:
                continue
            ov = fa.intersection(fb)
            if ov.is_empty or ov.area < 4:
                continue
            t = ov.representative_point()
            for own in (a, b):
                add('layer', 'sectors %d and %d share space, from %d' % (a, b, own),
                    camera(lv.foot[own], (t.x, t.y), lv.foot[own]), own, (t.x, t.y, lv.eye(own, t.x, t.y)))
    if 'link' in only:
        pairs = {}
        for sp in lv.m.sprites:
            if sp.statnum >= 1024 or not sp.xs or sp.type not in LINK_TYPES:
                continue
            pairs.setdefault(sp.xs.data1, {})[LINK_TYPES[sp.type]] = sp
        for key, pr in sorted(pairs.items()):
            if 'up' not in pr or 'low' not in pr:
                continue
            for side, ceiling in (('up', False), ('low', True)):
                si = pr[side].sectnum
                f = lv.foot[si]
                if f is None:
                    continue
                t = f.representative_point()
                tz = lv.z(si, ceiling, t.x, t.y)
                add('link', 'link %d: sector %d %s' % (key, si, 'ceiling (looking up)' if ceiling else 'floor (looking down)'),
                    camera(f, (t.x, t.y), f, prefer=128), si, (t.x, t.y, tz))
    if 'mover' in only:
        for s in lv.m.sectors:
            if s.type not in MOVER_TYPES or not s.xs or lv.foot[s.index] is None:
                continue
            shared = {}
            for wi in range(s.wallptr, s.wallptr + s.wallnum):
                w = lv.m.walls[wi]
                if w.nextsector < 0 or lv.m.sectors[w.nextsector].type in MOVER_TYPES or lv.foot[w.nextsector] is None:
                    continue
                w2 = lv.m.walls[w.point2]
                shared[w.nextsector] = shared.get(w.nextsector, 0) + math.hypot(w2.x - w.x, w2.y - w.y)
            fm = lv.foot[s.index]
            t = fm.representative_point()
            tz = (lv.z(s.index, False, t.x, t.y) + lv.z(s.index, True, t.x, t.y)) / 2
            for n, _ in sorted(shared.items(), key=lambda kv: -kv[1])[:2]:
                add('mover', 'sector %d (type %d) from sector %d' % (s.index, s.type, n),
                    camera(lv.foot[n], (t.x, t.y), unary_union([lv.foot[n], fm])), n, (t.x, t.y, tz))
    return views


def run_nblood(lv, views_txt, out_dir, count):
    os.makedirs(bugs.NBLOOD_RUN, exist_ok=True)
    cfg = os.path.join(bugs.NBLOOD_RUN, 'nblood.cfg')
    with open(cfg, 'w', newline='') as f:
        f.write(bugs.NBLOOD_CFG)
    shots = os.path.join(bugs.NBLOOD_RUN, 'screenshots')
    if os.path.isdir(shots):
        for fn in os.listdir(shots):
            if fn.startswith('b3dv'):
                os.remove(os.path.join(shots, fn))
    subprocess.run([bugs.NBLOOD, '-usecwd', '-nosetup', '-quick'] + addons.nblood_map_args(bugs.BLOOD_DIR, lv.name) + ['-skill', '2',
                    '-nodudes', '1', '-b3dviews', views_txt, '-b3dshot', str(VIEW_SECONDS)], cwd=bugs.NBLOOD_RUN,
                   timeout=120 + count, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=bugs.NBLOOD_ENV)
    got = sorted(fn for fn in os.listdir(shots) if fn.startswith('b3dv')) if os.path.isdir(shots) else []
    os.makedirs(out_dir, exist_ok=True)
    for i, fn in enumerate(got):
        Image.open(os.path.join(shots, fn)).convert('RGB').resize((W, H)).save(os.path.join(out_dir, '%04d.png' % i))
    return len(got)


def run_godot(lv, views_txt, out_dir, extra=(), exec_extra=''):
    subprocess.run(['dotnet', 'build', os.path.join(bugs.GODOT_DIR, 'Chosen.csproj'), '-v', 'q'], check=True,
                   stdout=subprocess.DEVNULL)
    os.makedirs(out_dir, exist_ok=True)
    base = [bugs.GODOT, '--path', bugs.GODOT_DIR, '--windowed'] + bugs.OFFSCREEN + ['--audio-driver', 'Dummy', '--resolution', '%dx%d' % (W, H), '--']
    restore = bugs.restore_exec(bugs.SHOT_CVARS)      # the user's settings as found, put back afterwards
    subprocess.run(base + ['--map=' + lv.name, '--exec=' + GODOT_EXEC + exec_extra, '--viewtime=%g' % VIEW_SECONDS,
                           '--views=' + views_txt.replace(os.sep, '/'),
                           '--viewshots=' + out_dir.replace(os.sep, '/')] + list(extra), capture_output=True, timeout=600)
    # the settings the run changed (a run with --shot quits; one without would not)
    subprocess.run(base + ['--map=' + lv.name, '--exec=' + restore, '--wait=0.3',
                           '--shot=' + os.path.join(out_dir, 'restore.png').replace(os.sep, '/')], capture_output=True, timeout=300)
    return len([fn for fn in os.listdir(out_dir) if fn[:4].isdigit()])


def normalised(img):
    a = np.asarray(img.resize((320, 180), Image.BOX).filter(ImageFilter.GaussianBlur(1.5)), dtype=np.float32)
    lum = a.mean(axis=2, keepdims=True)
    return (a - lum.mean()) / (lum.std() + 8.0), lum[..., 0]


def edges(lum):
    gx = np.zeros_like(lum)
    gy = np.zeros_like(lum)
    gx[:, 1:-1] = lum[:, 2:] - lum[:, :-2]
    gy[1:-1, :] = lum[2:, :] - lum[:-2, :]
    e = np.hypot(gx, gy)
    return e / (e.mean() + 4.0)


def score(nb, gd):
    """difference of the two pictures with each one's brightness and contrast normalised (shading counts little,
    missing or extra things a lot): colour and edge differences, and the map of where they are"""
    a, la = normalised(nb)
    b, lb = normalised(gd)
    col = np.abs(a - b).mean(axis=2)
    edg = np.abs(edges(la) - edges(lb))
    diff = col + 0.5 * edg
    return float(diff.mean()), diff


def heat(diff):
    d = np.clip(diff / 3.0, 0, 1)
    rgb = np.stack([d, d * 0.3, 1 - d], axis=2) * 255 * np.minimum(1, d * 4)[..., None]
    return Image.fromarray(rgb.astype(np.uint8)).resize((W // 2, H // 2), Image.NEAREST)


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return
    name = args[0]
    maxn = int(args[args.index('--max') + 1]) if '--max' in args else 80
    only = args[args.index('--only') + 1].split(',') if '--only' in args else ['layer', 'stack', 'link', 'mover']
    lv = Level(name)
    out = os.path.join(OUT_ROOT, lv.name)
    nb_dir = os.path.join(out, 'nb')
    os.makedirs(out, exist_ok=True)
    # the cameras of an earlier run (NBlood's shots of them kept), else picked from the map
    old = os.path.join(out, 'views.json')
    fresh = '--newviews' in args or not os.path.exists(old)
    if fresh:
        views = make_views(lv, only)[:maxn]
    else:
        views = [{k: v[k] for k in ('kind', 'desc', 'cam', 'yaw', 'pitch', 'sector', 'target') if k in v}
                 for v in json.load(open(old))][:maxn]
    views_txt = os.path.join(out, 'views.txt')
    with open(views_txt, 'w') as f:
        for v in views:
            # the camera's sector too: where rooms stack or share space the position alone doesn't tell. Both engines
            # keep it only where the camera lies in it and otherwise look around it: at the view's moment a moving
            # sector can stand there (E1M2's train car over platform 255, view 17); without it NBlood's search from
            # nowhere can miss (E3M8's shaft 511, view 3)
            f.write('%s %s %s %s %s %d\n' % (v['cam'][0], v['cam'][1], v['cam'][2], v['yaw'], v['pitch'], v['sector']))
    print('%s: %d views (%s)' % (lv.name, len(views), ', '.join('%d %s' % (sum(v['kind'] == k for v in views), k) for k in only)))
    gd_dir, pair_dir = (os.path.join(out, d) for d in ('gd', 'pair'))
    if '--skip-nblood' not in args and (fresh or not os.path.isdir(nb_dir)):
        shutil.rmtree(nb_dir, ignore_errors=True)
        print('  NBlood: %d screenshots' % run_nblood(lv, views_txt, nb_dir, len(views)))
    if '--skip-godot' not in args:
        shutil.rmtree(gd_dir, ignore_errors=True)
        print('  Godot: %d screenshots' % run_godot(lv, views_txt, gd_dir))
    shutil.rmtree(pair_dir, ignore_errors=True)
    os.makedirs(pair_dir)
    for i, v in enumerate(views):
        nbp, gdp = os.path.join(nb_dir, '%04d.png' % i), os.path.join(gd_dir, '%04d.png' % i)
        if not (os.path.exists(nbp) and os.path.exists(gdp)):
            v['score'] = None
            continue
        nb, gd = Image.open(nbp).convert('RGB'), Image.open(gdp).convert('RGB').resize((W, H))
        v['score'], diff = score(nb, gd)
        sheet = Image.new('RGB', (W + W // 2, H // 2))
        sheet.paste(nb.resize((W // 2, H // 2)), (0, 0))
        sheet.paste(gd.resize((W // 2, H // 2)), (W // 2, 0))
        sheet.paste(heat(diff), (W, 0))
        sheet.save(os.path.join(pair_dir, '%04d.jpg' % i), quality=85)
        v['id'] = i
    with open(os.path.join(out, 'views.json'), 'w') as f:
        json.dump(views, f, indent=1)
    ranked = sorted([v for v in views if v.get('score') is not None], key=lambda v: -v['score'])
    rows = ''.join('<tr><td>%04d<br>%.2f<br>%s<br>%s<br><code>cam %s %s %s %s %s</code></td><td><img src="pair/%04d.jpg" width="960"></td></tr>'
                   % (v['id'], v['score'], v['kind'], v['desc'], v['cam'][0], v['cam'][1], v['cam'][2], v['yaw'], v['pitch'], v['id'])
                   for v in ranked)
    with open(os.path.join(out, 'index.html'), 'w') as f:
        f.write('<!doctype html><meta charset="utf-8"><title>%s: NBlood | Godot | difference</title>'
                '<style>body{background:#111;color:#ddd;font:13px sans-serif}td{vertical-align:top;padding:4px}</style>'
                '<table>%s</table>' % (lv.name, rows))
    print('  worst views (%s):' % os.path.relpath(pair_dir, bugs.ROOT))
    for v in ranked[:15]:
        print('    %04d  %.2f  %-5s %s' % (v['id'], v['score'], v['kind'], v['desc']))
    missing = [v for v in views if v.get('score') is None]
    if missing:
        print('  %d views without both screenshots' % len(missing))


if __name__ == '__main__':
    main()
