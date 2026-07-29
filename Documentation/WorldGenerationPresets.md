# World Generation Presets

World generation is configured by a `WorldGenerationPresetData` asset. A preset
references individual layer assets and can therefore share common layers with
other world types while replacing only the settings that differ.

The project ships with:

- `Standard`: the default for new worlds. It enables the revised Volcano feature.
- `Legacy`: the fallback for manifests created before presets were recorded. Its
  feature layer is disabled so existing procedural terrain does not change.

Both are under `Assets/Project/Resources/WorldGeneration`. The preset catalog
must remain at `Resources/WorldGeneration/PresetCatalog` unless the loading code
is changed.

## Creating a world preset

1. Create the required assets from **Assets > Create > World Generation > Layers**.
2. Create a **World Generation > Preset** asset and assign every required layer.
3. Give the preset a unique, stable persistent ID and set its version to `1`.
4. Add the preset to `PresetCatalog.presets`. Enable `Available For New Worlds`
   if it should appear on the New World screen.
5. Increment the preset version whenever a change would alter generated terrain.
   Keep the old preset/version asset available while saved worlds still use it.

Layer execution order is fixed:

1. Climate noise and biome selection
2. Elevation shaping
3. Lakes, small pools, and valleys
4. Biome micro-terrain and outcrops
5. Feature cells
6. Height normalization and seasonal biome tint

Grass height and prop rules live in the surface-detail layer and are sampled by
their existing rendering/spawn systems. An empty biome list loads every biome
under `Resources/Biomes`. An empty prop-rule list currently falls back to the
legacy `WorldData.propSpawnRules` array to preserve the existing project asset;
new presets should populate the surface-detail layer directly.

Water, beach, mountain, and cliff thresholds now come from the active elevation
layer. `TileCoverageComponent` is unchanged: `Chunk` passes it the active
preset's water height when coverage is configured.

## Creating a feature

1. Create **Assets > Create > World Generation > Feature**.
2. Assign a permanent `persistentId`, selection weight, footprint radius,
   aspect range, edge warp, and placement constraints.
3. Add one or more entries to **Generators**.
4. Use the generator type dropdown to choose a concrete generator. All enabled
   entries run in array order and their strengths multiply their effects.
5. Add the feature asset to a Feature Cell layer.

Feature placement is deterministic for the world seed and cell coordinate.
Each occupied cell selects one weighted feature. The runtime considers nearby
cells, so features cross chunk and cell boundaries without seams.

The footprint is a randomly rotated ellipse with seeded aspect variation,
domain-warped edges, and a smooth outer fade. A generator receives this mask and
must not apply changes outside it. The supplied Volcano generator changes only
height; it deliberately does not replace or tint the surrounding biome.

For subtler landmarks:

- Keep `maximumRadius` well below the feature-cell size.
- Use an aspect range rather than `1 / 1`.
- Keep edge warp around `0.15–0.3`.
- Prefer moderate generator strength and a wider smooth fade over a large radius.
- Restrict climate or allowed biomes instead of forcing a feature biome across
  the whole footprint.

## Adding a generator type

Feature generators are serializable managed references, not ScriptableObjects.
Create a serializable subclass in the data-types assembly:

```csharp
[Serializable]
public sealed class CraterFeatureGenerator : FeatureGenerator
{
    public float depth = 0.2f;

    public override void Generate(
        ref TerrainGenerationState terrain,
        in FeatureGenerationContext context,
        float strength)
    {
        terrain.height -= depth * context.mask * strength;
    }
}
```

After Unity recompiles, the type automatically appears in the generator
dropdown. Generator implementations must be deterministic, must treat their
serialized fields as immutable configuration, and must not use `UnityEngine.Random`
or mutate ScriptableObject assets. Terrain sampling runs concurrently on worker
threads.

`TerrainGenerationState` exposes height, base height, moisture, temperature,
and the current biome blend. `FeatureGenerationContext` exposes the world and
local coordinates, feature center, warped normalized distance, footprint mask,
and deterministic detail noise.

## Saved-world behavior

`world.json` stores `presetId` and `presetVersion` beside the seed. Loading
resolves the exact pair through the preset catalog. Unknown versions are shown
as unavailable instead of silently loading with different terrain.

Old manifests without preset fields resolve to the catalog's `legacyFallback`.
The resolved identity is written back when the world is launched. New worlds
record the preset selected on the New World screen.

The old generation fields remain on `WorldData` only as a compatibility source
for tests and projects without a preset catalog. With the included catalog
present, the active preset is authoritative.
