using System.Collections.Generic;
using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public interface IEventService
    {
        bool IsActive(string eventId);
        bool IsFinished(string eventId);
        bool TryGetVariable(string eventId, string variable, out int value);
        bool TrySetVariable(string eventId, string variable, int value);
        bool TryAddVariable(string eventId, string variable, int amount);
        IReadOnlyCollection<EnemySpawnRule> ActiveEnemySpawnRules { get; }
        IReadOnlyCollection<EnemySpawnRule> DisabledEnemySpawnRules { get; }
    }
}
