#!/usr/bin/env python3
"""SVG round-trip harness for the SML 1.0-SVG-4 prototype (sml_prototype.py).

Writes the fixture corpus, runs every positive fixture through
XML -> model -> canonical SML -> model -> XML -> model, checks that every negative fixture is
rejected, runs the Core unit tests (test_sml_core.py) and a fixed-seed fuzz/differential pass
(fuzz_roundtrip.py), and writes results/*.sml, results/results.json, and results/REPORT.md.
Requires lxml for the independent cross-check.
"""
import datetime
import io
import json
import shutil
import unittest
from pathlib import Path

from lxml import etree

import fuzz_roundtrip as F
import sml_prototype as S

BASE = Path(__file__).resolve().parent
FUZZ = dict(seed=1, docs=300, mutations=4)   # the fixed fuzz pass; see fuzz_roundtrip.py for longer runs
CORPUS = BASE / 'corpus'
NEGATIVE = BASE / 'corpus-negative'
OUT = BASE / 'results'

# File name prefix = SV fixture number in §15 of the specification (01-18 are SV-1 to SV-18).
# X-prefixed fixtures are extra coverage without an SV number.
fixtures = {
'01-basic-shapes.svg':'''<svg xmlns="http://www.w3.org/2000/svg" width="400" height="200" viewBox="0 0 400 200"><rect x="10" y="20" width="50" height="60" fill="#4477cc"/><circle cx="100" cy="70" r="30"/><ellipse cx="190" cy="80" rx="30" ry="15"/><line x1="1" y1="2" x2="30" y2="50"/><polyline points="0,0 5,10 10,0"/><polygon points="2,2 22,2 10,18"/></svg>''',
'02-path-exact.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><path d="M10,10 C 20.000 30 40 30 50 10 L 90 70 z M0 0h10v-5" fill="none" stroke="black" stroke-width="1.00"/></svg>''',
'03-text-mixed.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><text x="12" xml:space="preserve">Before <tspan font-weight="bold"> bold </tspan> after<tspan dx="2">!</tspan></text></svg>''',
'04-whitespace.svg':'''<svg xmlns="http://www.w3.org/2000/svg">\n  <title>  Title  </title>\n  <desc>First\nSecond\tThird</desc>\n  <text xml:space="preserve"> A  B\n C </text>\n</svg>''',
'05-namespaces.svg':'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" xmlns:v="urn:vendor:graphics"><defs><g id="g1"><path d="M0 0L1 1"/></g></defs><use xlink:href="#g1" v:hint="keep-me"/><v:private v:flag="yes"><v:payload>value</v:payload></v:private></svg>''',
'06-prefix-shadow.svg':'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:p="urn:one"><metadata><p:info p:code="1"/><g xmlns:p="urn:two"><p:info p:code="2"/></g><p:info p:code="3"/></metadata></svg>''',
'07-definitions-references.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><defs><linearGradient id="grad"><stop offset="0%" stop-color="red"/><stop offset="100%" stop-color="blue"/></linearGradient><clipPath id="clip"><rect width="20" height="20"/></clipPath><filter id="blur"><feGaussianBlur stdDeviation="2.5"/></filter><symbol id="sym"><circle r="10"/></symbol></defs><rect width="50" height="50" fill="url(#grad)" clip-path="url(#clip)" filter="url(#blur)"/><use href="#sym"/></svg>''',
'08-styles-css.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><style>.a { fill: #f00; }\n#b:hover { opacity: .5; }</style><rect id="b" class="a" style="stroke: rgb(0, 0, 0); stroke-width: 1.00 !important" width="30" height="30"/></svg>''',
'09-transforms.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><g transform="translate(10,20) rotate(-30.5) matrix(1 0 0 1 0 0)"><path d="m0 0 10 10"/></g></svg>''',
'10-foreign-object.svg':'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:h="http://www.w3.org/1999/xhtml"><foreignObject width="100" height="70"><h:div><h:p>HTML <h:strong>here</h:strong> !</h:p></h:div></foreignObject></svg>''',
'11-comments-pi.svg':'''<?xml version="1.0"?><?edit mode="strict"?><svg xmlns="http://www.w3.org/2000/svg"><!-- preserve comment --><title>Sample</title><?proc data?><!-- after --></svg>''',
'12-unicode-entities.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><text>Zażółć 😀 &amp; &lt;test&gt; café</text><metadata note="A&#x9;B&#xA;C" data="&quot;quote&quot;"/></svg>''',
'13-images-href.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><image width="2" height="2" href="data:image/png;base64,iVBORw0KGgo="/><a href="https://example.org/?x=1&amp;y=2"><text>Link</text></a></svg>''',
'14-metadata-vendor.svg':'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:app="urn:app"><metadata><dc:title>Drawing</dc:title><app:object app:kind="vector" xmlns:other="urn:other" other:key="v"><app:child/></app:object></metadata><g id="layer1"><rect width="1" height="2"/></g></svg>''',
'15-svg-cdata.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><style><![CDATA[.a > rect { fill: red; }]]></style><text><![CDATA[< & >]]></text></svg>''',
'16-attribute-order.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><rect z="9" a="1" id="x" fill="none" data-case="A  B"/></svg>''',
'17-empty-and-siblings.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><g/><g></g><g><rect/><rect/></g></svg>''',
'18-whitespace-control.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><text xml:space="preserve">&#xA;&#x9; X &#xD;Y </text><rect data="A&#xA;B&#x9;C&#xD;D"/></svg>''',
'19-prolog-epilog.svg':'''<?xml version="1.0" encoding="UTF-8"?>\n<!-- before root -->\n<?app-config mode="edit"?>\n<svg xmlns="http://www.w3.org/2000/svg"><rect/></svg>\n<!-- after root -->\n<?app-trailer done?>\n''',
'20-unused-namespace.svg':'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:unused="urn:unused"><g xmlns:also="urn:also"><rect/></g></svg>''',
'21-prefix-in-css.svg':'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:app="urn:app"><style>@namespace app url(urn:app); app|marker { fill: red; }</style><g app:ref="app:item" app:type="app:layer"/></svg>''',
'22-default-ns-undeclaration.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><foreignObject width="10" height="10"><data xmlns=""><item key="a"/></data></foreignObject></svg>''',
'23-text-cr-lf-tab.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><desc>line1&#13;&#10;line2&#9;tab&#13;end</desc></svg>''',
'24-attribute-cr-lf-tab.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><g data-a="x&#13;y" data-b="x&#10;y" data-c="x&#9;y" data-d="&#9;lead and trail&#10;"/></svg>''',
'25-end-element.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><metadata><end xmlns="urn:x" id="e1">text</end><end xmlns="urn:x"/></metadata></svg>''',
'26-style-end-line.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><style>\n.a { fill: red; }\nend\n.b { fill: blue; }\n</style></svg>''',
'27-ns-on-empty-element.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><metadata><p:x xmlns:p="urn:vendor"/><p:y xmlns:p="urn:vendor" p:k="v">t</p:y></metadata></svg>''',
'29-nested-foreign-prefixes.svg':'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:a="urn:a"><a:outer a:x="1" xmlns:b="urn:b"><b:inner b:y="2" a:z="3"><a:leaf/></b:inner></a:outer></svg>''',
'30a-standalone-no.svg':'''<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n<svg xmlns="http://www.w3.org/2000/svg"/>\n''',
'30b-standalone-yes.svg':'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<svg xmlns="http://www.w3.org/2000/svg"/>\n''',
'32-multiple-prefixes.svg':'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:a="urn:shared" xmlns:b="urn:shared"><g a:one="1" b:two="2"><a:x/><b:x/></g></svg>''',
'59-pi-trailing-space.svg':'''<?app-hint compact ?><svg xmlns="http://www.w3.org/2000/svg"><?app-note keep  ?></svg>''',
'60a-indent-2.svg':'''<svg xmlns="http://www.w3.org/2000/svg">\n  <defs>\n    <linearGradient id="g">\n      <stop offset="0"/>\n      <stop offset="1"/>\n    </linearGradient>\n  </defs>\n  <g id="layer">\n    <rect width="10" height="10"/>\n    <!-- note -->\n    <circle r="5"/>\n  </g>\n</svg>\n''',
'60b-indent-4.svg':'''<svg xmlns="http://www.w3.org/2000/svg">\n    <defs>\n        <linearGradient id="g">\n            <stop offset="0"/>\n            <stop offset="1"/>\n        </linearGradient>\n    </defs>\n    <g id="layer">\n        <rect width="10" height="10"/>\n        <!-- note -->\n        <circle r="5"/>\n    </g>\n</svg>\n''',
'60c-indent-tab.svg':'''<svg xmlns="http://www.w3.org/2000/svg">\n\t<defs>\n\t\t<linearGradient id="g">\n\t\t\t<stop offset="0"/>\n\t\t\t<stop offset="1"/>\n\t\t</linearGradient>\n\t</defs>\n\t<g id="layer">\n\t\t<rect width="10" height="10"/>\n\t\t<!-- note -->\n\t\t<circle r="5"/>\n\t</g>\n</svg>\n''',
'61-mixed-content-indent.svg':'''<svg xmlns="http://www.w3.org/2000/svg">\n  <text x="1"><tspan>A</tspan> B</text>\n  <text x="2">C <tspan>D</tspan></text>\n</svg>\n''',
'62-minified-in-indented.svg':'''<svg xmlns="http://www.w3.org/2000/svg">\n  <g><rect/><rect/></g>\n  <g>\n    <circle/>\n  </g>\n</svg>\n''',
'63-irregular-indent.svg':'''<svg xmlns="http://www.w3.org/2000/svg">\n  <g>\n\n    <rect/>\n   <rect/>\n  </g>\n  <g>\n    <circle/></g>\n</svg>\n''',
'66-doctype-public.svg':'''<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">\n<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" version="1.1">\n  <use xlink:href="#a"/>\n</svg>\n''',
'67a-doctype-system.svg':'''<!DOCTYPE svg SYSTEM "svg11.dtd"><svg xmlns="http://www.w3.org/2000/svg"/>''',
'67b-doctype-bare.svg':'''<!DOCTYPE svg><svg xmlns="http://www.w3.org/2000/svg"/>''',
'68-doctype-between-comments.svg':'''<!-- first --><!DOCTYPE svg><?between yes?><!-- second --><svg xmlns="http://www.w3.org/2000/svg"/>''',
'73-cdata-end-marker.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><text>a ]]&gt; b</text><style><![CDATA[x]]]]><![CDATA[>y]]></style></svg>''',
'74-root-default-undeclared.svg':'''<svg xmlns=""><rect/></svg>''',
'75-explicit-xml-prefix.svg':'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:xml="http://www.w3.org/XML/1998/namespace"><text xml:space="preserve"> x </text></svg>''',
'X1-long-values.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><path d="M 0 0 C 10 20 30 40 50 60 S 70 80 90 100 S 110 120 130 140 S 150 160 170 180 S 190 200 210 220 Z"/><image href="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="/></svg>''',
'X2-inline-parens.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><g transform="translate(10,20) rotate(45)" clip-path="url(#c)" fill="url(#grad)"><rect/></g></svg>''',
'77-comment-run.svg':'''<?xml version="1.0" encoding="UTF-8"?>
<!--=== prolog banner ===-->
<!--= second line =-->
<svg xmlns="http://www.w3.org/2000/svg">
  <!--==========================================-->
  <!--=  Example test case                     =-->
  <!--# looks like a source comment-->
  <!--==========================================-->
  <!-- spaced -->
  <g><!--a--><!--b--><!--"""--></g>
  <rect width="1"/>
</svg>''',
'78-laid-out-comment.svg':'''<?xml version="1.0" encoding="UTF-8"?>
<!--
  Prolog comment at depth 0
-->
<svg xmlns="http://www.w3.org/2000/svg">
  <!--
    Created with an editor
    second line

    after an empty line
  -->
  <g>
    <!--
      deeper comment
    -->
    <rect/>
  </g>
  <!--
  flush lines are not laid out
  -->
  <text><!--
    in mixed content
  -->x</text>
</svg>
<!--
  epilog
-->''',
}

