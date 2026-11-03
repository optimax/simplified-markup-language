#!/usr/bin/env python3
"""Fuzz and differential tests for the SML 1.0-SVG-1 prototype (sml_prototype.py).

Three campaigns, all seeded and reproducible:

1. Generated documents: random valid documents must survive SML and XML round trips, give
   stable canonical SML, and match lxml's independent view of the exported XML (element tree,
   namespace scopes, prolog/epilog, standalone, DOCTYPE identifiers).
   Each document is also written by VariantWriter in randomly chosen equivalent, non-canonical
   SML (aliases, quoting, continuation lines, longer block markers, end QName, comments), and the
   reader must produce the same document from it.
2. SML mutations: random edits to canonical and variant SML. The reader must reject them with
   SmlError, or accept a document that round-trips and exports XML that lxml accepts. Nothing
   may crash.
3. XML mutations: random edits to exported XML, read by both the prototype importer and lxml.
   Their verdicts and results must agree, except where the prototype rejects a construct on
   purpose (UnsupportedXmlError) or a divergence is listed in KNOWN_DIVERGENCES.

Run `python fuzz_roundtrip.py --docs 2000 --seed 7` for a longer campaign. Findings are shrunk
and written to results/fuzz-findings/. Requires lxml.
"""
import argparse
import random
import re
import xml.parsers.expat as expat
from pathlib import Path

from lxml import etree

import sml_prototype as S

BASE = Path(__file__).resolve().parent
FINDINGS = BASE / 'results' / 'fuzz-findings'
SECURE = dict(resolve_entities=False, load_dtd=False, no_network=True, strip_cdata=False,
              remove_blank_text=False, remove_comments=False, remove_pis=False)

# Divergences between libxml2 (lxml) and expat (the prototype importer) that are understood.
KNOWN_DIVERGENCES = {
    'uri': 'libxml2 rejects namespace names that are not URI references; expat and the prototype '
           'accept them',
    'version': 'libxml2 accepts a malformed XML version number with only a warning; expat rejects it',
    'names': 'expat uses XML 1.0 4th-edition name characters and rejects names that 5th-edition '
             'parsers such as libxml2 accept',
}

URIS = ['http://www.w3.org/2000/svg', 'http://www.w3.org/1999/xlink', 'urn:a', 'urn:b',
        'urn:x(1,2)', 'urn:a#frag', 'urn:%41', 'http://example.com/ns?x=1&y=2', 'rel', 'rel/path']
PREFIXES = ['a', 'b', 'p', 'svg', 'x-y', 'n.1', 'é']
LOCALS = ['g', 'rect', 'text', 'tspan', 'end', 'data', 'style', 'path', 'x-1', 'a.b', 'é', '中文', 'Item_2']
ATTR_LOCALS = ['id', 'd', 'x', 'style', 'class', 'data-a', 'end', 'k', 'é']
WORDS = ['1', 'none', 'url(#a)', 'translate(10,20)', 'M 0 0 L 10 10', '#4477cc', 'a,b', 'x)y', '(',
         'end', '"""', '|', '|x', '"q"', "it's", '', ' ', 'a  b', '\\n', 'end path', '@ns x', '# not a comment']
CHARS = list('aZ0 \t\n\r"\'\\|(),#=&<>]-./:;@`~{}') + [
    'é', '😀', '\u2028', '\u2029', '\u0085', '\ufeff', '\u200d', '\u00a0', '\u007f', '\u00ad',
    'end', ']]>', '--', '?>', '\\u{41}']
PI_TARGETS = ['app', 'x-y', 'a.b', 'proc', 'é', 'xml-stylesheet', 'XMLish']
PUBIDS = ['-//W3C//DTD SVG 1.1//EN', "-//A B//X'Y//EN", 'x (y) +,./:=?;!*#@$_%']
SYSIDS = ['svg11.dtd', 'http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd', 'a"b', "a'b", 'é.dtd']
UNITS = ['  ', '    ', '\t', ' ', '   ', '']

SML_TOKENS = ['|', '"', '\\', '(', ')', ',', '=', '.', '@', '#', ' ', '\t', '\n', 'end', '/', ':',
              'x', '"""', '""""', '\\u{', '@ns ', '@comments """\n', '@block g .y x\n', '@node g x\n', '@noindent\n', '@indent 2\n', 'xmlns=',
              '\r', '\ufeff', 'end g\n', '@element end\n', '"\n']
