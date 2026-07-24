using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public abstract class EntityConditionDefinition : ScriptableObject
    {
        public IOperationCondition Build(GameObject host)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            return Build(host, new HashSet<EntityConditionDefinition>());
        }

        internal IOperationCondition Build(
            GameObject host,
            HashSet<EntityConditionDefinition> buildPath)
        {
            if (!buildPath.Add(this))
                throw new InvalidOperationException(
                    $"Condition graph contains a cycle at '{name}'.");

            try
            {
                return BuildCondition(host, buildPath) ??
                    throw new InvalidOperationException(
                        $"Condition '{name}' returned no runtime condition.");
            }
            finally
            {
                buildPath.Remove(this);
            }
        }

        protected abstract IOperationCondition BuildCondition(
            GameObject host,
            HashSet<EntityConditionDefinition> buildPath);
    }
}
