"""Bug tracker for in-game reports (console: reportbug <text>, gotobug <id>).

usage:
  python bugs.py import                          copy new reports from game/blood3d/data/bugs into bugs/<id>/
  python bugs.py list [open|fixed|all]           list bugs (default: open)
  python bugs.py show <id>                       print a bug's report and status
  python bugs.py repro <id>                      run the Godot game at the bug's spot and save bugs/<id>/repro.jpg
  python bugs.py replay <id> [frame]             replay the bug's input recording in the Godot game (up to frame),
                                                 print how far it drifts, save bugs/<id>/replay.jpg
  python bugs.py nblood <id> [seconds] [s,s]     NBlood (third_party/nblood, patched) at the report's camera: saves
                                                 bugs/<id>/nblood.jpg and compare.jpg (the report beside NBlood);
                                                 s,s: Build sectors turned on at the start (movers mid-motion; c<n> toggles channel n, o<n> turns sector n off)
                                                 a Forge report (forge/<id>, Report a Problem): its 3D camera, and
                                                 from Play its nblood_send and nblood_seconds when not given
  python bugs.py fix <id> "<cause>" "<fix>"      mark fixed (records date, cause and fix)
  python bugs.py reopen <id> "<note>"            mark open again
  python bugs.py note <id> "<text>"              add a note, status unchanged
  python bugs.py tag <id> <cat>[,<cat>] ["<setup>" [seconds] ["<check>"]]
                                                 put a bug in regression categories (bugs/regression/<cat>/); setup:
                                                 console commands bringing the level into the bug's state (send s136 on),
                                                 seconds to wait after them; check: what to look for when it needs play
  python bugs.py untag <id> [<cat>]              take a bug out of one category (or all)
  python bugs.py categories                      list the regression categories and what code each covers
  python bugs.py regress [<cat>|<id>,..|all] [--nblood] [--mapx]
                                                 screenshot every bug of the categories (or the bugs) in the Godot game (one run),
                                                 compare with each one's accepted baseline.jpg, write
                                                 bugs/regression/<cat>/README.md and sheet.jpg (report | NBlood |
                                                 baseline | now); --nblood also makes the missing NBlood views;
                                                 --mapx plays each level from a .MAPX made of its .MAP (maptool
                                                 tomapx into tools/mapx, fs_mapx): the views must not change
  python bugs.py accept <id>|<cat>               the last regress shot becomes the baseline (after checking it)
  python bugs.py motion [<id>,..] [--selftest]   the rendering steps between game frames: each case in
                                                 bugs/regression/motion/cases.txt replayed to a moment things move, held at
                                                 several blends: the view as drawn against every moving sector built exactly
                                                 for that blend (BROKEN: the blend parts or moves something), and against its
                                                 accepted baseline (CHANGED); --selftest draws with r_meshblend 2, which is
                                                 wrong on purpose, to see the test catch it (the boat, the yard door and
                                                 E2M1, whose floors don't move rigidly)
  python bugs.py motion accept [<id>,..]         the last motion shots become the baselines (after checking them)

Each bug folder holds report.txt (from the game), screenshot.jpg, status.md (status, cause, fix, history),
recording.rec (the input recording of the level up to the report), start.sav when the recording began from a
loaded game (DarkPlaces reports), and optionally repro.jpg / replay.jpg. bugs/README.md is the generated index.

Regression: tricky bugs are tagged with categories by the code they exercise (a "Regression:" line in status.md,
with optional "Regression setup:" and "Regression check:" lines). After changing that code, "bugs.py regress <cat>"
retakes their views and flags the ones that changed from the accepted baseline.jpg. bugs/regression/ holds the
generated index of each category; the bugs stay in bugs/<id>/.

bugs.py nblood shows the report's spot in NBlood, the reference (build_nblood.bat builds third_party/nblood with
pipeline/nblood_b3dview.patch: a camera override and screenshot-and-quit), and puts it beside the report.

Reports come from the Godot game (godot/; "engine: godot" in report.txt). Older reports were made in the retired
DarkPlaces version (Deprecated/darkplaces): repro and replay run them in the Godot game too.
"""
import datetime
import glob
import os
import shutil
import subprocess
import sys
import time

from PIL import Image, ImageChops, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blood_install  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = os.path.join(ROOT, 'game', 'blood3d')
INBOX = os.path.join(GAME, 'data', 'bugs')
BUGS = os.path.join(ROOT, 'bugs')
GODOT_DIR = os.path.join(ROOT, 'godot')
NBLOOD = os.path.join(ROOT, 'third_party', 'nblood', 'nblood.exe')
NBLOOD_RUN = os.path.join(ROOT, 'tools', 'nblood_run')     # its working folder (nblood.cfg: windowed, no setup)
BLOOD_DIR = blood_install.find_or_missing()      # BLOOD, else found as ChosenLoader finds it
NBLOOD_CFG = ('[Setup]\r\nForceSetup = 0\r\nNoAutoLoad = 1\r\nInputMouse = 0\r\n\r\n[Screen Setup]\r\n'
              'ScreenBPP = 32\r\nScreenWidth = 1280\r\nScreenHeight = 720\r\nScreenMode = 0\r\nPolymer = 0\r\n'
              'WindowPosX = 20000\r\nWindowPosY = 20000\r\n')
# test windows open off the desktop (the shots read the rendered picture), so they don't cover the user's work
OFFSCREEN = ['--position', '20000,20000']
# NBlood keeps the config's off-desktop position (B3D_OFFSCREEN, the patch) and shows its window without focus
NBLOOD_ENV = dict(os.environ, B3D_OFFSCREEN='1', SDL_WINDOW_NO_ACTIVATION_WHEN_SHOWN='1')
GODOT = os.path.join(ROOT, 'tools', 'godot-4.7-mono', 'Godot_v4.7.2-stable_mono_win64', 'Godot_v4.7.2-stable_mono_win64_console.exe')
NL = chr(10)


