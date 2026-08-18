using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public enum MusicPriority
    {
        Plane = 0,
        Biome = 100,
        Event = 200
    }

    public interface IAudioService
    {
        SongData CurrentMusic { get; }

        void SetCurrentBgm(string ownerKey, SongData song, MusicPriority priority);
        void ClearCurrentBgm(string ownerKey);
        void PlayOneShot(SongData sound);
        void PlayOneShot(SongData sound, Vector3 worldPosition);
        void SetEventParameter(string name, float value);
    }

    public interface IDangerService
    {
        void SetEventContribution(string ownerKey, float value);
        void ClearEventContribution(string ownerKey);
    }
}
