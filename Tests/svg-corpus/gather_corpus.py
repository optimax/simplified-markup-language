#!/usr/bin/env python3
"""Gathers the SVG corpus: downloads the sources in SOURCES at their pinned versions, selects the SVG files,
copies them unchanged, and writes THIRD-PARTY-NOTICES.md with each source's exact license text.

Usage:
    python gather_corpus.py [--dest DIR] [--list]

    --dest DIR   Folder to write to (default: the folder of this script).
    --list       Print the selection only; write nothing.

Needs Python 3.10 or later, git, and network access to registry.npmjs.org and github.com. Downloads go to a
temporary folder that is removed afterwards. In the destination, each source's folder and THIRD-PARTY-NOTICES.md
are replaced; other files are left alone.

The selection is deterministic, so the pinned sources always give the same files. A new source must be under
a license that allows redistribution with the source code, such as MIT, ISC, BSD, Apache-2.0, or CC0, and its files must say
nothing to the contrary: a file with an "All Rights Reserved" notice is never selected, and the run stops if
one is selected by name.
"""
import argparse
import io
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
from dataclasses import dataclass
from pathlib import Path

NOTICES = 'THIRD-PARTY-NOTICES.md'
MAX_BYTES = 200_000
SAFE_NAME = re.compile(r'^[A-Za-z0-9 _.-]+$')

# Brand and product logos are trademarks even where the drawing is MIT-licensed; leave them out.
BRANDS = re.compile(
    r'(^|[-_])(brand|logo|logos|mark-github|github|gitlab|git|google|android|apple|mac|ios|microsoft|windows|'
    r'amazon|aws|facebook|meta|instagram|whatsapp|messenger|twitter|x-twitter|threads|linkedin|youtube|twitch|'
    r'tiktok|snapchat|pinterest|reddit|discord|slack|skype|telegram|signal|wechat|line|mastodon|bluesky|'
    r'dribbble|behance|figma|framer|sketch|codepen|codesandbox|chrome|chromium|firefox|edge|opera|safari|'
    r'paypal|stripe|visa|mastercard|bitcoin|ethereum|steam|playstation|xbox|nintendo|spotify|soundcloud|'
    r'trello|pocket|dropbox|medium|vimeo|wordpress|npm|nodejs|react|angular|vue|python|javascript|'
    r'typescript|html5|css3|docker|kubernetes|ubuntu|debian|tux|linux|copilot|openai|claude|anthropic|'
    r'vercel|netlify|heroku|bootstrap|tailwind|ionic|ionitron|electron|capacitor|stencil|sass|less|'
    r'markdown|yahoo|tumblr|flickr|foursquare|yelp|quora|vk|xing|alipay|wikipedia|duolingo|strava|'
    r'substack|patreon|kickstarter|airbnb|uber|lyft|ebay|etsy|shopify|alexa|cortana|siri|teams|'
    r'outlook|onedrive|bing|office|excel|word|powerpoint|jira|confluence|atlassian|bitbucket|'
    r'unity|unreal|blender|opencollective|buymeacoffee|ko-fi|hacker|stackoverflow|stack-overflow|'
    r'sourcetree|vscode|visual-studio|jetbrains|tor|signal|matrix|sourceforge|litecoin|dogecoin|monero|tether|'
    r'binance|solana|cardano|nvidia|amd|intel|qualcomm|arm|raspberry|arduino|gnome|kde|mozilla|mdn|ny-times|'
    r'nytimes)([-_.]|$)',
    re.IGNORECASE,
)

# More brands, for the sources added later; kept apart so that the earlier sources' selections stay the same.
MORE_BRANDS = re.compile(
    r'(^|[-_])(weibo|taobao|dingtalk|dingding|zhihu|yuque|alibaba|aliwangwang|aliyun|ant|qq|baidu|bilibili|java|'
    r'ruby|dotnet|harmony|open-a-i|ie|x|vk|vkontakte|ok|odnoklassniki|mail|mailru|youla|boom|clips|tiktok|'
    r'telegram|logo|service|services|netflix|tik-tok|whats-app|code-sandbox|dot-net|hugging-face|gemini|deep-seek|'
    r'mistral|ollama|perplexity|qwen|replicate|eleven-labs)([-_.]|$)',
    re.IGNORECASE,
)


def unbranded(files):
    """The files whose names do not match MORE_BRANDS."""
    return {p: data for p, data in files.items() if not MORE_BRANDS.search(p.rsplit('/', 1)[-1][:-4])}


# Test-suite files with these features are always selected, because few files have them.
RARE = re.compile(r'<!DOCTYPE|<!ENTITY|xml:space|xml:lang|<\?xml-stylesheet|encoding="[^Uu]|&#x?[0-9A-Fa-f]+;')