def read_report(path):
    d = {}
    for line in open(path, encoding='utf-8', errors='replace'):
        if ': ' in line:
            k, v = line.rstrip(NL).split(': ', 1)
            d[k] = v
    return d


def read_status(bug):
    p = os.path.join(BUGS, bug, 'status.md')
    if not os.path.exists(p):
        return 'open'
    for line in open(p, encoding='utf-8'):
        if line.startswith('Status: '):
            return line[8:].strip()
    return 'open'


def save_jpg(src, dst, width=1280):
    im = Image.open(src).convert('RGB')
    if im.width > width:
        im = im.resize((width, round(im.height * width / im.width)))
    im.save(dst, quality=85)


def write_index():
    rows = []
    for bug in sorted(os.listdir(BUGS)) if os.path.isdir(BUGS) else []:
        rp = os.path.join(BUGS, bug, 'report.txt')
        if not os.path.exists(rp):
            continue
        r = read_report(rp)
        rows.append('| [%s](%s/status.md) | %s | %s | %s |' % (bug, bug, read_status(bug), r.get('map', ''),
                                                            r.get('description', '').replace('|', '/')))
    with open(os.path.join(BUGS, 'README.md'), 'w', encoding='utf-8') as f:
        f.write('# Bug reports' + NL + NL)
        f.write('Made in game with `reportbug <what is wrong>`; `gotobug <id>` returns to the spot. Managed with '
                '`pipeline/bugs.py` (import, list, repro, fix). Maps ending in .map were reported in the Godot game. '
                'Tricky bugs are also grouped by the code they exercise in [regression](regression/README.md).'
                + NL + NL)
        f.write('| Id | Status | Map | Description |' + NL + '|---|---|---|---|' + NL)
        f.write(NL.join(rows) + NL)


def cmd_import():
    if not os.path.isdir(INBOX):
        print('no reports in', INBOX)
        return
    os.makedirs(BUGS, exist_ok=True)
    n = 0
    for fn in sorted(os.listdir(INBOX)):
        if not fn.endswith('.txt'):
            continue
        bug = fn[:-4]
        dst = os.path.join(BUGS, bug)
        if os.path.exists(dst):
            continue
        os.makedirs(dst)
        shutil.copy(os.path.join(INBOX, fn), os.path.join(dst, 'report.txt'))
        png = os.path.join(INBOX, bug + '.png')
        if os.path.exists(png):
            save_jpg(png, os.path.join(dst, 'screenshot.jpg'))
        import_recording(bug, dst)
        r = read_report(os.path.join(dst, 'report.txt'))
        with open(os.path.join(dst, 'status.md'), 'w', encoding='utf-8') as f:
            f.write('# %s: %s%s%s' % (bug, r.get('description', ''), NL, NL))
            f.write('Status: open' + NL + NL)
            f.write('## History' + NL + NL + '- %s reported in %s' % (r.get('date', ''), r.get('map', '')) + NL)
        print('imported', bug, '-', r.get('description', ''))
        n += 1
    write_index()
    print('%d new report(s)' % n)


def recording_start(path):
    """the savegame a recording starts from (None: the level's start)"""
    for line in open(path, encoding='utf-8', errors='replace'):
        if line.startswith(('f|', 'c|')):
            break
        if line.startswith('start load '):
            return line[len('start load '):].strip()
    return None


def import_recording(bug, dst):
    rec = os.path.join(INBOX, bug + '.rec')
    if not os.path.exists(rec):
        return
    shutil.copy(rec, os.path.join(dst, 'recording.rec'))
    save = recording_start(rec)
    if save:
        sav = os.path.join(GAME, save + '.sav')
        if os.path.exists(sav):
            shutil.copy(sav, os.path.join(dst, 'start.sav'))
        else:
            print('  %s: the recording starts from %s.sav, which is gone' % (bug, save))


def cmd_list(which='open'):
    if not os.path.isdir(BUGS):
        print('no bugs')
        return
    for bug in sorted(os.listdir(BUGS)):
        rp = os.path.join(BUGS, bug, 'report.txt')
        if not os.path.exists(rp):
            continue
        st = read_status(bug)
        if which != 'all' and st != which:
            continue
        r = read_report(rp)
        print('%s  %-6s %-16s %s' % (bug, st, r.get('map', ''), r.get('description', '')))


def cmd_show(bug):
    for name in ('report.txt', 'status.md'):
        p = os.path.join(BUGS, bug, name)
        if os.path.exists(p):
            print(open(p, encoding='utf-8').read())


def update_status(bug, status, lines):
    p = os.path.join(BUGS, bug, 'status.md')
    text = open(p, encoding='utf-8').read()
    out = []
    for line in text.split(NL):
        out.append('Status: ' + status if line.startswith('Status: ') else line)
    text = NL.join(out).rstrip(NL) + NL
    stamp = datetime.date.today().isoformat()
    text += NL.join('- %s %s' % (stamp, l) for l in lines) + NL
    open(p, 'w', encoding='utf-8').write(text)
    write_index()


def cmd_fix(bug, cause, fix):
    update_status(bug, 'fixed', ['fixed. Cause: ' + cause, 'Fix: ' + fix])
    print(bug, 'marked fixed')


def cmd_note(bug, text):
    update_status(bug, read_status(bug), ['note: ' + text])
    print(bug, 'note added')


def cmd_reopen(bug, note):
    update_status(bug, 'open', ['reopened: ' + note])
    print(bug, 'reopened')