# Inserted right after a quote character: targets value constraints at value edges
# (leading/trailing whitespace, CR, '-' and '--' in comments, '?>' in PIs).
EDGE_TOKENS = [' ', '\t', '\\t', '\\n', '\\r', '\\u{D}', '-', '--', '?>', 'end', '|', '\\"']
XML_TOKENS = ['<', '>', '&', '"', "'", '/', '=', ':', '!', '?', '-', ' ', 'x', ']]>', '&#13;', '&#0;',
              '&amp;', '&nbsp;', '<!--', '-->', '<?', '?>', 'xmlns:q="urn:q" ', 'q:', '<![CDATA[',
              '<!DOCTYPE r [<!ENTITY e "v">]>', '\r', '\t', 'é', 'xmlns="" ', '<?xml version="1.0"?>']


# ---------------------------------------------------------------------------------------------
# Random valid documents

class Generator:
    def __init__(self, rng: random.Random):
        self.r = rng
        self.unit = rng.choice(UNITS) if rng.random() < 0.5 else None

    def value(self) -> str:
        r = self.r
        kind = r.random()
        if kind < 0.35:
            return r.choice(WORDS)
        if kind < 0.65:
            return ''.join(r.choice(CHARS) for _ in range(r.randint(1, 12)))
        if kind < 0.85:
            lines = []
            for _ in range(r.randint(2, 4)):
                line = r.choice(WORDS + ['end', '"""', '""""', 'x y'])
                lines.append(r.choice(['', '', '  ', '\t']) + line + r.choice(['', '', ' ']))
            return '\n'.join(lines)
        if kind < 0.95:
            if r.random() < 0.5:
                return ' '.join(r.choice(['M', 'L', 'C', '10', '20.5', '-3', 'Z']) for _ in range(r.randint(30, 90)))
            return ''.join(r.choice('ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/') for _ in range(r.randint(90, 260)))
        return ''.join(r.choice(' \t\n\r') for _ in range(r.randint(1, 5)))

    def comment(self) -> S.Comment:
        data = self.value().replace('\r', '')
        while '--' in data:
            data = data.replace('--', '-x')
        return S.Comment(S.comment_text(data, own_line=True))

    def laid_out_lines(self) -> str:
        return '\n'.join(self.r.choice(['Created with an editor', 'http://example.org/', 'end', '"""', '', '  indented',
                                         '# x']) for _ in range(self.r.randint(1, 4)))

    def multi_line_comment(self) -> S.Comment:
        """A multi-line comment, as pretty-printers write them; its data is trimmed (§9)."""
        return S.Comment(S.comment_text(self.laid_out_lines(), own_line=True))

    def pi(self) -> S.PI:
        data = self.value().replace('\r', '').replace('?>', '? >').lstrip(S.XML_WS)
        return S.PI(self.r.choice(PI_TARGETS), data)

    def doctype(self) -> S.Doctype:
        r = self.r
        name = r.choice(['svg', 'html', 'é', 'a:b'])
        form = r.random()
        if form < 0.3:
            return S.Doctype(name, None, None)
        if form < 0.6:
            return S.Doctype(name, None, r.choice(SYSIDS))
        return S.Doctype(name, r.choice(PUBIDS), r.choice(SYSIDS))

    def element(self, depth: int, ctx: dict, indentation: str = '') -> S.Element:
        r = self.r
        # This element's step: mostly the document's unit, sometimes another (a local @indent, §6.4).
        step = None if self.unit is None else self.unit if r.random() < 0.85 else r.choice(UNITS)
        decls = {}
        if r.random() < 0.3:
            decls[None] = '' if ctx.get(None) and r.random() < 0.2 else r.choice(URIS)
        for _ in range(r.choice([0, 0, 0, 1, 2])):
            decls[r.choice(PREFIXES)] = r.choice(URIS)
        if r.random() < 0.03:
            decls['xml'] = S.XML_NS
        scope = dict(ctx)
        scope.update(decls)
        bound = [p for p, u in scope.items() if p and u and p != 'xml']
        local = r.choice(LOCALS)
        qname = f'{r.choice(bound)}:{local}' if bound and r.random() < 0.3 else local
        el = S.Element(qname)
        el.ns_decls = decls
        el.expanded = S.resolve_qname(qname, scope, False, None)
        for _ in range(r.randint(0, 4)):
            if r.random() < 0.08:
                name = r.choice(['xml:space', 'xml:lang', 'xml:base'])
            elif bound and r.random() < 0.25:
                name = f'{r.choice(bound)}:{r.choice(ATTR_LOCALS)}'
            else:
                name = r.choice(ATTR_LOCALS)
            expanded = S.resolve_qname(name, scope, True, None)
            if name in el.attrs or expanded in el.attr_expanded.values():
                continue
            el.attrs[name] = self.value()
            el.attr_expanded[name] = expanded
        if depth < 4:
            for _ in range(r.randint(0, 4 if depth < 2 else 2)):
                pick = r.random()
                if pick < 0.45:
                    el.children.append(self.element(depth + 1, scope, indentation + (step or '')))
                elif pick < 0.75:
                    el.children.append(S.Text(self.value()))
                elif pick < 0.87:
                    el.children.append(self.comment())
                else:
                    el.children.append(self.pi())
        if (self.unit is not None and el.children and not any(isinstance(c, S.Text) for c in el.children)
                and r.random() < 0.8):
            inner = indentation + step
            spaced = []
            for c in el.children:
                if isinstance(c, S.Comment) and r.random() < 0.4:
                    c = self.multi_line_comment()
                elif (isinstance(c, S.Element) and (not c.children or (len(c.children) == 1
                      and isinstance(c.children[0], S.Text))) and r.random() < 0.5):
                    # A text-only element pretty-printed like <p>\n  text\n</p>, sometimes without attributes so
                    # that it is written in the compact form.
                    c.children = [S.Text(S.lay_out_block(self.laid_out_lines(), inner, self.unit))]
                    if r.random() < 0.5:
                        c.attrs.clear()
                        c.attr_expanded.clear()
                spaced += [S.Text('\n' + inner), c]
            el.children = spaced + [S.Text('\n' + indentation)]
        return el

    def document(self) -> S.Document:
        r = self.r
        d = S.Document()
        d.standalone = r.choice([None, None, 'yes'])
        d.prolog = [self.comment() if r.random() < 0.5 else self.pi() for _ in range(r.choice([0, 0, 1, 2, 3]))]
        if self.unit is not None and r.random() < 0.3:
            d.prolog.append(self.multi_line_comment())
        if r.random() < 0.3:
            d.prolog.insert(r.randint(0, len(d.prolog)), self.doctype())
        d.root = self.element(0, dict(S.BASE_NS))
        d.epilog = [self.comment() if r.random() < 0.5 else self.pi() for _ in range(r.choice([0, 0, 1, 2]))]
        return d


