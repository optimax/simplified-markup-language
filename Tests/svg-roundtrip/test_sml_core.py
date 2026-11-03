#!/usr/bin/env python3
"""Core parser and writer tests for the SML 1.0-SVG-1 prototype.

CoreFixtures covers CP-1 to CP-24 (§15.1). SmlSideFixtures covers the SV fixtures of §15 that
need only SML: SV-31, SV-33 to SV-56, SV-58, SV-64, SV-70 to SV-72, and SV-76.
Run with `python -m unittest test_sml_core` from this directory.
"""
import unittest

import sml_prototype as S

SVG = 'http://www.w3.org/2000/svg'
XML_NS = S.XML_NS


def read(src):
    return S.read_sml(src)


def root(src):
    return read(src).root


def kids(el, kind=S.Element):
    return [c for c in el.children if isinstance(c, kind)]


def text_of(el):
    return ''.join(c.data for c in el.children if isinstance(c, S.Text))


def sig(src):
    return S.signature(read(src))


class Base(unittest.TestCase):
    def rejects(self, src, fragment=None):
        with self.assertRaises(S.SmlError) as cm:
            read(src)
        if fragment:
            self.assertIn(fragment, str(cm.exception))

    def attr(self, src, name):
        return root(src).attrs[name]


class CoreFixtures(Base):
    def test_CP01_bom(self):
        src = 'svg(xmlns=http://www.w3.org/2000/svg, a=1)/\n'
        self.assertEqual(sig('\ufeff' + src), sig(src))
        self.assertEqual(S.signature(S.read_sml(('\ufeff' + src).encode('utf-8'))), sig(src))
        self.assertFalse(S.write_sml(read('\ufeff' + src)).startswith('\ufeff'))

    def test_CP02_line_endings(self):
        lf = 'g\n  .a 1\n  = """\n    x\n    y\n  """\nend\n'
        self.assertEqual(sig(lf.replace('\n', '\r\n')), sig(lf))
        self.assertEqual(sig(lf.replace('\n', '\r')), sig(lf))

    def test_CP03_unicode_line_separators_are_data(self):
        el = root('g\n  .a x\u0085y\u2028z\u2029w\nend\n')
        self.assertEqual(el.attrs['a'], 'x\u0085y\u2028z\u2029w')
        out = S.write_sml(read('g\n  .a x\u2028y\nend\n'))
        self.assertIn('\\u{2028}', out)

    def test_CP04_unicode_escapes(self):
        ok = {r'"\u{41}"': 'A', r'"\u{1f600}"': '\U0001F600', r'"\u{01F600}"': '\U0001F600'}
        for value, expected in ok.items():
            self.assertEqual(self.attr(f'g\n  .a {value}\nend\n', 'a'), expected)
        for bad in (r'\u{}', r'\u{001F600}', r'\u{D800}', r'\u{110000}', r'\u{0}', r'\u0041'):
            self.rejects(f'g\n  .a "{bad}"\nend\n')

    def test_CP05_unknown_escape(self):
        self.rejects('g\n  .a "\\x41"\nend\n', 'unknown escape')

    def test_CP06_end_is_reserved(self):
        for stmt in ('end()', 'end/', 'end(id=a)/', 'end = x'):
            self.rejects(f'g\n  {stmt}\nend\n')
        el = root('g(xmlns:x=urn:x)\n  x:end/\nend\n')
        self.assertEqual(kids(el)[0].qname, 'x:end')

    def test_CP07_checked_end(self):
        root('g\n  h\n  end h\nend g\n')
        self.rejects('g\n  h\n  end g\nend\n', 'does not match')
        self.rejects('g\nend g h\n')

    def test_CP08_end_lines_are_payload(self):
        self.assertEqual(self.attr('g\n  .a """\n    end path\n    end\n  """\nend\n', 'a'), 'end path\nend')

    def test_CP09_longer_block_markers(self):
        self.assertEqual(self.attr('g\n  .a """"\n    x\n    """\n  """"\nend\n', 'a'), 'x\n"""')
        self.assertEqual(self.attr('g\n  .a """""\n    """"\n  """""\nend\n', 'a'), '""""')
        self.rejects('g\n  .a """"\n    x\n  """\nend\n', 'unterminated block')
        self.assertEqual(self.attr('g\n  .a ""\nend\n', 'a'), '')
        self.rejects('g\n  .a """x\nend\n')

    def test_CP10_values_beginning_with_bar(self):
        self.assertEqual(root('g(a=|x)/\n').attrs['a'], '|x')
        self.assertEqual(read('@pi t |x\ng/\n').prolog[0].data, '|x')
        self.assertEqual(root('g\n  @ns |x:y\nend\n').ns_decls[None], '|x:y')
        self.assertEqual(root('g(a="|x")/\n').attrs['a'], '|x')
        self.assertEqual(read('@pi t "|x"\ng/\n').prolog[0].data, '|x')
        self.assertEqual(root('g\n  @ns "|x"\nend\n').ns_decls[None], '|x')

    def test_CP11_inline_whitespace(self):
        el = root('a( x = 1 , y=2 )/\n')
        self.assertEqual(el.attrs, {'x': '1', 'y': '2'})

    def test_CP12_balanced_parentheses(self):
        el = root('use(transform=translate(10,20) rotate(45), fill=url(#a))/\n')
        self.assertEqual(el.attrs['transform'], 'translate(10,20) rotate(45)')
        self.assertEqual(el.attrs['fill'], 'url(#a)')
        self.rejects('rect(a=(x)/\n')
        self.rejects('rect(a=x)y)/\n')

    def test_CP13_continuation_lines(self):
        src = ('g\n  .a "one "\n    "two"\n  = "t1"\n    "t2"\n  h = "i1"\n    "i2"\n'
               '  @comment "c1"\n    "c2"\n  @pi p "p1"\n    "p2"\nend\n')
        el = root(src)
        self.assertEqual(el.attrs['a'], 'one two')
        self.assertEqual(el.children[0].data, 't1t2')
        self.assertEqual(text_of(kids(el)[0]), 'i1i2')
        self.assertEqual(kids(el, S.Comment)[0].data, 'c1c2')
        self.assertEqual(kids(el, S.PI)[0].data, 'p1p2')
        self.rejects('g\n  .a raw\n  "x"\nend\n', 'continuation')
        self.rejects('g\n  .a "x"\n\n  "y"\nend\n', 'continuation')
        self.rejects('g\n  .a "x"\n  # note\n  "y"\nend\n', 'continuation')
        self.rejects('g\n  @ns "urn:x"\n  "y"\nend\n', 'continuation')

    def test_CP14_block_dedentation(self):
        src = 'g\n  .a """\n    a\n\n      b\n   \t  \n    c\n  """\nend\n'
        self.assertEqual(self.attr(src, 'a'), 'a\n\n  b\n\nc')
        self.assertEqual(self.attr('g\n  .a """\n\t a\n\t b\n  """\nend\n', 'a'), 'a\nb')
        self.assertEqual(self.attr('g\n  .a """\n\ta\n  b\n  """\nend\n', 'a'), '\ta\n  b')
        self.assertEqual(self.attr('g\n  .a """\n  """\nend\n', 'a'), '')

    def test_CP15_alias_inside_explicit_element(self):
        el = root('g\n  @block path .d data\n  @element path\n    data """\n      M 0 0\n    """\n  end\nend\n')
        self.assertEqual(kids(el)[0].attrs['d'], 'M 0 0')

    def test_CP16_ns_must_come_first(self):
        for stmt in ('.a 1', '@block g .y x', '@noindent'):
            self.rejects(f'g\n  {stmt}\n  @ns urn:x\nend\n', '@ns must come')
        self.rejects('g\n  @block g .y x\n  x """\n    v\n  """\n  @ns urn:x\nend\n', '@ns must come')

    def test_CP17_ns_operands(self):
        self.rejects('g\n  @ns s\nend\n', 'must contain ":"')
        self.rejects('g\n  @ns a b c\nend\n')
        self.assertEqual(root('g\n  @ns ""\nend\n').ns_decls[None], '')
        self.assertEqual(root('g\n  @ns "relative"\nend\n').ns_decls[None], 'relative')
        self.rejects('g(xmlns=s)/\n', 'must contain ":"')

    def test_CP18_xmlns_declarations(self):
        self.rejects('g\n  .xmlns urn:x\nend\n')
        self.rejects('g\n  .xmlns:p urn:x\nend\n')
        el = root('p:x(xmlns:p=urn:vendor, xmlns=urn:d)/\n')
        self.assertEqual(el.ns_decls, {'p': 'urn:vendor', None: 'urn:d'})
        self.assertEqual(el.expanded, ('urn:vendor', 'x'))
        self.assertEqual(root('y(xmlns=urn:d) = t\n').expanded, ('urn:d', 'y'))
        self.rejects('g(xmlns:p=urn:a)\n  @ns p urn:b\nend\n', 'declared twice')

    def test_CP19_reserved_namespaces(self):
        el = root(f'g\n  @ns xml {XML_NS}\nend\n')
        self.assertEqual(el.ns_decls, {'xml': XML_NS})
        self.rejects('g\n  @ns xml urn:other\nend\n')
        self.rejects(f'g\n  @ns p {XML_NS}\nend\n')
        self.rejects(f'g\n  @ns {XML_NS}\nend\n')
        self.rejects('g\n  @ns p http://www.w3.org/2000/xmlns/\nend\n')
        self.rejects('g\n  @ns xmlns urn:x\nend\n')

    def test_CP20_version_header(self):
        self.rejects('@comment x\n@sml 1.0-SVG-1\ng/\n', 'first statement')
        self.rejects('@sml 1.0-SVG-2\ng/\n', 'unsupported draft')
        for header in ('@sml 1.0-SVG-1\n', '@sml 1.0\n', '', '# note\n@sml 1.0-SVG-1\n'):
            read(header + 'g/\n')

    def test_CP21_extensions(self):
        self.rejects('@use geo 1.0\ng/\n', 'not implemented')
        plain = 'g\n  .a 1\n  .b 2\n  h/\nend\n'
        with_ext = ('@use geo 1.0 optional\n@geo:grid 10\ng\n  .a 1\n  @geo:hint """\n    x\n  """\n'
                    '  .b 2\n  @geo:note "q"\n    "r"\n  h/\nend\n@geo:tail\n')
        self.assertEqual(sig(with_ext), sig(plain))
        self.rejects('@geo:grid 10\ng/\n', 'undeclared extension')

    def test_CP22_extension_directive_keeps_declaration_section_open(self):
        el = root('@use geo 1.0 optional\ng\n  .a 1\n  @geo:hint x\n  .b 2\nend\n')
        self.assertEqual(el.attrs, {'a': '1', 'b': '2'})

    def test_CP23_canonical_form_is_unique(self):
        variants = [
            'svg(xmlns=http://www.w3.org/2000/svg, width=10, d=x)\n  path(d=M0 0)/\nend\n',
            'svg\n  @ns http://www.w3.org/2000/svg\n  .d "x"\n  .width 10\n  path\n    .d M0 0\n  end\nend svg\n',
            '# comment\nsvg\n  @ns "http://www.w3.org/2000/svg"\n  @block path .d p\n  .width "1"\n    "0"\n'
            '  .d x\n  path\n    p """\n      M0 0\n    """\n  end\nend\n',
        ]
        outputs = {S.write_sml(read(v)) for v in variants}
        self.assertEqual(len(outputs), 1, outputs)

    def test_CP24_canonical_writer_is_idempotent(self):
        src = ('@indent 2\nsvg(xmlns=http://www.w3.org/2000/svg)\n  text\n    @noindent\n    tspan = A\n'
               '    = " B"\n  end\n  style = """"\n    a\n    """\n    end\n  """"\nend\n')
        once = S.write_sml(read(src))
        self.assertEqual(S.write_sml(read(once)), once)


