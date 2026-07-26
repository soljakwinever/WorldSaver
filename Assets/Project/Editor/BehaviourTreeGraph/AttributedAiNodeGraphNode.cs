using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Project.Scripts.AI;
using Project.Scripts.AI.GraphEditor;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Project.Editor.AI
{
    [Serializable]
    internal abstract class AttributedAiNodeGraphNode : BehaviourTreeGraphNode
    {
        internal abstract Type RuntimeNodeType { get; }

        protected override string DefaultDisplayName
        {
            get
            {
                var attribute = RuntimeNodeType
                    .GetCustomAttribute<AiNodeAttribute>();
                return string.IsNullOrWhiteSpace(attribute?.Name)
                    ? ObjectNames.NicifyVariableName(RuntimeNodeType.Name)
                    : attribute.Name;
            }
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            base.OnDefineOptions(context);

            foreach (FieldInfo field in GetPortFields<OutputPortAttribute>())
            {
                if (!IsNodeCollection(field.FieldType))
                    continue;

                context.AddOption<int>(GetCountOptionName(field))
                    .WithDisplayName($"{GetPortName(field, field.GetCustomAttribute<OutputPortAttribute>().Name)} Count")
                    .WithDefaultValue(2)
                    .Delayed();
            }
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            ApplyDisplayName();
            if (RuntimeNodeType != typeof(RootNode))
                AddParentPort(context);

            object defaults = CreateRuntimeNode();
            foreach (FieldInfo field in GetPortFields<InputPortAttribute>())
            {
                string name = GetPortName(
                    field,
                    field.GetCustomAttribute<InputPortAttribute>().Name);
                var builder = context.AddInputPort(name)
                    .WithDataType(field.FieldType)
                    .WithDisplayName(ObjectNames.NicifyVariableName(name));

                object defaultValue = defaults != null
                    ? field.GetValue(defaults)
                    : null;
                if (defaultValue != null)
                    builder.WithDefaultValue(defaultValue);
                builder.Build();
            }

            foreach (FieldInfo field in GetPortFields<OutputPortAttribute>())
            {
                string name = GetPortName(
                    field,
                    field.GetCustomAttribute<OutputPortAttribute>().Name);

                if (typeof(AiNode).IsAssignableFrom(field.FieldType))
                {
                    AddChildPort(context, name, string.Empty);
                    continue;
                }

                if (IsNodeCollection(field.FieldType))
                {
                    GetNodeOptionByName(GetCountOptionName(field))
                        .TryGetValue<int>(out int count);
                    for (int i = 0; i < Mathf.Max(1, count); i++)
                    {
                        AddChildPort(
                            context,
                            $"{name}{i}",
                            (i + 1).ToString());
                    }
                    continue;
                }

                context.AddOutputPort(name)
                    .WithDataType(field.FieldType)
                    .WithDisplayName(ObjectNames.NicifyVariableName(name))
                    .Build();
            }
        }

        internal IEnumerable<FieldInfo> GetPortFields<TAttribute>()
            where TAttribute : Attribute
        {
            for (Type type = RuntimeNodeType;
                 type != null && type != typeof(object);
                 type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(
                             BindingFlags.Instance |
                             BindingFlags.Public |
                             BindingFlags.NonPublic |
                             BindingFlags.DeclaredOnly))
                {
                    if (field.GetCustomAttribute<TAttribute>() != null)
                        yield return field;
                }
            }
        }

        internal object CreateRuntimeNode()
        {
            try
            {
                return Activator.CreateInstance(RuntimeNodeType, true);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not construct attributed AI node '{RuntimeNodeType.FullName}'. " +
                    "It needs a parameterless constructor.\n" + exception);
                return null;
            }
        }

        internal static bool IsNodeCollection(Type type)
        {
            if (!typeof(IEnumerable).IsAssignableFrom(type) ||
                !type.IsGenericType)
                return false;

            return typeof(AiNode).IsAssignableFrom(
                type.GetGenericArguments()[0]);
        }

        internal static string GetPortName(FieldInfo field, string overrideName)
        {
            return string.IsNullOrWhiteSpace(overrideName)
                ? field.Name.TrimStart('_')
                : overrideName;
        }

        internal static string GetCountOptionName(FieldInfo field)
        {
            return $"{field.Name}Count";
        }
    }
}
