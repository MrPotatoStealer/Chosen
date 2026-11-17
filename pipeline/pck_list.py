"""The files a Godot export carries in its pack (an exe with the .pck embedded, or a .pck), and a check that none of
them is Blood's or a picture of it: build_release.bat runs it on every program before zipping a release.

usage: python -I pck_list.py <exe or pck> [--check]

Godot's pack: "GDPC", u32 format (2: Godot 4.0-4.3, 3: 4.4 on), u32 major, minor, patch, u32 flags, u64 file base,
(format 3) u64 directory offset, 16 reserved u32; the directory: u32 count, then each u32 path length (padded to 4),
the path, u64 offset, u64 size, 16 bytes MD5, u32 flags. Embedded in an exe, the pack ends with u64 size and "GDPC".
"""
import os
import struct
import sys

# what may be in a Chosen program's pack: the project's own scenes, scripts' resources, shaders, settings and texts,
# and Godot's own files. Anything else (a picture, a sound, a map, a Blood file) fails the check.
ALLOWED_EXT = {'.tscn', '.scn', '.tres', '.res', '.gdshader', '.shader', '.godot', '.cfg', '.txt', '.json', '.uid',
               '.import', '.remap', '.bin', '.cs', '.gd', '.md', '.ini', ''}
ALLOWED_PATHS = {'project.binary', 'icon.svg', 'icon.png', 'icon.ico'}
# never, whatever the extension
FORBIDDEN_PARTS = ('tests/', 'screenshot', 'bugs/', 'blood3d/', '.godot/imported/')


def read_pack(path):
    with open(path, 'rb') as f:
        d = f.read()
    start = 0
    if d[:4] != b'GDPC':
        if d[-4:] != b'GDPC':
            raise ValueError('%s: no Godot pack in it' % path)
        size = struct.unpack_from('<Q', d, len(d) - 12)[0]
        start = len(d) - 12 - size
        if d[start:start + 4] != b'GDPC':
            raise ValueError('%s: the pack trailer points at no pack' % path)
    fmt, major, minor, patch, flags = struct.unpack_from('<5I', d, start + 4)
    if flags & 1:
        raise ValueError('%s: the pack\'s directory is encrypted' % path)
    pos = start + 24
    pos += 8                                    # file base
    if fmt >= 3:
        dir_offset = struct.unpack_from('<Q', d, pos)[0]
        pos = start + dir_offset
    else:
        pos += 16 * 4
    count = struct.unpack_from('<I', d, pos)[0]
    pos += 4
    files = []
    for _ in range(count):
        n = struct.unpack_from('<I', d, pos)[0]
        pos += 4
        name = d[pos:pos + n].rstrip(b'\0').decode('utf-8', 'replace')
        pos += n
        offset, size = struct.unpack_from('<QQ', d, pos)
        pos += 16 + 16 + 4                      # offset, size, md5, flags
        files.append((name[6:] if name.startswith('res://') else name, size))
    return (fmt, major, minor, patch), files


def problems(files):
    bad = []
    for name, _ in files:
        low = name.lower()
        ext = os.path.splitext(low)[1]
        if any(p in low for p in FORBIDDEN_PARTS) and not low.startswith('.godot/exported/') or \
                (ext not in ALLOWED_EXT and os.path.basename(low) not in ALLOWED_PATHS
                 and not low.startswith(('.godot/', 'addons/'))):
            bad.append(name)
    return bad


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    if not args:
        print(__doc__)
        sys.exit(2)
    ver, files = read_pack(args[0])
    if '--check' in sys.argv:
        bad = problems(files)
        for b in bad:
            print('NOT ALLOWED in %s: %s' % (os.path.basename(args[0]), b))
        print('%s: %d files in its pack, %d not allowed' % (os.path.basename(args[0]), len(files), len(bad)))
        sys.exit(1 if bad else 0)
    print('pack format %d, Godot %d.%d.%d, %d files' % (ver[0], ver[1], ver[2], ver[3], len(files)))
    for name, size in files:
        print('%10d  %s' % (size, name))


if __name__ == '__main__':
    main()
