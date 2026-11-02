@sml 1.0
svg
  @ns "http://www.w3.org/2000/svg"
  text
    .xml:space "preserve"
    = "\n\t X \rY "
  end
  rect
    .data "A\nB\tC\rD"
  end
end
