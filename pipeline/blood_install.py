"""Where Blood is installed on this computer, as the game's ChosenLoader finds it (shared/Chosen.Blood/Files/
BloodInstall.cs): the BLOOD environment variable if set, else Steam's library folders (Blood: Refreshed Supply, then
One Unit Whole Blood), else GOG's usual folders; a folder counts when it holds BLOOD.RFF and TILES000.ART.

usage: python -I blood_install.py      (prints the folder, or says how to give it; the .bat files call it)
"""
import os
import re
import sys

STEAM_NAMES = ('Blood - Refreshed Supply', 'One Unit Whole Blood', 'Blood')
# what the scripts are given when no install is found: a path that names the fix in the error it causes
MISSING = '<Blood not found: set the BLOOD environment variable to your Blood folder>'


def find_file(folder, name):
    """A file in folder by name, case-insensitively (the install's names vary)."""
    if not os.path.isdir(folder):
        return None
    return next((os.path.join(folder, f) for f in os.listdir(folder) if f.lower() == name.lower()), None)


def is_valid(folder):
    return bool(folder) and find_file(folder, 'BLOOD.RFF') is not None and find_file(folder, 'TILES000.ART') is not None


def steam_libraries():
    roots = [os.environ.get('ProgramFiles(x86)'), os.environ.get('ProgramFiles')]
    steam = [os.path.join(r, 'Steam') for r in roots if r]
    libs = list(steam)
    for s in steam:
        vdf = os.path.join(s, 'steamapps', 'libraryfolders.vdf')
        if os.path.isfile(vdf):
            with open(vdf, encoding='utf-8', errors='replace') as f:
                libs += [m.replace('\\\\', '\\') for m in re.findall(r'"path"\s*"([^"]+)"', f.read())]
    seen, out = set(), []
    for lib in libs:
        if lib.lower() not in seen:
            seen.add(lib.lower())
            out.append(lib)
    return out


def candidates():
    for lib in steam_libraries():
        for n in STEAM_NAMES:
            yield os.path.join(lib, 'steamapps', 'common', n)
    for root in (os.environ.get('ProgramFiles(x86)'), r'C:\GOG Games', r'D:\GOG Games'):
        if root:
            yield os.path.join(root, 'GOG Galaxy', 'Games', 'Blood Fresh Supply')
            yield os.path.join(root, 'Blood Fresh Supply')
            yield os.path.join(root, 'One Unit Whole Blood')


def find():
    """The BLOOD environment variable, else the first valid install found, else None."""
    env = os.environ.get('BLOOD')
    if env:
        return env
    return next((c for c in candidates() if is_valid(c)), None)


def find_or_missing():
    """find(), or MISSING (a module constant's default: what fails with it says what to do)."""
    return find() or MISSING


if __name__ == '__main__':
    found = find()
    if not found:
        print('Blood not found: set the BLOOD environment variable to your Blood folder', file=sys.stderr)
        sys.exit(1)
    print(found)
