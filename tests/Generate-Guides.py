# -*- coding: utf-8 -*-
"""Writes the player guides from tests/guides.py, with button names taken from the mod's own
translation tables, so a guide names every button exactly as the game shows it.

English goes to package/INSTALL.txt and package/WORKSHOP-INSTALL.txt; the other languages to
package/guides/INSTALL-GK2COOP.<lang>.txt and package/guides/WORKSHOP-INSTALL.<lang>.txt.
Fails when a guide names a button the mod has no text for.

Run: python tests/Generate-Guides.py
"""
import importlib.util
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, '..')
sys.path.insert(0, HERE)
import guides  # noqa: E402

spec = importlib.util.spec_from_file_location('check', os.path.join(HERE, 'Check-Translations.py'))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)


def version():
    text = open(os.path.join(ROOT, 'src', 'GK2Coop', 'Plugin.cs'), encoding='utf-8-sig').read()
    return re.search(r'Version = "([^"]+)"', text).group(1)


def fill(template, lang, tables, keys):
    def button(m):
        english = m.group(1)
        if english not in keys:
            raise SystemExit('Guide (%s) names a button the mod does not show: %s' % (lang, english))
        if lang == 'en':
            return english
        table = tables.get(lang, {})
        if english not in table:
            raise SystemExit('No %s text for the button: %s' % (lang, english))
        return table[english]
    return re.sub(r'\{B:([^}]+)\}', button, template).replace('{VERSION}', version())


def workshop_with_play(workshop, install):
    """The Workshop guide, then everything of the zip guide after its own install steps (playing,
    keys, what is shared, fights, limits, uninstall, settings): a Workshop subscriber gets no other
    guide. The Workshop guide's own one-line uninstall gives way to the full one; the play steps
    are numbered on from the Workshop steps."""
    own = workshop.rstrip('\n').split('\n\n')[:-1]
    play = install.rstrip('\n').split('\n\n')[2:]
    last = max(int(n) for n in re.findall(r'(?m)^(\d+)\. ', '\n\n'.join(own)))
    counter = [last]

    def renumber(m):
        counter[0] += 1
        return '%d. ' % counter[0]
    play = [re.sub(r'(?m)^\d+\. ', renumber, block) for block in play]
    return '\n\n'.join(own + play) + '\n'


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8-sig', newline='\r\n') as f:
        f.write(text)


def main():
    tables = check.tables()
    keys = check.used_keys()
    out = os.path.join(ROOT, 'package')
    for lang, template in guides.INSTALL.items():
        text = fill(template, lang, tables, keys)
        write(os.path.join(out, 'INSTALL.txt') if lang == 'en' else os.path.join(out, 'guides', 'INSTALL-GK2COOP.%s.txt' % lang), text)
    for lang, template in guides.WORKSHOP.items():
        text = fill(workshop_with_play(template, guides.INSTALL[lang]), lang, tables, keys)
        write(os.path.join(out, 'WORKSHOP-INSTALL.txt') if lang == 'en' else os.path.join(out, 'guides', 'WORKSHOP-INSTALL.%s.txt' % lang), text)
    missing = sorted(set(guides.INSTALL) ^ set(guides.WORKSHOP))
    if missing:
        raise SystemExit('Guides missing for: %s' % ', '.join(missing))
    print('wrote guides for', ', '.join(sorted(guides.INSTALL)))


if __name__ == '__main__':
    main()
