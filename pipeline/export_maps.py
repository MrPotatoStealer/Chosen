"""Copy Blood's maps as they are stored into the game's data folder, for the game to load them itself (Sim/Build).

usage: python -I export_maps.py [blood_dir] [game_dir]

Writes game/blood3d/maps/<NAME>.map (the base game's: PATCH01.RFF over BLOOD.RFF) and game/blood3d/addons/<id>/maps/
<NAME>.map (each add-on's own, pipeline/addons.py), each with <NAME>.patch beside it when Refreshed Supply fixes the map
(defs/levelHacks: one command per line, 'sector 423 ceilingz -92160', 'sprite 85 novoxel'; the game applies them).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import addons  # noqa: E402
import blood_assets as ba  # noqa: E402
from rff import RFF  # noqa: E402


def write(folder, name, data, blood_dir, addon=None):
    os.makedirs(folder, exist_ok=True)
    with open(os.path.join(folder, name.upper() + '.map'), 'wb') as f:
        f.write(data)
    patch = addons.level_patch(blood_dir, name, data, addon)
    pp = os.path.join(folder, name.upper() + '.patch')
    if patch:
        with open(pp, 'w') as f:
            f.write(''.join(' '.join(c) + '\n' for c in patch))
    elif os.path.exists(pp):
        os.remove(pp)


def main():
    blood_dir = sys.argv[1] if len(sys.argv) > 1 else ba.BLOOD_DIR
    game = sys.argv[2] if len(sys.argv) > 2 else addons.GAME
    names = []
    for fn in ('PATCH01.RFF', 'BLOOD.RFF'):
        p = os.path.join(blood_dir, fn)
        if os.path.exists(p):
            for e in RFF(p).entries:
                if e['type'].upper() == 'MAP' and e['name'].upper() not in names:
                    names.append(e['name'].upper())
    for n in names:
        write(os.path.join(game, 'maps'), n, addons.map_bytes(blood_dir, n), blood_dir)
    print('%d base maps' % len(names))
    for a in addons.all_addons(blood_dir):
        k = 0
        for n in a.maps:
            f = a.map_file(n)
            if f is None:
                continue
            with open(f, 'rb') as fh:
                write(os.path.join(a.game, 'maps'), n, fh.read(), blood_dir, a)
            k += 1
        print('%s: %d maps' % (a.id, k))


if __name__ == '__main__':
    main()