# regression categories: the code a change to which calls for a rerun of the category
CATEGORIES = {
    'room-over-room': 'Room-over-room links (Blood stack links): portal rendering and alignment (PortalRenderer, '
                      'World.NativeLinks, the eye crossing links in Game.cs), sprites and lights seen through portals.',
    'stacked-rooms': 'Rooms stacked so they overlap in height, without a link: StackedRooms.cs (the walk), '
                     'SectorMap.FindOverlaps, sprites hidden by their Build sector.',
    'movers': 'Doors, lifts, sliding/rotating/z-motion sectors: trInit and the busy procedures (World.NativeSectors), '
              "the moving sectors' meshes and planes (Render.Native).",
    'carried-sprites': 'Sprites that move with sectors (cstat 8192/16384, moveZPart/zPart, turntables) and sprites that '
                       'wait inside another sector.',
    'sector-lighting': "Build shading: sector shades, light effects and switches (World.SectorFx, the native world's "
                       'b3d_sector data, BuildShading), sprite shading, overall brightness against NBlood.',
    'dynamic-lights': 'DynamicLights.cs: fixture lights, their switches, shadows, copies through portals, performance.',
    'sprites': 'SpriteRenderer and voxels: offsets, one-sided and wall sprites, translucency, procedural fire, flips.',
    'masked-walls': 'Masked, translucent and one-way walls (cstat 16/32/128/512), palookup hints, double-sided faces.',
    'geometry': 'The native world mesh (Render.Native.WorldMesher, WorldShader): holes, cracks and seams, walls '
                'between sloped planes, texture pegging, sky.',
    'collision': 'Player/actor collision (Sim/Collision): blocking walls and sprites, ledges, link crossing. The '
                 'screenshot only finds the spot: retest by playing there (or bugs.py replay).',
    'performance': 'Frame rate: portal culling, occlusion, hitscan traces, shadow map updates. The screenshot only '
                   'finds the spot: retest by looking there with perf.',
    'water': 'Water and other liquids: entering and leaving through water links, underwater sectors (World.Physics '
             'CheckLink, InUnderwaterSector), swimming, the screen palettes (ViewPalette). The screenshot only finds '
             'the spot: retest by playing the recording (bugs.py replay) and looking at the end.',
}
REGRESSION = os.path.join(BUGS, 'regression')
# the enhanced features (Options > Enhanced: transparent water, fog, ground mist, weather) are off for the shots, as
# ambient occlusion and the overlay are: the baselines are Blood's look; the user's values are put back afterwards
ENHANCED_CVARS = ('r_waterportals', 'r_fog', 'r_groundmist', 'r_weather', 'r_waterwarp', 'r_waterfog', 'r_watermist', 'r_caustics', 'r_waterripple', 'r_watershine', 'r_waterreflect',
                  'r_moonlight', 'r_moonfog', 'r_lightflicker', 'r_fogsky', 'r_lightning', 'r_glow', 'r_areacolor', 'r_embers', 'r_smoke', 'r_aftermath', 'r_rainripples', 'r_dust', 'r_ash')
# (those that are 1 as Blood has them: the brightness, the dynamic lights' reach)
SHOT_ONES = ('brightness', 'r_lightrange')
SHOT_CVARS = ('r_perfoverlay', 'r_ao', 'r_useindexedcolortextures') + SHOT_ONES + ENHANCED_CVARS
SHOT_EXEC = '; '.join('%s %d' % (c, c in SHOT_ONES) for c in SHOT_CVARS)
# (the skill too: on the user's own the views on E3M5's platform and E1M3's train came out different)
REGRESS_EXEC = 'b3d_monsters 0; b3d_hudsize 0; b3d_crosshair 0; b3d_skill 2; ' + SHOT_EXEC
# what the test runs change, put back as the user had them (these values when the config has none)
TEST_CVARS = {'b3d_monsters': '1', 'b3d_hudsize': '3', 'b3d_crosshair': '1', 'b3d_skill': '2', 'b3d_fixblood': '1'}
# dynamic lights and shadows are pinned per bug so a baseline doesn't depend on how the user last toggled them: on for
# the dynamic-lights category, off for the rest; the user's own values are put back afterwards
LIGHT_CVARS = ('r_dynamiclights', 'r_shadows')
CONFIG = os.path.join(os.environ.get('APPDATA', ''), 'Godot', 'app_userdata', 'Chosen', 'config.cfg')


def saved_cvars(names):
    """the user's saved values of these cvars (config.cfg lines: name "value")"""
    out = {}
    if os.path.exists(CONFIG):
        for line in open(CONFIG, encoding='utf-8'):
            parts = line.split(None, 1)
            if len(parts) == 2 and parts[0] in names:
                out[parts[0]] = parts[1].strip().strip('"')
    return out


def restore_exec(more=()):
    """console commands putting back the settings a test run changes, as they are now (read before the run): the
    user's saved values, TEST_CVARS' for those not saved"""
    names = tuple(TEST_CVARS) + tuple(more)
    vals = dict(TEST_CVARS)
    vals.update(saved_cvars(names))
    return '; '.join('%s %s' % (n, vals[n]) for n in names if n in vals)


CHANGED = 0.2           # baseline difference (compare.score) above which a view counts as changed (repeat runs: < 0.1)


# ---------------------------------------------------------------- motion: the rendering steps between game frames
MOTION_DIR = os.path.join(BUGS, 'regression', 'motion')
MOTION_LERPS = (0.1, 0.5, 0.9)
MOTION_BROKEN = 0.03    # % of the view differing from the exact picture above which a blend is broken
MOTION_STILL = 0.05     # % of the view differing between the first and last blend below which nothing moves there
# what a held frame must not depend on: no rain, flicker, fog glow or HUD; water portals on, as the game is played (the
# regression shots have them off, and a boat's slits showed over the water)
MOTION_EXEC = ('brightness 1; r_useindexedcolortextures 0; b3d_monsters 1; b3d_hudsize 0; b3d_crosshair 0; r_perfoverlay 0; r_dynamiclights 0; r_shadows 0; r_ao 0; r_fog 0; '
               'r_groundmist 0; r_weather 0; r_waterportals 1; ' + '; '.join('%s 0' % c for c in ENHANCED_CVARS if c not in ('r_waterportals', 'r_fog', 'r_groundmist', 'r_weather')))
