using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "New Item Data", menuName = "Data/Item Data", order = 0)]
    public class ItemData : ScriptableObject
    {
        [Tooltip("Stable identifier used in save data. Do not change after the item ships.")]
        public string persistentId;

        [TextArea(2,4)]
        public string description;
        public int maxStack;
        
        public int goldValue;
        
        public enum Rarity
        {
            Common,
            Uncommon,
            Rare,
            Mythic,
            Legendary,
        }
    }
}
