#!/usr/bin/env python3
"""SML 1.0-SVG-1 prototype: reader, canonical writer, XML importer/exporter, and comparison.

This is a prototype written to exercise Documents/SML-Language-Specification.md. It implements
the Core syntax, the strict profile's information model, and the canonical form (§14.2), but it
has not been validated independently and is NOT a certified conforming implementation.

Not implemented: the readable import profile (§3.1), alias generation by writers, and any
extension (every required extension is rejected; optional ones are skipped).
"""
from __future__ import annotations

import codecs
import re
import unicodedata
import xml.parsers.expat as expat

SML_VERSION = '1.0-SVG-1'
XML_NS = 'http://www.w3.org/XML/1998/namespace'
XMLNS_NS = 'http://www.w3.org/2000/xmlns/'
BASE_NS = {'xml': XML_NS}
LINE_LIMIT = 100        # canonical form: maximum statement line length (§14.2)
SEGMENT_LIMIT = 80      # canonical form: maximum continuation segment length (§14.2)


# ---------------------------------------------------------------------------------------------
# Errors

class SmlError(ValueError):
    """A document is malformed or not representable."""

    def __init__(self, message: str, line: int | None = None):
        self.line = line
        super().__init__(f'line {line}: {message}' if line else message)


class XmlInputError(SmlError):
    """XML input is malformed or violates Namespaces in XML."""


class UnsupportedXmlError(XmlInputError):
    """XML input contains information outside the supported subset (UNSUPPORTED_XML_INFORMATION)."""


# ---------------------------------------------------------------------------------------------
# Information model (§2)

class Text:
    __slots__ = ('data',)

    def __init__(self, data: str):
        self.data = data


class Comment:
    __slots__ = ('data',)

    def __init__(self, data: str):
        self.data = data


class PI:
    __slots__ = ('target', 'data')

    def __init__(self, target: str, data: str):
        self.target = target
        self.data = data


class Doctype:
    __slots__ = ('name', 'public_id', 'system_id')

    def __init__(self, name: str, public_id: str | None, system_id: str | None):
        self.name = name
        self.public_id = public_id
        self.system_id = system_id


class Element:
    """An element. ns_decls maps prefix (None = default) to URI, as declared on this element."""
    __slots__ = ('qname', 'expanded', 'ns_decls', 'attrs', 'attr_expanded', 'children')

    def __init__(self, qname: str):
        self.qname = qname
        self.expanded: tuple[str | None, str] | None = None
        self.ns_decls: dict[str | None, str] = {}
        self.attrs: dict[str, str] = {}
        self.attr_expanded: dict[str, tuple[str | None, str]] = {}
        self.children: list = []


class Document:
    def __init__(self):
        self.prolog: list = []          # Comment, PI, Doctype in document order
        self.root: Element | None = None
        self.epilog: list = []          # Comment, PI
        self.standalone: str | None = None   # 'yes', or None: standalone="no" is the same as no flag (§11)


# ---------------------------------------------------------------------------------------------
# Names and characters

_NSC = (r'A-Z_a-zÀ-ÖØ-öø-˿Ͱ-ͽͿ-῿‌-‍'
        r'⁰-↏Ⰰ-⿯、-퟿豈-﷏ﷰ-�\U00010000-\U000EFFFF')
_NC = _NSC + r'\-.0-9·̀-ͯ‿-⁀'
NCNAME = rf'[{_NSC}][{_NC}]*'
QNAME = rf'{NCNAME}(?::{NCNAME})?'
NAME = rf'[:{_NSC}][:{_NC}]*'
NCNAME_RE = re.compile(NCNAME)
QNAME_RE = re.compile(QNAME)
NAME_RE = re.compile(NAME)
ALIAS_RE = re.compile(rf'({NCNAME})[ \t]*("{{3,}})')
BLOCK_MARKER_RE = re.compile(r'"{3,}')
DIRECTIVE_RE = re.compile(r'@([^\s|"]+)')
UESC_RE = re.compile(r'\{([0-9A-Fa-f]{1,6})\}')
PUBID_RE = re.compile(r"[ \r\na-zA-Z0-9\-'()+,./:=?;!*#@$_%]*")
SIMPLE_ESCAPES = {'\\': '\\', '"': '"', 'n': '\n', 'r': '\r', 't': '\t'}
XML_WS = ' \t\n\r'


def is_ncname(s: str) -> bool:
    return NCNAME_RE.fullmatch(s) is not None


def is_qname(s: str) -> bool:
    return QNAME_RE.fullmatch(s) is not None


def is_name(s: str) -> bool:
    return NAME_RE.fullmatch(s) is not None


def is_xml_char(c: str) -> bool:
    o = ord(c)
    return (o in (0x9, 0xA, 0xD) or 0x20 <= o <= 0xD7FF or 0xE000 <= o <= 0xFFFD
            or 0x10000 <= o <= 0x10FFFF)


def check_chars(s: str, line: int | None = None) -> str:
    for c in s:
        if not is_xml_char(c):
            raise SmlError(f'character U+{ord(c):04X} is not allowed in XML', line)
    return s


def needs_escape(c: str) -> bool:
    """Characters writers write as \\u{…} so that no data is invisible (§4)."""
    o = ord(c)
    return 0x80 <= o <= 0x9F or o in (0x2028, 0x2029) or unicodedata.category(c) == 'Cf'


def strip_ht(s: str) -> str:
    return s.strip(' \t')


# ---------------------------------------------------------------------------------------------
# Namespace and node validation shared by the SML reader and the XML importer

def check_ns_decl(prefix: str | None, uri: str, line: int | None, error=SmlError) -> None:
    if prefix is not None and not is_ncname(prefix):
        raise error(f'invalid namespace prefix {prefix!r}', line)
    if prefix == 'xmlns':
        raise error("the prefix 'xmlns' cannot be declared", line)
    if prefix == 'xml' and uri != XML_NS:
        raise error("the prefix 'xml' can only be bound to " + XML_NS, line)
    if prefix != 'xml' and uri == XML_NS:
        raise error(XML_NS + " can only be bound to the prefix 'xml'", line)
    if uri == XMLNS_NS:
        raise error(XMLNS_NS + ' cannot be bound', line)
    if prefix is not None and uri == '':
        raise error(f'prefix {prefix!r} cannot be undeclared under Namespaces in XML 1.0', line)
    check_chars(uri, line)


def resolve_qname(qname: str, ns: dict, attr: bool, line: int | None, error=SmlError):
    if not is_qname(qname):
        raise error(f'invalid name {qname!r}', line)
    if ':' in qname:
        prefix, local = qname.split(':', 1)
        uri = ns.get(prefix)
        if not uri:
            raise error(f'prefix {prefix!r} is not bound', line)
        return (uri, local)
    if attr:
        return (None, qname)
    return (ns.get(None) or None, qname)


COMMENT_TRIM = ' \t\n'


def check_comment(data: str, line: int | None, error=SmlError) -> None:
    check_chars(data, line)
    if '--' in data:
        raise error("an XML comment cannot contain '--'", line)
    if '\r' in data:
        raise error('an XML comment cannot contain CR (U+000D)', line)


def shared_indentation(lines) -> str:
    """The leading spaces and tabs that all lines with a character other than space and tab share."""
    prefix = None
    for ln in lines:
        body = ln.lstrip(' \t')
        if not body:
            continue
        lead = ln[:len(ln) - len(body)]
        if prefix is None:
            prefix = lead
        else:
            k = 0
            while k < len(prefix) and k < len(lead) and prefix[k] == lead[k]:
                k += 1
            prefix = prefix[:k]
    return prefix or ''


def comment_text(data: str, own_line: bool = False) -> str:
    """A comment's normalized data (§9). The spaces, tabs, and line breaks at its end go. If its text starts on its own
    line (always so for SML, own_line), the blank lines before it go, and so does the indentation all its lines share;
    otherwise the first line loses its leading spaces and tabs, and the other lines the indentation they share. A
    line of spaces and tabs only that does not begin with the shared indentation becomes empty."""
    lines = data.rstrip(COMMENT_TRIM).split('\n')
    first = []
    if own_line or not lines[0].strip(' \t'):
        while lines and not lines[0].strip(' \t'):
            lines.pop(0)
    else:
        first = [lines.pop(0).lstrip(' \t')]
    shared = shared_indentation(lines)
    return '\n'.join(first + [ln[len(shared):] if ln.startswith(shared) else '' for ln in lines])


