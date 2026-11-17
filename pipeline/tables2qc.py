"""Convert NBlood C data tables (struct initializers) into flat QuakeC arrays.

usage: python -I tables2qc.py <nblood_src_dir> <out.qc>

Each table is flattened: one QC array per table with a fixed stride (number of scalar
values per element). Accessor constants <TABLE>_STRIDE are emitted alongside.
Enum symbols are resolved from all NBlood headers/sources.
"""
import os
import re
import sys

SRC_FILES_FOR_ENUMS = None


def strip_comments(s):
    s = re.sub(r'/\*.*?\*/', ' ', s, flags=re.S)
    s = re.sub(r'//[^\n]*', ' ', s)
    return s


def strip_preproc(s):
    # keep #ifdef NOONE_EXTENSIONS blocks out (vanilla tables), drop other preprocessor lines
    out = []
    skip = 0
    stack = []
    for line in s.split('\n'):
        t = line.strip()
        if t.startswith('#if'):
            cond = 'NOONE_EXTENSIONS' in t and not t.startswith('#ifndef')
            stack.append(cond)
            continue
        if t.startswith('#else'):
            if stack:
                stack[-1] = not stack[-1]
            continue
        if t.startswith('#endif'):
            if stack:
                stack.pop()
            continue
        if t.startswith('#'):
            continue
        if any(stack):
            continue
        out.append(line)
    return '\n'.join(out)


def collect_enums(src_dir):
    syms = {}
    for fn in os.listdir(src_dir):
        if not fn.endswith(('.h', '.cpp')):
            continue
        with open(os.path.join(src_dir, fn), encoding='latin1') as f:
            s = strip_preproc(strip_comments(f.read()))
        for m in re.finditer(r'enum\s*\w*\s*(?::\s*\w+\s*)?\{(.*?)\}', s, flags=re.S):
            val = -1
            for item in m.group(1).split(','):
                item = item.strip()
                if not item:
                    continue
                if '=' in item:
                    name, expr = [x.strip() for x in item.split('=', 1)]
                    try:
                        val = evaluate(expr, syms)
                    except Exception:
                        continue
                else:
                    name = item
                    val += 1
                if re.match(r'^[A-Za-z_]\w*$', name):
                    syms.setdefault(name, val)
        for m in re.finditer(r'#define\s+(\w+)\s+([^\n]+)', s):
            pass
    # defines (from raw text incl. preprocessor)
    for fn in os.listdir(src_dir):
        if not fn.endswith(('.h',)):
            continue
        with open(os.path.join(src_dir, fn), encoding='latin1') as f:
            s = strip_comments(f.read())
        for m in re.finditer(r'^\s*#define\s+(\w+)\s+([^\n]+)$', s, flags=re.M):
            name, expr = m.group(1), m.group(2).strip()
            try:
                syms.setdefault(name, evaluate(expr, syms))
            except Exception:
                pass
    return syms


def evaluate(expr, syms):
    e = expr.strip()
    e = re.sub(r'\b(0x[0-9a-fA-F]+)[uUlL]*\b', lambda m: str(int(m.group(1), 16)), e)
    e = re.sub(r'\b(\d+)[uUlL]+\b', r'\1', e)
    e = re.sub(r'\(\s*(?:int|short|char|unsigned|signed|DAMAGE_TYPE|FX_ID|COMMAND_ID|VECTOR_TYPE)\s*\)', '', e)

    def rep(m):
        n = m.group(0)
        if n in syms:
            return '(%d)' % syms[n]
        raise KeyError(n)
    e = re.sub(r'\b[A-Za-z_]\w*\b', rep, e)
    e = e.replace('/', '//')
    v = eval(e, {'__builtins__': {}}, {})
    return int(v)


def find_table(src, name):
    m = re.search(r'\b' + re.escape(name) + r'\s*(\[[^\]]*\])*\s*=\s*\{', src)
    if not m:
        raise KeyError('table %s not found' % name)
    i = m.end() - 1
    depth = 0
    for j in range(i, len(src)):
        if src[j] == '{':
            depth += 1
        elif src[j] == '}':
            depth -= 1
            if depth == 0:
                return src[i:j + 1]
    raise ValueError('unbalanced ' + name)


def parse_init(text):
    """Parse a brace initializer into nested lists of token strings."""
    tokens = re.findall(r'\{|\}|,|[^{},]+', text)
    stack = [[]]
    cur = ''
    for t in tokens:
        if t == '{':
            stack.append([])
        elif t == '}':
            if cur.strip():
                stack[-1].append(cur.strip())
            cur = ''
            done = stack.pop()
            stack[-1].append(done)
        elif t == ',':
            if cur.strip():
                stack[-1].append(cur.strip())
            cur = ''
        else:
            cur += t
    return stack[0][0]


def flatten(x):
    if isinstance(x, list):
        for y in x:
            yield from flatten(y)
    else:
        yield x


