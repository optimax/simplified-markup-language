@sml 1.0-SVG-1
@indent none
svg(xmlns=http://www.w3.org/2000/svg)
  defs
    linearGradient(id=grad)
      stop(offset=0%, stop-color=red)/
      stop(offset=100%, stop-color=blue)/
    end
    clipPath(id=clip)
      rect(width=20, height=20)/
    end
    filter(id=blur)
      feGaussianBlur(stdDeviation=2.5)/
    end
    symbol(id=sym)
      circle(r=10)/
    end
  end
  rect(width=50, height=50, fill=url(#grad), clip-path=url(#clip), filter=url(#blur))/
  use(href=#sym)/
end