def comment_data(data: str, line: int | None, error=SmlError, own_line: bool = True) -> str:
    """A comment's data (§9): checked, then normalized by comment_text."""
    check_comment(data, line, error)
    return comment_text(data, own_line)


def check_pi(target: str, data: str, line: int | None, error=SmlError) -> None:
    if not is_ncname(target):
        raise error(f'invalid PI target {target!r} (PI targets are NCNames)', line)
    if target.lower() == 'xml':
        raise error("the PI target 'xml' is reserved", line)
    check_chars(data, line)
    if '?>' in data or '\r' in data:
        raise error("PI data cannot contain '?>' or CR (U+000D)", line)
    if data and data[0] in XML_WS:
        raise error('PI data cannot begin with whitespace', line)


def check_doctype(dt: Doctype, line: int | None, error=SmlError) -> None:
    if not is_name(dt.name):
        raise error(f'invalid DOCTYPE name {dt.name!r}', line)
    if dt.public_id is not None:
        p = dt.public_id
        if PUBID_RE.fullmatch(p) is None or any(c in p for c in '\r\n'):
            raise error('invalid character in DOCTYPE public ID', line)
        if p != p.strip(' ') or '  ' in p:
            raise error('DOCTYPE public ID is not normalized', line)
    if dt.system_id is not None:
        check_chars(dt.system_id, line)
        if '"' in dt.system_id and "'" in dt.system_id:
            raise error('DOCTYPE system ID cannot contain both quote characters', line)
        if '\r' in dt.system_id:
            raise error('DOCTYPE system ID cannot contain CR (U+000D); XML cannot represent it', line)


# ---------------------------------------------------------------------------------------------
# SML reader (§§4–13)

class _Frame:
    __slots__ = ('el', 'lexical', 'parent_ns', 'ns', 'depth', 'line', 'decl_open', 'ns_phase',
                 'inline_attrs', 'aliases', 'nodes', 'alias', 'noindent', 'layout', 'indentation', 'step',
                 'local_step')

    def __init__(self, el: Element, lexical: str, parent_ns: dict, depth: int, line: int):
        self.el = el
        self.lexical = lexical
        self.parent_ns = parent_ns
        self.ns: dict | None = None
        self.depth = depth
        self.line = line
        self.decl_open = True
        self.ns_phase = True
        self.inline_attrs: list = []
        self.aliases: dict = {}
        self.nodes: dict[str, str] = {}     # element aliases declared here, for its content (§8.7)
        self.alias: str | None = None       # the element alias it was opened with
        self.noindent = False
        self.layout: bool | None = None
        self.indentation = ''               # the element's own indentation (§6.4)
        self.step: str | None = None        # how much deeper its children are: the unit or its own @indent
        self.local_step = False


class _Pending:
    """A value that continuation lines (§6.5) may still extend."""
    __slots__ = ('append', 'finish')

    def __init__(self, append, finish=None):
        self.append = append
        self.finish = finish


def dedent_block(lines: list[str], prefix: str, first_line: int = 0) -> str:
    """The §7 dedentation: prefix, the closing marker's indentation, comes off each payload line, and the rest of
    the line is data. A line of spaces and tabs only that does not begin with prefix is empty; any other line must
    begin with prefix. first_line is the number of the first payload line, for errors."""
    out = []
    for k, ln in enumerate(lines):
        if ln.startswith(prefix):
            out.append(ln[len(prefix):])
        elif strip_ht(ln) == '':
            out.append('')
        else:
            raise SmlError("a block line must begin with the closing marker's indentation", first_line + k)
    return '\n'.join(out)