# ---------------------------------------------------------------------------------------------
# Independent views of a document: the prototype's model versus lxml

def lxml_signature(node):
    if isinstance(node, etree._Comment):
        return ['comment', S.comment_text(node.text or '')]
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


def _misc(node):
    if isinstance(node, etree._Comment):
        return ('comment', S.comment_text(node.text or ''))
    return ('pi', node.target, node.text or '')


def lxml_parse(xml: bytes):
    """Returns (root, error, warning types); root is None if libxml2 reported an error.

    lxml raises only when the *last* logged message is an error, so a namespace error followed
    by any warning would otherwise pass silently; the error log is the reliable verdict."""
    parser = etree.XMLParser(**SECURE)
    try:
        root = etree.fromstring(xml, parser)
    except etree.XMLSyntaxError as e:
        return None, str(e), set()
    errors = [e for e in parser.error_log if e.level_name in ('ERROR', 'FATAL')]
    if errors:
        return None, f'{errors[0].message} (logged; lxml did not raise)', set()
    return root, None, {e.type_name for e in parser.error_log if e.level_name == 'WARNING'}


def lxml_names(root) -> set:
    names = set()
    nodes = list(root.itersiblings(preceding=True)) + list(root.iter()) + list(root.itersiblings())
    for node in nodes:
        if isinstance(node, etree._ProcessingInstruction):
            names.add(node.target)
        elif isinstance(node.tag, str):
            for key in [node.tag, *node.attrib]:
                names.update(part for part in key.rsplit('}', 1)[-1].split(':') if part)
            if node.prefix:
                names.add(node.prefix)
    return names


def model_names(doc: S.Document) -> set:
    names = {x.target for x in doc.prolog + doc.epilog if isinstance(x, S.PI)}
    names.update(part for x in doc.prolog if isinstance(x, S.Doctype) for part in x.name.split(':') if part)
    stack = [doc.root]
    while stack:
        el = stack.pop()
        for q in [el.qname, *el.attrs, *(p for p in el.ns_decls if p)]:
            names.update(q.split(':'))
        for c in el.children:
            if isinstance(c, S.Element):
                stack.append(c)
            elif isinstance(c, S.PI):
                names.add(c.target)
    return names


def expat_rejects_a_name(names: set) -> bool:
    """True if expat rejects one of these names (each is a valid XML 1.0 5th-edition NCName)."""
    for name in names:
        if name.isascii():
            continue
        try:
            expat.ParserCreate().Parse(f'<{name}/>'.encode('utf-8'), True)
        except expat.ExpatError:
            return True
    return False


