using System;
using Project.Scripts.AI;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Project.Editor.AI
{
    [Serializable]
    internal abstract class NamedGraphNode : Node
    {
        internal const string CustomNameOptionName = "CustomName";

        protected virtual string DefaultDisplayName =>
            ObjectNames.NicifyVariableName(GetType().Name);

        protected override void OnDefineOptions(
            IOptionDefinitionContext context)
        {
            context.AddOption<string>(CustomNameOptionName)
                .WithDisplayName("Custom Name")
                .WithDefaultValue(string.Empty)
                .Delayed();
        }

        protected void ApplyDisplayName()
        {
            GetNodeOptionByName(CustomNameOptionName)
                .TryGetValue<string>(out string customName);

            string typeName = DefaultDisplayName;
            if (string.IsNullOrWhiteSpace(customName))
            {
                Title = typeName;
                Subtitle = string.Empty;
                return;
            }

            Title = customName.Trim();
            Subtitle = typeName;
        }
    }

    [Serializable]
    internal abstract class BehaviourTreeGraphNode : NamedGraphNode
    {
        internal const string ParentPortName = "Parent";

        protected static void AddParentPort(IPortDefinitionContext context)
        {
            context.AddInputPort(ParentPortName)
                .WithDisplayName(string.Empty)
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();
        }

        protected static void AddChildPort(
            IPortDefinitionContext context,
            string name,
            string displayName)
        {
            context.AddOutputPort(name)
                .WithDisplayName(displayName)
                .WithConnectorUI(PortConnectorUI.Arrowhead)
                .Build();
        }
    }

    [Serializable]
    internal abstract class DecoratorGraphNode : BehaviourTreeGraphNode
    {
        internal const string ChildPortName = "Child";

        protected static void AddDecoratorPorts(
            IPortDefinitionContext context)
        {
            AddParentPort(context);
            AddChildPort(context, ChildPortName, string.Empty);
        }
    }

    [Serializable]
    internal abstract class AiKeyConstant : NamedGraphNode
    {
        internal const string ValuePortName = "Value";
        internal abstract AiKeys.Key Value { get; }
    }

    [Serializable]
    internal sealed class SelfKey : AiKeyConstant
    {
        internal override AiKeys.Key Value => AiKeys.Key.Self;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            ApplyDisplayName();
            context.AddOutputPort<GameObject>(ValuePortName)
                .WithDisplayName(string.Empty)
                .Build();
        }
    }

    [Serializable]
    internal sealed class TargetKey : AiKeyConstant
    {
        internal override AiKeys.Key Value => AiKeys.Key.Target;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            ApplyDisplayName();
            context.AddOutputPort<Transform>(ValuePortName)
                .WithDisplayName(string.Empty)
                .Build();
        }
    }

    [Serializable]
    internal sealed class DestinationKey : AiKeyConstant
    {
        internal override AiKeys.Key Value => AiKeys.Key.Destination;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            ApplyDisplayName();
            context.AddOutputPort<Vector3>(ValuePortName)
                .WithDisplayName(string.Empty)
                .Build();
        }
    }

    [Serializable]
    internal abstract class AiFloatSpecialVariable : NamedGraphNode
    {
        internal const string ValuePortName = "Value";
        internal abstract AiFloatVariable Variable { get; }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            ApplyDisplayName();
            context.AddOutputPort<float>(ValuePortName)
                .WithDisplayName(string.Empty)
                .Build();
        }
    }

    [Serializable]
    internal sealed class MovementSpeedVariable : AiFloatSpecialVariable
    {
        protected override string DefaultDisplayName => "Movement Speed";
        internal override AiFloatVariable Variable =>
            AiFloatVariable.MovementSpeed;
    }

    [Serializable]
    internal sealed class SprintMultiplierVariable : AiFloatSpecialVariable
    {
        protected override string DefaultDisplayName => "Sprint Multiplier";
        internal override AiFloatVariable Variable =>
            AiFloatVariable.SprintMultiplier;
    }

    [Serializable]
    internal sealed class AccuracyVariable : AiFloatSpecialVariable
    {
        protected override string DefaultDisplayName => "Accuracy";
        internal override AiFloatVariable Variable =>
            AiFloatVariable.Accuracy;
    }

    [Serializable]
    internal sealed class FloatMath : NamedGraphNode
    {
        internal const string LeftPortName = "Left";
        internal const string RightPortName = "Right";
        internal const string ValuePortName = "Value";
        private const string OperationOptionName = "Operation";

        internal AiFloatOperation Operation
        {
            get
            {
                GetNodeOptionByName(OperationOptionName)
                    .TryGetValue(out AiFloatOperation operation);
                return operation;
            }
        }

        protected override string DefaultDisplayName => "Float Math";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            base.OnDefineOptions(context);
            context.AddOption<AiFloatOperation>(OperationOptionName)
                .WithDisplayName("Operation")
                .WithDefaultValue(AiFloatOperation.Add);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            ApplyDisplayName();
            context.AddInputPort<float>(LeftPortName)
                .WithDefaultValue(0f)
                .Build();
            context.AddInputPort<float>(RightPortName)
                .WithDefaultValue(0f)
                .Build();
            context.AddOutputPort<float>(ValuePortName)
                .WithDisplayName(string.Empty)
                .Build();
        }
    }

    [Serializable]
    internal sealed class Root : BehaviourTreeGraphNode
    {
        internal const string ChildPortName = "Child";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            ApplyDisplayName();
            AddChildPort(context, ChildPortName, string.Empty);
        }
    }

    [Serializable]
    internal sealed class CheckHealth : BehaviourTreeGraphNode
    {
        internal const string HealthSourcePortName = "HealthSource";
        internal const string ThresholdPortName = "Threshold";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            ApplyDisplayName();
            AddParentPort(context);
            context.AddInputPort<UnityEngine.Object>(HealthSourcePortName)
                .WithDisplayName("Health Source")
                .Build();
            context.AddInputPort<float>(ThresholdPortName)
                .WithDisplayName("Below")
                .WithDefaultValue(0.5f)
                .Build();
        }
    }
}
