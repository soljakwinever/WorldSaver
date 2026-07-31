using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public readonly struct ClimateCoreInfluence
    {
        public readonly NodeId EntityId;
        public readonly Vector2 Position;
        public readonly float InnerRadius;
        public readonly float OuterRadius;
        public readonly float SpreadRate;
        public readonly float SpreadAmount;
        public readonly long AtTick;
        public readonly float TemperatureOffset;
        public readonly string PermanentWeatherId;

        public ClimateCoreInfluence(
            NodeId entityId,
            Vector2 position,
            float innerRadius,
            float outerRadius,
            float spreadRate,
            float spreadAmount,
            long atTick,
            float temperatureOffset,
            string permanentWeatherId)
        {
            EntityId = entityId;
            Position = position;
            InnerRadius = Mathf.Max(0f, innerRadius);
            OuterRadius = Mathf.Max(InnerRadius, outerRadius);
            SpreadRate = Mathf.Max(0f, spreadRate);
            SpreadAmount = Mathf.Clamp(
                spreadAmount,
                InnerRadius,
                OuterRadius);
            AtTick = atTick;
            TemperatureOffset = temperatureOffset;
            PermanentWeatherId = permanentWeatherId ?? string.Empty;
        }

        public float GetSpreadAt(long tick)
        {
            long elapsed = System.Math.Max(0, tick - AtTick);
            return Mathf.Min(
                OuterRadius,
                SpreadAmount + SpreadRate * elapsed);
        }
    }

    public interface IClimateCoreInfluenceRegistry
    {
        bool RegisterOrUpdate(ClimateCoreInfluence influence);
        void Remove(NodeId entityId);
    }

    public sealed class NullClimateCoreInfluenceRegistry :
        IClimateCoreInfluenceRegistry
    {
        public bool RegisterOrUpdate(ClimateCoreInfluence influence)
        {
            return true;
        }

        public void Remove(NodeId entityId)
        {
        }
    }
}
