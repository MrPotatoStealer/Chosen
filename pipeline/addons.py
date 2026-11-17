"""Refreshed Supply's add-ons (the install's addons/<folder>: Death Wish, Cryptic Passage, MARROW): each a folder with
its episode file (an .ini like blood.ini), its maps (.MAP), the art files that replace the base game's (tilesNNN.art,
each a whole file: NBlood's -game_dir searches the folder first), its music (bloodNN.ogg CD tracks, .mid songs, SC-55
recordings) and its cutscenes (.ogv with .srt subtitles).

An add-on's id is its .ini's name in lower case (dw, cryptic, marrow). Its data folder is game/blood3d/addons/<id>
(pipeline/export_addon.py: the images of its tiles, its tile table, its .ini as data/blood.ini, its music and
cutscenes), and its maps (export_maps.py: game/blood3d/addons/<id>/maps).
"""
import glob
import os
import struct

import blood_assets as ba

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = os.path.join(ROOT, 'game', 'blood3d')


def read_ini(path):
    """an .ini's sections: {section (lower case): {key (lower case): value}}"""
    sections, cur = {}, None
    with open(path, encoding='latin-1') as f:
        for raw in f:
            line = raw.split(';', 1)[0].strip()
            if not line:
                continue
            if line.startswith('[') and line.endswith(']'):
                cur = sections.setdefault(line[1:-1].strip().lower(), {})
            elif cur is not None and '=' in line:
                k, v = line.split('=', 1)
                cur[k.strip().lower()] = v.strip()
    return sections


class Addon:
    def __init__(self, folder):
        self.folder = folder
        self.name = os.path.basename(folder.rstrip('/\\'))
        inis = sorted(glob.glob(os.path.join(folder, '*.ini')))
        self.ini = inis[0] if inis else None
        self.id = os.path.splitext(os.path.basename(self.ini))[0].lower() if self.ini else self.name.lower().replace(' ', '')
        self.game = os.path.join(GAME, 'addons', self.id)
        # its maps in episode order (the .ini's [EpisodeN] MapN)
        self.maps = []
        if self.ini:
            sec = read_ini(self.ini)
            e = 1
            while 'episode%d' % e in sec:
                m = 1
                while 'map%d' % m in sec['episode%d' % e]:
                    name = os.path.splitext(os.path.basename(sec['episode%d' % e]['map%d' % m].replace('\\', '/')))[0]
                    if name.upper() not in self.maps:
                        self.maps.append(name.upper())
                    m += 1
                e += 1

    def map_file(self, name):
        """the add-on's file for a map name (any case), None if it has none"""
        for f in os.listdir(self.folder):
            if f.lower() == name.lower() + '.map':
                return os.path.join(self.folder, f)
        return None

    def art_tiles(self):
        """the tiles the add-on's art files hold (each file replaces the base game's of its name whole)"""
        tiles = set()
        for p in glob.glob(os.path.join(self.folder, 'tiles*.art')):
            with open(p, 'rb') as f:
                _, _, start, end = struct.unpack('<iiii', f.read(16))
            tiles.update(range(start, end + 1))
        return tiles


def mod_folders():
    """the mods made for NBlood installed as add-ons (maptool extract --mod, ChosenLoader's Add a Mod Folder): their
    folders, which each one's data/addon.txt names ("source"), so NBlood shows their levels too (bugs.py nblood,
    compare.py, playat.bat)"""
    found = []
    for info in sorted(glob.glob(os.path.join(GAME, 'addons', '*', 'data', 'addon.txt'))):
        with open(info, encoding='utf-8') as f:
            for line in f:
                if line.startswith('source ') and os.path.isdir(line[7:].strip()):
                    found.append(line[7:].strip())
    return found


def all_addons(blood_dir):
    root = os.path.join(blood_dir, 'addons')
    found = []
    if os.path.isdir(root):
        found = [Addon(os.path.join(root, d)) for d in sorted(os.listdir(root)) if os.path.isdir(os.path.join(root, d))]
    return found + [Addon(d) for d in mod_folders()]


def find(blood_dir, name):
    """an add-on by its folder name or id (any case)"""
    for a in all_addons(blood_dir):
        if name.lower() in (a.name.lower(), a.id):
            return a
    raise SystemExit('no add-on "%s" in %s (have: %s)' % (name, os.path.join(blood_dir, 'addons'),
                                                          ', '.join(a.name for a in all_addons(blood_dir))))


def activate(addon):
    """from now on the asset loaders read the add-on's art over the base game's, and its tiles' images go to its data
    folder (blood_assets.game_for)"""
    ba.ADDON_DIR = addon.folder if addon else None
    ba.ADDON_GAME = addon.game if addon else None
    ba.ADDON_TILES = addon.art_tiles() if addon else set()


