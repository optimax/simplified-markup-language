@sml 1.0-SVG-1
@indent none
svg
  @ns http://www.w3.org/2000/svg
  @ns xlink http://www.w3.org/1999/xlink
  @ns v urn:vendor:graphics
  defs
    g(id=g1)
      path(d=M0 0L1 1)/
    end
  end
  use(xlink:href=#g1, v:hint=keep-me)/
  v:private(v:flag=yes)
    v:payload = value
  end
end