# Inputs a strict importer MUST reject (SV-28, SV-57, SV-69, and general XML/namespace errors).
negative_fixtures = {
'28a-internal-subset.svg':'''<!DOCTYPE svg [<!ENTITY x "expanded">]><svg xmlns="http://www.w3.org/2000/svg">&x;</svg>''',
'28b-empty-internal-subset.svg':'''<!DOCTYPE svg [ ]><svg xmlns="http://www.w3.org/2000/svg"/>''',
'28c-external-entity.svg':'''<!DOCTYPE svg [<!ENTITY ext SYSTEM "http://example.invalid/secret">]><svg xmlns="http://www.w3.org/2000/svg">&ext;</svg>''',
'57-pi-colon-target.svg':'''<?a:b data?><svg xmlns="http://www.w3.org/2000/svg"/>''',
'69-undeclared-entity.svg':'''<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd"><svg xmlns="http://www.w3.org/2000/svg"><text>&nbsp;</text></svg>''',
'N1-xml-1-1.svg':'''<?xml version="1.1"?><svg xmlns="http://www.w3.org/2000/svg"/>''',
'N2-unbound-prefix.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><p:x/></svg>''',
'N3-duplicate-expanded-attribute.svg':'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:a="urn:x" xmlns:b="urn:x"><g a:k="1" b:k="2"/></svg>''',
'N4-malformed.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><g></svg>''',
}

