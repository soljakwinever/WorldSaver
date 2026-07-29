using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [Serializable]
    public sealed class DropData
    {
        public ItemData item;
        [Range(0f, 1f)] public float dropChance = 1f;
        [Min(1)] public int rolls = 1;
        [Min(1)] public int minimumAmount = 1;
        [Min(1)] public int maximumAmount = 1;

        public bool PassesDropChance(float roll)
        {
            if (dropChance <= 0f)
                return false;
            if (dropChance >= 1f)
                return true;

            return roll < dropChance;
        }

        public int RollAmount() =>
            UnityEngine.Random.Range(minimumAmount, maximumAmount + 1);
    }

    [CreateAssetMenu(
        fileName = "New Enemy Data",
        menuName = "Data/Enemy Data",
        order = 0)]
    public sealed class EnemyData : ScriptableObject
    {
        [Header("Stats")]
        [Min(1)] public int hp = 10;
        [Min(0)] public int defense;
        [Min(0)] public int experienceValue;

        [Header("Presentation and AI")]
        [Tooltip("Prefab spawned for this enemy. It may already contain health and AI components.")]
        public GameObject visual;
        public BehaviourTreeData behaviourTree;

        [Header("Death Drops")]
        public DropData[] drops = Array.Empty<DropData>();
        [Min(0f)] public float dropExplosionImpulse = 2.5f;
        [Range(0f, 1f)] public float dropExplosionVariation = 0.25f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            hp = Mathf.Max(1, hp);
            defense = Mathf.Max(0, defense);
            experienceValue = Mathf.Max(0, experienceValue);
            dropExplosionImpulse = Mathf.Max(0f, dropExplosionImpulse);
            drops ??= Array.Empty<DropData>();

            foreach (DropData drop in drops)
            {
                if (drop == null)
                    continue;

                drop.rolls = Mathf.Max(1, drop.rolls);
                drop.minimumAmount = Mathf.Max(1, drop.minimumAmount);
                drop.maximumAmount =
                    Mathf.Max(drop.minimumAmount, drop.maximumAmount);
            }
        }
#endif
    }
}
