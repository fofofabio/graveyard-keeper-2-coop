# -*- coding: utf-8 -*-
"""Pictures for the GitHub page and the Steam Workshop item, from a tests/Capture-Media.ps1 run.

    python scripts/Make-Media.py <capture folder> <output folder> [<folder with the language pictures>]

Writes, from the picks below:
  gallery/NN-name.png and .jpg   the picture, cropped, at 1920x1080
  cards/NN-name.png              the same with its title and a line under it
  title.png / title.jpg          the title picture with the lettering (16:9)
  preview.jpg                    square, under 1 MB, for the Workshop item
  languages.png                  one quick message as three players read it
  walking.gif                    the others walking past, for the GitHub page
  captions.md                    the titles and lines, for the posts
"""
import glob
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONT_TITLE = r'C:\Windows\Fonts\georgiab.ttf'
FONT_TEXT = r'C:\Windows\Fonts\segoeui.ttf'
FONT_TEXT_BOLD = r'C:\Windows\Fonts\segoeuib.ttf'
GOLD = (236, 196, 110)
CREAM = (244, 234, 214)

# (source name part, output name, title, line, crop box in the 2560x1440 source or None)
PICKS = [
    ('staged-village-circle-mortimer', 'together', 'Up to four keepers, one world',
     'Everyone in their own look, with their name over their head', None),
    ('staged-graveyard-graves-mortimer', 'graveyard', 'Tend the graveyard together',
     'One shared world: what one player does is there for everyone', None),
    ('scene-offer', 'story-offer', 'Watch story scenes together',
     'When a scene starts for one player, the others are asked whether to watch', None),
    ('sermon-watching', 'sermon', 'Sermons for the whole congregation',
     'Watch a friend preach in the church; faith and money go to the preacher once', None),
    ('chat-mortimer', 'chat', 'Chat and quick messages',
     'T opens the chat; quick messages arrive in each reader\'s own language', None),
    ('fight-notice-en', 'fight', 'One fight at a time',
     'The others are told who is fighting and how it ended; the clock waits for everyone', None),
    ('worklock', 'grave-lock', 'One keeper per grave',
     'While someone works a grave or the autopsy table, the others are told who', None),
    ('pause-coop-controller', 'controller', 'Made for controllers too',
     'The co-op menus use the game\'s own buttons and icons: Xbox, PlayStation, Switch', None),
    ('coop-host', 'host', 'Host over Steam in two clicks',
     'Friends join from their Steam friends list or an invite; no ports to forward', None),
    ('joining-copy', 'joining', 'Join a friend\'s world',
     'Your own saves stay untouched: you play in a copy of the host\'s world', None),
]


def font(path, size):
    return ImageFont.truetype(path, size)


def fit(img, box=None, size=(1920, 1080)):
    if box:
        img = img.crop(box)
    return img.convert('RGB').resize(size, Image.LANCZOS)


def band(img, title, line):
    """A dark band at the foot with the title and its line."""
    w, h = img.size
    out = img.copy()
    shade = Image.new('L', (w, 260), 0)
    d = ImageDraw.Draw(shade)
    for y in range(260):
        d.line([(0, y), (w, y)], fill=int(215 * min(1.0, y / 150.0)))
    black = Image.new('RGB', (w, 260), (12, 10, 14))
    out.paste(black, (0, h - 260), shade)
    d = ImageDraw.Draw(out)
    d.text((90, h - 168), title, font=font(FONT_TITLE, 62), fill=GOLD)
    d.text((92, h - 86), line, font=font(FONT_TEXT, 34), fill=CREAM)
    return out


def lettering(img, big=110, small=40):
    """The title picture's lettering, over a soft dark shade at the top."""
    w, h = img.size
    out = img.copy()
    shade = Image.new('L', (w, h), 0)
    d = ImageDraw.Draw(shade)
    for y in range(int(h * 0.42)):
        d.line([(0, y), (w, y)], fill=int(200 * (1 - y / (h * 0.42))))
    out.paste(Image.new('RGB', (w, h), (10, 8, 12)), (0, 0), shade)
    d = ImageDraw.Draw(out)
    line1 = 'Graveyard Keeper 2'
    line2 = 'CO-OP'
    line3 = '2 to 4 players  ·  Steam or LAN  ·  11 languages'
    f1, f2, f3 = font(FONT_TITLE, small), font(FONT_TITLE, big), font(FONT_TEXT_BOLD, int(small * 0.8))
    for text, f, y, colour in ((line1, f1, int(h * 0.05), CREAM), (line2, f2, int(h * 0.05) + small + 14, GOLD),
                               (line3, f3, int(h * 0.05) + small + big + 40, CREAM)):
        tw = d.textlength(text, font=f)
        # a thin dark outline keeps the letters readable over any ground
        for dx, dy in ((-2, 0), (2, 0), (0, -2), (0, 2)):
            d.text(((w - tw) / 2 + dx, y + dy), text, font=f, fill=(0, 0, 0))
        d.text(((w - tw) / 2, y), text, font=f, fill=colour)
    return out


def find(folders, part):
    # Several capture folders separated by ';': the last one that has the picture wins.
    hit = None
    for folder in folders.split(';'):
        hits = sorted(glob.glob(os.path.join(folder, '*%s*.png' % part)))
        if hits:
            hit = hits[-1]
    return hit