class SmlSideFixtures(Base):
    def test_SV31_cr_in_comment_or_pi(self):
        self.rejects('g\n  @comment "a\\rb"\nend\n', 'CR')
        self.rejects('g\n  @pi t "a\\rb"\nend\n', 'CR')

    def test_SV33_no_default_namespace(self):
        el = root('body\n  @block div .style style\n  div\n    style """\n      a: 1;\n    """\n  end\nend\n')
        div = kids(el)[0]
        self.assertEqual(div.expanded, (None, 'div'))
        self.assertEqual(div.attr_expanded['style'], (None, 'style'))

    def test_SV34_default_namespace(self):
        el = root(f'svg\n  @ns {SVG}\n  @block path .d data\n  path\n    data """\n      M 0 0\n    """\n  end\nend\n')
        path = kids(el)[0]
        self.assertEqual(path.expanded, (SVG, 'path'))
        self.assertEqual(path.attr_expanded['d'], (None, 'd'))

    def test_SV35_prefixed_declaration_is_not_retargeted(self):
        src = (f'svg\n  @ns {SVG}\n  @ns s {SVG}\n  @block s:path .d data\n  g\n    @ns s urn:other\n'
               '    path\n      data """\n        M 0 0\n      """\n    end\n  end\nend\n')
        self.assertEqual(kids(kids(root(src))[0])[0].attrs['d'], 'M 0 0')
        bad = src.replace('    path\n', '    s:path\n')
        self.rejects(bad, 'no active alias')

    def test_SV36_unbound_or_late_prefix(self):
        self.rejects('g\n  @block s:path .d data\nend\n', 'not bound')
        self.rejects('g\n  @block path .d data\n  @ns s urn:x\nend\n', '@ns must come')

    def test_SV37_alias_versus_real_child(self):
        el = root('g\n  @block g .d data\n  data """\n    v\n  """\n  data\n    = child\n  end\nend\n')
        self.assertEqual(el.attrs['d'], 'v')
        self.assertEqual(kids(el)[0].qname, 'data')

    def test_SV38_named_block_after_attribute(self):
        el = root('g\n  @block g .d data\n  .a 1\n  data """\n    v\n  """\n  .b 2\nend\n')
        self.assertEqual(el.attrs, {'a': '1', 'd': 'v', 'b': '2'})

    def test_SV39_named_block_and_attribute(self):
        self.rejects('g\n  @block g .d data\n  .d x\n  data """\n    v\n  """\nend\n', 'duplicate')

    def test_SV40_two_aliases_same_target(self):
        base = 'g\n  @block g .d p\n  @block g .d q\n  p """\n    v\n  """\n'
        root(base + 'end\n')
        self.rejects(base + '  q """\n    w\n  """\nend\n', 'duplicate')

    def test_SV41_late_block_declaration(self):
        self.rejects('g\n  h/\n  @block g .y x\nend\n', 'after the element')

    def test_SV42_alias_named_end(self):
        self.rejects('g\n  @block g .y end\nend\n', 'invalid alias')

    def test_SV43_shadowing(self):
        src = ('g\n  @block h .a v\n  h\n    v """\n      1\n    """\n  end\n  k\n    @block h .b v\n'
               '    h\n      v """\n        2\n      """\n    end\n  end\nend\n')
        el = root(src)
        self.assertEqual(kids(el)[0].attrs, {'a': '1'})
        self.assertEqual(kids(kids(el)[1])[0].attrs, {'b': '2'})
        self.rejects('g\n  @block h .a v\n  @block h .b v\nend\n', 'declared twice')

    def test_SV44_quote_line_lengthens_the_marker(self):
        self.assertEqual(S.block_tag('a\nend\nb'), '"""')
        self.assertEqual(S.block_tag('"""\nend'), '""""')
        self.assertEqual(S.block_tag('a\n  """"\n"""'), '"""""')
        el = root('g\n  @block g .d data\n  data """"\n    """\n  """"\nend\n')
        self.assertEqual(el.attrs['d'], '"""')

    def test_SV45_unrepresentable_blocks(self):
        self.assertIsNone(S.block_tag(' a\n b'))
        self.assertIsNone(S.block_tag('a \nb'))
        self.assertEqual(S.block_tag('a\n'), '"""')
        self.assertEqual(S.block_tag('\na'), '"""')

    def test_SV46_malformed_block_declarations(self):
        for decl in ('@block g data .d', '@block g .d', '@block g .d data x'):
            self.rejects(f'g\n  {decl}\nend\n', '@block takes: element-qname .attribute-qname alias')

    def test_SV47_alias_after_content(self):
        self.rejects('g\n  @block g .s style\n  h/\n  style """\n    x\n  """\nend\n', 'after the element')
        el = root('g\n  @block g .s style\n  h/\n  style\n  end\nend\n')
        self.assertEqual([k.qname for k in kids(el)], ['h', 'style'])

    def test_SV48_two_default_namespace_scopes(self):
        src = ('g(xmlns=urn:a)\n  @block item .va v\n  k(xmlns=urn:b)\n    @block item .vb v\n    item\n'
               '      v """\n        b\n      """\n    end\n  end\n  item\n    v """\n      a\n    """\n  end\nend\n')
        el = root(src)
        self.assertEqual(kids(kids(el)[0])[0].attrs, {'vb': 'b'})
        self.assertEqual(kids(el)[1].attrs, {'va': 'a'})

    def test_SV50_root_alias_without_namespace(self):
        self.assertEqual(self.attr('g\n  @block g .y x\n  x """\n    v\n  """\nend\n', 'y'), 'v')

    def test_SV51_bare_alias_name_is_an_element(self):
        el = root('g\n  @block g .d data\n  data\n  end\nend\n')
        self.assertEqual(kids(el)[0].qname, 'data')
        self.rejects('g\n  @block g .d data\n  data/\n  .a 1\nend\n', 'after the element')

    def test_SV52_alias_without_declaration(self):
        self.rejects('g\n  data """\n    v\n  """\nend\n', 'no active alias')

    def test_SV53_trimmed_lines(self):
        el = root(f'g  \n  @ns {SVG}   \n  .x   value   \n  .d """  \n    v\n  """\nend\n')
        self.assertEqual(el.ns_decls[None], SVG)
        self.assertEqual(el.attrs, {'x': 'value', 'd': 'v'})

    def test_SV54_outer_whitespace_is_quoted(self):
        doc = read('g\n  .a " x "\n  = " t "\n  @comment " c "\n  @pi p "d "\nend\n')
        out = S.write_sml(doc)
        for fragment in ('.a " x "', '= " t "', '@comment " c "', '@pi p "d "'):
            self.assertIn(fragment, out)
        self.assertEqual(S.signature(read(out)), S.signature(doc))

    def test_SV55_trailing_spaces_after_quote(self):
        self.assertEqual(self.attr('g\n  .a "x"   \nend\n', 'a'), 'x')

    def test_SV56_block_trailing_whitespace(self):
        self.assertEqual(self.attr('g\n  .a """\n    x  \n    y\t\n  """\nend\n', 'a'), 'x\ny')
        self.assertIsNone(S.block_tag('x \ny'))

    def test_SV58_pi_leading_whitespace(self):
        for data in ('" x"', '"\\tx"', '"\\nx"', '"\\rx"'):
            self.rejects(f'@pi t {data}\ng/\n', 'PI data')

    def test_SV64_indent_errors(self):
        self.rejects('@indent 2\ng\n  h/\n  = x\nend\n', 'laid-out')
        self.rejects('@indent 2\ng\n  h/\n  @noindent\nend\n', 'after the element')
        self.rejects('@indent 2\n@indent 2\ng/\n', 'only once')
        self.rejects('@indent 17\ng/\n')
        self.rejects('@indent 01\ng/\n')

    def test_SV70_doctype_identifiers(self):
        self.rejects('@doctype svg PUBLIC " -//W3C//DTD SVG 1.1//EN" "x"\nsvg/\n', 'not normalized')
        self.rejects('@doctype svg PUBLIC "-//W3C//DTD  SVG" "x"\nsvg/\n', 'not normalized')
        self.rejects('@doctype svg SYSTEM "a\\"b\'c"\nsvg/\n', 'both quote')
        self.rejects('@doctype svg SYSTEM "a\\rb"\nsvg/\n', 'CR')

    def test_SV71_marker_without_space(self):
        spaced = ('g\n  .d """\n    v\n  """\n  @comment """\n    c\n  """\n  = """\n    t\n  """\n'
                  '  h = """\n    i\n  """\nend\n')
        unspaced = spaced.replace(' """', '"""')
        self.assertEqual(sig(unspaced), sig(spaced))
        self.assertIn('.d """', S.write_sml(read('g\n  .d"""\n    v\n    w\n  """\nend\n')))

    def test_SV72_bar_at_value_start(self):
        el = root('g(a=|x, b=|)\n  .c |x y\n  @comment |c\n  @pi t |p\n  = |\nend\n')
        self.assertEqual((el.attrs['a'], el.attrs['b'], el.attrs['c']), ('|x', '|', '|x y'))
        self.assertEqual([(type(n).__name__, getattr(n, 'data', None)) for n in el.children],
                         [('Comment', '|c'), ('PI', '|p'), ('Text', '|')])
        self.assertEqual(S.write_sml(read('g(a=|x)\n  = |\nend\n')), '@sml 1.0-SVG-1\ng(a=|x) = |\n')
        self.rejects('g\n  .d|x\nend\n', 'expected a space')
        self.rejects('g\n  x |\nend\n', "unexpected ' |' after element name")

    def test_SV77_comment_runs(self):
        el = root('g\n  @comments """\n    a\n    # b\n    end\n  """\n  h/\nend\n')
        self.assertEqual([c.data for c in kids(el, S.Comment)], ['a', '# b', 'end'])
        laid = root('@indent 2\ng\n  @comments """\n    a\n    b\n  """\nend\n')
        self.assertEqual([c.data if isinstance(c, S.Comment) else c.data for c in laid.children], ['\n  ', 'a', '\n  ', 'b', '\n'])
        self.assertEqual([c.data for c in read('@comments """\n  p\n  q\n"""\ng/\n').prolog], ['p', 'q'])
        self.rejects('g\n  @comments """\n    a\n\n    b\n  """\nend\n', 'blank lines')
        self.rejects('g\n  @comments """\n  """\nend\n', 'blank lines')
        self.rejects('g\n  @comments a\nend\n', 'takes a block')
        self.rejects('g\n  @comments """\n    a--b\n  """\nend\n', "'--'")
        self.rejects('g\n  @comments """\n    x\n  """\n  .a 1\nend\n', 'after the element')
        doc = read('g\n  @comment a\n  @comment " b"\n  @comment c\n  @comment d\n  @comment "\\"\\"\\""\nend\n')
        self.assertEqual([c.data for c in kids(doc.root, S.Comment)], ['a', ' b', 'c', 'd', '"""'])
        out = S.write_sml(doc)
        self.assertIn('  @comment a\n  @comment " b"\n  @comments """"\n    c\n    d\n    """\n  """"\n', out)
        self.assertEqual(S.signature(read(out)), S.signature(doc))

    def test_SV78_laid_out_comment_blocks(self):
        src = '@indent 2\n@comment """\n  top\n"""\ng\n  @comment """\n    a\n\n    b\n  """\n  h\n    @comment """\n      deep\n    """\n  end\nend\n'
        doc = read(src)
        self.assertEqual(doc.prolog[0].data, '\n  top\n')
        g = doc.root
        self.assertEqual(kids(g, S.Comment)[0].data, '\n    a\n\n    b\n  ')
        self.assertEqual(kids(kids(g)[0], S.Comment)[0].data, '\n      deep\n    ')
        self.assertEqual(S.write_sml(doc), src.replace('@indent 2\n', '@sml 1.0-SVG-1\n@indent 2\n'))
        # Without @indent, in @noindent elements, and in mixed content, blocks are literal.
        self.assertEqual(kids(root('g\n  @comment """\n    a\n  """\nend\n'), S.Comment)[0].data, 'a')
        el = root('@indent 2\ng\n  @noindent\n  @comment """\n    a\n  """\nend\n')
        self.assertEqual(kids(el, S.Comment)[0].data, 'a')
        el = root('@indent 2\ng\n  = x\n  @comment """\n    a\n  """\nend\n')
        self.assertEqual(kids(el, S.Comment)[0].data, 'a')
        self.assertEqual(kids(root('@indent 2\ng\n  @comment """\n  """\nend\n'), S.Comment)[0].data, '\n  ')
        # Data without that layout is quoted, never a literal block, in a laid-out position.
        for data in ('\n  flush\n  ', 'a\nb', '\n    a  \n  ', '\n      a\n      b\n  '):
            doc = S.read_xml(f'<g>\n  <!--{data}-->\n</g>'.encode())
            out = S.write_sml(doc)
            self.assertIn('@comment "', out)
            self.assertEqual(S.signature(read(out)), S.signature(doc))

    def test_SV79_indent_0(self):
        el = root('@indent 0\ng\n  h/\n  k\n    m/\n  end\nend\n')
        self.assertEqual([c.data for c in el.children if isinstance(c, S.Text)], ['\n', '\n', '\n'])
        self.assertEqual([c.data for c in kids(el)[1].children if isinstance(c, S.Text)], ['\n', '\n'])
        doc = S.read_xml(b'<g>\n<h/>\n<k>\n<m/>\n</k>\n<!--\nline\n-->\n</g>')
        out = S.write_sml(doc)
        self.assertEqual(out, '@sml 1.0-SVG-1\n@indent 0\ng\n  h/\n  k\n    m/\n  end\n  @comment """\n    line\n  """\nend\n')
        self.assertEqual(S.signature(read(out)), S.signature(doc))

    def test_SV80_line_breaks_are_quoted(self):
        self.assertIsNone(S.block_tag('\n'))
        self.assertIsNone(S.block_tag('\n\n'))
        self.assertEqual(S.block_tag('a\n'), '"""')
        doc = S.read_xml(b'<g a="&#10;"><t>\n</t><h/>\n\n<h/><!--\n--></g>')
        out = S.write_sml(doc)
        for fragment in ('.a "\\n"', 't = "\\n"', '= "\\n\\n"', '@comment "\\n"'):
            self.assertIn(fragment, out)
        self.assertNotIn('"""', out)
        self.assertEqual(S.signature(read(out)), S.signature(doc))
        self.assertEqual(kids(root('g\n  = """\n\n\n  """\nend\n'), S.Text)[0].data, '\n')   # still readable
        laid = S.read_xml(b'<g>\n  <!--\n  -->\n</g>')
        self.assertIn('@comment "\\n  "', S.write_sml(laid))

    def test_SV81_local_indent(self):
        src = '@indent 2\ng\n  h\n    @indent 4\n    a/\n    b\n      c/\n    end\n  end\n  k\n    @indent tab\n    m/\n  end\nend\n'
        doc = read(src)
        h = kids(doc.root)[0]
        self.assertEqual([t.data for t in h.children if isinstance(t, S.Text)], ['\n      ', '\n      ', '\n  '])
        b = kids(h)[1]
        self.assertEqual([t.data for t in b.children if isinstance(t, S.Text)], ['\n        ', '\n      '])
        k = kids(doc.root)[1]
        self.assertEqual([t.data for t in k.children if isinstance(t, S.Text)], ['\n  \t', '\n  '])
        self.assertEqual(S.write_sml(doc), '@sml 1.0-SVG-1\n' + src)
        self.rejects('g\n  @indent 4\n  h/\nend\n', 'needs the @indent header')
        self.rejects('@indent 2\ng\n  @indent 4\n  @indent 2\nend\n', 'only once')
        self.rejects('@indent 2\ng\n  h/\n  @indent 4\nend\n', 'after the element')
        self.rejects('@indent 2\ng\n  @indent 3x\nend\n', 'width from 0 to 16')
        self.rejects('@indent 2\ng/\n@indent 4\n', 'only allowed before the root')

    def test_SV82_laid_out_text(self):
        src = '@indent 2\ng\n  p = """\n    one\n\n    two\n  """\n  q\n    @indent 4\n    r = """\n      deep\n    """\n  end\nend\n'
        doc = read(src)
        p, q = kids(doc.root)
        self.assertEqual(p.children[0].data, '\n    one\n\n    two\n  ')
        self.assertEqual(kids(q)[0].children[0].data, '\n        deep\n      ')
        self.assertEqual(S.write_sml(doc), '@sml 1.0-SVG-1\n' + src)
        # The normal form, @noindent parents, mixed content, and documents without @indent stay literal.
        literal = ('@indent 2\ng\n  p\n    = """\n      a\n      b\n    """\n  end\nend\n',
                   '@indent 2\ng\n  @noindent\n  p = """\n    a\n    b\n  """\nend\n',
                   '@indent 2\ng\n  = x\n  p = """\n    a\n    b\n  """\nend\n',
                   'g\n  p = """\n    a\n    b\n  """\nend\n')
        for text in literal:
            self.assertEqual([c for c in kids(read(text).root)][0].children[0].data, 'a\nb')
        # In a laid-out position, text that needs a literal block uses the normal form; other text is quoted.
        doc = S.read_xml(b'<g>\n  <p>a\nb</p>\n  <p>\n  flush\n  </p>\n  <p>\n    x\n  </p>\n</g>')
        out = S.write_sml(doc)
        self.assertIn('  p\n    = """\n      a\n      b\n    """\n  end\n', out)
        self.assertIn('  p = "\\n  flush\\n  "\n', out)
        self.assertIn('  p = """\n    x\n  """\n', out)
        self.assertEqual(S.signature(read(out)), S.signature(doc))

    def test_SV83_node_aliases(self):
        src = ('@node g group\n@node p para\nsvg(xmlns=http://www.w3.org/2000/svg)\n  group(id=a)\n    rect/\n  end group\n'
               '  group/\n  g = plain\n  foreignObject\n    @node div box\n    box(xmlns=http://www.w3.org/1999/xhtml)\n'
               '      para = Hello\n      @element para\n      end para\n    end div\n  end\n  box/\nend\n')
        doc = read(src)
        xml = ('<svg xmlns="http://www.w3.org/2000/svg"><g id="a"><rect/></g><g/><g>plain</g><foreignObject>'
               '<div xmlns="http://www.w3.org/1999/xhtml"><p>Hello</p><para/></div></foreignObject><box/></svg>')
        self.assertIsNone(S.first_difference(S.signature(doc), S.signature(S.read_xml(xml.encode()))))
        self.assertNotIn('@node', S.write_sml(doc))
        # The alias stands for the name as written: its prefix is resolved where the element is opened.
        el = read('@node d:info info\nsvg\n  info(xmlns:d=urn:d)/\nend\n').root.children[0]
        self.assertEqual((el.qname, el.expanded), ('d:info', ('urn:d', 'info')))
        # Shadowing in a nested scope; aliases do not chain.
        self.assertEqual([e.qname for e in kids(read('@node g x\nsvg\n  @node p x\n  x/\n  h\n    x/\n  end\nend\n').root)],
                         ['p', 'h'])
        self.assertEqual([e.qname for e in kids(read('svg\n  @node g x\n  @node x y\n  y/\nend\n').root)], ['x'])
        self.rejects('@node g\nsvg/\n', 'takes')
        self.rejects('@node g end\nsvg/\n', 'invalid element alias')
        self.rejects('@node g a:b\nsvg/\n', 'invalid element alias')
        self.rejects('@node 1x y\nsvg/\n', 'invalid element name')
        self.rejects('@node g x\n@node p x\nsvg/\n', 'twice')
        self.rejects('svg\n  @node g x\n  @node p x\nend\n', 'twice')
        self.rejects('svg/\n@node g x\n', 'only allowed before the root')
        self.rejects('svg\n  a/\n  @node g x\nend\n', 'after the element')
        self.rejects('@node g group\ngroup\nend p\n', 'does not match')

    def test_SV76_reverse_round_trip(self):
        src = ('@sml 1.0-SVG-1\n@indent 2\n@comment " authored in SML "\n'
               '@doctype svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd"\n'
               f'svg(xmlns={SVG}, xmlns:xlink=http://www.w3.org/1999/xlink)\n'
               '  @block path .d data\n'
               '  defs\n    linearGradient(id=g)\n      stop(offset=0)/\n    end\n  end\n'
               '  path(fill=url(#g), transform=translate(10,20))\n    data """\n      M 0 0\n      L 10 10\n    """\n  end\n'
               '  image\n    .xlink:href "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAAB"\n'
               '      "CAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="\n  end\n'
               '  p:meta(xmlns:p=urn:vendor, p:k=v)/\n'
               '  desc = """\n    if fill is none then\n      use stroke\n    end\n  """\n'
               '  text\n    @noindent\n    tspan = A\n    = " B"\n  end\nend svg\n')
        d1 = read(src)
        d2 = S.read_xml(S.write_xml(d1))
        self.assertIsNone(S.first_difference(S.signature(d1), S.signature(d2)))
        d3 = read(S.write_sml(d2))
        self.assertIsNone(S.first_difference(S.signature(d1), S.signature(d3)))
        d4 = S.read_xml(S.write_xml(d3))
        self.assertIsNone(S.first_difference(S.signature(d1), S.signature(d4)))


if __name__ == '__main__':
    unittest.main()
