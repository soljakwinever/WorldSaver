using System;
using System.Collections.Generic;
using System.IO;
using IngameDebugConsole;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.TimeAndWeather;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public sealed class EventService :
        IEventService,
        IInitializable,
        ITickable,
        IDisposable
    {
        private const uint SaveMagic = 0x45535757; // WWSE
        private const ushort SaveVersion = 1;
        private sealed class RuntimeState
        {
            public bool Active;
            public bool Finished;
            public long ActivatedDay;
            public readonly Dictionary<EnemyData, int> DefeatBaseline = new();
            public int AllDefeatsBaseline;
            public readonly Dictionary<ItemCollectedEventCondition, int>
                ItemBaselines = new();
            public readonly Dictionary<RandomDurationEventCondition, long>
                DurationDeadlines = new();
            public readonly Dictionary<string, int> Variables =
                new(StringComparer.Ordinal);
        }

        private readonly WorldData _world;
        private readonly ITimeController _time;
        private readonly IRegionalWeatherService _weather;
        private readonly FeatureBuildingService _buildings;
        private readonly Chunkloader _chunkloader;
        private readonly PlayerDataController _player;
        private readonly EntityBus _entities;
        private readonly IAudioService _audio;
        private readonly IDangerService _danger;
        private readonly Dictionary<EventData, RuntimeState> _states = new();
        private readonly Dictionary<EnemyData, int> _defeats = new();
        private readonly HashSet<EnemySpawnRule> _activeRules = new();
        private readonly HashSet<EnemySpawnRule> _disabledRules = new();
        private readonly Dictionary<EventData, List<GameObject>> _eventPortals = new();
        private int _allDefeats;
        private float _baseTemperature;
        private bool _saveDirty;
        private float _nextSaveTime;
        private long _lastHourlyEvaluation = long.MinValue;

        public IReadOnlyCollection<EnemySpawnRule> ActiveEnemySpawnRules =>
            _activeRules;
        public IReadOnlyCollection<EnemySpawnRule> DisabledEnemySpawnRules =>
            _disabledRules;

        public EventService(
            WorldData world,
            ITimeController time,
            IRegionalWeatherService weather,
            FeatureBuildingService buildings,
            Chunkloader chunkloader,
            PlayerDataController player,
            EntityBus entities,
            IAudioService audio,
            IDangerService danger)
        {
            _world = world;
            _time = time;
            _weather = weather;
            _buildings = buildings;
            _chunkloader = chunkloader;
            _player = player;
            _entities = entities;
            _audio = audio;
            _danger = danger;
        }

        public void Initialize()
        {
            _baseTemperature = _weather.GlobalTemperatureOffset;
            _entities.EnemyDefeated += OnEnemyDefeated;
            _entities.EntityRemoved += OnEntityRemoved;
            foreach (EventData data in _world.events ?? Array.Empty<EventData>())
                if (data != null && !_states.ContainsKey(data))
                {
                    RuntimeState state = new();
                    foreach (EventVariable variable in
                             data.blackboard ?? new List<EventVariable>())
                    {
                        string name = variable?.name?.Trim();
                        if (!string.IsNullOrEmpty(name))
                            state.Variables[name] = variable.initialValue;
                    }
                    _states.Add(data, state);
                }
            if (_world.startingEvent != null &&
                !_states.ContainsKey(_world.startingEvent))
            {
                RuntimeState state = new();
                InitializeVariables(_world.startingEvent, state);
                _states.Add(_world.startingEvent, state);
            }

            bool restored = LoadState();
            RebuildEffects();
            foreach (KeyValuePair<EventData, RuntimeState> pair in _states)
                if (pair.Value.Active)
                    SetEventMusic(pair.Key);
            if (!restored &&
                _world.startingEvent != null &&
                _states.TryGetValue(
                    _world.startingEvent,
                    out RuntimeState startingState))
            {
                Activate(_world.startingEvent, startingState);
            }

            DebugLogConsole.AddCommand(
                "event.current",
                "Lists all currently active events.",
                DebugCurrentEvents);
            DebugLogConsole.AddCommand<string>(
                "event.activate",
                "Force-activates an event by persistent ID or asset name.",
                DebugActivateEvent,
                "eventName");
        }

        public void Dispose()
        {
            _entities.EnemyDefeated -= OnEnemyDefeated;
            _entities.EntityRemoved -= OnEntityRemoved;
            SaveState();
            _weather.SetGlobalTemperatureOffset(_baseTemperature);
            DebugLogConsole.RemoveCommand(DebugCurrentEvents);
            DebugLogConsole.RemoveCommand<string>(DebugActivateEvent);
            foreach (List<GameObject> portals in _eventPortals.Values)
                foreach (GameObject portal in portals)
                    if (portal != null)
                        UnityEngine.Object.Destroy(portal);
            _eventPortals.Clear();
            foreach (EventData data in _states.Keys)
                _danger.ClearEventContribution(GetDangerOwner(data));
        }

        public void Tick()
        {
            long hour = GetAbsoluteDay() * 24L +
                        Mathf.Clamp(_time.Hour, 0, 23);
            bool evaluateHourlyConditions = hour != _lastHourlyEvaluation;
            if (evaluateHourlyConditions)
                _lastHourlyEvaluation = hour;

            foreach (KeyValuePair<EventData, RuntimeState> pair in _states)
            {
                EventData data = pair.Key;
                RuntimeState state = pair.Value;
                if (!state.Active && !state.Finished &&
                    ShouldEvaluate(
                        data.activationCondition,
                        evaluateHourlyConditions) &&
                    Evaluate(data.activationCondition, data, state))
                {
                    Activate(data, state);
                }

                if (state.Active && data.endCondition != null &&
                    ShouldEvaluate(
                        data.endCondition,
                        evaluateHourlyConditions) &&
                    Evaluate(data.endCondition, data, state))
                {
                    Finish(data, state);
                }
            }

            if (_saveDirty && Time.unscaledTime >= _nextSaveTime)
                SaveState();
        }

        public bool IsActive(string eventId) =>
            TryGetState(eventId, out RuntimeState state) && state.Active;

        public bool IsFinished(string eventId) =>
            TryGetState(eventId, out RuntimeState state) && state.Finished;

        public bool TryGetVariable(
            string eventId,
            string variable,
            out int value)
        {
            value = default;
            return TryGetState(eventId, out RuntimeState state) &&
                   state.Variables.TryGetValue(
                       variable?.Trim() ?? string.Empty,
                       out value);
        }

        public bool TrySetVariable(
            string eventId,
            string variable,
            int value)
        {
            if (!TryGetState(eventId, out RuntimeState state))
                return false;
            string name = variable?.Trim();
            if (string.IsNullOrEmpty(name) ||
                !state.Variables.ContainsKey(name))
                return false;
            state.Variables[name] = value;
            if (state.Active)
                RebuildEffects();
            MarkSaveDirty();
            return true;
        }

        public bool TryAddVariable(
            string eventId,
            string variable,
            int amount)
        {
            if (!TryGetVariable(eventId, variable, out int value))
                return false;
            return TrySetVariable(
                eventId,
                variable,
                SaturatingAdd(value, amount));
        }

        private void DebugCurrentEvents()
        {
            List<string> active = new();
            foreach (KeyValuePair<EventData, RuntimeState> pair in _states)
            {
                if (!pair.Value.Active)
                    continue;

                string id = string.IsNullOrWhiteSpace(pair.Key.persistentId)
                    ? pair.Key.name
                    : pair.Key.persistentId;
                active.Add(
                    string.Equals(
                        id,
                        pair.Key.name,
                        StringComparison.Ordinal)
                        ? id
                        : $"{pair.Key.name} ({id})");
            }

            if (active.Count == 0)
            {
                Debug.Log("No events are currently active.");
                return;
            }

            active.Sort(StringComparer.OrdinalIgnoreCase);
            Debug.Log(
                $"Active events ({active.Count}): {string.Join(", ", active)}");
        }

        private void DebugActivateEvent(string eventName)
        {
            string normalized = eventName?.Trim();
            EventData match = null;
            RuntimeState state = null;
            foreach (KeyValuePair<EventData, RuntimeState> pair in _states)
            {
                if (!string.Equals(
                        pair.Key.persistentId,
                        normalized,
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        pair.Key.name,
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                match = pair.Key;
                state = pair.Value;
                break;
            }

            if (match == null)
            {
                Debug.LogWarning(
                    $"No configured event named '{eventName}' was found.");
                return;
            }
            if (state.Active)
            {
                Debug.Log($"Event '{match.name}' is already active.");
                return;
            }

            state.Finished = false;
            state.DurationDeadlines.Clear();
            state.ItemBaselines.Clear();
            state.DefeatBaseline.Clear();
            Activate(match, state);
        }

        private bool TryGetState(string eventId, out RuntimeState state)
        {
            string id = eventId?.Trim();
            foreach (KeyValuePair<EventData, RuntimeState> pair in _states)
            {
                if (string.Equals(
                        pair.Key.persistentId,
                        id,
                        StringComparison.Ordinal))
                {
                    state = pair.Value;
                    return true;
                }
            }

            state = null;
            return false;
        }

        private bool Evaluate(
            EventCondition condition,
            EventData owner,
            RuntimeState state)
        {
            switch (condition)
            {
                case null:
                    return false;
                case AllEventCondition all:
                    if (all.conditions == null)
                        return true;
                    foreach (EventCondition child in all.conditions)
                        if (!Evaluate(child, owner, state))
                            return false;
                    return true;
                case AnyEventCondition any:
                    if (any.conditions == null)
                        return false;
                    foreach (EventCondition child in any.conditions)
                        if (Evaluate(child, owner, state))
                            return true;
                    return false;
                case NotEventCondition not:
                    return !Evaluate(not.condition, owner, state);
                case CalendarEventCondition calendar:
                    return MatchesCalendar(
                        calendar,
                        GetCalendarValue(calendar.unit));
                case TimeOfDayEventCondition timeOfDay:
                    return timeOfDay.AllowsHour(_time.Hour);
                case WeatherEventCondition weather:
                    return weather.weather != null &&
                           _chunkloader.track != null &&
                           _weather.IsWeatherActive(
                               _chunkloader.track.position,
                               weather.weather.WeatherId);
                case RandomChanceEventCondition chance:
                    return DailyRoll(owner.persistentId) <
                           Mathf.Clamp01(chance.chance);
                case EventVariableCondition variable:
                    return EvaluateVariable(variable, owner);
                case OtherEventCondition other:
                    if (other.eventData == null ||
                        !_states.TryGetValue(other.eventData, out RuntimeState otherState))
                        return false;
                    return other.requiredState == EventStateRequirement.Active
                        ? otherState.Active
                        : otherState.Finished;
                case EnemiesDefeatedEventCondition defeated:
                    int count = defeated.enemy == null
                        ? _allDefeats
                        : GetDefeats(defeated.enemy);
                    if (defeated.sinceEventActivated)
                        count -= defeated.enemy == null
                            ? state.AllDefeatsBaseline
                            : GetBaseline(state, defeated.enemy);
                    return count >= Mathf.Max(1, defeated.amount);
                case RandomDurationEventCondition duration:
                    if (!state.Active)
                        return false;
                    if (!state.DurationDeadlines.TryGetValue(
                            duration,
                            out long deadline))
                    {
                        int minimum = Mathf.Max(1, duration.minimum);
                        int maximum = Mathf.Max(minimum, duration.maximum);
                        int length = DeterministicRange(
                            owner.persistentId + ":duration",
                            minimum,
                            maximum);
                        deadline = state.ActivatedDay +
                                   ToDays(length, duration.unit);
                        state.DurationDeadlines.Add(duration, deadline);
                    }
                    return GetAbsoluteDay() >= deadline;
                case ItemCollectedEventCondition item:
                    if (item.item == null)
                        return false;
                    int itemCount = GetItemCount(item);
                    if (item.sinceEventActivated)
                    {
                        if (!state.ItemBaselines.TryGetValue(item, out int baseline))
                        {
                            baseline = itemCount;
                            state.ItemBaselines.Add(item, baseline);
                        }
                        itemCount -= baseline;
                    }
                    return itemCount >= Mathf.Max(1, item.amount);
                default:
                    return false;
            }
        }

        private static bool ShouldEvaluate(
            EventCondition condition,
            bool evaluateHourlyConditions) =>
            !ContainsTimeOfDayCondition(condition) ||
            evaluateHourlyConditions;

        private static bool MatchesCalendar(
            CalendarEventCondition condition,
            long value)
        {
            int every = Mathf.Max(1, condition.every);
            return value >= condition.offset &&
                   (value - condition.offset) % every == 0;
        }

        private static bool ContainsTimeOfDayCondition(
            EventCondition condition)
        {
            switch (condition)
            {
                case TimeOfDayEventCondition:
                    return true;
                case AllEventCondition all when all.conditions != null:
                    foreach (EventCondition child in all.conditions)
                        if (ContainsTimeOfDayCondition(child))
                            return true;
                    return false;
                case AnyEventCondition any when any.conditions != null:
                    foreach (EventCondition child in any.conditions)
                        if (ContainsTimeOfDayCondition(child))
                            return true;
                    return false;
                case NotEventCondition not:
                    return ContainsTimeOfDayCondition(not.condition);
                default:
                    return false;
            }
        }

        private void Activate(EventData data, RuntimeState state)
        {
            state.Active = true;
            Debug.Log(
                $"Event '{data.DisplayName}' started " +
                $"(ID: '{data.persistentId}').");
            state.ActivatedDay = GetAbsoluteDay();
            state.AllDefeatsBaseline = _allDefeats;
            foreach (KeyValuePair<EnemyData, int> pair in _defeats)
                state.DefeatBaseline[pair.Key] = pair.Value;
            CaptureActivationBaselines(data.endCondition, state);
            SetEventMusic(data);
            RebuildEffects();
            ApplyActivationEffectsAsync(data);
            MarkSaveDirty();
        }

        private void Finish(EventData data, RuntimeState state)
        {
            state.Active = false;
            state.Finished = true;
            _audio.ClearCurrentBgm(GetMusicOwner(data));
            RebuildEffects();
            MarkSaveDirty();
            DisablePortals(data);
        }

        private void SetEventMusic(EventData data)
        {
            _audio.SetCurrentBgm(
                GetMusicOwner(data),
                data.musicOverride,
                MusicPriority.Event);
        }

        private static string GetMusicOwner(EventData data)
        {
            string id = data.persistentId?.Trim();
            return "event:" + (string.IsNullOrEmpty(id)
                ? data.name
                : id);
        }

        private static string GetDangerOwner(EventData data) =>
            GetMusicOwner(data) + ":danger";

        private void RebuildEffects()
        {
            _activeRules.Clear();
            _disabledRules.Clear();
            float temperature = _baseTemperature;
            foreach (KeyValuePair<EventData, RuntimeState> pair in _states)
            {
                _danger.ClearEventContribution(GetDangerOwner(pair.Key));
                if (!pair.Value.Active)
                    continue;
                EventEffects effects = pair.Key.effects;
                if (effects == null)
                    continue;
                temperature += effects.globalTemperatureOffset;
                float eventDanger = 0f;
                foreach (EventDangerEffect danger in
                         effects.danger ?? Array.Empty<EventDangerEffect>())
                {
                    string variable = danger?.variable?.Trim();
                    if (!string.IsNullOrEmpty(variable) &&
                        pair.Value.Variables.TryGetValue(variable, out int value))
                        eventDanger += danger.Evaluate(value);
                }
                _danger.SetEventContribution(
                    GetDangerOwner(pair.Key),
                    Mathf.Clamp01(eventDanger));
                foreach (EnemySpawnRule rule in
                         effects.enabledEnemySpawnRules ??
                         Array.Empty<EnemySpawnRule>())
                    if (rule != null)
                        _activeRules.Add(rule);
                foreach (EnemySpawnRule rule in
                         effects.disabledEnemySpawnRules ??
                         Array.Empty<EnemySpawnRule>())
                    if (rule != null)
                        _disabledRules.Add(rule);
            }
            _activeRules.ExceptWith(_disabledRules);
            _weather.SetGlobalTemperatureOffset(temperature);
        }

        private async void ApplyActivationEffectsAsync(EventData data)
        {
            try
            {
                EventEffects effects = data.effects;
                if (effects == null || _chunkloader.track == null)
                    return;
                Vector2Int chunk = WorldPartition.WorldToChunk(
                    _chunkloader.track.position);
                Vector2Int region = WorldPartition.ChunkToRegion(chunk);
                if (effects.weather != null)
                    await _weather.TryStartWeatherAsync(
                        region,
                        effects.weather.WeatherId);
                CreatePortals(data, effects);
                foreach (EventFeatureBuildingEffect feature in
                         GetFeatureBuildingEffects(effects))
                {
                    if (feature.building == null)
                        continue;
                    if (feature.spawnMode == EventFeatureSpawnMode.NaturalTerrain)
                    {
                        bool configured = false;
                        foreach (FeatureBuildingData candidate in
                                 _buildings.NaturallyGeneratedBuildings)
                        {
                            if (candidate == feature.building)
                            {
                                configured = true;
                                break;
                            }
                        }
                        if (!configured)
                        {
                            Debug.LogWarning(
                                $"Event feature '{feature.building.name}' is set to natural terrain spawning but is not in the active feature preset.");
                        }
                        continue;
                    }

                    await SpawnFeatureBuildingsAsync(
                        feature.building,
                        Mathf.Max(1, feature.count),
                        Mathf.Max(0, feature.excludedRegionRadiusFromPlayer),
                        region);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void CreatePortals(EventData owner, EventEffects effects)
        {
            DisablePortals(owner);
            List<GameObject> instances = new();
            foreach (EventPortalEffect portal in
                     effects.portals ?? Array.Empty<EventPortalEffect>())
            {
                if (portal == null)
                    continue;
                GameObject prefab = portal.prefab != null
                    ? portal.prefab
                    : Resources.Load<GameObject>("Portal");
                if (prefab == null)
                {
                    Debug.LogWarning($"Event '{owner.name}' could not load a Portal prefab.");
                    continue;
                }

                Vector3 position = portal.spawnRelativeToPlayer
                    ? _chunkloader.track.position + (Vector3)portal.entranceOffset
                    : (Vector3)portal.entranceWorldPosition;
                GameObject instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
                FastTravelPortal controller = instance.GetComponent<FastTravelPortal>() ??
                                              instance.AddComponent<FastTravelPortal>();
                controller.Initialize(portal, _chunkloader.track, _chunkloader);
                instances.Add(instance);
            }
            if (instances.Count > 0)
                _eventPortals[owner] = instances;
        }

        private void DisablePortals(EventData owner)
        {
            if (!_eventPortals.Remove(owner, out List<GameObject> portals))
                return;
            foreach (GameObject portal in portals)
                if (portal != null)
                    portal.SetActive(false);
        }

        private async Awaitable SpawnFeatureBuildingsAsync(
            FeatureBuildingData building,
            int count,
            int excludedRadius,
            Vector2Int origin)
        {
            int spawned = 0;
            int radius = excludedRadius + 1;
            int maximumRadius =
                radius + Mathf.Max(
                    2,
                    Mathf.CeilToInt(Mathf.Sqrt(count)) + 2);
            while (spawned < count && radius <= maximumRadius)
            {
                foreach (Vector2Int region in EnumerateRegionRing(origin, radius))
                {
                    if (await _buildings.TrySpawnAsync(
                            building.persistentId,
                            region))
                    {
                        spawned++;
                        if (spawned >= count)
                            break;
                    }
                }
                radius++;
            }

            if (spawned < count)
            {
                Debug.LogWarning(
                    $"Event requested {count} '{building.persistentId}' feature buildings, but only {spawned} could be placed near region {origin}.");
            }
        }

        private static IEnumerable<Vector2Int> EnumerateRegionRing(
            Vector2Int origin,
            int radius)
        {
            if (radius == 0)
            {
                yield return origin;
                yield break;
            }

            for (int x = -radius; x <= radius; x++)
            {
                yield return origin + new Vector2Int(x, -radius);
                yield return origin + new Vector2Int(x, radius);
            }
            for (int y = -radius + 1; y < radius; y++)
            {
                yield return origin + new Vector2Int(-radius, y);
                yield return origin + new Vector2Int(radius, y);
            }
        }

        private static IEnumerable<EventFeatureBuildingEffect>
            GetFeatureBuildingEffects(EventEffects effects)
        {
            if (effects.featureBuildings != null)
            {
                foreach (EventFeatureBuildingEffect feature in
                         effects.featureBuildings)
                    if (feature != null)
                        yield return feature;
            }

            foreach (FeatureBuildingData legacy in
                     effects.buildings ?? Array.Empty<FeatureBuildingData>())
            {
                if (legacy != null)
                {
                    yield return new EventFeatureBuildingEffect
                    {
                        building = legacy,
                        count = 1,
                        excludedRegionRadiusFromPlayer = 0,
                        spawnMode = EventFeatureSpawnMode.SpawnOnActivation
                    };
                }
            }
        }

        private void OnEnemyDefeated(
            EnemyData enemy,
            Vector3 _,
            int __,
            GameObject ___)
        {
            _allDefeats++;
            if (enemy != null)
                _defeats[enemy] = GetDefeats(enemy) + 1;
            MarkSaveDirty();
        }

        private void OnEntityRemoved(
            NodeData nodeData,
            NodeId _,
            Vector3 __)
        {
            if (nodeData == null)
                return;

            foreach (KeyValuePair<EventData, RuntimeState> pair in _states)
            {
                foreach (NodeDestroyedVariableChange change in
                         pair.Key.nodeDestroyedChanges ??
                         new List<NodeDestroyedVariableChange>())
                {
                    if (change == null ||
                        change.nodeData != nodeData ||
                        change.onlyWhileEventIsActive && !pair.Value.Active)
                    {
                        continue;
                    }

                    string variable = change.variable?.Trim();
                    if (string.IsNullOrEmpty(variable) ||
                        !pair.Value.Variables.TryGetValue(
                            variable,
                            out int current))
                    {
                        continue;
                    }

                    pair.Value.Variables[variable] =
                        SaturatingAdd(current, change.amount);
                    MarkSaveDirty();
                }
            }
        }

        private bool EvaluateVariable(
            EventVariableCondition condition,
            EventData owner)
        {
            EventData leftEvent = condition.eventData == null
                ? owner
                : condition.eventData;
            if (!TryGetVariable(
                    leftEvent,
                    condition.variable,
                    out int left))
            {
                return false;
            }

            int right = condition.value;
            if (condition.compareToVariable)
            {
                EventData rightEvent = condition.otherEventData == null
                    ? leftEvent
                    : condition.otherEventData;
                if (!TryGetVariable(
                        rightEvent,
                        condition.otherVariable,
                        out right))
                {
                    return false;
                }
            }

            return condition.comparison switch
            {
                EventVariableComparison.Equal => left == right,
                EventVariableComparison.NotEqual => left != right,
                EventVariableComparison.Less => left < right,
                EventVariableComparison.LessOrEqual => left <= right,
                EventVariableComparison.Greater => left > right,
                EventVariableComparison.GreaterOrEqual => left >= right,
                _ => false
            };
        }

        private bool TryGetVariable(
            EventData eventData,
            string variable,
            out int value)
        {
            value = default;
            return eventData != null &&
                   _states.TryGetValue(eventData, out RuntimeState state) &&
                   state.Variables.TryGetValue(
                       variable?.Trim() ?? string.Empty,
                       out value);
        }

        private static int SaturatingAdd(int left, int right)
        {
            long result = (long)left + right;
            return result > int.MaxValue
                ? int.MaxValue
                : result < int.MinValue
                    ? int.MinValue
                    : (int)result;
        }

        private int GetItemCount(ItemCollectedEventCondition condition)
        {
            PersistentInventory inventory =
                _player == null ? null : _player.GetComponent<PersistentInventory>();
            return inventory == null
                ? 0
                : inventory.GetCount(condition.item, condition.rarity);
        }

        private void CaptureActivationBaselines(
            EventCondition condition,
            RuntimeState state)
        {
            switch (condition)
            {
                case AllEventCondition all when all.conditions != null:
                    foreach (EventCondition child in all.conditions)
                        CaptureActivationBaselines(child, state);
                    break;
                case AnyEventCondition any when any.conditions != null:
                    foreach (EventCondition child in any.conditions)
                        CaptureActivationBaselines(child, state);
                    break;
                case NotEventCondition not:
                    CaptureActivationBaselines(not.condition, state);
                    break;
                case ItemCollectedEventCondition item
                    when item.sinceEventActivated && item.item != null:
                    state.ItemBaselines[item] = GetItemCount(item);
                    break;
            }
        }

        private int GetDefeats(EnemyData enemy) =>
            _defeats.TryGetValue(enemy, out int count) ? count : 0;

        private static int GetBaseline(RuntimeState state, EnemyData enemy) =>
            state.DefeatBaseline.TryGetValue(enemy, out int count) ? count : 0;

        private long GetAbsoluteDay() =>
            (long)_time.Year * Mathf.Max(1, _world.daysInMonth) * 4L +
            (long)(int)_time.Season * Mathf.Max(1, _world.daysInMonth) +
            Mathf.Max(0, _time.DayInMonth - 1);

        private long GetCalendarValue(EventTimeUnit unit) => unit switch
        {
            EventTimeUnit.Years => _time.Year,
            EventTimeUnit.Months => (long)_time.Year * 4L + (int)_time.Season,
            _ => GetAbsoluteDay()
        };

        private long ToDays(int amount, EventTimeUnit unit) => unit switch
        {
            EventTimeUnit.Years =>
                (long)amount * Mathf.Max(1, _world.daysInMonth) * 4L,
            EventTimeUnit.Months =>
                (long)amount * Mathf.Max(1, _world.daysInMonth),
            _ => amount
        };

        private float DailyRoll(string salt)
        {
            uint hash = StableHash(salt);
            hash ^= (uint)GetAbsoluteDay() * 747796405u;
            hash = hash * 2891336453u + 277803737u;
            return (hash & 0x00FFFFFFu) / 16777216f;
        }

        private int DeterministicRange(string salt, int minimum, int maximum)
        {
            uint hash = StableHash(salt) ^ (uint)GetAbsoluteDay();
            return minimum + (int)(hash % (uint)(maximum - minimum + 1));
        }

        private static uint StableHash(string value)
        {
            uint hash = 2166136261u;
            foreach (char character in value ?? string.Empty)
                hash = (hash ^ character) * 16777619u;
            return hash;
        }

        private void MarkSaveDirty()
        {
            _saveDirty = true;
            _nextSaveTime = Time.unscaledTime + 1f;
        }

        private string GetSavePath()
        {
            string worldId = PlayerPrefs.GetString(
                "WorldSaver.ActiveWorld",
                "default");
            foreach (char invalid in Path.GetInvalidFileNameChars())
                worldId = worldId.Replace(invalid, '_');
            return Path.Combine(
                Application.persistentDataPath,
                "Worlds",
                string.IsNullOrWhiteSpace(worldId) ? "default" : worldId,
                "events.wse");
        }

        private void SaveState()
        {
            if (!_saveDirty && File.Exists(GetSavePath()))
                return;

            string path = GetSavePath();
            string directory = Path.GetDirectoryName(path);
            string temporary = path + ".tmp";
            string backup = path + ".bak";
            Directory.CreateDirectory(directory);

            try
            {
                // Dispose both handles before replacing the file. A using
                // declaration would keep the temp file open until this method
                // exits and make File.Move/File.Replace fail on Windows.
                using (FileStream stream = new(
                           temporary,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                using (BinaryWriter writer = new(stream))
                {
                    writer.Write(SaveMagic);
                    writer.Write(SaveVersion);
                    writer.Write(_allDefeats);
                    writer.Write(_defeats.Count);
                    foreach (KeyValuePair<EnemyData, int> defeat in _defeats)
                    {
                        writer.Write(defeat.Key == null
                            ? string.Empty
                            : defeat.Key.name);
                        writer.Write(defeat.Value);
                    }

                    writer.Write(_states.Count);
                    foreach (KeyValuePair<EventData, RuntimeState> pair in _states)
                        WriteEventState(writer, pair.Key, pair.Value);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                ReplaceSaveFile(temporary, path, backup);
                _saveDirty = false;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static void ReplaceSaveFile(
            string temporary,
            string path,
            string backup)
        {
            if (!File.Exists(path))
            {
                File.Move(temporary, path);
                return;
            }

            try
            {
                File.Replace(temporary, path, backup);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(path, backup, overwrite: true);
                File.Delete(path);
                File.Move(temporary, path);
            }
        }

        private void WriteEventState(
            BinaryWriter writer,
            EventData data,
            RuntimeState state)
        {
            writer.Write(data.persistentId ?? string.Empty);
            writer.Write(state.Active);
            writer.Write(state.Finished);
            writer.Write(state.ActivatedDay);
            writer.Write(state.AllDefeatsBaseline);
            writer.Write(state.Variables.Count);
            foreach (KeyValuePair<string, int> variable in state.Variables)
            {
                writer.Write(variable.Key);
                writer.Write(variable.Value);
            }
            writer.Write(state.DefeatBaseline.Count);
            foreach (KeyValuePair<EnemyData, int> defeat in state.DefeatBaseline)
            {
                writer.Write(defeat.Key == null
                    ? string.Empty
                    : defeat.Key.name);
                writer.Write(defeat.Value);
            }

            List<KeyValuePair<string, EventCondition>> conditions =
                GetConditionPaths(data);
            List<(string path, long deadline)> durations = new();
            List<(string path, int baseline)> items = new();
            foreach (KeyValuePair<string, EventCondition> condition in conditions)
            {
                if (condition.Value is RandomDurationEventCondition duration &&
                    state.DurationDeadlines.TryGetValue(
                        duration,
                        out long deadline))
                    durations.Add((condition.Key, deadline));
                if (condition.Value is ItemCollectedEventCondition item &&
                    state.ItemBaselines.TryGetValue(item, out int baseline))
                    items.Add((condition.Key, baseline));
            }
            writer.Write(durations.Count);
            foreach ((string path, long deadline) in durations)
            {
                writer.Write(path);
                writer.Write(deadline);
            }
            writer.Write(items.Count);
            foreach ((string path, int baseline) in items)
            {
                writer.Write(path);
                writer.Write(baseline);
            }
        }

        private bool LoadState()
        {
            string path = GetSavePath();
            if (!File.Exists(path))
                return false;

            try
            {
                using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                using BinaryReader reader = new(stream);
                if (reader.ReadUInt32() != SaveMagic ||
                    reader.ReadUInt16() != SaveVersion)
                    throw new InvalidDataException("Unsupported event save file.");

                _allDefeats = ReadNonNegative(reader, "enemy defeat total");
                Dictionary<string, EnemyData> enemies = BuildEnemyCatalog();
                int defeatCount = ReadCount(reader, "enemy defeat entries");
                for (int i = 0; i < defeatCount; i++)
                {
                    string enemyName = reader.ReadString();
                    int count = ReadNonNegative(reader, "enemy defeat count");
                    if (enemies.TryGetValue(enemyName, out EnemyData enemy))
                        _defeats[enemy] = count;
                }

                Dictionary<string, EventData> events = new(
                    StringComparer.Ordinal);
                foreach (EventData data in _states.Keys)
                    if (!string.IsNullOrWhiteSpace(data.persistentId))
                        events[data.persistentId] = data;
                int eventCount = ReadCount(reader, "event entries");
                for (int i = 0; i < eventCount; i++)
                    ReadEventState(reader, events, enemies);
                if (stream.Position != stream.Length)
                    throw new InvalidDataException(
                        "Event save contains trailing data.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        private void ReadEventState(
            BinaryReader reader,
            Dictionary<string, EventData> events,
            Dictionary<string, EnemyData> enemies)
        {
            string eventId = reader.ReadString();
            bool active = reader.ReadBoolean();
            bool finished = reader.ReadBoolean();
            long activatedDay = reader.ReadInt64();
            int allBaseline = ReadNonNegative(reader, "event defeat baseline");
            events.TryGetValue(eventId, out EventData data);
            RuntimeState state = data != null ? _states[data] : null;
            if (state != null)
            {
                state.Active = active;
                state.Finished = finished;
                state.ActivatedDay = activatedDay;
                state.AllDefeatsBaseline = allBaseline;
            }

            int variableCount = ReadCount(reader, "event variables");
            for (int i = 0; i < variableCount; i++)
            {
                string name = reader.ReadString();
                int value = reader.ReadInt32();
                if (state != null && state.Variables.ContainsKey(name))
                    state.Variables[name] = value;
            }
            int baselineCount = ReadCount(reader, "event enemy baselines");
            for (int i = 0; i < baselineCount; i++)
            {
                string enemyName = reader.ReadString();
                int value = ReadNonNegative(reader, "event enemy baseline");
                if (state != null &&
                    enemies.TryGetValue(enemyName, out EnemyData enemy))
                    state.DefeatBaseline[enemy] = value;
            }

            Dictionary<string, EventCondition> conditions = new(
                StringComparer.Ordinal);
            if (data != null)
                foreach (KeyValuePair<string, EventCondition> condition in
                         GetConditionPaths(data))
                    conditions[condition.Key] = condition.Value;
            int durationCount = ReadCount(reader, "duration deadlines");
            for (int i = 0; i < durationCount; i++)
            {
                string key = reader.ReadString();
                long value = reader.ReadInt64();
                if (state != null &&
                    conditions.TryGetValue(key, out EventCondition condition) &&
                    condition is RandomDurationEventCondition duration)
                    state.DurationDeadlines[duration] = value;
            }
            int itemCount = ReadCount(reader, "item baselines");
            for (int i = 0; i < itemCount; i++)
            {
                string key = reader.ReadString();
                int value = ReadNonNegative(reader, "item baseline");
                if (state != null &&
                    conditions.TryGetValue(key, out EventCondition condition) &&
                    condition is ItemCollectedEventCondition item)
                    state.ItemBaselines[item] = value;
            }
        }

        private Dictionary<string, EnemyData> BuildEnemyCatalog()
        {
            Dictionary<string, EnemyData> result =
                new(StringComparer.Ordinal);
            foreach (EventData data in _states.Keys)
            {
                foreach (KeyValuePair<string, EventCondition> pair in
                         GetConditionPaths(data))
                {
                    if (pair.Value is EnemiesDefeatedEventCondition defeated &&
                        defeated.enemy != null)
                        result[defeated.enemy.name] = defeated.enemy;
                }
            }
            return result;
        }

        private static List<KeyValuePair<string, EventCondition>>
            GetConditionPaths(EventData data)
        {
            List<KeyValuePair<string, EventCondition>> result = new();
            AddConditionPaths(data.activationCondition, "a", result);
            AddConditionPaths(data.endCondition, "e", result);
            return result;
        }

        private static void AddConditionPaths(
            EventCondition condition,
            string path,
            List<KeyValuePair<string, EventCondition>> result)
        {
            if (condition == null)
                return;
            result.Add(new KeyValuePair<string, EventCondition>(
                path,
                condition));
            if (condition is AllEventCondition all && all.conditions != null)
                for (int i = 0; i < all.conditions.Count; i++)
                    AddConditionPaths(all.conditions[i], $"{path}.{i}", result);
            else if (condition is AnyEventCondition any &&
                     any.conditions != null)
                for (int i = 0; i < any.conditions.Count; i++)
                    AddConditionPaths(any.conditions[i], $"{path}.{i}", result);
            else if (condition is NotEventCondition not)
                AddConditionPaths(not.condition, $"{path}.0", result);
        }

        private static int ReadCount(BinaryReader reader, string label)
        {
            int value = reader.ReadInt32();
            if (value < 0 || value > 100000)
                throw new InvalidDataException($"Invalid {label}: {value}.");
            return value;
        }

        private static int ReadNonNegative(BinaryReader reader, string label)
        {
            int value = reader.ReadInt32();
            if (value < 0)
                throw new InvalidDataException($"Invalid {label}: {value}.");
            return value;
        }

        private static void InitializeVariables(
            EventData data,
            RuntimeState state)
        {
            foreach (EventVariable variable in
                     data.blackboard ?? new List<EventVariable>())
            {
                string name = variable?.name?.Trim();
                if (!string.IsNullOrEmpty(name))
                    state.Variables[name] = variable.initialValue;
            }
        }
    }
}
