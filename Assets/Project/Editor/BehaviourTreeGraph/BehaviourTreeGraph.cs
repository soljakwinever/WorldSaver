using System;
using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;

namespace Project.Editor.AI
{
    [Serializable]
    [Graph(AssetExtension)]
    internal sealed class BehaviourTreeGraph : Graph
    {
        internal const string AssetExtension = "btree";

        [MenuItem("Assets/Create/AI/Behaviour Tree Graph")]
        private static void CreateAsset()
        {
            GraphDatabase.PromptInProjectBrowserToCreateNewAsset<BehaviourTreeGraph>(
                "New Behaviour Tree");
        }

        public override void OnGraphChanged(GraphLogger logger)
        {
            base.OnGraphChanged(logger);

            List<Root> roots = GetNodes().OfType<Root>().ToList();
            if (roots.Count == 0)
            {
                logger.LogError("The behaviour tree needs one Root node.", this);
                return;
            }

            foreach (Root extraRoot in roots.Skip(1))
                logger.LogError("Only one Root node is allowed.", extraRoot);

            ValidateChild(roots[0], Root.ChildPortName, logger);

            foreach (DecoratorGraphNode decorator in
                     GetNodes().OfType<DecoratorGraphNode>())
            {
                ValidateChild(
                    decorator,
                    DecoratorGraphNode.ChildPortName,
                    logger);
            }

            foreach (AttributedAiNodeGraphNode attributed in
                     GetNodes().OfType<AttributedAiNodeGraphNode>())
            {
                foreach (IPort output in attributed.GetOutputPorts())
                {
                    if (output.DataType == null)
                        ValidateChild(attributed, output.Name, logger);
                }
            }
        }

        private static void ValidateChild(
            BehaviourTreeGraphNode node,
            string portName,
            GraphLogger logger)
        {
            IPort port = node.GetOutputPortByName(portName);
            if (port == null || !port.IsConnected)
                logger.LogError($"'{node.GetType().Name}' has an unconnected child port.", node);
        }
    }
}
