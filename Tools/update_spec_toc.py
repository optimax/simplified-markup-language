#!/usr/bin/env python3
"""Regenerates the table of contents of Documents/SML-Language-Specification.md.

Usage:
    python Tools/update_spec_toc.py

The contents are the document's ## and ### headings, linked with the anchors GitHub gives them, written between the
lines <!-- toc --> and <!-- /toc -->. Without those lines, the contents go before the first ## heading. Headings
inside fenced code blocks are ignored. Run it after adding, removing, or renaming a section.
"""
import re
from pathlib import Path

SPEC = Path(__file__).resolve().parent.parent / 'Documents' / 'SML-Language-Specification.md'
START, END = '<!-- toc -->', '<!-- /toc -->'


def anchor(heading: str, seen: dict) -> str:
    """GitHub's anchor for a heading: lowercase, without punctuation other than - and _, spaces as hyphens, and
    -1, -2, ... after repeats."""
    slug = re.sub(r'[^\w\- ]', '', heading.lower()).replace(' ', '-')
    count = seen.get(slug, 0)
    seen[slug] = count + 1
    return slug if count == 0 else f'{slug}-{count}'


def contents(lines: list[str]) -> list[str]:
    toc, seen, fence = [], {}, None
    for line in lines:
        marker = re.match(r'(`{3,}|~{3,})', line)
        if marker:
            if fence is None:
                fence = marker.group(1)
            elif line.startswith(fence) and not line[len(fence):].strip():
                fence = None
            continue
        if fence is not None:
            continue
        m = re.match(r'(#{2,3}) (.+?)\s*$', line)
        if m:
            level, text = len(m.group(1)), m.group(2)
            toc.append('  ' * (level - 2) + f'- [{text}](#{anchor(text, seen)})')
    return toc


def main() -> None:
    text = SPEC.read_text(encoding='utf-8')
    lines = text.split('\n')
    if START in lines and END in lines:
        start, end = lines.index(START), lines.index(END)
        body = lines[:start] + lines[end + 1:]
    else:
        first = next(i for i, line in enumerate(lines) if line.startswith('## '))
        start, body = first, lines
    block = [START, '## Contents', ''] + contents(body) + ['', END]
    if START not in lines:
        block += ['']
    new = lines[:start] + block + (lines[end + 1:] if START in lines else lines[start:])
    SPEC.write_text('\n'.join(new), encoding='utf-8', newline='\n')
    print(f'{SPEC.name}: {len(contents(body))} headings')


if __name__ == '__main__':
    main()
