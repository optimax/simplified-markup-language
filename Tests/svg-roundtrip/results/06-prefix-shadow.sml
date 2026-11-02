@sml 1.0
svg
  @ns "http://www.w3.org/2000/svg"
  @ns p "urn:one"
  metadata
    p:info
      .p:code "1"
    end
    g
      @ns p "urn:two"
      p:info
        .p:code "2"
      end
    end
    p:info
      .p:code "3"
    end
  end
end