def lxml_view(root) -> dict:
    info = root.getroottree().docinfo
    scopes = []
    for el in root.iter(tag=etree.Element):
        parent = el.getparent()
        # An empty default namespace means "no default"; compare effective bindings only.
        pmap = {k: v for k, v in (parent.nsmap if parent is not None else {}).items() if v}
        cmap = {k: v for k, v in el.nsmap.items() if v}
        changes = {(k or '', v) for k, v in cmap.items() if pmap.get(k) != v}
        changes |= {(k or '', '') for k in pmap if k not in cmap}
        scopes.append((el.prefix or '', tuple(sorted(changes))))
    public_id = info.public_id
    if public_id is not None:  # libxml2 reports the raw literal; XML 1.0 §4.2.2 normalizes it
        public_id = re.sub(r'[ \t\r\n]+', ' ', public_id).strip(' ')
    return {
        'tree': lxml_signature(root),
        'prolog': [_misc(n) for n in reversed(list(root.itersiblings(preceding=True)))],
        'epilog': [_misc(n) for n in root.itersiblings()],
        'standalone': info.standalone is True,
        'doctype_ids': (public_id, info.system_url),
        'scopes': scopes,
    }


def model_view(doc: S.Document) -> dict:
    scopes = []

    def walk(el, ctx):
        new = dict(ctx)
        new.update(el.ns_decls)
        changes = set()
        for p in el.ns_decls:
            if p != 'xml' and (ctx.get(p) or None) != (new.get(p) or None):
                changes.add((p or '', new[p]))
        prefix = el.qname.split(':', 1)[0] if ':' in el.qname else ''
        scopes.append((prefix, tuple(sorted(changes))))
        for c in el.children:
            if isinstance(c, S.Element):
                walk(c, new)
    walk(doc.root, S.BASE_NS)
    doctypes = [x for x in doc.prolog if isinstance(x, S.Doctype)]
    dt = doctypes[0] if doctypes else None
    return {
        'tree': S.rt1_signature(doc),
        'prolog': [S._misc_sig(x) for x in doc.prolog if not isinstance(x, S.Doctype)],
        'epilog': [S._misc_sig(x) for x in doc.epilog],
        'standalone': doc.standalone == 'yes',
        'doctype_ids': (dt.public_id, dt.system_id) if dt else (None, None),
        'scopes': scopes,
    }


def view_difference(mine: dict, theirs: dict) -> str | None:
    for key in mine:
        if mine[key] != theirs[key]:
            return f'{key} differs: prototype {mine[key]!r:.300} / lxml {theirs[key]!r:.300}'
    return None


def lxml_only_rejection(error: str) -> str | None:
    """Known reason when libxml2 rejects something expat accepts."""
    return KNOWN_DIVERGENCES['uri'] if 'is not a valid URI' in error else None


def expat_only_rejection(root, warnings: set) -> str | None:
    """Known reason when expat rejects something libxml2 accepted (root is libxml2's result)."""
    if 'WAR_UNKNOWN_VERSION' in warnings:
        return KNOWN_DIVERGENCES['version']
    if expat_rejects_a_name(lxml_names(root)):
        return KNOWN_DIVERGENCES['names']
    return None


# ---------------------------------------------------------------------------------------------
# Equivalent, non-canonical SML spellings

def reader_raw_ok(v: str) -> bool:
    """A value the reader accepts raw (looser than the canonical writer's raw_ok)."""
    return bool(v) and v[0] not in '" \t' and v[-1] not in ' \t' and '\n' not in v and '\r' not in v


def reader_inline_ok(v: str) -> bool:
    if v == '':
        return True
    if not reader_raw_ok(v):
        return False
    depth = 0
    for c in v:
        if c == '(':
            depth += 1
        elif c == ')':
            if depth == 0:
                return False
            depth -= 1
        elif c == ',' and depth == 0:
            return False
    return depth == 0


def iter_elements(el: S.Element):
    yield el
    for c in el.children:
        if isinstance(c, S.Element):
            yield from iter_elements(c)


