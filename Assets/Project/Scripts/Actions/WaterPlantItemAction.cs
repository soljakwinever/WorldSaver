using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "Water Plant", menuName = "Data/Item Actions/Water Plant")]
    public sealed class WaterPlantItemAction : ItemAction
    {
        public override bool CanPerform(ActionContext context) =>
            TryGet(context, out PersistentPlant plant, out _) && plant.WaterLevel >= 0f;

        public override bool Perform(ActionContext context)
        {
            if (!TryGet(context, out PersistentPlant plant, out WaterPlantItemActionData data)) return false;
            float before = plant.WaterLevel;
            plant.Water(data.waterPoints);
            return plant.WaterLevel > before;
        }

        private static bool TryGet(ActionContext context, out PersistentPlant plant,
            out WaterPlantItemActionData data)
        {
            plant = null; data = null;
            if (context.Item == null || !context.Item.TryGetActionData(out data)) return false;
            Collider2D[] hits = Physics2D.OverlapPointAll(context.TargetPosition);
            foreach (Collider2D hit in hits)
            {
                plant = hit.GetComponentInParent<PersistentPlant>() ?? hit.GetComponentInChildren<PersistentPlant>();
                if (plant != null) return true;
            }
            return false;
        }
    }
}
