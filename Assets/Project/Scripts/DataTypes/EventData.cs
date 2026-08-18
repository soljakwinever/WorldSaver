using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public enum EventTimeUnit
    {
        Days,
        Months,
        Years
    }

    public enum EventStateRequirement
    {
        Active,
        Finished
    }

    public enum EventVariableComparison
    {
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual
    }

    public enum EventFeatureSpawnMode
    {
        SpawnOnActivation,
        NaturalTerrain
    }

    [CreateAssetMenu(fileName = "Event", menuName = "World/Event")]
    public sealed class EventData : ScriptableObject
    {
        [Tooltip("Player-facing event name. Falls back to the asset name when blank.")]
        public string displayName;

        [Tooltip("Stable ID used by conditions, saves, and enemy spawn rules.")]
        public string persistentId = "event";

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();

        [Header("Audio")]
        [Tooltip("Optional high-priority music while this event is active.")]
        public SongData musicOverride;

        [Header("Blackboard")]
        [Tooltip("Integer variables owned by this event at runtime.")]
        public List<EventVariable> blackboard = new();

        [Tooltip("Changes applied when a matching persistent node is removed from the world.")]
        public List<NodeDestroyedVariableChange> nodeDestroyedChanges = new();

        [Header("Activation")]
        [SerializeReference, ManagedReferenceSelector(typeof(EventCondition))]
        public EventCondition activationCondition = new AllEventCondition();

        [Header("Ending")]
        [SerializeReference, ManagedReferenceSelector(typeof(EventCondition))]
        public EventCondition endCondition;

        [Header("Effects")]
        public EventEffects effects = new();

        private void OnValidate()
        {
            displayName = displayName?.Trim();
            persistentId = persistentId?.Trim();
        }
    }

    [Serializable]
    public abstract class EventCondition
    {
        protected static string NameOf(UnityEngine.Object value, string fallback) =>
            value == null ? fallback : value.name;

        protected static string Units(EventTimeUnit unit, int amount)
        {
            string text = unit switch
            {
                EventTimeUnit.Days => "Day",
                EventTimeUnit.Months => "Month",
                EventTimeUnit.Years => "Year",
                _ => unit.ToString()
            };
            return amount == 1 ? text : text + "s";
        }
    }

    [Serializable]
    public sealed class EventVariable
    {
        public string name = "variable";
        public int initialValue;
    }

    [Serializable]
    public sealed class NodeDestroyedVariableChange
    {
        public NodeData nodeData;
        [Tooltip("Name of a variable in this event's blackboard.")]
        public string variable = "variable";
        [Tooltip("Signed amount. Use a negative number to decrease the variable.")]
        public int amount = 1;
        [Tooltip("Ignore matching destruction unless this event is active.")]
        public bool onlyWhileEventIsActive = true;
    }

    [Serializable]
    public sealed class EventVariableCondition : EventCondition
    {
        [Tooltip("Blank uses the event containing this condition.")]
        public EventData eventData;
        public string variable = "variable";
        public EventVariableComparison comparison =
            EventVariableComparison.GreaterOrEqual;
        public int value;
        [Tooltip("Compare against another blackboard variable instead of Value.")]
        public bool compareToVariable;
        [Tooltip("Blank uses the same event selected above.")]
        public EventData otherEventData;
        public string otherVariable = "variable";

        public override string ToString()
        {
            string left = string.IsNullOrWhiteSpace(variable)
                ? "<variable>"
                : variable.Trim();
            string operation = comparison switch
            {
                EventVariableComparison.Equal => "==",
                EventVariableComparison.NotEqual => "!=",
                EventVariableComparison.Less => "<",
                EventVariableComparison.LessOrEqual => "<=",
                EventVariableComparison.Greater => ">",
                EventVariableComparison.GreaterOrEqual => ">=",
                _ => "?"
            };
            string right = compareToVariable
                ? string.IsNullOrWhiteSpace(otherVariable)
                    ? "<variable>"
                    : otherVariable.Trim()
                : value.ToString();
            return $"Blackboard: {left} {operation} {right}";
        }
    }

    [Serializable]
    public sealed class AllEventCondition : EventCondition
    {
        [SerializeReference, ManagedReferenceSelector(typeof(EventCondition))]
        public List<EventCondition> conditions = new();

        public override string ToString() =>
            $"All: {conditions?.Count ?? 0} Conditions";
    }

    [Serializable]
    public sealed class AnyEventCondition : EventCondition
    {
        [SerializeReference, ManagedReferenceSelector(typeof(EventCondition))]
        public List<EventCondition> conditions = new();

        public override string ToString() =>
            $"Any: {conditions?.Count ?? 0} Conditions";
    }

    [Serializable]
    public sealed class NotEventCondition : EventCondition
    {
        [SerializeReference, ManagedReferenceSelector(typeof(EventCondition))]
        public EventCondition condition;

        public override string ToString() =>
            condition == null ? "Not: <None>" : $"Not: {condition}";
    }

    [Serializable]
    public sealed class CalendarEventCondition : EventCondition
    {
        [Min(1), Tooltip("Matches every N units, starting at Offset.")]
        public int every = 1;
        [Min(0)] public int offset;
        public EventTimeUnit unit = EventTimeUnit.Days;

        public override string ToString()
        {
            int interval = Mathf.Max(1, every);
            string result = $"Calendar: Every {interval} {Units(unit, interval)}";
            return offset > 0 ? $"{result}, Offset {offset}" : result;
        }
    }

    [Serializable]
    public sealed class TimeOfDayEventCondition : EventCondition
    {
        [Range(0, 23)]
        [Tooltip("First eligible hour.")]
        public int firstHour;

        [Range(0, 24)]
        [Tooltip("Exclusive final eligible hour. An earlier value creates an overnight window.")]
        public int lastHour = 24;

        public bool AllowsHour(int hour)
        {
            int first = Mathf.Clamp(firstHour, 0, 23);
            int last = Mathf.Clamp(lastHour, 0, 24);
            if (first == last)
                return true;
            return first < last
                ? hour >= first && hour < last
                : hour >= first || hour < last;
        }

        public override string ToString() =>
            $"Time of Day: {firstHour:00}:00-{lastHour:00}:00";
    }

    [Serializable]
    public sealed class WeatherEventCondition : EventCondition
    {
        public WeatherData weather;

        public override string ToString() =>
            $"Weather: {NameOf(weather, "<Any Weather>")}";
    }

    [Serializable]
    public sealed class RandomChanceEventCondition : EventCondition
    {
        [Range(0f, 1f), Tooltip("Rolled once per in-game day while eligible.")]
        public float chance = 0.5f;

        public override string ToString() =>
            $"Random Chance: {Mathf.Clamp01(chance) * 100f:0.##}%";
    }

    [Serializable]
    public sealed class OtherEventCondition : EventCondition
    {
        public EventData eventData;
        public EventStateRequirement requiredState = EventStateRequirement.Finished;

        public override string ToString() =>
            $"Event: {NameOf(eventData, "<None>")} Is {requiredState}";
    }

    [Serializable]
    public sealed class EnemiesDefeatedEventCondition : EventCondition
    {
        [Tooltip("Null counts every enemy type.")]
        public EnemyData enemy;
        [Min(1)] public int amount = 1;
        [Tooltip("When enabled, only defeats since this event activated count.")]
        public bool sinceEventActivated;

        public override string ToString() =>
            $"Enemies Defeated: {Mathf.Max(1, amount)} " +
            $"{NameOf(enemy, "Any Enemy")}" +
            (sinceEventActivated ? " Since Activation" : string.Empty);
    }

    [Serializable]
    public sealed class RandomDurationEventCondition : EventCondition
    {
        public EventTimeUnit unit = EventTimeUnit.Days;
        [Min(1)] public int minimum = 1;
        [Min(1)] public int maximum = 1;

        public override string ToString()
        {
            int min = Mathf.Max(1, minimum);
            int max = Mathf.Max(min, maximum);
            return min == max
                ? $"Duration: {min} {Units(unit, min)}"
                : $"Random Duration: {min}-{max} {Units(unit, max)}";
        }
    }

    [Serializable]
    public sealed class ItemCollectedEventCondition : EventCondition
    {
        public ItemData item;
        public ItemData.Rarity rarity = ItemData.Rarity.Common;
        [Min(1)] public int amount = 1;
        [Tooltip("Counts inventory gained since the event activated.")]
        public bool sinceEventActivated = true;

        public override string ToString() =>
            $"Items Collected: {Mathf.Max(1, amount)} " +
            $"{NameOf(item, "<No Item>")} ({rarity})" +
            (sinceEventActivated ? " Since Activation" : string.Empty);
    }

    [Serializable]
    public sealed class EventEffects
    {
        [Tooltip("Added to the global normalized temperature while active.")]
        [Range(-2f, 2f)] public float globalTemperatureOffset;
        [Tooltip("Weather started in the player's region when the event activates.")]
        public WeatherData weather;
        [Tooltip("Additional enemy rules enabled only while this event is active.")]
        public EnemySpawnRule[] enabledEnemySpawnRules = Array.Empty<EnemySpawnRule>();
        [Tooltip("Enemy rules suppressed while this event is active. Disabled rules take precedence over enabled rules.")]
        public EnemySpawnRule[] disabledEnemySpawnRules = Array.Empty<EnemySpawnRule>();
        [Tooltip("Feature building placement rules for this event.")]
        public EventFeatureBuildingEffect[] featureBuildings =
            Array.Empty<EventFeatureBuildingEffect>();
        [Tooltip("One-shot fast-travel portals created when the event activates.")]
        public EventPortalEffect[] portals = Array.Empty<EventPortalEffect>();
        [Tooltip("Blackboard variables mapped into the normalized FMOD Danger parameter while this event is active.")]
        public EventDangerEffect[] danger = Array.Empty<EventDangerEffect>();

        [HideInInspector, Tooltip("Legacy one-per-event building list.")]
        public FeatureBuildingData[] buildings = Array.Empty<FeatureBuildingData>();
    }

    [Serializable]
    public sealed class EventDangerEffect
    {
        [Tooltip("Name of a blackboard variable owned by this event.")]
        public string variable = "wave";
        public int inputMinimum;
        public int inputMaximum = 1;
        [Range(0f, 1f)] public float outputMinimum;
        [Range(0f, 1f)] public float outputMaximum = 1f;

        public float Evaluate(int value)
        {
            if (inputMinimum == inputMaximum)
                return value < inputMinimum
                    ? Mathf.Clamp01(outputMinimum)
                    : Mathf.Clamp01(outputMaximum);
            float amount = Mathf.InverseLerp(inputMinimum, inputMaximum, value);
            return Mathf.Clamp01(Mathf.Lerp(outputMinimum, outputMaximum, amount));
        }
    }

    [Serializable]
    public sealed class EventPortalEffect
    {
        [Tooltip("Portal prefab. If empty, Resources/Portal is used.")]
        public GameObject prefab;
        [Tooltip("Optional shared target texture. A private texture is created when empty.")]
        public RenderTexture renderTexture;
        [Tooltip("Place the entrance relative to the player when the event activates.")]
        public bool spawnRelativeToPlayer = true;
        public Vector2 entranceWorldPosition;
        public Vector2 entranceOffset = new(2f, 0f);
        [Tooltip("World-space position shown by the portal and used for travel.")]
        public Vector2 destinationWorldPosition;
        [Min(32)] public int textureSize = 256;
        [Min(0.1f)] public float activationDistance = 1.5f;
        [Min(0.05f)] public float transitionDuration = 0.45f;
        [Min(0.1f), Tooltip("Seconds spent building the portal before it can be entered.")]
        public float creationDuration = 3f;
        [Min(0.1f), Tooltip("Multiplier applied to the portal's authored particle sizes.")]
        public float particleSizeMultiplier = 6f;
        [Tooltip("Spawned once when portal construction begins.")]
        public GameObject startOneShot;
        [Tooltip("Spawned once when the destination becomes visible.")]
        public GameObject readyOneShot;
        [Tooltip("Persistent visible particles around the portal. These do not write to the destination stencil.")]
        public GameObject outerRimEffect;
    }

    [Serializable]
    public sealed class EventFeatureBuildingEffect
    {
        public FeatureBuildingData building;
        public EventFeatureSpawnMode spawnMode =
            EventFeatureSpawnMode.SpawnOnActivation;
        [Min(1), Tooltip("For Spawn On Activation, the requested number of buildings. Each is placed in a nearby region.")]
        public int count = 1;
        [Min(0), Tooltip("For Spawn On Activation, buildings cannot be placed in the player's region or any region within this radius. A value of 2 begins placement on region ring 3.")]
        public int excludedRegionRadiusFromPlayer;
    }
}