@dataclass(frozen=True)
class Source:
    folder: str        # folder in the corpus
    license: str       # license name for the notices
    location: str      # where the files are in the source, for the notices
    npm: str = ''      # npm package name, or
    git: str = ''      # git repository URL
    version: str = ''  # npm version or git commit
    repo: str = ''     # project repository, for npm packages
    license_file: str = 'LICENSE'
    root: str = ''     # folder in the package or repository that holds the files
    note: str = ''
    notice_file: str = ''        # an Apache-2.0 NOTICE file, copied into the notices too
    files: tuple = ()            # a hand-picked selection, relative to root, instead of a computed one
    max_bytes: int = MAX_BYTES   # the largest file selected


SOURCES = [
    Source('bootstrap-icons', 'MIT', '`icons/`', npm='bootstrap-icons', version='1.13.2',
           repo='https://github.com/twbs/icons', root='icons'),
    Source('feather', 'MIT', '`dist/icons/`', npm='feather-icons', version='4.29.2',
           repo='https://github.com/feathericons/feather', root='dist/icons'),
    Source('flag-icons', 'MIT', '`flags/`', npm='flag-icons', version='7.6.0',
           repo='https://github.com/lipis/flag-icons', root='flags'),
    Source('fluent-emoji', 'MIT', '`assets/<emoji>/<style>/`, stored here under their file names',
           git='https://github.com/microsoft/fluentui-emoji',
           version='1ffb34c752ecf5d402f04cfb4b392c77f57c54bc', root='assets'),
    Source('heroicons', 'MIT', 'the package root', npm='heroicons', version='2.2.0',
           repo='https://github.com/tailwindlabs/heroicons'),
    Source('iconoir', 'MIT', '`icons/`', npm='iconoir', version='7.12.1',
           repo='https://github.com/iconoir-icons/iconoir', root='icons'),
    Source('ionicons', 'MIT', '`dist/svg/`', npm='ionicons', version='8.1.0',
           repo='https://github.com/ionic-team/ionicons', root='dist/svg'),
    Source('lucide', 'ISC', '`icons/`', npm='lucide-static', version='1.66.0',
           repo='https://github.com/lucide-icons/lucide', root='icons'),
    Source('octicons', 'MIT', '`build/svg/`', npm='@primer/octicons', version='19.42.0',
           repo='https://github.com/primer/octicons', root='build/svg'),
    Source('phosphor', 'MIT', '`assets/`', npm='@phosphor-icons/core', version='2.1.1',
           repo='https://github.com/phosphor-icons/phosphor-core', root='assets'),
    Source('resvg', 'MIT (resvg is licensed under Apache-2.0 OR MIT; these files are used under MIT)',
           '`crates/resvg/tests/tests/`', git='https://github.com/linebender/resvg',
           version='cd64e5155706cada17316fc2ed413e673d5de04b', license_file='LICENSE-MIT',
           root='crates/resvg/tests/tests'),
    Source('svg-loaders', 'MIT', 'the package root', npm='svg-loaders', version='0.2.0',
           repo='https://github.com/SamHerbert/SVG-Loaders', license_file='LICENSE.md'),
    Source('svg-spinners', 'MIT', '`svg/`', npm='svg-spinners', version='1.0.0',
           repo='https://github.com/rayliwell/svg-spinners, from https://github.com/n3r4zzurr0/svg-spinners',
           root='svg'),
    Source('tabler', 'MIT', '`icons/`', npm='@tabler/icons', version='3.50.0',
           repo='https://github.com/tabler/tabler-icons', root='icons'),
    Source('weather-icons', 'MIT', 'the package root', npm='@bybas/weather-icons', version='2.0.0',
           repo='https://github.com/basmilius/weather-icons'),
    # Real-world files from around the world: editors' exports, maps, diagrams, and text in several scripts.
    Source('ant-design-icons', 'MIT', '`packages/icons-svg/svg/`',
           git='https://github.com/ant-design/ant-design-icons',
           version='7f2516ac91226d2b41f93b35cb5197c8d94f7189', root='packages/icons-svg/svg'),
    Source('batik', 'Apache-2.0', '`samples/`', git='https://github.com/apache/xmlgraphics-batik',
           version='0d9a9b91931ea7512ee79292f940a10899fee66a',
           license_file='batik-shared-resources/src/main/resources/LICENSE',
           notice_file='batik-shared-resources/src/main/resources/NOTICE', root='samples', max_bytes=300_000,
           files=('GVT.svg', 'anne.svg', 'barChart.svg', 'chessFont.svg', 'chessboard.svg', 'gradients.svg',
                  'henryV.svg', 'mapSpain.svg', 'mapWaadt.svg', 'mathMetal.svg', 'mines.svg', 'moonPhases.svg',
                  'sizeOfSun.svg', 'starfield.svg', 'strokeFont.svg', 'sydney.svg', 'textRotate.svg',
                  'textRotateShadows.svg', 'extensions/flowText.svg', 'extensions/regularPolygon.svg',
                  'tests/spec/fonts/fontArabic.svg', 'tests/spec/text/textBiDi.svg', 'tests/spec12/text/flowBidi.svg'),
           note='The samples that draw the Batik or Apache logo or name are left out.'),
    Source('drawio-diagrams', 'Apache-2.0', '`diagrams/`', git='https://github.com/jgraph/drawio-diagrams',
           version='cc78dc2e7df8cf3ad437e6f876fbc33d2d0b7e01', root='diagrams',
           files=('flowchart.drawio.svg', 'svgfile.svg')),
    Source('noto-emoji', 'Apache-2.0', '`2D/svg/`', git='https://github.com/googlefonts/noto-emoji',
           version='99701e07d3d1fe1bdbcdb14964547f0fd592966f', license_file='2D/svg/LICENSE', root='2D/svg'),
    Source('openstreetmap-carto', 'CC0-1.0', '`symbols/`', git='https://github.com/gravitystorm/openstreetmap-carto',
           version='003cefdd67918e8317e9753312ec3327d78f5d1f', license_file='LICENSE.txt', root='symbols'),
    Source('region-flags', 'Public domain', '`third_party/region-flags/svg/` of Noto Emoji',
           git='https://github.com/googlefonts/noto-emoji', version='99701e07d3d1fe1bdbcdb14964547f0fd592966f',
           license_file='third_party/region-flags/LICENSE', root='third_party/region-flags/svg',
           note='The flags were taken from Wikimedia Commons; the copying notice below lists the ones that are '
                'public domain under national law. Only flags saved by Inkscape are selected.'),
    Source('vk-icons', 'MIT', '`src/svg/`', npm='@vkontakte/icons', version='3.79.0',
           repo='https://github.com/VKCOM/icons', root='src/svg'),
]