MOTION_CVARS = LIGHT_CVARS + SHOT_CVARS


def motion_gaps(a, b):
    """% of the pixels of a showing what isn't around them in b, either way, and the picture of where: a colour from
    elsewhere, as a gap's background is. An edge landing a pixel over only swaps the colours beside it, and doesn't count
    (E3M5's turning door: the same picture with an edge's rounding, a long line of pixels differing)."""
    la, lb = a.convert('L'), b.convert('L')
    high, low = lb.filter(ImageFilter.MaxFilter(3)), lb.filter(ImageFilter.MinFilter(3))
    out = ImageChops.lighter(ImageChops.subtract(la, high), ImageChops.subtract(low, la))
    high, low = la.filter(ImageFilter.MaxFilter(3)), la.filter(ImageFilter.MinFilter(3))
    out = ImageChops.lighter(out, ImageChops.lighter(ImageChops.subtract(lb, high), ImageChops.subtract(low, lb)))
    h = out.histogram()
    return 100.0 * sum(h[24:]) / (a.width * a.height), out.point(lambda v: 255 if v >= 24 else 0)


def motion_cases(only=None):
    out = []
    for line in open(os.path.join(MOTION_DIR, 'cases.txt'), encoding='utf-8'):
        line = line.strip()
        if line and not line.startswith('#'):
            bug, when, what = [x.strip() for x in line.split('|', 2)]
            if not only or bug in only:
                out.append((bug, when, what))
    return out


def motion_diff(a, b):
    """% of the pixels differing by more than a little, and the picture of where (amplified)"""
    d = ImageChops.difference(a.convert('L'), b.convert('L'))
    h = d.histogram()
    return 100.0 * sum(h[24:]) / (a.width * a.height), d.point(lambda v: 255 if v >= 24 else 0)


def cmd_motion(rest):
    work = os.path.join(ROOT, 'tools', 'regress', 'motion')
    selftest = '--selftest' in rest
    rest = [r for r in rest if not r.startswith('--')]
    if rest and rest[0] == 'accept':
        only = rest[1].split(',') if len(rest) > 1 else None
        os.makedirs(os.path.join(MOTION_DIR, 'baseline'), exist_ok=True)
        for bug, frame, what in motion_cases(only):
            for i in range(len(MOTION_LERPS)):
                src = os.path.join(work, '%s_%d_blend.png' % (bug, i))
                if os.path.exists(src):
                    save_jpg(src, os.path.join(MOTION_DIR, 'baseline', '%s_%d.jpg' % (bug, i)))
            print(bug, 'baseline updated')
        return
    import compare
    only = rest[0].split(',') if rest else None
    cases = motion_cases(only)
    shutil.rmtree(work, ignore_errors=True)
    os.makedirs(work)
    restore = restore_exec(MOTION_CVARS)
    subprocess.run(['dotnet', 'build', os.path.join(GODOT_DIR, 'Chosen.csproj'), '-v', 'q'], check=True, stdout=subprocess.DEVNULL)
    base = [GODOT, '--path', GODOT_DIR, '--windowed'] + OFFSCREEN + ['--audio-driver', 'Dummy', '--resolution', '1280x720', '--']
    lerps = ','.join(str(l) for l in MOTION_LERPS)
    rows, broken, changed, still, missing = [], 0, 0, 0, 0
    try:
        for bug, frame, what in cases:
            prefix = os.path.join(work, bug).replace(os.sep, '/')
            exec_ = MOTION_EXEC + ('; r_meshblend 2' if selftest else '')
            if '@' in frame:
                # at the report's spot, a mover set going: that long after (no monsters: a level loaded anew has no replay's
                # random numbers, and what they do differs from run to run)
                cmds, _, secs = frame.rpartition('@')
                level = os.path.splitext(os.path.basename(read_report(os.path.join(BUGS, bug, 'report.txt'))['map']))[0]
                args = ['--scripted', '--map=' + level, '--exec=%s; b3d_monsters 0; restart; gotobug %s; %s; motionshots %s %s quit after=%s' % (exec_, bug, cmds.strip(), prefix, lerps, secs.strip())]
            else:
                # a report's recording replayed to where things move
                args = ['--scripted', '--play=' + bug, '--speed=50', '--exec=' + exec_, '--atend=motionshots %s %s quit' % (prefix, lerps)]
                if frame != '0':
                    args.append('--frame=' + frame)
            try:
                subprocess.run(base + args, capture_output=True, timeout=600)
            except subprocess.TimeoutExpired:
                pass
            shots = [(os.path.join(work, '%s_%d_blend.png' % (bug, i)), os.path.join(work, '%s_%d_exact.png' % (bug, i))) for i in range(len(MOTION_LERPS))]
            if not all(os.path.exists(a) and os.path.exists(b) for a, b in shots):
                print('%s  MISSING  (no shots) %s' % (bug, what))
                missing += 1
                rows.append((bug, what, 'MISSING', '', []))
                continue
            moved, _ = motion_diff(Image.open(shots[0][1]), Image.open(shots[-1][1]))
            worst, pics, base_score, new = 0.0, [], 0.0, False
            for i, (a, b) in enumerate(shots):
                ia, ib = Image.open(a).convert('RGB'), Image.open(b).convert('RGB')
                pct, mask = motion_gaps(ia, ib)
                worst = max(worst, pct)
                pics.append((ia, ib, mask, pct))
                bl = os.path.join(MOTION_DIR, 'baseline', '%s_%d.jpg' % (bug, i))
                if os.path.exists(bl):
                    sc, _ = compare.score(Image.open(bl).convert('RGB').resize((1280, 720)), ia)
                    base_score = max(base_score, sc)
                else:
                    new = True
            state = 'BROKEN' if worst > MOTION_BROKEN else 'CHANGED' if base_score > CHANGED else 'still' if moved < MOTION_STILL else 'new' if new else 'same'
            broken += state == 'BROKEN'
            changed += state == 'CHANGED'
            still += state == 'still'
            print('%s  %-7s  against exact %.3f%%  baseline %.2f  moves %.2f%%  %s' % (bug, state, worst, base_score, moved, what))
            rows.append((bug, what, state, 'against exact %.3f%%, baseline %.2f, moves %.2f%%' % (worst, base_score, moved), pics))
    finally:
        # the settings the run changed, put back in a run of their own
        subprocess.run(base + ['--map=e1m1', '--exec=' + restore, '--wait=0.3', '--shot=' + os.path.join(work, 'restore.png').replace(os.sep, '/')],
                       capture_output=True, timeout=300)
    # the sheet: each case's worst blend as drawn, exact, and where they differ
    w, h = 512, 288
    sheet = Image.new('RGB', (w * 3, (h + 16) * len(rows)), (20, 0, 30))
    drw = ImageDraw.Draw(sheet)
    for r, (bug, what, state, text, pics) in enumerate(rows):
        y = r * (h + 16)
        drw.text((4, y + 2), '%s %s %s %s' % (bug, state, text, what), fill=(255, 255, 0))
        if pics:
            ia, ib, mask, pct = max(pics, key=lambda p: p[3])
            for k, im in enumerate((ia, ib, mask.convert('RGB'))):
                sheet.paste(im.resize((w, h)), (k * w, y + 16))
    if not selftest:
        sheet.save(os.path.join(MOTION_DIR, 'sheet.jpg'), quality=85)
    else:
        sheet.save(os.path.join(work, 'selftest.jpg'), quality=85)
    print('%d broken, %d changed, %d not moving, %d missing of %d%s' % (broken, changed, still, missing, len(cases), ' (selftest: r_meshblend 2; the boat, the yard door and E2M1 must show BROKEN)' if selftest else ''))


