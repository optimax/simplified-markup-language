@sml 1.0-SVG-1
@indent 2
@comments """
  === prolog banner ===
  = second line =
  """
svg(xmlns=http://www.w3.org/2000/svg)
  @comments """
    ==========================================
    =  Example test case                     =
    # looks like a source comment
    ==========================================
    """
  @rem " spaced "
  g
    @noindent
    @comments """"
      a
      b
      """
      """"
  end
  rect(width=1)/
end
