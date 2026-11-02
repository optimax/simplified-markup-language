#!/usr/bin/env python3
"""Core parser and writer tests for the SML 1.0-SVG-3 prototype.

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
        lf = 'g\n  .a 1\n  = |\n    x\n    y\n  end\nend\n'
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

    def test_CP08_block_end_with_name_is_payload(self):
        self.assertEqual(self.attr('g\n  .a |\n    end path\n  end\nend\n', 'a'), 'end path')

    def test_CP09_custom_terminators(self):
        self.assertEqual(self.attr('g\n  .a |EOT\n    x\n    end\n  EOT\nend\n', 'a'), 'x\nend')
        self.rejects('g\n  .a |end\n    x\n  end\nend\n')
        self.rejects('g\n  .a |1x\n    x\n  1x\nend\n')

    def test_CP10_raw_values_cannot_begin_with_bar_or_quote(self):
        self.rejects('g(a=|x)/\n')
        self.rejects('@pi t |x\ng/\n')
        self.rejects('g\n  @ns |x\nend\n')
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
        src = 'g\n  .a |\n    a\n\n      b\n   \t  \n    c\n  end\nend\n'
        self.assertEqual(self.attr(src, 'a'), 'a\n\n  b\n\nc')
        self.assertEqual(self.attr('g\n  .a |\n\t a\n\t b\n  end\nend\n', 'a'), 'a\nb')
        self.assertEqual(self.attr('g\n  .a |\n\ta\n  b\n  end\nend\n', 'a'), '\ta\n  b')
        self.assertEqual(self.attr('g\n  .a |\n  end\nend\n', 'a'), '')

    def test_CP15_alias_inside_explicit_element(self):
        el = root('g\n  @block path data .d\n  @element path\n    data |\n      M 0 0\n    end\n  end\nend\n')
        self.assertEqual(kids(el)[0].attrs['d'], 'M 0 0')

    def test_CP16_ns_must_come_first(self):
        for stmt in ('.a 1', '@block g x .y', '@noindent'):
            self.rejects(f'g\n  {stmt}\n  @ns urn:x\nend\n', '@ns must come')
        self.rejects('g\n  @block g x .y\n  x |\n    v\n  end\n  @ns urn:x\nend\n', '@ns must come')

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
        self.rejects('@comment x\n@sml 1.0-SVG-3\ng/\n', 'first statement')
        self.rejects('@sml 1.0-SVG-2\ng/\n', 'unsupported draft')
        for header in ('@sml 1.0-SVG-3\n', '@sml 1.0\n', '', '# note\n@sml 1.0-SVG-3\n'):
            read(header + 'g/\n')

    def test_CP21_extensions(self):
        self.rejects('@use geo 1.0\ng/\n', 'not implemented')
        plain = 'g\n  .a 1\n  .b 2\n  h/\nend\n'
        with_ext = ('@use geo 1.0 optional\n@geo:grid 10\ng\n  .a 1\n  @geo:hint |\n    x\n  end\n'
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
            '# comment\nsvg\n  @ns "http://www.w3.org/2000/svg"\n  @block path p .d\n  .width "1"\n    "0"\n'
            '  .d x\n  path\n    p |\n      M0 0\n    end\n  end\nend\n',
        ]
        outputs = {S.write_sml(read(v)) for v in variants}
        self.assertEqual(len(outputs), 1, outputs)

    def test_CP24_canonical_writer_is_idempotent(self):
        src = ('@indent 2\nsvg(xmlns=http://www.w3.org/2000/svg)\n  text\n    @noindent\n    tspan = A\n'
               '    = " B"\n  end\n  style = |EOT\n    a\n    end\n  EOT\nend\n')
        once = S.write_sml(read(src))
        self.assertEqual(S.write_sml(read(once)), once)


class SmlSideFixtures(Base):
    def test_SV31_cr_in_comment_or_pi(self):
        self.rejects('g\n  @comment "a\\rb"\nend\n', 'CR')
        self.rejects('g\n  @pi t "a\\rb"\nend\n', 'CR')

    def test_SV33_no_default_namespace(self):
        el = root('body\n  @block div style .style\n  div\n    style |\n      a: 1;\n    end\n  end\nend\n')
        div = kids(el)[0]
        self.assertEqual(div.expanded, (None, 'div'))
        self.assertEqual(div.attr_expanded['style'], (None, 'style'))

    def test_SV34_default_namespace(self):
        el = root(f'svg\n  @ns {SVG}\n  @block path data .d\n  path\n    data |\n      M 0 0\n    end\n  end\nend\n')
        path = kids(el)[0]
        self.assertEqual(path.expanded, (SVG, 'path'))
        self.assertEqual(path.attr_expanded['d'], (None, 'd'))

    def test_SV35_prefixed_declaration_is_not_retargeted(self):
        src = (f'svg\n  @ns {SVG}\n  @ns s {SVG}\n  @block s:path data .d\n  g\n    @ns s urn:other\n'
               '    path\n      data |\n        M 0 0\n      end\n    end\n  end\nend\n')
        self.assertEqual(kids(kids(root(src))[0])[0].attrs['d'], 'M 0 0')
        bad = src.replace('    path\n', '    s:path\n')
        self.rejects(bad, 'no active alias')

    def test_SV36_unbound_or_late_prefix(self):
        self.rejects('g\n  @block s:path data .d\nend\n', 'not bound')
        self.rejects('g\n  @block path data .d\n  @ns s urn:x\nend\n', '@ns must come')

    def test_SV37_alias_versus_real_child(self):
        el = root('g\n  @block g data .d\n  data |\n    v\n  end\n  data\n    = child\n  end\nend\n')
        self.assertEqual(el.attrs['d'], 'v')
        self.assertEqual(kids(el)[0].qname, 'data')

    def test_SV38_named_block_after_attribute(self):
        el = root('g\n  @block g data .d\n  .a 1\n  data |\n    v\n  end\n  .b 2\nend\n')
        self.assertEqual(el.attrs, {'a': '1', 'd': 'v', 'b': '2'})

    def test_SV39_named_block_and_attribute(self):
        self.rejects('g\n  @block g data .d\n  .d x\n  data |\n    v\n  end\nend\n', 'duplicate')

    def test_SV40_two_aliases_same_target(self):
        base = 'g\n  @block g p .d\n  @block g q .d\n  p |\n    v\n  end\n'
        root(base + 'end\n')
        self.rejects(base + '  q |\n    w\n  end\nend\n', 'duplicate')

    def test_SV41_late_block_declaration(self):
        self.rejects('g\n  h/\n  @block g x .y\nend\n', 'after the element')

    def test_SV42_alias_named_end(self):
        self.rejects('g\n  @block g end .y\nend\n', 'invalid alias')

    def test_SV43_shadowing(self):
        src = ('g\n  @block h v .a\n  h\n    v |\n      1\n    end\n  end\n  k\n    @block h v .b\n'
               '    h\n      v |\n        2\n      end\n    end\n  end\nend\n')
        el = root(src)
        self.assertEqual(kids(el)[0].attrs, {'a': '1'})
        self.assertEqual(kids(kids(el)[1])[0].attrs, {'b': '2'})
        self.rejects('g\n  @block h v .a\n  @block h v .b\nend\n', 'declared twice')

    def test_SV44_end_line_uses_custom_terminator(self):
        self.assertEqual(S.block_tag('a\nend\nb'), 'EOT')
        self.assertEqual(S.block_tag('EOT\nend'), 'EOT1')
        el = root('g\n  @block g data .d\n  data |EOT\n    end\n  EOT\nend\n')
        self.assertEqual(el.attrs['d'], 'end')

    def test_SV45_unrepresentable_blocks(self):
        self.assertIsNone(S.block_tag(' a\n b'))
        self.assertIsNone(S.block_tag('a \nb'))
        self.assertEqual(S.block_tag('a\n'), 'end')
        self.assertEqual(S.block_tag('\na'), 'end')

    def test_SV46_legacy_separator(self):
        self.rejects('g\n  @block g data = .d\nend\n', 'no longer valid')

    def test_SV47_alias_after_content(self):
        self.rejects('g\n  @block g style .s\n  h/\n  style |\n    x\n  end\nend\n', 'after the element')
        el = root('g\n  @block g style .s\n  h/\n  style\n  end\nend\n')
        self.assertEqual([k.qname for k in kids(el)], ['h', 'style'])

    def test_SV48_two_default_namespace_scopes(self):
        src = ('g(xmlns=urn:a)\n  @block item v .va\n  k(xmlns=urn:b)\n    @block item v .vb\n    item\n'
               '      v |\n        b\n      end\n    end\n  end\n  item\n    v |\n      a\n    end\n  end\nend\n')
        el = root(src)
        self.assertEqual(kids(kids(el)[0])[0].attrs, {'vb': 'b'})
        self.assertEqual(kids(el)[1].attrs, {'va': 'a'})

    def test_SV50_root_alias_without_namespace(self):
        self.assertEqual(self.attr('g\n  @block g x .y\n  x |\n    v\n  end\nend\n', 'y'), 'v')

    def test_SV51_bare_alias_name_is_an_element(self):
        el = root('g\n  @block g data .d\n  data\n  end\nend\n')
        self.assertEqual(kids(el)[0].qname, 'data')
        self.rejects('g\n  @block g data .d\n  data/\n  .a 1\nend\n', 'after the element')

    def test_SV52_alias_without_declaration(self):
        self.rejects('g\n  data |\n    v\n  end\nend\n', 'no active alias')

    def test_SV53_trimmed_lines(self):
        el = root(f'g  \n  @ns {SVG}   \n  .x   value   \n  .d |  \n    v\n  end\nend\n')
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
        self.assertEqual(self.attr('g\n  .a |\n    x  \n    y\t\n  end\nend\n', 'a'), 'x\ny')
        self.assertIsNone(S.block_tag('x \ny'))

    def test_SV58_pi_leading_whitespace(self):
        for data in ('" x"', '"\\tx"', '"\\nx"', '"\\rx"'):
            self.rejects(f'@pi t {data}\ng/\n', 'PI data')

    def test_SV64_indent_errors(self):
        self.rejects('@indent 2\ng\n  h/\n  = x\nend\n', 'laid-out')
        self.rejects('@indent 2\ng\n  h/\n  @noindent\nend\n', 'after the element')
        self.rejects('@indent 2\n@indent 2\ng/\n', 'only once')
        self.rejects('@indent 0\ng/\n')
        self.rejects('@indent 17\ng/\n')

    def test_SV70_doctype_identifiers(self):
        self.rejects('@doctype svg PUBLIC " -//W3C//DTD SVG 1.1//EN" "x"\nsvg/\n', 'not normalized')
        self.rejects('@doctype svg PUBLIC "-//W3C//DTD  SVG" "x"\nsvg/\n', 'not normalized')
        self.rejects('@doctype svg SYSTEM "a\\"b\'c"\nsvg/\n', 'both quote')
        self.rejects('@doctype svg SYSTEM "a\\rb"\nsvg/\n', 'CR')

    def test_SV71_marker_without_space(self):
        spaced = 'g\n  .d |\n    v\n  end\n  @comment |\n    c\n  end\n  = |\n    t\n  end\n  h = |\n    i\n  end\nend\n'
        unspaced = spaced.replace(' |', '|')
        self.assertEqual(sig(unspaced), sig(spaced))
        self.assertIn('.d |', S.write_sml(read('g\n  .d|\n    v\n    w\n  end\nend\n')))

    def test_SV72_custom_terminator_markers(self):
        for marker in ('|EOT', ' |EOT'):
            self.assertEqual(self.attr(f'g\n  .d{marker}\n    v\n  EOT\nend\n', 'd'), 'v')
        self.rejects('g\n  .d |x y\nend\n', 'invalid block marker')

    def test_SV76_reverse_round_trip(self):
        src = ('@sml 1.0-SVG-3\n@indent 2\n@comment " authored in SML "\n'
               '@doctype svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd"\n'
               f'svg(xmlns={SVG}, xmlns:xlink=http://www.w3.org/1999/xlink)\n'
               '  @block path data .d\n'
               '  defs\n    linearGradient(id=g)\n      stop(offset=0)/\n    end\n  end\n'
               '  path(fill=url(#g), transform=translate(10,20))\n    data |\n      M 0 0\n      L 10 10\n    end\n  end\n'
               '  image\n    .xlink:href "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAAB"\n'
               '      "CAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="\n  end\n'
               '  p:meta(xmlns:p=urn:vendor, p:k=v)/\n'
               '  desc = |EOT\n    if fill is none then\n      use stroke\n    end\n  EOT\n'
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
