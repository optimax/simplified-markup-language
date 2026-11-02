@sml 1.0
svg
  @ns "http://www.w3.org/2000/svg"
  text
    .x "12"
    .xml:space "preserve"
    = "Before "
    tspan
      .font-weight "bold"
      = " bold "
    end
    = " after"
    tspan
      .dx "2"
      = "!"
    end
  end
end
