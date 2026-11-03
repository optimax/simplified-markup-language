@sml 1.0-SVG-1
@indent 2
svg(xmlns=http://www.w3.org/2000/svg, viewBox=0 0 200 60)
  @indent 4
  = "\n    "
  defs
    linearGradient(id=fire)
      stop(offset=0%, stop-color=hotpink)/
      stop(offset=100%, stop-color=goldenrod)/
    end
  end
  = "\n    "
  text(dy=0.5em, fill=url(#fire), font-size=50) = Wow!
  = "\n  "
end
