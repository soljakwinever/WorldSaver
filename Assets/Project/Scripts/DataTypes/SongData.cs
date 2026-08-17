using FMODUnity;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Song", menuName = "Audio/Song")]
    public sealed class SongData : ScriptableObject
    {
        [Tooltip("FMOD event played for this track or sound effect.")]
        public EventReference eventReference;

        [Range(0f, 1f)] public float volume = 1f;

        [Min(0f), Tooltip("Seconds used when transitioning to or from this music track.")]
        public float crossfadeDuration = 1f;

        public bool IsValid => !eventReference.IsNull;
    }
}
