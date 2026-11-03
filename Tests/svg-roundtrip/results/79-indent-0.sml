@sml 1.0-SVG-4
@indent 0
svg(xmlns=http://www.w3.org/2000/svg, fill=none, height=32, viewBox=0 0 32 32, width=32)
  path(d=M15 15V29, stroke=url(#a))/
  g
    path(d=M1 1L2 2)/
    @comment """
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