def spread(items, k):
    """k items evenly spaced over the sorted list."""
    items = sorted(items)
    n = len(items)
    if k >= n:
        return items
    return [items[int((i + 0.5) * n / k)] for i in range(k)]


def reserved(data):
    """Whether a file carries an "All Rights Reserved" notice, which contradicts free redistribution."""
    return b'all rights reserved' in data.lower()


def ok(path, data, brands=True, max_bytes=MAX_BYTES):
    """Whether a file may be selected: a plain file name, not a brand logo, no "All Rights Reserved" notice, and
    at most max_bytes."""
    name = path.rsplit('/', 1)[-1]
    if not SAFE_NAME.match(name):
        return False
    if brands and BRANDS.search(name[:-4]):
        return False
    return len(data) <= max_bytes and not reserved(data)


def candidates(files, folder='', brands=True, max_bytes=MAX_BYTES):
    """The sorted paths of the selectable files in folder (and its subfolders)."""
    prefix = folder + '/' if folder else ''
    return sorted(p for p, data in files.items() if p.startswith(prefix) and ok(p, data, brands, max_bytes))


def below(files, root):
    """The .svg files below root, keyed by their path relative to root."""
    prefix = root + '/' if root else ''
    return {p[len(prefix):]: data for p, data in files.items() if p.startswith(prefix) and p.endswith('.svg')}


def npm_package(src):
    """The .svg files and the license file of an npm package, keyed by their path in the package."""
    url = f'https://registry.npmjs.org/{src.npm}/-/{src.npm.rsplit("/", 1)[-1]}-{src.version}.tgz'
    with urllib.request.urlopen(url, timeout=600) as response:
        data = response.read()
    files = {}
    with tarfile.open(fileobj=io.BytesIO(data), mode='r:gz') as tar:
        for member in tar:
            path = member.name.split('/', 1)[-1]  # below the tarball's top folder, normally package/
            if member.isfile() and (path.endswith('.svg') or path == src.license_file):
                files[path] = tar.extractfile(member).read()
    return files


def git(folder, *args, text=True):
    return subprocess.run(['git', '-C', str(folder), *args], capture_output=True, text=text, check=True).stdout


