# Persistent chunk tiles

Player-placed tiles are stored as chunk overrides. They survive chunk unloading
and are written by the existing region save system. `Node` and
`PersistentEntity` are not involved because a tile belongs to a tilemap cell,
not to an entity.

## Setup

Create a `TileData` asset for every world tile, assign its Unity `TileBase`,
base tint, and unique non-negative `TileId`, then register it in
`WorldData.tiles`. `TileId` is the persistent identity, so the registry can be
reordered, but an ID must never be reused for a different tile after saving.

The `Chunk` prefab must keep its ground and water `Tilemap` references assigned.
For the built-in debug control, optionally assign `mousePlacementTile` on
`Chunkloader`; otherwise it uses the chunk's `WallTile`.

## Debug controls

- Left click places the configured tile on the ground layer.
- Right click retains the existing runtime-entity spawn demo.

Placement is accepted only after the target chunk has completed restoration.

## Calling the API

Coordinates passed to the API are world grid cells, not positions relative to
the chunk:

```csharp
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

Vector3Int cell = new(42, -3, 0);

if (chunkloader.TryGetLoadedChunk(cell, out Chunk chunk))
{
    // selectedTile is a TileData registered in WorldData.tiles.
    bool placed = chunk.TryPlaceTile(
        cell,
        PersistentTileLayer.Ground,
        selectedTile);

    // Makes this layer/cell persistently empty.
    bool cleared = chunk.TryClearTile(
        cell,
        PersistentTileLayer.Ground);

    // Removes the override and restores generated terrain.
    bool reset = chunk.TryResetTile(
        cell,
        PersistentTileLayer.Ground);
}
```

The methods return `false` when the cell is outside the chunk, restoration is
incomplete, the layer is invalid, or the `TileData` is null, invalid, or not
registered in `WorldData.tiles`.

Generated terrain multiplies the biome tint by `TileData.Color`. Player-placed
tiles use `TileData.Color` directly. Unity's raw `TileBase` is an implementation
detail stored inside `TileData`.

`Clear` and `Reset` are intentionally different. Clearing saves an empty cell,
so procedural terrain will stay hidden after loading. Resetting removes the
saved override and immediately restores the original generated ground or water
tile.

## Saving

No explicit save call is required after each edit. When a chunk unloads,
`DataController` captures its tile overrides and requests a region flush.
`DataController.SaveAsync()` also captures edits in chunks that are still
loaded, so an existing manual or shutdown save flow can persist them.

Region format version 2 contains tile overrides. Version 1 region files remain
readable and simply load with no tile overrides.