def convert(src_text, name, syms, qcname, stride=None, elements_are_lists=True):
    tbl = parse_init(find_table(src_text, name))
    rows = []
    if elements_are_lists and all(isinstance(r, list) for r in tbl):
        for r in tbl:
            rows.append([evaluate(v, syms) for v in flatten(r)])
    else:
        flat = [evaluate(v, syms) for v in flatten(tbl)]
        if stride is None:
            stride = 1
        rows = [flat[i:i + stride] for i in range(0, len(flat), stride)]
    width = max(len(r) for r in rows)
    if stride is not None and stride > width:
        width = stride
    vals = []
    for r in rows:
        vals.extend(r + [0] * (width - len(r)))
    out = ['// %s: %d entries x %d' % (name, len(rows), width),
           'const float %s_STRIDE = %d;' % (qcname, width),
           'const float %s_COUNT = %d;' % (qcname, len(rows)),
           'float %s[%d] = {' % (qcname, len(vals))]
    for i in range(0, len(vals), width):
        out.append('\t' + ', '.join(str(v) for v in vals[i:i + width]) + ',')
    out[-1] = out[-1].rstrip(',')
    out.append('};')
    return '\n'.join(out) + '\n'


TABLES = [
    # (file, C table name, qc array name)
    ('actor.cpp', 'gVectorData', 'tbl_vector'),
    ('actor.cpp', 'gItemData', 'tbl_item'),
    ('actor.cpp', 'gAmmoItemData', 'tbl_ammoitem'),
    ('actor.cpp', 'gWeaponItemData', 'tbl_weaponitem'),
    ('actor.cpp', 'missileInfo', 'tbl_missile'),
    ('actor.cpp', 'thingInfo', 'tbl_thing'),
    ('actor.cpp', 'explodeInfo', 'tbl_explode'),
    ('actor.cpp', 'gPlayerGibThingComments', 'tbl_gibcomments'),
    ('dude.cpp', 'dudeInfo', 'tbl_dude'),
    ('dude.cpp', 'gPlayerTemplate', 'tbl_playertemplate'),
    ('fx.cpp', 'gFXData', 'tbl_fx'),
    ('player.cpp', 'gPowerUpInfo', 'tbl_powerup'),
    ('player.cpp', 'gPostureDefaults', 'tbl_posture'),
    ('player.cpp', 'gAmmoInfo', 'tbl_ammoinfo'),
    ('player.cpp', 'armorData', 'tbl_armor'),
    ('player.cpp', 'damageInfo', 'tbl_damageinfo'),
    ('player.cpp', 'Handicap', 'tbl_handicap'),
]


def emit_flat(qcname, rows, width):
    vals = []
    for r in rows:
        vals.extend(r + [0] * (width - len(r)))
    out = ['const float %s_STRIDE = %d;' % (qcname, width),
           'const float %s_COUNT = %d;' % (qcname, len(rows)),
           'float %s[%d] = {' % (qcname, max(1, len(vals)))]
    if not vals:
        vals = [0]
    for i in range(0, len(vals), width):
        out.append('\t' + ', '.join(str(v) for v in vals[i:i + width]) + ',')
    out[-1] = out[-1].rstrip(',')
    out.append('};')
    return '\n'.join(out) + '\n'


def convert_gibs(src_text, syms):
    """gib.cpp: pool all GIBFX / GIBTHING arrays and express gibList as (fxStart, fxCount, thingStart, thingCount, at10)."""
    fx_pool, thing_pool = [], []
    fx_index, thing_index = {}, {}
    for m in re.finditer(r'GIBFX\s+(\w+)\s*\[\s*\]\s*=', src_text):
        name = m.group(1)
        rows = parse_init(find_table(src_text, name))
        fx_index[name] = len(fx_pool)
        for r in rows:
            fx_pool.append([evaluate(v, syms) for v in flatten(r)])
    for m in re.finditer(r'GIBTHING\s+(\w+)\s*\[\s*\]\s*=', src_text):
        name = m.group(1)
        rows = parse_init(find_table(src_text, name))
        thing_index[name] = len(thing_pool)
        for r in rows:
            thing_pool.append([evaluate(v, syms) for v in flatten(r)])
    lst = parse_init(find_table(src_text, 'gibList'))
    rows = []
    for r in lst:
        fxname, fxcount, thname, thcount, at10 = [x.strip() for x in r]
        rows.append([fx_index.get(fxname, -1), evaluate(fxcount, syms), thing_index.get(thname, -1),
                     evaluate(thcount, syms), evaluate(at10, syms)])
    return ('// gib.cpp pools\n' + emit_flat('tbl_gibfx', fx_pool, 6) + emit_flat('tbl_gibthing', thing_pool, 5)
            + emit_flat('tbl_giblist', rows, 5))


def main():
    src_dir = sys.argv[1]
    out = sys.argv[2]
    extra = sys.argv[3:]  # optional extra file:table:qcname triples
    syms = collect_enums(src_dir)
    syms.setdefault('NULL', -1)
    cache = {}
    parts = ['// generated by pipeline/tables2qc.py from NBlood sources -- do not edit\n']
    tables = list(TABLES)
    for e in extra:
        f, t, q = e.split(':')
        tables.append((f, t, q))
    for fn, name, qc in tables:
        if fn not in cache:
            with open(os.path.join(src_dir, fn), encoding='latin1') as f:
                cache[fn] = strip_preproc(strip_comments(f.read()))
        try:
            parts.append(convert(cache[fn], name, syms, qc))
        except Exception as ex:
            print('FAILED', fn, name, ex)
            raise
    with open(os.path.join(src_dir, 'gib.cpp'), encoding='latin1') as f:
        parts.append(convert_gibs(strip_preproc(strip_comments(f.read())), syms))
    with open(out, 'w') as f:
        f.write('\n'.join(parts))
    print('wrote', out)


if __name__ == '__main__':
    main()
