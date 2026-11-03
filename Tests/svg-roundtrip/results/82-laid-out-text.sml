@sml 1.0-SVG-1
svg(xmlns=http://www.w3.org/2000/svg)
  desc = """
    Pretty-printed text,
    one line per line.
    """
  style(type=text/css) = """
    .a { fill: red; }
    .b { fill: blue; }
    """
  title = Short
  desc
    = """
      Starts at once
          and goes on.
      """
  end
  desc(x=1, y=2)
    = """

        flush text keeps its indentation
        
      """
  end
  info(xmlns=urn:x)
    @indent 4
    p = """
      Inside a subtree with its own step
      """
  end
end
