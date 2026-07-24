using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "New Item Data", menuName = "Data/Item Data", order = 0)]
    public class ItemData : ScriptableObject
    {
        public const int FuelUnitsPerBaseValue = 4;

        [Tooltip("Stable identifier used in save data. Do not change after the item ships.")]
        public string persistentId;

        [TextArea(2,4)]
        public string description;
        public int maxStack;
        
        public Sprite sprite;
        
        public int goldValue;
        [Min(1)] public int fuelValue = 1;

        [SerializeField] private ItemTag[] tags = System.Array.Empty<ItemTag>();

        public bool HasTag(ItemTag tag)
        {
            if (tag == null)
                return false;

            for (int i = 0; i < (tags?.Length ?? 0); i++)
            {
                if (tags[i] == tag)
                    return true;
            }

            return false;
        }

        public long GetFuelUnits(Rarity rarity)
        {
            int rarityMultiplier = rarity switch
            {
                Rarity.Common => 4,
                Rarity.Uncommon => 5,
                Rarity.Rare => 6,
                Rarity.Mythic => 8,
                Rarity.Legendary => 12,
                _ => throw new System.ArgumentOutOfRangeException(
                    nameof(rarity), rarity, "Unknown item rarity.")
            };

            return checked((long)fuelValue * rarityMultiplier);
        }
        
        public enum Rarity
        {
            Common,
            Uncommon,
            Rare,
            Mythic,
            Legendary,
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            fuelValue = Mathf.Max(1, fuelValue);
        }
#endif
    }
}