class VariantWriter:
    """Writes a document as randomly chosen SML that a reader must parse to the same document."""

    def __init__(self, doc: S.Document, r: random.Random):
        self.doc, self.r, self.out = doc, r, []
        self.scopes: list[dict[str, str]] = [{}]    # element aliases (§8.7), innermost last
        self.names = sorted({e.qname for e in iter_elements(doc.root)})

    def emit(self, line: str) -> None:
        if self.r.random() < 0.05:
            self.out.append(self.r.choice(['', '# note', '   ', '#|x']))
        self.out.append(line)

    def write(self) -> str:
        d, r = self.doc, self.r
        if r.random() < 0.5:
            self.emit(r.choice(['@sml 1.0-SVG-1', '@sml 1.0', '@sml 1.0-SVG-1 tidy', '@sml\t1.0  tidy']))
        if d.standalone:
            self.emit('@standalone ' + d.standalone)
        elif r.random() < 0.2:
            self.emit(r.choice(['@standalone no', '@standalone\tno']))   # the default, written out
        self.emit(r.choice(['@indent none', '@indent\tnone']))     # whitespace is written out below
        if r.random() < 0.3:
            self.declare_nodes('', 3)
        self.items(d.prolog, '')
        self.element(d.root, '')
        self.items(d.epilog, '')
        newline = r.choice(['\n', '\n', '\r\n'])
        return ('\ufeff' if r.random() < 0.05 else '') + newline.join(self.out) + newline

    def items(self, nodes: list, ind: str) -> None:
        """Emits content; a run of comments that can be @comments lines (§9) is sometimes one @comments block."""
        r, i = self.r, 0
        while i < len(nodes):
            j = i
            while j < len(nodes) and isinstance(nodes[j], S.Comment) and S.comment_run_ok(nodes[j].data):
                j += 1
            if j > i and r.random() < 0.6:
                k = r.randint(i + 1, j)
                lines = [n.data for n in nodes[i:k]]
                marker = S.block_marker('\n'.join(lines))
                longer = marker + '"' * r.randint(1, 2)
                if r.random() < 0.3 and longer not in {S.strip_ht(x) for x in lines}:
                    marker = longer
                self.emit(ind + '@comments' + r.choice([' ', '', '  ']) + marker)
                pad = ind + r.choice(['  ', '    ', '\t', ' ', ''])
                self.out.extend(pad + x for x in lines)
                self.out.append(pad + marker + r.choice(['', ' ', '\t']))
                i = k
                continue
            node = nodes[i]
            if isinstance(node, S.Text):
                self.value(ind + '=', node.data, ind)
            elif isinstance(node, S.Element):
                self.element(node, ind)
            else:
                self.misc(node, ind)
            i += 1

    def value(self, head: str, v: str, ind: str, block: bool = True, alias: bool = False) -> None:
        """Emits `head` and value v in a random equivalent form."""
        r = self.r
        sep = r.choice([' ', ' ', '  ', '\t'])
        forms = ['quoted']
        if v == '':
            forms.append('empty')
        if reader_raw_ok(v):
            forms += ['raw', 'raw']
        tag = S.block_marker(v) if block and '\n' in v else None   # also blocks of blank lines
        if tag is not None:
            forms += ['block', 'block']
        form = r.choice(forms)
        if alias:  # an alias invocation always takes a block (§8.3)
            form = 'block'
        if form == 'empty':
            self.emit(head)
        elif form == 'raw':
            self.emit(head + sep + v + r.choice(['', '', ' ', '\t']))
        elif form == 'quoted':
            units = S.escape_units(v)
            cuts = sorted(r.sample(range(1, len(units)), min(r.randint(0, 3), max(0, len(units) - 1)))) if units else []
            segs = [''.join(units[a:b]) for a, b in zip([0] + cuts, cuts + [len(units)])] or ['']
            self.emit(head + sep + '"' + segs[0] + '"')
            for seg in segs[1:]:
                self.out.append(ind + r.choice(['  ', '    ', '']) + '"' + seg + '"')
        else:
            stripped = {S.strip_ht(x) for x in v.split('\n')}
            longer = tag + '"' * r.randint(1, 2)
            marker = longer if r.random() < 0.3 and longer not in stripped else tag
            # Verbatim block (§7): every line keeps what follows the closing marker's indentation, pad. An empty line
            # may also be written as pad, or as spaces and tabs that pad does not begin with.
            self.emit(head + r.choice([' ', '', '  ']) + marker)
            pad = ind + r.choice(['  ', '    ', '\t', ' ', ''])
            empties = ['', pad] + ([pad[:-1]] if pad else [])
            self.out.extend(pad + x if x else r.choice(empties) for x in v.split('\n'))
            self.out.append(pad + marker + r.choice(['', ' ', '\t']))

    def misc(self, item, ind: str) -> None:
        if isinstance(item, S.Comment):
            # A reader trims a comment's data and removes the indentation all its lines share (§9), so blank lines
            # around it and indentation added to every line are optional.
            r, data = self.r, item.data
            if r.random() < 0.3:
                extra = r.choice([' ', '\t', '    '])
                data = '\n'.join(extra + ln if ln else r.choice(['', extra]) for ln in data.split('\n'))
            data = r.choice(['', '', '\n', ' \n\t\n']) + data + r.choice(['', '', ' ', '\n', '\n  '])
            self.value(ind + r.choice(['@rem', '@comment']), data, ind)
        elif isinstance(item, S.PI):
            self.value(ind + '@pi ' + item.target, item.data, ind, block=False)
        else:
            line = ind + '@doctype ' + item.name
            if item.public_id is not None:
                line += ' PUBLIC ' + S.quote(item.public_id) + ' ' + S.quote(item.system_id)
            elif item.system_id is not None:
                line += ' SYSTEM ' + S.quote(item.system_id)
            self.emit(line)

    def node_target(self, name: str) -> str | None:
        for scope in reversed(self.scopes):
            if name in scope:
                return scope[name]
        return None

    def declare_nodes(self, ind: str, most: int) -> None:
        """Declares up to `most` element aliases in the innermost scope. An alias is sometimes the name of a real
        element, which must then be written with @element."""
        r, scope = self.r, self.scopes[-1]
        for _ in range(r.randint(1, most)):
            element = r.choice(self.names)
            plain = [n for n in self.names if ':' not in n and n != 'end']
            alias = r.choice(plain) if plain and r.random() < 0.3 else r.choice(['n', 'node', 'x']) + str(r.randint(0, 9))
            if alias in scope:
                continue
            scope[alias] = element
            self.emit(ind + '@node' + r.choice([' ', '  ', '\t']) + element + r.choice([' ', '\t ']) + alias)

    def opener_name(self, qname: str) -> str | None:
        """The name to open an element with: qname or an alias for it, or None if only @element can open it."""
        target = self.node_target(qname)
        if target is not None and target != qname:
            return None
        aliases = [a for scope in self.scopes for a in scope if self.node_target(a) == qname and a != qname]
        return self.r.choice(aliases) if aliases and self.r.random() < 0.7 else qname

    def element(self, el: S.Element, ind: str) -> None:
        r = self.r
        inner = ind + r.choice(['  ', '    ', '\t', ''])
        nodes = [c for c in el.children if not (isinstance(c, S.Text) and c.data == '')]
        decls = list(el.ns_decls.items())
        attrs = list(el.attrs.items())
        r.shuffle(decls)
        r.shuffle(attrs)

        def ns_entry(p, u, inline):
            name = 'xmlns' if p is None else 'xmlns:' + p
            raw = reader_inline_ok(u) if inline else (reader_raw_ok(u) and ' ' not in u and '\t' not in u)
            v = u if raw and ':' in u and r.random() < 0.7 else S.quote(u)
            return f'{name}={v}' if inline else '@ns ' + ('' if p is None else p + ' ') + v

        def attr_entry(q, v):
            return f'{q}={v}' if reader_inline_ok(v) and r.random() < 0.7 else f'{q}={S.quote(v)}'
        name = self.opener_name(el.qname)
        explicit = el.qname == 'end' or name is None or r.random() < 0.08
        inline = not explicit and r.random() < 0.5
        inline_attrs = [a for a in attrs if r.random() < 0.6] if inline else []
        vertical_attrs = [a for a in attrs if a not in inline_attrs]
        opener = name or el.qname
        if inline:
            entries = [ns_entry(p, u, True) for p, u in decls] + [attr_entry(q, v) for q, v in inline_attrs]
            if entries or r.random() < 0.2:
                spacer = r.choice([', ', ',', ' , '])
                opener += '(' + r.choice(['', ' ']) + spacer.join(entries) + ')'
        if inline and not vertical_attrs and not nodes and r.random() < 0.7:
            self.emit(ind + opener + r.choice(['/', '/ ']))
            return
        if (inline and not vertical_attrs and len(nodes) == 1 and isinstance(nodes[0], S.Text)
                and r.random() < 0.7):
            self.value(ind + opener + ' =', nodes[0].data, ind)
            return
        self.emit(ind + ('@element ' + el.qname if explicit else opener))
        if not inline:
            for p, u in decls:
                self.emit(inner + ns_entry(p, u, False))
        aliases = 0
        for q, v in vertical_attrs:
            if '\n' in v and S.block_marker(v) is not None and r.random() < 0.4:
                name = f'v{aliases}'
                aliases += 1
                self.emit(inner + f'@block {el.qname} .{q} {name}')
                self.value(inner + name, v, inner, alias=True)
            else:
                self.value(inner + '.' + q, v, inner)
        self.scopes.append({})
        if r.random() < 0.1:
            self.declare_nodes(inner, 2)
        self.items(nodes, inner)
        self.scopes.pop()
        closing = el.qname if explicit else r.choice([opener.split('(')[0], el.qname])
        self.emit(ind + ('end ' + closing if r.random() < 0.3 else 'end'))


