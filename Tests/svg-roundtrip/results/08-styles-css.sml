@sml 1.0-SVG-1
@indent none
svg(xmlns=http://www.w3.org/2000/svg)
  style = """
    .a { fill: #f00; }
    #b:hover { opacity: .5; }
    """
  rect
    .id b
    .class a
    .style stroke: rgb(0, 0, 0); stroke-width: 1.00 !important
    .width 30
    .height 30
  end
end
