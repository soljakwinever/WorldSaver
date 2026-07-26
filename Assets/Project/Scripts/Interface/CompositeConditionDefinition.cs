using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public abstract class CompositeConditionDefinition : EntityConditionDefinition
    {
        [SerializeField] protected EntityConditionDefinition[] conditions =
            Array.Empty<EntityConditionDefinition>();

        protected IOperationCondition[] BuildChildren(
            GameObject host,
            HashSet<EntityConditionDefinition> buildPath)
        {
            if (conditions.Length == 0)
                throw new InvalidOperationException(
                    $"Composite condition '{name}' requires at least one child.");

            IOperationCondition[] children = new IOperationCondition[conditions.Length];
            for (int i = 0; i < conditions.Length; i++)
            {
                EntityConditionDefinition child = conditions[i];
                if (child == null)
                    throw new InvalidOperationException(
                        $"Composite condition '{name}' has a null child at index {i}.");
                children[i] = child.Build(host, buildPath);
            }

            return children;
        }
    }

    internal sealed class AllCondition : IOperationCondition
    {
        private readonly IOperationCondition[] _children;

        public AllCondition(IOperationCondition[] children)
        {
            List<IOperationCondition> unique = new(children.Length);
            for (int i = 0; i < children.Length; i++)
            {
                bool alreadyAdded = false;
                for (int j = 0; j < unique.Count; j++)
                {
                    if (ReferenceEquals(unique[j], children[i]))
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded)
                    unique.Add(children[i]);
            }

            _children = unique.ToArray();
        }

        public bool ConsumesOperations
        {
            get
            {
                for (int i = 0; i < _children.Length; i++)
                {
                    if (_children[i].ConsumesOperations)
                        return true;
                }

                return false;
            }
        }

        public long AvailableOperations
        {
            get
            {
                long available = long.MaxValue;
                for (int i = 0; i < _children.Length; i++)
                    available = Math.Min(available, _children[i].AvailableOperations);
                return available;
            }
        }

        public void Commit(long operations)
        {
            if (operations < 0 || operations > AvailableOperations)
                throw new ArgumentOutOfRangeException(nameof(operations));

            for (int i = 0; i < _children.Length; i++)
                _children[i].Commit(operations);
        }
    }

    internal sealed class AnyCondition : IOperationCondition
    {
        private readonly IOperationCondition[] _children;

        public AnyCondition(IOperationCondition[] children)
        {
            _children = children;
        }

        public bool ConsumesOperations
        {
            get
            {
                for (int i = 0; i < _children.Length; i++)
                {
                    if (_children[i].ConsumesOperations)
                        return true;
                }

                return false;
            }
        }

        public long AvailableOperations
        {
            get
            {
                long available = 0;
                for (int i = 0; i < _children.Length; i++)
                    available = Math.Max(available, _children[i].AvailableOperations);
                return available;
            }
        }

        public void Commit(long operations)
        {
            if (operations < 0)
                throw new ArgumentOutOfRangeException(nameof(operations));

            for (int i = 0; i < _children.Length; i++)
            {
                if (_children[i].AvailableOperations < operations)
                    continue;

                _children[i].Commit(operations);
                return;
            }

            throw new InvalidOperationException(
                "No alternative condition can commit the requested operations.");
        }
    }

    internal sealed class NotCondition : IOperationCondition
    {
        private readonly IOperationCondition _child;

        public NotCondition(IOperationCondition child)
        {
            _child = child;
        }

        public bool ConsumesOperations => false;

        public long AvailableOperations =>
            _child.AvailableOperations == 0 ? long.MaxValue : 0;

        public void Commit(long operations)
        {
            if (operations < 0 || operations > AvailableOperations)
                throw new ArgumentOutOfRangeException(nameof(operations));
        }
    }
}
