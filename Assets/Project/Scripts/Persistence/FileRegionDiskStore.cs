using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Persistence
{
    public sealed class FileRegionDiskStore : IRegionDiskStore
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim>
            SaveLocks = new(StringComparer.OrdinalIgnoreCase);

        private readonly PlaneSelection _planeSelection;
        public string RegionDirectory => Path.Combine(
            Application.persistentDataPath,
            "Worlds",
            PlayerPrefs.GetString("WorldSaver.ActiveWorld", "default"),
            "planes",
            SanitizePathSegment(_planeSelection.PlaneId),
            "regions");

        public FileRegionDiskStore(PlaneSelection planeSelection)
        {
            _planeSelection = planeSelection ??
                throw new ArgumentNullException(nameof(planeSelection));
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("Plane ID cannot be empty.");
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Trim();
        }

        public async Awaitable<RegionSaveData> LoadAsync(
            Vector2Int regionPosition)
        {
            string path = GetPath(regionPosition);
            RegionSaveData result = null;

            await Awaitable.BackgroundThreadAsync();

            try
            {
                if (!File.Exists(path))
                    return null;

                using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                result = RegionSaveCodec.Read(stream);

                if (result.coordinate != regionPosition)
                {
                    throw new InvalidDataException(
                        $"Region file {path} contains coordinate " +
                        $"{result.coordinate}, expected {regionPosition}.");
                }
            }
            finally
            {
                await Awaitable.MainThreadAsync();
            }

            return result;
        }

        public async Awaitable SaveAsync(RegionSaveData snapshot)
        {
            string path = GetPath(snapshot.coordinate);
            string tempPath = path + ".tmp";
            string backupPath = path + ".bak";
            SemaphoreSlim saveLock = SaveLocks.GetOrAdd(
                path,
                static _ => new SemaphoreSlim(1, 1));

            await saveLock.WaitAsync();

            try
            {
                await Awaitable.BackgroundThreadAsync();

                try
                {
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(path) ?? RegionDirectory);

                    using (FileStream stream = new(
                               tempPath,
                               FileMode.Create,
                               FileAccess.Write,
                               FileShare.None,
                               4096,
                               FileOptions.WriteThrough))
                    {
                        RegionSaveCodec.Write(stream, snapshot);
                        stream.Flush(flushToDisk: true);
                    }

                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Replace(tempPath, path, backupPath);
                        }
                        catch (PlatformNotSupportedException)
                        {
                            // Desktop platforms normally support File.Replace. This
                            // fallback is less crash-safe and should be replaced with
                            // the platform's atomic rename API when shipping there.
                            File.Copy(path, backupPath, overwrite: true);
                            File.Delete(path);
                            File.Move(tempPath, path);
                        }
                    }
                    else
                    {
                        File.Move(tempPath, path);
                    }
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);

                    await Awaitable.MainThreadAsync();
                }
            }
            finally
            {
                saveLock.Release();
            }
        }

        private string GetPath(Vector2Int position)
        {
            return Path.Combine(
                RegionDirectory,
                $"r_{position.x}_{position.y}.wsr");
        }
    }
}
