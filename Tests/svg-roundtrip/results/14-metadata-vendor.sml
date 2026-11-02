@sml 1.0
svg
  @ns "http://www.w3.org/2000/svg"
  @ns dc "http://purl.org/dc/elements/1.1/"
  @ns app "urn:app"
  metadata
    dc:title
      = "Drawing"
    end
    app:object
      @ns other "urn:other"
      .app:kind "vector"
      .other:key "v"
      app:child
      end
    end
  end
  g
    .id "layer1"
    rect
      .width "1"
      .height "2"
    end
  end
end