SECURE = dict(resolve_entities=False, load_dtd=False, no_network=True, strip_cdata=False,
              remove_blank_text=False, remove_comments=False, remove_pis=False)


def lxml_signature(node):
    """RT-1 element-tree signature computed by lxml, independently of the prototype."""
    if isinstance(node, etree._Comment):
        return ['comment', node.text or '']
    if isinstance(node, etree._ProcessingInstruction):
        return ['pi', node.target, node.text or '']
    attrs = sorted([str(k), v] for k, v in node.attrib.items())
    children = []
    if node.text:
        children.append(['text', node.text])
    for child in node:
        children.append(lxml_signature(child))
        if child.tail:
            children.append(['text', child.tail])
    merged = []
    for item in children:
        if item[0] == 'text' and merged and merged[-1][0] == 'text':
            merged[-1] = ['text', merged[-1][1] + item[1]]
        else:
            merged.append(item)
    return ['element', str(node.tag), attrs, merged]


def run_positive(path: Path) -> dict:
    r = {'id': path.stem, 'xml_baseline': 'FAIL', 'lxml_crosscheck': 'FAIL', 'sml_round_trip': 'FAIL',
         'xml_reimport': 'FAIL', 'stable': 'FAIL', 'kdl_2_0': 'NOT_RUN', 'notes': []}
    try:
        raw = path.read_bytes()
        x = etree.fromstring(raw, etree.XMLParser(**SECURE))
        again = etree.fromstring(etree.tostring(x, encoding='utf-8'), etree.XMLParser(**SECURE))
        assert lxml_signature(x) == lxml_signature(again), 'lxml parse/serialize/reparse mismatch'
        r['xml_baseline'] = 'PASS'

        d1 = S.read_xml(raw)
        root, error, _ = F.lxml_parse(raw)
        diff = error or F.view_difference(F.model_view(d1), F.lxml_view(root))
        if diff:
            r['notes'].append('importer disagrees with lxml: ' + diff)
            return r
        r['lxml_crosscheck'] = 'PASS'

        sml = S.write_sml(d1)
        (OUT / (path.stem + '.sml')).write_text(sml, encoding='utf-8', newline='\n')
        d2 = S.read_sml(sml)
        diff = S.first_difference(S.signature(d1), S.signature(d2))
        if diff:
            r['notes'].append('SML round trip: ' + diff)
            return r
        r['sml_round_trip'] = 'PASS'

        xml2 = S.write_xml(d2)
        d3 = S.read_xml(xml2)
        diff = S.first_difference(S.signature(d1), S.signature(d3))
        if diff or lxml_signature(etree.fromstring(xml2.encode(), etree.XMLParser(**SECURE))) != lxml_signature(x):
            r['notes'].append('XML re-import: ' + (diff or 'lxml signature differs'))
            return r
        r['xml_reimport'] = 'PASS'

        if S.write_sml(d3) != sml:
            r['notes'].append('second cycle produced different canonical SML')
            return r
        r['stable'] = 'PASS'
    except Exception as e:  # report and continue with the next fixture
        r['notes'].append(f'{type(e).__name__}: {e}')
    return r


