@sml 1.0-SVG-1
@indent none
svg(xmlns=http://www.w3.org/2000/svg)
  text = Zażółć 😀 & <test> café
  metadata
    .note """
      A	B
      C
      """
    .data "\"quote\""
  end
end
