using System;
using System.IO;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Persistence
{
    public sealed class FileRegionDiskStore : IRegionDiskStore
    {
        private readonly string _regionDirectory;

        public FileRegionDiskStore()
        {
            _regionDirectory = Path.Combine(
                Application.persistentDataPath,
                "Worlds",
                "default",
                "regions");
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

            await Awaitable.BackgroundThreadAsync();

            try
            {
                Directory.CreateDirectory(_regionDirectory);

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

        private string GetPath(Vector2Int position)
        {
            return Path.Combine(
                _regionDirectory,
                $"r_{position.x}_{position.y}.wsr");
        }
    }
}
