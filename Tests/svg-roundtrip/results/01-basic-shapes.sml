@sml 1.0
svg
  @ns "http://www.w3.org/2000/svg"
  .width "400"
  .height "200"
  .viewBox "0 0 400 200"
  rect
    .x "10"
    .y "20"
    .width "50"
    .height "60"
    .fill "#4477cc"
  end
  circle
    .cx "100"
    .cy "70"
    .r "30"
  end
  ellipse
    .cx "190"
    .cy "80"
    .rx "30"
    .ry "15"
  end
  line
    .x1 "1"
    .y1 "2"
    .x2 "30"
    .y2 "50"
  end
  polyline
    .points "0,0 5,10 10,0"
  end
  polygon
    .points "2,2 22,2 10,18"
  end
end
