#!/usr/bin/env python3
"""Differential test between the two implementations of SML 1.0-SVG-1: the Python prototype
(sml_prototype.py) and the C# converter (Source/SmlXml, smlxml).

Uses fuzz_roundtrip.Generator to create random documents, then checks that:

1. XML -> SML: smlxml writes canonical SML byte-identical to the Python canonical writer.
2. SML -> XML: smlxml converts each document's randomized non-canonical SML (VariantWriter) to XML
   that the Python importer reads back as the same document.
3. SML mutants: smlxml and the Python reader agree on accepting or rejecting every mutant, and on
   the resulting document when both accept.

Build smlxml first (dotnet build Source/SmlXml -c Release). Usage:
    python crosscheck_csharp.py [--smlxml PATH] [--docs N] [--seed N]
"""
import argparse
import random
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import fuzz_roundtrip as F
import sml_prototype as S

REPO = Path(__file__).resolve().parents[2]
DEFAULT_SMLXML = REPO / 'Source' / 'SmlXml' / 'bin' / 'Release' / 'net10.0' / ('smlxml.exe' if sys.platform == 'win32' else 'smlxml')


def run_smlxml(smlxml: Path, files: list[Path]) -> dict[str, str]:
    """Runs smlxml on files in batches; returns the error message for each file that failed."""
    errors = {}
    for start in range(0, len(files), 200):
        batch = [str(f) for f in files[start:start + 200]]
        proc = subprocess.run([str(smlxml), *batch], capture_output=True, text=True, encoding='utf-8')
        for line in proc.stderr.splitlines():
            for f in batch:
                if line.startswith(f + '(') or line.startswith(f + ':'):
                    errors[f] = line
    return errors


def xml_output(sml: Path) -> Path | None:
    for suffix in ('.svg', '.xml'):
        if sml.with_suffix(suffix).exists():
            return sml.with_suffix(suffix)
    return None


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--smlxml', type=Path, default=DEFAULT_SMLXML)
    ap.add_argument('--docs', type=int, default=300)
    ap.add_argument('--seed', type=int, default=1)
    ap.add_argument('--mutants', type=int, default=4, help='SML mutants per document')
    args = ap.parse_args()
    if not args.smlxml.exists():
        print(f'smlxml not found at {args.smlxml}; build Source/SmlXml first', file=sys.stderr)
        return 2

    work = Path(tempfile.mkdtemp(prefix='smlxml-crosscheck-'))
    findings = []
    try:
        xml_dir, var_dir, mut_dir = work / 'xml', work / 'variant', work / 'mutant'
        for d in (xml_dir, var_dir, mut_dir):
            d.mkdir()
        docs, mutants = {}, {}
        for i in range(args.docs):
            r = random.Random(f'{args.seed}:{i}')
            doc = F.Generator(r).document()
            docs[i] = doc
            (xml_dir / f'd{i}.svg').write_text(S.write_xml(doc), encoding='utf-8', newline='')
            variant = F.VariantWriter(doc, r).write()
            (var_dir / f'd{i}.sml').write_text(variant, encoding='utf-8', newline='')
            canonical = S.write_sml(doc)
            for k in range(args.mutants):
                text = F.mutate(canonical if k % 2 == 0 else variant, F.SML_TOKENS, r, F.EDGE_TOKENS)
                path = mut_dir / f'd{i}m{k}.sml'
                path.write_text(text, encoding='utf-8', newline='')
                mutants[path] = text

        # 1. XML -> SML, byte-identical canonical output
        files = sorted(xml_dir.glob('*.svg'))
        errors = run_smlxml(args.smlxml, files)
        for f in files:
            expected = S.write_sml(S.read_xml(f.read_bytes()))
            out = f.with_suffix('.sml')
            if str(f) in errors:
                findings.append(('xml->sml rejected', f.name, errors[str(f)]))
            elif out.read_text(encoding='utf-8') != expected:
                findings.append(('xml->sml differs from Python', f.name, ''))

        # 2. Variant SML -> XML, read back by Python
        files = sorted(var_dir.glob('*.sml'))
        errors = run_smlxml(args.smlxml, files)
        for f in files:
            doc = docs[int(f.stem[1:])]
            if str(f) in errors:
                findings.append(('variant sml->xml rejected', f.name, errors[str(f)]))
                continue
            try:
                diff = S.first_difference(S.signature(doc), S.signature(S.read_xml(xml_output(f).read_bytes())))
            except S.XmlInputError as e:
                diff = f'Python cannot import the XML: {e}'
            if diff:
                findings.append(('variant sml->xml differs', f.name, diff))

        # 3. Mutants: same verdict, same document
        files = sorted(mutants)
        errors = run_smlxml(args.smlxml, files)
        agree_accept = agree_reject = 0
        for f in files:
            try:
                expected, py_error = S.read_sml(mutants[f]), None
            except S.SmlError as e:
                expected, py_error = None, e
            cs_error = errors.get(str(f))
            if expected is None and cs_error:
                agree_reject += 1
            elif expected is None:
                findings.append(('C# accepts, Python rejects', f.name, str(py_error)))
            elif cs_error:
                if 'already exists' in cs_error:
                    continue
                findings.append(('Python accepts, C# rejects', f.name, cs_error))
            else:
                out = xml_output(f)
                try:
                    got = S.read_xml(out.read_bytes())
                    diff = S.first_difference(S.signature(expected), S.signature(got))
                except S.XmlInputError as e:
                    diff = None if F.expat_rejects_a_name(F.model_names(expected)) else f'Python cannot import: {e}'
                if diff:
                    findings.append(('mutant documents differ', f.name, diff))
                else:
                    agree_accept += 1
        print(f'seed {args.seed}: {args.docs} documents; {len(mutants)} SML mutants '
              f'({agree_accept} accepted by both, {agree_reject} rejected by both)')
        for kind, name, detail in findings[:30]:
            print(f'FINDING {kind}: {name}: {detail[:300]}')
            source = next((p for p in (xml_dir / name, var_dir / name, mut_dir / name) if p.exists()), None)
            if source and source.suffix == '.sml':
                print('  input:', repr(source.read_text(encoding='utf-8')[:300]))
        print(f'{len(findings)} findings')
        return 1 if findings else 0
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == '__main__':
    raise SystemExit(main())