def read_field(bug, key):
    p = os.path.join(BUGS, bug, 'status.md')
    if os.path.exists(p):
        for line in open(p, encoding='utf-8'):
            if line.startswith(key + ': '):
                return line[len(key) + 2:].rstrip(NL)
    return ''


def write_fields(bug, fields):
    """set (or with '' remove) "key: value" lines placed under the Status line of status.md"""
    p = os.path.join(BUGS, bug, 'status.md')
    lines = open(p, encoding='utf-8').read().split(NL)
    lines = [l for l in lines if not any(l.startswith(k + ': ') for k in fields)]
    at = next(i for i, l in enumerate(lines) if l.startswith('Status: ')) + 1
    lines[at:at] = ['%s: %s' % (k, v) for k, v in fields.items() if v]
    open(p, 'w', encoding='utf-8').write(NL.join(lines))


def categories_of(bug):
    return [c.strip() for c in read_field(bug, 'Regression').split(',') if c.strip()]


def all_bugs():
    return sorted(b for b in os.listdir(BUGS) if os.path.exists(os.path.join(BUGS, b, 'report.txt')))


def cmd_tag(bug, cats, setup=None, seconds=None, check=None):
    new = [c.strip() for c in cats.split(',') if c.strip()]
    for c in new:
        if c not in CATEGORIES:
            sys.exit('unknown category %s (bugs.py categories)' % c)
    have = categories_of(bug)
    fields = {'Regression': ', '.join(have + [c for c in new if c not in have])}
    if setup is not None:
        fields['Regression setup'] = setup + (' | ' + seconds if setup and seconds else '')
    if check is not None:
        fields['Regression check'] = check
    write_fields(bug, fields)
    print(bug, 'regression:', fields['Regression'])


def cmd_untag(bug, cat=None):
    have = [c for c in categories_of(bug) if cat and c != cat]
    fields = {'Regression': ', '.join(have)}
    if not have:
        fields.update({'Regression setup': '', 'Regression check': ''})
    write_fields(bug, fields)
    print(bug, 'regression:', fields['Regression'] or '(none)')


def cmd_categories():
    for c, what in CATEGORIES.items():
        n = sum(1 for b in all_bugs() if c in categories_of(b))
        print('%-16s %3d  %s' % (c, n, what))


def report_map(bug):
    m = read_report(os.path.join(BUGS, bug, 'report.txt')).get('map', '')
    return os.path.splitext(os.path.basename(m))[0].lower()