# ---------------------------------------------------------------------------------------------
# Properties

def export_problems(doc: S.Document, prefix: str):
    """Exports doc as XML and checks re-import and lxml agreement. Returns (kind, message) or a
    known-divergence reason string, or None if everything agrees."""
    xml = S.write_xml(doc).encode('utf-8')
    root, error, warnings = lxml_parse(xml)
    try:
        diff = S.first_difference(S.signature(doc), S.signature(S.read_xml(xml)))
    except S.XmlInputError as e:
        if expat_rejects_a_name(model_names(doc)):
            return KNOWN_DIVERGENCES['names']
        reason = expat_only_rejection(root, warnings) if root is not None else None
        return reason or (prefix + 'export-not-reimportable', str(e))
    if diff:
        return (prefix + 'xml-round-trip', diff)
    if root is None:
        return lxml_only_rejection(error) or (prefix + 'lxml-rejects-export', error)
    diff = view_difference(model_view(doc), lxml_view(root))
    return (prefix + 'lxml-differential', diff) if diff else None


def check_document(doc: S.Document, variant: str | None = None):
    """All round-trip and differential properties for one valid document. variant is an
    equivalent non-canonical SML spelling of doc (from VariantWriter)."""
    results = []
    try:
        sml = S.write_sml(doc)
        again = S.read_sml(sml)
        diff = S.first_difference(S.signature(doc), S.signature(again))
        if diff:
            results.append(('sml-round-trip', diff))
        elif S.write_sml(again) != sml:
            results.append(('canonical-stability', 'second canonical write differs'))
    except Exception as e:
        results.append(('sml-crash', f'{type(e).__name__}: {e}'))
    if variant is not None:
        try:
            diff = S.first_difference(S.signature(doc), S.signature(S.read_sml(variant)))
            if diff:
                results.append(('variant-sml', diff))
        except Exception as e:
            results.append(('variant-sml', f'{type(e).__name__}: {e}'))
    try:
        outcome = export_problems(doc, '')
        if outcome:
            results.append(outcome)
    except Exception as e:
        results.append(('xml-crash', f'{type(e).__name__}: {e}'))
    return results