def git_fetch(src, work):
    """A blob-less clone of the pinned commit, with nothing checked out yet."""
    folder = work / src.folder
    subprocess.run(['git', 'init', '-q', str(folder)], check=True)
    git(folder, 'remote', 'add', 'origin', src.git)
    git(folder, 'fetch', '-q', '--depth', '1', '--filter=blob:none', 'origin', src.version)
    return folder


def git_checkout(folder, patterns):
    """Checks out only the paths that match the patterns (gitignore syntax)."""
    git(folder, 'sparse-checkout', 'set', '--no-cone', *patterns)
    git(folder, 'checkout', '-q', '--detach', 'FETCH_HEAD')


def select_npm(src, files):
    """The selection from an npm package: [(path in the corpus folder, bytes)]."""
    svgs = below(files, src.root)

    def take(folder, k):
        return [(p, svgs[p]) for p in spread(candidates(svgs, folder), k)]

    if src.folder == 'flag-icons':
        # Every flag in both aspect ratios: countries, regions, organizations, and the placeholder (xx).
        return [(p, svgs[p]) for p in candidates(svgs, brands=False)]
    if src.folder == 'vk-icons':
        # Icons exported from Sketch, which keep its generator comment and layer names.
        sketch = unbranded({p: data for p, data in svgs.items() if b'Generator: Sketch' in data})
        return [(p, svgs[p]) for p in spread(candidates(sketch), 10)]
    if src.folder == 'phosphor':
        # A different three icons in each of the six weights.
        weights = ('thin', 'light', 'regular', 'bold', 'fill', 'duotone')
        chosen = [p for i, w in enumerate(weights) for p in spread(candidates(svgs, w), 18)[3 * i:3 * i + 3]]
        return [(p, svgs[p]) for p in chosen]
    quotas = {
        'lucide': [('', 20)],
        'feather': [('', 15)],
        'heroicons': [('24/outline', 5), ('24/solid', 4), ('20/solid', 3), ('16/solid', 3)],
        'tabler': [('outline', 12), ('filled', 8)],
        'bootstrap-icons': [('', 20)],
        'octicons': [('', 15)],
        'ionicons': [('', 15)],
        'iconoir': [('regular', 10), ('solid', 5)],
        'svg-spinners': [('', 20)],
        'svg-loaders': [('svg-smil-loaders', 12), ('svg-css-loaders', 6)],
        'weather-icons': [('production/fill/all', 12), ('production/line/all', 6), ('design/line/export', 6)],
    }[src.folder]
    return [item for folder, k in quotas for item in take(folder, k)]


def select_fluent(src, work):
    """Fluent Emoji: base emoji only (no skin-tone folders), a different emoji for each style."""
    folder = git_fetch(src, work)
    paths = git(folder, 'ls-tree', '-r', '-z', '--name-only', 'FETCH_HEAD', src.root).split('\0')
    chosen = []
    for style, k, offset in (('Color', 24, 0), ('Flat', 10, 1), ('High Contrast', 6, 2)):
        styled = sorted(p for p in paths
                        if p.endswith('.svg') and p.count('/') == 3 and p.split('/')[2] == style
                        and SAFE_NAME.match(p.split('/')[1]) and SAFE_NAME.match(p.split('/')[3]))
        n = len(styled)
        chosen += [styled[(int((i + 0.5) * n / k) + offset) % n] for i in range(k)]
    git_checkout(folder, ['/' + src.license_file] + ['/' + p for p in chosen])
    selection = [(p.rsplit('/', 1)[-1], (folder / p).read_bytes()) for p in chosen]
    return selection, (folder / src.license_file).read_bytes()


def select_tests(src, work, total):
    """A test suite: every file with a RARE feature, plus an even spread over the rest."""
    folder = git_fetch(src, work)
    git_checkout(folder, ['/' + src.license_file, f'/{src.root}/**/*.svg'])
    root = folder / src.root
    svgs = {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob('*.svg')}
    paths = candidates(svgs, brands=False)
    rare = [p for p in paths if RARE.search(svgs[p].decode('utf-8', errors='replace'))]
    rest = [p for p in paths if p not in rare]
    chosen = sorted(rare + spread(rest, total - len(rare)))
    return [(p, svgs[p]) for p in chosen], (folder / src.license_file).read_bytes()