class SmlReader:
    def __init__(self, source):
        if isinstance(source, bytes):
            try:
                source = source.decode('utf-8')
            except UnicodeDecodeError as e:
                raise SmlError(f'invalid UTF-8: {e}') from None
        if source.startswith('﻿'):
            source = source[1:]
        self.lines = source.replace('\r\n', '\n').replace('\r', '\n').split('\n')
        self.i = 0
        self.doc = Document()
        self.stack: list[_Frame] = []
        self.phase = 'prolog'
        self.indent: str | None = DEFAULT_UNIT     # §6.4; @indent none turns the model off
        self.nodes: dict[str, str] = {}     # element aliases declared in the prolog (§8.7)
        self.seen_statement = False
        self.seen: set[str] = set()
        self.extensions: dict[str, bool] = {}
        self.pending: _Pending | None = None

    # -- driver ---------------------------------------------------------------------------------

    def parse(self) -> Document:
        while self.i < len(self.lines):
            raw = self.lines[self.i]
            self.i += 1
            ln = self.i
            s = strip_ht(raw)
            if self.pending is not None:
                if s.startswith('"'):
                    self.pending.append(self.parse_quoted_whole(s, ln))
                    continue
                self.finish_pending()
            if not s or s.startswith('#'):
                continue
            if s.startswith('"'):
                raise SmlError('continuation line without a preceding quoted value', ln)
            self.statement(s, ln)
            self.seen_statement = True
        self.finish_pending()
        if self.stack:
            raise SmlError(f'element {self.stack[-1].lexical!r} is not closed', self.stack[-1].line)
        if self.doc.root is None:
            raise SmlError('the document has no root element')
        return self.doc

    def finish_pending(self) -> None:
        if self.pending is not None:
            p, self.pending = self.pending, None
            if p.finish:
                p.finish()

    def statement(self, s: str, ln: int) -> None:
        if s == 'end' or (s.startswith('end') and s[3] in ' \t'):
            return self.st_end(s, ln)
        c = s[0]
        if c == '.':
            return self.st_attribute(s, ln)
        if c == '=':
            return self.st_text(s, ln)
        if c == '@':
            return self.st_directive(s, ln)
        m = ALIAS_RE.fullmatch(s)
        if m:
            return self.st_alias(m.group(1), m.group(2), ln)
        return self.st_element(s, ln)

    # -- values ---------------------------------------------------------------------------------

    def parse_quoted(self, s: str, pos: int, ln: int) -> tuple[str, int]:
        i, n, out = pos + 1, len(s), []
        while i < n:
            c = s[i]
            if c == '"':
                return ''.join(out), i + 1
            if c == '\\':
                e = s[i + 1] if i + 1 < n else ''
                if e in SIMPLE_ESCAPES and e:
                    out.append(SIMPLE_ESCAPES[e])
                    i += 2
                    continue
                if e == 'u':
                    m = UESC_RE.match(s, i + 2)
                    if not m:
                        raise SmlError('malformed \\u{…} escape (1 to 6 hex digits)', ln)
                    cp = int(m.group(1), 16)
                    if cp > 0x10FFFF or 0xD800 <= cp <= 0xDFFF:
                        raise SmlError(f'\\u{{{m.group(1)}}} is not a Unicode scalar value', ln)
                    if not is_xml_char(chr(cp)):
                        raise SmlError(f'\\u{{{m.group(1)}}} is not allowed in XML', ln)
                    out.append(chr(cp))
                    i = m.end()
                    continue
                raise SmlError(f'unknown escape \\{e}', ln)
            if not is_xml_char(c):
                raise SmlError(f'character U+{ord(c):04X} is not allowed in XML', ln)
            out.append(c)
            i += 1
        raise SmlError('unterminated quoted string', ln)

    def parse_quoted_whole(self, s: str, ln: int) -> str:
        value, end = self.parse_quoted(s, 0, ln)
        if end != len(s):
            raise SmlError('unexpected characters after the closing quote', ln)
        return value

    def parse_value_tail(self, rest: str, ln: int, allow_block: bool = True):
        """Returns (kind, value): 'empty', 'raw', 'quoted', or 'block' with its marker."""
        if rest == '':
            return 'empty', ''
        if rest[0] in ' \t':
            v = rest.lstrip(' \t')
        elif BLOCK_MARKER_RE.fullmatch(rest):
            v = rest        # the space before a block marker is optional (§6.2)
        else:
            raise SmlError('expected a space before the value', ln)
        if BLOCK_MARKER_RE.fullmatch(v):
            if not allow_block:
                raise SmlError('this value cannot be a block', ln)
            return 'block', v
        if v[0] == '"':
            return 'quoted', self.parse_quoted_whole(v, ln)
        return 'raw', check_chars(v, ln)

    def read_block(self, marker: str, ln: int) -> str:
        """Reads a block's payload up to the line that repeats its marker (§7)."""
        payload = []
        while self.i < len(self.lines):
            raw = self.lines[self.i]
            self.i += 1
            if strip_ht(raw) == marker:
                prefix = raw[:len(raw) - len(raw.lstrip(' \t'))]
                return check_chars(dedent_block(payload, prefix, ln + 1), ln)
            payload.append(raw)
        raise SmlError(f'unterminated block (expected a line {marker})', ln)

    def value_of(self, kind, value, ln):
        """Turns a parse_value_tail result into a string, reading a block if needed."""
        return self.read_block(value, ln) if kind == 'block' else value

    def tokens(self, rest: str, ln: int) -> list[tuple[str, bool]]:
        """Space-separated operands, each raw (no spaces) or quoted."""
        out, i, n = [], 0, len(rest)
        while True:
            while i < n and rest[i] in ' \t':
                i += 1
            if i >= n:
                return out
            if rest[i] == '"':
                value, i = self.parse_quoted(rest, i, ln)
                out.append((value, True))
            else:
                j = i
                while j < n and rest[j] not in ' \t':
                    j += 1
                out.append((check_chars(rest[i:j], ln), False))
                i = j
            if i < n and rest[i] not in ' \t':
                raise SmlError('operands must be separated by spaces', ln)

    # -- frames ---------------------------------------------------------------------------------

    def fix_ns(self, f: _Frame) -> None:
        if not f.ns_phase:
            return
        ns = dict(f.parent_ns)
        ns.update(f.el.ns_decls)
        f.ns = ns
        f.ns_phase = False
        f.el.expanded = resolve_qname(f.lexical, ns, False, f.line)
        for qname, value, ln in f.inline_attrs:
            self.set_attr(f, qname, value, ln)

    def set_attr(self, f: _Frame, qname: str, value: str, ln: int) -> None:
        if qname == 'xmlns' or qname.startswith('xmlns:'):
            raise SmlError("'xmlns' declarations are written with @ns or inline, not as attributes", ln)
        expanded = resolve_qname(qname, f.ns, True, ln)
        if expanded in f.el.attr_expanded.values() or qname in f.el.attrs:
            raise SmlError(f'duplicate attribute {qname!r}', ln)
        f.el.attrs[qname] = value
        f.el.attr_expanded[qname] = expanded

    def add_ns_decl(self, f: _Frame, prefix: str | None, uri: str, quoted: bool, ln: int) -> None:
        if not quoted and ':' not in uri:
            raise SmlError(f'a raw namespace URI must contain ":"; quote {uri!r} if it is relative', ln)
        check_ns_decl(prefix, uri, ln)
        if prefix in f.el.ns_decls:
            what = 'the default namespace' if prefix is None else f'prefix {prefix!r}'
            raise SmlError(f'{what} is declared twice on this element', ln)
        f.el.ns_decls[prefix] = uri

    def decl_frame(self, ln: int, what: str) -> _Frame:
        if not self.stack:
            raise SmlError(f'{what} outside an element', ln)
        f = self.stack[-1]
        if not f.decl_open:
            raise SmlError(f'{what} after the element\'s content has begun', ln)
        self.fix_ns(f)
        return f

    def close_decl(self, f: _Frame, is_text: bool) -> None:
        if f.decl_open:
            f.decl_open = False
            self.fix_ns(f)
            f.layout = self.indent is not None and not f.noindent and not is_text

    def add_child(self, node, is_text: bool, ln: int) -> None:
        """Adds content to the current element, or to the prolog/epilog for comments and PIs."""
        if not self.stack:
            if is_text or isinstance(node, Element):
                if isinstance(node, Element) and self.phase == 'prolog':
                    self.doc.root = node
                    self.phase = 'root'
                    return
                raise SmlError('content outside the root element', ln)
            (self.doc.prolog if self.phase == 'prolog' else self.doc.epilog).append(node)
            return
        f = self.stack[-1]
        self.close_decl(f, is_text)
        if is_text and f.layout:
            raise SmlError('text in a laid-out element (§6.4); mark the element @noindent', ln)
        if f.layout:
            f.el.children.append(Text('\n' + f.indentation + f.step))
        f.el.children.append(node)

    def open_element(self, qname: str, inline: list, kind: str, ln: int) -> _Frame:
        el = Element(qname)
        parent_ns = BASE_NS
        if self.stack:
            self.close_decl(self.stack[-1], False)
            parent_ns = self.stack[-1].ns
        f = _Frame(el, qname, parent_ns, len(self.stack), ln)
        if self.stack:
            parent = self.stack[-1]
            f.indentation = parent.indentation + (parent.step or '')
        f.step = self.indent
        for name, value, quoted in inline:
            if name == 'xmlns':
                self.add_ns_decl(f, None, value, quoted, ln)
            elif name.startswith('xmlns:'):
                self.add_ns_decl(f, name[6:], value, quoted, ln)
            else:
                f.inline_attrs.append((name, value, ln))
        self.add_child(el, False, ln)
        if kind == 'open':
            self.stack.append(f)
        else:
            self.fix_ns(f)
        return f

    # -- statements -----------------------------------------------------------------------------

    def st_end(self, s: str, ln: int) -> None:
        if not self.stack:
            raise SmlError("'end' without an open element", ln)
        f = self.stack[-1]
        if s != 'end':
            name = s[3:].lstrip(' \t')
            if not is_qname(name):
                raise SmlError("'end' must be followed by nothing or a single element name", ln)
            if name != f.lexical and name != f.alias:
                raise SmlError(f'end {name} does not match the open element {f.lexical!r}', ln)
        self.close_decl(f, True)
        if f.layout:
            f.el.children.append(Text('\n' + f.indentation))
        self.stack.pop()
        if not self.stack:
            self.phase = 'epilog'

    def st_attribute(self, s: str, ln: int) -> None:
        m = QNAME_RE.match(s, 1)
        if not m:
            raise SmlError('expected an attribute name after "."', ln)
        qname = m.group(0)
        f = self.decl_frame(ln, 'attribute')
        kind, value = self.parse_value_tail(s[m.end():], ln)
        self.set_attr(f, qname, self.value_of(kind, value, ln), ln)
        if kind == 'quoted':
            def append(more, f=f, q=qname):
                f.el.attrs[q] += more
            self.pending = _Pending(append)

    def st_text(self, s: str, ln: int) -> None:
        if not self.stack:
            raise SmlError('text outside the root element', ln)
        kind, value = self.parse_value_tail(s[1:], ln)
        node = Text(self.value_of(kind, value, ln))
        self.add_child(node, True, ln)
        if kind == 'quoted':
            self.pending = _Pending(lambda more, t=node: setattr(t, 'data', t.data + more))

    def st_alias(self, alias: str, marker: str, ln: int) -> None:
        f = self.decl_frame(ln, 'alias invocation')
        target = None
        for scope in reversed(self.stack):
            target = scope.aliases.get((f.el.expanded, alias))
            if target:
                break
        if target is None:
            raise SmlError(f'no active alias {alias!r} for element {f.lexical!r}', ln)
        self.set_attr(f, target, self.read_block(marker, ln), ln)

    def st_element(self, s: str, ln: int) -> None:
        m = QNAME_RE.match(s)
        if not m:
            raise SmlError(f'unrecognized statement {s!r}', ln)
        qname, pos = m.group(0), m.end()
        if qname == 'end':
            raise SmlError("the element name 'end' is reserved; use '@element end'", ln)
        alias, target = None, self.node_target(qname)
        if target is not None:
            alias, qname = qname, target
        inline = []
        if pos < len(s) and s[pos] == '(':
            inline, pos = self.parse_inline_list(s, pos, ln)
        rest = s[pos:]
        if rest == '':
            self.open_element(qname, inline, 'open', ln).alias = alias
        elif rest == '/':
            self.open_element(qname, inline, 'empty', ln)
        else:
            m2 = re.match(r'[ \t]+=', rest)
            if not m2:
                raise SmlError(f'unexpected {rest!r} after element name {qname!r}', ln)
            f = self.open_element(qname, inline, 'text', ln)
            kind, value = self.parse_value_tail(rest[m2.end():], ln)
            if kind != 'empty':
                node = Text(self.value_of(kind, value, ln))
                if kind == 'block' and self.indent is not None and (not self.stack or self.stack[-1].layout):
                    node.data = lay_out_block(node.data, f.indentation, self.indent)
                f.el.children.append(node)
                if kind == 'quoted':
                    self.pending = _Pending(lambda more, t=node: setattr(t, 'data', t.data + more))

    def parse_inline_list(self, s: str, pos: int, ln: int):
        i, n, entries = pos + 1, len(s), []

        def skip(i):
            while i < n and s[i] in ' \t':
                i += 1
            return i
        i = skip(i)
        if i < n and s[i] == ')':
            return entries, i + 1
        while True:
            m = QNAME_RE.match(s, i)
            if not m:
                raise SmlError('expected an attribute name in the inline list', ln)
            name = m.group(0)
            i = skip(m.end())
            if i >= n or s[i] != '=':
                raise SmlError(f"expected '=' after inline attribute {name!r}", ln)
            i = skip(i + 1)
            if i < n and s[i] == '"':
                value, i = self.parse_quoted(s, i, ln)
                quoted = True
            else:
                start, depth = i, 0
                while i < n:
                    c = s[i]
                    if c == '(':
                        depth += 1
                    elif c == ')':
                        if depth == 0:
                            break
                        depth -= 1
                    elif c == ',' and depth == 0:
                        break
                    i += 1
                if i >= n:
                    raise SmlError('unclosed inline list or unbalanced parenthesis', ln)
                value, quoted = check_chars(s[start:i].rstrip(' \t'), ln), False
            entries.append((name, value, quoted))
            i = skip(i)
            if i < n and s[i] == ',':
                i = skip(i + 1)
                continue
            if i < n and s[i] == ')':
                return entries, i + 1
            raise SmlError("expected ',' or ')' in the inline list", ln)

    def st_directive(self, s: str, ln: int) -> None:
        m = DIRECTIVE_RE.match(s)
        if not m:
            raise SmlError(f'malformed directive {s!r}', ln)
        name, rest = m.group(1), s[m.end():]
        if rest and rest[0] not in ' \t' and not BLOCK_MARKER_RE.fullmatch(rest):
            raise SmlError(f'expected a space after @{name}', ln)
        if ':' in name:
            return self.st_extension(name, rest, ln)
        handler = getattr(self, 'd_' + name, None)
        if handler is None:
            raise SmlError(f'unknown directive @{name}', ln)
        handler(rest, ln)

    def header(self, name: str, ln: int) -> None:
        if self.phase != 'prolog':
            raise SmlError(f'@{name} is only allowed before the root element', ln)
        if name in self.seen and name != 'use':
            raise SmlError(f'@{name} may appear only once', ln)
        self.seen.add(name)

    def d_sml(self, rest: str, ln: int) -> None:
        if self.seen_statement:
            raise SmlError('@sml must be the first statement', ln)
        self.header('sml', ln)
        operands = [t for t in re.split(r'[ \t]+', rest) if t]
        if len(operands) == 2 and operands[1] == 'tidy':   # written by a readable import (§3.1); no effect here
            operands.pop()
        if len(operands) != 1:
            raise SmlError('@sml takes a version, optionally followed by tidy', ln)
        version = operands[0]
        if version in (SML_VERSION, '1.0'):
            return
        if version.startswith('1.0-SVG-'):
            raise SmlError(f'unsupported draft revision {version}; this reader implements {SML_VERSION}', ln)
        raise SmlError(f'unsupported SML version {version!r}', ln)

    def d_standalone(self, rest: str, ln: int) -> None:
        self.header('standalone', ln)
        value = strip_ht(rest)
        if value not in ('yes', 'no') or not rest[:1] in (' ', '\t'):
            raise SmlError('@standalone takes yes or no', ln)
        self.doc.standalone = 'yes' if value == 'yes' else None   # no is the default (§11)

    def d_indent(self, rest: str, ln: int) -> None:
        value = strip_ht(rest)
        if value == 'none' and not self.stack:
            self.header('indent', ln)
            self.indent = None
            return
        if value == 'tab':
            unit = '\t'
        elif re.fullmatch(r'0|[1-9][0-9]?', value) and int(value) <= 16:
            unit = ' ' * int(value)
        else:
            raise SmlError('@indent takes tab, a width from 0 to 16, or (as a header) none', ln)
        if self.stack:     # in an element: how much deeper its children are (§6.4)
            f = self.decl_frame(ln, '@indent')
            if self.indent is None:
                raise SmlError('@indent in an element is not allowed under @indent none', ln)
            if f.local_step:
                raise SmlError('@indent may appear only once per element', ln)
            f.step, f.local_step = unit, True
            return
        self.header('indent', ln)
        self.indent = unit

    def d_doctype(self, rest: str, ln: int) -> None:
        self.header('doctype', ln)
        toks = self.tokens(rest, ln)
        if not toks or toks[0][1]:
            raise SmlError('@doctype needs a name', ln)
        kinds = [t[1] for t in toks]
        words = [t[0] for t in toks]
        if len(toks) == 1:
            dt = Doctype(words[0], None, None)
        elif words[1] == 'PUBLIC' and not kinds[1] and kinds[2:] == [True, True]:
            dt = Doctype(words[0], words[2], words[3])
        elif words[1] == 'SYSTEM' and not kinds[1] and kinds[2:] == [True]:
            dt = Doctype(words[0], None, words[2])
        else:
            raise SmlError('@doctype takes: name [PUBLIC "pubid" "sysid" | SYSTEM "sysid"]', ln)
        check_doctype(dt, ln)
        self.doc.prolog.append(dt)

    def d_use(self, rest: str, ln: int) -> None:
        self.header('use', ln)
        toks = [t for t, q in self.tokens(rest, ln)]
        if len(toks) not in (2, 3) or not is_ncname(toks[0]) or not re.fullmatch(r'[0-9]+\.[0-9]+', toks[1]):
            raise SmlError('@use takes: identifier major.minor [optional]', ln)
        if len(toks) == 3 and toks[2] != 'optional':
            raise SmlError("the third @use operand can only be 'optional'", ln)
        if toks[0] in self.extensions:
            raise SmlError(f'extension {toks[0]!r} is declared twice', ln)
        if len(toks) == 2:
            raise SmlError(f'required extension {toks[0]!r} is not implemented by this reader', ln)
        self.extensions[toks[0]] = True

    def st_extension(self, name: str, rest: str, ln: int) -> None:
        ident = name.split(':', 1)[0]
        if ident not in self.extensions:
            raise SmlError(f'directive @{name} belongs to an undeclared extension', ln)
        kind, value = self.parse_value_tail(rest, ln)
        self.value_of(kind, value, ln)          # optional extension: read and skip
        if kind == 'quoted':
            self.pending = _Pending(lambda more: None)

    def d_ns(self, rest: str, ln: int) -> None:
        if not self.stack:
            raise SmlError('@ns outside an element', ln)
        f = self.stack[-1]
        if not f.decl_open:
            raise SmlError("@ns after the element's content has begun", ln)
        if not f.ns_phase:
            raise SmlError('@ns must come before every other declaration statement (§5.1)', ln)
        toks = self.tokens(rest, ln)
        if len(toks) == 1:
            prefix, (uri, quoted) = None, toks[0]
        elif len(toks) == 2 and not toks[0][1]:
            prefix, (uri, quoted) = toks[0][0], toks[1]
        else:
            raise SmlError('@ns takes one or two operands: [prefix] URI', ln)
        self.add_ns_decl(f, prefix, uri, quoted, ln)

    def d_block(self, rest: str, ln: int) -> None:
        f = self.decl_frame(ln, '@block')
        toks = [t for t in re.split(r'[ \t]+', rest) if t]
        if len(toks) != 3 or not toks[1].startswith('.'):
            raise SmlError('@block takes: element-qname .attribute-qname alias', ln)
        element, attr, alias = toks[0], toks[1][1:], toks[2]
        expanded = resolve_qname(element, f.ns, False, ln)
        if not is_ncname(alias) or alias == 'end':
            raise SmlError(f'invalid alias name {alias!r}', ln)
        if attr == 'xmlns' or attr.startswith('xmlns:'):
            raise SmlError('an alias cannot target a namespace declaration', ln)
        resolve_qname(attr, f.ns, True, ln)
        if (expanded, alias) in f.aliases:
            raise SmlError(f'alias {alias!r} is declared twice for this element in one scope', ln)
        f.aliases[(expanded, alias)] = attr

    def node_target(self, name: str) -> str | None:
        """The element name that an element alias in scope gives name (§8.7), or None. The nearest scope wins."""
        for scope in reversed(self.stack):
            if name in scope.nodes:
                return scope.nodes[name]
        return self.nodes.get(name)

    def d_node(self, rest: str, ln: int) -> None:
        toks = [t for t in re.split(r'[ \t]+', rest) if t]
        if len(toks) != 2:
            raise SmlError('@node takes: element-qname alias', ln)
        element, alias = toks
        if not is_qname(element):
            raise SmlError(f'invalid element name {element!r} in @node', ln)
        if not is_ncname(alias) or alias == 'end':
            raise SmlError(f'invalid element alias {alias!r}', ln)
        if self.stack:
            scope = self.decl_frame(ln, '@node').nodes
        elif self.phase == 'prolog':
            scope = self.nodes
        else:
            raise SmlError('@node is only allowed before the root element or in a declaration section', ln)
        if alias in scope:
            raise SmlError(f'element alias {alias!r} is declared twice in one scope', ln)
        scope[alias] = element

    def d_noindent(self, rest: str, ln: int) -> None:
        f = self.decl_frame(ln, '@noindent')
        if rest.strip(' \t'):
            raise SmlError('@noindent takes no operands', ln)
        if f.noindent:
            raise SmlError('@noindent may appear only once per element', ln)
        f.noindent = True

    def d_rem(self, rest: str, ln: int) -> None:
        """@rem, or its synonym @comment: an XML comment (§9)."""
        self.d_comment(rest, ln)

    def d_comment(self, rest: str, ln: int) -> None:
        kind, value = self.parse_value_tail(rest, ln)
        node = Comment(self.value_of(kind, value, ln))
        self.add_child(node, False, ln)
        if kind == 'quoted':
            def append(more, c=node):
                c.data += more

            def finish(c=node):
                c.data = comment_data(c.data, ln)
            self.pending = _Pending(append, finish)
        else:
            node.data = comment_data(node.data, ln)

    def d_comments(self, rest: str, ln: int) -> None:
        kind, value = self.parse_value_tail(rest, ln)
        if kind != 'block':
            raise SmlError('@comments takes a block: @comments """, one comment per line', ln)
        data = self.read_block(value, ln)
        lines = [comment_data(line, ln) for line in data.split('\n')]
        if '' in lines:
            raise SmlError('@comments cannot have blank lines; each line is one comment', ln)
        for line in lines:
            self.add_child(Comment(line), False, ln)

    def d_pi(self, rest: str, ln: int) -> None:
        m = re.match(r'[ \t]+([^ \t]+)(.*)', rest)
        if not m:
            raise SmlError('@pi needs a target', ln)
        target, tail = m.group(1), m.group(2)
        kind, value = self.parse_value_tail(tail, ln, allow_block=False)
        node = PI(target, value)
        self.add_child(node, False, ln)
        if kind == 'quoted':
            def append(more, p=node):
                p.data += more
            self.pending = _Pending(append, lambda p=node: check_pi(p.target, p.data, ln))
        else:
            check_pi(target, value, ln)

    def d_element(self, rest: str, ln: int) -> None:
        qname = strip_ht(rest)
        if not rest[:1] in (' ', '\t') or not is_qname(qname):
            raise SmlError('@element takes a single element name', ln)
        self.open_element(qname, [], 'open', ln)


