using System;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class PersistentRespawn : MonoBehaviour, IGeneratedEntityRespawn
    {
        private NodeId _entityId;
        private float _minimumDays;
        private float _maximumDays;
        private float _ticksPerDay;

        public bool RespawnInsideTownInfluence { get; private set; }

        public void Initialize(
            NodeId entityId,
            float minimumDays,
            float maximumDays,
            bool respawnInsideTownInfluence,
            float ticksPerDay)
        {
            _entityId = entityId;
            _minimumDays = Mathf.Max(0f, minimumDays);
            _maximumDays = Mathf.Max(_minimumDays, maximumDays);
            _ticksPerDay = Mathf.Max(1f, ticksPerDay);
            RespawnInsideTownInfluence = respawnInsideTownInfluence;
        }

        public long GetRespawnTick(long removedAtTick)
        {
            // Stable per removal and entity, so save/reload cannot reroll it.
            ulong bits = _entityId.value ^ (ulong)removedAtTick;
            bits ^= bits >> 33;
            bits *= 0xff51afd7ed558ccdUL;
            bits ^= bits >> 33;
            float sample = (bits & 0xffffffUL) / (float)0x1000000;
            double days = _minimumDays +
                          (_maximumDays - _minimumDays) * sample;
            long delay = Math.Max(1L, (long)Math.Ceiling(days * _ticksPerDay));
            return removedAtTick > long.MaxValue - delay
                ? long.MaxValue
                : removedAtTick + delay;
        }
    }
}
