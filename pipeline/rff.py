"""Blood RFF archive reader/extractor (format per NBlood resource.cpp)."""
import os
import struct
import sys

DICT_CRYPT = 16


def _crypt(data, key):
    out = bytearray(data)
    for i in range(len(out)):
        out[i] ^= (((key + i) & 0xFFFF) >> 1) & 0xFF
    return bytes(out)


class RFF:
    def __init__(self, path):
        self.path = path
        with open(path, 'rb') as f:
            self.blob = f.read()
        sign, version, _, offset, filenum = struct.unpack_from('<4shhII', self.blob, 0)
        if sign != b'RFF\x1a':
            raise ValueError('not an RFF: ' + path)
        crypt = (version & 0xff00) == 0x300
        dsize = filenum * 48
        d = self.blob[offset:offset + dsize]
        if crypt:
            d = _crypt(d, (offset + (version & 0xff) * offset) & 0xFFFF)
        self.entries = []
        for i in range(filenum):
            e = d[i * 48:(i + 1) * 48]
            off, size = struct.unpack_from('<II', e, 16)
            flags = e[32]
            typ = e[33:36].split(b'\0')[0].decode('latin1')
            name = e[36:44].split(b'\0')[0].decode('latin1')
            rid = struct.unpack_from('<i', e, 44)[0]
            self.entries.append(dict(name=name, type=typ, id=rid, offset=off, size=size, flags=flags))

    def read(self, e):
        data = self.blob[e['offset']:e['offset'] + e['size']]
        if e['flags'] & DICT_CRYPT:
            n = min(0x100, len(data))
            data = _crypt(data[:n], 0) + data[n:]
        return data

    def find(self, name=None, type=None, id=None):
        for e in self.entries:
            if type is not None and e['type'].upper() != type.upper():
                continue
            if name is not None and e['name'].upper() != name.upper():
                continue
            if id is not None and e['id'] != id:
                continue
            return e
        return None

    def by_type(self, type):
        return [e for e in self.entries if e['type'].upper() == type.upper()]


def extract_all(rff_path, out_dir):
    r = RFF(rff_path)
    os.makedirs(out_dir, exist_ok=True)
    for e in r.entries:
        fn = '%s.%s' % (e['name'], e['type'])
        with open(os.path.join(out_dir, fn), 'wb') as f:
            f.write(r.read(e))
    return r


if __name__ == '__main__':
    r = RFF(sys.argv[1])
    if len(sys.argv) > 2:
        extract_all(sys.argv[1], sys.argv[2])
    from collections import Counter
    print(len(r.entries), Counter(e['type'] for e in r.entries))