def read_sml(source) -> Document:
    return SmlReader(source).parse()


# ---------------------------------------------------------------------------------------------
# Canonical SML writer (§14.2)

DEFAULT_UNIT = '  '     # the unit without an @indent header (§6.4)
INDENT_CANDIDATES = ['  ', '    ', '\t'] + [' ' * n for n in (0, 1, 3) + tuple(range(5, 17))]


def content_nodes(el: Element) -> list:
    """Children with adjacent text coalesced and empty text dropped."""
    out = []
    for ch in el.children:
        if isinstance(ch, Text):
            if not ch.data:
                continue
            if out and isinstance(out[-1], Text):
                out[-1] = Text(out[-1].data + ch.data)
                continue
        out.append(ch)
    return out


def matches_layout(nodes: list, indentation: str, step: str) -> bool:
    """Whether content nodes are laid out (§6.4): LF, the indentation, and the step before each non-text child,
    and LF and the indentation before the end."""
    k = sum(1 for n in nodes if not isinstance(n, Text))
    if k == 0 or len(nodes) != 2 * k + 1:
        return False
    for idx, node in enumerate(nodes):
        if idx % 2 == 1:
            if isinstance(node, Text):
                return False
            continue
        want = '\n' + indentation + ('' if idx == 2 * k else step)
        if not isinstance(node, Text) or node.data != want:
            return False
    return True