def select_git(src, work):
    """A repository's files: the hand-picked ones, or a selection by folder."""
    folder = git_fetch(src, work)
    extra = ['/' + src.notice_file] if src.notice_file else []
    patterns = [f'/{src.root}/{p}' for p in src.files] if src.files else [f'/{src.root}/**/*.svg']
    git_checkout(folder, ['/' + src.license_file] + extra + patterns)
    root = folder / src.root
    svgs = {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob('*.svg')}
    if src.files:
        for p in src.files:
            if p not in svgs or not ok(p, svgs[p], max_bytes=src.max_bytes):
                raise ValueError(f'{src.folder}/{p} is missing or may not be selected')
        chosen = list(src.files)
    elif src.folder == 'region-flags':
        inkscape = {p: data for p, data in svgs.items() if b'inkscape:version' in data}
        chosen = spread(candidates(inkscape, brands=False), 10)
    else:
        quotas = {
            'ant-design-icons': [('filled', 4), ('outlined', 4), ('twotone', 4)],
            'noto-emoji': [('', 12)],
            'openstreetmap-carto': [('religion', 8), ('amenity', 4), ('shop', 3), ('historic', 2), ('tourism', 2)],
        }[src.folder]
        chosen = [p for sub, k in quotas for p in spread(candidates(unbranded(svgs), sub, max_bytes=src.max_bytes), k)]
    license_text = (folder / src.license_file).read_bytes()
    if src.notice_file:
        license_text += b'\n\n' + (folder / src.notice_file).read_bytes()
    return [(p, svgs[p]) for p in chosen], license_text


def gather(src, work):
    """Downloads one source; returns ([(path in the corpus folder, bytes)], license text as bytes)."""
    if src.npm:
        files = npm_package(src)
        return select_npm(src, files), files[src.license_file]
    if src.folder == 'fluent-emoji':
        return select_fluent(src, work)
    if src.folder == 'resvg':
        return select_tests(src, work, 100)
    return select_git(src, work)


def notices(results):
    """THIRD-PARTY-NOTICES.md for the gathered sources."""
    out = ['# Third-Party Notices', '',
           'The SVG files in this folder are copies of third-party files, unchanged, kept as test examples. '
           'They are part of the source distribution of this repository only: `smlxml` builds and publishes '
           'do not include them or this file.', '',
           'Each section below names a folder, the source its files were taken from, the location of the files '
           'in that source, and the license, followed by the license text exactly as the source distributes it. '
           'A file keeps its path relative to that location, except where noted. '
           '`gather_corpus.py` writes this file together with the corpus.', '']
    for src, (selection, license_text) in sorted(results.items(), key=lambda item: item[0].folder):
        if src.npm:
            package = f'[`{src.npm}`](https://www.npmjs.com/package/{src.npm})'
            origin = f'npm package {package} {src.version} ({src.repo})'
        else:
            origin = f'{src.git}, commit `{src.version}`'
        text = license_text.decode('utf-8').replace('\r\n', '\n').rstrip('\n')
        if '```' in text:
            raise ValueError(f'the license of {src.folder} contains ```')
        out += [f'## `{src.folder}/`', '',
                f'- Source: {origin}',
                f'- Files: {len(selection)}, from {src.location}',
                f'- License: {src.license}' + (', followed by its NOTICE file' if src.notice_file else ''), '']
        if src.note:
            out += [src.note, '']
        out += ['```text', text, '```', '']
    return '\n'.join(out)


def main():
    parser = argparse.ArgumentParser(description='Gathers the SVG corpus and its third-party notices.')
    parser.add_argument('--dest', type=Path, default=Path(__file__).resolve().parent,
                        help='folder to write to (default: the folder of this script)')
    parser.add_argument('--list', action='store_true', help='print the selection only; write nothing')
    args = parser.parse_args()

    results = {}
    with tempfile.TemporaryDirectory(prefix='svg-corpus-') as temp:
        for src in SOURCES:
            print(f'{src.folder}: {src.npm or src.git} {src.version}', file=sys.stderr, flush=True)
            results[src] = gather(src, Path(temp))
            paths = [p for p, _ in results[src][0]]
            if len(paths) != len(set(paths)):
                raise ValueError(f'{src.folder}: two files have the same name')
            for path, data in results[src][0]:
                if reserved(data):
                    raise ValueError(f'{src.folder}/{path} has an "All Rights Reserved" notice; leave it out')

    if args.list:
        for src, (selection, _) in results.items():
            print(f'{src.folder} ({len(selection)}): ' + ', '.join(p for p, _ in selection))
    else:
        for src, (selection, _) in results.items():
            folder = args.dest / src.folder
            if folder.exists():
                shutil.rmtree(folder)
            for path, data in selection:
                (folder / path).parent.mkdir(parents=True, exist_ok=True)
                (folder / path).write_bytes(data)
        (args.dest / NOTICES).write_text(notices(results), encoding='utf-8', newline='\n')
    print(f'{sum(len(s) for s, _ in results.values())} files from {len(results)} sources'
          + ('' if args.list else f' written to {args.dest}'))


if __name__ == '__main__':
    main()
