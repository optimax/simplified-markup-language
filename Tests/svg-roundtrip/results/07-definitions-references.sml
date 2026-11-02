@sml 1.0
svg
  @ns "http://www.w3.org/2000/svg"
  defs
    linearGradient
      .id "grad"
      stop
        .offset "0%"
        .stop-color "red"
      end
      stop
        .offset "100%"
        .stop-color "blue"
      end
    end
    clipPath
      .id "clip"
      rect
        .width "20"
        .height "20"
      end
    end
    filter
      .id "blur"
      feGaussianBlur
        .stdDeviation "2.5"
      end
    end
    symbol
      .id "sym"
      circle
        .r "10"
      end
    end
  end
  rect
    .width "50"
    .height "50"
    .fill "url(#grad)"
    .clip-path "url(#clip)"
    .filter "url(#blur)"
  end
  use
    .href "#sym"
  end
end
