using System;
using System.Collections.Generic;

namespace Project.Scripts.AI
{
    /// <summary>
    /// Runtime state shared by behaviour-tree nodes.
    /// Child blackboards inherit values from their parent and can override them locally.
    /// </summary>
    public sealed class Blackboard
    {
        private readonly Dictionary<object, object> _values = new Dictionary<object, object>();

        public Blackboard Parent { get; }
        public int LocalCount => _values.Count;

        public event Action<object> ValueChanged;

        public Blackboard(Blackboard parent = null)
        {
            Parent = parent;
        }

        public Blackboard CreateChild() => new Blackboard(this);

        public void Set<T>(BlackboardKey<T> key, T value)
        {
            ValidateKey(key);
            _values[key] = value;
            ValueChanged?.Invoke(key);
        }

        public bool TryGet<T>(BlackboardKey<T> key, out T value)
        {
            ValidateKey(key);

            if (_values.TryGetValue(key, out object storedValue))
            {
                if (storedValue == null)
                {
                    value = default;
                    return true;
                }

                if (storedValue is T typedValue)
                {
                    value = typedValue;
                    return true;
                }

                throw new InvalidOperationException(
                    $"The value stored for '{key.Name}' is not assignable to {typeof(T).FullName}.");
            }

            if (Parent != null)
                return Parent.TryGet(key, out value);

            value = default;
            return false;
        }

        /// <summary>
        /// Retrieves a value when the key's generic type is only known at runtime.
        /// This is primarily useful for generic blackboard conditions and tooling.
        /// </summary>
        public bool TryGetValue(object key, out object value)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (_values.TryGetValue(key, out value))
                return true;

            if (Parent != null)
                return Parent.TryGetValue(key, out value);

            value = null;
            return false;
        }

        public T Get<T>(BlackboardKey<T> key)
        {
            if (TryGet(key, out T value))
                return value;

            throw new KeyNotFoundException($"No value was found for blackboard key '{key.Name}'.");
        }

        public T GetOrDefault<T>(BlackboardKey<T> key, T fallback = default)
        {
            return TryGet(key, out T value) ? value : fallback;
        }

        public bool Contains<T>(BlackboardKey<T> key)
        {
            ValidateKey(key);
            return _values.ContainsKey(key) || Parent != null && Parent.Contains(key);
        }

        public bool ContainsLocal<T>(BlackboardKey<T> key)
        {
            ValidateKey(key);
            return _values.ContainsKey(key);
        }

        public bool Remove<T>(BlackboardKey<T> key)
        {
            ValidateKey(key);
            bool removed = _values.Remove(key);

            if (removed)
                ValueChanged?.Invoke(key);

            return removed;
        }

        public void Clear()
        {
            if (_values.Count == 0)
                return;

            _values.Clear();
            ValueChanged?.Invoke(null);
        }

        private static void ValidateKey<T>(BlackboardKey<T> key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
        }
    }
}
