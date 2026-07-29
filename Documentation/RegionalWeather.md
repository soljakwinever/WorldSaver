# Regional weather

The weather system samples the immutable world generator climate, applies
calendar and event modifiers, then advances deterministic weather phases for
each 8×8-chunk region.

## Create the weather catalog

1. In Unity, create
   `Assets/Project/Resources/Weather/WeatherSimulationSettings.asset` with
   **Create → World Saver → Weather → Simulation Settings**. The Resources path
   and asset name are required by `GameDataInstaller`.
2. Leave **Samples Per Chunk Axis** at `2` for four quarter-center samples per
   chunk. A region then uses 256 samples. Raising this value improves sampling
   density but increases the one-time cost when a region climate is first used.
3. Tune the seasonal temperature/moisture offsets, yearly variation, daily
   temperature range, simulation interval, neighbor influence, and passive
   accumulation melt.
4. Create effect assets with **Create → World Saver → Weather → Standard
   Effect**.
5. Create phase assets with **Create → World Saver → Weather → Phase**, set
   their duration range and environmental offsets, and add effect assets.
6. Create weather assets with **Create → World Saver → Weather → Weather**,
   configure their eligible seasons and climate ranges, then add phases in
   chronological order.
7. Add every weather asset to the simulation settings asset's **Weather** list.
   Weather IDs and effect IDs must be stable and unique because weather IDs are
   persisted in regional saves.

With no settings asset or no eligible weather definitions, the simulation
falls back to clear weather instead of preventing the game from starting.

## Configure effects

`StandardWeatherEffectData` supports both presentation and environmental
behavior:

- Use **Spawn On Phase Start** for a long-lived particle prefab such as rain,
  snow, ash, or petals. It is destroyed automatically when the phase stops.
- Use **Spawn On Pulse** for intermittent prefabs such as lightning or
  tornadoes. Set the pulse interval, spawn chance, and prefab lifetime.
- Enable **Full Screen Particle Effect** for camera-wide precipitation, sand,
  ash, or similar overlays. The system keeps one instance for the camera's
  current weather region, parents it to `Camera.main`, and scales it so
  **Full Screen Reference Height** fills the viewport vertically.
- A full-screen prefab may contain multiple child particle systems. Their
  authored emission-rate multipliers represent intensity `1`; the presenter
  scales both time and distance emission rates from `0` to that value as
  weather intensity changes. Use local simulation space and configure the
  particle renderer's sorting layer/order so particles render over the world
  but below any desired UI.
- **Full Screen Camera Distance** controls the prefab's distance in front of
  the camera. **Full Screen Intensity Response** controls how quickly emission
  catches up to simulation pulses.
- Enable **Finish Particles On Stop** to stop emission without clearing living
  particles. The presenter keeps the prefab alive until every child particle
  system and sub-emitter finishes, then destroys it. **Particle Stop Timeout
  Seconds** is a safety limit for incorrectly configured looping systems.
- Set **Temperature Offset** to affect ambient temperature while the effect is
  active.
- Set puddle or snow accumulation per tick to update the persisted regional
  accumulation values.
- The intensity curve uses normalized phase progress from `0` to `1`.
  A curve such as `(0,0) → (0.15,1) → (0.85,1) → (1,0)` produces a natural
  taper in and out. The included precipitation examples use this curve.

The built-in presenter only instantiates configured prefabs. Gameplay-specific
logic can live on those prefabs or in a system subscribed to `WeatherBus`.

## Query climate and temperature

Inject `IRegionalWeatherService` into a Zenject-managed object:

```csharp
[Inject] private IRegionalWeatherService weather;

float ambient = weather.GetAmbientTemperature(transform.position);
WeatherSample sample = weather.Sample(transform.position);
```

`ambient` is always clamped to `-1` (deadly cold) through `1` (deadly hot).
It includes base generator temperature, season, year, time of day, game-event
modifiers, the current weather phase, and temperature effects. Equipment,
shelter, wetness, and character resistance should modify this ambient value in
the character survival system.

The concrete `Chunkloader` also exposes:

```csharp
float temperature = chunkloader.CurrentAmbientTemperature;
WeatherSample weather = chunkloader.CurrentWeather;
```

Use `IRegionalClimateService.GetBaseline(region)` for immutable generator
statistics or `GetCurrentSnapshot(region)` for calendar-adjusted climate.

## React to weather

Inject `WeatherBus`, subscribe when the object becomes active, and unsubscribe
when it is disposed:

```csharp
weatherBus.RegionChanged += OnWeatherChanged;
weatherBus.Effect += OnWeatherEffect;
```

`RegionChanged` reports weather and phase transitions. `Effect` reports
`Started`, `Pulse`, and `Stopped` messages with regional world bounds,
intensity, tick, effect data, and an optional deterministic pulse position.
Use the bounds or region coordinate to ignore events outside an entity's area.

To integrate future game events, bind an implementation of
`IWeatherModifierSource` before `GameDataInstaller` installs its fallback.
Apply scoped temperature, moisture, or weather-weight modifiers in its
`Apply` method.

## Saving and offline behavior

Dynamic state is stored as region component type `0x5754`. It includes weather
and phase IDs/timing, deterministic random state, and puddle/snow accumulation.
Climate baselines are recomputed and cached rather than saved. Existing saves
without the component initialize weather deterministically when first loaded.

Regional catch-up uses a detached snapshot pipeline. Unity-facing weather
definitions and intensity curves are baked into plain worker inputs on the
main thread; phase transitions and accumulation then run on a background
thread. Results return to the main thread and are applied only when the live
region revision still matches the captured revision, preventing stale work
from overwriting chunk unloads, saves, or live weather updates.
