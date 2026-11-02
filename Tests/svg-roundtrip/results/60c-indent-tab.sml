@sml 1.0-SVG-3
@indent tab
svg(xmlns=http://www.w3.org/2000/svg)
  defs
    linearGradient(id=g)
      stop(offset=0)/
      stop(offset=1)/
    end
  end
  g(id=layer)
    rect(height=10, width=10)/
    @comment " note "
    circle(r=5)/
  end
end