def step_from(nodes: list, indentation: str) -> str | None:
    """The step that an element's first whitespace suggests, if it is a possible @indent value (§6.4)."""
    if nodes and isinstance(nodes[0], Text) and nodes[0].data.startswith('\n' + indentation):
        step = nodes[0].data[1 + len(indentation):]
        if step in INDENT_CANDIDATES:
            return step
    return None


def layout_step(nodes: list, indentation: str, unit: str) -> str | None:
    """The step with which content nodes are laid out at an indentation (rule 9.1–9.2): the unit, or the step their
    first whitespace suggests; None if neither lays them out."""
    if matches_layout(nodes, indentation, unit):
        return unit
    candidate = step_from(nodes, indentation)
    if candidate is not None and candidate != unit and matches_layout(nodes, indentation, candidate):
        return candidate
    return None


def laid_out_children(el: Element, indentation: str, unit: str) -> int:
    """How many child elements of el rule 9.1–9.2 lay out when they have this indentation."""
    count = 0
    for child in el.children:
        if isinstance(child, Element):
            nodes = content_nodes(child)
            if any(not isinstance(n, Text) for n in nodes) and layout_step(nodes, indentation, unit) is not None:
                count += 1
    return count


def plan(el: Element, nodes: list, indentation: str, unit: str | None):
    """The writer's whitespace decisions for an element (rule 9): (laid_out, step, local, noindent). step is the
    whitespace its children are indented by beyond its indentation, local the step written as @indent, if any."""
    if unit is None or all(isinstance(n, Text) for n in nodes):
        return False, unit, None, False
    step = layout_step(nodes, indentation, unit)
    if step is not None:
        return True, step, (step if step != unit else None), False
    # Exact (rule 9.3). Its children's indentation still follows its first whitespace if that lays out more of them.
    local = step_from(nodes, indentation)
    if local == unit or (local is not None and laid_out_children(el, indentation + local, unit)
                         <= laid_out_children(el, indentation + unit, unit)):
        local = None
    return False, unit if local is None else local, local, not isinstance(nodes[0], Text)


def default_matters(doc: Document) -> bool:
    """Whether SML written without a unit would read differently under the default unit (§6.4): the document has an
    element with a non-text child, or multi-line text as the root's only content. Then the writer declares
    @indent none (rule 9)."""
    stack = [doc.root]
    while stack:
        el = stack.pop()
        if any(not isinstance(n, Text) for n in content_nodes(el)):
            return True
        stack.extend(c for c in el.children if isinstance(c, Element))
    return any('\n' in n.data for n in content_nodes(doc.root))