def run_negative(path: Path) -> dict:
    r = {'id': path.stem, 'expected': 'REJECT', 'result': 'ACCEPTED', 'notes': []}
    try:
        S.read_xml(path.read_bytes())
        r['notes'].append('accepted, but must be rejected')
    except S.XmlInputError as e:
        r['result'] = 'REJECTED'
        kind = 'UNSUPPORTED_XML_INFORMATION' if isinstance(e, S.UnsupportedXmlError) else 'XML_ERROR'
        r['notes'].append(f'{kind}: {e}')
    except Exception as e:
        r['notes'].append(f'unexpected {type(e).__name__}: {e}')
    return r


def run_unit_tests() -> dict:
    stream = io.StringIO()
    suite = unittest.defaultTestLoader.loadTestsFromName('test_sml_core')
    result = unittest.TextTestRunner(stream=stream, verbosity=0).run(suite)
    failed = [str(t) for t, _ in result.failures + result.errors]
    return {'run': result.testsRun, 'failed': failed}


def write_report(report: dict) -> None:
    pos, neg, unit, fuzz = report['results'], report['negative_results'], report['unit_tests'], report['fuzz']
    cols = ['xml_baseline', 'lxml_crosscheck', 'sml_round_trip', 'xml_reimport', 'stable']
    passed = sum(all(r[c] == 'PASS' for c in cols) for r in pos)
    rejected = sum(r['result'] == 'REJECTED' for r in neg)
    lines = [
        '# SVG Round-Trip Test Results', '',
        f'Generated by `build_and_test.py` on {report["date"]} for SML {S.SML_VERSION}.', '',
        f'**Positive fixtures passed:** {passed} / {len(pos)}  ',
        f'**Negative fixtures rejected:** {rejected} / {len(neg)}  ',
        f'**Core unit tests (CP and SML-side SV):** {unit["run"] - len(unit["failed"])} / {unit["run"]} passed  ',
        f'**Fuzz/differential findings:** {len(fuzz["findings"])} (seed {fuzz["seed"]}, {fuzz["documents"]} documents)  ',
        '**KDL:** not run (no converter or parser available).', '',
        '## Positive fixtures', '',
        '| Fixture | XML baseline | lxml cross-check | SML round trip | XML re-import | Stable | Notes |',
        '|---|---|---|---|---|---|---|',
    ]
    for r in pos:
        lines.append(f'| {r["id"]} | ' + ' | '.join(r[c] for c in cols) + f' | {"; ".join(r["notes"])} |')
    lines += ['', '## Negative fixtures (must be rejected)', '', '| Fixture | Result | Reason |', '|---|---|---|']
    for r in neg:
        lines.append(f'| {r["id"]} | {r["result"]} | {"; ".join(r["notes"]).replace("|", "/")} |')
    if unit['failed']:
        lines += ['', '## Failed unit tests', ''] + [f'- {t}' for t in unit['failed']]
    lines += ['', '## Fuzz and differential tests (`fuzz_roundtrip.py`)', '',
              f'- {fuzz["documents"]} generated documents: SML, variant-SML and XML round trips, canonical '
              'stability, and agreement with lxml (libxml2).',
              f'- {fuzz["sml_mutants"]} SML mutants, {fuzz["sml_mutants_accepted"]} accepted by the reader; every '
              'accepted mutant must round-trip and export XML that lxml accepts.',
              f'- {fuzz["xml_mutants"]} XML mutants, {fuzz["xml_mutants_both_accepted"]} accepted by both importers; '
              'verdicts and results must agree.',
              f'- Findings: {len(fuzz["findings"])}.']
    for f in fuzz['findings']:
        lines.append(f'  - `{f["kind"]}`: {f["message"][:200].replace("|", "/")}')
    if fuzz['known_divergences']:
        lines += ['', 'Known divergences between libxml2 and expat (counted, not failures):', '']
        lines += [f'- {reason} ({count}x)' for reason, count in sorted(fuzz['known_divergences'].items())]
    lines += ['', '## Limits', ''] + [f'- {c}' for c in report['caveats']]
    (OUT / 'REPORT.md').write_text('\n'.join(lines) + '\n', encoding='utf-8', newline='\n')


