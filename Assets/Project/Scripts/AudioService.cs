using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.TimeAndWeather;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public sealed class AudioService :
        IAudioService,
        IInitializable,
        ITickable,
        IDisposable
    {
        private const string PlaneOwner = "plane";
        private const string BiomeOwner = "biome";
        private const string TemperatureParameter = "Temperature";
        private const float TemperatureUpdateInterval = 1f;

        private sealed class MusicEntry
        {
            public string Owner;
            public SongData Song;
            public MusicPriority Priority;
            public long Sequence;
            public EventInstance Instance;
            public bool HasInstance;
            public bool Removed;
        }

        private readonly TimeSignalBus _timeBus;
        private readonly ITimeController _time;
        private readonly PlaneSelection _plane;
        private readonly IWorldGenerator _world;
        private readonly PlayerDataController _player;
        private readonly Grid _grid;
        private readonly IRegionalWeatherService _weather;
        private readonly Dictionary<string, MusicEntry> _entries =
            new(StringComparer.Ordinal);

        private MusicEntry _current;
        private MusicEntry _outgoing;
        private long _sequence;
        private float _transitionTime;
        private float _transitionDuration;
        private Vector3Int _lastPlayerCell;
        private bool _hasPlayerCell;
        private float _temperatureUpdateTime;

        public SongData CurrentMusic => _current?.Song;

        public AudioService(
            TimeSignalBus timeBus,
            ITimeController time,
            PlaneSelection plane,
            IWorldGenerator world,
            PlayerDataController player,
            Grid grid,
            IRegionalWeatherService weather)
        {
            _timeBus = timeBus;
            _time = time;
            _plane = plane;
            _world = world;
            _player = player;
            _grid = grid;
            _weather = weather;
        }

        public void Initialize()
        {
            _timeBus.HourChanged += OnHourChanged;
            _plane.Changed += OnPlaneChanged;
            SetTimeParameters(_time.Hour, _time.Season);
            RefreshTemperature();
            SetCurrentBgm(
                PlaneOwner,
                _plane.Plane.DefaultMusic,
                MusicPriority.Plane);
            RefreshBiomeMusic(force: true);
        }

        public void Tick()
        {
            RefreshBiomeMusic(force: false);
            UpdateTemperature(Time.unscaledDeltaTime);
            UpdateTransition(Time.unscaledDeltaTime);
        }

        public void Dispose()
        {
            _timeBus.HourChanged -= OnHourChanged;
            _plane.Changed -= OnPlaneChanged;
            HashSet<MusicEntry> released = new();
            foreach (MusicEntry entry in _entries.Values)
                Release(entry, released);
            Release(_outgoing, released);
            _entries.Clear();
            _current = null;
            _outgoing = null;
        }

        private void OnPlaneChanged(PlaneData _, PlaneData current)
        {
            SetCurrentBgm(
                PlaneOwner,
                current != null ? current.DefaultMusic : null,
                MusicPriority.Plane);
            _hasPlayerCell = false;
            RefreshTemperature();
            RefreshBiomeMusic(force: true);
        }

        public void SetCurrentBgm(
            string ownerKey,
            SongData song,
            MusicPriority priority)
        {
            ownerKey = ownerKey?.Trim();
            if (string.IsNullOrEmpty(ownerKey))
                throw new ArgumentException(
                    "A music owner key is required.", nameof(ownerKey));

            if (song == null || !song.IsValid)
            {
                ClearCurrentBgm(ownerKey);
                return;
            }

            if (_entries.TryGetValue(ownerKey, out MusicEntry existing) &&
                existing.Song == song && existing.Priority == priority)
                return;

            if (existing != null)
            {
                _entries.Remove(ownerKey);
                existing.Removed = true;
            }

            MusicEntry entry = new()
            {
                Owner = ownerKey,
                Song = song,
                Priority = priority,
                Sequence = ++_sequence
            };
            _entries.Add(ownerKey, entry);
            SelectWinner();
        }

        public void ClearCurrentBgm(string ownerKey)
        {
            ownerKey = ownerKey?.Trim();
            if (string.IsNullOrEmpty(ownerKey) ||
                !_entries.Remove(ownerKey, out MusicEntry removed))
                return;

            removed.Removed = true;
            if (removed != _current)
                StopAndRelease(removed);
            SelectWinner();
        }

        public void PlayOneShot(SongData sound) =>
            PlayOneShotInternal(sound, null);

        public void PlayOneShot(SongData sound, Vector3 worldPosition) =>
            PlayOneShotInternal(sound, worldPosition);

        public void SetEventParameter(string name, float value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(
                    "An event parameter name is required.", nameof(name));

            RuntimeManager.StudioSystem.setParameterByName(name.Trim(), value);
        }

        private void SelectWinner()
        {
            MusicEntry winner = null;
            foreach (MusicEntry candidate in _entries.Values)
            {
                if (winner == null || candidate.Priority > winner.Priority ||
                    candidate.Priority == winner.Priority &&
                    candidate.Sequence > winner.Sequence)
                    winner = candidate;
            }

            if (winner == _current)
                return;
            BeginTransition(winner);
        }

        private void BeginTransition(MusicEntry incoming)
        {
            if (_outgoing != null && _outgoing != _current)
                FinishOutgoing(_outgoing);

            _outgoing = _current;
            _current = incoming;
            _transitionTime = 0f;
            _transitionDuration = Mathf.Max(
                _outgoing?.Song?.crossfadeDuration ?? 0f,
                _current?.Song?.crossfadeDuration ?? 0f);

            if (_current != null)
            {
                EnsureInstance(_current);
                _current.Instance.setPaused(false);
                _current.Instance.setVolume(
                    _transitionDuration > 0f ? 0f : _current.Song.volume);
            }

            if (_transitionDuration <= 0f)
                CompleteTransition();
        }

        private void UpdateTransition(float deltaTime)
        {
            if (_outgoing == null && _transitionDuration <= 0f)
                return;

            _transitionTime += Mathf.Max(0f, deltaTime);
            float amount = _transitionDuration <= 0f
                ? 1f
                : Mathf.Clamp01(_transitionTime / _transitionDuration);
            if (_outgoing?.HasInstance == true)
                _outgoing.Instance.setVolume(
                    _outgoing.Song.volume * (1f - amount));
            if (_current?.HasInstance == true)
                _current.Instance.setVolume(_current.Song.volume * amount);
            if (amount >= 1f)
                CompleteTransition();
        }

        private void CompleteTransition()
        {
            if (_outgoing != null)
                FinishOutgoing(_outgoing);
            _outgoing = null;
            _transitionDuration = 0f;
            if (_current?.HasInstance == true)
                _current.Instance.setVolume(_current.Song.volume);
        }

        private static void FinishOutgoing(MusicEntry entry)
        {
            if (!entry.HasInstance)
                return;
            if (entry.Removed)
                StopAndRelease(entry);
            else
            {
                entry.Instance.setPaused(true);
                entry.Instance.setVolume(entry.Song.volume);
            }
        }

        private static void EnsureInstance(MusicEntry entry)
        {
            if (entry.HasInstance)
                return;
            entry.Instance = RuntimeManager.CreateInstance(
                entry.Song.eventReference);
            entry.HasInstance = true;
            entry.Instance.start();
        }

        private static void StopAndRelease(MusicEntry entry)
        {
            if (entry == null || !entry.HasInstance)
                return;
            entry.Instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            entry.Instance.release();
            entry.HasInstance = false;
        }

        private static void Release(
            MusicEntry entry,
            HashSet<MusicEntry> released)
        {
            if (entry != null && released.Add(entry))
                StopAndRelease(entry);
        }

        private static void PlayOneShotInternal(
            SongData sound,
            Vector3? worldPosition)
        {
            if (sound == null || !sound.IsValid)
                return;
            EventInstance instance = RuntimeManager.CreateInstance(
                sound.eventReference);
            if (worldPosition.HasValue)
                instance.set3DAttributes(
                    RuntimeUtils.To3DAttributes(worldPosition.Value));
            instance.setVolume(sound.volume);
            instance.start();
            instance.release();
        }

        private void RefreshBiomeMusic(bool force)
        {
            if (_player == null || _grid == null || _world == null)
                return;
            Vector3Int cell = _grid.WorldToCell(_player.transform.position);
            if (!force && _hasPlayerCell && cell == _lastPlayerCell)
                return;
            _hasPlayerCell = true;
            _lastPlayerCell = cell;
            BiomeData biome = _world.GetTerrainSample(cell.x, cell.y)
                .biomeBlend.dominantBiome;
            SetCurrentBgm(
                BiomeOwner,
                biome != null ? biome.musicOverride : null,
                MusicPriority.Biome);
        }

        private void OnHourChanged(TimeChangedArgs args) =>
            SetTimeParameters(args.Hour, args.Season);

        private void SetTimeParameters(int hour, Enums.Season season)
        {
            SetEventParameter("Hour", hour);
            SetEventParameter("Season", (int)season);
        }

        private void UpdateTemperature(float deltaTime)
        {
            _temperatureUpdateTime += Mathf.Max(0f, deltaTime);
            if (_temperatureUpdateTime < TemperatureUpdateInterval)
                return;

            _temperatureUpdateTime %= TemperatureUpdateInterval;
            RefreshTemperature();
        }

        private void RefreshTemperature()
        {
            if (_player == null || _weather == null)
                return;

            SetEventParameter(
                TemperatureParameter,
                _weather.GetAmbientTemperature(_player.transform.position));
        }
    }
}
