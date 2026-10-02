"""Every named network message must have its own name: Netcode keeps one handler per name, so a
second module registering the same name silently takes the first one's messages.

Run: python tests/Check-Messages.py   (exit code 1 on a duplicate)
"""
import glob
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'GK2Coop')
names = {}
for path in glob.glob(os.path.join(ROOT, '*.cs')):
    for m in re.finditer(r'const string (\w+) = "(GK2Coop\.[^"]+)"', open(path, encoding='utf-8-sig').read()):
        names.setdefault(m.group(2), []).append(os.path.basename(path) + ':' + m.group(1))
dupes = {k: v for k, v in names.items() if len(v) > 1}
for k, v in sorted(dupes.items()):
    print('DUPLICATE', k, 'in', ', '.join(v))
print('%d message names, %d duplicated' % (len(names), len(dupes)))
sys.exit(1 if dupes else 0)
