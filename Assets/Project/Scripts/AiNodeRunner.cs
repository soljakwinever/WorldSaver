using Project.Scripts.AI;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public class AiNodeRunner : MonoBehaviour
    {
        public AiNodeData Data;
        public Blackboard Blackboard { get; } = new Blackboard();

        private AiNode _root;
        private AiNodeData _runtimeData;

        [Inject]
        public void Construct(
            IPathFindingService pathFindingService,
            IPathFindingMap pathFindingMap)
        {
            if (pathFindingService != null)
                Blackboard.Set(AiKeys.PathFindingService, pathFindingService);
            if (pathFindingMap != null)
                Blackboard.Set(AiKeys.PathFindingMap, pathFindingMap);
        }

        private void Awake()
        {
            AiKeys.RegisterDefaults(Blackboard, gameObject);

            if (Data == null)
                return;

            _runtimeData = Data.CreateRuntimeInstance();
            SetTree(_runtimeData.Root);
        }

        public void SetTree(AiNode root)
        {
            if (!ReferenceEquals(_root, root))
                _root?.Abort();

            _root = root;
            _root?.Bind(Blackboard);
        }

        private void Update()
        {
            _root?.Evaluate();
        }

        private void OnDestroy()
        {
            if (_runtimeData != null)
                Destroy(_runtimeData);
        }

        private void OnDisable()
        {
            _root?.Abort();
        }
    }
}