def choose_indent(doc: Document) -> str | None:
    """The @indent unit (rule 9): the one with which the writer lays out the most elements, and then writes the fewest
    per-element @indent statements; ties go to the earlier candidate. None if no element would be laid out."""
    best, best_score = None, None
    for unit in INDENT_CANDIDATES:
        laid_out = locals_ = 0
        stack = [(doc.root, '')]
        while stack:
            el, indentation = stack.pop()
            lo, step, local, _ = plan(el, content_nodes(el), indentation, unit)
            laid_out += lo
            locals_ += local is not None
            stack.extend((c, indentation + step) for c in el.children if isinstance(c, Element))
        score = (laid_out, -locals_)
        if laid_out and (best_score is None or score > best_score):
            best, best_score = unit, score
    return best


def escape_units(value: str) -> list[str]:
    units = []
    for c in value:
        if c == '\\':
            units.append('\\\\')
        elif c == '"':
            units.append('\\"')
        elif c == '\n':
            units.append('\\n')
        elif c == '\r':
            units.append('\\r')
        elif c == '\t':
            units.append('\\t')
        elif needs_escape(c):
            units.append('\\u{%X}' % ord(c))
        else:
            units.append(c)
    return units


def quote(value: str) -> str:
    return '"' + ''.join(escape_units(value)) + '"'


def raw_ok(value: str) -> bool:
    """A non-empty value that can be written raw (§6.2) under canonical rule 7."""
    if not value or value[0] in '" \t' or value[-1] in ' \t':
        return False
    return not any(c in '\t\r\n' or needs_escape(c) for c in value)


def raw_inline_ok(value: str) -> bool:
    if value == '':
        return True
    if not raw_ok(value):
        return False
    depth = 0
    for c in value:
        if c == '(':
            depth += 1
        elif c == ')':
            if depth == 0:
                return False
            depth -= 1
        elif c == ',' and depth == 0:
            return False
    return depth == 0


def block_marker(value: str) -> str | None:
    """The marker to write value as a block's payload (§7): the shortest run of three or more quotation marks that
    no payload line equals, or None if a block cannot reproduce the value. The empty value has no payload lines."""
    if '\r' in value or any(needs_escape(c) for c in value):
        return None
    lines = value.split('\n') if value else []
    stripped = {strip_ht(ln) for ln in lines}
    marker = '"""'
    while marker in stripped:
        marker += '"'
    return marker


def visible(value: str) -> bool:
    """Whether a value has a character other than a space, tab, or line break: a block of whitespace only would show
    nothing, so it is clearer quoted (canonical rule 7.2)."""
    return value.strip(' \t\n') != ''


def block_tag(value: str) -> str | None:
    """The marker to write a value that contains LF and a visible character as a block (canonical rule 7.2), or
    None."""
    return block_marker(value) if '\n' in value and visible(value) else None


def lay_out_block(value: str, indentation: str, unit: str) -> str:
    """The text of a compact text element whose value is a block, in a laid-out position (§6.4): LF, each payload
    line indented one unit deeper than the element (empty lines stay empty), LF, and the element's own
    indentation."""
    if value == '':
        return '\n' + indentation
    inner = indentation + unit
    return '\n' + '\n'.join(inner + ln if ln else '' for ln in value.split('\n')) + '\n' + indentation


def laid_out_value(data: str, indentation: str, unit: str) -> str | None:
    """The block value that lay_out_block turns into data, or None if the data does not have that layout."""
    outer = indentation
    if not data.startswith('\n') or not data.endswith(outer) or len(data) < 1 + len(outer):
        return None
    middle = data[1:len(data) - len(outer)]
    if middle == '':
        value = ''
    else:
        if not middle.endswith('\n'):
            return None
        inner, lines = indentation + unit, []
        for ln in middle[:-1].split('\n'):
            if ln.startswith(inner):
                lines.append(ln[len(inner):])
            elif ln == '':
                lines.append('')
            else:
                return None
        value = '\n'.join(lines)
    if block_marker(value) is None or lay_out_block(value, indentation, unit) != data:
        return None
    return value


def comment_run_ok(data: str) -> bool:
    """Whether a comment can be one line of an @comments block (§9): non-empty, one line, and none of the
    characters §4 says to escape. Comment data never begins or ends with whitespace."""
    return data != '' and '\n' not in data and '\r' not in data and not any(needs_escape(c) for c in data)


def split_segments(units: list[str]) -> list[str]:
    segs, i = [], 0
    while i < len(units):
        j, length = i, 0
        while j < len(units) and length + len(units[j]) <= SEGMENT_LIMIT:
            length += len(units[j])
            j += 1
        if j == i:
            j = i + 1
        if j < len(units):
            spaces = [t for t in range(i, j) if units[t] == ' ']
            if spaces:
                j = spaces[-1] + 1
        segs.append(''.join(units[i:j]))
        i = j
    return segs


