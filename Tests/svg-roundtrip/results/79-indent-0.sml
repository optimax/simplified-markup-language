@sml 1.0-SVG-1
@indent 0
svg(xmlns=http://www.w3.org/2000/svg, width=32, height=32, viewBox=0 0 32 32, fill=none)
  path(d=M15 15V29, stroke=url(#a))/
  g
    path(d=M1 1L2 2)/
    @rem """
      two-line
      comment
      """
  end
  defs
    linearGradient(id=a)
      stop(stop-color=#000)/
    end
  end
end
