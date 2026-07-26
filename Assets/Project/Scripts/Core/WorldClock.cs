using System;
using System.IO;
using Project.Scripts.Enums;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Core
{
    // Replace this with an adapter around TimeController once the calendar owns
    // the authoritative persistent tick.
    public sealed class WorldClock : MonoBehaviour, IWorldClock
    {
        private const uint FileMagic = 0x43575357; // WSWC
        private const ushort CurrentFileVersion = 2;

        [Header("World Save")]
        [SerializeField] private string worldId = "default";
        [Min(1f)]
        [SerializeField] private float autoSaveInterval = 30f;

        [Header("Clock")]
        [Min(0.01f)]
        [SerializeField] private float secondsPerTick = 1f;
        [SerializeField] private long currentTick;

        [InjectOptional] private ITimeController _timeController;

        private float _accumulator;
        private float _nextAutoSaveTime;
        private bool _loaded;

        public long CurrentTick => currentTick;
        public string WorldFilePath => GetWorldFilePath();

        private void Start()
        {
            TryLoad();
            _loaded = true;
            _nextAutoSaveTime = Time.unscaledTime + autoSaveInterval;
        }

        private void Update()
        {
            _accumulator += Time.deltaTime;

            while (_accumulator >= secondsPerTick)
            {
                _accumulator -= secondsPerTick;
                currentTick++;
            }

            if (Time.unscaledTime >= _nextAutoSaveTime)
            {
                TrySave();
                _nextAutoSaveTime = Time.unscaledTime + autoSaveInterval;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && _loaded)
                TrySave();
        }

        private void OnApplicationQuit()
        {
            if (_loaded)
                TrySave();
        }

        private void OnDisable()
        {
            if (_loaded)
                TrySave();
        }

        public void Restore(long tick)
        {
            currentTick = System.Math.Max(0, tick);
            _accumulator = 0f;
        }

        public void Save()
        {
            string path = GetWorldFilePath();
            string directory = Path.GetDirectoryName(path);
            string tempPath = path + ".tmp";
            string backupPath = path + ".bak";

            Directory.CreateDirectory(directory);

            try
            {
                using (FileStream stream = new(
                           tempPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                using (BinaryWriter writer = new(stream))
                {
                    writer.Write(FileMagic);
                    writer.Write(CurrentFileVersion);
                    writer.Write(currentTick);
                    writer.Write(_timeController != null);
                    if (_timeController != null)
                    {
                        writer.Write(_timeController.DayInMonth);
                        writer.Write((int)_timeController.Season);
                        writer.Write(_timeController.Year);
                        writer.Write(_timeController.DayProgress);
                    }
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                ReplaceFile(tempPath, path, backupPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        public bool Load()
        {
            string path = GetWorldFilePath();
            if (!File.Exists(path))
                return false;

            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using BinaryReader reader = new(stream);

            if (reader.ReadUInt32() != FileMagic)
                throw new InvalidDataException("Not a WorldSaver world file.");

            ushort version = reader.ReadUInt16();
            if (version == 0 || version > CurrentFileVersion)
                throw new InvalidDataException(
                    $"Unsupported world file version {version}.");

            long savedTick = reader.ReadInt64();
            if (savedTick < 0)
                throw new InvalidDataException(
                    $"World file contains invalid tick {savedTick}.");

            if (version >= 2 && reader.ReadBoolean())
            {
                int dayInMonth = reader.ReadInt32();
                int seasonValue = reader.ReadInt32();
                int year = reader.ReadInt32();
                float dayProgress = reader.ReadSingle();

                if (dayInMonth < 1 ||
                    !Enum.IsDefined(typeof(Season), seasonValue) ||
                    year < 0 ||
                    float.IsNaN(dayProgress) ||
                    float.IsInfinity(dayProgress) ||
                    dayProgress < 0f ||
                    dayProgress > 1f)
                {
                    throw new InvalidDataException(
                        "World file contains invalid date/time state.");
                }

                _timeController?.RestoreTime(
                    dayInMonth,
                    (Season)seasonValue,
                    year,
                    dayProgress);
            }

            if (stream.Position != stream.Length)
                throw new InvalidDataException(
                    "World file contains unexpected trailing data.");

            Restore(savedTick);
            return true;
        }

        private string GetWorldFilePath()
        {
            return Path.Combine(
                Application.persistentDataPath,
                "Worlds",
                SanitizePathSegment(worldId, "default"),
                "world.wsw");
        }

        private void TrySave()
        {
            try
            {
                Save();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void TryLoad()
        {
            try
            {
                Load();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private static void ReplaceFile(
            string tempPath,
            string path,
            string backupPath)
        {
            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }

            try
            {
                File.Replace(tempPath, path, backupPath);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(path, backupPath, overwrite: true);
                File.Delete(path);
                File.Move(tempPath, path);
            }
        }

        private static string SanitizePathSegment(
            string value,
            string fallback)
        {
            string result =
                string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');

            return result;
        }

        private void OnValidate()
        {
            secondsPerTick = Mathf.Max(0.01f, secondsPerTick);
            autoSaveInterval = Mathf.Max(1f, autoSaveInterval);
            currentTick = Math.Max(0, currentTick);
        }
    }
}
