@sml 1.0-SVG-1
svg(xmlns=http://www.w3.org/2000/svg)
  style = """
    .a { fill: #f00; }
    #b:hover { opacity: .5; }
    """
  rect
    .class a
    .height 30
    .id b
    .style stroke: rgb(0, 0, 0); stroke-width: 1.00 !important
    .width 30
  end
end
