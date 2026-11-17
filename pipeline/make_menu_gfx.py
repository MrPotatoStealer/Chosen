"""Render the images DarkPlaces' built-in menu expects (gfx/*.png) with Blood's fonts and art.

usage: python -I make_menu_gfx.py [game_dir]

Without these, DarkPlaces shows its "missing content" fallback menu. Item images use Blood's menu font
(tiles 4192+), titles the gothic font (4288+). Items are 20 pixels apart, as the Quake menu code expects.
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'game', 'blood3d')
MASKED = os.path.join(GAME, 'textures', 'blood_masked')
OPAQUE = os.path.join(GAME, 'textures', 'blood')
OUT = os.path.join(GAME, 'gfx')

FONT_MENU = (4192, 1)    # base tile, spacing
FONT_TITLE = (4288, 1)
FONT_SMALL = (4384, 1)


def glyph(base, ch):
    p = os.path.join(MASKED, '%04d.png' % (base + ((ord(ch) - 32) & 127)))
    return Image.open(p).convert('RGBA') if os.path.exists(p) else None


def text_image(s, font):
    base, space = font
    gl = [glyph(base, c) for c in s]
    w = sum((g.width + space) if g else 6 for g in gl)
    h = max((g.height for g in gl if g), default=8)
    im = Image.new('RGBA', (max(w, 1), h), (0, 0, 0, 0))
    x = 0
    for g in gl:
        if g:
            im.paste(g, (x, h - g.height), g)
            x += g.width + space
        else:
            x += 6
    return im


def item_list(items, width=240, step=20, font=FONT_MENU):
    im = Image.new('RGBA', (width, step * len(items)), (0, 0, 0, 0))
    for i, s in enumerate(items):
        t = text_image(s, font)
        im.paste(t, (0, i * step + (step - t.height) // 2), t)
    return im


def tile(num, masked=True):
    return Image.open(os.path.join(MASKED if masked else OPAQUE, '%04d.png' % num)).convert('RGBA')


def save(im, name):
    im.save(os.path.join(OUT, name + '.png'))


def text_page(lines, font=FONT_SMALL, size=(320, 200)):
    im = Image.new('RGBA', size, (0, 0, 0, 200))
    y = 8
    for s in lines:
        t = text_image(s, font) if s else None
        if t:
            im.paste(t, ((size[0] - t.width) // 2, y), t)
        y += 11
    return im


def main():
    os.makedirs(OUT, exist_ok=True)
    save(item_list(['NEW GAME', 'MULTIPLAYER', 'OPTIONS', 'HELP', 'QUIT']), 'mainmenu')
    save(item_list(['NEW GAME', 'LOAD GAME', 'SAVE GAME']), 'sp_menu')
    save(item_list(['JOIN A GAME', 'NEW GAME', 'SETUP']), 'mp_menu')
    for name, title in (('ttl_main', 'BLOOD'), ('ttl_sgl', 'SINGLE PLAYER'), ('p_option', 'OPTIONS'),
                        ('p_load', 'LOAD GAME'), ('p_save', 'SAVE GAME'), ('p_multi', 'MULTIPLAYER'),
                        ('ttl_cstm', 'CONTROLS'), ('vidmodes', 'VIDEO'), ('colorcontrol', 'COLORS')):
        save(text_image(title, FONT_TITLE), name)
    # left plaque: the skull key art; menu cursor: the selection skull from the status bar inventory
    save(tile(2552), 'qplaque')
    for i in range(1, 7):
        save(tile(2559), 'menudot%d' % i)
    # console background: Blood title screen
    bg = tile(2046, masked=False)
    save(bg, 'conback')
    save(text_image('LOADING', FONT_TITLE), 'loading')
    # text box border pieces (save names, player setup)
    for n in ('box_tl', 'box_tm', 'box_tr', 'box_ml', 'box_mm', 'box_mm2', 'box_mr', 'box_bl', 'box_bm', 'box_br'):
        im = Image.new('RGBA', (8, 8), (60, 20, 20, 255) if n.startswith('box_m') and n != 'box_ml' and n != 'box_mr'
                       else (140, 40, 40, 255))
        save(im, n)
    # help pages
    controls = ['CONTROLS', '', 'MOVE  W A S D    RUN  SHIFT', 'JUMP  SPACE    CROUCH  C', 'OPEN / USE  E',
                'FIRE  MOUSE 1    ALT FIRE  MOUSE 2', 'WEAPONS  1-0    NEXT/PREV  WHEEL', 'LAST WEAPON  Q    HOLSTER  X',
                'USE ITEM  ENTER    SELECT ITEM  [ ]', 'MEDKIT  M   CRYSTAL BALL  K', 'BEAST VISION  B   JUMP BOOTS  J',
                'TURN AROUND  BACKSPACE']
    settings = ['SETTINGS (CONSOLE)', '', 'B3D_SKILL 0-4  DIFFICULTY', 'B3D_AUTOAIM 0-2', 'B3D_LEVELSTATS 1',
                'B3D_CROSSHAIR 0/1', 'B3D_HUDSIZE 0-3  OR  - AND = KEYS', '', 'NEW GAME STARTS THE MAP IN', 'STARTMAP_SP',
                '(SEE DEFAULT.CFG)']
    save(text_page(controls), 'help0')
    save(text_page(settings), 'help1')
    for i in range(2, 6):
        save(text_page(controls if i % 2 == 0 else settings), 'help%d' % i)
    print('menu graphics written to', OUT)


if __name__ == '__main__':
    main()
