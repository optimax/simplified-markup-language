#!/usr/bin/env python3
"""SVG round-trip test harness and intentionally narrow SML interchange prototype.
Only canonical, fully quoted expanded SML is supported; NOT an SML 1.0 conformance parser.
"""
from lxml import etree
from pathlib import Path
import json, re, sys, traceback, datetime
BASE=Path(__file__).resolve().parent
CORPUS=BASE/'corpus'; OUT=BASE/'results'; CORPUS.mkdir(exist_ok=True); OUT.mkdir(exist_ok=True)
SVG='http://www.w3.org/2000/svg'; XL='http://www.w3.org/1999/xlink'; INK='http://www.inkscape.org/namespaces/inkscape'; DC='http://purl.org/dc/elements/1.1/'
fixtures={
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
'18-whitespace-control.svg':'''<svg xmlns="http://www.w3.org/2000/svg"><text xml:space="preserve">&#xA;&#x9; X &#xD;Y </text><rect data="A&#xA;B&#x9;C&#xD;D"/></svg>'''
}
for name,src in fixtures.items(): (CORPUS/name).write_text(src,encoding='utf-8',newline='')

def parse_xml(raw):
    parser=etree.XMLParser(resolve_entities=False,load_dtd=False,no_network=True,remove_blank_text=False,strip_cdata=False,remove_comments=False,remove_pis=False)
    return etree.fromstring(raw.encode('utf-8'),parser)

def sig_node(node):
    if isinstance(node,etree._Comment): return ['comment',node.text or '']
    if isinstance(node,etree._ProcessingInstruction): return ['pi',node.target,node.text or '']
    attrs=sorted((str(k),v) for k,v in node.attrib.items())
    children=[]
    if node.text is not None and node.text!='': children.append(['text',node.text])
    for ch in node:
        children.append(sig_node(ch))
        if ch.tail is not None and ch.tail!='':children.append(['text',ch.tail])
    return ['element',str(node.tag),attrs,children]

def quoted(s): return json.dumps(s,ensure_ascii=False)
def encode_qname(expanded,nsmap,attr=False):
    if expanded.startswith('{http://www.w3.org/XML/1998/namespace}'):
        return 'xml:'+expanded.split('}',1)[1]
    if not expanded.startswith('{'):return expanded
    uri,local=expanded[1:].split('}',1)
    matches=[p for p,u in nsmap.items() if u==uri and (p is not None or not attr)]
    if not matches: raise ValueError('QName mapping missing '+expanded)
    prefix=next((x for x in matches if x is not None),None) if attr else (None if None in matches else matches[0])
    if attr and prefix is None: raise ValueError('No attribute prefix '+expanded)
    return (prefix+':' if prefix else '')+local

def to_sml(root):
    out=['@sml 1.0']
    def emit_node(node,depth,parent_ns=None):
        ind='  '*depth
        if isinstance(node,etree._Comment):out.append(ind+'@comment '+quoted(node.text or ''));return
        if isinstance(node,etree._ProcessingInstruction):out.append(ind+'@pi '+node.target+' '+quoted(node.text or ''));return
        nsmap=dict(node.nsmap)
        # preserve lexical qualified element prefix
        ename=(node.prefix+':'+etree.QName(node).localname) if node.prefix else etree.QName(node).localname
        out.append(ind+ename)
        for prefix,uri in nsmap.items():
            if parent_ns is None or parent_ns.get(prefix)!=uri:
                out.append(ind+'  @ns '+(prefix+' ' if prefix is not None else '')+quoted(uri))
        for key,val in node.attrib.items():out.append(ind+'  .'+encode_qname(key,nsmap,True)+' '+quoted(val))
        if node.text:out.append(ind+'  = '+quoted(node.text))
        for child in node:
            emit_node(child,depth+1,nsmap)
            if child.tail:out.append(ind+'  = '+quoted(child.tail))
        out.append(ind+'end')
    emit_node(root,0)
    return '\n'.join(out)+'\n'

def from_sml(source):
    stack=[]; root=None; prolog=[]
    # This prototype accepts ONLY the exact expanded subset generated by to_sml.
    lines=source.splitlines()
    i=0
    def resolve(qname,nsmap,attr=False):
        if ':' in qname:
            p,local=qname.split(':',1)
            if p=='xml': uri='http://www.w3.org/XML/1998/namespace'
            else:uri=nsmap[p]
            return '{'+uri+'}'+local
        return qname if attr else ('{'+nsmap[None]+'}'+qname if nsmap.get(None) else qname)
    def attach(parent,node):
        if parent is None:raise ValueError('Outside root')
        parent.append(node)
    while i<len(lines):
        line=lines[i].strip();i+=1
        if not line or line=='@sml 1.0':continue
        if line=='end':
            if not stack:raise ValueError('unmatched end')
            stack.pop();continue
        if line.startswith('@ns ') or line.startswith('.'):
            if not stack:raise ValueError('orphan declaration')
            frame=stack[-1]
            if frame['body']:raise ValueError('declaration after content')
            frame['decl'].append(line);continue
        if stack and not stack[-1]['finalized']:
            finalize(stack[-1])
        if line.startswith('= '):
            if not stack:raise ValueError('orphan text')
            val=json.loads(line[2:]); el=stack[-1]['node']
            if len(el):el[-1].tail=(el[-1].tail or '')+val
            else:el.text=(el.text or '')+val
            stack[-1]['body']=True;continue
        if line.startswith('@comment ') or line.startswith('@pi '):
            if line.startswith('@comment '):node=etree.Comment(json.loads(line[9:]))
            else:
                target,data=line[4:].split(' ',1);node=etree.ProcessingInstruction(target,json.loads(data))
            if stack: stack[-1]['node'].append(node);stack[-1]['body']=True
            else:prolog.append(node)
            continue
        # opening element; provisional no-namespace element until decl resolved
        fr={'name':line,'parent':stack[-1]['node'] if stack else None,'decl':[],'body':False,'finalized':False,'node':None,'nsmap':None}
        stack.append(fr)
        # finalization deferred to next actual content or close
        # so on `end`, finalize first (handled below by post-fix)
    if stack:raise ValueError('unclosed element')
    if root is None:raise ValueError('missing root')
    return root

