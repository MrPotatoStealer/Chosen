"""Export one of Refreshed Supply's add-ons into its data folder, game/blood3d/addons/<id> (pipeline/addons.py).

usage: python -I export_addon.py "<add-on folder or id>" [blood_dir]      (e.g. "Death Wish" or dw)

Writes only what the add-on brings over the base game (export.py's output, which the game falls back to):
  textures/blood/NNNN.png, textures/blood_masked/NNNN.png   the tiles its art files replace
  data/tiles.txt, data/rt_tiles.txt                          the tile table with its art in place of the base game's
  data/blood.ini                                             its episode file (episodes, maps, music, cutscenes)
  data/addon.txt                                             its title for the game's ADD-ONS menu
  sound/cdtracks/trackNNN.ogg, sound/midi/, sound/sc55/      its music (bloodNN.ogg, .mid, SC-55/*.ogg)
  movie/remastered/                                          its cutscenes (.ogv) and their English subtitles
"""
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import addons  # noqa: E402
import blood_assets as ba  # noqa: E402
import export  # noqa: E402
import gen_qcdata  # noqa: E402
from rff import RFF  # noqa: E402


def copy(src, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    if not os.path.exists(dst) or os.path.getmtime(dst) < os.path.getmtime(src):
        shutil.copyfile(src, dst)
        return 1
    return 0


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    blood_dir = sys.argv[2] if len(sys.argv) > 2 else ba.BLOOD_DIR
    ba.BLOOD_DIR = blood_dir
    addon = addons.find(blood_dir, sys.argv[1])
    addons.activate(addon)
    game = addon.game
    os.makedirs(os.path.join(game, 'data'), exist_ok=True)
    pal = ba.load_palette(RFF(os.path.join(blood_dir, 'BLOOD.RFF')))
    tiles = ba.load_tiles(blood_dir)

    # its tiles' images (written again each time: an updated add-on replaces them)
    n = 0
    for sub, masked in (('blood', False), ('blood_masked', True)):
        os.makedirs(os.path.join(game, 'textures', sub), exist_ok=True)
    for num in sorted(ba.ADDON_TILES):
        t = tiles.get(num)
        if t is None or t.w <= 0 or t.h <= 0:
            continue
        export.tile_image(t, pal, False).convert('RGB').save(os.path.join(game, 'textures', 'blood', '%04d.png' % num))
        export.tile_image(t, pal, True).save(os.path.join(game, 'textures', 'blood_masked', '%04d.png' % num))
        n += 1
    print('%s: %d tile images' % (addon.name, n))

    # the tile table with its art in place (sizes, offsets, animation from its art files' headers)
    export.export_tiledata(tiles, game)
    gen_qcdata.gen_tiles(game, None)

    # Refreshed Supply's strings (the add-ons' episode and level titles by code: $dw_ep1), in the base data folder
    export.export_strings(blood_dir, addons.GAME)

    # its episode file and title
    if addon.ini:
        copy(addon.ini, os.path.join(game, 'data', 'blood.ini'))
    with open(os.path.join(game, 'data', 'addon.txt'), 'w') as f:
        f.write('title %s\nfolder %s\nmaps %s\n' % (addon.name, addon.name, ' '.join(m.lower() for m in addon.maps)))

    # music and cutscenes
    music = movies = 0
    for f in os.listdir(addon.folder):
        low, src = f.lower(), os.path.join(addon.folder, f)
        if low.startswith('blood') and low.endswith('.ogg') and low[5:-4].isdigit():
            music += copy(src, os.path.join(game, 'sound', 'cdtracks', 'track%03d.ogg' % int(low[5:-4])))
        elif low.endswith('.mid'):
            music += copy(src, os.path.join(game, 'sound', 'midi', low))
        elif low.endswith('.ogv') or (low.endswith('.srt') and '_' not in low):
            movies += copy(src, os.path.join(game, 'movie', 'remastered', low))
    sc55 = os.path.join(addon.folder, 'SC-55')
    if os.path.isdir(sc55):
        for f in os.listdir(sc55):
            if f.lower().endswith('.ogg'):
                music += copy(os.path.join(sc55, f), os.path.join(game, 'sound', 'sc55', f.lower()))
    print('%s: %d music files, %d cutscene files copied; %d maps in %s' % (
        addon.name, music, movies, len(addon.maps), os.path.basename(addon.ini or '(no .ini)')))
    print('data folder:', game)


if __name__ == '__main__':
    main()
