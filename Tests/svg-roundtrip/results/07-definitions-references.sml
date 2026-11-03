@sml 1.0-SVG-1
svg(xmlns=http://www.w3.org/2000/svg)
  defs
    linearGradient(id=grad)
      stop(offset=0%, stop-color=red)/
      stop(offset=100%, stop-color=blue)/
    end
    clipPath(id=clip)
      rect(height=20, width=20)/
    end
    filter(id=blur)
      feGaussianBlur(stdDeviation=2.5)/
    end
    symbol(id=sym)
      circle(r=10)/
    end
  end
  rect(clip-path=url(#clip), fill=url(#grad), filter=url(#blur), height=50, width=50)/
  use(href=#sym)/
end
