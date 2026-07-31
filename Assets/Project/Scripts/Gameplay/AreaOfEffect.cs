using System;
using System.IO;
using Project.Scripts.Core;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public sealed class AreaOfEffect : MonoBehaviour, IPersistentComponent,
        IOfflineSimulatable
    {
        public const ushort TypeId = 0x414F;
        private const ushort CurrentVersion = 1;

        [SerializeField, Min(0f)] private float outerRadius = 64f;
        [SerializeField, Min(0f)] private float innerRadius = 16f;
        [SerializeField, Min(0f)] private float spreadRate = 0.05f;
        [SerializeField, Min(0f)] private float spreadAmount = 16f;

        [InjectOptional] private IWorldClock _clock;
        private long _lastLiveTick = -1;
        private float _baselineSpreadAmount;

        public event Action Changed;

        public float OuterRadius => outerRadius;
        public float InnerRadius => innerRadius;
        public float SpreadRate => spreadRate;
        public float SpreadAmount => spreadAmount;
        public float EffectiveOuterRadius =>
            Mathf.Clamp(spreadAmount, innerRadius, outerRadius);
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;

        public void Initialize(
            float configuredOuterRadius,
            float configuredInnerRadius,
            float configuredSpreadRate)
        {
            innerRadius = Mathf.Max(0f, configuredInnerRadius);
            outerRadius = Mathf.Max(innerRadius, configuredOuterRadius);
            spreadRate = Mathf.Max(0f, configuredSpreadRate);
            _baselineSpreadAmount = spreadAmount = innerRadius;
            _lastLiveTick = _clock?.CurrentTick ?? -1;
        }

        private void Update()
        {
            if (_clock == null)
                return;

            long tick = _clock.CurrentTick;
            if (_lastLiveTick < 0)
            {
                _lastLiveTick = tick;
                return;
            }

            Advance(_lastLiveTick, tick);
            _lastLiveTick = tick;
        }

        public void SimulateOffline(
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy)
        {
            if (policy == OfflineSimulationPolicy.None)
                return;

            Advance(fromTick, toTick);
            _lastLiveTick = toTick;
        }

        public float GetInfluence(Vector2 source, Vector2 sample)
        {
            float distance = Vector2.Distance(source, sample);
            if (distance <= innerRadius)
                return 1f;

            float edge = EffectiveOuterRadius;
            if (distance >= edge || edge <= innerRadius)
                return 0f;

            float t = Mathf.InverseLerp(edge, innerRadius, distance);
            return t * t * (3f - 2f * t);
        }

        public static float EvaluateInfluence(
            float distance,
            float inner,
            float outer)
        {
            inner = Mathf.Max(0f, inner);
            outer = Mathf.Max(inner, outer);
            if (distance <= inner)
                return 1f;
            if (distance >= outer || outer <= inner)
                return 0f;
            float t = Mathf.InverseLerp(outer, inner, distance);
            return t * t * (3f - 2f * t);
        }

        private void Advance(long fromTick, long toTick)
        {
            if (toTick <= fromTick || spreadAmount >= outerRadius)
                return;

            float previous = spreadAmount;
            spreadAmount = Mathf.Min(
                outerRadius,
                spreadAmount + spreadRate * (toTick - fromTick));
            if (!Mathf.Approximately(previous, spreadAmount))
                Changed?.Invoke();
        }

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            writer.Write(spreadAmount);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion != CurrentVersion)
                throw new InvalidDataException(
                    $"Unsupported area-of-effect state version {savedVersion}.");

            spreadAmount = Mathf.Clamp(
                reader.ReadSingle(),
                innerRadius,
                outerRadius);
            Changed?.Invoke();
        }

        public bool IsAtBaseline() =>
            Mathf.Approximately(spreadAmount, _baselineSpreadAmount);

        private void OnValidate()
        {
            innerRadius = Mathf.Max(0f, innerRadius);
            outerRadius = Mathf.Max(innerRadius, outerRadius);
            spreadRate = Mathf.Max(0f, spreadRate);
            spreadAmount = Mathf.Clamp(
                spreadAmount,
                innerRadius,
                outerRadius);
        }
    }
}
