# Test Data Specification

This document is the source-level specification for the generated `.ply` test
data. It is written so the same point data can be reproduced from C# without
reading the Python implementation.

## Global Rules

- Output root: `TestData/`.
- PLY version: `1.0`.
- Standard generation seed: `12345`.
- Standard generation sequence: `54`.
- Coordinates use IEEE 754 single-precision floats unless a case explicitly
  declares `double`.
- Colors use `uchar` channels in the range `0..255`.
- Binary files write vertex rows in declared property order.
- ASCII files write one vertex per line in declared property order.
- The standard set is generated in the order listed in this document.

## PCG32

All pseudo-random values use PCG32. State and arithmetic are unsigned 64-bit
unless stated otherwise.

Constants:

```text
multiplier = 6364136223846793005
mask64 = 0xffffffffffffffff
mask32 = 0xffffffff
seed = 12345
sequence = 54
```

Initialization:

```text
state = 0
increment = ((sequence << 1) | 1) & mask64
NextUInt32()
state = (state + seed) & mask64
NextUInt32()
```

`NextUInt32()`:

```text
oldState = state
state = (oldState * multiplier + increment) & mask64
xorshifted = (((oldState >> 18) ^ oldState) >> 27) & mask32
rotation = oldState >> 59
return ((xorshifted >> rotation) | (xorshifted << ((-rotation) & 31))) & mask32
```

Derived values:

```text
Uniform01() = NextUInt32() / 4294967296.0
Uniform(min, max) = min + (max - min) * Uniform01()
Integer(min, max) = min + NextUInt32() % (max - min)
Sign() = -1.0 if (NextUInt32() & 1) == 0 else 1.0
```

The C# implementation must use unchecked unsigned integer overflow semantics.

## Shared Shape Functions

`Linspace(min, max, count)` returns `count` evenly spaced values including both
endpoints. If `count == 1`, return `min`.

`Grid(nx, ny, nz)`:

```text
xs = Linspace(-1.0, 1.0, nx)
ys = Linspace(-1.0, 1.0, ny)
zs = Linspace(-1.0, 1.0, nz)

for i in 0 .. nx * ny * nz - 1:
    iz = i % nz
    ix = (i / nz) % nx
    iy = i / (nx * nz)
    point = (xs[ix], ys[iy], zs[iz])
```

`Sphere(count)`:

```text
goldenAngle = pi * (3.0 - sqrt(5.0))

for i in 0 .. count - 1:
    y = 1.0 - 2.0 * ((i + 0.5) / count)
    radiusAtY = sqrt(max(0.0, 1.0 - y * y))
    theta = i * goldenAngle
    radius = Uniform(0.9, 1.0)
    x = cos(theta) * radiusAtY * radius
    z = sin(theta) * radiusAtY * radius
    point = (x, y * radius, z)
```

`Clusters(count)`:

```text
centers = [
    (-0.6, -0.4,  0.0),
    ( 0.5, -0.2,  0.3),
    ( 0.0,  0.5, -0.4),
]

for i in 0 .. count - 1:
    center = centers[Integer(0, 3)]
    noise = (
        Uniform(-0.12, 0.12),
        Uniform(-0.12, 0.12),
        Uniform(-0.12, 0.12)
    )
    point = center + noise
```

`CubeSurface(count)`:

```text
for i in 0 .. count - 1:
    point = (
        Uniform(-1.0, 1.0),
        Uniform(-1.0, 1.0),
        Uniform(-1.0, 1.0)
    )
    axis = Integer(0, 3)
    point[axis] = Sign()
```

`GradientPlane(nx, ny)`:

```text
xs = Linspace(-1.0, 1.0, nx)
ys = Linspace(-0.65, 0.65, ny)

for i in 0 .. nx * ny - 1:
    ix = i % nx
    iy = i / nx
    x = xs[ix]
    y = ys[iy]
    z = sin(x * pi) * cos(y * pi) * 0.1
    point = (x, y, z)
```

## Shared Attribute Functions

`Colorize(points)`:

```text
minX, minY, minZ = component-wise minimum over all points
maxX, maxY, maxZ = component-wise maximum over all points
range = max(maxValue - minValue, 1e-6) per component

for each point:
    red   = (uchar)Clamp(((x - minX) / rangeX) * 255.0, 0.0, 255.0)
    green = (uchar)Clamp(((y - minY) / rangeY) * 255.0, 0.0, 255.0)
    blue  = (uchar)Clamp(((z - minZ) / rangeZ) * 255.0, 0.0, 255.0)
```

The cast to `uchar` truncates toward zero.

`Normals(points)`:

```text
length = sqrt(x * x + y * y + z * z)
normal = (x / length, y / length, z / length)
```

`IntensityConfidence(points)`:

```text
maxDistance = maximum length over all points

for i in 0 .. count - 1:
    distance = length(points[i])
    intensity = Clamp(1.0 - distance / maxDistance, 0.0, 1.0)
    confidence = 0.2 + (1.0 - 0.2) * i / (count - 1)
```

`Rgba(points)` uses `Colorize(points)` and constant `alpha = 220`.

## Generation Order

Create one `PCG32(seed: 12345, sequence: 54)` instance and consume it in this
exact order:

1. `grid_1k = Grid(10, 10, 10)`
2. `grid_10k = Grid(100, 100, 1)`
3. `sphere_2k = Sphere(2048)`
4. `sphere_50k = Sphere(50000)`
5. `clusters = Clusters(4096)`
6. `cube = CubeSurface(4096)`
7. `plane = GradientPlane(96, 64)`
8. Write the standard cases below in listed order.
9. During `mixed_numeric_types.ply`, consume `Integer(0, 2048)` once per point
   for all labels, then `Integer(-10, 10)` once per point for all scan IDs.
10. If generating optional large data, create `Sphere(1000000)` after the
    standard cases have been assembled.

The deterministic shapes `Grid` and `GradientPlane` do not consume PRNG values.

## Standard Files

`valid/xyz_ascii.ply`

- Format: `ascii`.
- Points: `grid_1k`.
- Properties: `float x`, `float y`, `float z`.

`valid/xyz_binary_le.ply`

- Format: `binary_little_endian`.
- Points: `grid_1k`.
- Properties: `float x`, `float y`, `float z`.

`valid/xyz_binary_be.ply`

- Format: `binary_big_endian`.
- Points: `grid_1k`.
- Properties: `float x`, `float y`, `float z`.

`valid/xyz_rgb_ascii.ply`

- Format: `ascii`.
- Points: `plane`.
- Properties: `float x`, `float y`, `float z`, `uchar red`, `uchar green`, `uchar blue`.
- Colors: `Colorize(plane)`.

`valid/xyz_rgba_binary_le.ply`

- Format: `binary_little_endian`.
- Points: `sphere_2k`.
- Properties: `float x`, `float y`, `float z`, `uchar red`, `uchar green`, `uchar blue`, `uchar alpha`.
- Colors: `Colorize(sphere_2k)`.
- Alpha: `220`.

`valid/xyz_normal_binary_le.ply`

- Format: `binary_little_endian`.
- Points: `sphere_2k`.
- Properties: `float x`, `float y`, `float z`, `float nx`, `float ny`, `float nz`.
- Normals: `Normals(sphere_2k)`.

`edge_cases/xyz_intensity_confidence.ply`

- Format: `binary_little_endian`.
- Points: `clusters`.
- Properties: `float x`, `float y`, `float z`, `float intensity`, `float confidence`.
- Extra attributes: `IntensityConfidence(clusters)`.

`edge_cases/property_order_variant.ply`

- Format: `ascii`.
- Points: `cube`.
- Properties: `float z`, `float x`, `float y`.
- Vertex row order: write `z`, then `x`, then `y`.

`edge_cases/comments_object_info.ply`

- Format: `ascii`.
- Points: `plane`.
- Properties: `float x`, `float y`, `float z`, `uchar red`, `uchar green`, `uchar blue`.
- Colors: `Colorize(plane)`.
- Header comments:
  - `Generated by PlyPointCloudTestData`
  - `Exercises non-vertex header metadata`
- Header object info:
  - `source synthetic`
  - `coordinate_system right_handed_y_up`

`edge_cases/mixed_numeric_types.ply`

- Format: `binary_big_endian`.
- Points: `clusters`.
- Properties: `double x`, `double y`, `double z`, `uchar red`, `uchar green`, `uchar blue`, `ushort label`, `int scan_id`.
- Colors: `Colorize(clusters)`.
- Labels: `Integer(0, 2048)` per point.
- Scan IDs: `Integer(-10, 10)` per point.

`stress/small_grid_10k.ply`

- Format: `binary_little_endian`.
- Points: `grid_10k`.
- Properties: `float x`, `float y`, `float z`, `uchar red`, `uchar green`, `uchar blue`.
- Colors: `Colorize(grid_10k)`.

`stress/sphere_rgb_50k.ply`

- Format: `binary_little_endian`.
- Points: `sphere_50k`.
- Properties: `float x`, `float y`, `float z`, `uchar red`, `uchar green`, `uchar blue`.
- Colors: `Colorize(sphere_50k)`.

## Optional Large File

`stress/large_sphere_rgb_1m.ply` is generated only when large data is requested.

- Format: `binary_little_endian`.
- Points: `Sphere(1000000)`.
- Properties: `float x`, `float y`, `float z`, `uchar red`, `uchar green`, `uchar blue`.
- Colors: `Colorize(points)`.