def main():
    src, out = sys.argv[1], sys.argv[2]
    for sub in ('gallery', 'cards'):
        os.makedirs(os.path.join(out, sub), exist_ok=True)
    captions = ['# Pictures for the posts', '']
    n = 0
    for part, name, title, line, box in PICKS:
        path = find(src, part)
        if not path:
            print('missing', part)
            continue
        n += 1
        img = fit(Image.open(path), box)
        img.save(os.path.join(out, 'gallery', '%02d-%s.png' % (n, name)))
        img.save(os.path.join(out, 'gallery', '%02d-%s.jpg' % (n, name)), quality=90)
        band(img, title, line).save(os.path.join(out, 'cards', '%02d-%s.png' % (n, name)))
        captions += ['**%s** — %s  ' % (title, line), '`gallery/%02d-%s.png`' % (n, name), '']
        print('picked', os.path.basename(path), '->', name)

    hero = find(src, 'staged-town-square-mortimer') or find(src, 'together-village-mortimer')
    if hero:
        img = fit(Image.open(hero))
        t = lettering(img)
        t.save(os.path.join(out, 'title.png'))
        t.save(os.path.join(out, 'title.jpg'), quality=90)
        # Square for the Workshop: the middle of the picture, where the players stand.
        full = Image.open(hero).convert('RGB')
        s = min(full.size)
        cx = full.size[0] // 2
        sq = full.crop((cx - s // 2, 0, cx + s // 2, s)).resize((1024, 1024), Image.LANCZOS)
        sq = lettering(sq, big=150, small=46)
        q = 90
        while True:
            p = os.path.join(out, 'preview.jpg')
            sq.save(p, quality=q)
            if os.path.getsize(p) < 1000 * 1000 or q < 50:
                break
            q -= 5
        print('title and preview from', os.path.basename(hero), 'preview quality', q)

    # One quick message as three players read it (the chat lines sit at the lower left).
    lang_src = sys.argv[3] if len(sys.argv) > 3 else src
    parts = [(find(lang_src, 'quick-message-' + lang), label) for lang, label in (('en', 'English'), ('de', 'Deutsch'), ('ja', '日本語'))]
    parts = [(p, l) for p, l in parts if p]
    if parts:
        # The chat line at the foot of the screen, doubled with hard edges (it is a pixel font).
        crops = [Image.open(p).convert('RGB').resize((2560, 1440), Image.NEAREST).crop((0, 1268, 640, 1322)).resize((1280, 108), Image.NEAREST) for p, _ in parts]
        w = max(c.size[0] for c in crops)
        h = sum(c.size[1] + 70 for c in crops) + 40
        sheet = Image.new('RGB', (w + 80, h), (18, 15, 20))
        d = ImageDraw.Draw(sheet)
        y = 30
        label_font = font(r'C:\Windows\Fonts\YuGothB.ttc', 34) if os.path.exists(r'C:\Windows\Fonts\YuGothB.ttc') else font(FONT_TEXT_BOLD, 34)
        for (p, label), c in zip(parts, crops):
            d.text((40, y), label, font=label_font, fill=GOLD)
            sheet.paste(c, (40, y + 50))
            y += c.size[1] + 70
        sheet.save(os.path.join(out, 'languages.png'))
        captions += ['**Everyone in their own language** — one quick message, as three players read it  ', '`languages.png`', '']
        print('languages from', len(parts), 'pictures')

    # The highlights as a short GIF for the top of the GitHub page: the cards, a moment each.
    cards = [os.path.join(out, 'cards', '%02d-%s.png' % (i + 1, name)) for i, (_, name, _, _, _) in enumerate(PICKS)]
    cards = [c for c in cards if os.path.exists(c)][:6]
    if cards:
        imgs = [Image.open(c).convert('RGB').resize((960, 540), Image.LANCZOS) for c in cards]
        pal = Image.new('RGB', (960, 540 * len(imgs)))
        for i, im in enumerate(imgs):
            pal.paste(im, (0, 540 * i))
        pal = pal.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
        seq = [im.quantize(palette=pal, dither=Image.Dither.FLOYDSTEINBERG) for im in imgs]
        seq[0].save(os.path.join(out, 'highlights.gif'), save_all=True, append_images=seq[1:], duration=2200, loop=0, optimize=True)
        captions += ['**Highlights** — the cards as a short GIF for the top of the GitHub page  ', '`highlights.gif`', '']
        print('highlights gif from', len(cards), 'cards, %.1f MB' % (os.path.getsize(os.path.join(out, 'highlights.gif')) / 1e6))

    frames = sorted(glob.glob(os.path.join(src.split(';')[-1], 'gif-frames', 'frame-*.png')))
    if len(frames) >= 20:
        first = Image.open(frames[0])
        w, h = first.size
        box = (int(w * 0.22), int(h * 0.20), int(w * 0.78), int(h * 0.62))
        imgs = [Image.open(f).convert('RGB').crop(box).resize((840, int(840 * (box[3] - box[1]) / (box[2] - box[0]))), Image.LANCZOS) for f in frames]
        pal = imgs[len(imgs) // 2].quantize(colors=255, method=Image.Quantize.MEDIANCUT)
        seq = [im.quantize(palette=pal, dither=Image.Dither.NONE) for im in imgs]
        seq[0].save(os.path.join(out, 'walking.gif'), save_all=True, append_images=seq[1:], duration=100, loop=0, optimize=True)
        captions += ['**Walking together** — the others pass by on a joiner\'s screen  ', '`walking.gif`', '']
        print('gif from', len(frames), 'frames, %.1f MB' % (os.path.getsize(os.path.join(out, 'walking.gif')) / 1e6))

    with open(os.path.join(out, 'captions.md'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(captions) + '\n')
    print('done:', out)


if __name__ == '__main__':
    main()
