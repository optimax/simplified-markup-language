@sml 1.0-SVG-1
@indent 2
@comment """
  Prolog comment at depth 0
  """
svg(xmlns=http://www.w3.org/2000/svg)
  @comment """
    Created with an editor
    second line

    after an empty line
    """
  g
    @comment """
      deeper comment
      """
    rect/
  end
  @comment "\n  flush lines are not laid out\n  "
  text
    @noindent
    @comment """

          in mixed content
        
      """
    = x
  end
end
@comment """
  epilog
  """