class SmlWriter:
    def __init__(self, doc: Document):
        self.doc = doc
        self.out: list[str] = []
        self.unit = choose_indent(doc)

    def write(self) -> str:
        d = self.doc
        self.out.append('@sml ' + SML_VERSION)
        if d.standalone:
            self.out.append('@standalone ' + d.standalone)
        if self.unit is None:
            if default_matters(d):
                self.out.append('@indent none')
        elif self.unit != DEFAULT_UNIT:
            self.out.append('@indent ' + ('tab' if self.unit == '\t' else str(len(self.unit))))
        self.items(d.prolog, 0, self.unit is not None, '')
        self.element(d.root, 0, '', self.unit is not None)
        self.items(d.epilog, 0, self.unit is not None, '')
        return '\n'.join(self.out) + '\n'

    def value(self, prefix: str, value: str, depth: int, allow_block: bool = True) -> None:
        """Writes `prefix` followed by value in canonical form (rule 7); depth is the statement's."""
        ind = '  ' * depth
        if value == '':
            self.out.append(prefix)
            return
        if raw_ok(value) and len(prefix) + 1 + len(value) <= LINE_LIMIT:
            self.out.append(prefix + ' ' + value)
            return
        tag = block_tag(value) if allow_block else None
        if tag is not None:
            self.out.append(prefix + ' ' + tag)
            inner = ind + '  '
            self.out.extend(inner + ln if ln else '' for ln in value.split('\n'))
            self.out.append(inner + tag)
            return
        units = escape_units(value)
        if len(prefix) + 3 + sum(map(len, units)) <= LINE_LIMIT:
            self.out.append(prefix + ' "' + ''.join(units) + '"')
            return
        segs = split_segments(units)
        self.out.append(prefix + ' "' + segs[0] + '"')
        self.out.extend(ind + '  "' + seg + '"' for seg in segs[1:])

    def items(self, nodes: list, depth: int, laid_out: bool, indentation: str) -> None:
        """Writes content statements at depth; a run of two or more comments that comment_run_ok accepts is one
        @comments block (canonical rule 11). In laid-out content, indentation is the children's indentation."""
        i = 0
        while i < len(nodes):
            j = i
            while j < len(nodes) and isinstance(nodes[j], Comment) and comment_run_ok(nodes[j].data):
                j += 1
            if j - i >= 2:
                lines = [n.data for n in nodes[i:j]]
                ind = '  ' * depth
                marker = block_marker('\n'.join(lines))
                self.out.append(ind + '@comments ' + marker)
                self.out.extend(ind + '  ' + ln for ln in lines)
                self.out.append(ind + '  ' + marker)
                i = j
                continue
            node = nodes[i]
            if isinstance(node, Text):
                self.value('  ' * depth + '=', node.data, depth)
            elif isinstance(node, Element):
                self.element(node, depth, indentation, laid_out)
            else:
                self.misc(node, depth)
            i += 1

    def block(self, prefix: str, value: str, depth: int) -> None:
        """Writes prefix and value as a block whose payload is value's lines."""
        ind, marker = '  ' * depth, block_marker(value)
        self.out.append(prefix + ' ' + marker)
        if value:
            self.out.extend(ind + '  ' + ln if ln else '' for ln in value.split('\n'))
        self.out.append(ind + '  ' + marker)

    def comment(self, data: str, depth: int) -> None:
        """Writes a comment like any other value (rule 7): a block is literal wherever it is (§9)."""
        self.value('  ' * depth + '@rem', data, depth)

    def compact_text(self, prefix: str, data: str, depth: int, indentation: str, laid_out: bool) -> bool:
        """Writes prefix and an element's only text in the compact form, and returns True; or returns False when
        the element must use the normal form: in a laid-out position a compact block has predicted edges
        (§6.4), so text that needs a literal block is written as an = statement instead (rule 6)."""
        if self.unit is None or not laid_out or not data or (raw_ok(data) and len(prefix) + 1 + len(data) <= LINE_LIMIT):
            self.value(prefix, data, depth)
            return True
        value = laid_out_value(data, indentation, self.unit)
        if value is not None and visible(value):
            self.block(prefix, value, depth)
            return True
        if block_tag(data) is not None:
            return False
        self.value(prefix, data, depth, allow_block=False)
        return True

    def misc(self, item, depth: int) -> None:
        ind = '  ' * depth
        if isinstance(item, Comment):
            self.comment(item.data, depth)
        elif isinstance(item, PI):
            self.value(ind + '@pi ' + item.target, item.data, depth, allow_block=False)
        elif isinstance(item, Doctype):
            line = ind + '@doctype ' + item.name
            if item.public_id is not None:
                line += ' PUBLIC ' + quote(item.public_id) + ' ' + quote(item.system_id)
            elif item.system_id is not None:
                line += ' SYSTEM ' + quote(item.system_id)
            self.out.append(line)

    def element(self, el: Element, depth: int, indentation: str = '', laid_out_position: bool = False) -> None:
        """Writes an element; indentation is its own indentation in the XML (§6.4), and laid_out_position says
        whether whitespace before it is predicted."""
        ind, inner = '  ' * depth, '  ' * (depth + 1)
        nodes = content_nodes(el)
        laid_out, step, local, noindent = plan(el, nodes, indentation, self.unit)
        if laid_out:
            nodes = [n for n in nodes if not isinstance(n, Text)]
        decls = sorted(el.ns_decls.items(), key=lambda kv: (kv[0] is not None, kv[0] or ''))

        def attr_key(q):
            uri, local = el.attr_expanded[q]
            return (uri is not None, uri or '', local)
        attrs = [(q, el.attrs[q]) for q in sorted(el.attrs, key=attr_key)]
        entries = [('xmlns' if p is None else 'xmlns:' + p, u, True) for p, u in decls]
        entries += [(q, v, False) for q, v in attrs]
        inline = el.qname != 'end' and all(
            raw_inline_ok(v) and (not is_ns or ':' in v) for _, v, is_ns in entries)
        opener = el.qname
        if entries and inline:
            opener += '(' + ', '.join(f'{q}={v}' for q, v, _ in entries) + ')'
            inline = len(ind + opener) <= LINE_LIMIT
        if inline or not entries:
            if el.qname != 'end' and not nodes:
                self.out.append(ind + opener + '/')
                return
            if (el.qname != 'end' and len(nodes) == 1 and isinstance(nodes[0], Text)
                    and self.compact_text(ind + opener + ' =', nodes[0].data, depth, indentation, laid_out_position)):
                return
        if el.qname == 'end':
            self.out.append(ind + '@element end')
        elif inline or not entries:
            self.out.append(ind + opener)
        else:
            self.out.append(ind + el.qname)
        if entries and not (inline and el.qname != 'end'):
            for p, u in decls:
                tok = u if raw_ok(u) and ':' in u and ' ' not in u else quote(u)
                self.out.append(inner + '@ns ' + ('' if p is None else p + ' ') + tok)
            for q, v in attrs:
                self.value(inner + '.' + q, v, depth + 1)
        if local is not None:
            self.out.append(inner + '@indent ' + ('tab' if local == '\t' else str(len(local))))
        if noindent:
            self.out.append(inner + '@noindent')
        self.items(nodes, depth + 1, laid_out, indentation + (step or ''))
        self.out.append(ind + 'end')


def write_sml(doc: Document) -> str:
    return SmlWriter(doc).write()


# ---------------------------------------------------------------------------------------------
# XML importer (expat, no namespace processing; namespaces are resolved here)

EXPAT_ENCODINGS = {'utf-8', 'utf-16', 'iso-8859-1', 'us-ascii'}
# Registered (IANA) charset names transcoded through Python codecs, plus the common "UTF8"
# spelling. Python also knows non-standard aliases ("UTF", "U8") that XML parsers reject.
TRANSCODED_ENCODINGS = (
    {'utf8', 'shift_jis', 'euc-jp', 'iso-2022-jp', 'gb2312', 'gbk', 'gb18030', 'big5', 'euc-kr',
     'koi8-r', 'koi8-u'}
    | {f'iso-8859-{n}' for n in range(2, 17) if n != 12}
    | {f'windows-{n}' for n in range(1250, 1259)})
XML_DECL_ENCODING_RE = re.compile(
    rb'<\?xml[ \t\r\n]+version[ \t\r\n]*=[ \t\r\n]*(?:"[^"]*"|\'[^\']*\')[ \t\r\n]+'
    rb'encoding[ \t\r\n]*=[ \t\r\n]*["\']([A-Za-z][A-Za-z0-9._-]*)["\']')


def _transcode(data: bytes) -> tuple[bytes, str | None]:
    """Expat handles only its four built-in encodings natively, and decodes any other declared
    encoding one byte at a time. For the encodings in TRANSCODED_ENCODINGS, decode with Python's
    codec instead and parse as UTF-8; reject every other declared encoding."""
    m = XML_DECL_ENCODING_RE.match(data[3:] if data.startswith(b'\xef\xbb\xbf') else data)
    if not m or m.group(1).decode('ascii').lower() in EXPAT_ENCODINGS:
        return data, None
    name = m.group(1).decode('ascii')
    if name.lower() not in TRANSCODED_ENCODINGS:
        raise XmlInputError(f'unsupported encoding {name!r}')
    try:
        return data.decode(codecs.lookup(name).name).encode('utf-8'), 'UTF-8'
    except UnicodeDecodeError as e:
        raise XmlInputError(f'the document is not valid {name}: {e.reason}') from None


