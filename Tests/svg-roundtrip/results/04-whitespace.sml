@sml 1.0
svg
  @ns "http://www.w3.org/2000/svg"
  = "\n  "
  title
    = "  Title  "
  end
  = "\n  "
  desc
    = "First\nSecond\tThird"
  end
  = "\n  "
  text
    .xml:space "preserve"
    = " A  B\n C "
  end
  = "\n"
end