# Actual parser with stack frames that finalize when first body content or closing marker is seen.
def parse_sml(source):
    stack=[]; root=None
    def resolve(q,ns,attr=False):
        if ':' in q:
            p,l=q.split(':',1); uri='http://www.w3.org/XML/1998/namespace' if p=='xml' else ns[p]
            return '{'+uri+'}'+l
        return q if attr or not ns.get(None) else '{'+ns[None]+'}'+q
    def fin(f):
        nonlocal root
        if f['node'] is not None:return
        ns=dict(f['parent_ns'])
        own={}
        for s in f['decl']:
            if s.startswith('@ns '):
                v=s[4:]; parts=v.split(' ',1)
                if len(parts)==1: own[None]=json.loads(parts[0])
                elif parts[0].startswith('"'):own[None]=json.loads(v)
                else:own[parts[0]]=json.loads(parts[1])
        ns.update(own)
        tag=resolve(f['name'],ns)
        elem=etree.Element(tag,nsmap=own or None)
        for s in f['decl']:
            if s.startswith('.'):
                k,v=s[1:].split(' ',1);elem.set(resolve(k,ns,True),json.loads(v))
        f['node']=elem;f['ns']=ns
        if f['parent'] is None:root=elem
        else:f['parent']['node'].append(elem)
    for raw in source.splitlines():
        s=raw.strip()
        if not s or s=='@sml 1.0':continue
        if s=='end':
            if not stack:raise ValueError('unmatched end')
            fin(stack[-1]);stack.pop();continue
        if s.startswith('@ns ') or s.startswith('.'):
            if not stack or stack[-1]['started']:raise ValueError('late declaration')
            stack[-1]['decl'].append(s);continue
        if stack:
            fin(stack[-1]);stack[-1]['started']=True
        if s.startswith('= '):
            if not stack:raise ValueError('orphan text')
            el=stack[-1]['node'];v=json.loads(s[2:])
            if len(el):el[-1].tail=(el[-1].tail or '')+v
            else:el.text=(el.text or '')+v
        elif s.startswith('@comment ') or s.startswith('@pi '):
            if s.startswith('@comment '):node=etree.Comment(json.loads(s[9:]))
            else:
                t,v=s[4:].split(' ',1);node=etree.ProcessingInstruction(t,json.loads(v))
            if stack:stack[-1]['node'].append(node)
            else:pass # top-level PI not currently included in prototype model
        else:
            stack.append({'name':s,'parent':stack[-1] if stack else None,'parent_ns':stack[-1]['ns'] if stack else {},'decl':[],'node':None,'started':False})
    if stack:raise ValueError('unclosed')
    if root is None:raise ValueError('missing root')
    return root

results=[]
for path in sorted(CORPUS.glob('*.svg')):
    result={'id':path.stem,'xml_baseline':'FAIL','sml_prototype':'FAIL','kdl_2_0':'NOT_RUN','notes':[]}
    try:
        x=parse_xml(path.read_text(encoding='utf8'))
        base=etree.fromstring(etree.tostring(x,encoding='utf-8'),etree.XMLParser(resolve_entities=False,load_dtd=False,no_network=True,remove_blank_text=False,remove_comments=False,remove_pis=False))
        assert sig_node(x)==sig_node(base),'XML semantic baseline mismatch'
        result['xml_baseline']='PASS'
        sml=to_sml(x)
        (OUT/(path.stem+'.sml')).write_text(sml,encoding='utf8')
        xx=parse_sml(sml)
        if sig_node(x)!=sig_node(xx):
            result['notes'].append('SML semantic mismatch')
        else:
            xml2=etree.tostring(xx,encoding='utf-8')
            assert sig_node(x)==sig_node(etree.fromstring(xml2,etree.XMLParser(remove_blank_text=False,remove_comments=False,remove_pis=False)))
            result['sml_prototype']='PASS'
    except Exception as e:result['notes'].append(type(e).__name__+': '+str(e))
    results.append(result)
report={'profile':'SVG-RTCP 1.0 draft','date':datetime.date.today().isoformat(),'method':'lxml XML baseline + narrow generated-SML subset prototype; no KDL parser','caveats':['SML prototype is NOT an SML 1.0 general parser','Top-level XML processing instructions/comments not checked by subtree comparison','XML source lexical differences not compared','No visual rendering comparison','No KDL translation run'], 'results':results}
(OUT/'results.json').write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf8')
print('XML baseline:',sum(x['xml_baseline']=='PASS' for x in results),'/',len(results))
print('SML prototype:',sum(x['sml_prototype']=='PASS' for x in results),'/',len(results))
for x in results:
 if x['sml_prototype']!='PASS':print('FAIL',x['id'],x['notes'])
