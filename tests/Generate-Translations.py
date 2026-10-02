# -*- coding: utf-8 -*-
"""Writes src/GK2Coop/CoopText.<lang>.cs from tests/translations.py (German is kept by hand).

Run: python tests/Generate-Translations.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import translations  # noqa: E402

OUT = os.path.join(HERE, '..', 'src', 'GK2Coop')


def cs(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'


def main():
    for lang, (method, suffix) in translations.LANGS.items():
        lines = [
            '// Generated from tests/translations.py by tests/Generate-Translations.py; edit there.',
            'using System.Collections.Generic;',
            '',
            'namespace GK2Coop',
            '{',
            '    internal static partial class L',
            '    {',
            '        private static void %s(Dictionary<string, Dictionary<string, string>> tables)' % method,
            '        {',
            '            tables[%s] = new Dictionary<string, string>' % cs(lang),
            '            {',
        ]
        for english, values in translations.T.items():
            value = values.get(lang)
            if value is None:
                continue
            lines.append('                [%s] = %s,' % (cs(english), cs(value)))
        lines += ['            };', '        }', '    }', '}', '']
        path = os.path.join(OUT, 'CoopText.%s.cs' % suffix)
        with open(path, 'w', encoding='utf-8', newline='\r\n') as f:
            f.write('\n'.join(lines))
        print('wrote', os.path.basename(path), len(translations.T), 'entries')


if __name__ == '__main__':
    main()
