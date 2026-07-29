# Tile coverage

Tile coverage is normalized from `0` (clear) to `1` (fully covered) and is
stored per world cell. Loaded chunks advance coverage once per world tick and
save it through the chunk persistence component.

## Configure a coverage type

Create **World Saver → Weather → Coverage** and configure:

- a stable, unique coverage ID;
- the overlay sprite, color, render priority, and material tiling;
- allowed seasons and ambient temperature;
- optional weather, phase, and active effect IDs; and
- whether invalid conditions decay gradually or clear immediately.

Weather, phase, and effect collections are optional. Entries use the stable IDs
from their corresponding weather assets. Multiple non-empty condition groups
must all match.

## Enable coverage for a biome

Enable **Use Coverage** on a biome and add one or more settings. Each setting
selects a coverage asset and supplies accumulation, decay, and initial coverage
per world tick. Initial coverage is used only the first time a cell is created
and only when the coverage conditions currently match.

Coverage is currently limited to land cells. Multiple values may accumulate on
one cell; the highest-priority nonzero coverage is displayed.

## Rendering and persistence

The exact per-cell sprite and opacity are rendered on the chunk's Coverage
Tilemap. The dominant coverage for the chunk is also sent to `GroundTile.mat`
through `_CoverageTexture`, `_CoverageColor`, `_CoverageAmount`,
`_CoverageTiling`, and `_WorldOffset`.

Coverage values are captured by `DataController` with the rest of the chunk.
Zero-valued initialized cells are retained so a fully decayed tile does not
receive its initial coverage again after loading.
