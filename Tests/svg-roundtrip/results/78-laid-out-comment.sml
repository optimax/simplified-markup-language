@sml 1.0-SVG-1
@indent 2
@rem """
  Prolog comment at depth 0
  """
svg(xmlns=http://www.w3.org/2000/svg)
  @rem """
    Created with an editor
    second line

    after an empty line
    """
  g
    @rem """
      deeper comment
      """
    rect/
  end
  @rem "\n  flush lines are not laid out\n  "
  text
    @noindent
    @rem """

          in mixed content
        
      """
    = x
  end
end
@rem """
  epilog
  """