def owner(blood_dir, name):
    """the add-on whose .ini lists a map (any case), None for the base game's"""
    for a in all_addons(blood_dir):
        if name.upper() in a.maps:
            return a
    return None


def load_map(blood_dir, name):
    """a map by name: the base game's (PATCH01.RFF or BLOOD.RFF), else an add-on's that lists it (its .MAP file)"""
    import blood_map
    try:
        return blood_map.load(blood_dir, name)
    except FileNotFoundError:
        a = owner(blood_dir, name)
        if a is None or a.map_file(name) is None:
            raise
        with open(a.map_file(name), 'rb') as f:
            return blood_map.BloodMap(f.read())


def nblood_map_args(blood_dir, name):
    """NBlood's arguments for a level: the install, and for an add-on's its folder searched first and its episode file
    (-game_dir, -ini: its art, maps, music), then the map"""
    args = ['-j', blood_dir]
    a = owner(blood_dir, name)
    if a is not None:
        args += ['-game_dir', a.folder] + (['-ini', os.path.basename(a.ini)] if a.ini else [])
    return args + ['-map', name]


def map_bytes(blood_dir, name):
    """a map's file as stored: the base game's (PATCH01.RFF over BLOOD.RFF), else an add-on's that lists it"""
    from rff import RFF
    for fn in ('PATCH01.RFF', 'BLOOD.RFF'):
        p = os.path.join(blood_dir, fn)
        if not os.path.exists(p):
            continue
        r = RFF(p)
        for e in r.entries:
            if e['type'].upper() == 'MAP' and e['name'].upper() == name.upper():
                return r.read(e)
    a = owner(blood_dir, name)
    if a is not None and a.map_file(name):
        with open(a.map_file(name), 'rb') as f:
            return f.read()
    raise FileNotFoundError(name + '.MAP')


# the patches' lines left out, by map: Blood's own content they take away (E1M4's hanging Duke Nukem, sprite 952:
# Refreshed Supply draws it flat instead of its voxel model, likely for licensing; the original game has it)
SKIP_PATCH = {'E1M4': {('sprite', '952', 'novoxel')}}


def level_patch(blood_dir, name, data, addon=None):
    """Refreshed Supply's fixes for one map (defs/levelHacks/<map>.txt in BloodEX.kpf and the add-on's .kpf): the
    commands (lists of words: 'sprite 85 novoxel', 'sector 423 ceilingz -92160'...), [] when it has none or its
    checksum isn't this map's. The file's first line is the CRC-32 Blood stores at the end of the map file (the
    version it fixes; 0: any); '#include "<path>"' reads another file of the same package."""
    import zipfile
    kpfs = [os.path.join(blood_dir, 'BloodEX.kpf')]
    if addon is not None:
        kpfs = glob.glob(os.path.join(addon.folder, '*.kpf')) + kpfs
    stored = struct.unpack('<I', data[-4:])[0] if len(data) >= 4 else None
    for kpf in kpfs:
        if not os.path.exists(kpf):
            continue
        with zipfile.ZipFile(kpf) as z:
            names = {n.lower(): n for n in z.namelist()}
            path = 'defs/levelhacks/%s.txt' % name.lower()
            if path not in names:
                continue

            def read(p, depth=0):
                out = []
                for line in z.read(names[p]).decode('utf-8', 'replace').splitlines():
                    words = line.split()
                    if not words or words[0].startswith('//'):
                        continue
                    if words[0] == '#include' and len(words) > 1 and depth < 4:
                        inc = words[1].strip('"').lower()
                        if inc in names:
                            out += read(inc, depth + 1)
                        continue
                    out.append(words)
                return out
            cmds = read(path)
            crc = int(cmds[0][0]) if cmds and cmds[0][0].isdigit() and len(cmds[0]) == 1 else 0
            if cmds and cmds[0][0].isdigit() and len(cmds[0]) == 1:
                cmds = cmds[1:]
            if crc and crc != stored:
                print('  level patch %s: for another version of the map (CRC %d, this map %s): not applied' % (path, crc, stored))
                return []
            skip = SKIP_PATCH.get(name.upper(), set())
            return [c for c in cmds if tuple(c) not in skip]
    return []


if __name__ == '__main__':
    # python addons.py nbargs <map>: NBlood's arguments for a map, quoted for a batch file (godot/playat.bat)
    import sys
    if len(sys.argv) >= 3 and sys.argv[1] == 'nbargs':
        print(' '.join('"%s"' % a if ' ' in a else a for a in nblood_map_args(ba.BLOOD_DIR, sys.argv[2])))
