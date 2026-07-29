# Custom Auto Tiles

World tiles are resolved by `WorldTilemapRenderer` from logical `TileData`
stored in per-chunk arrays. Each `Chunk` owns its Ground and Water Tilemaps;
Unity `RuleTile` assets are not placed on them.

## Runtime flow

1. `Chunk.Init` sends logical tile identity and tint to the renderer.
2. The renderer stores ground and water arrays keyed by chunk coordinate.
3. For each auto-tiled cell it reads the eight neighboring logical cells.
   World coordinates are converted with floor division, so this also works for
   negative coordinates and across chunk borders.
4. The coordinator copies a 34×34 integer connectivity snapshot for each
   layer. Worker tasks calculate the 1,024 neighbor masks without accessing
   Unity objects.
5. Completed, version-matched masks return to the main thread and
   `AutoTileDefinition` selects the first matching rule.
6. A cached static Unity `Tile` and transform are written to the owning
   chunk's Tilemap using chunk-local coordinates.

Loading or unloading a chunk rebakes its loaded neighbors, including cells
whose results are displayed by another chunk. Changing a logical cell rebakes
only that cell and its eight neighbors.

Worker results carry a chunk bake version. A result is discarded and
rescheduled when logical data or a neighboring chunk changed after its snapshot
was captured. Unity assets and Tilemap APIs are only accessed on the main
thread.

Gameplay and persistence query the logical cache rather than the baked visual
tile, so sprite variants do not change tile identity.

## Authoring

Create an asset with **Create > World > Auto Tile**, then assign it to the
`AutoTile` field of a `TileData` asset.

Neighbor masks use this bit layout:

| Bit | Direction |
| ---: | :--- |
| 0 | North-west |
| 1 | North |
| 2 | North-east |
| 3 | West |
| 4 | East |
| 5 | South-west |
| 6 | South |
| 7 | South-east |

`RequiredNeighbors` bits must connect and `ForbiddenNeighbors` bits must not
connect. Unset bits are ignored. Rules are evaluated in array order.

Two cells connect when they use the same `TileData`. They can also connect
across different tile definitions by assigning the same non-empty
`ConnectivityGroup`.

Rule transforms can rotate or mirror a pattern and its output. Multiple sprites
and random transforms are selected deterministically from world coordinates,
so a chunk produces the same baked result after reload.

## Legacy migration

Use **Tools > World > Migrate All TileData To Custom Auto Tiles** to copy the
sprites, neighbor constraints, collider type, and transform settings from
legacy RuleTile assets into `AutoTileDefinition` assets. The migration reads
serialized data only; runtime assemblies do not reference the RuleTile package.

The migration also runs once after the implementation's first script reload so
the existing project tiles immediately use the new renderer path.
