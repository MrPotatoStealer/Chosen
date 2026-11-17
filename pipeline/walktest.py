"""Walk tests: a made-up input recording (warp to a spot, then walk) run through the Godot game headless
(inputcheck), with Build's collision, printing where the player ends up. For collision bugs that need play: does the
player get through, stay on the floor, stop at the wall?

usage: python walktest.py <map> "<warp x y z yaw [pitch [sector]]>[; console cmds]" <segments> [--trace a-b]
  segments: frames:forward:strafe[:yaw[:jump[:fire[:crouch]]]] separated by commas, forward/strafe -1..1 (run speed), yaw degrees
  e.g. python walktest.py e3m5 "802 -1040.5 56 170.3 0 80" 60:1:0,40:0:1
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bugs  # noqa: E402

WALK_DIR = os.path.join(bugs.ROOT, 'tools', 'walk')


def write_rec(path, mapname, warp, segments):
    # "warp ...; cmd; cmd": console commands after the warp (send 104 on: a door opening first)
    warp, *cmds = [c.strip() for c in warp.split(';')]
    w = warp.split() if warp != '-' else []
    yaw = float(w[3]) if len(w) > 3 else 0.0
    pitch = float(w[4]) if len(w) > 4 else 0.0
    lines = ['# Chosen walk test', 'map ' + mapname, 'engine godot', 'start map', 'seed 1',
             'cvar b3d_skill 2', 'cvar b3d_monsters 0', 'cvar cl_autoaim 0',
             # the game's default (Blood's own bugs fixed); "; setfixblood 0" after the warp: Blood as it was
             'cvar b3d_fixblood 1',
             'input movement button0 button2 button3 button4 button5 impulse v_angle', 'state']
    if warp != '-':
        lines.append('c|2|warp ' + warp)
    for c in cmds:
        lines.append('c|8|' + c)
    frame = 0
    # a few still frames for the warp to land
    for _ in range(8):
        lines.append('f|%d|0|0|0|0|0|0|0|%g %g 0' % (frame, pitch, yaw))
        frame += 1
    for seg in segments.split(','):
        p = seg.split(':')
        n, fwd, side = int(p[0]), float(p[1]), float(p[2])
        if len(p) > 3 and p[3] != '':
            yaw = float(p[3])
        jump = len(p) > 4 and p[4] == '1'
        fire = len(p) > 5 and p[5] == '1'
        crouch = len(p) > 6 and p[6] == '1'
        for _ in range(n):
            lines.append('f|%d|%g %g 0|%d|%d|0|%d|0|0|%g %g 0' % (frame, fwd * 2048, -side * 2048, 1 if fire else 0,
                                                                 1 if jump else 0, 1 if crouch else 0, pitch, yaw))
            frame += 1
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')


def main():
    args = sys.argv[1:]
    if len(args) < 3:
        sys.exit(__doc__)
    mapname, warp, segments = args[0], args[1], args[2]
    trace = args[args.index('--trace') + 1] if '--trace' in args else ''
    os.makedirs(WALK_DIR, exist_ok=True)
    rec = os.path.join(WALK_DIR, 'walk.rec')
    write_rec(rec, mapname, warp, segments)
    shot = os.path.join(WALK_DIR, 'walk.png').replace(os.sep, '/')
    cmd = 'inputcheck %s%s' % (rec.replace(os.sep, '/'), (' ' + trace) if trace else '')
    out = bugs.run_godot(['--map=' + mapname, '--exec=' + cmd, '--wait=0.2', '--shot=' + shot], 300)
    for l in out.splitlines():
        if l.startswith('end:') or (trace and l.startswith('frame ')):
            print(l)


if __name__ == '__main__':
    main()
