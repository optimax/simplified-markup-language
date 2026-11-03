# SVG path commands

The `d` attribute of `<path>` (and the `path` attribute of `animateMotion` and `textPath`) is a sequence of commands, each a letter followed by numbers.

- **Uppercase** letters use absolute coordinates; **lowercase** letters are relative to the current point.
- Numbers are separated by spaces or commas. A separator may be omitted where no ambiguity arises: before a minus sign (`10-5`), or before a second decimal point (`.5.5` is `0.5 0.5`).
- A command letter may be left out when the same command repeats: `L 10 10 20 20` draws two lines. After `M`/`m`, extra coordinate pairs are treated as `L`/`l`.
- The **current point** is where the previous command ended. The **subpath start** is the point of the last `M`/`m`.

## Move

| Command | Parameters | Meaning |
|-----|----------|-------------------------|
| `M` / `m` | `x y` | Start a new subpath at (x, y) without drawing. Following pairs are implicit `L`/`l`. |

## Lines

| Command | Parameters | Meaning |
|-----|----------|-------------------------|
| `L` / `l` | `x y` | Straight line to (x, y). |
| `H` / `h` | `x` | Horizontal line to x; y stays the same. |
| `V` / `v` | `y` | Vertical line to y; x stays the same. |
| `Z` / `z` | none | Close the subpath with a straight line back to its start. `Z` and `z` are identical. |

## Cubic Bézier curves

| Command | Parameters | Meaning |
|-----|----------|-------------------------|
| `C` / `c` | `x1 y1 x2 y2 x y` | Cubic curve to (x, y), with control points (x1, y1) near the start and (x2, y2) near the end. |
| `S` / `s` | `x2 y2 x y` | Smooth cubic curve to (x, y). The first control point is the reflection of the previous command's second control point about the current point, or the current point itself if the previous command was not `C`, `c`, `S`, or `s`. |

## Quadratic Bézier curves

| Command | Parameters | Meaning |
|-----|----------|-------------------------|
| `Q` / `q` | `x1 y1 x y` | Quadratic curve to (x, y) with the single control point (x1, y1). |
| `T` / `t` | `x y` | Smooth quadratic curve to (x, y). The control point is the reflection of the previous control point, or the current point if the previous command was not `Q`, `q`, `T`, or `t`. |

## Elliptical arcs

| Command | Parameters | Meaning |
|-----|----------|-------------------------|
| `A` / `a` | `rx ry angle large-arc sweep x y` | Arc of an ellipse to (x, y). |

The arc parameters:

- `rx`, `ry`: the ellipse's radii. If they are too small to reach (x, y), they are scaled up until they do.
- `angle`: rotation of the ellipse's x-axis, in degrees.
- `large-arc`: `1` takes the arc of 180° or more, `0` the smaller one.
- `sweep`: `1` draws in the positive-angle direction (clockwise in SVG's y-down coordinates), `0` the other way.
- The two flags are single characters `0` or `1` and need no separator: `a.75.75 0 0 0-.75-.75` and `a 0.75 0.75 0 0 0 -0.75 -0.75` are the same arc.

If `rx` or `ry` is 0, the arc is a straight line. If (x, y) is the current point, nothing is drawn.

## Example

    M 10 10        move to (10, 10)
    h 80           line 80 to the right
    v 80           line 80 down
    H 10           line back to x = 10
    Z              close the square
    M 30 50
    Q 50 20 70 50  quadratic curve with control point (50, 20)
    T 110 50       smooth continuation
    A 20 20 0 0 1 150 50
                   half-circle arc of radius 20