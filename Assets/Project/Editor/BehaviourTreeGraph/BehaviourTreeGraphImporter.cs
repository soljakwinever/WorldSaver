using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Project.Scripts.AI;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.Interface.Decorator;
using Unity.GraphToolkit.Editor;
using UnityEditor.AssetImporters;
using UnityEngine;
using RuntimeCheckHealth = Project.Scripts.AI.Leaves.Sensors.CheckHealth;

namespace Project.Editor.AI
{
    [ScriptedImporter(1, BehaviourTreeGraph.AssetExtension)]
    internal sealed class BehaviourTreeGraphImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            BehaviourTreeGraph graph =
                GraphDatabase.LoadGraphForImporter<BehaviourTreeGraph>(
                    context.assetPath);

            AiNodeData runtimeAsset = ScriptableObject.CreateInstance<AiNodeData>();
            runtimeAsset.name = System.IO.Path.GetFileNameWithoutExtension(
                context.assetPath);

            if (graph == null)
            {
                Debug.LogError(
                    $"Could not load behaviour tree graph '{context.assetPath}'.");
            }
            else
            {
                Root root = graph.GetNodes()
                    .OfType<Root>()
                    .FirstOrDefault();

                if (root != null)
                {
                    var visiting = new HashSet<INode>();
                    AiNode child = BuildConnectedChild(
                        root.GetOutputPortByName(Root.ChildPortName),
                        visiting);
                    runtimeAsset.SetRoot(new RootNode(child));
                }
            }

            context.AddObjectToAsset("BehaviourTree", runtimeAsset);
            context.SetMainObject(runtimeAsset);
        }

        private static AiNode BuildConnectedChild(
            IPort parentOutput,
            HashSet<INode> visiting)
        {
            INode childModel = parentOutput?.FirstConnectedPort?.GetNode();
            if (childModel == null)
                return null;

            if (!visiting.Add(childModel))
            {
                Debug.LogError("A cycle was found in a behaviour tree graph.");
                return null;
            }

            AiNode result;
            switch (childModel)
            {
                case AttributedAiNodeGraphNode attributed:
                    result = BuildAttributedNode(attributed, visiting);
                    break;

                case CheckHealth checkHealth:
                    IPort sourcePort = checkHealth.GetInputPortByName(
                        CheckHealth.HealthSourcePortName);
                    float threshold = Mathf.Clamp01(GetPortValue<float>(
                        checkHealth.GetInputPortByName(
                            CheckHealth.ThresholdPortName)));

                    if (TryGetAiKey(sourcePort, out AiKeys.Key sourceKey))
                    {
                        result = new RuntimeCheckHealth(sourceKey, threshold);
                    }
                    else
                    {
                        UnityEngine.Object source =
                            GetPortValue<UnityEngine.Object>(sourcePort);
                        IHasHealth health = source switch
                        {
                            GameObject gameObject =>
                                gameObject.GetComponent<IHasHealth>(),
                            Component component =>
                                component.GetComponent<IHasHealth>(),
                            _ => null
                        };
                        result = new RuntimeCheckHealth(
                            health,
                            threshold);
                    }
                    break;

                default:
                    Debug.LogError(
                        $"Unsupported behaviour tree node '{childModel.GetType().Name}'.");
                    result = null;
                    break;
            }

            visiting.Remove(childModel);
            return result;
        }

        private static AiNode BuildAttributedNode(
            AttributedAiNodeGraphNode graphNode,
            HashSet<INode> visiting)
        {
            if (!(graphNode.CreateRuntimeNode() is AiNode runtimeNode))
                return null;

            foreach (FieldInfo field in
                     graphNode.GetPortFields<InputPortAttribute>())
            {
                if (field.IsInitOnly)
                {
                    Debug.LogError(
                        $"Input port field '{field.Name}' on " +
                        $"'{graphNode.RuntimeNodeType.Name}' cannot be readonly.");
                    continue;
                }

                var attribute = field.GetCustomAttribute<InputPortAttribute>();
                string portName = AttributedAiNodeGraphNode.GetPortName(
                    field,
                    attribute.Name);
                IPort port = graphNode.GetInputPortByName(portName);
                object value = GetPortValue(port, field.FieldType);
                field.SetValue(runtimeNode, value);
            }

            foreach (FieldInfo field in
                     graphNode.GetPortFields<OutputPortAttribute>())
            {
                if (field.IsInitOnly)
                {
                    Debug.LogError(
                        $"Output port field '{field.Name}' on " +
                        $"'{graphNode.RuntimeNodeType.Name}' cannot be readonly.");
                    continue;
                }

                var attribute = field.GetCustomAttribute<OutputPortAttribute>();
                string portName = AttributedAiNodeGraphNode.GetPortName(
                    field,
                    attribute.Name);

                if (typeof(AiNode).IsAssignableFrom(field.FieldType))
                {
                    field.SetValue(
                        runtimeNode,
                        BuildConnectedChild(
                            graphNode.GetOutputPortByName(portName),
                            visiting));
                    continue;
                }

                if (AttributedAiNodeGraphNode.IsNodeCollection(field.FieldType))
                {
                    if (!(System.Activator.CreateInstance(field.FieldType)
                          is System.Collections.IList children))
                    {
                        Debug.LogError(
                            $"Output collection '{field.Name}' on " +
                            $"'{graphNode.RuntimeNodeType.Name}' must be a " +
                            "constructible IList.");
                        continue;
                    }

                    foreach (IPort port in graphNode.GetOutputPorts()
                                 .Where(port => port.Name.StartsWith(portName)))
                    {
                        AiNode child = BuildConnectedChild(port, visiting);
                        if (child != null)
                            children.Add(child);
                    }

                    field.SetValue(runtimeNode, children);
                }
            }

            return runtimeNode;
        }

        private static T GetPortValue<T>(IPort port)
        {
            if (port?.IsConnected == true)
            {
                switch (port.FirstConnectedPort.GetNode())
                {
                    case IConstantNode constant
                        when constant.TryGetValue(out T constantValue):
                        return constantValue;
                    case IVariableNode variable
                        when variable.Variable.TryGetDefaultValue(
                            out T variableValue):
                        return variableValue;
                }
            }

            if (port != null && port.TryGetValue(out T value))
                return value;

            return default;
        }

        private static object GetPortValue(IPort port, System.Type valueType)
        {
            MethodInfo method = typeof(BehaviourTreeGraphImporter)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(candidate =>
                    candidate.Name == nameof(GetPortValue) &&
                    candidate.IsGenericMethodDefinition);
            return method.MakeGenericMethod(valueType)
                .Invoke(null, new object[] { port });
        }

        private static bool TryGetAiKey(
            IPort port,
            out AiKeys.Key key)
        {
            if (port?.FirstConnectedPort?.GetNode() is AiKeyConstant constant)
            {
                key = constant.Value;
                return true;
            }

            key = default;
            return false;
        }
    }
}