def main() -> int:
    for folder, items in ((CORPUS, fixtures), (NEGATIVE, negative_fixtures)):
        folder.mkdir(exist_ok=True)
        for name, src in items.items():
            (folder / name).write_text(src, encoding='utf-8', newline='')
    OUT.mkdir(exist_ok=True)
    for stale in OUT.glob('*.sml'):
        stale.unlink()
    results = [run_positive(p) for p in sorted(CORPUS.glob('*.svg'))]
    negative = [run_negative(p) for p in sorted(NEGATIVE.glob('*.svg'))]
    unit = run_unit_tests()
    shutil.rmtree(F.FINDINGS, ignore_errors=True)
    fuzz = F.run(**FUZZ)
    report = {
        'profile': 'SVG-RTCP 1.0 draft, RT-2 with prefix preservation (strict profile of SML ' + S.SML_VERSION + ')',
        'date': datetime.date.today().isoformat(),
        'method': ('expat-based importer and SML 1.0-SVG-4 prototype reader/canonical writer; lxml used as an '
                   'independent element-tree cross-check; no KDL parser'),
        'caveats': [
            'The SML reader and writer are a prototype, not an independently validated conforming implementation.',
            'The lxml cross-check compares the element tree, prolog/epilog, standalone flag, DOCTYPE identifiers, '
            'and namespace scopes; attribute prefix spelling and the DOCTYPE name are checked by the prototype only.',
            'Known libxml2/expat divergences are counted rather than failed; see the fuzz section.',
            'No visual rendering comparison.',
            'No KDL translation run.',
            'Not executed: SV-49 (writer-generated aliases) and SV-65 (readable import profile); neither is '
            'implemented by the prototype.',
        ],
        'results': results,
        'negative_results': negative,
        'unit_tests': unit,
        'fuzz': fuzz,
    }
    (OUT / 'results.json').write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    write_report(report)
    cols = ['xml_baseline', 'lxml_crosscheck', 'sml_round_trip', 'xml_reimport', 'stable']
    ok = [r for r in results if all(r[c] == 'PASS' for c in cols)]
    print(f'Positive fixtures passed: {len(ok)} / {len(results)}')
    print(f'Negative fixtures rejected: {sum(r["result"] == "REJECTED" for r in negative)} / {len(negative)}')
    print(f'Unit tests passed: {unit["run"] - len(unit["failed"])} / {unit["run"]}')
    print(f'Fuzz findings: {len(fuzz["findings"])} ({fuzz["documents"]} documents, '
          f'{fuzz["sml_mutants"]} SML and {fuzz["xml_mutants"]} XML mutants)')
    for f in fuzz['findings']:
        print('FUZZ', f['kind'], f['message'][:200])
    for r in results:
        if r not in ok:
            print('FAIL', r['id'], r['notes'])
    for r in negative:
        if r['result'] != 'REJECTED':
            print('NOT REJECTED', r['id'], r['notes'])
    failures = (len(results) - len(ok) + sum(r['result'] != 'REJECTED' for r in negative) + len(unit['failed'])
                + len(fuzz['findings']))
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
