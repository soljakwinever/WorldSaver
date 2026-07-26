using System;

namespace Project.Scripts.AI
{
    /// <summary>
    /// A strongly typed, identity-based key used to access blackboard values.
    /// Keep commonly used keys in a shared static class so nodes use the same instance.
    /// </summary>
    public sealed class BlackboardKey<T>
    {
        public string Name { get; }

        public BlackboardKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A blackboard key must have a name.", nameof(name));

            Name = name;
        }

        public override string ToString() => $"{Name} ({typeof(T).Name})";
    }
}