def mutate(text: str, tokens: list[str], r: random.Random, edges: list[str] | None = None) -> str:
    for _ in range(r.randint(1, 3)):
        lines = text.split('\n')
        op = r.random()
        pos = r.randint(0, len(text))
        quotes = [i for i, c in enumerate(text) if c == '"'] if edges else []
        if quotes and op < 0.2:
            q = r.choice(quotes) + 1
            text = text[:q] + r.choice(edges) + text[q:]
        elif op < 0.35:
            text = text[:pos] + r.choice(tokens) + text[pos:]
        elif op < 0.55:
            text = text[:pos] + text[pos + r.randint(1, 3):]
        elif op < 0.7:
            text = text[:pos] + r.choice(tokens) + text[pos + 1:]
        elif op < 0.8 and lines:
            i = r.randrange(len(lines))
            lines.insert(i, lines[i])
            text = '\n'.join(lines)
        elif op < 0.9 and len(lines) > 1:
            del lines[r.randrange(len(lines))]
            text = '\n'.join(lines)
        elif len(lines) > 1:
            i, j = r.randrange(len(lines)), r.randrange(len(lines))
            lines[i], lines[j] = lines[j], lines[i]
            text = '\n'.join(lines)
    return text


def check_sml_mutant(source: str):
    """None if the reader handled the mutant correctly, a known-divergence reason string, or
    (kind, message) for a finding."""
    try:
        doc = S.read_sml(source)
    except S.SmlError:
        return None
    except Exception as e:
        return ('sml-reader-crash', f'{type(e).__name__}: {e}')
    try:
        diff = S.first_difference(S.signature(doc), S.signature(S.read_sml(S.write_sml(doc))))
        if diff:
            return ('accepted-mutant-sml-round-trip', diff)
        return export_problems(doc, 'accepted-mutant-')
    except Exception as e:
        return ('accepted-mutant-crash', f'{type(e).__name__}: {e}')


def check_xml_mutant(xml: bytes):
    """None if both parsers agree, a known-divergence reason string, or (kind, message)."""
    try:
        mine, mine_error = S.read_xml(xml), None
    except S.XmlInputError as e:
        mine, mine_error = None, e
    except Exception as e:
        return ('xml-importer-crash', f'{type(e).__name__}: {e}')
    root, error, warnings = lxml_parse(xml)
    if mine is None and root is None:
        return None
    if mine is None:
        if isinstance(mine_error, S.UnsupportedXmlError):
            return None
        return expat_only_rejection(root, warnings) or ('prototype-rejects-lxml-accepts', str(mine_error))
    if root is None:
        return lxml_only_rejection(error) or ('prototype-accepts-lxml-rejects', error)
    diff = view_difference(model_view(mine), lxml_view(root))
    return ('importers-disagree', diff) if diff else None