def thumb(path, w=384, h=216, label=None):
    from PIL import ImageDraw
    im = Image.new('RGB', (w, h), (40, 0, 40))
    if path and os.path.exists(path):
        src = Image.open(path).convert('RGB')
        s = min(w / src.width, h / src.height)
        src = src.resize((round(src.width * s), round(src.height * s)))
        im.paste(src, ((w - src.width) // 2, (h - src.height) // 2))
    if label:
        d = ImageDraw.Draw(im)
        d.rectangle((0, 0, 8 + 6 * len(label), 14), fill=(0, 0, 0))
        d.text((4, 2), label, fill=(255, 255, 0))
    return im


def make_mapx(levels):
    """Each level's .MAP (and .patch) as tools/mapx/<NAME>.mapx (maptool tomapx); the folder."""
    out = os.path.join(ROOT, 'tools', 'mapx')
    os.makedirs(out, exist_ok=True)
    maps = glob.glob(os.path.join(ROOT, 'game', 'blood3d', 'maps', '*.map')) + glob.glob(os.path.join(ROOT, 'game', 'blood3d', 'addons', '*', 'maps', '*.map'))
    by_name = {os.path.splitext(os.path.basename(p))[0].lower(): p for p in maps}
    tool = os.path.join(ROOT, 'shared', 'Chosen.MapTool')
    subprocess.run(['dotnet', 'build', tool, '-v', 'q'], check=True, stdout=subprocess.DEVNULL)
    for lv in sorted(set(levels)):
        if lv not in by_name:
            sys.exit('no .MAP for %s' % lv)
        subprocess.run(['dotnet', 'run', '--project', tool, '--no-build', '--', 'tomapx', by_name[lv],
                        os.path.join(out, lv.upper() + '.mapx')], check=True, stdout=subprocess.DEVNULL)
    print('mapx: %d level(s) converted into %s' % (len(set(levels)), out))
    return out


def cmd_regress(which='all', nblood=False, mapx=False):
    import compare
    ids = [w for w in which.split(',') if os.path.isdir(os.path.join(BUGS, w))]
    cats = list(CATEGORIES) if which == 'all' else sorted({c for b in ids for c in categories_of(b)}) if ids else [which]
    for c in cats:
        if c not in CATEGORIES:
            sys.exit('unknown category %s (bugs.py categories)' % c)
    todo = ids or [b for b in all_bugs() if set(categories_of(b)) & set(cats)]
    if not todo:
        print('no bugs in', which)
        return
    todo.sort(key=lambda b: (report_map(b), b))
    work = os.path.join(ROOT, 'tools', 'regress')
    shots = os.path.join(work, 'shots')
    shutil.rmtree(shots, ignore_errors=True)
    os.makedirs(shots)
    lst = os.path.join(work, 'bugs.txt')
    # put back as found after the run (HUD, monsters, ambient occlusion off for the shots; lights pinned per bug)
    restore = restore_exec(LIGHT_CVARS + SHOT_CVARS)
    with open(lst, 'w', encoding='utf-8') as f:
        for b in todo:
            setup, _, secs = read_field(b, 'Regression setup').partition(' | ')
            on = '1' if 'dynamic-lights' in categories_of(b) else '0'
            # (the light from the sky off again: a bug's setup turning it on stayed on for the bugs after it in the batch)
            # (Blood's bugs fixed, the game's default: a bug's setup may turn it off to see Blood's own behaviour)
            setup = '; '.join(['%s %s' % (c, on) for c in LIGHT_CVARS] + ['r_moonlight 0', 'b3d_fixblood 1'] + ([setup] if setup.strip() else []))
            f.write('%s|%s|%s%s' % (b, setup, secs.strip(), NL))
    print('regress: %d bug(s) in %s' % (len(todo), ', '.join(cats)))
    subprocess.run(['dotnet', 'build', os.path.join(GODOT_DIR, 'Chosen.csproj'), '-v', 'q'], check=True,
                   stdout=subprocess.DEVNULL)
    base = [GODOT, '--path', GODOT_DIR, '--windowed'] + OFFSCREEN + ['--audio-driver', 'Dummy', '--resolution', '1280x720', '--']
    first = report_map(todo[0])
    run_exec, start = REGRESS_EXEC, '--map=' + first
    if mapx:
        # every level from its .MAPX: the first as the file itself (--exec runs after it loads), the others through
        # fs_mapx (unsaved: the restore run below needs nothing for it)
        folder = make_mapx([first] + [report_map(b) for b in todo]).replace(os.sep, '/')
        run_exec += '; fs_mapx ' + folder
        start = '--file=%s/%s.mapx' % (folder, first.upper())
    ran = subprocess.run(base + [start, '--exec=' + run_exec, '--bugs=' + lst.replace(os.sep, '/'),
                                 '--bugshots=' + shots.replace(os.sep, '/')], capture_output=True, timeout=60 * 30)
    # (the graphics driver out of bindings: what comes next isn't drawn, a leak from level to level; no picture says why)
    ranout = (ran.stdout + ran.stderr).count(b'not enough room')
    if ranout:
        print('D3D12 DESCRIPTOR HEAP RAN OUT %d times in this run: materials or pictures kept from level to level?' % ranout)
    # the settings the run changed, put back in a run of their own; checked after, and again if the batch game was
    # still saving its own as it quit (a full run once left HUD, monsters and ambient occlusion off)
    want = dict(c.split(None, 1) for c in restore.split('; '))
    for attempt in range(3):
        if attempt:
            time.sleep(3)
        subprocess.run(base + ['--map=' + first, '--exec=' + restore, '--wait=0.3',
                               '--shot=' + os.path.join(work, 'restore.png').replace(os.sep, '/')], capture_output=True,
                       timeout=300)
        now = saved_cvars(tuple(want))
        off = [n for n, v in want.items() if n in now and now[n] != v]
        if not off:
            break
        print('settings not restored (%s), again' % ', '.join(off))
    results = {}
    for b in todo:
        src = os.path.join(shots, b + '.png')
        if not os.path.exists(src):
            results[b] = ('missing', None)
            continue
        now = os.path.join(BUGS, b, 'regress.jpg')
        save_jpg(src, now)
        bl = os.path.join(BUGS, b, 'baseline.jpg')
        if not os.path.exists(bl):
            shutil.copy(now, bl)
            results[b] = ('new baseline', None)
            continue
        sc, _ = compare.score(Image.open(bl).convert('RGB').resize((1280, 720)), Image.open(now).convert('RGB'))
        results[b] = ('CHANGED' if sc > CHANGED else 'same', sc)
    if nblood:
        for b in todo:
            if not os.path.exists(os.path.join(BUGS, b, 'nblood.jpg')):
                cmd_nblood(b)
    stamp = datetime.datetime.now().strftime('%Y-%m-%d %H:%M')
    # the last result of every bug, so a run of a few bugs leaves the others' in the category pages
    import json
    store = os.path.join(REGRESSION, 'results.json')
    os.makedirs(REGRESSION, exist_ok=True)
    saved = json.load(open(store, encoding='utf-8')) if os.path.exists(store) else {}
    for b, (st, sc) in results.items():
        saved[b] = [st, sc, stamp]
    with open(store, 'w', encoding='utf-8') as f:
        json.dump(saved, f, indent=1, sort_keys=True)
    for c in cats:
        write_category(c, saved, set(results))
    write_regression_index()
    for b in todo:
        st, sc = results[b]
        print('%s  %-12s %5s  %s' % (b, st, '' if sc is None else '%.2f' % sc,
                                     read_report(os.path.join(BUGS, b, 'report.txt')).get('description', '')[:70]))
    changed = [b for b in todo if results[b][0] in ('CHANGED', 'missing')]
    print('%d changed or missing of %d (bugs/regression/<category>/README.md)' % (len(changed), len(todo)))


def write_category(cat, saved, ran):
    """bugs/regression/<cat>/README.md and sheet.jpg from the saved results (bug: [status, score, when]); ran: the
    bugs of this run"""
    bugs_in = [b for b in all_bugs() if cat in categories_of(b)]
    d = os.path.join(REGRESSION, cat)
    os.makedirs(d, exist_ok=True)
    rows, sheet = [], []
    imgs = [('report', 'screenshot.jpg'), ('NBlood', 'nblood.jpg'), ('baseline', 'baseline.jpg'), ('now', 'regress.jpg')]
    for b in bugs_in:
        r = read_report(os.path.join(BUGS, b, 'report.txt'))
        st, sc, when = saved.get(b, ('not run', None, ''))
        check = read_field(b, 'Regression check')
        setup = read_field(b, 'Regression setup')
        rows.append('### [%s](../../%s/status.md) %s' % (b, b, r.get('description', '')))
        rows.append('')
        rows.append('%s, %s%s. `gotobug %s`%s%s' % (
            report_map(b), st + (' ' + when if when else ''), '' if sc is None else ' (%.2f)' % sc, b,
            ', setup `%s`' % setup if setup else '', '. Check: ' + check if check else ''))
        rows.append('')
        rows.append(' '.join('[%s](../../%s/%s)' % (n, b, f) for n, f in imgs
                             if os.path.exists(os.path.join(BUGS, b, f))))
        rows.append('')
        if b in saved:
            sheet.append([thumb(os.path.join(BUGS, b, f), label=(b + ' ' + st if n == 'report' else n))
                          for n, f in imgs])
    with open(os.path.join(d, 'README.md'), 'w', encoding='utf-8') as f:
        f.write('# Regression: %s%s%s%s%s' % (cat, NL, NL, CATEGORIES[cat], NL + NL))
        f.write('%d bug(s), %d in the last run. Rerun: `python pipeline/bugs.py regress %s` ([sheet.jpg](sheet.jpg): '
                'report, NBlood, baseline, now).%s' % (len(bugs_in), len(ran & set(bugs_in)), cat, NL + NL))
        f.write(NL.join(rows) + NL)
    if sheet:
        w, h = 384, 216
        im = Image.new('RGB', (4 * w, len(sheet) * h))
        for y, row in enumerate(sheet):
            for x, t in enumerate(row):
                im.paste(t, (x * w, y * h))
        im.save(os.path.join(d, 'sheet.jpg'), quality=80)


def write_regression_index():
    os.makedirs(REGRESSION, exist_ok=True)
    with open(os.path.join(REGRESSION, 'README.md'), 'w', encoding='utf-8') as f:
        f.write('# Regression categories' + NL + NL)
        f.write('Tricky bugs grouped by the code they exercise (the bugs themselves stay in bugs/<id>/). After '
                'changing that code run `python pipeline/bugs.py regress <category>`: it retakes each bug\'s view in '
                'the Godot game and flags the ones that differ from the accepted baseline; `bugs.py accept <id>` '
                'makes a checked new look the baseline. Tag a bug with `bugs.py tag <id> <category>`.' + NL + NL)
        f.write('| Category | Bugs | Covers |' + NL + '|---|---|---|' + NL)
        for c, what in CATEGORIES.items():
            n = sum(1 for b in all_bugs() if c in categories_of(b))
            f.write('| [%s](%s/README.md) | %d | %s |%s' % (c, c, n, what, NL))


def cmd_accept(which):
    todo = [which] if os.path.isdir(os.path.join(BUGS, which)) else [b for b in all_bugs() if which in categories_of(b)]
    for b in todo:
        src = os.path.join(BUGS, b, 'regress.jpg')
        if os.path.exists(src):
            shutil.copy(src, os.path.join(BUGS, b, 'baseline.jpg'))
            print(b, 'baseline updated')


def is_godot(bug):
    return read_report(os.path.join(BUGS, bug, 'report.txt')).get('engine') == 'godot'


def run_godot(args, timeout):
    """the Godot game, windowed at 1280x720 without sound; returns its output"""
    subprocess.run(['dotnet', 'build', os.path.join(GODOT_DIR, 'Chosen.csproj'), '-v', 'q'], check=True)
    p = subprocess.run([GODOT, '--path', GODOT_DIR] + OFFSCREEN + ['--audio-driver', 'Dummy', '--resolution', '1280x720', '--'] + args,
                       capture_output=True, text=True, errors='replace', timeout=timeout)
    return p.stdout


def godot_repro(bug):
    shot = os.path.join(INBOX, bug + '-repro.png')
    out = run_godot(['--exec=god; gotobug ' + bug, '--shot=' + shot.replace(os.sep, '/')], 120)
    for line in out.splitlines():
        if line.startswith(('gotobug', bug)):
            print(line)
    return shot


def godot_replay(bug, end):
    shot = os.path.join(INBOX, bug + '-replay.png')
    args = ['--play=' + bug, '--speed=50', '--shot=' + shot.replace(os.sep, '/')]
    if str(end) != '0':
        args.append('--frame=' + str(end))
    out = run_godot(args, 900)
    for line in out.splitlines():
        if line.startswith(('input replay', 'play ')):
            print(line)
    return shot


def cmd_repro(bug):
    """start the Godot game at the report's spot (gotobug works for any report), screenshot, quit"""
    src = godot_repro(bug)
    if os.path.exists(src):
        save_jpg(src, os.path.join(BUGS, bug, 'repro.jpg'))
        print('saved', os.path.join(BUGS, bug, 'repro.jpg'))
    else:
        print('no screenshot was taken')


def cmd_replay(bug, end='0'):
    """replay the input recording in the Godot game (up to frame end) and save replay.jpg"""
    if not os.path.exists(os.path.join(BUGS, bug, 'recording.rec')):
        print('no recording for', bug)
        return
    shot = godot_replay(bug, end)
    if os.path.exists(shot):
        save_jpg(shot, os.path.join(BUGS, bug, 'replay.jpg'))
        print('saved', os.path.join(BUGS, bug, 'replay.jpg'))


def nblood_shot(mapname, cam, angles, seconds=3.0, skill=2, send=None):
    """NBlood at a camera (Quake units) and angles (pitch yaw): the screenshot it saves (third_party/nblood is
    patched: -b3dview x,y,z,yaw,pitch draws the view from there, -b3dshot s saves it after s seconds and quits)"""
    os.makedirs(NBLOOD_RUN, exist_ok=True)
    cfg = os.path.join(NBLOOD_RUN, 'nblood.cfg')
    with open(cfg, 'w', newline='') as f:
        f.write(NBLOOD_CFG)
    shots = os.path.join(NBLOOD_RUN, 'screenshots')
    if os.path.isdir(shots):
        for fn in os.listdir(shots):
            if fn.startswith('b3dv'):
                os.remove(os.path.join(shots, fn))
    view = 'v%s,%s,%s,%s,%s' % (cam[0], cam[1], cam[2], angles[1], angles[0])     # v: a negative x is no switch
    import addons
    subprocess.run([NBLOOD, '-usecwd', '-nosetup', '-quick'] + addons.nblood_map_args(BLOOD_DIR, mapname) + ['-skill', str(skill),
                    '-b3dview', view, '-b3dshot', str(seconds)] + (['-b3dsend', send] if send else []),
                   cwd=NBLOOD_RUN, timeout=120, env=NBLOOD_ENV,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if os.path.isdir(shots):
        for fn in sorted(os.listdir(shots)):
            if fn.startswith('b3dv'):
                return os.path.join(shots, fn)
    return None


def cmd_nblood(bug, seconds=None, send=None):
    """NBlood's view of the report's spot (bugs/<id>/nblood.jpg) and the two side by side (compare.jpg). A Forge
    report (forge/<id>: Help > Report a Problem) gives the 3D preview's camera and, from Play, the sectors to send
    and how long after (nblood_send, nblood_seconds); seconds and send given here override them."""
    r = read_report(os.path.join(BUGS, bug, 'report.txt'))
    if seconds is None:
        seconds = r.get('nblood_seconds', '3')
    if send is None:
        send = r.get('nblood_send') or None
    cam = [float(v) for v in r['camera'].split()]
    ang = [float(v) for v in r['angles'].split()]
    mapname = os.path.splitext(os.path.basename(r.get('map', 'e1m1')))[0].lower()
    src = nblood_shot(mapname, cam, ang, float(seconds), send=send)
    if not src:
        print('NBlood saved no screenshot')
        return
    out = os.path.join(BUGS, bug, 'nblood.jpg')
    save_jpg(src, out)
    print('saved', out)
    shot = os.path.join(BUGS, bug, 'screenshot.jpg')
    if not os.path.exists(shot):
        shot = os.path.join(BUGS, bug, 'screenshot.png')
    if os.path.exists(shot):
        a = Image.open(shot).convert('RGB')
        b = Image.open(src).convert('RGB')
        h = 540
        a = a.resize((round(a.width * h / a.height), h))
        b = b.resize((round(b.width * h / b.height), h))
        c = Image.new('RGB', (a.width + b.width, h))
        c.paste(a, (0, 0))
        c.paste(b, (a.width, 0))
        c.save(os.path.join(BUGS, bug, 'compare.jpg'), quality=85)
        print('saved', os.path.join(BUGS, bug, 'compare.jpg'), '(report | NBlood)')


def main():
    a = sys.argv[1:]
    if not a:
        print(__doc__)
    elif a[0] == 'import':
        cmd_import()
    elif a[0] == 'list':
        cmd_list(a[1] if len(a) > 1 else 'open')
    elif a[0] == 'show':
        cmd_show(a[1])
    elif a[0] == 'repro':
        cmd_repro(a[1])
    elif a[0] == 'replay':
        cmd_replay(a[1], a[2] if len(a) > 2 else '0')
    elif a[0] == 'nblood':
        cmd_nblood(a[1], a[2] if len(a) > 2 else None, a[3] if len(a) > 3 else None)
    elif a[0] == 'fix':
        cmd_fix(a[1], a[2], a[3])
    elif a[0] == 'note':
        cmd_note(a[1], a[2])
    elif a[0] == 'reopen':
        cmd_reopen(a[1], a[2])
    elif a[0] == 'tag':
        cmd_tag(a[1], a[2], *a[3:6])
    elif a[0] == 'untag':
        cmd_untag(a[1], a[2] if len(a) > 2 else None)
    elif a[0] == 'categories':
        cmd_categories()
    elif a[0] == 'regress':
        rest = [x for x in a[1:] if not x.startswith('--')]
        cmd_regress(rest[0] if rest else 'all', '--nblood' in a, '--mapx' in a)
    elif a[0] == 'accept':
        cmd_accept(a[1])
    elif a[0] == 'motion':
        cmd_motion(a[1:])
    else:
        print(__doc__)


if __name__ == '__main__':
    main()
