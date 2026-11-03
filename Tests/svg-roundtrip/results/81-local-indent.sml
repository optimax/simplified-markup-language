@sml 1.0-SVG-1
@indent 2
svg(xmlns=http://www.w3.org/2000/svg)
  metadata
    info
      @indent 4
      a/
      b
        c/
      end
    end
  end
  g
    @indent tab
    rect/
  end
  h
    = "\n\t"
    rect/
    = "\n  "
  end
end