def shrink(text: str, still_fails, limit: int = 3000) -> str:
    """Greedy delta debugging: remove lines, then characters, while the failure persists."""
    calls = 0
    for unit in ('\n', ''):
        chunk = max(1, len(text.split(unit)) // 2) if unit else max(1, len(text) // 2)
        while chunk >= 1 and calls < limit:
            parts = text.split(unit) if unit else list(text)
            i, changed = 0, False
            while i < len(parts) and calls < limit:
                candidate = unit.join(parts[:i] + parts[i + chunk:])
                calls += 1
                if candidate != text and still_fails(candidate):
                    text, parts, changed = candidate, parts[:i] + parts[i + chunk:], True
                else:
                    i += chunk
            if not changed:
                chunk //= 2
    return text


# ---------------------------------------------------------------------------------------------
# Campaign

def run(seed: int = 1, docs: int = 300, mutations: int = 3, save: bool = True) -> dict:
    stats = {'seed': seed, 'documents': 0, 'sml_mutants': 0, 'sml_mutants_accepted': 0,
             'xml_mutants': 0, 'xml_mutants_both_accepted': 0, 'known_divergences': {}, 'findings': []}
    seen = set()

    def note(outcome, sample, fmt, reproduce):
        """Counts a known divergence, or shrinks, records, and saves a finding."""
        if isinstance(outcome, str):
            stats['known_divergences'][outcome] = stats['known_divergences'].get(outcome, 0) + 1
            return
        kind, message = outcome
        if (kind, message[:60]) in seen:
            return
        seen.add((kind, message[:60]))
        small = sample
        if reproduce is not None:
            def same_kind(candidate):
                try:
                    got = reproduce(candidate)
                except Exception:
                    return False
                return isinstance(got, tuple) and got[0] == kind
            small = shrink(sample, same_kind)
            message = reproduce(small)[1]
        finding = {'kind': kind, 'message': message, 'input_format': fmt, 'input': small,
                   'original_input': sample}
        stats['findings'].append(finding)
        if save:
            FINDINGS.mkdir(parents=True, exist_ok=True)
            stem = f"{len(stats['findings']):03d}-{kind}"
            (FINDINGS / f'{stem}.{fmt}').write_text(small, encoding='utf-8', newline='')
            (FINDINGS / f'{stem}.original.{fmt}').write_text(sample, encoding='utf-8', newline='')
            (FINDINGS / f'{stem}.txt').write_text(message + '\n', encoding='utf-8')

    def reproduce_xml(candidate):
        return check_xml_mutant(candidate.encode('utf-8'))

    for i in range(docs):
        r = random.Random(f'{seed}:{i}')
        doc = Generator(r).document()
        stats['documents'] += 1
        variant = VariantWriter(doc, r).write()
        for outcome in check_document(doc, variant):
            if isinstance(outcome, tuple) and outcome[0] == 'variant-sml':
                note(outcome, variant, 'sml', None)
            else:
                note(outcome, S.write_xml(doc), 'xml', None)
        try:
            sml = S.write_sml(doc)
            xml = S.write_xml(doc)
        except Exception:
            continue
        for m in range(mutations):
            mutant = mutate(sml if m % 2 == 0 else variant, SML_TOKENS, r, EDGE_TOKENS)
            stats['sml_mutants'] += 1
            try:
                S.read_sml(mutant)
                stats['sml_mutants_accepted'] += 1
            except Exception:
                pass
            outcome = check_sml_mutant(mutant)
            if outcome:
                note(outcome, mutant, 'sml', check_sml_mutant)

            xmut = mutate(xml, XML_TOKENS, r)
            stats['xml_mutants'] += 1
            outcome = reproduce_xml(xmut)
            if outcome:
                note(outcome, xmut, 'xml', reproduce_xml)
            else:
                try:
                    S.read_xml(xmut.encode('utf-8'))
                    stats['xml_mutants_both_accepted'] += 1
                except Exception:
                    pass
    return stats


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--seed', type=int, default=1)
    ap.add_argument('--docs', type=int, default=300)
    ap.add_argument('--mutations', type=int, default=3, help='SML and XML mutants per document')
    args = ap.parse_args()
    stats = run(args.seed, args.docs, args.mutations)
    print(f"seed {stats['seed']}: {stats['documents']} documents, "
          f"{stats['sml_mutants']} SML mutants ({stats['sml_mutants_accepted']} accepted), "
          f"{stats['xml_mutants']} XML mutants ({stats['xml_mutants_both_accepted']} accepted by both)")
    for reason, count in stats['known_divergences'].items():
        print(f'known divergence x{count}: {reason}')
    for f in stats['findings']:
        print(f"FINDING {f['kind']}: {f['message'][:300]}")
        print('  input:', repr(f['input'][:300]))
    return 1 if stats['findings'] else 0


if __name__ == '__main__':
    raise SystemExit(main())