def read_xml(data) -> Document:
    if isinstance(data, str):
        data = data.encode('utf-8')
    data, override = _transcode(data)
    doc = Document()
    stack: list[tuple[Element, dict]] = []
    p = expat.ParserCreate(override)
    p.ordered_attributes = True
    p.buffer_text = True
    p.SetParamEntityParsing(expat.XML_PARAM_ENTITY_PARSING_NEVER)

    def line():
        return p.CurrentLineNumber

    def place(node):
        if stack:
            stack[-1][0].children.append(node)
        elif doc.root is None:
            doc.prolog.append(node)
        else:
            doc.epilog.append(node)

    def xml_decl(version, encoding, standalone):
        if version != '1.0':
            raise UnsupportedXmlError(f'XML {version} is not supported', line())
        doc.standalone = 'yes' if standalone == 1 else None   # standalone="no" is the same as no flag (§11)

    def doctype(name, sysid, pubid, has_internal_subset):
        if has_internal_subset:
            raise UnsupportedXmlError('DTD internal subsets are not supported', line())
        if pubid is not None:  # XML 1.0 §4.2.2 normalization (XML whitespace only)
            pubid = re.sub(r'[ \t\r\n]+', ' ', pubid).strip(' ')
        if sysid is not None:  # expat skips XML §2.11 line-end normalization in this literal
            sysid = sysid.replace('\r\n', '\n').replace('\r', '\n')
        dt = Doctype(name, pubid, sysid)
        check_doctype(dt, line(), UnsupportedXmlError)
        doc.prolog.append(dt)

    def unsupported(what):
        def handler(*args):
            raise UnsupportedXmlError(what, line())
        return handler

    def start(name, attrs):
        el = Element(name)
        parent_ns = stack[-1][1] if stack else BASE_NS
        pairs = list(zip(attrs[0::2], attrs[1::2]))
        for q, v in pairs:
            if q == 'xmlns' or q.startswith('xmlns:'):
                prefix = None if q == 'xmlns' else q[6:]
                check_ns_decl(prefix, v, line(), XmlInputError)
                el.ns_decls[prefix] = v
        ns = dict(parent_ns)
        ns.update(el.ns_decls)
        el.expanded = resolve_qname(name, ns, False, line(), XmlInputError)
        for q, v in pairs:
            if q == 'xmlns' or q.startswith('xmlns:'):
                continue
            expanded = resolve_qname(q, ns, True, line(), XmlInputError)
            if expanded in el.attr_expanded.values():
                raise XmlInputError(f'duplicate expanded attribute name {q!r}', line())
            el.attrs[q] = v
            el.attr_expanded[q] = expanded
        if stack:
            stack[-1][0].children.append(el)
        else:
            doc.root = el
        stack.append((el, ns))

    def end(name):
        stack.pop()

    def chars(data):
        if not stack:
            return
        kids = stack[-1][0].children
        if kids and isinstance(kids[-1], Text):
            kids[-1].data += data
        else:
            kids.append(Text(data))

    def comment(data):
        place(Comment(comment_data(data, line(), XmlInputError, own_line=False)))

    def pi(target, data):
        check_pi(target, data, line(), XmlInputError)
        place(PI(target, data))

    p.XmlDeclHandler = xml_decl
    p.StartDoctypeDeclHandler = doctype
    p.EntityDeclHandler = unsupported('entity declarations are not supported')
    p.SkippedEntityHandler = unsupported('reference to an undeclared entity')
    p.ExternalEntityRefHandler = unsupported('external entities are not supported')
    p.StartElementHandler = start
    p.EndElementHandler = end
    p.CharacterDataHandler = chars
    p.CommentHandler = comment
    p.ProcessingInstructionHandler = pi
    try:
        p.Parse(data, True)
    except expat.ExpatError as e:
        raise XmlInputError(f'malformed XML: {expat.errors.messages[e.code]}', e.lineno) from None
    except LookupError as e:  # expat raises this for an unknown encoding declaration
        raise XmlInputError(f'unsupported encoding: {e}') from None
    if doc.root is None:
        raise XmlInputError('the document has no root element')
    return doc


# ---------------------------------------------------------------------------------------------
# XML exporter

def _esc_text(s: str) -> str:
    return (s.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')
            .replace('\r', '&#13;'))


def _esc_attr(s: str) -> str:
    return (s.replace('&', '&amp;').replace('<', '&lt;').replace('"', '&quot;')
            .replace('\t', '&#9;').replace('\n', '&#10;').replace('\r', '&#13;'))


def _tail_indentation(node) -> str | None:
    """The spaces and tabs after the last line break of a text node, if nothing else follows it there."""
    if isinstance(node, Text) and '\n' in node.data:
        tail = node.data[node.data.rindex('\n') + 1:]
        if not tail.strip(' \t'):
            return tail
    return None


def _comment_xml(data: str, indentation: str = '', step: str = '  ') -> str:
    """A comment as XML (§11): <!-- data --> on one line, or a multi-line comment as pretty-printers write it, its
    lines one step deeper than the comment and --> at the comment's indentation."""
    if '\n' not in data:
        return f'<!-- {data} -->' if data else '<!---->'
    inner = indentation + step
    return '<!--\n' + '\n'.join(inner + ln if ln else '' for ln in data.split('\n')) + '\n' + indentation + '-->'


def _misc_xml(item) -> str:
    if isinstance(item, Comment):
        return _comment_xml(item.data)
    if isinstance(item, PI):
        return f'<?{item.target} {item.data}?>' if item.data else f'<?{item.target}?>'
    s = '<!DOCTYPE ' + item.name
    if item.system_id is not None:
        q = "'" if '"' in item.system_id else '"'
        if item.public_id is not None:
            s += f' PUBLIC "{item.public_id}"'
        else:
            s += ' SYSTEM'
        s += f' {q}{item.system_id}{q}'
    return s + '>'


def _element_xml(el: Element, out: list[str], indentation: str = '') -> None:
    """Writes an element; indentation is the spaces and tabs before it on its line, as far as its preceding text
    shows them (§11), else its parent's."""
    out.append('<' + el.qname)
    for p, u in el.ns_decls.items():
        out.append(f' {"xmlns" if p is None else "xmlns:" + p}="{_esc_attr(u)}"')
    for q, v in el.attrs.items():
        out.append(f' {q}="{_esc_attr(v)}"')
    if not el.children:
        out.append('/>')
        return
    out.append('>')
    previous = None
    for ch in el.children:
        own = _tail_indentation(previous)
        at = indentation if own is None else own
        if isinstance(ch, Element):
            _element_xml(ch, out, at)
        elif isinstance(ch, Text):
            out.append(_esc_text(ch.data))
        elif isinstance(ch, Comment):
            deeper = len(at) > len(indentation) and at.startswith(indentation)
            out.append(_comment_xml(ch.data, at, at[len(indentation):] if deeper else '  '))
        else:
            out.append(_misc_xml(ch))
        previous = ch
    out.append(f'</{el.qname}>')


def write_xml(doc: Document) -> str:
    decl = '<?xml version="1.0" encoding="UTF-8"'
    if doc.standalone:
        decl += f' standalone="{doc.standalone}"'
    parts = [decl + '?>']
    parts += [_misc_xml(x) for x in doc.prolog]
    body: list[str] = []
    _element_xml(doc.root, body)
    parts.append(''.join(body))
    parts += [_misc_xml(x) for x in doc.epilog]
    return '\n'.join(parts) + '\n'


# ---------------------------------------------------------------------------------------------
# Comparison: the strict profile's equality (§3), including prefixes and declaration locations

def _misc_sig(item):
    if isinstance(item, Comment):
        return ('comment', item.data)
    if isinstance(item, PI):
        return ('pi', item.target, item.data)
    return ('doctype', item.name, item.public_id, item.system_id)


def element_signature(el: Element):
    def name(e):
        return (e[0] or '', e[1])
    decls = tuple(sorted(((p or ''), u) for p, u in el.ns_decls.items()))
    attrs = tuple(sorted((name(el.attr_expanded[q]), q, v) for q, v in el.attrs.items()))
    kids = []
    for n in content_nodes(el):
        if isinstance(n, Text):
            kids.append(('text', n.data))
        elif isinstance(n, Element):
            kids.append(element_signature(n))
        else:
            kids.append(_misc_sig(n))
    return ('element', el.qname, name(el.expanded), decls, attrs, tuple(kids))


def signature(doc: Document):
    return ('document', doc.standalone, tuple(_misc_sig(x) for x in doc.prolog),
            element_signature(doc.root), tuple(_misc_sig(x) for x in doc.epilog))


def first_difference(a, b, path: str = '') -> str | None:
    """Describes the first difference between two signatures, or returns None if equal."""
    if a == b:
        return None
    if type(a) is not type(b) or not isinstance(a, tuple):
        return f'{path or "/"}: {a!r} != {b!r}'
    if a[:1] == ('element',) and b[:1] == ('element',) and a[1] == b[1]:
        path = f'{path}/{a[1]}'
    for idx, (x, y) in enumerate(zip(a, b)):
        diff = first_difference(x, y, f'{path}[{idx}]' if not isinstance(x, tuple) else path)
        if diff:
            return diff
    return f'{path or "/"}: length {len(a)} != {len(b)}'


def rt1_signature(doc: Document):
    """The element-tree signature used by the lxml cross-check (RT-1, root subtree only)."""
    def clark(e):
        return e[1] if e[0] is None else '{%s}%s' % e

    def sig(el):
        attrs = sorted((clark(el.attr_expanded[q]), v) for q, v in el.attrs.items())
        kids = []
        for n in content_nodes(el):
            if isinstance(n, Text):
                kids.append(['text', n.data])
            elif isinstance(n, Element):
                kids.append(sig(n))
            elif isinstance(n, Comment):
                kids.append(['comment', n.data])
            else:
                kids.append(['pi', n.target, n.data])
        return ['element', clark(el.expanded), [list(x) for x in attrs], kids]
    return sig(doc.root)
