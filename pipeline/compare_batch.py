"""Compare many maps with NBlood in one go (pipeline/compare.py for each): python compare_batch.py <map|addon id>...
[--max N]. An add-on's id (dw, cryptic, marrow) stands for all its maps. Each map's output is compare.py's
(tools/compare/<map>/index.html); tools/compare/batch.txt lists every map, the one that got worse most first: its mean
score now and in the run before (the same cameras), its worst views and those that got worse. The user's settings are
read once at the start and put back at the end (a map whose run fails leaves its test settings, which the next map's
compare.py would take for the user's)."""
import json
import os
import subprocess
import sys

import addons
import blood_assets as ba
import bugs

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), 'tools', 'compare')


def main():
    args = sys.argv[1:]
    maxn = args[args.index('--max') + 1] if '--max' in args else '40'
    names = []
    for a in args:
        if a.startswith('--') or a == maxn:
            continue
        ad = next((x for x in addons.all_addons(ba.BLOOD_DIR) if x.id == a.lower()), None)
        names += ad.maps if ad else [a.upper()]
    restore = bugs.restore_exec(bugs.SHOT_CVARS)
    summary = []
    for n in names:
        print(n, flush=True)
        vj = os.path.join(OUT, n.lower(), 'views.json')
        # the run before: each view's score by what it looks at
        before = {v.get('desc'): v.get('score') for v in json.load(open(vj))} if os.path.exists(vj) else {}
        subprocess.run([sys.executable, os.path.join(HERE, 'compare.py'), n, '--max', maxn],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=1800)
        if not os.path.exists(vj):
            summary.append((99, n, ['no views.json']))
            continue
        views = [v for v in json.load(open(vj)) if v.get('score') is not None]
        both = [(v['score'], before[v['desc']]) for v in views if before.get(v.get('desc')) is not None]
        now = sum(a for a, _ in both) / len(both) if both else sum(v['score'] for v in views) / max(len(views), 1)
        was = sum(b for _, b in both) / len(both) if both else now
        worse = sorted(((v['score'] - before[v['desc']], v) for v in views
                        if before.get(v.get('desc')) is not None and v['score'] > before[v['desc']] + 0.05),
                       key=lambda t: -t[0])
        views.sort(key=lambda v: -v['score'])
        lines = ['mean %.3f (before %.3f)' % (now, was)]
        lines += ['worst %04d %.2f %s %s' % (v['id'], v['score'], v['kind'], v['desc']) for v in views[:4]]
        lines += ['worse %04d %.2f (was %.2f) %s %s' % (v['id'], v['score'], v['score'] - d, v['kind'], v['desc'])
                  for d, v in worse[:6]]
        summary.append((now - was, n, lines))
        with open(os.path.join(OUT, 'batch.txt'), 'w') as f:
            for d, m, ls in sorted(summary, key=lambda t: -t[0]):
                f.write('%s %+.3f\n' % (m, d) + ''.join('    %s\n' % l for l in ls))
    bugs.run_godot(['--map=e1m1', '--exec=' + restore, '--wait=0.3',
                    '--shot=' + os.path.join(OUT, 'restore.png').replace(os.sep, '/')], 300)


if __name__ == '__main__':
    main()
