using Project.Scripts.AI;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public class AiNodeRunner : MonoBehaviour, IStunnable
    {
        private const int MaxPeriodicEvaluationsPerFrame = 4;

        private static int _nextEvaluationOffset;
        private static int _evaluationBudgetFrame = -1;
        private static int _periodicEvaluationsThisFrame;

        public AiNodeData Data;
        public Blackboard Blackboard { get; } = new Blackboard();

        private AiNode _root;
        private AiNodeData _runtimeData;

        [SerializeField, Min(1), Tooltip(
            "Frames between full behaviour-tree evaluations. Running branches " +
            "continue to update every frame.")]
        private int tickRate = 30;

        private int tickCounter;
        private int _evaluationOffset;
        private float _requestedEvaluationTime = float.PositiveInfinity;
        private float _stunnedUntil;

        [Inject]
        public void Construct(
            IPathFindingService pathFindingService,
            IPathFindingMap pathFindingMap,
            IAttackService attackService,
            IProjectileService projectileService)
        {
            if (pathFindingService != null)
                Blackboard.Set(AiKeys.PathFindingService, pathFindingService);
            if (pathFindingMap != null)
                Blackboard.Set(AiKeys.PathFindingMap, pathFindingMap);
            if (attackService != null)
                Blackboard.Set(AiKeys.AttackService, attackService);
            if (projectileService != null)
                Blackboard.Set(AiKeys.ProjectileService, projectileService);
        }

        private void Awake()
        {
            _evaluationOffset = _nextEvaluationOffset++;
            AiKeys.RegisterDefaults(Blackboard, gameObject);

            Configure(Data);
        }

        public void Configure(AiNodeData data)
        {
            if (_runtimeData != null)
                Destroy(_runtimeData);

            Data = data;
            _runtimeData = Data != null
                ? Data.CreateRuntimeInstance()
                : null;
            SetTree(_runtimeData != null ? _runtimeData.Root : null);
        }

        public void SetTree(AiNode root)
        {
            if (!ReferenceEquals(_root, root))
                _root?.Abort();

            _root = root;
            _root?.Bind(Blackboard, RequestEvaluation);
            int interval = Mathf.Max(1, tickRate);
            // Spread initial evaluations too. Chunk loading can create dozens
            // of NPCs together, and immediately evaluating all of their trees
            // would simply move the spawn hitch into the next frame.
            tickCounter =
                (int)((uint)_evaluationOffset % (uint)interval);
            _requestedEvaluationTime = float.PositiveInfinity;
        }

        private void Update()
        {
            if (_root == null)
                return;

            if (Time.time < _stunnedUntil)
                return;

            tickCounter++;
            bool periodicEvaluation = tickCounter >= Mathf.Max(1, tickRate);
            bool requestedEvaluation = Time.time >= _requestedEvaluationTime;

            if (requestedEvaluation ||
                periodicEvaluation && TryConsumePeriodicEvaluationBudget())
            {
                EvaluateTree();
                return;
            }

            if (!_root.IsActive)
                return;

            AiNode.NodeState result = _root.Continue();

            // A running action can finish between scheduled decision ticks
            // (for example, on reaching the final waypoint). Reevaluate now so
            // the NPC immediately chooses what to do next.
            if (result != AiNode.NodeState.Running)
                EvaluateTree();
        }

        private void EvaluateTree()
        {
            tickCounter = 0;
            _requestedEvaluationTime = float.PositiveInfinity;
            _root?.Evaluate();
        }

        /// <summary>
        /// Lets gameplay events wake this AI before its next periodic decision
        /// tick (for example, taking damage or acquiring a target).
        /// </summary>
        public void TickEarly()
        {
            RequestEvaluation(0f);
        }

        public void Stun(float duration)
        {
            if (duration <= 0f)
                return;

            _stunnedUntil = Mathf.Max(
                _stunnedUntil,
                Time.time + duration);
            _root?.Abort();
            _requestedEvaluationTime = _stunnedUntil;
        }

        private void RequestEvaluation(float delaySeconds)
        {
            float requestedTime = Time.time + Mathf.Max(0f, delaySeconds);
            _requestedEvaluationTime = Mathf.Min(
                _requestedEvaluationTime,
                requestedTime);
        }

        private static bool TryConsumePeriodicEvaluationBudget()
        {
            int frame = Time.frameCount;
            if (_evaluationBudgetFrame != frame)
            {
                _evaluationBudgetFrame = frame;
                _periodicEvaluationsThisFrame = 0;
            }

            if (_periodicEvaluationsThisFrame >=
                MaxPeriodicEvaluationsPerFrame)
            {
                return false;
            }

            _periodicEvaluationsThisFrame++;
            return true;
        }

        private void OnDestroy()
        {
            if (_runtimeData != null)
                Destroy(_runtimeData);
        }

        private void OnDisable()
        {
            _root?.Abort();
            _requestedEvaluationTime = float.PositiveInfinity;
        }
    }
}
