using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using IngameDebugConsole;
using UnityEngine;
using Zenject;

namespace Project.Scripts.TimeAndWeather
{
    public sealed class RegionalWeatherService :
        IRegionalWeatherService,
        IOfflineRegionSimulation,
        IPreparedRegionSimulation,
        IRegionSimulationAppliedHandler,
        IInitializable,
        ITickable,
        IDisposable
    {
        private const ushort WeatherComponentTypeId = 0x5754;
        private const ushort WeatherComponentVersion = 1;

        private readonly IRegionalClimateService _climate;
        private readonly WeatherSimulationSettings _settings;
        private readonly IRegionRepository _regions;
        private readonly IWeatherWorldClock _clock;
        private readonly MapSignalBus _mapSignals;
        private readonly WeatherBus _weatherBus;
        private readonly IClimateCoreWeatherSource _climateCores;
        private readonly PlaneSelection _planeSelection;
        private float _globalTemperatureOffset;
        private readonly Dictionary<Vector2Int, ActiveRegion> _active = new();
        private readonly Dictionary<string, WeatherData> _weatherById =
            new(StringComparer.Ordinal);
        private readonly Dictionary<Vector2Int, CachedWeatherSample>
            _sampleCache = new();

        private long _lastLiveTick = -1;

        private readonly struct CachedWeatherSample
        {
            public readonly long Tick;
            public readonly WeatherSample Sample;

            public CachedWeatherSample(long tick, WeatherSample sample)
            {
                Tick = tick;
                Sample = sample;
            }
        }

        private sealed class ActiveRegion
        {
            public RuntimeRegion Region;
            public WeatherRegionState State;
            public int LoadedChunkCount;
            public int LoadVersion;
        }

        private sealed class WeatherRegionState
        {
            public string WeatherId = string.Empty;
            public int PhaseIndex = -1;
            public long PhaseStartTick;
            public long PhaseEndTick;
            public long LastWeatherTick;
            public long CooldownEndTick;
            public uint RandomState;
            public float Intensity;
            public float PuddleAccumulation;
            public float SnowAccumulation;

            public WeatherRegionState Clone()
            {
                return (WeatherRegionState)MemberwiseClone();
            }
        }

        private sealed class KernelWeather
        {
            public string Id;
            public int CooldownTicks;
            public float SelectionWeight;
            public KernelPhase[] Phases;
        }

        private sealed class KernelPhase
        {
            public int MinimumDurationTicks;
            public int MaximumDurationTicks;
            public float BaseIntensity;
            public KernelEffect[] Effects;
        }

        private sealed class KernelEffect
        {
            public float PuddlePerTick;
            public float SnowPerTick;
            public float[] IntensitySamples;

            public float Evaluate(float progress)
            {
                if (IntensitySamples == null || IntensitySamples.Length == 0)
                    return 0f;

                float scaled = Clamp01(progress) *
                               (IntensitySamples.Length - 1);
                int lower = Math.Min(
                    (int)scaled,
                    IntensitySamples.Length - 1);
                int upper = Math.Min(
                    lower + 1,
                    IntensitySamples.Length - 1);
                float blend = scaled - lower;
                return Clamp01(
                    IntensitySamples[lower] +
                    (IntensitySamples[upper] -
                     IntensitySamples[lower]) * blend);
            }
        }

        private sealed class WeatherSimulationWork : IRegionSimulationWork
        {
            private readonly RuntimeRegion _region;
            private readonly WeatherRegionState _state;
            private readonly Dictionary<string, KernelWeather> _weatherById;
            private readonly KernelWeather[] _weather;
            private readonly long _fromTick;
            private readonly long _toTick;
            private readonly float _meltPerTick;
            private readonly string _forcedWeatherId;
            private readonly long _forcedWeatherStartTick;
            private readonly int _forcedPhaseIndex;

            public WeatherSimulationWork(
                RuntimeRegion region,
                WeatherRegionState state,
                Dictionary<string, KernelWeather> weatherById,
                KernelWeather[] weather,
                long fromTick,
                long toTick,
                float meltPerTick,
                string forcedWeatherId,
                long forcedWeatherStartTick,
                int forcedPhaseIndex)
            {
                _region = region;
                _state = state;
                _weatherById = weatherById;
                _weather = weather;
                _fromTick = fromTick;
                _toTick = toTick;
                _meltPerTick = meltPerTick;
                _forcedWeatherId = forcedWeatherId ?? string.Empty;
                _forcedWeatherStartTick = forcedWeatherStartTick;
                _forcedPhaseIndex = forcedPhaseIndex;
            }

            public void Execute()
            {
                Advance();
                WriteState(_region, _state);
            }

            private void Advance()
            {
                if (_toTick <= _fromTick)
                    return;

                long cursor = _fromTick;
                int transitionGuard = 0;
                while (cursor < _toTick && transitionGuard++ < 1024)
                {
                    EnsureForcedWeather(cursor);
                    bool forced = cursor >= _forcedWeatherStartTick;
                    KernelWeather weather = GetWeather(_state.WeatherId);
                    KernelPhase phase = GetPhase(weather, _state.PhaseIndex);

                    if (phase == null)
                    {
                        SelectWeather(cursor, _state.WeatherId);
                        weather = GetWeather(_state.WeatherId);
                        phase = GetPhase(weather, _state.PhaseIndex);
                        if (phase == null)
                        {
                            _state.LastWeatherTick = _toTick;
                            return;
                        }
                    }

                    long segmentEnd = Math.Min(_toTick, _state.PhaseEndTick);
                    if (cursor < _forcedWeatherStartTick)
                    {
                        segmentEnd = Math.Min(
                            segmentEnd,
                            _forcedWeatherStartTick);
                    }
                    long elapsed = Math.Max(0, segmentEnd - cursor);
                    ApplyAccumulation(
                        phase,
                        elapsed,
                        segmentEnd,
                        forced);
                    cursor = segmentEnd;

                    if (forced)
                        break;

                    if (cursor < _state.PhaseEndTick)
                        break;

                    if (_state.PhaseIndex + 1 < weather.Phases.Length)
                    {
                        _state.PhaseIndex++;
                        BeginPhase(weather, cursor);
                    }
                    else
                    {
                        string previousWeather = _state.WeatherId;
                        _state.CooldownEndTick =
                            checked(cursor + weather.CooldownTicks);
                        SelectWeather(cursor, previousWeather);
                    }
                }

                _state.LastWeatherTick = _toTick;
                _state.Intensity =
                    GetPhase(
                        GetWeather(_state.WeatherId),
                        _state.PhaseIndex)?.BaseIntensity ?? 0f;
            }

            private void ApplyAccumulation(
                KernelPhase phase,
                long elapsedTicks,
                long atTick,
                bool forced)
            {
                float puddles = 0f;
                float snow = 0f;
                // Sample the middle of the simulated segment. Sampling exactly
                // at a phase boundary evaluates precipitation curves at their
                // final zero key and incorrectly melts an entire offline
                // blizzard segment.
                long sampleTick = atTick - elapsedTicks / 2;
                float progress = forced
                    ? 0.5f
                    : GetPhaseProgress(_state, sampleTick);

                foreach (KernelEffect effect in phase.Effects)
                {
                    float intensity =
                        effect.Evaluate(progress) * phase.BaseIntensity;
                    puddles += effect.PuddlePerTick * intensity;
                    snow += effect.SnowPerTick * intensity;
                }

                _state.PuddleAccumulation = Clamp01(
                    _state.PuddleAccumulation +
                    puddles * elapsedTicks -
                    _meltPerTick * elapsedTicks);
                _state.SnowAccumulation = Clamp01(
                    _state.SnowAccumulation +
                    snow * elapsedTicks -
                    _meltPerTick * elapsedTicks);
            }

            private void SelectWeather(long tick, string previousWeatherId)
            {
                if (tick >= _forcedWeatherStartTick &&
                    TrySelectForcedWeather(tick))
                {
                    return;
                }

                float total = 0f;
                foreach (KernelWeather weather in _weather)
                {
                    if (tick < _state.CooldownEndTick &&
                        string.Equals(
                            weather.Id,
                            previousWeatherId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    total += weather.SelectionWeight;
                }

                if (total <= 0f)
                {
                    _state.WeatherId = string.Empty;
                    _state.PhaseIndex = -1;
                    _state.PhaseStartTick = tick;
                    _state.PhaseEndTick = long.MaxValue;
                    _state.Intensity = 0f;
                    return;
                }

                float roll = NextFloat(ref _state.RandomState) * total;
                KernelWeather selected = null;
                foreach (KernelWeather weather in _weather)
                {
                    if (tick < _state.CooldownEndTick &&
                        string.Equals(
                            weather.Id,
                            previousWeatherId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    selected = weather;
                    roll -= weather.SelectionWeight;
                    if (roll <= 0f)
                        break;
                }

                if (selected == null)
                    return;

                _state.WeatherId = selected.Id;
                _state.PhaseIndex = 0;
                BeginPhase(selected, tick);
            }

            private void EnsureForcedWeather(long tick)
            {
                if (tick < _forcedWeatherStartTick ||
                    string.Equals(
                        _state.WeatherId,
                        _forcedWeatherId,
                        StringComparison.Ordinal) &&
                    _state.PhaseIndex == _forcedPhaseIndex)
                {
                    return;
                }

                TrySelectForcedWeather(tick);
            }

            private bool TrySelectForcedWeather(long tick)
            {
                KernelWeather forced = GetWeather(_forcedWeatherId);
                if (forced == null || forced.Phases.Length == 0)
                    return false;

                _state.WeatherId = forced.Id;
                _state.PhaseIndex = Math.Max(
                    0,
                    Math.Min(_forcedPhaseIndex, forced.Phases.Length - 1));
                _state.CooldownEndTick = tick;
                _state.PhaseStartTick = tick;
                _state.PhaseEndTick = long.MaxValue;
                _state.Intensity =
                    forced.Phases[_state.PhaseIndex].BaseIntensity;
                return true;
            }

            private void BeginPhase(KernelWeather weather, long tick)
            {
                KernelPhase phase = GetPhase(weather, _state.PhaseIndex);
                if (phase == null)
                {
                    _state.PhaseEndTick = tick;
                    return;
                }

                int duration = NextInt(
                    ref _state.RandomState,
                    phase.MinimumDurationTicks,
                    phase.MaximumDurationTicks + 1);
                _state.PhaseStartTick = tick;
                _state.PhaseEndTick = checked(tick + duration);
                _state.Intensity = phase.BaseIntensity;
            }

            private KernelWeather GetWeather(string id)
            {
                if (string.IsNullOrEmpty(id))
                    return null;
                _weatherById.TryGetValue(id, out KernelWeather weather);
                return weather;
            }

            private static KernelPhase GetPhase(
                KernelWeather weather,
                int index)
            {
                return weather != null &&
                       index >= 0 &&
                       index < weather.Phases.Length
                    ? weather.Phases[index]
                    : null;
            }
        }

        public RegionalWeatherService(
            IRegionalClimateService climate,
            WeatherSimulationSettings settings,
            IRegionRepository regions,
            IWeatherWorldClock clock,
            MapSignalBus mapSignals,
            WeatherBus weatherBus,
            WorldData worldData,
            IClimateCoreWeatherSource climateCores,
            PlaneSelection planeSelection)
        {
            _climate = climate;
            _settings = settings;
            _regions = regions;
            _clock = clock;
            _mapSignals = mapSignals;
            _weatherBus = weatherBus;
            _climateCores = climateCores;
            _planeSelection = planeSelection;
            _globalTemperatureOffset =
                Mathf.Clamp(worldData.globalTemperatureOffset, -2f, 2f);

            foreach (WeatherData weather in settings.Weather)
            {
                if (weather == null || string.IsNullOrEmpty(weather.WeatherId))
                    continue;

                if (!_weatherById.TryAdd(weather.WeatherId, weather))
                {
                    Debug.LogError(
                        $"Duplicate weather id '{weather.WeatherId}' in {settings.name}.");
                }
            }
        }

        public void Initialize()
        {
            _mapSignals.ChunkLoaded += OnChunkLoaded;
            _mapSignals.ChunkUnloaded += OnChunkUnloaded;
            _planeSelection.Changed += OnPlaneChanged;
            DebugLogConsole.AddCommand<float>(
                "weather.temperature_offset",
                "Sets the normalized global temperature offset.",
                DebugSetGlobalTemperatureOffset,
                "offset");
            DebugLogConsole.AddCommand<string, int, int>(
                "weather.start",
                "Starts a weather ID in a region.",
                DebugStartWeather,
                "weatherId",
                "regionX",
                "regionY");
        }

        public void Dispose()
        {
            _mapSignals.ChunkLoaded -= OnChunkLoaded;
            _mapSignals.ChunkUnloaded -= OnChunkUnloaded;
            _planeSelection.Changed -= OnPlaneChanged;
            DebugLogConsole.RemoveCommand<float>(DebugSetGlobalTemperatureOffset);
            DebugLogConsole.RemoveCommand<string, int, int>(DebugStartWeather);
        }

        private void OnPlaneChanged(PlaneData _, PlaneData __)
        {
            _active.Clear();
            _sampleCache.Clear();
            _lastLiveTick = -1;
        }

        public float GlobalTemperatureOffset => _globalTemperatureOffset;

        public void SetGlobalTemperatureOffset(float offset)
        {
            _globalTemperatureOffset = Mathf.Clamp(offset, -2f, 2f);
            _sampleCache.Clear();
        }

        private void DebugSetGlobalTemperatureOffset(float offset)
        {
            SetGlobalTemperatureOffset(offset);
            Debug.Log(
                $"Global temperature offset set to {_globalTemperatureOffset:0.###}.");
        }

        private async void DebugStartWeather(
            string weatherId,
            int regionX,
            int regionY)
        {
            bool started = await TryStartWeatherAsync(
                new Vector2Int(regionX, regionY),
                weatherId);
            if (!started)
            {
                Debug.LogWarning(
                    $"Unable to start unknown or invalid weather '{weatherId}'.");
            }
        }

        public void Tick()
        {
            long tick = _clock.CurrentTick;
            if (tick == _lastLiveTick)
                return;

            _lastLiveTick = tick;
            _sampleCache.Clear();
            foreach (KeyValuePair<Vector2Int, ActiveRegion> pair in _active)
            {
                ActiveRegion active = pair.Value;
                if (active.Region == null || active.LoadedChunkCount <= 0)
                    continue;

                AdvanceState(
                    pair.Key,
                    active.State,
                    active.State.LastWeatherTick,
                    tick,
                    emitEvents: true);
                WriteState(active.Region, active.State);
                _regions.MarkDirty(active.Region);
                EmitPulses(pair.Key, active.State, tick);
            }
        }

        public WeatherSample Sample(Vector2 worldPosition)
        {
            WeatherSample regional = GetRegionSample(
                WeatherRegionUtility.WorldToRegion(worldPosition));
            ClimateSnapshot localClimate =
                _climate.GetLocalSnapshot(worldPosition, regional.Climate);
            return ApplyLocalForcedWeather(
                worldPosition,
                BuildLocalSample(regional, localClimate));
        }

        public WeatherSample Sample(
            Vector2 worldPosition,
            float normalizedTerrainTemperature)
        {
            WeatherSample regional = GetRegionSample(
                WeatherRegionUtility.WorldToRegion(worldPosition));
            ClimateSnapshot localClimate = _climate.GetLocalSnapshot(
                worldPosition,
                normalizedTerrainTemperature,
                regional.Climate);
            return ApplyLocalForcedWeather(
                worldPosition,
                BuildLocalSample(regional, localClimate));
        }

        private static WeatherSample BuildLocalSample(
            WeatherSample regional,
            ClimateSnapshot localClimate)
        {
            float localAmbient =
                regional.AmbientTemperature +
                localClimate.Temperature -
                regional.Climate.Temperature;

            return new WeatherSample(
                regional.Region,
                localClimate,
                regional.WeatherId,
                regional.PhaseId,
                regional.Intensity,
                localAmbient,
                regional.AmbientColorTint,
                regional.PuddleAccumulation,
                regional.SnowAccumulation,
                regional.ActiveEffects,
                regional.HasWeatherOverride,
                regional.WeatherOverrideInfluence);
        }

        private WeatherSample ApplyLocalForcedWeather(
            Vector2 worldPosition,
            WeatherSample sample)
        {
            if (!_climateCores.TryGetPermanentWeather(
                    worldPosition,
                    _clock.CurrentTick,
                    out string weatherId,
                    out float influence))
            {
                if (_climateCores.TryGetPermanentWeather(
                        sample.Region,
                        _clock.CurrentTick,
                        out string touchingWeatherId,
                        out _) &&
                    IsWeatherAllowed(touchingWeatherId))
                {
                    // The core touches this region but not this exact point.
                    // Remove its region-level presentation so particle
                    // emission reaches zero at the radial outer boundary.
                    return new WeatherSample(
                        sample.Region,
                        sample.Climate,
                        touchingWeatherId,
                        string.Empty,
                        0f,
                        sample.Climate.Temperature,
                        Color.white,
                        sample.PuddleAccumulation,
                        sample.SnowAccumulation,
                        Array.Empty<WeatherEffectSample>(),
                        hasWeatherOverride: true,
                        weatherOverrideInfluence: 0f);
                }

                return sample;
            }

            WeatherData weather = GetWeather(weatherId);
            if (weather != null && !IsWeatherAllowed(weather.WeatherId))
                return sample;

            if (weather == null || weather.Phases.Count == 0)
            {
                // A configured core override remains authoritative even when
                // its weather definition is missing or invalid. Falling back
                // to the regional sample here allows regional lifecycle state
                // to visibly replace permanent core weather.
                return new WeatherSample(
                    sample.Region,
                    sample.Climate,
                    weatherId,
                    string.Empty,
                    influence,
                    sample.Climate.Temperature,
                    Color.white,
                    sample.PuddleAccumulation,
                    sample.SnowAccumulation,
                    Array.Empty<WeatherEffectSample>(),
                    hasWeatherOverride: true,
                    weatherOverrideInfluence: influence);
            }

            int phaseIndex = SelectForcedPhaseIndex(
                influence,
                weather.Phases.Count);
            WeatherPhaseData phase = weather.Phases[phaseIndex];
            if (phase == null)
                return sample;

            const float sustainedProgress = 0.5f;
            float spatialIntensity =
                phase.BaseIntensity * Mathf.Clamp01(influence);
            float ambient = sample.Climate.Temperature +
                            phase.TemperatureOffset * influence;
            WeatherEffectSample[] effects =
                new WeatherEffectSample[phase.Effects.Count];
            for (int i = 0; i < phase.Effects.Count; i++)
            {
                WeatherEffectData effect = phase.Effects[i];
                if (effect == null)
                    continue;

                float intensity = effect.IsFullScreenParticleEffect
                    // Forced fullscreen precipitation represents spatial
                    // strength: zero at the outer radius and the prefab's
                    // full authored emission at the inner radius.
                    ? influence
                    : phase.BaseIntensity *
                      effect.EvaluateIntensity(sustainedProgress) *
                      influence;
                ambient += effect.TemperatureOffset * intensity;
                effects[i] = new WeatherEffectSample(effect, intensity);
            }

            return new WeatherSample(
                sample.Region,
                sample.Climate,
                weather.WeatherId,
                phase.PhaseId,
                spatialIntensity,
                ambient,
                Color.Lerp(
                    Color.white,
                    phase.AmbientColorTint,
                    Mathf.Clamp01(influence)),
                sample.PuddleAccumulation,
                sample.SnowAccumulation,
                effects,
                hasWeatherOverride: true,
                weatherOverrideInfluence: influence);
        }

        public WeatherSample GetRegionSample(Vector2Int region)
        {
            long tick = _clock.CurrentTick;
            if (_sampleCache.TryGetValue(
                    region,
                    out CachedWeatherSample cached) &&
                cached.Tick == tick)
            {
                return cached.Sample;
            }

            ClimateSnapshot climate = ApplyGlobalTemperatureOffset(
                _climate.GetCurrentSnapshot(region));
            WeatherSample sample = BuildSample(region, climate, tick);
            _sampleCache[region] = new CachedWeatherSample(tick, sample);
            return sample;
        }

        public bool TryGetCachedRegionSample(
            Vector2Int region,
            out WeatherSample sample)
        {
            long tick = _clock.CurrentTick;
            if (_sampleCache.TryGetValue(
                    region,
                    out CachedWeatherSample cached) &&
                cached.Tick == tick)
            {
                sample = cached.Sample;
                return true;
            }

            if (!_climate.TryGetCurrentSnapshot(
                    region,
                    out ClimateSnapshot climate))
            {
                sample = default;
                return false;
            }

            climate = ApplyGlobalTemperatureOffset(climate);
            sample = BuildSample(region, climate, tick);
            _sampleCache[region] = new CachedWeatherSample(tick, sample);
            return true;
        }

        private WeatherSample BuildSample(
            Vector2Int region,
            ClimateSnapshot climate,
            long tick)
        {
            WeatherRegionState state;

            if (_active.TryGetValue(region, out ActiveRegion active) &&
                active.State != null)
            {
                state = active.State;
            }
            else
            {
                state = CreateInitialState(region, tick);
            }

            state = GetForcedSampleState(region, state, tick);

            WeatherData weather = IsWeatherAllowed(state.WeatherId)
                ? GetWeather(state.WeatherId)
                : null;
            WeatherPhaseData phase = GetPhase(weather, state.PhaseIndex);
            float phaseProgress = GetPhaseProgress(state, tick);
            float ambient = climate.Temperature;
            Color ambientColorTint = Color.white;
            WeatherEffectSample[] activeEffects =
                phase == null
                    ? Array.Empty<WeatherEffectSample>()
                    : new WeatherEffectSample[phase.Effects.Count];

            if (phase != null)
            {
                ambient += phase.TemperatureOffset;
                ambientColorTint = Color.Lerp(
                    Color.white,
                    phase.AmbientColorTint,
                    state.Intensity);
                for (int i = 0; i < phase.Effects.Count; i++)
                {
                    WeatherEffectData effect = phase.Effects[i];
                    if (effect != null)
                    {
                        float effectIntensity =
                            effect.EvaluateIntensity(phaseProgress) *
                            state.Intensity;
                        ambient += effect.TemperatureOffset * effectIntensity;
                        activeEffects[i] = new WeatherEffectSample(
                            effect,
                            effectIntensity);
                    }
                }
            }

            return new WeatherSample(
                region,
                climate,
                weather?.WeatherId,
                phase?.PhaseId,
                phase == null ? 0f : state.Intensity,
                ambient,
                ambientColorTint,
                state.PuddleAccumulation,
                state.SnowAccumulation,
                activeEffects);
        }

        public float GetAmbientTemperature(Vector2 worldPosition)
        {
            return Sample(worldPosition).AmbientTemperature;
        }

        public bool IsWeatherActive(Vector2 worldPosition, string weatherId)
        {
            if (string.IsNullOrWhiteSpace(weatherId))
                return false;

            return string.Equals(
                Sample(worldPosition).WeatherId,
                weatherId.Trim(),
                   StringComparison.Ordinal);
        }

        public bool DoesWeatherWaterPlants(Vector2 worldPosition)
        {
            WeatherSample sample = Sample(worldPosition);
            return sample.Intensity > 0f &&
                   GetWeather(sample.WeatherId)?.WatersPlants == true;
        }

        public async Awaitable<bool> TryStartWeatherAsync(
            Vector2Int region,
            string weatherId)
        {
            string normalizedId = weatherId?.Trim();
            WeatherData weather = GetWeather(normalizedId);
            if (weather == null ||
                weather.Phases.Count == 0 ||
                !IsWeatherAllowed(weather.WeatherId))
                return false;

            long tick = _clock.CurrentTick;
            RuntimeRegion runtimeRegion =
                await _regions.GetReadyAsync(region, tick);
            _active.TryGetValue(region, out ActiveRegion active);
            WeatherRegionState state = active?.State ??
                                       ReadState(runtimeRegion) ??
                                       CreateInitialState(region, tick);
            string previousWeatherId = state.WeatherId;
            bool emitEvents = active != null &&
                              active.LoadedChunkCount > 0;
            if (emitEvents)
                StopEffects(region, state, tick);

            state.WeatherId = weather.WeatherId;
            state.PhaseIndex = 0;
            state.CooldownEndTick = tick;
            state.LastWeatherTick = tick;
            BeginPhase(state, weather, tick);

            if (active != null)
            {
                active.Region = runtimeRegion;
                active.State = state;
            }

            WriteState(runtimeRegion, state);
            _regions.MarkDirty(runtimeRegion);
            _sampleCache.Remove(region);

            if (emitEvents)
            {
                NotifyTransition(region, previousWeatherId, state, tick);
                StartEffects(region, state, tick);
            }

            Debug.Log(
                $"Started weather '{weather.WeatherId}' in region {region}.");
            return true;
        }

        private ClimateSnapshot ApplyGlobalTemperatureOffset(
            ClimateSnapshot climate)
        {
            return new ClimateSnapshot(
                climate.Baseline,
                climate.Context,
                climate.Temperature + _globalTemperatureOffset,
                climate.Moisture,
                climate.WaterInfluence,
                climate.WeatherWeightMultiplier);
        }

        public long GetNextUpdateTick(
            long currentTick,
            OfflineSimulationPolicy policy)
        {
            return checked(currentTick + _settings.SimulationIntervalTicks);
        }

        public void Simulate(
            RuntimeRegion region,
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy)
        {
            if (policy == OfflineSimulationPolicy.None || toTick <= fromTick)
                return;

            WeatherRegionState state = ReadState(region) ??
                                       CreateInitialState(region.Position, fromTick);
            long stateFromTick = Math.Max(fromTick, state.LastWeatherTick);
            AdvanceState(
                region.Position,
                state,
                stateFromTick,
                toTick,
                emitEvents: false);
            WriteState(region, state);

            if (_active.TryGetValue(region.Position, out ActiveRegion active) &&
                ReferenceEquals(active.Region, region))
            {
                active.State = state;
            }
        }

        public IRegionSimulationWork Prepare(
            RuntimeRegion detachedRegion,
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy)
        {
            if (policy == OfflineSimulationPolicy.None || toTick <= fromTick)
                return null;

            // Do not depend on chunk-load callback or simulation-list ordering:
            // this detached region is the authoritative source for influences
            // that must participate in its offline weather catch-up.
            _climateCores.HydrateRegion(detachedRegion);

            WeatherRegionState state = ReadState(detachedRegion) ??
                                       CreateInitialState(
                                           detachedRegion.Position,
                                           fromTick);
            long stateFromTick = Math.Max(fromTick, state.LastWeatherTick);
            ClimateSnapshot climate = ApplyGlobalTemperatureOffset(
                _climate.GetCurrentSnapshot(detachedRegion.Position));
            List<KernelWeather> weather = new();
            Dictionary<string, KernelWeather> byId =
                new(StringComparer.Ordinal);

            foreach (WeatherData definition in _settings.Weather)
            {
                if (definition == null ||
                    definition.Phases.Count == 0 ||
                    !IsWeatherAllowed(definition.WeatherId))
                    continue;

                KernelWeather kernel = BakeWeather(
                    detachedRegion.Position,
                    definition,
                    climate);
                byId[kernel.Id] = kernel;
                if (kernel.SelectionWeight > 0f)
                    weather.Add(kernel);
            }

            string forcedWeatherId = string.Empty;
            long forcedWeatherStartTick = long.MaxValue;
            int forcedPhaseIndex = 0;
            if (_climateCores.TryGetPermanentWeather(
                    detachedRegion.Position,
                    toTick,
                    out string permanentWeatherId,
                    out float currentInfluence) &&
                byId.ContainsKey(permanentWeatherId))
            {
                forcedWeatherId = permanentWeatherId;
                forcedWeatherStartTick = FindForcedWeatherStartTick(
                    detachedRegion.Position,
                    stateFromTick,
                    toTick);
                forcedPhaseIndex = SelectForcedPhaseIndex(
                    currentInfluence,
                    byId[permanentWeatherId].Phases.Length);
            }

            return new WeatherSimulationWork(
                detachedRegion,
                state,
                byId,
                weather.ToArray(),
                stateFromTick,
                toTick,
                _settings.AccumulationMeltPerTick,
                forcedWeatherId,
                forcedWeatherStartTick,
                forcedPhaseIndex);
        }

        public static int SelectForcedPhaseIndex(
            float influence,
            int phaseCount)
        {
            if (phaseCount <= 1)
                return 0;

            return Mathf.Clamp(
                Mathf.FloorToInt(
                    Mathf.Clamp01(influence) * phaseCount),
                0,
                phaseCount - 1);
        }

        private long FindForcedWeatherStartTick(
            Vector2Int region,
            long fromTick,
            long toTick)
        {
            if (_climateCores.TryGetPermanentWeather(
                    region,
                    fromTick,
                    out _))
            {
                return fromTick;
            }

            long low = fromTick;
            long high = toTick;
            while (low + 1 < high)
            {
                long middle = low + (high - low) / 2;
                if (_climateCores.TryGetPermanentWeather(
                        region,
                        middle,
                        out _))
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }

            return high;
        }

        public void OnRegionSimulationApplied(RuntimeRegion region)
        {
            _sampleCache.Remove(region.Position);
            if (_active.TryGetValue(region.Position, out ActiveRegion active) &&
                ReferenceEquals(active.Region, region))
            {
                active.State = ReadState(region) ??
                               CreateInitialState(
                                   region.Position,
                                   region.LastSimulatedTick);
            }
        }

        private KernelWeather BakeWeather(
            Vector2Int region,
            WeatherData definition,
            ClimateSnapshot climate)
        {
            KernelPhase[] phases = new KernelPhase[definition.Phases.Count];
            for (int phaseIndex = 0;
                 phaseIndex < definition.Phases.Count;
                 phaseIndex++)
            {
                WeatherPhaseData phase = definition.Phases[phaseIndex];
                if (phase == null)
                {
                    phases[phaseIndex] = new KernelPhase
                    {
                        MinimumDurationTicks = 1,
                        MaximumDurationTicks = 1,
                        Effects = Array.Empty<KernelEffect>()
                    };
                    continue;
                }

                List<KernelEffect> effects = new();
                foreach (WeatherEffectData effect in phase.Effects)
                {
                    if (effect == null)
                        continue;

                    const int sampleCount = 33;
                    float[] intensity = new float[sampleCount];
                    for (int i = 0; i < sampleCount; i++)
                    {
                        intensity[i] =
                            effect.EvaluateIntensity((float)i /
                                                     (sampleCount - 1));
                    }

                    effects.Add(new KernelEffect
                    {
                        PuddlePerTick = effect.PuddleAccumulationPerTick,
                        SnowPerTick = effect.SnowAccumulationPerTick,
                        IntensitySamples = intensity
                    });
                }

                phases[phaseIndex] = new KernelPhase
                {
                    MinimumDurationTicks = phase.MinimumDurationTicks,
                    MaximumDurationTicks = phase.MaximumDurationTicks,
                    BaseIntensity = phase.BaseIntensity,
                    Effects = effects.ToArray()
                };
            }

            return new KernelWeather
            {
                Id = definition.WeatherId,
                CooldownTicks = definition.CooldownTicks,
                SelectionWeight =
                    definition.GetSelectionWeight(climate) *
                    GetNeighborMultiplier(region, definition.WeatherId),
                Phases = phases
            };
        }

        private async void OnChunkLoaded(Vector2Int chunkPosition, IChunk loaded)
        {
            Vector2Int regionPosition =
                WorldPartition.ChunkToRegion(chunkPosition);
            _sampleCache.Remove(regionPosition);
            if (!_active.TryGetValue(regionPosition, out ActiveRegion active))
            {
                active = new ActiveRegion();
                _active.Add(regionPosition, active);
            }

            active.LoadedChunkCount++;
            int version = ++active.LoadVersion;
            RuntimeRegion region = await _regions.GetReadyAsync(
                regionPosition,
                _clock.CurrentTick);

            if (!_active.TryGetValue(regionPosition, out active) ||
                active.LoadVersion != version &&
                active.Region != null)
            {
                return;
            }

            active.Region = region;
            bool shouldStartEffects = active.State == null;
            active.State = ReadState(region) ??
                           CreateInitialState(regionPosition, _clock.CurrentTick);
            AdvanceState(
                regionPosition,
                active.State,
                active.State.LastWeatherTick,
                _clock.CurrentTick,
                emitEvents: false);
            WriteState(region, active.State);
            if (shouldStartEffects && active.LoadedChunkCount > 0)
                StartEffects(regionPosition, active.State, _clock.CurrentTick);
        }

        private void OnChunkUnloaded(Vector2Int chunkPosition)
        {
            Vector2Int regionPosition =
                WorldPartition.ChunkToRegion(chunkPosition);
            _sampleCache.Remove(regionPosition);
            if (!_active.TryGetValue(regionPosition, out ActiveRegion active))
                return;

            active.LoadedChunkCount = Mathf.Max(0, active.LoadedChunkCount - 1);
            if (active.LoadedChunkCount > 0)
                return;

            StopEffects(regionPosition, active.State, _clock.CurrentTick);
            _active.Remove(regionPosition);
        }

        private WeatherRegionState CreateInitialState(
            Vector2Int region,
            long tick)
        {
            RegionalClimateBaseline baseline = _climate.GetBaseline(region);
            WeatherRegionState state = new()
            {
                LastWeatherTick = tick,
                RandomState = baseline.ClimateHash == 0
                    ? 0x9e3779b9u
                    : baseline.ClimateHash
            };
            SelectWeather(region, state, tick, string.Empty);
            return state;
        }

        private void AdvanceState(
            Vector2Int region,
            WeatherRegionState state,
            long fromTick,
            long toTick,
            bool emitEvents)
        {
            if (!IsWeatherAllowed(state.WeatherId))
            {
                string previousWeather = state.WeatherId;
                if (emitEvents)
                    StopEffects(region, state, fromTick);

                SelectWeather(region, state, fromTick, previousWeather);
                if (emitEvents)
                {
                    NotifyTransition(region, previousWeather, state, fromTick);
                    StartEffects(region, state, fromTick);
                }
            }

            if (toTick <= fromTick)
            {
                state.LastWeatherTick = toTick;
                return;
            }

            long cursor = fromTick;
            int transitionGuard = 0;
            while (cursor < toTick && transitionGuard++ < 1024)
            {
                WeatherRegionState previousForcedState =
                    emitEvents ? state.Clone() : null;
                bool forced = TryApplyForcedWeatherState(
                    region,
                    state,
                    toTick);
                if (forced &&
                    emitEvents &&
                    ForcedStateChanged(previousForcedState, state))
                {
                    StopEffects(region, previousForcedState, toTick);
                    NotifyTransition(
                        region,
                        previousForcedState.WeatherId,
                        state,
                        toTick);
                    StartEffects(region, state, toTick);
                }
                WeatherData weather = IsWeatherAllowed(state.WeatherId)
                    ? GetWeather(state.WeatherId)
                    : null;
                WeatherPhaseData phase = GetPhase(weather, state.PhaseIndex);

                if (phase == null)
                {
                    string previousWeather = state.WeatherId;
                    SelectWeather(region, state, cursor, previousWeather);
                    weather = GetWeather(state.WeatherId);
                    phase = GetPhase(weather, state.PhaseIndex);
                    if (phase == null)
                    {
                        state.LastWeatherTick = toTick;
                        return;
                    }

                    if (emitEvents)
                        NotifyTransition(region, previousWeather, state, cursor);
                }

                long segmentEnd = Math.Min(toTick, state.PhaseEndTick);
                long elapsed = Math.Max(0, segmentEnd - cursor);
                ApplyAccumulation(
                    state,
                    phase,
                    elapsed,
                    segmentEnd,
                    forced);
                cursor = segmentEnd;

                if (forced)
                    break;

                if (cursor < state.PhaseEndTick)
                    break;

                if (emitEvents)
                    StopEffects(region, state, cursor);

                if (state.PhaseIndex + 1 < weather.Phases.Count)
                {
                    state.PhaseIndex++;
                    BeginPhase(state, weather, cursor);
                    if (emitEvents)
                    {
                        NotifyTransition(region, weather.WeatherId, state, cursor);
                        StartEffects(region, state, cursor);
                    }
                }
                else
                {
                    string previousWeather = state.WeatherId;
                    state.CooldownEndTick = checked(
                        cursor + weather.CooldownTicks);
                    SelectWeather(region, state, cursor, previousWeather);
                    if (emitEvents)
                    {
                        NotifyTransition(region, previousWeather, state, cursor);
                        StartEffects(region, state, cursor);
                    }
                }
            }

            if (transitionGuard >= 1024)
            {
                Debug.LogError(
                    $"Weather transition guard reached for region {region}.");
            }

            state.LastWeatherTick = toTick;
            state.Intensity = GetPhase(
                    GetWeather(state.WeatherId),
                    state.PhaseIndex)?.BaseIntensity ?? 0f;
        }

        private static bool ForcedStateChanged(
            WeatherRegionState previous,
            WeatherRegionState current)
        {
            return previous != null &&
                   (!string.Equals(
                        previous.WeatherId,
                        current.WeatherId,
                        StringComparison.Ordinal) ||
                    previous.PhaseIndex != current.PhaseIndex);
        }

        private void ApplyAccumulation(
            WeatherRegionState state,
            WeatherPhaseData phase,
            long elapsedTicks,
            long atTick,
            bool forced = false)
        {
            float puddles = 0f;
            float snow = 0f;
            float progress = forced
                ? 0.5f
                : GetPhaseProgress(state, atTick);

            foreach (WeatherEffectData effect in phase.Effects)
            {
                if (effect == null)
                    continue;

                float intensity =
                    effect.EvaluateIntensity(progress) * phase.BaseIntensity;
                puddles += effect.PuddleAccumulationPerTick * intensity;
                snow += effect.SnowAccumulationPerTick * intensity;
            }

            state.PuddleAccumulation = Mathf.Clamp01(
                state.PuddleAccumulation +
                puddles * elapsedTicks -
                _settings.AccumulationMeltPerTick * elapsedTicks);
            state.SnowAccumulation = Mathf.Clamp01(
                state.SnowAccumulation +
                snow * elapsedTicks -
                _settings.AccumulationMeltPerTick * elapsedTicks);
        }

        private void SelectWeather(
            Vector2Int region,
            WeatherRegionState state,
            long tick,
            string previousWeatherId)
        {
            if (_climateCores.TryGetPermanentWeather(
                    region,
                    tick,
                    out string forcedWeatherId,
                    out float influence))
            {
                WeatherData forced = GetWeather(forcedWeatherId);
                if (forced != null &&
                    forced.Phases.Count > 0 &&
                    IsWeatherAllowed(forced.WeatherId))
                {
                    state.WeatherId = forced.WeatherId;
                    state.PhaseIndex = SelectForcedPhaseIndex(
                        influence,
                        forced.Phases.Count);
                    state.CooldownEndTick = tick;
                    state.PhaseStartTick = tick - 1;
                    state.PhaseEndTick = tick + 1;
                    state.Intensity =
                        forced.Phases[state.PhaseIndex].BaseIntensity;
                    return;
                }
            }

            ClimateSnapshot climate = ApplyGlobalTemperatureOffset(
                _climate.GetCurrentSnapshot(region));
            float total = 0f;
            List<(WeatherData weather, float weight)> candidates = new();

            foreach (WeatherData weather in _settings.Weather)
            {
                if (weather == null ||
                    weather.Phases.Count == 0 ||
                    !IsWeatherAllowed(weather.WeatherId))
                    continue;

                float weight = weather.GetSelectionWeight(climate);
                if (tick < state.CooldownEndTick &&
                    string.Equals(
                        weather.WeatherId,
                        previousWeatherId,
                        StringComparison.Ordinal))
                {
                    weight = 0f;
                }

                weight *= GetNeighborMultiplier(region, weather.WeatherId);
                if (weight <= 0f)
                    continue;

                candidates.Add((weather, weight));
                total += weight;
            }

            if (candidates.Count == 0)
            {
                state.WeatherId = string.Empty;
                state.PhaseIndex = -1;
                state.PhaseStartTick = tick;
                state.PhaseEndTick = long.MaxValue;
                state.Intensity = 0f;
                return;
            }

            float roll = NextFloat(ref state.RandomState) * total;
            WeatherData selected = candidates[candidates.Count - 1].weather;
            foreach ((WeatherData weather, float weight) in candidates)
            {
                roll -= weight;
                if (roll <= 0f)
                {
                    selected = weather;
                    break;
                }
            }

            state.WeatherId = selected.WeatherId;
            state.PhaseIndex = 0;
            BeginPhase(state, selected, tick);
        }

        private WeatherRegionState GetForcedSampleState(
            Vector2Int region,
            WeatherRegionState state,
            long tick)
        {
            if (!_climateCores.TryGetPermanentWeather(
                    region,
                    tick,
                    out string weatherId,
                    out float influence))
            {
                return state;
            }

            WeatherData weather = GetWeather(weatherId);
            if (weather == null ||
                weather.Phases.Count == 0 ||
                !IsWeatherAllowed(weather.WeatherId))
                return state;
            WeatherRegionState forced = state.Clone();
            forced.WeatherId = weather.WeatherId;
            forced.PhaseIndex = SelectForcedPhaseIndex(
                influence,
                weather.Phases.Count);
            // A forced phase is spatially stable rather than progressing
            // through its normal lifecycle. Center it at 50% so phase effect
            // curves represent their sustained intensity.
            forced.PhaseStartTick = tick - 1;
            forced.PhaseEndTick = tick + 1;
            forced.Intensity =
                weather.Phases[forced.PhaseIndex]?.BaseIntensity ?? 0f;
            return forced;
        }

        private bool TryApplyForcedWeatherState(
            Vector2Int region,
            WeatherRegionState state,
            long tick)
        {
            if (!_climateCores.TryGetPermanentWeather(
                    region,
                    tick,
                    out string weatherId,
                    out float influence))
            {
                return false;
            }

            WeatherData weather = GetWeather(weatherId);
            if (weather == null ||
                weather.Phases.Count == 0 ||
                !IsWeatherAllowed(weather.WeatherId))
                return false;

            state.WeatherId = weather.WeatherId;
            state.PhaseIndex = SelectForcedPhaseIndex(
                influence,
                weather.Phases.Count);
            state.PhaseStartTick = tick - 1;
            state.PhaseEndTick = tick + 1;
            state.CooldownEndTick = tick;
            state.Intensity =
                weather.Phases[state.PhaseIndex]?.BaseIntensity ?? 0f;
            return true;
        }

        private float GetNeighborMultiplier(
            Vector2Int region,
            string weatherId)
        {
            if (_settings.NeighborWeatherInfluence <= 0f)
                return 1f;

            int matches = 0;
            Vector2Int[] offsets =
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.up,
                Vector2Int.down
            };

            foreach (Vector2Int offset in offsets)
            {
                Vector2Int neighborRegion = region + offset;
                string neighborWeatherId;
                if (_active.TryGetValue(neighborRegion, out ActiveRegion neighbor) &&
                    neighbor.State != null)
                {
                    neighborWeatherId = neighbor.State.WeatherId;
                }
                else
                    continue;

                if (string.Equals(
                        neighborWeatherId,
                        weatherId,
                        StringComparison.Ordinal))
                {
                    matches++;
                }
            }

            return 1f + matches * _settings.NeighborWeatherInfluence;
        }

        private static void BeginPhase(
            WeatherRegionState state,
            WeatherData weather,
            long tick)
        {
            WeatherPhaseData phase = GetPhase(weather, state.PhaseIndex);
            if (phase == null)
            {
                state.PhaseEndTick = tick;
                return;
            }

            int duration = NextInt(
                ref state.RandomState,
                phase.MinimumDurationTicks,
                phase.MaximumDurationTicks + 1);
            state.PhaseStartTick = tick;
            state.PhaseEndTick = checked(tick + duration);
            state.Intensity = phase.BaseIntensity;
        }

        private void EmitPulses(
            Vector2Int region,
            WeatherRegionState state,
            long tick)
        {
            WeatherData weather = GetWeather(state.WeatherId);
            WeatherPhaseData phase = GetPhase(weather, state.PhaseIndex);
            if (phase == null)
                return;

            float phaseProgress = GetPhaseProgress(state, tick);
            Rect bounds = GetRegionBounds(region);
            foreach (WeatherEffectData effect in phase.Effects)
            {
                if (effect == null ||
                    (tick - state.PhaseStartTick) % effect.PulseIntervalTicks != 0)
                {
                    continue;
                }

                float intensity =
                    phase.BaseIntensity * effect.EvaluateIntensity(phaseProgress);
                uint pulseState = Hash(
                    state.RandomState,
                    region.x,
                    region.y,
                    unchecked((int)tick ^ (int)StableStringHash(effect.EffectId)));
                if (NextFloat(ref pulseState) > effect.SpawnChancePerPulse)
                    continue;

                Vector2 position = new(
                    Mathf.Lerp(bounds.xMin, bounds.xMax, NextFloat(ref pulseState)),
                    Mathf.Lerp(bounds.yMin, bounds.yMax, NextFloat(ref pulseState)));
                _weatherBus.RaiseEffect(new WeatherEffectEvent(
                    WeatherEffectEventType.Pulse,
                    region,
                    bounds,
                    weather.WeatherId,
                    phase.PhaseId,
                    effect,
                    intensity,
                    tick,
                    position));
            }
        }

        private void StartEffects(
            Vector2Int region,
            WeatherRegionState state,
            long tick)
        {
            RaiseLifecycleEvents(
                WeatherEffectEventType.Started,
                region,
                state,
                tick);
        }

        private void StopEffects(
            Vector2Int region,
            WeatherRegionState state,
            long tick)
        {
            if (state == null)
                return;

            RaiseLifecycleEvents(
                WeatherEffectEventType.Stopped,
                region,
                state,
                tick);
        }

        private void RaiseLifecycleEvents(
            WeatherEffectEventType eventType,
            Vector2Int region,
            WeatherRegionState state,
            long tick)
        {
            WeatherData weather = GetWeather(state.WeatherId);
            WeatherPhaseData phase = GetPhase(weather, state.PhaseIndex);
            if (phase == null)
                return;

            Rect bounds = GetRegionBounds(region);
            float phaseProgress = GetPhaseProgress(state, tick);
            foreach (WeatherEffectData effect in phase.Effects)
            {
                if (effect == null)
                    continue;

                float intensity = phase.BaseIntensity *
                                  effect.EvaluateIntensity(phaseProgress);
                _weatherBus.RaiseEffect(new WeatherEffectEvent(
                    eventType,
                    region,
                    bounds,
                    weather.WeatherId,
                    phase.PhaseId,
                    effect,
                    intensity,
                    tick));
            }
        }

        private void NotifyTransition(
            Vector2Int region,
            string previousWeatherId,
            WeatherRegionState state,
            long tick)
        {
            WeatherData weather = GetWeather(state.WeatherId);
            WeatherPhaseData phase = GetPhase(weather, state.PhaseIndex);
            _weatherBus.RaiseRegionChanged(new WeatherRegionChangedEvent(
                region,
                previousWeatherId,
                weather?.WeatherId,
                phase?.PhaseId,
                tick));
        }

        private WeatherData GetWeather(string weatherId)
        {
            if (string.IsNullOrEmpty(weatherId))
                return null;

            _weatherById.TryGetValue(weatherId, out WeatherData weather);
            return weather;
        }

        private bool IsWeatherAllowed(string weatherId)
        {
            PlaneData plane = _planeSelection?.Plane;
            return plane == null || plane.AllowsWeather(weatherId);
        }

        private static WeatherPhaseData GetPhase(
            WeatherData weather,
            int phaseIndex)
        {
            return weather != null &&
                   phaseIndex >= 0 &&
                   phaseIndex < weather.Phases.Count
                ? weather.Phases[phaseIndex]
                : null;
        }

        private static float GetPhaseProgress(
            WeatherRegionState state,
            long tick)
        {
            long duration = state.PhaseEndTick - state.PhaseStartTick;
            if (duration <= 0 || state.PhaseEndTick == long.MaxValue)
                return 0f;

            return Clamp01(
                (float)(tick - state.PhaseStartTick) / duration);
        }

        private static Rect GetRegionBounds(Vector2Int region)
        {
            float size = WorldPartition.RegionSizeInChunks *
                         ChunkBuildResult.ChunkSize;
            return new Rect(region.x * size, region.y * size, size, size);
        }

        private static uint Next(ref uint state)
        {
            if (state == 0)
                state = 0x9e3779b9u;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        private static float Clamp01(float value)
        {
            if (value <= 0f)
                return 0f;
            return value >= 1f ? 1f : value;
        }

        private static float NextFloat(ref uint state)
        {
            return (Next(ref state) & 0x00ffffffu) / 16777216f;
        }

        private static int NextInt(ref uint state, int minimum, int maximum)
        {
            if (maximum <= minimum)
                return minimum;
            return minimum + (int)(NextFloat(ref state) * (maximum - minimum));
        }

        private static uint Hash(uint seed, int a, int b, int c)
        {
            uint hash = seed ^ 2166136261u;
            hash = (hash ^ unchecked((uint)a)) * 16777619u;
            hash = (hash ^ unchecked((uint)b)) * 16777619u;
            hash = (hash ^ unchecked((uint)c)) * 16777619u;
            return hash == 0 ? 0x9e3779b9u : hash;
        }

        private static uint StableStringHash(string value)
        {
            uint hash = 2166136261u;
            if (value == null)
                return hash;

            foreach (char character in value)
                hash = (hash ^ character) * 16777619u;
            return hash;
        }

        private static WeatherRegionState ReadState(RuntimeRegion region)
        {
            if (!region.Components.TryGetValue(
                    WeatherComponentTypeId,
                    out RegionComponentRecord component) ||
                component == null ||
                component.data == null ||
                component.data.Length == 0)
            {
                return null;
            }

            try
            {
                using MemoryStream stream = new(component.data, writable: false);
                using BinaryReader reader = new(stream);
                WeatherRegionState state = new()
                {
                    WeatherId = reader.ReadString(),
                    PhaseIndex = reader.ReadInt32(),
                    PhaseStartTick = reader.ReadInt64(),
                    PhaseEndTick = reader.ReadInt64(),
                    LastWeatherTick = reader.ReadInt64(),
                    CooldownEndTick = reader.ReadInt64(),
                    RandomState = reader.ReadUInt32(),
                    Intensity = reader.ReadSingle(),
                    PuddleAccumulation = reader.ReadSingle(),
                    SnowAccumulation = reader.ReadSingle()
                };
                return state;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Ignoring invalid weather state for region {region.Position}: " +
                    exception.Message);
                return null;
            }
        }

        private static void WriteState(
            RuntimeRegion region,
            WeatherRegionState state)
        {
            using MemoryStream stream = new();
            using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(state.WeatherId ?? string.Empty);
                writer.Write(state.PhaseIndex);
                writer.Write(state.PhaseStartTick);
                writer.Write(state.PhaseEndTick);
                writer.Write(state.LastWeatherTick);
                writer.Write(state.CooldownEndTick);
                writer.Write(state.RandomState);
                writer.Write(state.Intensity);
                writer.Write(state.PuddleAccumulation);
                writer.Write(state.SnowAccumulation);
            }

            region.SetComponent(new RegionComponentRecord
            {
                typeId = WeatherComponentTypeId,
                version = WeatherComponentVersion,
                data = stream.ToArray(),
                isAtBaseline = false
            });
        }
    }
}
