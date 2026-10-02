"""Checks the mod's translations: every text the code shows through L.T / L.F or the menu's
ui.Label / ui.Button / ui.Title must have an entry in every language table, every entry must
keep the English text's {0} {1} placeholders, and no table may carry keys the code no longer uses.

Run: python tests/Check-Translations.py   (exit code 1 when something is missing)
"""
import glob
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'GK2Coop')

# C# string literal (no verbatim strings are used for player text)
LIT = r'"((?:[^"\\]|\\.)*)"'


def unescape(s):
    return s.encode('utf-8').decode('unicode_escape').encode('latin-1').decode('utf-8') if '\\' in s else s


def used_keys():
    keys = {}
    for path in glob.glob(os.path.join(ROOT, '*.cs')):
        name = os.path.basename(path)
        if name.startswith('CoopText'):
            continue
        text = open(path, encoding='utf-8-sig').read()
        for m in re.finditer(r'\bL\.(?:T|F|Key)\(\s*' + LIT, text):
            keys.setdefault(unescape(m.group(1)), name)
        if name == 'CoopMenu.cs':
            # The menu's UI adapters translate what pages draw.
            for m in re.finditer(r'\bui\.(?:Label|Button|Title)\(\s*' + LIT, text):
                keys.setdefault(unescape(m.group(1)), name)
            # Address labels are translated where they are collected.
            for m in re.finditer(r'return\s+' + LIT + r';', text[text.find('private static string Describe(NetworkInterface'):]):
                keys.setdefault(unescape(m.group(1)), name)
    return keys


def tables():
    found = {}
    for path in glob.glob(os.path.join(ROOT, 'CoopText*.cs')):
        text = open(path, encoding='utf-8-sig').read()
        for block in re.finditer(r'\["(?P<lang>[a-z_-]+)"\]\s*=\s*new Dictionary<string, string>\s*\{(?P<body>.*?)\n\s*\}', text, re.S):
            entries = {}
            for m in re.finditer(r'\[' + LIT + r'\]\s*=\s*' + LIT + r'(?:\s*\+\s*' + LIT + r')*', block.group('body')):
                entries[unescape(m.group(1))] = unescape(m.group(2))
            # values split over lines with +
            for m in re.finditer(r'\[' + LIT + r'\]\s*=\s*((?:' + LIT + r'\s*\+?\s*)+),', block.group('body'), re.S):
                parts = re.findall(LIT, m.group(2))
                entries[unescape(m.group(1))] = ''.join(unescape(p) for p in parts)
            found[block.group('lang')] = entries
    return found


def placeholders(s):
    return sorted(set(re.findall(r'\{\d+\}', s)))


def main():
    keys = used_keys()
    langs = tables()
    # Texts that stay as they are in every language (names, numbers, the brand).
    same_everywhere = {'Tailscale', 'Port', 'OK', 'Version {0}', 'Ping {0} ms', '(in {0})'}
    problems = 0
    if not langs:
        print('No language tables found.')
        return 1
    for lang, entries in sorted(langs.items()):
        missing = [k for k in keys if k not in entries and k not in same_everywhere]
        unused = [k for k in entries if k not in keys]
        broken = [k for k, v in entries.items() if placeholders(k) != placeholders(v)]
        untranslated = [k for k, v in entries.items() if v == k and k not in same_everywhere and len(k) > 3]
        print('%-6s %3d entries  missing=%d unused=%d placeholders=%d unchanged=%d' % (lang, len(entries), len(missing), len(unused), len(broken), len(untranslated)))
        for k in missing:
            print('   missing:', k, '(' + keys[k] + ')')
        for k in unused:
            print('   unused :', k)
        for k in broken:
            print('   placeholders differ:', k, '->', entries[k])
        for k in untranslated:
            print('   same as English:', k)
        problems += len(missing) + len(broken) + len(unused)
    print('%d texts in the code, %d languages besides English' % (len(keys), len(langs)))
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
