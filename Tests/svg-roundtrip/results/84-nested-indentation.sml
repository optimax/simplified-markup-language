@sml 1.0-SVG-1
svg(xmlns=http://www.w3.org/2000/svg, viewBox=0 0 200 60)
  @indent 4
  = "\n    "
  defs
    linearGradient(id=fire)
      stop(stop-color=hotpink, offset=0%)/
      stop(stop-color=goldenrod, offset=100%)/
    end
  end
  = "\n    "
  text(dy=0.5em, fill=url(#fire), font-size=50) = Wow!
  = "\n  "
end
